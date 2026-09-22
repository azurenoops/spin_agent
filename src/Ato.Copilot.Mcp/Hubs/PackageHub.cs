using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Ato.Copilot.Mcp.Hubs.Notifications;

namespace Ato.Copilot.Mcp.Hubs;

/// <summary>
/// SignalR hub for real-time authorization package generation progress.
/// Clients join a package-specific group to receive status updates.
/// </summary>
[Authorize(AuthenticationSchemes = NotificationHubAuthenticationHandler.SchemeName)]
public class PackageHub : Hub
{
    private readonly ILogger<PackageHub> _logger;
    private readonly NotificationConnectionRegistry _connections;
    private readonly WorkspaceProgressDeliveryService _progress;

    public PackageHub(ILogger<PackageHub> logger, NotificationConnectionRegistry connections, WorkspaceProgressDeliveryService progress)
    {
        _logger = logger;
        _connections = connections;
        _progress = progress;
    }

    /// <summary>
    /// Called by the client to subscribe to updates for a specific package.
    /// </summary>
    public async Task SubscribeToPackage(string packageId)
    {
        var group = await _progress.SubscribeAsync(Context.ConnectionId, ProgressResourceKind.Package, packageId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
        _logger.LogInformation("PackageHub: {ConnectionId} subscribed to package {PackageId}",
            Context.ConnectionId, packageId);
    }

    /// <summary>
    /// Called by the client to unsubscribe from a package's updates.
    /// </summary>
    public async Task UnsubscribeFromPackage(string packageId)
    {
        var group = await _progress.UnsubscribeAsync(Context.ConnectionId, ProgressResourceKind.Package, packageId, Context.ConnectionAborted);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
    }

    public override async Task OnConnectedAsync()
    {
        await _connections.ConnectAsync(Context.ConnectionId,
            Context.GetHttpContext() ?? throw new HubException("HTTP connection required"), Context.Abort, Context.ConnectionAborted);
        _logger.LogInformation("PackageHub connection: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
