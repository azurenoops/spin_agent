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

// ─── #648 Decomposition: Components domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapComponentRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
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
                // T057: use FirstOrDefaultAsync so the tenant query filter applies
                // (FindAsync bypasses query filters in EF Core).
                var component = await db.SystemComponents.FirstOrDefaultAsync(c => c.Id == componentId, ct);
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
                    // Verify capability exists (T057: FirstOrDefaultAsync to apply tenant filter)
                    var cap = await db.SecurityCapabilities.FirstOrDefaultAsync(c => c.Id == capId, ct);
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
                [FromQuery] string? sourceUrl,
                [FromQuery] string? sourceUrls,
                CapabilityService capService,
                DocumentNarrativeGenerateAdapterTool documentNarrativeTool,
                CancellationToken ct) =>
            {
                if (!string.IsNullOrWhiteSpace(sourceUrl) || !string.IsNullOrWhiteSpace(sourceUrls))
                {
                    var toolArgs = new Dictionary<string, object?>
                    {
                        ["system_id"] = systemId,
                        ["control_id"] = controlId,
                        ["save_draft"] = "true",
                        ["change_reason"] = "Dashboard regenerate using configured document sources",
                    };

                    if (!string.IsNullOrWhiteSpace(sourceUrl))
                        toolArgs["source_url"] = sourceUrl;
                    if (!string.IsNullOrWhiteSpace(sourceUrls))
                        toolArgs["source_urls"] = sourceUrls;

                    var toolResult = await documentNarrativeTool.ExecuteAsync(toolArgs, ct);
                    try
                    {
                        using var json = System.Text.Json.JsonDocument.Parse(toolResult);
                        var root = json.RootElement;
                        var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
                        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) &&
                            root.TryGetProperty("data", out var dataEl) &&
                            dataEl.TryGetProperty("suggested_narrative", out var narrativeEl))
                        {
                            return Results.Ok(new { narrative = narrativeEl.GetString() ?? string.Empty });
                        }

                        if (root.TryGetProperty("message", out var messageEl))
                        {
                            return Results.BadRequest(new ErrorResponse
                            {
                                Error = messageEl.GetString() ?? "Narrative generation from document sources failed",
                                ErrorCode = "DOCUMENT_SOURCE_GENERATION_FAILED",
                            });
                        }

                        return Results.BadRequest(new ErrorResponse
                        {
                            Error = "Narrative generation from document sources failed",
                            ErrorCode = "DOCUMENT_SOURCE_GENERATION_FAILED",
                        });
                    }
                    catch
                    {
                        return Results.BadRequest(new ErrorResponse
                        {
                            Error = "Narrative generation from document sources returned an invalid payload",
                            ErrorCode = "DOCUMENT_SOURCE_INVALID_PAYLOAD",
                        });
                    }
                }

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
                [FromQuery] string? sourceUrl,
                [FromQuery] string? sourceUrls,
                AtoCopilotContext context,
                CapabilityService capService,
                DocumentNarrativeGenerateAdapterTool documentNarrativeTool,
                CancellationToken ct) =>
            {
                if (!string.IsNullOrWhiteSpace(sourceUrl) || !string.IsNullOrWhiteSpace(sourceUrls))
                {
                    var systemExists = await context.RegisteredSystems
                        .AnyAsync(s => s.Id == systemId && s.IsActive, ct);
                    if (!systemExists)
                    {
                        return Results.NotFound(new ErrorResponse
                        {
                            Error = "System or capability not found",
                            ErrorCode = "NOT_FOUND",
                        });
                    }

                    var capabilityExists = await context.SecurityCapabilities
                        .AnyAsync(c => c.Id == capabilityId, ct);
                    if (!capabilityExists)
                    {
                        return Results.NotFound(new ErrorResponse
                        {
                            Error = "System or capability not found",
                            ErrorCode = "NOT_FOUND",
                        });
                    }

                    var impls = await context.ControlImplementations
                        .Where(ci => ci.RegisteredSystemId == systemId && ci.SecurityCapabilityId == capabilityId)
                        .Select(ci => new { ci.ControlId, ci.IsManuallyCustomized })
                        .ToListAsync(ct);

                    var totalControls = impls.Count;
                    var regenerated = 0;
                    var skippedCustom = 0;
                    var failed = 0;
                    var regeneratedControlIds = new List<string>();

                    foreach (var impl in impls)
                    {
                        if (impl.IsManuallyCustomized)
                        {
                            skippedCustom++;
                            continue;
                        }

                        var toolArgs = new Dictionary<string, object?>
                        {
                            ["system_id"] = systemId,
                            ["control_id"] = impl.ControlId,
                            ["save_draft"] = "true",
                            ["change_reason"] = "Bulk regenerate using configured document sources",
                        };

                        if (!string.IsNullOrWhiteSpace(sourceUrl))
                            toolArgs["source_url"] = sourceUrl;
                        if (!string.IsNullOrWhiteSpace(sourceUrls))
                            toolArgs["source_urls"] = sourceUrls;

                        try
                        {
                            var toolResult = await documentNarrativeTool.ExecuteAsync(toolArgs, ct);
                            using var json = System.Text.Json.JsonDocument.Parse(toolResult);
                            var root = json.RootElement;
                            var status = root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;

                            if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                            {
                                regenerated++;
                                regeneratedControlIds.Add(impl.ControlId);
                            }
                            else
                            {
                                failed++;
                            }
                        }
                        catch
                        {
                            failed++;
                        }
                    }

                    return Results.Ok(new
                    {
                        totalControls,
                        regenerated,
                        skippedCustom,
                        failed,
                        regeneratedControlIds,
                    });
                }

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
        group.MapPost("/ai/component-description", GenerateComponentDescription)
            .WithName("GenerateComponentDescription");

        async Task<IResult> GenerateComponentDescription(
                GenerateComponentDescriptionRequest body,
                [FromServices] IChatClient? chatClient,
                CancellationToken ct)
        {
            if (chatClient is null)
                return Results.StatusCode(503);

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
        }

        // ─── AI Capability Description ─────────────────────────────────────
        group.MapPost("/ai/capability-description", GenerateCapabilityDescription)
            .WithName("GenerateCapabilityDescription");

        async Task<IResult> GenerateCapabilityDescription(
                GenerateCapabilityDescriptionRequest body,
                [FromServices] IChatClient? chatClient,
                CancellationToken ct)
        {
            if (chatClient is null)
                return Results.StatusCode(503);

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
        }

        // ─── AI System Description ─────────────────────────────────────────
        group.MapPost("/ai/system-description", GenerateSystemDescription)
            .WithName("GenerateSystemDescription");

        async Task<IResult> GenerateSystemDescription(
                GenerateSystemDescriptionRequest body,
                [FromServices] IChatClient? chatClient,
                CancellationToken ct)
        {
            if (chatClient is null)
                return Results.StatusCode(503);

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
        }

        // ─── Capabilities (US3) ──────────────────────────────────────────────
    }
}
