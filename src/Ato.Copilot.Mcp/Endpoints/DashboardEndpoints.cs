using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Kanban;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Services;

using KanbanTaskStatus = Ato.Copilot.Core.Models.Kanban.TaskStatus;

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
                        Suggestion = "Use: AuthorizingOfficial, Issm, Isso, Sca, SystemOwner"
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
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var result = await compService.CreateComponentAsync(systemId, request, "system", ct);
                if (result is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "ComponentCreated",
                    Actor = "dashboard-user",
                    Summary = $"Component '{request.Name}' created (type: {request.ComponentType})",
                    RelatedEntityType = "SystemComponent",
                    RelatedEntityId = result.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/components/{result.Id}", result);
            })
            .WithName("CreateComponent");

        group.MapPut("/components/{id}", async (
                string id,
                CreateComponentRequest request,
                ComponentService compService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var result = await compService.UpdateComponentAsync(id, request, ct);
                if (result is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Component not found",
                        ErrorCode = "COMPONENT_NOT_FOUND",
                        Suggestion = "Check the component ID and try again",
                    });

                var comp = await context.SystemComponents.FindAsync([id], ct);
                if (comp != null)
                {
                    context.DashboardActivities.Add(new DashboardActivity
                    {
                        RegisteredSystemId = comp.RegisteredSystemId,
                        EventType = "ComponentUpdated",
                        Actor = "dashboard-user",
                        Summary = $"Component '{request.Name}' updated",
                        RelatedEntityType = "SystemComponent",
                        RelatedEntityId = id,
                    });
                    await context.SaveChangesAsync(ct);
                }

                return Results.Ok(result);
            })
            .WithName("UpdateComponent");

        group.MapDelete("/components/{id}", async (
                string id,
                ComponentService compService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var comp = await context.SystemComponents.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
                var result = await compService.DeleteComponentAsync(id, "system", ct);
                if (result is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Component not found",
                        ErrorCode = "COMPONENT_NOT_FOUND",
                        Suggestion = "Check the component ID and try again",
                    });

                if (comp != null)
                {
                    context.DashboardActivities.Add(new DashboardActivity
                    {
                        RegisteredSystemId = comp.RegisteredSystemId,
                        EventType = "ComponentDeleted",
                        Actor = "dashboard-user",
                        Summary = $"Component '{comp.Name}' deleted",
                        RelatedEntityType = "SystemComponent",
                        RelatedEntityId = id,
                    });
                    await context.SaveChangesAsync(ct);
                }

                return Results.Ok(result);
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
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var result = await service.CreateCapabilityAsync(request, "system", ct);
                if (result is null)
                    return Results.Conflict(new ErrorResponse
                    {
                        Error = "A capability with this name already exists",
                        ErrorCode = "CAPABILITY_NAME_DUPLICATE",
                        Suggestion = "Use a unique name or update the existing capability",
                    });

                context.DashboardActivities.Add(new DashboardActivity
                {
                    EventType = "CapabilityCreated",
                    Actor = "dashboard-user",
                    Summary = $"Security capability '{request.Name}' created",
                    RelatedEntityType = "SecurityCapability",
                    RelatedEntityId = result.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/capabilities/{result.Id}", result);
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
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await boundaryService.CreateAsync(systemId, request, "system", ct);

                    context.DashboardActivities.Add(new DashboardActivity
                    {
                        RegisteredSystemId = systemId,
                        EventType = "BoundaryCreated",
                        Actor = "dashboard-user",
                        Summary = $"Authorization boundary '{request.Name}' created",
                        RelatedEntityType = "AuthorizationBoundaryDefinition",
                        RelatedEntityId = result.Id,
                    });
                    await context.SaveChangesAsync(ct);

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

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = definition.RegisteredSystemId,
                    EventType = "BoundaryResourceAdded",
                    Actor = "dashboard-user",
                    Summary = $"Resource '{body.ResourceName ?? body.ResourceId}' added to boundary",
                    RelatedEntityType = "AuthorizationBoundary",
                    RelatedEntityId = id,
                });
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

                var def = await context.AuthorizationBoundaryDefinitions
                    .FirstOrDefaultAsync(d => d.Id == definitionId, ct);
                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = def?.RegisteredSystemId ?? "",
                    EventType = "BoundaryResourceRemoved",
                    Actor = "dashboard-user",
                    Summary = $"Resource '{entry.ResourceName ?? entry.ResourceId}' removed from boundary",
                    RelatedEntityType = "AuthorizationBoundary",
                    RelatedEntityId = resourceEntryId,
                });

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

                if (boundariesCreated > 0 || componentsCreated > 0)
                {
                    context.DashboardActivities.Add(new DashboardActivity
                    {
                        RegisteredSystemId = systemId,
                        EventType = "AzureResourcesImported",
                        Actor = "dashboard-user",
                        Summary = $"Azure discovery applied — {boundariesCreated} boundaries, {componentsCreated} components created",
                        RelatedEntityType = "RegisteredSystem",
                        RelatedEntityId = systemId,
                    });
                    await context.SaveChangesAsync(ct);
                }

                return Results.Ok(new ApplyDiscoveryResponse
                {
                    BoundariesCreated = boundariesCreated,
                    ComponentsCreated = componentsCreated,
                    Skipped = skipped
                });
            })
            .WithName("ApplyAzureDiscovery");

        // ─── Set Categorization ──────────────────────────────────────────────
        group.MapPost("/systems/{systemId}/categorization", async (
                string systemId,
                SetCategorizationRequest body,
                ICategorizationService categorizationService,
                ComplianceTrendSnapshotService trendSnapshotService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                if (body.InformationTypes is not { Count: > 0 })
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "At least one information type is required",
                        ErrorCode = "INVALID_INPUT"
                    });

                try
                {
                    var infoTypes = body.InformationTypes.Select(it => new InformationTypeInput
                    {
                        Sp80060Id = it.Sp80060Id,
                        Name = it.Name,
                        Category = it.Category,
                        ConfidentialityImpact = it.ConfidentialityImpact,
                        IntegrityImpact = it.IntegrityImpact,
                        AvailabilityImpact = it.AvailabilityImpact,
                        UsesProvisional = it.UsesProvisional,
                        AdjustmentJustification = it.AdjustmentJustification,
                    });

                    var result = await categorizationService.CategorizeSystemAsync(
                        systemId,
                        infoTypes,
                        "dashboard-user",
                        body.IsNationalSecuritySystem,
                        body.Justification,
                        ct);

                    context.DashboardActivities.Add(new DashboardActivity
                    {
                        RegisteredSystemId = systemId,
                        EventType = "CategorizationSet",
                        Actor = "dashboard-user",
                        Summary = $"Security categorization set to {result.OverallCategorization} (C:{result.ConfidentialityImpact} I:{result.IntegrityImpact} A:{result.AvailabilityImpact})",
                        RelatedEntityType = "SecurityCategorization",
                        RelatedEntityId = result.Id,
                    });
                    await context.SaveChangesAsync(ct);

                    try { await trendSnapshotService.CaptureSnapshotAsync(systemId, ct); }
                    catch { /* non-fatal */ }

                    return Results.Ok(new
                    {
                        id = result.Id,
                        overallCategorization = result.OverallCategorization.ToString(),
                        confidentialityImpact = result.ConfidentialityImpact.ToString(),
                        integrityImpact = result.IntegrityImpact.ToString(),
                        availabilityImpact = result.AvailabilityImpact.ToString(),
                        dodImpactLevel = result.DoDImpactLevel,
                        nistBaseline = result.NistBaseline,
                        informationTypeCount = result.InformationTypes.Count,
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
                }
            })
            .WithName("SetCategorization");

        // ─── Select Baseline ──────────────────────────────────────────────────
        group.MapPost("/systems/{systemId}/baseline", async (
                string systemId,
                SelectBaselineRequest body,
                IBaselineService baselineService,
                ComplianceTrendSnapshotService trendSnapshotService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                try
                {
                    var baseline = await baselineService.SelectBaselineAsync(
                        systemId,
                        applyOverlay: body.ApplyOverlay,
                        overlayName: body.OverlayName,
                        selectedBy: "dashboard-user",
                        cancellationToken: ct);

                    context.DashboardActivities.Add(new DashboardActivity
                    {
                        RegisteredSystemId = systemId,
                        EventType = "BaselineSelected",
                        Actor = "dashboard-user",
                        Summary = $"Control baseline selected: {baseline.BaselineLevel} ({baseline.TotalControls} controls)",
                        RelatedEntityType = "ControlBaseline",
                        RelatedEntityId = baseline.Id,
                    });
                    await context.SaveChangesAsync(ct);

                    try { await trendSnapshotService.CaptureSnapshotAsync(systemId, ct); }
                    catch { /* non-fatal */ }

                    return Results.Ok(new
                    {
                        baselineId = baseline.Id,
                        baselineLevel = baseline.BaselineLevel,
                        totalControls = baseline.TotalControls,
                        overlayApplied = baseline.OverlayApplied,
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
                }
            })
            .WithName("SelectBaseline");

        // ─── Advance RMF Step ────────────────────────────────────────────────
        group.MapPost("/systems/{systemId}/advance-rmf-step", async (
                string systemId,
                AdvanceRmfStepRequest body,
                IRmfLifecycleService lifecycleService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.TargetStep))
                    return Results.BadRequest(new ErrorResponse { Error = "targetStep is required", ErrorCode = "INVALID_INPUT" });

                if (!Enum.TryParse<RmfPhase>(body.TargetStep, true, out var targetStep))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = $"Invalid target step '{body.TargetStep}'",
                        ErrorCode = "INVALID_INPUT",
                        Suggestion = "Use: Prepare, Categorize, Select, Implement, Assess, Authorize, Monitor"
                    });

                var result = await lifecycleService.AdvanceRmfStepAsync(
                    systemId, targetStep, body.Force ?? false, "dashboard-user", ct);

                if (!result.Success)
                {
                    return Results.Json(new
                    {
                        success = false,
                        previousStep = result.PreviousStep.ToString(),
                        newStep = result.NewStep.ToString(),
                        error = result.ErrorMessage,
                        gateResults = result.GateResults.Select(g => new
                        {
                            gateName = g.GateName,
                            passed = g.Passed,
                            message = g.Message,
                            severity = g.Severity,
                        }),
                    }, statusCode: 422);
                }

                // Save failed gates as deferred prerequisites when force-advancing
                if (result.WasForced)
                {
                    var failedGates = result.GateResults.Where(g => !g.Passed).ToList();
                    foreach (var gate in failedGates)
                    {
                        context.DeferredPrerequisites.Add(new DeferredPrerequisite
                        {
                            RegisteredSystemId = systemId,
                            GateName = gate.GateName,
                            Message = gate.Message,
                            SkippedFromPhase = result.PreviousStep.ToString(),
                            AdvancedToPhase = result.NewStep.ToString(),
                            CreatedBy = "dashboard-user",
                        });
                    }
                    if (failedGates.Count > 0)
                        await context.SaveChangesAsync(ct);
                }

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "RmfPhaseAdvanced",
                    Actor = "dashboard-user",
                    Summary = $"RMF phase advanced from {result.PreviousStep} to {result.NewStep}{(result.WasForced ? " (force-advanced)" : "")}",
                    RelatedEntityType = "RegisteredSystem",
                    RelatedEntityId = systemId,
                });
                await context.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    success = true,
                    previousStep = result.PreviousStep.ToString(),
                    newStep = result.NewStep.ToString(),
                    wasForced = result.WasForced,
                    gateResults = result.GateResults.Select(g => new
                    {
                        gateName = g.GateName,
                        passed = g.Passed,
                        message = g.Message,
                        severity = g.Severity,
                    }),
                });
            })
            .WithName("AdvanceRmfStep");

        // ─── Phase Readiness Preflight ──────────────────────────────────────
        group.MapGet("/systems/{systemId}/phase-readiness", async (
                string systemId,
                IRmfLifecycleService lifecycleService,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var system = await db.RegisteredSystems
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == systemId, ct);

                if (system == null)
                    return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "NOT_FOUND" });

                var currentPhase = system.CurrentRmfStep.ToString();
                var phases = Enum.GetValues<RmfPhase>();
                var currentIdx = Array.IndexOf(phases, system.CurrentRmfStep);
                var nextPhase = currentIdx >= 0 && currentIdx < phases.Length - 1
                    ? phases[currentIdx + 1]
                    : (RmfPhase?)null;

                if (nextPhase == null)
                {
                    return Results.Ok(new
                    {
                        currentPhase,
                        nextPhase = (string?)null,
                        ready = true,
                        gateResults = Array.Empty<object>(),
                    });
                }

                var gates = await lifecycleService.CheckGateConditionsAsync(systemId, nextPhase.Value, ct);
                var allPassed = gates.All(g => g.Passed || g.Severity != "Error");

                return Results.Ok(new
                {
                    currentPhase,
                    nextPhase = nextPhase.Value.ToString(),
                    ready = allPassed,
                    gateResults = gates.Select(g => new
                    {
                        gateName = g.GateName,
                        passed = g.Passed,
                        message = g.Message,
                        severity = g.Severity,
                    }),
                });
            })
            .WithName("GetPhaseReadiness");

        // ─── Quick Action: Create PTA ──────────────────────────────────────
        group.MapPost("/systems/{systemId}/pta", async (
                string systemId,
                CreatePtaRequest body,
                IPrivacyService privacyService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var result = await privacyService.CreatePtaAsync(
                    systemId,
                    analyzedBy: "dashboard-user",
                    manualMode: true,
                    collectsPii: body.CollectsPii,
                    maintainsPii: body.MaintainsPii,
                    disseminatesPii: body.DisseminatesPii,
                    piiCategories: body.PiiCategories,
                    estimatedRecordCount: body.EstimatedRecordCount,
                    cancellationToken: ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "PtaCreated",
                    Actor = "dashboard-user",
                    Summary = $"Privacy Threshold Analysis completed — determination: {result.Determination}",
                    RelatedEntityType = "PrivacyThresholdAnalysis",
                    RelatedEntityId = result.PtaId,
                });
                await context.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    ptaId = result.PtaId,
                    determination = result.Determination.ToString(),
                    collectsPii = result.CollectsPii,
                    piiCategories = result.PiiCategories,
                    rationale = result.Rationale,
                });
            })
            .WithName("CreatePta");

        // ─── Quick Action: Add Interconnection ─────────────────────────────
        group.MapPost("/systems/{systemId}/interconnections", async (
                string systemId,
                AddInterconnectionRequest body,
                IInterconnectionService interconnectionService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                if (!Enum.TryParse<DataFlowDirection>(body.Direction, true, out var direction))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = $"Invalid direction '{body.Direction}'",
                        ErrorCode = "INVALID_INPUT",
                        Suggestion = "Use: Inbound, Outbound, Bidirectional"
                    });

                if (!Enum.TryParse<InterconnectionType>(body.Type, true, out var connType))
                    connType = InterconnectionType.Direct;

                var result = await interconnectionService.AddInterconnectionAsync(
                    systemId,
                    body.RemoteSystem,
                    connType,
                    direction,
                    body.DataClassification ?? "CUI",
                    createdBy: "dashboard-user",
                    protocolsUsed: string.IsNullOrWhiteSpace(body.Protocol) ? null : new List<string> { body.Protocol },
                    portsUsed: string.IsNullOrWhiteSpace(body.Port) ? null : new List<string> { body.Port },
                    cancellationToken: ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "InterconnectionAdded",
                    Actor = "dashboard-user",
                    Summary = $"Interconnection added to {result.TargetSystemName} ({body.Direction})",
                    RelatedEntityType = "SystemInterconnection",
                    RelatedEntityId = result.InterconnectionId,
                });
                await context.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    interconnectionId = result.InterconnectionId,
                    targetSystemName = result.TargetSystemName,
                    status = result.Status.ToString(),
                });
            })
            .WithName("AddInterconnection");

        // ─── Quick Action: Certify No Interconnections ─────────────────────
        group.MapPost("/systems/{systemId}/certify-no-interconnections", async (
                string systemId,
                IInterconnectionService interconnectionService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                await interconnectionService.CertifyNoInterconnectionsAsync(systemId, true, ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "NoInterconnectionsCertified",
                    Actor = "dashboard-user",
                    Summary = "Certified that system has no external interconnections",
                    RelatedEntityType = "RegisteredSystem",
                    RelatedEntityId = systemId,
                });
                await context.SaveChangesAsync(ct);

                return Results.Ok(new { certified = true });
            })
            .WithName("CertifyNoInterconnections");

        // ─── Quick Action: Generate & Approve PIA ─────────────────────────
        group.MapPost("/systems/{systemId}/generate-approve-pia", async (
                string systemId,
                IPrivacyService privacyService,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var piaResult = await privacyService.GeneratePiaAsync(
                    systemId,
                    createdBy: "dashboard-user",
                    cancellationToken: ct);

                var reviewResult = await privacyService.ReviewPiaAsync(
                    systemId,
                    PiaReviewDecision.Approved,
                    reviewerComments: "Approved via dashboard.",
                    reviewedBy: "dashboard-user",
                    cancellationToken: ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "PiaApproved",
                    Actor = "dashboard-user",
                    Summary = $"Privacy Impact Assessment generated and approved (expires {reviewResult.ExpirationDate:yyyy-MM-dd})",
                    RelatedEntityType = "PrivacyImpactAssessment",
                    RelatedEntityId = piaResult.PiaId,
                });
                await context.SaveChangesAsync(ct);

                return Results.Ok(new
                {
                    piaId = piaResult.PiaId,
                    status = reviewResult.NewStatus.ToString(),
                    expirationDate = reviewResult.ExpirationDate,
                });
            })
            .WithName("GenerateAndApprovePia");

        // ─── Document Catalog ──────────────────────────────────────────────
        group.MapGet("/systems/{systemId}/documents", async (
                string systemId,
                AtoCopilotContext context,
                CancellationToken ct) =>
            {
                var system = await context.RegisteredSystems
                    .Include(s => s.PrivacyThresholdAnalysis)
                    .Include(s => s.PrivacyImpactAssessment)
                    .Include(s => s.SystemInterconnections)
                    .FirstOrDefaultAsync(s => s.Id == systemId, ct);

                if (system is null)
                    return Results.NotFound(new ErrorResponse { Error = "System not found", ErrorCode = "NOT_FOUND" });

                // SSP narrative progress
                var totalNarratives = await context.ControlImplementations
                    .CountAsync(ci => ci.RegisteredSystemId == systemId, ct);
                var completedNarratives = await context.ControlImplementations
                    .CountAsync(ci => ci.RegisteredSystemId == systemId &&
                        ci.ImplementationStatus != ImplementationStatus.Planned &&
                        ci.Narrative != null && ci.Narrative != "", ct);
                var narrativePct = totalNarratives > 0 ? Math.Round((double)completedNarratives / totalNarratives * 100, 1) : 0;

                // SAP (latest)
                var sap = await context.SecurityAssessmentPlans
                    .Where(s => s.RegisteredSystemId == systemId)
                    .OrderByDescending(s => s.Status == SapStatus.Finalized ? 1 : 0)
                    .ThenByDescending(s => s.GeneratedAt)
                    .FirstOrDefaultAsync(ct);

                // Authorization decision (latest)
                var authDecision = await context.AuthorizationDecisions
                    .Where(d => d.RegisteredSystemId == systemId)
                    .OrderByDescending(d => d.DecisionDate)
                    .FirstOrDefaultAsync(ct);

                // POA&M
                var poamCount = await context.PoamItems
                    .CountAsync(p => p.RegisteredSystemId == systemId && p.Status != PoamStatus.Completed && p.Status != PoamStatus.RiskAccepted, ct);
                var poamOverdue = await context.PoamItems
                    .CountAsync(p => p.RegisteredSystemId == systemId &&
                        p.Status != PoamStatus.Completed && p.Status != PoamStatus.RiskAccepted &&
                        p.ScheduledCompletionDate < DateTime.UtcNow, ct);

                // Baseline
                var baseline = await context.ControlBaselines
                    .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, ct);

                // Interconnections with agreements
                var interconnections = await context.SystemInterconnections
                    .Where(ic => ic.RegisteredSystemId == systemId)
                    .ToListAsync(ct);
                var interconnectionIds = interconnections.Select(ic => ic.Id).ToList();
                var agreements = await context.InterconnectionAgreements
                    .Where(a => interconnectionIds.Contains(a.SystemInterconnectionId))
                    .ToListAsync(ct);

                // ConMon
                var conMonPlan = await context.ConMonPlans
                    .FirstOrDefaultAsync(p => p.RegisteredSystemId == systemId, ct);
                var conMonReportCount = await context.ConMonReports
                    .CountAsync(r => r.RegisteredSystemId == systemId, ct);
                var lastReport = await context.ConMonReports
                    .Where(r => r.RegisteredSystemId == systemId)
                    .OrderByDescending(r => r.GeneratedAt)
                    .FirstOrDefaultAsync(ct);

                // SSP Sections
                var sspSections = await context.SspSections
                    .Where(s => s.RegisteredSystemId == systemId)
                    .OrderBy(s => s.SectionNumber)
                    .ToListAsync(ct);

                // Narrative governance
                var narrativeStatuses = await context.ControlImplementations
                    .Where(ci => ci.RegisteredSystemId == systemId)
                    .Select(ci => ci.ApprovalStatus)
                    .ToListAsync(ct);

                // Scan imports
                var imports = await context.ScanImportRecords
                    .Where(i => i.RegisteredSystemId == systemId)
                    .OrderByDescending(i => i.ImportedAt)
                    .Take(20)
                    .ToListAsync(ct);

                // Inventory
                var inventoryCount = await context.InventoryItems
                    .CountAsync(i => i.RegisteredSystemId == systemId, ct);

                var now = DateTime.UtcNow;

                return Results.Ok(new SystemDocumentsResponse
                {
                    SystemId = systemId,
                    SystemName = system.Name,
                    CurrentPhase = system.CurrentRmfStep.ToString(),

                    Ssp = new SspDocumentInfo
                    {
                        NarrativeCompletionPct = narrativePct,
                        TotalNarratives = totalNarratives,
                        CompletedNarratives = completedNarratives,
                    },

                    Sap = sap is null ? null : new SapDocumentInfo
                    {
                        SapId = sap.Id,
                        Status = sap.Status.ToString(),
                        Title = sap.Title,
                        ContentHash = sap.ContentHash,
                        TotalControls = sap.TotalControls,
                        FinalizedAt = sap.Status == SapStatus.Finalized ? sap.GeneratedAt : null,
                        ScheduleStart = sap.ScheduleStart,
                        ScheduleEnd = sap.ScheduleEnd,
                    },

                    Authorization = authDecision is null ? null : new AuthDecisionInfo
                    {
                        DecisionId = authDecision.Id,
                        DecisionType = authDecision.DecisionType.ToString(),
                        DecisionDate = authDecision.DecisionDate,
                        ExpirationDate = authDecision.ExpirationDate,
                        ResidualRisk = authDecision.ResidualRiskLevel.ToString(),
                        IssuedBy = authDecision.IssuedBy,
                        DaysUntilExpiration = authDecision.ExpirationDate.HasValue
                            ? (int)(authDecision.ExpirationDate.Value - now).TotalDays
                            : null,
                    },

                    PoamCount = poamCount,
                    PoamOverdueCount = poamOverdue,
                    HasBaseline = baseline != null,
                    BaselineControlCount = baseline?.TotalControls ?? 0,

                    Pta = system.PrivacyThresholdAnalysis is null ? null : new PtaDocumentInfo
                    {
                        PtaId = system.PrivacyThresholdAnalysis.Id,
                        Determination = system.PrivacyThresholdAnalysis.Determination.ToString(),
                        CollectsPii = system.PrivacyThresholdAnalysis.CollectsPii,
                        PiiCategories = system.PrivacyThresholdAnalysis.PiiCategories,
                        AnalyzedAt = system.PrivacyThresholdAnalysis.AnalyzedAt,
                        AnalyzedBy = system.PrivacyThresholdAnalysis.AnalyzedBy,
                    },

                    Pia = system.PrivacyImpactAssessment is null ? null : new PiaDocumentInfo
                    {
                        PiaId = system.PrivacyImpactAssessment.Id,
                        Status = system.PrivacyImpactAssessment.Status.ToString(),
                        Version = system.PrivacyImpactAssessment.Version,
                        ApprovedBy = system.PrivacyImpactAssessment.ApprovedBy,
                        ApprovedAt = system.PrivacyImpactAssessment.ApprovedAt,
                        ExpirationDate = system.PrivacyImpactAssessment.ExpirationDate,
                        DaysUntilExpiration = system.PrivacyImpactAssessment.ExpirationDate.HasValue
                            ? (int)(system.PrivacyImpactAssessment.ExpirationDate.Value - now).TotalDays
                            : null,
                    },

                    Interconnections = interconnections.Select(ic =>
                    {
                        var agreement = agreements.FirstOrDefault(a =>
                            a.SystemInterconnectionId == ic.Id);
                        return new InterconnectionDocInfo
                        {
                            InterconnectionId = ic.Id,
                            TargetSystem = ic.TargetSystemName,
                            Direction = ic.DataFlowDirection.ToString(),
                            Status = ic.Status.ToString(),
                            HasAgreement = agreement != null,
                            AgreementType = agreement?.AgreementType.ToString(),
                            AgreementStatus = agreement?.Status.ToString(),
                        };
                    }).ToList(),

                    ConMon = conMonPlan is null ? null : new ConMonInfo
                    {
                        PlanId = conMonPlan.Id,
                        Frequency = conMonPlan.AssessmentFrequency,
                        ReportCount = conMonReportCount,
                        LastReportDate = lastReport?.GeneratedAt,
                    },

                    SspSections = sspSections.Select(s => new SspSectionInfo
                    {
                        SectionNumber = s.SectionNumber,
                        Title = s.SectionTitle,
                        Status = s.Status.ToString(),
                        AuthoredBy = s.AuthoredBy,
                        AuthoredAt = s.AuthoredAt,
                        ReviewedBy = s.ReviewedBy,
                        ReviewedAt = s.ReviewedAt,
                        Version = s.Version,
                    }).ToList(),

                    NarrativeGovernance = totalNarratives == 0 ? null : new NarrativeGovernanceInfo
                    {
                        TotalNarratives = totalNarratives,
                        Draft = narrativeStatuses.Count(s => s == SspSectionStatus.Draft || s == SspSectionStatus.NotStarted),
                        InReview = narrativeStatuses.Count(s => s == SspSectionStatus.UnderReview),
                        Approved = narrativeStatuses.Count(s => s == SspSectionStatus.Approved),
                        NeedsRevision = narrativeStatuses.Count(s => s == SspSectionStatus.NeedsRevision),
                        ApprovalPct = totalNarratives > 0
                            ? Math.Round((double)narrativeStatuses.Count(s => s == SspSectionStatus.Approved) / totalNarratives * 100, 1)
                            : 0,
                    },

                    ImportHistory = imports.Select(i => new ScanImportInfo
                    {
                        ImportId = i.Id,
                        ImportType = i.ImportType.ToString(),
                        FileName = i.FileName,
                        ImportedAt = i.ImportedAt,
                        TotalEntries = i.TotalEntries,
                        OpenCount = i.OpenCount,
                        PassCount = i.PassCount,
                        BenchmarkTitle = i.BenchmarkTitle,
                    }).ToList(),

                    InventoryItemCount = inventoryCount,
                });
            })
            .WithName("GetSystemDocuments");

        // ───────────── Assessments ────────────────────────────────────────────

        app.MapGet("/api/dashboard/assessments", async (
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var assessments = await context.Assessments
                .OrderByDescending(a => a.AssessedAt)
                .Take(100)
                .AsNoTracking()
                .ToListAsync(ct);

            var systemIds = assessments
                .Where(a => a.RegisteredSystemId != null)
                .Select(a => a.RegisteredSystemId!)
                .Distinct()
                .ToList();

            var systemNames = await context.RegisteredSystems
                .Where(s => systemIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .AsNoTracking()
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

            // Check which systems have categorization
            var categorizedSystemIds = await context.SecurityCategorizations
                .Where(sc => systemIds.Contains(sc.RegisteredSystemId))
                .Select(sc => sc.RegisteredSystemId)
                .AsNoTracking()
                .ToListAsync(ct);

            var findingCounts = await context.Findings
                .Where(f => assessments.Select(a => a.Id).Contains(f.AssessmentId))
                .GroupBy(f => f.AssessmentId)
                .Select(g => new { AssessmentId = g.Key, Count = g.Count() })
                .AsNoTracking()
                .ToDictionaryAsync(x => x.AssessmentId, x => x.Count, ct);

            var items = assessments.Select(a => new AssessmentListItemDto
            {
                AssessmentId = a.Id,
                SystemId = a.RegisteredSystemId,
                SystemName = a.RegisteredSystemId != null && systemNames.TryGetValue(a.RegisteredSystemId, out var name) ? name : null,
                Framework = a.Framework,
                Status = a.Status.ToString(),
                ScanType = a.ScanType,
                ComplianceScore = Math.Round(a.ComplianceScore, 1),
                TotalControls = a.TotalControls,
                PassedControls = a.PassedControls,
                FailedControls = a.FailedControls,
                TotalFindings = findingCounts.GetValueOrDefault(a.Id, 0),
                AssessedAt = a.AssessedAt,
                InitiatedBy = a.InitiatedBy,
                HasCategorization = a.RegisteredSystemId != null && categorizedSystemIds.Contains(a.RegisteredSystemId),
            }).ToList();

            return Results.Ok(items);
        })
        .WithName("ListAssessments");

        app.MapGet("/api/dashboard/assessments/{assessmentId}", async (
            string assessmentId,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var assessment = await context.Assessments
                .Include(a => a.Findings)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == assessmentId, ct);
            if (assessment is null)
                return Results.NotFound(new { error = "Assessment not found" });

            string? systemName = null;
            if (assessment.RegisteredSystemId is not null)
            {
                systemName = await context.RegisteredSystems
                    .Where(s => s.Id == assessment.RegisteredSystemId)
                    .Select(s => s.Name)
                    .FirstOrDefaultAsync(ct);
            }

            // Build per-family breakdown from stored ControlFamilyResults or derive from findings
            var familyResults = assessment.ControlFamilyResults is { Count: > 0 }
                ? assessment.ControlFamilyResults.Select(f => new AssessmentFamilyDto
                {
                    FamilyCode = f.FamilyCode,
                    FamilyName = f.FamilyName,
                    TotalControls = f.TotalControls,
                    PassedControls = f.PassedControls,
                    FailedControls = f.FailedControls,
                    ComplianceScore = Math.Round(f.ComplianceScore, 1),
                }).ToList()
                : assessment.Findings
                    .GroupBy(f => f.ControlId?.Split('-').FirstOrDefault() ?? "Unknown")
                    .Select(g => new AssessmentFamilyDto
                    {
                        FamilyCode = g.Key,
                        FamilyName = g.Key,
                        TotalControls = 0,
                        PassedControls = 0,
                        FailedControls = g.Count(),
                        ComplianceScore = 0,
                    }).ToList();

            var findingDtos = assessment.Findings
                .OrderBy(f => f.ControlId)
                .Select(f => new AssessmentFindingDto
                {
                    FindingId = f.Id,
                    ControlId = f.ControlId,
                    ControlFamily = f.ControlId?.Split('-').FirstOrDefault() ?? "",
                    Title = f.Title,
                    Description = f.Description,
                    Severity = f.Severity.ToString(),
                    Status = f.Status.ToString(),
                    ResourceType = f.ResourceType,
                    ResourceId = f.ResourceId,
                    RemediationGuidance = f.RemediationGuidance,
                    DiscoveredAt = f.DiscoveredAt,
                }).ToList();

            // Compute severity counts
            int criticalCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.Critical);
            int highCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.High);
            int mediumCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.Medium);
            int lowCount = assessment.Findings.Count(f => f.Severity == FindingSeverity.Low);

            return Results.Ok(new AssessmentDetailDto
            {
                AssessmentId = assessment.Id,
                SystemId = assessment.RegisteredSystemId,
                SystemName = systemName,
                Framework = assessment.Framework,
                ScanType = assessment.ScanType,
                Status = assessment.Status.ToString(),
                ComplianceScore = Math.Round(assessment.ComplianceScore, 1),
                TotalControls = assessment.TotalControls,
                PassedControls = assessment.PassedControls,
                FailedControls = assessment.FailedControls,
                NotAssessedControls = assessment.NotAssessedControls,
                AssessedAt = assessment.AssessedAt,
                CompletedAt = assessment.CompletedAt,
                InitiatedBy = assessment.InitiatedBy,
                ExecutiveSummary = assessment.ExecutiveSummary,
                CriticalCount = criticalCount,
                HighCount = highCount,
                MediumCount = mediumCount,
                LowCount = lowCount,
                FamilyResults = familyResults,
                Findings = findingDtos,
            });
        })
        .WithName("GetAssessmentDetail");

        app.MapPost("/api/dashboard/systems/{systemId}/run-assessment", async (
            string systemId,
            IAtoComplianceEngine complianceEngine,
            ComplianceTrendSnapshotService trendSnapshotService,
            IAuthorizationService authorizationService,
            IKanbanService kanbanService,
            IRemediationEngine remediationEngine,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var system = await context.RegisteredSystems
                .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, ct);
            if (system is null)
                return Results.NotFound(new { error = "System not found" });

            var hasCategorization = await context.SecurityCategorizations
                .AnyAsync(sc => sc.RegisteredSystemId == systemId, ct);
            if (!hasCategorization)
                return Results.BadRequest(new { error = "System must be categorized before running an assessment." });

            var subscriptionId = system.AzureProfile?.SubscriptionIds.FirstOrDefault();

            ComplianceAssessment assessment;

            if (!string.IsNullOrWhiteSpace(subscriptionId))
            {
                // Use the real compliance engine (same as chat) when Azure subscription exists
                assessment = await complianceEngine.RunComprehensiveAssessmentAsync(
                    subscriptionId, resourceGroup: null, progress: null, cancellationToken: ct);
                assessment.RegisteredSystemId = systemId;
                assessment.InitiatedBy = "dashboard-user";

                // The engine persists assessment via its own DbContext, so update
                // RegisteredSystemId and InitiatedBy in our context
                var existingAssessment = await context.Assessments
                    .FirstOrDefaultAsync(a => a.Id == assessment.Id, ct);
                if (existingAssessment is not null)
                {
                    existingAssessment.RegisteredSystemId = systemId;
                    existingAssessment.InitiatedBy = "dashboard-user";
                    await context.SaveChangesAsync(ct);
                }

                // Create ControlEffectiveness records so the heatmap updates
                var failedControlIds = new HashSet<string>(
                    assessment.Findings.Select(f => f.ControlId).Where(id => id != null)!,
                    StringComparer.OrdinalIgnoreCase);

                var baseline = await context.ControlBaselines
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, ct);

                if (baseline is not null)
                {
                    var azEffRecords = new List<ControlEffectiveness>();
                    foreach (var controlId in baseline.ControlIds)
                    {
                        var failed = failedControlIds.Contains(controlId);
                        var finding = failed
                            ? assessment.Findings.FirstOrDefault(f =>
                                string.Equals(f.ControlId, controlId, StringComparison.OrdinalIgnoreCase))
                            : null;

                        azEffRecords.Add(new ControlEffectiveness
                        {
                            AssessmentId = assessment.Id,
                            RegisteredSystemId = systemId,
                            ControlId = controlId,
                            Determination = failed
                                ? EffectivenessDetermination.OtherThanSatisfied
                                : EffectivenessDetermination.Satisfied,
                            AssessmentMethod = "Examine",
                            AssessorId = "dashboard-user",
                            AssessedAt = DateTime.UtcNow,
                            CatSeverity = failed && finding?.CatSeverity != null
                                ? finding.CatSeverity
                                : (failed ? Ato.Copilot.Core.Models.Compliance.CatSeverity.CatII : null),
                        });
                    }
                    context.ControlEffectivenessRecords.AddRange(azEffRecords);
                    await context.SaveChangesAsync(ct);
                }
            }
            else
            {
                // Fallback: evaluate control implementations against baseline
                var baseline = await context.ControlBaselines
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, ct);
                if (baseline is null)
                    return Results.BadRequest(new { error = "System must have a control baseline selected before running an assessment." });

                var implementations = await context.ControlImplementations
                    .Where(ci => ci.RegisteredSystemId == systemId)
                    .ToListAsync(ct);

                var implByControl = implementations.ToDictionary(ci => ci.ControlId, StringComparer.OrdinalIgnoreCase);

                // Load valid NIST control IDs to avoid FK violations on Findings
                var validControlIds = await context.NistControls
                    .Select(nc => nc.Id)
                    .AsNoTracking()
                    .ToListAsync(ct);
                var validSet = new HashSet<string>(validControlIds, StringComparer.OrdinalIgnoreCase);

                int totalControls = baseline.ControlIds.Count;
                int passedControls = 0;
                int failedControls = 0;
                var findings = new List<ComplianceFinding>();

                // Evaluate each control and update implementation status based on narrative content
                foreach (var controlId in baseline.ControlIds)
                {
                    if (implByControl.TryGetValue(controlId, out var impl))
                    {
                        // Skip controls already marked NotApplicable
                        if (impl.ImplementationStatus == ImplementationStatus.NotApplicable)
                        {
                            passedControls++;
                            continue;
                        }

                        bool hasNarrative = !string.IsNullOrWhiteSpace(impl.Narrative);
                        bool isReviewed = impl.ReviewedBy is not null;

                        if (hasNarrative && (isReviewed || !impl.AiSuggested))
                        {
                            // Reviewed narrative or manually-authored → Implemented
                            impl.ImplementationStatus = ImplementationStatus.Implemented;
                            passedControls++;
                        }
                        else if (hasNarrative)
                        {
                            // AI-generated narrative not yet reviewed → PartiallyImplemented
                            impl.ImplementationStatus = ImplementationStatus.PartiallyImplemented;
                            failedControls++;
                            findings.Add(new ComplianceFinding
                            {
                                AssessmentId = "",
                                ControlId = controlId,
                                Title = $"Control {controlId} pending review",
                                Description = $"Control {controlId} has an auto-generated narrative that has not been reviewed. Mark as reviewed to achieve full compliance.",
                                Severity = FindingSeverity.Medium,
                                CatSeverity = Ato.Copilot.Core.Models.Compliance.CatSeverity.CatII,
                                Status = FindingStatus.Open,
                                ResourceType = "ControlImplementation",
                                ResourceId = controlId,
                                DiscoveredAt = DateTime.UtcNow,
                            });
                        }
                        else
                        {
                            // No narrative → stays Planned
                            impl.ImplementationStatus = ImplementationStatus.Planned;
                            failedControls++;
                            findings.Add(new ComplianceFinding
                            {
                                AssessmentId = "",
                                ControlId = controlId,
                                Title = $"Control {controlId} not implemented",
                                Description = $"Control {controlId} has no implementation narrative. Add a narrative to demonstrate compliance.",
                                Severity = FindingSeverity.High,
                                CatSeverity = Ato.Copilot.Core.Models.Compliance.CatSeverity.CatI,
                                Status = FindingStatus.Open,
                                ResourceType = "ControlImplementation",
                                ResourceId = controlId,
                                DiscoveredAt = DateTime.UtcNow,
                            });
                        }
                    }
                    else
                    {
                        // No implementation record at all
                        failedControls++;
                        findings.Add(new ComplianceFinding
                        {
                            AssessmentId = "",
                            ControlId = controlId,
                            Title = $"Control {controlId} not implemented",
                            Description = $"No control implementation record exists for {controlId}.",
                            Severity = FindingSeverity.High,
                            CatSeverity = Ato.Copilot.Core.Models.Compliance.CatSeverity.CatI,
                            Status = FindingStatus.Open,
                            ResourceType = "ControlImplementation",
                            ResourceId = controlId,
                            DiscoveredAt = DateTime.UtcNow,
                        });
                    }
                }

                // Persist updated implementation statuses
                await context.SaveChangesAsync(ct);

                // Build per-family breakdown
                var familyStats = new Dictionary<string, (int Total, int Passed, int Failed)>(StringComparer.OrdinalIgnoreCase);
                foreach (var controlId in baseline.ControlIds)
                {
                    var family = controlId.Contains('-') ? controlId[..controlId.IndexOf('-')] : controlId;
                    if (!familyStats.ContainsKey(family))
                        familyStats[family] = (0, 0, 0);
                    var s = familyStats[family];
                    bool passed = implByControl.TryGetValue(controlId, out var ci) &&
                        ci.ImplementationStatus is ImplementationStatus.Implemented or ImplementationStatus.NotApplicable;
                    familyStats[family] = (s.Total + 1, s.Passed + (passed ? 1 : 0), s.Failed + (passed ? 0 : 1));
                }

                var familyResults = familyStats
                    .OrderBy(kvp => kvp.Key)
                    .Select(kvp => new ControlFamilyAssessment
                    {
                        FamilyCode = kvp.Key,
                        FamilyName = ControlFamilies.FamilyNames.GetValueOrDefault(kvp.Key, kvp.Key),
                        TotalControls = kvp.Value.Total,
                        PassedControls = kvp.Value.Passed,
                        FailedControls = kvp.Value.Failed,
                        ComplianceScore = kvp.Value.Total > 0
                            ? Math.Round((double)kvp.Value.Passed / kvp.Value.Total * 100, 1)
                            : 0,
                        Status = FamilyAssessmentStatus.Completed,
                    }).ToList();

                assessment = new ComplianceAssessment
                {
                    SubscriptionId = "",
                    Framework = "NIST 800-53",
                    ScanType = "combined",
                    Status = AssessmentStatus.Completed,
                    InitiatedBy = "dashboard-user",
                    AssessedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    RegisteredSystemId = systemId,
                    ComplianceScore = totalControls > 0
                        ? Math.Round((double)passedControls / totalControls * 100, 1)
                        : 0,
                    TotalControls = totalControls,
                    PassedControls = passedControls,
                    FailedControls = failedControls,
                    ControlFamilyResults = familyResults,
                };

                // Persist findings
                context.Assessments.Add(assessment);
                await context.SaveChangesAsync(ct);

                foreach (var finding in findings)
                    finding.AssessmentId = assessment.Id;

                // Only persist findings whose ControlId exists in NistControls (FK constraint)
                var persistableFindings = findings.Where(f => validSet.Contains(f.ControlId)).ToList();
                if (persistableFindings.Count > 0)
                {
                    context.Findings.AddRange(persistableFindings);
                    await context.SaveChangesAsync(ct);
                }

                // Create ControlEffectiveness records so the heatmap updates
                var effectivenessRecords = new List<ControlEffectiveness>();
                foreach (var controlId in baseline.ControlIds)
                {
                    var passed = implByControl.TryGetValue(controlId, out var ci) &&
                        ci.ImplementationStatus is ImplementationStatus.Implemented or ImplementationStatus.NotApplicable;
                    effectivenessRecords.Add(new ControlEffectiveness
                    {
                        AssessmentId = assessment.Id,
                        RegisteredSystemId = systemId,
                        ControlId = controlId,
                        Determination = passed
                            ? EffectivenessDetermination.Satisfied
                            : EffectivenessDetermination.OtherThanSatisfied,
                        AssessmentMethod = "Examine",
                        AssessorId = "dashboard-user",
                        AssessedAt = DateTime.UtcNow,
                        CatSeverity = passed ? null
                            : (implByControl.TryGetValue(controlId, out var imp) && imp.ImplementationStatus == ImplementationStatus.PartiallyImplemented
                                ? Ato.Copilot.Core.Models.Compliance.CatSeverity.CatII
                                : Ato.Copilot.Core.Models.Compliance.CatSeverity.CatI),
                    });
                }
                context.ControlEffectivenessRecords.AddRange(effectivenessRecords);
                await context.SaveChangesAsync(ct);

                assessment.Findings = findings;
            }

            // Log activity
            context.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "AssessmentCompleted",
                Actor = assessment.InitiatedBy ?? "dashboard-user",
                Summary = $"Compliance assessment completed — score {assessment.ComplianceScore:F1}%, {assessment.Findings.Count} findings ({assessment.PassedControls}/{assessment.TotalControls} controls passed)",
                RelatedEntityType = "ComplianceAssessment",
                RelatedEntityId = assessment.Id,
            });
            await context.SaveChangesAsync(ct);

            // Capture a trend snapshot after assessment completes
            try { await trendSnapshotService.CaptureSnapshotAsync(systemId, ct); }
            catch { /* non-fatal */ }

            // ─── Auto-create POA&M items from open findings ──────────────────
            var poamCreated = 0;
            var openFindings = assessment.Findings
                .Where(f => f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress)
                .ToList();

            foreach (var finding in openFindings)
            {
                try
                {
                    var severity = finding.CatSeverity ?? (finding.Severity switch
                    {
                        FindingSeverity.Critical or FindingSeverity.High => Ato.Copilot.Core.Models.Compliance.CatSeverity.CatI,
                        FindingSeverity.Medium => Ato.Copilot.Core.Models.Compliance.CatSeverity.CatII,
                        _ => Ato.Copilot.Core.Models.Compliance.CatSeverity.CatIII,
                    });

                    var dueDate = severity switch
                    {
                        Ato.Copilot.Core.Models.Compliance.CatSeverity.CatI => DateTime.UtcNow.AddDays(30),
                        Ato.Copilot.Core.Models.Compliance.CatSeverity.CatII => DateTime.UtcNow.AddDays(90),
                        _ => DateTime.UtcNow.AddDays(180),
                    };

                    var poam = await authorizationService.CreatePoamAsync(
                        systemId,
                        finding.Title ?? finding.Description ?? $"Finding for {finding.ControlId}",
                        finding.ControlId ?? "Unknown",
                        severity.ToString(),
                        "dashboard-user",
                        dueDate,
                        finding.Id,
                        finding.RemediationGuidance,
                        cancellationToken: ct);
                    poamCreated++;
                }
                catch { /* non-fatal — continue creating remaining POA&M items */ }
            }

            // ─── Auto-create Kanban remediation board from assessment ─────────
            string? boardId = null;
            var kanbanTaskCount = 0;
            try
            {
                var board = await kanbanService.CreateBoardFromAssessmentAsync(
                    assessment.Id,
                    $"{system.Name} — Assessment {DateTime.UtcNow:yyyy-MM-dd}",
                    system.AzureProfile?.SubscriptionIds.FirstOrDefault() ?? systemId,
                    assessment.InitiatedBy ?? "dashboard-user",
                    ct);
                boardId = board.Id;
                kanbanTaskCount = board.Tasks.Count;

                // Link POA&M items to kanban tasks via FindingId
                var poamItems = await context.PoamItems
                    .Where(p => p.RegisteredSystemId == systemId && p.FindingId != null)
                    .ToListAsync(ct);
                var tasksByFinding = board.Tasks
                    .Where(t => t.FindingId != null)
                    .ToDictionary(t => t.FindingId!, t => t.Id);

                foreach (var poam in poamItems)
                {
                    if (poam.FindingId != null && tasksByFinding.TryGetValue(poam.FindingId, out var taskId))
                        poam.RemediationTaskId = taskId;
                }
                await context.SaveChangesAsync(ct);
            }
            catch { /* non-fatal — board creation failure doesn't block assessment */ }

            // ─── Auto-generate remediation plan ──────────────────────────────
            string? remediationPlanId = null;
            try
            {
                var plan = await remediationEngine.GenerateRemediationPlanAsync(
                    openFindings,
                    null,
                    ct);
                remediationPlanId = plan.Id;
            }
            catch { /* non-fatal */ }

            return Results.Ok(new
            {
                assessmentId = assessment.Id,
                status = assessment.Status.ToString(),
                systemId,
                scanType = assessment.ScanType,
                complianceScore = assessment.ComplianceScore,
                totalControls = assessment.TotalControls,
                passedControls = assessment.PassedControls,
                failedControls = assessment.FailedControls,
                totalFindings = assessment.Findings.Count,
                poamItemsCreated = poamCreated,
                remediationBoardId = boardId,
                remediationTaskCount = kanbanTaskCount,
                remediationPlanId,
            });
        })
        .WithName("RunAssessment");

        // ───────────── Narratives ─────────────────────────────────────────────

        app.MapGet("/api/dashboard/systems/{systemId}/narratives", async (
            string systemId,
            string? family,
            string? status,
            string? search,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var query = context.ControlImplementations
                .Where(ci => ci.RegisteredSystemId == systemId)
                .AsNoTracking();

            if (!string.IsNullOrEmpty(family))
                query = query.Where(ci => ci.ControlId.StartsWith(family));

            if (!string.IsNullOrEmpty(status))
            {
                if (Enum.TryParse<ImplementationStatus>(status, true, out var implStatus))
                    query = query.Where(ci => ci.ImplementationStatus == implStatus);
            }

            if (!string.IsNullOrEmpty(search))
                query = query.Where(ci => ci.ControlId.Contains(search) ||
                    (ci.Narrative != null && ci.Narrative.Contains(search)));

            var items = await query
                .OrderBy(ci => ci.ControlId)
                .Select(ci => new NarrativeListItemDto
                {
                    Id = ci.Id,
                    ControlId = ci.ControlId,
                    Family = ci.ControlId.Length >= 2 ? ci.ControlId.Substring(0, ci.ControlId.IndexOf('-') > 0 ? ci.ControlId.IndexOf('-') : 2) : ci.ControlId,
                    Narrative = ci.Narrative,
                    ImplementationStatus = ci.ImplementationStatus.ToString(),
                    ApprovalStatus = ci.ApprovalStatus.ToString(),
                    AuthoredBy = ci.AuthoredBy,
                    AuthoredAt = ci.AuthoredAt,
                    Version = ci.CurrentVersion,
                    IsAutoPopulated = ci.IsAutoPopulated,
                    AiSuggested = ci.AiSuggested,
                })
                .ToListAsync(ct);

            return Results.Ok(items);
        })
        .WithName("ListNarratives");

        app.MapPut("/api/dashboard/systems/{systemId}/narratives/bulk-update", async (
            string systemId,
            BulkNarrativeUpdateRequest request,
            ComplianceTrendSnapshotService trendSnapshotService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var narratives = await context.ControlImplementations
                .Where(ci => ci.RegisteredSystemId == systemId &&
                    request.ControlIds.Contains(ci.ControlId))
                .ToListAsync(ct);

            if (narratives.Count == 0)
                return Results.NotFound(new { error = "No matching narratives found" });

            var updatedBy = request.UpdatedBy ?? "dashboard-user";
            var now = DateTime.UtcNow;

            foreach (var ci in narratives)
            {
                if (!string.IsNullOrEmpty(request.ImplementationStatus) &&
                    Enum.TryParse<ImplementationStatus>(request.ImplementationStatus, true, out var newStatus))
                {
                    ci.ImplementationStatus = newStatus;
                }

                if (!string.IsNullOrEmpty(request.ApprovalStatus) &&
                    Enum.TryParse<SspSectionStatus>(request.ApprovalStatus, true, out var newApproval))
                {
                    ci.ApprovalStatus = newApproval;
                }

                ci.ModifiedAt = now;
            }

            context.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "NarrativesUpdated",
                Actor = updatedBy,
                Summary = $"Bulk updated {narratives.Count} narratives",
                RelatedEntityType = "ControlImplementation",
                RelatedEntityId = systemId,
            });
            await context.SaveChangesAsync(ct);

            try { await trendSnapshotService.CaptureSnapshotAsync(systemId, ct); }
            catch { /* non-fatal */ }

            return Results.Ok(new { updatedCount = narratives.Count, controlIds = narratives.Select(n => n.ControlId).ToList() });
        })
        .WithName("BulkUpdateNarratives");

        // ───────────── Deferred Prerequisites ─────────────────────────────────

        app.MapPost("/api/dashboard/systems/{systemId}/deferred-prerequisites/{id}/resolve", async (
            string systemId,
            string id,
            AtoCopilotContext context,
            IRmfLifecycleService lifecycleService,
            CancellationToken ct) =>
        {
            var item = await context.DeferredPrerequisites
                .FirstOrDefaultAsync(d => d.Id == id && d.RegisteredSystemId == systemId, ct);

            if (item is null)
                return Results.NotFound(new { error = "Deferred prerequisite not found" });

            if (item.IsResolved)
                return Results.Ok(new { id = item.Id, alreadyResolved = true });

            // Verify the gate is actually satisfied before allowing resolution
            if (Enum.TryParse<RmfPhase>(item.AdvancedToPhase, true, out var targetPhase))
            {
                try
                {
                    var gates = await lifecycleService.CheckGateConditionsAsync(systemId, targetPhase, ct);
                    var matchingGate = gates.FirstOrDefault(g =>
                        g.GateName.Equals(item.GateName, StringComparison.OrdinalIgnoreCase));

                    if (matchingGate is not null && !matchingGate.Passed)
                    {
                        // Gate still failing — determine an action link based on gate name
                        var gateLower = item.GateName.ToLowerInvariant();
                        string actionLink = $"/systems/{systemId}";
                        string actionLabel = "Go to System";

                        if (gateLower.Contains("categorization") || gateLower.Contains("information type"))
                        {
                            actionLabel = "Set Categorization in Phase Readiness";
                        }
                        else if (gateLower.Contains("privacy"))
                        {
                            actionLabel = "Create PTA in Phase Readiness";
                        }
                        else if (gateLower.Contains("boundary"))
                        {
                            actionLink = $"/systems/{systemId}/boundaries";
                            actionLabel = "Manage Boundaries";
                        }
                        else if (gateLower.Contains("interconnection"))
                        {
                            actionLabel = "Add Interconnection in Phase Readiness";
                        }
                        else if (gateLower.Contains("role"))
                        {
                            actionLabel = "Assign Roles";
                        }
                        else if (gateLower.Contains("baseline"))
                        {
                            actionLink = $"/systems/{systemId}/gaps";
                            actionLabel = "Select Baseline";
                        }
                        else if (gateLower.Contains("narrative"))
                        {
                            actionLink = $"/systems/{systemId}/narratives";
                            actionLabel = "Write Narratives";
                        }

                        return Results.Json(new
                        {
                            resolved = false,
                            gateName = item.GateName,
                            message = matchingGate.Message,
                            severity = matchingGate.Severity,
                            actionLink,
                            actionLabel,
                        }, statusCode: 422);
                    }
                }
                catch
                {
                    // If gate check fails, still allow manual resolution
                }
            }

            item.IsResolved = true;
            item.ResolvedAt = DateTime.UtcNow;
            item.ResolvedBy = "dashboard-user";
            await context.SaveChangesAsync(ct);

            return Results.Ok(new { id = item.Id, resolved = true });
        })
        .WithName("ResolveDeferredPrerequisite");

        // ───────────── Authorization & Monitor Phase Endpoints ────────────────

        // ─── Issue Authorization Decision (ATO/ATOwC/IATT/DATO) ─────────────
        app.MapPost("/api/dashboard/systems/{systemId}/authorization", async (
            string systemId,
            IssueAuthorizationRequest body,
            IAuthorizationService authorizationService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            try
            {
                var decision = await authorizationService.IssueAuthorizationAsync(
                    systemId,
                    body.DecisionType,
                    body.ExpirationDate,
                    body.ResidualRiskLevel ?? "Medium",
                    body.TermsAndConditions,
                    body.ResidualRiskJustification,
                    body.RiskAcceptances,
                    body.IssuedBy ?? "dashboard-user",
                    body.IssuedByName ?? "Dashboard User",
                    ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "AuthorizationIssued",
                    Actor = body.IssuedBy ?? "dashboard-user",
                    Summary = $"Authorization decision issued: {decision.DecisionType} (expires {decision.ExpirationDate:yyyy-MM-dd})",
                    RelatedEntityType = "AuthorizationDecision",
                    RelatedEntityId = decision.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/systems/{systemId}/authorization/{decision.Id}", new
                {
                    id = decision.Id,
                    decisionType = decision.DecisionType.ToString(),
                    expirationDate = decision.ExpirationDate,
                    residualRiskLevel = decision.ResidualRiskLevel.ToString(),
                    issuedBy = decision.IssuedBy,
                    issuedAt = decision.DecisionDate,
                    riskAcceptanceCount = decision.RiskAcceptances.Count,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
            }
        })
        .WithName("IssueAuthorization");

        // ─── List POA&M Items ────────────────────────────────────────────────
        app.MapGet("/api/dashboard/systems/{systemId}/poam", async (
            string systemId,
            string? status,
            string? severity,
            bool? overdueOnly,
            IAuthorizationService authorizationService,
            CancellationToken ct) =>
        {
            var items = await authorizationService.ListPoamAsync(
                systemId, status, severity, overdueOnly ?? false, ct);

            return Results.Ok(new
            {
                items = items.Select(p => new
                {
                    p.Id,
                    p.Weakness,
                    p.WeaknessSource,
                    controlId = p.SecurityControlNumber,
                    catSeverity = p.CatSeverity.ToString(),
                    p.PointOfContact,
                    p.PocEmail,
                    p.ResourcesRequired,
                    p.CostEstimate,
                    p.ScheduledCompletionDate,
                    p.ActualCompletionDate,
                    status = p.Status.ToString(),
                    p.Comments,
                    p.FindingId,
                    p.RemediationTaskId,
                    isOverdue = p.Status != PoamStatus.Completed &&
                                p.Status != PoamStatus.RiskAccepted &&
                                p.ScheduledCompletionDate < DateTime.UtcNow,
                    milestones = p.Milestones.Select(m => new
                    {
                        m.Id,
                        m.Description,
                        m.TargetDate,
                        m.CompletedDate,
                        m.Sequence,
                    }),
                }),
                totalCount = items.Count,
            });
        })
        .WithName("ListPoamItems");

        // ─── Create POA&M Item ───────────────────────────────────────────────
        app.MapPost("/api/dashboard/systems/{systemId}/poam", async (
            string systemId,
            CreatePoamRequest body,
            IAuthorizationService authorizationService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            try
            {
                var poam = await authorizationService.CreatePoamAsync(
                    systemId,
                    body.Weakness,
                    body.ControlId,
                    body.CatSeverity,
                    body.PointOfContact,
                    body.ScheduledCompletionDate,
                    body.FindingId,
                    body.ResourcesRequired,
                    body.Milestones,
                    ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "PoamCreated",
                    Actor = "dashboard-user",
                    Summary = $"POA&M item created for {body.ControlId} ({body.CatSeverity})",
                    RelatedEntityType = "PoamItem",
                    RelatedEntityId = poam.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/systems/{systemId}/poam/{poam.Id}", new
                {
                    id = poam.Id,
                    controlId = poam.SecurityControlNumber,
                    catSeverity = poam.CatSeverity.ToString(),
                    status = poam.Status.ToString(),
                    scheduledCompletionDate = poam.ScheduledCompletionDate,
                    milestoneCount = poam.Milestones.Count,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
            }
        })
        .WithName("CreatePoamItem");

        // ─── Accept Risk ─────────────────────────────────────────────────────
        app.MapPost("/api/dashboard/systems/{systemId}/risk-acceptances", async (
            string systemId,
            AcceptRiskRequest body,
            IAuthorizationService authorizationService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            try
            {
                var risk = await authorizationService.AcceptRiskAsync(
                    systemId,
                    body.FindingId,
                    body.ControlId,
                    body.CatSeverity,
                    body.Justification,
                    body.ExpirationDate,
                    body.CompensatingControl,
                    body.AcceptedBy ?? "dashboard-user",
                    ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "RiskAccepted",
                    Actor = body.AcceptedBy ?? "dashboard-user",
                    Summary = $"Risk accepted for {body.ControlId} ({body.CatSeverity}) — expires {body.ExpirationDate:yyyy-MM-dd}",
                    RelatedEntityType = "RiskAcceptance",
                    RelatedEntityId = risk.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/systems/{systemId}/risk-acceptances/{risk.Id}", new
                {
                    id = risk.Id,
                    controlId = risk.ControlId,
                    catSeverity = risk.CatSeverity.ToString(),
                    expirationDate = risk.ExpirationDate,
                    isActive = risk.IsActive,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
            }
        })
        .WithName("AcceptRisk");

        // ─── Create ConMon Plan ──────────────────────────────────────────────
        app.MapPost("/api/dashboard/systems/{systemId}/conmon-plan", async (
            string systemId,
            CreateConMonPlanRequest body,
            IConMonService conMonService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            try
            {
                var plan = await conMonService.CreatePlanAsync(
                    systemId,
                    body.AssessmentFrequency ?? "Monthly",
                    body.AnnualReviewDate ?? DateTime.UtcNow.AddYears(1),
                    body.ReportDistribution,
                    body.SignificantChangeTriggers,
                    "dashboard-user",
                    ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "ConMonPlanCreated",
                    Actor = "dashboard-user",
                    Summary = $"Continuous monitoring plan created (frequency: {body.AssessmentFrequency ?? "Monthly"})",
                    RelatedEntityType = "ConMonPlan",
                    RelatedEntityId = plan.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/systems/{systemId}/conmon-plan/{plan.Id}", new
                {
                    id = plan.Id,
                    assessmentFrequency = plan.AssessmentFrequency,
                    annualReviewDate = plan.AnnualReviewDate,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
            }
        })
        .WithName("CreateConMonPlan");

        // ─── Generate ConMon Report ──────────────────────────────────────────
        app.MapPost("/api/dashboard/systems/{systemId}/conmon-report", async (
            string systemId,
            GenerateConMonReportRequest body,
            IConMonService conMonService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            try
            {
                var report = await conMonService.GenerateReportAsync(
                    systemId,
                    body.ReportType ?? "Monthly",
                    body.Period ?? DateTime.UtcNow.ToString("yyyy-MM"),
                    "dashboard-user",
                    ct);

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = systemId,
                    EventType = "ConMonReportGenerated",
                    Actor = "dashboard-user",
                    Summary = $"ConMon report generated ({body.ReportType ?? "Monthly"} — {body.Period ?? DateTime.UtcNow.ToString("yyyy-MM")})",
                    RelatedEntityType = "ConMonReport",
                    RelatedEntityId = report.Id,
                });
                await context.SaveChangesAsync(ct);

                return Results.Created($"/api/dashboard/systems/{systemId}/conmon-report/{report.Id}", new
                {
                    id = report.Id,
                    reportType = report.ReportType,
                    period = report.ReportPeriod,
                    complianceScore = report.ComplianceScore,
                    scoreDelta = report.ComplianceScore - (report.AuthorizedBaselineScore ?? report.ComplianceScore),
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_INPUT" });
            }
        })
        .WithName("GenerateConMonReport");

        // ─── Remediation Summary (cross-system) ─────────────────────────────
        app.MapGet("/api/dashboard/remediation/summary", async (
            string? systemId,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            // POA&M items — optionally filtered by system
            var poamQuery = context.PoamItems.AsNoTracking();
            if (!string.IsNullOrEmpty(systemId))
                poamQuery = poamQuery.Where(p => p.RegisteredSystemId == systemId);

            var poams = await poamQuery
                .Include(p => p.Milestones)
                .Include(p => p.RegisteredSystem)
                .OrderByDescending(p => p.CatSeverity)
                .ThenBy(p => p.ScheduledCompletionDate)
                .ToListAsync(ct);

            var now = DateTime.UtcNow;

            var openPoams = poams.Where(p => p.Status != PoamStatus.Completed && p.Status != PoamStatus.RiskAccepted).ToList();
            var overduePoams = openPoams.Where(p => p.ScheduledCompletionDate < now).ToList();
            var catI = openPoams.Count(p => p.CatSeverity == CatSeverity.CatI);
            var catII = openPoams.Count(p => p.CatSeverity == CatSeverity.CatII);
            var catIII = openPoams.Count(p => p.CatSeverity == CatSeverity.CatIII);

            // Avg days to close — completed items only
            var completedPoams = poams.Where(p => p.Status == PoamStatus.Completed && p.ActualCompletionDate != null).ToList();
            var avgDaysToClose = completedPoams.Count > 0
                ? Math.Round(completedPoams.Average(p => (p.ActualCompletionDate!.Value - p.CreatedAt).TotalDays), 1)
                : 0.0;

            // Aging buckets (open only)
            var aging = new
            {
                days0To30 = openPoams.Count(p => (now - p.CreatedAt).TotalDays <= 30),
                days31To60 = openPoams.Count(p => { var d = (now - p.CreatedAt).TotalDays; return d > 30 && d <= 60; }),
                days61To90 = openPoams.Count(p => { var d = (now - p.CreatedAt).TotalDays; return d > 60 && d <= 90; }),
                days90Plus = openPoams.Count(p => (now - p.CreatedAt).TotalDays > 90),
            };

            // By-system breakdown
            var bySystem = poams
                .Where(p => p.Status != PoamStatus.Completed && p.Status != PoamStatus.RiskAccepted)
                .GroupBy(p => new { p.RegisteredSystemId, SystemName = p.RegisteredSystem?.Name ?? p.RegisteredSystemId })
                .Select(g => new
                {
                    systemId = g.Key.RegisteredSystemId,
                    systemName = g.Key.SystemName,
                    open = g.Count(),
                    overdue = g.Count(p => p.ScheduledCompletionDate < now),
                    catI = g.Count(p => p.CatSeverity == CatSeverity.CatI),
                })
                .OrderByDescending(s => s.overdue)
                .ThenByDescending(s => s.catI)
                .ToList();

            // Remediation tasks across all boards (or filtered by system via board's subscription)
            var taskQuery = context.RemediationTasks.AsNoTracking();
            if (!string.IsNullOrEmpty(systemId))
            {
                var boardIds = await context.RemediationBoards
                    .Where(b => b.SubscriptionId == systemId || b.AssessmentId != null)
                    .Select(b => b.Id)
                    .ToListAsync(ct);
                // Also include tasks linked to POA&M items for this system
                var poamTaskIds = poams.Where(p => p.RemediationTaskId != null).Select(p => p.RemediationTaskId!).ToHashSet();
                taskQuery = taskQuery.Where(t => boardIds.Contains(t.BoardId) || poamTaskIds.Contains(t.Id));
            }

            var tasks = await taskQuery.ToListAsync(ct);

            var tasksByStatus = new
            {
                backlog = tasks.Count(t => t.Status == KanbanTaskStatus.Backlog),
                todo = tasks.Count(t => t.Status == KanbanTaskStatus.ToDo),
                inProgress = tasks.Count(t => t.Status == KanbanTaskStatus.InProgress),
                inReview = tasks.Count(t => t.Status == KanbanTaskStatus.InReview),
                blocked = tasks.Count(t => t.Status == KanbanTaskStatus.Blocked),
                done = tasks.Count(t => t.Status == KanbanTaskStatus.Done),
            };

            // Severity heatbar for open POA&Ms
            var totalOpen = openPoams.Count;
            var severityBreakdown = new
            {
                catI,
                catII,
                catIII,
                catIPercent = totalOpen > 0 ? Math.Round(100.0 * catI / totalOpen, 1) : 0,
                catIIPercent = totalOpen > 0 ? Math.Round(100.0 * catII / totalOpen, 1) : 0,
                catIIIPercent = totalOpen > 0 ? Math.Round(100.0 * catIII / totalOpen, 1) : 0,
            };

            return Results.Ok(new
            {
                totalPoams = poams.Count,
                openCount = openPoams.Count,
                overdueCount = overduePoams.Count,
                completedCount = poams.Count(p => p.Status == PoamStatus.Completed),
                riskAcceptedCount = poams.Count(p => p.Status == PoamStatus.RiskAccepted),
                delayedCount = poams.Count(p => p.Status == PoamStatus.Delayed),
                avgDaysToClose,
                severityBreakdown,
                aging,
                bySystem,
                tasksByStatus,
                totalTasks = tasks.Count,
                poams = poams.Select(p => new
                {
                    p.Id,
                    p.RegisteredSystemId,
                    systemName = p.RegisteredSystem?.Name,
                    p.Weakness,
                    p.WeaknessSource,
                    controlId = p.SecurityControlNumber,
                    catSeverity = p.CatSeverity.ToString(),
                    p.PointOfContact,
                    p.PocEmail,
                    p.ResourcesRequired,
                    p.CostEstimate,
                    p.ScheduledCompletionDate,
                    p.ActualCompletionDate,
                    status = p.Status.ToString(),
                    p.Comments,
                    p.FindingId,
                    p.RemediationTaskId,
                    p.CreatedAt,
                    isOverdue = p.Status != PoamStatus.Completed &&
                                p.Status != PoamStatus.RiskAccepted &&
                                p.ScheduledCompletionDate < now,
                    daysRemaining = p.Status == PoamStatus.Completed ? (int?)null
                        : (int)Math.Ceiling((p.ScheduledCompletionDate - now).TotalDays),
                    milestones = p.Milestones.OrderBy(m => m.Sequence).Select(m => new
                    {
                        m.Id,
                        m.Description,
                        m.TargetDate,
                        m.CompletedDate,
                        m.Sequence,
                        m.IsOverdue,
                    }),
                    milestoneProgress = new
                    {
                        total = p.Milestones.Count,
                        completed = p.Milestones.Count(m => m.CompletedDate != null),
                    },
                }),
            });
        })
        .WithName("GetRemediationSummary");

        // ─── Remediation Tasks (cross-board) ─────────────────────────────────
        app.MapGet("/api/dashboard/remediation/tasks", async (
            string? systemId,
            string? status,
            string? severity,
            bool? overdueOnly,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var taskQuery = context.RemediationTasks
                .Include(t => t.Board)
                .AsNoTracking();

            if (!string.IsNullOrEmpty(systemId))
            {
                var boardIds = await context.RemediationBoards
                    .Where(b => b.SubscriptionId == systemId)
                    .Select(b => b.Id)
                    .ToListAsync(ct);
                taskQuery = taskQuery.Where(t => boardIds.Contains(t.BoardId));
            }

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<KanbanTaskStatus>(status, true, out var ts))
                taskQuery = taskQuery.Where(t => t.Status == ts);

            if (!string.IsNullOrEmpty(severity) && Enum.TryParse<FindingSeverity>(severity, true, out var sv))
                taskQuery = taskQuery.Where(t => t.Severity == sv);

            if (overdueOnly == true)
                taskQuery = taskQuery.Where(t => t.DueDate < DateTime.UtcNow && t.Status != KanbanTaskStatus.Done);

            var tasks = await taskQuery
                .OrderByDescending(t => t.Severity)
                .ThenBy(t => t.DueDate)
                .ToListAsync(ct);

            // Look up CAT severity from linked POA&M items
            var poamItemIds = tasks
                .Where(t => t.PoamItemId != null)
                .Select(t => t.PoamItemId!)
                .Distinct()
                .ToList();
            var poamCatMap = poamItemIds.Count > 0
                ? await context.PoamItems
                    .Where(p => poamItemIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.CatSeverity })
                    .AsNoTracking()
                    .ToDictionaryAsync(p => p.Id, p => p.CatSeverity.ToString(), ct)
                : new Dictionary<string, string>();

            return Results.Ok(new
            {
                items = tasks.Select(t => new
                {
                    t.Id,
                    t.TaskNumber,
                    t.BoardId,
                    boardName = t.Board?.Name,
                    t.Title,
                    t.Description,
                    t.ControlId,
                    t.ControlFamily,
                    severity = t.Severity.ToString(),
                    catSeverity = t.PoamItemId != null && poamCatMap.TryGetValue(t.PoamItemId, out var cat) ? cat : (string?)null,
                    status = t.Status.ToString(),
                    t.AssigneeId,
                    t.AssigneeName,
                    t.DueDate,
                    t.CreatedAt,
                    t.UpdatedAt,
                    t.FindingId,
                    t.PoamItemId,
                    t.RemediationScript,
                    t.RemediationScriptType,
                    t.ValidationCriteria,
                    isOverdue = t.DueDate < DateTime.UtcNow && t.Status != KanbanTaskStatus.Done,
                    affectedResourceCount = t.AffectedResources.Count,
                }),
                totalCount = tasks.Count,
            });
        })
        .WithName("GetRemediationTasks");

        // ─── Move Remediation Task (Kanban column change) ────────────────────
        app.MapPut("/api/dashboard/remediation/tasks/{taskId}/move", async (
            string taskId,
            MoveTaskRequest body,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<KanbanTaskStatus>(body.Status, true, out var newStatus))
                return Results.BadRequest(new ErrorResponse { Error = $"Invalid status: {body.Status}", ErrorCode = "INVALID_INPUT" });

            var task = await context.RemediationTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
            if (task == null)
                return Results.NotFound(new ErrorResponse { Error = "Task not found", ErrorCode = "NOT_FOUND" });

            var oldStatus = task.Status;
            task.Status = newStatus;
            task.UpdatedAt = DateTime.UtcNow;

            task.History.Add(new TaskHistoryEntry
            {
                TaskId = taskId,
                EventType = HistoryEventType.StatusChanged,
                OldValue = oldStatus.ToString(),
                NewValue = newStatus.ToString(),
                ActingUserId = "dashboard-user",
                ActingUserName = "Dashboard User",
                Timestamp = DateTime.UtcNow,
            });

            await context.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                id = task.Id,
                taskNumber = task.TaskNumber,
                previousStatus = oldStatus.ToString(),
                newStatus = newStatus.ToString(),
                updatedAt = task.UpdatedAt,
            });
        })
        .WithName("MoveRemediationTask");

        // ─── Update POA&M Status ─────────────────────────────────────────────
        app.MapPut("/api/dashboard/systems/{systemId}/poam/{poamId}/status", async (
            string systemId,
            string poamId,
            UpdatePoamStatusRequest body,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var poam = await context.PoamItems
                .FirstOrDefaultAsync(p => p.Id == poamId && p.RegisteredSystemId == systemId, ct);
            if (poam == null)
                return Results.NotFound(new ErrorResponse { Error = "POA&M item not found", ErrorCode = "NOT_FOUND" });

            if (!Enum.TryParse<PoamStatus>(body.Status, true, out var newStatus))
                return Results.BadRequest(new ErrorResponse { Error = $"Invalid status: {body.Status}", ErrorCode = "INVALID_INPUT" });

            poam.Status = newStatus;
            poam.ModifiedAt = DateTime.UtcNow;
            if (newStatus == PoamStatus.Completed)
                poam.ActualCompletionDate = DateTime.UtcNow;
            if (body.Comments != null)
                poam.Comments = body.Comments;

            context.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "PoamStatusUpdated",
                Actor = "dashboard-user",
                Summary = $"POA&M {poam.SecurityControlNumber} status changed to {newStatus}",
                RelatedEntityType = "PoamItem",
                RelatedEntityId = poam.Id,
            });
            await context.SaveChangesAsync(ct);

            return Results.Ok(new { id = poam.Id, status = poam.Status.ToString(), modifiedAt = poam.ModifiedAt });
        })
        .WithName("UpdatePoamStatus");

        // ─── Bulk Update POA&M Status ────────────────────────────────────────
        app.MapPut("/api/dashboard/remediation/poam/bulk-status", async (
            BulkPoamStatusRequest body,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            if (body.PoamIds == null || body.PoamIds.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "No POA&M IDs provided", ErrorCode = "INVALID_INPUT" });

            if (!Enum.TryParse<PoamStatus>(body.Status, true, out var newStatus))
                return Results.BadRequest(new ErrorResponse { Error = $"Invalid status: {body.Status}", ErrorCode = "INVALID_INPUT" });

            var poams = await context.PoamItems
                .Where(p => body.PoamIds.Contains(p.Id))
                .ToListAsync(ct);

            foreach (var poam in poams)
            {
                poam.Status = newStatus;
                poam.ModifiedAt = DateTime.UtcNow;
                if (newStatus == PoamStatus.Completed)
                    poam.ActualCompletionDate = DateTime.UtcNow;

                context.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = poam.RegisteredSystemId,
                    EventType = "PoamStatusUpdated",
                    Actor = "dashboard-user",
                    Summary = $"POA&M {poam.SecurityControlNumber} bulk status change to {newStatus}",
                    RelatedEntityType = "PoamItem",
                    RelatedEntityId = poam.Id,
                });
            }

            await context.SaveChangesAsync(ct);

            return Results.Ok(new { updated = poams.Count, status = newStatus.ToString() });
        })
        .WithName("BulkUpdatePoamStatus");

        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Request DTOs for new Authorize/Monitor endpoints
    // ═══════════════════════════════════════════════════════════════════════════

    private record IssueAuthorizationRequest(
        string DecisionType,
        DateTime? ExpirationDate,
        string? ResidualRiskLevel,
        string? TermsAndConditions,
        string? ResidualRiskJustification,
        List<RiskAcceptanceInput>? RiskAcceptances,
        string? IssuedBy,
        string? IssuedByName);

    private record CreatePoamRequest(
        string Weakness,
        string ControlId,
        string CatSeverity,
        string PointOfContact,
        DateTime ScheduledCompletionDate,
        string? FindingId,
        string? ResourcesRequired,
        List<MilestoneInput>? Milestones);

    private record AcceptRiskRequest(
        string FindingId,
        string ControlId,
        string CatSeverity,
        string Justification,
        DateTime ExpirationDate,
        string? CompensatingControl,
        string? AcceptedBy);

    private record CreateConMonPlanRequest(
        string? AssessmentFrequency,
        DateTime? AnnualReviewDate,
        List<string>? ReportDistribution,
        List<string>? SignificantChangeTriggers);

    private record GenerateConMonReportRequest(
        string? ReportType,
        string? Period);

    private record UpdatePoamStatusRequest(
        string Status,
        string? Comments);

    private record BulkPoamStatusRequest(
        List<string> PoamIds,
        string Status);

    private record MoveTaskRequest(
        string Status);
}
