using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Hubs;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// SignalR-based implementation of <see cref="INotificationBroadcaster"/>.
/// Pushes real-time notification events to connected dashboard clients.
/// </summary>
public class SignalRNotificationBroadcaster : INotificationBroadcaster
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<SignalRNotificationBroadcaster> _logger;

    public SignalRNotificationBroadcaster(
        IHubContext<NotificationHub> hubContext,
        ILogger<SignalRNotificationBroadcaster> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task BroadcastToUserAsync(string userId, AlertNotification notification, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"user:{userId}").SendAsync("NewNotification", new
        {
            id = notification.Id,
            alertId = notification.AlertId,
            channel = notification.Channel.ToString(),
            subject = notification.Subject,
            body = notification.Body,
            sentAt = notification.SentAt,
            isRead = notification.IsRead,
        }, cancellationToken);

        _logger.LogDebug("Broadcast notification {NotificationId} to user {UserId}", notification.Id, userId);
    }

    public async Task BroadcastUnreadCountAsync(string userId, int unreadCount, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group($"user:{userId}").SendAsync("UnreadCountUpdated", new
        {
            unreadCount,
        }, cancellationToken);
    }
}
