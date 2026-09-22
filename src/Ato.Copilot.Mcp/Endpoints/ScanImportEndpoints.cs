// ═══════════════════════════════════════════════════════════════════════════
// Feature 204 (UF-005) — T-063-13..20: Scan Import REST Endpoints
// POST  /api/dashboard/systems/{systemId}/scans/import
// GET   /api/dashboard/systems/{systemId}/scans/import/{importId}/status
// DELETE /api/dashboard/systems/{systemId}/scans/import/{importId}
// ═══════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ato.Copilot.Mcp.Services;
using Ato.Copilot.Mcp.Services.Tenancy;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Core.Interfaces.Tenancy;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// Scan import REST endpoints for the SCAP/STIG streaming import UI (UF-005 / spec-063 Phase 3).
///
/// File type detection reads the first few hundred bytes to identify the root XML element:
///   CHECKLIST              → CKL (STIG Viewer checklist)
///   Benchmark / TestResult → XCCDF (SCAP results)
///   NessusClientData_v2    → Nessus results
/// </summary>
public static class ScanImportEndpoints
{
    public static IEndpointRouteBuilder MapScanImportEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── POST /api/dashboard/systems/{systemId}/scans/import ─────────────
        // Accepts a multipart form-data file upload; enqueues the job off the
        // HTTP thread and returns 202 Accepted with the job ID for polling.
        app.MapPost("/api/dashboard/systems/{systemId}/scans/import", async (
            string systemId,
            HttpRequest request,
            ScanImportQueue queue,
            ScanImportStatusTracker tracker,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count == 0)
                return Results.BadRequest(new { error = "No file uploaded", errorCode = "NO_FILE" });

            var file = request.Form.Files[0];

            // Size guard — 256MB (T-063-15)
            const long MaxBytes = 256L * 1024 * 1024;
            if (file.Length > MaxBytes)
                return Results.BadRequest(new
                {
                    error = $"File too large ({file.Length / 1024 / 1024}MB). Maximum is 256MB.",
                    errorCode = "FILE_TOO_LARGE",
                });

            var temporaryFilePath = Path.Combine(
                Path.GetTempPath(),
                $"ato-copilot-scan-{Guid.NewGuid():N}.upload");

            try
            {
                await using (var temporaryFile = new FileStream(
                                 temporaryFilePath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 81920,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await file.CopyToAsync(temporaryFile, cancellationToken);
                    await temporaryFile.FlushAsync(cancellationToken);
                }

                // Detect file type from the bounded header after the upload reaches disk.
                var header = new byte[Math.Min(512, checked((int)file.Length))];
                await using (var storedFile = new FileStream(
                                 temporaryFilePath,
                                 FileMode.Open,
                                 FileAccess.Read,
                                 FileShare.Read,
                                 bufferSize: 512,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    _ = await storedFile.ReadAsync(header, cancellationToken);
                }

                var detectedType = DetectFileType(header, file.FileName);
                if (detectedType is null)
                {
                    File.Delete(temporaryFilePath);
                    return Results.StatusCode(415); // Unsupported Media Type
                }

                // Create a job ID and register with the in-memory tracker
                var importJobId = Guid.NewGuid().ToString();
                tracker.Register(importJobId, systemId);

                // Enqueue — the BackgroundWorker drains the channel
                var enqueued = queue.TryEnqueue(new ScanImportJob
                {
                    JobId = importJobId,
                    SystemId = systemId,
                    FileName = file.FileName,
                    TemporaryFilePath = temporaryFilePath,
                    ImportType = detectedType,
                    ImportedBy = WorkspaceService.Identity(request.HttpContext.User).ObjectId.ToString(),
                });

                if (!enqueued)
                {
                    tracker.Remove(importJobId);
                    File.Delete(temporaryFilePath);
                    return Results.StatusCode(503); // Queue full — try again later
                }

                return Results.Accepted(
                    $"/api/dashboard/systems/{systemId}/scans/import/{importJobId}/status",
                    new
                    {
                        importJobId,
                        statusUrl = $"/api/dashboard/systems/{systemId}/scans/import/{importJobId}/status",
                        detectedFileType = detectedType,
                        fileName = file.FileName,
                        fileSizeBytes = file.Length,
                    });
            }
            catch
            {
                File.Delete(temporaryFilePath);
                throw;
            }
        })
        .WithName("UploadScanImport")
        .WithTags("Scan Import")
        .DisableAntiforgery()
        .RequireAuthorization()
        .RequireProgressAccess(mutate: true);

        // ─── GET .../scans/import/{importId}/status ───────────────────────────
        // Returns the current status of an import job for polling fallback.
        app.MapGet("/api/dashboard/systems/{systemId}/scans/import/{importId}/status", (
            string systemId,
            string importId,
            ScanImportStatusTracker tracker) =>
        {
            var state = tracker.TryGet(systemId, importId);
            if (state is null)
                return Results.NotFound(new { error = "Import job not found", errorCode = "NOT_FOUND" });

            return Results.Ok(new
            {
                id = state.JobId,
                status = state.Status.ToString(),
                processedCount = state.ProcessedCount,
                totalCount = state.TotalCount,
                errorMessage = state.ErrorMessage,
                cancelRequested = state.CancelRequested,
            });
        })
        .WithName("GetScanImportStatus")
        .WithTags("Scan Import")
        .RequireAuthorization()
        .RequireProgressAccess(mutate: false);

        // ─── DELETE .../scans/import/{importId} ───────────────────────────────
        // Requests cancellation of an in-progress import job.
        app.MapDelete("/api/dashboard/systems/{systemId}/scans/import/{importId}", (
            string systemId,
            string importId,
            ScanImportStatusTracker tracker) =>
        {
            var cancelled = tracker.RequestCancel(systemId, importId);
            if (!cancelled)
                return Results.NotFound(new { error = "Import job not found", errorCode = "NOT_FOUND" });

            return Results.Ok(new { id = importId, cancelRequested = true });
        })
        .WithName("CancelScanImport")
        .WithTags("Scan Import")
        .RequireAuthorization()
        .RequireProgressAccess(mutate: true);

        return app;
    }

    private static RouteHandlerBuilder RequireProgressAccess(this RouteHandlerBuilder route, bool mutate) =>
        route.WithMetadata(new WorkspaceAuthorizedEndpoint()).AddEndpointFilter(async (invocation, next) =>
        {
            var http = invocation.HttpContext;
            var services = http.RequestServices;
            try
            {
                var actor = await WorkspaceNotificationActor.ResolveAsync(http,
                    services.GetRequiredService<IWorkspaceService>(), http.RequestAborted);
                using var tenantScope = services.GetRequiredService<ITenantContextAccessor>().Push(actor.Context);
                var systemId = http.Request.RouteValues["systemId"]?.ToString();
                if (string.IsNullOrWhiteSpace(systemId))
                    throw new WorkspaceException(404, "SYSTEM_NOT_FOUND", "The system is not accessible in this workspace.");
                var access = await services.GetRequiredService<ISystemWorkspaceAccessService>()
                    .GetAccessAsync(actor.Context.EffectiveTenantId, actor.Workspace.PersonId, systemId,
                        actor.Context.IsCspAdmin, http.RequestAborted);
                if (!access.Permissions.CanRead)
                    throw new WorkspaceException(404, "SYSTEM_NOT_FOUND", "The system is not accessible in this workspace.");
                if (mutate && !access.Permissions.CanRunAssessments)
                    throw new WorkspaceException(403, "WORKSPACE_OPERATION_NOT_AUTHORIZED", "Your system assignments do not authorize scan imports.");
                return await next(invocation);
            }
            catch (WorkspaceException ex)
            {
                services.GetRequiredService<ILoggerFactory>().CreateLogger("ScanImportEndpoints")
                    .LogInformation("Scan import request denied: {ErrorCode}", ex.Code);
                return Results.Json(new { status = "error", error = new { errorCode = ex.Code, message = ex.Message } },
                    statusCode: ex.StatusCode);
            }
        });

    // ─── File type detection ──────────────────────────────────────────────────

    private static string? DetectFileType(byte[] content, string fileName)
    {
        // Try extension first (fast path)
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is ".ckl") return "CKL";
        if (ext is ".xml" or ".xccdf") return DetectXmlType(content);
        if (ext is ".nessus") return "Nessus";

        // Fall back to root element sniffing for ambiguous extensions
        if (ext is ".xml" or "") return DetectXmlType(content);

        return null;
    }

    private static string? DetectXmlType(byte[] content)
    {
        try
        {
            // Read the first 512 bytes as a string — enough to find the root element
            var header = System.Text.Encoding.UTF8.GetString(content, 0, Math.Min(512, content.Length));
            if (header.Contains("CHECKLIST")) return "CKL";
            if (header.Contains("Benchmark") || header.Contains("TestResult")) return "XCCDF";
            if (header.Contains("NessusClientData_v2")) return "Nessus";
        }
        catch
        {
            // Binary or non-UTF8 content
        }

        return null;
    }
}
