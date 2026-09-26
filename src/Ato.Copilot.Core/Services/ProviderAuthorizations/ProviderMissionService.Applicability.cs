using System.Text.Json;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using ProviderAuthorizationRecord = Ato.Copilot.Core.Models.ProviderAuthorizations.ProviderAuthorizationRecord;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderMissionService
{
    public async Task<PagedResult<ApplicableProviderCapabilityResponse>> ApplicableAsync(string systemId, int page, int pageSize,
        Guid? assignmentId, Guid? offeringId, string? environment, Guid? capabilityId, Guid? releaseId, CancellationToken ct)
    {
        await AuthorizeAsync(systemId, false, false, ct);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new ArgumentException("Use page >=1 and pageSize between1 and100.");
        if (environment is not (null or "AzureCloud" or "AzureUSGovernment"))
            throw new ArgumentException("Use AzureCloud or AzureUSGovernment.");
        var allocations = await Allocations(systemId).Where(x =>
            (!assignmentId.HasValue || x.Id == assignmentId) && (!offeringId.HasValue || x.OfferingId == offeringId)).ToListAsync(ct);
        var canAdopt = await responsibilities.AuthorizeAsync(systemId, false, ct);
        var results = new List<ApplicableProviderCapabilityResponse>();
        foreach (var allocation in allocations.OrderBy(x => x.Id))
        {
            var scopes = Read<ProviderAzureScope[]>(allocation.AssignedScopesJson);
            if (environment is not null && !scopes.Any(x => x.Cloud == environment)) continue;
            var offering = await OfferingAsync(allocation, ct);
            var hosting = await HostingAsync(allocation, ct);
            var relationship = await Relationships(systemId).AsNoTracking().SingleOrDefaultAsync(x =>
                x.AssignmentId == allocation.Id && x.ProviderId == allocation.ProviderId && x.OfferingId == allocation.OfferingId, ct);
            var relationshipSummary = await ProjectAsync(allocation, relationship, await AccessAsync(systemId, ct), ct);
            var contexts = await db.Set<ProviderCatalogContextSnapshot>().IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.ProviderId == allocation.ProviderId && x.OfferingId == allocation.OfferingId
                    && x.CapabilityId != null && x.ReleaseId != null && (!capabilityId.HasValue || x.CapabilityId == capabilityId)
                    && (!releaseId.HasValue || x.ReleaseId == releaseId)).ToListAsync(ct);
            var linkedReleases = contexts.Select(x => x.ReleaseId!.Value).Distinct().ToArray();
            var releases = await db.ProviderCapabilityReleases.AsNoTracking().Where(x => linkedReleases.Contains(x.Id)).ToListAsync(ct);
            foreach (var context in contexts.GroupBy(x => x.CapabilityId).Select(group => group
                .OrderByDescending(x => releases.Single(r => r.Id == x.ReleaseId).Revision)
                .ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id).First()).OrderBy(x => x.CapabilityId))
            {
                var capability = await db.CspInheritedCapabilities.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.Id == context.CapabilityId && x.Status == CspInheritedCapabilityStatus.Mapped
                    && x.CspInheritedComponent.CspProfileId == allocation.ProviderId
                    && x.CspInheritedComponent.Status == CspInheritedComponentStatus.Published, ct);
                if (capability is null) continue;
                var release = releases.Single(x => x.Id == context.ReleaseId && x.CapabilityId == capability.Id);
                if (Hash(context.SnapshotJson) != context.SnapshotHash)
                    throw new InvalidDataException("The retained offering applicability snapshot has an invalid digest.");
                var material = Read<ProviderPublicationContextMaterial>(context.SnapshotJson);
                var reasons = new List<string>();
                if (material.OfferingId != offering.Id || material.OfferingRevision != offering.Revision || offering.Lifecycle == "Retired")
                    reasons.Add("OFFERING_CONTEXT_STALE");
                var boundary = await db.Set<ProviderBoundaryRevision>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                    x.ProviderId == allocation.ProviderId && x.OfferingId == offering.Id && x.Id == material.BoundaryRevisionId, ct);
                if (offering.CurrentBoundaryRevisionId != material.BoundaryRevisionId || boundary is null
                    || material.BoundaryHash != boundary.SnapshotHash || Hash(boundary.SnapshotJson) != boundary.SnapshotHash)
                    reasons.Add("BOUNDARY_CONTEXT_STALE");
                if (hosting.Id != material.HostingScopeRevisionId || offering.CurrentHostingScopeRevisionId != hosting.Id
                    || hosting.SnapshotHash != material.HostingScopeHash || Hash(hosting.SnapshotJson) != hosting.SnapshotHash
                    || !ProviderHostingService.Fits(Read<CreateProviderHostingScopeRequest>(hosting.SnapshotJson), scopes))
                    reasons.Add("HOSTING_CONTEXT_STALE");
                if (await db.ProviderCapabilityReleases.AnyAsync(x => x.CapabilityId == capability.Id && x.Revision > release.Revision, ct))
                    reasons.Add("RELEASE_SUPERSEDED");
                if (!await db.Set<ProviderAuthorizationImpactReview>().IgnoreQueryFilters().AnyAsync(x =>
                    x.Id == context.ImpactReviewId && x.ProviderId == allocation.ProviderId && x.OfferingId == offering.Id
                    && x.Disposition == "AcceptForPublication" && x.ReviewedBy != null && x.ReviewedAt != null
                    && x.InvalidatedAt == null && x.ContextSnapshotHash == context.SnapshotHash, ct))
                    reasons.Add("PROVIDER_REVIEW_REQUIRED");
                var hasProviderDecision = false;
                var references = new List<ProviderMissionSourceReference>();
                foreach (var decisionContext in material.AuthorizationRevisions)
                {
                    var decision = await db.Set<ProviderAuthorizationRevision>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                        x.Id == decisionContext.RevisionId && x.RecordId == decisionContext.RecordId
                        && x.ProviderId == allocation.ProviderId && x.OfferingId == offering.Id, ct);
                    if (decision is null) { reasons.Add("DECISION_CONTEXT_STALE"); continue; }
                    var source = Read<CreateProviderDecisionRequest>(decision.SnapshotJson);
                    references.AddRange(source.Citations.Select(x => new ProviderMissionSourceReference(x.ArtifactId,
                        "Provider-private recorded reference", "Content unavailable in this workspace", false)));
                    hasProviderDecision |= source.RecordKind == "ProviderDecision";
                    var events = await db.Set<ProviderAuthorizationLifecycleEvent>().IgnoreQueryFilters().AsNoTracking().Where(x =>
                        x.ProviderId == allocation.ProviderId && x.OfferingId == offering.Id && x.RecordId == decision.RecordId).ToListAsync(ct);
                    if (decision.SnapshotHash != decisionContext.SnapshotHash || decision.BoundaryRevisionId != material.BoundaryRevisionId
                        || ProviderDecisionEligibility.Evaluate(decision, events, source.RecordKind) is not null
                        || !await db.Set<ProviderAuthorizationRecord>().IgnoreQueryFilters().AnyAsync(x =>
                            x.Id == decision.RecordId && x.ProviderId == allocation.ProviderId && x.OfferingId == offering.Id
                            && x.CurrentRevisionId == decision.Id, ct)
                        || !await EvidenceAvailableAsync(allocation.ProviderId, source.Citations, ct))
                        reasons.Add("DECISION_CONTEXT_STALE");
                }
                if (!hasProviderDecision) reasons.Add("PROVIDER_DECISION_REQUIRED");
                using var snapshot = JsonDocument.Parse(release.SnapshotJson);
                if (!snapshot.RootElement.TryGetProperty("DutiesJson", out var dutiesJson) || dutiesJson.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Published release duties are unavailable; explicit provider review is required.");
                var duties = Read<Dictionary<string, string>>(dutiesJson.GetString()!);
                if (duties.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value is not ("Provider" or "Shared" or "Customer")))
                    throw new InvalidDataException("Published release contains unsupported duties.");
                var decisions = new List<string> { "ResponsibilitiesNotConfirmedByAssociationOrAdoption" };
                if (relationship is null) decisions.Add("MissionAssociationRequired");
                if (relationshipSummary.ReviewRequired) decisions.Add("AuthorizationRelationshipReviewRequired");
                reasons = reasons.Distinct().Order(StringComparer.Ordinal).ToList();
                var preview = Hash(Json(new { context.Id, context.SnapshotHash, ReleaseId = release.Id,
                    ReleaseHash = release.SnapshotHash, ReleaseContentHash = Hash(release.SnapshotJson),
                    AssignmentId = allocation.Id, allocation.Revision, allocation.AssignedScopesJson,
                    RelationshipId = relationship?.Id, RelationshipRevision = relationship?.Revision, Reasons = reasons }));
                results.Add(new(capability.Id, release.Id, release.Revision, release.SnapshotHash, offering.Id,
                    allocation.Id, allocation.Revision, new(context.Id, context.Revision, context.SnapshotHash),
                    preview, reasons.Count == 0 ? "Applicable" : "ReviewRequired", reasons, relationshipSummary.State,
                    relationshipSummary.ReviewRequired, Duties("Provider"), Duties("Shared"), Duties("Customer"),
                    decisions, references.Distinct().ToArray(), canAdopt && relationship is not null && reasons.Count == 0, canAdopt,
                    capability.Name, offering.Name));
                string[] Duties(string kind) => duties.Where(x => x.Value == kind).Select(x => x.Key).Order(StringComparer.Ordinal).ToArray();
            }
        }
        return new(results.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), page, pageSize, results.Count);
    }

    public async Task<ProviderCapabilityAdoptionResponse> AdoptAsync(string systemId, AdoptProviderCapabilityRequest request,
        string actor, CancellationToken ct, string? key = null)
    {
        await AuthorizeAsync(systemId, true, false, ct);
        await responsibilities.AuthorizeAsync(systemId, true, ct);
        var allocation = await AllocationAsync(systemId, request.AssignmentId, ct);
        return await WriteAsync(allocation, "Adopt", key, request, actor, async () =>
        {
            await AuthorizeAsync(systemId, true, false, ct);
            await responsibilities.AuthorizeAsync(systemId, true, ct);
            allocation = await AllocationAsync(systemId, request.AssignmentId, ct);
            Expected(allocation, request.ExpectedAssignmentRevision);
            var candidates = await ApplicableAsync(systemId, 1, 1, allocation.Id, allocation.OfferingId, null,
                request.CapabilityId, request.ReleaseId, ct);
            var selected = candidates.Items.SingleOrDefault();
            if (selected is null || !selected.CanProposeAdoption || selected.Applicability.SnapshotHash != request.ContextSnapshotHash
                || selected.ApplicabilityPreviewHash != request.ApplicabilityPreviewHash)
                throw new DbUpdateConcurrencyException("The selected release or applicability context changed. Reload the exact capability.");
            var subscription = await responsibilities.SubscribeAsync(systemId, request.CapabilityId, actor, ct);
            var row = new CapabilityAdoptionSnapshot
            {
                ProviderId = allocation.ProviderId, OfferingId = allocation.OfferingId, TenantId = TenantId, SystemId = systemId,
                SubscriptionId = subscription.Id, AssignmentId = allocation.Id, AssignmentRevision = allocation.Revision,
                CapabilityId = request.CapabilityId, ReleaseId = request.ReleaseId, ContextSnapshotId = selected.Applicability.RevisionId,
                SnapshotJson = Json(new { Request = request, Selected = selected }), CreatedBy = actor
            };
            row.SnapshotHash = Hash(row.SnapshotJson);
            db.Add(row);
            return new ProviderCapabilityAdoptionResponse(subscription, row.Id, request.ReleaseId, request.ContextSnapshotHash);
        }, ct);
    }
}
