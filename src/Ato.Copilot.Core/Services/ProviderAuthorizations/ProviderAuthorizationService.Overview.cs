using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Services.PackageImports;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderAuthorizationService
{
    public async Task<OfferingOverview> OverviewAsync(Guid id, int authorizationPage, int packagePage,
        int pageSize, CancellationToken ct)
    {
        await using var db = await store.Factory.CreateDbContextAsync(ct);
        var offering = await store.OfferingAsync(db, id, ct);
        if (pageSize is < 1 or > 100 || authorizationPage < 1 || packagePage < 1
            || authorizationPage > int.MaxValue / pageSize || packagePage > int.MaxValue / pageSize)
            throw new ArgumentException("Use pages >=1 and pageSize between1 and100.");

        var authorizations = await OverviewAuthorizationsAsync(db, offering, authorizationPage, pageSize, ct);
        var receipts = await OverviewPackagesAsync(db, offering, packagePage, pageSize, ct);
        var capabilities = await BoundaryCapabilitiesAsync(db, offering, ct);
        return new(id, offering.Revision, authorizations, receipts,
            new(capabilities.Count(x => x.PublicationState == "Unpublished"),
                capabilities.Count(x => x.PublicationState == "Unpublished" && x.ReviewState == "NeedsReview"),
                capabilities.Count(x => x.PublicationState == "Unpublished" && x.ReviewState == "Reviewed"),
                capabilities.Count(x => x.PublicationState == "Published"),
                capabilities.Count(x => x.PublicationState == "Archived")),
            await OverviewHostingAsync(db, offering, ct));
    }

    private static async Task<OfferingOverviewAuthorizations> OverviewAuthorizationsAsync(
        AtoCopilotContext db, ProviderOffering offering, int page, int pageSize, CancellationToken ct)
    {
        var records = await db.Set<ProviderAuthorizationRecord>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id).ToListAsync(ct);
        var currentIds = records.Select(x => x.CurrentRevisionId).ToArray();
        var revisions = await db.Set<ProviderAuthorizationRevision>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id && currentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        foreach (var record in records)
            if (!revisions.TryGetValue(record.CurrentRevisionId, out var revision) || revision.RecordId != record.Id)
                throw new InvalidDataException("An authorization record has no matching current immutable revision.");

        var decisions = revisions.Values.Where(x =>
        {
            var source = Read<CreateProviderDecisionRequest>(x.SnapshotJson);
            if (source.RecordKind is not ("ProviderDecision" or "InheritedMicrosoftReference")
                || source.BoundaryRevisionId == Guid.Empty || source.SourceCandidateRefs is null
                || source.Conditions is null || source.Citations is null)
                throw new InvalidDataException("An authorization revision has invalid retained decision material.");
            return source.RecordKind == "ProviderDecision";
        }).OrderByDescending(x => x.Revision).ThenBy(x => x.Id).ToArray();
        var events = await db.Set<ProviderAuthorizationLifecycleEvent>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id).ToListAsync(ct);
        var fences = records.ToDictionary(x => x.Id, x => x.Revision);
        var items = new List<ProviderDecisionResponse>();
        foreach (var revision in decisions.Skip((page - 1) * pageSize).Take(pageSize))
        {
            ProviderDecisionResponse item;
            try { item = Decision(revision, events.Where(x => x.RecordId == revision.RecordId)); }
            catch (ArgumentException error) { throw new InvalidDataException("Invalid retained authorization dates.", error); }
            items.Add(item with
            {
                Revision = fences[item.RecordId],
                ImpactReviewRequired = await RequiresImpactAsync(db, offering, item, ct)
            });
        }
        return new(items, page, pageSize, decisions.Length,
            decisions.Count(x => x.MetadataReviewState == "Recorded"),
            decisions.Count(x => x.MetadataReviewState == "Unconfirmed"),
            decisions.Count(x => x.MetadataReviewState == "Rejected"));
    }

    private static async Task<OfferingOverviewPackages> OverviewPackagesAsync(
        AtoCopilotContext db, ProviderOffering offering, int page, int pageSize, CancellationToken ct)
    {
        var versions = await db.Set<ProviderPackageVersion>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id).ToListAsync(ct);
        var linkedIds = versions.Select(x => x.PackageId).ToArray();
        var receipts = await db.CspPackages.AsNoTracking().Where(x => x.ProviderId == offering.ProviderId
            && (x.OfferingId == offering.Id || linkedIds.Contains(x.Id))).ToListAsync(ct);
        var receiptMap = receipts.ToDictionary(x => x.Id);
        if (versions.GroupBy(x => x.PackageId).Any(x => x.Count() != 1)
            || versions.Any(x => !receiptMap.ContainsKey(x.PackageId) || x.Version is < 1 or > int.MaxValue))
            throw new InvalidDataException("An offering package version has invalid retained receipt provenance.");
        var byPackage = versions.ToDictionary(x => x.PackageId);
        var boundaries = (await db.Set<ProviderBoundaryRevision>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id).Select(x => x.Id).ToListAsync(ct)).ToHashSet();
        foreach (var receipt in receipts)
        {
            byPackage.TryGetValue(receipt.Id, out var version);
            if (receipt.OfferingId.HasValue && receipt.OfferingId != offering.Id
                || receipt.PackageVersionId.HasValue && receipt.PackageVersionId != version?.Id
                || version is not null && receipt.BoundaryRevisionId.HasValue && receipt.BoundaryRevisionId != version.BoundaryRevisionId
                || (version?.BoundaryRevisionId ?? receipt.BoundaryRevisionId) is { } boundary && !boundaries.Contains(boundary))
                throw new InvalidDataException("An offering receipt has inconsistent retained package or boundary links.");
        }
        // DateTimeOffset ordering is client-side for parity with SQLite and SQL Server.
        var ordered = receipts.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
        var ids = receipts.Select(x => x.Id).ToArray();
        var candidates = await db.CspPackageCandidates.AsNoTracking().Where(x => ids.Contains(x.PackageId))
            .Select(x => new { x.PackageId, x.Type, x.ReviewState }).ToListAsync(ct);
        var grouped = candidates.ToLookup(x => x.PackageId);
        var selected = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var selectedIds = selected.Select(x => x.Id).ToArray();
        var entries = await db.CspPackageEntries.AsNoTracking().Where(x => selectedIds.Contains(x.PackageId))
            .Select(x => new { x.PackageId, x.Status }).ToListAsync(ct);
        var states = entries.ToLookup(x => x.PackageId, x => x.Status);
        var items = selected.Select(receipt =>
        {
            byPackage.TryGetValue(receipt.Id, out var version);
            return new OfferingOverviewPackage(CspPackageService.Status(receipt, states[receipt.Id].ToArray()),
                version?.Id, version is null ? null : (int)version.Version,
                version?.BoundaryRevisionId ?? receipt.BoundaryRevisionId,
                grouped[receipt.Id].Count(x => x.ReviewState == "NeedsReview"),
                grouped[receipt.Id].Count(x => x.ReviewState != "Rejected"
                    && x.Type is "AuthorizationDecisionClaim" or "AuthorizationReference"));
        }).ToArray();
        var preferred = candidates.Where(x => x.ReviewState != "Rejected"
                && x.Type is "AuthorizationDecisionClaim" or "AuthorizationReference")
            .OrderBy(x => x.Type == "AuthorizationDecisionClaim" ? 0 : 1)
            .ThenBy(x => x.ReviewState == "NeedsReview" ? 0 : 1)
            .ThenByDescending(x => receiptMap[x.PackageId].CreatedAt).ThenBy(x => x.PackageId)
            .FirstOrDefault();
        return new(items, page, pageSize, receipts.Count,
            receipts.Count(x => x.ProcessingState is "NeedsAttention" or "Failed"),
            receipts.Count(x => x.ProcessingState is "Received" or "Processing"),
            candidates.Count(x => x.ReviewState == "NeedsReview"),
            preferred is null ? null : new(preferred.PackageId, receiptMap[preferred.PackageId].Name, preferred.Type));
    }

    private static async Task<OfferingOverviewHosting> OverviewHostingAsync(
        AtoCopilotContext db, ProviderOffering offering, CancellationToken ct)
    {
        string? name = null;
        var scopes = 0;
        if (offering.CurrentHostingScopeRevisionId is { } id)
        {
            var hosting = await db.Set<ProviderHostingScopeRevision>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == id && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct)
                ?? throw new InvalidDataException("The offering's current immutable hosting scope is unavailable.");
            var source = Read<CreateProviderHostingScopeRequest>(hosting.SnapshotJson);
            if (string.IsNullOrWhiteSpace(source.Name) || source.PermittedScopes is null || source.PermittedScopes.Count == 0
                || source.PermittedScopes.Any(x => x is null) || source.Exclusions is null || source.Citations is null)
                throw new InvalidDataException("The offering's retained hosting scope material is invalid.");
            name = source.Name;
            scopes = source.PermittedScopes.Count;
        }
        var assignments = db.Set<ProviderHostingAssignment>().AsNoTracking()
            .Where(x => x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id);
        var total = await assignments.CountAsync(ct);
        var relationships = await BoundaryRelationships(db, offering, assignments).ToListAsync(ct);
        var latest = relationships.GroupBy(x => x.AssignmentId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(r => r.Revision).ThenBy(r => r.Id).First());
        var missions = await (from assignment in assignments
                              join system in db.RegisteredSystems.AsNoTracking()
                                  on new { TenantId = assignment.TargetTenantId, Id = assignment.SystemId }
                                  equals new { system.TenantId, system.Id }
                              select new { assignment.Id, assignment.Revision, assignment.TargetTenantId, assignment.SystemId })
            .ToListAsync(ct);
        var associated = missions.Where(x => latest.TryGetValue(x.Id, out var relationship)
                && relationship.AssignmentRevision == x.Revision)
            .Select(x => (x.TargetTenantId, x.SystemId)).Distinct().Count();
        return new(name, offering.CurrentHostingScopeRevisionId.HasValue, scopes, total, associated);
    }
}
