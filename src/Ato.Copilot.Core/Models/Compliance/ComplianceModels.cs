using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

// ───────────────────────────────────────────── Enums ─────────────────────────────────────────────

/// <summary>
/// Severity level of a compliance finding, ordered from most to least critical.
/// </summary>
public enum FindingSeverity
{
    /// <summary>Immediate action required — active exploitation or total control failure.</summary>
    Critical,
    /// <summary>Significant risk — must be addressed within remediation window.</summary>
    High,
    /// <summary>Moderate risk — should be addressed in next remediation cycle.</summary>
    Medium,
    /// <summary>Minor risk — address when convenient.</summary>
    Low,
    /// <summary>No risk — informational observation only.</summary>
    Informational
}

/// <summary>
/// Status of a compliance finding through its lifecycle.
/// </summary>
public enum FindingStatus
{
    /// <summary>Finding is open and unaddressed.</summary>
    Open,
    /// <summary>Remediation is underway.</summary>
    InProgress,
    /// <summary>Finding has been remediated and validated.</summary>
    Remediated,
    /// <summary>Finding accepted as-is with documented risk acceptance.</summary>
    Accepted,
    /// <summary>Finding determined to be a false positive.</summary>
    FalsePositive
}

/// <summary>
/// Assessment lifecycle state. See data-model.md for state transition diagram.
/// Transitions: Pending → InProgress → Completed | Failed | Cancelled
/// </summary>
public enum AssessmentStatus
{
    /// <summary>Assessment created but not yet started.</summary>
    Pending,
    /// <summary>Assessment is currently running scans.</summary>
    InProgress,
    /// <summary>Assessment completed successfully.</summary>
    Completed,
    /// <summary>Assessment failed due to an error.</summary>
    Failed,
    /// <summary>Assessment was cancelled by user or timeout.</summary>
    Cancelled
}

/// <summary>
/// Identifies which scan source discovered a finding.
/// </summary>
public enum ScanSourceType
{
    /// <summary>Azure Resource Graph query.</summary>
    Resource,
    /// <summary>Azure Policy compliance state.</summary>
    Policy,
    /// <summary>Microsoft Defender for Cloud recommendation.</summary>
    Defender,
    /// <summary>Correlated from multiple scan sources.</summary>
    Combined,
    /// <summary>Cloud security posture management scan (e.g., Prisma Cloud CSPM).</summary>
    Cloud
}

/// <summary>
/// Type of remediation action required for a finding.
/// </summary>
public enum RemediationType
{
    /// <summary>Remediation type not yet determined.</summary>
    Unknown,
    /// <summary>Direct resource configuration change (e.g., enable encryption).</summary>
    ResourceConfiguration,
    /// <summary>Azure Policy assignment or enforcement mode change.</summary>
    PolicyAssignment,
    /// <summary>Azure Policy remediation task (deploy-if-not-exists).</summary>
    PolicyRemediation,
    /// <summary>Requires manual intervention — no automated fix available.</summary>
    Manual
}

/// <summary>
/// Risk level for a finding's control family. AC, IA, SC are high-risk
/// because changes can impact user access and security boundaries.
/// </summary>
public enum RiskLevel
{
    /// <summary>Standard risk — normal remediation workflow.</summary>
    Standard,
    /// <summary>High risk (AC, IA, SC families) — requires additional approval.</summary>
    High
}

// ─────────────────────────────────── Compliance Watch Enums ──────────────────────────────────────

/// <summary>
/// Status of a compliance alert through its lifecycle.
/// Valid transitions defined in data-model.md state machine.
/// </summary>
public enum AlertStatus
{
    /// <summary>Alert just created, unacknowledged.</summary>
    New,
    /// <summary>Alert seen and acknowledged by a user.</summary>
    Acknowledged,
    /// <summary>Remediation is underway for this alert.</summary>
    InProgress,
    /// <summary>Alert has been resolved (auto or manual).</summary>
    Resolved,
    /// <summary>Alert dismissed by Compliance Officer (requires justification).</summary>
    Dismissed,
    /// <summary>Alert escalated due to SLA expiry.</summary>
    Escalated
}

/// <summary>
/// Type of compliance alert detected by the monitoring engine.
/// </summary>
public enum AlertType
{
    /// <summary>Resource configuration deviated from baseline.</summary>
    Drift,
    /// <summary>New resource found non-compliant.</summary>
    Violation,
    /// <summary>Compliance score dropped below threshold.</summary>
    Degradation,
    /// <summary>Unusual pattern detected (actor correlation).</summary>
    Anomaly,
    /// <summary>SLA expired, escalation triggered.</summary>
    Escalation,
    /// <summary>Auto-remediation applied successfully.</summary>
    Resolution
}

/// <summary>
/// Severity of a compliance alert, determining SLA deadlines.
/// </summary>
public enum AlertSeverity
{
    /// <summary>SLA &lt; 1 hour.</summary>
    Critical,
    /// <summary>SLA &lt; 4 hours.</summary>
    High,
    /// <summary>SLA &lt; 24 hours.</summary>
    Medium,
    /// <summary>SLA &lt; 7 days.</summary>
    Low
}

/// <summary>
/// Monitoring check frequency for scheduled compliance monitoring.
/// </summary>
public enum MonitoringFrequency
{
    /// <summary>Check every 15 minutes.</summary>
    FifteenMinutes,
    /// <summary>Check every hour.</summary>
    Hourly,
    /// <summary>Check once per day.</summary>
    Daily,
    /// <summary>Check once per week.</summary>
    Weekly
}

/// <summary>
/// Monitoring mode for compliance watch configurations.
/// </summary>
public enum MonitoringMode
{
    /// <summary>Periodic timer-based checks only.</summary>
    Scheduled,
    /// <summary>Triggered by platform events only.</summary>
    EventDriven,
    /// <summary>Combined scheduled + event-driven monitoring.</summary>
    Both
}

/// <summary>
/// Channel for delivering alert notifications.
/// </summary>
public enum NotificationChannel
{
    /// <summary>In-app chat notification (always enabled).</summary>
    Chat,
    /// <summary>Email notification (configurable).</summary>
    Email,
    /// <summary>Webhook POST notification (configurable).</summary>
    Webhook
}

/// <summary>
/// Type of alert suppression rule.
/// </summary>
public enum SuppressionType
{
    /// <summary>Auto-expires after configured duration.</summary>
    Temporary,
    /// <summary>Permanent suppression — requires justification, visible to auditors.</summary>
    Permanent
}

/// <summary>
/// Category of compliance evidence artifact.
/// </summary>
public enum EvidenceCategory
{
    /// <summary>Resource configuration export.</summary>
    Configuration,
    /// <summary>Azure Policy compliance snapshot.</summary>
    PolicyCompliance,
    /// <summary>Azure Resource Graph resource state.</summary>
    ResourceCompliance,
    /// <summary>Defender for Cloud security assessment.</summary>
    SecurityAssessment,
    /// <summary>Azure Activity Log entries.</summary>
    ActivityLog,
    /// <summary>Resource inventory listing.</summary>
    Inventory
}

/// <summary>
/// Remediation plan lifecycle state.
/// Transitions: Planned → Approved → InProgress → Completed | PartiallyCompleted | Failed
///              Planned → Rejected
/// </summary>
public enum RemediationStatus
{
    /// <summary>Plan created but not yet approved.</summary>
    Planned,
    /// <summary>Plan approved by ComplianceOfficer.</summary>
    Approved,
    /// <summary>Remediation steps are executing.</summary>
    InProgress,
    /// <summary>All steps completed successfully.</summary>
    Completed,
    /// <summary>Some steps succeeded, others failed.</summary>
    PartiallyCompleted,
    /// <summary>Remediation failed (see FailureReason).</summary>
    Failed,
    /// <summary>Plan rejected by ComplianceOfficer.</summary>
    Rejected
}

/// <summary>
/// Individual remediation step execution state.
/// Transitions: Pending → InProgress → Completed | Failed
///              Pending → Skipped
/// </summary>
public enum StepStatus
{
    /// <summary>Step not yet started.</summary>
    Pending,
    /// <summary>Step currently executing.</summary>
    InProgress,
    /// <summary>Step completed successfully.</summary>
    Completed,
    /// <summary>Step failed during execution.</summary>
    Failed,
    /// <summary>Step skipped (e.g., batch stop-on-failure).</summary>
    Skipped
}

/// <summary>
/// Outcome of an audited action, captured in AuditLogEntry.
/// </summary>
public enum AuditOutcome
{
    /// <summary>Action completed successfully.</summary>
    Success,
    /// <summary>Action failed with an error.</summary>
    Failure,
    /// <summary>Action partially succeeded (e.g., partial scan).</summary>
    Partial,
    /// <summary>Action denied by RBAC authorization.</summary>
    Denied
}

// ────────────────────────────── Compliance Engine Enums ──────────────────────────────────────────

/// <summary>
/// Risk level for risk profiles and assessments. Provides four-tier granularity
/// (distinct from <see cref="RiskLevel"/> which only has Standard/High).
/// </summary>
public enum ComplianceRiskLevel
{
    /// <summary>Risk score &lt; 20. Minimal remediation needed.</summary>
    Low,
    /// <summary>Risk score ≥ 20 and &lt; 50. Some findings require attention.</summary>
    Medium,
    /// <summary>Risk score ≥ 50 and &lt; 100. Significant findings requiring prompt action.</summary>
    High,
    /// <summary>Risk score ≥ 100. Immediate remediation required.</summary>
    Critical
}

/// <summary>
/// Status of a per-family scan result within a comprehensive assessment.
/// </summary>
public enum FamilyAssessmentStatus
{
    /// <summary>Family scan has not started.</summary>
    Pending,
    /// <summary>Family scan completed successfully.</summary>
    Completed,
    /// <summary>Family scan failed due to an error.</summary>
    Failed,
    /// <summary>Family scan was skipped (e.g., cancellation).</summary>
    Skipped
}

/// <summary>
/// Evidence artifact category for evidence collection packages
/// (distinct from <see cref="EvidenceCategory"/> which covers storage-level categories).
/// </summary>
public enum EvidenceType
{
    /// <summary>Resource configuration export (e.g., ARM template snapshot).</summary>
    Configuration,
    /// <summary>Log evidence (e.g., activity logs, diagnostic logs).</summary>
    Log,
    /// <summary>Metric evidence (e.g., Azure Monitor metrics).</summary>
    Metric,
    /// <summary>Policy evidence (e.g., Azure Policy compliance state).</summary>
    Policy,
    /// <summary>Access control evidence (e.g., RBAC assignments, conditional access).</summary>
    AccessControl
}

/// <summary>
/// Significant event categories detected in compliance timelines.
/// </summary>
public enum TimelineEventType
{
    /// <summary>Compliance score improved significantly (≥ 10%).</summary>
    ScoreImprovement,
    /// <summary>Compliance score degraded significantly (≥ 10%).</summary>
    ScoreDegradation,
    /// <summary>Spike in finding count (≥ 5 new findings).</summary>
    FindingSpike,
    /// <summary>Significant number of findings resolved.</summary>
    FindingResolution,
    /// <summary>Compliance certificate was issued.</summary>
    CertificateIssued,
    /// <summary>Compliance certificate expired.</summary>
    CertificateExpired,
    /// <summary>Compliance baseline was updated.</summary>
    BaselineChanged,
    /// <summary>Remediation plan completed.</summary>
    RemediationCompleted,
    /// <summary>Configuration drift detected from baseline.</summary>
    DriftDetected,
    /// <summary>Alert escalated due to SLA expiry.</summary>
    AlertEscalated
}

/// <summary>
/// Overall compliance trend direction over a time period.
/// </summary>
public enum TrendDirection
{
    /// <summary>Compliance score trending upward.</summary>
    Improving,
    /// <summary>Compliance score relatively stable.</summary>
    Stable,
    /// <summary>Compliance score trending downward.</summary>
    Degrading
}

/// <summary>
/// Compliance certificate lifecycle status.
/// </summary>
public enum CertificateStatus
{
    /// <summary>Certificate is currently valid.</summary>
    Active,
    /// <summary>Certificate has passed its expiration date.</summary>
    Expired,
    /// <summary>Certificate was manually revoked.</summary>
    Revoked
}

/// <summary>
/// Finding-level remediation tracking status
/// (distinct from plan-level <see cref="RemediationStatus"/>).
/// </summary>
public enum RemediationTrackingStatus
{
    /// <summary>Remediation has not been started.</summary>
    NotStarted,
    /// <summary>Remediation is in progress.</summary>
    InProgress,
    /// <summary>Remediation completed successfully.</summary>
    Completed,
    /// <summary>Finding will not be fixed (documented risk acceptance).</summary>
    WontFix,
    /// <summary>Remediation deferred to a future cycle.</summary>
    Deferred
}

/// <summary>
/// Priority level for remediation items, mapped from finding severity.
/// P0 = fix immediately, P4 = best effort.
/// </summary>
public enum RemediationPriority
{
    /// <summary>Critical findings — fix immediately.</summary>
    P0,
    /// <summary>High findings — fix within 24 hours.</summary>
    P1,
    /// <summary>Medium findings — fix within 7 days.</summary>
    P2,
    /// <summary>Low findings — fix within 30 days.</summary>
    P3,
    /// <summary>Other findings — best effort.</summary>
    P4
}

/// <summary>
/// Lifecycle status of a remediation execution operation.
/// </summary>
public enum RemediationExecutionStatus
{
    /// <summary>Awaiting approval before execution.</summary>
    Pending,
    /// <summary>Approved, awaiting execution.</summary>
    Approved,
    /// <summary>Currently executing remediation.</summary>
    InProgress,
    /// <summary>Remediation completed successfully.</summary>
    Completed,
    /// <summary>Remediation execution failed.</summary>
    Failed,
    /// <summary>Remediation was rolled back after failure.</summary>
    RolledBack,
    /// <summary>Approval was rejected.</summary>
    Rejected,
    /// <summary>Cancelled during batch (FailFast mode).</summary>
    Cancelled,
    /// <summary>Scheduled for future execution.</summary>
    Scheduled
}

/// <summary>
/// Type of remediation script language.
/// </summary>
public enum ScriptType
{
    /// <summary>Azure CLI script.</summary>
    AzureCli,
    /// <summary>PowerShell script.</summary>
    PowerShell,
    /// <summary>Bicep infrastructure-as-code configuration.</summary>
    Bicep,
    /// <summary>Terraform infrastructure-as-code configuration.</summary>
    Terraform
}

// ──────────────────────────── RMF Lifecycle Enums ────────────────────────────────────────────────

/// <summary>
/// The seven phases of the NIST Risk Management Framework (SP 800-37 Rev 2).
/// Defines the lifecycle position of a <see cref="RegisteredSystem"/>.
/// Not to be confused with the <see cref="RmfStep"/> record used for RMF guidance data.
/// </summary>
public enum RmfPhase
{
    /// <summary>Step 0 — Prepare: Establish context, register system, assign roles.</summary>
    Prepare,
    /// <summary>Step 1 — Categorize: FIPS 199 categorization, determine Impact Level.</summary>
    Categorize,
    /// <summary>Step 2 — Select: Choose NIST baseline, apply overlays, tailor controls.</summary>
    Select,
    /// <summary>Step 3 — Implement: Write SSP narratives, implement controls, remediate findings.</summary>
    Implement,
    /// <summary>Step 4 — Assess: SCA evaluates control effectiveness, takes snapshots.</summary>
    Assess,
    /// <summary>Step 5 — Authorize: AO issues ATO/ATOwC/IATT/DATO decision.</summary>
    Authorize,
    /// <summary>Step 6 — Monitor: Continuous monitoring, ConMon reports, reauthorization.</summary>
    Monitor
}

/// <summary>
/// System type classification per DoDI 8510.01.
/// </summary>
public enum SystemType
{
    /// <summary>A discrete set of information resources organized for the collection, processing, maintenance, use, sharing, dissemination, or disposition of information.</summary>
    MajorApplication,
    /// <summary>Collection of computing environments connected by one or more internal networks under the control of a single authority and security policy.</summary>
    Enclave,
    /// <summary>Shared IT service such as email, DNS, or Active Directory.</summary>
    PlatformIt
}

/// <summary>
/// Mission criticality designation for DoD systems.
/// </summary>
public enum MissionCriticality
{
    /// <summary>System directly supports a military mission; loss has immediate operational impact.</summary>
    MissionCritical,
    /// <summary>System supports essential functions; loss causes significant degradation.</summary>
    MissionEssential,
    /// <summary>System supports administrative/business functions; loss is inconvenient but manageable.</summary>
    MissionSupport
}

/// <summary>
/// Azure cloud environment types for system registration.
/// Determines ARM endpoints, authentication endpoints, and service availability.
/// </summary>
public enum AzureCloudEnvironment
{
    /// <summary>Azure Commercial (public cloud).</summary>
    Commercial,
    /// <summary>Azure Government (IL2–IL5, connected).</summary>
    Government,
    /// <summary>Azure Government air-gapped IL5 (disconnected).</summary>
    GovernmentAirGappedIl5,
    /// <summary>Azure Government air-gapped IL6 / classified (disconnected).</summary>
    GovernmentAirGappedIl6
}

/// <summary>
/// FIPS 199 security impact value for confidentiality, integrity, or availability.
/// Integer values support high-water-mark comparison via <c>Math.Max</c>.
/// </summary>
public enum ImpactValue
{
    /// <summary>Limited adverse effect.</summary>
    Low = 0,
    /// <summary>Serious adverse effect.</summary>
    Moderate = 1,
    /// <summary>Severe or catastrophic adverse effect.</summary>
    High = 2
}

/// <summary>
/// RMF role assignments per DoDI 8510.01. Maps to RBAC role constants
/// in <see cref="Constants.ComplianceRoles"/>.
/// </summary>
public enum RmfRole
{
    /// <summary>Authorizing Official — issues ATO decisions.</summary>
    AuthorizingOfficial,
    /// <summary>Information System Security Manager — oversight of ISSO activities.</summary>
    Issm,
    /// <summary>Information System Security Officer — day-to-day security operations.</summary>
    Isso,
    /// <summary>Security Control Assessor — independent assessment of control effectiveness.</summary>
    Sca,
    /// <summary>System Owner — responsible for system implementation (maps to Engineer persona).</summary>
    SystemOwner,
    /// <summary>Mission Owner — provides system-level business context (maps to Mission Owner persona).</summary>
    MissionOwner
}

/// <summary>
/// Control tailoring actions applied to a baseline.
/// </summary>
public enum TailoringAction
{
    /// <summary>Control added to baseline (beyond standard NIST selection).</summary>
    Added,
    /// <summary>Control removed from baseline with documented rationale.</summary>
    Removed
}

/// <summary>
/// Control inheritance type for FedRAMP/DoD shared responsibility.
/// </summary>
public enum InheritanceType
{
    /// <summary>Fully inherited from a cloud service provider or common control provider.</summary>
    Inherited,
    /// <summary>Shared responsibility between provider and customer.</summary>
    Shared,
    /// <summary>Fully customer-implemented (customer is solely responsible).</summary>
    Customer
}

/// <summary>
/// SSP control implementation status per NIST SP 800-53A.
/// </summary>
public enum ImplementationStatus
{
    /// <summary>Control is fully implemented and operational.</summary>
    Implemented,
    /// <summary>Control is partially implemented; some aspects remain incomplete.</summary>
    PartiallyImplemented,
    /// <summary>Control is planned but not yet implemented.</summary>
    Planned,
    /// <summary>Control is not applicable to this system (with documented justification).</summary>
    NotApplicable
}

/// <summary>
/// SCA determination of whether a control satisfies its security requirements.
/// </summary>
public enum EffectivenessDetermination
{
    /// <summary>Control meets all security requirements.</summary>
    Satisfied,
    /// <summary>Control does not fully meet security requirements — findings generated.</summary>
    OtherThanSatisfied
}

/// <summary>
/// DoD CAT (Category) severity levels for findings per DoDI 8510.01.
/// Maps from <see cref="FindingSeverity"/>: Critical/High → CAT I, Medium → CAT II, Low → CAT III.
/// </summary>
public enum CatSeverity
{
    /// <summary>Any finding that directly and immediately results in loss of confidentiality, availability, or integrity.</summary>
    CatI,
    /// <summary>Any finding that has a potential to result in loss of confidentiality, availability, or integrity.</summary>
    CatII,
    /// <summary>Any finding that degrades measures to protect against loss of confidentiality, availability, or integrity.</summary>
    CatIII
}

/// <summary>
/// Authorization decision types issued by the Authorizing Official.
/// </summary>
public enum AuthorizationDecisionType
{
    /// <summary>Authorization to Operate — full authority with expiration date.</summary>
    Ato,
    /// <summary>Authorization to Operate with Conditions — conditional with required mitigations.</summary>
    AtoWithConditions,
    /// <summary>Interim Authorization to Test — limited scope and duration for testing.</summary>
    Iatt,
    /// <summary>Denial of Authorization to Operate — system must not operate.</summary>
    Dato
}

/// <summary>
/// Plan of Action and Milestones item lifecycle status.
/// </summary>
public enum PoamStatus
{
    /// <summary>Remediation is in progress or awaiting action.</summary>
    Ongoing,
    /// <summary>Remediation has been completed and validated.</summary>
    Completed,
    /// <summary>Remediation is behind schedule.</summary>
    Delayed,
    /// <summary>Risk has been formally accepted by the AO in lieu of remediation.</summary>
    RiskAccepted
}

// ──────────────────────── Remediation Options ────────────────────────

/// <summary>
/// Runtime options for a single remediation execution.
/// </summary>
public class RemediationExecutionOptions
{
    /// <summary>Preview only, no changes applied to Azure resources.</summary>
    public bool DryRun { get; set; }

    /// <summary>Enter Pending state before executing, requiring explicit approval.</summary>
    public bool RequireApproval { get; set; }

    /// <summary>Validate remediation success after execution.</summary>
    public bool AutoValidate { get; set; } = true;

    /// <summary>Automatically rollback if post-execution validation fails.</summary>
    public bool AutoRollbackOnFailure { get; set; }

    /// <summary>Attempt AI script generation (Tier 1) before deterministic fallback.</summary>
    public bool UseAiScript { get; set; } = true;
}

/// <summary>
/// Options for batch remediation operations with concurrency control.
/// </summary>
public class BatchRemediationOptions
{
    /// <summary>Maximum number of concurrent remediations (SemaphoreSlim limit).</summary>
    public int MaxConcurrentRemediations { get; set; } = 3;

    /// <summary>Cancel remaining remediations on first failure.</summary>
    public bool FailFast { get; set; }

    /// <summary>Continue batch processing when individual remediations fail.</summary>
    public bool ContinueOnError { get; set; } = true;

    /// <summary>Preview only for entire batch, no changes applied.</summary>
    public bool DryRun { get; set; }
}

/// <summary>
/// Filters for remediation plan generation.
/// </summary>
public class RemediationPlanOptions
{
    /// <summary>Minimum severity to include in the plan (e.g., "Critical", "High").</summary>
    public string? MinSeverity { get; set; }

    /// <summary>Only include findings from these control families (e.g., AC, AU, SC).</summary>
    public List<string>? IncludeFamilies { get; set; }

    /// <summary>Exclude findings from these control families.</summary>
    public List<string>? ExcludeFamilies { get; set; }

    /// <summary>Only include findings that can be automatically remediated.</summary>
    public bool AutomatableOnly { get; set; }

    /// <summary>Group remediation items by Azure resource.</summary>
    public bool GroupByResource { get; set; }
}

// ──────────────────────────────────────── Value Types ────────────────────────────────────────────

/// <summary>
/// Summary statistics for a single scan source (resource or policy).
/// Configured as an EF Core owned type of ComplianceAssessment.
/// </summary>
public class ScanSummary
{
    /// <summary>Number of Azure resources scanned.</summary>
    public int ResourcesScanned { get; set; }

    /// <summary>Number of policies evaluated (policy scan only).</summary>
    public int PoliciesEvaluated { get; set; }

    /// <summary>Number of compliant items found.</summary>
    public int Compliant { get; set; }

    /// <summary>Number of non-compliant items found.</summary>
    public int NonCompliant { get; set; }

    /// <summary>Compliance percentage (0.0 to 100.0).</summary>
    public double CompliancePercentage { get; set; }
}

/// <summary>
/// Additional metadata for generated compliance documents (SSP, SAR, POA&M).
/// Configured as an EF Core owned type of ComplianceDocument.
/// </summary>
public class DocumentMetadata
{
    /// <summary>System description for the document header.</summary>
    public string SystemDescription { get; set; } = string.Empty;

    /// <summary>Authorization boundary description.</summary>
    public string AuthorizationBoundary { get; set; } = string.Empty;

    /// <summary>Date range covered by this document.</summary>
    public string DateRange { get; set; } = string.Empty;

    /// <summary>Name of the person who prepared the document.</summary>
    public string PreparedBy { get; set; } = string.Empty;

    /// <summary>Name of the person who approved the document.</summary>
    public string ApprovedBy { get; set; } = string.Empty;
}

// ──────────────────────────────────────── Entities ───────────────────────────────────────────────

/// <summary>
/// Represents a compliance assessment result containing scan findings and statistics.
/// Extends existing entity with assessment lifecycle, baseline, progress, and scan summaries.
/// </summary>
[TenantScoped]
public class ComplianceAssessment
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique assessment identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Azure subscription ID (must be valid GUID format).</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Compliance framework (NIST80053, FedRAMPHigh, FedRAMPModerate, DoDIL5).</summary>
    public string Framework { get; set; } = "NIST80053";

    /// <summary>FedRAMP baseline level (High, Moderate, Low).</summary>
    public string Baseline { get; set; } = "High";

    /// <summary>Scan type: resource, policy, or combined.</summary>
    public string ScanType { get; set; } = "combined";

    /// <summary>Assessment lifecycle state.</summary>
    public AssessmentStatus Status { get; set; } = AssessmentStatus.Pending;

    /// <summary>User or role who initiated the assessment.</summary>
    public string InitiatedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp when assessment was initiated.</summary>
    public DateTime AssessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when assessment completed (null if still in progress).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Last progress update message for real-time feedback.</summary>
    public string ProgressMessage { get; set; } = string.Empty;

    /// <summary>Overall compliance score (0.0 to 100.0).</summary>
    public double ComplianceScore { get; set; }

    /// <summary>Total number of controls evaluated.</summary>
    public int TotalControls { get; set; }

    /// <summary>Number of controls that passed.</summary>
    public int PassedControls { get; set; }

    /// <summary>Number of controls that failed.</summary>
    public int FailedControls { get; set; }

    /// <summary>Number of controls not assessed (e.g., no data available).</summary>
    public int NotAssessedControls { get; set; }

    /// <summary>Resource Graph scan statistics. EF Core owned type (columns in Assessments table).</summary>
    public ScanSummary? ResourceScanSummary { get; set; }

    /// <summary>Policy compliance scan statistics. EF Core owned type (columns in Assessments table).</summary>
    public ScanSummary? PolicyScanSummary { get; set; }

    /// <summary>Findings discovered during this assessment.</summary>
    public List<ComplianceFinding> Findings { get; set; } = new();

    // ─── New Properties (Feature 008) ────────────────────────────────────────

    /// <summary>Per-family assessment breakdown. Persisted as JSON column.</summary>
    public List<ControlFamilyAssessment> ControlFamilyResults { get; set; } = new();

    /// <summary>Markdown executive summary with score, risk, and finding counts.</summary>
    public string ExecutiveSummary { get; set; } = string.Empty;

    /// <summary>Severity-weighted risk profile. Persisted as JSON column.</summary>
    public RiskProfile? RiskProfile { get; set; }

    /// <summary>Environment identifier for multi-subscription assessments (e.g., "Production").</summary>
    public string? EnvironmentName { get; set; }

    /// <summary>All subscription IDs assessed (single or multi). Persisted as JSON column.</summary>
    public List<string> SubscriptionIds { get; set; } = new();

    /// <summary>Resource group constraint (null = full subscription scope).</summary>
    public string? ResourceGroupFilter { get; set; }

    /// <summary>Total wall-clock assessment time.</summary>
    public TimeSpan? AssessmentDuration { get; set; }

    /// <summary>Per-pillar success/failure (ARM, Policy, Defender). Persisted as JSON column.</summary>
    public Dictionary<string, bool> ScanPillarResults { get; set; } = new();

    // ─── New Properties (Feature 015 — US7: Assessment Artifacts) ─────────

    /// <summary>Optional FK → RegisteredSystem (nullable for backward compat with pre-RMF assessments).</summary>
    public string? RegisteredSystemId { get; set; }

    /// <summary>Navigation to RegisteredSystem (if linked).</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }
}

/// <summary>
/// Represents a compliance finding (violation or observation) linked to a specific
/// NIST 800-53 control and Azure resource.
/// </summary>
[TenantScoped]
public class ComplianceFinding
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique finding identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>NIST control ID (e.g., "AC-2", "AC-2.1"). Must match ^[A-Z]{2}-\d+(\.\d+)?$.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Two-letter NIST control family abbreviation (e.g., "AC", "AU").</summary>
    public string ControlFamily { get; set; } = string.Empty;

    /// <summary>Brief human-readable title of the finding.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Detailed description of the compliance gap.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Finding severity level.</summary>
    public FindingSeverity Severity { get; set; }

    /// <summary>Current finding lifecycle status.</summary>
    public FindingStatus Status { get; set; }

    /// <summary>Azure resource ID affected by this finding.</summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>Azure resource type (e.g., "Microsoft.Storage/storageAccounts").</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>Human-readable remediation guidance.</summary>
    public string RemediationGuidance { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the finding was discovered.</summary>
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    /// <summary>PowerShell or Azure CLI remediation script, if available.</summary>
    public string? RemediationScript { get; set; }

    /// <summary>Whether this finding can be remediated automatically.</summary>
    public bool AutoRemediable { get; set; }

    /// <summary>Original source of the finding (e.g., "ResourceGraph", "PolicyInsights").</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Which scan source discovered this finding.</summary>
    public ScanSourceType ScanSource { get; set; } = ScanSourceType.Combined;

    /// <summary>Azure Policy definition ID (for policy-based findings).</summary>
    public string? PolicyDefinitionId { get; set; }

    /// <summary>Azure Policy assignment ID (for policy-based findings).</summary>
    public string? PolicyAssignmentId { get; set; }

    /// <summary>Defender for Cloud recommendation ID (for Defender findings).</summary>
    public string? DefenderRecommendationId { get; set; }

    /// <summary>Type of remediation required for this finding.</summary>
    public RemediationType RemediationType { get; set; } = RemediationType.Unknown;

    /// <summary>Risk level based on control family (AC, IA, SC = High).</summary>
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Standard;

    /// <summary>Azure subscription ID that owns the affected resource.</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Foreign key to parent ComplianceAssessment.</summary>
    public string AssessmentId { get; set; } = string.Empty;

    // ─── New Properties (Feature 008) ────────────────────────────────────────

    /// <summary>Human-readable control title from NIST catalog.</summary>
    public string ControlTitle { get; set; } = string.Empty;

    /// <summary>Full control description from NIST catalog.</summary>
    public string ControlDescription { get; set; } = string.Empty;

    /// <summary>Whether this finding came from STIG validation.</summary>
    public bool StigFinding { get; set; }

    /// <summary>STIG rule ID (populated only when <see cref="StigFinding"/> is true).</summary>
    public string? StigId { get; set; }

    /// <summary>Lifecycle tracking for finding-level remediation.</summary>
    public RemediationTrackingStatus RemediationTrackingStatus { get; set; } = RemediationTrackingStatus.NotStarted;

    /// <summary>UTC timestamp when remediation was completed.</summary>
    public DateTime? RemediatedAt { get; set; }

    /// <summary>Identity of the user or service that performed the remediation.</summary>
    public string? RemediatedBy { get; set; }

    // ─── New Properties (Feature 015 — US7: Assessment Artifacts) ─────────

    /// <summary>DoD CAT severity level for this finding (CAT I, CAT II, or CAT III).</summary>
    public CatSeverity? CatSeverity { get; set; }

    // ─── New Properties (Feature 017 — SCAP/STIG Viewer Import) ──────────

    /// <summary>
    /// FK to <see cref="ScanImportRecord"/> when this finding was created or updated via scan import.
    /// Null for manually-created findings. See data-model.md §Modified Entities.
    /// </summary>
    public string? ImportRecordId { get; set; }

    // ─── New Properties (Feature 035 — Deviation Management) ──────────────

    /// <summary>FK → Deviation (optional link to active deviation record).</summary>
    public string? DeviationId { get; set; }

    // ─── Component Linkage (Feature 040) ────────────────────────────────────────

    /// <summary>
    /// FK to the <see cref="SystemComponent"/> whose AzureResourceId matches this finding's ResourceId.
    /// Null when the finding's resource has not been imported as a component.
    /// </summary>
    [MaxLength(36)]
    public string? ComponentId { get; set; }

    /// <summary>Linked component (nullable).</summary>
    public SystemComponent? Component { get; set; }
}

/// <summary>
/// Represents a NIST 800-53 Rev 5 control loaded from the OSCAL catalog.
/// Supports self-referential enhancements with depth limit of 2.
/// </summary>
[GlobalReference]
public class NistControl
{
    /// <summary>NIST control ID (e.g., "ac-2", "ac-2(1)", "ac-2.1"). Supports both v4 and v5 enhancement numbering.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Two-letter control family abbreviation (e.g., "AC").</summary>
    public string Family { get; set; } = string.Empty;

    /// <summary>Control title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Full control description text.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Impact level (Low, Moderate, High).</summary>
    public string ImpactLevel { get; set; } = string.Empty;

    /// <summary>Legacy enhancements list (retained for backward compatibility).</summary>
    public List<string> Enhancements { get; set; } = new();

    /// <summary>Azure implementation guidance text.</summary>
    public string AzureImplementation { get; set; } = string.Empty;

    /// <summary>Applicable FedRAMP baselines (High, Moderate, Low). Persisted as JSON.</summary>
    public List<string> Baselines { get; set; } = new();

    /// <summary>FedRAMP-specific parameter values, if any.</summary>
    public string? FedRampParameters { get; set; }

    /// <summary>Mapped Azure Policy definition IDs. Persisted as JSON.</summary>
    public List<string> AzurePolicyDefinitionIds { get; set; } = new();

    /// <summary>Nested control enhancements (self-referential, depth limit 2).</summary>
    public List<NistControl> ControlEnhancements { get; set; } = new();

    /// <summary>Parent control ID for enhancements (null for base controls).</summary>
    public string? ParentControlId { get; set; }

    /// <summary>True if this is an enhancement, not a base control.</summary>
    public bool IsEnhancement { get; set; }
}

/// <summary>
/// Remediation plan for compliance findings. Contains ordered steps
/// with dry-run-by-default behavior and approval workflow.
/// </summary>
[TenantScoped]
public class RemediationPlan
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique plan identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Azure subscription ID targeted for remediation.</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the plan was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Ordered remediation steps (EF Core owned collection in RemediationSteps table).</summary>
    public List<RemediationStep> Steps { get; set; } = new();

    /// <summary>Total number of findings addressed by this plan.</summary>
    public int TotalFindings { get; set; }

    /// <summary>Number of findings that can be auto-remediated.</summary>
    public int AutoRemediableCount { get; set; }

    /// <summary>Plan lifecycle state.</summary>
    public RemediationStatus Status { get; set; } = RemediationStatus.Planned;

    /// <summary>Whether this is a dry-run plan (default true per SEC-018).</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>ComplianceOfficer who approved the plan (null if not yet approved).</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>UTC timestamp when approval was given.</summary>
    public DateTime? ApprovedAt { get; set; }

    /// <summary>UTC timestamp when remediation completed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>ID of the step that caused failure, if any.</summary>
    public string? FailedStepId { get; set; }

    /// <summary>Reason for failure, if Status is Failed.</summary>
    public string? FailureReason { get; set; }

    // ─── Enhanced properties (Feature 009) ───

    /// <summary>Prioritized remediation items (enhanced plan generation).</summary>
    [NotMapped]
    public List<RemediationItem>? Items { get; set; }

    /// <summary>5-phase implementation timeline.</summary>
    [NotMapped]
    public ImplementationTimeline? Timeline { get; set; }

    /// <summary>Plan-level executive summary with counts and risk projection.</summary>
    [NotMapped]
    public RemediationExecutiveSummary? ExecutiveSummary { get; set; }

    /// <summary>Current/projected risk scores and reduction percentage.</summary>
    [NotMapped]
    public RiskMetrics? RiskMetrics { get; set; }

    /// <summary>Whether items are grouped by Azure resource.</summary>
    [NotMapped]
    public bool GroupByResource { get; set; }

    /// <summary>Filters applied when generating this plan.</summary>
    [NotMapped]
    public RemediationPlanOptions? Filters { get; set; }
}

/// <summary>
/// Individual step within a RemediationPlan. Configured as EF Core
/// owned entity (OwnsMany) with implicit FK to RemediationPlan.Id.
/// </summary>
public class RemediationStep
{
    /// <summary>Unique step identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Finding ID this step addresses.</summary>
    public string FindingId { get; set; } = string.Empty;

    /// <summary>NIST control ID related to this step.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Execution priority (lower = higher priority).</summary>
    public int Priority { get; set; }

    /// <summary>Human-readable description of the remediation action.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>PowerShell or Azure CLI script to execute.</summary>
    public string Script { get; set; } = string.Empty;

    /// <summary>Estimated effort level (e.g., "Low", "Medium", "High").</summary>
    public string Effort { get; set; } = string.Empty;

    /// <summary>Whether this step can be executed automatically.</summary>
    public bool AutoRemediable { get; set; }

    /// <summary>Type of remediation action for this step.</summary>
    public RemediationType RemediationType { get; set; } = RemediationType.Unknown;

    /// <summary>Step execution status.</summary>
    public StepStatus Status { get; set; } = StepStatus.Pending;

    /// <summary>Resource state captured before the change (JSON snapshot).</summary>
    public string? BeforeState { get; set; }

    /// <summary>Resource state captured after the change (JSON snapshot).</summary>
    public string? AfterState { get; set; }

    /// <summary>UTC timestamp when the step was executed.</summary>
    public DateTime? ExecutedAt { get; set; }

    /// <summary>Azure resource ID being modified.</summary>
    public string? ResourceId { get; set; }

    /// <summary>Risk level based on control family (AC, IA, SC = High).</summary>
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Standard;
}

/// <summary>
/// Evidence collected for compliance controls, with SHA-256 content hash
/// for integrity verification during audit.
/// </summary>
[TenantScoped]
public class ComplianceEvidence
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique evidence identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>NIST control ID this evidence supports.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Azure subscription ID the evidence was collected from.</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Evidence type (ConfigurationExport, PolicySnapshot, ResourceSnapshot, etc.).</summary>
    public string EvidenceType { get; set; } = string.Empty;

    /// <summary>Human-readable description of the evidence artifact.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Evidence content (JSON, text, or other format).</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the evidence was collected.</summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Identity of the user or service that collected the evidence.</summary>
    public string CollectedBy { get; set; } = string.Empty;

    /// <summary>Assessment ID this evidence is linked to (optional).</summary>
    public string? AssessmentId { get; set; }

    /// <summary>Category of evidence artifact.</summary>
    public EvidenceCategory EvidenceCategory { get; set; } = EvidenceCategory.Configuration;

    /// <summary>Specific Azure resource ID this evidence covers.</summary>
    public string? ResourceId { get; set; }

    /// <summary>SHA-256 hash of Content for integrity verification.</summary>
    public string ContentHash { get; set; } = string.Empty;

    // ─── New Properties (Feature 015 — US7: Assessment Artifacts) ─────────

    /// <summary>Identity of the collector (user, service principal, or automated agent).</summary>
    public string? CollectorIdentity { get; set; }

    /// <summary>Collection method used (e.g., "Automated", "Manual", "API", "Script").</summary>
    public string? CollectionMethod { get; set; }

    /// <summary>UTC timestamp when evidence integrity was last verified via hash recomputation.</summary>
    public DateTime? IntegrityVerifiedAt { get; set; }
}

/// <summary>
/// Generated compliance document (SSP, SAR, POA&M) in FedRAMP Markdown format.
/// </summary>
[TenantScoped]
public class ComplianceDocument
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique document identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Document type: SSP, SAR, or POAM.</summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>System name for the document header.</summary>
    public string SystemName { get; set; } = string.Empty;

    /// <summary>Compliance framework this document covers.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>Generated document content (Markdown format).</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the document was generated.</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Assessment ID this document is based on (optional).</summary>
    public string? AssessmentId { get; set; }

    /// <summary>System owner name.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>User who generated the document.</summary>
    public string GeneratedBy { get; set; } = string.Empty;

    /// <summary>Additional document metadata. EF Core owned type.</summary>
    public DocumentMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Configuration settings stored in IAgentStateManager shared state (NOT EF Core).
/// Thread-safe access via SemaphoreSlim for multi-step operations.
/// </summary>
public class ConfigurationSettings
{
    /// <summary>Default Azure subscription ID (GUID format or null).</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Default compliance framework.</summary>
    public string Framework { get; set; } = "NIST80053";

    /// <summary>Default FedRAMP baseline level.</summary>
    public string Baseline { get; set; } = "High";

    /// <summary>Azure cloud environment (AzureGovernment or AzureCloud).</summary>
    public string CloudEnvironment { get; set; } = "AzureGovernment";

    /// <summary>Default dry-run preference for remediations.</summary>
    public bool DryRunDefault { get; set; } = true;

    /// <summary>Default scan type (resource, policy, combined).</summary>
    public string DefaultScanType { get; set; } = "combined";

    /// <summary>Preferred Azure region.</summary>
    public string Region { get; set; } = "usgovvirginia";

    /// <summary>UTC timestamp when settings were last changed.</summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

// ────────────────────────────── Compliance Engine Models ─────────────────────────────────────────

/// <summary>
/// Per-family scan result aggregation. Returned by each <see cref="IComplianceScanner"/>.
/// Contains control counts, compliance score, findings, and scanner diagnostics.
/// </summary>
public class ControlFamilyAssessment
{
    /// <summary>Two-letter NIST family code (e.g., "AC").</summary>
    public string FamilyCode { get; set; } = string.Empty;

    /// <summary>Human-readable family name (from ControlFamilies.FamilyNames).</summary>
    public string FamilyName { get; set; } = string.Empty;

    /// <summary>Number of controls in this family.</summary>
    public int TotalControls { get; set; }

    /// <summary>Controls that passed assessment.</summary>
    public int PassedControls { get; set; }

    /// <summary>Controls with findings.</summary>
    public int FailedControls { get; set; }

    /// <summary>Per-family score: <c>passed / total * 100</c>.</summary>
    public double ComplianceScore { get; set; }

    /// <summary>Findings discovered in this family.</summary>
    public List<ComplianceFinding> Findings { get; set; } = new();

    /// <summary>Time spent scanning this family.</summary>
    public TimeSpan AssessmentDuration { get; set; } = TimeSpan.Zero;

    /// <summary>Scanner class name that handled this family.</summary>
    public string ScannerName { get; set; } = string.Empty;

    /// <summary>Family assessment lifecycle status.</summary>
    public FamilyAssessmentStatus Status { get; set; } = FamilyAssessmentStatus.Pending;

    /// <summary>Error message if <see cref="Status"/> is <see cref="FamilyAssessmentStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a failed family assessment result for error isolation.
    /// </summary>
    /// <param name="familyCode">The two-letter family code.</param>
    /// <param name="error">The error message describing the failure.</param>
    /// <returns>A <see cref="ControlFamilyAssessment"/> with Failed status.</returns>
    public static ControlFamilyAssessment Failed(string familyCode, string error) => new()
    {
        FamilyCode = familyCode,
        Status = FamilyAssessmentStatus.Failed,
        ErrorMessage = error
    };
}

/// <summary>
/// Progress reporting during long-running assessments.
/// Used with <see cref="IProgress{AssessmentProgress}"/>.
/// </summary>
public class AssessmentProgress
{
    /// <summary>Total families to scan (typically 20).</summary>
    public int TotalFamilies { get; set; } = 20;

    /// <summary>Families completed so far.</summary>
    public int CompletedFamilies { get; set; }

    /// <summary>Family currently being scanned (null if between scans).</summary>
    public string? CurrentFamily { get; set; }

    /// <summary>Completion percentage: <c>completed / total * 100</c>.</summary>
    public double PercentComplete { get; set; }

    /// <summary>Estimated time remaining based on average family scan time.</summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>Completed family codes.</summary>
    public List<string> FamilyResults { get; set; } = new();
}

/// <summary>
/// Severity-weighted risk summary attached to an assessment.
/// Calculated from finding counts using severity weights:
/// Critical = 10.0, High = 7.5, Medium = 5.0, Low = 2.5.
/// </summary>
public class RiskProfile
{
    /// <summary>Weighted risk score: <c>Σ(severity_weight × count)</c>.</summary>
    public double RiskScore { get; set; }

    /// <summary>Risk level derived from score thresholds (≥100 Critical, ≥50 High, ≥20 Medium, &lt;20 Low).</summary>
    public ComplianceRiskLevel RiskLevel { get; set; } = ComplianceRiskLevel.Low;

    /// <summary>Count of critical-severity findings.</summary>
    public int CriticalCount { get; set; }

    /// <summary>Count of high-severity findings.</summary>
    public int HighCount { get; set; }

    /// <summary>Count of medium-severity findings.</summary>
    public int MediumCount { get; set; }

    /// <summary>Count of low-severity findings.</summary>
    public int LowCount { get; set; }

    /// <summary>Up to 5 families with compliance score &lt; 70%, ordered ascending.</summary>
    public List<FamilyRisk> TopRisks { get; set; } = new();
}

/// <summary>
/// Entry in <see cref="RiskProfile.TopRisks"/> identifying a family at risk.
/// </summary>
public class FamilyRisk
{
    /// <summary>Two-letter family code.</summary>
    public string FamilyCode { get; set; } = string.Empty;

    /// <summary>Human-readable family name.</summary>
    public string FamilyName { get; set; } = string.Empty;

    /// <summary>Family compliance score (0.0 to 100.0).</summary>
    public double ComplianceScore { get; set; }

    /// <summary>Number of findings in this family.</summary>
    public int FindingCount { get; set; }
}

/// <summary>
/// Full 8-category risk analysis for a subscription.
/// Categories: Data Protection, Access Control, Network Security, Incident Response,
/// Business Continuity, Compliance, Third-Party Risk, Configuration Management.
/// </summary>
public class RiskAssessment
{
    /// <summary>Subscription assessed.</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Assessment timestamp.</summary>
    public DateTime AssessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>8 risk category results.</summary>
    public List<RiskCategory> Categories { get; set; } = new();

    /// <summary>Average across category scores.</summary>
    public double OverallScore { get; set; }

    /// <summary>Risk level derived from overall score (≥8 Low, ≥5 Medium, ≥3 High, &lt;3 Critical).</summary>
    public ComplianceRiskLevel OverallRiskLevel { get; set; } = ComplianceRiskLevel.Low;

    /// <summary>Mitigation recommendations for categories scoring below 5.</summary>
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// One of 8 risk assessment categories scored on a 1-10 scale.
/// </summary>
public class RiskCategory
{
    /// <summary>Category name (e.g., "Data Protection", "Access Control").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Score on 1-10 scale: <c>1 + (avgFamilyCompliance / 100 × 9)</c>, clamped to [1, 10].</summary>
    public double Score { get; set; }

    /// <summary>Risk level derived from category score.</summary>
    public ComplianceRiskLevel RiskLevel { get; set; } = ComplianceRiskLevel.Low;

    /// <summary>Related finding count across contributing families.</summary>
    public int Findings { get; set; }

    /// <summary>Recommended mitigations for this category.</summary>
    public List<string> Mitigations { get; set; } = new();
}

/// <summary>
/// Aggregated evidence collection result for a control family.
/// Scored for completeness based on distinct evidence types collected.
/// </summary>
public class EvidencePackage
{
    /// <summary>Control family code.</summary>
    public string FamilyCode { get; set; } = string.Empty;

    /// <summary>Azure subscription assessed.</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Individual evidence artifacts.</summary>
    public List<EvidenceItem> EvidenceItems { get; set; } = new();

    /// <summary>Completeness percentage: <c>distinct_types / expected_types * 100</c>.</summary>
    public double CompletenessScore { get; set; }

    /// <summary>Expected evidence types for this family (typically 5).</summary>
    public int ExpectedEvidenceTypes { get; set; } = 5;

    /// <summary>Distinct evidence types collected.</summary>
    public int CollectedEvidenceTypes { get; set; }

    /// <summary>Human-readable evidence summary.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Formal attestation text.</summary>
    public string AttestationStatement { get; set; } = string.Empty;

    /// <summary>Collection timestamp.</summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Individual evidence artifact within an <see cref="EvidencePackage"/>.
/// </summary>
public class EvidenceItem
{
    /// <summary>Evidence category.</summary>
    public EvidenceType Type { get; set; } = EvidenceType.Configuration;

    /// <summary>Evidence title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Evidence description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Evidence content (JSON or text).</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Azure resource ID (if resource-specific).</summary>
    public string? ResourceId { get; set; }

    /// <summary>Collection timestamp.</summary>
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>SHA-256 hash of <see cref="Content"/> for integrity verification.</summary>
    public string ContentHash { get; set; } = string.Empty;
}

/// <summary>
/// ATO compliance certificate with 6-month validity, per-family attestations,
/// and SHA-256 verification hash. Issued only when compliance score ≥ 80%.
/// </summary>
public class ComplianceCertificate
{
    /// <summary>Unique certificate identifier.</summary>
    public string CertificateId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Subscription certified.</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Compliance framework (e.g., "NIST80053").</summary>
    public string Framework { get; set; } = "NIST80053";

    /// <summary>Score at time of certification (must be ≥ 80.0).</summary>
    public double ComplianceScore { get; set; }

    /// <summary>Certificate issue date.</summary>
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Certificate expiration date (6-month validity).</summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(180);

    /// <summary>Issuer identity.</summary>
    public string IssuedBy { get; set; } = string.Empty;

    /// <summary>Per-family attestation entries.</summary>
    public List<FamilyAttestation> FamilyAttestations { get; set; } = new();

    /// <summary>Families covered by this certificate.</summary>
    public List<string> CoverageFamilies { get; set; } = new();

    /// <summary>SHA-256 hash of certificate content for integrity verification.</summary>
    public string VerificationHash { get; set; } = string.Empty;

    /// <summary>Certificate lifecycle status.</summary>
    public CertificateStatus Status { get; set; } = CertificateStatus.Active;
}

/// <summary>
/// Per-family attestation entry within a <see cref="ComplianceCertificate"/>.
/// </summary>
public class FamilyAttestation
{
    /// <summary>Two-letter family code.</summary>
    public string FamilyCode { get; set; } = string.Empty;

    /// <summary>Human-readable family name.</summary>
    public string FamilyName { get; set; } = string.Empty;

    /// <summary>Compliance score at certification time.</summary>
    public double ComplianceScore { get; set; }

    /// <summary>Number of controls evaluated.</summary>
    public int ControlsAssessed { get; set; }

    /// <summary>Number of controls that passed.</summary>
    public int ControlsPassed { get; set; }

    /// <summary>Formal attestation statement for this family.</summary>
    public string AttestationText { get; set; } = string.Empty;
}

/// <summary>
/// Historical compliance trend data with daily data points and significant events.
/// </summary>
public class ComplianceTimeline
{
    /// <summary>Subscription ID.</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Timeline start date.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Timeline end date.</summary>
    public DateTime EndDate { get; set; }

    /// <summary>Daily compliance data points.</summary>
    public List<TimelineDataPoint> DataPoints { get; set; } = new();

    /// <summary>Detected significant events during the period.</summary>
    public List<SignificantEvent> SignificantEvents { get; set; } = new();

    /// <summary>Overall trend direction.</summary>
    public TrendDirection Trend { get; set; } = TrendDirection.Stable;

    /// <summary>Auto-generated insights (trajectory, volatility, remediation effectiveness).</summary>
    public List<string> Insights { get; set; } = new();
}

/// <summary>
/// Daily compliance snapshot within a <see cref="ComplianceTimeline"/>.
/// </summary>
public class TimelineDataPoint
{
    /// <summary>Day of the data point.</summary>
    public DateTime Date { get; set; }

    /// <summary>Compliance score on this day.</summary>
    public double ComplianceScore { get; set; }

    /// <summary>Total findings on this day.</summary>
    public int FindingCount { get; set; }

    /// <summary>Critical findings on this day.</summary>
    public int CriticalCount { get; set; }

    /// <summary>High findings on this day.</summary>
    public int HighCount { get; set; }
}

/// <summary>
/// Notable compliance event in a <see cref="ComplianceTimeline"/>.
/// </summary>
public class SignificantEvent
{
    /// <summary>Event date.</summary>
    public DateTime Date { get; set; }

    /// <summary>Event category.</summary>
    public TimelineEventType EventType { get; set; }

    /// <summary>Human-readable description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Score delta (signed). Absolute value ≥ 10.0 triggers score events.</summary>
    public double ScoreChange { get; set; }

    /// <summary>Finding count delta (signed). Value ≥ 5 triggers FindingSpike.</summary>
    public int FindingChange { get; set; }
}

/// <summary>
/// Real-time compliance posture aggregated from Compliance Watch.
/// Combines monitoring status, drift detection, alerts, and per-control status.
/// </summary>
public class ContinuousComplianceStatus
{
    /// <summary>Subscription ID.</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Latest compliance score.</summary>
    public double OverallScore { get; set; }

    /// <summary>Whether Compliance Watch monitoring is enabled.</summary>
    public bool MonitoringEnabled { get; set; }

    /// <summary>Whether drift has been detected from baseline.</summary>
    public bool DriftDetected { get; set; }

    /// <summary>Count of active (non-resolved/dismissed) alerts.</summary>
    public int ActiveAlerts { get; set; }

    /// <summary>Most recent assessment date.</summary>
    public DateTime? LastAssessedAt { get; set; }

    /// <summary>Most recent drift check timestamp.</summary>
    public DateTime? LastDriftCheckAt { get; set; }

    /// <summary>Per-control compliance status entries.</summary>
    public List<ControlComplianceStatus> ControlStatuses { get; set; } = new();

    /// <summary>Whether auto-remediation rules exist.</summary>
    public bool AutoRemediationEnabled { get; set; }
}

/// <summary>
/// Per-control monitoring entry within <see cref="ContinuousComplianceStatus"/>.
/// </summary>
public class ControlComplianceStatus
{
    /// <summary>NIST control ID.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Current finding status for this control.</summary>
    public FindingStatus Status { get; set; }

    /// <summary>Whether drift has been detected for this control.</summary>
    public bool DriftDetected { get; set; }

    /// <summary>Last check timestamp for this control.</summary>
    public DateTime LastCheckedAt { get; set; }
}

// ──────────────────────────────── Compliance Watch Entities ──────────────────────────────────────

/// <summary>
/// A detected compliance issue with full lifecycle tracking.
/// Auto-generated human-readable AlertId (ALT-YYYYMMDDNNNNN).
/// Self-referential FK for correlated/grouped alerts.
/// </summary>
[TenantScoped]
public class ComplianceAlert
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique internal identifier (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Human-readable alert ID: ALT-YYYYMMDDNNNNN. Must match ^ALT-\d{8}\d{5}$.</summary>
    public string AlertId { get; set; } = string.Empty;

    /// <summary>Type of compliance issue detected.</summary>
    public AlertType Type { get; set; }

    /// <summary>Alert severity determining SLA deadline.</summary>
    public AlertSeverity Severity { get; set; }

    /// <summary>Current lifecycle status.</summary>
    public AlertStatus Status { get; set; } = AlertStatus.New;

    /// <summary>Brief title (max 500 chars).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Detailed description of the compliance gap.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Azure subscription ID where the issue was detected.</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Azure resource IDs affected by this alert. JSON-serialized List&lt;string&gt;.</summary>
    public List<string> AffectedResources { get; set; } = new();

    /// <summary>NIST control ID (e.g., "SC-8").</summary>
    public string? ControlId { get; set; }

    /// <summary>NIST control family (e.g., "SC").</summary>
    public string? ControlFamily { get; set; }

    /// <summary>JSON change details: { property, oldValue, newValue }.</summary>
    public string? ChangeDetails { get; set; }

    /// <summary>Identity of the actor who made the detected change.</summary>
    public string? ActorId { get; set; }

    /// <summary>Human-readable recommended remediation action.</summary>
    public string? RecommendedAction { get; set; }

    /// <summary>User assigned to resolve this alert.</summary>
    public string? AssignedTo { get; set; }

    /// <summary>Required justification when alert is dismissed.</summary>
    public string? DismissalJustification { get; set; }

    /// <summary>Identity of user who dismissed the alert.</summary>
    public string? DismissedBy { get; set; }

    /// <summary>
    /// Optional FK to the registered system this alert is associated with.
    /// Nullable for backward compatibility — pre-Feature 015 alerts remain null.
    /// Populated by ComplianceWatchService when creating alerts for subscriptions
    /// that belong to a registered system (resolved via AzureEnvironmentProfile.SubscriptionIds).
    /// </summary>
    public string? RegisteredSystemId { get; set; }

    /// <summary>FK to parent alert if this is part of a correlated group.</summary>
    public Guid? GroupedAlertId { get; set; }

    /// <summary>True if this alert is a correlation parent.</summary>
    public bool IsGrouped { get; set; }

    /// <summary>Number of correlated child alerts.</summary>
    public int ChildAlertCount { get; set; }

    /// <summary>UTC timestamp when the alert was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp when the alert was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>UTC timestamp when acknowledged.</summary>
    public DateTimeOffset? AcknowledgedAt { get; set; }

    /// <summary>Identity of user who acknowledged.</summary>
    public string? AcknowledgedBy { get; set; }

    /// <summary>UTC timestamp when resolved.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>UTC timestamp when escalated.</summary>
    public DateTimeOffset? EscalatedAt { get; set; }

    /// <summary>Computed SLA deadline based on severity.</summary>
    public DateTimeOffset SlaDeadline { get; set; }

    // Navigation properties

    /// <summary>Parent grouped alert (if child).</summary>
    public ComplianceAlert? GroupedAlert { get; set; }

    /// <summary>Child alerts in this correlation group.</summary>
    public ICollection<ComplianceAlert> ChildAlerts { get; set; } = new List<ComplianceAlert>();

    /// <summary>Notifications sent for this alert.</summary>
    public ICollection<AlertNotification> Notifications { get; set; } = new List<AlertNotification>();

    /// <summary>Navigation to the registered system (if linked).</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }
}

/// <summary>
/// Defines monitoring mode, frequency, and scope for a subscription or resource group.
/// One configuration per unique (SubscriptionId, ResourceGroupName) scope.
/// </summary>
[TenantScoped]
public class MonitoringConfiguration
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique configuration identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Azure subscription ID to monitor.</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Resource group name. Null means entire subscription scope.</summary>
    public string? ResourceGroupName { get; set; }

    /// <summary>Monitoring mode (Scheduled, EventDriven, Both).</summary>
    public MonitoringMode Mode { get; set; }

    /// <summary>How often scheduled checks run.</summary>
    public MonitoringFrequency Frequency { get; set; }

    /// <summary>Whether this monitoring configuration is currently active.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Next scheduled run time (for scheduled monitoring).</summary>
    public DateTimeOffset NextRunAt { get; set; }

    /// <summary>When the last monitoring check ran.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>High-water mark for event-driven monitoring (last event timestamp processed).</summary>
    public DateTimeOffset? LastEventCheckAt { get; set; }

    /// <summary>Identity of user who created this configuration.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp when created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp when last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Point-in-time snapshot of a resource's compliant configuration.
/// Captured after successful assessment or remediation. Used for drift detection.
/// </summary>
[TenantScoped]
public class ComplianceBaseline
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique baseline identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Azure subscription ID this baseline belongs to.</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Full Azure resource ID.</summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>Azure resource type (e.g., "Microsoft.Storage/storageAccounts").</summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the resource configuration (64-char hex).</summary>
    public string ConfigurationHash { get; set; } = string.Empty;

    /// <summary>JSON snapshot of relevant configuration properties.</summary>
    public string ConfigurationSnapshot { get; set; } = string.Empty;

    /// <summary>JSON of policy compliance state at baseline capture time.</summary>
    public string? PolicyComplianceState { get; set; }

    /// <summary>FK to the assessment that established this baseline (optional).</summary>
    public Guid? AssessmentId { get; set; }

    /// <summary>UTC timestamp when this baseline was captured.</summary>
    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>Whether this baseline is currently the active one for this resource. Only one active baseline per ResourceId.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Database-backed date-partitioned counter for generating human-readable alert IDs.
/// One row per calendar date, atomically incremented within serializable transaction.
/// </summary>
[TenantScoped]
public class AlertIdCounter
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Calendar date (PK).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Last used sequence number for this date. Atomically incremented.</summary>
    public int LastSequence { get; set; }
}

/// <summary>
/// Record of a notification sent through a specific channel for a specific alert.
/// Append-only audit trail.
/// </summary>
[TenantScoped]
public class AlertNotification
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique notification identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the compliance alert.</summary>
    public Guid AlertId { get; set; }

    /// <summary>Notification channel used.</summary>
    public NotificationChannel Channel { get; set; }

    /// <summary>Recipient identifier (email, webhook URL, user ID).</summary>
    public string Recipient { get; set; } = string.Empty;

    /// <summary>Notification subject line.</summary>
    public string? Subject { get; set; }

    /// <summary>Notification body content.</summary>
    public string? Body { get; set; }

    /// <summary>Whether delivery was confirmed.</summary>
    public bool IsDelivered { get; set; }

    /// <summary>Delivery error message, if any.</summary>
    public string? DeliveryError { get; set; }

    /// <summary>UTC timestamp when sent.</summary>
    public DateTimeOffset SentAt { get; set; }

    /// <summary>UTC timestamp when delivery was confirmed.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>Target user ID for this notification.</summary>
    public string? UserId { get; set; }

    /// <summary>Whether the notification has been read by the user.</summary>
    public bool IsRead { get; set; }

    /// <summary>UTC timestamp when the notification was read.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    // Navigation
    /// <summary>The alert this notification belongs to.</summary>
    public ComplianceAlert Alert { get; set; } = null!;
}

/// <summary>
/// Per-user notification preferences persisted in EF Core.
/// </summary>
[TenantScoped]
public class NotificationPreferences
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public bool PoamOverdueAlerts { get; set; } = true;
    public bool AtoExpirationAlerts { get; set; } = true;
    public bool ComplianceDriftAlerts { get; set; } = true;
    public int AlertDaysBefore { get; set; } = 30;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Audit log entry for compliance-related actions. Persisted in EF Core.
/// Retention: 730 days per SEC-015.
/// </summary>
[TenantScoped]
public class AuditLogEntry
{
    /// <summary>
    /// Home tenant of the entry. For impersonation rows this matches
    /// <c>EffectiveTenantId</c> (the impersonated tenant). Populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021 / FR-052).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Home tenant of the principal who initiated the action. Differs from
    /// <see cref="TenantId"/> only when a CSP-Admin is impersonating another
    /// tenant (FR-052). Nullable for legacy rows written before the
    /// tenant-attribution columns existed and for system-driven actions
    /// performed without an authenticated principal.
    /// </summary>
    public Guid? ActorTenantId { get; set; }

    /// <summary>
    /// The tenant the principal was impersonating when the row was written.
    /// Non-null only on rows produced under an active impersonation session
    /// (FR-052). Equal to <see cref="TenantId"/> when set.
    /// </summary>
    public Guid? ImpersonatedTenantId { get; set; }

    /// <summary>Unique log entry identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Identity of the user who initiated the action (must be non-empty).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Role of the user (Administrator, Auditor, Analyst, Viewer).</summary>
    public string UserRole { get; set; } = string.Empty;

    /// <summary>Action type (Assessment, Remediation, EvidenceCollection, DocumentGeneration, etc.).</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Scan type if applicable (resource, policy, combined).</summary>
    public string? ScanType { get; set; }

    /// <summary>UTC timestamp when the action occurred.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Target Azure subscription ID.</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Azure resource IDs affected by the action. Persisted as JSON.</summary>
    public List<string> AffectedResources { get; set; } = new();

    /// <summary>NIST control IDs affected by the action. Persisted as JSON.</summary>
    public List<string> AffectedControls { get; set; } = new();

    /// <summary>Outcome of the action.</summary>
    public AuditOutcome Outcome { get; set; } = AuditOutcome.Success;

    /// <summary>Additional context or error details.</summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>Duration of the action.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// W3C Trace Context / request correlation id (Feature 048 FR-061).
    /// Surfaced through <c>/api/audit</c> so CSP-Admin queries can stitch
    /// a workflow together across services. Populated by
    /// <c>AuditLoggingMiddleware</c> from
    /// <c>HttpContext.Items["CorrelationId"]</c> with a fallback to
    /// <c>Activity.Current?.Id ?? HttpContext.TraceIdentifier</c>.
    /// </summary>
    public string? CorrelationId { get; set; }
}

/// <summary>
/// A user-defined or default rule that specifies alert conditions, severity overrides, and recipients.
/// </summary>
[TenantScoped]
public class AlertRule
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique rule identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Human-readable rule name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of what the rule does.</summary>
    public string? Description { get; set; }

    /// <summary>Scope: subscription ID (null = all subscriptions).</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Scope: resource group name (null = entire subscription).</summary>
    public string? ResourceGroupName { get; set; }

    /// <summary>Filter by Azure resource type.</summary>
    public string? ResourceType { get; set; }

    /// <summary>Scope: specific resource ID.</summary>
    public string? ResourceId { get; set; }

    /// <summary>NIST control family filter (e.g., "AC").</summary>
    public string? ControlFamily { get; set; }

    /// <summary>NIST control ID filter (e.g., "AC-2").</summary>
    public string? ControlId { get; set; }

    /// <summary>JSON expression for custom trigger conditions.</summary>
    public string? TriggerCondition { get; set; }

    /// <summary>Override default severity when this rule matches.</summary>
    public AlertSeverity? SeverityOverride { get; set; }

    /// <summary>Override default notification recipients. JSON-serialized List&lt;string&gt;.</summary>
    public List<string> RecipientOverrides { get; set; } = new();

    /// <summary>True if this is a pre-created default rule.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Whether the rule is currently active.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Identity of the user who created the rule.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the rule was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp when the rule was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Temporary or permanent rule that mutes alerts for a defined scope.
/// </summary>
[TenantScoped]
public class SuppressionRule
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique suppression rule identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Scope: subscription ID (null = all subscriptions).</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Scope: resource group name.</summary>
    public string? ResourceGroupName { get; set; }

    /// <summary>Scope: specific resource ID.</summary>
    public string? ResourceId { get; set; }

    /// <summary>NIST control family filter.</summary>
    public string? ControlFamily { get; set; }

    /// <summary>NIST control ID filter.</summary>
    public string? ControlId { get; set; }

    /// <summary>Type of suppression: Temporary or Permanent.</summary>
    public SuppressionType Type { get; set; }

    /// <summary>Required justification for permanent suppressions.</summary>
    public string? Justification { get; set; }

    /// <summary>Expiration for temporary suppressions (must be in future).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Whether the suppression is currently active.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Identity of the user who created the suppression.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the suppression was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Start of quiet hours window (e.g., 22:00). Both start and end must be set, or neither.</summary>
    public TimeOnly? QuietHoursStart { get; set; }

    /// <summary>End of quiet hours window (e.g., 06:00). Both start and end must be set, or neither.</summary>
    public TimeOnly? QuietHoursEnd { get; set; }
}

// ─── Escalation & Notification Entities (US4) ───────────────────────────────

/// <summary>
/// Chain of notification actions triggered when an alert is not acknowledged within SLA.
/// </summary>
[TenantScoped]
public class EscalationPath
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Which severity level triggers this escalation path.</summary>
    public AlertSeverity TriggerSeverity { get; set; }

    /// <summary>Minutes after SLA deadline before escalating.</summary>
    public int EscalationDelayMinutes { get; set; }

    /// <summary>Recipients to notify (user IDs or roles). JSON-serialized.</summary>
    public List<string> Recipients { get; set; } = new();

    /// <summary>Preferred notification channel for escalation.</summary>
    public NotificationChannel Channel { get; set; }

    /// <summary>How often (minutes) to re-notify if still unacknowledged.</summary>
    public int RepeatIntervalMinutes { get; set; }

    /// <summary>Stop after N escalation attempts.</summary>
    public int MaxEscalations { get; set; } = 3;

    /// <summary>External webhook URL for integration.</summary>
    public string? WebhookUrl { get; set; }

    public bool IsEnabled { get; set; } = true;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// ─── Dashboard & Historical Reporting Entities (US7) ────────────────────────

/// <summary>
/// Point-in-time compliance posture snapshot for historical trend analysis.
/// Captured daily (at midnight UTC) and promoted to weekly on Sundays.
/// </summary>
[TenantScoped]
public class ComplianceSnapshot
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique snapshot identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Azure subscription ID this snapshot represents.</summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>Compliance score (0-100) at the time of capture.</summary>
    public double ComplianceScore { get; set; }

    /// <summary>Total number of controls assessed.</summary>
    public int TotalControls { get; set; }

    /// <summary>Number of controls passing.</summary>
    public int PassedControls { get; set; }

    /// <summary>Number of controls failing.</summary>
    public int FailedControls { get; set; }

    /// <summary>Total number of resources assessed.</summary>
    public int TotalResources { get; set; }

    /// <summary>Number of compliant resources.</summary>
    public int CompliantResources { get; set; }

    /// <summary>Number of non-compliant resources.</summary>
    public int NonCompliantResources { get; set; }

    /// <summary>Number of active (non-resolved/dismissed) alerts at capture time.</summary>
    public int ActiveAlertCount { get; set; }

    /// <summary>Number of Critical-severity active alerts.</summary>
    public int CriticalAlertCount { get; set; }

    /// <summary>Number of High-severity active alerts.</summary>
    public int HighAlertCount { get; set; }

    /// <summary>JSON-serialized breakdown by control family (e.g., {"AC": 5, "SC": 3}).</summary>
    public string? ControlFamilyBreakdown { get; set; }

    /// <summary>UTC timestamp when the snapshot was captured.</summary>
    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>True if this is a weekly rollup snapshot (Sundays).</summary>
    public bool IsWeeklySnapshot { get; set; }

    // ─── New Properties (Feature 015 — US7: Assessment Artifacts) ─────────

    /// <summary>SHA-256 integrity hash over canonical snapshot content for tamper detection.</summary>
    public string IntegrityHash { get; set; } = string.Empty;

    /// <summary>True once snapshot is finalized — no UPDATE or DELETE allowed.</summary>
    public bool IsImmutable { get; set; }
}

/// <summary>
/// Opt-in rule that defines automatic remediation for trusted, low-risk violations.
/// High-risk control families (AC, IA, SC) are blocked and always require human approval.
/// </summary>
[TenantScoped]
public class AutoRemediationRule
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique rule identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Human-readable rule name (max 200 chars).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of what this rule does.</summary>
    public string? Description { get; set; }

    /// <summary>Target subscription scope (null = all subscriptions).</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Target resource group scope (null = entire subscription).</summary>
    public string? ResourceGroupName { get; set; }

    /// <summary>Target control family (AC, IA, SC blocked).</summary>
    public string? ControlFamily { get; set; }

    /// <summary>Target specific control ID.</summary>
    public string? ControlId { get; set; }

    /// <summary>Remediation action description/identifier.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Approval mode: "auto" or "require-approval".</summary>
    public string ApprovalMode { get; set; } = "require-approval";

    /// <summary>Whether this rule is currently enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Total times this rule has been executed.</summary>
    public int ExecutionCount { get; set; }

    /// <summary>Last time this rule was executed.</summary>
    public DateTimeOffset? LastExecutedAt { get; set; }

    /// <summary>User who created this rule.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>When this rule was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this rule was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Result of an auto-remediation attempt for an alert.
/// </summary>
public class AutoRemediationResult
{
    /// <summary>Whether the auto-remediation was attempted.</summary>
    public bool Attempted { get; set; }

    /// <summary>Whether the auto-remediation succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>The rule that was matched, if any.</summary>
    public AutoRemediationRule? MatchedRule { get; set; }

    /// <summary>Human-readable outcome message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>If failed, the reason why.</summary>
    public string? FailureReason { get; set; }
}

// ────────────────────── Remediation Engine Models (Feature 009) ──────────────────────

/// <summary>
/// Tracks a single remediation operation through its lifecycle.
/// </summary>
public class RemediationExecution
{
    /// <summary>Unique execution ID (GUID).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>ID of the finding being remediated.</summary>
    public string FindingId { get; set; } = string.Empty;

    /// <summary>Target Azure subscription.</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Current execution status.</summary>
    public RemediationExecutionStatus Status { get; set; } = RemediationExecutionStatus.Pending;

    /// <summary>JSON snapshot of resource before remediation.</summary>
    public string? BeforeSnapshot { get; set; }

    /// <summary>JSON snapshot of resource after remediation.</summary>
    public string? AfterSnapshot { get; set; }

    /// <summary>Reference to snapshot for rollback.</summary>
    public string? BackupId { get; set; }

    /// <summary>When execution started.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>When execution completed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Total execution duration.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Number of steps completed.</summary>
    public int StepsExecuted { get; set; }

    /// <summary>Description of changes made.</summary>
    public List<string> ChangesApplied { get; set; } = new();

    /// <summary>Which pipeline tier executed (1=AI, 2=Service, 3=ARM).</summary>
    public int TierUsed { get; set; }

    /// <summary>Error message if failed.</summary>
    public string? Error { get; set; }

    /// <summary>Approver identity if approval workflow used.</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>When approved.</summary>
    public DateTime? ApprovedAt { get; set; }

    /// <summary>Rejector identity if rejected.</summary>
    public string? RejectedBy { get; set; }

    /// <summary>When rejected.</summary>
    public DateTime? RejectedAt { get; set; }

    /// <summary>Reason for rejection.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Whether this was a dry-run execution.</summary>
    public bool DryRun { get; set; }

    /// <summary>Execution options used.</summary>
    public RemediationExecutionOptions? Options { get; set; }
}

/// <summary>
/// A single activity entry for tracking/audit purposes.
/// </summary>
public class RemediationActivity
{
    /// <summary>Activity ID.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Related execution ID.</summary>
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>What happened (e.g., "SnapshotCaptured", "TierFallback").</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Additional details.</summary>
    public string? Details { get; set; }

    /// <summary>When it occurred.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Risk scoring for a plan or analysis.
/// </summary>
public class RiskMetrics
{
    /// <summary>Severity-weighted current risk score.</summary>
    public double CurrentRiskScore { get; set; }

    /// <summary>Risk score after remediation (non-remediable findings only).</summary>
    public double ProjectedRiskScore { get; set; }

    /// <summary>Risk reduction percentage.</summary>
    public double RiskReductionPercentage { get; set; }
}

/// <summary>
/// A single finding paired with remediation metadata —
/// priority, steps, timeline, and dependencies.
/// </summary>
public class RemediationItem
{
    /// <summary>Unique item identifier (GUID).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The compliance finding to remediate.</summary>
    public ComplianceFinding Finding { get; set; } = null!;

    /// <summary>P0–P4 priority level.</summary>
    public RemediationPriority Priority { get; set; }

    /// <summary>Human-readable priority label (e.g., "P0 - Immediate").</summary>
    public string PriorityLabel { get; set; } = string.Empty;

    /// <summary>Estimated time to remediate.</summary>
    public TimeSpan EstimatedDuration { get; set; }

    /// <summary>Detailed remediation steps.</summary>
    public List<RemediationStep> Steps { get; set; } = new();

    /// <summary>Steps to verify remediation success.</summary>
    public List<string> ValidationSteps { get; set; } = new();

    /// <summary>Description of how to undo this remediation.</summary>
    public string RollbackPlan { get; set; } = string.Empty;

    /// <summary>IDs of other items this depends on.</summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>Whether this can be auto-remediated.</summary>
    public bool IsAutoRemediable { get; set; }

    /// <summary>Type of remediation action.</summary>
    public RemediationType RemediationType { get; set; }

    /// <summary>Azure resource ID if applicable.</summary>
    public string? AffectedResourceId { get; set; }
}

/// <summary>
/// 5-phase implementation timeline for a remediation plan.
/// </summary>
public class ImplementationTimeline
{
    /// <summary>Ordered timeline phases.</summary>
    public List<TimelinePhase> Phases { get; set; } = new();

    /// <summary>Sum of all phase durations.</summary>
    public TimeSpan TotalEstimatedDuration { get; set; }

    /// <summary>Timeline start date.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Timeline end date.</summary>
    public DateTime EndDate { get; set; }
}

/// <summary>
/// A single phase within the implementation timeline.
/// </summary>
public class TimelinePhase
{
    /// <summary>Phase name (Immediate, 24 Hours, Week 1, Month 1, Backlog).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>P0–P4 priority for this phase.</summary>
    public RemediationPriority Priority { get; set; }

    /// <summary>Items in this phase.</summary>
    public List<RemediationItem> Items { get; set; } = new();

    /// <summary>Phase start date.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Phase end date.</summary>
    public DateTime EndDate { get; set; }

    /// <summary>Total effort for this phase.</summary>
    public TimeSpan EstimatedDuration { get; set; }
}

/// <summary>
/// Plan-level executive summary with counts and risk projection.
/// </summary>
public class RemediationExecutiveSummary
{
    /// <summary>Total findings in plan.</summary>
    public int TotalFindings { get; set; }

    /// <summary>Critical severity count.</summary>
    public int CriticalCount { get; set; }

    /// <summary>High severity count.</summary>
    public int HighCount { get; set; }

    /// <summary>Medium severity count.</summary>
    public int MediumCount { get; set; }

    /// <summary>Low severity count.</summary>
    public int LowCount { get; set; }

    /// <summary>Auto-remediable findings count.</summary>
    public int AutoRemediableCount { get; set; }

    /// <summary>Manual-only findings count.</summary>
    public int ManualCount { get; set; }

    /// <summary>Sum of all item durations.</summary>
    public TimeSpan TotalEstimatedEffort { get; set; }

    /// <summary>Projected risk reduction percentage.</summary>
    public double ProjectedRiskReduction { get; set; }
}

/// <summary>
/// Aggregate outcome of a batch remediation operation.
/// </summary>
public class BatchRemediationResult
{
    /// <summary>Unique batch ID (GUID).</summary>
    public string BatchId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Individual execution results.</summary>
    public List<RemediationExecution> Executions { get; set; } = new();

    /// <summary>Number of successful remediations.</summary>
    public int SuccessCount { get; set; }

    /// <summary>Number of failed remediations.</summary>
    public int FailureCount { get; set; }

    /// <summary>Number cancelled (FailFast mode).</summary>
    public int CancelledCount { get; set; }

    /// <summary>Number skipped (not auto-remediable).</summary>
    public int SkippedCount { get; set; }

    /// <summary>Aggregate statistics.</summary>
    public BatchRemediationSummary Summary { get; set; } = new();

    /// <summary>Batch start time.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Batch completion time.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Total batch duration.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Options used for batch.</summary>
    public BatchRemediationOptions? Options { get; set; }
}

/// <summary>
/// Aggregate statistics for a batch remediation.
/// </summary>
public class BatchRemediationSummary
{
    /// <summary>Percentage of successful remediations.</summary>
    public double SuccessRate { get; set; }

    /// <summary>Count of Critical findings fixed.</summary>
    public int CriticalFindingsRemediated { get; set; }

    /// <summary>Count of High findings fixed.</summary>
    public int HighFindingsRemediated { get; set; }

    /// <summary>Count of Medium findings fixed.</summary>
    public int MediumFindingsRemediated { get; set; }

    /// <summary>Count of Low findings fixed.</summary>
    public int LowFindingsRemediated { get; set; }

    /// <summary>Projected risk reduction percentage.</summary>
    public double EstimatedRiskReduction { get; set; }

    /// <summary>Unique control families touched.</summary>
    public List<string> ControlFamiliesAffected { get; set; } = new();

    /// <summary>Sum of all execution durations.</summary>
    public TimeSpan TotalDuration { get; set; }
}

/// <summary>
/// Post-execution validation outcome.
/// </summary>
public class RemediationValidationResult
{
    /// <summary>ID of the execution validated.</summary>
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>Overall validation result.</summary>
    public bool IsValid { get; set; }

    /// <summary>Individual check results.</summary>
    public List<ValidationCheck> Checks { get; set; } = new();

    /// <summary>Overall failure reason if not valid.</summary>
    public string? FailureReason { get; set; }

    /// <summary>When validation was performed.</summary>
    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A single validation check within a validation result.
/// </summary>
public class ValidationCheck
{
    /// <summary>Check name (e.g., "ExecutionStatus", "StepsCompleted").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this check passed.</summary>
    public bool Passed { get; set; }

    /// <summary>What was expected.</summary>
    public string? ExpectedValue { get; set; }

    /// <summary>What was found.</summary>
    public string? ActualValue { get; set; }

    /// <summary>Additional details.</summary>
    public string? Details { get; set; }
}

/// <summary>
/// Outcome of a rollback operation.
/// </summary>
public class RemediationRollbackResult
{
    /// <summary>ID of the original execution.</summary>
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>Whether rollback succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Steps executed during rollback.</summary>
    public List<string> RollbackSteps { get; set; } = new();

    /// <summary>JSON of restored resource state.</summary>
    public string? RestoredSnapshot { get; set; }

    /// <summary>Error message if rollback failed.</summary>
    public string? Error { get; set; }

    /// <summary>When rollback was performed.</summary>
    public DateTime RolledBackAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Planned rollback approach for a remediation item.
/// </summary>
public class RemediationRollbackPlan
{
    /// <summary>What the rollback will do.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Ordered rollback steps.</summary>
    public List<string> Steps { get; set; } = new();

    /// <summary>Whether a before-snapshot is needed.</summary>
    public bool RequiresSnapshot { get; set; }

    /// <summary>Estimated rollback time.</summary>
    public TimeSpan EstimatedDuration { get; set; }
}

/// <summary>
/// Active workflow state for a subscription.
/// </summary>
public class RemediationWorkflowStatus
{
    /// <summary>Target subscription.</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Executions awaiting approval.</summary>
    public List<RemediationExecution> PendingApprovals { get; set; } = new();

    /// <summary>Currently executing.</summary>
    public List<RemediationExecution> InProgressExecutions { get; set; } = new();

    /// <summary>Completed in last 24 hours.</summary>
    public List<RemediationExecution> RecentlyCompleted { get; set; } = new();

    /// <summary>When this snapshot was taken.</summary>
    public DateTime RetrievedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of an approval or rejection decision.
/// </summary>
public class RemediationApprovalResult
{
    /// <summary>Execution that was approved/rejected.</summary>
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>Whether approved (true) or rejected (false).</summary>
    public bool Approved { get; set; }

    /// <summary>Identity of the approver.</summary>
    public string ApproverName { get; set; } = string.Empty;

    /// <summary>Approver comments.</summary>
    public string? Comments { get; set; }

    /// <summary>When the decision was made.</summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether execution was auto-triggered post-approval.</summary>
    public bool ExecutionTriggered { get; set; }
}

/// <summary>
/// Result of scheduling a remediation for future execution.
/// The engine creates the schedule record but does not execute automatically.
/// The calling layer (e.g., ComplianceWatchService) polls due schedules.
/// </summary>
public class RemediationScheduleResult
{
    /// <summary>Unique schedule ID (GUID).</summary>
    public string ScheduleId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>When remediation will execute.</summary>
    public DateTime ScheduledTime { get; set; }

    /// <summary>Findings included.</summary>
    public List<string> FindingIds { get; set; } = new();

    /// <summary>Number of findings.</summary>
    public int FindingCount { get; set; }

    /// <summary>Schedule status (Scheduled, Executed, Cancelled).</summary>
    public string Status { get; set; } = "Scheduled";

    /// <summary>Options to use at execution time.</summary>
    public BatchRemediationOptions? Options { get; set; }

    /// <summary>When schedule was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Subscription-level progress snapshot.
/// </summary>
public class RemediationProgress
{
    /// <summary>Target subscription.</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Successful remediations.</summary>
    public int CompletedCount { get; set; }

    /// <summary>Currently executing.</summary>
    public int InProgressCount { get; set; }

    /// <summary>Failed remediations.</summary>
    public int FailedCount { get; set; }

    /// <summary>Awaiting approval.</summary>
    public int PendingCount { get; set; }

    /// <summary>Total remediations.</summary>
    public int TotalCount { get; set; }

    /// <summary>Percentage complete.</summary>
    public double CompletionRate { get; set; }

    /// <summary>Average execution duration.</summary>
    public TimeSpan AverageRemediationTime { get; set; }

    /// <summary>Time period covered (e.g., "Last 30 days").</summary>
    public string Period { get; set; } = "Last 30 days";

    /// <summary>When snapshot was calculated.</summary>
    public DateTime AsOf { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Date-range execution history with aggregate metrics.
/// </summary>
public class RemediationHistory
{
    /// <summary>Target subscription.</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Range start.</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Range end.</summary>
    public DateTime EndDate { get; set; }

    /// <summary>Executions in range.</summary>
    public List<RemediationExecution> Executions { get; set; } = new();

    /// <summary>Aggregate metrics.</summary>
    public RemediationMetric Metrics { get; set; } = new();

    /// <summary>Pagination offset (default 0).</summary>
    public int Skip { get; set; }

    /// <summary>Pagination page size (default 50).</summary>
    public int Take { get; set; } = 50;

    /// <summary>Total matching executions before pagination.</summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Aggregate metrics for remediation history.
/// </summary>
public class RemediationMetric
{
    /// <summary>Total execution count.</summary>
    public int TotalExecutions { get; set; }

    /// <summary>Completed successfully.</summary>
    public int SuccessfulExecutions { get; set; }

    /// <summary>Failed.</summary>
    public int FailedExecutions { get; set; }

    /// <summary>Rolled back.</summary>
    public int RolledBackExecutions { get; set; }

    /// <summary>Average duration.</summary>
    public TimeSpan AverageExecutionTime { get; set; }

    /// <summary>Most frequently remediated control family.</summary>
    public string? MostRemediatedFamily { get; set; }
}

/// <summary>
/// Pre-execution risk analysis with current/projected risk scores.
/// </summary>
public class RemediationImpactAnalysis
{
    /// <summary>Current/projected risk scores and reduction percentage.</summary>
    public RiskMetrics RiskMetrics { get; set; } = new();

    /// <summary>Total findings considered.</summary>
    public int TotalFindingsAnalyzed { get; set; }

    /// <summary>Auto-remediable findings.</summary>
    public int AutoRemediableCount { get; set; }

    /// <summary>Manual-only findings.</summary>
    public int ManualCount { get; set; }

    /// <summary>Per-resource impact details.</summary>
    public List<ResourceImpact> ResourceImpacts { get; set; } = new();

    /// <summary>Actionable recommendations.</summary>
    public List<string> Recommendations { get; set; } = new();

    /// <summary>When analysis was performed.</summary>
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-resource impact detail within an impact analysis.
/// </summary>
public class ResourceImpact
{
    /// <summary>Azure resource ID.</summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>Resource type.</summary>
    public string? ResourceType { get; set; }

    /// <summary>Number of findings for this resource.</summary>
    public int FindingsCount { get; set; }

    /// <summary>What would change.</summary>
    public List<string> ProposedChanges { get; set; } = new();

    /// <summary>Impact risk level.</summary>
    public RiskLevel RiskLevel { get; set; }
}

/// <summary>
/// Comprehensive guide for non-automatable findings.
/// </summary>
public class ManualRemediationGuide
{
    /// <summary>Finding this guide is for.</summary>
    public string FindingId { get; set; } = string.Empty;

    /// <summary>NIST control ID.</summary>
    public string? ControlId { get; set; }

    /// <summary>Guide title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Step-by-step instructions.</summary>
    public List<string> Steps { get; set; } = new();

    /// <summary>What must be in place before starting.</summary>
    public List<string> Prerequisites { get; set; } = new();

    /// <summary>Required skill level (Beginner, Intermediate, Advanced).</summary>
    public string SkillLevel { get; set; } = "Intermediate";

    /// <summary>Azure permissions needed.</summary>
    public List<string> RequiredPermissions { get; set; } = new();

    /// <summary>How to verify the fix worked.</summary>
    public List<string> ValidationSteps { get; set; } = new();

    /// <summary>How to undo if something goes wrong.</summary>
    public string RollbackPlan { get; set; } = string.Empty;

    /// <summary>How long it should take.</summary>
    public TimeSpan EstimatedDuration { get; set; }

    /// <summary>Microsoft Docs or NIST reference links.</summary>
    public List<string> References { get; set; } = new();
}
