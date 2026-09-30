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
