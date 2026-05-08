namespace Ato.Copilot.Core.Models.Compliance;

using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

// ───────────────────────────── Enums (Feature 021) ─────────────────────────────

/// <summary>
/// Privacy Threshold Analysis determination outcome.
/// </summary>
public enum PtaDetermination
{
    /// <summary>System collects/maintains/disseminates PII — full PIA required.</summary>
    PiaRequired,
    /// <summary>System does not process PII — PIA not required.</summary>
    PiaNotRequired,
    /// <summary>System is exempt from PIA requirement (e.g., national security, government-to-government).</summary>
    Exempt,
    /// <summary>Ambiguous PII info types flagged — awaiting human confirmation before final determination.</summary>
    PendingConfirmation
}

/// <summary>
/// PIA lifecycle status.
/// </summary>
public enum PiaStatus
{
    /// <summary>PIA is being drafted.</summary>
    Draft,
    /// <summary>PIA submitted for ISSM review.</summary>
    UnderReview,
    /// <summary>PIA approved by ISSM/Privacy Officer.</summary>
    Approved,
    /// <summary>PIA approval has expired (annual review overdue).</summary>
    Expired
}

/// <summary>
/// PIA reviewer decision.
/// </summary>
public enum PiaReviewDecision
{
    /// <summary>PIA meets all requirements — approved.</summary>
    Approved,
    /// <summary>PIA has deficiencies — returned to ISSO for revision.</summary>
    RequestRevision
}

// ───────────────────────────── Entities (Feature 021) ─────────────────────────────

/// <summary>
/// Determines whether a system requires a full Privacy Impact Assessment. One per system.
/// </summary>
[TenantScoped]
public class PrivacyThresholdAnalysis
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>System this PTA belongs to.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>PTA determination outcome.</summary>
    [Required]
    public PtaDetermination Determination { get; set; }

    /// <summary>Whether the system collects PII.</summary>
    public bool CollectsPii { get; set; }

    /// <summary>Whether the system stores/maintains PII.</summary>
    public bool MaintainsPii { get; set; }

    /// <summary>Whether the system shares PII with external parties.</summary>
    public bool DisseminatesPii { get; set; }

    /// <summary>PII categories identified (e.g., "SSN", "Medical Records").</summary>
    public List<string> PiiCategories { get; set; } = new();

    /// <summary>Population affected (e.g., "Federal employees", "General public").</summary>
    [MaxLength(200)]
    public string? AffectedIndividuals { get; set; }

    /// <summary>Estimated number of PII records (≥10 triggers PIA per E-Gov Act).</summary>
    public int? EstimatedRecordCount { get; set; }

    /// <summary>SP 800-60 info type IDs that contain PII (auto-detected from SecurityCategorization).</summary>
    public List<string> PiiSourceInfoTypes { get; set; } = new();

    /// <summary>Required if Determination = Exempt (e.g., government-to-government, national security).</summary>
    [MaxLength(2000)]
    public string? ExemptionRationale { get; set; }

    /// <summary>AI-generated or user-provided justification for the determination.</summary>
    [MaxLength(4000)]
    public string? Rationale { get; set; }

    /// <summary>User who conducted the PTA.</summary>
    [Required]
    [MaxLength(200)]
    public string AnalyzedBy { get; set; } = string.Empty;

    /// <summary>When the PTA was completed (UTC).</summary>
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Full PIA document with lifecycle management. One per system (when PTA determines PIA is required).
/// </summary>
[TenantScoped]
public class PrivacyImpactAssessment
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>System this PIA belongs to.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>
    /// Triggering PTA. Nullable: when a PTA is invalidated (e.g. info types
    /// change), the PTA row is deleted but the PIA document is preserved with
    /// status <see cref="PiaStatus.UnderReview"/> and this FK cleared.
    /// </summary>
    [MaxLength(36)]
    public string? PtaId { get; set; }

    /// <summary>PIA lifecycle status.</summary>
    [Required]
    public PiaStatus Status { get; set; } = PiaStatus.Draft;

    /// <summary>Document version (incremented on resubmission after revision).</summary>
    public int Version { get; set; } = 1;

    /// <summary>Pre-populated from RegisteredSystem.Description.</summary>
    [MaxLength(4000)]
    public string? SystemDescription { get; set; }

    /// <summary>Why PII is collected.</summary>
    [MaxLength(4000)]
    public string? PurposeOfCollection { get; set; }

    /// <summary>How PII is used.</summary>
    [MaxLength(4000)]
    public string? IntendedUse { get; set; }

    /// <summary>External parties PII is shared with.</summary>
    public List<string> SharingPartners { get; set; } = new();

    /// <summary>How individuals are notified and consent is obtained.</summary>
    [MaxLength(4000)]
    public string? NoticeAndConsent { get; set; }

    /// <summary>How individuals can access/correct their records.</summary>
    [MaxLength(4000)]
    public string? IndividualAccess { get; set; }

    /// <summary>Security measures protecting PII (pre-populated from control baseline).</summary>
    [MaxLength(4000)]
    public string? Safeguards { get; set; }

    /// <summary>How long PII is retained.</summary>
    [MaxLength(500)]
    public string? RetentionPeriod { get; set; }

    /// <summary>How PII is disposed/destroyed.</summary>
    [MaxLength(500)]
    public string? DisposalMethod { get; set; }

    /// <summary>Whether a System of Records Notice is required.</summary>
    public bool? SornRequired { get; set; }

    /// <summary>Federal Register citation if SORN exists.</summary>
    [MaxLength(200)]
    public string? SornReference { get; set; }

    /// <summary>Full PIA narrative (markdown format).</summary>
    [MaxLength(16000)]
    public string? NarrativeDocument { get; set; }

    /// <summary>Reviewer notes (populated on review).</summary>
    [MaxLength(4000)]
    public string? ReviewerComments { get; set; }

    /// <summary>Specific deficiencies if revision requested.</summary>
    public List<string> ReviewDeficiencies { get; set; } = new();

    /// <summary>Reviewer who approved the PIA.</summary>
    [MaxLength(200)]
    public string? ApprovedBy { get; set; }

    /// <summary>Approval timestamp (UTC).</summary>
    public DateTime? ApprovedAt { get; set; }

    /// <summary>Approval + 1 year (annual review requirement).</summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>User who initiated the PIA.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last modification (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>PIA questionnaire sections with question-answer pairs.</summary>
    public List<PiaSection> Sections { get; set; } = new();
}

/// <summary>
/// Individual PIA questionnaire section with question-answer pairs.
/// Stored as JSON column on PrivacyImpactAssessment.
/// </summary>
public class PiaSection
{
    /// <summary>Section identifier (e.g., "1.1", "2.3").</summary>
    [Required]
    [MaxLength(10)]
    public string SectionId { get; set; } = string.Empty;

    /// <summary>Section title per OMB M-03-22.</summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Questionnaire question.</summary>
    [Required]
    [MaxLength(1000)]
    public string Question { get; set; } = string.Empty;

    /// <summary>User or AI-drafted response.</summary>
    [MaxLength(4000)]
    public string? Answer { get; set; }

    /// <summary>Whether the answer was auto-filled from system data.</summary>
    public bool IsPrePopulated { get; set; }

    /// <summary>Source entity/field if pre-populated.</summary>
    [MaxLength(200)]
    public string? SourceField { get; set; }
}

// ───────────────────────────── DTOs (Feature 021) ─────────────────────────────

/// <summary>
/// PTA analysis result returned by PrivacyService.
/// </summary>
public record PtaResult(
    string PtaId,
    PtaDetermination Determination,
    bool CollectsPii,
    bool MaintainsPii,
    bool DisseminatesPii,
    List<string> PiiCategories,
    List<string> PiiSourceInfoTypes,
    string Rationale);

/// <summary>
/// PIA generation result with document content.
/// </summary>
public record PiaResult(
    string PiaId,
    PiaStatus Status,
    int Version,
    string NarrativeDocument,
    List<PiaSection> Sections,
    int PrePopulatedSections,
    int TotalSections);

/// <summary>
/// PIA review result.
/// </summary>
public record PiaReviewResult(
    string PiaId,
    PiaReviewDecision Decision,
    PiaStatus NewStatus,
    string ReviewerComments,
    List<string> Deficiencies,
    DateTime? ExpirationDate);

/// <summary>
/// Privacy compliance dashboard result.
/// </summary>
public record PrivacyComplianceResult(
    string SystemId,
    string SystemName,
    PtaDetermination? PtaDetermination,
    PiaStatus? PiaStatus,
    bool PrivacyGateSatisfied,
    int ActiveInterconnections,
    int InterconnectionsWithAgreements,
    int ExpiredAgreements,
    int ExpiringWithin90Days,
    bool InterconnectionGateSatisfied,
    bool HasNoExternalInterconnections,
    string OverallStatus);
