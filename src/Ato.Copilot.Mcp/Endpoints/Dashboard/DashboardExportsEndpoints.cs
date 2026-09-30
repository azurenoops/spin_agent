using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Document.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Services;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Ato.Copilot.Mcp.Authorization;

using KanbanTaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Mcp.Endpoints;

// ─── #648 Decomposition: Exports domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static async ValueTask<object?> GuardDocumentEvidenceAccessAsync(
        EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        try { return await next(invocation); }
        catch (UnauthorizedAccessException)
        {
            return Results.Json(new ErrorResponse { Error = "Current evidence sharing permission is required.", ErrorCode = "EVIDENCE_ACCESS_DENIED" }, statusCode: 403);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new ErrorResponse { Error = "Approved evidence summary is unavailable in this system.", ErrorCode = "EVIDENCE_UNAVAILABLE" });
        }
        catch (IOException)
        {
            return Results.Conflict(new ErrorResponse { Error = "Retained document evidence failed integrity or availability validation.", ErrorCode = "EVIDENCE_INTEGRITY_UNVERIFIED" });
        }
    }

    private static string? SnapshotIdempotencyKey(HttpContext http)
    {
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var values)) return null;
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > 100)
            throw new ArgumentException("Idempotency-Key must be a single value containing 1-100 characters.");
        return values[0];
    }

    private static void MapExportRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app, ICurrentUserService currentUser)
    {
        foreach (var documentType in new[] { "sap", "sar", "poam" })
        {
            group.MapGet($"/systems/{{systemId}}/documents/{documentType}/preview", async (
                    string systemId, AtoCopilotContext db, WorkingDocumentPreviewService previews,
                    HttpContext http, CancellationToken ct) =>
                {
                    http.Response.Headers.CacheControl = "no-store";
                    var system = await db.RegisteredSystems.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == systemId && x.IsActive, ct);
                    if (system == null)
                        return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "NOT_FOUND" });
                    return Results.Ok(await previews.PreviewAsync(system, documentType, ct));
                })
                .WithName($"Preview{documentType.ToUpperInvariant()}Document")
                .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        }

        group.MapPost("/systems/{systemId}/documents/ssp/preview", async (
                string systemId, AtoCopilotContext db, ISspExportService service, HttpContext http, CancellationToken ct) =>
            {
                if (!await db.RegisteredSystems.AnyAsync(s => s.Id == systemId && s.IsActive, ct))
                    return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "NOT_FOUND" });
                http.Response.Headers.CacheControl = "no-store";
                try
                {
                    return Results.Ok(await service.CreatePreviewAsync(systemId, currentUser.CurrentUserId, ct, SnapshotIdempotencyKey(http)));
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "SNAPSHOT_REQUEST_CONFLICT" });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
                }
                catch (IOException ex)
                {
                    http.RequestServices.GetRequiredService<ILogger<SspExportService>>()
                        .LogError(ex, "Retained SSP preview storage failed for system {SystemId}", systemId);
                    return Results.Json(new ErrorResponse
                    {
                        Error = "Retained preview storage is unavailable. Contact an administrator to check the configured export directory.",
                        ErrorCode = "DOCUMENT_STORAGE_UNAVAILABLE"
                    }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .WithName("RetainSspDocumentPreview")
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)
            .AddEndpointFilter(GuardDocumentEvidenceAccessAsync)
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        group.MapGet("/systems/{systemId}/documents/ssp/preview", async (
                string systemId,
                AtoCopilotContext db,
                IOscalSspExportService exportService,
                HttpContext http,
                CancellationToken ct) =>
            {
                if (!await db.RegisteredSystems.AnyAsync(system => system.Id == systemId && system.IsActive, ct))
                    return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "NOT_FOUND" });
                var result = await exportService.PreviewAsync(systemId, true, true, ct);
                var gaps = result.BuildPreviewSourceGaps();
                http.Response.Headers.CacheControl = "no-store";
                return Results.Ok(new DocumentPreviewDto(
                    systemId, "json", "application/json", result.OscalJson,
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.OscalJson))),
                    DateTimeOffset.UtcNow, gaps) { SourceManifest = result.SourceManifest });
            })
            .WithName("PreviewSspDocument")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        group.MapPost("/systems/{systemId}/exports", async (
                string systemId,
                CreateExportRequest body,
                ISspExportService exportService,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var format = body.Format?.ToLowerInvariant();
                if (format is not ("docx" or "pdf" or "json"))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Invalid format",
                        ErrorCode = "INVALID_FORMAT",
                        Details = $"Format '{body.Format}' not supported.",
                        Suggestion = "Use: docx, pdf, json",
                    });

                var userId = currentUser.CurrentUserId;

                try
                {
                    var requestKey = SnapshotIdempotencyKey(httpContext);
                    if (requestKey != null && !body.SourcePreviewId.HasValue)
                        return Results.BadRequest(new ErrorResponse { Error = "Idempotency-Key requires sourcePreviewId for this export route.", ErrorCode = "RETAINED_PREVIEW_REQUIRED" });
                    if (body.SourcePreviewId.HasValue && (format != "json" || body.TemplateId.HasValue))
                        return Results.BadRequest(new ErrorResponse { Error = "Retained preview export currently supports JSON without a template only.", ErrorCode = "UNSUPPORTED_SNAPSHOT_FORMAT" });
                    var export = body.SourcePreviewId.HasValue
                        ? await exportService.EnqueueFromPreviewAsync(systemId, body.SourcePreviewId.Value, userId, ct, requestKey)
                        : await exportService.EnqueueExportAsync(systemId, format, body.TemplateId, userId, ct);
                    return Results.Accepted($"/api/dashboard/systems/{systemId}/exports/{export.Id}", new ExportSummaryDto
                    {
                        ExportId = export.Id,
                        Format = export.Format,
                        Status = export.Status,
                        GeneratedBy = export.GeneratedBy,
                        GeneratedAt = export.GeneratedAt,
                    });
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "SNAPSHOT_REQUEST_CONFLICT" });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "VALIDATION_ERROR",
                    });
                }
            })
            .WithName("CreateSspExport")
            .AddEndpointFilter(GuardDocumentEvidenceAccessAsync)
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        // T014: GET /systems/{systemId}/exports — list exports
        group.MapGet("/systems/{systemId}/exports", async (
                string systemId,
                string? format,
                bool? includeFailed,
                int? limit,
                int? offset,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var exports = await exportService.ListExportsAsync(
                    systemId,
                    includeFailed ?? false,
                    Math.Clamp(limit ?? 25, 1, 100),
                    Math.Max(offset ?? 0, 0),
                    ct);
                return Results.Ok(new { items = exports, totalCount = exports.Count });
            })
            .WithName("ListSspExports")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        // T015: GET /systems/{systemId}/exports/{exportId} — get export detail
        group.MapGet("/systems/{systemId}/exports/{exportId:guid}", async (
                string systemId,
                Guid exportId,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var detail = await exportService.GetExportAsync(exportId, ct);
                if (detail is null || detail.SystemId != systemId)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Export not found",
                        ErrorCode = "NOT_FOUND",
                    });
                return Results.Ok(detail);
            })
            .WithName("GetSspExport")
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        // T011: GET /systems/{systemId}/exports/{exportId}/download — download file
        group.MapGet("/systems/{systemId}/exports/{exportId:guid}/download", async (
                string systemId,
                Guid exportId,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var detail = await exportService.GetExportAsync(exportId, ct);
                if (detail is null || detail.SystemId != systemId)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Export not found",
                        ErrorCode = "NOT_FOUND",
                    });

                var result = await exportService.GetExportFileStreamAsync(exportId, ct);
                if (result is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Export not found",
                        ErrorCode = "NOT_FOUND",
                    });

                var (stream, fileName, contentType) = result.Value;
                if (stream is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Export file not ready or missing",
                        ErrorCode = "EXPORT_NOT_READY",
                        Suggestion = "Wait for the export to complete before downloading.",
                    });

                return Results.File(stream, contentType ?? "application/octet-stream", fileName);
            })
            .WithName("DownloadSspExport")
            .AddEndpointFilter(GuardDocumentEvidenceAccessAsync)
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        // ─── SSP Template Management (Feature 037 US4) ───────────────────────

        // T022: POST /templates — upload custom DOCX template
        group.MapPost("/templates", async (
                HttpRequest request,
                ISspExportService exportService,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Request must be multipart/form-data",
                        ErrorCode = "INVALID_CONTENT_TYPE",
                    });

                var form = await request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");
                var name = form["name"].ToString();

                if (file is null || file.Length == 0)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "File is required",
                        ErrorCode = "MISSING_FILE",
                    });

                if (string.IsNullOrWhiteSpace(name))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Name is required",
                        ErrorCode = "MISSING_NAME",
                    });

                var description = form["description"].ToString();
                var userId = currentUser.CurrentUserId;

                try
                {
                    using var stream = file.OpenReadStream();
                    var result = await exportService.UploadTemplateAsync(
                        name,
                        string.IsNullOrWhiteSpace(description) ? null : description,
                        stream,
                        file.FileName,
                        userId,
                        ct);
                    return Results.Created($"/api/dashboard/templates/{result.Id}", result);
                }

                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "VALIDATION_ERROR",
                    });
                }
            })
            .WithName("UploadSspTemplate")
            .DisableAntiforgery();

        // T023: GET /templates — list templates with pagination
        group.MapGet("/templates", async (
                int? limit,
                int? offset,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var templates = await exportService.ListTemplatesAsync(
                    Math.Clamp(limit ?? 25, 1, 100),
                    Math.Max(offset ?? 0, 0),
                    ct);
                return Results.Ok(new { items = templates, totalCount = templates.Count });
            })
            .WithName("ListSspTemplates");

        // T024: DELETE /templates/{templateId} — soft-delete template
        group.MapDelete("/templates/{templateId:guid}", async (
                Guid templateId,
                HttpContext httpContext,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var userId = currentUser.CurrentUserId;
                var deleted = await exportService.DeleteTemplateAsync(templateId, userId, ct);
                return deleted ? Results.NoContent() : Results.NotFound(new ErrorResponse
                {
                    Error = "Template not found",
                    ErrorCode = "NOT_FOUND",
                });
            })
            .WithName("DeleteSspTemplate");

        // T024a: PUT /templates/{templateId} — rename/update template
        group.MapPut("/templates/{templateId:guid}", async (
                Guid templateId,
                UpdateTemplateRequest body,
                HttpContext httpContext,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                try
                {
                    var userId = currentUser.CurrentUserId;
                    var result = await exportService.UpdateTemplateAsync(
                        templateId, body.Name, body.Description, userId, ct);
                    return result is not null
                        ? Results.Ok(result)
                        : Results.NotFound(new ErrorResponse
                        {
                            Error = "Template not found",
                            ErrorCode = "NOT_FOUND",
                        });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "VALIDATION_ERROR",
                    });
                }
            })
            .WithName("UpdateSspTemplate");

        // ═══════════════════════════════════════════════════════════════════════
        // Feature 038 — Evidence Repository
        // ═══════════════════════════════════════════════════════════════════════

        // ─── Evidence Upload (US1 T014) ──────────────────────────────────────

        // T014: POST /systems/{systemId}/evidence — upload evidence artifact
    }
}
