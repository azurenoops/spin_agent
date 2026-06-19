// =============================================================================
//  AtoPostureEndpoints.cs
//  Ato.Copilot.Mcp — Endpoints
//  Issue #422 — AO Posture API (W10 cATO Gap Closure)
//
//  GET /api/systems/{systemId}/ato-posture
//  Response: AtoPostureDto with X-Cache-Hit + X-Snapshot-Age-Seconds headers.
//  RFC 7807 ProblemDetails on all errors.
// =============================================================================

#nullable enable

using System.Security.Claims;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Services.Roles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// ATO Posture REST endpoints (Issue #422 — W10 cATO Gap Closure).
///
/// <para>
/// <b>GET /api/systems/{systemId}/ato-posture</b> — machine-readable ATO posture snapshot.
/// Minimum role: authenticated (Viewer). Role-gated fields require AuthorizingOfficial.
/// forceRefresh query param requires ISSM+.
/// </para>
/// </summary>
public static class AtoPostureEndpoints
{
    public static IEndpointRouteBuilder MapAtoPostureEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/systems")
            .RequireAuthorization(); // Minimum: authenticated caller

        // ─── GET /api/systems/{systemId}/ato-posture ─────────────────────────
        group.MapGet("/{systemId}/ato-posture", GetAtoPostureAsync)
            .WithName("GetAtoPosture")
            .WithSummary("Get ATO posture snapshot")
            .WithDescription(
                "Aggregated, machine-readable ATO posture snapshot for the specified system. " +
                "Cached 5 minutes per system. Use ?refresh=true to bypass (requires ISSM+).");

        return app;
    }

    private static async Task<IResult> GetAtoPostureAsync(
        string systemId,
        HttpContext httpContext,
        IAtoPostureService postureService,
        ICallerEffectiveRoleResolver roleResolver,
        ILogger<AtoPostureEndpoints> logger,
        bool refresh = false,
        CancellationToken ct = default)
    {
        // ── Parse systemId ──
        if (!Guid.TryParse(systemId, out var systemGuid))
        {
            return Results.Problem(
                detail: $"'{systemId}' is not a valid system identifier.",
                title: "Invalid System ID",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://httpstatuses.com/400");
        }

        // ── Resolve caller roles ──
        var callerRoles = ExtractCallerRoles(httpContext.User);

        var snapshotCapturedAt = DateTimeOffset.UtcNow; // populated from response after call

        try
        {
            var posture = await postureService.GetPostureAsync(
                systemGuid, callerRoles, forceRefresh: refresh, cancellationToken: ct);

            if (posture is null)
            {
                return Results.Problem(
                    detail: $"Registered system '{systemId}' was not found or is inactive.",
                    title: "System Not Found",
                    statusCode: StatusCodes.Status404NotFound,
                    type: "https://httpstatuses.com/404");
            }

            // ── Set response headers ──
            httpContext.Response.Headers["X-Cache-Hit"] = posture.ServedFromCache ? "true" : "false";

            var ageSeconds = (int)(DateTimeOffset.UtcNow - posture.RetrievedAt).TotalSeconds;
            httpContext.Response.Headers["X-Snapshot-Age-Seconds"] = Math.Max(0, ageSeconds).ToString();

            return Results.Ok(posture);
        }
        catch (ForbiddenException ex)
        {
            logger.LogWarning(ex,
                "Forbidden: caller lacks required role for forceRefresh on system {SystemId}", systemId);

            return Results.Problem(
                detail: ex.Message,
                title: "Forbidden",
                statusCode: StatusCodes.Status403Forbidden,
                type: "https://httpstatuses.com/403");
        }
        catch (SystemNotFoundException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                title: "System Not Found",
                statusCode: StatusCodes.Status404NotFound,
                type: "https://httpstatuses.com/404");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unexpected error retrieving ATO posture for system {SystemId}", systemId);

            return Results.Problem(
                detail: "An unexpected error occurred while retrieving ATO posture.",
                title: "Internal Server Error",
                statusCode: StatusCodes.Status500InternalServerError,
                type: "https://httpstatuses.com/500");
        }
    }

    /// <summary>
    /// Extracts normalized role names from the authenticated caller's ClaimsPrincipal.
    /// Unions role claims with the SPIN Agent-specific "spin-role" claim type.
    /// </summary>
    private static IReadOnlySet<string> ExtractCallerRoles(ClaimsPrincipal user)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Standard "roles" / ClaimTypes.Role
        foreach (var claim in user.Claims)
        {
            if (claim.Type == ClaimTypes.Role ||
                claim.Type == "roles" ||
                claim.Type == "role" ||
                claim.Type == "spin-role")
            {
                roles.Add(claim.Value);
            }
        }

        return roles;
    }
}

