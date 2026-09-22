using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Mcp.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Hubs.Notifications;

public enum ProgressResourceKind { Package, Import }

/// <summary>Server-owned job binding. An existing subscription cannot follow a retargeted job.</summary>
public sealed record ProgressResource(ProgressResourceKind Kind, Guid Id, Guid TenantId, string SystemId);

/// <summary>Resource-authorized progress subscriptions and delivery using the shared workspace registry.</summary>
public sealed class WorkspaceProgressDeliveryService(
    NotificationConnectionRegistry connections,
    IServiceScopeFactory scopeFactory,
    ITenantContextAccessor accessor,
    ILogger<WorkspaceProgressDeliveryService> logger)
{
    public async Task<string> SubscribeAsync(string connectionId, ProgressResourceKind kind, string resourceId,
        CancellationToken ct = default)
    {
        var id = Parse(resourceId);
        var resolved = await RequireAsync(connectionId, kind, ct);
        var resource = await ResolveResourceAsync(resolved, kind, id, ct);
        if (resource is null) throw new HubException("Progress resource access denied");
        resolved.Connection.ProgressResources[(kind, id)] = resource;
        return Group(resolved.Connection, kind, id);
    }

    public async Task<string> UnsubscribeAsync(string connectionId, ProgressResourceKind kind, string resourceId,
        CancellationToken ct = default)
    {
        var id = Parse(resourceId);
        var resolved = await RequireAsync(connectionId, kind, ct);
        resolved.Connection.ProgressResources.TryRemove((kind, id), out _);
        return Group(resolved.Connection, kind, id);
    }

    public async Task SendGroupAsync(ProgressResourceKind kind, string group, string method, object?[] arguments,
        NotificationSend send, CancellationToken ct = default)
    {
        var prefix = kind == ProgressResourceKind.Package ? "package:" : "import:";
        if (!group.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParse(group[prefix.Length..], out var id) || id == Guid.Empty
            || arguments.Length != 1 || arguments[0] is null || !AllowedEvent(kind, method))
            throw new InvalidOperationException("Invalid workspace progress event.");
        var payload = JsonSerializer.SerializeToElement(arguments[0]);
        var idField = kind == ProgressResourceKind.Package ? "packageId" : "jobId";
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(idField, out var property)
            || property.ValueKind != JsonValueKind.String || !property.TryGetGuid(out var payloadId) || payloadId != id)
            throw new InvalidOperationException("Progress event does not match its resource.");

        foreach (var candidate in connections.Connections)
        {
            if (!candidate.ProgressResources.TryGetValue((kind, id), out var subscribed)) continue;
            ValidatedNotificationConnection resolved;
            try
            {
                resolved = await RequireAsync(candidate.ConnectionId, kind, ct);
            }
            catch (HubException)
            {
                logger.LogInformation("Progress delivery skipped for a disconnected or unauthorized subscriber");
                continue;
            }
            var resource = await ResolveResourceAsync(resolved, kind, id, ct);
            if (resource != subscribed)
            {
                candidate.ProgressResources.TryRemove((kind, id), out _);
                logger.LogInformation("Progress subscription lost resource authorization");
                continue;
            }
            await send(candidate.ConnectionId, method, arguments, ct);
        }
    }

    private async Task<ProgressResource?> ResolveResourceAsync(ValidatedNotificationConnection resolved,
        ProgressResourceKind kind, Guid id, CancellationToken ct)
    {
        using var tenantScope = accessor.Push(resolved.Context);
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>().CreateDbContextAsync(ct);
        string? systemId;
        Guid? packageTenant = null;
        if (kind == ProgressResourceKind.Package)
        {
            var key = id.ToString();
            var package = await db.AuthorizationPackages.AsNoTracking().Where(p => p.Id == key)
                .Select(p => new { p.TenantId, p.RegisteredSystemId }).SingleOrDefaultAsync(ct);
            systemId = package?.RegisteredSystemId;
            packageTenant = package?.TenantId;
        }
        else
            systemId = scope.ServiceProvider.GetRequiredService<ScanImportStatusTracker>().TryGet(id.ToString())?.SystemId;
        if (string.IsNullOrWhiteSpace(systemId)) return null;
        var system = await db.RegisteredSystems.AsNoTracking().Where(s => s.Id == systemId && s.IsActive)
            .Select(s => new { s.TenantId }).SingleOrDefaultAsync(ct);
        if (system is null || system.TenantId == Guid.Empty
            || packageTenant.HasValue && packageTenant != system.TenantId
            || resolved.Workspace.Kind != "csp" && resolved.Workspace.TenantId != system.TenantId)
            return null;
        return await scope.ServiceProvider.GetRequiredService<INotificationAccessService>()
            .CanReadSystemAsync(resolved.Actor, systemId, ct)
            ? new(kind, id, system.TenantId, systemId) : null;
    }

    private async Task<ValidatedNotificationConnection> RequireAsync(string id, ProgressResourceKind kind, CancellationToken ct)
    {
        var resolved = await connections.RequireAsync(id, ct);
        var expected = kind == ProgressResourceKind.Package ? "/hubs/package" : "/hubs/import-progress";
        if (resolved.Connection.HubPath != expected)
            throw new HubException("Progress subscription belongs to another transport");
        return resolved;
    }

    private static bool AllowedEvent(ProgressResourceKind kind, string method) => kind switch
    {
        ProgressResourceKind.Package => method is "PackageStatusChanged" or "PackageArtifactGenerated"
            or "PackageValidationComplete" or "PackageComplete" or "PackageFailed",
        ProgressResourceKind.Import => method == "ImportProgress",
        _ => false,
    };

    private static Guid Parse(string value) =>
        Guid.TryParse(value, out var id) && id != Guid.Empty ? id : throw new HubException("Progress resource ID must be a nonempty GUID");

    private static string Group(NotificationConnection connection, ProgressResourceKind kind, Guid id) =>
        $"{connection.GroupName}:{kind}:{id:N}";
}
