using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderAuthorizationService(
    ProviderAuthorizationStore store, ICspPackageService packages) : IProviderAuthorizationService
{
    public async Task<PagedResult<ProviderOfferingResponse>> ListAsync(int page, int pageSize,
        string? search, string? lifecycle, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var provider = await store.ProviderAsync(db, ct);
        var query = db.Set<ProviderOffering>().AsNoTracking().Where(x => x.ProviderId == provider);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Name.Contains(search));
        if (!string.IsNullOrWhiteSpace(lifecycle)) query = query.Where(x => x.Lifecycle == lifecycle);
        return await PageAsync(query.OrderBy(x => x.Name).ThenBy(x => x.Id), page, pageSize, Offering, ct);
    }

    public async Task<ProviderOfferingResponse> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        return Offering(await store.OfferingAsync(db, id, ct));
    }

    public Task<ProviderOfferingResponse> CreateAsync(CreateProviderOfferingRequest request, string key, string actor, CancellationToken ct) =>
        store.WriteAsync(null, "OfferingCreated", key, request, actor, (db, provider, _) =>
        {
            var environments = Environments(request.Environments);
            var row = new ProviderOffering
            {
                ProviderId = provider, Name = Text(request.Name, "name", 256),
                Description = Text(request.Description, "description", 8000, false),
                EnvironmentsJson = Json(environments), CreatedBy = actor
            };
            row.OfferingId = row.Id;
            db.Set<ProviderOffering>().Add(row);
            return Task.FromResult(Offering(row));
        }, ct);

    public Task<ProviderOfferingResponse> UpdateAsync(Guid id, UpdateProviderOfferingRequest request, string actor, CancellationToken ct) =>
        store.WriteAsync(id, "OfferingUpdated", null, request, actor, async (db, _, offering) =>
        {
            var row = offering!;
            Expected(row, request.ExpectedRevision);
            row.Name = Text(request.Name, "name", 256);
            row.Description = Text(request.Description, "description", 8000, false);
            row.EnvironmentsJson = Json(Environments(request.Environments));
            row.Revision++;
            await InvalidateAsync(db, row, actor, "Offering identity or environment changed", ct);
            return Offering(row);
        }, ct);

    public Task<ProviderBoundaryResponse> CreateBoundaryAsync(Guid id, CreateProviderBoundaryRequest request,
        string key, string actor, CancellationToken ct) =>
        store.WriteAsync(id, $"BoundaryCreated:{id:D}", key, request, actor, async (db, provider, offering) =>
        {
            var owner = offering!;
            Expected(owner, request.ExpectedOfferingRevision);
            if (request.PredecessorRevisionId != owner.CurrentBoundaryRevisionId)
                throw new DbUpdateConcurrencyException("Select the current boundary as the predecessor.");
            var normalized = await ValidateBoundaryAsync(db, owner, request, ct);
            var version = (await db.Set<ProviderBoundaryRevision>().Where(x => x.OfferingId == id && x.ProviderId == provider)
                .Select(x => (long?)x.Revision).MaxAsync(ct) ?? 0) + 1;
            var row = new ProviderBoundaryRevision
            {
                OfferingId = id, ProviderId = provider, Revision = version, PredecessorId = request.PredecessorRevisionId,
                SnapshotJson = Json(normalized), SnapshotHash = Hash(Json(normalized)), CreatedBy = actor
            };
            db.Set<ProviderBoundaryRevision>().Add(row);
            owner.CurrentBoundaryRevisionId = row.Id;
            owner.Revision++;
            await InvalidateAsync(db, owner, actor, "A new immutable boundary was recorded", ct);
            return Boundary(row, owner.Revision);
        }, ct);

    public async Task<PagedResult<ProviderBoundaryResponse>> BoundariesAsync(Guid id, int page, int pageSize, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, id, ct);
        return await PageAsync(db.Set<ProviderBoundaryRevision>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == id).OrderByDescending(x => x.Revision),
            page, pageSize, x => Boundary(x, offering.Revision), ct);
    }

    public async Task<ProviderBoundaryResponse> BoundaryAsync(Guid id, Guid revisionId, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, id, ct);
        return Boundary(await RequireBoundaryAsync(db, offering, revisionId, ct), offering.Revision);
    }

    public static async Task<ProviderBoundaryRevision> RequireBoundaryAsync(AtoCopilotContext db, ProviderOffering offering, Guid id, CancellationToken ct) =>
        await db.Set<ProviderBoundaryRevision>().SingleOrDefaultAsync(x =>
            x.Id == id && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct)
        ?? throw new KeyNotFoundException("The immutable boundary was not found in this offering.");

    private async Task<CreateProviderBoundaryRequest> ValidateBoundaryAsync(AtoCopilotContext db,
        ProviderOffering offering, CreateProviderBoundaryRequest request, CancellationToken ct)
    {
        Text(request.Name, "boundary name", 256);
        Text(request.ScopeStatement, "boundary scope", 8000);
        Bounded(request.Services, "services"); Bounded(request.ComponentSnapshotIds, "component snapshots");
        Bounded(request.IncludedScopes, "included scopes"); Bounded(request.Exclusions, "exclusions");
        Bounded(request.ProviderResponsibilities, "provider responsibilities"); Bounded(request.CustomerResponsibilities, "customer responsibilities");
        foreach (var text in request.Services.Concat(request.ProviderResponsibilities).Concat(request.CustomerResponsibilities))
            Text(text, "boundary statement", 2000);
        foreach (var component in request.ComponentSnapshotIds)
            if (!await db.Set<ProviderCatalogContextSnapshot>().AnyAsync(x => x.Id == component
                && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id && x.ComponentId != null, ct))
                throw new KeyNotFoundException("Component snapshot was not found in this offering.");
        var environments = Read<string[]>(offering.EnvironmentsJson);
        var scopes = request.IncludedScopes.Select(Normalize).ToArray();
        var exclusions = request.Exclusions.Select(x => x with
        {
            Scope = x.Scope is null ? null : Normalize(x.Scope),
            Description = Text(x.Description, "exclusion", 2000),
            Rationale = Text(x.Rationale, "exclusion rationale", 2000)
        }).ToArray();
        if (scopes.Concat(exclusions.Where(x => x.Scope is not null).Select(x => x.Scope!)).Any(x => !environments.Contains(x.Cloud)))
            throw new ArgumentException("Boundary scopes must use an explicitly recorded offering environment.");
        await store.CitationsAsync(db, offering.ProviderId, request.Citations, ct);
        return request with { Name = request.Name.Trim(), ScopeStatement = request.ScopeStatement.Trim(), IncludedScopes = scopes, Exclusions = exclusions };
    }

    internal static ProviderOfferingResponse Offering(ProviderOffering row) => new(row.Id, row.ProviderId, row.Name,
        row.Description, Read<string[]>(row.EnvironmentsJson), row.Revision, row.Lifecycle,
        row.CurrentBoundaryRevisionId, row.CurrentHostingScopeRevisionId);

    internal static ProviderBoundaryResponse Boundary(ProviderBoundaryRevision row, long offeringRevision)
    {
        var body = Read<CreateProviderBoundaryRequest>(row.SnapshotJson);
        return new(row.OfferingId, offeringRevision, row.Id, row.Revision, row.SnapshotHash, row.PredecessorId,
            row.CreatedAt, body.Name, body.ScopeStatement, body.Services, body.ComponentSnapshotIds, body.IncludedScopes,
            body.Exclusions, body.ProviderResponsibilities, body.CustomerResponsibilities, body.Citations);
    }

    private static string[] Environments(IReadOnlyList<string> values)
    {
        Bounded(values, "environments", 1);
        if (values.Any(x => x is not ("AzureCloud" or "AzureUSGovernment")))
            throw new ArgumentException("Choose AzureCloud or AzureUSGovernment; environments are never inferred.");
        return values.Distinct().Order(StringComparer.Ordinal).ToArray();
    }

    public static async Task InvalidateAsync(AtoCopilotContext db, ProviderOffering offering,
        string actor, string reason, CancellationToken ct)
    {
        var reviews = await db.Set<ProviderAuthorizationImpactReview>().Where(x =>
            x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id && x.InvalidatedAt == null).ToListAsync(ct);
        foreach (var review in reviews) review.InvalidatedAt = DateTimeOffset.UtcNow;
        var targets = await db.Set<ProviderHostingAssignment>().Where(x => x.ProviderId == offering.ProviderId
            && x.OfferingId == offering.Id).Select(x => new ProviderImpactTargetResponse(
                "MissionSystem", x.SystemId, x.TargetTenantId, x.SystemId, "PendingReview")).ToArrayAsync(ct);
        var relationships = await db.Set<MissionProviderRelationshipReview>().IgnoreQueryFilters()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id).ToListAsync(ct);
        foreach (var relationship in relationships) relationship.ReviewRequired = true;
        db.Set<ProviderAuthorizationImpactReview>().Add(new()
        {
            OfferingId = offering.Id, ProviderId = offering.ProviderId, CreatedBy = actor,
            ContextJson = Json(new { offering.Id, offering.Revision, reason }),
            ContextSnapshotHash = Hash(Json(new { offering.Id, offering.Revision, reason })),
            PreviewHash = Hash(Json(new { offering.Id, offering.Revision, reason })),
            TargetsJson = Json(targets),
            BlockersJson = Json(new[] { new ProviderImpactBlocker("SOURCE_CHANGE_REVIEW_REQUIRED", reason) })
        });
    }
}
