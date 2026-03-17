using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Hubs;

/// <summary>
/// SignalR hub for real-time notification delivery to connected dashboard clients.
/// Clients join a user-specific group to receive their notifications.
/// </summary>
public class NotificationHub : Hub
{
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ILogger<NotificationHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Called by the client to register for notifications under a user ID.
    /// </summary>
    public async Task RegisterUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new HubException("UserId is required");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        _logger.LogInformation("NotificationHub: {ConnectionId} registered for user {UserId}",
            Context.ConnectionId, userId);
    }

    /// <summary>
    /// Called by the client to mark a notification as read in real-time.
    /// </summary>
    public async Task MarkRead(string notificationId)
    {
        if (string.IsNullOrWhiteSpace(notificationId))
            throw new HubException("NotificationId is required");

        // Broadcast to all connections for this user so multi-tab stays in sync
        var userId = Context.User?.Identity?.Name ?? Context.ConnectionId;
        await Clients.Group($"user:{userId}").SendAsync("NotificationRead", notificationId);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("NotificationHub connection: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("NotificationHub disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
