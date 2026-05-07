namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Tenant-scoped reference document seeded for narrative generation (Step 7). Bytes and
/// indexing live in the Feature 038 evidence repository (referenced via
/// <see cref="EvidenceArtifactId"/>); this entity tracks onboarding-specific metadata.
/// </summary>
public class NarrativeSeedDocument
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Admin-supplied display label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>JSON string array of free-form tags.</summary>
    public string Tags { get; set; } = "[]";

    /// <summary>FK to <c>EvidenceArtifact.Id</c> (Feature 038).</summary>
    public Guid EvidenceArtifactId { get; set; }

    /// <summary>Indexing pipeline status.</summary>
    public NarrativeSeedIndexingStatus IndexingStatus { get; set; } = NarrativeSeedIndexingStatus.Pending;

    /// <summary>FK → indexing <see cref="WizardJobStatus.Id"/>.</summary>
    public Guid? IndexJobId { get; set; }

    /// <summary>Lifecycle status (separate from indexing).</summary>
    public NarrativeSeedStatus Status { get; set; } = NarrativeSeedStatus.Active;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Indexing pipeline status for a narrative seed.</summary>
public enum NarrativeSeedIndexingStatus
{
    Pending,
    Indexed,
    Failed,
}

/// <summary>Lifecycle status of a narrative seed.</summary>
public enum NarrativeSeedStatus
{
    Active,
    Deleted,
}
