using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderImpactService
{
    internal sealed record Material(ProviderPublicationContextMaterial Context, string ContextHash, string PreviewHash,
        IReadOnlyList<ProviderImpactBlocker> Blockers, IReadOnlyList<ProviderImpactTargetResponse> Targets, string DependencyGraphJson);

    internal static async Task<Material> BuildAsync(AtoCopilotContext db, ProviderOffering offering,
        CreateProviderImpactPreviewRequest request, CancellationToken ct)
    {
        var blockers = new List<ProviderImpactBlocker>();
        var context = await ContextAsync(db, offering, request, blockers, ct);
        var dependencies = new List<string>();
        foreach (var change in request.Changes.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.RecordId))
        {
            var actual = await ChangeAsync(db, offering.ProviderId, change.Kind, change.RecordId, ct);
            if (actual != change) blockers.Add(new("CHANGE_STALE", "Reload the exact changed record and snapshot hash.", change.RecordId.ToString()));
            dependencies.Add(Json(actual));
            var candidate = await db.CspPackageCandidates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == change.RecordId
                && db.CspPackages.Any(p => p.Id == x.PackageId && p.ProviderId == offering.ProviderId), ct);
            if (candidate is not null && (candidate.ReviewState is not ("Reviewed" or "Approved" or "Published") || candidate.ReviewedBy is null))
                blockers.Add(new("CHANGE_UNREVIEWED", "Explicitly review the selected source candidate before impact acceptance.", change.RecordId.ToString()));
        }
        foreach (var version in context.PackageVersions)
        {
            var package = await db.CspPackages.AsNoTracking().SingleAsync(x => x.Id == version.PackageId && x.ProviderId == offering.ProviderId, ct);
            var entries = await db.CspPackageEntries.AsNoTracking().Where(x => x.PackageId == package.Id).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Revision, x.Sha256, x.Status, x.ExclusionReason }).ToListAsync(ct);
            dependencies.Add(Json(new { package.Id, package.Revision, package.ProcessingState, Entries = entries }));
        }
        var graph = await ProviderPublicationGuard.GraphAsync(db, offering.ProviderId, request.Changes.Select(x => x.RecordId), ct);
        dependencies.AddRange(graph.Dependencies);
        if (!graph.Offerings.Contains(offering.Id) && request.Changes.All(x =>
                x.RecordId != request.BoundaryRevisionId && x.RecordId != request.HostingScopeRevisionId))
            blockers.Add(new("OFFERING_ASSOCIATION_REQUIRED", "The selected changes have no association with this offering."));
        foreach (var component in graph.Components.Order())
        {
            var candidate = await db.CspPackageCandidates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == component
                && db.CspPackages.Any(p => p.Id == x.PackageId && p.ProviderId == offering.ProviderId), ct);
            if (candidate is not null)
            {
                dependencies.Add(Json(new { candidate.Id, candidate.Revision, candidate.PayloadJson, candidate.UnresolvedDependenciesJson }));
                if (candidate.ReviewState is not ("Reviewed" or "Approved" or "Published") || candidate.ReviewedBy is null)
                    blockers.Add(new("DEPENDENCY_UNREVIEWED", "Explicitly review the component dependency.", component.ToString()));
            }
            else
            {
                var row = await db.CspInheritedComponents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == component && x.CspProfileId == offering.ProviderId, ct);
                if (row is null || row.Status != Models.Tenancy.CspInheritedComponentStatus.Published)
                    blockers.Add(new("DEPENDENCY_UNREVIEWED", "Use a published component or a reviewed selected package candidate.", component.ToString()));
                if (row is not null) dependencies.Add(Json(new { row.Id, row.Name, row.Description, row.ComponentType, row.Status, row.UpdatedAt, row.RowVersion }));
            }
        }
        var targets = new List<ProviderImpactTargetResponse>();
        targets.AddRange(graph.Components.Select(x => new ProviderImpactTargetResponse("Component", x.ToString(), null, null, "ReviewRequired")));
        targets.AddRange(graph.Capabilities.Select(x => new ProviderImpactTargetResponse("Capability", x.ToString(), null, null, "ReviewRequired")));
        if (request.HostingScopeRevisionId.HasValue)
            targets.Add(new("HostingScope", request.HostingScopeRevisionId.Value.ToString(), null, null, "ReviewRequired"));
        var offeringIds = graph.Offerings.Append(offering.Id).Distinct().ToArray();
        var assignments = await db.Set<ProviderHostingAssignment>().AsNoTracking().Where(x =>
            x.ProviderId == offering.ProviderId && offeringIds.Contains(x.OfferingId)).ToListAsync(ct);
        foreach (var assignment in assignments.OrderBy(x => x.Id))
        {
            dependencies.Add(Json(new { assignment.Id, assignment.Revision, assignment.OfferingId, assignment.HostingScopeRevisionId, assignment.AssignedScopesJson }));
            targets.Add(new("System", assignment.SystemId, assignment.TargetTenantId, assignment.SystemId, "ReviewRequired"));
        }
        var subscriptions = await db.CapabilitySubscriptions.AsNoTracking().Where(x => x.IsActive)
            .Select(x => new { x.CspInheritedCapabilityId, x.RegisteredSystemId, x.RoutingTenantId }).ToListAsync(ct);
        foreach (var subscription in subscriptions.Where(x => Guid.TryParse(x.CspInheritedCapabilityId, out var id) && graph.Capabilities.Contains(id)))
            targets.Add(new("System", subscription.RegisteredSystemId, subscription.RoutingTenantId, subscription.RegisteredSystemId, "ReviewRequired"));
        var orderedTargets = targets.Distinct().OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.RecordId, StringComparer.Ordinal)
            .ThenBy(x => x.TenantId).ToArray();
        var contextHash = Hash(Json(context));
        var previewHash = Hash(Json(new { ContextHash = contextHash, Dependencies = dependencies.Order(StringComparer.Ordinal),
            Offerings = graph.Offerings.Order(), Targets = orderedTargets, Blockers = blockers }));
        return new(context, contextHash, previewHash, blockers, orderedTargets, Json(dependencies.Order(StringComparer.Ordinal)));
    }

    internal static async Task<ProviderPublicationContextMaterial> ContextAsync(AtoCopilotContext db, ProviderOffering offering,
        CreateProviderImpactPreviewRequest request, List<ProviderImpactBlocker> blockers, CancellationToken ct)
    {
        if (offering.Revision != request.ExpectedOfferingRevision || offering.Lifecycle == "Retired")
            blockers.Add(new("OFFERING_STALE", "Reload the current active offering revision."));
        var boundary = await db.Set<ProviderBoundaryRevision>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == request.BoundaryRevisionId && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct)
            ?? throw new KeyNotFoundException("Boundary revision was not found in this offering.");
        if (offering.CurrentBoundaryRevisionId != boundary.Id)
            blockers.Add(new("BOUNDARY_STALE", "Review the current immutable boundary revision."));
        if (Hash(boundary.SnapshotJson) != boundary.SnapshotHash)
            blockers.Add(new("BOUNDARY_STALE", "The retained boundary snapshot no longer matches its immutable digest."));
        ProviderHostingScopeRevision? hosting = null;
        if (request.HostingScopeRevisionId.HasValue)
            hosting = await db.Set<ProviderHostingScopeRevision>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == request.HostingScopeRevisionId && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct)
                ?? throw new KeyNotFoundException("Hosting scope was not found in this offering.");
        if (offering.CurrentHostingScopeRevisionId != hosting?.Id)
            blockers.Add(new("HOSTING_SCOPE_STALE", "Include the current hosting scope revision, separately from the authorization boundary."));
        if (hosting is not null && Hash(hosting.SnapshotJson) != hosting.SnapshotHash)
            blockers.Add(new("HOSTING_SCOPE_STALE", "The retained hosting snapshot no longer matches its immutable digest."));
        var decisions = new List<ProviderDecisionContext>();
        var hasProviderDecision = false;
        foreach (var id in request.AuthorizationRevisionIds.Order())
        {
            var row = await db.Set<ProviderAuthorizationRevision>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == id && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct)
                ?? throw new KeyNotFoundException("Decision revision was not found in this offering.");
            var events = await db.Set<ProviderAuthorizationLifecycleEvent>().AsNoTracking().Where(x =>
                x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id && x.RecordId == row.RecordId).OrderBy(x => x.Id).ToListAsync(ct);
            var source = Read<CreateProviderDecisionRequest>(row.SnapshotJson);
            hasProviderDecision |= source.RecordKind == "ProviderDecision";
            var standing = ProviderAuthorizationService.Standing(row, events);
            var record = await db.Set<ProviderAuthorizationRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.RecordId
                && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct);
            var eligibility = ProviderDecisionEligibility.Evaluate(row, events,
                source.RecordKind == "InheritedMicrosoftReference" ? "InheritedMicrosoftReference" : "ProviderDecision");
            if (eligibility is not null) blockers.Add(eligibility);
            if (record?.CurrentRevisionId != id || row.BoundaryRevisionId != boundary.Id)
                blockers.Add(new("DECISION_NOT_ELIGIBLE", "Use a current, source-backed recorded positive decision for this boundary. Recording metadata does not verify external authority.", id.ToString()));
            var evidence = new List<string>();
            foreach (var citation in source.Citations)
            {
                var entry = await db.CspPackageEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == citation.ArtifactId
                    && x.PackageId == citation.PackageId && db.CspPackages.Any(p => p.Id == x.PackageId && p.ProviderId == offering.ProviderId), ct);
                if (entry is null || entry.Status == "Excluded" || entry.ArchivePath != citation.ArchivePath
                    || string.IsNullOrWhiteSpace(citation.Quote) || !Read<CspPackageSourceSegment[]>(entry.SegmentsJson)
                        .Any(x => x.Locator == citation.Locator && x.Text.Contains(citation.Quote, StringComparison.Ordinal)))
                    blockers.Add(new("SOURCE_EVIDENCE_UNAVAILABLE", "Restore or explicitly review valid retained source evidence before publication.", id.ToString()));
                if (entry is not null) evidence.Add(Json(new { entry.Id, entry.PackageId, entry.Sha256, entry.Revision, entry.Status }));
            }
            decisions.Add(new(row.RecordId, row.Id, row.SnapshotHash,
                Hash(Json(new { RecordVersion = record?.Revision, record?.CurrentRevisionId,
                    AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    row.MetadataReviewState, row.RecordedBy, row.RecordedAt, Standing = standing,
                    source.IssuedOn, source.EffectiveOn, source.ExpiresOn, source.ExpiryBasis, Events = events, Evidence = evidence }))));
        }
        if (!hasProviderDecision)
            blockers.Add(new("PROVIDER_DECISION_REQUIRED", request.AuthorizationRevisionIds.Count == 0
                ? "No external authorization was selected; authorization coverage is not established. This assessment does not permit acceptance or publication."
                : "Inherited cloud references alone do not establish a recorded provider boundary decision."));
        var versions = new List<ProviderPackageContext>();
        foreach (var id in request.PackageVersionIds.Order())
        {
            var version = await db.Set<ProviderPackageVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id
                && x.ProviderId == offering.ProviderId && x.OfferingId == offering.Id, ct)
                ?? throw new KeyNotFoundException("Package version was not found in this offering.");
            if (version.BoundaryRevisionId != boundary.Id)
                blockers.Add(new("PACKAGE_BOUNDARY_STALE", "Use source package versions associated with this exact boundary.", id.ToString()));
            versions.Add(new(version.Id, version.PackageId, version.ManifestHash));
        }
        return new(offering.Id, offering.Revision, boundary.Id, boundary.SnapshotHash, hosting?.Id, hosting?.SnapshotHash,
            decisions, versions, request.Changes.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.RecordId).ToArray());
    }

    public static async Task<ProviderImpactChange> ChangeAsync(AtoCopilotContext db, Guid providerId, string kind, Guid id, CancellationToken ct)
    {
        if (kind is "Component" or "Capability")
        {
            var candidate = await db.CspPackageCandidates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Type == kind
                && db.CspPackages.Any(p => p.Id == x.PackageId && p.ProviderId == providerId), ct);
            if (candidate is not null && Read<PackageCandidateResponse>(candidate.PayloadJson).PublishedRecordId is null)
                return new(kind, id, candidate.Revision, Hash(candidate.PayloadJson));
        }
        if (kind == "Capability")
        {
            var working = await db.ProviderCapabilityWorkingRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.CapabilityId == id
                && db.CspInheritedCapabilities.Any(c => c.Id == id && c.CspInheritedComponent.CspProfileId == providerId), ct)
                ?? throw new KeyNotFoundException("Capability working revision was not found in this provider.");
            return new(kind, id, working.Revision, working.SnapshotHash);
        }
        if (kind == "Component")
        {
            var row = await db.CspInheritedComponents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CspProfileId == providerId, ct)
                ?? throw new KeyNotFoundException("Component was not found in this provider.");
            return new(kind, id, (row.UpdatedAt ?? row.ImportedAt).UtcTicks, Hash(Json(new
                { row.Id, row.Name, row.Description, row.ComponentType, row.Status, row.SourceArtifactReference, row.UpdatedAt, row.RowVersion })));
        }
        if (kind == "Boundary")
        {
            var row = await db.Set<ProviderBoundaryRevision>().SingleOrDefaultAsync(x => x.Id == id && x.ProviderId == providerId, ct)
                ?? throw new KeyNotFoundException("Boundary revision was not found in this provider.");
            return new(kind, id, row.Revision, row.SnapshotHash);
        }
        if (kind == "HostingScope")
        {
            var row = await db.Set<ProviderHostingScopeRevision>().SingleOrDefaultAsync(x => x.Id == id && x.ProviderId == providerId, ct)
                ?? throw new KeyNotFoundException("Hosting scope revision was not found in this provider.");
            return new(kind, id, row.Revision, row.SnapshotHash);
        }
        throw new ArgumentException("Unsupported impact change kind.");
    }
}
