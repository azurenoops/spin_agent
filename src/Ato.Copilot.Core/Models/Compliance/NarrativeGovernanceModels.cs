using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

// ─── Enums ───────────────────────────────────────────────────────────────────

/// <summary>Decision rendered by a reviewer on a narrative version.</summary>
public enum ReviewDecision
{
    /// <summary>Narrative approved as-is.</summary>
    Approve,
    /// <summary>Reviewer requests changes before approval.</summary>
    RequestRevision
}

// ─── Entities ────────────────────────────────────────────────────────────────

/// <summary>
/// Immutable snapshot of a control-implementation narrative at a specific version.
/// Each edit creates a new NarrativeVersion row; the parent ControlImplementation
/// tracks the current version number.
/// </summary>
/// <remarks>Feature 024 – Narrative Governance.</remarks>
[TenantScoped]
public class NarrativeVersion
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the parent ControlImplementation.</summary>
    [Required]
    [MaxLength(36)]
    public string ControlImplementationId { get; set; } = string.Empty;

    /// <summary>Monotonically increasing version number (1-based).</summary>
    public int VersionNumber { get; set; } = 1;

    /// <summary>Full narrative text captured at this version (up to 8 000 chars).</summary>
    [Required]
    [MaxLength(8000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>Lifecycle status of this version snapshot.</summary>
    public SspSectionStatus Status { get; set; } = SspSectionStatus.Draft;

    /// <summary>User who authored this version.</summary>
    [Required]
    [MaxLength(200)]
    public string AuthoredBy { get; set; } = string.Empty;

    /// <summary>When this version was created (UTC).</summary>
    public DateTime AuthoredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional reason / commit-message for the change.</summary>
    [MaxLength(1000)]
    public string? ChangeReason { get; set; }

    /// <summary>User who submitted this version for review (null if not yet submitted).</summary>
    [MaxLength(200)]
    public string? SubmittedBy { get; set; }

    /// <summary>When this version was submitted for review (UTC, null if not yet submitted).</summary>
    public DateTime? SubmittedAt { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────

    /// <summary>Parent control implementation.</summary>
    public ControlImplementation ControlImplementation { get; set; } = null!;

    /// <summary>Reviews attached to this version.</summary>
    public ICollection<NarrativeReview> Reviews { get; set; } = new List<NarrativeReview>();
}

/// <summary>
/// A single review decision recorded against a <see cref="NarrativeVersion"/>.
/// Multiple reviews can exist per version (e.g. request-revision then approve after edits).
/// </summary>
/// <remarks>Feature 024 – Narrative Governance.</remarks>
[TenantScoped]
public class NarrativeReview
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID string).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the reviewed NarrativeVersion.</summary>
    [Required]
    [MaxLength(36)]
    public string NarrativeVersionId { get; set; } = string.Empty;

    /// <summary>User who performed the review.</summary>
    [Required]
    [MaxLength(200)]
    public string ReviewedBy { get; set; } = string.Empty;

    /// <summary>Approve or RequestRevision.</summary>
    public ReviewDecision Decision { get; set; }

    /// <summary>Optional reviewer comments / feedback.</summary>
    [MaxLength(2000)]
    public string? ReviewerComments { get; set; }

    /// <summary>When the review was recorded (UTC).</summary>
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;

    // ─── Navigation ──────────────────────────────────────────────────────

    /// <summary>The version that was reviewed.</summary>
    public NarrativeVersion NarrativeVersion { get; set; } = null!;
}

// ─── DTOs ────────────────────────────────────────────────────────────────────

/// <summary>Diff between two narrative versions.</summary>
public class NarrativeDiff
{
    /// <summary>From-version number.</summary>
    public int FromVersion { get; set; }

    /// <summary>To-version number.</summary>
    public int ToVersion { get; set; }

    /// <summary>Unified diff text.</summary>
    public string UnifiedDiff { get; set; } = string.Empty;

    /// <summary>Lines added.</summary>
    public int LinesAdded { get; set; }

    /// <summary>Lines removed.</summary>
    public int LinesRemoved { get; set; }
}

/// <summary>Governance-level progress for a single control family.</summary>
public class GovernanceFamilyProgress
{
    /// <summary>Control family prefix (e.g. "AC").</summary>
    public string Family { get; set; } = string.Empty;

    /// <summary>Total controls in the family.</summary>
    public int Total { get; set; }

    /// <summary>Controls with approved narratives.</summary>
    public int Approved { get; set; }

    /// <summary>Controls with narratives under review.</summary>
    public int UnderReview { get; set; }

    /// <summary>Controls with draft narratives.</summary>
    public int Draft { get; set; }

    /// <summary>Controls needing revision.</summary>
    public int NeedsRevision { get; set; }

    /// <summary>Controls with no narrative at all.</summary>
    public int NotStarted { get; set; }
}

/// <summary>Full governance progress report for a registered system.</summary>
public class GovernanceProgressReport
{
    /// <summary>System ID.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>Overall approval percentage (0-100).</summary>
    public double OverallApprovalPercent { get; set; }

    /// <summary>Per-family breakdown.</summary>
    public List<GovernanceFamilyProgress> FamilyBreakdowns { get; set; } = new();

    /// <summary>Total controls.</summary>
    public int TotalControls { get; set; }

    /// <summary>Total approved.</summary>
    public int TotalApproved { get; set; }

    /// <summary>Total under review.</summary>
    public int TotalUnderReview { get; set; }

    /// <summary>Total draft.</summary>
    public int TotalDraft { get; set; }

    /// <summary>Total needing revision.</summary>
    public int TotalNeedsRevision { get; set; }

    /// <summary>Total not started.</summary>
    public int TotalNotStarted { get; set; }

    /// <summary>Control IDs currently in UnderReview status (the review queue).</summary>
    public List<string> ReviewQueue { get; set; } = new();

    /// <summary>Staleness warnings for controls with unapproved drafts under an approved SSP.</summary>
    public List<StalenessWarning> StalenessWarnings { get; set; } = new();
}

/// <summary>Warning about a stale/unapproved narrative.</summary>
public class StalenessWarning
{
    /// <summary>Control identifier.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Human-readable warning message.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Result of a batch submit-for-review operation.</summary>
public class BatchSubmitResult
{
    /// <summary>Number of narratives successfully submitted.</summary>
    public int SubmittedCount { get; set; }

    /// <summary>Number of narratives skipped (already under review or approved).</summary>
    public int SkippedCount { get; set; }

    /// <summary>Control IDs that were submitted.</summary>
    public List<string> SubmittedControlIds { get; set; } = new();

    /// <summary>Control IDs that were skipped with reasons.</summary>
    public List<string> SkippedReasons { get; set; } = new();
}
