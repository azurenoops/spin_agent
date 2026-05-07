using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints.Onboarding;

/// <summary>
/// Onboarding wizard Step 2 — Person directory endpoints (FR-022 / research §R1).
/// </summary>
public static class PersonEndpoints
{
    public static IEndpointRouteBuilder MapPersonEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding/persons")
            .WithTags("Onboarding")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName);

        group.MapGet("/", async (
                string? query,
                HttpContext http,
                IPersonService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                var results = string.IsNullOrWhiteSpace(query)
                    ? await service.ListAsync(tenantId, ct)
                    : await service.SearchLocalAsync(tenantId, query, ct);
                return Results.Ok(new { ok = true, data = results.Select(Project) });
            })
            .WithName("ListPersons");

        group.MapPost("/", async (
                CreatePersonRequest request,
                HttpContext http,
                IPersonService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                if (request is null)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, "Request body is required.");
                }
                try
                {
                    var person = await service.CreateLocalAsync(
                        tenantId,
                        request.DisplayName ?? string.Empty,
                        request.Email ?? string.Empty,
                        request.PhoneNumber,
                        actorId,
                        Guid.NewGuid(),
                        ct);
                    return Results.Ok(new { ok = true, data = Project(person) });
                }
                catch (ArgumentException ex)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, ex.Message);
                }
            })
            .WithName("CreatePerson");

        group.MapGet("/directory", async (
                string? query,
                IPersonService service,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return Results.Ok(new { ok = true, data = Array.Empty<DirectoryPersonDto>() });
                }
                var results = await service.SearchDirectoryAsync(query, ct);
                return Results.Ok(new { ok = true, data = results });
            })
            .WithName("SearchDirectoryForPersons");

        group.MapPost("/{personId:guid}/promote", async (
                Guid personId,
                PromotePersonRequest request,
                HttpContext http,
                IPersonService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                if (request is null || request.EntraObjectId == Guid.Empty)
                {
                    return Envelope.Failure(
                        WizardErrorCodes.JobFailed,
                        "EntraObjectId is required.");
                }
                try
                {
                    var promoted = await service.PromoteToDirectoryAsync(
                        tenantId, personId, request.EntraObjectId,
                        actorId, Guid.NewGuid(), ct);
                    return Results.Ok(new { ok = true, data = Project(promoted) });
                }
                catch (InvalidOperationException ex)
                {
                    var status = ex.Message.Contains("already linked") ? 409 : 400;
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = WizardErrorCodes.JobFailed,
                        message = ex.Message,
                        suggestion = (string?)null,
                    }, statusCode: status);
                }
            })
            .WithName("PromotePerson");

        return app;
    }

    private static object Project(Person p) => new
    {
        id = p.Id,
        displayName = p.DisplayName,
        email = p.Email,
        phoneNumber = p.PhoneNumber,
        entraObjectId = p.EntraObjectId,
        isLinkedToDirectory = p.IsLinkedToDirectory,
    };

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

/// <summary>HTTP request body for <c>POST /api/onboarding/persons</c>.</summary>
public sealed class CreatePersonRequest
{
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
}

/// <summary>HTTP request body for <c>POST /api/onboarding/persons/{id}/promote</c>.</summary>
public sealed class PromotePersonRequest
{
    public Guid EntraObjectId { get; set; }
}
