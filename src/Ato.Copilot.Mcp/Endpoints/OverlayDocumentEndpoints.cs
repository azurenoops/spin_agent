using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// Admin-only CRUD endpoints for NIST control overlay documents.
/// Overlays extend the SP 800-53 baseline with Navy, DoD, and NSS-specific guidance.
/// </summary>
public static class OverlayDocumentEndpoints
{
    public static IEndpointRouteBuilder MapOverlayDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/overlay-documents")
            .WithTags("OverlayDocuments")
            .RequireAuthorization("AdminOnly");

        // GET /api/v1/admin/overlay-documents
        group.MapGet("/", async (
                string? controlId,
                string? type,
                bool? includeInactive,
                AtoCopilotContext db,
                CancellationToken ct) =>
        {
            var query = db.OverlayDocuments.AsQueryable();
            // By default only return active documents; opt-in to include inactive.
            if (!(includeInactive ?? false))
                query = query.Where(o => o.IsActive);
            if (!string.IsNullOrEmpty(controlId))
                query = query.Where(o => o.ControlId == controlId.ToUpperInvariant());
            if (!string.IsNullOrEmpty(type))
                query = query.Where(o => o.Type == type);

            var items = await query
                .OrderBy(o => o.ControlId).ThenBy(o => o.Type)
                .Select(o => new OverlayDocumentDto(o))
                .ToListAsync(ct);
            return Results.Ok(items);
        }).WithName("ListOverlayDocuments");

        // GET /api/v1/admin/overlay-documents/{id}
        group.MapGet("/{id:guid}", async (
                Guid id,
                AtoCopilotContext db,
                CancellationToken ct) =>
        {
            var doc = await db.OverlayDocuments.FindAsync([id], ct);
            return doc is null ? Results.NotFound() : Results.Ok(new OverlayDocumentDto(doc));
        }).WithName("GetOverlayDocument");

        // POST /api/v1/admin/overlay-documents
        group.MapPost("/", async (
                CreateOverlayDocumentRequest req,
                HttpContext httpContext,
                AtoCopilotContext db,
                CancellationToken ct) =>
        {
            var actor = httpContext.User.FindFirstValue("oid")
                     ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? "system";

            var doc = new OverlayDocument
            {
                Type = req.Type,
                Title = req.Title,
                ControlId = req.ControlId.ToUpperInvariant(),
                Content = req.Content,
                SourceReference = req.SourceReference,
                TenantId = req.TenantId,
                IsActive = true,
                CreatedBy = actor
            };
            db.OverlayDocuments.Add(doc);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/overlay-documents/{doc.Id}", new OverlayDocumentDto(doc));
        }).WithName("CreateOverlayDocument");

        // PUT /api/v1/admin/overlay-documents/{id}
        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateOverlayDocumentRequest req,
                HttpContext httpContext,
                AtoCopilotContext db,
                CancellationToken ct) =>
        {
            var doc = await db.OverlayDocuments.FindAsync([id], ct);
            if (doc is null) return Results.NotFound();

            var actor = httpContext.User.FindFirstValue("oid")
                     ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? "system";

            doc.Title = req.Title ?? doc.Title;
            doc.Content = req.Content ?? doc.Content;
            doc.SourceReference = req.SourceReference ?? doc.SourceReference;
            doc.IsActive = req.IsActive ?? doc.IsActive;
            doc.ModifiedBy = actor;
            doc.ModifiedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(new OverlayDocumentDto(doc));
        }).WithName("UpdateOverlayDocument");

        // DELETE /api/v1/admin/overlay-documents/{id}  (soft delete)
        group.MapDelete("/{id:guid}", async (
                Guid id,
                HttpContext httpContext,
                AtoCopilotContext db,
                CancellationToken ct) =>
        {
            var doc = await db.OverlayDocuments.FindAsync([id], ct);
            if (doc is null) return Results.NotFound();

            var actor = httpContext.User.FindFirstValue("oid")
                     ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? "system";

            doc.IsActive = false;
            doc.ModifiedBy = actor;
            doc.ModifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithName("DeleteOverlayDocument");

        return app;
    }
}

public record OverlayDocumentDto(Guid Id, string Type, string Title, string ControlId,
    string Content, string? SourceReference, Guid? TenantId, bool IsActive,
    string CreatedBy, DateTime CreatedAt, string? ModifiedBy, DateTime? ModifiedAt)
{
    public OverlayDocumentDto(OverlayDocument o)
        : this(o.Id, o.Type, o.Title, o.ControlId, o.Content, o.SourceReference,
               o.TenantId, o.IsActive, o.CreatedBy, o.CreatedAt, o.ModifiedBy, o.ModifiedAt) { }
}

public record CreateOverlayDocumentRequest(
    string Type,
    string Title,
    string ControlId,
    string Content,
    string? SourceReference,
    Guid? TenantId);

public record UpdateOverlayDocumentRequest(
    string? Title,
    string? Content,
    string? SourceReference,
    bool? IsActive);
