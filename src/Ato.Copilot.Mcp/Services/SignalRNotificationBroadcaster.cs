using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Hubs;
using Ato.Copilot.Mcp.Hubs.Notifications;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// SignalR-based implementation of <see cref="INotificationBroadcaster"/>.
/// Pushes real-time notification events to connected dashboard clients.
/// </summary>
public class SignalRNotificationBroadcaster : INotificationBroadcaster
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<SignalRNotificationBroadcaster> _logger;
    private readonly NotificationDeliveryService _delivery;

    public SignalRNotificationBroadcaster(
        IHubContext<NotificationHub> hubContext,
        ILogger<SignalRNotificationBroadcaster> logger,
        NotificationDeliveryService delivery)
    {
        _hubContext = hubContext;
        _logger = logger;
        _delivery = delivery;
    }

    public async Task BroadcastToUserAsync(string userId, AlertNotification notification, CancellationToken cancellationToken = default)
    {
        await _delivery.BroadcastAsync(userId, notification, SendAsync, cancellationToken);

        _logger.LogDebug("Processed notification delivery {NotificationId}", notification.Id);
    }

    public async Task BroadcastUnreadCountAsync(string userId, int unreadCount, CancellationToken cancellationToken = default)
    {
        // The legacy argument may aggregate multiple workspaces; recompute from authorized rows.
        await _delivery.UnreadCountAsync(userId, SendAsync, cancellationToken);
    }

    private Task SendAsync(string connectionId, string method, object?[] arguments, CancellationToken ct) =>
        _hubContext.Clients.Client(connectionId).SendCoreAsync(method, arguments, ct);
}
