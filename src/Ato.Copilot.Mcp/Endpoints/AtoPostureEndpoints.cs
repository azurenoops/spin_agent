// =============================================================================
//  AtoPostureEndpoints.cs
//  Ato.Copilot.Mcp — Endpoints
//  Issue #422 — AO Posture API (Phase 2, W10 cATO Gap Closure)
//
//  GET /api/systems/{id}/ato-posture
//  Headers: X-Cache-Hit, X-Snapshot-Age-Seconds
//  Auth: JWT bearer, minimum Viewer role
//  Role-gated fields: authorizingOfficial, csrmcPillarStatus (AuthorizingOfficial only)
//  forceRefresh: requires ISSM+ role → ForbiddenException → 403
// =============================================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ato.Copilot.Core.Interfaces.Compliance;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// Maps <c>GET /api/systems/{id}/ato-posture</c> — AO Posture snapshot endpoint.
/// </summary>
public static class AtoPostureEndpoints
{
    // Roles allowed to call forceRefresh=true (mirrors IAtoPostureService.RefreshRoles)
    private static readonly HashSet<string> IssmmPlusRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ISSM", "ISSO", "SCA", "AuthorizingOfficial", "AO",
        "Compliance.Administrator", "Compliance.SecurityLead"
    };

    /// <summary>
    /// Registers the ATO posture endpoint group.
    /// </summary>
    public static IEndpointRouteBuilder MapAtoPostureEndpoints(this IEndpointRouteBuilder app)
    {
        // Note: /api/systems is an existing group — this endpoint extends it.
        // Route convention matches existing /api/systems/{id}/* pattern.
        app.MapGet("/api/systems/{id}/ato-posture", HandleGetAtoPosture)
            .WithTags("AO Posture")
            .WithName("GetAtoPosture")
            .WithSummary("Get ATO posture snapshot")
            .WithDescription("""
                Aggregated, machine-readable ATO posture snapshot for the specified system.
                Cached 5 minutes per system ID. Use ?refresh=true to bypass (requires ISSM+).
                Role-gated fields: authorization.authorizingOfficial and catoEligibility.csrmcPillarStatus
                require AuthorizingOfficial role.
                """)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleGetAtoPosture(
        string id,
        bool? refresh,
        IAtoPostureService postureService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // Parse system ID
        if (!Guid.TryParse(id, out var systemId))
        {
            return Results.Problem(
                title: "Invalid System ID",
                detail: $"'{id}' is not a valid GUID.",
                statusCode: 400,
                type: "https://spinagent.io/errors/bad-request");
        }

        // Extract caller roles from JWT claims
        var callerRoles = ExtractRoles(httpContext.User);

        var forceRefresh = refresh ?? false;

        try
        {
            var posture = await postureService.GetPostureAsync(
                systemId, callerRoles, forceRefresh, ct);

            if (posture is null)
            {
                return Results.Problem(
                    title: "System Not Found",
                    detail: $"Registered system '{systemId}' was not found.",
                    statusCode: 404,
                    type: "https://spinagent.io/errors/system-not-found");
            }

            // Attach cache headers per OpenAPI spec
            httpContext.Response.Headers["X-Cache-Hit"] =
                posture.ServedFromCache.ToString().ToLowerInvariant();
            httpContext.Response.Headers["X-Snapshot-Age-Seconds"] =
                posture.ServedFromCache
                    ? ((int)(DateTimeOffset.UtcNow - posture.RetrievedAt).TotalSeconds).ToString()
                    : "0";

            // Shape the response to match OpenAPI contract
            return Results.Ok(new
            {
                systemId = posture.SystemId,
                systemName = posture.SystemName,
                snapshotTimestamp = posture.RetrievedAt,
                servedFromCache = posture.ServedFromCache,
                authorization = posture.AuthorizationStatus is null ? null : new
                {
                    status = GetAuthStatusString(posture.AuthorizationStatus),
                    type = posture.AuthorizationStatus.DecisionType?.ToString(),
                    decisionDate = posture.AuthorizationStatus.DecisionDate,
                    expirationDate = posture.AuthorizationStatus.ExpirationDate,
                    daysUntilExpiration = posture.AuthorizationStatus.DaysUntilExpiration,
                    authorizingOfficial = posture.AuthorizationStatus.AuthorizingOfficial,
                    residualRisk = (string?)null    // populated in Phase 5 (OSCAL import)
                },
                compliance = new
                {
                    score = posture.ComplianceSummary.ComplianceScore,
                    totalControls = posture.ComplianceSummary.TotalControls,
                    satisfied = posture.ComplianceSummary.Satisfied,
                    otherThanSatisfied = posture.ComplianceSummary.OtherThanSatisfied,
                    notAssessed = posture.ComplianceSummary.NotAssessed
                },
                findings = new
                {
                    total = posture.FindingsSummary.Total,
                    catI = posture.FindingsSummary.CatI,
                    catII = posture.FindingsSummary.CatII,
                    catIII = posture.FindingsSummary.CatIII
                },
                poam = new
                {
                    open = posture.PoamSummary.Open,
                    overdue = posture.PoamSummary.Overdue,
                    completed = posture.PoamSummary.Completed,
                    riskAccepted = posture.PoamSummary.RiskAccepted
                },
                conmon = new
                {
                    enabled = posture.ConMonSummary.IsEnabled,
                    lastCheck = posture.ConMonSummary.LastReportDate,
                    assessmentFrequency = posture.ConMonSummary.AssessmentFrequency,
                    latestComplianceScore = posture.ConMonSummary.LatestComplianceScore,
                    authorizedBaselineScore = posture.ConMonSummary.AuthorizedBaselineScore
                },
                catoEligibility = (object?)null,    // populated via separate EvaluateCatoEligibility call
                csrmcPillarStatus = posture.CsrmcPillarStatus is null ? null : new
                {
                    pillar1_reciprocity = posture.CsrmcPillarStatus.Pillar1Status.ToString().ToLowerInvariant(),
                    pillar2_automation = posture.CsrmcPillarStatus.Pillar2Status.ToString().ToLowerInvariant(),
                    pillar3_devsecops = posture.CsrmcPillarStatus.Pillar3Status.ToString().ToLowerInvariant(),
                    evaluatedAt = posture.CsrmcPillarStatus.EvaluatedAt
                }
            });
        }
        catch (ForbiddenException ex)
        {
            return Results.Problem(
                title: "Forbidden",
                detail: ex.Message,
                statusCode: 403,
                type: "https://spinagent.io/errors/forbidden");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred while retrieving the ATO posture snapshot.",
                statusCode: 500,
                type: "https://spinagent.io/errors/internal-server-error");
        }
    }

    private static IReadOnlySet<string> ExtractRoles(ClaimsPrincipal user)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Standard role claim types
        foreach (var claim in user.Claims)
        {
            if (claim.Type is ClaimTypes.Role or "roles" or "role")
            {
                roles.Add(claim.Value);
            }
        }

        // SPIN Agent simulation header (dev/test only — stripped in production by SimulationGate)
        // Role short codes per the SPIN Agent RBAC table
        var simRole = user.FindFirstValue("X-Simulated-Role");
        if (!string.IsNullOrWhiteSpace(simRole))
            roles.Add(simRole);

        return roles;
    }

    private static string GetAuthStatusString(AuthorizationStatusDto auth)
    {
        if (!auth.IsActive && auth.DecisionType is null) return "None";
        if (auth.IsExpired) return "Expired";
        if (auth.IsActive) return "Active";
        return "None";
    }
}
