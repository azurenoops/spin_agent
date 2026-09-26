using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Workspaces;

[GlobalReference]
public sealed class OrganizationNameReservation
{
    [Key]
    [MaxLength(256)]
    public string NormalizedName { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[GlobalReference]
public sealed class ProviderCapabilityWorkingRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CapabilityId { get; set; }
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
    [MaxLength(64)] public string Classification { get; set; } = string.Empty;
    [MaxLength(120)] public string ServiceCategory { get; set; } = string.Empty;
    public string ContributorsJson { get; set; } = "[]";
    public string DutiesJson { get; set; } = "{}";
    [MaxLength(64)] public string SnapshotHash { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(200)] public string UpdatedBy { get; set; } = string.Empty;
    public long? ApprovedRevision { get; set; }
    [MaxLength(64)] public string? ApprovedSnapshotHash { get; set; }
    public Guid? ApprovedPreviewId { get; set; }
    [MaxLength(64)] public string? ApprovedPreviewHash { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    [MaxLength(200)] public string? ApprovedBy { get; set; }
}

[GlobalReference]
public sealed class ProviderCapabilityContributor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkingRevisionId { get; set; }
    [MaxLength(200)] public string ContributorId { get; set; } = string.Empty;
}

[GlobalReference]
public sealed class ProviderCapabilityDuty
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkingRevisionId { get; set; }
    [MaxLength(20)] public string ControlId { get; set; } = string.Empty;
    [MaxLength(16)] public string Duty { get; set; } = string.Empty;
}

[GlobalReference]
public sealed class ProviderCapabilityRelease
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CapabilityId { get; set; }
    public long Revision { get; set; }
    [MaxLength(64)] public string SnapshotHash { get; set; } = string.Empty;
    public Guid? PreviewId { get; set; }
    [MaxLength(64)] public string? PreviewHash { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    [MaxLength(100)] public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(200)] public string PublishedBy { get; set; } = string.Empty;
}

[GlobalReference]
public sealed class ProviderPublicationPreview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CapabilityId { get; set; }
    public long Revision { get; set; }
    [MaxLength(64)] public string WorkingSnapshotHash { get; set; } = string.Empty;
    [MaxLength(64)] public string PreviewHash { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? InvalidatedAt { get; set; }
}

[GlobalReference]
public sealed class ProviderReleaseImpact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReleaseId { get; set; }
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string RegisteredSystemId { get; set; } = string.Empty;
    [MaxLength(36)] public string? SubscriptionId { get; set; }
    [MaxLength(20)] public string ControlId { get; set; } = string.Empty;
    [MaxLength(16)] public string ChangeKind { get; set; } = "Changed";
    [MaxLength(24)] public string DeliveryState { get; set; } = "Pending";
    [MaxLength(24)] public string CustomerReviewState { get; set; } = "Pending";
    [MaxLength(24)] public string NarrativeState { get; set; } = "Pending";
    public Guid? SourceEventId { get; set; }
    [MaxLength(64)] public string? SourceRevision { get; set; }
    [Column("Attempts")] public int DeliveryAttempts { get; set; }
    [MaxLength(40)] public string? LastDeliveryOutcome { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeliveredAt { get; set; }
}

[GlobalReference]
public sealed class OrganizationProvisioningOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(100)] public string IdempotencyKey { get; set; } = string.Empty;
    [MaxLength(24)] public string TenantState { get; set; } = "Completed";
    [MaxLength(24)] public string AdministratorState { get; set; } = "Pending";
    [MaxLength(24)] public string MembershipState { get; set; } = "Pending";
    public Guid? DirectoryTenantId { get; set; }
    public Guid? ObjectId { get; set; }
    public Guid? PersonId { get; set; }
    public long Revision { get; set; }
    [MaxLength(64)] public string? CreationIntentHash { get; set; }
    public string? InitialAdministratorJson { get; set; }
    public DateTimeOffset? AdministratorBoundAt { get; set; }
    [MaxLength(200)] public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[TenantScoped]
public sealed class CapabilitySetupOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(100)] public string IdempotencyKey { get; set; } = string.Empty;
    [MaxLength(16)] public string SourceKind { get; set; } = "local";
    [MaxLength(64)] public string SourceRecordId { get; set; } = string.Empty;
    [MaxLength(36)] public string? RegisteredSystemId { get; set; }
    [MaxLength(24)] public string RecordState { get; set; } = "Pending";
    [MaxLength(24)] public string ComponentLinksState { get; set; } = "Pending";
    [MaxLength(24)] public string SubscriptionState { get; set; } = "NotRequested";
    public bool SubscribeRequested { get; set; }
    public string ComponentIdsJson { get; set; } = "[]";
    public string OutcomesJson { get; set; } = "[]";
    public string? LocalCapabilityJson { get; set; }
    public string? SystemIntentJson { get; set; }
    public string? SystemPlanJson { get; set; }
    public Guid? ExecutionClaimId { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public long Revision { get; set; }
    [MaxLength(200)] public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
