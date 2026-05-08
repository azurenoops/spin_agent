using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

// ═══════════════════════════════════════════════════════════════════════════════
// Assessment Artifact Entities (Feature 015 — US7)
// Per-control effectiveness determinations and aggregate assessment records.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Records an SCA's per-control effectiveness determination during an assessment.
/// Maps to DoD CAT severity levels when the determination is OtherThanSatisfied.
/// </summary>
[TenantScoped]
public class ControlEffectiveness
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK → ComplianceAssessment this determination belongs to.</summary>
    [Required]
    public string AssessmentId { get; set; } = string.Empty;

    /// <summary>FK → RegisteredSystem being assessed.</summary>
    [Required]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>NIST 800-53 control ID (e.g., "AC-2", "SC-7").</summary>
    [Required]
    [MaxLength(20)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>SCA determination: Satisfied or OtherThanSatisfied.</summary>
    public EffectivenessDetermination Determination { get; set; }

    /// <summary>Assessment method used: "Test", "Interview", "Examine".</summary>
    [MaxLength(50)]
    public string? AssessmentMethod { get; set; }

    /// <summary>Links to ComplianceEvidence record IDs (stored as JSON).</summary>
    public List<string> EvidenceIds { get; set; } = new();

    /// <summary>Assessor notes about this determination.</summary>
    [MaxLength(4000)]
    public string? Notes { get; set; }

    /// <summary>
    /// DoD CAT severity level. Required when Determination is OtherThanSatisfied.
    /// CAT I = Critical/High, CAT II = Medium, CAT III = Low.
    /// </summary>
    public CatSeverity? CatSeverity { get; set; }

    /// <summary>SCA user ID who made the determination.</summary>
    [Required]
    public string AssessorId { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the assessment determination.</summary>
    public DateTime AssessedAt { get; set; } = DateTime.UtcNow;

    // ─── Navigation Properties ───────────────────────────────────────────────

    /// <summary>Navigation to parent ComplianceAssessment.</summary>
    public ComplianceAssessment? Assessment { get; set; }

    /// <summary>Navigation to parent RegisteredSystem.</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }
}

/// <summary>
/// Aggregate per-system assessment summary linked to a ComplianceAssessment.
/// Referenced by authorization tools and the dashboard. Records overall compliance
/// posture computed from individual ControlEffectiveness determinations.
/// </summary>
[TenantScoped]
public class AssessmentRecord
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK → RegisteredSystem this assessment covers.</summary>
    [Required]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>FK → ComplianceAssessment this record summarizes.</summary>
    [Required]
    public string ComplianceAssessmentId { get; set; } = string.Empty;

    /// <summary>Total number of controls assessed.</summary>
    public int ControlsAssessed { get; set; }

    /// <summary>Controls determined Satisfied.</summary>
    public int ControlsSatisfied { get; set; }

    /// <summary>Controls determined OtherThanSatisfied.</summary>
    public int ControlsOtherThanSatisfied { get; set; }

    /// <summary>Controls excluded from scoring (N/A).</summary>
    public int ControlsNotApplicable { get; set; }

    /// <summary>
    /// Compliance score computed as: Satisfied / (Assessed - NotApplicable) × 100.
    /// Rounded to 2 decimal places.
    /// </summary>
    public double ComplianceScore { get; set; }

    /// <summary>Overall determination: "Authorized", "Denied", "Conditional".</summary>
    [MaxLength(50)]
    public string? OverallDetermination { get; set; }

    /// <summary>SCA user ID who performed the assessment.</summary>
    [Required]
    public string AssessorId { get; set; } = string.Empty;

    /// <summary>SCA display name.</summary>
    [Required]
    [MaxLength(200)]
    public string AssessorName { get; set; } = string.Empty;

    /// <summary>UTC timestamp of assessment completion.</summary>
    public DateTime AssessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Assessment summary notes.</summary>
    [MaxLength(4000)]
    public string? Notes { get; set; }

    // ─── Navigation Properties ───────────────────────────────────────────────

    /// <summary>Navigation to parent RegisteredSystem.</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }

    /// <summary>Navigation to parent ComplianceAssessment.</summary>
    public ComplianceAssessment? ComplianceAssessment { get; set; }
}
