using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Hubs;

/// <summary>
/// SignalR hub for real-time authorization package generation progress.
/// Clients join a package-specific group to receive status updates.
/// </summary>
public class PackageHub : Hub
{
    private readonly ILogger<PackageHub> _logger;

    public PackageHub(ILogger<PackageHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Called by the client to subscribe to updates for a specific package.
    /// </summary>
    public async Task SubscribeToPackage(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new HubException("PackageId is required");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"package:{packageId}");
        _logger.LogInformation("PackageHub: {ConnectionId} subscribed to package {PackageId}",
            Context.ConnectionId, packageId);
    }

    /// <summary>
    /// Called by the client to unsubscribe from a package's updates.
    /// </summary>
    public async Task UnsubscribeFromPackage(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new HubException("PackageId is required");

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"package:{packageId}");
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("PackageHub connection: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }
}
