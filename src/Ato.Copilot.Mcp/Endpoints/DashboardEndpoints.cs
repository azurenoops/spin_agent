using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Services;
using Ato.Copilot.Mcp.Services;

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

        // ─── Org-Wide Component Library (Feature 036) ────────────────────────
        group.MapGet("/components", async (
                [AsParameters] OrgComponentQuery query,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.GetAllComponentsAsync(query, ct);
                return Results.Ok(result);
            })
            .WithName("GetAllComponents");

        group.MapGet("/components/{componentId}", async (
                string componentId,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.GetComponentByIdAsync(componentId, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Component not found",
                        ErrorCode = "COMPONENT_NOT_FOUND",
                        Suggestion = "Check the component ID and try again",
                    });
            })
            .WithName("GetComponentById");

        group.MapPost("/components", async (
                CreateComponentRequest request,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.CreateOrgComponentAsync(request, "dashboard-user", ct);
                return Results.Created($"/api/dashboard/components/{result.Id}", result);
            })
            .WithName("CreateOrgComponent");

        group.MapPut("/components/{componentId}", async (
                string componentId,
                CreateComponentRequest request,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.UpdateOrgComponentAsync(componentId, request, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Component not found",
                        ErrorCode = "COMPONENT_NOT_FOUND",
                        Suggestion = "Check the component ID and try again",
                    });
            })
            .WithName("UpdateOrgComponent");

        group.MapDelete("/components/{componentId}", async (
                string componentId,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.DeleteComponentAsync(componentId, "dashboard-user", ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Component not found",
                        ErrorCode = "COMPONENT_NOT_FOUND",
                        Suggestion = "Check the component ID and try again",
                    });
            })
            .WithName("DeleteOrgComponent");

        group.MapPost("/components/{componentId}/assignments", async (
                string componentId,
                AssignComponentRequest request,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var (assignment, error) = await compService.AssignToSystemAsync(componentId, request, "dashboard-user", ct);
                if (error == "Component not found" || error == "System not found")
                    return Results.NotFound(new ErrorResponse { Error = error, ErrorCode = "NOT_FOUND" });
                if (error == "Assignment already exists")
                    return Results.Conflict(new ErrorResponse { Error = error, ErrorCode = "DUPLICATE_ASSIGNMENT" });
                return Results.Created($"/api/dashboard/components/{componentId}/assignments/{assignment!.Id}", assignment);
            })
            .WithName("AssignComponentToSystem");

        group.MapDelete("/components/{componentId}/assignments/{assignmentId}", async (
                string componentId,
                string assignmentId,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.RemoveAssignmentAsync(componentId, assignmentId, ct);
                return result
                    ? Results.NoContent()
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Assignment not found",
                        ErrorCode = "ASSIGNMENT_NOT_FOUND",
                    });
            })
            .WithName("RemoveComponentAssignment");

        group.MapPost("/components/{componentId}/capabilities", async (
                string componentId,
                LinkComponentCapabilitiesRequest request,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var component = await db.SystemComponents.FindAsync(new object[] { componentId }, ct);
                if (component is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Component not found",
                        ErrorCode = "COMPONENT_NOT_FOUND",
                    });

                var linksCreated = 0;
                var linksAlreadyExist = 0;
                foreach (var capId in request.CapabilityIds)
                {
                    var exists = await db.ComponentCapabilityLinks
                        .AnyAsync(l => l.SystemComponentId == componentId && l.SecurityCapabilityId == capId, ct);
                    if (exists)
                    {
                        linksAlreadyExist++;
                        continue;
                    }
                    // Verify capability exists
                    var cap = await db.SecurityCapabilities.FindAsync(new object[] { capId }, ct);
                    if (cap is null) continue;

                    db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
                    {
                        SystemComponentId = componentId,
                        SecurityCapabilityId = capId,
                    });
                    linksCreated++;
                }
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { componentId, linksCreated, linksAlreadyExist });
            })
            .WithName("LinkComponentCapabilities");

        group.MapDelete("/components/{componentId}/capabilities/{capabilityId}", async (
                string componentId,
                string capabilityId,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var link = await db.ComponentCapabilityLinks
                    .FirstOrDefaultAsync(l => l.SystemComponentId == componentId && l.SecurityCapabilityId == capabilityId, ct);
                if (link is null) return Results.NoContent(); // idempotent
                db.ComponentCapabilityLinks.Remove(link);
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .WithName("UnlinkComponentCapability");

        group.MapGet("/components/{componentId}/impact-preview", async (
                string componentId,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.GetComponentImpactPreviewAsync(componentId, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Component not found",
                        ErrorCode = "COMPONENT_NOT_FOUND",
                        Suggestion = "Check the component ID and try again",
                    });
            })
            .WithName("GetComponentImpactPreview");

        // ─── AI Narrative Regeneration ─────────────────────────────────────────
        group.MapPost("/systems/{systemId}/controls/{controlId}/regenerate-ai", async (
                string systemId,
                string controlId,
                CapabilityService capService,
                CancellationToken ct) =>
            {
                var (narrative, errorCode) = await capService.RegenerateNarrativeWithAiAsync(
                    systemId, controlId, "dashboard-user", ct);
                return errorCode switch
                {
                    "NOT_FOUND" => Results.NotFound(new ErrorResponse
                    {
                        Error = "Control implementation not found",
                        ErrorCode = "CONTROL_NOT_FOUND",
                    }),
                    _ => Results.Ok(new { narrative }),
                };
            })
            .WithName("RegenerateNarrativeWithAi");

        // ─── Bulk Narrative Regeneration for a Capability ──────────────────────
        group.MapPost("/systems/{systemId}/capabilities/{capabilityId}/bulk-regenerate", async (
                string systemId,
                string capabilityId,
                CapabilityService capService,
                CancellationToken ct) =>
            {
                var result = await capService.BulkRegenerateNarrativesForCapabilityAsync(
                    systemId, capabilityId, "dashboard-user", ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System or capability not found",
                        ErrorCode = "NOT_FOUND",
                    });
            })
            .WithName("BulkRegenerateNarrativesForCapability");

        // ─── Capability Coverage (US5) ────────────────────────────────────────
        group.MapGet("/systems/{systemId}/capability-coverage", async (
                string systemId,
                CapabilityService capService,
                CancellationToken ct) =>
            {
                var result = await capService.GetCapabilityCoverageAsync(systemId, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
            })
            .WithName("GetCapabilityCoverage");

        // ─── Capability Links (Feature 042 — System Intake Wizard) ───────────
        group.MapPost("/systems/{systemId}/capability-links", async (
                string systemId,
                LinkCapabilitiesRequest body,
                SystemCapabilityLinkService linkService,
                CancellationToken ct) =>
            {
                if (body.CapabilityIds is null || body.CapabilityIds.Count == 0)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "At least one capability ID is required",
                        ErrorCode = "INVALID_INPUT",
                    });
                try
                {
                    var (linkedCount, items) = await linkService.LinkCapabilitiesAsync(
                        systemId, body.CapabilityIds, "dashboard-user", ct);
                    return Results.Ok(new
                    {
                        linkedCount,
                        items = items.Select(l => new
                        {
                            id = l.Id,
                            systemId = l.RegisteredSystemId,
                            capabilityId = l.SecurityCapabilityId,
                            capabilityName = l.SecurityCapability?.Name,
                            linkedAt = l.LinkedAt,
                        }),
                    });
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "INVALID_CAPABILITY_IDS",
                    });
                }
            })
            .WithName("LinkCapabilities");

        group.MapGet("/systems/{systemId}/capability-links", async (
                string systemId,
                SystemCapabilityLinkService linkService,
                CancellationToken ct) =>
            {
                var links = await linkService.GetLinksForSystemAsync(systemId, ct);
                return Results.Ok(new
                {
                    items = links.Select(l => new
                    {
                        id = l.Id,
                        capabilityId = l.SecurityCapabilityId,
                        capabilityName = l.SecurityCapability?.Name,
                        provider = l.SecurityCapability?.Provider,
                        category = l.SecurityCapability?.Category,
                        implementationStatus = l.SecurityCapability?.ImplementationStatus.ToString(),
                        linkedAt = l.LinkedAt,
                    }),
                    totalCount = links.Count,
                });
            })
            .WithName("GetCapabilityLinks");

        group.MapDelete("/systems/{systemId}/capability-links/{linkId}", async (
                string systemId,
                string linkId,
                SystemCapabilityLinkService linkService,
                CancellationToken ct) =>
            {
                var removed = await linkService.RemoveLinkAsync(systemId, linkId, ct);
                return removed
                    ? Results.Ok(new { deletedId = linkId, message = "Capability link removed" })
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Capability link not found",
                        ErrorCode = "LINK_NOT_FOUND",
                    });
            })
            .WithName("RemoveCapabilityLink");

        // ─── Components — System-Scoped (US5, modified by Feature 036) ───────
        group.MapGet("/systems/{systemId}/components", async (
                string systemId,
                [AsParameters] ComponentQuery query,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var result = await compService.GetSystemScopedComponentsAsync(systemId, query, ct);
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
                if (result is null)
                    return Results.Conflict(new ErrorResponse
                    {
                        Error = "A capability with this name already exists",
                        ErrorCode = "CAPABILITY_NAME_DUPLICATE",
                        Suggestion = "Use a unique name or update the existing capability",
                    });

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

        group.MapGet("/capabilities/{id}/impact-preview", async (
                string id,
                CapabilityService service,
                CancellationToken ct) =>
            {
                var result = await service.GetCapabilityImpactPreviewAsync(id, ct);
                return result is not null
                    ? Results.Ok(result)
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Capability not found",
                        ErrorCode = "CAPABILITY_NOT_FOUND",
                        Suggestion = "Check the capability ID and try again",
                    });
            })
            .WithName("GetCapabilityImpactPreview");

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

        // ─── Feature 045: Capabilities Hub Import/Coverage Endpoints ────────
        group.MapPost("/capabilities/import/csp-profile", async (
                CspProfileImportRequest request,
                CapabilityImportService importService,
                CancellationToken ct) =>
            {
                try
                {
                    if (request.DryRun == true)
                    {
                        var preview = await importService.ImportCspProfilePreviewAsync(
                            request.ProfileId, request.ConflictResolution ?? "skip", ct);
                        return Results.Ok(preview);
                    }

                    var result = await importService.ImportCspProfileAsync(
                        request.ProfileId, request.ConflictResolution ?? "skip", ct);
                    return Results.Ok(result);
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = $"CSP profile '{request.ProfileId}' not found",
                        ErrorCode = "PROFILE_NOT_FOUND",
                        Suggestion = "Check the profile ID and try again",
                    });
                }
            })
            .WithName("ImportCspProfile");

        group.MapGet("/capabilities/coverage", async (
                bool? includePerSystem,
                bool? includePerFamily,
                CapabilityImportService importService,
                CancellationToken ct) =>
            {
                var result = await importService.ComputeCoverageAsync(
                    includePerSystem ?? false, includePerFamily ?? true, ct);
                return Results.Ok(result);
            })
            .WithName("GetOrgWideCoverage");

        group.MapGet("/capabilities/csp-profiles", (
                Ato.Copilot.Mcp.Services.CspProfileService cspProfileService) =>
            {
                var profiles = cspProfileService.GetProfiles();
                return Results.Ok(new
                {
                    profiles = profiles.Select(p => new
                    {
                        profileId = p.ProfileId,
                        name = p.Name,
                        provider = p.Provider,
                        baselineLevel = p.BaselineLevel,
                        description = p.Description,
                        controlCount = p.Controls.Count,
                        serviceCount = p.Services?.Count ?? 0,
                        version = p.Version
                    })
                });
            })
            .WithName("ListCapabilityCspProfiles");

        group.MapPost("/capabilities/import/crm", async (
                HttpRequest httpRequest,
                CapabilityImportService importService,
                CrmExportService crmExportService,
                CancellationToken ct) =>
            {
                var form = await httpRequest.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");
                if (file is null || file.Length == 0)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "No file uploaded",
                        ErrorCode = "FILE_REQUIRED",
                        Suggestion = "Upload a CSV or Excel file",
                    });

                var columnMappingJson = form["columnMapping"].ToString();
                var conflictResolution = form["conflictResolution"].ToString();
                var dryRunStr = form["dryRun"].ToString();
                var dryRun = dryRunStr.Equals("true", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(conflictResolution)) conflictResolution = "skip";

                // Parse file
                CrmExportService.ImportParseResult parsed;
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                using var stream = file.OpenReadStream();
                if (ext is ".xlsx" or ".xls")
                    parsed = crmExportService.ParseExcel(stream);
                else
                    parsed = crmExportService.ParseCsv(stream);

                // Determine column mapping
                Dictionary<string, string> mapping;
                if (!string.IsNullOrEmpty(columnMappingJson))
                {
                    mapping = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(columnMappingJson)
                        ?? new Dictionary<string, string>();
                }
                else
                {
                    // Auto-detect: use exact column names
                    mapping = new Dictionary<string, string>
                    {
                        ["controlId"] = parsed.Columns.FirstOrDefault(c => c.Contains("control", StringComparison.OrdinalIgnoreCase)) ?? "controlId",
                        ["inheritanceType"] = parsed.Columns.FirstOrDefault(c => c.Contains("inheritance", StringComparison.OrdinalIgnoreCase)) ?? "inheritanceType",
                        ["provider"] = parsed.Columns.FirstOrDefault(c => c.Contains("provider", StringComparison.OrdinalIgnoreCase)) ?? "provider",
                        ["customerResponsibility"] = parsed.Columns.FirstOrDefault(c => c.Contains("responsibility", StringComparison.OrdinalIgnoreCase)) ?? "customerResponsibility",
                    };
                }

                // Map parsed rows to CrmImportRow using column mapping
                var rows = parsed.Rows.Select(row => new CrmImportRow
                {
                    ControlId = mapping.TryGetValue("controlId", out var cidCol) && row.TryGetValue(cidCol, out var cid) ? cid : "",
                    InheritanceType = mapping.TryGetValue("inheritanceType", out var itCol) && row.TryGetValue(itCol, out var it) ? it : "",
                    Provider = mapping.TryGetValue("provider", out var pCol) && row.TryGetValue(pCol, out var p) ? p : null,
                    CustomerResponsibility = mapping.TryGetValue("customerResponsibility", out var crCol) && row.TryGetValue(crCol, out var cr) ? cr : null,
                }).Where(r => !string.IsNullOrWhiteSpace(r.ControlId)).ToList();

                if (dryRun)
                {
                    var preview = await importService.ImportCrmPreviewAsync(
                        file.FileName, rows, conflictResolution, ct);
                    preview.DetectedColumns = parsed.Columns;
                    preview.SampleRows = parsed.SampleRows;
                    return Results.Ok(preview);
                }

                var result = await importService.ImportCrmAsync(
                    file.FileName, rows, conflictResolution, ct);
                return Results.Ok(result);
            })
            .DisableAntiforgery()
            .WithName("ImportCrm");

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

        // ─── System Profile (Feature 046) ──────────────────────────────────

        group.MapGet("/systems/{systemId}/profile", async (
                string systemId,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await profileService.GetProfileOverviewAsync(systemId, ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("SYSTEM_NOT_FOUND"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
                }
            })
            .WithName("GetSystemProfile");

        group.MapGet("/systems/{systemId}/profile/{sectionType}", async (
                string systemId,
                string sectionType,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                if (!Enum.TryParse<ProfileSectionType>(sectionType, true, out var parsedType))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = $"Invalid section type '{sectionType}'",
                        ErrorCode = "INVALID_INPUT",
                    });

                var result = await profileService.GetSectionDetailAsync(systemId, parsedType, ct);
                if (result is null)
                {
                    // Return a synthesized "not started" section matching what the frontend expects
                    return Results.Ok(new
                    {
                        id = (string?)null,
                        sectionType = parsedType.ToString(),
                        governanceStatus = "NotStarted",
                        draftContent = (string?)null,
                        approvedContent = (string?)null,
                        completionPercentage = 0,
                        lastEditedBy = (string?)null,
                        lastEditedAt = (string?)null,
                        submittedBy = (string?)null,
                        submittedAt = (string?)null,
                        reviewedBy = (string?)null,
                        reviewedAt = (string?)null,
                        reviewerComments = (string?)null,
                        userCategories = Array.Empty<object>(),
                        dataTypeEntries = Array.Empty<object>(),
                        ppsEntries = Array.Empty<object>(),
                        leveragedAuthorizations = Array.Empty<object>(),
                    });
                }

                return Results.Ok(new
                {
                    id = result.Id,
                    sectionType = result.SectionType.ToString(),
                    governanceStatus = result.GovernanceStatus.ToString(),
                    draftContent = result.DraftContent,
                    approvedContent = result.ApprovedContent,
                    completionPercentage = result.CompletionPercentage,
                    lastEditedBy = result.LastEditedBy,
                    lastEditedAt = result.LastEditedAt?.ToString("O"),
                    submittedBy = result.SubmittedBy,
                    submittedAt = result.SubmittedAt?.ToString("O"),
                    reviewedBy = result.ReviewedBy,
                    reviewedAt = result.ReviewedAt?.ToString("O"),
                    reviewerComments = result.ReviewerComments,
                    userCategories = result.UserCategories.OrderBy(c => c.SortOrder).Select(c => new
                    {
                        c.Id, categoryName = c.CategoryName, description = c.Description,
                        approximateCount = c.ApproximateCount, accessMethod = c.AccessMethod,
                        dataSensitivityLevel = c.DataSensitivityLevel, sortOrder = c.SortOrder,
                    }),
                    dataTypeEntries = result.DataTypeEntries.OrderBy(d => d.SortOrder).Select(d => new
                    {
                        d.Id, dataTypeName = d.DataTypeName, description = d.Description,
                        sensitivityClassification = d.SensitivityClassification,
                        source = d.Source, destination = d.Destination,
                        applicableRegulations = d.ApplicableRegulations, sortOrder = d.SortOrder,
                    }),
                    ppsEntries = result.PpsEntries.OrderBy(p => p.SortOrder).Select(p => new
                    {
                        p.Id, portOrRange = p.PortOrRange, protocol = p.Protocol,
                        serviceName = p.ServiceName, direction = p.Direction,
                        justification = p.Justification, sortOrder = p.SortOrder,
                    }),
                    leveragedAuthorizations = result.LeveragedAuthorizations.OrderBy(l => l.SortOrder).Select(l => new
                    {
                        l.Id, providerName = l.ProviderName, authorizationType = l.AuthorizationType,
                        authorizationDate = l.AuthorizationDate, coveredControlFamilies = l.CoveredControlFamilies,
                        sortOrder = l.SortOrder,
                    }),
                });
            })
            .WithName("GetProfileSection");

        group.MapPut("/systems/{systemId}/profile/{sectionType}", async (
                string systemId,
                string sectionType,
                SaveProfileSectionBody body,
                HttpContext httpContext,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                if (!Enum.TryParse<ProfileSectionType>(sectionType, true, out var parsedType))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = $"Invalid section type '{sectionType}'",
                        ErrorCode = "INVALID_INPUT",
                    });

                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";
                try
                {
                    var result = await profileService.SaveDraftAsync(
                        systemId, parsedType, body.Content, userId, ct);

                    return Results.Ok(new
                    {
                        id = result.Id,
                        sectionType = result.SectionType.ToString(),
                        governanceStatus = result.GovernanceStatus.ToString(),
                        draftContent = result.DraftContent,
                        approvedContent = result.ApprovedContent,
                        completionPercentage = result.CompletionPercentage,
                        lastEditedBy = result.LastEditedBy,
                        lastEditedAt = result.LastEditedAt?.ToString("O"),
                        submittedBy = result.SubmittedBy,
                        submittedAt = result.SubmittedAt?.ToString("O"),
                        reviewedBy = result.ReviewedBy,
                        reviewedAt = result.ReviewedAt?.ToString("O"),
                        reviewerComments = result.ReviewerComments,
                        userCategories = Array.Empty<object>(),
                        dataTypeEntries = Array.Empty<object>(),
                        ppsEntries = Array.Empty<object>(),
                        leveragedAuthorizations = Array.Empty<object>(),
                    });
                }
                catch (InvalidOperationException ex)
                {
                    var code = ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
                    var statusCode = code switch
                    {
                        "UNAUTHORIZED" => StatusCodes.Status403Forbidden,
                        "SYSTEM_NOT_FOUND" => StatusCodes.Status404NotFound,
                        _ => StatusCodes.Status400BadRequest,
                    };
                    return Results.Json(new ErrorResponse { Error = ex.Message, ErrorCode = code }, statusCode: statusCode);
                }
            })
            .WithName("SaveProfileSection");

        group.MapPost("/systems/{systemId}/profile/submit", async (
                string systemId,
                SubmitSectionsBody body,
                HttpContext httpContext,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";
                try
                {
                    var sectionTypes = body.SectionTypes?.Select(s =>
                    {
                        Enum.TryParse<ProfileSectionType>(s, true, out var t);
                        return t;
                    }).ToList();

                    if (string.Equals(body.Action, "withdraw", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = await profileService.WithdrawSectionAsync(systemId, sectionTypes, userId, ct);
                        return Results.Ok(new
                        {
                            withdrawnSections = result.WithdrawnSections.Select(s => s.ToString()),
                            skippedSections = result.SkippedSections.Select(s => new { sectionType = s.SectionType.ToString(), s.Reason }),
                            withdrawnBy = result.WithdrawnBy,
                            withdrawnAt = result.WithdrawnAt.ToString("O"),
                        });
                    }
                    else
                    {
                        var result = await profileService.SubmitForReviewAsync(systemId, sectionTypes, userId, ct);
                        return Results.Ok(new
                        {
                            submittedSections = result.SubmittedSections.Select(s => s.ToString()),
                            skippedSections = result.SkippedSections.Select(s => new { sectionType = s.SectionType.ToString(), s.Reason }),
                            submittedBy = result.SubmittedBy,
                            submittedAt = result.SubmittedAt.ToString("O"),
                        });
                    }
                }
                catch (InvalidOperationException ex)
                {
                    var code = ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
                    var statusCode = code switch
                    {
                        "UNAUTHORIZED" => StatusCodes.Status403Forbidden,
                        _ => StatusCodes.Status400BadRequest,
                    };
                    return Results.Json(new ErrorResponse { Error = ex.Message, ErrorCode = code }, statusCode: statusCode);
                }
            })
            .WithName("SubmitProfileSections");

        group.MapPost("/systems/{systemId}/profile/{sectionType}/review", async (
                string systemId,
                string sectionType,
                ReviewSectionBody body,
                HttpContext httpContext,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                if (!Enum.TryParse<ProfileSectionType>(sectionType, true, out var parsedType))
                    return Results.BadRequest(new ErrorResponse { Error = $"Invalid section type", ErrorCode = "INVALID_INPUT" });

                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";
                var decision = body.Decision.Equals("approve", StringComparison.OrdinalIgnoreCase)
                    ? ReviewDecision.Approve
                    : ReviewDecision.RequestRevision;

                try
                {
                    var result = await profileService.ReviewSectionAsync(
                        systemId, parsedType, decision, userId, body.Comments, ct);
                    return Results.Ok(new
                    {
                        sectionType = result.SectionType.ToString(),
                        newStatus = result.GovernanceStatus.ToString(),
                        reviewedBy = result.ReviewedBy,
                        reviewedAt = result.ReviewedAt?.ToString("O"),
                    });
                }
                catch (InvalidOperationException ex)
                {
                    var code = ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
                    var statusCode = code switch
                    {
                        "UNAUTHORIZED" => StatusCodes.Status403Forbidden,
                        "COMMENTS_REQUIRED" => StatusCodes.Status400BadRequest,
                        _ => StatusCodes.Status400BadRequest,
                    };
                    return Results.Json(new ErrorResponse { Error = ex.Message, ErrorCode = code }, statusCode: statusCode);
                }
            })
            .WithName("ReviewProfileSection");

        group.MapPost("/systems/{systemId}/profile/batch-approve", async (
                string systemId,
                HttpContext httpContext,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";
                try
                {
                    var result = await profileService.BatchApproveSectionsAsync(systemId, userId, ct);
                    return Results.Ok(new
                    {
                        approvedSections = result.ApprovedSections.Select(s => s.ToString()),
                        approvedCount = result.ApprovedCount,
                        reviewedBy = result.ReviewedBy,
                        reviewedAt = result.ReviewedAt.ToString("O"),
                    });
                }
                catch (InvalidOperationException ex)
                {
                    var code = ex.Message.Contains(':') ? ex.Message[..ex.Message.IndexOf(':')] : "OPERATION_FAILED";
                    return Results.Json(new ErrorResponse { Error = ex.Message, ErrorCode = code }, statusCode: StatusCodes.Status403Forbidden);
                }
            })
            .WithName("BatchApproveProfile");

        group.MapGet("/systems/{systemId}/profile/completeness", async (
                string systemId,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await profileService.GetCompletenessAsync(systemId, ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("SYSTEM_NOT_FOUND"))
                {
                    return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "SYSTEM_NOT_FOUND" });
                }
            })
            .WithName("GetProfileCompleteness");

        group.MapGet("/systems/{systemId}/profile/todos", async (
                string systemId,
                HttpContext httpContext,
                ISystemProfileService profileService,
                CancellationToken ct) =>
            {
                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";
                var result = await profileService.GetProfileTodosAsync(systemId, userId, ct);
                return Results.Ok(result);
            })
            .WithName("GetProfileTodos");

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

        group.MapGet("/boundary-definitions/{id}/components", async (
                string id,
                ComponentService compService,
                CancellationToken ct) =>
            {
                var items = await compService.GetComponentsByBoundaryAsync(id, ct);
                return Results.Ok(new { items, totalCount = items.Count });
            })
            .WithName("GetBoundaryComponents");

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
                catch (Azure.Identity.CredentialUnavailableException)
                {
                    return Results.Json(new ErrorResponse
                    {
                        Error = "Azure credentials not configured. Run 'az login' (use 'az cloud set --name AzureUSGovernment' for GovCloud) or configure service principal environment variables.",
                        ErrorCode = "AZURE_AUTH_FAILED",
                        Suggestion = "Run 'az login' on the Docker host so credentials are mounted into the container"
                    }, statusCode: 502);
                }
                catch (Azure.Identity.AuthenticationFailedException)
                {
                    return Results.Json(new ErrorResponse
                    {
                        Error = "Azure authentication failed for both Government and Commercial clouds. Run 'az login' with the correct cloud.",
                        ErrorCode = "AZURE_AUTH_FAILED",
                        Suggestion = "For GovCloud: 'az cloud set --name AzureUSGovernment && az login'"
                    }, statusCode: 502);
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
                IBaselineService baselineService,
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
                    // Check current baseline level before categorization change
                    var existingBaseline = await context.ControlBaselines
                        .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, ct);
                    var previousBaselineLevel = existingBaseline?.BaselineLevel;

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

                    // Auto-reselect baseline if the derived level changed
                    string? baselineReselected = null;
                    int? baselineControls = null;
                    int? inheritancesReapplied = null;
                    var newBaselineLevel = result.NistBaseline;

                    if (previousBaselineLevel != null &&
                        !string.Equals(previousBaselineLevel, newBaselineLevel, StringComparison.OrdinalIgnoreCase))
                    {
                        var newBaseline = await baselineService.SelectBaselineAsync(
                            systemId,
                            applyOverlay: true,
                            selectedBy: "dashboard-user",
                            cancellationToken: ct);

                        baselineReselected = newBaseline.BaselineLevel;
                        baselineControls = newBaseline.TotalControls;
                        inheritancesReapplied = newBaseline.InheritedControls + newBaseline.SharedControls + newBaseline.CustomerControls;

                        context.DashboardActivities.Add(new DashboardActivity
                        {
                            RegisteredSystemId = systemId,
                            EventType = "BaselineAutoReselected",
                            Actor = "dashboard-user",
                            Summary = $"Baseline auto-reselected from {previousBaselineLevel} to {newBaseline.BaselineLevel} ({newBaseline.TotalControls} controls) due to categorization change",
                            RelatedEntityType = "ControlBaseline",
                            RelatedEntityId = newBaseline.Id,
                        });
                        await context.SaveChangesAsync(ct);
                    }

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
                        baselineReselected,
                        baselineControls,
                        inheritancesReapplied,
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

        // ─── GET Baseline Detail ─────────────────────────────────────────────
        group.MapGet("/systems/{systemId}/baseline", async (
                string systemId,
                IBaselineService baselineService,
                CancellationToken ct) =>
            {
                var baseline = await baselineService.GetBaselineAsync(systemId, includeDetails: true, cancellationToken: ct);
                if (baseline == null)
                    return Results.NotFound(new ErrorResponse { Error = "No baseline configured for this system", ErrorCode = "BASELINE_NOT_FOUND" });

                // Build family breakdown from ControlIds
                var familyBreakdown = baseline.ControlIds
                    .GroupBy(c => ComplianceFrameworks.ExtractControlFamily(c))
                    .OrderBy(g => g.Key)
                    .Select(g => new { family = g.Key, count = g.Count() })
                    .ToList();

                // Tailoring records
                var tailorings = baseline.Tailorings
                    .OrderByDescending(t => t.TailoredAt)
                    .Select(t => new
                    {
                        id = t.Id,
                        controlId = t.ControlId,
                        action = t.Action.ToString(),
                        rationale = t.Rationale,
                        isOverlayRequired = t.IsOverlayRequired,
                        tailoredBy = t.TailoredBy,
                        tailoredAt = t.TailoredAt,
                    })
                    .ToList();

                return Results.Ok(new
                {
                    baselineId = baseline.Id,
                    baselineLevel = baseline.BaselineLevel,
                    totalControls = baseline.TotalControls,
                    overlayApplied = baseline.OverlayApplied,
                    inheritedControls = baseline.InheritedControls,
                    sharedControls = baseline.SharedControls,
                    customerControls = baseline.CustomerControls,
                    tailoredInControls = baseline.TailoredInControls,
                    tailoredOutControls = baseline.TailoredOutControls,
                    createdAt = baseline.CreatedAt,
                    createdBy = baseline.CreatedBy,
                    modifiedAt = baseline.ModifiedAt,
                    familyBreakdown,
                    tailorings,
                    controlIds = baseline.ControlIds,
                });
            })
            .WithName("GetBaselineDetail");

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

                // SAR (latest)
                var sar = await context.SecurityAssessmentReports
                    .Where(s => s.RegisteredSystemId == systemId)
                    .OrderByDescending(s => s.Status == SarStatus.Approved ? 2 : s.Status == SarStatus.UnderReview ? 1 : 0)
                    .ThenByDescending(s => s.CreatedAt)
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

                // Active waivers (Feature 035)
                int activeWaiverCount;
                try
                {
                    activeWaiverCount = await context.Deviations
                        .CountAsync(d => d.RegisteredSystemId == systemId
                            && d.DeviationType == DeviationType.Waiver
                            && (d.Status == DeviationStatus.Pending || d.Status == DeviationStatus.Approved), ct);
                }
                catch (Microsoft.Data.SqlClient.SqlException)
                {
                    activeWaiverCount = 0;
                }

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

                    Sar = sar is null ? null : new SarDocumentInfo
                    {
                        SarId = sar.Id,
                        Status = sar.Status.ToString(),
                        Title = sar.Title,
                        TotalControlsAssessed = sar.TotalControlsAssessed,
                        SatisfiedCount = sar.SatisfiedCount,
                        NotSatisfiedCount = sar.NotSatisfiedCount,
                        CreatedBy = sar.CreatedBy,
                        CreatedAt = sar.CreatedAt,
                        ApprovedBy = sar.ApprovedBy,
                        ApprovedAt = sar.ApprovedAt,
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

                    ActiveWaiverCount = activeWaiverCount,

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

            var findingDeviationIds = assessment.Findings
                .Where(f => f.DeviationId != null)
                .Select(f => f.DeviationId!)
                .Distinct()
                .ToList();
            Dictionary<string, string> deviationTypes;
            try
            {
                deviationTypes = findingDeviationIds.Count > 0
                    ? await context.Deviations
                        .Where(d => findingDeviationIds.Contains(d.Id))
                        .Select(d => new { d.Id, Type = d.DeviationType.ToString() })
                        .ToDictionaryAsync(d => d.Id, d => d.Type, ct)
                    : new Dictionary<string, string>();
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                deviationTypes = new Dictionary<string, string>();
            }

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
                    DeviationId = f.DeviationId,
                    DeviationType = f.DeviationId != null && deviationTypes.TryGetValue(f.DeviationId, out var dt) ? dt : null,
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

        // ─── Component Risk Summary (Feature 040 US6) ─────────────────────────

        app.MapGet("/api/dashboard/systems/{systemId}/assessments/{assessmentId}/component-risks", async (
            string systemId,
            string assessmentId,
            ComponentService componentService,
            CancellationToken ct) =>
        {
            var result = await componentService.GetComponentRiskSummaryAsync(systemId, assessmentId, ct);
            return Results.Ok(result);
        })
        .WithName("GetAssessmentComponentRisks");

        // ─── Assessment Findings with optional componentId filter (Feature 040 US6) ──

        app.MapGet("/api/dashboard/systems/{systemId}/assessments/{assessmentId}/findings", async (
            string systemId,
            string assessmentId,
            string? componentId,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var assessment = await context.Assessments
                .Include(a => a.Findings)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == assessmentId && a.RegisteredSystemId == systemId, ct);
            if (assessment is null)
                return Results.NotFound(new { error = "Assessment not found" });

            IEnumerable<ComplianceFinding> findings = assessment.Findings;

            if (componentId == "unlinked")
                findings = findings.Where(f => f.ComponentId == null);
            else if (!string.IsNullOrEmpty(componentId))
                findings = findings.Where(f => f.ComponentId == componentId);

            var dtos = findings.OrderBy(f => f.ControlId).Select(f => new AssessmentFindingDto
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
                DeviationId = f.DeviationId,
                DeviationType = null,
            }).ToList();

            return Results.Ok(new { items = dtos, totalCount = dtos.Count });
        })
        .WithName("GetAssessmentFindings");

        // ─── Resolve Finding Components (Feature 040 US6) ─────────────────────

        app.MapPost("/api/dashboard/systems/{systemId}/resolve-finding-components", async (
            string systemId,
            ComponentService componentService,
            CancellationToken ct) =>
        {
            var linked = await componentService.ResolveFindingComponentsAsync(systemId, ct);
            return Results.Ok(new { linkedCount = linked });
        })
        .WithName("ResolveFindingComponents");

        app.MapPost("/api/dashboard/systems/{systemId}/components/{componentId}/relink-findings", async (
            string systemId,
            string componentId,
            ComponentService componentService,
            CancellationToken ct) =>
        {
            var linked = await componentService.RelinkComponentFindingsAsync(systemId, componentId, ct);
            return Results.Ok(new { linkedCount = linked });
        })
        .WithName("RelinkComponentFindings");

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
                    .ToDictionary(t => t.FindingId!, t => t);

                foreach (var poam in poamItems)
                {
                    if (poam.FindingId != null && tasksByFinding.TryGetValue(poam.FindingId, out var task))
                    {
                        poam.RemediationTaskId = task.Id;
                        task.PoamItemId = poam.Id;
                    }
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

        // List NIST controls that don't yet have a narrative for this system
        app.MapGet("/api/dashboard/systems/{systemId}/available-controls", async (
            string systemId,
            string? search,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var existingControlIds = await context.ControlImplementations
                .Where(ci => ci.RegisteredSystemId == systemId)
                .Select(ci => ci.ControlId)
                .ToListAsync(ct);

            var query = context.NistControls.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(search))
                query = query.Where(n => n.Id.Contains(search) || n.Title.Contains(search));

            var controls = await query
                .Where(n => !existingControlIds.Contains(n.Id))
                .OrderBy(n => n.Family).ThenBy(n => n.Id)
                .Select(n => new { n.Id, n.Family, n.Title })
                .Take(200)
                .ToListAsync(ct);

            return Results.Ok(controls);
        })
        .WithName("ListAvailableControls");

        // Create a new narrative (ControlImplementation) for a control
        app.MapPost("/api/dashboard/systems/{systemId}/narratives", async (
            string systemId,
            CreateNarrativeRequest request,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            // Validate the control exists
            var control = await context.NistControls
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == request.ControlId, ct);
            if (control is null)
                return Results.NotFound(new ErrorResponse { Error = "NIST control not found", ErrorCode = "CONTROL_NOT_FOUND" });

            // Check for duplicate
            var exists = await context.ControlImplementations
                .AnyAsync(ci => ci.RegisteredSystemId == systemId && ci.ControlId == request.ControlId, ct);
            if (exists)
                return Results.Conflict(new ErrorResponse { Error = "Narrative already exists for this control", ErrorCode = "DUPLICATE" });

            var now = DateTime.UtcNow;
            var impl = new ControlImplementation
            {
                ControlId = request.ControlId,
                RegisteredSystemId = systemId,
                ImplementationStatus = Enum.TryParse<ImplementationStatus>(request.ImplementationStatus, true, out var s)
                    ? s : ImplementationStatus.Planned,
                ApprovalStatus = SspSectionStatus.Draft,
                Narrative = request.Narrative,
                AiSuggested = false,
                AuthoredBy = "dashboard-user",
                AuthoredAt = now,
                CurrentVersion = 1,
            };

            context.ControlImplementations.Add(impl);
            await context.SaveChangesAsync(ct);

            return Results.Created($"/api/dashboard/systems/{systemId}/narratives", new
            {
                impl.Id,
                impl.ControlId,
                family = control.Family,
                impl.Narrative,
                implementationStatus = impl.ImplementationStatus.ToString(),
                approvalStatus = impl.ApprovalStatus.ToString(),
            });
        })
        .WithName("CreateNarrative");

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

        // ─── Save single narrative text ────────────────────────────────────
        app.MapPatch("/api/dashboard/systems/{systemId}/controls/{controlId}/narrative", async (
            string systemId,
            string controlId,
            SaveNarrativeRequest request,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var impl = await context.ControlImplementations
                .FirstOrDefaultAsync(ci => ci.RegisteredSystemId == systemId && ci.ControlId == controlId, ct);
            if (impl is null)
                return Results.NotFound(new ErrorResponse { Error = "Control implementation not found", ErrorCode = "CONTROL_NOT_FOUND" });

            impl.Narrative = request.Narrative;
            impl.AiSuggested = false;
            impl.ModifiedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(ct);

            return Results.Ok(new { controlId, narrative = impl.Narrative });
        })
        .WithName("SaveNarrativeText");

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
                    .Where(b => b.SubscriptionId == systemId)
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
                // Also include tasks linked to POA&M items for this system
                var poamTaskIds = await context.PoamItems
                    .Where(p => p.RegisteredSystemId == systemId && p.RemediationTaskId != null)
                    .Select(p => p.RemediationTaskId!)
                    .Distinct()
                    .ToListAsync(ct);
                taskQuery = taskQuery.Where(t => boardIds.Contains(t.BoardId) || poamTaskIds.Contains(t.Id));
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

            // Look up component names for findings linked to components (Feature 040 US6)
            var findingIds = tasks
                .Where(t => t.FindingId != null)
                .Select(t => t.FindingId!)
                .Distinct()
                .ToList();
            var findingComponentMap = new Dictionary<string, (string componentId, string componentName)>();
            if (findingIds.Count > 0)
            {
                var linkedFindings = await context.Findings
                    .Where(f => findingIds.Contains(f.Id) && f.ComponentId != null)
                    .Select(f => new { f.Id, f.ComponentId })
                    .AsNoTracking()
                    .ToListAsync(ct);
                if (linkedFindings.Count > 0)
                {
                    var compIds = linkedFindings.Select(f => f.ComponentId!).Distinct().ToList();
                    var compNames = await context.SystemComponents
                        .Where(c => compIds.Contains(c.Id))
                        .Select(c => new { c.Id, c.Name })
                        .AsNoTracking()
                        .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
                    foreach (var lf in linkedFindings)
                    {
                        if (compNames.TryGetValue(lf.ComponentId!, out var name))
                            findingComponentMap[lf.Id] = (lf.ComponentId!, name);
                    }
                }
            }

            return Results.Ok(new
            {
                items = tasks.Select(t =>
                {
                    findingComponentMap.TryGetValue(t.FindingId ?? "", out var comp);
                    return new
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
                        componentId = comp.componentId,
                        componentName = comp.componentName,
                    };
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

        // ─── Deviation CRUD (Feature 035) ────────────────────────────────────

        group.MapGet("/systems/{systemId}/deviations", async (
                string systemId,
                string? type,
                string? status,
                string? severity,
                string? search,
                int? expiringWithinDays,
                int? page,
                int? pageSize,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                var result = await deviationService.ListDeviationsAsync(
                    systemId, type, status, severity, search, expiringWithinDays,
                    page ?? 1, pageSize ?? 50, ct);
                return Results.Ok(result);
            })
            .WithName("ListDeviations");

        group.MapGet("/systems/{systemId}/deviations/summary", async (
                string systemId,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                var result = await deviationService.GetDeviationSummaryAsync(systemId, ct);
                return Results.Ok(result);
            })
            .WithName("GetDeviationSummary");

        group.MapGet("/deviations/{deviationId}", async (
                string deviationId,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                var detail = await deviationService.GetDeviationDetailAsync(deviationId, ct);
                if (detail is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Deviation not found",
                        ErrorCode = "DEVIATION_NOT_FOUND",
                        Suggestion = "Check the deviation ID and try again",
                    });
                return Results.Ok(detail);
            })
            .WithName("GetDeviationDetail");

        group.MapPost("/systems/{systemId}/deviations", async (
                string systemId,
                CreateDeviationRequest request,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                try
                {
                    var deviation = await deviationService.CreateDeviationAsync(
                        systemId, request, "dashboard-user", ct);
                    return Results.Created($"/api/dashboard/deviations/{deviation.Id}", deviation);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("DUPLICATE_DEVIATION"))
                {
                    return Results.Conflict(new ErrorResponse
                    {
                        Error = "Duplicate active deviation",
                        ErrorCode = "DUPLICATE_DEVIATION",
                        Details = ex.Message,
                        Suggestion = "Revoke or wait for the existing deviation to expire before creating a new one",
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "System not found",
                        ErrorCode = "SYSTEM_NOT_FOUND",
                        Suggestion = "Check the system ID and try again",
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Invalid request",
                        ErrorCode = "VALIDATION_ERROR",
                        Details = ex.Message,
                    });
                }
            })
            .WithName("CreateDeviation");

        // ─── Deviation Workflow (Feature 035) ────────────────────────────────

        group.MapPut("/deviations/{deviationId}/review", async (
                string deviationId,
                ReviewDeviationRequest request,
                string? reviewerRole,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await deviationService.ReviewDeviationAsync(
                        deviationId, request, "dashboard-user", reviewerRole ?? "ISSM", ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("NOT_PENDING"))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Deviation is not pending",
                        ErrorCode = "NOT_PENDING",
                        Details = ex.Message,
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Deviation not found",
                        ErrorCode = "DEVIATION_NOT_FOUND",
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Invalid decision",
                        ErrorCode = "INVALID_DECISION",
                        Details = ex.Message,
                    });
                }
            })
            .WithName("ReviewDeviation");

        group.MapPut("/deviations/{deviationId}/revoke", async (
                string deviationId,
                RevokeDeviationRequest request,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await deviationService.RevokeDeviationAsync(
                        deviationId, request, "dashboard-user", ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("NOT_APPROVED"))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Deviation is not approved",
                        ErrorCode = "NOT_APPROVED",
                        Details = ex.Message,
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Deviation not found",
                        ErrorCode = "DEVIATION_NOT_FOUND",
                    });
                }
            })
            .WithName("RevokeDeviation");

        group.MapPut("/deviations/{deviationId}/extend", async (
                string deviationId,
                ExtendDeviationRequest request,
                IDeviationService deviationService,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await deviationService.ExtendDeviationAsync(
                        deviationId, request, "dashboard-user", ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("NOT_APPROVED"))
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Deviation must be approved to extend",
                        ErrorCode = "NOT_APPROVED",
                        Details = ex.Message,
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Deviation not found",
                        ErrorCode = "DEVIATION_NOT_FOUND",
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Invalid extension request",
                        ErrorCode = "VALIDATION_ERROR",
                        Details = ex.Message,
                    });
                }
            })
            .WithName("ExtendDeviation");

        // ─── SSP Export (Feature 037) ─────────────────────────────────────────

        // T010: POST /systems/{systemId}/exports — enqueue SSP export
        group.MapPost("/systems/{systemId}/exports", async (
                string systemId,
                CreateExportRequest body,
                ISspExportService exportService,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var format = body.Format?.ToLowerInvariant();
                if (format is not ("docx" or "pdf" or "json"))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Invalid format",
                        ErrorCode = "INVALID_FORMAT",
                        Details = $"Format '{body.Format}' not supported.",
                        Suggestion = "Use: docx, pdf, json",
                    });

                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";

                try
                {
                    var export = await exportService.EnqueueExportAsync(systemId, format, body.TemplateId, userId, ct);
                    return Results.Accepted($"/api/dashboard/systems/{systemId}/exports/{export.Id}", new ExportSummaryDto
                    {
                        ExportId = export.Id,
                        Format = export.Format,
                        Status = export.Status,
                        GeneratedBy = export.GeneratedBy,
                        GeneratedAt = export.GeneratedAt,
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "VALIDATION_ERROR",
                    });
                }
            })
            .WithName("CreateSspExport");

        // T014: GET /systems/{systemId}/exports — list exports
        group.MapGet("/systems/{systemId}/exports", async (
                string systemId,
                string? format,
                bool? includeFailed,
                int? limit,
                int? offset,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var exports = await exportService.ListExportsAsync(
                    systemId,
                    includeFailed ?? false,
                    Math.Clamp(limit ?? 25, 1, 100),
                    Math.Max(offset ?? 0, 0),
                    ct);
                return Results.Ok(new { items = exports, totalCount = exports.Count });
            })
            .WithName("ListSspExports");

        // T015: GET /systems/{systemId}/exports/{exportId} — get export detail
        group.MapGet("/systems/{systemId}/exports/{exportId:guid}", async (
                string systemId,
                Guid exportId,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var detail = await exportService.GetExportAsync(exportId, ct);
                if (detail is null || detail.SystemId != systemId)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Export not found",
                        ErrorCode = "NOT_FOUND",
                    });
                return Results.Ok(detail);
            })
            .WithName("GetSspExport");

        // T011: GET /systems/{systemId}/exports/{exportId}/download — download file
        group.MapGet("/systems/{systemId}/exports/{exportId:guid}/download", async (
                string systemId,
                Guid exportId,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var result = await exportService.GetExportFileStreamAsync(exportId, ct);
                if (result is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Export not found",
                        ErrorCode = "NOT_FOUND",
                    });

                var (stream, fileName, contentType) = result.Value;
                if (stream is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Export file not ready or missing",
                        ErrorCode = "EXPORT_NOT_READY",
                        Suggestion = "Wait for the export to complete before downloading.",
                    });

                return Results.File(stream, contentType ?? "application/octet-stream", fileName);
            })
            .WithName("DownloadSspExport");

        // ─── SSP Template Management (Feature 037 US4) ───────────────────────

        // T022: POST /templates — upload custom DOCX template
        group.MapPost("/templates", async (
                HttpRequest request,
                ISspExportService exportService,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Request must be multipart/form-data",
                        ErrorCode = "INVALID_CONTENT_TYPE",
                    });

                var form = await request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");
                var name = form["name"].ToString();

                if (file is null || file.Length == 0)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "File is required",
                        ErrorCode = "MISSING_FILE",
                    });

                if (string.IsNullOrWhiteSpace(name))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Name is required",
                        ErrorCode = "MISSING_NAME",
                    });

                var description = form["description"].ToString();
                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";

                try
                {
                    using var stream = file.OpenReadStream();
                    var result = await exportService.UploadTemplateAsync(
                        name,
                        string.IsNullOrWhiteSpace(description) ? null : description,
                        stream,
                        file.FileName,
                        userId,
                        ct);
                    return Results.Created($"/api/dashboard/templates/{result.Id}", result);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "VALIDATION_ERROR",
                    });
                }
            })
            .WithName("UploadSspTemplate")
            .DisableAntiforgery();

        // T023: GET /templates — list templates with pagination
        group.MapGet("/templates", async (
                int? limit,
                int? offset,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var templates = await exportService.ListTemplatesAsync(
                    Math.Clamp(limit ?? 25, 1, 100),
                    Math.Max(offset ?? 0, 0),
                    ct);
                return Results.Ok(new { items = templates, totalCount = templates.Count });
            })
            .WithName("ListSspTemplates");

        // T024: DELETE /templates/{templateId} — soft-delete template
        group.MapDelete("/templates/{templateId:guid}", async (
                Guid templateId,
                HttpContext httpContext,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";
                var deleted = await exportService.DeleteTemplateAsync(templateId, userId, ct);
                return deleted ? Results.NoContent() : Results.NotFound(new ErrorResponse
                {
                    Error = "Template not found",
                    ErrorCode = "NOT_FOUND",
                });
            })
            .WithName("DeleteSspTemplate");

        // T024a: PUT /templates/{templateId} — rename/update template
        group.MapPut("/templates/{templateId:guid}", async (
                Guid templateId,
                UpdateTemplateRequest body,
                HttpContext httpContext,
                ISspExportService exportService,
                CancellationToken ct) =>
            {
                try
                {
                    var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";
                    var result = await exportService.UpdateTemplateAsync(
                        templateId, body.Name, body.Description, userId, ct);
                    return result is not null
                        ? Results.Ok(result)
                        : Results.NotFound(new ErrorResponse
                        {
                            Error = "Template not found",
                            ErrorCode = "NOT_FOUND",
                        });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "VALIDATION_ERROR",
                    });
                }
            })
            .WithName("UpdateSspTemplate");

        // ═══════════════════════════════════════════════════════════════════════
        // Feature 038 — Evidence Repository
        // ═══════════════════════════════════════════════════════════════════════

        // ─── Evidence Upload (US1 T014) ──────────────────────────────────────

        // T014: POST /systems/{systemId}/evidence — upload evidence artifact
        group.MapPost("/systems/{systemId}/evidence", async (
                string systemId,
                HttpRequest request,
                IEvidenceArtifactService evidenceService,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Request must be multipart/form-data",
                        ErrorCode = "INVALID_CONTENT_TYPE",
                    });

                var form = await request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");

                if (file is null || file.Length == 0)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "File is required and must not be empty",
                        ErrorCode = "MISSING_FILE",
                    });

                var categoryStr = form["artifactCategory"].ToString();
                if (string.IsNullOrWhiteSpace(categoryStr) ||
                    !Enum.TryParse<ArtifactCategory>(categoryStr, ignoreCase: true, out var category))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Valid artifactCategory is required",
                        ErrorCode = "INVALID_CATEGORY",
                    });

                var controlImplementationId = form["controlImplementationId"].ToString();
                var securityCapabilityId = form["securityCapabilityId"].ToString();

                if (string.IsNullOrWhiteSpace(controlImplementationId))
                    controlImplementationId = null;
                if (string.IsNullOrWhiteSpace(securityCapabilityId))
                    securityCapabilityId = null;

                if (controlImplementationId is null && securityCapabilityId is null)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Either controlImplementationId or securityCapabilityId is required",
                        ErrorCode = "MISSING_TARGET",
                    });

                var description = form["description"].ToString();
                if (string.IsNullOrWhiteSpace(description))
                    description = null;

                var collectionMethod = CollectionMethod.Manual;
                var methodStr = form["collectionMethod"].ToString();
                if (!string.IsNullOrWhiteSpace(methodStr))
                    Enum.TryParse<CollectionMethod>(methodStr, ignoreCase: true, out collectionMethod);

                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";

                try
                {
                    using var stream = file.OpenReadStream();
                    var artifact = await evidenceService.UploadAsync(
                        systemId,
                        file.FileName,
                        file.ContentType,
                        stream,
                        category,
                        userId,
                        controlImplementationId,
                        securityCapabilityId,
                        description,
                        collectionMethod,
                        ct);

                    return Results.Created($"/api/dashboard/systems/{systemId}/evidence/{artifact.Id}", new
                    {
                        artifact.Id,
                        artifact.FileName,
                        artifact.ContentType,
                        artifact.FileSizeBytes,
                        ArtifactCategory = artifact.ArtifactCategory.ToString(),
                        CollectionMethod = artifact.CollectionMethod.ToString(),
                        artifact.ContentHash,
                        artifact.UploadedBy,
                        artifact.UploadedAt,
                        artifact.ControlImplementationId,
                        artifact.SecurityCapabilityId,
                        artifact.Description,
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "VALIDATION_ERROR",
                    });
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "NOT_FOUND",
                    });
                }
            })
            .WithName("UploadEvidence")
            .DisableAntiforgery();

        // ─── Control Evidence (US1 T015) ─────────────────────────────────────

        // T015: GET /systems/{systemId}/controls/{controlId}/evidence
        group.MapGet("/systems/{systemId}/controls/{controlId}/evidence", async (
                string systemId,
                string controlId,
                IEvidenceArtifactService evidenceService,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                // Find control implementations for this control in this system
                var controlImpls = await db.ControlImplementations
                    .Where(ci => ci.RegisteredSystemId == systemId && ci.ControlId == controlId)
                    .Select(ci => ci.Id)
                    .ToListAsync(ct);

                // Direct evidence: artifacts attached to this control's implementations
                var directArtifacts = new List<EvidenceArtifact>();
                foreach (var implId in controlImpls)
                {
                    var artifacts = await evidenceService.ListForControlAsync(implId, ct);
                    directArtifacts.AddRange(artifacts);
                }

                // Inherited evidence: from capabilities linked to this control
                var capabilityIds = await db.CapabilityControlMappings
                    .Where(ccm => ccm.ControlId == controlId &&
                                  (ccm.RegisteredSystemId == null || ccm.RegisteredSystemId == systemId))
                    .Select(ccm => ccm.SecurityCapabilityId)
                    .Distinct()
                    .ToListAsync(ct);

                var inheritedArtifacts = await db.EvidenceArtifacts
                    .Where(ea => ea.RegisteredSystemId == systemId &&
                                 ea.SecurityCapabilityId != null &&
                                 capabilityIds.Contains(ea.SecurityCapabilityId) &&
                                 !ea.IsDeleted)
                    .Include(ea => ea.SecurityCapability)
                    .ToListAsync(ct);

                // Automated evidence: from ComplianceEvidence table
                var automatedEvidence = await db.Evidence
                    .Where(ce => ce.ControlId == controlId &&
                                 db.ControlImplementations.Any(ci =>
                                     ci.ControlId == ce.ControlId && ci.RegisteredSystemId == systemId))
                    .ToListAsync(ct);

                return Results.Ok(new
                {
                    direct = directArtifacts.Select(a => new
                    {
                        a.Id,
                        Source = "Manual",
                        a.FileName,
                        a.ContentType,
                        a.FileSizeBytes,
                        ArtifactCategory = a.ArtifactCategory.ToString(),
                        CollectionMethod = a.CollectionMethod.ToString(),
                        a.ContentHash,
                        a.UploadedBy,
                        a.UploadedAt,
                        a.ControlImplementationId,
                        a.Description,
                    }),
                    inherited = inheritedArtifacts.Select(a => new
                    {
                        a.Id,
                        Source = "Manual",
                        a.FileName,
                        a.ContentType,
                        a.FileSizeBytes,
                        ArtifactCategory = a.ArtifactCategory.ToString(),
                        CollectionMethod = a.CollectionMethod.ToString(),
                        a.ContentHash,
                        a.UploadedBy,
                        a.UploadedAt,
                        a.SecurityCapabilityId,
                        InheritedFromCapability = a.SecurityCapability?.Name,
                        a.Description,
                    }),
                    automated = automatedEvidence.Select(ce => new
                    {
                        ce.Id,
                        Source = "Automated",
                        FileName = (string?)null,
                        ContentType = (string?)null,
                        FileSizeBytes = (long?)null,
                        ArtifactCategory = ce.EvidenceCategory.ToString(),
                        ce.ControlId,
                        Description = $"Automated evidence for {ce.ControlId}",
                        UploadedBy = "ATO Copilot (automated)",
                        UploadedAt = ce.CollectedAt,
                        ce.ContentHash,
                    }),
                });
            })
            .WithName("GetControlEvidence");

        // ─── Evidence Download (US1 T016) ────────────────────────────────────

        // T016: GET /systems/{systemId}/evidence/{evidenceId}/download
        group.MapGet("/systems/{systemId}/evidence/{evidenceId}/download", async (
                string systemId,
                string evidenceId,
                IEvidenceArtifactService evidenceService,
                CancellationToken ct) =>
            {
                var result = await evidenceService.DownloadAsync(evidenceId, ct);
                if (result is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Evidence not found or no file available",
                        ErrorCode = "NOT_FOUND",
                    });

                var (stream, fileName, contentType) = result.Value;
                return Results.File(stream, contentType, fileName);
            })
            .WithName("DownloadEvidence");

        // ─── Evidence Repository (US2 T020) ─────────────────────────────────

        // T020: GET /systems/{systemId}/evidence — paginated evidence list
        group.MapGet("/systems/{systemId}/evidence", async (
                string systemId,
                int? page,
                int? pageSize,
                string? search,
                string? controlFamily,
                string? category,
                string? source,
                string? dateFrom,
                string? dateTo,
                string? sortBy,
                string? sortOrder,
                IEvidenceArtifactService evidenceService,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var p = Math.Max(page ?? 1, 1);
                var ps = Math.Clamp(pageSize ?? 50, 1, 100);
                var desc = string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase) ? false : true;
                ArtifactCategory? catFilter = null;
                if (!string.IsNullOrWhiteSpace(category) &&
                    Enum.TryParse<ArtifactCategory>(category, ignoreCase: true, out var parsedCat))
                    catFilter = parsedCat;

                var showManual = source is null || string.Equals(source, "manual", StringComparison.OrdinalIgnoreCase);
                var showAutomated = source is null || string.Equals(source, "automated", StringComparison.OrdinalIgnoreCase);

                var items = new List<object>();
                var totalCount = 0;

                // Manual evidence
                if (showManual)
                {
                    var (manualItems, manualTotal) = await evidenceService.ListForSystemAsync(
                        systemId, p, ps, search, controlFamily, catFilter,
                        sortBy ?? "uploadedAt", desc, ct);
                    totalCount += manualTotal;

                    items.AddRange(manualItems.Select(a => new
                    {
                        a.Id,
                        Source = "Manual",
                        a.FileName,
                        a.ContentType,
                        a.FileSizeBytes,
                        ArtifactCategory = a.ArtifactCategory.ToString(),
                        ControlId = (string?)null,
                        a.ControlImplementationId,
                        a.SecurityCapabilityId,
                        a.Description,
                        a.UploadedBy,
                        a.UploadedAt,
                        a.ContentHash,
                    }));
                }

                // Automated evidence
                if (showAutomated)
                {
                    var autoQuery = db.Evidence
                        .Where(ce => db.ControlImplementations.Any(ci =>
                            ci.ControlId == ce.ControlId && ci.RegisteredSystemId == systemId));

                    if (!string.IsNullOrWhiteSpace(controlFamily))
                        autoQuery = autoQuery.Where(ce => ce.ControlId.StartsWith(controlFamily));

                    if (!string.IsNullOrWhiteSpace(search))
                        autoQuery = autoQuery.Where(ce =>
                            ce.ControlId.Contains(search) || ce.Description.Contains(search));

                    if (!string.IsNullOrWhiteSpace(dateFrom) && DateTime.TryParse(dateFrom, out var from))
                        autoQuery = autoQuery.Where(ce => ce.CollectedAt >= from);
                    if (!string.IsNullOrWhiteSpace(dateTo) && DateTime.TryParse(dateTo, out var to))
                        autoQuery = autoQuery.Where(ce => ce.CollectedAt <= to);

                    var autoTotal = await autoQuery.CountAsync(ct);
                    totalCount += autoTotal;

                    var autoItems = await autoQuery
                        .OrderByDescending(ce => ce.CollectedAt)
                        .Skip((p - 1) * ps)
                        .Take(ps)
                        .ToListAsync(ct);

                    items.AddRange(autoItems.Select(ce => new
                    {
                        ce.Id,
                        Source = "Automated",
                        FileName = (string?)null,
                        ContentType = (string?)null,
                        FileSizeBytes = (long?)null,
                        ArtifactCategory = ce.EvidenceCategory.ToString(),
                        ControlId = (string?)ce.ControlId,
                        ControlImplementationId = (string?)null,
                        SecurityCapabilityId = (string?)null,
                        Description = (string?)ce.Description,
                        UploadedBy = "ATO Copilot (automated)",
                        UploadedAt = ce.CollectedAt,
                        ContentHash = (string?)ce.ContentHash,
                    }));
                }

                return Results.Ok(new { items, totalCount, page = p, pageSize = ps });
            })
            .WithName("ListEvidence");

        // ─── Evidence Summary (US2 T021) ─────────────────────────────────────

        // T021: GET /systems/{systemId}/evidence/summary
        group.MapGet("/systems/{systemId}/evidence/summary", async (
                string systemId,
                IEvidenceArtifactService evidenceService,
                CancellationToken ct) =>
            {
                var summary = await evidenceService.GetSummaryAsync(systemId, ct);
                return Results.Ok(new
                {
                    summary.TotalCount,
                    summary.ManualCount,
                    summary.AutomatedCount,
                    summary.ControlsWithEvidence,
                    summary.TotalControls,
                    summary.CoveragePercentage,
                });
            })
            .WithName("GetEvidenceSummary");

        // ─── Evidence Detail (US2 T022) ──────────────────────────────────────

        // T022: GET /systems/{systemId}/evidence/{evidenceId}
        group.MapGet("/systems/{systemId}/evidence/{evidenceId}", async (
                string systemId,
                string evidenceId,
                IEvidenceArtifactService evidenceService,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                // Try manual evidence first
                var artifact = await evidenceService.GetByIdAsync(evidenceId, ct);
                if (artifact is not null && artifact.RegisteredSystemId == systemId)
                {
                    var versions = await db.EvidenceVersions
                        .Where(v => v.EvidenceArtifactId == evidenceId)
                        .OrderByDescending(v => v.ReplacedAt)
                        .Select(v => new
                        {
                            v.Id,
                            v.FileName,
                            v.FileSizeBytes,
                            v.ContentHash,
                            v.ReplacedBy,
                            v.ReplacedAt,
                            v.PurgeAfter,
                            v.IsFilePurged,
                        })
                        .ToListAsync(ct);

                    // Resolve controlId from ControlImplementation
                    string? controlId = null;
                    if (artifact.ControlImplementationId is not null)
                    {
                        controlId = await db.ControlImplementations
                            .Where(ci => ci.Id == artifact.ControlImplementationId)
                            .Select(ci => ci.ControlId)
                            .FirstOrDefaultAsync(ct);
                    }

                    return Results.Ok(new
                    {
                        artifact.Id,
                        Source = "Manual",
                        artifact.FileName,
                        artifact.ContentType,
                        artifact.FileSizeBytes,
                        artifact.StoragePath,
                        ArtifactCategory = artifact.ArtifactCategory.ToString(),
                        CollectionMethod = artifact.CollectionMethod.ToString(),
                        ControlId = controlId,
                        artifact.ControlImplementationId,
                        artifact.SecurityCapabilityId,
                        CapabilityName = artifact.SecurityCapability?.Name,
                        artifact.Description,
                        artifact.UploadedBy,
                        artifact.UploadedAt,
                        artifact.ContentHash,
                        Versions = versions,
                    });
                }

                // Try automated evidence
                var compEvidence = await db.Evidence
                    .FirstOrDefaultAsync(ce => ce.Id == evidenceId, ct);

                if (compEvidence is not null)
                {
                    return Results.Ok(new
                    {
                        compEvidence.Id,
                        Source = "Automated",
                        FileName = (string?)null,
                        ContentType = (string?)null,
                        FileSizeBytes = (long?)null,
                        StoragePath = (string?)null,
                        ArtifactCategory = compEvidence.EvidenceCategory.ToString(),
                        CollectionMethod = (string?)compEvidence.CollectionMethod,
                        ControlId = (string?)compEvidence.ControlId,
                        ControlImplementationId = (string?)null,
                        SecurityCapabilityId = (string?)null,
                        CapabilityName = (string?)null,
                        Description = (string?)compEvidence.Description,
                        UploadedBy = "ATO Copilot (automated)",
                        UploadedAt = compEvidence.CollectedAt,
                        ContentHash = (string?)compEvidence.ContentHash,
                        Versions = Array.Empty<object>(),
                    });
                }

                return Results.NotFound(new ErrorResponse
                {
                    Error = "Evidence not found",
                    ErrorCode = "NOT_FOUND",
                });
            })
            .WithName("GetEvidenceDetail");

        // ─── Collect Evidence Trigger (US5 T033) ────────────────────────────

        // T033: POST /systems/{systemId}/controls/{controlId}/collect-evidence
        group.MapPost("/systems/{systemId}/controls/{controlId}/collect-evidence", async (
                string systemId,
                string controlId,
                IEvidenceStorageService evidenceStorageService,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                // Resolve a subscription ID from the system's Azure profile
                var system = await db.RegisteredSystems
                    .Where(s => s.Id == systemId)
                    .FirstOrDefaultAsync(ct);

                var subscriptionId = system?.AzureProfile?.SubscriptionIds?.FirstOrDefault();

                if (string.IsNullOrEmpty(subscriptionId))
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "No Azure subscription found for this system. Configure an authorization boundary with a subscription ID.",
                        ErrorCode = "NO_SUBSCRIPTION",
                    });

                try
                {
                    var evidence = await evidenceStorageService.CollectEvidenceAsync(
                        controlId, subscriptionId, cancellationToken: ct);

                    return Results.Ok(new
                    {
                        EvidenceId = evidence.Id,
                        evidence.ControlId,
                        evidence.EvidenceType,
                        CollectedAt = evidence.CollectedAt,
                        evidence.ContentHash,
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Results.StatusCode(502);
                }
            })
            .WithName("CollectEvidence");

        // ─── Delete Evidence (US6 T035) ─────────────────────────────────────

        // T035: DELETE /systems/{systemId}/evidence/{evidenceId}
        group.MapDelete("/systems/{systemId}/evidence/{evidenceId}", async (
                string systemId,
                string evidenceId,
                IEvidenceArtifactService evidenceService,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";
                var deleted = await evidenceService.DeleteAsync(evidenceId, userId, ct);

                return deleted
                    ? Results.NoContent()
                    : Results.NotFound(new ErrorResponse
                    {
                        Error = "Evidence not found",
                        ErrorCode = "NOT_FOUND",
                    });
            })
            .WithName("DeleteEvidence");

        // ─── Replace Evidence (US6 T036) ─────────────────────────────────────

        // T036: PUT /systems/{systemId}/evidence/{evidenceId}
        group.MapPut("/systems/{systemId}/evidence/{evidenceId}", async (
                string systemId,
                string evidenceId,
                HttpRequest request,
                IEvidenceArtifactService evidenceService,
                HttpContext httpContext,
                CancellationToken ct) =>
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "Request must be multipart/form-data",
                        ErrorCode = "INVALID_CONTENT_TYPE",
                    });

                var form = await request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");

                if (file is null || file.Length == 0)
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = "File is required",
                        ErrorCode = "MISSING_FILE",
                    });

                var description = form["description"].ToString();
                if (string.IsNullOrWhiteSpace(description))
                    description = null;

                var userId = httpContext.User?.Identity?.Name ?? "dashboard-user";

                try
                {
                    using var stream = file.OpenReadStream();
                    var artifact = await evidenceService.ReplaceAsync(
                        evidenceId,
                        file.FileName,
                        file.ContentType,
                        stream,
                        userId,
                        description: description,
                        cancellationToken: ct);

                    return Results.Ok(new
                    {
                        artifact.Id,
                        artifact.FileName,
                        artifact.ContentType,
                        artifact.FileSizeBytes,
                        ArtifactCategory = artifact.ArtifactCategory.ToString(),
                        CollectionMethod = artifact.CollectionMethod.ToString(),
                        artifact.ContentHash,
                        artifact.UploadedBy,
                        artifact.UploadedAt,
                        artifact.ControlImplementationId,
                        artifact.SecurityCapabilityId,
                        artifact.Description,
                    });
                }
                catch (KeyNotFoundException ex)
                {
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "NOT_FOUND",
                    });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new ErrorResponse
                    {
                        Error = ex.Message,
                        ErrorCode = "VALIDATION_ERROR",
                    });
                }
            })
            .WithName("ReplaceEvidence")
            .DisableAntiforgery();

        // ─── Version History (US6 T037) ──────────────────────────────────────

        // T037: GET /systems/{systemId}/evidence/{evidenceId}/versions
        group.MapGet("/systems/{systemId}/evidence/{evidenceId}/versions", async (
                string systemId,
                string evidenceId,
                AtoCopilotContext db,
                CancellationToken ct) =>
            {
                var versions = await db.EvidenceVersions
                    .Where(v => v.EvidenceArtifactId == evidenceId)
                    .OrderByDescending(v => v.ReplacedAt)
                    .Select(v => new
                    {
                        v.Id,
                        v.FileName,
                        v.FileSizeBytes,
                        v.ContentHash,
                        v.ReplacedBy,
                        v.ReplacedAt,
                        v.PurgeAfter,
                        v.IsFilePurged,
                    })
                    .ToListAsync(ct);
                return Results.Ok(versions);
            })
            .WithName("GetEvidenceVersions");

        // T037b: GET /systems/{systemId}/evidence/{evidenceId}/versions/{versionId}/download
        group.MapGet("/systems/{systemId}/evidence/{evidenceId}/versions/{versionId}/download", async (
                string systemId,
                string evidenceId,
                string versionId,
                AtoCopilotContext db,
                Core.Interfaces.Storage.IFileStorageProvider storageProvider,
                CancellationToken ct) =>
            {
                var version = await db.EvidenceVersions
                    .FirstOrDefaultAsync(v => v.Id == versionId && v.EvidenceArtifactId == evidenceId, ct);

                if (version is null)
                    return Results.NotFound(new ErrorResponse
                    {
                        Error = "Version not found",
                        ErrorCode = "NOT_FOUND",
                    });

                if (version.IsFilePurged)
                    return Results.StatusCode(410); // Gone

                var exists = await storageProvider.ExistsAsync(version.StoragePath, ct);
                if (!exists)
                    return Results.StatusCode(410);

                var stream = await storageProvider.GetAsync(version.StoragePath, ct);
                return Results.File(stream, "application/octet-stream", version.FileName);
            })
            .WithName("DownloadEvidenceVersion");

        // ── GET /evidence/settings ─────────────────────────────────────────
        group.MapGet("/evidence/settings", (IConfiguration configuration) =>
            {
                return Results.Ok(new
                {
                    StorageProvider = configuration.GetValue<string>("Evidence:StorageProvider") ?? "Local",
                    RetentionDays = configuration.GetValue<int>("Evidence:RetentionDays") is > 0 and var rd ? rd : 365,
                    LocalStoragePath = configuration.GetValue<string>("Evidence:LocalStoragePath") ?? "/data/evidence",
                    PurgeIntervalHours = configuration.GetValue<int>("Evidence:PurgeIntervalHours") is > 0 and var pi ? pi : 24,
                });
            })
            .WithName("GetEvidenceSettings");

        // ═══════════════════════════════════════════════════════════════════════
        // POA&M Management Endpoints (Feature 039)
        // ═══════════════════════════════════════════════════════════════════════

        // ── GET /systems/{systemId}/poam — list POA&M items (paginated, filtered)
        group.MapGet("/systems/{systemId}/poam", async (
            string systemId,
            int? page, int? pageSize, string? sortBy, string? sortDirection,
            string? status, string? catSeverity, bool? overdue,
            string? componentId, string? search,
            PoamService poamService,
            CancellationToken ct) =>
        {
            PoamStatus? statusFilter = null;
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<PoamStatus>(status, ignoreCase: true, out var s))
                statusFilter = s;

            CatSeverity? sevFilter = null;
            if (!string.IsNullOrEmpty(catSeverity))
                sevFilter = catSeverity.ToUpperInvariant() switch
                {
                    "I" or "CATI" => CatSeverity.CatI,
                    "II" or "CATII" => CatSeverity.CatII,
                    "III" or "CATIII" => CatSeverity.CatIII,
                    _ => null
                };

            var (items, totalCount) = await poamService.ListAsync(
                systemId, page ?? 1, pageSize ?? 25, sortBy ?? "scheduledCompletionDate",
                sortDirection ?? "asc", statusFilter, sevFilter, overdue, componentId, search, ct);

            var effectivePageSize = Math.Clamp(pageSize ?? 25, 1, 100);
            return Results.Ok(new
            {
                items = items.Select(MapToListItem),
                totalCount,
                page = page ?? 1,
                pageSize = effectivePageSize,
                totalPages = effectivePageSize > 0 ? (int)Math.Ceiling((double)totalCount / effectivePageSize) : 0
            });
        }).WithName("ListPoamItemsV2");

        // ── GET /poam — cross-system POA&M list
        group.MapGet("/poam", async (
            int? page, int? pageSize, string? sortBy, string? sortDirection,
            string? status, string? catSeverity, bool? overdue,
            string? componentId, string? search, string? systemId,
            PoamService poamService,
            CancellationToken ct) =>
        {
            PoamStatus? statusFilter = null;
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<PoamStatus>(status, ignoreCase: true, out var s))
                statusFilter = s;

            CatSeverity? sevFilter = null;
            if (!string.IsNullOrEmpty(catSeverity))
                sevFilter = catSeverity.ToUpperInvariant() switch
                {
                    "I" or "CATI" => CatSeverity.CatI,
                    "II" or "CATII" => CatSeverity.CatII,
                    "III" or "CATIII" => CatSeverity.CatIII,
                    _ => null
                };

            var (items, totalCount) = await poamService.ListAsync(
                systemId, page ?? 1, pageSize ?? 25, sortBy ?? "scheduledCompletionDate",
                sortDirection ?? "asc", statusFilter, sevFilter, overdue, componentId, search, ct);

            var effectivePageSize = Math.Clamp(pageSize ?? 25, 1, 100);
            return Results.Ok(new
            {
                items = items.Select(MapToListItem),
                totalCount,
                page = page ?? 1,
                pageSize = effectivePageSize,
                totalPages = effectivePageSize > 0 ? (int)Math.Ceiling((double)totalCount / effectivePageSize) : 0
            });
        }).WithName("ListPoamItemsCrossSystem");

        // ── GET /poam/{poamId} — detail
        group.MapGet("/poam/{poamId}", async (
            string poamId, PoamService poamService, CancellationToken ct) =>
        {
            var poam = await poamService.GetByIdAsync(poamId, includeHistory: true, ct);
            if (poam == null) return Results.NotFound(new ErrorResponse
            {
                Error = "POA&M item not found.",
                ErrorCode = "POAM_NOT_FOUND"
            });

            return Results.Ok(MapToDetail(poam));
        }).WithName("GetPoamDetail");

        // ── POST /systems/{systemId}/poam — create
        group.MapPost("/systems/{systemId}/poam", async (
            string systemId, Feature039CreatePoamRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Weakness))
                return Results.BadRequest(new ErrorResponse { Error = "Weakness is required.", ErrorCode = "INVALID_INPUT" });
            if (string.IsNullOrWhiteSpace(req.ControlId))
                return Results.BadRequest(new ErrorResponse { Error = "ControlId is required.", ErrorCode = "INVALID_INPUT" });
            if (string.IsNullOrWhiteSpace(req.Poc))
                return Results.BadRequest(new ErrorResponse { Error = "POC is required.", ErrorCode = "INVALID_INPUT" });

            if (!Enum.TryParse<CatSeverity>("Cat" + req.CatSeverity, ignoreCase: true, out var sevEnum))
                return Results.BadRequest(new ErrorResponse { Error = $"Invalid CatSeverity: {req.CatSeverity}.", ErrorCode = "INVALID_INPUT" });

            var milestones = req.Milestones?.Select(m => (m.Description, m.TargetDate));

            var poam = await poamService.CreateAsync(
                systemId, req.Weakness, req.WeaknessSource ?? "Manual", req.ControlId,
                sevEnum, req.Poc, req.ScheduledCompletionDate,
                req.PocEmail, req.ResourcesRequired, req.CostEstimate, req.Comments,
                req.FindingId, "mcp-user", req.ComponentIds, milestones, ct);

            return Results.Created($"/api/dashboard/poam/{poam.Id}", MapToDetail(poam));
        }).WithName("CreatePoamItemV2");

        // ── PUT /poam/{poamId} — update
        group.MapPut("/poam/{poamId}", async (
            string poamId, Feature039UpdatePoamRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (!Guid.TryParse(req.RowVersion, out var rv))
                return Results.BadRequest(new ErrorResponse { Error = "Valid rowVersion is required.", ErrorCode = "INVALID_INPUT" });

            try
            {
                var updated = await poamService.UpdateAsync(poamId, rv, poam =>
                {
                    if (req.Weakness != null) poam.Weakness = req.Weakness;
                    if (req.ControlId != null) poam.SecurityControlNumber = req.ControlId;
                    if (req.Poc != null) poam.PointOfContact = req.Poc;
                    if (req.PocEmail != null) poam.PocEmail = req.PocEmail;
                    if (req.Comments != null) poam.Comments = req.Comments;
                    if (req.ResourcesRequired != null) poam.ResourcesRequired = req.ResourcesRequired;
                    if (req.ScheduledCompletionDate.HasValue) poam.ScheduledCompletionDate = req.ScheduledCompletionDate.Value;
                    if (req.CostEstimate.HasValue) poam.CostEstimate = req.CostEstimate;
                }, ct: ct);

                return Results.Ok(MapToDetail(updated));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("CONCURRENCY"))
            {
                return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_CONCURRENCY_CONFLICT" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_NOT_FOUND" });
            }
        }).WithName("UpdatePoamItem");

        // ── GET /systems/{systemId}/poam/metrics
        group.MapGet("/systems/{systemId}/poam/metrics", async (
            string systemId, PoamService poamService, CancellationToken ct) =>
        {
            var metrics = await poamService.GetMetricsAsync(systemId, ct);
            return Results.Ok(metrics);
        }).WithName("GetPoamMetrics");

        // ── GET /poam/metrics — cross-system
        group.MapGet("/poam/metrics", async (
            PoamService poamService, CancellationToken ct) =>
        {
            var metrics = await poamService.GetMetricsAsync(null, ct);
            return Results.Ok(metrics);
        }).WithName("GetPoamMetricsCrossSystem");

        // ── POST /systems/{systemId}/poam/bulk-create — bulk create from findings
        group.MapPost("/systems/{systemId}/poam/bulk-create", async (
            string systemId, Feature039BulkCreateRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (req.FindingIds == null || req.FindingIds.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one findingId is required.", ErrorCode = "INVALID_INPUT" });

            var result = await poamService.BulkCreateFromFindingsAsync(
                systemId, req.FindingIds, req.ComponentIds, req.LinkRemediationTasks, "dashboard-user", ct);

            return Results.Ok(new
            {
                created = result.Created,
                skippedDuplicates = result.SkippedDuplicates,
                results = result.Results.Select(r => new
                {
                    findingId = r.FindingId,
                    poamId = r.PoamId,
                    status = r.Status
                })
            });
        }).WithName("BulkCreatePoamFromFindings");

        // ── PUT /poam/{poamId}/status — lifecycle status change
        group.MapPut("/poam/{poamId}/status", async (
            string poamId, Feature039StatusUpdateRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (!Guid.TryParse(req.RowVersion, out var rv))
                return Results.BadRequest(new ErrorResponse { Error = "Valid rowVersion is required.", ErrorCode = "INVALID_INPUT" });

            if (!Enum.TryParse<PoamStatus>(req.Status, ignoreCase: true, out var newStatus))
                return Results.BadRequest(new ErrorResponse { Error = $"Invalid status: {req.Status}.", ErrorCode = "INVALID_INPUT" });

            try
            {
                var updated = await poamService.UpdateStatusAsync(
                    poamId, newStatus, rv, "dashboard-user",
                    req.DelayReason, req.RevisedDate.HasValue ? req.RevisedDate.Value : null,
                    req.DeviationId, req.Comments, req.CascadeToTask, ct);

                return Results.Ok(new { poam = MapToDetail(updated) });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("CONCURRENCY"))
            {
                return Results.Conflict(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_CONCURRENCY_CONFLICT" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("INVALID_TRANSITION") ||
                                                       ex.Message.Contains("REQUIRED") ||
                                                       ex.Message.Contains("DEVIATION"))
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_LIFECYCLE_ERROR" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_NOT_FOUND" });
            }
        }).WithName("UpdatePoamStatusV2");

        // ── POST /poam/bulk-status — bulk status updates
        group.MapPost("/poam/bulk-status", async (
            Feature039BulkStatusRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (req.PoamIds == null || req.PoamIds.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one poamId is required.", ErrorCode = "INVALID_INPUT" });

            if (!Enum.TryParse<PoamStatus>(req.Status, ignoreCase: true, out var newStatus))
                return Results.BadRequest(new ErrorResponse { Error = $"Invalid status: {req.Status}.", ErrorCode = "INVALID_INPUT" });

            var results = await poamService.BulkUpdateStatusAsync(
                req.PoamIds, newStatus, "dashboard-user", req.DelayReason, req.RevisedDate, req.Comments, ct);

            return Results.Ok(new
            {
                succeeded = results.Count(r => r.Success),
                failed = results.Count(r => !r.Success),
                results = results.Select(r => new { poamId = r.PoamId, success = r.Success, error = r.Error })
            });
        }).WithName("BulkUpdatePoamStatusV2");

        // ── POST /poam/{poamId}/components — link components
        group.MapPost("/poam/{poamId}/components", async (
            string poamId, Feature039LinkComponentsRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (req.ComponentIds == null || req.ComponentIds.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one componentId is required.", ErrorCode = "INVALID_INPUT" });

            try
            {
                await poamService.LinkComponentsAsync(poamId, req.ComponentIds, "dashboard-user", ct);
                return Results.Ok(new { linked = req.ComponentIds.Count });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_NOT_FOUND" });
            }
        }).WithName("LinkPoamComponents");

        // ── DELETE /poam/{poamId}/components — unlink components
        group.MapDelete("/poam/{poamId}/components", async (
            string poamId, [FromBody] Feature039UnlinkComponentsRequest req, PoamService poamService, CancellationToken ct) =>
        {
            if (req.ComponentIds == null || req.ComponentIds.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one componentId is required.", ErrorCode = "INVALID_INPUT" });

            try
            {
                await poamService.UnlinkComponentsAsync(poamId, req.ComponentIds, "dashboard-user", ct);
                return Results.Ok(new { unlinked = req.ComponentIds.Count });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "POAM_NOT_FOUND" });
            }
        }).WithName("UnlinkPoamComponents");

        // ── GET /components/{componentId}/poam — POA&Ms by component with risk summary
        group.MapGet("/components/{componentId}/poam", async (
            string componentId, PoamService poamService, CancellationToken ct) =>
        {
            var summary = await poamService.GetPoamsByComponentAsync(componentId, ct);
            return Results.Ok(new
            {
                componentId = summary.ComponentId,
                totalPoams = summary.TotalPoams,
                openCount = summary.OpenCount,
                overdueCount = summary.OverdueCount,
                highestSeverity = summary.HighestSeverity?.ToString().Replace("Cat", ""),
                items = summary.Items.Select(MapToListItem)
            });
        }).WithName("GetPoamsByComponent");

        // ── POST /poam/{poamId}/task — create remediation task from POA&M
        group.MapPost("/poam/{poamId}/task", async (
            string poamId, Feature039CreateTaskRequest req, PoamSyncService syncService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.BoardId))
                return Results.BadRequest(new ErrorResponse { Error = "boardId is required.", ErrorCode = "INVALID_INPUT" });

            try
            {
                var task = await syncService.CreateTaskFromPoamAsync(poamId, req.BoardId, "dashboard-user", ct);
                return Results.Ok(new { taskId = task.Id, poamId, linked = true });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "NOT_FOUND" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
            }
        }).WithName("CreateTaskFromPoam");

        // ── POST /poam/{poamId}/link-task — link existing task to POA&M
        group.MapPost("/poam/{poamId}/link-task", async (
            string poamId, Feature039LinkTaskRequest req, PoamSyncService syncService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.TaskId))
                return Results.BadRequest(new ErrorResponse { Error = "taskId is required.", ErrorCode = "INVALID_INPUT" });

            try
            {
                await syncService.LinkAsync(poamId, req.TaskId, "dashboard-user", ct);
                return Results.Ok(new { poamId, taskId = req.TaskId, linked = true });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "NOT_FOUND" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
            }
        }).WithName("LinkPoamTask");

        // ── DELETE /poam/{poamId}/unlink-task — unlink task from POA&M
        group.MapDelete("/poam/{poamId}/unlink-task", async (
            string poamId, PoamSyncService syncService, CancellationToken ct) =>
        {
            try
            {
                await syncService.UnlinkAsync(poamId, "dashboard-user", ct);
                return Results.Ok(new { poamId, unlinked = true });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "NOT_FOUND" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
            }
        }).WithName("UnlinkPoamTask");

        // ── GET /systems/{systemId}/poam/trend — trend analysis
        group.MapGet("/systems/{systemId}/poam/trend", async (
            string systemId,
            string? period,
            DateTime? startDate,
            DateTime? endDate,
            PoamService poamService,
            CancellationToken ct) =>
        {
            var trend = await poamService.GetTrendDataAsync(
                systemId, period ?? "monthly", startDate, endDate, ct);
            return Results.Ok(trend);
        }).WithName("GetPoamTrend");

        // ── GET /systems/{systemId}/poam/trend/export — PDF export
        group.MapGet("/systems/{systemId}/poam/trend/export", async (
            string systemId,
            string? period,
            DateTime? startDate,
            DateTime? endDate,
            PoamService poamService,
            CancellationToken ct) =>
        {
            var pdf = await poamService.ExportTrendReportPdfAsync(
                systemId, period ?? "monthly", startDate, endDate, ct);
            return Results.File(pdf, "application/pdf", $"poam-trend-{systemId}-{DateTime.UtcNow:yyyyMMdd}.pdf");
        }).WithName("ExportPoamTrendPdf");

        // ── GET /systems/{systemId}/ticketing — get config
        group.MapGet("/systems/{systemId}/ticketing", async (
            string systemId, TicketingService ticketingService, CancellationToken ct) =>
        {
            var config = await ticketingService.GetConfigAsync(systemId, ct);
            if (config == null) return Results.Ok(new { configured = false });
            return Results.Ok(new
            {
                configured = true,
                provider = config.Provider.ToString(),
                baseUrl = config.BaseUrl,
                projectKey = config.ProjectKeyOrTableName,
                syncEnabled = config.SyncEnabled,
            });
        }).WithName("GetTicketingConfig");

        // ── POST /systems/{systemId}/ticketing — configure
        group.MapPost("/systems/{systemId}/ticketing", async (
            string systemId, ConfigureTicketingRequest req, TicketingService ticketingService, CancellationToken ct) =>
        {
            try
            {
                if (!Enum.TryParse<Ato.Copilot.Core.Models.Poam.TicketingProvider>(req.Provider, true, out var provider))
                    return Results.BadRequest(new ErrorResponse { Error = $"Invalid provider: {req.Provider}", ErrorCode = "INVALID_INPUT" });

                var config = await ticketingService.ConfigureAsync(
                    systemId, provider, req.BaseUrl, req.ProjectKey, req.ApiKeySecretName, req.SyncEnabled, ct);
                return Results.Ok(new { configured = true, provider = config.Provider.ToString() });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
            }
        }).WithName("ConfigureTicketing");

        // ── POST /poam/{poamId}/sync-ticket — single sync
        group.MapPost("/poam/{poamId}/sync-ticket", async (
            string poamId, SyncTicketRequest? req, TicketingService ticketingService, CancellationToken ct) =>
        {
            try
            {
                var result = await ticketingService.SyncTicketAsync(poamId, req?.Direction ?? "push", ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "NOT_FOUND" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
            }
        }).WithName("SyncPoamTicket");

        // ── POST /systems/{systemId}/poam/bulk-sync — bulk sync
        group.MapPost("/systems/{systemId}/poam/bulk-sync", async (
            string systemId, SyncTicketRequest? req, TicketingService ticketingService, CancellationToken ct) =>
        {
            try
            {
                var results = await ticketingService.BulkSyncAsync(systemId, req?.Direction ?? "push", ct);
                return Results.Ok(new
                {
                    total = results.Count,
                    succeeded = results.Count(r => r.Success),
                    failed = results.Count(r => !r.Success),
                    results,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "VALIDATION_ERROR" });
            }
        }).WithName("BulkSyncTickets");

        // ── GET /systems/{systemId}/poam/export — export POA&M data
        group.MapGet("/systems/{systemId}/poam/export", async (
            string systemId,
            string format,
            string? status,
            string? catSeverity,
            bool? includeAll,
            PoamService poamService,
            CancellationToken ct) =>
        {
            try
            {
                byte[] data;
                string contentType;
                string fileName;

                switch (format.ToLowerInvariant())
                {
                    case "emass_excel":
                        data = await poamService.ExportEmassExcelAsync(systemId, status, catSeverity, includeAll ?? false, ct);
                        contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                        fileName = $"poam-{systemId}-{DateTime.UtcNow:yyyy-MM-dd}.xlsx";
                        break;
                    case "oscal_json":
                        data = await poamService.ExportOscalJsonAsync(systemId, status, catSeverity, includeAll ?? false, ct);
                        contentType = "application/json";
                        fileName = $"poam-{systemId}-{DateTime.UtcNow:yyyy-MM-dd}.oscal.json";
                        break;
                    case "csv":
                        data = await poamService.ExportCsvAsync(systemId, status, catSeverity, includeAll ?? false, ct);
                        contentType = "text/csv";
                        fileName = $"poam-{systemId}-{DateTime.UtcNow:yyyy-MM-dd}.csv";
                        break;
                    default:
                        return Results.BadRequest(new ErrorResponse { Error = $"Unsupported export format: {format}", ErrorCode = "EXPORT_FORMAT_INVALID" });
                }

                return Results.File(data, contentType, fileName);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        }).WithName("ExportPoam");

        // ─── Component-Centric Boundary: Azure Discovery (Feature 040) ───────

        group.MapPost("/components/discover-azure", async (
                DiscoverAzureComponentsRequest body,
                AzureResourceDiscoveryService discoveryService,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.SubscriptionId))
                    return Results.BadRequest(new ErrorResponse { Error = "subscriptionId is required", ErrorCode = "INVALID_INPUT" });

                try
                {
                    var result = await discoveryService.DiscoverForComponentsAsync(
                        body.SubscriptionId, systemId: null,
                        body.ResourceGroupFilter, body.ResourceTypeFilter, body.SearchFilter, body.Cursor, ct);

                    return Results.Ok(new
                    {
                        resources = result.Resources.Select(r => new
                        {
                            resourceId = r.ResourceId, name = r.Name, type = r.Type,
                            resourceGroup = r.ResourceGroup, location = r.Location,
                            alreadyImported = r.AlreadyImported,
                        }),
                        nextCursor = result.NextCursor,
                        totalCount = result.TotalCount,
                        failedResourceGroups = result.FailedResourceGroups,
                    });
                }
                catch (Azure.Identity.CredentialUnavailableException)
                {
                    return Results.Json(new { error = "Azure credentials not configured. Run 'az login' (use 'az cloud set --name AzureUSGovernment' for GovCloud) or configure service principal environment variables.", errorCode = "AZURE_AUTH_FAILED" }, statusCode: 502);
                }
                catch (Azure.Identity.AuthenticationFailedException)
                {
                    return Results.Json(new { error = "Azure authentication failed for both Government and Commercial clouds. Run 'az login' with the correct cloud.", errorCode = "AZURE_AUTH_FAILED" }, statusCode: 502);
                }
                catch (Azure.RequestFailedException ex) when (ex.Status is 401 or 403)
                {
                    return Results.Json(new { error = $"Azure RBAC denied ({ex.ErrorCode}). Ensure Reader role is assigned on the subscription.", errorCode = "AZURE_RBAC_DENIED" }, statusCode: ex.Status);
                }
            })
            .WithName("DiscoverAzureResourcesForComponents");

        group.MapPost("/components/import-azure", async (
                ImportAzureComponentsRequest body,
                ComponentService componentService,
                CancellationToken ct) =>
            {
                if (body.Resources == null || body.Resources.Count == 0)
                    return Results.BadRequest(new ErrorResponse { Error = "resources is required", ErrorCode = "INVALID_INPUT" });

                var resources = body.Resources.Select(r => new AzureImportResource
                {
                    ResourceId = r.ResourceId, Name = r.Name, Type = r.Type,
                    ResourceGroup = r.ResourceGroup, Location = r.Location,
                }).ToList();

                var result = await componentService.ImportAzureComponentsAsync(resources, "dashboard-user", ct);

                return Results.Ok(new
                {
                    imported = result.Imported,
                    skipped = result.Skipped,
                    skippedDetails = result.SkippedDetails.Select(s => new { resourceId = s.ResourceId, reason = s.Reason }),
                    components = result.Components.Select(c => new { id = c.Id, name = c.Name, componentType = c.ComponentType, azureResourceId = c.BoundaryDefinitionId }),
                });
            })
            .WithName("ImportAzureComponents");

        group.MapPost("/systems/{systemId}/components/discover-azure", async (
                string systemId,
                DiscoverAzureComponentsRequest body,
                AzureResourceDiscoveryService discoveryService,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.SubscriptionId))
                    return Results.BadRequest(new ErrorResponse { Error = "subscriptionId is required", ErrorCode = "INVALID_INPUT" });

                try
                {
                    var result = await discoveryService.DiscoverForComponentsAsync(
                        body.SubscriptionId, systemId: systemId,
                        body.ResourceGroupFilter, body.ResourceTypeFilter, body.SearchFilter, body.Cursor, ct);

                    return Results.Ok(new
                    {
                        resources = result.Resources.Select(r => new
                        {
                            resourceId = r.ResourceId, name = r.Name, type = r.Type,
                            resourceGroup = r.ResourceGroup, location = r.Location,
                            alreadyImported = r.AlreadyImported,
                            existsInOrgLibrary = r.ExistsInOrgLibrary,
                            orgLibraryComponentId = r.OrgLibraryComponentId,
                        }),
                        nextCursor = result.NextCursor,
                        totalCount = result.TotalCount,
                        failedResourceGroups = result.FailedResourceGroups,
                    });
                }
                catch (Azure.Identity.CredentialUnavailableException)
                {
                    return Results.Json(new { error = "Azure credentials not configured. Run 'az login' (use 'az cloud set --name AzureUSGovernment' for GovCloud) or configure service principal environment variables.", errorCode = "AZURE_AUTH_FAILED" }, statusCode: 502);
                }
                catch (Azure.Identity.AuthenticationFailedException)
                {
                    return Results.Json(new { error = "Azure authentication failed for both Government and Commercial clouds. Run 'az login' with the correct cloud.", errorCode = "AZURE_AUTH_FAILED" }, statusCode: 502);
                }
                catch (Azure.RequestFailedException ex) when (ex.Status is 401 or 403)
                {
                    return Results.Json(new { error = $"Azure RBAC denied ({ex.ErrorCode}). Ensure Reader role is assigned on the subscription.", errorCode = "AZURE_RBAC_DENIED" }, statusCode: ex.Status);
                }
            })
            .WithName("DiscoverSystemAzureResources");

        group.MapPost("/systems/{systemId}/components/import-azure", async (
                string systemId,
                ImportSystemAzureComponentsRequest body,
                ComponentService componentService,
                CancellationToken ct) =>
            {
                if (body.Resources == null || body.Resources.Count == 0)
                    return Results.BadRequest(new ErrorResponse { Error = "resources is required", ErrorCode = "INVALID_INPUT" });

                var resources = body.Resources.Select(r => new AzureImportResource
                {
                    ResourceId = r.ResourceId, Name = r.Name, Type = r.Type,
                    ResourceGroup = r.ResourceGroup, Location = r.Location,
                }).ToList();

                var result = await componentService.ImportSystemAzureComponentsAsync(
                    systemId, resources, body.AssignExistingOrgComponents, "dashboard-user", ct);

                return Results.Ok(new
                {
                    imported = result.Imported,
                    assignedFromOrg = result.AssignedFromOrg,
                    skipped = result.Skipped,
                    components = result.Components.Select(c => new { id = c.Id, name = c.Name, componentType = c.ComponentType }),
                });
            })
            .WithName("ImportSystemAzureComponents");

        // ─── Entra ID Discovery Endpoints (Feature 040 — US9) ───────────────

        group.MapPost("/components/discover-entra", async (
            Ato.Copilot.Agents.Compliance.Services.EntraIdDiscoveryService entraService,
            Microsoft.Extensions.Options.IOptions<Ato.Copilot.Core.Configuration.FeatureOptions> featureOptions,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            if (!featureOptions.Value.EntraIdDiscoveryEnabled)
                return Results.Json(new { error = "Entra ID discovery is disabled", errorCode = "FEATURE_DISABLED" }, statusCode: 403);

            var result = await entraService.DiscoverUsersAndGroupsAsync(context, null, ct);
            return Results.Ok(new
            {
                items = result.Items.Select(i => new
                {
                    entraObjectId = i.EntraObjectId,
                    displayName = i.DisplayName,
                    email = i.Email,
                    kind = i.Kind,
                    department = i.Department,
                    jobTitle = i.JobTitle,
                    alreadyImported = i.AlreadyImported,
                }),
                partialFailure = result.PartialFailure,
                failureMessage = result.FailureMessage,
            });
        })
        .WithName("DiscoverEntraIdUsers");

        group.MapPost("/components/import-entra", async (
            ImportEntraComponentsRequest body,
            Microsoft.Extensions.Options.IOptions<Ato.Copilot.Core.Configuration.FeatureOptions> featureOptions,
            ComponentService componentService,
            CancellationToken ct) =>
        {
            if (!featureOptions.Value.EntraIdDiscoveryEnabled)
                return Results.Json(new { error = "Entra ID discovery is disabled", errorCode = "FEATURE_DISABLED" }, statusCode: 403);

            if (body.People == null || body.People.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "people is required", ErrorCode = "INVALID_INPUT" });

            var result = await componentService.ImportEntraIdPeopleAsync(body.People, "dashboard-user", ct);
            return Results.Ok(new { imported = result.Imported, skipped = result.Skipped });
        })
        .WithName("ImportEntraComponents");

        // ─── Boundary Component Assignment Endpoints (Feature 040 — US3) ─────

        group.MapGet("/systems/{systemId}/boundary-definitions/{boundaryId}/components", async (
            string systemId,
            string boundaryId,
            string? search,
            string? type,
            string? scope,
            int? page,
            int? pageSize,
            ComponentService componentService) =>
            {
                var query = new BoundaryComponentQuery
                {
                    Search = search,
                    TypeFilter = type,
                    ScopeFilter = scope,
                    Page = page ?? 1,
                    PageSize = pageSize ?? 50,
                };
                var result = await componentService.ListBoundaryComponentsAsync(boundaryId, query);
                return Results.Ok(result);
            })
            .WithName("ListBoundaryComponents");

        group.MapPost("/systems/{systemId}/boundary-definitions/{boundaryId}/components", async (
            string systemId,
            string boundaryId,
            AssignComponentToBoundaryRequest request,
            ComponentService componentService) =>
            {
                var (dto, error) = await componentService.AssignComponentToBoundaryAsync(
                    boundaryId,
                    request.ComponentId,
                    request.IsInScope,
                    request.ExclusionRationale,
                    request.InheritanceProvider,
                    request.CreatedBy ?? "dashboard");

                if (error == "DUPLICATE_ASSIGNMENT")
                    return Results.Conflict(new { error, message = "Component already assigned to this boundary." });
                if (error == "RATIONALE_REQUIRED")
                    return Results.BadRequest(new { error, message = "Exclusion rationale is required when component is excluded." });
                if (error == "NOT_FOUND")
                    return Results.NotFound(new { error, message = "Component not found." });

                return Results.Created($"/systems/{systemId}/boundary-definitions/{boundaryId}/components/{dto!.AssignmentId}", dto);
            })
            .WithName("AssignComponentToBoundary");

        group.MapPut("/systems/{systemId}/boundary-definitions/{boundaryId}/components/{assignmentId}", async (
            string systemId,
            string boundaryId,
            string assignmentId,
            UpdateBoundaryAssignmentRequest request,
            ComponentService componentService) =>
            {
                var (dto, error) = await componentService.UpdateBoundaryAssignmentAsync(
                    assignmentId,
                    request.IsInScope,
                    request.ExclusionRationale,
                    request.InheritanceProvider,
                    request.ModifiedBy ?? "dashboard");

                if (error == "RATIONALE_REQUIRED")
                    return Results.BadRequest(new { error, message = "Exclusion rationale is required when component is excluded." });
                if (error == "NOT_FOUND")
                    return Results.NotFound(new { error, message = "Assignment not found." });

                return Results.Ok(dto);
            })
            .WithName("UpdateBoundaryAssignment");

        group.MapDelete("/systems/{systemId}/boundary-definitions/{boundaryId}/components/{assignmentId}", async (
            string systemId,
            string boundaryId,
            string assignmentId,
            ComponentService componentService) =>
            {
                var removed = await componentService.RemoveComponentFromBoundaryAsync(assignmentId);
                if (!removed)
                    return Results.NotFound(new { error = "NOT_FOUND", message = "Assignment not found." });

                return Results.Ok(new { deleted = true, componentRetained = true, message = "Assignment removed. Component remains in the library." });
            })
            .WithName("RemoveBoundaryComponent");

        // ─── Boundary Lock Endpoints (Feature 040 — US3) ────────────────────

        group.MapPost("/systems/{systemId}/boundary-definitions/{boundaryId}/lock", (
            string systemId,
            string boundaryId,
            AcquireLockRequest request,
            BoundaryLockService lockService) =>
            {
                var (acquired, entry) = lockService.AcquireLock(boundaryId, request.UserId, request.UserDisplayName);
                var result = new
                {
                    locked = true,
                    lockedBy = entry.DisplayName,
                    lockedAt = entry.AcquiredAt.ToString("o"),
                    expiresAt = entry.ExpiresAt.ToString("o"),
                    message = acquired ? (string?)null : $"This boundary is currently being updated by {entry.DisplayName}.",
                };

                return acquired ? Results.Ok(result) : Results.Conflict(result);
            })
            .WithName("AcquireBoundaryLock");

        group.MapDelete("/systems/{systemId}/boundary-definitions/{boundaryId}/lock", (
            string systemId,
            string boundaryId,
            BoundaryLockService lockService) =>
            {
                lockService.ReleaseLock(boundaryId);
                return Results.Ok(new { released = true });
            })
            .WithName("ReleaseBoundaryLock");

        group.MapGet("/systems/{systemId}/boundary-definitions/{boundaryId}/lock", (
            string systemId,
            string boundaryId,
            BoundaryLockService lockService) =>
            {
                var entry = lockService.GetLockStatus(boundaryId);
                return Results.Ok(new
                {
                    locked = entry != null,
                    lockedBy = entry?.DisplayName,
                    lockedAt = entry?.AcquiredAt.ToString("o"),
                    expiresAt = entry?.ExpiresAt.ToString("o"),
                });
            })
            .WithName("GetBoundaryLockStatus");

        // ─── Feature 043: Control Inheritance Endpoints ──────────────────────────

        // ── GET /systems/{systemId}/inheritance — list designations with filters & pagination
        group.MapGet("/systems/{systemId}/inheritance", async (
            string systemId,
            [FromQuery] string? family,
            [FromQuery] string? inheritanceType,
            [FromQuery] string? search,
            [FromQuery] string? source,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] string? sortBy,
            [FromQuery] string? sortDirection,
            IBaselineService baselineService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 200) pageSize = 200;

            var baseline = await baselineService.GetBaselineAsync(systemId, includeDetails: true, cancellationToken: ct);
            if (baseline == null)
                return Results.NotFound(new ErrorResponse
                {
                    Error = "System or baseline not found",
                    ErrorCode = "BASELINE_NOT_FOUND",
                    Suggestion = "Ensure the system has a control baseline configured"
                });

            // Load org defaults for LEFT JOIN
            var orgDefaultLookup = await context.OrgInheritanceDefaults
                .AsNoTracking()
                .ToDictionaryAsync(d => d.ControlId, d => d, StringComparer.OrdinalIgnoreCase, ct);

            // Build designation list from all control IDs + inheritance records
            var inheritanceLookup = baseline.Inheritances.ToDictionary(i => i.ControlId, i => i, StringComparer.OrdinalIgnoreCase);
            var allDesignations = baseline.ControlIds.Select(cid =>
            {
                inheritanceLookup.TryGetValue(cid, out var inh);
                orgDefaultLookup.TryGetValue(cid, out var orgDefault);
                return new
                {
                    id = inh?.Id ?? string.Empty,
                    controlId = cid,
                    family = ComplianceFrameworks.ExtractControlFamily(cid),
                    inheritanceType = inh?.InheritanceType.ToString() ?? "Undesignated",
                    provider = inh?.Provider,
                    customerResponsibility = inh?.CustomerResponsibility,
                    designationSource = inh?.DesignationSource,
                    orgDefault = orgDefault == null ? null : new
                    {
                        id = orgDefault.Id,
                        inheritanceType = orgDefault.InheritanceType.ToString(),
                        provider = orgDefault.Provider,
                        sourceCapabilities = orgDefault.SourceCapabilityNames,
                        mappingRole = orgDefault.MappingRole.ToString(),
                    },
                    setBy = inh?.SetBy,
                    setAt = inh?.SetAt
                };
            }).ToList();

            // Apply filters
            var filtered = allDesignations.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(family))
                filtered = filtered.Where(d => d.family.Equals(family, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(inheritanceType))
                filtered = filtered.Where(d => d.inheritanceType.Equals(inheritanceType, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(search))
                filtered = filtered.Where(d =>
                    d.controlId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (d.provider != null && d.provider.Contains(search, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrWhiteSpace(source))
            {
                filtered = source.ToLowerInvariant() switch
                {
                    "org" or "orgdefault" or "orgderived" => filtered.Where(d => d.designationSource == "OrgDerived"),
                    "override" or "manual" => filtered.Where(d => d.designationSource is "Manual" or "ProfileApply" or "CrmImport" or "BulkUpdate"),
                    "undesignated" => filtered.Where(d => d.designationSource == null && d.inheritanceType == "Undesignated"),
                    _ => filtered,
                };
            }

            // Sort
            var sortField = sortBy?.ToLowerInvariant() ?? "controlid";
            var desc = sortDirection?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true;
            filtered = sortField switch
            {
                "family" => desc ? filtered.OrderByDescending(d => d.family) : filtered.OrderBy(d => d.family),
                "inheritancetype" => desc ? filtered.OrderByDescending(d => d.inheritanceType) : filtered.OrderBy(d => d.inheritanceType),
                "setat" => desc ? filtered.OrderByDescending(d => d.setAt) : filtered.OrderBy(d => d.setAt),
                _ => desc ? filtered.OrderByDescending(d => d.controlId) : filtered.OrderBy(d => d.controlId),
            };

            var total = filtered.Count();
            var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Summary
            var totalControls = baseline.ControlIds.Count;
            var inheritedCount = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Inherited);
            var sharedCount = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Shared);
            var customerCount = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Customer);
            var undesignatedCount = totalControls - inheritedCount - sharedCount - customerCount;
            var pct = totalControls > 0 ? Math.Round((double)(inheritedCount + sharedCount + customerCount) / totalControls * 100, 1) : 0;

            // Source breakdown
            var orgDefaultCount = baseline.Inheritances.Count(i => i.DesignationSource == "OrgDerived");
            var systemOverrideCount = baseline.Inheritances.Count(i => i.DesignationSource is "Manual" or "ProfileApply" or "CrmImport" or "BulkUpdate");

            return Results.Ok(new
            {
                items,
                totalItems = total,
                page,
                pageSize,
                summary = new
                {
                    totalControls,
                    inheritedCount,
                    sharedCount,
                    customerCount,
                    undesignatedCount,
                    inheritancePercentage = pct,
                    orgDefaultCount,
                    systemOverrideCount,
                    sourceBreakdown = new
                    {
                        orgDerived = orgDefaultCount,
                        manual = baseline.Inheritances.Count(i => i.DesignationSource == "Manual"),
                        profileApply = baseline.Inheritances.Count(i => i.DesignationSource == "ProfileApply"),
                        crmImport = baseline.Inheritances.Count(i => i.DesignationSource == "CrmImport"),
                        bulkUpdate = baseline.Inheritances.Count(i => i.DesignationSource == "BulkUpdate"),
                        undesignated = undesignatedCount,
                    }
                }
            });
        }).WithName("ListInheritanceDesignations");

        // ── PUT /systems/{systemId}/inheritance — set designations (single + bulk)
        // TODO(FR-026): Add role validation — restrict writes to AO and Security Engineer roles
        //   when auth context is available. Return 403 Forbidden for unauthorized users.
        group.MapPut("/systems/{systemId}/inheritance", async (
            string systemId,
            Feature043SetInheritanceRequest req,
            IBaselineService baselineService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            if (req.Designations == null || req.Designations.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one designation is required.", ErrorCode = "INVALID_INPUT" });

            // Validate inheritance types
            foreach (var d in req.Designations)
            {
                if (!Enum.TryParse<InheritanceType>(d.InheritanceType, true, out var iType))
                    return Results.BadRequest(new ErrorResponse { Error = $"Invalid inheritance type '{d.InheritanceType}'", ErrorCode = "INVALID_INPUT" });

                if ((iType == InheritanceType.Inherited || iType == InheritanceType.Shared) && string.IsNullOrWhiteSpace(d.Provider))
                    return Results.BadRequest(new ErrorResponse { Error = $"Provider is required for '{d.InheritanceType}' on control {d.ControlId}", ErrorCode = "INVALID_INPUT" });

                if (iType == InheritanceType.Customer && string.IsNullOrWhiteSpace(d.CustomerResponsibility))
                    return Results.BadRequest(new ErrorResponse { Error = $"Customer responsibility is required for 'Customer' on control {d.ControlId}", ErrorCode = "INVALID_INPUT" });
            }

            var changeSource = InheritanceChangeSource.Manual;
            if (!string.IsNullOrWhiteSpace(req.ChangeSource))
                Enum.TryParse(req.ChangeSource, true, out changeSource);

            var mappings = req.Designations.Select(d => new InheritanceInput
            {
                ControlId = d.ControlId,
                InheritanceType = d.InheritanceType,
                Provider = d.Provider,
                CustomerResponsibility = d.CustomerResponsibility
            });

            var result = await baselineService.SetInheritanceAsync(systemId, mappings, "dashboard-user", changeSource, ct);

            var totalControls = result.Baseline.ControlIds.Count;
            var undesignated = totalControls - result.InheritedCount - result.SharedCount - result.CustomerCount;
            var pct = totalControls > 0 ? Math.Round((double)(result.InheritedCount + result.SharedCount + result.CustomerCount) / totalControls * 100, 1) : 0;

            context.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "InheritanceUpdated",
                Actor = "dashboard-user",
                Summary = $"Updated {result.ControlsUpdated} control inheritance designations (source: {changeSource})",
                RelatedEntityType = "ControlBaseline",
                RelatedEntityId = result.Baseline.Id,
            });
            await context.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                controlsUpdated = result.ControlsUpdated,
                inheritedCount = result.InheritedCount,
                sharedCount = result.SharedCount,
                customerCount = result.CustomerCount,
                skippedControls = result.SkippedControls,
                narrativesAutoUpdated = result.NarrativesAutoUpdated,
                summary = new
                {
                    totalControls,
                    inheritedCount = result.InheritedCount,
                    sharedCount = result.SharedCount,
                    customerCount = result.CustomerCount,
                    undesignatedCount = undesignated,
                    inheritancePercentage = pct
                }
            });
        }).WithName("SetInheritanceDesignations");

        // ── POST /systems/{systemId}/inheritance/revert-to-org-defaults — revert selected controls
        group.MapPost("/systems/{systemId}/inheritance/revert-to-org-defaults", async (
            string systemId,
            Feature044RevertRequest req,
            IOrgInheritanceService orgService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            if (req.ControlIds == null || req.ControlIds.Count == 0)
                return Results.BadRequest(new ErrorResponse { Error = "At least one control ID is required.", ErrorCode = "INVALID_INPUT" });

            var revertedBy = req.RevertedBy ?? "dashboard-user";
            var result = await orgService.RevertToOrgDefaultsAsync(systemId, req.ControlIds, revertedBy, ct);

            context.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = systemId,
                EventType = "InheritanceReverted",
                Actor = revertedBy,
                Summary = $"Reverted {result.RevertedCount} controls to org defaults, {result.Skipped.Count} skipped",
                RelatedEntityType = "ControlInheritance",
            });
            await context.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                revertedCount = result.RevertedCount,
                skipped = result.Skipped.Select(s => new { s.ControlId, s.Reason }),
            });
        }).WithName("RevertToOrgDefaults");

        // ── GET /systems/{systemId}/inheritance/{controlId}/audit — per-control audit trail
        group.MapGet("/systems/{systemId}/inheritance/{controlId}/audit", async (
            string systemId,
            string controlId,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            // Verify system + baseline exists
            var baseline = await context.ControlBaselines
                .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, ct);
            if (baseline == null)
                return Results.NotFound(new ErrorResponse
                {
                    Error = "System or baseline not found",
                    ErrorCode = "BASELINE_NOT_FOUND"
                });

            var entries = await context.InheritanceAuditEntries
                .Where(e => e.ControlBaselineId == baseline.Id && e.ControlId == controlId)
                .OrderBy(e => e.Timestamp)
                .Select(e => new
                {
                    id = e.Id,
                    actor = e.Actor,
                    previousInheritanceType = e.PreviousInheritanceType,
                    newInheritanceType = e.NewInheritanceType,
                    previousProvider = e.PreviousProvider,
                    newProvider = e.NewProvider,
                    previousCustomerResponsibility = e.PreviousCustomerResponsibility,
                    newCustomerResponsibility = e.NewCustomerResponsibility,
                    changeSource = e.ChangeSource.ToString(),
                    changeSourceLabel = e.ChangeSource == InheritanceChangeSource.OrgDerived ? "Org Default"
                        : e.ChangeSource == InheritanceChangeSource.OrgPropagation ? "Org Propagation"
                        : e.ChangeSource == InheritanceChangeSource.ProfileApply ? "CSP Profile"
                        : e.ChangeSource == InheritanceChangeSource.CrmImport ? "CRM Import"
                        : e.ChangeSource == InheritanceChangeSource.BulkUpdate ? "Bulk Update"
                        : "Manual",
                    timestamp = e.Timestamp
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                controlId,
                entries
            });
        }).WithName("GetInheritanceAudit");

        // ── GET /systems/{systemId}/inheritance/crm — generate CRM
        group.MapGet("/systems/{systemId}/inheritance/crm", async (
            string systemId,
            IBaselineService baselineService,
            CancellationToken ct) =>
        {
            try
            {
                var crm = await baselineService.GenerateCrmAsync(systemId, ct);
                return Results.Ok(crm);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new ErrorResponse
                {
                    Error = ex.Message,
                    ErrorCode = "BASELINE_NOT_FOUND",
                    Suggestion = "Ensure the system has a control baseline configured"
                });
            }
        }).WithName("GetCrm");

        // ── GET /systems/{systemId}/inheritance/crm/export — export CRM as CSV or Excel
        group.MapGet("/systems/{systemId}/inheritance/crm/export", async (
            string systemId,
            [FromQuery] string format,
            [FromQuery] string? layout,
            IBaselineService baselineService,
            Ato.Copilot.Mcp.Services.CrmExportService crmExportService,
            CancellationToken ct) =>
        {
            var fmt = format?.ToLowerInvariant();
            if (fmt != "csv" && fmt != "excel")
                return Results.BadRequest(new ErrorResponse { Error = $"Unsupported export format: {format}", ErrorCode = "EXPORT_FORMAT_INVALID" });

            var exportLayout = layout ?? "custom";

            CrmResult crm;
            try
            {
                crm = await baselineService.GenerateCrmAsync(systemId, ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new ErrorResponse { Error = ex.Message, ErrorCode = "BASELINE_NOT_FOUND" });
            }

            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (fmt == "csv")
            {
                var bytes = crmExportService.GenerateCsv(crm, exportLayout);
                return Results.File(bytes, "text/csv", $"crm-{systemId}-{date}.csv");
            }
            else
            {
                var bytes = crmExportService.GenerateExcel(crm, exportLayout);
                return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"crm-{systemId}-{date}.xlsx");
            }
        }).WithName("ExportCrm");

        // ── GET /systems/{systemId}/inheritance/csp-profiles — list available profiles
        group.MapGet("/systems/{systemId}/inheritance/csp-profiles", (
            string systemId,
            Ato.Copilot.Mcp.Services.CspProfileService cspProfileService) =>
        {
            var profiles = cspProfileService.GetProfiles();
            return Results.Ok(new
            {
                profiles = profiles.Select(p => new
                {
                    profileId = p.ProfileId,
                    name = p.Name,
                    provider = p.Provider,
                    baselineLevel = p.BaselineLevel,
                    description = p.Description,
                    controlCount = p.Controls.Count,
                    version = p.Version
                })
            });
        }).WithName("ListCspProfiles");

        // Feature 045: Old CSP/CRM import endpoints removed — replaced by
        // POST /capabilities/import/csp-profile and POST /capabilities/import/crm

        // ─── Feature 044: Org-Level Inheritance Endpoints ──────────────────────

        // ── GET /inheritance/org-defaults — list org-level inheritance defaults with filters & pagination
        group.MapGet("/inheritance/org-defaults", async (
            [FromQuery] string? family,
            [FromQuery] string? inheritanceType,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            IOrgInheritanceService orgService = default!,
            CancellationToken ct = default) =>
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 200) pageSize = 200;

            var result = await orgService.GetOrgDefaultsAsync(family, inheritanceType, search, page, pageSize, ct);
            return Results.Ok(result);
        }).WithName("GetOrgInheritanceDefaults");

        // ── POST /inheritance/org-defaults/derive — trigger org-level inheritance derivation
        group.MapPost("/inheritance/org-defaults/derive", async (
            IOrgInheritanceService orgService,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var result = await orgService.DeriveOrgDefaultsAsync("dashboard-user", ct);

            return Results.Ok(result);
        }).WithName("DeriveOrgInheritanceDefaults");

        // ─── Control Catalog (top-level, not system-scoped) ────────────────────
        group.MapGet("/controls", async (
                INistControlsService nistService,
                IReferenceDataService refData,
                string? search,
                string? family,
                string? type,
                int page = 1,
                int pageSize = 50,
                CancellationToken ct = default) =>
            {
                var allControls = await nistService.GetAllControlsAsync(ct);
                if (allControls.Count == 0)
                    return Results.Ok(new { items = Array.Empty<object>(), total = 0, page, pageSize });

                // NIST 800-53B baselines
                var nistLowSet = new HashSet<string>(refData.GetBaselineControlIds("Low"), StringComparer.OrdinalIgnoreCase);
                var nistModSet = new HashSet<string>(refData.GetBaselineControlIds("Moderate"), StringComparer.OrdinalIgnoreCase);
                var nistHighSet = new HashSet<string>(refData.GetBaselineControlIds("High"), StringComparer.OrdinalIgnoreCase);

                // FedRAMP Rev 5 baselines (distinct from NIST 800-53B)
                var fedrampLiSaasSet = new HashSet<string>(refData.GetFedRampBaselineControlIds("li-saas"), StringComparer.OrdinalIgnoreCase);
                var fedrampLowSet = new HashSet<string>(refData.GetFedRampBaselineControlIds("low"), StringComparer.OrdinalIgnoreCase);
                var fedrampModSet = new HashSet<string>(refData.GetFedRampBaselineControlIds("moderate"), StringComparer.OrdinalIgnoreCase);
                var fedrampHighSet = new HashSet<string>(refData.GetFedRampBaselineControlIds("high"), StringComparer.OrdinalIgnoreCase);

                // DoD CNSSI 1253 overlay lookup sets
                var il2Controls = new HashSet<string>(
                    refData.GetOverlayEntries("IL2").Select(o => o.ControlId),
                    StringComparer.OrdinalIgnoreCase);
                var il4Controls = new HashSet<string>(
                    refData.GetOverlayEntries("IL4").Select(o => o.ControlId),
                    StringComparer.OrdinalIgnoreCase);
                var il5Controls = new HashSet<string>(
                    refData.GetOverlayEntries("IL5").Select(o => o.ControlId),
                    StringComparer.OrdinalIgnoreCase);
                var il6Controls = new HashSet<string>(
                    refData.GetOverlayEntries("IL6").Select(o => o.ControlId),
                    StringComparer.OrdinalIgnoreCase);

                // Filter
                IEnumerable<NistControl> filtered = allControls;

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var q = search.Trim();
                    filtered = filtered.Where(c =>
                        c.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.Family.Contains(q, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(family))
                    filtered = filtered.Where(c => c.Family.Equals(family, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(type))
                {
                    if (type.Equals("control", StringComparison.OrdinalIgnoreCase))
                        filtered = filtered.Where(c => !c.IsEnhancement);
                    else if (type.Equals("enhancement", StringComparison.OrdinalIgnoreCase))
                        filtered = filtered.Where(c => c.IsEnhancement);
                }

                var sortedList = filtered
                    .OrderBy(c => c.Family)
                    .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var total = sortedList.Count;
                var paged = sortedList
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(c =>
                    {
                        var upperId = c.Id.ToUpperInvariant();
                        return new
                        {
                            id = upperId,
                            family = c.Family.ToUpperInvariant(),
                            familyName = NistFamilyNames.GetValueOrDefault(c.Family.ToUpperInvariant(), c.Family),
                            title = c.Title,
                            type = c.IsEnhancement ? "Enhancement" : "Control",
                            baselines = new
                            {
                                nistLow = nistLowSet.Contains(upperId) || nistLowSet.Contains(c.Id),
                                nistModerate = nistModSet.Contains(upperId) || nistModSet.Contains(c.Id),
                                nistHigh = nistHighSet.Contains(upperId) || nistHighSet.Contains(c.Id),
                                fedrampLiSaas = fedrampLiSaasSet.Contains(upperId) || fedrampLiSaasSet.Contains(c.Id),
                                fedrampLow = fedrampLowSet.Contains(upperId) || fedrampLowSet.Contains(c.Id),
                                fedrampModerate = fedrampModSet.Contains(upperId) || fedrampModSet.Contains(c.Id),
                                fedrampHigh = fedrampHighSet.Contains(upperId) || fedrampHighSet.Contains(c.Id),
                                il2 = il2Controls.Contains(upperId) || il2Controls.Contains(c.Id),
                                il4 = il4Controls.Contains(upperId) || il4Controls.Contains(c.Id),
                                il5 = il5Controls.Contains(upperId) || il5Controls.Contains(c.Id),
                                il6 = il6Controls.Contains(upperId) || il6Controls.Contains(c.Id),
                            },
                        };
                    })
                    .ToList();

                return Results.Ok(new { items = paged, total, page, pageSize });
            })
            .WithName("GetControlCatalog");

        // ─── Control Detail ─────────────────────────────────────────────────
        group.MapGet("/controls/{controlId}", async (
                string controlId,
                INistControlsService nistService,
                IReferenceDataService refData,
                CancellationToken ct) =>
            {
                var allControls = await nistService.GetAllControlsAsync(ct);
                var control = allControls.FirstOrDefault(c =>
                    c.Id.Equals(controlId, StringComparison.OrdinalIgnoreCase));

                if (control == null)
                    return Results.NotFound(new { error = $"Control '{controlId}' not found." });

                var upperId = control.Id.ToUpperInvariant();

                // Gather baseline membership
                var nistLow = refData.GetBaselineControlIds("Low");
                var nistMod = refData.GetBaselineControlIds("Moderate");
                var nistHigh = refData.GetBaselineControlIds("High");
                var frLiSaas = refData.GetFedRampBaselineControlIds("li-saas");
                var frLow = refData.GetFedRampBaselineControlIds("low");
                var frMod = refData.GetFedRampBaselineControlIds("moderate");
                var frHigh = refData.GetFedRampBaselineControlIds("high");

                // Gather overlay parameters per IL
                var overlayDetails = new[] { "IL2", "IL4", "IL5", "IL6" }
                    .Select(il =>
                    {
                        var entry = refData.GetOverlayEntries(il)
                            .FirstOrDefault(o => o.ControlId.Equals(control.Id, StringComparison.OrdinalIgnoreCase)
                                              || o.ControlId.Equals(upperId, StringComparison.OrdinalIgnoreCase));
                        return new
                        {
                            level = il,
                            applicable = entry != null,
                            parameters = entry?.Parameters ?? new Dictionary<string, string>(),
                            enhancements = entry?.Enhancements ?? new List<string>(),
                            notes = entry?.Notes,
                        };
                    })
                    .ToList();

                // Gather child enhancements
                var enhancements = allControls
                    .Where(c => c.ParentControlId != null &&
                                c.ParentControlId.Equals(control.Id, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(c => new
                    {
                        id = c.Id.ToUpperInvariant(),
                        title = c.Title,
                    })
                    .ToList();

                bool ContainsAny(IReadOnlyList<string> list, string id1, string id2)
                    => list.Any(x => x.Equals(id1, StringComparison.OrdinalIgnoreCase)
                                  || x.Equals(id2, StringComparison.OrdinalIgnoreCase));

                return Results.Ok(new
                {
                    id = upperId,
                    family = control.Family.ToUpperInvariant(),
                    familyName = NistFamilyNames.GetValueOrDefault(control.Family.ToUpperInvariant(), control.Family),
                    title = control.Title,
                    description = control.Description,
                    type = control.IsEnhancement ? "Enhancement" : "Control",
                    parentControlId = control.ParentControlId?.ToUpperInvariant(),
                    azureImplementation = control.AzureImplementation,
                    fedRampParameters = control.FedRampParameters,
                    baselines = new
                    {
                        nistLow = ContainsAny(nistLow, upperId, control.Id),
                        nistModerate = ContainsAny(nistMod, upperId, control.Id),
                        nistHigh = ContainsAny(nistHigh, upperId, control.Id),
                        fedrampLiSaas = ContainsAny(frLiSaas, upperId, control.Id),
                        fedrampLow = ContainsAny(frLow, upperId, control.Id),
                        fedrampModerate = ContainsAny(frMod, upperId, control.Id),
                        fedrampHigh = ContainsAny(frHigh, upperId, control.Id),
                    },
                    dodOverlays = overlayDetails,
                    enhancements,
                });
            })
            .WithName("GetControlDetail");

        // ─── Multi-Framework Catalog Endpoints (Feature 044) ────────────────

        group.MapGet("/frameworks", async (
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var frameworks = await context.ComplianceFrameworks
                .Where(f => f.IsActive)
                .OrderBy(f => f.Name)
                .Select(f => new
                {
                    f.Id,
                    f.Identifier,
                    f.Name,
                    f.Version,
                    f.Publisher,
                    f.ControlCount,
                    f.ImportedAt,
                    f.IsActive,
                    baselines = f.Baselines
                        .OrderBy(b => b.Level)
                        .Select(b => new { b.Id, b.Level, b.ControlCount, b.ImportedAt })
                        .ToList(),
                })
                .ToListAsync(ct);

            return Results.Ok(frameworks);
        })
        .WithName("ListFrameworks");

        group.MapGet("/frameworks/{frameworkId}/controls", async (
            string frameworkId,
            AtoCopilotContext context,
            string? search,
            string? family,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default) =>
        {
            // Resolve by ID or Identifier
            var framework = await context.ComplianceFrameworks
                .FirstOrDefaultAsync(f => f.Id == frameworkId || f.Identifier == frameworkId, ct);

            if (framework is null)
                return Results.NotFound(new { error = "Framework not found" });

            IQueryable<FrameworkControl> query = context.FrameworkControls
                .Where(c => c.FrameworkId == framework.Id && !c.Withdrawn);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim();
                query = query.Where(c =>
                    c.ControlId.Contains(q) ||
                    c.Title.Contains(q) ||
                    c.Family.Contains(q));
            }

            if (!string.IsNullOrWhiteSpace(family))
                query = query.Where(c => c.Family == family.ToUpperInvariant());

            var total = await query.CountAsync(ct);

            var items = await query
                .OrderBy(c => c.SortOrder)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.ControlId,
                    c.Family,
                    c.Title,
                    c.IsEnhancement,
                    c.ParentControlId,
                    type = c.IsEnhancement ? "Enhancement" : "Control",
                })
                .ToListAsync(ct);

            // Look up baseline membership for returned controls
            var controlIds = items.Select(i => i.ControlId).ToList();
            var baselineIds = await context.FrameworkBaselines
                .Where(b => b.FrameworkId == framework.Id)
                .Select(b => new { b.Id, b.Level })
                .ToListAsync(ct);

            var baselineMembership = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);
            foreach (var bl in baselineIds)
            {
                var memberIds = await context.BaselineControlEntries
                    .Where(e => e.BaselineId == bl.Id && controlIds.Contains(e.ControlId))
                    .Select(e => e.ControlId)
                    .ToListAsync(ct);

                var memberSet = new HashSet<string>(memberIds, StringComparer.OrdinalIgnoreCase);
                foreach (var cid in controlIds)
                {
                    if (!baselineMembership.ContainsKey(cid))
                        baselineMembership[cid] = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    baselineMembership[cid][bl.Level] = memberSet.Contains(cid);
                }
            }

            var result = items.Select(i => new
            {
                id = i.ControlId,
                family = i.Family,
                familyName = NistFamilyNames.GetValueOrDefault(i.Family, i.Family),
                title = i.Title,
                type = i.type,
                baselines = baselineMembership.GetValueOrDefault(i.ControlId) ?? new Dictionary<string, bool>(),
            });

            return Results.Ok(new { items = result, total, page, pageSize, frameworkId = framework.Identifier });
        })
        .WithName("GetFrameworkControls");

        group.MapGet("/frameworks/{frameworkId}/controls/{controlId}", async (
            string frameworkId,
            string controlId,
            AtoCopilotContext context,
            CancellationToken ct) =>
        {
            var framework = await context.ComplianceFrameworks
                .FirstOrDefaultAsync(f => f.Id == frameworkId || f.Identifier == frameworkId, ct);
            if (framework is null)
                return Results.NotFound(new { error = "Framework not found" });

            var control = await context.FrameworkControls
                .FirstOrDefaultAsync(c => c.FrameworkId == framework.Id && c.ControlId == controlId, ct);
            if (control is null)
                return Results.NotFound(new { error = "Control not found" });

            // Child enhancements
            var enhancements = await context.FrameworkControls
                .Where(c => c.FrameworkId == framework.Id && c.ParentControlId == control.ControlId && !c.Withdrawn)
                .OrderBy(c => c.SortOrder)
                .Select(c => new { c.ControlId, c.Title })
                .ToListAsync(ct);

            // Baseline membership
            var baselines = await context.FrameworkBaselines
                .Where(b => b.FrameworkId == framework.Id)
                .ToListAsync(ct);

            var baselineMap = new Dictionary<string, bool>();
            foreach (var bl in baselines)
            {
                var isMember = await context.BaselineControlEntries
                    .AnyAsync(e => e.BaselineId == bl.Id && e.ControlId == controlId, ct);
                baselineMap[bl.Level] = isMember;
            }

            return Results.Ok(new
            {
                id = control.ControlId,
                family = control.Family,
                familyName = NistFamilyNames.GetValueOrDefault(control.Family, control.Family),
                title = control.Title,
                description = control.Description,
                type = control.IsEnhancement ? "Enhancement" : "Control",
                parentControlId = control.ParentControlId,
                withdrawn = control.Withdrawn,
                withdrawnTo = control.WithdrawnTo,
                baselines = baselineMap,
                enhancements,
                framework = new { framework.Identifier, framework.Name, framework.Version },
            });
        })
        .WithName("GetFrameworkControlDetail");

        group.MapPost("/frameworks/import", async (
            IFrameworkImportService importService,
            CancellationToken ct) =>
        {
            var result = await importService.ImportAllAsync(ct);
            return Results.Ok(new
            {
                frameworksImported = result.FrameworksImported,
                totalControls = result.TotalControls,
                totalBaselines = result.TotalBaselines,
                errors = result.Errors,
            });
        })
        .WithName("ImportAllFrameworks");

        group.MapPost("/frameworks/{identifier}/import", async (
            string identifier,
            IFrameworkImportService importService,
            CancellationToken ct) =>
        {
            var count = await importService.ImportFrameworkAsync(identifier, ct);
            return Results.Ok(new { identifier, controlsImported = count });
        })
        .WithName("ImportSingleFramework");

        return app;
    }

    // ─── NIST Family Name Lookup ────────────────────────────────────────────
    private static readonly Dictionary<string, string> NistFamilyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AC"] = "Access Control",
        ["AT"] = "Awareness and Training",
        ["AU"] = "Audit and Accountability",
        ["CA"] = "Assessment, Authorization, and Monitoring",
        ["CM"] = "Configuration Management",
        ["CP"] = "Contingency Planning",
        ["IA"] = "Identification and Authentication",
        ["IR"] = "Incident Response",
        ["MA"] = "Maintenance",
        ["MP"] = "Media Protection",
        ["PE"] = "Physical and Environmental Protection",
        ["PL"] = "Planning",
        ["PM"] = "Program Management",
        ["PS"] = "Personnel Security",
        ["PT"] = "PII Processing and Transparency",
        ["RA"] = "Risk Assessment",
        ["SA"] = "System and Services Acquisition",
        ["SC"] = "System and Communications Protection",
        ["SI"] = "System and Information Integrity",
        ["SR"] = "Supply Chain Risk Management",
    };

    // ─── Feature 043: Import helpers ────────────────────────────────────────

    private static readonly Dictionary<string, Ato.Copilot.Mcp.Services.CrmExportService.ImportParseResult> ImportPreviewCache = new();

    private static string MapImportType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Customer";
        var lower = raw.ToLowerInvariant().Trim();
        return lower switch
        {
            "inherited" or "system" or "csp" => "Inherited",
            "shared" or "hybrid" => "Shared",
            "customer" or "system-specific" or "not implemented" => "Customer",
            _ => "Customer"
        };
    }

    // ─── Feature 039: POA&M mapping helpers ──────────────────────────────────

    private static object MapToListItem(PoamItem p) => new
    {
        id = p.Id,
        controlId = p.SecurityControlNumber,
        weakness = p.Weakness,
        catSeverity = p.CatSeverity.ToString().Replace("Cat", ""),
        status = p.Status.ToString(),
        components = p.ComponentLinks.Select(cl => new
        {
            id = cl.SystemComponentId,
            name = cl.SystemComponent?.Name ?? "",
            type = cl.SystemComponent?.ComponentType.ToString() ?? ""
        }),
        poc = p.PointOfContact,
        dueDate = p.ScheduledCompletionDate.ToString("o"),
        daysRemaining = (p.ScheduledCompletionDate - DateTime.UtcNow).Days,
        milestoneProgress = new
        {
            completed = p.Milestones.Count(m => m.CompletedDate.HasValue),
            total = p.Milestones.Count
        },
        deviationType = p.DeviationId != null ? "linked" : (string?)null,
        externalTicketRef = p.ExternalTicketRef,
        remediationTaskId = p.RemediationTaskId,
        remediationTaskStatus = (string?)null,
        isOverdue = p.ScheduledCompletionDate < DateTime.UtcNow &&
                    p.Status != PoamStatus.Completed &&
                    p.Status != PoamStatus.RiskAccepted,
        systemId = p.RegisteredSystemId,
        systemName = p.RegisteredSystem?.Name ?? ""
    };

    private static object MapToDetail(PoamItem p) => new
    {
        id = p.Id,
        controlId = p.SecurityControlNumber,
        weakness = p.Weakness,
        weaknessSource = p.WeaknessSource,
        catSeverity = p.CatSeverity.ToString().Replace("Cat", ""),
        status = p.Status.ToString(),
        components = p.ComponentLinks.Select(cl => new
        {
            id = cl.SystemComponentId,
            name = cl.SystemComponent?.Name ?? "",
            type = cl.SystemComponent?.ComponentType.ToString() ?? ""
        }),
        poc = p.PointOfContact,
        pocEmail = p.PocEmail,
        dueDate = p.ScheduledCompletionDate.ToString("o"),
        scheduledCompletionDate = p.ScheduledCompletionDate.ToString("o"),
        actualCompletionDate = p.ActualCompletionDate?.ToString("o"),
        daysRemaining = (p.ScheduledCompletionDate - DateTime.UtcNow).Days,
        milestoneProgress = new
        {
            completed = p.Milestones.Count(m => m.CompletedDate.HasValue),
            total = p.Milestones.Count
        },
        deviationType = p.DeviationId != null ? "linked" : (string?)null,
        externalTicketRef = p.ExternalTicketRef,
        remediationTaskId = p.RemediationTaskId,
        remediationTaskStatus = (string?)null,
        isOverdue = p.ScheduledCompletionDate < DateTime.UtcNow &&
                    p.Status != PoamStatus.Completed &&
                    p.Status != PoamStatus.RiskAccepted,
        systemId = p.RegisteredSystemId,
        systemName = p.RegisteredSystem?.Name ?? "",
        resourcesRequired = p.ResourcesRequired,
        costEstimate = p.CostEstimate,
        comments = p.Comments,
        findingId = p.FindingId,
        deviationId = p.DeviationId,
        createdAt = p.CreatedAt.ToString("o"),
        modifiedAt = p.ModifiedAt?.ToString("o"),
        createdBy = p.CreatedBy,
        rowVersion = p.RowVersion.ToString(),
        milestones = p.Milestones.OrderBy(m => m.Sequence).Select(m => new
        {
            id = m.Id,
            description = m.Description,
            targetDate = m.TargetDate.ToString("o"),
            completedDate = m.CompletedDate?.ToString("o"),
            sequence = m.Sequence,
            isOverdue = m.IsOverdue
        }),
        history = p.History.OrderByDescending(h => h.Timestamp).Select(h => new
        {
            id = h.Id,
            eventType = h.EventType.ToString(),
            oldValue = h.OldValue,
            newValue = h.NewValue,
            actingUserName = h.ActingUserName,
            timestamp = h.Timestamp.ToString("o"),
            details = h.Details,
            cascadeOrigin = h.CascadeOrigin?.ToString()
        }),
        ticketSync = (object?)null
    };

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
        string? RowVersion = null,
        string? DelayReason = null,
        string? RevisedDate = null,
        string? DeviationId = null,
        string? Comments = null);

    private record BulkPoamStatusRequest(
        List<string> PoamIds,
        string Status,
        string? DelayReason = null,
        string? RevisedDate = null,
        string? Comments = null);

    private record MoveTaskRequest(
        string Status);

    // ─── Feature 039: POA&M request DTOs ─────────────────────────────────────

    private record Feature039CreatePoamRequest(
        string Weakness,
        string? WeaknessSource,
        string ControlId,
        string CatSeverity,
        string Poc,
        DateTime ScheduledCompletionDate,
        string? PocEmail = null,
        string? ResourcesRequired = null,
        decimal? CostEstimate = null,
        string? Comments = null,
        string? FindingId = null,
        List<string>? ComponentIds = null,
        List<Feature039MilestoneInput>? Milestones = null);

    private record Feature039MilestoneInput(
        string Description,
        DateTime TargetDate);

    private record Feature039UpdatePoamRequest(
        string RowVersion,
        string? Weakness = null,
        string? ControlId = null,
        string? Poc = null,
        string? PocEmail = null,
        string? Comments = null,
        string? ResourcesRequired = null,
        DateTime? ScheduledCompletionDate = null,
        decimal? CostEstimate = null);

    private record Feature039LinkComponentsRequest(
        List<string> ComponentIds);

    private record Feature039UnlinkComponentsRequest(
        List<string> ComponentIds);

    private record Feature039CreateTaskRequest(
        string BoardId);

    private record Feature039LinkTaskRequest(
        string TaskId);

    private record Feature039BulkCreateRequest(
        List<string> FindingIds,
        List<string>? ComponentIds = null,
        bool LinkRemediationTasks = false);

    private record Feature039StatusUpdateRequest(
        string Status,
        string RowVersion,
        string? DelayReason = null,
        DateTime? RevisedDate = null,
        string? DeviationId = null,
        string? Comments = null,
        bool CascadeToTask = false);

    private record Feature039BulkStatusRequest(
        List<string> PoamIds,
        string Status,
        string? DelayReason = null,
        DateTime? RevisedDate = null,
        string? Comments = null);

    // ─── Feature 039: Ticketing DTOs ────────────────────────────────────────

    private record ConfigureTicketingRequest(
        string Provider,
        string BaseUrl,
        string ProjectKey,
        string ApiKeySecretName,
        bool SyncEnabled = true);

    private record SyncTicketRequest(
        string Direction = "push");

    // ─── Feature 040: Component Discovery DTOs ──────────────────────────────

    private record DiscoverAzureComponentsRequest(
        string SubscriptionId,
        string? ResourceGroupFilter = null,
        string? ResourceTypeFilter = null,
        string? SearchFilter = null,
        string? Cursor = null);

    private record ImportAzureResourceItem(
        string ResourceId,
        string Name,
        string Type,
        string ResourceGroup,
        string Location);

    private record ImportAzureComponentsRequest(
        List<ImportAzureResourceItem> Resources);

    private record ImportSystemAzureComponentsRequest(
        List<ImportAzureResourceItem> Resources,
        List<string>? AssignExistingOrgComponents = null);

    // ─── Feature 040: Boundary Component Assignment DTOs ────────────────────

    private record AssignComponentToBoundaryRequest(
        string ComponentId,
        bool IsInScope = true,
        string? ExclusionRationale = null,
        string? InheritanceProvider = null,
        string? CreatedBy = null);

    private record UpdateBoundaryAssignmentRequest(
        bool IsInScope,
        string? ExclusionRationale = null,
        string? InheritanceProvider = null,
        string? ModifiedBy = null);

    private record AcquireLockRequest(
        string UserId,
        string UserDisplayName);

    // ─── Feature 042: System Capability Link DTOs ───────────────────────────

    private record LinkCapabilitiesRequest(
        List<string> CapabilityIds);

    // ─── Feature 043: Control Inheritance DTOs ──────────────────────────────

    private record Feature043DesignationInput(
        string ControlId,
        string InheritanceType,
        string? Provider = null,
        string? CustomerResponsibility = null);

    private record Feature043SetInheritanceRequest(
        List<Feature043DesignationInput> Designations,
        string? ChangeSource = "Manual");

    private record Feature043ApplyProfileRequest(
        string ProfileId,
        string? ConflictResolution = "skip",
        bool Preview = false);

    private record Feature043ImportApplyRequest(
        string PreviewToken,
        Feature043ColumnMapping ColumnMapping,
        string? ConflictResolution = "overwrite");

    private record Feature043ColumnMapping(
        string ControlId,
        string InheritanceType,
        string Provider,
        string CustomerResponsibility);

    // Feature 044 request types
    private record Feature044RevertRequest(
        List<string> ControlIds,
        string? RevertedBy = null);
}
