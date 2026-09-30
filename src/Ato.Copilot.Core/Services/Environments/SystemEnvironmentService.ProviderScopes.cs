using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.Environments;

public sealed partial class SystemEnvironmentService
{
    private IQueryable<ProviderHostingAssignment> SystemHosting(AtoCopilotContext db, string systemId) =>
        db.Set<ProviderHostingAssignment>().IgnoreQueryFilters().Where(x =>
            x.TargetTenantId == TenantId && x.SystemId == systemId && db.CspProfiles.Any(p => p.Id == x.ProviderId));
    private IQueryable<SystemProviderScopeSelection> ProviderSelections(AtoCopilotContext db, string systemId) =>
        db.Set<SystemProviderScopeSelection>().Where(x => x.TenantId == TenantId && x.SystemId == systemId);

    private static IQueryable<ProviderCatalogContextSnapshot> PublishedScopeContexts(AtoCopilotContext db) =>
        from context in db.Set<ProviderCatalogContextSnapshot>().IgnoreQueryFilters().AsNoTracking()
        join release in db.ProviderCapabilityReleases on context.ReleaseId equals release.Id
        join capability in db.CspInheritedCapabilities on release.CapabilityId equals capability.Id
        where context.CapabilityId == capability.Id && capability.Status == CspInheritedCapabilityStatus.Mapped
            && capability.CspInheritedComponent.Status == CspInheritedComponentStatus.Published
            && capability.CspInheritedComponent.CspProfileId == context.ProviderId
            && !db.ProviderCapabilityReleases.Any(x => x.CapabilityId == capability.Id && x.Revision > release.Revision)
        select context;

    public async Task<SystemProviderScopeChoicesResponse> ProviderScopeChoicesAsync(string systemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var permissions = await AuthorizeAsync(db, systemId, false, ct);
        return new(systemId, await VersionAsync(db, systemId, ct), permissions.CanManageEnvironments,
            await EligibleProviderScopesAsync(db, systemId, ct));
    }

    private async Task<IReadOnlyList<SystemProviderScopeChoice>> EligibleProviderScopesAsync(
        AtoCopilotContext db, string systemId, CancellationToken ct)
    {
        // Provider-private rows are crossed only through published catalog bindings or exact customer-targeted records.
        var published = await PublishedScopeContexts(db).ToListAsync(ct);
        var existing = await SystemHosting(db, systemId).AsNoTracking().ToListAsync(ct);
        var targeted = await Allocations(db).ToListAsync(ct);
        var eligibleIds = published.Select(x => x.OfferingId).Concat(existing.Select(x => x.OfferingId))
            .Concat(targeted.Select(x => x.OfferingId)).Distinct().ToArray();
        var offerings = await db.Set<ProviderOffering>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => eligibleIds.Contains(x.Id) && x.Lifecycle == "Active").ToListAsync(ct);
        var result = new List<SystemProviderScopeChoice>();
        foreach (var offering in offerings.OrderBy(x => x.Name).ThenBy(x => x.Id))
        {
            var hosting = await db.Set<ProviderHostingScopeRevision>().IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id
                    && x.Id == offering.CurrentHostingScopeRevisionId, ct);
            if (hosting is null) continue;
            var source = existing.Any(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id
                && x.HostingScopeRevisionId == hosting.Id) ? "ExistingSystemAssignment" : null;
            if (source is null && targeted.Any(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id
                && x.HostingScopeRevisionId == hosting.Id && AllocationState(x) == "Active"))
                source = "OrganizationAllocation";
            foreach (var context in published.Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id))
            {
                if (source is not null) break;
                if (await MatchesPublishedScopeAsync(db, context, offering, hosting, ct))
                    source = "PublishedCatalogRelease";
            }
            if (source is null) continue;
            var materialScope = Read<CreateProviderHostingScopeRequest>(hosting.SnapshotJson);
            var providerName = await db.CspProfiles.Where(x => x.Id == offering.ProviderId)
                .Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
            result.Add(new(offering.Id, offering.Revision, offering.Name, providerName, hosting.Id,
                materialScope.Name, materialScope.PermittedScopes, materialScope.Exclusions, source)
            {
                ProviderId = offering.ProviderId, HostingScopeRevision = hosting.Revision,
                PublishedDuties = await PublishedDutiesAsync(db, offering, hosting,
                    published.Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id).ToArray(), ct)
            });
        }
        return result;
    }

    public Task<SystemEnvironmentsResponse> AddProviderScopeAsync(string systemId, AddSystemProviderScopeRequest request,
        string key, string actor, CancellationToken ct = default) =>
        WriteAsync(systemId, "AddProviderScope", request, key, actor, request.ExpectedVersion, async db =>
        {
            var choice = (await EligibleProviderScopesAsync(db, systemId, ct)).SingleOrDefault(x =>
                x.OfferingId == request.OfferingId && x.HostingScopeRevisionId == request.HostingScopeRevisionId)
                ?? throw new KeyNotFoundException("The released provider scope is not available to this system. Select an eligible published scope.");
            ExpectedVersion(choice.OfferingVersion, request.ExpectedOfferingVersion);
            var offering = await db.Set<ProviderOffering>().IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.Id == choice.OfferingId, ct);
            var candidates = await SystemHosting(db, systemId).Where(x => x.OfferingId == offering.Id
                && x.ProviderId == offering.ProviderId && x.HostingScopeRevisionId == choice.HostingScopeRevisionId)
                .OrderBy(x => x.Id).ToListAsync(ct);
            var assignment = candidates.FirstOrDefault(x => Json(Read<ProviderScope[]>(x.AssignedScopesJson)) == Json(choice.PermittedScopes));
            if (assignment is null)
            {
                assignment = new() { ProviderId = offering.ProviderId, OfferingId = offering.Id, TargetTenantId = TenantId,
                    SystemId = systemId, HostingScopeRevisionId = choice.HostingScopeRevisionId,
                    AssignedScopesJson = Json(choice.PermittedScopes), CreatedBy = actor };
                db.Add(assignment);
            }
            var selection = await ProviderSelections(db, systemId).SingleOrDefaultAsync(x => x.AssignmentId == assignment.Id, ct);
            if (selection is null)
                db.Add(new SystemProviderScopeSelection { TenantId = TenantId, SystemId = systemId,
                    AssignmentId = assignment.Id, UpdatedBy = actor });
            else if (selection.State == "Removed")
            {
                selection.HistoryJson = AppendHistory(selection.HistoryJson, "Readded", actor, "Explicit provider-scope selection", selection.Version);
                selection.State = "Active"; selection.Version++; selection.UpdatedBy = actor; selection.UpdatedAt = DateTimeOffset.UtcNow;
            }
            if (!await db.Set<MissionProviderRelationshipReview>().AnyAsync(x => x.TenantId == TenantId
                && x.SystemId == systemId && x.AssignmentId == assignment.Id, ct))
                db.Add(new MissionProviderRelationshipReview { TenantId = TenantId, SystemId = systemId,
                    ProviderId = offering.ProviderId, OfferingId = offering.Id, AssignmentId = assignment.Id,
                    AssignmentRevision = assignment.Revision, CreatedBy = actor });
        }, ct);

    private async Task<IReadOnlyList<SystemProviderScope>> ProjectProviderScopesAsync(AtoCopilotContext db, string systemId, CancellationToken ct)
    {
        var selections = await ProviderSelections(db, systemId).ToDictionaryAsync(x => x.AssignmentId, ct);
        var reviews = await db.Set<MissionProviderRelationshipReview>().Where(x => x.TenantId == TenantId && x.SystemId == systemId).ToListAsync(ct);
        var result = new List<SystemProviderScope>();
        foreach (var assignment in await SystemHosting(db, systemId).OrderBy(x => x.Id).ToListAsync(ct))
        {
            var offering = await db.Set<ProviderOffering>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == assignment.OfferingId && x.ProviderId == assignment.ProviderId, ct);
            var hosting = await db.Set<ProviderHostingScopeRevision>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == assignment.HostingScopeRevisionId && x.OfferingId == assignment.OfferingId && x.ProviderId == assignment.ProviderId, ct);
            if (offering is null || hosting is null) continue;
            var review = reviews.SingleOrDefault(x => x.AssignmentId == assignment.Id
                && x.ProviderId == assignment.ProviderId && x.OfferingId == assignment.OfferingId);
            var selection = selections.GetValueOrDefault(assignment.Id);
            var providerName = await db.CspProfiles.Where(x => x.Id == assignment.ProviderId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
            var reviewRequired = review is null || review.ReviewRequired || review.AssignmentRevision != assignment.Revision
                || offering.CurrentHostingScopeRevisionId != hosting.Id || offering.Lifecycle != "Active";
            result.Add(new(assignment.Id, assignment.Revision, review?.Id, offering.Id, offering.Name, providerName,
                hosting.Id, Read<CreateProviderHostingScopeRequest>(hosting.SnapshotJson).Name, selection?.State ?? "Active",
                review?.State ?? "Undetermined", reviewRequired, Read<ProviderScope[]>(assignment.AssignedScopesJson),
                selection?.Version ?? 0)
            {
                ProviderId = assignment.ProviderId, HostingScopeRevision = hosting.Revision,
                PublishedDuties = await PublishedDutiesAsync(db, offering, hosting,
                    await PublishedScopeContexts(db).Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id).ToListAsync(ct), ct),
                ResponsibilityReview = await ScopeResponsibilitiesAsync(db, systemId, assignment, selection?.State == "Removed", ct)
            });
        }
        return result;
    }

    private static string AppendHistory(string json, string action, string actor, string rationale, long version)
    {
        var history = Read<List<System.Text.Json.JsonElement>>(json);
        history.Add(System.Text.Json.JsonSerializer.SerializeToElement(new { action, actor, rationale, version, at = DateTimeOffset.UtcNow }, JsonOptions));
        return Json(history);
    }
}
