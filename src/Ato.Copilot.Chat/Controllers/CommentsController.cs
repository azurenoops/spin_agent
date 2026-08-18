// =============================================================================
// CommentsController.cs — #1357 Sub-task 4: Comment/Annotation System
//
// REST endpoints for document comment threads:
//   GET    /api/documents/{documentId}/comments
//   POST   /api/documents/{documentId}/comments
//   POST   /api/documents/{documentId}/comments/{threadId}/replies
//   PATCH  /api/documents/{documentId}/comments/{threadId}/resolve
//   DELETE /api/documents/{documentId}/comments/{threadId}
// =============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Chat.Data;
using Ato.Copilot.Chat.Models;

namespace Ato.Copilot.Chat.Controllers;

[ApiController]
[Route("api/documents/{documentId}/comments")]
public class CommentsController : ControllerBase
{
    private readonly ChatDbContext _db;
    private readonly ILogger<CommentsController> _logger;

    public CommentsController(ChatDbContext db, ILogger<CommentsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ─── Threads ──────────────────────────────────────────────────────────────

    /// <summary>List all comment threads for a document (open + resolved).</summary>
    [HttpGet]
    public async Task<IActionResult> ListComments(
        string documentId,
        [FromQuery] bool includeResolved = true)
    {
        var query = _db.DocumentCommentThreads
            .Include(t => t.Replies)
            .Where(t => t.DocumentId == documentId);

        if (!includeResolved)
            query = query.Where(t => t.ResolvedAt == null);

        var threads = await query.OrderBy(t => t.CreatedAt).ToListAsync();
        return Ok(threads.Select(ToDto).ToList());
    }

    /// <summary>Create a new comment thread anchored to a text selection.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateComment(
        string documentId,
        [FromBody] CreateCommentRequest request)
    {
        var thread = new DocumentCommentThread
        {
            DocumentId = documentId,
            AnchorBlockId = request.AnchorBlockId,
            AnchorStart = request.AnchorStart,
            AnchorEnd = request.AnchorEnd,
            UserId = request.UserId,
            DisplayName = request.DisplayName,
            Text = request.Text,
        };

        _db.DocumentCommentThreads.Add(thread);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Comment thread {ThreadId} created in document {DocumentId} by {UserId}",
            thread.Id, documentId, request.UserId);

        return Created(
            $"/api/documents/{documentId}/comments/{thread.Id}",
            ToDto(thread));
    }

    // ─── Replies ──────────────────────────────────────────────────────────────

    /// <summary>Add a reply to a thread.</summary>
    [HttpPost("{threadId}/replies")]
    public async Task<IActionResult> AddReply(
        string documentId,
        string threadId,
        [FromBody] ReplyToCommentRequest request)
    {
        var thread = await _db.DocumentCommentThreads
            .FirstOrDefaultAsync(t => t.DocumentId == documentId && t.Id == threadId);

        if (thread is null)
            return NotFound();

        var reply = new CommentReply
        {
            ThreadId = threadId,
            UserId = request.UserId,
            DisplayName = request.DisplayName,
            Text = request.Text,
        };

        _db.CommentReplies.Add(reply);
        await _db.SaveChangesAsync();

        return Ok(new CommentReplyDto(
            reply.Id,
            reply.UserId,
            reply.DisplayName,
            reply.Text,
            reply.CreatedAt));
    }

    // ─── Resolve ──────────────────────────────────────────────────────────────

    /// <summary>Resolve (or re-open) a comment thread.</summary>
    [HttpPatch("{threadId}/resolve")]
    public async Task<IActionResult> ResolveComment(
        string documentId,
        string threadId,
        [FromBody] ResolveCommentRequest request)
    {
        var thread = await _db.DocumentCommentThreads
            .Include(t => t.Replies)
            .FirstOrDefaultAsync(t => t.DocumentId == documentId && t.Id == threadId);

        if (thread is null)
            return NotFound();

        thread.ResolvedAt = DateTime.UtcNow;
        thread.ResolvedByUserId = request.ResolvedByUserId;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Comment thread {ThreadId} resolved by {UserId}",
            threadId, request.ResolvedByUserId);

        return Ok(ToDto(thread));
    }

    /// <summary>Delete a comment thread and all replies.</summary>
    [HttpDelete("{threadId}")]
    public async Task<IActionResult> DeleteComment(string documentId, string threadId)
    {
        var thread = await _db.DocumentCommentThreads
            .FirstOrDefaultAsync(t => t.DocumentId == documentId && t.Id == threadId);

        if (thread is null)
            return NotFound();

        _db.DocumentCommentThreads.Remove(thread);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // ─── Mapping helper ───────────────────────────────────────────────────────

    private static CommentThreadDto ToDto(DocumentCommentThread t) => new(
        t.Id,
        t.DocumentId,
        t.AnchorBlockId,
        t.AnchorStart,
        t.AnchorEnd,
        t.UserId,
        t.DisplayName,
        t.Text,
        t.CreatedAt,
        t.ResolvedAt.HasValue,
        t.ResolvedAt,
        (t.Replies ?? []).Select(r => new CommentReplyDto(
            r.Id,
            r.UserId,
            r.DisplayName,
            r.Text,
            r.CreatedAt
        )).ToList()
    );
}
