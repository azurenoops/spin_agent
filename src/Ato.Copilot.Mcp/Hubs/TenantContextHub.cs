using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Hubs;

/// <summary>
/// Real-time channel for tenant impersonation events (Feature 048 §T149).
/// Connected dashboard clients subscribe per CSP-Admin user id and receive
/// <c>ImpersonationStarted</c> / <c>ImpersonationEnded</c> messages so the
/// dashboard header / status bar can refresh without polling.
/// </summary>
/// <remarks>
/// <para>This hub is intentionally minimal: it is a fan-out target, not a
/// command surface. The broadcast is invoked from
/// <see cref="Endpoints.TenantsEndpoints"/> after the impersonation cookie is
/// issued / cleared so the wire-side state is the source of truth, not the
/// hub message.</para>
/// <para>Mounted at <c>/hubs/tenant-context</c> in
/// <see cref="Program"/>.</para>
/// </remarks>
public class TenantContextHub : Hub
{
    private readonly ILogger<TenantContextHub> _logger;

    public TenantContextHub(ILogger<TenantContextHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Subscribe the current connection to a CSP-Admin user's tenant-context
    /// fan-out group. A single CSP-Admin can have multiple tabs open, all of
    /// which need to see impersonation changes simultaneously per SC-005.
    /// </summary>
    public async Task RegisterUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new HubException("UserId is required");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(userId));
        _logger.LogDebug(
            "TenantContextHub: {ConnectionId} registered for user {UserId}",
            Context.ConnectionId, userId);
    }

    public override Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue("oid")
                     ?? Context.User?.Identity?.Name;
        _logger.LogDebug(
            "TenantContextHub: connected {ConnectionId} (user={UserId})",
            Context.ConnectionId, userId ?? "anonymous");
        return base.OnConnectedAsync();
    }

    /// <summary>Group naming helper used by <see cref="ITenantContextNotifier"/>.</summary>
    public static string GroupName(string userId) => $"tenant-context:{userId}";
}

/// <summary>
/// Server-side helper that wraps the SignalR <see cref="IHubContext{T}"/> so
/// non-hub callers (HTTP endpoints, services) can broadcast impersonation
/// events without taking a project reference on SignalR primitives.
/// </summary>
public interface ITenantContextNotifier
{
    Task ImpersonationStartedAsync(
        string userId,
        Guid impersonatedTenantId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    Task ImpersonationEndedAsync(
        string userId,
        CancellationToken cancellationToken = default);
}

/// <summary>Default implementation backed by <see cref="IHubContext{T}"/>.</summary>
public sealed class TenantContextNotifier : ITenantContextNotifier
{
    private readonly IHubContext<TenantContextHub> _hub;
    private readonly ILogger<TenantContextNotifier> _logger;

    public TenantContextNotifier(
        IHubContext<TenantContextHub> hub,
        ILogger<TenantContextNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task ImpersonationStartedAsync(
        string userId,
        Guid impersonatedTenantId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Task.CompletedTask;
        _logger.LogInformation(
            "TenantContextNotifier: ImpersonationStarted user={UserId} tenant={TenantId}",
            userId, impersonatedTenantId);
        return _hub.Clients
            .Group(TenantContextHub.GroupName(userId))
            .SendAsync(
                "ImpersonationStarted",
                new { impersonatedTenantId, expiresAt },
                cancellationToken);
    }

    public Task ImpersonationEndedAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Task.CompletedTask;
        _logger.LogInformation(
            "TenantContextNotifier: ImpersonationEnded user={UserId}", userId);
        return _hub.Clients
            .Group(TenantContextHub.GroupName(userId))
            .SendAsync("ImpersonationEnded", new { }, cancellationToken);
    }
}
