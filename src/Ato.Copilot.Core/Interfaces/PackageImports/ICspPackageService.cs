using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.PackageImports;
using System.Text.Json.Serialization;

namespace Ato.Copilot.Core.Interfaces.PackageImports;

/// <summary>Provider-private durable upload, review and publication boundary.</summary>
public interface ICspPackageService
{
    Task<PackageStatus> ReceiveAsync(Guid providerId, string? key, string name, IReadOnlyList<PackageUpload> files, string actor, CancellationToken ct);
    Task<PackageStatus> ReceiveForOfferingAsync(Guid providerId, string key, string name, IReadOnlyList<PackageUpload> files,
        PackageOfferingContext context, string actor, CancellationToken ct);
    Task<PagedResult<PackageStatus>> ListAsync(int page, int pageSize, CancellationToken ct);
    Task<PackageStatus> GetAsync(Guid id, CancellationToken ct);
    Task<PackageReviewStateResponse> ReviewStateAsync(Guid id, CancellationToken ct);
    Task<PagedResult<PackageEntryResponse>> EntriesAsync(Guid id, int page, int pageSize, CancellationToken ct);
    Task<PagedResult<PackageCandidateResponse>> CandidatesAsync(Guid id, int page, int pageSize, string? type, string? reviewState, CancellationToken ct);
    Task<PackageContent> ContentAsync(Guid id, Guid artifactId, string actor, CancellationToken ct);
    Task<PackageCandidateResponse> EditAsync(Guid id, Guid candidateId, EditPackageCandidateRequest request, string actor, CancellationToken ct);
    Task<PackageEntryResponse> ExcludeAsync(Guid id, Guid entryId, ExcludePackageEntryRequest request, string actor, CancellationToken ct);
    Task<PackageStatus> RetryAsync(Guid id, string key, string actor, CancellationToken ct);
    Task<PackagePreviewResponse> PreviewAsync(Guid id, PackagePreviewRequest request, string actor, CancellationToken ct);
    Task<PackagePreviewResponse> ApproveAsync(Guid id, PackageDecisionRequest request, string actor, CancellationToken ct);
    Task<PackagePublicationResponse> PublishAsync(Guid id, PackageDecisionRequest request, string key, string actor, CancellationToken ct);
    Task<PackageUploadTally> ProcessSynchronouslyAsync(Guid id, CancellationToken ct);
}

/// <summary>Upload stream; ownership remains with the caller.</summary>
public sealed record PackageUpload(string FileName, string MediaType, Stream Content);
/// <summary>Server-validated context for atomic wizard receipt association.</summary>
public sealed record PackageOfferingContext(Guid OfferingId, long ExpectedOfferingRevision, Guid BoundaryRevisionId,
    Guid? SeriesId, Guid? PreviousVersionId);
/// <summary>Explicit association only; legacy receipts retain null.</summary>
public sealed record PackageOfferingAssociation(Guid OfferingId, Guid PackageVersionId, Guid BoundaryRevisionId);
/// <summary>Authorized attachment content; the response owns the stream.</summary>
public sealed record PackageContent(string FileName, string MediaType, Stream Content);
/// <summary>Coverage preserves exceptions and explicit exclusions.</summary>
public sealed record PackageCoverage(int Total, int Pending, int Processed, int Unsupported, int Unreadable, int Failed, int Excluded);
/// <summary>Durable package receipt and status.</summary>
public sealed record PackageStatus(Guid PackageId, Guid OperationId, string Name, long Revision, string ProcessingState,
    string PublicationState, PackageCoverage Coverage, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    PackageOfferingAssociation? Association = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CspPackageAnalysisProgress? AnalysisProgress = null);
/// <summary>Manifest metadata without source bytes or storage keys.</summary>
public sealed record PackageEntryResponse(Guid EntryId, Guid ArtifactId, string FileName, string ArchivePath, string MediaType,
    long ByteLength, string Sha256, string Status, string? Reason, int CandidateCount, string? ExclusionReason, long Revision);
/// <summary>A source-supported private candidate, never an authorization decision.</summary>
public sealed record PackageCandidateResponse(Guid CandidateId, string Type, string Name, string Description, string ComponentType,
    string Classification, string ServiceCategory, IReadOnlyDictionary<string, string> ControlDuties,
    IReadOnlyList<Guid> ContributorIds, IReadOnlyList<PackageCitation> Citations, IReadOnlyList<PackageDuplicate> DuplicateMatches,
    string? DuplicateResolution, string? Rationale, string ReviewState, long Revision, double? Confidence, Guid? PublishedRecordId,
    IReadOnlyList<string>? UnresolvedDependencies = null, PackageAuthorizationReference? AuthorizationReference = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CspPackageClaim? Claim = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? AnalysisProfileVersion = null);
/// <summary>Source-backed stated metadata, never verified authorization or a mission-system ATO.</summary>
public sealed record PackageAuthorizationReference(string Reference, string? Issuer, DateTimeOffset? IssuedAt, DateTimeOffset? ExpiresAt);
/// <summary>Exact supporting quote in an immutable retained artifact segment.</summary>
public sealed record PackageCitation(Guid ArtifactId, string ArchivePath, string Locator, string Quote);
/// <summary>A possible duplicate requiring an explicit human decision.</summary>
public sealed record PackageDuplicate(Guid RecordId, string Name, string Type, bool Published);
/// <summary>Candidate review/edit with optimistic concurrency; component type is inapplicable to authorization references.</summary>
public sealed record EditPackageCandidateRequest(long ExpectedRevision, string Name, string Description, string? ComponentType,
    string Classification, string ServiceCategory, IReadOnlyDictionary<string, string> ControlDuties,
    IReadOnlyList<Guid> ContributorIds, string ReviewAction, string? Rationale, string? DuplicateResolution,
    PackageAuthorizationReference? AuthorizationReference = null);
/// <summary>Explicit entry exclusion with a reason.</summary>
public sealed record ExcludePackageEntryRequest(long ExpectedRevision, string Rationale);
/// <summary>An exact candidate revision selected by a human.</summary>
public sealed record PackageSelection(Guid CandidateId, long Revision);
/// <summary>Bounded selection of at most 100 reviewed candidates.</summary>
public sealed record PackagePreviewRequest(long ExpectedRevision, IReadOnlyList<PackageSelection> Candidates,
    IReadOnlyList<Guid>? ImpactReviewIds = null);
/// <summary>Approval or publication binds the entire preview, not just one row.</summary>
public sealed record PackageDecisionRequest(Guid PreviewId, string PreviewHash, long Revision);
/// <summary>Exact review preview with eligibility blockers and proposed new-record impact.</summary>
public sealed record PackagePreviewResponse(Guid PreviewId, string PreviewHash, long Revision, string State,
    IReadOnlyList<PackageSelection> Candidates, IReadOnlyList<string> Blockers, int NewComponents, int NewCapabilities,
    IReadOnlyList<Guid>? ImpactReviewIds = null, string? ContextSnapshotHash = null);
/// <summary>Persisted latest decision and independent publication outcome; stale previews cannot authorize publication.</summary>
public sealed record PackageReviewStateResponse(Guid PackageId, long Revision, PackagePreviewResponse? Preview,
    bool PreviewIsStale, PackagePublicationResponse? Publication);
/// <summary>Canonical release outcomes for the selected set, replayable by idempotency key.</summary>
public sealed record PackagePublicationResponse(Guid PackageId, string PublicationState, IReadOnlyList<PackagePublishedRecord> Records, bool Existing);
/// <summary>A published new record or explicitly reused existing contributor.</summary>
public sealed record PackagePublishedRecord(Guid CandidateId, Guid RecordId, Guid? ReleaseId, string Type);
/// <summary>Legacy synchronous response shape, with all generated capabilities still requiring review.</summary>
public sealed record PackageUploadTally(int DocumentsAccepted, int ComponentsExtracted, int CapabilitiesMapped,
    int CapabilitiesNeedsReview, bool AiMappingAvailable, IReadOnlyList<PackageFileTally> Files);
/// <summary>Legacy per-file totals.</summary>
public sealed record PackageFileTally(string FileName, string SourceFormat, int ComponentsExtracted, int CapabilitiesMapped, int CapabilitiesNeedsReview);
