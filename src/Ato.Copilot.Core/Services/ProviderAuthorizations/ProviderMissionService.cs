using System.Data;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderMissionService(AtoCopilotContext db, ITenantContext tenant,
    ISystemWorkspaceAccessService access, ICapabilityResponsibilityService responsibilities,
    ILogger<ProviderMissionService>? logger = null) : IProviderMissionService
{
    private Guid TenantId => tenant.EffectiveTenantId;
    private static bool CanAssociate(SystemWorkspaceAccessResponse permission) =>
        permission.Roles.Any(x => x is "MissionOwner" or "SystemOwner" or "Issm" or "Isso");

    private async Task<SystemWorkspaceAccessResponse> AccessAsync(string systemId, CancellationToken ct)
    {
        if (tenant.IsCspAdmin || tenant.ImpersonatedTenantId.HasValue || TenantId == Guid.Empty || tenant.PersonId is null)
            throw new UnauthorizedAccessException("Use the assigned mission workspace; provider or support context cannot act for the customer.");
        var result = await access.GetAccessAsync(TenantId, tenant.PersonId, systemId, false, ct);
        if (!result.Permissions.CanRead || !await db.RegisteredSystems.AnyAsync(x =>
                x.Id == systemId && x.TenantId == TenantId && x.IsActive, ct))
            throw new KeyNotFoundException("System not found in this mission workspace.");
        return result;
    }

    public async Task AuthorizeAsync(string systemId, bool manage, bool covered, CancellationToken ct)
    {
        var permission = await AccessAsync(systemId, ct);
        if (covered ? !permission.Permissions.CanDecideAuthorization || !permission.Roles.Contains("AuthorizingOfficial")
            : manage && !CanAssociate(permission))
            throw new UnauthorizedAccessException(covered ? "An effective assigned AO is required for recorded coverage review."
                : "An effective assigned Mission Owner, System Owner, ISSM or ISSO is required.");
    }

    // These narrowly qualified projections are the only crossing of provider-private query filters.
    // Authorization precedes every caller; no provider source contents are returned.
    private IQueryable<ProviderHostingAssignment> Allocations(string systemId) =>
        db.Set<ProviderHostingAssignment>().IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TargetTenantId == TenantId && x.SystemId == systemId && db.CspProfiles.Any(p => p.Id == x.ProviderId));

    private async Task<ProviderHostingAssignment> AllocationAsync(string systemId, Guid id, CancellationToken ct) =>
        await Allocations(systemId).SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new KeyNotFoundException("Allocation not found for this tenant and system.");

    private Task<ProviderOffering> OfferingAsync(ProviderHostingAssignment allocation, CancellationToken ct) =>
        db.Set<ProviderOffering>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
            x.Id == allocation.OfferingId && x.ProviderId == allocation.ProviderId, ct);

    private Task<ProviderHostingScopeRevision> HostingAsync(ProviderHostingAssignment allocation, CancellationToken ct) =>
        db.Set<ProviderHostingScopeRevision>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x =>
            x.Id == allocation.HostingScopeRevisionId && x.OfferingId == allocation.OfferingId && x.ProviderId == allocation.ProviderId, ct);

    private IQueryable<MissionProviderRelationshipReview> Relationships(string systemId) =>
        db.Set<MissionProviderRelationshipReview>().Where(x => x.TenantId == TenantId && x.SystemId == systemId);

    public async Task<PagedResult<MissionProviderRelationshipResponse>> RelationshipsAsync(
        string systemId, int page, int pageSize, CancellationToken ct)
    {
        var permission = await AccessAsync(systemId, ct);
        var allocations = await PageAsync(Allocations(systemId).OrderBy(x => x.Id), page, pageSize, x => x, ct);
        var rows = new List<MissionProviderRelationshipResponse>();
        foreach (var allocation in allocations.Items)
            rows.Add(await ProjectAsync(allocation, await Relationships(systemId).AsNoTracking()
                .SingleOrDefaultAsync(x => x.AssignmentId == allocation.Id && x.ProviderId == allocation.ProviderId
                    && x.OfferingId == allocation.OfferingId, ct), permission, ct));
        return new(rows, page, pageSize, allocations.Total);
    }

    public async Task<MissionProviderRelationshipResponse> AssociateAsync(string systemId,
        CreateMissionProviderRelationshipRequest request, string actor, CancellationToken ct, string? key = null)
    {
        await AuthorizeAsync(systemId, true, false, ct);
        var allocation = await AllocationAsync(systemId, request.AssignmentId, ct);
        return await WriteAsync(allocation, "Associate", key, request, actor, async () =>
        {
            await AuthorizeAsync(systemId, true, false, ct);
            allocation = await AllocationAsync(systemId, request.AssignmentId, ct);
            Expected(allocation, request.ExpectedAssignmentRevision);
            var offering = await OfferingAsync(allocation, ct);
            if (offering.Lifecycle == "Retired" || offering.CurrentHostingScopeRevisionId != allocation.HostingScopeRevisionId)
                throw new DbUpdateConcurrencyException("This allocation requires a current provider hosting scope before association.");
            var relationship = await Relationships(systemId).SingleOrDefaultAsync(x =>
                x.AssignmentId == allocation.Id && x.ProviderId == allocation.ProviderId && x.OfferingId == allocation.OfferingId, ct);
            if (relationship is null)
            {
                relationship = new()
                {
                    ProviderId = allocation.ProviderId, OfferingId = allocation.OfferingId, TenantId = TenantId, SystemId = systemId,
                    AssignmentId = allocation.Id, AssignmentRevision = allocation.Revision, CreatedBy = actor,
                    HistoryJson = Json(new[] { new { Action = "Associated", Actor = actor, At = DateTimeOffset.UtcNow } })
                };
                db.Add(relationship);
            }
            return await ProjectAsync(allocation, relationship, await AccessAsync(systemId, ct), ct);
        }, ct);
    }

    private async Task<MissionProviderRelationshipResponse> ProjectAsync(ProviderHostingAssignment allocation,
        MissionProviderRelationshipReview? relationship, SystemWorkspaceAccessResponse permission, CancellationToken ct)
    {
        var offering = await OfferingAsync(allocation, ct);
        var hosting = await HostingAsync(allocation, ct);
        var stale = relationship is null || relationship.ReviewRequired || relationship.AssignmentRevision != allocation.Revision
            || offering.CurrentHostingScopeRevisionId != hosting.Id || offering.Lifecycle == "Retired";
        if (relationship?.State == "ExplicitlyCoveredByRecordedScope")
            stale |= (await CoverageBlockersAsync(allocation, relationship.AuthorizationRevisionId,
                relationship.BoundaryRevisionId, Read<ProviderCitation[]>(relationship.EvidenceJson), ct)).Count != 0;
        var systemName = await db.RegisteredSystems.Where(x => x.Id == allocation.SystemId && x.TenantId == TenantId)
            .Select(x => x.Name).SingleOrDefaultAsync(ct);
        var providerName = await db.CspProfiles.Where(x => x.Id == allocation.ProviderId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
        return new(relationship?.Id, relationship?.Revision ?? 0, allocation.Id, allocation.Revision, offering.Id,
            allocation.SystemId, relationship?.State ?? "Undetermined", stale, relationship?.AuthorizationRevisionId,
            relationship?.BoundaryRevisionId, relationship?.ReviewedBy, relationship?.ReviewedAt,
            Read<ProviderAzureScope[]>(allocation.AssignedScopesJson), offering.Name, providerName, systemName,
            Read<CreateProviderHostingScopeRequest>(hosting.SnapshotJson).Name,
            relationship is null && CanAssociate(permission) && offering.Lifecycle != "Retired"
                && offering.CurrentHostingScopeRevisionId == hosting.Id);
    }

    private Task<T> WriteAsync<T>(ProviderHostingAssignment allocation, string action, string? key,
        object intent, string actor, Func<Task<T>> mutation, CancellationToken ct)
    {
        Text(actor, "actor", 200);
        if (key is not null) Text(key, "Idempotency-Key", 100);
        var operation = $"Mission{action}:{TenantId:D}:{allocation.SystemId}";
        var hash = Hash(Json(intent));
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A retry must reload both mission rows and canonical subscription state in the shared context.
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var prior = key is null ? null : await db.Set<ProviderAuthorizationOperation>().IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.ProviderId == allocation.ProviderId && x.OperationScope == operation
                    && x.IdempotencyKey == key, ct);
            if (prior is not null)
            {
                if (prior.IntentHash != hash) throw new DbUpdateConcurrencyException("The key identifies a different mission intent.");
                return Read<T>(prior.ResponseJson);
            }
            try
            {
                var result = await mutation();
                if (key is not null)
                    db.Add(new ProviderAuthorizationOperation
                    {
                        ProviderId = allocation.ProviderId, OfferingId = allocation.OfferingId, OperationScope = operation,
                        IdempotencyKey = key, IntentHash = hash, ResponseJson = Json(result), CreatedBy = actor
                    });
                await db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
                return result;
            }
            catch (DbUpdateException) when (key is not null && transaction is not null)
            {
                await transaction.RollbackAsync(ct);
                await transaction.DisposeAsync();
                db.ChangeTracker.Clear();
                var winner = await db.Set<ProviderAuthorizationOperation>().IgnoreQueryFilters().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.ProviderId == allocation.ProviderId && x.OperationScope == operation
                        && x.IdempotencyKey == key, ct);
                if (winner is null) throw;
                if (winner.IntentHash != hash)
                {
                    logger?.LogWarning("Concurrent mission {Action} rejected because the intent changed", action);
                    throw new DbUpdateConcurrencyException("The key identifies a different mission intent.");
                }
                logger?.LogWarning("Concurrent mission {Action} recovered from its retained idempotent response", action);
                return Read<T>(winner.ResponseJson);
            }
        });
    }
}
