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
/// Onboarding wizard Step 2 — Organization role assignment endpoints
/// (FR-020..FR-026 / FR-002 last-Administrator invariant).
/// </summary>
public static class RoleAssignmentEndpoints
{
    public static IEndpointRouteBuilder MapRoleAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding/role-assignments")
            .WithTags("Onboarding")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName);

        group.MapGet("/", async (
                HttpContext http,
                IOrganizationRoleAssignmentService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                var assignments = await service.ListAsync(tenantId, ct);
                return Results.Ok(new { ok = true, data = assignments.Select(Project) });
            })
            .WithName("ListRoleAssignments");

        group.MapPost("/", async (
                CreateRoleAssignmentRequest request,
                HttpContext http,
                IOrganizationRoleAssignmentService service,
                IOnboardingStateService stateService,
                Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                if (request is null)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, "Request body is required.");
                }
                if (!Enum.TryParse<OrganizationRole>(request.Role, ignoreCase: true, out var role))
                {
                    return Envelope.Failure(
                        WizardErrorCodes.JobFailed,
                        $"Unknown role '{request.Role}'.",
                        suggestion: "Use one of: Issm, Isso, Administrator, Assessor.");
                }
                if (request.PersonId == Guid.Empty)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, "PersonId is required.");
                }
                try
                {
                    var result = await service.AddAsync(
                        tenantId, role, request.PersonId, actorId, Guid.NewGuid(), ct);

                    // Mark wizard Step 2 (`Roles`) complete once all required roles
                    // (ISSM + ISSO + Administrator) have at least one active holder.
                    // Best-effort — never fails the user's primary save.
                    try
                    {
                        var active = await service.ListAsync(tenantId, ct);
                        var hasIssm = active.Any(a => a.Role == OrganizationRole.Issm);
                        var hasIsso = active.Any(a => a.Role == OrganizationRole.Isso);
                        var hasAdmin = active.Any(a => a.Role == OrganizationRole.Administrator);
                        if (hasIssm && hasIsso && hasAdmin)
                        {
                            await stateService.MarkStepCompletedAsync(
                                tenantId,
                                "Roles",
                                durationMs: 0,
                                actorId,
                                Guid.NewGuid(),
                                ct);
                        }
                    }
                    catch (Exception markEx)
                    {
                        loggerFactory
                            .CreateLogger("RoleAssignmentEndpoints")
                            .LogWarning(
                                markEx,
                                "Failed to mark Roles step completed for tenant {TenantId}.",
                                tenantId);
                    }

                    return Results.Ok(new
                    {
                        ok = true,
                        data = Project(result.Assignment),
                        warnings = result.Warnings,
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, ex.Message);
                }
            })
            .WithName("CreateRoleAssignment");

        group.MapDelete("/{assignmentId:guid}", async (
                Guid assignmentId,
                HttpContext http,
                IOrganizationRoleAssignmentService service,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();
                try
                {
                    await service.RemoveAsync(
                        tenantId, assignmentId, actorId, Guid.NewGuid(), ct);
                    return Results.Ok(new { ok = true, data = (object?)null });
                }
                catch (InvalidOperationException ex) when (ex.Message == WizardErrorCodes.LastAdminProtected)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        errorCode = WizardErrorCodes.LastAdminProtected,
                        message = "Cannot remove the last Administrator for this tenant.",
                        suggestion = "Assign another Administrator first, then retry the removal.",
                    }, statusCode: 409);
                }
                catch (InvalidOperationException ex)
                {
                    return Envelope.Failure(WizardErrorCodes.JobFailed, ex.Message);
                }
            })
            .WithName("DeleteRoleAssignment");

        return app;
    }

    private static object Project(OrganizationRoleAssignment a) => new
    {
        id = a.Id,
        role = a.Role.ToString(),
        personId = a.PersonId,
        isPrimary = a.IsPrimary,
        createdAt = a.CreatedAt,
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

/// <summary>HTTP request body for <c>POST /api/onboarding/role-assignments</c>.</summary>
public sealed class CreateRoleAssignmentRequest
{
    public string? Role { get; set; }
    public Guid PersonId { get; set; }
}
