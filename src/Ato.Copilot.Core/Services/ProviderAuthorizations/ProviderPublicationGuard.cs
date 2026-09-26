using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Workspaces;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

/// <summary>Database-backed gate shared by direct, package and nested canonical publication.</summary>
public static class ProviderPublicationGuard
{
    public sealed record Binding(IReadOnlyList<ProviderAuthorizationImpactReview> Reviews, string? ContextHash);
    internal sealed record Graph(HashSet<Guid> Components, HashSet<Guid> Capabilities, HashSet<Guid> Offerings,
        IReadOnlyList<string> Dependencies);

    public static void AuthorizeContext(AtoCopilotContext db)
    {
        if (!db.TenantFilterDisabled && !db.TenantFilterCspAdminAll)
            throw new UnauthorizedAccessException("Use an ordinary provider administrator workspace without tenant impersonation.");
    }

    internal static async Task<Graph> GraphAsync(AtoCopilotContext db, Guid providerId, IEnumerable<Guid> recordIds, CancellationToken ct)
    {
        var ids = recordIds.ToHashSet();
        var components = new HashSet<Guid>();
        var changedComponents = new HashSet<Guid>();
        var capabilities = new HashSet<Guid>();
        var offerings = new HashSet<Guid>();
        if (!await db.Set<ProviderOffering>().AnyAsync(x => x.ProviderId == providerId, ct))
            return new(components, capabilities, offerings, []);
        var catalog = await db.CspInheritedCapabilities.AsNoTracking()
            .Where(x => x.CspInheritedComponent.CspProfileId == providerId)
            .Select(x => new { x.Id, x.CspInheritedComponentId }).ToListAsync(ct);
        var works = await db.ProviderCapabilityWorkingRevisions.AsNoTracking().ToListAsync(ct);
        var releases = await db.ProviderCapabilityReleases.AsNoTracking().OrderByDescending(x => x.Revision).ToListAsync(ct);
        var candidates = await db.CspPackageCandidates.AsNoTracking()
            .Where(x => db.CspPackages.Any(p => p.Id == x.PackageId && p.ProviderId == providerId)).ToListAsync(ct);
        var packages = await db.CspPackages.AsNoTracking().Where(x => x.ProviderId == providerId && x.OfferingId != null).ToListAsync(ct);
        var versions = await db.Set<ProviderPackageVersion>().AsNoTracking().Where(x => x.ProviderId == providerId).ToListAsync(ct);
        var payloads = candidates.ToDictionary(x => x.Id, x => Read<PackageCandidateResponse>(x.PayloadJson));
        foreach (var candidate in candidates)
        {
            var payload = payloads[candidate.Id];
            if (!ids.Contains(candidate.Id) && !(payload.PublishedRecordId.HasValue && ids.Contains(payload.PublishedRecordId.Value))) continue;
            if (candidate.Type == "Component")
            {
                components.Add(payload.PublishedRecordId ?? candidate.Id);
                changedComponents.Add(payload.PublishedRecordId ?? candidate.Id);
            }
            if (candidate.Type == "Capability") capabilities.Add(payload.PublishedRecordId ?? candidate.Id);
            components.UnionWith(payload.ContributorIds);
            offerings.UnionWith(versions.Where(x => x.PackageId == candidate.PackageId).Select(x => x.OfferingId));
            offerings.UnionWith(packages.Where(x => x.Id == candidate.PackageId).Select(x => x.OfferingId!.Value));
        }
        changedComponents.UnionWith(await db.CspInheritedComponents.Where(x => x.CspProfileId == providerId && ids.Contains(x.Id))
            .Select(x => x.Id).ToListAsync(ct));
        components.UnionWith(changedComponents);
        capabilities.UnionWith(catalog.Where(x => ids.Contains(x.Id)).Select(x => x.Id));
        var edges = catalog.ToDictionary(x => x.Id, x => new HashSet<Guid> { x.CspInheritedComponentId });
        var edgeSnapshots = new Dictionary<Guid, string>();
        foreach (var capability in catalog)
        {
            var working = works.SingleOrDefault(x => x.CapabilityId == capability.Id);
            if (working is not null) edges[capability.Id].UnionWith(Read<string[]>(working.ContributorsJson)
                .Where(x => Guid.TryParse(x, out _)).Select(Guid.Parse));
            var previous = releases.FirstOrDefault(x => x.CapabilityId == capability.Id);
            if (previous is not null)
            {
                using var document = JsonDocument.Parse(previous.SnapshotJson);
                if (document.RootElement.TryGetProperty("ContributorsJson", out var property))
                    edges[capability.Id].UnionWith(Read<string[]>(property.GetString()!)
                        .Where(x => Guid.TryParse(x, out _)).Select(Guid.Parse));
            }
            edgeSnapshots[capability.Id] = Json(new
            {
                CapabilityId = capability.Id, ParentComponentId = capability.CspInheritedComponentId,
                WorkingRevision = working?.Revision, WorkingSnapshotHash = working?.SnapshotHash,
                CurrentContributors = working?.ContributorsJson,
                PreviousReleaseId = previous?.Id, PreviousRevision = previous?.Revision,
                PreviousSnapshotHash = previous is null ? null : Hash(previous.SnapshotJson)
            });
        }
        // Changes to shared contributors affect both previous and proposed consumer sets.
        capabilities.UnionWith(edges.Where(x => x.Value.Overlaps(changedComponents)).Select(x => x.Key));
        foreach (var edge in edges.Where(x => capabilities.Contains(x.Key))) components.UnionWith(edge.Value);
        ids.UnionWith(components);
        ids.UnionWith(capabilities);
        foreach (var candidate in candidates)
        {
            var payload = payloads[candidate.Id];
            if (ids.Contains(candidate.Id) || payload.PublishedRecordId.HasValue && ids.Contains(payload.PublishedRecordId.Value))
            {
                offerings.UnionWith(versions.Where(x => x.PackageId == candidate.PackageId).Select(x => x.OfferingId));
                offerings.UnionWith(packages.Where(x => x.Id == candidate.PackageId).Select(x => x.OfferingId!.Value));
            }
        }
        var contexts = await db.Set<ProviderCatalogContextSnapshot>().AsNoTracking().Where(x => x.ProviderId == providerId
            && (x.ComponentId.HasValue && ids.Contains(x.ComponentId.Value) || x.CapabilityId.HasValue && ids.Contains(x.CapabilityId.Value))).ToListAsync(ct);
        offerings.UnionWith(contexts.Select(x => x.OfferingId));
        var boundaries = await db.Set<ProviderBoundaryRevision>().AsNoTracking().Where(x => x.ProviderId == providerId).ToListAsync(ct);
        foreach (var boundary in boundaries)
            if (ids.Contains(boundary.Id) || Read<CreateProviderBoundaryRequest>(boundary.SnapshotJson).ComponentSnapshotIds.Any(ids.Contains))
                offerings.Add(boundary.OfferingId);
        offerings.UnionWith(await db.Set<ProviderHostingScopeRevision>().Where(x => x.ProviderId == providerId && ids.Contains(x.Id)).Select(x => x.OfferingId).ToListAsync(ct));
        return new(components, capabilities, offerings,
            edgeSnapshots.Where(x => capabilities.Contains(x.Key)).OrderBy(x => x.Key).Select(x => x.Value).ToArray());
    }

    private static Guid Parse(string value) => Guid.TryParse(value, out var id) ? id
        : throw new DbUpdateConcurrencyException("AUTHORIZATION_CONTEXT_STALE: Contributor identity is invalid; repair and review the working revision.");

    public static async Task<bool> IsLinkedAsync(AtoCopilotContext db, Guid providerId, IEnumerable<Guid> ids, CancellationToken ct) =>
        (await GraphAsync(db, providerId, ids, ct)).Offerings.Count != 0;

    public static async Task RequireLegacyMutationAsync(AtoCopilotContext db, Guid providerId, IEnumerable<Guid> ids, CancellationToken ct)
    {
        AuthorizeContext(db);
        if (await IsLinkedAsync(db, providerId, ids, ct))
            throw new ProviderPublicationConflictException("AUTHORIZATION_WORKFLOW_REQUIRED", "This record or dependency belongs to an offering. Edit a private working revision and use Authorizations impact review and exact publication; direct catalog mutation is not permitted.");
    }

    public static async Task<Binding> BindAsync(AtoCopilotContext db, Guid providerId, IReadOnlyList<ProviderImpactChange> changes,
        IReadOnlyList<Guid>? reviewIds, Guid? packageId, CancellationToken ct)
    {
        AuthorizeContext(db);
        var graph = await GraphAsync(db, providerId, changes.Select(x => x.RecordId), ct);
        var version = packageId.HasValue ? await db.Set<ProviderPackageVersion>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProviderId == providerId && x.PackageId == packageId, ct) : null;
        if (packageId.HasValue)
        {
            var package = await db.CspPackages.AsNoTracking().SingleAsync(x => x.Id == packageId && x.ProviderId == providerId, ct);
            if ((package.OfferingId.HasValue || package.PackageVersionId.HasValue || package.BoundaryRevisionId.HasValue)
                && (version is null || package.OfferingId != version.OfferingId || package.PackageVersionId != version.Id
                    || package.BoundaryRevisionId != version.BoundaryRevisionId))
                throw new ProviderPublicationConflictException("OFFERING_ASSOCIATION_REQUIRED",
                    "Repair and review the exact package/offering/boundary association before publication.");
        }
        if (version is not null) graph.Offerings.Add(version.OfferingId);
        if (packageId.HasValue && graph.Offerings.Count != 0 && version is null)
            throw new ProviderPublicationConflictException("OFFERING_ASSOCIATION_REQUIRED",
                "Associate this source receipt with an immutable offering package version before publishing offering-linked dependencies.");
        if (graph.Offerings.Count == 0 && (reviewIds is null || reviewIds.Count == 0)) return new([], null);
        if (reviewIds is null || reviewIds.Count == 0)
            throw new ProviderPublicationConflictException("AUTHORIZATION_WORKFLOW_REQUIRED", "Generate and explicitly accept exact impact previews for every affected offering before publication.");
        Bounded(reviewIds, "impactReviewIds", 1);
        if (reviewIds.Distinct().Count() != reviewIds.Count) throw new ArgumentException("Impact review IDs must be distinct.");
        var reviews = await db.Set<ProviderAuthorizationImpactReview>().AsNoTracking()
            .Where(x => x.ProviderId == providerId && reviewIds.Contains(x.Id)).OrderBy(x => x.OfferingId).ThenBy(x => x.Id).ToListAsync(ct);
        if (reviews.Count != reviewIds.Count || reviews.Select(x => x.OfferingId).Distinct().Count() != reviews.Count
            || !graph.Offerings.SetEquals(reviews.Select(x => x.OfferingId)))
            throw ProviderImpactService.Stale("Provide exactly one accepted impact review for each affected offering, including prior and proposed contributors.");
        foreach (var review in reviews)
        {
            if (review.Disposition != "AcceptForPublication" || string.IsNullOrWhiteSpace(review.ReviewedBy) || review.ReviewedAt is null
                || Read<ProviderImpactBlocker[]>(review.BlockersJson).Length != 0)
                throw ProviderImpactService.Stale("An authorized human must accept each unblocked exact impact preview.");
            await ProviderImpactService.EnsureFreshAsync(db, review, ct);
            var context = Read<ProviderPublicationContextMaterial>(review.ContextJson);
            if (!context.Changes.Where(x => x.Kind is "Component" or "Capability").ToHashSet().SetEquals(changes))
                throw ProviderImpactService.Stale("Impact review does not bind every selected exact record revision and hash.");
            if (version is not null && version.OfferingId == review.OfferingId && !context.PackageVersions.Any(x => x.PackageVersionId == version.Id))
                throw ProviderImpactService.Stale("Include this exact source package version in the accepted impact context.");
        }

        return new(reviews, Hash(Json(reviews.Select(x => new { x.Id, x.Revision, x.ContextSnapshotHash, x.PreviewHash }))));
    }

    public static async Task<Binding> WorkingAsync(AtoCopilotContext db, ProviderCapabilityWorkingRevision working,
        IReadOnlyList<Guid>? reviewIds, CancellationToken ct)
    {
        AuthorizeContext(db);
        var provider = await db.CspInheritedCapabilities.Where(x => x.Id == working.CapabilityId)
            .Select(x => x.CspInheritedComponent.CspProfileId).SingleAsync(ct);
        var staged = await db.Set<ProviderCatalogContextSnapshot>().Where(x => x.ProviderId == provider
            && x.CapabilityId == working.CapabilityId && x.ReleaseId == null && x.PackageApprovalId != null).ToListAsync(ct);
        if (staged.Count == 0)
            return await BindAsync(db, provider, [new("Capability", working.CapabilityId, working.Revision, working.SnapshotHash)], reviewIds, null, ct);

        // Only the package transaction can see its staged contexts. Recheck the exact approved
        // candidate and source context, not a caller-supplied "skip authorization" flag.
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is null)
            throw ProviderImpactService.Stale("Package context staging requires the publication transaction.");
        var approvalId = staged.Select(x => x.PackageApprovalId).Distinct().Single();
        var approval = await db.CspPackageApprovals.SingleAsync(x => x.Id == approvalId, ct);
        var package = await db.CspPackages.SingleAsync(x => x.Id == approval.PackageId && x.ProviderId == provider, ct);
        var candidate = await db.CspPackageCandidates.SingleAsync(x => x.Id == working.CapabilityId && x.PackageId == package.Id, ct);
        var payload = Read<PackageCandidateResponse>(candidate.PayloadJson);
        var actualContributors = Read<string[]>(working.ContributorsJson).Select(Parse).Order().ToArray();
        var contributors = new List<Guid>();
        foreach (var contributor in payload.ContributorIds)
        {
            var selected = await db.CspPackageCandidates.SingleOrDefaultAsync(x => x.Id == contributor && x.PackageId == package.Id, ct);
            var source = selected is null ? null : Read<PackageCandidateResponse>(selected.PayloadJson);
            contributors.Add(source?.DuplicateResolution == "ReusePublished" ? source.ContributorIds.Single() : contributor);
        }
        if (approval.State != "Approved" || approval.ApprovedBy is null || approval.ExpiresAt <= DateTimeOffset.UtcNow
            || package.PublicationState != "Publishing" || candidate.ReviewState != "Approved"
            || !Read<PackageSelection[]>(approval.SelectionJson).Any(x => x.CandidateId == candidate.Id && x.Revision == candidate.Revision)
            || working.Classification != payload.Classification || working.ServiceCategory != payload.ServiceCategory
            || !actualContributors.SequenceEqual(contributors.Order())
            || Json(Read<Dictionary<string, string>>(working.DutiesJson).OrderBy(x => x.Key)) != Json(payload.ControlDuties.OrderBy(x => x.Key)))
            throw ProviderImpactService.Stale("Nested publication does not match the exact approved package candidate.");
        var reviews = new List<ProviderAuthorizationImpactReview>();
        foreach (var stage in staged.OrderBy(x => x.OfferingId))
        {
            var review = await db.Set<ProviderAuthorizationImpactReview>().SingleAsync(x => x.Id == stage.ImpactReviewId && x.ProviderId == provider, ct);
            var offering = await db.Set<ProviderOffering>().SingleAsync(x => x.Id == stage.OfferingId && x.ProviderId == provider, ct);
            var blockers = new List<ProviderImpactBlocker>();
            var context = await ProviderImpactService.ContextAsync(db, offering, Read<CreateProviderImpactPreviewRequest>(review.InputJson), blockers, ct);
            if (review.Disposition != "AcceptForPublication" || review.InvalidatedAt.HasValue || review.ExpiresAt <= DateTimeOffset.UtcNow
                || review.ReviewedBy is null || blockers.Count != 0 || Hash(Json(context)) != stage.SnapshotHash
                || stage.SnapshotHash != review.ContextSnapshotHash || !context.Changes.Contains(new("Capability", candidate.Id, candidate.Revision, Hash(candidate.PayloadJson))))
                throw ProviderImpactService.Stale("Staged package applicability no longer matches the human-reviewed source context.");
            reviews.Add(review);
        }
        return new(reviews, Hash(Json(reviews.Select(x => new { x.Id, x.Revision, x.ContextSnapshotHash, x.PreviewHash }))));
    }

    public static void Stage(AtoCopilotContext db, Binding binding, string actor, Guid? capabilityId = null,
        Guid? componentId = null, Guid? approvalId = null, Guid? releaseId = null)
    {
        foreach (var review in binding.Reviews)
            db.Add(new ProviderCatalogContextSnapshot
            {
                ProviderId = review.ProviderId, OfferingId = review.OfferingId, CreatedBy = actor,
                CapabilityId = capabilityId, ComponentId = componentId, PackageApprovalId = approvalId,
                ReleaseId = releaseId, ImpactReviewId = review.Id, SnapshotJson = review.ContextJson,
                SnapshotHash = review.ContextSnapshotHash
            });
    }

    public static async Task AttachReleaseAsync(AtoCopilotContext db, Binding binding,
        ProviderCapabilityRelease release, string actor, CancellationToken ct)
    {
        foreach (var review in binding.Reviews)
        {
            var staged = await db.Set<ProviderCatalogContextSnapshot>().SingleOrDefaultAsync(x =>
                x.ProviderId == review.ProviderId && x.OfferingId == review.OfferingId && x.ImpactReviewId == review.Id
                && x.CapabilityId == release.CapabilityId && x.ReleaseId == null && x.PackageApprovalId != null, ct);
            if (staged is not null) staged.ReleaseId = release.Id;
            else Stage(db, new([review], binding.ContextHash), actor, capabilityId: release.CapabilityId, releaseId: release.Id);
            var offering = await db.Set<ProviderOffering>().SingleAsync(x => x.ProviderId == review.ProviderId && x.Id == review.OfferingId, ct);
            Audit(db, offering, release.Id, "OfferingCapabilityReleased", actor, new
                { release.CapabilityId, release.Revision, release.SnapshotHash, ImpactReviewId = review.Id, review.ContextSnapshotHash });
        }
    }

    public static async Task InvalidatePrivateChangesAsync(AtoCopilotContext db, Guid providerId, IEnumerable<Guid> ids,
        string actor, CancellationToken ct)
    {
        var graph = await GraphAsync(db, providerId, ids, ct);
        foreach (var offeringId in graph.Offerings)
        {
            var offering = await db.Set<ProviderOffering>().SingleAsync(x => x.Id == offeringId && x.ProviderId == providerId, ct);
            foreach (var review in await db.Set<ProviderAuthorizationImpactReview>()
                .Where(x => x.ProviderId == providerId && x.OfferingId == offeringId && x.InvalidatedAt == null).ToListAsync(ct))
                review.InvalidatedAt = DateTimeOffset.UtcNow;
            db.Add(new ProviderAuthorizationImpactReview
            {
                ProviderId = providerId, OfferingId = offeringId, CreatedBy = actor,
                BlockersJson = Json(new[] { new ProviderImpactBlocker("PRIVATE_CHANGE_REVIEW_REQUIRED",
                    "A private inventory revision changed. Generate an exact impact preview before releasing it.") }),
                TargetsJson = Json(graph.Capabilities.Order().Select(x => new ProviderImpactTargetResponse("Capability", x.ToString(), null, null, "ReviewRequired")))
            });
            Audit(db, offering, offeringId, "PrivatePublicationInputsChanged", actor);
        }
    }
}
