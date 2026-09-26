using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Models.PackageImports;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.PackageImports;

public sealed partial class CspPackageService
{
    public Task<PackageCandidateResponse> EditAsync(Guid id, Guid candidateId, EditPackageCandidateRequest request, string actor, CancellationToken ct) =>
        ChangeAsync(id, "CandidateReviewed", actor, async (db, package) =>
        {
            Editable(package);
            var candidate = await db.CspPackageCandidates.SingleOrDefaultAsync(x => x.PackageId == id && x.Id == candidateId, ct)
                ?? throw new KeyNotFoundException("Candidate was not found.");
            if (candidate.Revision != request.ExpectedRevision) throw new DbUpdateConcurrencyException("Candidate revision is stale.");
            if (Candidate(candidate).PublishedRecordId.HasValue) throw new DbUpdateConcurrencyException("Published candidates are immutable.");
            ValidateEdit(request, candidate.Type);
            var reference = request.AuthorizationReference ?? Candidate(candidate).AuthorizationReference;
            ValidateAuthorizationReference(candidate.Type, reference, request.ReviewAction);
            if (candidate.Type == "AuthorizationReference" && request.ReviewAction == "Reviewed")
            {
                var entries = await db.CspPackageEntries.Where(x => x.PackageId == id).ToListAsync(ct);
                var citations = Candidate(candidate).Citations;
                // Reviewing cited reference metadata does not clear surrounding semantic-analysis exceptions.
                if (citations.Count == 0 || citations.Any(c => !SupportsCitation(entries.SingleOrDefault(x => x.Id == c.ArtifactId), c, allowIncomplete: true)))
                    throw new DbUpdateConcurrencyException("A reviewed authorization reference requires non-excluded, source-supported citations.");
            }
            await InvalidateAsync(db, package, ct);
            candidate.Revision++;
            candidate.ReviewState = request.ReviewAction;
            candidate.ReviewedBy = request.ReviewAction == "Reviewed" ? actor : null;
            candidate.ReviewedAt = request.ReviewAction == "Reviewed" ? DateTimeOffset.UtcNow : null;
            var payload = Candidate(candidate) with
            {
                Name = request.Name.Trim(), Description = request.Description.Trim(),
                ComponentType = candidate.Type == "AuthorizationReference"
                    ? Candidate(candidate).ComponentType
                    : request.ComponentType ?? throw new ArgumentException("Component type is required."),
                Classification = request.Classification.Trim(), ServiceCategory = request.ServiceCategory.Trim(),
                ControlDuties = request.ControlDuties.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value),
                ContributorIds = request.ContributorIds.Distinct().Order().ToArray(),
                DuplicateResolution = request.DuplicateResolution, Rationale = request.Rationale,
                AuthorizationReference = reference
            };
            candidate.PayloadJson = Json(payload);
            Audit(db, package, "CandidateDecision", actor, Json(new
            {
                candidateId, request.ExpectedRevision, candidate.Revision, request.ReviewAction, request.Rationale,
                request.DuplicateResolution, SnapshotHash = Hash(Json(payload))
            }));
            return payload;
        }, ct);

    public Task<PackageEntryResponse> ExcludeAsync(Guid id, Guid entryId, ExcludePackageEntryRequest request, string actor, CancellationToken ct) =>
        ChangeAsync(id, "EntryExcluded", actor, async (db, package) =>
        {
            Editable(package);
            if (string.IsNullOrWhiteSpace(request.Rationale) || request.Rationale.Length > 2000)
                throw new ArgumentException("An explicit exclusion rationale of 1-2000 characters is required.");
            var entries = await db.CspPackageEntries.Where(x => x.PackageId == id).ToListAsync(ct);
            var entry = entries.SingleOrDefault(x => x.Id == entryId) ?? throw new KeyNotFoundException("Entry was not found.");
            if (entry.Revision != request.ExpectedRevision) throw new DbUpdateConcurrencyException("Entry revision is stale.");
            var excluded = new HashSet<Guid> { entryId };
            while (true)
            {
                var before = excluded.Count;
                foreach (var child in entries.Where(x => x.OriginalEntryId.HasValue && excluded.Contains(x.OriginalEntryId.Value)))
                    excluded.Add(child.Id);
                if (before == excluded.Count) break;
            }
            var candidates = await db.CspPackageCandidates.Where(x => x.PackageId == id).ToListAsync(ct);
            if (candidates.Any(x => Candidate(x).PublishedRecordId.HasValue && Candidate(x).Citations.Any(c => excluded.Contains(c.ArtifactId))))
                throw new DbUpdateConcurrencyException("Cannot exclude evidence already used by a published record.");
            foreach (var child in entries.Where(x => excluded.Contains(x.Id)))
            {
                child.Status = "Excluded";
                child.ExclusionReason = request.Rationale.Trim();
                child.Revision++;
            }
            foreach (var candidate in candidates.Where(x => x.Type == "AuthorizationReference" && x.ReviewState == "Reviewed"
                && Candidate(x).Citations.Any(c => excluded.Contains(c.ArtifactId))))
            {
                candidate.ReviewState = "NeedsReview";
                candidate.ReviewedAt = null;
                candidate.ReviewedBy = null;
                candidate.Revision++;
                Audit(db, package, "ReferenceEvidenceExcluded", actor, candidate.Id.ToString("D"));
            }
            await InvalidateAsync(db, package, ct);
            Audit(db, package, "CoverageDecision", actor, Json(new { entryId, request.ExpectedRevision, request.Rationale, AffectedEntries = excluded.Order() }));
            return Entry(entry, candidates.Count(x => Candidate(x).Citations.Any(c => c.ArtifactId == entryId)));
        }, ct);

    public Task<PackageStatus> RetryAsync(Guid id, string key, string actor, CancellationToken ct)
    {
        Authorize();
        ValidateKey(key);
        return ChangeAsync(id, "RetryRequested", actor, async (db, package) =>
        {
            var keys = Read<List<string>>(package.RetryKeysJson);
            if (keys.Contains(key)) return await StatusAsync(db, package, ct);
            Editable(package);
            if (package.ProcessingState is not ("Failed" or "NeedsAttention"))
                throw new DbUpdateConcurrencyException("Only failed or incomplete packages can be retried.");
            if (keys.Count >= 100) throw new ArgumentException("Package retry limit reached; contact the administrator.");
            keys.Add(key);
            package.RetryKeysJson = Json(keys);
            if (package.AnalysisCheckpointJson is not null)
            {
                var checkpoint = Read<CspPackageAnalysisCheckpoint>(package.AnalysisCheckpointJson);
                package.AnalysisCheckpointJson = Json(checkpoint with
                {
                    AutomaticContinuationEntryKeys = [],
                    AnalysisProgress = checkpoint.AnalysisProgress is { } progress
                        ? progress with { ContinuingAutomatically = false } : null
                });
            }
            package.ProcessingState = "Received";
            package.LeaseId = null;
            package.LeaseExpiresTicks = 0;
            await InvalidateAsync(db, package, ct);
            return await StatusAsync(db, package, ct);
        }, ct);
    }

    public async Task<PackageUploadTally> ProcessSynchronouslyAsync(Guid id, CancellationToken ct)
    {
        await GetAsync(id, ct);
        var processor = new CspPackageProcessor(factory, storage, analyzer, logger);
        await processor.ProcessAsync(id, ct);
        while (true)
        {
            var state = await GetAsync(id, ct);
            if (state.ProcessingState is not ("Received" or "Processing")) break;
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            await processor.ProcessAsync(id, ct);
        }
        await using var db = await factory.CreateDbContextAsync(ct);
        var package = await LoadAsync(db, id, ct);
        if (package.ProcessingState == "Failed") throw new PackageStateException(package.LastError ?? "Package analysis failed.");
        var originals = await db.CspPackageEntries.Where(x => x.PackageId == id && x.IsOriginal).ToListAsync(ct);
        var entries = await db.CspPackageEntries.Where(x => x.PackageId == id).ToListAsync(ct);
        if (entries.Any(x => x.Status is "Failed" or "Unreadable"))
            throw new PackageAnalysisException("PARSE_FAILED", $"Package {id:D} was retained, but contains unreadable sources. Review its coverage before retrying.");
        if (entries.Any(x => x.Status is "Unsupported" or "Pending"))
            throw new PackageAnalysisException("UNSUPPORTED_ATO_DOCUMENT", $"Package {id:D} was retained, but analysis is incomplete. Review its coverage and explicitly exclude unsupported content.");
        var candidates = (await db.CspPackageCandidates.Where(x => x.PackageId == id).ToListAsync(ct)).Select(Candidate).ToArray();
        Guid Root(Guid artifact)
        {
            var row = entries.Single(x => x.Id == artifact);
            return row.OriginalEntryId.HasValue ? Root(row.OriginalEntryId.Value) : artifact;
        }
        var files = originals.Select(original =>
        {
            var related = candidates.Where(x => x.Citations.Any(c => Root(c.ArtifactId) == original.Id)).ToArray();
            var format = Path.GetExtension(original.FileName).ToLowerInvariant() switch
            {
                ".pdf" => "Pdf", ".docx" => "Docx", ".json" => "OscalJson",
                ".xlsx" => "Xlsx", ".zip" => "EmassZip", _ => "Package"
            };
            return new PackageFileTally(original.FileName, format,
                related.Count(x => x.Type == "Component"), 0, related.Count(x => x.Type == "Capability"));
        }).ToArray();
        return new(originals.Count, candidates.Count(x => x.Type == "Component"), 0,
            candidates.Count(x => x.Type == "Capability"), false, files);
    }

    private static void Editable(CspPackage package)
    {
        if (package.ProcessingState is "Processing" or "Received" || package.PublicationState == "Publishing")
            throw new DbUpdateConcurrencyException("Package work is in progress; wait for the current operation.");
    }

    private static void ValidateEdit(EditPackageCandidateRequest request, string candidateType)
    {
        var isReference = candidateType == "AuthorizationReference";
        if (request.Description is null || request.Classification is null || request.ServiceCategory is null
            || request.ContributorIds is null || request.ControlDuties is null)
            throw new ArgumentException("Description, classification, category, contributors and control duties must not be null.");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > (isReference ? 2000 : 256) || request.Description.Length > 2000
            || request.Classification.Length > 100 || request.ServiceCategory.Length > 100)
            throw new ArgumentException("Name, description, classification or category is invalid.");
        if (!isReference && (!Enum.TryParse<CspComponentType>(request.ComponentType, out var type) || !Enum.IsDefined(type)))
            throw new ArgumentException("Component type is invalid.");
        if (request.ReviewAction is not ("NeedsReview" or "Reviewed" or "Rejected"))
            throw new ArgumentException("Review action must be NeedsReview, Reviewed or Rejected.");
        if (request.DuplicateResolution is not (null or "KeepSeparate" or "ReusePublished"))
            throw new ArgumentException("Resolve duplicates by rejecting, KeepSeparate, or ReusePublished.");
        if ((request.ReviewAction == "Rejected" || request.DuplicateResolution is not null)
            && string.IsNullOrWhiteSpace(request.Rationale))
            throw new ArgumentException("A rationale is required for rejection or duplicate resolution.");
        if (request.Rationale?.Length > 2000 || request.ContributorIds.Count > 100 || request.ControlDuties.Count > 500
            || request.ControlDuties.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 50 || x.Value is not ("Provider" or "Shared" or "Customer")))
            throw new ArgumentException("Rationale, contributor selection or control duties exceed the allowed bounds.");
    }

    private static void ValidateAuthorizationReference(string type, PackageAuthorizationReference? reference, string action)
    {
        if (type != "AuthorizationReference")
        {
            if (reference is not null) throw new ArgumentException("Authorization metadata is only valid for authorization-reference candidates.");
            return;
        }
        if (reference is null)
        {
            if (action == "Reviewed") throw new ArgumentException("A source-supported authorization reference is required before review.");
            return;
        }
        if (string.IsNullOrWhiteSpace(reference.Reference) || reference.Reference.Length > 2000 || reference.Issuer?.Length > 500
            || (reference.IssuedAt.HasValue && reference.ExpiresAt.HasValue && reference.ExpiresAt < reference.IssuedAt))
            throw new ArgumentException("Reference must contain 1-2000 characters, issuer at most 500, and expiration cannot precede issuance.");
    }

    private static bool SupportsCitation(CspPackageEntry? source, PackageCitation citation, bool allowIncomplete = false) =>
        source is not null && (source.Status == "Processed" || allowIncomplete && source.Status == "Unsupported")
        && !string.IsNullOrWhiteSpace(citation.Quote)
        && source.ArchivePath == citation.ArchivePath
        && Read<CspPackageSourceSegment[]>(source.SegmentsJson)
            .Any(x => x.Locator == citation.Locator && x.Text.Contains(citation.Quote, StringComparison.Ordinal));

    private static async Task InvalidateAsync(AtoCopilotContext db, CspPackage package, CancellationToken ct)
    {
        Touch(package, true);
        var changedIds = await db.CspPackageCandidates.Where(x => x.PackageId == package.Id).Select(x => x.Id).ToListAsync(ct);
        var impactReviews = await db.Set<ProviderAuthorizationImpactReview>()
            .Where(x => x.ProviderId == package.ProviderId && x.InvalidatedAt == null).ToListAsync(ct);
        foreach (var review in impactReviews)
        {
            var context = ProviderAuthorizationStore.Read<ProviderPublicationContextMaterial>(review.ContextJson);
            if (context.PackageVersions?.Any(x => x.PackageId == package.Id) == true
                || context.Changes?.Any(x => changedIds.Contains(x.RecordId)) == true)
                review.InvalidatedAt = DateTimeOffset.UtcNow;
        }
        var approvals = await db.CspPackageApprovals.Where(x => x.PackageId == package.Id && x.State != "Published").ToListAsync(ct);
        foreach (var approval in approvals) approval.State = "Invalidated";
        var candidates = await db.CspPackageCandidates.Where(x => x.PackageId == package.Id && x.ReviewState == "Approved").ToListAsync(ct);
        foreach (var candidate in candidates)
            if (!Candidate(candidate).PublishedRecordId.HasValue) candidate.ReviewState = "Reviewed";
    }

    public Task<PackagePreviewResponse> PreviewAsync(Guid id, PackagePreviewRequest request, string actor, CancellationToken ct) =>
        PreviewAsync(id, request, request.ImpactReviewIds, actor, ct);

    public Task<PackagePreviewResponse> PreviewAsync(Guid id, PackagePreviewRequest request,
        IReadOnlyList<Guid>? impactReviewIds, string actor, CancellationToken ct) =>
        ChangeAsync(id, "ApprovalPreviewed", actor, async (db, package) =>
        {
            Editable(package);
            if (request.ExpectedRevision != package.Revision) throw new DbUpdateConcurrencyException("Package revision is stale.");
            if (request.Candidates is null || request.Candidates.Count is < 1 or > 100 || request.Candidates.Select(x => x.CandidateId).Distinct().Count() != request.Candidates.Count)
                throw new ArgumentException("Select 1-100 distinct exact candidate revisions.");
            var selection = request.Candidates.OrderBy(x => x.CandidateId).ToArray();
            var material = await ValidateSelectionAsync(db, package, selection, ct, impactReviewIds);
            var approval = new CspPackageApproval
            {
                PackageId = id, Revision = package.Revision, CreatedVersion = package.Version, SelectionJson = Json(selection),
                PreviewHash = material.Hash, BlockersJson = Json(material.Blockers), SnapshotJson = material.Snapshot
            };
            db.CspPackageApprovals.Add(approval);
            return Preview(approval, material.Candidates);
        }, ct);

    public Task<PackagePreviewResponse> ApproveAsync(Guid id, PackageDecisionRequest request, string actor, CancellationToken ct) =>
        ChangeAsync(id, "Approved", actor, async (db, package) =>
        {
            Editable(package);
            var approval = await DecisionAsync(db, package, request, ct);
            if (approval.State is not ("Preview" or "Approved")) throw new DbUpdateConcurrencyException("Preview has been invalidated.");
            var material = await ValidateSelectionAsync(db, package, Read<PackageSelection[]>(approval.SelectionJson), ct,
                PackageImpactIds(approval.SnapshotJson));
            RequireEligible(approval, material);
            foreach (var selected in material.Candidates)
            {
                var candidate = await db.CspPackageCandidates.SingleAsync(x => x.PackageId == id && x.Id == selected.CandidateId, ct);
                candidate.ReviewState = "Approved";
            }
            approval.State = "Approved";
            approval.ApprovedAt = DateTimeOffset.UtcNow;
            approval.ApprovedBy = actor;
            return Preview(approval, material.Candidates);
        }, ct);

    private static PackagePreviewResponse Preview(CspPackageApproval approval, IReadOnlyList<PackageCandidateResponse> candidates)
    {
        using var document = System.Text.Json.JsonDocument.Parse(approval.SnapshotJson);
        var contextHash = document.RootElement.TryGetProperty("ContextSnapshotHash", out var context) ? context.GetString() : null;
        return new(approval.Id, approval.PreviewHash, approval.Revision, approval.State, Read<PackageSelection[]>(approval.SelectionJson),
            Read<string[]>(approval.BlockersJson), candidates.Count(x => x.Type == "Component" && x.DuplicateResolution != "ReusePublished"),
            candidates.Count(x => x.Type == "Capability"), PackageImpactIds(approval.SnapshotJson), contextHash);
    }

    private static async Task<CspPackageApproval> DecisionAsync(AtoCopilotContext db, CspPackage package, PackageDecisionRequest request, CancellationToken ct)
    {
        var approval = await db.CspPackageApprovals.SingleOrDefaultAsync(x => x.PackageId == package.Id && x.Id == request.PreviewId, ct)
            ?? throw new KeyNotFoundException("Package preview was not found.");
        if (request.Revision != package.Revision || approval.Revision != request.Revision || approval.PreviewHash != request.PreviewHash
            || approval.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new DbUpdateConcurrencyException("The exact package preview is stale; generate a new preview.");
        return approval;
    }

    private sealed record SelectionMaterial(string Hash, string Snapshot, IReadOnlyList<string> Blockers,
        IReadOnlyList<PackageCandidateResponse> Candidates, ProviderPublicationGuard.Binding Binding);

    private static Guid[] PackageImpactIds(string snapshot)
    {
        using var document = System.Text.Json.JsonDocument.Parse(snapshot);
        return document.RootElement.TryGetProperty("ImpactReviewIds", out var ids)
            ? ids.EnumerateArray().Select(x => x.GetGuid()).ToArray() : [];
    }
    private static void RequireEligible(CspPackageApproval approval, SelectionMaterial material)
    {
        if (material.Blockers.Count != 0) throw new DbUpdateConcurrencyException(string.Join(" ", material.Blockers));
        if (material.Hash != approval.PreviewHash) throw new DbUpdateConcurrencyException("Candidate, dependency, evidence or publication impact changed.");
    }

    private static async Task<SelectionMaterial> ValidateSelectionAsync(AtoCopilotContext db, CspPackage package,
        IReadOnlyList<PackageSelection> selection, CancellationToken ct, IReadOnlyList<Guid>? impactReviewIds = null)
    {
        var blockers = new List<string>();
        if (package.ProcessingState is not ("ReadyForReview" or "NeedsAttention")) blockers.Add("Package analysis is not ready for review.");
        var entries = await db.CspPackageEntries.Where(x => x.PackageId == package.Id).OrderBy(x => x.Id).ToListAsync(ct);
        if (entries.Any(x => x.Status is not ("Processed" or "Excluded"))) blockers.Add("Resolve or explicitly exclude every coverage exception.");
        var allRows = await db.CspPackageCandidates.Where(x => x.PackageId == package.Id).ToListAsync(ct);
        var candidates = new List<PackageCandidateResponse>();
        var published = await db.CspInheritedComponents.Where(x => x.CspProfileId == package.ProviderId
            && x.Status == CspInheritedComponentStatus.Published).OrderBy(x => x.Id).ToListAsync(ct);
        var publishedCapabilities = await db.CspInheritedCapabilities.Where(x => x.CspInheritedComponent.CspProfileId == package.ProviderId
            && x.CspInheritedComponent.Status == CspInheritedComponentStatus.Published
            && db.ProviderCapabilityReleases.Any(r => r.CapabilityId == x.Id)).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Name, x.Description }).ToListAsync(ct);
        foreach (var selected in selection)
        {
            var row = allRows.SingleOrDefault(x => x.Id == selected.CandidateId);
            if (row is null) { blockers.Add($"Candidate {selected.CandidateId} is absent."); continue; }
            var candidate = Candidate(row);
            candidates.Add(candidate);
            if (row.Revision != selected.Revision) blockers.Add($"Candidate {row.Id} revision is stale.");
            if (row.ReviewState is not ("Reviewed" or "Approved") || row.ReviewedBy is null) blockers.Add($"Candidate {row.Id} needs explicit human review.");
            if (candidate.PublishedRecordId.HasValue) blockers.Add($"Candidate {row.Id} was already published.");
            if (candidate.Type is not ("Component" or "Capability")) blockers.Add($"Candidate {row.Id} is supporting analysis; incorporate its duties into a reviewed capability, not a standalone publication.");
            if (Read<string[]>(row.UnresolvedDependenciesJson).Length > 0
                && (candidate.ContributorIds.Count == 0 || string.IsNullOrWhiteSpace(candidate.Rationale)))
                blockers.Add($"Candidate {row.Id} has unresolved source dependencies; identify reviewed contributors and record a resolution rationale.");
            if (candidate.Citations.Count == 0) blockers.Add($"Candidate {row.Id} has no evidence.");
            foreach (var citation in candidate.Citations)
            {
                var source = entries.SingleOrDefault(x => x.Id == citation.ArtifactId);
                if (!SupportsCitation(source, citation))
                    blockers.Add($"Candidate {row.Id} has excluded, missing or invalid source evidence.");
            }
            var duplicates = candidate.DuplicateMatches.Count > 0 || allRows.Any(x => x.Id != row.Id && x.Type == row.Type
                && Candidate(x).Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase) && x.ReviewState != "Rejected")
                || (candidate.Type == "Component" && published.Any(x => x.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)))
                || (candidate.Type == "Capability" && publishedCapabilities.Any(x => x.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)));
            if (duplicates && (candidate.DuplicateResolution is null || string.IsNullOrWhiteSpace(candidate.Rationale)))
                blockers.Add($"Candidate {row.Id} requires an explicit duplicate resolution.");
            if (candidate.DuplicateResolution == "ReusePublished" && (candidate.Type != "Component" || candidate.ContributorIds.Count != 1
                || !published.Any(x => x.Id == candidate.ContributorIds[0])))
                blockers.Add($"Candidate {row.Id} must identify exactly one eligible published component to reuse.");
            if (candidate.Type == "Component" && candidate.DuplicateResolution != "ReusePublished" && candidate.ContributorIds.Count != 0)
                blockers.Add($"Component {row.Id} cannot declare capability contributors; use ReusePublished for an existing component.");
            if (candidate.Type == "Capability" && (string.IsNullOrWhiteSpace(candidate.Classification) || string.IsNullOrWhiteSpace(candidate.ServiceCategory)
                || candidate.ContributorIds.Count == 0 || candidate.ControlDuties.Count == 0))
                blockers.Add($"Capability {row.Id} needs classification, category, contributors and control duties.");
            foreach (var contributor in candidate.ContributorIds)
                if (!published.Any(x => x.Id == contributor)
                    && !selection.Any(x => x.CandidateId == contributor && allRows.Any(r => r.Id == contributor && r.Type == "Component")))
                    blockers.Add($"Candidate {row.Id} requires reviewed component {contributor} in the selected set or an eligible published contributor.");
        }
        var usedIds = candidates.SelectMany(x => x.ContributorIds).ToHashSet();
        var changes = new List<ProviderImpactChange>();
        foreach (var candidate in candidates.Where(x => x.Type is "Component" or "Capability"))
            changes.Add(await ProviderImpactService.ChangeAsync(db, package.ProviderId, candidate.Type, candidate.CandidateId, ct));
        var binding = await ProviderPublicationGuard.BindAsync(db, package.ProviderId, changes, impactReviewIds, package.Id, ct);
        var snapshot = Json(new
        {
            package.Id, package.Revision, Selection = selection,
            Candidates = candidates.Select(x => x with { ReviewState = "Reviewed" }),
            Entries = entries.Select(x => new { x.Id, x.Revision, x.Status, x.Sha256, x.ExclusionReason }),
            Dependencies = published.Where(x => usedIds.Contains(x.Id)).Select(x => new { x.Id, x.Name, x.Description, x.Status, x.ComponentType, x.UpdatedAt }),
            DuplicateComponents = published.Where(x => candidates.Any(c => c.Type == "Component" && c.Name.Equals(x.Name, StringComparison.OrdinalIgnoreCase))).Select(x => new { x.Id, x.Name }),
            DuplicateCapabilities = publishedCapabilities.Where(x => candidates.Any(c => c.Type == "Capability" && c.Name.Equals(x.Name, StringComparison.OrdinalIgnoreCase))),
            Impact = new { NewComponents = candidates.Count(x => x.Type == "Component" && x.DuplicateResolution != "ReusePublished"), NewCapabilities = candidates.Count(x => x.Type == "Capability"), ExistingSubscriptions = 0 },
            ImpactReviewIds = binding.Reviews.Select(x => x.Id).ToArray(),
            ContextSnapshotHash = binding.ContextHash
        });
        return new(Hash(snapshot), snapshot, blockers.Distinct().ToArray(), candidates, binding);
    }
}
