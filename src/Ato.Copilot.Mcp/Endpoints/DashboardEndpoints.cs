using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;

namespace Ato.Copilot.Mcp.Endpoints;

/// <summary>
/// Maps all /api/dashboard/* REST endpoints for the Visual Compliance Dashboard.
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>
    /// Registers dashboard route group and all dashboard API endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard")
            .WithTags("Dashboard");

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

        // ─── Register System ─────────────────────────────────────────────────
        group.MapPost("/systems", async (
                RegisterSystemRequest body,
                IRmfLifecycleService lifecycleService,
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
                        Suggestion = "Use: AuthorizingOfficial, Issm, Isso, Sca, SystemOwner"
                    });

                var userId = body.UserId ?? body.UserDisplayName.Replace(" ", ".").ToLowerInvariant();
                var assignment = await boundaryService.AssignRmfRoleAsync(
                    systemId, rmfRole, userId, body.UserDisplayName, "dashboard-user", ct);

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
        group.MapGet("/systems/{systemId}/gaps", async (
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

        // ─── Components (US5) ────────────────────────────────────────────────
        group.MapGet("/systems/{systemId}/components", async (
                string systemId,
                [AsParameters] ComponentQuery query,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.GetComponentsAsync(systemId, query, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
            })
            .WithName("GetComponents");

        group.MapPost("/systems/{systemId}/components", async (
                string systemId,
                CreateComponentRequest request,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.CreateComponentAsync(systemId, request, "system", ct);
                return result is not null
                    ? Results.Created($"/api/dashboard/components/{result.Id}", result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
            })
            .WithName("CreateComponent");

        group.MapPut("/components/{id}", async (
                string id,
                CreateComponentRequest request,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.UpdateComponentAsync(id, request, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Component not found",
                        ErrorCode = "COMPONENT_NOT_FOUND",
                        Suggestion = "Check the component ID and try again",
                    });
            })
            .WithName("UpdateComponent");

        group.MapDelete("/components/{id}", async (
                string id,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.DeleteComponentAsync(id, "system", ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Component not found",
                        ErrorCode = "COMPONENT_NOT_FOUND",
                        Suggestion = "Check the component ID and try again",
                    });
            })
            .WithName("DeleteComponent");

        // ─── AI Component Description ────────────────────────────────────────
        group.MapPost("/ai/component-description", async (
                GenerateComponentDescriptionRequest body,
                IChatClient chatClient,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.Name))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Name is required",
                        ErrorCode = "INVALID_INPUT",
                    });

                var prompt = $"""Write a concise 2-3 sentence description for a system component used in a federal IT authorization boundary. The component is named "{body.Name}", is classified as a "{body.ComponentType}" type component{(string.IsNullOrWhiteSpace(body.SubType) ? "" : $" with sub-type \"{body.SubType}\"")}. The description should explain what the component does, its role in the system architecture, and its relevance to security and compliance. Do not include any markdown formatting. Return only the description text.""";

                var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);
                var description = response.Text?.Trim() ?? "";

                return Results.Ok(new { description });
            })
            .WithName("GenerateComponentDescription");

        // ─── AI Capability Description ─────────────────────────────────────
        group.MapPost("/ai/capability-description", async (
                GenerateCapabilityDescriptionRequest body,
                IChatClient chatClient,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.Name))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Name is required",
                        ErrorCode = "INVALID_INPUT",
                    });

                var prompt = $"""Write a concise 2-3 sentence description for a security capability used in a federal information system's authorization boundary. The capability is named "{body.Name}", provided by "{body.Provider}"{(string.IsNullOrWhiteSpace(body.Category) ? "" : $", mapped to the NIST 800-53 \"{body.Category}\" control family")}. The description should explain what the capability does, how it contributes to the system's security posture, and its relevance to RMF compliance. Do not include any markdown formatting. Return only the description text.""";

                var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);
                var description = response.Text?.Trim() ?? "";

                return Results.Ok(new { description });
            })
            .WithName("GenerateCapabilityDescription");

        // ─── AI System Description ─────────────────────────────────────────
        group.MapPost("/ai/system-description", async (
                GenerateSystemDescriptionRequest body,
                IChatClient chatClient,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.Name))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Name is required",
                        ErrorCode = "INVALID_INPUT",
                    });

                var prompt = $"""Write a concise 2-3 sentence description for a federal information system undergoing RMF authorization. The system is named "{body.Name}", classified as a "{body.SystemType}" with "{body.MissionCriticality}" mission criticality, hosted in "{body.HostingEnvironment}". The description should explain the system's purpose, its operational significance to the organization's mission, and its relevance to security authorization. Do not include any markdown formatting. Return only the description text.""";

                var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);
                var description = response.Text?.Trim() ?? "";

                return Results.Ok(new { description });
            })
            .WithName("GenerateSystemDescription");

        // ─── Capabilities (US3) ──────────────────────────────────────────────
        group.MapGet("/capabilities", async (
                [AsParameters] CapabilityQuery query,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.GetCapabilitiesAsync(query, ct);
                return Results.Ok(result);
            })
            .WithName("GetCapabilities");

        group.MapPost("/capabilities", async (
                CreateCapabilityRequest request,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.CreateCapabilityAsync(request, "system", ct);
                return result is not null
                    ? Results.Created($"/api/dashboard/capabilities/{result.Id}", result)
                    : Results.Conflict(new ErrorResponse
                    {
                        Error = "A capability with this name already exists",
                        ErrorCode = "CAPABILITY_NAME_DUPLICATE",
                        Suggestion = "Use a unique name or update the existing capability",
                    });
            })
            .WithName("CreateCapability");

        group.MapPut("/capabilities/{id}", async (
                string id,
                CreateCapabilityRequest request,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var (result, nameConflict) = await service.UpdateCapabilityAsync(id, request, "system", ct);
                if (nameConflict)
                    return Results.Conflict(new ErrorResponse
                    {
                        Error = "A capability with this name already exists",
                        ErrorCode = "CAPABILITY_NAME_DUPLICATE",
                        Suggestion = "Use a unique name or update the existing capability",
                    });
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Capability not found",
                        ErrorCode = "CAPABILITY_NOT_FOUND",
                        Suggestion = "Check the capability ID and try again",
                    });
            })
            .WithName("UpdateCapability");

        group.MapDelete("/capabilities/{id}", async (
                string id,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.DeleteCapabilityAsync(id, "system", ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Capability not found",
                        ErrorCode = "CAPABILITY_NOT_FOUND",
                        Suggestion = "Check the capability ID and try again",
                    });
            })
            .WithName("DeleteCapability");

        group.MapGet("/capabilities/{id}/mappings", async (
                string id,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.GetMappingsAsync(id, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Capability not found",
                        ErrorCode = "CAPABILITY_NOT_FOUND",
                        Suggestion = "Check the capability ID and try again",
                    });
            })
            .WithName("GetCapabilityMappings");

        group.MapPost("/capabilities/{id}/mappings", async (
                string id,
                CreateMappingsRequest request,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.CreateMappingsAsync(id, request, "system", ct);
                return result is not null
                    ? Results.Created($"/api/dashboard/capabilities/{id}/mappings", result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Capability not found",
                        ErrorCode = "CAPABILITY_NOT_FOUND",
                        Suggestion = "Check the capability ID and try again",
                    });
            })
            .WithName("CreateCapabilityMappings");

        // ─── Trends (US6) ───────────────────────────────────────────────────
        group.MapGet("/systems/{systemId}/trends", async (
                string systemId,
                [AsParameters] TrendQuery query,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetTrendsAsync(systemId, query, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
            })
            .WithName("GetTrends");

        // ─── Implementation Roadmap (Feature 031) ────────────────────────────
        group.MapGet("/systems/{systemId}/roadmap", async (
                string systemId,
                bool? includeItems,
                Ato.Copilot.Core.Interfaces.Roadmap.IRoadmapService roadmapService,
                CancellationToken ct) =>
            {
                var roadmap = await roadmapService.GetRoadmapAsync(
                    systemId, includeItems ?? true, ct);

                if (roadmap is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = $"No active roadmap found for system {systemId}",
                        ErrorCode = "ROADMAP_NOT_FOUND",
                        Suggestion = "Generate a roadmap first using the compliance_generate_roadmap tool",
                    });

                var allItems = roadmap.Phases.SelectMany(p => p.Items).ToList();
                var completedItems = allItems.Count(i => i.Status == Ato.Copilot.Core.Models.Roadmap.ItemStatus.Complete);
                var overallCompletion = allItems.Count > 0 ? (double)completedItems / allItems.Count * 100 : 0;

                var dto = new RoadmapDto
                {
                    RoadmapId = roadmap.Id,
                    SystemId = roadmap.SystemId,
                    SystemName = roadmap.Name,
                    Status = roadmap.Status.ToString(),
                    BaselineLevel = roadmap.BaselineLevel,
                    TotalGaps = roadmap.TotalGaps,
                    TotalEstimatedEffortDays = roadmap.TotalEstimatedEffort,
                    TotalRiskPoints = roadmap.TotalRiskPoints,
                    OverallCompletionPercent = Math.Round(overallCompletion, 1),
                    Phases = roadmap.Phases.OrderBy(p => p.DisplayOrder).Select(p => new RoadmapPhaseDto
                    {
                        PhaseId = p.Id,
                        Name = p.Name,
                        DisplayOrder = p.DisplayOrder,
                        EstimatedEffortDays = p.EstimatedEffort,
                        RiskPoints = p.RiskPoints,
                        RiskReductionPercent = Math.Round(p.RiskReductionPercent, 1),
                        TargetStartWeek = p.TargetStartWeek,
                        TargetEndWeek = p.TargetEndWeek,
                        Status = p.Status.ToString(),
                        CompletedItemCount = p.CompletedItemCount,
                        TotalItemCount = p.TotalItemCount,
                        Items = (includeItems ?? true)
                            ? p.Items.OrderBy(i => i.DisplayOrder).Select(i => new RoadmapItemDto
                            {
                                ItemId = i.Id,
                                ControlId = i.ControlId,
                                ControlTitle = i.ControlTitle,
                                ControlFamily = i.ControlFamily,
                                GapType = i.GapType.ToString(),
                                Severity = i.Severity.ToString(),
                                RiskPoints = i.RiskPoints,
                                EstimatedEffortDays = i.EstimatedEffortDays,
                                AssignedRole = i.AssignedRole,
                                DependsOn = string.IsNullOrEmpty(i.DependsOn) ? null : i.DependsOn.Split(',', StringSplitOptions.TrimEntries).ToList(),
                                Status = i.Status.ToString(),
                                LinkedTaskId = i.LinkedTaskId
                            }).ToList()
                            : null
                    }).ToList(),
                    CreatedAt = roadmap.CreatedAt,
                    UpdatedAt = roadmap.UpdatedAt
                };

                return Results.Ok(dto);
            })
            .WithName("GetRoadmap");

        group.MapGet("/systems/{systemId}/roadmap/progress", async (
                string systemId,
                Ato.Copilot.Core.Interfaces.Roadmap.IRoadmapService roadmapService,
                CancellationToken ct) =>
            {
                var progress = await roadmapService.GetRoadmapProgressAsync(systemId, ct);
                if (progress is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = $"No active roadmap found for system {systemId}",
                        ErrorCode = "ROADMAP_NOT_FOUND",
                    });

                var dto = new RoadmapProgressDto
                {
                    RoadmapId = progress.RoadmapId,
                    SystemName = progress.SystemName,
                    OverallCompletionPercent = progress.OverallCompletionPercent,
                    ItemsCompleted = progress.ItemsCompleted,
                    ItemsTotal = progress.ItemsTotal,
                    RiskCurve = progress.RiskCurve.Select(p => new RiskCurvePointDto
                    {
                        Week = p.Week,
                        RiskPoints = p.RiskPoints,
                        RiskReductionPercent = p.RiskReductionPercent
                    }).ToList(),
                    PhaseProgress = progress.PhaseProgress.Select(p => new PhaseProgressDto
                    {
                        Name = p.Name,
                        DisplayOrder = p.DisplayOrder,
                        CompletionPercent = p.CompletionPercent,
                        Status = p.Status,
                        ActualRiskReductionPercent = p.ActualRiskReductionPercent,
                        IsOverdue = p.IsOverdue,
                        DaysOverdue = p.DaysOverdue
                    }).ToList()
                };

                return Results.Ok(dto);
            })
            .WithName("GetRoadmapProgress");

        group.MapGet("/systems/{systemId}/roadmap/export", async (
                string systemId,
                Ato.Copilot.Core.Interfaces.Roadmap.IRoadmapService roadmapService,
                CancellationToken ct) =>
            {
                try
                {
                    var pdfBytes = await roadmapService.ExportRoadmapPdfAsync(systemId, ct);
                    var fileName = $"Implementation_Roadmap_{DateTime.UtcNow:yyyy-MM-dd}.pdf";
                    return Results.File(pdfBytes, "application/pdf", fileName);
                }
                catch (NotImplementedException)
                {
                    return Results.StatusCode(501);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "ROADMAP_NOT_FOUND",
                    });
                }
            })
            .WithName("ExportRoadmapPdf");

        // ─── Todo List ───────────────────────────────────────────────────────
        group.MapGet("/systems/{systemId}/todos", async (
                string systemId,
                TodoService todoService,
                CancellationToken ct) =>
            {
                var result = await todoService.GetTodoListAsync(systemId, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
            })
            .WithName("GetTodoList");

        // ─── Boundary Definitions (Feature 033) ─────────────────────────────
        group.MapGet("/systems/{systemId}/boundary-definitions", async (
                string systemId,
                BoundaryDefinitionService boundaryService,
                CancellationToken ct) =>
            {
                var items = await boundaryService.ListAsync(systemId, ct);
                return Results.Ok(new { items, totalCount = items.Count });
            })
            .WithName("GetBoundaryDefinitions");

        group.MapPost("/systems/{systemId}/boundary-definitions", async (
                string systemId,
                CreateBoundaryDefinitionRequest request,
                BoundaryDefinitionService boundaryService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await boundaryService.CreateAsync(systemId, request, "system", ct);
                    return Results.Created(
                        $"/api/dashboard/boundary-definitions/{result.Id}", result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
                {
                    return Results.Conflict(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "BOUNDARY_NAME_DUPLICATE",
                        Suggestion = "Use a unique name or update the existing boundary",
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
                }
            })
            .WithName("CreateBoundaryDefinition");

        group.MapPut("/boundary-definitions/{id}", async (
                string id,
                CreateBoundaryDefinitionRequest request,
                BoundaryDefinitionService boundaryService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await boundaryService.UpdateAsync(id, request, ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
                {
                    return Results.Conflict(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "BOUNDARY_NAME_DUPLICATE",
                        Suggestion = "Use a unique name or update the existing boundary",
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Boundary definition not found",
                        ErrorCode = "BOUNDARY_NOT_FOUND",
                        Suggestion = "Check the boundary definition ID and try again",
                    });
                }
            })
            .WithName("UpdateBoundaryDefinition");

        group.MapDelete("/boundary-definitions/{id}", async (
                string id,
                BoundaryDefinitionService boundaryService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await boundaryService.DeleteAsync(id, "system", ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Primary"))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "PRIMARY_BOUNDARY_DELETE",
                        Suggestion = "The Primary boundary cannot be deleted",
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Boundary definition not found",
                        ErrorCode = "BOUNDARY_NOT_FOUND",
                        Suggestion = "Check the boundary definition ID and try again",
                    });
                }
            })
            .WithName("DeleteBoundaryDefinition");

        // ─── Boundary Resources ─────────────────────────────────────────────
        group.MapGet("/boundary-definitions/{id}/resources", async (
                string id,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var definition = await context.AuthorizationBoundaryDefinitions
                    .FirstOrDefaultAsync(d => d.Id == id, ct);
                if (definition == null)
                    return Results.NotFound(new ErrorResponse { Error = "Boundary definition not found", ErrorCode = "BOUNDARY_NOT_FOUND" });

                var resources = await context.AuthorizationBoundaries
                    .Where(b => b.AuthorizationBoundaryDefinitionId == id)
                    .OrderBy(b => b.ResourceName)
                    .Select(b => new
                    {
                        b.Id,
                        b.ResourceId,
                        b.ResourceType,
                        b.ResourceName,
                        b.IsInBoundary,
                        b.ExclusionRationale,
                        b.InheritanceProvider
                    })
                    .ToListAsync(ct);

                return Results.Ok(new { items = resources, totalCount = resources.Count });
            })
            .WithName("GetBoundaryResources");

        group.MapPost("/boundary-definitions/{id}/resources", async (
                string id,
                AddBoundaryResourceRequest body,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var definition = await context.AuthorizationBoundaryDefinitions
                    .FirstOrDefaultAsync(d => d.Id == id, ct);
                if (definition == null)
                    return Results.NotFound(new ErrorResponse { Error = "Boundary definition not found", ErrorCode = "BOUNDARY_NOT_FOUND" });

                if (string.IsNullOrWhiteSpace(body.ResourceId))
                    return Results.BadRequest(new ErrorResponse { Error = "Resource ID is required", ErrorCode = "INVALID_INPUT" });

                if (string.IsNullOrWhiteSpace(body.ResourceType))
                    return Results.BadRequest(new ErrorResponse { Error = "Resource type is required", ErrorCode = "INVALID_INPUT" });

                // Check for duplicate
                var existing = await context.AuthorizationBoundaries
                    .FirstOrDefaultAsync(b =>
                        b.RegisteredSystemId == definition.RegisteredSystemId &&
                        b.ResourceId == body.ResourceId, ct);

                if (existing != null)
                {
                    // Update to point to this boundary definition
                    existing.AuthorizationBoundaryDefinitionId = id;
                    existing.IsInBoundary = true;
                    existing.ExclusionRationale = null;
                }
                else
                {
                    context.AuthorizationBoundaries.Add(new AuthorizationBoundary
                    {
                        RegisteredSystemId = definition.RegisteredSystemId,
                        ResourceId = body.ResourceId.Trim(),
                        ResourceType = body.ResourceType.Trim(),
                        ResourceName = body.ResourceName?.Trim(),
                        InheritanceProvider = body.InheritanceProvider?.Trim(),
                        IsInBoundary = true,
                        AddedBy = "dashboard-user",
                        AuthorizationBoundaryDefinitionId = id
                    });
                }

                await context.SaveChangesAsync(ct);
                return Results.Created();
            })
            .WithName("AddBoundaryResource");

        group.MapDelete("/boundary-definitions/{definitionId}/resources/{resourceEntryId}", async (
                string definitionId,
                string resourceEntryId,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var entry = await context.AuthorizationBoundaries
                    .FirstOrDefaultAsync(b => b.Id == resourceEntryId && b.AuthorizationBoundaryDefinitionId == definitionId, ct);
                if (entry == null)
                    return Results.NotFound(new ErrorResponse { Error = "Resource not found", ErrorCode = "RESOURCE_NOT_FOUND" });

                context.AuthorizationBoundaries.Remove(entry);
                await context.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithName("DeleteBoundaryResource");

        // ─── Azure Resource Discovery (Feature 033 US8) ─────────────────────
        group.MapGet("/systems/{systemId}/azure-discovery", async (
                string systemId,
                AzureResourceDiscoveryService discoveryService,
                AtoCopilotContext context,
                string? resourceGroup,
                string? resourceType,
                string? search,
                string? cursor,
                CancellationToken ct) =>
            {
                var system = await context.RegisteredSystems.FindAsync([systemId], ct);
                if (system == null)
                    return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "SYSTEM_NOT_FOUND" });

                var subscriptionId = system.AzureProfile?.SubscriptionIds.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(subscriptionId))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "System has no Azure subscription configured",
                        ErrorCode = "NO_SUBSCRIPTION",
                        Suggestion = "Register a system with a valid Azure subscription ID"
                    });

                var existingResourceIds = (await context.AuthorizationBoundaries
                    .Where(b => b.RegisteredSystemId == systemId)
                    .Select(b => b.ResourceId)
                    .ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

                var existingBoundaryNames = (await context.AuthorizationBoundaryDefinitions
                    .Where(bd => bd.RegisteredSystemId == systemId)
                    .Select(bd => bd.Name)
                    .ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

                try
                {
                    var result = await discoveryService.DiscoverResourcesAsync(
                        subscriptionId, existingResourceIds, existingBoundaryNames,
                        resourceGroup, resourceType, search, cursor, ct);
                    return Results.Ok(result);
                }
                catch (Azure.RequestFailedException ex) when (ex.Status == 401)
                {
                    return Results.Json(new ErrorResponse
                    {
                        Error = "Azure credentials unavailable. Ensure DefaultAzureCredential is configured.",
                        ErrorCode = "AZURE_AUTH_FAILED",
                        Suggestion = "Check managed identity or service principal configuration"
                    }, statusCode: 401);
                }
                catch (Azure.RequestFailedException ex) when (ex.Status == 403)
                {
                    return Results.Json(new ErrorResponse
                    {
                        Error = "Insufficient RBAC permissions. Reader role required on the subscription.",
                        ErrorCode = "AZURE_RBAC_DENIED",
                        Suggestion = "Assign the Reader role to the service principal on the subscription"
                    }, statusCode: 403);
                }
            })
            .WithName("DiscoverAzureResources");

        group.MapPost("/systems/{systemId}/azure-discovery/apply", async (
                string systemId,
                ApplyDiscoveryRequest request,
                BoundaryDefinitionService boundaryService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var system = await context.RegisteredSystems.FindAsync([systemId], ct);
                if (system == null)
                    return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "SYSTEM_NOT_FOUND" });

                var boundariesCreated = 0;
                var componentsCreated = 0;
                var skipped = 0;

                // Create boundaries from accepted resource groups
                var boundaryIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var b in request.Boundaries)
                {
                    try
                    {
                        var created = await boundaryService.CreateAsync(systemId,
                            new CreateBoundaryDefinitionRequest(b.Name, b.BoundaryType, b.Description), "azure-discovery", ct);
                        boundaryIdMap[b.ResourceGroupName] = created.Id;
                        boundariesCreated++;
                    }
                    catch (InvalidOperationException)
                    {
                        skipped++; // duplicate name
                    }
                }

                // Create components
                foreach (var c in request.Components)
                {
                    var defId = c.BoundaryDefinitionId;
                    if (string.IsNullOrEmpty(defId))
                    {
                        // try to look up from newly created boundaries via resource group extraction
                        var rg = AzureResourceDiscoveryService.ExtractResourceGroup(c.ResourceId);
                        if (!string.IsNullOrEmpty(rg) && boundaryIdMap.TryGetValue(rg, out var mapped))
                            defId = mapped;
                    }

                    context.SystemComponents.Add(new SystemComponent
                    {
                        RegisteredSystemId = systemId,
                        Name = c.Name,
                        ComponentType = ComponentType.Thing,
                        SubType = c.SubType,
                        AuthorizationBoundaryDefinitionId = defId,
                        CreatedBy = "azure-discovery"
                    });
                    componentsCreated++;
                }

                await context.SaveChangesAsync(ct);

                return Results.Ok(new ApplyDiscoveryResponse
                {
                    BoundariesCreated = boundariesCreated,
                    ComponentsCreated = componentsCreated,
                    Skipped = skipped
                });
            })
            .WithName("ApplyAzureDiscovery");

        return app;
    }
}
