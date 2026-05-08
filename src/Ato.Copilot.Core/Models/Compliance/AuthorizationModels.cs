using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Poam;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

// ═══════════════════════════════════════════════════════════════════════════════
// Authorization & POA&M Entities (Feature 015 — US8)
// Authorization decisions, risk acceptance, POA&M items, and milestones.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Records an Authorizing Official's authorization decision for a registered system.
/// Decision types: ATO, ATOwC, IATT, DATO per DoDI 8510.01.
/// RBAC: Compliance.AuthorizingOfficial only.
/// </summary>
[TenantScoped]
public class AuthorizationDecision
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK → RegisteredSystem this decision covers.</summary>
    [Required]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Type of authorization decision (ATO, ATOwC, IATT, DATO).</summary>
    public AuthorizationDecisionType DecisionType { get; set; }

    /// <summary>UTC timestamp when the decision was issued.</summary>
    public DateTime DecisionDate { get; set; } = DateTime.UtcNow;

    /// <summary>UTC expiration date. Null for DATO.</summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>Terms, conditions, and constraints of the authorization.</summary>
    [MaxLength(8000)]
    public string? TermsAndConditions { get; set; }

    /// <summary>Residual risk level at time of decision.</summary>
    public ComplianceRiskLevel ResidualRiskLevel { get; set; }

    /// <summary>Justification for accepting the residual risk level.</summary>
    [MaxLength(4000)]
    public string? ResidualRiskJustification { get; set; }

    /// <summary>Compliance score at the time of authorization.</summary>
    public double ComplianceScoreAtDecision { get; set; }

    /// <summary>JSON snapshot of finding counts at decision time: { catI: n, catII: n, catIII: n }.</summary>
    public string FindingsAtDecision { get; set; } = "{}";

    /// <summary>AO user ID who issued the decision.</summary>
    [Required]
    public string IssuedBy { get; set; } = string.Empty;

    /// <summary>AO display name.</summary>
    [Required]
    [MaxLength(200)]
    public string IssuedByName { get; set; } = string.Empty;

    /// <summary>True for the current active authorization; false when superseded or expired.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>FK → self: reference to the decision that supersedes this one.</summary>
    public string? SupersededById { get; set; }

    // ─── Navigation Properties ───────────────────────────────────────────────

    /// <summary>Navigation to parent RegisteredSystem.</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }

    /// <summary>Navigation to superseding decision.</summary>
    public AuthorizationDecision? SupersededBy { get; set; }

    /// <summary>Risk acceptances associated with this decision.</summary>
    public List<RiskAcceptance> RiskAcceptances { get; set; } = new();
}

/// <summary>
/// Records an AO's formal acceptance of risk for a specific finding.
/// Risk acceptances have expiration dates and can be revoked.
/// RBAC: Compliance.AuthorizingOfficial only.
/// </summary>
[TenantScoped]
public class RiskAcceptance
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK → AuthorizationDecision this acceptance belongs to.</summary>
    [Required]
    public string AuthorizationDecisionId { get; set; } = string.Empty;

    /// <summary>FK → ComplianceFinding being accepted.</summary>
    [Required]
    public string FindingId { get; set; } = string.Empty;

    /// <summary>NIST 800-53 control ID associated with the finding.</summary>
    [Required]
    [MaxLength(20)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>CAT severity of the accepted finding.</summary>
    public CatSeverity CatSeverity { get; set; }

    /// <summary>Justification for accepting the risk.</summary>
    [Required]
    [MaxLength(4000)]
    public string Justification { get; set; } = string.Empty;

    /// <summary>Compensating control description, if any.</summary>
    [MaxLength(2000)]
    public string? CompensatingControl { get; set; }

    /// <summary>UTC expiration date for auto-revert.</summary>
    public DateTime ExpirationDate { get; set; }

    /// <summary>AO user ID who accepted the risk.</summary>
    [Required]
    public string AcceptedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp when risk was accepted.</summary>
    public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True when active; false when expired or revoked.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC timestamp when revoked (null if still active or expired naturally).</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>User ID who revoked the acceptance.</summary>
    public string? RevokedBy { get; set; }

    /// <summary>Reason for revocation.</summary>
    [MaxLength(1000)]
    public string? RevocationReason { get; set; }

    // ─── Navigation Properties ───────────────────────────────────────────────

    /// <summary>Navigation to parent AuthorizationDecision.</summary>
    public AuthorizationDecision? AuthorizationDecision { get; set; }

    /// <summary>Navigation to the accepted ComplianceFinding.</summary>
    public ComplianceFinding? Finding { get; set; }
}

/// <summary>
/// A Plan of Action and Milestones (POA&amp;M) item tracking a weakness
/// and remediation plan, linked to a ComplianceFinding and optionally to a Kanban RemediationTask.
/// </summary>
[TenantScoped]
public class PoamItem : ConcurrentEntity
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK → RegisteredSystem this POA&amp;M belongs to.</summary>
    [Required]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>FK → ComplianceFinding (optional link to originating finding).</summary>
    public string? FindingId { get; set; }

    /// <summary>FK → RemediationTask (optional link to Kanban task).</summary>
    public string? RemediationTaskId { get; set; }

    /// <summary>Weakness description.</summary>
    [Required]
    [MaxLength(2000)]
    public string Weakness { get; set; } = string.Empty;

    /// <summary>Source of the weakness: "ACAS", "STIG", "SCA Assessment", "Manual".</summary>
    [Required]
    [MaxLength(100)]
    public string WeaknessSource { get; set; } = string.Empty;

    /// <summary>NIST 800-53 control ID affected.</summary>
    [Required]
    [MaxLength(20)]
    public string SecurityControlNumber { get; set; } = string.Empty;

    /// <summary>CAT severity of the weakness.</summary>
    public CatSeverity CatSeverity { get; set; }

    /// <summary>Point of contact responsible for remediation.</summary>
    [Required]
    [MaxLength(200)]
    public string PointOfContact { get; set; } = string.Empty;

    /// <summary>POC email address.</summary>
    [MaxLength(200)]
    public string? PocEmail { get; set; }

    /// <summary>Resources needed for remediation.</summary>
    [MaxLength(1000)]
    public string? ResourcesRequired { get; set; }

    /// <summary>Estimated cost for remediation.</summary>
    public decimal? CostEstimate { get; set; }

    /// <summary>Target fix date.</summary>
    public DateTime ScheduledCompletionDate { get; set; }

    /// <summary>Actual completion date (null if still open).</summary>
    public DateTime? ActualCompletionDate { get; set; }

    /// <summary>Current POA&amp;M lifecycle status.</summary>
    public PoamStatus Status { get; set; } = PoamStatus.Ongoing;

    /// <summary>Additional comments or notes.</summary>
    [MaxLength(4000)]
    public string? Comments { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC last modification timestamp.</summary>
    public DateTime? ModifiedAt { get; set; }

    // ─── Navigation Properties ───────────────────────────────────────────────

    /// <summary>Navigation to parent RegisteredSystem.</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }

    /// <summary>Navigation to linked ComplianceFinding.</summary>
    public ComplianceFinding? Finding { get; set; }

    /// <summary>Milestones for this POA&amp;M item.</summary>
    public List<PoamMilestone> Milestones { get; set; } = new();

    // ─── New Properties (Feature 035 — Deviation Management) ──────────────

    /// <summary>FK → Deviation (optional link to active deviation record).</summary>
    public string? DeviationId { get; set; }

    // ─── New Properties (Feature 039 — POA&M Management) ──────────────────

    /// <summary>Actor who created this POA&amp;M item.</summary>
    [MaxLength(200)]
    public string? CreatedBy { get; set; }

    /// <summary>Actor who last modified this POA&amp;M item.</summary>
    [MaxLength(200)]
    public string? ModifiedBy { get; set; }

    /// <summary>External ticket reference (e.g., JIRA-123).</summary>
    [MaxLength(200)]
    public string? ExternalTicketRef { get; set; }

    /// <summary>Linked system components (many-to-many via junction).</summary>
    public List<PoamComponentLink> ComponentLinks { get; set; } = new();

    /// <summary>Audit trail history entries.</summary>
    public List<PoamHistoryEntry> History { get; set; } = new();
}

/// <summary>
/// A milestone within a POA&amp;M item tracking incremental progress toward remediation.
/// </summary>
[TenantScoped]
public class PoamMilestone
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK → parent PoamItem.</summary>
    [Required]
    public string PoamItemId { get; set; } = string.Empty;

    /// <summary>Milestone description.</summary>
    [Required]
    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Target completion date.</summary>
    public DateTime TargetDate { get; set; }

    /// <summary>Actual completion date (null if incomplete).</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>Order within the parent POA&amp;M item.</summary>
    public int Sequence { get; set; }

    // ─── Computed Properties ─────────────────────────────────────────────────

    /// <summary>True if past target date and not yet completed.</summary>
    public bool IsOverdue => TargetDate < DateTime.UtcNow && CompletedDate == null;

    // ─── Navigation Properties ───────────────────────────────────────────────

    /// <summary>Navigation to parent PoamItem.</summary>
    public PoamItem? PoamItem { get; set; }
}
