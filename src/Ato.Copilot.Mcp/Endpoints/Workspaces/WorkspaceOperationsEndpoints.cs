using System.Security.Claims;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Services.Workspaces;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints.Workspaces;

public static class WorkspaceOperationsEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var csp = app.MapGroup("/api/csp").WithTags("CSP Workspace")
            .WithMetadata(new WorkspaceAuthorizedEndpoint());
        csp.MapGet("/catalog", ListProviderCatalogAsync);
        csp.MapGet("/catalog/overview", GetProviderCatalogOverviewAsync)
            .WithSummary("Read hosting provider identity and paged source artifact provenance")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status403Forbidden);
        csp.MapGet("/catalog/capabilities/{capabilityId:guid}", GetProviderCapabilityAsync)
            .WithSummary("Read one provider capability and its persisted working contributors")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
        csp.MapGet("/catalog/capabilities/{capabilityId:guid}/subscribers", ListProviderSubscribersAsync);
        csp.MapGet("/catalog/capabilities/{capabilityId:guid}/working-revision", GetWorkingRevisionAsync);
        csp.MapPut("/catalog/capabilities/{capabilityId:guid}/working-revision", SaveWorkingRevisionAsync);
        csp.MapPost("/catalog/capabilities/{capabilityId:guid}/publication-previews", GeneratePublicationPreviewAsync);
        csp.MapPost("/catalog/capabilities/{capabilityId:guid}/working-revision/approve", ApproveWorkingRevisionAsync);
        csp.MapPost("/catalog/capabilities/{capabilityId:guid}/publish", PublishAsync);
        csp.MapGet("/organizations", ListOrganizationsAsync);
        csp.MapGet("/organizations/{tenantId:guid}", GetOrganizationAsync);
        csp.MapPost("/organizations/{tenantId:guid}/provisioning", ProvisionOrganizationAsync);
        csp.MapGet("/organizations/{tenantId:guid}/provisioning", GetProvisionOrganizationAsync);
        csp.MapGet("/organizations/{tenantId:guid}/provisioning/current", GetCurrentProvisionOrganizationAsync);
        csp.MapPatch("/organizations/{tenantId:guid}/provisioning/{operationId:guid}", UpdateProvisionOrganizationAsync);

        var organizations = app.MapGroup("/api/workspaces/organizations/{tenantId:guid}")
            .WithTags("Organization Workspace")
            .WithMetadata(new WorkspaceAuthorizedEndpoint());
        organizations.MapGet("/capabilities", ListOrganizationCapabilitiesAsync);
        organizations.MapGet("/capabilities/{source}/{recordId}", GetOrganizationCapabilityAsync);
        organizations.MapGet("/catalog-access", GetOrganizationCatalogAccessAsync)
            .RequireAuthorization()
            .WithSummary("Read organization catalog authoring permission without resolving systems");
        organizations.MapPost("/catalog-additions", AddOrganizationCatalogAsync)
            .RequireAuthorization()
            .WithSummary("Atomically create or adopt organization-wide catalog records and contribution")
            .Produces(StatusCodes.Status201Created).Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status500InternalServerError);
        organizations.MapPost("/capabilities/{source}/{recordId}/narrative-proposals/{proposalId:guid}/review",
            ReviewCapabilityNarrativeAsync);
        organizations.MapPost("/capability-setups/prepare", PrepareCapabilitySetupAsync);
        organizations.MapPost("/capability-setups", CompleteCapabilitySetupAsync);
        organizations.MapGet("/capability-setups/{operationId:guid}", GetCapabilitySetupAsync);
        return app;
    }

    private static async Task<IResult> ListProviderCatalogAsync(
        ITenantContext tenant, IWorkspaceOperationsService service, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null, [FromQuery] string? grouping = null,
        [FromQuery] string? sort = null, [FromQuery] string? direction = null,
        [FromQuery] string? lifecycle = null, [FromQuery] string? review = null,
        [FromQuery] string? componentId = null)
    {
        if (!tenant.IsCspAdmin || tenant.ImpersonatedTenantId.HasValue) return Forbidden();
        if (componentId is not null && !Guid.TryParse(componentId, out _))
            return Results.BadRequest(Error("INVALID_COMPONENT_ID", "ComponentId must be a provider component GUID."));
        return Results.Ok(new { data = await service.ListProviderCatalogAsync(
            new(page, pageSize, search, grouping, sort, direction, lifecycle, review, componentId), ct) });
    }

    private static async Task<IResult> GetProviderCatalogOverviewAsync(
        ITenantContext tenant, IWorkspaceOperationsService service, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (!tenant.IsCspAdmin || tenant.ImpersonatedTenantId.HasValue) return Forbidden();
        return TypedResults.Ok(new { data = await service.GetProviderCatalogOverviewAsync(page, pageSize, ct) });
    }

    private static async Task<IResult> GetProviderCapabilityAsync(
        Guid capabilityId, ITenantContext tenant, IWorkspaceOperationsService service, CancellationToken ct)
    {
        if (!tenant.IsCspAdmin || tenant.ImpersonatedTenantId.HasValue) return Forbidden();
        var result = await service.GetProviderCapabilityAsync(capabilityId, ct);
        if (result is null)
            return TypedResults.NotFound(Error("PROVIDER_CAPABILITY_NOT_FOUND", "Provider capability was not found."));
        return TypedResults.Ok(new { data = result });
    }

    private static async Task<IResult> SaveWorkingRevisionAsync(
        HttpContext http, Guid capabilityId, ITenantContext tenant,
        IWorkspaceOperationsService service, [FromBody] SaveWorkingRevisionRequest body, CancellationToken ct)
    {
        if (!tenant.IsCspAdmin) return Forbidden();
        try
        {
            return Results.Ok(new { data = await service.SaveWorkingRevisionAsync(
                capabilityId, body, Actor(http), ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> GetWorkingRevisionAsync(
        Guid capabilityId, ITenantContext tenant,
        IWorkspaceOperationsService service, CancellationToken ct)
    {
        if (!tenant.IsCspAdmin) return Forbidden();
        var result = await service.GetWorkingRevisionAsync(capabilityId, ct);
        return result is null
            ? Results.NotFound(Error("WORKING_REVISION_NOT_FOUND", "Working revision was not found."))
            : Results.Ok(new { data = result });
    }

    private static async Task<IResult> GeneratePublicationPreviewAsync(
        Guid capabilityId, ITenantContext tenant, IWorkspaceOperationsService service,
        [FromBody] GeneratePublicationPreviewRequest body, CancellationToken ct)
    {
        if (!tenant.IsCspAdmin) return Forbidden();
        try
        {
            return Results.Ok(new
            {
                data = await service.GeneratePublicationPreviewAsync(capabilityId, body.Revision, ct)
            });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> ListProviderSubscribersAsync(
        Guid capabilityId, ITenantContext tenant, IWorkspaceOperationsService service,
        CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (!tenant.IsCspAdmin || tenant.ImpersonatedTenantId.HasValue) return Forbidden();
        try
        {
            return Results.Ok(new
            {
                data = await service.ListProviderSubscribersAsync(capabilityId, page, pageSize, ct)
            });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> ApproveWorkingRevisionAsync(
        HttpContext http, Guid capabilityId, ITenantContext tenant,
        IWorkspaceOperationsService service, [FromBody] ApproveWorkingRevisionBody body, CancellationToken ct)
    {
        if (!tenant.IsCspAdmin) return Forbidden();
        try
        {
            return Results.Ok(new { data = await service.ApproveWorkingRevisionAsync(
                capabilityId, new(body.Revision, body.PreviewId, body.PreviewHash), Actor(http), ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> PublishAsync(
        HttpContext http, Guid capabilityId, ITenantContext tenant,
        IWorkspaceOperationsService service, [FromBody] PublishWorkingRevisionRequest body, CancellationToken ct)
    {
        if (!tenant.IsCspAdmin) return Forbidden();
        try
        {
            return Results.Ok(new { data = await service.PublishAsync(capabilityId, body, Actor(http), ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> ListOrganizationsAsync(
        ITenantContext tenant, IWorkspaceOperationsService service, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null,
        [FromQuery] string? lifecycle = null, [FromQuery] string? onboarding = null,
        [FromQuery] string? review = null)
    {
        if (!tenant.IsCspAdmin) return Forbidden();
        return Results.Ok(new { data = await service.ListOrganizationsAsync(
            new(page, pageSize, search, lifecycle, onboarding, review), ct) });
    }

    private static async Task<IResult> GetOrganizationAsync(
        Guid tenantId, ITenantContext tenant, IWorkspaceOperationsService service, CancellationToken ct)
    {
        if (!tenant.IsCspAdmin) return Forbidden();
        var result = await service.GetOrganizationAsync(tenantId, ct);
        return result is null ? Results.NotFound(Error("ORGANIZATION_NOT_FOUND", "Organization was not found."))
            : Results.Ok(new { data = result });
    }

    private static async Task<IResult> ProvisionOrganizationAsync(
        Guid tenantId, ITenantContext tenant, IWorkspaceOperationsService service,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken ct)
    {
        if (!tenant.IsCspAdmin) return Forbidden();
        try
        {
            return Results.Ok(new { data = await service.GetOrCreateProvisioningAsync(tenantId, idempotencyKey, ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> GetProvisionOrganizationAsync(
        Guid tenantId, ITenantContext tenant, IWorkspaceOperationsService service,
        CancellationToken ct, [FromQuery] string idempotencyKey)
    {
        if (!tenant.IsCspAdmin) return Forbidden();
        try
        {
            var result = await service.GetProvisioningAsync(tenantId, idempotencyKey, ct);
            return result is null
                ? Results.NotFound(Error("PROVISIONING_NOT_FOUND", "Provisioning operation was not found."))
                : Results.Ok(new { data = result });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> UpdateProvisionOrganizationAsync(
        HttpContext http, Guid tenantId, Guid operationId, ITenantContext tenant,
        IWorkspaceOperationsService service, IOrganizationMembershipService memberships,
        [FromBody] UpdateProvisioningRequest body, CancellationToken ct)
    {
        if (!tenant.IsCspAdmin) return Forbidden();
        var executionStarted = false;
        try
        {
            var state = await service.UpdateProvisioningAsync(tenantId, operationId, body, ct);
            executionStarted = true;
            if (state.MembershipState != "Completed")
            {
                await memberships.GrantAsync(http, tenantId,
                    new(body.DirectoryTenantId, body.ObjectId, body.PersonId), ct);
                state = await service.UpdateProvisioningAsync(tenantId, operationId, body, ct);
            }

            if (state.AdministratorState != "Completed")
            {
                await memberships.EnrollAdministratorAsync(http, tenantId, body.PersonId, ct);
                state = await service.UpdateProvisioningAsync(tenantId, operationId, body, ct);
            }
            return Results.Ok(new { data = state });
        }
        catch (Exception ex)
        {
            if (executionStarted)
                await service.RecordProvisioningFailureAsync(tenantId, operationId, ex.Message, ct);
            return MapMutationError(ex);
        }
    }

    private static async Task<IResult> GetCurrentProvisionOrganizationAsync(
        Guid tenantId, ITenantContext tenant, IWorkspaceOperationsService service,
        CancellationToken ct)
    {
        if (!tenant.IsCspAdmin) return Forbidden();
        var result = await service.GetCurrentProvisioningAsync(tenantId, ct);
        return result is null
            ? Results.NotFound(Error("PROVISIONING_NOT_FOUND", "Provisioning operation was not found."))
            : Results.Ok(new { data = result });
    }

    private static async Task<IResult> ListOrganizationCapabilitiesAsync(
        Guid tenantId, ITenantContext tenant, IWorkspaceOperationsService service,
        ISystemWorkspaceAccessService access,
        IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> factory, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null,
        [FromQuery] string? grouping = null, [FromQuery] string? sort = null,
        [FromQuery] string? direction = null, [FromQuery] string? source = null,
        [FromQuery] string? systemId = null, [FromQuery] string? componentId = null)
    {
        if (!AuthorizedOrganization(tenant, tenantId)) return Results.NotFound();
        try
        {
            if (systemId is null && !await AuthorizedCatalogReadAsync(tenantId, tenant, factory, ct))
                return Results.NotFound();
            var readableSystems = systemId is null ? [] : await AuthorizedSystemsAsync(
                tenantId, tenant, access, factory, x => x.CanRead, systemId, ct);
            if (systemId is not null && readableSystems.Count == 0) return Results.NotFound();
            return Results.Ok(new { data = await service.ListOrganizationCapabilitiesAsync(
                tenantId, new(page, pageSize, search, grouping, sort, direction, ComponentId: componentId),
                source, systemId, readableSystems, ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> GetOrganizationCapabilityAsync(
        Guid tenantId, string source, string recordId, ITenantContext tenant,
        IWorkspaceOperationsService service, ISystemWorkspaceAccessService access,
        IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> factory,
        CancellationToken ct, [FromQuery] string? systemId = null, [FromQuery] string? recordType = null)
    {
        if (!AuthorizedOrganization(tenant, tenantId)) return Results.NotFound();
        if (recordType is not null and not ("component" or "capability"))
            return Results.BadRequest(Error("INVALID_REQUEST", "Record type must be component or capability."));
        if (systemId is null && !await AuthorizedCatalogReadAsync(tenantId, tenant, factory, ct))
            return Results.NotFound();
        var readableSystems = systemId is null ? [] : await AuthorizedSystemsAsync(
            tenantId, tenant, access, factory, x => x.CanRead, systemId, ct);
        if (systemId is not null && readableSystems.Count == 0) return Results.NotFound();
        var result = await service.GetOrganizationCapabilityAsync(
            tenantId, source, recordId, systemId, readableSystems, ct, recordType);
        return result is null ? Results.NotFound(Error("CAPABILITY_NOT_FOUND", "Capability was not found."))
            : Results.Ok(new { data = result });
    }

    private static async Task<IResult> GetOrganizationCatalogAccessAsync(
        Guid tenantId, ITenantContext tenant, IWorkspaceService workspace,
        IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> factory, CancellationToken ct)
    {
        if (!await AuthorizedCatalogReadAsync(tenantId, tenant, factory, ct)) return Results.NotFound();
        return TypedResults.Ok(new { data = new { canManageCatalog = CanManageCatalog(tenantId, tenant, workspace) } });
    }

    private static async Task<IResult> AddOrganizationCatalogAsync(
        HttpContext http, Guid tenantId, ITenantContext tenant, IWorkspaceService workspace,
        IWorkspaceOperationsService service,
        IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> factory,
        [FromBody] OrganizationCatalogAdditionRequest body, ILogger<WorkspaceOperationsService> logger, CancellationToken ct)
    {
        if (!await AuthorizedCatalogReadAsync(tenantId, tenant, factory, ct)) return Results.NotFound();
        if (!CanManageCatalog(tenantId, tenant, workspace))
            return Results.Json(Error("CATALOG_WRITE_FORBIDDEN", "Organization catalog management permission is required."),
                statusCode: StatusCodes.Status403Forbidden);
        try
        {
            var result = await service.AddOrganizationCatalogAsync(tenantId, body, Actor(http), ct);
            var payload = new { data = result };
            return result.Existing ? Results.Ok(payload)
                : Results.Created($"/api/workspaces/organizations/{tenantId}/capabilities/{result.Source}/" +
                    $"{Uri.EscapeDataString(result.RecordId)}?recordType={result.RecordType}", payload);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException
            or InvalidOperationException or DbUpdateConcurrencyException)
        {
            logger.LogWarning("Organization catalog addition rejected for {TenantId}: {FailureType}",
                tenantId, ex.GetType().Name);
            return MapMutationError(ex);
        }
        catch (DbUpdateException)
        {
            logger.LogError("Organization catalog persistence failed for {TenantId}", tenantId);
            return Results.Json(Error("CATALOG_PERSISTENCE_FAILED", "The catalog addition could not be saved."),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static bool CanManageCatalog(Guid tenantId, ITenantContext tenant, IWorkspaceService workspace) =>
        !tenant.IsCspAdmin && workspace.Current is { Kind: "organization", Mode: "ordinary" } current
        && current.TenantId == tenantId && current.Permissions.CanManageOrganization;

    private static async Task<bool> AuthorizedCatalogReadAsync(
        Guid tenantId, ITenantContext tenant,
        IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> factory, CancellationToken ct)
    {
        if (!AuthorizedOrganization(tenant, tenantId)) return false;
        if (tenant.IsCspAdmin && tenant.ImpersonatedTenantId == tenantId) return true;
        if (tenant.PersonId is not { } personId) return false;
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.OrganizationMemberships.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId && x.PersonId == personId && x.RevokedAt == null
            && db.Persons.IgnoreQueryFilters().Any(p => p.TenantId == tenantId && p.Id == personId), ct);
    }

    private static async Task<IResult> CompleteCapabilitySetupAsync(
        HttpContext http, Guid tenantId, ITenantContext tenant,
        IWorkspaceOperationsService service, ISystemWorkspaceAccessService access,
        IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> factory,
        [FromBody] CompleteCapabilitySetupRequest body, CancellationToken ct)
    {
        if (!AuthorizedOrganization(tenant, tenantId)) return Results.NotFound();
        if (tenant.IsCspAdmin && !tenant.ImpersonatedTenantId.HasValue)
            return Results.Json(Error("PROVIDER_MUTATION_DENIED", "Enter authorized support mode to mutate an organization."),
                statusCode: StatusCodes.Status403Forbidden);
        try
        {
            var manageableSystems = await AuthorizedSystemsAsync(
                tenantId, tenant, access, factory, x => x.CanManageSystem, body.SystemId, ct);
            if (manageableSystems.Count == 0)
                return Results.Json(Error("SYSTEM_SETUP_FORBIDDEN", "System setup permission is required."),
                    statusCode: StatusCodes.Status403Forbidden);
            return Results.Ok(new { data = await service.CompleteSetupAsync(
                tenantId, body, Actor(http), manageableSystems, ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> PrepareCapabilitySetupAsync(
        Guid tenantId, ITenantContext tenant,
        IWorkspaceOperationsService service, ISystemWorkspaceAccessService access,
        IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> factory,
        [FromBody] PrepareCapabilitySetupRequest body, CancellationToken ct)
    {
        if (!AuthorizedOrganization(tenant, tenantId)) return Results.NotFound();
        if (tenant.IsCspAdmin && !tenant.ImpersonatedTenantId.HasValue)
            return Results.Json(
                Error("PROVIDER_MUTATION_DENIED", "Enter authorized support mode to prepare organization setup."),
                statusCode: StatusCodes.Status403Forbidden);
        try
        {
            var manageableSystems = await AuthorizedSystemsAsync(
                tenantId, tenant, access, factory, x => x.CanManageSystem, body.SystemId, ct);
            if (manageableSystems.Count == 0)
                return Results.Json(Error("SYSTEM_SETUP_FORBIDDEN", "System setup permission is required."),
                    statusCode: StatusCodes.Status403Forbidden);
            var result = await service.PrepareSetupAsync(tenantId, body, manageableSystems, ct);
            var payload = new { data = result.Operation };
            return result.Existing
                ? Results.Ok(payload)
                : Results.Created(
                    $"/api/workspaces/organizations/{tenantId}/capability-setups/{result.Operation.OperationId}",
                    payload);
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> GetCapabilitySetupAsync(
        Guid tenantId, Guid operationId, ITenantContext tenant,
        IWorkspaceOperationsService service, ISystemWorkspaceAccessService access,
        IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> factory, CancellationToken ct)
    {
        if (!AuthorizedOrganization(tenant, tenantId)) return Results.NotFound();
        if (tenant.IsCspAdmin && !tenant.ImpersonatedTenantId.HasValue)
            return Results.Json(
                Error("PROVIDER_MUTATION_DENIED", "Enter authorized support mode to access setup operations."),
                statusCode: StatusCodes.Status403Forbidden);
        var result = await service.GetCapabilitySetupAsync(tenantId, operationId, ct);
        if (result?.SystemId is null) return Results.NotFound();
        var manageable = await AuthorizedSystemsAsync(
            tenantId, tenant, access, factory, x => x.CanManageSystem, result.SystemId, ct);
        if (manageable.Count == 0)
            return Results.Json(Error("SYSTEM_SETUP_FORBIDDEN", "System setup permission is required."),
                statusCode: StatusCodes.Status403Forbidden);
        return Results.Ok(new { data = result });
    }

    private static async Task<IResult> ReviewCapabilityNarrativeAsync(
        HttpContext http, Guid tenantId, string source, string recordId, Guid proposalId,
        ITenantContext tenant, ISystemWorkspaceAccessService access,
        IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> factory,
        NarrativeProposalService proposals, [FromBody] ReviewCapabilityNarrativeRequest body,
        CancellationToken ct)
    {
        if (!AuthorizedOrganization(tenant, tenantId)) return Results.NotFound();
        var systems = await AuthorizedSystemsAsync(
            tenantId, tenant, access, factory, x => x.CanReviewNarratives, body.SystemId, ct);
        if (systems.Count == 0) return Results.NotFound();
        await using var db = await factory.CreateDbContextAsync(ct);
        var sourceKind = source switch
        {
            "provider" => "CspCapability",
            "local" => "OrganizationCapability",
            _ => null
        };
        var matches = sourceKind is not null && await db.NarrativeProposals.AsNoTracking().AnyAsync(x =>
            x.Id == proposalId && x.TenantId == tenantId && x.RegisteredSystemId == body.SystemId
            && x.ChangeSourceKind == sourceKind && x.ChangeSourceId == recordId, ct);
        if (!matches) return Results.NotFound(Error("NARRATIVE_PROPOSAL_NOT_FOUND", "Narrative proposal was not found."));
        try
        {
            return Results.Ok(new { data = await proposals.ReviewAsync(
                body.SystemId, proposalId, Actor(http), body.ExpectedRevision, body.Decision, body.Note, ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IReadOnlyList<string>> AuthorizedSystemsAsync(
        Guid tenantId, ITenantContext tenant, ISystemWorkspaceAccessService access,
        IDbContextFactory<Ato.Copilot.Core.Data.Context.AtoCopilotContext> factory,
        Func<SystemWorkspacePermissions, bool> permission, string? requestedSystemId, CancellationToken ct)
    {
        string[] systemIds;
        if (!string.IsNullOrWhiteSpace(requestedSystemId))
            systemIds = [requestedSystemId];
        else
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            systemIds = await db.RegisteredSystems.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive)
                .OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(ct);
        }
        var oversight = tenant.IsCspAdmin && tenant.ImpersonatedTenantId == tenantId;
        var authorized = new List<string>();
        foreach (var batch in systemIds.Chunk(100))
        {
            var decisions = await access.GetAccessBatchAsync(
                tenantId, tenant.PersonId, batch, oversight, ct);
            authorized.AddRange(decisions.Where(x => permission(x.Permissions)).Select(x => x.SystemId));
        }
        return authorized;
    }

    private static bool AuthorizedOrganization(ITenantContext tenant, Guid tenantId) =>
        tenant.IsWorkspaceRequest && tenant.EffectiveTenantId == tenantId
        && (!tenant.IsCspAdmin || tenant.ImpersonatedTenantId.HasValue);

    private static string Actor(HttpContext http) =>
        http.User.FindFirstValue("oid") ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";

    private static IResult MapMutationError(Exception exception) => exception switch
    {
        ArgumentException => Results.BadRequest(Error("INVALID_REQUEST", exception.Message)),
        KeyNotFoundException => Results.NotFound(Error("NOT_FOUND", exception.Message)),
        DbUpdateConcurrencyException => Results.Conflict(Error("STALE_REVISION", exception.Message)),
        UnauthorizedAccessException => Results.Json(Error("FORBIDDEN", exception.Message),
            statusCode: StatusCodes.Status403Forbidden),
        WorkspaceException workspace => Results.Json(Error(workspace.Code, workspace.Message),
            statusCode: workspace.StatusCode),
        InvalidOperationException => Results.Conflict(Error("CONFLICT", exception.Message)),
        _ => throw exception
    };

    private static IResult Forbidden() => Results.Json(
        Error("FORBIDDEN_NOT_CSP_ADMIN", "CSP administrator permission is required."),
        statusCode: StatusCodes.Status403Forbidden);

    private static object Error(string code, string message) => new { error = new { code, message } };

    public sealed record GeneratePublicationPreviewRequest(long Revision);
    public sealed record ApproveWorkingRevisionBody(long Revision, Guid PreviewId, string PreviewHash);
    public sealed record ReviewCapabilityNarrativeRequest(
        string SystemId, int ExpectedRevision, string Decision, string? Note);
}
