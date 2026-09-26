using Ato.Copilot.Core.Interfaces.PackageImports;
using Ato.Copilot.Core.Interfaces.Workspaces;
using System.Text.Json.Serialization;

namespace Ato.Copilot.Core.Interfaces.ProviderAuthorizations;

/// <summary>Explicitly recorded Azure scope; this is not a permission or authorization grant.</summary>
public sealed record ProviderAzureScope(string Cloud, Guid DirectoryTenantId, Guid SubscriptionId, string ResourceId);
/// <summary>An exact immutable source citation.</summary>
public sealed record ProviderCitation(Guid PackageId, Guid ArtifactId, string ArchivePath, string Locator, string Quote);
/// <summary>An immutable stored revision.</summary>
public sealed record ProviderSnapshotRef(Guid RevisionId, long Revision, string SnapshotHash);
/// <summary>Provider-owned service identity.</summary>
public sealed record ProviderOfferingResponse(Guid OfferingId, Guid ProviderId, string Name, string Description,
    IReadOnlyList<string> Environments, long Revision, string Lifecycle, Guid? CurrentBoundaryRevisionId,
    Guid? CurrentHostingScopeRevisionId);
/// <summary>Read-only offering summary with independent bounded pages and complete offering-scoped counts.</summary>
public sealed record OfferingOverview(Guid OfferingId, long OfferingRevision,
    OfferingOverviewAuthorizations Authorizations, OfferingOverviewPackages Packages,
    OfferingOverviewCapabilities Capabilities, OfferingOverviewHosting Hosting);
/// <summary>Current provider decisions only; review-state totals cover all pages.</summary>
public sealed record OfferingOverviewAuthorizations(IReadOnlyList<ProviderDecisionResponse> Items,
    int Page, int PageSize, int Total, int Recorded, int Unconfirmed, int Rejected);
/// <summary>Associated receipts and global processing, source-review and next-source facts.</summary>
public sealed record OfferingOverviewPackages(IReadOnlyList<OfferingOverviewPackage> Items,
    int Page, int PageSize, int Total, int NeedsAttention, int Processing, int AwaitingReview,
    OfferingAuthorizationReviewTarget? PreferredAuthorizationReview);
/// <summary>Existing receipt projection with exact retained version provenance and candidate counts.</summary>
public sealed record OfferingOverviewPackage(PackageStatus Package, Guid? PackageVersionId, int? Version,
    Guid? BoundaryRevisionId, int AwaitingReview, int AuthorizationDetails);
/// <summary>Offering-scoped source review link; type identifies the existing candidate filter.</summary>
public sealed record OfferingAuthorizationReviewTarget(Guid PackageId, string PackageName, string Type);
/// <summary>Identity-deduplicated unpublished proposals and separate canonical publication totals.</summary>
public sealed record OfferingOverviewCapabilities(int Proposed, int AwaitingReview, int AwaitingApproval, int Published, int Archived);
/// <summary>Exact current technical hosting scope and distinct associated mission systems, not authorization coverage.</summary>
public sealed record OfferingOverviewHosting(string? Name, bool Configured, int ScopeCount,
    int AssignmentCount, int AssociatedSystemCount);
/// <summary>Offering-linked inventory and hosting assignments, without an authorization or adoption grant.</summary>
public sealed record OfferingBoundaryOverview(Guid OfferingId, long OfferingRevision,
    OfferingBoundaryCapabilities Capabilities, PagedResult<OfferingBoundaryMission> MissionSystems);
/// <summary>Full deduplicated counts and a bounded page of offering-linked capability records.</summary>
public sealed record OfferingBoundaryCapabilities(IReadOnlyList<OfferingBoundaryCapability> Items,
    int Page, int PageSize, int Total, int AwaitingReview, int Published);
/// <summary>A private source proposal or canonical release with its exact retained boundary context.</summary>
public sealed record OfferingBoundaryCapability(Guid? CapabilityId, Guid? CandidateId, Guid? PackageId,
    string Name, string ReviewState, string PublicationState, Guid? ReleaseId, Guid? BoundaryRevisionId);
/// <summary>An assignment with separately observed association and active, assignment-bound adoption count.</summary>
public sealed record OfferingBoundaryMission(Guid AssignmentId, string SystemId, string? SystemName,
    string RelationshipState, bool Associated, int AdoptedCapabilityCount, IReadOnlyList<ProviderAzureScope> AssignedScopes);
/// <summary>Create a service offering without asserting authorization.</summary>
public sealed record CreateProviderOfferingRequest(string Name, string Description, IReadOnlyList<string> Environments);
/// <summary>Optimistic update of service identity.</summary>
public sealed record UpdateProviderOfferingRequest(long ExpectedRevision, string Name, string Description, IReadOnlyList<string> Environments);
/// <summary>An explicit recorded exclusion.</summary>
public sealed record ProviderBoundaryExclusion(ProviderAzureScope? Scope, string Description, string Rationale);
/// <summary>Create an immutable boundary version before receipt or as an explicitly proposed successor.</summary>
public sealed record CreateProviderBoundaryRequest(long ExpectedOfferingRevision, Guid? PredecessorRevisionId,
    string Name, string ScopeStatement, IReadOnlyList<string> Services, IReadOnlyList<Guid> ComponentSnapshotIds,
    IReadOnlyList<ProviderAzureScope> IncludedScopes, IReadOnlyList<ProviderBoundaryExclusion> Exclusions,
    IReadOnlyList<string> ProviderResponsibilities, IReadOnlyList<string> CustomerResponsibilities,
    IReadOnlyList<ProviderCitation> Citations);
/// <summary>Exact source-stated boundary, not verified coverage.</summary>
public sealed record ProviderBoundaryResponse(Guid OfferingId, long OfferingRevision, Guid BoundaryRevisionId,
    long Version, string SnapshotHash, Guid? PredecessorRevisionId, DateTimeOffset CreatedAt,
    string Name, string ScopeStatement, IReadOnlyList<string> Services, IReadOnlyList<Guid> ComponentSnapshotIds,
    IReadOnlyList<ProviderAzureScope> IncludedScopes, IReadOnlyList<ProviderBoundaryExclusion> Exclusions,
    IReadOnlyList<string> ProviderResponsibilities, IReadOnlyList<string> CustomerResponsibilities,
    IReadOnlyList<ProviderCitation> Citations);
/// <summary>Offering association retained separately from package processing.</summary>
public sealed record ProviderPackageVersionResponse(Guid PackageVersionId, Guid OfferingId, Guid SeriesId,
    long Version, Guid PackageId, Guid BoundaryRevisionId, Guid? PreviousVersionId, string ManifestHash, DateTimeOffset CreatedAt);
/// <summary>Explicitly associate a retained unlinked receipt.</summary>
public sealed record AssociateProviderPackageRequest(long ExpectedPackageRevision, Guid OfferingId,
    long ExpectedOfferingRevision, Guid BoundaryRevisionId, Guid? SeriesId = null, Guid? PreviousVersionId = null);
/// <summary>Wizard receipt context, required before new upload.</summary>
public sealed record ReceiveProviderPackageRequest(string Name, Guid BoundaryRevisionId,
    long ExpectedOfferingRevision, Guid? SeriesId = null, Guid? PreviousVersionId = null);
/// <summary>Durable receipt plus exact offering/boundary provenance.</summary>
public sealed record ProviderPackageReceiptResponse(PackageStatus Package, ProviderPackageVersionResponse PackageVersion);
/// <summary>Exact reviewed source proposal reference.</summary>
public sealed record ProviderSourceCandidateRef(Guid PackageId, Guid CandidateId, long Revision);
/// <summary>Unconfirmed source decision metadata. Dates remain date-only strings when present.</summary>
public sealed record CreateProviderDecisionRequest(long ExpectedOfferingRevision, Guid BoundaryRevisionId,
    IReadOnlyList<ProviderSourceCandidateRef> SourceCandidateRefs, string RecordKind, string Reference,
    string? IssuingAuthority, string? DecisionAsStated, string? IssuedOn, string? EffectiveOn, string? ExpiresOn,
    string ExpiryBasis, string ScopeStatement, IReadOnlyList<string> Conditions, IReadOnlyList<ProviderCitation> Citations);
/// <summary>Successor draft; never modifies a previously recorded version.</summary>
public sealed record UpdateProviderDecisionRequest(long ExpectedRevision, Guid BoundaryRevisionId,
    IReadOnlyList<ProviderSourceCandidateRef> SourceCandidateRefs, string RecordKind, string Reference,
    string? IssuingAuthority, string? DecisionAsStated, string? IssuedOn, string? EffectiveOn, string? ExpiresOn,
    string ExpiryBasis, string ScopeStatement, IReadOnlyList<string> Conditions, IReadOnlyList<ProviderCitation> Citations);
/// <summary>Flat externally issued decision projection and independent review state.</summary>
public sealed record ProviderDecisionResponse(Guid RecordId, Guid OfferingId, Guid RevisionId, long Revision,
    string SnapshotHash, string MetadataReviewState, string CurrentStanding, string? RecordedBy,
    DateTimeOffset? RecordedAt, bool ImpactReviewRequired, Guid BoundaryRevisionId,
    IReadOnlyList<ProviderSourceCandidateRef> SourceCandidateRefs, string RecordKind, string Reference,
    string? IssuingAuthority, string? DecisionAsStated, string? IssuedOn, string? EffectiveOn, string? ExpiresOn,
    string ExpiryBasis, string ScopeStatement, IReadOnlyList<string> Conditions, IReadOnlyList<ProviderCitation> Citations);
/// <summary>Record the exact reviewed source decision; does not issue an ATO.</summary>
public sealed record RecordProviderDecisionRequest(long ExpectedRevision, Guid RevisionId, string SnapshotHash, string Rationale);
/// <summary>Append a source-backed external lifecycle change.</summary>
public sealed record ProviderDecisionLifecycleRequest(long ExpectedRevision, string Kind, string EffectiveOn,
    Guid? ReplacementRevisionId, string Rationale, IReadOnlyList<ProviderCitation> Citations);
/// <summary>Retained lifecycle event and required impact work.</summary>
public sealed record ProviderDecisionLifecycleResponse(Guid EventId, ProviderDecisionResponse Record, Guid ImpactReviewId);
/// <summary>Individual changed catalog or scope revision.</summary>
public sealed record ProviderImpactChange(string Kind, Guid RecordId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long ExpectedRevision,
    string ProposedSnapshotHash);
/// <summary>Exact authorization/context preview input.</summary>
public sealed record CreateProviderImpactPreviewRequest(long ExpectedOfferingRevision,
    IReadOnlyList<ProviderImpactChange> Changes, IReadOnlyList<Guid> AuthorizationRevisionIds,
    Guid BoundaryRevisionId, Guid? HostingScopeRevisionId, IReadOnlyList<Guid> PackageVersionIds);
/// <summary>An actionable reason preventing approval or release.</summary>
public sealed record ProviderImpactBlocker(string Code, string Message, string? TargetId = null);
/// <summary>Bounded target counts; details are read separately through pagination.</summary>
public sealed record ProviderAffectedCounts(int Components, int Capabilities, int Scopes, int Systems);
/// <summary>Approval input binds immutable context and exact impact membership.</summary>
public sealed record ProviderImpactPreviewResponse(Guid ReviewId, long Revision, Guid PreviewId,
    string PreviewHash, string ContextSnapshotHash, DateTimeOffset ExpiresAt,
    IReadOnlyList<ProviderImpactBlocker> Blockers, ProviderAffectedCounts AffectedCounts);
/// <summary>Explicit impact disposition, separate from publication.</summary>
public sealed record ReviewProviderImpactRequest(long ExpectedRevision, Guid PreviewId, string PreviewHash,
    string Disposition, string Rationale);
/// <summary>Retained impact review.</summary>
public sealed record ProviderImpactReviewResponse(Guid ReviewId, long Revision, string Disposition,
    string? ReviewedBy, DateTimeOffset? ReviewedAt, string ContextSnapshotHash, bool Stale)
{
    public string? Title { get; init; }
    public string? Summary { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public ProviderAffectedCounts? AffectedCounts { get; init; }
}
/// <summary>A tenant-routed affected target, visible only in an authorized projection.</summary>
public sealed record ProviderImpactTargetResponse(string Kind, string RecordId, Guid? TenantId, string? SystemId, string ReviewState);
/// <summary>Recorded decision and its append-only lifecycle input at review time.</summary>
public sealed record ProviderDecisionContext(Guid RecordId, Guid RevisionId, string SnapshotHash, string LifecycleHash);
/// <summary>Exact source version used by a publication context.</summary>
public sealed record ProviderPackageContext(Guid PackageVersionId, Guid PackageId, string ManifestHash);
/// <summary>Immutable context material stored in impact reviews and canonical release links.</summary>
public sealed record ProviderPublicationContextMaterial(Guid OfferingId, long OfferingRevision,
    Guid BoundaryRevisionId, string BoundaryHash, Guid? HostingScopeRevisionId, string? HostingScopeHash,
    IReadOnlyList<ProviderDecisionContext> AuthorizationRevisions, IReadOnlyList<ProviderPackageContext> PackageVersions,
    IReadOnlyList<ProviderImpactChange> Changes);

/// <summary>Provider-only authorization management; every method rechecks ownership.</summary>
public interface IProviderAuthorizationService
{
    Task<PagedResult<ProviderOfferingResponse>> ListAsync(int page, int pageSize, string? search, string? lifecycle, CancellationToken ct);
    Task<ProviderOfferingResponse> GetAsync(Guid id, CancellationToken ct);
    Task<OfferingBoundaryOverview> BoundaryOverviewAsync(Guid id, int capabilityPage, int missionPage, int pageSize, CancellationToken ct);
    Task<OfferingOverview> OverviewAsync(Guid id, int authorizationPage, int packagePage, int pageSize, CancellationToken ct);
    Task<ProviderOfferingResponse> CreateAsync(CreateProviderOfferingRequest request, string key, string actor, CancellationToken ct);
    Task<ProviderOfferingResponse> UpdateAsync(Guid id, UpdateProviderOfferingRequest request, string actor, CancellationToken ct);
    Task<ProviderBoundaryResponse> CreateBoundaryAsync(Guid id, CreateProviderBoundaryRequest request, string key, string actor, CancellationToken ct);
    Task<PagedResult<ProviderBoundaryResponse>> BoundariesAsync(Guid id, int page, int pageSize, CancellationToken ct);
    Task<ProviderBoundaryResponse> BoundaryAsync(Guid id, Guid revisionId, CancellationToken ct);
    Task<ProviderPackageReceiptResponse> ReceiveAsync(Guid id, ReceiveProviderPackageRequest request, IReadOnlyList<PackageUpload> files, string key, string actor, CancellationToken ct);
    Task<ProviderPackageReceiptResponse> AssociateAsync(Guid packageId, AssociateProviderPackageRequest request, string key, string actor, CancellationToken ct);
    Task<PagedResult<ProviderPackageVersionResponse>> PackagesAsync(Guid id, int page, int pageSize, Guid? seriesId, CancellationToken ct);
    Task<ProviderDecisionResponse> CreateDecisionAsync(Guid id, CreateProviderDecisionRequest request, string key, string actor, CancellationToken ct);
    Task<ProviderDecisionResponse> UpdateDecisionAsync(Guid id, Guid recordId, UpdateProviderDecisionRequest request, string actor, CancellationToken ct);
    Task<ProviderDecisionResponse> RecordDecisionAsync(Guid id, Guid recordId, RecordProviderDecisionRequest request, string actor, CancellationToken ct);
    Task<ProviderDecisionResponse> DecisionAsync(Guid id, Guid recordId, CancellationToken ct);
    Task<PagedResult<ProviderDecisionResponse>> DecisionsAsync(Guid id, int page, int pageSize, Guid? recordId, CancellationToken ct, string? recordKind = null);
    Task<ProviderDecisionLifecycleResponse> LifecycleAsync(Guid id, Guid recordId, ProviderDecisionLifecycleRequest request, string key, string actor, CancellationToken ct);
}
