using System.Text.Json;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;
using static Ato.Copilot.Core.Services.ProviderAuthorizations.ProviderAuthorizationStore;

namespace Ato.Copilot.Core.Services.ProviderAuthorizations;

public sealed partial class ProviderMissionService
{
    private sealed record RelationshipPreview(PreviewMissionProviderRelationshipRequest Request, string ContextHash);

    public async Task<MissionProviderRelationshipPreviewResponse> PreviewAsync(string systemId, Guid relationshipId,
        PreviewMissionProviderRelationshipRequest request, string actor, CancellationToken ct)
    {
        if (request.RelationshipState is not ("Undetermined" or "SeparateBoundaryConsumer" or "ExplicitlyCoveredByRecordedScope"))
            throw new ArgumentException("Select an explicit supported relationship state.");
        var covered = request.RelationshipState == "ExplicitlyCoveredByRecordedScope";
        await AuthorizeAsync(systemId, false, false, ct);
        var relationship = await RequireRelationshipAsync(systemId, relationshipId, ct);
        await AuthorizeAsync(systemId, true, covered || relationship.State == "ExplicitlyCoveredByRecordedScope", ct);
        var allocation = await AllocationAsync(systemId, relationship.AssignmentId, ct);
        return await WriteAsync(allocation, "Preview", null, request, actor, async () =>
        {
            relationship = await RequireRelationshipAsync(systemId, relationshipId, ct);
            await AuthorizeAsync(systemId, true, covered || relationship.State == "ExplicitlyCoveredByRecordedScope", ct);
            allocation = await AllocationAsync(systemId, relationship.AssignmentId, ct);
            Expected(relationship, request.ExpectedRevision);
            Expected(allocation, request.ExpectedAssignmentRevision);
            Text(request.Rationale, "rationale", 4000);
            Bounded(request.Evidence, "evidence", covered ? 1 : 0);
            var blockers = covered ? await CoverageBlockersAsync(allocation, request.AuthorizationRevisionId,
                request.BoundaryRevisionId, request.Evidence, ct) : [];
            var context = await RelationshipContextAsync(allocation, request.AuthorizationRevisionId, ct);
            var material = Json(new RelationshipPreview(request, context));
            relationship.PreviewId = Guid.NewGuid();
            relationship.PreviewHash = Hash(material);
            relationship.PreviewJson = material;
            relationship.PreviewExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
            relationship.Revision++;
            return new MissionProviderRelationshipPreviewResponse(relationship.PreviewId.Value,
                relationship.PreviewHash, relationship.Revision, context, blockers, blockers.Count == 0);
        }, ct);
    }

    public async Task<MissionProviderRelationshipResponse> ReviewAsync(string systemId, Guid relationshipId,
        ReviewMissionProviderRelationshipRequest request, string actor, CancellationToken ct)
    {
        await AuthorizeAsync(systemId, false, false, ct);
        var relationship = await RequireRelationshipAsync(systemId, relationshipId, ct);
        if (relationship.PreviewJson is null) throw new DbUpdateConcurrencyException("Generate an exact relationship preview first.");
        var preview = Read<RelationshipPreview>(relationship.PreviewJson);
        var covered = preview.Request.RelationshipState == "ExplicitlyCoveredByRecordedScope";
        await AuthorizeAsync(systemId, true, covered || relationship.State == "ExplicitlyCoveredByRecordedScope", ct);
        var allocation = await AllocationAsync(systemId, relationship.AssignmentId, ct);
        return await WriteAsync(allocation, "Review", null, request, actor, async () =>
        {
            relationship = await RequireRelationshipAsync(systemId, relationshipId, ct);
            await AuthorizeAsync(systemId, true, covered || relationship.State == "ExplicitlyCoveredByRecordedScope", ct);
            allocation = await AllocationAsync(systemId, relationship.AssignmentId, ct);
            Expected(relationship, request.ExpectedRevision);
            if (relationship.PreviewId != request.PreviewId || relationship.PreviewHash != request.PreviewHash
                || relationship.PreviewHash != Hash(relationship.PreviewJson!) || relationship.PreviewExpiresAt <= DateTimeOffset.UtcNow
                || preview.Request.ExpectedAssignmentRevision != allocation.Revision
                || preview.ContextHash != await RelationshipContextAsync(allocation, preview.Request.AuthorizationRevisionId, ct)
                || covered && (await CoverageBlockersAsync(allocation, preview.Request.AuthorizationRevisionId,
                    preview.Request.BoundaryRevisionId, preview.Request.Evidence, ct)).Count != 0)
                throw new DbUpdateConcurrencyException("The relationship evidence or context changed. Generate and review a fresh preview.");
            var rationale = Text(request.Rationale, "rationale", 4000);
            relationship.State = preview.Request.RelationshipState;
            relationship.AssignmentRevision = allocation.Revision;
            relationship.AuthorizationRevisionId = covered ? preview.Request.AuthorizationRevisionId : null;
            relationship.BoundaryRevisionId = covered ? preview.Request.BoundaryRevisionId : null;
            relationship.EvidenceJson = Json(preview.Request.Evidence);
            relationship.ReviewRequired = relationship.State == "Undetermined";
            relationship.ReviewedBy = actor;
            relationship.ReviewedAt = DateTimeOffset.UtcNow;
            relationship.Revision++;
            var history = Read<List<JsonElement>>(relationship.HistoryJson);
            history.Add(JsonSerializer.SerializeToElement(new { Action = "Reviewed", Actor = actor,
                State = relationship.State, Rationale = rationale, At = relationship.ReviewedAt }, JsonOptions));
            relationship.HistoryJson = Json(history);
            relationship.PreviewExpiresAt = DateTimeOffset.UtcNow;
            return await ProjectAsync(allocation, relationship, await AccessAsync(systemId, ct), ct);
        }, ct);
    }

    private async Task<MissionProviderRelationshipReview> RequireRelationshipAsync(string systemId, Guid id, CancellationToken ct) =>
        await Relationships(systemId).SingleOrDefaultAsync(x => x.Id == id && db.CspProfiles.Any(p => p.Id == x.ProviderId), ct)
        ?? throw new KeyNotFoundException("Relationship not found in this mission workspace.");

    private async Task<List<ProviderImpactBlocker>> CoverageBlockersAsync(ProviderHostingAssignment allocation,
        Guid? decisionId, Guid? boundaryId, IReadOnlyList<ProviderCitation> evidence, CancellationToken ct)
    {
        var blockers = new List<ProviderImpactBlocker>();
        var offering = await OfferingAsync(allocation, ct);
        var boundary = await db.Set<ProviderBoundaryRevision>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == boundaryId && x.ProviderId == allocation.ProviderId && x.OfferingId == allocation.OfferingId, ct);
        var decision = await db.Set<ProviderAuthorizationRevision>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == decisionId && x.ProviderId == allocation.ProviderId && x.OfferingId == allocation.OfferingId, ct);
        if (boundary is null || decision is null)
        {
            blockers.Add(new("RECORDED_SCOPE_REQUIRED", "Select a recorded provider decision and its exact boundary."));
            return blockers;
        }
        var events = await db.Set<ProviderAuthorizationLifecycleEvent>().IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.ProviderId == allocation.ProviderId && x.OfferingId == allocation.OfferingId && x.RecordId == decision.RecordId).ToListAsync(ct);
        var eligibility = ProviderDecisionEligibility.Evaluate(decision, events);
        if (eligibility is not null) blockers.Add(eligibility);
        if (offering.Lifecycle == "Retired" || offering.CurrentBoundaryRevisionId != boundary.Id
            || decision.BoundaryRevisionId != boundary.Id || Hash(boundary.SnapshotJson) != boundary.SnapshotHash
            || !await db.Set<ProviderAuthorizationRecord>().IgnoreQueryFilters().AnyAsync(x =>
                x.Id == decision.RecordId && x.ProviderId == allocation.ProviderId && x.OfferingId == allocation.OfferingId
                && x.CurrentRevisionId == decision.Id, ct))
            blockers.Add(new("RECORDED_SCOPE_STALE", "Review the current decision and boundary."));
        var source = Read<CreateProviderDecisionRequest>(decision.SnapshotJson);
        if (evidence.Count == 0 || evidence.Any(x => !source.Citations.Contains(x)))
            blockers.Add(new("RECORDED_EVIDENCE_REQUIRED", "Explicit evidence must match the retained recorded decision."));
        if (!await EvidenceAvailableAsync(allocation.ProviderId, source.Citations, ct))
            blockers.Add(new("SOURCE_EVIDENCE_UNAVAILABLE", "Retained evidence is unavailable or excluded; a provider review is required."));
        if (offering.CurrentHostingScopeRevisionId != allocation.HostingScopeRevisionId)
            blockers.Add(new("HOSTING_CONTEXT_STALE", "The existing allocation requires a current hosting scope."));
        var body = Read<CreateProviderBoundaryRequest>(boundary.SnapshotJson);
        var scopes = Read<ProviderAzureScope[]>(allocation.AssignedScopesJson);
        if (scopes.Length == 0 || scopes.Any(x => !body.IncludedScopes.Any(p => Contains(p, x))
            || body.Exclusions.Any(e => e.Scope is null || Contains(e.Scope, x) || Contains(x, e.Scope))))
            blockers.Add(new("OUTSIDE_RECORDED_SCOPE", "The allocation is not within the recorded Azure scope."));
        return blockers;
    }

    private async Task<bool> EvidenceAvailableAsync(Guid providerId, IReadOnlyList<ProviderCitation> citations, CancellationToken ct)
    {
        foreach (var citation in citations)
        {
            var source = await (from entry in db.CspPackageEntries.IgnoreQueryFilters().AsNoTracking()
                join package in db.CspPackages.IgnoreQueryFilters().AsNoTracking() on entry.PackageId equals package.Id
                where package.ProviderId == providerId && package.Id == citation.PackageId && entry.Id == citation.ArtifactId
                select entry).SingleOrDefaultAsync(ct);
            if (source is null || source.Status == "Excluded" || source.ArchivePath != citation.ArchivePath
                || string.IsNullOrWhiteSpace(citation.Quote) || !Read<CspPackageSourceSegment[]>(source.SegmentsJson)
                    .Any(x => x.Locator == citation.Locator && x.Text.Contains(citation.Quote, StringComparison.Ordinal)))
                return false;
        }
        return citations.Count != 0;
    }

    private async Task<string> RelationshipContextAsync(ProviderHostingAssignment allocation, Guid? decisionId, CancellationToken ct)
    {
        var offering = await OfferingAsync(allocation, ct);
        var decision = await db.Set<ProviderAuthorizationRevision>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == decisionId && x.ProviderId == allocation.ProviderId && x.OfferingId == allocation.OfferingId, ct);
        var events = decision is null ? [] : await db.Set<ProviderAuthorizationLifecycleEvent>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.ProviderId == allocation.ProviderId && x.OfferingId == allocation.OfferingId && x.RecordId == decision.RecordId)
            .OrderBy(x => x.Id).ToListAsync(ct);
        return Hash(Json(new { offering.Revision, offering.CurrentBoundaryRevisionId, offering.CurrentHostingScopeRevisionId,
            AllocationRevision = allocation.Revision, allocation.AssignedScopesJson, allocation.HostingScopeRevisionId,
            Decision = decision?.SnapshotHash, Events = events, Date = DateOnly.FromDateTime(DateTime.UtcNow) }));
    }
}
