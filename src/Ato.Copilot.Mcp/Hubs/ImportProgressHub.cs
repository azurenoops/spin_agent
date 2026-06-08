// ═══════════════════════════════════════════════════════════════════════════
// Feature 204 (UF-005) — T-063-21: SignalR ImportProgressHub
// Clients connect, join a per-job group, and receive ImportProgress events.
// ═══════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.SignalR;

namespace Ato.Copilot.Mcp.Hubs;

/// <summary>
/// SignalR hub for real-time scan import progress delivery.
///
/// Client flow:
///   1. Connect to /hubs/import-progress
///   2. invoke("JoinImportGroup", importJobId) — subscribes to progress events
///   3. Listen for "ImportProgress" events:
///        { processedCount, totalCount, status, errorMessage }
///   4. invoke("LeaveImportGroup", importJobId) — when done / dialog closed
/// </summary>
public sealed class ImportProgressHub : Hub
{
    /// <summary>
    /// Subscribe the caller to progress events for a specific import job.
    /// </summary>
    /// <param name="importJobId">The job ID returned by POST /scans/import.</param>
    public async Task JoinImportGroup(string importJobId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"import:{importJobId}");

    /// <summary>
    /// Unsubscribe the caller from progress events for a specific import job.
    /// </summary>
    /// <param name="importJobId">The job ID to leave.</param>
    public async Task LeaveImportGroup(string importJobId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"import:{importJobId}");
}
