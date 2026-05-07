using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints.Onboarding;

/// <summary>
/// Step 7 — Narrative Seed Documents endpoints. Implements
/// <c>contracts/templates-api.yaml</c> (FR-051..FR-054). Files are byte-stored
/// via <see cref="Core.Interfaces.Storage.IFileStorageProvider"/> and tracked
/// by <see cref="INarrativeSeedDocumentService"/>.
/// </summary>
public static class NarrativeSeedEndpoints
{
    public static IEndpointRouteBuilder MapNarrativeSeedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding/narrative-seeds")
            .WithTags("Onboarding")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName)
            .DisableAntiforgery();

        group.MapGet("/", async (
                HttpContext http,
                INarrativeSeedDocumentService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                var rows = await service.ListAsync(tenantId, includeDeleted: false, ct);
                return Results.Ok(new { ok = true, data = rows });
            });

        group.MapPost("/", async (
                HttpContext http,
                INarrativeSeedDocumentService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                if (!http.Request.HasFormContentType)
                    return Envelope.Failure(WizardErrorCodes.SspPdfUnreadable,
                        "Upload must be multipart/form-data.");

                var form = await http.Request.ReadFormAsync(ct);
                var file = form.Files.FirstOrDefault(f => f.Length > 0);
                if (file is null)
                    return Envelope.Failure(WizardErrorCodes.SspPdfUnreadable, "A file is required.");
                var label = form["label"].ToString();
                if (string.IsNullOrWhiteSpace(label))
                    return Envelope.Failure(WizardErrorCodes.SspPdfUnreadable, "Label is required.");
                var tags = form["tags"].ToArray()
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t!.Trim())
                    .ToList();

                try
                {
                    await using var s = file.OpenReadStream();
                    var result = await service.UploadAsync(
                        tenantId, actorId, label, tags,
                        file.FileName, file.ContentType ?? "application/octet-stream",
                        s, file.Length, ct);
                    return Results.Accepted(
                        $"/api/onboarding/narrative-seeds/{result.Document.Id:D}",
                        new { ok = true, data = result });
                }
                catch (InvalidOperationException ex)
                    when (ex.Message == WizardErrorCodes.SspPdfUnreadable)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = WizardErrorCodes.SspPdfUnreadable,
                        message = $"File '{file.FileName}' exceeds the narrative-seed size limit.",
                    }, statusCode: StatusCodes.Status413PayloadTooLarge);
                }
            });

        group.MapDelete("/{id:guid}", async (
                HttpContext http,
                Guid id,
                bool? confirmCitations,
                INarrativeSeedDocumentService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                try
                {
                    await service.DeleteAsync(tenantId, id, actorId, confirmCitations ?? false, ct);
                    return Results.NoContent();
                }
                catch (InvalidOperationException ex)
                    when (ex.Message == "WIZARD_NARRATIVE_SEED_HAS_CITATIONS")
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = "WIZARD_NARRATIVE_SEED_HAS_CITATIONS",
                        message = "This document is referenced by existing narrative citations.",
                        suggestion = "Pass ?confirmCitations=true to acknowledge the cascade and proceed.",
                    }, statusCode: StatusCodes.Status409Conflict);
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { ok = false, errorCode = "NotFound" });
                }
            });

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
