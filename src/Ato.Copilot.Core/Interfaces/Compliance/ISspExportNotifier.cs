namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Broadcasts SSP export lifecycle events to connected clients via SignalR.
/// Implemented in the Mcp layer; consumed by SspExportService in the Agents layer.
/// </summary>
public interface ISspExportNotifier
{
    /// <summary>Report progress during export processing.</summary>
    Task SendProgressAsync(string userId, Guid exportId, string step, int percentage, CancellationToken cancellationToken = default);

    /// <summary>Notify that an export completed successfully.</summary>
    Task SendExportReadyAsync(string userId, Guid exportId, string format, CancellationToken cancellationToken = default);

    /// <summary>Notify that an export failed.</summary>
    Task SendExportFailedAsync(string userId, Guid exportId, string error, CancellationToken cancellationToken = default);
}
