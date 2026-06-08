using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

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

        group.MapGet("/", async (
                string? controlId,
                string? type,
                AtoCopilotContext db,
                CancellationToken ct) =>
        {
            var query = db.OverlayDocuments.Where(o => o.IsActive);
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

        group.MapGet("/{id:guid}", async (
                Guid id,
                AtoCopilotContext db,
                CancellationToken ct) =>
        {
            var doc = await db.OverlayDocuments.FindAsync([id], ct);
            return doc is null ? Results.NotFound() : Results.Ok(new OverlayDocumentDto(doc));
        }).WithName("GetOverlayDocument");

        group.MapPost("/", async (
                CreateOverlayDocumentRequest req,
                AtoCopilotContext db,
                CancellationToken ct) =>
        {
            var doc = new OverlayDocument
            {
                Type = req.Type,
                Title = req.Title,
                ControlId = req.ControlId.ToUpperInvariant(),
                Content = req.Content,
                SourceReference = req.SourceReference,
                TenantId = req.TenantId,
                IsActive = true,
                CreatedBy = "admin"
            };
            db.OverlayDocuments.Add(doc);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/overlay-documents/{doc.Id}", new OverlayDocumentDto(doc));
        }).WithName("CreateOverlayDocument");

        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateOverlayDocumentRequest req,
                AtoCopilotContext db,
                CancellationToken ct) =>
        {
            var doc = await db.OverlayDocuments.FindAsync([id], ct);
            if (doc is null) return Results.NotFound();

            doc.Title = req.Title ?? doc.Title;
            doc.Content = req.Content ?? doc.Content;
            doc.SourceReference = req.SourceReference ?? doc.SourceReference;
            doc.IsActive = req.IsActive ?? doc.IsActive;
            doc.ModifiedBy = "admin";
            doc.ModifiedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(new OverlayDocumentDto(doc));
        }).WithName("UpdateOverlayDocument");

        group.MapDelete("/{id:guid}", async (
                Guid id,
                AtoCopilotContext db,
                CancellationToken ct) =>
        {
            var doc = await db.OverlayDocuments.FindAsync([id], ct);
            if (doc is null) return Results.NotFound();
            doc.IsActive = false;
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
