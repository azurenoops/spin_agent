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
/// Onboarding wizard Step 1 endpoints — read / upsert organization context (FR-010..FR-014).
/// Mounts <c>GET /api/onboarding/organization-context</c> and
/// <c>PUT /api/onboarding/organization-context</c> per
/// <see href="../../../../specs/047-onboarding-wizard/contracts/onboarding-api.yaml"/>.
/// </summary>
public static class OrganizationContextEndpoints
{
    /// <summary>
    /// Map the organization-context endpoints. The same authorization policy used by other
    /// wizard endpoints (<see cref="OnboardingAdministratorRequirement.PolicyName"/>) is
    /// applied here.
    /// </summary>
    public static IEndpointRouteBuilder MapOrganizationContextEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding/organization-context")
            .WithTags("Onboarding")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName);

        group.MapGet("/", async (
                HttpContext http,
                IOrganizationContextService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId))
                {
                    return Forbidden();
                }
                var context = await service.GetAsync(tenantId, ct);
                return Results.Ok(new { ok = true, data = Project(context) });
            })
            .WithName("GetOrganizationContext");

        group.MapPut("/", async (
                OrganizationContextRequest request,
                HttpContext http,
                IOrganizationContextService service,
                IOnboardingStateService stateService,
                Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId))
                {
                    return Forbidden();
                }
                if (!TryGetSubject(http.User, out var actorId))
                {
                    return Forbidden();
                }
                if (request is null)
                {
                    return Envelope.Failure(
                        WizardErrorCodes.JobFailed,
                        "Request body is required.");
                }

                if (!TryParseBranch(request.Branch, out var branch))
                {
                    return Envelope.Failure(
                        WizardErrorCodes.JobFailed,
                        $"Branch '{request.Branch}' is not a recognized BranchAffiliation value.");
                }

                ClassificationPosture? posture = null;
                if (!string.IsNullOrWhiteSpace(request.ClassificationPosture))
                {
                    if (!Enum.TryParse<ClassificationPosture>(
                        request.ClassificationPosture, ignoreCase: true, out var parsedPosture))
                    {
                        return Envelope.Failure(
                            WizardErrorCodes.JobFailed,
                            $"Classification posture '{request.ClassificationPosture}' is not recognized.");
                    }
                    posture = parsedPosture;
                }

                var input = new OrganizationContextInput(
                    OrganizationName: request.OrganizationName ?? string.Empty,
                    Branch: branch,
                    BranchQualifier: request.BranchQualifier,
                    SubOrganization: request.SubOrganization,
                    ClassificationPosture: posture,
                    AuthoritativeRepositoryUrl: request.AuthoritativeRepositoryUrl,
                    PrimaryPocEmail: request.PrimaryPocEmail);

                try
                {
                    var saved = await service.UpsertAsync(
                        tenantId, input, actorId, Guid.NewGuid(), ct);

                    // Mark wizard Step 1 (`OrganizationContext`) as completed so the
                    // OnboardingGate stops forcing the modal once required steps are
                    // satisfied. Best-effort — never fails the user's primary save.
                    try
                    {
                        await stateService.MarkStepCompletedAsync(
                            tenantId,
                            "OrganizationContext",
                            durationMs: 0,
                            actorId,
                            Guid.NewGuid(),
                            ct);
                    }
                    catch (Exception markEx)
                    {
                        loggerFactory
                            .CreateLogger("OrganizationContextEndpoints")
                            .LogWarning(
                                markEx,
                                "Failed to mark OrganizationContext step completed for tenant {TenantId}.",
                                tenantId);
                    }

                    return Results.Ok(new { ok = true, data = Project(saved) });
                }
                catch (ArgumentException ex)
                {
                    return Envelope.Failure(
                        WizardErrorCodes.JobFailed,
                        ex.Message);
                }
            })
            .WithName("UpsertOrganizationContext");

        return app;
    }

    private static bool TryParseBranch(string? raw, out BranchAffiliation branch)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            branch = BranchAffiliation.CivilAgency;
            return false;
        }
        return Enum.TryParse(raw, ignoreCase: true, out branch);
    }

    private static object? Project(OrganizationContext? c) => c is null ? null : new
    {
        organizationName = c.OrganizationName,
        branch = c.Branch.ToString(),
        branchQualifier = c.BranchQualifier,
        subOrganization = c.SubOrganization,
        classificationPosture = c.ClassificationPosture?.ToString(),
        authoritativeRepositoryUrl = c.AuthoritativeRepositoryUrl,
        primaryPocEmail = c.PrimaryPocEmail,
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

/// <summary>
/// HTTP request body for <c>PUT /api/onboarding/organization-context</c>.
/// </summary>
public sealed class OrganizationContextRequest
{
    public string? OrganizationName { get; set; }
    public string? Branch { get; set; }
    public string? BranchQualifier { get; set; }
    public string? SubOrganization { get; set; }
    public string? ClassificationPosture { get; set; }
    public string? AuthoritativeRepositoryUrl { get; set; }
    public string? PrimaryPocEmail { get; set; }
}
