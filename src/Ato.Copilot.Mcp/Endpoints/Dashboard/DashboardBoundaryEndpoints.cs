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

// ─── #648 Decomposition: Boundary domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapBoundaryRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
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
                // T057: use FirstOrDefaultAsync so the tenant query filter applies.
                var system = await context.RegisteredSystems.FirstOrDefaultAsync(s => s.Id == systemId, ct);
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
                // T057: use FirstOrDefaultAsync so the tenant query filter applies.
                var system = await context.RegisteredSystems.FirstOrDefaultAsync(s => s.Id == systemId, ct);
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
    }
}
