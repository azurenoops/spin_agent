using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Mcp.Hubs;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// SignalR-based implementation of <see cref="ISspExportNotifier"/>.
/// Pushes SSP export lifecycle events (progress, ready, failed) to connected dashboard clients.
/// </summary>
public class SignalRSspExportNotifier : ISspExportNotifier
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<SignalRSspExportNotifier> _logger;

    public SignalRSspExportNotifier(
        IHubContext<NotificationHub> hubContext,
        ILogger<SignalRSspExportNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendProgressAsync(string userId, Guid exportId, string step, int percentage, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("SspExportProgress", new
            {
                exportId,
                step,
                percentage,
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send progress for export {ExportId}", exportId);
        }
    }

    public async Task SendExportReadyAsync(string userId, Guid exportId, string format, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("SspExportReady", new
            {
                exportId,
                format,
                completedAt = DateTimeOffset.UtcNow,
            }, cancellationToken);

            _logger.LogDebug("Sent SspExportReady for export {ExportId} to user {UserId}", exportId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send SspExportReady for export {ExportId}", exportId);
        }
    }

    public async Task SendExportFailedAsync(string userId, Guid exportId, string error, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group($"user:{userId}").SendAsync("SspExportFailed", new
            {
                exportId,
                error,
                failedAt = DateTimeOffset.UtcNow,
            }, cancellationToken);

            _logger.LogDebug("Sent SspExportFailed for export {ExportId} to user {UserId}", exportId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to send SspExportFailed for export {ExportId}", exportId);
        }
    }
}
