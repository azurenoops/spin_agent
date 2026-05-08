using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

// ═══════════════════════════════════════════════════════════════════════════════
// Enums
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Classification of a compliance deviation.
/// </summary>
public enum DeviationType
{
    /// <summary>Scan result does not represent a real vulnerability.</summary>
    FalsePositive,
    /// <summary>Known risk accepted by authority with compensating controls.</summary>
    RiskAcceptance,
    /// <summary>Control determined not applicable for a scope/boundary.</summary>
    Waiver
}

/// <summary>
/// Lifecycle status of a deviation request.
/// Transitions: Pending → Approved | Denied; Approved → Expired | Revoked.
/// </summary>
public enum DeviationStatus
{
    /// <summary>Awaiting reviewer approval.</summary>
    Pending,
    /// <summary>Active deviation; linked entities transitioned.</summary>
    Approved,
    /// <summary>Rejected by reviewer; finding remains Open.</summary>
    Denied,
    /// <summary>Past expiration without renewal; entities reverted.</summary>
    Expired,
    /// <summary>Manually withdrawn; entities reverted.</summary>
    Revoked
}

// ═══════════════════════════════════════════════════════════════════════════════
// Entity
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// A formal compliance exception record — false positive, risk acceptance, or waiver —
/// with an approval workflow, evidence linkage, expiration, and review cycle.
/// </summary>
[TenantScoped]
public class Deviation
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK → RegisteredSystem this deviation belongs to.</summary>
    [Required]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Classification of the deviation.</summary>
    public DeviationType DeviationType { get; set; }

    /// <summary>Current lifecycle status.</summary>
    public DeviationStatus Status { get; set; } = DeviationStatus.Pending;

    /// <summary>NIST 800-53 control ID (e.g., "AC-2").</summary>
    [Required]
    [MaxLength(20)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>DoD CAT severity level.</summary>
    public CatSeverity CatSeverity { get; set; }

    /// <summary>Reason for the deviation request.</summary>
    [Required]
    [MaxLength(4000)]
    public string Justification { get; set; } = string.Empty;

    /// <summary>Description of compensating controls, if any.</summary>
    [MaxLength(2000)]
    public string? CompensatingControls { get; set; }

    /// <summary>JSON array of ScanImportRecord IDs used as evidence.</summary>
    public string EvidenceReferences { get; set; } = "[]";

    /// <summary>UTC date when this deviation auto-expires.</summary>
    public DateTime ExpirationDate { get; set; }

    /// <summary>Review cycle interval: "90d", "180d", or "Annual".</summary>
    [Required]
    [MaxLength(20)]
    public string ReviewCycle { get; set; } = "180d";

    // ─── Foreign Keys ────────────────────────────────────────────────────────

    /// <summary>FK → ComplianceFinding (optional link to originating finding).</summary>
    public string? FindingId { get; set; }

    /// <summary>FK → PoamItem (optional link to POA&amp;M entry).</summary>
    public string? PoamEntryId { get; set; }

    /// <summary>FK → AuthorizationDecision (optional parent authorization context).</summary>
    public string? AuthorizationDecisionId { get; set; }

    /// <summary>FK → AuthorizationBoundaryDefinition (waivers only; boundary scope).</summary>
    public string? BoundaryDefinitionId { get; set; }

    // ─── Request Metadata ────────────────────────────────────────────────────

    /// <summary>User ID of the requestor.</summary>
    [Required]
    [MaxLength(200)]
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the request was submitted.</summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    // ─── Review Metadata ─────────────────────────────────────────────────────

    /// <summary>User ID of the reviewer who rendered a final decision (ISSM or AO).</summary>
    [MaxLength(200)]
    public string? ReviewedBy { get; set; }

    /// <summary>UTC timestamp of the final review decision.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Role of the final reviewer: "ISSM" or "AO".</summary>
    [MaxLength(50)]
    public string? ReviewerRole { get; set; }

    /// <summary>Reviewer's comments on the decision.</summary>
    [MaxLength(2000)]
    public string? ReviewerComments { get; set; }

    // ─── CAT I Two-Step ISSM Recommendation ──────────────────────────────────

    /// <summary>ISSM recommendation for CAT I deviations: "Approve" or "Deny".</summary>
    [MaxLength(20)]
    public string? ISSMRecommendation { get; set; }

    /// <summary>ISSM user ID who submitted the recommendation (CAT I two-step only).</summary>
    [MaxLength(200)]
    public string? ISSMRecommendedBy { get; set; }

    /// <summary>UTC timestamp when the ISSM recommendation was recorded.</summary>
    public DateTime? ISSMRecommendedAt { get; set; }

    // ─── Revocation Metadata ─────────────────────────────────────────────────

    /// <summary>User ID who revoked the deviation.</summary>
    [MaxLength(200)]
    public string? RevokedBy { get; set; }

    /// <summary>UTC timestamp when the deviation was revoked.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Reason for revocation.</summary>
    [MaxLength(1000)]
    public string? RevocationReason { get; set; }

    // ─── Timestamps ──────────────────────────────────────────────────────────

    /// <summary>UTC record creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC last modification timestamp.</summary>
    public DateTime? ModifiedAt { get; set; }

    // ─── Navigation Properties ───────────────────────────────────────────────

    /// <summary>Navigation to parent RegisteredSystem.</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }

    /// <summary>Navigation to the linked ComplianceFinding.</summary>
    public ComplianceFinding? Finding { get; set; }

    /// <summary>Navigation to the linked POA&amp;M item.</summary>
    public PoamItem? PoamEntry { get; set; }

    /// <summary>Navigation to the parent AuthorizationDecision.</summary>
    public AuthorizationDecision? AuthorizationDecision { get; set; }

    /// <summary>Navigation to the boundary definition (waivers only).</summary>
    public AuthorizationBoundaryDefinition? BoundaryDefinition { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Constants
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Constants for deviation management.
/// </summary>
public static class DeviationConstants
{
    /// <summary>Maximum allowed review cycle / extension duration in days.</summary>
    public const int MaxReviewCycleDays = 365;

    /// <summary>Valid review cycle values.</summary>
    public static readonly string[] ValidReviewCycles = ["90d", "180d", "Annual"];
}

// ═══════════════════════════════════════════════════════════════════════════════
// Request / Input DTOs
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Input DTO for creating a deviation request.
/// </summary>
public class CreateDeviationRequest
{
    /// <summary>Classification: FalsePositive, RiskAcceptance, or Waiver.</summary>
    [Required]
    public string DeviationType { get; set; } = string.Empty;

    /// <summary>NIST 800-53 control ID.</summary>
    [Required]
    [MaxLength(20)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>CAT severity: "CatI", "CatII", "CatIII".</summary>
    [Required]
    public string CatSeverity { get; set; } = string.Empty;

    /// <summary>Reason for the deviation request.</summary>
    [Required]
    [MaxLength(4000)]
    public string Justification { get; set; } = string.Empty;

    /// <summary>Description of compensating controls.</summary>
    [MaxLength(2000)]
    public string? CompensatingControls { get; set; }

    /// <summary>ScanImportRecord IDs used as evidence.</summary>
    public List<string>? EvidenceIds { get; set; }

    /// <summary>ISO-8601 expiration date.</summary>
    [Required]
    public DateTime ExpirationDate { get; set; }

    /// <summary>Review cycle: "90d", "180d", or "Annual". Default: "180d".</summary>
    [MaxLength(20)]
    public string ReviewCycle { get; set; } = "180d";

    /// <summary>ComplianceFinding ID to link.</summary>
    public string? FindingId { get; set; }

    /// <summary>POA&amp;M entry ID to link.</summary>
    public string? PoamEntryId { get; set; }

    /// <summary>AuthorizationBoundaryDefinition ID (waivers only).</summary>
    public string? BoundaryDefinitionId { get; set; }
}

/// <summary>
/// Input DTO for reviewing (approve/deny) a deviation.
/// </summary>
public class ReviewDeviationRequest
{
    /// <summary>Decision: "Approve" or "Deny".</summary>
    [Required]
    public string Decision { get; set; } = string.Empty;

    /// <summary>Optional reviewer comments.</summary>
    [MaxLength(2000)]
    public string? Comments { get; set; }
}

/// <summary>
/// Input DTO for revoking a deviation.
/// </summary>
public class RevokeDeviationRequest
{
    /// <summary>Reason for revocation.</summary>
    [Required]
    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Input DTO for extending a deviation's expiration date.
/// </summary>
public class ExtendDeviationRequest
{
    /// <summary>New expiration date (must not exceed MaxReviewCycleDays from today).</summary>
    [Required]
    public DateTime NewExpirationDate { get; set; }

    /// <summary>Updated justification for the extension.</summary>
    [MaxLength(4000)]
    public string? Justification { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Response / Detail DTOs
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Summary row for deviation list views.
/// </summary>
public class DeviationListItem
{
    /// <summary>Deviation ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Deviation type.</summary>
    public string DeviationType { get; set; } = string.Empty;

    /// <summary>NIST control ID.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>CAT severity value.</summary>
    public int CatSeverity { get; set; }

    /// <summary>Current status.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Justification text.</summary>
    public string Justification { get; set; } = string.Empty;

    /// <summary>Expiration date.</summary>
    public DateTime ExpirationDate { get; set; }

    /// <summary>Days until expiration (negative = expired).</summary>
    public int DaysUntilExpiration { get; set; }

    /// <summary>Who requested the deviation.</summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>When the deviation was requested.</summary>
    public DateTime RequestedAt { get; set; }

    /// <summary>Who reviewed the deviation.</summary>
    public string? ReviewedBy { get; set; }

    /// <summary>When the deviation was reviewed.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Number of evidence references.</summary>
    public int EvidenceCount { get; set; }

    /// <summary>Linked finding ID.</summary>
    public string? FindingId { get; set; }

    /// <summary>Linked POA&amp;M entry ID.</summary>
    public string? PoamEntryId { get; set; }

    /// <summary>Linked boundary definition ID.</summary>
    public string? BoundaryDefinitionId { get; set; }
}

/// <summary>
/// Paginated response for deviation list.
/// </summary>
public class DeviationListResponse
{
    /// <summary>Deviation items for the current page.</summary>
    public List<DeviationListItem> Items { get; set; } = [];

    /// <summary>Total count of matching deviations.</summary>
    public int TotalCount { get; set; }

    /// <summary>Current page number.</summary>
    public int Page { get; set; }

    /// <summary>Items per page.</summary>
    public int PageSize { get; set; }
}

/// <summary>
/// Summary counts for deviation metric cards.
/// </summary>
public class DeviationSummary
{
    /// <summary>Total deviations for the system.</summary>
    public int Total { get; set; }

    /// <summary>Pending review count.</summary>
    public int Pending { get; set; }

    /// <summary>Approved (active) count.</summary>
    public int Approved { get; set; }

    /// <summary>Denied count.</summary>
    public int Denied { get; set; }

    /// <summary>Expired count.</summary>
    public int Expired { get; set; }

    /// <summary>Revoked count.</summary>
    public int Revoked { get; set; }

    /// <summary>Count of deviations expiring within 30 days.</summary>
    public int ExpiringWithin30d { get; set; }

    /// <summary>Count of CAT I deviations.</summary>
    public int CatI { get; set; }

    /// <summary>Count of CAT II deviations.</summary>
    public int CatII { get; set; }

    /// <summary>Count of CAT III deviations.</summary>
    public int CatIII { get; set; }

    /// <summary>Count of deviations without evidence.</summary>
    public int WithoutEvidence { get; set; }
}

/// <summary>
/// Full deviation detail including linked entities and audit timeline.
/// </summary>
public class DeviationDetail
{
    /// <summary>Deviation ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Deviation type.</summary>
    public string DeviationType { get; set; } = string.Empty;

    /// <summary>NIST control ID.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>CAT severity value.</summary>
    public int CatSeverity { get; set; }

    /// <summary>Current status.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Justification text.</summary>
    public string Justification { get; set; } = string.Empty;

    /// <summary>Compensating controls description.</summary>
    public string? CompensatingControls { get; set; }

    /// <summary>Evidence reference IDs.</summary>
    public List<string> EvidenceReferences { get; set; } = [];

    /// <summary>Expiration date.</summary>
    public DateTime ExpirationDate { get; set; }

    /// <summary>Review cycle interval.</summary>
    public string ReviewCycle { get; set; } = string.Empty;

    /// <summary>Who requested the deviation.</summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>When the deviation was requested.</summary>
    public DateTime RequestedAt { get; set; }

    /// <summary>Who reviewed the deviation (final decision).</summary>
    public string? ReviewedBy { get; set; }

    /// <summary>When the deviation was reviewed.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Reviewer role: "ISSM" or "AO".</summary>
    public string? ReviewerRole { get; set; }

    /// <summary>Reviewer's comments.</summary>
    public string? ReviewerComments { get; set; }

    /// <summary>ISSM recommendation for CAT I deviations.</summary>
    public string? ISSMRecommendation { get; set; }

    /// <summary>ISSM who recommended.</summary>
    public string? ISSMRecommendedBy { get; set; }

    /// <summary>When the ISSM recommended.</summary>
    public DateTime? ISSMRecommendedAt { get; set; }

    /// <summary>Who revoked the deviation.</summary>
    public string? RevokedBy { get; set; }

    /// <summary>When the deviation was revoked.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Reason for revocation.</summary>
    public string? RevocationReason { get; set; }

    /// <summary>Linked boundary definition ID.</summary>
    public string? BoundaryDefinitionId { get; set; }

    /// <summary>Linked boundary definition name.</summary>
    public string? BoundaryDefinitionName { get; set; }

    /// <summary>Linked finding summary.</summary>
    public DeviationFindingRef? Finding { get; set; }

    /// <summary>Linked POA&amp;M entry summary.</summary>
    public DeviationPoamRef? PoamEntry { get; set; }

    /// <summary>Hydrated evidence details.</summary>
    public List<DeviationEvidenceRef> Evidence { get; set; } = [];

    /// <summary>Audit trail for this deviation.</summary>
    public List<DeviationAuditEntry> AuditTrail { get; set; } = [];
}

/// <summary>
/// Linked finding reference for deviation detail.
/// </summary>
public class DeviationFindingRef
{
    /// <summary>Finding ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>NIST control ID.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Current finding status.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Finding severity.</summary>
    public string Severity { get; set; } = string.Empty;
}

/// <summary>
/// Linked POA&amp;M reference for deviation detail.
/// </summary>
public class DeviationPoamRef
{
    /// <summary>POA&amp;M entry ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Weakness description.</summary>
    public string Weakness { get; set; } = string.Empty;

    /// <summary>Current POA&amp;M status.</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Hydrated evidence reference for deviation detail.
/// </summary>
public class DeviationEvidenceRef
{
    /// <summary>ScanImportRecord ID.</summary>
    public string ScanImportRecordId { get; set; } = string.Empty;

    /// <summary>Original file name.</summary>
    public string? FileName { get; set; }

    /// <summary>Scan type (CKL, XCCDF, PRISMA).</summary>
    public string? ScanType { get; set; }

    /// <summary>Date of the scan.</summary>
    public DateTime? ScanDate { get; set; }

    /// <summary>Benchmark title.</summary>
    public string? BenchmarkTitle { get; set; }
}

/// <summary>
/// Audit trail entry for deviation lifecycle events.
/// </summary>
public class DeviationAuditEntry
{
    /// <summary>Event type (e.g., "DeviationCreated", "DeviationApproved").</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Actor who performed the action.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the event.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Human-readable summary of the event.</summary>
    public string Summary { get; set; } = string.Empty;
}
