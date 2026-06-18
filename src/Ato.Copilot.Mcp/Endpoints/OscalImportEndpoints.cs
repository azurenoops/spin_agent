using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// OSCAL 1.1.2 SSP import endpoints (Feature 076 — T011).
/// POST /api/systems/{systemId}/oscal/import/ssp?mode=preview|full
/// GET  /api/systems/{systemId}/oscal/import/runs
/// </summary>
public static class OscalImportEndpoints
{
    private const long MaxImportBytes = 26_214_400; // 25 MB

    public static IEndpointRouteBuilder MapOscalImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/systems/{systemId}/oscal")
            .WithTags("OSCAL")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName)
            .DisableAntiforgery();

        // POST /import/ssp?mode=preview|full
        group.MapPost("/import/ssp", async (
                string systemId,
                string mode,
                HttpContext http,
                IOscalSspImportService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();

                if (!http.Request.HasFormContentType)
                    return Envelope.Failure("OSCAL_UPLOAD_INVALID",
                        "Upload must be multipart/form-data.",
                        "POST with Content-Type: multipart/form-data and a 'file' part containing the OSCAL JSON.");

                var form = await http.Request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");

                if (file is null || file.Length == 0)
                    return Envelope.Failure("OSCAL_UPLOAD_MISSING", "A 'file' part is required.");

                if (file.Length > MaxImportBytes)
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = "OSCAL_FILE_TOO_LARGE",
                        message = $"File exceeds the 25 MB import limit ({file.Length:N0} bytes).",
                    }, statusCode: StatusCodes.Status413PayloadTooLarge);

                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (ext != ".json")
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = "OSCAL_WRONG_FORMAT",
                        message = $"File '{file.FileName}' is not a JSON file. OSCAL XML is not supported — export as JSON.",
                    }, statusCode: StatusCodes.Status422UnprocessableEntity);

                using var reader = new StreamReader(file.OpenReadStream());
                var oscalJson = await reader.ReadToEndAsync(ct);

                var isPreview = string.Equals(mode, "preview", StringComparison.OrdinalIgnoreCase);

                if (isPreview)
                {
                    var preview = await service.PreviewAsync(tenantId.ToString(), systemId, oscalJson, ct);

                    if (!preview.SchemaValid && preview.ValidationErrors.Count > 0)
                        return Results.Json(new
                        {
                            ok = false,
                            errorCode = "OSCAL_SCHEMA_INVALID",
                            message = "The uploaded file does not conform to the OSCAL 1.1.2 SSP JSON Schema.",
                            errors = preview.ValidationErrors,
                            warnings = preview.ValidationWarnings,
                        }, statusCode: StatusCodes.Status422UnprocessableEntity);

                    return Results.Ok(new { ok = true, data = preview });
                }
                else
                {
                    var result = await service.ImportAsync(
                        tenantId.ToString(), systemId, oscalJson, actorId.ToString(), ct);

                    return Results.Ok(new { ok = true, data = result });
                }
            })
            .WithName("ImportOscalSsp")
            .Accepts<IFormFile>("multipart/form-data");

        // GET /import/runs  (stub)
        group.MapGet("/import/runs", (string systemId) =>
                Results.Ok(new { runs = Array.Empty<object>(), total = 0 }))
            .WithName("ListOscalImportRuns");

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
        "You do not have permission to import OSCAL documents.",
        suggestion: "Sign in with an account that holds the Administrator role for your tenant.");
}
