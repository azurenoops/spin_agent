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

// ─── #648 Decomposition: AzureDiscovery domain routes ─────────────────────────────
public static partial class DashboardEndpoints
{
    private static void MapAzureDiscoveryRoutes(IEndpointRouteBuilder group, IEndpointRouteBuilder app)
    {
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
    }
}
