using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.ProviderAuthorizations;

/// <summary>Shared columns only; each concrete aggregate has its own table and scope filter.</summary>
[NotMapped]
public abstract class ProviderOwnedRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public Guid OfferingId { get; set; }
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string CreatedBy { get; set; } = "";
}

[ProviderScoped]
public sealed class ProviderOffering : ProviderOwnedRow
{
    [MaxLength(256)] public string Name { get; set; } = "";
    [MaxLength(8000)] public string Description { get; set; } = "";
    public string EnvironmentsJson { get; set; } = "[]";
    [MaxLength(32)] public string Lifecycle { get; set; } = "Draft";
    public Guid? CurrentBoundaryRevisionId { get; set; }
    public Guid? CurrentHostingScopeRevisionId { get; set; }
}

[ProviderScoped]
public sealed class ProviderBoundaryRevision : ProviderOwnedRow
{
    public Guid? PredecessorId { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    [MaxLength(64)] public string SnapshotHash { get; set; } = "";
}

[ProviderScoped]
public sealed class ProviderHostingScopeRevision : ProviderOwnedRow
{
    public Guid? PredecessorId { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    [MaxLength(64)] public string SnapshotHash { get; set; } = "";
}

[ProviderScoped]
public sealed class ProviderAuthorizationRecord : ProviderOwnedRow
{
    public Guid CurrentRevisionId { get; set; }
}

[ProviderScoped]
public sealed class ProviderAuthorizationRevision : ProviderOwnedRow
{
    public Guid RecordId { get; set; }
    public Guid BoundaryRevisionId { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    [MaxLength(64)] public string SnapshotHash { get; set; } = "";
    [MaxLength(32)] public string MetadataReviewState { get; set; } = "Unconfirmed";
    [MaxLength(254)] public string? RecordedBy { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
    public string? ReviewRationale { get; set; }
}

[ProviderScoped]
public sealed class ProviderAuthorizationLifecycleEvent : ProviderOwnedRow
{
    public Guid RecordId { get; set; }
    public Guid AuthorizationRevisionId { get; set; }
    [MaxLength(32)] public string Kind { get; set; } = "";
    [MaxLength(10)] public string EffectiveOn { get; set; } = "";
    public Guid? ReplacementRevisionId { get; set; }
    public string Rationale { get; set; } = "";
    public string CitationsJson { get; set; } = "[]";
}

[ProviderScoped]
public sealed class ProviderPackageVersion : ProviderOwnedRow
{
    public Guid SeriesId { get; set; }
    public long Version { get; set; }
    public Guid PackageId { get; set; }
    public Guid BoundaryRevisionId { get; set; }
    public Guid? PreviousVersionId { get; set; }
    [MaxLength(64)] public string ManifestHash { get; set; } = "";
}

[ProviderScoped]
public sealed class ProviderAuthorizationImpactReview : ProviderOwnedRow
{
    public Guid PreviewId { get; set; } = Guid.NewGuid();
    public string InputJson { get; set; } = "{}";
    public string ContextJson { get; set; } = "{}";
    [MaxLength(64)] public string PreviewHash { get; set; } = "";
    [MaxLength(64)] public string ContextSnapshotHash { get; set; } = "";
    public string TargetsJson { get; set; } = "[]";
    public string BlockersJson { get; set; } = "[]";
    [MaxLength(32)] public string Disposition { get; set; } = "PendingReview";
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(30);
    public DateTimeOffset? InvalidatedAt { get; set; }
    [MaxLength(254)] public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? Rationale { get; set; }
}

[ProviderScoped]
public sealed class ProviderCatalogContextSnapshot : ProviderOwnedRow
{
    public Guid? ReleaseId { get; set; }
    public Guid? PackageApprovalId { get; set; }
    public Guid? CapabilityId { get; set; }
    public Guid? ComponentId { get; set; }
    public Guid ImpactReviewId { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    [MaxLength(64)] public string SnapshotHash { get; set; } = "";
}

[ProviderScoped]
public sealed class ProviderHostingAssignment : ProviderOwnedRow
{
    public Guid TargetTenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    public Guid HostingScopeRevisionId { get; set; }
    public string AssignedScopesJson { get; set; } = "[]";
    public string ReferencesJson { get; set; } = "[]";
}

[TenantScoped]
public sealed class MissionProviderRelationshipReview : ProviderOwnedRow
{
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    public Guid AssignmentId { get; set; }
    public long AssignmentRevision { get; set; }
    [MaxLength(48)] public string State { get; set; } = "Undetermined";
    public Guid? AuthorizationRevisionId { get; set; }
    public Guid? BoundaryRevisionId { get; set; }
    public Guid? PreviewId { get; set; }
    public string? PreviewJson { get; set; }
    [MaxLength(64)] public string? PreviewHash { get; set; }
    public DateTimeOffset? PreviewExpiresAt { get; set; }
    public bool ReviewRequired { get; set; } = true;
    public string HistoryJson { get; set; } = "[]";
    public string EvidenceJson { get; set; } = "[]";
    [MaxLength(254)] public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}

[TenantScoped]
public sealed class CapabilityAdoptionSnapshot : ProviderOwnedRow
{
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    [MaxLength(36)] public string SubscriptionId { get; set; } = "";
    public Guid AssignmentId { get; set; }
    public long AssignmentRevision { get; set; }
    public Guid CapabilityId { get; set; }
    public Guid ReleaseId { get; set; }
    public Guid ContextSnapshotId { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    [MaxLength(64)] public string SnapshotHash { get; set; } = "";
}

[ProviderScoped]
public sealed class ProviderFinding : ProviderOwnedRow
{
    [MaxLength(256)] public string Title { get; set; } = "";
    public string Observation { get; set; } = "";
    [MaxLength(2000)] public string? SeverityAsStated { get; set; }
    [MaxLength(32)] public string WorkflowState { get; set; } = "Open";
    public string? SourceCandidateRefJson { get; set; }
    public string ControlIdsJson { get; set; } = "[]";
    public string CitationsJson { get; set; } = "[]";
}

[ProviderScoped]
public sealed class ProviderPoamItem : ProviderOwnedRow
{
    [MaxLength(256)] public string Title { get; set; } = "";
    public string FindingIdsJson { get; set; } = "[]";
    public string CorrectiveAction { get; set; } = "";
    public string? OwnerAsStated { get; set; }
    public string MilestonesJson { get; set; } = "[]";
    [MaxLength(32)] public string WorkflowState { get; set; } = "Open";
    public string? SourceCandidateRefJson { get; set; }
    public string CitationsJson { get; set; } = "[]";
    public string HistoryJson { get; set; } = "[]";
}

[ProviderScoped]
public sealed class ProviderFindingEvidence : ProviderOwnedRow
{
    public Guid FindingId { get; set; }
    [MaxLength(512)] public string FileName { get; set; } = "";
    [MaxLength(256)] public string MediaType { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public long ByteLength { get; set; }
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    public string Description { get; set; } = "";
    [MaxLength(32)] public string State { get; set; } = "PendingReview";
}

[ProviderScoped]
public sealed class ProviderFindingReview : ProviderOwnedRow
{
    public Guid FindingId { get; set; }
    public string EvidenceIdsJson { get; set; } = "[]";
    [MaxLength(32)] public string Disposition { get; set; } = "";
    public string Rationale { get; set; } = "";
}

[ProviderScoped]
public sealed class ProviderAuthorizationOperation : ProviderOwnedRow
{
    [MaxLength(200)] public string OperationScope { get; set; } = "";
    [MaxLength(100)] public string IdempotencyKey { get; set; } = "";
    [MaxLength(64)] public string IntentHash { get; set; } = "";
    public string ResponseJson { get; set; } = "";
}

[ProviderScoped]
public sealed class ProviderAuthorizationAudit : ProviderOwnedRow
{
    [MaxLength(100)] public string Action { get; set; } = "";
    public Guid TargetId { get; set; }
    public string DetailJson { get; set; } = "{}";
}

[ProviderScoped]
public sealed class ProviderClaimReview : ProviderOwnedRow
{
    public Guid PackageId { get; set; }
    public Guid CandidateId { get; set; }
    public long CandidateRevision { get; set; }
    [MaxLength(32)] public string Action { get; set; } = "";
    public string Rationale { get; set; } = "";
    public string ResolutionsJson { get; set; } = "[]";
}

[ProviderScoped]
public sealed class ProviderPackageEnrichment : ProviderOwnedRow
{
    public Guid PackageId { get; set; }
    public int SourceProfileVersion { get; set; }
    public int TargetProfileVersion { get; set; }
    [MaxLength(32)] public string State { get; set; } = "Received";
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}
