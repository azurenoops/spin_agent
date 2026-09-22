using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services.Tenancy;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Services;

/// <summary>Server-resolved identity and workspace shared by REST and real-time authorization.</summary>
public sealed record WorkspaceNotificationActor(
    Guid DirectoryId, Guid ObjectId, WorkspaceResponse Workspace, TenantContext Context)
{
    public void ValidateRecipientHint(HttpContext http)
    {
        if (!http.Request.Query.TryGetValue("userId", out var values)) return;
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            throw new WorkspaceException(400, "INVALID_USER_ID", "Supply at most one nonempty userId assertion.");
        if (!Guid.TryParse(values[0], out var asserted) || asserted != ObjectId)
            throw new WorkspaceException(403, "NOTIFICATION_IDENTITY_MISMATCH", "userId must match the authenticated object identity.");
    }

    public static async Task<WorkspaceNotificationActor> ResolveAsync(
        HttpContext http, IWorkspaceService workspace, CancellationToken ct)
    {
        var identity = WorkspaceService.Identity(http.User);
        var context = new TenantContext();
        await workspace.ResolveAsync(http, context, ct);
        if (workspace.Current is not { } current)
            throw new WorkspaceException(409, "WORKSPACE_REQUIRED", "Select an authorized workspace.");
        if (current.Mode == "support" && (workspace.SupportSession is not { } session || session.ExpiresAt <= DateTimeOffset.UtcNow))
            throw new WorkspaceException(403, "SUPPORT_SESSION_INVALID", "The support session is invalid or expired.");
        return new(identity.DirectoryId, identity.ObjectId, current, context);
    }
}

public interface INotificationAccessService
{
    Task<bool> OwnsRecipientAsync(AtoCopilotContext db, WorkspaceNotificationActor actor, CancellationToken ct);
    Task<bool> CanReadSystemAsync(WorkspaceNotificationActor actor, string systemId, CancellationToken ct);
    Task<bool> CanReadAsync(AtoCopilotContext db, WorkspaceNotificationActor actor, AlertNotification notification, CancellationToken ct);
    Task<IQueryable<AlertNotification>> VisibleAsync(AtoCopilotContext db, WorkspaceNotificationActor actor, CancellationToken ct);
}

/// <summary>One recipient/system visibility policy for notification REST and hub delivery.</summary>
public sealed class NotificationAccessService(ISystemWorkspaceAccessService systems) : INotificationAccessService
{
    public async Task<bool> OwnsRecipientAsync(AtoCopilotContext db, WorkspaceNotificationActor actor, CancellationToken ct)
    {
        // Include historical grants: revocation must not transfer an oid-only record to another directory.
        var directories = await db.OrganizationMemberships.AsNoTracking()
            .Where(m => m.TenantId == actor.Workspace.TenantId && m.ObjectId == actor.ObjectId)
            .Select(m => m.DirectoryTenantId).Distinct().ToListAsync(ct);
        return directories.Count == 1 && directories[0] == actor.DirectoryId;
    }

    public Task<bool> CanReadSystemAsync(WorkspaceNotificationActor actor, string systemId, CancellationToken ct) =>
        systems.CanReadAsync(actor.Context.EffectiveTenantId, actor.Workspace.PersonId, systemId,
            IsOversight(actor), ct);

    public async Task<bool> CanReadAsync(AtoCopilotContext db, WorkspaceNotificationActor actor,
        AlertNotification notification, CancellationToken ct)
    {
        if (notification.AlertId == Guid.Empty) return false;
        var alert = await db.ComplianceAlerts.AsNoTracking().SingleOrDefaultAsync(a =>
            a.Id == notification.AlertId && a.TenantId == actor.Workspace.TenantId, ct);
        return alert is not null && !string.IsNullOrWhiteSpace(alert.RegisteredSystemId)
            && await CanReadSystemAsync(actor, alert.RegisteredSystemId, ct);
    }

    public async Task<IQueryable<AlertNotification>> VisibleAsync(
        AtoCopilotContext db, WorkspaceNotificationActor actor, CancellationToken ct)
    {
        var userId = actor.ObjectId.ToString();
        var candidates = from notification in db.AlertNotifications
                         join alert in db.ComplianceAlerts on notification.AlertId equals alert.Id
                         where notification.TenantId == actor.Workspace.TenantId && notification.UserId == userId
                             && alert.TenantId == actor.Workspace.TenantId
                             && alert.RegisteredSystemId != null && alert.RegisteredSystemId != ""
                         select new { Notification = notification, SystemId = alert.RegisteredSystemId };
        var systemIds = await candidates.Select(n => n.SystemId!).Distinct().ToListAsync(ct);
        var visible = new List<string>();
        foreach (var batch in systemIds.Chunk(100))
        {
            var access = await systems.GetAccessBatchAsync(actor.Context.EffectiveTenantId,
                actor.Workspace.PersonId, batch, IsOversight(actor), ct);
            visible.AddRange(access.Where(a => a.Permissions.CanRead).Select(a => a.SystemId));
        }
        return candidates.Where(n => visible.Contains(n.SystemId!)).Select(n => n.Notification);
    }

    private static bool IsOversight(WorkspaceNotificationActor actor) =>
        actor.Context.IsCspAdmin && (actor.Workspace.Kind == "csp" || actor.Workspace.Mode == "support");
}
