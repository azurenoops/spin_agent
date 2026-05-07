using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints.Onboarding;

/// <summary>
/// Step 6 — Custom Document Templates endpoints. Implements
/// <c>contracts/templates-api.yaml</c> per FR-080..FR-096. Authorization is
/// enforced via <see cref="OnboardingAdministratorRequirement.PolicyName"/>.
/// </summary>
public static class OrganizationTemplateEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding/templates")
            .WithTags("Onboarding")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName)
            .DisableAntiforgery();

        group.MapGet("/", async (
                HttpContext http,
                IOrganizationTemplateService service,
                string? templateType,
                bool? includeDeleted,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                TemplateType? typed = null;
                if (!string.IsNullOrWhiteSpace(templateType)
                    && Enum.TryParse<TemplateType>(templateType, true, out var t))
                {
                    typed = t;
                }
                var rows = await service.ListAsync(tenantId, typed, includeDeleted ?? false, ct);
                return Results.Ok(new { ok = true, data = rows });
            });

        group.MapPost("/upload", async (
                HttpContext http,
                IOrganizationTemplateService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                if (!http.Request.HasFormContentType)
                    return Envelope.Failure(WizardErrorCodes.TemplateWrongFormat,
                        "Upload must be multipart/form-data.");

                var form = await http.Request.ReadFormAsync(ct);
                var file = form.Files.FirstOrDefault(f => f.Length > 0);
                if (file is null)
                    return Envelope.Failure(WizardErrorCodes.TemplateWrongFormat, "A file is required.");
                var typeRaw = form["templateType"].ToString();
                if (!Enum.TryParse<TemplateType>(typeRaw, true, out var templateType))
                    return Envelope.Failure(WizardErrorCodes.TemplateWrongFormat,
                        $"Unknown templateType '{typeRaw}'.");
                var label = form["label"].ToString();
                var version = form["version"].ToString();
                var isDefault = bool.TryParse(form["isDefault"].ToString(), out var b) && b;

                try
                {
                    await using var s = file.OpenReadStream();
                    var result = await service.UploadAsync(
                        tenantId, actorId, templateType, label, version,
                        file.FileName, s, file.Length, isDefault, ct);
                    return Results.Created(
                        $"/api/onboarding/templates/{result.Template.Id:D}",
                        new { ok = true, data = new { result.Template, result.Warnings } });
                }
                catch (InvalidOperationException ex)
                    when (ex.Message == WizardErrorCodes.TemplateTooLarge)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = WizardErrorCodes.TemplateTooLarge,
                        message = $"File '{file.FileName}' exceeds the org-template size limit.",
                    }, statusCode: StatusCodes.Status413PayloadTooLarge);
                }
                catch (InvalidOperationException ex)
                    when (ex.Message == WizardErrorCodes.TemplateWrongFormat)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = WizardErrorCodes.TemplateWrongFormat,
                        message = $"File '{file.FileName}' is the wrong format for {templateType}.",
                        suggestion = "Use a DOCX file for SSP/SAR/SAP slots and an XLSX file for CRM/HwSwInventory slots.",
                    }, statusCode: StatusCodes.Status415UnsupportedMediaType);
                }
            });

        group.MapGet("/{id:guid}", async (
                HttpContext http,
                Guid id,
                IOrganizationTemplateService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                var t = await service.GetAsync(tenantId, id, ct);
                return t is null
                    ? Results.NotFound(new { ok = false, errorCode = "NotFound" })
                    : Results.Ok(new { ok = true, data = t });
            });

        group.MapPatch("/{id:guid}", async (
                HttpContext http,
                Guid id,
                PatchTemplateRequest body,
                IOrganizationTemplateService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                try
                {
                    var t = await service.PatchMetadataAsync(
                        tenantId, id, actorId, body.Label, body.Version, ct);
                    return Results.Ok(new { ok = true, data = t });
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { ok = false, errorCode = "NotFound" });
                }
            });

        group.MapDelete("/{id:guid}", async (
                HttpContext http,
                Guid id,
                IOrganizationTemplateService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                try
                {
                    await service.DeleteAsync(tenantId, id, actorId, ct);
                    return Results.NoContent();
                }
                catch (InvalidOperationException ex)
                    when (ex.Message == WizardErrorCodes.TemplateDefaultProtected)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = WizardErrorCodes.TemplateDefaultProtected,
                        message = "Cannot delete a template currently marked default. Demote it first.",
                    }, statusCode: StatusCodes.Status409Conflict);
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { ok = false, errorCode = "NotFound" });
                }
            });

        group.MapGet("/{id:guid}/download", async (
                HttpContext http,
                Guid id,
                IOrganizationTemplateService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                var t = await service.GetAsync(tenantId, id, ct);
                if (t is null) return Results.NotFound();
                var s = await service.DownloadAsync(tenantId, id, ct);
                if (s is null) return Results.NotFound();
                return Results.File(
                    s,
                    contentType: "application/octet-stream",
                    fileDownloadName: t.OriginalFileName);
            });

        group.MapPost("/{id:guid}/replace", async (
                HttpContext http,
                Guid id,
                IOrganizationTemplateService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                if (!http.Request.HasFormContentType)
                    return Envelope.Failure(WizardErrorCodes.TemplateWrongFormat,
                        "Replace must be multipart/form-data.");
                var form = await http.Request.ReadFormAsync(ct);
                var file = form.Files.FirstOrDefault(f => f.Length > 0);
                if (file is null)
                    return Envelope.Failure(WizardErrorCodes.TemplateWrongFormat, "A file is required.");
                var version = form["version"].ToString();
                try
                {
                    await using var s = file.OpenReadStream();
                    var result = await service.ReplaceFileAsync(
                        tenantId, id, actorId, file.FileName, s, file.Length,
                        string.IsNullOrWhiteSpace(version) ? null : version, ct);
                    return Results.Ok(new { ok = true, data = result });
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { ok = false, errorCode = "NotFound" });
                }
                catch (InvalidOperationException ex)
                    when (ex.Message == WizardErrorCodes.TemplateTooLarge)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = WizardErrorCodes.TemplateTooLarge,
                        message = $"File '{file.FileName}' exceeds the org-template size limit.",
                    }, statusCode: StatusCodes.Status413PayloadTooLarge);
                }
            });

        group.MapPost("/{id:guid}/default", async (
                HttpContext http,
                Guid id,
                IOrganizationTemplateService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                try
                {
                    var t = await service.MarkDefaultAsync(tenantId, id, actorId, ct);
                    return Results.Ok(new { ok = true, data = t });
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { ok = false, errorCode = "NotFound" });
                }
            });

        group.MapDelete("/{id:guid}/default/clear", async (
                HttpContext http,
                Guid id,
                IOrganizationTemplateService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                try
                {
                    await service.ClearDefaultAsync(tenantId, id, actorId, ct);
                    return Results.NoContent();
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

/// <summary>Wire format for PATCH /templates/{id}.</summary>
public sealed class PatchTemplateRequest
{
    public string? Label { get; set; }
    public string? Version { get; set; }
}
