using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Services;

/// <summary>Visible notification list; totalCount preserves the existing returned-items count contract.</summary>
public sealed record NotificationListResponse(IReadOnlyList<NotificationDto> Items, int TotalCount);

/// <summary>Number of authorized unread records changed by a read operation.</summary>
public sealed record NotificationReadResponse(int MarkedCount);

public interface IWorkspaceNotificationService
{
    Task InitializeAsync(HttpContext http, CancellationToken ct);
    Task<NotificationListResponse> ListAsync(bool unreadOnly, int limit, CancellationToken ct);
    Task<NotificationSummaryDto> SummaryAsync(CancellationToken ct);
    Task<NotificationReadResponse> MarkReadAsync(IReadOnlyCollection<Guid>? ids, CancellationToken ct);
    Task<NotificationReadResponse> MarkAllReadAsync(CancellationToken ct);
    Task<NotificationPreferencesDto> GetPreferencesAsync(CancellationToken ct);
    Task<NotificationPreferencesDto> SavePreferencesAsync(NotificationPreferencesDto preferences, CancellationToken ct);
}

/// <summary>Request-scoped notification operations with explicit actor, tenant, and resource authorization.</summary>
public sealed class WorkspaceNotificationService(
    IWorkspaceService workspace,
    IDbContextFactory<AtoCopilotContext> factory,
    ITenantContextAccessor accessor,
    INotificationAccessService access) : IWorkspaceNotificationService
{
    private WorkspaceNotificationActor? _actor;
    private WorkspaceNotificationActor Actor => _actor ?? throw new InvalidOperationException("Notification workspace was not initialized.");

    public async Task InitializeAsync(HttpContext http, CancellationToken ct)
    {
        var actor = await WorkspaceNotificationActor.ResolveAsync(http, workspace, ct);
        actor.ValidateRecipientHint(http);
        if (actor.Workspace is not { Kind: "organization", TenantId: not null })
            throw new WorkspaceException(409, "ORGANIZATION_WORKSPACE_REQUIRED", "Select an organization to access personal notifications.");
        using var tenantScope = accessor.Push(actor.Context);
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await access.OwnsRecipientAsync(db, actor, ct))
            throw new WorkspaceException(403, "NOTIFICATION_RECIPIENT_AMBIGUOUS", "The notification recipient cannot be bound to this directory identity.");
        _actor = actor;
    }

    public async Task<NotificationListResponse> ListAsync(bool unreadOnly, int limit, CancellationToken ct)
    {
        using var tenantScope = accessor.Push(Actor.Context);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = (await access.VisibleAsync(db, Actor, ct)).AsNoTracking();
        if (unreadOnly) query = query.Where(n => !n.IsRead);
        var page = await PageAsync(db, query, Math.Clamp(limit, 1, 200), ct);
        var items = await page.Select(n => new NotificationDto
            {
                Id = n.Id, AlertId = n.AlertId, Channel = n.Channel.ToString(), Subject = n.Subject,
                Body = n.Body, IsRead = n.IsRead, ReadAt = n.ReadAt, SentAt = n.SentAt,
                AlertTitle = n.Alert.Title, AlertSeverity = n.Alert.Severity.ToString(),
            }).ToListAsync(ct);
        return new(items.OrderByDescending(n => n.SentAt).ThenBy(n => n.Id).ToList(), items.Count);
    }

    public async Task<NotificationSummaryDto> SummaryAsync(CancellationToken ct)
    {
        using var tenantScope = accessor.Push(Actor.Context);
        await using var db = await factory.CreateDbContextAsync(ct);
        var visible = await access.VisibleAsync(db, Actor, ct);
        return new() { UnreadCount = await visible.CountAsync(n => !n.IsRead, ct), TotalCount = await visible.CountAsync(ct) };
    }

    public async Task<NotificationReadResponse> MarkReadAsync(IReadOnlyCollection<Guid>? ids, CancellationToken ct)
    {
        if (ids is null || ids.Count is 0 or > 200 || ids.Contains(Guid.Empty))
            throw new WorkspaceException(400, "INVALID_INPUT", "Supply 1 to 200 nonempty notification IDs.");
        using var tenantScope = accessor.Push(Actor.Context);
        await using var db = await factory.CreateDbContextAsync(ct);
        var requested = ids.Distinct().ToArray();
        var visible = await access.VisibleAsync(db, Actor, ct);
        var rows = await visible.Where(n => requested.Contains(n.Id)).ToListAsync(ct);
        if (rows.Count != requested.Length)
            throw new WorkspaceException(404, "NOTIFICATION_NOT_FOUND", "One or more notifications are not accessible in this workspace.");
        var changed = Mark(rows);
        await db.SaveChangesAsync(ct);
        return new(changed);
    }

    public async Task<NotificationReadResponse> MarkAllReadAsync(CancellationToken ct)
    {
        using var tenantScope = accessor.Push(Actor.Context);
        await using var db = await factory.CreateDbContextAsync(ct);
        var visible = await access.VisibleAsync(db, Actor, ct);
        var rows = await visible.Where(n => !n.IsRead).ToListAsync(ct);
        var changed = Mark(rows);
        await db.SaveChangesAsync(ct);
        return new(changed);
    }

    public async Task<NotificationPreferencesDto> GetPreferencesAsync(CancellationToken ct)
    {
        using var tenantScope = accessor.Push(Actor.Context);
        await using var db = await factory.CreateDbContextAsync(ct);
        var userId = Actor.ObjectId.ToString();
        var row = await db.NotificationPreferences.AsNoTracking().SingleOrDefaultAsync(p =>
            p.TenantId == Actor.Workspace.TenantId && p.UserId == userId, ct);
        return row is null ? new() : Preferences(row);
    }

    public async Task<NotificationPreferencesDto> SavePreferencesAsync(NotificationPreferencesDto preferences, CancellationToken ct)
    {
        using var tenantScope = accessor.Push(Actor.Context);
        await using var db = await factory.CreateDbContextAsync(ct);
        var userId = Actor.ObjectId.ToString();
        var row = await db.NotificationPreferences.SingleOrDefaultAsync(p =>
            p.TenantId == Actor.Workspace.TenantId && p.UserId == userId, ct);
        if (row is null)
        {
            row = new NotificationPreferences { Id = Guid.NewGuid(), TenantId = Actor.Context.EffectiveTenantId, UserId = userId };
            db.NotificationPreferences.Add(row);
        }
        row.PoamOverdueAlerts = preferences.PoamOverdueAlerts;
        row.AtoExpirationAlerts = preferences.AtoExpirationAlerts;
        row.ComplianceDriftAlerts = preferences.ComplianceDriftAlerts;
        row.AlertDaysBefore = preferences.AlertDaysBefore;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Preferences(row);
    }

    private static int Mark(IEnumerable<AlertNotification> rows)
    {
        var now = DateTimeOffset.UtcNow;
        var count = 0;
        foreach (var row in rows.Where(n => !n.IsRead))
        {
            row.IsRead = true;
            row.ReadAt = now;
            count++;
        }
        return count;
    }

    private static async Task<IQueryable<AlertNotification>> PageAsync(
        AtoCopilotContext db, IQueryable<AlertNotification> query, int take, CancellationToken ct)
    {
        if (!db.Database.IsSqlite())
            return query.OrderByDescending(n => n.SentAt).ThenBy(n => n.Id).Take(take);
        // SQLite cannot order DateTimeOffset. Sort authorized metadata, then fetch only the bounded page's bodies.
        var keys = await query.Select(n => new { n.Id, n.SentAt }).ToListAsync(ct);
        var ids = keys.OrderByDescending(n => n.SentAt).ThenBy(n => n.Id).Take(take).Select(n => n.Id).ToArray();
        return query.Where(n => ids.Contains(n.Id));
    }

    private static NotificationPreferencesDto Preferences(NotificationPreferences row) => new()
    {
        PoamOverdueAlerts = row.PoamOverdueAlerts, AtoExpirationAlerts = row.AtoExpirationAlerts,
        ComplianceDriftAlerts = row.ComplianceDriftAlerts, AlertDaysBefore = row.AlertDaysBefore,
    };
}
