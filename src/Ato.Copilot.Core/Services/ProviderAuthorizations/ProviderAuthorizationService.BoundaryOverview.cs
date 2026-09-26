using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderAuthorizationService
{
    public async Task<OfferingBoundaryOverview> BoundaryOverviewAsync(Guid id, int capabilityPage,
        int missionPage, int pageSize, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, id, ct);
        if (pageSize is < 1 or > 100 || capabilityPage < 1 || missionPage < 1
            || capabilityPage > int.MaxValue / pageSize || missionPage > int.MaxValue / pageSize)
            throw new ArgumentException("Use pages >=1 and pageSize between1 and100.");

        var capabilities = await BoundaryCapabilitiesAsync(db, offering, ct);
        var ordered = capabilities.OrderBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.CapabilityId ?? x.CandidateId).ToArray();
        return new(id, offering.Revision,
            new(ordered.Skip((capabilityPage - 1) * pageSize).Take(pageSize).ToArray(),
                capabilityPage, pageSize, ordered.Length,
                ordered.Count(x => x.ReviewState == "NeedsReview"),
                ordered.Count(x => x.PublicationState == "Published")),
            await BoundaryMissionsAsync(db, offering, missionPage, pageSize, ct));
    }

    private static async Task<IReadOnlyList<OfferingBoundaryCapability>> BoundaryCapabilitiesAsync(
        AtoCopilotContext db, ProviderOffering offering, CancellationToken ct)
    {
        var versions = db.Set<ProviderPackageVersion>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id);
        var linkedPackages = db.CspPackages.AsNoTracking().Where(x => x.ProviderId == offering.ProviderId
            && (x.OfferingId == offering.Id || versions.Any(v => v.PackageId == x.Id)));
        var sources = await (from candidate in db.CspPackageCandidates.AsNoTracking()
                             join package in linkedPackages on candidate.PackageId equals package.Id
                             where candidate.Type == "Capability" && candidate.ReviewState != "Rejected"
                             select new
                             {
                                 candidate.Id, candidate.PackageId, candidate.ReviewState, candidate.PayloadJson,
                                 BoundaryId = versions.Where(v => v.PackageId == package.Id)
                                     .Select(v => (Guid?)v.BoundaryRevisionId).FirstOrDefault() ?? package.BoundaryRevisionId
                             }).ToListAsync(ct);
        var contexts = await db.Set<ProviderCatalogContextSnapshot>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id
                && x.CapabilityId != null && x.ReleaseId != null).ToListAsync(ct);
        var capabilityIds = contexts.Select(x => x.CapabilityId!.Value).Distinct().ToArray();
        var releaseIds = contexts.Select(x => x.ReleaseId!.Value).Distinct().ToArray();
        var catalog = await db.CspInheritedCapabilities.AsNoTracking()
            .Where(x => capabilityIds.Contains(x.Id) && x.CspInheritedComponent.CspProfileId == offering.ProviderId)
            .Select(x => new { x.Id, x.Name, x.Status }).ToDictionaryAsync(x => x.Id, ct);
        var releases = await db.ProviderCapabilityReleases.AsNoTracking()
            .Where(x => releaseIds.Contains(x.Id) && capabilityIds.Contains(x.CapabilityId))
            .Select(x => new { x.Id, x.CapabilityId, x.Revision }).ToDictionaryAsync(x => x.Id, ct);

        foreach (var context in contexts)
            if (!catalog.ContainsKey(context.CapabilityId!.Value)
                || !releases.TryGetValue(context.ReleaseId!.Value, out var release)
                || release.CapabilityId != context.CapabilityId)
                throw new InvalidDataException("An offering capability context does not identify a retained provider release.");

        var published = new Dictionary<Guid, OfferingBoundaryCapability>();
        foreach (var group in contexts.GroupBy(x => x.CapabilityId!.Value))
        {
            var context = group.OrderByDescending(x => releases[x.ReleaseId!.Value].Revision).ThenBy(x => x.Id).First();
            var material = Read<ProviderPublicationContextMaterial>(context.SnapshotJson);
            if (material.OfferingId != offering.Id || material.BoundaryRevisionId == Guid.Empty)
                throw new InvalidDataException("An offering capability has invalid retained boundary context.");
            var capability = catalog[group.Key];
            published.Add(group.Key, new(capability.Id, null, null, capability.Name, capability.Status.ToString(),
                capability.Status == CspInheritedCapabilityStatus.Archived ? "Archived" : "Published",
                context.ReleaseId, material.BoundaryRevisionId));
        }

        var rows = new List<OfferingBoundaryCapability>();
        foreach (var source in sources.OrderBy(x => x.Id))
        {
            var payload = Read<PackageCandidateResponse>(source.PayloadJson);
            if (string.IsNullOrWhiteSpace(payload.Name))
                throw new InvalidDataException("An offering capability candidate has no retained name.");
            if (payload.PublishedRecordId is { } canonicalId)
            {
                if (source.ReviewState != "Published" || !published.TryGetValue(canonicalId, out var canonical))
                    throw new InvalidDataException("A published offering candidate has no matching canonical offering release.");
                // Provenance is identity-based; equal names are not duplicates.
                if (canonical.CandidateId is null)
                    published[canonicalId] = canonical with
                    {
                        CandidateId = source.Id, PackageId = source.PackageId, ReviewState = source.ReviewState
                    };
            }
            else
            {
                if (source.ReviewState == "Published")
                    throw new InvalidDataException("A published offering candidate has no canonical record identity.");
                rows.Add(new(null, source.Id, source.PackageId, payload.Name, source.ReviewState,
                    "Unpublished", null, source.BoundaryId));
            }
        }
        rows.AddRange(published.Values);
        return rows;
    }

    private static async Task<PagedResult<OfferingBoundaryMission>> BoundaryMissionsAsync(
        AtoCopilotContext db, ProviderOffering offering, int page, int pageSize, CancellationToken ct)
    {
        var assignments = db.Set<ProviderHostingAssignment>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id);
        var total = await assignments.CountAsync(ct);
        var rows = await assignments.OrderBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var selected = assignments.Where(x => ids.Contains(x.Id));
        var names = await (from system in db.RegisteredSystems.AsNoTracking()
                           join assignment in selected on new { system.TenantId, system.Id }
                               equals new { TenantId = assignment.TargetTenantId, Id = assignment.SystemId }
                           select new { assignment.Id, system.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var relationships = await BoundaryRelationships(db, offering, selected).ToListAsync(ct);
        var adoptions = await (from adoption in db.Set<CapabilityAdoptionSnapshot>().AsNoTracking()
                               join assignment in selected on adoption.AssignmentId equals assignment.Id
                               join subscription in db.CapabilitySubscriptions.AsNoTracking() on adoption.SubscriptionId equals subscription.Id
                               where adoption.ProviderId == offering.ProviderId && adoption.OfferingId == offering.Id
                                   && adoption.TenantId == assignment.TargetTenantId && adoption.SystemId == assignment.SystemId
                                   && subscription.IsActive && subscription.RegisteredSystemId == assignment.SystemId
                                   && subscription.RoutingTenantId == assignment.TargetTenantId
                               select new { adoption.AssignmentId, adoption.CapabilityId, subscription.CspInheritedCapabilityId })
            .ToListAsync(ct);
        var counts = adoptions.Where(x => Guid.TryParse(x.CspInheritedCapabilityId, out var id) && id == x.CapabilityId)
            .GroupBy(x => x.AssignmentId).ToDictionary(x => x.Key, x => x.Select(a => a.CapabilityId).Distinct().Count());
        var latest = relationships.GroupBy(x => x.AssignmentId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(r => r.Revision).ThenBy(r => r.Id).First());
        return new(rows.Select(assignment =>
        {
            latest.TryGetValue(assignment.Id, out var relationship);
            var state = relationship is null ? "Undetermined"
                : relationship.ReviewRequired || relationship.AssignmentRevision != assignment.Revision
                    ? "ReviewRequired" : relationship.State;
            return new OfferingBoundaryMission(assignment.Id, assignment.SystemId,
                names.GetValueOrDefault(assignment.Id), state, relationship is not null,
                counts.GetValueOrDefault(assignment.Id), Read<ProviderAzureScope[]>(assignment.AssignedScopesJson));
        }).ToArray(), page, pageSize, total);
    }

    private static IQueryable<MissionProviderRelationshipReview> BoundaryRelationships(
        AtoCopilotContext db, ProviderOffering offering, IQueryable<ProviderHostingAssignment> assignments) =>
        from relationship in db.Set<MissionProviderRelationshipReview>().AsNoTracking()
        join assignment in assignments on relationship.AssignmentId equals assignment.Id
        where relationship.ProviderId == offering.ProviderId && relationship.OfferingId == offering.Id
            && relationship.TenantId == assignment.TargetTenantId && relationship.SystemId == assignment.SystemId
        select relationship;
}
