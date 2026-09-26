using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Microsoft.AspNetCore.Mvc;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Ato.Copilot.Mcp.Endpoints.Workspaces;

public static partial class WorkspaceOperationsEndpoints
{
    private static string SystemCapabilityActor(ITenantContext tenant) =>
        tenant.PersonId?.ToString("D") ?? throw new UnauthorizedAccessException("A current workspace Person is required.");

    private static void MapSystemSecurityCapabilityEndpoints(RouteGroupBuilder organizations)
    {
        var group = organizations.MapGroup("/systems/{systemId}/security-capabilities").RequireAuthorization()
            .WithTags("System Security Capabilities");
        group.MapGet("", ListSystemSecurityCapabilitiesAsync)
            .WithSummary("Read applied system inventory or available library capabilities")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{source}/{recordType}/{recordId}", GetSystemSecurityCapabilityAsync)
            .WithSummary("Read system-specific contributors, placements, duties and protected evidence")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{source}/component/{recordId}/placements", GetSystemComponentPlacementsAsync)
            .WithSummary("Read permitted boundary placement actions separately from source ownership")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);
        group.MapPost("/{source}/component/{recordId}/placements/assign", AssignSystemComponentPlacementAsync)
            .WithSummary("Assign a source-qualified component to a selected-system boundary using reviewed revisions")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);
        group.MapPost("/{source}/component/{recordId}/placements/{placementId}/unassign", UnassignSystemComponentPlacementAsync)
            .WithSummary("Remove only the reviewed boundary assignment while retaining shared source records")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);
        group.MapPost("/setups/prepare", PrepareSystemCapabilitySetupAsync)
            .WithSummary("Prepare an immutable multi-selection system applicability plan")
            .Produces(StatusCodes.Status201Created).Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status409Conflict);
        group.MapGet("/setups/{operationId:guid}", GetSystemCapabilityOperationAsync)
            .WithSummary("Recover a selected-system setup or removal operation");
        group.MapPost("/setups/{operationId:guid}/complete", CompleteSystemCapabilitySetupAsync)
            .WithSummary("Execute or retry only incomplete writes from the confirmed plan")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status503ServiceUnavailable);
        group.MapPost("/{source}/capability/{recordId}/removals/prepare", PrepareSystemCapabilityRemovalAsync)
            .WithSummary("Preview removing only this system's applicability while retaining shared records and placements");
        group.MapPost("/{source}/capability/{recordId}/narrative-proposals/{proposalId:guid}/review",
            ReviewSystemCapabilityNarrativeAsync).WithSummary("Review a proposal bound to this system and originating capability");
        group.MapPost("/{source}/capability/{recordId}/narrative-proposals", GenerateSystemCapabilityNarrativeAsync)
            .WithSummary("Generate a separately reviewable policy or technical proposal with exact capability provenance")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status409Conflict);
        group.MapPost("/provider/capability/{recordId:guid}/responsibilities/confirm", ConfirmSystemCapabilityResponsibilitiesAsync)
            .WithSummary("Confirm reviewed provider coverage and customer duties with required review notes")
            .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> GetSystemComponentPlacementsAsync(Guid tenantId, string systemId,
        string source, string recordId, ITenantContext tenant, ISystemWorkspaceAccessService access,
        IWorkspaceOperationsService service, CancellationToken ct)
    {
        try
        {
            var permission = await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct);
            return TypedResults.Ok(new { data = await service.GetSystemComponentPlacementsAsync(
                tenantId, systemId, source, recordId, permission, ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> AssignSystemComponentPlacementAsync(Guid tenantId, string systemId,
        string source, string recordId, ITenantContext tenant, ISystemWorkspaceAccessService access,
        IWorkspaceOperationsService service, AssignSystemComponentPlacementRequest body, CancellationToken ct)
    {
        try
        {
            var permission = await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct);
            return TypedResults.Ok(new { data = await service.AssignSystemComponentPlacementAsync(
                tenantId, systemId, source, recordId, body, permission, SystemCapabilityActor(tenant), ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> UnassignSystemComponentPlacementAsync(Guid tenantId, string systemId,
        string source, string recordId, string placementId, ITenantContext tenant, ISystemWorkspaceAccessService access,
        IWorkspaceOperationsService service, UnassignSystemComponentPlacementRequest body, CancellationToken ct)
    {
        try
        {
            var permission = await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct);
            return TypedResults.Ok(new { data = await service.UnassignSystemComponentPlacementAsync(
                tenantId, systemId, source, recordId, placementId, body, permission, SystemCapabilityActor(tenant), ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> ConfirmSystemCapabilityResponsibilitiesAsync(Guid tenantId, string systemId,
        Guid recordId, ITenantContext tenant, ISystemWorkspaceAccessService access,
        ICapabilityResponsibilityService service, ConfirmCapabilityResponsibilitiesRequest body, CancellationToken ct)
    {
        try
        {
            var permission = await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct);
            if (!permission.CanReviewResponsibilities)
                throw new UnauthorizedAccessException("An effective assigned ISSM or ISSO is required.");
            body.ValidateReviewEvidence(required: true);
            return TypedResults.Ok(new { data = await service.ConfirmAsync(systemId, recordId, body, SystemCapabilityActor(tenant), ct) });
        }
        catch (ResponsibilityReviewConflictException ex)
        { return Results.Json(Error("STALE_RESPONSIBILITY_REVIEW", ex.Message), statusCode: StatusCodes.Status409Conflict); }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<SystemSecurityCapabilityAccess> SystemCapabilityAccessAsync(
        Guid tenantId, string systemId, ITenantContext tenant, ISystemWorkspaceAccessService access, CancellationToken ct)
    {
        if (!AuthorizedOrganization(tenant, tenantId))
            throw new KeyNotFoundException("System was not found in this workspace.");
        var result = await access.GetAccessAsync(tenantId, tenant.PersonId, systemId,
            tenant.IsCspAdmin && tenant.ImpersonatedTenantId == tenantId, ct);
        if (!result.Permissions.CanRead) throw new KeyNotFoundException("System was not found in this workspace.");
        var p = result.Permissions;
        return new(p.CanRead, p.CanManageSystem,
            !tenant.IsCspAdmin && result.Roles.Any(x => x is "Issm" or "Isso"),
            p.CanManageEvidence, p.CanAuthorNarratives, p.CanReviewNarratives);
    }

    private static async Task<IResult> ListSystemSecurityCapabilitiesAsync(
        Guid tenantId, string systemId, ITenantContext tenant, ISystemWorkspaceAccessService access,
        IWorkspaceOperationsService service, CancellationToken ct,
        [FromQuery] string scope = "applied", [FromQuery] string grouping = "capability",
        [FromQuery] string? source = null, [FromQuery] string? search = null,
        [FromQuery] string? componentType = null, [FromQuery] string? boundaryId = null,
        [FromQuery] string sort = "name", [FromQuery] string direction = "asc",
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        try
        {
            var permission = await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct);
            return TypedResults.Ok(new { data = await service.ListSystemSecurityCapabilitiesAsync(tenantId, systemId,
                new(scope, grouping, source, search, componentType, boundaryId, sort, direction, page, pageSize), permission, ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> GetSystemSecurityCapabilityAsync(
        HttpContext http, Guid tenantId, string systemId, string source, string recordType, string recordId,
        ITenantContext tenant, ISystemWorkspaceAccessService access, IWorkspaceOperationsService service,
        NarrativeProposalService proposals, CancellationToken ct)
    {
        try
        {
            var permission = await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct);
            var detail = await service.GetSystemSecurityCapabilityAsync(tenantId, systemId, source, recordType, recordId, permission, ct);
            if (detail is null) return TypedResults.NotFound(Error("CAPABILITY_NOT_FOUND", "The source record is not accessible."));
            var narratives = new List<SystemCapabilityNarrative>();
            foreach (var narrative in detail.Narratives)
            {
                var references = new List<SystemCapabilityProposal>();
                foreach (var reference in narrative.Proposals)
                {
                    var current = await proposals.GetAsync(systemId, reference.Id, SystemCapabilityActor(tenant), ct);
                    references.Add(reference with { IsStale = current.IsStale, CanReview = current.CanReview });
                }
                narratives.Add(narrative with
                {
                    Proposals = references,
                    Freshness = references.Any(x => x.IsStale || x.Status is "Draft" or "PendingGeneration" or "GenerationFailed" or "NeedsRevision")
                        ? "ReviewRequired" : narrative.Freshness
                });
            }
            return TypedResults.Ok(new { data = detail with { Narratives = narratives } });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> PrepareSystemCapabilitySetupAsync(
        Guid tenantId, string systemId, ITenantContext tenant, ISystemWorkspaceAccessService access,
        IWorkspaceOperationsService service, PrepareSystemCapabilitySetupRequest body, CancellationToken ct)
    {
        try
        {
            var result = await service.PrepareSystemCapabilitySetupAsync(tenantId, systemId, body,
                await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct), ct);
            return PreparedSystemResult(tenantId, systemId, result);
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> PrepareSystemCapabilityRemovalAsync(
        Guid tenantId, string systemId, string source, string recordId, ITenantContext tenant,
        ISystemWorkspaceAccessService access, IWorkspaceOperationsService service,
        PrepareSystemCapabilityRemovalRequest body, CancellationToken ct)
    {
        try
        {
            var result = await service.PrepareSystemCapabilityRemovalAsync(tenantId, systemId, source, recordId, body,
                await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct), ct);
            return PreparedSystemResult(tenantId, systemId, result);
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static IResult PreparedSystemResult(Guid tenantId, string systemId, PreparedSystemCapabilityOperation result)
    {
        if (result.Existing) return TypedResults.Ok(new { data = result });
        return TypedResults.Created(
            $"/api/workspaces/organizations/{tenantId}/systems/{Uri.EscapeDataString(systemId)}/security-capabilities/setups/{result.Operation.OperationId}",
            new { data = result });
    }

    private static async Task<IResult> GetSystemCapabilityOperationAsync(Guid tenantId, string systemId, Guid operationId,
        ITenantContext tenant, ISystemWorkspaceAccessService access, IWorkspaceOperationsService service, CancellationToken ct)
    {
        try
        {
            var result = await service.GetSystemCapabilityOperationAsync(tenantId, systemId, operationId,
                await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct), ct);
            return result is null ? TypedResults.NotFound(Error("SETUP_NOT_FOUND", "The setup operation is not accessible."))
                : TypedResults.Ok(new { data = result });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> CompleteSystemCapabilitySetupAsync(HttpContext http, Guid tenantId,
        string systemId, Guid operationId, ITenantContext tenant, ISystemWorkspaceAccessService access,
        IWorkspaceOperationsService service, CompleteSystemCapabilitySetupRequest body, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(new { data = await service.CompleteSystemCapabilitySetupAsync(tenantId, systemId,
                operationId, body, await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct), SystemCapabilityActor(tenant), ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    /// <summary>Review input with system/source binding owned exclusively by the route.</summary>
    public sealed record ReviewSystemCapabilityNarrativeRequest(int ExpectedRevision, string Decision, string? Note);
    public sealed record GenerateSystemCapabilityNarrativeRequest(
        string ControlId, string NarrativeType, int ExpectedVersion, string SourceRevision);

    private static async Task<IResult> GenerateSystemCapabilityNarrativeAsync(HttpContext http, Guid tenantId,
        string systemId, string source, string recordId, ITenantContext tenant, ISystemWorkspaceAccessService access,
        IWorkspaceOperationsService service, NarrativeProposalService proposals, AtoCopilotContext db,
        GenerateSystemCapabilityNarrativeRequest body, CancellationToken ct)
    {
        try
        {
            var permission = await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct);
            if (!permission.CanAuthorNarratives) throw new UnauthorizedAccessException("Narrative author permission is required.");
            var detail = await service.GetSystemSecurityCapabilityAsync(tenantId, systemId, source, "capability", recordId, permission, ct)
                ?? throw new KeyNotFoundException("Capability not found.");
            if (!detail.Item.IsApplied) throw new ArgumentException("The capability is not applied to this system.");
            if (detail.Item.SourceRevision != body.SourceRevision)
                throw new SystemCapabilityConflictException("STALE_SOURCE", "Reload the source before generating.");
            if (body.NarrativeType is not ("Policy" or "Technical")
                || !detail.Controls.Any(x => x.ControlId == body.ControlId))
                throw new ArgumentException("Choose a mapped control and Policy or Technical narrative.");
            var actor = SystemCapabilityActor(tenant);
            var generated = await proposals.GenerateAsync(systemId, body.ControlId, body.NarrativeType,
                actor, body.ExpectedVersion, ct);
            permission = await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct);
            if (!permission.CanAuthorNarratives) throw new UnauthorizedAccessException("Narrative author permission changed.");
            var current = await service.GetSystemSecurityCapabilityAsync(tenantId, systemId, source, "capability",
                detail.Item.RecordId, permission, ct);
            if (current?.Item.IsApplied != true || current.Item.SourceRevision != body.SourceRevision
                || current.RelationshipRevision != detail.RelationshipRevision)
                throw new SystemCapabilityConflictException("STALE_SOURCE", "Source applicability changed during generation.");
            var sourceKind = source == "local" ? "OrganizationCapability" : "CspCapability";
            var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}:{detail.Item.RecordId}")));
            var impactId = $"capability:{generated.Id:D}:{sourceHash}";
            await proposals.QueueAsync(new(tenantId, systemId, [body.ControlId], [body.NarrativeType],
                sourceKind, detail.Item.RecordId, actor, impactId), ct);
            if (!await db.Set<NarrativeImpactReceipt>().AsNoTracking().AnyAsync(x => x.TenantId == tenantId
                    && x.RegisteredSystemId == systemId && x.ImpactId == impactId && x.NarrativeProposalId == generated.Id
                    && x.SourceKind == sourceKind && x.SourceId == detail.Item.RecordId, ct))
                throw new SystemCapabilityConflictException("STALE_SOURCE", "Narrative state changed during origin binding. Reload the proposals.");
            return TypedResults.Ok(new { data = await proposals.GetAsync(systemId, generated.Id, actor, ct) });
        }
        catch (HttpRequestException) { return Results.Json(Error("GENERATION_FAILED", "Draft generation failed; approved content is unchanged."), statusCode: 502); }
        catch (TimeoutException) { return Results.Json(Error("GENERATION_TIMEOUT", "Draft generation timed out; approved content is unchanged."), statusCode: 504); }
        catch (Exception ex) { return MapMutationError(ex); }
    }

    private static async Task<IResult> ReviewSystemCapabilityNarrativeAsync(HttpContext http, Guid tenantId,
        string systemId, string source, string recordId, Guid proposalId, ITenantContext tenant,
        ISystemWorkspaceAccessService access, IWorkspaceOperationsService service, NarrativeProposalService proposals,
        ReviewSystemCapabilityNarrativeRequest body, CancellationToken ct)
    {
        try
        {
            var permission = await SystemCapabilityAccessAsync(tenantId, systemId, tenant, access, ct);
            if (!permission.CanReviewNarratives) return Results.Json(Error("FORBIDDEN", "Narrative reviewer permission is required."), statusCode: 403);
            var detail = await service.GetSystemSecurityCapabilityAsync(tenantId, systemId, source, "capability", recordId, permission, ct);
            if (detail is null || !detail.Narratives.Any(x => x.Proposals.Any(p => p.Id == proposalId)))
                return TypedResults.NotFound(Error("NARRATIVE_PROPOSAL_NOT_FOUND", "The proposal does not originate from this capability."));
            return TypedResults.Ok(new { data = await proposals.ReviewAsync(systemId, proposalId, SystemCapabilityActor(tenant),
                body.ExpectedRevision, body.Decision, body.Note, ct) });
        }
        catch (Exception ex) { return MapMutationError(ex); }
    }
}
