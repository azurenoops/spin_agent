using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

// ═══════════════════════════════════════════════════════════════════════════════
// Continuous Monitoring Entities (Feature 015 — Phase 4 / US9)
//
// ConMonPlan        – formal monitoring plan with assessment frequency
// ConMonReport      – periodic monitoring report with compliance score
// SignificantChange – system changes that may trigger reauthorization
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Formal Continuous Monitoring (ConMon) plan for a registered system.
/// One plan per system (enforced by unique constraint on RegisteredSystemId).
/// Spec §4.1.
/// </summary>
[TenantScoped]
public class ConMonPlan
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Primary key — GUID.</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the monitored system. One plan per system.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Assessment frequency: Monthly, Quarterly, Annually.</summary>
    [Required]
    [MaxLength(50)]
    public string AssessmentFrequency { get; set; } = string.Empty;

    /// <summary>Anniversary date for the annual security review.</summary>
    public DateTime AnnualReviewDate { get; set; }

    /// <summary>User IDs or role names that receive reports (JSON column).</summary>
    public List<string> ReportDistribution { get; set; } = new();

    /// <summary>What constitutes a significant change for this system (JSON column).</summary>
    public List<string> SignificantChangeTriggers { get; set; } = new();

    /// <summary>User who created the plan.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last modification timestamp (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────
    /// <summary>The system being monitored.</summary>
    [ForeignKey(nameof(RegisteredSystemId))]
    public RegisteredSystem? RegisteredSystem { get; set; }

    /// <summary>Reports generated under this plan.</summary>
    public ICollection<ConMonReport> Reports { get; set; } = new List<ConMonReport>();

    /// <summary>Significant changes detected under this plan.</summary>
    public ICollection<SignificantChange> SignificantChanges { get; set; } = new List<SignificantChange>();
}

/// <summary>
/// Periodic continuous monitoring report (monthly/quarterly/annual).
/// Contains compliance score delta vs. authorized baseline, findings, POA&amp;M status.
/// Spec §4.2.
/// </summary>
[TenantScoped]
public class ConMonReport
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Primary key — GUID.</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the ConMon plan.</summary>
    [Required]
    [MaxLength(36)]
    public string ConMonPlanId { get; set; } = string.Empty;

    /// <summary>FK to the registered system.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Report period identifier, e.g. "2026-02", "2026-Q1".</summary>
    [Required]
    [MaxLength(50)]
    public string ReportPeriod { get; set; } = string.Empty;

    /// <summary>Report type: Monthly, Quarterly, Annual.</summary>
    [Required]
    [MaxLength(20)]
    public string ReportType { get; set; } = string.Empty;

    /// <summary>Current compliance score at time of report generation.</summary>
    public double ComplianceScore { get; set; }

    /// <summary>Score at authorization time — used for delta calculation. Null if no authorization.</summary>
    public double? AuthorizedBaselineScore { get; set; }

    /// <summary>Number of findings opened during this period.</summary>
    public int NewFindings { get; set; }

    /// <summary>Number of findings closed during this period.</summary>
    public int ResolvedFindings { get; set; }

    /// <summary>Current open POA&amp;M items at report time.</summary>
    public int OpenPoamItems { get; set; }

    /// <summary>Overdue POA&amp;M items at report time.</summary>
    public int OverduePoamItems { get; set; }

    /// <summary>Generated markdown report content.</summary>
    [MaxLength(50000)]
    public string ReportContent { get; set; } = string.Empty;

    /// <summary>When the report was generated (UTC).</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who generated the report.</summary>
    [Required]
    [MaxLength(200)]
    public string GeneratedBy { get; set; } = string.Empty;

    // ─── Watch Data Enrichment (Phase 17 §9a.3) ─────────────────────────────

    /// <summary>Whether continuous monitoring is enabled for the system's subscriptions.</summary>
    public bool? MonitoringEnabled { get; set; }

    /// <summary>Count of active drift alerts from ComplianceWatchService.</summary>
    public int? DriftAlertCount { get; set; }

    /// <summary>Count of auto-remediation rules configured for the system's subscriptions.</summary>
    public int? AutoRemediationRuleCount { get; set; }

    /// <summary>Timestamp of the last monitoring check from ComplianceWatchService (UTC).</summary>
    public DateTime? LastMonitoringCheck { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────
    /// <summary>The ConMon plan this report belongs to.</summary>
    [ForeignKey(nameof(ConMonPlanId))]
    public ConMonPlan? ConMonPlan { get; set; }

    /// <summary>The registered system.</summary>
    [ForeignKey(nameof(RegisteredSystemId))]
    public RegisteredSystem? RegisteredSystem { get; set; }
}

/// <summary>
/// A significant change to a system that may trigger reauthorization.
/// Spec §4.4.
/// </summary>
[TenantScoped]
public class SignificantChange
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Primary key — GUID.</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the registered system.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Change category, e.g. "New Interconnection", "Major Upgrade".</summary>
    [Required]
    [MaxLength(100)]
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>Detailed description of the change.</summary>
    [Required]
    [MaxLength(4000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>When the change was detected or reported (UTC).</summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who detected/reported the change — "System" or user ID.</summary>
    [Required]
    [MaxLength(200)]
    public string DetectedBy { get; set; } = string.Empty;

    /// <summary>Whether this change requires reauthorization.</summary>
    public bool RequiresReauthorization { get; set; }

    /// <summary>Whether reauthorization has been triggered.</summary>
    public bool ReauthorizationTriggered { get; set; }

    /// <summary>ISSM who reviewed the change.</summary>
    [MaxLength(200)]
    public string? ReviewedBy { get; set; }

    /// <summary>When the change was reviewed (UTC).</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Review outcome / disposition.</summary>
    [MaxLength(2000)]
    public string? Disposition { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────
    /// <summary>The registered system.</summary>
    [ForeignKey(nameof(RegisteredSystemId))]
    public RegisteredSystem? RegisteredSystem { get; set; }
}
