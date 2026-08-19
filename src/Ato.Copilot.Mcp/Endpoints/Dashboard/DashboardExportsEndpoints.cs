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

using KanbanTaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Mcp.Endpoints;

// ─── #648 Decomposition: Exports domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapExportRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
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

                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";

                try
                {
                    var export = await exportService.EnqueueExportAsync(systemId, format, body.TemplateId, userId, ct);
                    return Results.Accepted($"/api/dashboard/systems/{systemId}/exports/{export.Id}", new ExportSummaryDto
                    {
                        ExportId = export.Id,
                        Format = export.Format,
                        Status = export.Status,
                        GeneratedBy = export.GeneratedBy,
                        GeneratedAt = export.GeneratedAt,
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
            .WithName("CreateSspExport");

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
            .WithName("ListSspExports");

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
            .WithName("GetSspExport");

        // T011: GET /systems/{systemId}/exports/{exportId}/download — download file
        group.MapGet("/systems/{systemId}/exports/{exportId:guid}/download", async (
                string systemId,
                Guid exportId,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
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
            .WithName("DownloadSspExport");

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
                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";

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
                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";
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
                    var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";
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
