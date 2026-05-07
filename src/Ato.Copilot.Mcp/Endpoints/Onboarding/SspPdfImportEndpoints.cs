using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints.Onboarding;

/// <summary>
/// Onboarding wizard Step 4 — SSP PDF batch ingest endpoints (FR-040..FR-046).
/// Implements <c>contracts/imports-api.yaml</c>.
/// </summary>
public static class SspPdfImportEndpoints
{
    public static IEndpointRouteBuilder MapSspPdfImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding/imports/ssp-pdf")
            .WithTags("Onboarding")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName)
            .DisableAntiforgery();

        // POST /upload (multipart, 1..N "files" parts) → 202 + per-PDF jobIds
        group.MapPost("/upload", async (
                HttpContext http,
                ISspPdfImportService service,
                IOptions<OnboardingOptions> options,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();

                if (!http.Request.HasFormContentType)
                {
                    return Envelope.Failure(
                        WizardErrorCodes.SspPdfUnreadable,
                        "Upload must be multipart/form-data.",
                        "Use the wizard's batch upload widget or POST a multipart/form-data body with one or more 'files' parts.");
                }

                var form = await http.Request.ReadFormAsync(ct);
                var files = form.Files
                    .Where(f => f.Length > 0)
                    .ToList();
                if (files.Count == 0)
                {
                    return Envelope.Failure(WizardErrorCodes.SspPdfUnreadable, "At least one file is required.");
                }

                var batchMax = options.Value.Limits.MaxSspPdfBatchSize;
                if (files.Count > batchMax)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = WizardErrorCodes.SspPdfUnreadable,
                        message = $"Batch size {files.Count} exceeds limit ({batchMax}).",
                        suggestion = "Split the upload into multiple smaller batches.",
                    }, statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                var perFileMax = options.Value.Limits.MaxSspPdfImportBytes;
                foreach (var f in files)
                {
                    if (f.Length > perFileMax)
                    {
                        return Results.Json(new
                        {
                            ok = false,
                            errorCode = WizardErrorCodes.SspPdfUnreadable,
                            message = $"File '{f.FileName}' exceeds the SSP-PDF size limit ({perFileMax:N0} bytes).",
                            suggestion = "Re-export a smaller PDF or contact your administrator to raise Onboarding:Limits:MaxSspPdfImportBytes.",
                        }, statusCode: StatusCodes.Status413PayloadTooLarge);
                    }
                    var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
                    if (ext != ".pdf")
                    {
                        return Results.Json(new
                        {
                            ok = false,
                            errorCode = WizardErrorCodes.SspPdfUnreadable,
                            message = $"Unsupported file format '{ext}' for '{f.FileName}'.",
                            suggestion = "Upload digital SSP PDFs only.",
                        }, statusCode: StatusCodes.Status415UnsupportedMediaType);
                    }
                }

                var streams = new List<(string, string, Stream)>(files.Count);
                var openStreams = new List<Stream>(files.Count);
                try
                {
                    foreach (var f in files)
                    {
                        var s = f.OpenReadStream();
                        openStreams.Add(s);
                        streams.Add((f.FileName, f.ContentType ?? "application/pdf", s));
                    }
                    var result = await service.StartBatchAsync(
                        tenantId, streams, actorId, Guid.NewGuid(), ct);

                    return Results.Json(new
                    {
                        ok = true,
                        data = result,
                    }, statusCode: StatusCodes.Status202Accepted);
                }
                finally
                {
                    foreach (var s in openStreams) s.Dispose();
                }
            })
            .WithName("UploadSspPdfBatch")
            .Accepts<IFormFile>("multipart/form-data");

        // GET /batches/{batchId}/summary → 200
        group.MapGet("/batches/{batchId:guid}/summary", async (
                Guid batchId,
                HttpContext http,
                ISspPdfImportService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                var summary = await service.GetBatchSummaryAsync(tenantId, batchId, ct);
                return Results.Ok(new { ok = true, data = summary });
            })
            .WithName("GetSspPdfBatchSummary");

        // GET /{sessionId}/extraction → 200 result JSON or 404
        group.MapGet("/{sessionId:guid}/extraction", async (
                Guid sessionId,
                HttpContext http,
                ISspPdfImportService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                var extraction = await service.GetExtractionAsync(tenantId, sessionId, ct);
                if (extraction is null)
                {
                    return Envelope.Failure(
                        WizardErrorCodes.JobFailed,
                        "Extraction not yet available.",
                        "Subscribe to the SignalR hub or poll /api/onboarding/jobs/{extractJobId} for status.");
                }
                return Results.Ok(new { ok = true, data = extraction });
            })
            .WithName("GetSspPdfExtraction");

        // PUT /{sessionId}/corrections → 200
        group.MapPut("/{sessionId:guid}/corrections", async (
                Guid sessionId,
                CorrectionsRequest request,
                HttpContext http,
                ISspPdfImportService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                if (request?.Corrections is null)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, "Corrections are required.");
                }
                var parsed = request.Corrections
                    .Where(c => c is not null && !string.IsNullOrWhiteSpace(c!.FieldName))
                    .Select(c => new SspPdfFieldCorrection(c!.FieldName!, c.Value))
                    .ToList();
                await service.UpdateCorrectionsAsync(tenantId, sessionId, parsed, actorId, Guid.NewGuid(), ct);
                return Results.Ok(new { ok = true, data = new { sessionId } });
            })
            .WithName("UpdateSspPdfCorrections");

        // POST /{sessionId}/import → 201 + system id
        group.MapPost("/{sessionId:guid}/import", async (
                Guid sessionId,
                HttpContext http,
                ISspPdfImportService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                try
                {
                    var systemId = await service.CommitToSystemAsync(
                        tenantId, sessionId, actorId, Guid.NewGuid(), ct);
                    return Results.Json(new
                    {
                        ok = true,
                        data = new { sessionId, systemId },
                    }, statusCode: StatusCodes.Status201Created);
                }
                catch (InvalidOperationException ex)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, ex.Message);
                }
            })
            .WithName("ImportSspPdfSystem");

        return app;
    }

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var raw = user.FindFirstValue("tid")
            ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
        return Guid.TryParse(raw, out tenantId);
    }

    private static bool TryGetSubject(ClaimsPrincipal user, out Guid subjectId)
    {
        var raw = user.FindFirstValue("oid") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out subjectId);
    }

    private static IResult Forbidden() => Envelope.Failure(
        WizardErrorCodes.AuthForbidden,
        "You do not have permission to use the onboarding wizard.",
        suggestion: "Sign in with an account that holds the Administrator role for your tenant.");
}

/// <summary>Wire format for PUT corrections.</summary>
public sealed class CorrectionsRequest
{
    public List<CorrectionDto>? Corrections { get; set; }
}

/// <summary>Per-field correction wire format.</summary>
public sealed class CorrectionDto
{
    public string? FieldName { get; set; }
    public string? Value { get; set; }
}
