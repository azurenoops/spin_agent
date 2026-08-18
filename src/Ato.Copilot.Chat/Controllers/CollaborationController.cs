// =============================================================================
// CollaborationController.cs — #1357 Sub-task 1: Share & Access Panel
//
// REST endpoints for document sharing and access management:
//   GET    /api/documents/{documentId}/collaborators
//   POST   /api/documents/{documentId}/collaborators
//   PATCH  /api/documents/{documentId}/collaborators/{collaboratorId}
//   DELETE /api/documents/{documentId}/collaborators/{collaboratorId}
//   GET    /api/documents/{documentId}/link-sharing
//   PUT    /api/documents/{documentId}/link-sharing
//   GET    /api/documents/{documentId}/visibility        ← AppBar badge (AC-2)
// =============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Chat.Data;
using Ato.Copilot.Chat.Models;

namespace Ato.Copilot.Chat.Controllers;

[ApiController]
[Route("api/documents/{documentId}")]
public class CollaborationController : ControllerBase
{
    private readonly ChatDbContext _db;
    private readonly ILogger<CollaborationController> _logger;

    public CollaborationController(ChatDbContext db, ILogger<CollaborationController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ─── Collaborators ────────────────────────────────────────────────────────

    /// <summary>List all collaborators for a document.</summary>
    [HttpGet("collaborators")]
    public async Task<IActionResult> ListCollaborators(string documentId)
    {
        var collaborators = await _db.DocumentCollaborators
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.InvitedAt)
            .ToListAsync();

        var dtos = collaborators.Select(c => new CollaboratorDto(
            c.Id,
            c.Email,
            c.DisplayName,
            c.Permission,
            c.InvitedAt,
            c.AcceptedAt.HasValue
        )).ToList();

        return Ok(dtos);
    }

    /// <summary>Invite a collaborator.</summary>
    [HttpPost("collaborators")]
    public async Task<IActionResult> AddCollaborator(
        string documentId,
        [FromBody] InviteCollaboratorRequest request,
        [FromHeader(Name = "X-User-Id")] string? invitedByUserId = null)
    {
        // Idempotent: update permission if the email is already a collaborator.
        var existing = await _db.DocumentCollaborators
            .FirstOrDefaultAsync(c => c.DocumentId == documentId &&
                                      c.Email == request.Email.ToLowerInvariant());

        if (existing is not null)
        {
            existing.Permission = request.Permission;
            await _db.SaveChangesAsync();
            return Ok(ToDto(existing));
        }

        var collab = new DocumentCollaborator
        {
            DocumentId = documentId,
            Email = request.Email.ToLowerInvariant(),
            Permission = request.Permission,
            InvitedByUserId = invitedByUserId ?? string.Empty,
        };

        _db.DocumentCollaborators.Add(collab);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Collaborator {Email} added to document {DocumentId} with permission {Permission}",
            collab.Email, documentId, collab.Permission);

        return Created(
            $"/api/documents/{documentId}/collaborators/{collab.Id}",
            ToDto(collab));
    }

    /// <summary>Update a collaborator's permission.</summary>
    [HttpPatch("collaborators/{collaboratorId}")]
    public async Task<IActionResult> UpdateCollaborator(
        string documentId,
        string collaboratorId,
        [FromBody] UpdateCollaboratorRequest request)
    {
        var collab = await _db.DocumentCollaborators
            .FirstOrDefaultAsync(c => c.DocumentId == documentId && c.Id == collaboratorId);

        if (collab is null)
            return NotFound();

        collab.Permission = request.Permission;
        await _db.SaveChangesAsync();
        return Ok(ToDto(collab));
    }

    /// <summary>Remove a collaborator.</summary>
    [HttpDelete("collaborators/{collaboratorId}")]
    public async Task<IActionResult> RemoveCollaborator(string documentId, string collaboratorId)
    {
        var collab = await _db.DocumentCollaborators
            .FirstOrDefaultAsync(c => c.DocumentId == documentId && c.Id == collaboratorId);

        if (collab is null)
            return NotFound();

        _db.DocumentCollaborators.Remove(collab);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Collaborator {CollaboratorId} removed from document {DocumentId}",
            collaboratorId, documentId);

        return NoContent();
    }

    // ─── Link sharing ─────────────────────────────────────────────────────────

    /// <summary>Get current link-sharing setting.</summary>
    [HttpGet("link-sharing")]
    public async Task<IActionResult> GetLinkSharing(string documentId)
    {
        var ls = await _db.DocumentLinkSharing
            .FirstOrDefaultAsync(e => e.DocumentId == documentId);

        return Ok(new LinkSharingDto(ls?.LinkPermission));
    }

    /// <summary>Set (or update) link-sharing permission. Null permission disables link sharing.</summary>
    [HttpPut("link-sharing")]
    public async Task<IActionResult> SetLinkSharing(
        string documentId,
        [FromBody] SetLinkSharingRequest request)
    {
        var ls = await _db.DocumentLinkSharing
            .FirstOrDefaultAsync(e => e.DocumentId == documentId);

        if (ls is null)
        {
            ls = new DocumentLinkSharing { DocumentId = documentId };
            _db.DocumentLinkSharing.Add(ls);
        }

        ls.LinkPermission = request.LinkPermission;
        ls.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new LinkSharingDto(ls.LinkPermission));
    }

    // ─── Visibility badge (AC-2) ──────────────────────────────────────────────

    /// <summary>
    /// Returns the AppBar visibility badge string for the document:
    /// "Private" | "Shared with N" | "Link sharing on".
    /// </summary>
    [HttpGet("visibility")]
    public async Task<IActionResult> GetVisibility(string documentId)
    {
        var count = await _db.DocumentCollaborators
            .CountAsync(c => c.DocumentId == documentId);

        var ls = await _db.DocumentLinkSharing
            .FirstOrDefaultAsync(e => e.DocumentId == documentId);

        string badge;
        if (ls?.LinkPermission is not null)
            badge = "Link sharing on";
        else if (count > 0)
            badge = $"Shared with {count}";
        else
            badge = "Private";

        return Ok(new { badge, collaboratorCount = count, linkPermission = ls?.LinkPermission?.ToString() });
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static CollaboratorDto ToDto(DocumentCollaborator c) =>
        new(c.Id, c.Email, c.DisplayName, c.Permission, c.InvitedAt, c.AcceptedAt.HasValue);
}
