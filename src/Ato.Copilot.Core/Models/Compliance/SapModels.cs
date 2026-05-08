using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

// ═══════════════════════════════════════════════════════════════════════════════
// Feature 018 — Security Assessment Plan (SAP) Generation
// Entities, enum, and DTOs for RMF Step 4 SAP document assembly
// ═══════════════════════════════════════════════════════════════════════════════

// ─── Enum ────────────────────────────────────────────────────────────────────

/// <summary>Status of a Security Assessment Plan.</summary>
public enum SapStatus
{
    /// <summary>SAP is being drafted and can be modified.</summary>
    Draft,

    /// <summary>SAP is locked with SHA-256 integrity hash. No further modifications allowed.</summary>
    Finalized
}

// ─── Entities ────────────────────────────────────────────────────────────────

/// <summary>
/// Security Assessment Plan entity — the mandatory RMF Step 4 deliverable.
/// Tracks system, assessment scope, status (Draft/Finalized), content, and generation metadata.
/// </summary>
/// <remarks>Feature 018. Unique constraint on (RegisteredSystemId, Status) for Draft enforcement.</remarks>
[TenantScoped]
public class SecurityAssessmentPlan
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

    /// <summary>FK to RegisteredSystem.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Optional FK to ComplianceAssessment — links SAP to a specific assessment cycle.</summary>
    [MaxLength(36)]
    public string? AssessmentId { get; set; }

    /// <summary>SAP lifecycle status (Draft or Finalized).</summary>
    [Required]
    public SapStatus Status { get; set; } = SapStatus.Draft;

    /// <summary>SAP document title (e.g., "Security Assessment Plan — ACME System — FY26 Q2").</summary>
    [Required]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Baseline level at time of SAP generation ("Low", "Moderate", "High").</summary>
    [Required]
    [MaxLength(20)]
    public string BaselineLevel { get; set; } = string.Empty;

    /// <summary>SCA-provided notes on assessment scope, limitations, or special instructions.</summary>
    [MaxLength(4000)]
    public string? ScopeNotes { get; set; }

    /// <summary>Assessment constraints, availability windows, escalation procedures.</summary>
    [MaxLength(4000)]
    public string? RulesOfEngagement { get; set; }

    /// <summary>Planned assessment start date.</summary>
    public DateTime? ScheduleStart { get; set; }

    /// <summary>Planned assessment end date.</summary>
    public DateTime? ScheduleEnd { get; set; }

    /// <summary>Full rendered SAP content (Markdown).</summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of Content when finalized — integrity verification.</summary>
    [MaxLength(64)]
    public string? ContentHash { get; set; }

    /// <summary>Total controls in assessment scope.</summary>
    public int TotalControls { get; set; }

    /// <summary>Controls requiring direct assessment (customer responsibility).</summary>
    public int CustomerControls { get; set; }

    /// <summary>Controls assessed by provider (attestation review only).</summary>
    public int InheritedControls { get; set; }

    /// <summary>Shared responsibility controls.</summary>
    public int SharedControls { get; set; }

    /// <summary>Number of STIG benchmarks in testing plan.</summary>
    public int StigBenchmarkCount { get; set; }

    /// <summary>User who generated the SAP.</summary>
    [Required]
    [MaxLength(200)]
    public string GeneratedBy { get; set; } = string.Empty;

    /// <summary>SAP generation timestamp (UTC).</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who finalized the SAP.</summary>
    [MaxLength(200)]
    public string? FinalizedBy { get; set; }

    /// <summary>Finalization timestamp (UTC).</summary>
    public DateTime? FinalizedAt { get; set; }

    /// <summary>Output format of the SAP ("markdown", "docx", "pdf").</summary>
    [Required]
    [MaxLength(20)]
    public string Format { get; set; } = "markdown";

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent registered system.</summary>
    public RegisteredSystem RegisteredSystem { get; set; } = null!;

    /// <summary>Optional linked assessment cycle.</summary>
    public ComplianceAssessment? ComplianceAssessment { get; set; }

    /// <summary>Per-control assessment plan entries.</summary>
    public ICollection<SapControlEntry> ControlEntries { get; set; } = new List<SapControlEntry>();

    /// <summary>Assessment team members.</summary>
    public ICollection<SapTeamMember> TeamMembers { get; set; } = new List<SapTeamMember>();
}

/// <summary>
/// Per-control assessment plan entry within a SAP.
/// Stores control metadata, assessment objectives, methods, evidence requirements, and STIG coverage.
/// </summary>
/// <remarks>Feature 018. Unique constraint on (SecurityAssessmentPlanId, ControlId).</remarks>
[TenantScoped]
public class SapControlEntry
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

    /// <summary>FK to SecurityAssessmentPlan.</summary>
    [Required]
    [MaxLength(36)]
    public string SecurityAssessmentPlanId { get; set; } = string.Empty;

    /// <summary>NIST 800-53 control ID (e.g., "AC-2").</summary>
    [Required]
    [MaxLength(20)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Control title from OSCAL catalog.</summary>
    [Required]
    [MaxLength(500)]
    public string ControlTitle { get; set; } = string.Empty;

    /// <summary>Control family name (e.g., "Access Control").</summary>
    [Required]
    [MaxLength(100)]
    public string ControlFamily { get; set; } = string.Empty;

    /// <summary>Control inheritance designation (Customer, Inherited, or Shared).</summary>
    [Required]
    public InheritanceType InheritanceType { get; set; } = InheritanceType.Customer;

    /// <summary>CSP or provider name if inherited/shared.</summary>
    [MaxLength(200)]
    public string? Provider { get; set; }

    /// <summary>Assessment methods for this control (JSON array: "Examine", "Interview", "Test").</summary>
    public List<string> AssessmentMethods { get; set; } = new() { "Examine", "Interview", "Test" };

    /// <summary>OSCAL-derived assessment objective prose strings (JSON array).</summary>
    public List<string> AssessmentObjectives { get; set; } = new();

    /// <summary>Expected evidence artifacts per method (JSON array).</summary>
    public List<string> EvidenceRequirements { get; set; } = new();

    /// <summary>STIG benchmark IDs covering this control (JSON array).</summary>
    public List<string> StigBenchmarks { get; set; } = new();

    /// <summary>Number of evidence artifacts expected (derived from method count).</summary>
    public int EvidenceExpected { get; set; }

    /// <summary>Number of ComplianceEvidence records already collected for this control.</summary>
    public int EvidenceCollected { get; set; }

    /// <summary>Whether the SCA overrode the default assessment methods.</summary>
    public bool IsMethodOverridden { get; set; }

    /// <summary>Justification for method override.</summary>
    [MaxLength(2000)]
    public string? OverrideRationale { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent Security Assessment Plan.</summary>
    public SecurityAssessmentPlan SecurityAssessmentPlan { get; set; } = null!;
}

/// <summary>
/// Assessment team member within a SAP.
/// Stores name, organization, role (Lead Assessor / Assessor / Technical SME), and contact info.
/// </summary>
/// <remarks>Feature 018. Cascade-deleted when parent SAP is removed.</remarks>
[TenantScoped]
public class SapTeamMember
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

    /// <summary>FK to SecurityAssessmentPlan.</summary>
    [Required]
    [MaxLength(36)]
    public string SecurityAssessmentPlanId { get; set; } = string.Empty;

    /// <summary>Team member full name.</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Organization or company name.</summary>
    [Required]
    [MaxLength(200)]
    public string Organization { get; set; } = string.Empty;

    /// <summary>Assessment role: "Lead Assessor", "Assessor", or "Technical SME".</summary>
    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = string.Empty;

    /// <summary>Email, phone, or other contact info.</summary>
    [MaxLength(500)]
    public string? ContactInfo { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent Security Assessment Plan.</summary>
    public SecurityAssessmentPlan SecurityAssessmentPlan { get; set; } = null!;
}

// ─── DTOs (Not Persisted) ────────────────────────────────────────────────────

/// <summary>Input for per-control method override when generating or updating a SAP.</summary>
public record SapMethodOverrideInput(
    string ControlId,
    List<string> Methods,
    string? Rationale = null);

/// <summary>Input for assessment team member.</summary>
public record SapTeamMemberInput(
    string Name,
    string Organization,
    string Role,
    string? ContactInfo = null);

/// <summary>Input for SAP generation with optional overrides.</summary>
public record SapGenerationInput(
    string SystemId,
    string? AssessmentId = null,
    DateTime? ScheduleStart = null,
    DateTime? ScheduleEnd = null,
    string? ScopeNotes = null,
    string? RulesOfEngagement = null,
    List<SapTeamMemberInput>? TeamMembers = null,
    List<SapMethodOverrideInput>? MethodOverrides = null,
    string Format = "markdown");

/// <summary>Input for SAP update (draft only).</summary>
public record SapUpdateInput(
    string SapId,
    DateTime? ScheduleStart = null,
    DateTime? ScheduleEnd = null,
    string? ScopeNotes = null,
    string? RulesOfEngagement = null,
    List<SapTeamMemberInput>? TeamMembers = null,
    List<SapMethodOverrideInput>? MethodOverrides = null);

/// <summary>Result of SAP generation or retrieval.</summary>
public class SapDocument
{
    /// <summary>SAP entity ID.</summary>
    public string SapId { get; set; } = string.Empty;

    /// <summary>System ID the SAP belongs to.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>Optional linked assessment ID.</summary>
    public string? AssessmentId { get; set; }

    /// <summary>SAP document title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>SAP status (Draft or Finalized).</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>Output format (markdown, docx, pdf).</summary>
    public string Format { get; set; } = "markdown";

    /// <summary>Baseline level at SAP generation time.</summary>
    public string BaselineLevel { get; set; } = string.Empty;

    /// <summary>Full rendered SAP content (Markdown) or null for binary formats.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>SHA-256 content hash (Finalized SAPs only).</summary>
    public string? ContentHash { get; set; }

    /// <summary>Total controls in assessment scope.</summary>
    public int TotalControls { get; set; }

    /// <summary>Customer responsibility controls.</summary>
    public int CustomerControls { get; set; }

    /// <summary>Provider-assessed inherited controls.</summary>
    public int InheritedControls { get; set; }

    /// <summary>Shared responsibility controls.</summary>
    public int SharedControls { get; set; }

    /// <summary>Number of STIG benchmarks in testing plan.</summary>
    public int StigBenchmarkCount { get; set; }

    /// <summary>Number of controls with OSCAL assessment objectives.</summary>
    public int ControlsWithObjectives { get; set; }

    /// <summary>Number of controls with evidence gaps (collected &lt; expected).</summary>
    public int EvidenceGaps { get; set; }

    /// <summary>Per-family summary breakdown.</summary>
    public List<SapFamilySummary> FamilySummaries { get; set; } = new();

    /// <summary>SAP generation timestamp (UTC).</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Finalization timestamp (UTC), null for Draft SAPs.</summary>
    public DateTime? FinalizedAt { get; set; }

    /// <summary>Warnings generated during SAP creation (e.g., "System is not in Assess phase").</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>Per-family summary in SAP response.</summary>
public class SapFamilySummary
{
    /// <summary>Control family name (e.g., "Access Control (AC)").</summary>
    public string Family { get; set; } = string.Empty;

    /// <summary>Total controls in this family.</summary>
    public int ControlCount { get; set; }

    /// <summary>Customer responsibility controls in this family.</summary>
    public int CustomerCount { get; set; }

    /// <summary>Inherited controls in this family.</summary>
    public int InheritedCount { get; set; }

    /// <summary>Assessment methods used across controls in this family.</summary>
    public List<string> Methods { get; set; } = new();
}

/// <summary>SAP completeness validation result.</summary>
public class SapValidationResult
{
    /// <summary>True if SAP passes all completeness checks.</summary>
    public bool IsComplete { get; set; }

    /// <summary>Validation warnings for incomplete sections.</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Number of baseline controls with SapControlEntry records.</summary>
    public int ControlsCovered { get; set; }

    /// <summary>Number of controls missing OSCAL assessment objectives.</summary>
    public int ControlsMissingObjectives { get; set; }

    /// <summary>Number of controls with no assessment method assigned.</summary>
    public int ControlsMissingMethods { get; set; }

    /// <summary>True if at least one team member is assigned.</summary>
    public bool HasTeam { get; set; }

    /// <summary>True if schedule start and end dates are set.</summary>
    public bool HasSchedule { get; set; }
}

/// <summary>SAP-to-SAR alignment result — cross-references planned controls with assessment findings.</summary>
public class SapSarAlignmentResult
{
    /// <summary>SAP ID that was checked.</summary>
    public string SapId { get; set; } = string.Empty;

    /// <summary>Assessment ID that was cross-referenced.</summary>
    public string AssessmentId { get; set; } = string.Empty;

    /// <summary>Controls planned in the SAP that have findings in the assessment.</summary>
    public int PlannedAndAssessed { get; set; }

    /// <summary>Controls planned in the SAP but with no findings in the assessment.</summary>
    public List<string> PlannedButUnassessed { get; set; } = new();

    /// <summary>Controls with findings in the assessment but not planned in the SAP.</summary>
    public List<string> AssessedButUnplanned { get; set; } = new();

    /// <summary>True if all planned controls have been assessed.</summary>
    public bool IsFullyAligned { get; set; }
}
