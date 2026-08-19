using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Document.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Services;
using System.Text.RegularExpressions;

using KanbanTaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

namespace Ato.Copilot.Mcp.Endpoints;

// ─── #648 Decomposition: Systems domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapSystemRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
        group.MapGet("", async (
                [AsParameters] PortfolioQuery query,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetPortfolioAsync(query, ct);
                var items = result.Items ?? [];
                var avgCompliance = items.Count > 0
                    ? Math.Round(items.Average(i => i.ComplianceScore), 1)
                    : 0;
                var configuredSystems = items.Count(i => i.IsSetupComplete);
                var coveragePercent = items.Count > 0
                    ? Math.Round(100.0 * configuredSystems / items.Count, 1)
                    : 0.0;  // Issue #466: return 0 instead of null so frontend shows 0% not N/A
                return Results.Ok(new
                {
                    totalSystems = result.TotalCount,
                    avgCompliance,
                    openPoams = items.Sum(i => i.OpenPoamCount),
                    overduePoams = items.Sum(i => i.OverduePoamCount),
                    catIFindings = items.Sum(i => i.CatICounts),
                    catIIFindings = items.Sum(i => i.CatIICounts),
                    atoAtRisk = items.Count(i =>
                        string.Equals(i.AtoSeverity, "red", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(i.AtoSeverity, "expired", StringComparison.OrdinalIgnoreCase)),
                    coveragePercent,
                    _links = new { portfolio = "/api/dashboard/portfolio", metrics = "/api/dashboard/metrics" },
                });
            })
            .WithName("GetDashboardRoot");

        // ─── Portfolio (US1) ─────────────────────────────────────────────────
        group.MapGet("/portfolio", async (
                [AsParameters] PortfolioQuery query,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetPortfolioAsync(query, ct);
                return Results.Ok(result);
            })
            .WithName("GetPortfolio");

        // Legacy aliases for older dashboard bundles that still call deprecated routes.
        group.MapGet("/systems", async (
                [AsParameters] PortfolioQuery query,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetPortfolioAsync(query, ct);
                return Results.Ok(result);
            })
            .WithName("GetSystemsLegacy");

        group.MapGet("/metrics", async (
                [AsParameters] PortfolioQuery query,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetPortfolioAsync(query, ct);
                var items = result.Items ?? [];

                var avgCompliance = items.Count > 0
                    ? Math.Round(items.Average(i => i.ComplianceScore), 1)
                    : 0;

                var atoAtRisk = items.Count(i =>
                    string.Equals(i.AtoSeverity, "red", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(i.AtoSeverity, "expired", StringComparison.OrdinalIgnoreCase));

                var configuredSystems = items.Count(i => i.IsSetupComplete);
                var coveragePercent = items.Count > 0
                    ? Math.Round(100.0 * configuredSystems / items.Count, 1)
                    : 0.0;  // Issue #466: return 0 instead of null so frontend shows 0% not N/A

                return Results.Ok(new
                {
                    totalSystems = result.TotalCount,
                    avgCompliance,
                    openPoams = items.Sum(i => i.OpenPoamCount),
                    overduePoams = items.Sum(i => i.OverduePoamCount),
                    catIFindings = items.Sum(i => i.CatICounts),
                    catIIFindings = items.Sum(i => i.CatIICounts),
                    catIIIFindings = items.Sum(i => i.CatIIICounts),
                    atoAtRisk,
                    coveragePercent,
                });
            })
            .WithName("GetDashboardMetricsLegacy");

        group.MapGet("/coverage", async (
                [AsParameters] PortfolioQuery query,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetPortfolioAsync(query, ct);
                var items = result.Items ?? [];
                var configuredSystems = items.Count(i => i.IsSetupComplete);
                var coveragePercent = items.Count > 0
                    ? Math.Round(100.0 * configuredSystems / items.Count, 1)
                    : 0.0;  // Issue #466: return 0 instead of null so frontend shows 0% not N/A

                return Results.Ok(new
                {
                    totalSystems = result.TotalCount,
                    configuredSystems,
                    coveragePercent,
                    coverage = coveragePercent,
                });
            })
            .WithName("GetCoverageLegacy");

        // ─── Register System ─────────────────────────────────────────────────
        group.MapPost("/systems", async (
                RegisterSystemRequest body,
                IRmfLifecycleService lifecycleService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.Name))
                    return Results.BadRequest(new ErrorResponse { Error = "Name is required", ErrorCode = "INVALID_INPUT" });

                if (!Enum.TryParse<SystemType>(body.SystemType, true, out var systemType))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = $"Invalid system_type '{body.SystemType}'",
                        ErrorCode = "INVALID_INPUT",
                        Suggestion = "Use: MajorApplication, Enclave, PlatformIt"
                    });

                if (!Enum.TryParse<MissionCriticality>(body.MissionCriticality, true, out var mission))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = $"Invalid mission_criticality '{body.MissionCriticality}'",
                        ErrorCode = "INVALID_INPUT",
                        Suggestion = "Use: MissionCritical, MissionEssential, MissionSupport"
                    });

                AzureEnvironmentProfile? azureProfile = null;
                if (!string.IsNullOrWhiteSpace(body.CloudEnvironment) &&
                    Enum.TryParse<AzureCloudEnvironment>(body.CloudEnvironment, true, out var cloudEnv))
                {
                    azureProfile = new AzureEnvironmentProfile
                    {
                        CloudEnvironment = cloudEnv,
                        SubscriptionIds = body.SubscriptionIds ?? []
                    };
                }

                var system = await lifecycleService.RegisterSystemAsync(
                    body.Name, systemType, mission,
                    body.HostingEnvironment ?? "AzureGovernment",
                    "dashboard-user", body.Acronym, body.Description,
                    azureProfile, ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = system.Id,
                    EventType = "SystemRegistered",
                    Actor = "dashboard-user",
                    Summary = $"System '{system.Name}' registered (type: {system.SystemType}, criticality: {system.MissionCriticality})",
                    RelatedEntityType = "RegisteredSystem",
                    RelatedEntityId = system.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/systems/{system.Id}", new
                {
                    id = system.Id,
                    name = system.Name,
                    acronym = system.Acronym,
                    systemType = system.SystemType.ToString(),
                    missionCriticality = system.MissionCriticality.ToString(),
                    hostingEnvironment = system.HostingEnvironment,
                    currentRmfStep = system.CurrentRmfStep.ToString()
                });
            })
            .WithName("RegisterSystem");

        // ─── Update System ───────────────────────────────────────────────────
        group.MapPut("/systems/{systemId}", async (
                string systemId,
                UpdateSystemRequest body,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var system = await db.RegisteredSystems
                    .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, ct);

                if (system is null)
                    return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "SYSTEM_NOT_FOUND" });

                if (!string.IsNullOrWhiteSpace(body.Name))
                    system.Name = body.Name;
                if (body.Acronym is not null)
                    system.Acronym = body.Acronym == "" ? null : body.Acronym;
                if (!string.IsNullOrWhiteSpace(body.SystemType))
                {
                    if (!Enum.TryParse<SystemType>(body.SystemType, true, out var st))
                        return Results.BadRequest(new ErrorResponse { Error = $"Invalid system_type '{body.SystemType}'", ErrorCode = "INVALID_INPUT" });
                    system.SystemType = st;
                }
                if (!string.IsNullOrWhiteSpace(body.MissionCriticality))
                {
                    if (!Enum.TryParse<MissionCriticality>(body.MissionCriticality, true, out var mc))
                        return Results.BadRequest(new ErrorResponse { Error = $"Invalid mission_criticality '{body.MissionCriticality}'", ErrorCode = "INVALID_INPUT" });
                    system.MissionCriticality = mc;
                }
                if (!string.IsNullOrWhiteSpace(body.HostingEnvironment))
                    system.HostingEnvironment = body.HostingEnvironment;
                if (body.Description is not null)
                    system.Description = body.Description == "" ? null : body.Description;

                system.ModifiedAt = DateTime.UtcNow;

                db.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "SystemUpdated",
                    Actor = "dashboard-user",
                    Summary = $"System '{system.Name}' properties updated",
                    RelatedEntityType = "RegisteredSystem",
                    RelatedEntityId = systemId,
                });
                await db.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    id = system.Id,
                    name = system.Name,
                    acronym = system.Acronym,
                    systemType = system.SystemType.ToString(),
                    missionCriticality = system.MissionCriticality.ToString(),
                    hostingEnvironment = system.HostingEnvironment,
                    description = system.Description,
                });
            })
            .WithName("UpdateSystem");

        // ─── Delete System — #519/#524 ───────────────────────────────────────
        // Default (permanent=false): soft-delete — sets IsActive=false so the
        // system is excluded from all queries. Used by the intake wizard cancel path.
        // permanent=true: hard-deletes the RegisteredSystem row and all cascade
        // child rows (boundaries, roles, assessments, etc.) via EF cascade config.
        // Required to purge orphaned QA test systems that corrupt portfolio metrics.
        group.MapDelete("/systems/{systemId}", async (
                string systemId,
                [FromQuery] bool permanent,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var system = await db.RegisteredSystems
                    .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, ct);

                if (system is null)
                    return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "SYSTEM_NOT_FOUND" });

                if (permanent)
                {
                    // Hard-delete: EF cascade config removes all child rows automatically.
                    db.RegisteredSystems.Remove(system);
                    await db.SaveChangesAsync(ct);
                    return Results.Ok(new { id = systemId, deleted = true, permanent = true });
                }

                // Soft-delete (default — backward-compatible with wizard cancel, #459)
                system.IsActive = false;
                system.ModifiedAt = DateTime.UtcNow;

                db.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "SystemDeleted",
                    Actor = "dashboard-user",
                    Summary = $"System '{system.Name}' deleted",
                    RelatedEntityType = "RegisteredSystem",
                    RelatedEntityId = systemId,
                });
                await db.SaveChangesAsync(ct);

                return Results.Ok(new { id = systemId, deleted = true, permanent = false });
            })
            .WithName("DeleteSystem");

        // ─── RMF Role Assignments ────────────────────────────────────────────
        group.MapGet("/systems/{systemId}/roles", async (
                string systemId,
                IBoundaryService boundaryService,
                CancellationToken ct) =>
            {
                var roles = await boundaryService.ListRmfRolesAsync(systemId, ct);
                return Results.Ok(new
                {
                    items = roles.Select(r => new
                    {
                        id = r.Id,
                        role = r.RmfRole.ToString(),
                        userId = r.UserId,
                        userDisplayName = r.UserDisplayName,
                        assignedAt = r.AssignedAt,
                        assignedBy = r.AssignedBy,
                    }),
                    totalCount = roles.Count,
                });
            })
            .WithName("ListRmfRoles");

        group.MapPost("/systems/{systemId}/roles", async (
                string systemId,
                AssignRoleRequest body,
                IBoundaryService boundaryService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.Role))
                    return Results.BadRequest(new ErrorResponse { Error = "Role is required", ErrorCode = "INVALID_INPUT" });
                if (string.IsNullOrWhiteSpace(body.UserDisplayName))
                    return Results.BadRequest(new ErrorResponse { Error = "User name is required", ErrorCode = "INVALID_INPUT" });

                if (!Enum.TryParse<RmfRole>(body.Role, true, out var rmfRole))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = $"Invalid role '{body.Role}'",
                        ErrorCode = "INVALID_INPUT",
                        // Feature 049 / T041 — cross-endpoint contract pins the 7-role universe
                        // (FR-012 SC-007). NOTE: this legacy endpoint still operates on the 6-value
                        // RmfRole enum; the suggestion text MUST still surface the full 7-role
                        // universe so the client experience matches the unified /api/roles surface.
                        Suggestion = "Use one of: AuthorizingOfficial, Issm, Isso, Sca, SystemOwner, MissionOwner, Administrator"
                    });

                var userId = body.UserId ?? body.UserDisplayName.Replace(" ", ".").ToLowerInvariant();
                var assignment = await boundaryService.AssignRmfRoleAsync(
                    systemId, rmfRole, userId, body.UserDisplayName, "dashboard-user", ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "RoleAssigned",
                    Actor = "dashboard-user",
                    Summary = $"{body.UserDisplayName} assigned as {assignment.RmfRole}",
                    RelatedEntityType = "RmfRoleAssignment",
                    RelatedEntityId = assignment.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/systems/{systemId}/roles/{assignment.Id}", new
                {
                    id = assignment.Id,
                    role = assignment.RmfRole.ToString(),
                    userId = assignment.UserId,
                    userDisplayName = assignment.UserDisplayName,
                    assignedAt = assignment.AssignedAt,
                });
            })
            .WithName("AssignRmfRole");

        group.MapDelete("/systems/{systemId}/roles/{roleId}", async (
                string systemId,
                string roleId,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var assignment = await db.RmfRoleAssignments
                    .FirstOrDefaultAsync(r => r.Id == roleId
                        && r.RegisteredSystemId == systemId
                        && r.IsActive, ct);

                if (assignment is null)
                    return Results.NotFound(new ErrorResponse { Error = "Role assignment not found", ErrorCode = "NOT_FOUND" });

                assignment.IsActive = false;

                db.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "RoleRemoved",
                    Actor = "dashboard-user",
                    Summary = $"{assignment.UserDisplayName} removed from {assignment.RmfRole}",
                    RelatedEntityType = "RmfRoleAssignment",
                    RelatedEntityId = roleId,
                });
                await db.SaveChangesAsync(ct);

                return Results.Ok(new { deleted = true, id = roleId });
            })
            .WithName("DeleteRmfRole");

        // ─── System Detail (US2) ─────────────────────────────────────────────
        group.MapGet("/systems/{systemId}", async (
                string systemId,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetSystemDetailAsync(systemId, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
            })
            .WithName("GetSystemDetail");

        group.MapGet("/systems/{systemId}/heatmap", async (
                string systemId,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetHeatmapAsync(systemId, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System or baseline not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Ensure the system has a control baseline configured",
                    });
            })
            .WithName("GetHeatmap");

        group.MapGet("/systems/{systemId}/heatmap/{familyCode}/controls", async (
                string systemId,
                string familyCode,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetHeatmapControlsAsync(systemId, familyCode, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System, baseline, or family not found",
                        ErrorCode = "FAMILY_NOT_FOUND",
                        Suggestion = "Verify the family code is part of this system's baseline",
                    });
            })
            .WithName("GetHeatmapControls");

        // ─── Gap Analysis (US4) ──────────────────────────────────────────────
        group.MapGet("/systems/{systemId}/gap-analysis", async (
                string systemId,
                string? boundaryDefinitionId,
                CapabilityService capService,
                CancellationToken ct) =>
            {
                var result = await capService.GetGapAnalysisAsync(systemId, boundaryDefinitionId, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System or baseline not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Ensure the system has a control baseline configured",
                    });
            })
            .WithName("GetGapAnalysis");

        // ─── Org-Wide Component Library (Feature 036) ────────────────────────
    }
}
