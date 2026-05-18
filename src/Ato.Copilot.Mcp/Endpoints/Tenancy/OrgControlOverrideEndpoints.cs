using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints.Tenancy;

/// <summary>
/// Per-org NIST control override surface (Feature 048 follow-up — user
/// ask #2). Mounted under <c>/api/orgs/control-overrides</c>. Reads are
/// open to any authenticated tenant member so the dashboard can decorate
/// catalog rows; writes require the same administrator policy used by
/// the onboarding wizard.
/// </summary>
public static class OrgControlOverrideEndpoints
{
    public static IEndpointRouteBuilder MapOrgControlOverrideEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── Reads ───────────────────────────────────────────────────────
        // Available to any authenticated tenant member: the catalog UI
        // needs to render the "Org override" badge for everyone, not just
        // admins. Tenant scoping is enforced by the EF query filter on
        // OrgControlOverride; cross-tenant reads are not possible.
        var reads = app.MapGroup("/api/orgs/control-overrides")
            .WithTags("OrgControlOverrides");

        reads.MapGet("/", async (
                IOrgControlOverrideService service,
                CancellationToken ct) =>
            {
                var rows = await service.ListAsync(ct);
                return Results.Ok(new { ok = true, data = rows.Select(Project).ToList() });
            })
            .WithName("ListOrgControlOverrides");

        reads.MapGet("/{controlId}", async (
                string controlId,
                IOrgControlOverrideService service,
                CancellationToken ct) =>
            {
                var row = await service.GetAsync(controlId, ct);
                return row is null
                    ? Results.NotFound(new { ok = false, errorCode = "NotFound" })
                    : Results.Ok(new { ok = true, data = Project(row) });
            })
            .WithName("GetOrgControlOverride");

        // ─── Writes ──────────────────────────────────────────────────────
        // Gated by the same administrator policy that protects every
        // mutating onboarding-wizard endpoint, so the same role-claim
        // pipeline applies (no new role required).
        var writes = app.MapGroup("/api/orgs/control-overrides")
            .WithTags("OrgControlOverrides")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName);

        writes.MapPut("/{controlId}", async (
                string controlId,
                OrgControlOverrideRequest request,
                HttpContext http,
                IOrgControlOverrideService service,
                CancellationToken ct) =>
            {
                if (!TryGetActor(http.User, out var actor)) return Forbidden();
                if (request is null)
                {
                    return Envelope("ValidationFailed", "Request body is required.");
                }

                ControlImplementationStatus? status = null;
                if (!string.IsNullOrWhiteSpace(request.ImplementationStatus))
                {
                    if (!Enum.TryParse<ControlImplementationStatus>(
                            request.ImplementationStatus, ignoreCase: true, out var parsedStatus))
                    {
                        return Envelope("ValidationFailed",
                            $"ImplementationStatus '{request.ImplementationStatus}' is not recognized.");
                    }
                    status = parsedStatus;
                }

                ControlInheritanceApplicability? applicability = null;
                if (!string.IsNullOrWhiteSpace(request.InheritanceApplicability))
                {
                    if (!Enum.TryParse<ControlInheritanceApplicability>(
                            request.InheritanceApplicability, ignoreCase: true, out var parsedApp))
                    {
                        return Envelope("ValidationFailed",
                            $"InheritanceApplicability '{request.InheritanceApplicability}' is not recognized.");
                    }
                    applicability = parsedApp;
                }

                try
                {
                    var saved = await service.UpsertAsync(
                        controlId, status, applicability, request.Justification, actor, ct);
                    // saved == null means both override fields were null and
                    // any prior row was deleted — surface as 200 + null data
                    // so the dashboard can clear its local cache.
                    return Results.Ok(new { ok = true, data = saved is null ? null : Project(saved) });
                }
                catch (ArgumentException ex)
                {
                    return Envelope("ValidationFailed", ex.Message);
                }
            })
            .WithName("UpsertOrgControlOverride");

        writes.MapDelete("/{controlId}", async (
                string controlId,
                HttpContext http,
                IOrgControlOverrideService service,
                CancellationToken ct) =>
            {
                if (!TryGetActor(http.User, out var actor)) return Forbidden();
                try
                {
                    var removed = await service.DeleteAsync(controlId, actor, ct);
                    return removed
                        ? Results.Ok(new { ok = true, data = (object?)null })
                        : Results.NotFound(new { ok = false, errorCode = "NotFound" });
                }
                catch (ArgumentException ex)
                {
                    return Envelope("ValidationFailed", ex.Message);
                }
            })
            .WithName("DeleteOrgControlOverride");

        return app;
    }

    private static object Project(OrgControlOverride row) => new
    {
        id = row.Id,
        controlId = row.ControlId,
        implementationStatus = row.ImplementationStatus?.ToString(),
        inheritanceApplicability = row.InheritanceApplicability?.ToString(),
        justification = row.Justification,
        createdAt = row.CreatedAt,
        createdBy = row.CreatedBy,
        updatedAt = row.UpdatedAt,
        updatedBy = row.UpdatedBy,
    };

    private static bool TryGetActor(ClaimsPrincipal user, out string actor)
    {
        // Prefer the upn / email; fall back to oid for service-principal callers.
        actor = user.FindFirstValue("upn")
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("oid")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? string.Empty;
        return !string.IsNullOrWhiteSpace(actor);
    }

    private static IResult Forbidden() => Results.Json(new
    {
        ok = false,
        errorCode = "AuthForbidden",
        message = "You must be signed in to modify org control overrides.",
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult Envelope(string errorCode, string message) => Results.Json(new
    {
        ok = false,
        errorCode,
        message,
    }, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>HTTP request body for <c>PUT /api/orgs/control-overrides/{controlId}</c>.</summary>
public sealed class OrgControlOverrideRequest
{
    /// <summary>String name of <see cref="ControlImplementationStatus"/>; null to clear.</summary>
    public string? ImplementationStatus { get; set; }

    /// <summary>String name of <see cref="ControlInheritanceApplicability"/>; null to clear.</summary>
    public string? InheritanceApplicability { get; set; }

    /// <summary>Required when at least one override field is non-null.</summary>
    public string? Justification { get; set; }
}
