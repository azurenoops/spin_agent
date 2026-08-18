using System.ComponentModel.DataAnnotations;

namespace Ato.Copilot.Chat.Models;

// =============================================================================
// CollaborationModels — #1357 Workspace Collaboration & Document Sharing
//
// EF Core entities and DTO records for the four collaboration sub-tasks:
//   1. Document collaborators (share + access panel)
//   2. SignalR presence / cursor payloads (not persisted — in-memory only)
//   3. Section lock payloads (not persisted — in-memory only)
//   4. Document comments / annotation threads
// =============================================================================

// ─── Sub-task 1: Collaborator entity ─────────────────────────────────────────

/// <summary>
/// Permission tier for a document collaborator. Matches the frontend
/// <c>CollaboratorPermission</c> union type.
/// </summary>
public enum CollaboratorPermission
{
    View,
    Comment,
    Edit,
}

/// <summary>
/// A person who has been granted access to a document (conversation).
/// Stored in <see cref="Ato.Copilot.Chat.Data.ChatDbContext.DocumentCollaborators"/>.
/// </summary>
public class DocumentCollaborator
{
    [Key]
    [MaxLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The conversation/document this access record belongs to.</summary>
    [Required]
    [MaxLength(450)]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Email address of the invited collaborator.</summary>
    [Required]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Display name — populated when the invite is accepted.</summary>
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    /// <summary>Assigned access tier.</summary>
    public CollaboratorPermission Permission { get; set; } = CollaboratorPermission.View;

    /// <summary>UTC timestamp of the invite.</summary>
    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when the invite was accepted, or null if pending.</summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>UserId of the person who sent the invite.</summary>
    [MaxLength(450)]
    public string InvitedByUserId { get; set; } = string.Empty;
}

/// <summary>
/// Link-sharing setting for a document.
/// Stored in <see cref="Ato.Copilot.Chat.Data.ChatDbContext.DocumentLinkSharing"/>.
/// </summary>
public class DocumentLinkSharing
{
    [Key]
    [MaxLength(450)]
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>Permission for anyone who has the link. Null = link sharing off.</summary>
    public CollaboratorPermission? LinkPermission { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ─── Sub-task 1: REST DTOs ────────────────────────────────────────────────────

public record InviteCollaboratorRequest(
    string Email,
    CollaboratorPermission Permission
);

public record UpdateCollaboratorRequest(
    CollaboratorPermission Permission
);

public record SetLinkSharingRequest(
    CollaboratorPermission? LinkPermission  // null = off
);

public record CollaboratorDto(
    string Id,
    string Email,
    string? DisplayName,
    CollaboratorPermission Permission,
    DateTime InvitedAt,
    bool Accepted
);

public record LinkSharingDto(
    CollaboratorPermission? LinkPermission
);

// ─── Sub-task 2: Presence payloads (SignalR — not persisted) ─────────────────

/// <summary>
/// Broadcast by a client when it joins/leaves a document or moves its cursor.
/// Hub method: <c>UpdatePresence</c>.
/// </summary>
public record PresenceUpdatePayload(
    string DocumentId,
    string UserId,
    string DisplayName,
    /// <summary>Hex color string assigned to this session, e.g. "#3b82f6".</summary>
    string Color,
    /// <summary>"active" | "idle" | "left"</summary>
    string Status,
    CursorPositionPayload? Cursor
);

/// <summary>
/// A text-editor cursor position with optional block/offset coordinates for
/// section-level presence rendering.
/// </summary>
public record CursorPositionPayload(
    string BlockId,
    int Offset,
    /// <summary>Optional: client viewport Y position (px from top of editor) for overlay rendering.</summary>
    double? ViewportY
);

// ─── Sub-task 3: Section lock payloads (SignalR — not persisted) ──────────────

/// <summary>
/// Sent by a client when a user focuses (locks) or blurs (releases) a block.
/// Hub methods: <c>ClaimLock</c> / <c>ReleaseLock</c>.
/// </summary>
public record LockClaimPayload(
    string DocumentId,
    string BlockId,
    string UserId,
    string DisplayName,
    string Color
);

public record LockReleasePayload(
    string DocumentId,
    string BlockId,
    string UserId
);

/// <summary>
/// Returned to all clients when a lock conflict is detected (two users claiming
/// the same block without a prior release).  The frontend renders a
/// <c>ConflictResolutionBanner</c> when it receives this event.
/// </summary>
public record ConflictPayload(
    string DocumentId,
    string BlockId,
    string IncumbentUserId,
    string IncumbentContent,
    string ChallengerUserId,
    string ChallengerContent
);

// ─── Sub-task 4: Comment entities ────────────────────────────────────────────

/// <summary>
/// A comment reply within a thread.
/// </summary>
public class CommentReply
{
    [Key]
    [MaxLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(450)]
    public string ThreadId { get; set; } = string.Empty;

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? DisplayName { get; set; }

    [Required]
    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Nav
    public DocumentCommentThread? Thread { get; set; }
}

/// <summary>
/// An inline comment thread anchored to a text selection in a document.
/// Stored in <see cref="Ato.Copilot.Chat.Data.ChatDbContext.DocumentCommentThreads"/>.
/// </summary>
public class DocumentCommentThread
{
    [Key]
    [MaxLength(450)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(450)]
    public string DocumentId { get; set; } = string.Empty;

    // Text anchor — block + character offsets within that block
    [Required]
    [MaxLength(450)]
    public string AnchorBlockId { get; set; } = string.Empty;
    public int AnchorStart { get; set; }
    public int AnchorEnd { get; set; }

    // Opening comment (author of the thread)
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? DisplayName { get; set; }

    [Required]
    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when the thread was resolved, or null if open.</summary>
    public DateTime? ResolvedAt { get; set; }

    [MaxLength(450)]
    public string? ResolvedByUserId { get; set; }

    // Nav
    public List<CommentReply> Replies { get; set; } = new();
}

// ─── Sub-task 4: REST DTOs ────────────────────────────────────────────────────

public record CreateCommentRequest(
    string AnchorBlockId,
    int AnchorStart,
    int AnchorEnd,
    string Text,
    string UserId,
    string? DisplayName
);

public record ReplyToCommentRequest(
    string Text,
    string UserId,
    string? DisplayName
);

public record ResolveCommentRequest(
    string ResolvedByUserId
);

public record CommentReplyDto(
    string Id,
    string UserId,
    string? DisplayName,
    string Text,
    DateTime CreatedAt
);

public record CommentThreadDto(
    string Id,
    string DocumentId,
    string AnchorBlockId,
    int AnchorStart,
    int AnchorEnd,
    string UserId,
    string? DisplayName,
    string Text,
    DateTime CreatedAt,
    bool Resolved,
    DateTime? ResolvedAt,
    List<CommentReplyDto> Replies
);
