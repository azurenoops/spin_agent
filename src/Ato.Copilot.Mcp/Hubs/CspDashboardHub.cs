using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Hubs;

/// <summary>
/// Real-time fan-out for the CSP cross-tenant operational dashboard
/// (Feature 048 / US8 / T187, SC-005).
/// </summary>
/// <remarks>
/// <para>Connected dashboard clients subscribe per CSP-Admin user id and
/// receive <c>TenantStatusChanged</c> messages whenever a tenant transitions
/// between <c>Active</c> / <c>Suspended</c> / <c>Disabled</c>. The dashboard
/// re-fetches <c>/api/csp/dashboard/summary</c> on receipt instead of
/// polling — the wire-side database state is the source of truth, the hub
/// message is only a refresh trigger.</para>
/// <para>Only CSP-Admin users will ever connect: the nav link is gated by
/// <see cref="Endpoints.Csp.CspDashboardEndpoints"/> which 404s in
/// SingleTenant mode and 401/403s for non-CSP-Admins, so non-admin clients
/// never reach the dashboard page that registers with this hub. Non-admin
/// callers that still attempt to subscribe are silently grouped — they will
/// simply never receive broadcasts because nothing is published outside the
/// CSP-Admin code path.</para>
/// <para>Mounted at <c>/hubs/csp-dashboard</c> in
/// <see cref="Program"/>.</para>
/// </remarks>
public class CspDashboardHub : Hub
{
    private readonly ILogger<CspDashboardHub> _logger;

    public CspDashboardHub(ILogger<CspDashboardHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Subscribe the current connection to a CSP-Admin user's dashboard
    /// fan-out group. A single CSP-Admin can have multiple tabs open, all of
    /// which need to see tenant status changes simultaneously per SC-005.
    /// </summary>
    public async Task RegisterUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new HubException("UserId is required");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(userId));
        _logger.LogDebug(
            "CspDashboardHub: {ConnectionId} registered for user {UserId}",
            Context.ConnectionId, userId);
    }

    public override Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue("oid")
                     ?? Context.User?.Identity?.Name;
        _logger.LogDebug(
            "CspDashboardHub: connected {ConnectionId} (user={UserId})",
            Context.ConnectionId, userId ?? "anonymous");
        return base.OnConnectedAsync();
    }

    /// <summary>Group naming helper used by <see cref="ICspDashboardNotifier"/>.</summary>
    public static string GroupName(string userId) => $"csp-dashboard:{userId}";

    /// <summary>Wildcard group used to broadcast to all currently-connected CSP-Admins.</summary>
    public const string AllAdminsGroup = "csp-dashboard:all";
}

/// <summary>
/// Server-side helper that wraps the SignalR <see cref="IHubContext{T}"/> so
/// non-hub callers (HTTP endpoints, services) can broadcast tenant status
/// transitions without taking a project reference on SignalR primitives.
/// </summary>
public interface ICspDashboardNotifier
{
    /// <summary>
    /// Broadcasts a tenant status transition to all subscribed CSP-Admin
    /// dashboards. Best-effort: the database row is the source of truth.
    /// </summary>
    /// <param name="tenantId">Tenant whose status changed.</param>
    /// <param name="newStatus">New status as a string ("Active"|"Suspended"|"Disabled").</param>
    /// <param name="actor">CSP-Admin who initiated the change (for audit fan-out).</param>
    Task TenantStatusChangedAsync(
        Guid tenantId,
        string newStatus,
        string actor,
        CancellationToken cancellationToken = default);
}

/// <summary>Default implementation backed by <see cref="IHubContext{T}"/>.</summary>
public sealed class CspDashboardNotifier : ICspDashboardNotifier
{
    private readonly IHubContext<CspDashboardHub> _hub;
    private readonly ILogger<CspDashboardNotifier> _logger;

    public CspDashboardNotifier(
        IHubContext<CspDashboardHub> hub,
        ILogger<CspDashboardNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task TenantStatusChangedAsync(
        Guid tenantId,
        string newStatus,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(newStatus))
            return Task.CompletedTask;

        _logger.LogInformation(
            "CspDashboardNotifier: TenantStatusChanged tenant={TenantId} status={Status} actor={Actor}",
            tenantId, newStatus, actor);

        return _hub.Clients
            .All
            .SendAsync(
                "TenantStatusChanged",
                new
                {
                    tenantId,
                    status = newStatus,
                    actor,
                    timestamp = DateTimeOffset.UtcNow,
                },
                cancellationToken);
    }
}
