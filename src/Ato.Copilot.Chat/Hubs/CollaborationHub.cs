// =============================================================================
// CollaborationHub.cs — #1357 Workspace Collaboration & Document Sharing
//
// SignalR hub for real-time collaboration events:
//   Sub-task 2 — Presence & Cursor System
//   Sub-task 3 — Section Locking
//
// Group naming convention: "doc:{conversationId}" — parallel to ChatHub's
// conversation groups so both hubs can coexist without collision.
//
// All presence payloads are broadcast to OthersInGroup so the sender
// does not receive its own events back.
// =============================================================================

using Microsoft.AspNetCore.SignalR;

namespace Ato.Copilot.Chat.Hubs;

/// <summary>Cursor position reported by a connected user.</summary>
public sealed record CursorPositionPayload(
    string UserId,
    string? DisplayName,
    /// <summary>Top offset in pixels from the top of the scroll container.</summary>
    double Top,
    /// <summary>Left offset in pixels from the left edge of the editor.</summary>
    double Left,
    /// <summary>Optional blockId / messageId the cursor is inside.</summary>
    string? BlockId);

/// <summary>Section lock claim — sent when a user focuses a block.</summary>
public sealed record LockClaimPayload(
    string UserId,
    string? DisplayName,
    string BlockId);

/// <summary>Section lock release — sent on blur or 10-second inactivity timeout.</summary>
public sealed record LockReleasePayload(
    string UserId,
    string BlockId);

/// <summary>User joined / left the document.</summary>
public sealed record PresencePayload(
    string UserId,
    string? DisplayName,
    /// <summary>"viewing" or "editing"</summary>
    string Status);

/// <summary>
/// SignalR hub for real-time collaboration.
/// Clients join the "doc:{conversationId}" group on <see cref="JoinDocument"/>
/// and leave on <see cref="LeaveDocument"/> or disconnect.
/// </summary>
public class CollaborationHub : Hub
{
    private readonly ILogger<CollaborationHub> _logger;

    // In-memory presence map: connectionId → (conversationId, userId, displayName)
    // Keyed by connectionId so we can clean up on disconnect without client participation.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string, (string ConvId, string UserId, string? DisplayName)> _presence = new();

    public CollaborationHub(ILogger<CollaborationHub> logger)
    {
        _logger = logger;
    }

    // ─── Group helpers ────────────────────────────────────────────────────────

    private static string GroupName(string conversationId) => $"doc:{conversationId}";

    // ─── Join / Leave ─────────────────────────────────────────────────────────

    /// <summary>
    /// Join the collaboration group for a document.
    /// Broadcasts a "UserJoined" presence event to all other members.
    /// </summary>
    public async Task JoinDocument(string conversationId, string userId, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(userId))
            throw new HubException("conversationId and userId are required");

        var group = GroupName(conversationId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);

        _presence[Context.ConnectionId] = (conversationId, userId, displayName);

        _logger.LogInformation(
            "CollaborationHub: {UserId} joined doc {ConversationId} (conn {ConnectionId})",
            userId, conversationId, Context.ConnectionId);

        await Clients.OthersInGroup(group).SendAsync("UserJoined", new PresencePayload(
            userId, displayName, "viewing"));
    }

    /// <summary>
    /// Leave the collaboration group for a document.
    /// Broadcasts a "UserLeft" presence event.
    /// </summary>
    public async Task LeaveDocument(string conversationId, string userId)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return;

        var group = GroupName(conversationId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        _presence.TryRemove(Context.ConnectionId, out _);

        await Clients.OthersInGroup(group).SendAsync("UserLeft", new PresencePayload(
            userId, null, "left"));
    }

    // ─── Sub-task 2: Cursor presence ─────────────────────────────────────────

    /// <summary>
    /// Broadcast the caller's cursor position to all other members of the document group.
    /// Target interval: ≤100ms (throttling is client-side).
    /// </summary>
    public async Task UpdateCursor(string conversationId, CursorPositionPayload payload)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return;
        await Clients.OthersInGroup(GroupName(conversationId))
            .SendAsync("CursorMoved", payload);
    }

    // ─── Sub-task 3: Section locking ─────────────────────────────────────────

    /// <summary>
    /// Claim a lock on a block. Other users will receive "BlockLocked" and
    /// should prevent editing that block.
    /// </summary>
    public async Task ClaimLock(string conversationId, LockClaimPayload payload)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return;

        _logger.LogDebug(
            "CollaborationHub: {UserId} claimed lock on block {BlockId} in {ConversationId}",
            payload.UserId, payload.BlockId, conversationId);

        await Clients.OthersInGroup(GroupName(conversationId))
            .SendAsync("BlockLocked", payload);
    }

    /// <summary>
    /// Release a lock on a block. Other users will receive "BlockUnlocked"
    /// and should re-enable editing. Must propagate within 300ms (AC-5).
    /// </summary>
    public async Task ReleaseLock(string conversationId, LockReleasePayload payload)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return;

        await Clients.OthersInGroup(GroupName(conversationId))
            .SendAsync("BlockUnlocked", payload);
    }

    // ─── Disconnect cleanup ───────────────────────────────────────────────────

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_presence.TryRemove(Context.ConnectionId, out var info))
        {
            var group = GroupName(info.ConvId);
            await Clients.Group(group).SendAsync("UserLeft", new PresencePayload(
                info.UserId, info.DisplayName, "left"));

            _logger.LogInformation(
                "CollaborationHub: {UserId} disconnected from doc {ConversationId}",
                info.UserId, info.ConvId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
