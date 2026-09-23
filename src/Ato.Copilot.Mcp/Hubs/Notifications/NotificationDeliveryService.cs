using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Hubs.Notifications;

public delegate Task NotificationSend(string connectionId, string method, object?[] arguments, CancellationToken ct);

/// <summary>
/// Revalidates each recipient immediately before sending, rather than broadcasting to stale group
/// membership. This also guards the legacy SSP and wizard publishers through the hub lifetime manager.
/// </summary>
public sealed class NotificationDeliveryService(
    NotificationConnectionRegistry connections,
    IServiceScopeFactory scopeFactory,
    ITenantContextAccessor accessor,
    ILogger<NotificationDeliveryService> logger)
{
    public async Task BroadcastAsync(string userId, AlertNotification notification, NotificationSend send,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(userId, out var actor)
            || !Guid.TryParse(notification.UserId, out var recipient) || actor != recipient)
            throw new InvalidOperationException("Notification recipient must be an authenticated object identity.");
        var tenantId = await NotificationTenantAsync(notification, ct);
        var payload = new
        {
            id = notification.Id, alertId = notification.AlertId, channel = notification.Channel.ToString(),
            subject = notification.Subject, body = notification.Body, sentAt = notification.SentAt, isRead = notification.IsRead,
        };
        foreach (var candidate in connections.Connections.Where(c => c.Registered && c.ObjectId == actor))
        {
            var resolved = await TryResolveAsync(candidate, ct);
            if (resolved?.Workspace.TenantId != tenantId) continue;
            using var tenantScope = accessor.Push(resolved.Context);
            await using var scope = scopeFactory.CreateAsyncScope();
            await using var db = await DatabaseAsync(scope, ct);
            if (await Access(scope).OwnsRecipientAsync(db, resolved.Actor, ct)
                && await CanReadAsync(scope, db, resolved, notification, ct))
                await send(candidate.ConnectionId, "NewNotification", [payload], ct);
        }
    }

    public async Task MarkReadAsync(string connectionId, Guid notificationId, NotificationSend send,
        CancellationToken ct = default)
    {
        var caller = await connections.RequireAsync(connectionId, ct);
        if (!caller.Connection.Registered || caller.Workspace.TenantId is not { } tenantId)
            throw new HubException("Notification subscription required");
        using var tenantScope = accessor.Push(caller.Context);
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var db = await DatabaseAsync(scope, ct);
        var userId = caller.Connection.ObjectId.ToString();
        var notification = await db.AlertNotifications.AsNoTracking().SingleOrDefaultAsync(n =>
            n.TenantId == tenantId && n.Id == notificationId && n.UserId == userId, ct);
        if (notification is null || !await Access(scope).OwnsRecipientAsync(db, caller.Actor, ct)
            || !await CanReadAsync(scope, db, caller, notification, ct))
            throw new HubException("Notification access denied");
        foreach (var candidate in connections.Connections.Where(c => c.Registered
            && c.GroupName == caller.Connection.GroupName))
        {
            var resolved = await TryResolveAsync(candidate, ct);
            if (resolved is not null && await CanReadAsync(scope, db, resolved, notification, ct))
                await send(candidate.ConnectionId, "NotificationRead", [notificationId.ToString()], ct);
        }
    }

    public async Task UnreadCountAsync(string userId, NotificationSend send, CancellationToken ct = default)
    {
        if (!Guid.TryParse(userId, out var actor))
            throw new InvalidOperationException("Notification recipient must be an authenticated object identity.");
        foreach (var candidate in connections.Connections.Where(c => c.Registered && c.ObjectId == actor))
        {
            var resolved = await TryResolveAsync(candidate, ct);
            if (resolved?.Workspace.TenantId is not { } tenantId) continue;
            using var tenantScope = accessor.Push(resolved.Context);
            await using var scope = scopeFactory.CreateAsyncScope();
            await using var db = await DatabaseAsync(scope, ct);
            if (!await Access(scope).OwnsRecipientAsync(db, resolved.Actor, ct)) continue;
            var visible = await Access(scope).VisibleAsync(db, resolved.Actor, ct);
            var count = await visible.CountAsync(n => !n.IsRead, ct);
            await send(candidate.ConnectionId, "UnreadCountUpdated", [new { unreadCount = count }], ct);
        }
    }

    public async Task<string> SubscribeWizardAsync(string connectionId, Guid jobId, CancellationToken ct = default)
    {
        var resolved = await connections.RequireAsync(connectionId, ct);
        using var tenantScope = accessor.Push(resolved.Context);
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var db = await DatabaseAsync(scope, ct);
        if (!await CanReadWizardAsync(scope, db, resolved, jobId, ct))
            throw new HubException("Wizard job access denied");
        resolved.Connection.WizardJobs.TryAdd(jobId, 0);
        return $"{resolved.Connection.GroupName}:wizard:{jobId:N}";
    }

    public async Task SendLegacyGroupAsync(string groupName, string method, object?[] arguments,
        NotificationSend send, CancellationToken ct = default)
    {
        if (arguments.Length != 1 || arguments[0] is null) return;
        var payload = JsonSerializer.SerializeToElement(arguments[0]);
        if (method == "WizardJobStatus"
            && TryGuid(payload, "tenantId", out var tenantId) && TryGuid(payload, "jobId", out var jobId)
            && groupName == $"wizard-{tenantId}-job-{jobId}")
        {
            await SendWizardAsync(tenantId, jobId, method, arguments, send, ct);
        }
        else if (method is "SspExportProgress" or "SspExportReady" or "SspExportFailed"
            && groupName.StartsWith("user:", StringComparison.Ordinal)
            && Guid.TryParse(groupName["user:".Length..], out var actor)
            && TryGuid(payload, "exportId", out var exportId))
        {
            await SendExportAsync(actor, exportId, method, arguments, send, ct);
        }
        // There are no tenant-wide wizard subscriptions: job events are emitted twice by the
        // legacy publisher. Unknown/unscoped groups are not an authorization mechanism.
    }

    private async Task SendWizardAsync(Guid tenantId, Guid jobId, string method, object?[] arguments,
        NotificationSend send, CancellationToken ct)
    {
        foreach (var candidate in connections.Connections.Where(c => c.WizardJobs.ContainsKey(jobId)))
        {
            var resolved = await TryResolveAsync(candidate, ct);
            if (resolved?.Workspace.TenantId != tenantId) continue;
            using var tenantScope = accessor.Push(resolved.Context);
            await using var scope = scopeFactory.CreateAsyncScope();
            await using var db = await DatabaseAsync(scope, ct);
            if (await CanReadWizardAsync(scope, db, resolved, jobId, ct))
                await send(candidate.ConnectionId, method, arguments, ct);
        }
    }

    private async Task SendExportAsync(Guid actor, Guid exportId, string method, object?[] arguments,
        NotificationSend send, CancellationToken ct)
    {
        foreach (var candidate in connections.Connections.Where(c => c.Registered && c.ObjectId == actor))
        {
            var resolved = await TryResolveAsync(candidate, ct);
            if (resolved?.Workspace.TenantId is not { } tenantId) continue;
            using var tenantScope = accessor.Push(resolved.Context);
            await using var scope = scopeFactory.CreateAsyncScope();
            await using var db = await DatabaseAsync(scope, ct);
            var export = await (from e in db.SspExports.AsNoTracking()
                                join system in db.RegisteredSystems on e.SystemId equals system.Id
                                where e.Id == exportId && system.TenantId == tenantId
                                select e).SingleOrDefaultAsync(ct);
            if (export is not null && Guid.TryParse(export.GeneratedBy, out var owner) && owner == actor
                && await Access(scope).OwnsRecipientAsync(db, resolved.Actor, ct)
                && await CanReadSystemAsync(scope, resolved, export.SystemId, ct))
                await send(candidate.ConnectionId, method, arguments, ct);
        }
    }

    private async Task<Guid> NotificationTenantAsync(AlertNotification notification, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var db = await DatabaseAsync(scope, ct);
        var tenant = notification.TenantId;
        if (notification.AlertId != Guid.Empty)
        {
            var alertTenant = await db.ComplianceAlerts.AsNoTracking().Where(a => a.Id == notification.AlertId)
                .Select(a => (Guid?)a.TenantId).SingleOrDefaultAsync(ct);
            if (alertTenant is null || alertTenant == Guid.Empty || (tenant != Guid.Empty && tenant != alertTenant))
                throw new InvalidOperationException("Notification alert tenant cannot be verified.");
            tenant = alertTenant.Value;
        }
        if (tenant == Guid.Empty)
            throw new InvalidOperationException("A trusted notification tenant is required.");
        return tenant;
    }

    private static Task<bool> CanReadAsync(AsyncServiceScope scope, AtoCopilotContext db,
        ValidatedNotificationConnection connection, AlertNotification notification, CancellationToken ct) =>
        Access(scope).CanReadAsync(db, connection.Actor, notification, ct);

    private static Task<bool> CanReadSystemAsync(AsyncServiceScope scope,
        ValidatedNotificationConnection connection, string systemId, CancellationToken ct) =>
        Access(scope).CanReadSystemAsync(connection.Actor, systemId, ct);

    private static INotificationAccessService Access(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<INotificationAccessService>();

    private static async Task<bool> CanReadWizardAsync(AsyncServiceScope scope, AtoCopilotContext db,
        ValidatedNotificationConnection connection, Guid jobId, CancellationToken ct)
    {
        if (connection.Workspace.TenantId is not { } tenantId) return false;
        var result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(
            connection.Connection.Principal, null, OnboardingAdministratorRequirement.PolicyName);
        return result.Succeeded && await db.WizardJobStatuses.AnyAsync(j => j.Id == jobId && j.TenantId == tenantId, ct);
    }

    private async Task<ValidatedNotificationConnection?> TryResolveAsync(NotificationConnection connection, CancellationToken ct)
    {
        try
        {
            return await connections.RequireAsync(connection.ConnectionId, ct);
        }
        catch (HubException)
        {
            logger.LogDebug("Skipped disconnected or unauthorized notification subscriber");
            return null;
        }
    }

    private static Task<AtoCopilotContext> DatabaseAsync(AsyncServiceScope scope, CancellationToken ct) =>
        scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>().CreateDbContextAsync(ct);

    private static bool TryGuid(JsonElement payload, string name, out Guid value)
    {
        value = Guid.Empty;
        return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String && property.TryGetGuid(out value) && value != Guid.Empty;
    }
}
