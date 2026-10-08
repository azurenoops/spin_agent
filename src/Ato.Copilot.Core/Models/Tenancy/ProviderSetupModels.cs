using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

[ProviderScoped]
public sealed class ProviderSetupDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
    public string DraftJson { get; set; } = "{}";
    public string CommittedMetadataJson { get; set; } = "{}";
    public string? CompletionSnapshotJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string CreatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string UpdatedBy { get; set; } = "";
}

[ProviderScoped]
public sealed class ProviderSetupCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public Guid DraftId { get; set; }
    [MaxLength(32)] public string Operation { get; set; } = "";
    [MaxLength(100)] public string IdempotencyKey { get; set; } = "";
    [MaxLength(64)] public string IntentHash { get; set; } = "";
    public string RequestJson { get; set; } = "{}";
    public string OutcomeJson { get; set; } = "{}";
    public string HistoricalCommitSnapshotJson { get; set; } = "{}";
    public long CommittedDraftRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string CreatedBy { get; set; } = "";
}

[ProviderScoped]
public sealed class CspPackageUploadIntent
{
    public Guid Id { get; set; }
    public Guid ProviderId { get; set; }
    public Guid? DraftId { get; set; }
    [MaxLength(32)] public string EntryPoint { get; set; } = "Onboarding";
    public Guid? OfferingHintId { get; set; }
    [MaxLength(100)] public string IdempotencyKey { get; set; } = "";
    [MaxLength(64)] public string IntentHash { get; set; } = "";
    public string IntentJson { get; set; } = "{}";
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string CreatedBy { get; set; } = "";
}

[ProviderScoped]
public sealed class ServicePortfolio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    [MaxLength(256)] public string Name { get; set; } = "";
    [MaxLength(8000)] public string Description { get; set; } = "";
    [MaxLength(32)] public string Lifecycle { get; set; } = "Active";
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string CreatedBy { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string UpdatedBy { get; set; } = "";
}

[ProviderScoped]
public sealed class ServicePortfolioOfferingRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public Guid PortfolioId { get; set; }
    public Guid OfferingId { get; set; }
    public Guid? PredecessorId { get; set; }
    public bool IsPrimary { get; set; } = true;
    [MaxLength(32)] public string State { get; set; } = "Active";
    [MaxLength(2000)] public string? Reason { get; set; }
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string CreatedBy { get; set; } = "";
}

[ProviderScoped]
public sealed class ProviderOfferingAuthorizationIntentRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public Guid SetupId { get; set; }
    public Guid? OfferingId { get; set; }
    public Guid? PredecessorId { get; set; }
    [MaxLength(32)] public string StartingPoint { get; set; } = "DetermineLater";
    public string UnconfirmedFactsJson { get; set; } = "{}";
    public string SourcesJson { get; set; } = "[]";
    public string UnresolvedFieldsJson { get; set; } = "[]";
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string CreatedBy { get; set; } = "";
}

[ProviderScoped]
public sealed class ProviderSetupWorkItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProviderId { get; set; }
    public Guid SetupId { get; set; }
    public Guid? PortfolioId { get; set; }
    public Guid? OfferingId { get; set; }
    public Guid? UploadIntentId { get; set; }
    [MaxLength(64)] public string Type { get; set; } = "";
    [MaxLength(64)] public string ReasonCode { get; set; } = "";
    [MaxLength(32)] public string State { get; set; } = "Open";
    [MaxLength(64)] public string OwnerRole { get; set; } = "PortalAdministrator";
    [MaxLength(254)] public string? AccountablePrincipalId { get; set; }
    [MaxLength(254)] public string? ReviewerPrincipalId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    [MaxLength(2000)] public string AcceptanceCriteria { get; set; } = "";
    [MaxLength(2048)] public string Destination { get; set; } = "";
    public long SourceRevision { get; set; }
    [MaxLength(300)] public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string CreatedBy { get; set; } = "";
    public DateTimeOffset? ClosedAt { get; set; }
    [MaxLength(254)] public string? ClosedBy { get; set; }
}
