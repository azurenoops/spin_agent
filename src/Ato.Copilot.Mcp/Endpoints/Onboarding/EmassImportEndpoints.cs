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
/// Onboarding wizard Step 3 — eMASS bulk-import endpoints (FR-030..FR-038).
/// Implements <c>contracts/imports-api.yaml</c>.
/// </summary>
public static class EmassImportEndpoints
{
    public static IEndpointRouteBuilder MapEmassImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding/imports/emass")
            .WithTags("Onboarding")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName)
            .DisableAntiforgery();

        // POST /api/onboarding/imports/emass/upload (multipart) → 202 + jobId
        group.MapPost("/upload", async (
                HttpContext http,
                IEmassImportService service,
                IOptions<OnboardingOptions> options,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();

                if (!http.Request.HasFormContentType)
                {
                    return Envelope.Failure(
                        WizardErrorCodes.EmassInvalidFormat,
                        "Upload must be multipart/form-data.",
                        "Use the wizard's upload widget or POST a multipart/form-data body with a 'file' part.");
                }

                var form = await http.Request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
                if (file is null || file.Length == 0)
                {
                    return Envelope.Failure(
                        WizardErrorCodes.JobFailed,
                        "A file is required.");
                }

                var max = options.Value.Limits.MaxEmassImportBytes;
                if (file.Length > max)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = WizardErrorCodes.EmassTooLarge,
                        message = $"Upload exceeds the eMASS size limit ({max:N0} bytes).",
                        suggestion = "Split the export into smaller batches or contact your administrator to raise Onboarding:Limits:MaxEmassImportBytes.",
                    }, statusCode: StatusCodes.Status413PayloadTooLarge);
                }

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (ext is not ".xlsx" and not ".zip")
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = WizardErrorCodes.EmassInvalidFormat,
                        message = $"Unsupported eMASS upload format: '{ext}'.",
                        suggestion = "Upload an eMASS XLSX export or a Package ZIP.",
                    }, statusCode: StatusCodes.Status415UnsupportedMediaType);
                }

                await using var stream = file.OpenReadStream();
                var (session, parseJobId) = await service.StartParseAsync(
                    tenantId,
                    file.FileName,
                    file.ContentType ?? "application/octet-stream",
                    stream,
                    actorId,
                    Guid.NewGuid(),
                    ct);

                return Results.Json(new
                {
                    ok = true,
                    data = new
                    {
                        sessionId = session.Id,
                        parseJobId,
                        status = session.Status.ToString(),
                    },
                }, statusCode: StatusCodes.Status202Accepted);
            })
            .WithName("UploadEmassImport")
            .Accepts<IFormFile>("multipart/form-data");

        // GET /api/onboarding/imports/emass/{sessionId}/preview → 200 preview JSON
        group.MapGet("/{sessionId:guid}/preview", async (
                Guid sessionId,
                HttpContext http,
                IEmassImportService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                var preview = await service.GetPreviewAsync(tenantId, sessionId, ct);
                if (preview is null)
                {
                    return Envelope.Failure(
                        WizardErrorCodes.JobFailed,
                        "Preview not yet available — the parse job may still be running.",
                        "Subscribe to the SignalR hub or poll /api/onboarding/jobs/{parseJobId} for status.");
                }
                return Results.Ok(new { ok = true, data = preview });
            })
            .WithName("GetEmassImportPreview");

        // POST /api/onboarding/imports/emass/{sessionId}/commit → 202 + commit jobId
        group.MapPost("/{sessionId:guid}/commit", async (
                Guid sessionId,
                CommitEmassRequest request,
                HttpContext http,
                IEmassImportService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                if (request?.Instructions is null)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, "Instructions are required.");
                }

                var parsed = new List<EmassCommitInstruction>(request.Instructions.Count);
                foreach (var ins in request.Instructions)
                {
                    if (ins is null || string.IsNullOrWhiteSpace(ins.SystemIdentifier))
                    {
                        return Envelope.Failure(WizardErrorCodes.JobFailed, "Each instruction requires a systemIdentifier.");
                    }
                    if (!Enum.TryParse<EmassCommitDecision>(ins.Decision, ignoreCase: true, out var decision))
                    {
                        return Envelope.Failure(
                            WizardErrorCodes.JobFailed,
                            $"Unknown decision '{ins.Decision}'. Expected Skip, Merge, or Overwrite.");
                    }
                    parsed.Add(new EmassCommitInstruction(ins.SystemIdentifier!, decision));
                }

                try
                {
                    var commitJobId = await service.CommitAsync(
                        tenantId, sessionId, parsed, actorId, Guid.NewGuid(), ct);
                    return Results.Json(new
                    {
                        ok = true,
                        data = new { sessionId, commitJobId },
                    }, statusCode: StatusCodes.Status202Accepted);
                }
                catch (InvalidOperationException ex)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, ex.Message);
                }
            })
            .WithName("CommitEmassImport");

        // GET /api/onboarding/imports/emass/{sessionId}/log → 200 log JSON
        group.MapGet("/{sessionId:guid}/log", async (
                Guid sessionId,
                HttpContext http,
                IEmassImportService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                var log = await service.GetLogAsync(tenantId, sessionId, ct);
                if (log is null)
                {
                    return Envelope.Failure(
                        WizardErrorCodes.JobFailed,
                        "Log not yet available — the commit job may still be running.",
                        "Subscribe to the SignalR hub or poll /api/onboarding/jobs/{commitJobId} for status.");
                }
                return Results.Ok(new { ok = true, data = log });
            })
            .WithName("GetEmassImportLog");

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

/// <summary>HTTP request body for <c>POST /api/onboarding/imports/emass/{sessionId}/commit</c>.</summary>
public sealed class CommitEmassRequest
{
    public List<EmassCommitInstructionDto>? Instructions { get; set; }
}

/// <summary>Per-system commit instruction wire format.</summary>
public sealed class EmassCommitInstructionDto
{
    public string? SystemIdentifier { get; set; }
    public string? Decision { get; set; }
}
