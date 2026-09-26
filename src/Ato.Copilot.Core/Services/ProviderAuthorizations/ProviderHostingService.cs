using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed class ProviderHostingService(ProviderAuthorizationStore store) : IProviderHostingService
{
    public Task<ProviderHostingScopeResponse> CreateScopeAsync(Guid offeringId, CreateProviderHostingScopeRequest request,
        string key, string actor, CancellationToken ct) =>
        store.WriteAsync(offeringId, $"HostingScopeCreated:{offeringId:D}", key, request, actor, async (db, provider, offering) =>
        {
            Expected(offering!, request.ExpectedOfferingRevision);
            if (offering!.Lifecycle == "Retired" || request.PredecessorRevisionId != offering.CurrentHostingScopeRevisionId)
                throw new DbUpdateConcurrencyException("Select the current hosting scope of an active offering as predecessor.");
            Bounded(request.PermittedScopes, "permitted scopes", 1);
            Bounded(request.Exclusions, "exclusions");
            var body = request with
            {
                Name = Text(request.Name, "hosting scope name", 256),
                PermittedScopes = request.PermittedScopes.Select(Normalize).Distinct().ToArray(),
                Exclusions = request.Exclusions.Select(x => new ProviderHostingExclusion(Normalize(x.Scope),
                    Text(x.Rationale, "exclusion rationale", 2000))).ToArray()
            };
            var environments = Read<string[]>(offering.EnvironmentsJson);
            if (body.PermittedScopes.Any(x => !environments.Contains(x.Cloud))
                || body.Exclusions.Any(x => !body.PermittedScopes.Any(p => Contains(p, x.Scope))))
                throw new ArgumentException("Hosting scopes must use the offering environments; exclusions must be within a permitted scope.");
            await store.CitationsAsync(db, provider, body.Citations, ct);
            var revision = (await db.Set<ProviderHostingScopeRevision>().Where(x => x.ProviderId == provider && x.OfferingId == offeringId)
                .Select(x => (long?)x.Revision).MaxAsync(ct) ?? 0) + 1;
            var row = new ProviderHostingScopeRevision
            {
                ProviderId = provider, OfferingId = offeringId, Revision = revision,
                PredecessorId = request.PredecessorRevisionId, SnapshotJson = Json(body), SnapshotHash = Hash(Json(body)), CreatedBy = actor
            };
            db.Add(row);
            offering.CurrentHostingScopeRevisionId = row.Id;
            offering.Revision++;
            await ProviderAuthorizationService.InvalidateAsync(db, offering, actor, "A new immutable hosting scope requires applicability review", ct);
            var impactId = db.Set<ProviderAuthorizationImpactReview>().Local.Single(x => db.Entry(x).State == EntityState.Added).Id;
            return ProjectScope(row, offering.Revision, impactId);
        }, ct);

    public async Task<PagedResult<ProviderHostingScopeResponse>> ScopesAsync(Guid offeringId, int page, int pageSize, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        return await PageAsync(db.Set<ProviderHostingScopeRevision>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offeringId)
            .OrderByDescending(x => x.Revision), page, pageSize, x => ProjectScope(x, offering.Revision), ct);
    }

    public async Task<ProviderHostingScopeResponse> ScopeAsync(Guid offeringId, Guid revisionId, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        return ProjectScope(await RequireScopeAsync(db, offering, revisionId, ct), offering.Revision);
    }

    public Task<ProviderHostingAssignmentResponse> AssignAsync(Guid offeringId, CreateProviderHostingAssignmentRequest request,
        string key, string actor, CancellationToken ct) =>
        store.WriteAsync(offeringId, $"HostingAssigned:{offeringId:D}", key, request, actor, async (db, provider, offering) =>
        {
            if (offering!.Lifecycle == "Retired" || request.HostingScopeRevisionId != offering.CurrentHostingScopeRevisionId)
                throw new DbUpdateConcurrencyException("Use the current hosting scope of an active offering.");
            var hosting = await RequireScopeAsync(db, offering, request.HostingScopeRevisionId, ct);
            var body = Read<CreateProviderHostingScopeRequest>(hosting.SnapshotJson);
            var systemId = Text(request.SystemId, "system ID", 36);
            if (!await db.RegisteredSystems.AnyAsync(x => x.Id == systemId && x.TenantId == request.TargetTenantId && x.IsActive, ct)
                || !await db.Tenants.AnyAsync(x => x.Id == request.TargetTenantId && x.Status == TenantStatus.Active, ct))
                throw new KeyNotFoundException("An active system in the exact hosted customer tenant is required.");
            Bounded(request.AssignedScopes, "assigned scopes", 1);
            var scopes = request.AssignedScopes.Select(Normalize).Distinct().ToArray();
            if (!Fits(body, scopes))
                throw new ArgumentException("Assigned scopes must be inside permitted hosting scopes and cannot overlap exclusions.");
            await store.CitationsAsync(db, provider, request.References, ct);
            var row = new ProviderHostingAssignment
            {
                ProviderId = provider, OfferingId = offeringId, TargetTenantId = request.TargetTenantId, SystemId = systemId,
                HostingScopeRevisionId = hosting.Id, AssignedScopesJson = Json(scopes), ReferencesJson = Json(request.References), CreatedBy = actor
            };
            db.Add(row);
            offering.Revision++;
            await ProviderAuthorizationService.InvalidateAsync(db, offering, actor, "A hosting allocation changed applicability targets", ct);
            return await ProjectAssignmentAsync(db, row, hosting, ct);
        }, ct);

    public async Task<PagedResult<ProviderHostingAssignmentResponse>> AssignmentsAsync(Guid offeringId, int page, int pageSize, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        var result = await PageAsync(db.Set<ProviderHostingAssignment>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offeringId).OrderBy(x => x.Id),
            page, pageSize, x => x, ct);
        var items = new List<ProviderHostingAssignmentResponse>();
        foreach (var row in result.Items)
            items.Add(await ProjectAssignmentAsync(db, row, await RequireScopeAsync(db, offering, row.HostingScopeRevisionId, ct), ct));
        return new(items, page, pageSize, result.Total);
    }

    public async Task<ProviderHostingAssignmentResponse> AssignmentAsync(Guid offeringId, Guid assignmentId, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, offeringId, ct);
        var row = await db.Set<ProviderHostingAssignment>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.ProviderId == offering.ProviderId && x.OfferingId == offeringId && x.Id == assignmentId, ct)
            ?? throw new KeyNotFoundException("Allocation not found in this offering.");
        return await ProjectAssignmentAsync(db, row, await RequireScopeAsync(db, offering, row.HostingScopeRevisionId, ct), ct);
    }

    internal static bool Fits(CreateProviderHostingScopeRequest hosting, IReadOnlyList<ProviderAzureScope> scopes) =>
        scopes.Count != 0 && scopes.All(scope => hosting.PermittedScopes.Any(p => Contains(p, scope))
            && !hosting.Exclusions.Any(e => Contains(e.Scope, scope) || Contains(scope, e.Scope)));

    private static async Task<ProviderHostingScopeRevision> RequireScopeAsync(AtoCopilotContext db,
        ProviderOffering offering, Guid id, CancellationToken ct) =>
        await db.Set<ProviderHostingScopeRevision>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id && x.Id == id, ct)
        ?? throw new KeyNotFoundException("Hosting scope not found in this offering.");

    internal static ProviderHostingScopeResponse ProjectScope(ProviderHostingScopeRevision row, long offeringRevision, Guid? impactId = null)
    {
        var body = Read<CreateProviderHostingScopeRequest>(row.SnapshotJson);
        return new(row.OfferingId, offeringRevision, new(row.Id, row.Revision, row.SnapshotHash), impactId,
            row.PredecessorId, body.Name, body.PermittedScopes, body.Exclusions, body.Citations);
    }

    private static async Task<ProviderHostingAssignmentResponse> ProjectAssignmentAsync(AtoCopilotContext db,
        ProviderHostingAssignment row, ProviderHostingScopeRevision hosting, CancellationToken ct)
    {
        var name = await db.RegisteredSystems.Where(x => x.Id == row.SystemId && x.TenantId == row.TargetTenantId)
            .Select(x => x.Name).SingleOrDefaultAsync(ct);
        var tenantName = await db.Tenants.Where(x => x.Id == row.TargetTenantId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
        var relationship = await db.Set<MissionProviderRelationshipReview>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.ProviderId == row.ProviderId && x.OfferingId == row.OfferingId && x.TenantId == row.TargetTenantId
            && x.SystemId == row.SystemId && x.AssignmentId == row.Id, ct);
        var state = relationship is null ? "Undetermined"
            : relationship.ReviewRequired || relationship.AssignmentRevision != row.Revision ? "ReviewRequired" : relationship.State;
        return new(row.Id, row.Revision, row.OfferingId, row.SystemId, new(hosting.Id, hosting.Revision, hosting.SnapshotHash),
            Read<ProviderAzureScope[]>(row.AssignedScopesJson), state, name, tenantName);
    }
}
