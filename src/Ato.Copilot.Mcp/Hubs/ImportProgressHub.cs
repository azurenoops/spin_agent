// ═══════════════════════════════════════════════════════════════════════════
// Feature 204 (UF-005) — T-063-21: SignalR ImportProgressHub
// Clients connect, join a per-job group, and receive ImportProgress events.
// ═══════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Ato.Copilot.Mcp.Hubs.Notifications;

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
[Authorize(AuthenticationSchemes = NotificationHubAuthenticationHandler.SchemeName)]
public sealed class ImportProgressHub(
    NotificationConnectionRegistry connections, WorkspaceProgressDeliveryService progress) : Hub
{
    /// <summary>
    /// Subscribe the caller to progress events for a specific import job.
    /// </summary>
    /// <param name="importJobId">The job ID returned by POST /scans/import.</param>
    public async Task JoinImportGroup(string importJobId)
    {
        var group = await progress.SubscribeAsync(Context.ConnectionId, ProgressResourceKind.Import, importJobId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
    }

    /// <summary>
    /// Unsubscribe the caller from progress events for a specific import job.
    /// </summary>
    /// <param name="importJobId">The job ID to leave.</param>
    public async Task LeaveImportGroup(string importJobId)
    {
        var group = await progress.UnsubscribeAsync(Context.ConnectionId, ProgressResourceKind.Import, importJobId, Context.ConnectionAborted);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
    }

    public override async Task OnConnectedAsync()
    {
        await connections.ConnectAsync(Context.ConnectionId,
            Context.GetHttpContext() ?? throw new HubException("HTTP connection required"), Context.Abort, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
