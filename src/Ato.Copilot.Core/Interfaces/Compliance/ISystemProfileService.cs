using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for managing system profile sections, governance workflow,
/// completeness tracking, and business-context narrative drafts (Feature 046).
/// </summary>
public interface ISystemProfileService
{
    // ─── Profile Overview & Section Detail ────────────────────────────────

    /// <summary>
    /// Get the profile overview for a system, including all section statuses and completeness.
    /// Synthesizes <see cref="SspSectionStatus.NotStarted"/> entries for section types without a record.
    /// </summary>
    Task<ProfileOverviewResult> GetProfileOverviewAsync(
        string systemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detailed content for a specific profile section, including child entities.
    /// </summary>
    Task<SystemProfileSection?> GetSectionDetailAsync(
        string systemId,
        ProfileSectionType sectionType,
        CancellationToken cancellationToken = default);

    // ─── Draft Save ──────────────────────────────────────────────────────

    /// <summary>
    /// Save draft content for a specific profile section. Creates the section record on first save.
    /// Requires MissionOwner, SystemOwner, or Issm role for the system (FR-016).
    /// </summary>
    Task<SystemProfileSection> SaveDraftAsync(
        string systemId,
        ProfileSectionType sectionType,
        string? draftContent,
        string userId,
        RmfRole? simulatedRole = null,
        CancellationToken cancellationToken = default);

    // ─── Governance Workflow ─────────────────────────────────────────────

    /// <summary>
    /// Submit one or more profile sections for ISSM review (Draft/NeedsRevision → UnderReview).
    /// Requires MissionOwner role (FR-021).
    /// </summary>
    Task<SubmitResult> SubmitForReviewAsync(
        string systemId,
        IEnumerable<ProfileSectionType>? sectionTypes,
        string userId,
        RmfRole? simulatedRole = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraw one or more sections from review (UnderReview → Draft).
    /// Requires MissionOwner role (FR-021a).
    /// </summary>
    Task<WithdrawResult> WithdrawSectionAsync(
        string systemId,
        IEnumerable<ProfileSectionType>? sectionTypes,
        string userId,
        RmfRole? simulatedRole = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approve or request revision of a submitted profile section (ISSM-only, FR-023).
    /// </summary>
    Task<SystemProfileSection> ReviewSectionAsync(
        string systemId,
        ProfileSectionType sectionType,
        ReviewDecision decision,
        string reviewerId,
        string? comments = null,
        RmfRole? simulatedRole = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-approve all UnderReview profile sections for a system (ISSM-only, FR-024).
    /// </summary>
    Task<BatchApproveResult> BatchApproveSectionsAsync(
        string systemId,
        string reviewerId,
        RmfRole? simulatedRole = null,
        CancellationToken cancellationToken = default);

    // ─── Completeness & Todos ────────────────────────────────────────────

    /// <summary>
    /// Get profile completeness metrics using 5-mandatory denominator (R11).
    /// </summary>
    Task<ProfileCompletenessResult> GetCompletenessAsync(
        string systemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get profile tasks for the Mission Owner's To Do panel (FR-036).
    /// </summary>
    Task<ProfileTodosResult> GetProfileTodosAsync(
        string systemId,
        string userId,
        CancellationToken cancellationToken = default);

    // ─── Cross-System Review Queue (FR-027) ──────────────────────────────

    /// <summary>
    /// Get all UnderReview profile sections across systems where the caller has Issm role.
    /// </summary>
    Task<List<PendingReviewItem>> GetPendingReviewsAsync(
        string issmUserId,
        CancellationToken cancellationToken = default);

    // ─── Business Context ────────────────────────────────────────────────

    /// <summary>
    /// Save a Mission Owner's business-context narrative draft for a control (FR-028).
    /// </summary>
    Task<BusinessContextDraft> SaveBusinessContextAsync(
        string systemId,
        string controlId,
        string content,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the business-context draft for a specific control on a system.
    /// </summary>
    Task<BusinessContextDraft?> GetBusinessContextAsync(
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all controls flagged for business-context input on a system (static -1 list + ISSM overrides).
    /// </summary>
    Task<List<FlaggedControlItem>> GetFlaggedControlsAsync(
        string systemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flag or unflag a control for business-context input (ISSM-only, FR-028).
    /// </summary>
    Task SetControlFlagAsync(
        string systemId,
        string controlId,
        bool isFlagged,
        string userId,
        CancellationToken cancellationToken = default);
}

// ─── Result DTOs ─────────────────────────────────────────────────────────

/// <summary>Profile overview with section statuses and completeness.</summary>
public class ProfileOverviewResult
{
    /// <summary>System GUID.</summary>
    public string SystemId { get; set; } = string.Empty;
    /// <summary>System display name.</summary>
    public string SystemName { get; set; } = string.Empty;
    /// <summary>Assigned Mission Owner info, or null if none assigned.</summary>
    public MissionOwnerInfo? MissionOwner { get; set; }
    /// <summary>Overall completeness across all sections.</summary>
    public OverallCompleteness OverallCompleteness { get; set; } = new();
    /// <summary>Per-section summaries (always 6 entries — synthesized NotStarted for missing records).</summary>
    public List<SectionSummary> Sections { get; set; } = [];
}

/// <summary>Assigned Mission Owner identity.</summary>
public class MissionOwnerInfo
{
    /// <summary>User ID.</summary>
    public string UserId { get; set; } = string.Empty;
    /// <summary>Display name.</summary>
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>Completeness metrics.</summary>
public class OverallCompleteness
{
    /// <summary>Sections with content (any status except NotStarted).</summary>
    public int CompletedCount { get; set; }
    /// <summary>Total mandatory sections (always 5).</summary>
    public int MandatorySections { get; set; } = 5;
    /// <summary>Total sections including optional (always 6).</summary>
    public int AllSections { get; set; } = 6;
    /// <summary>Sections in Approved status.</summary>
    public int ApprovedCount { get; set; }
    /// <summary>Approved percentage based on mandatory sections.</summary>
    public int ApprovedPercentage { get; set; }
}

/// <summary>Per-section status summary.</summary>
public class SectionSummary
{
    /// <summary>Section type.</summary>
    public ProfileSectionType SectionType { get; set; }
    /// <summary>Governance status (NotStarted if no record exists).</summary>
    public SspSectionStatus GovernanceStatus { get; set; }
    /// <summary>Completion percentage of this section's fields.</summary>
    public int CompletionPercentage { get; set; }
    /// <summary>Last editor identity.</summary>
    public string? LastEditedBy { get; set; }
    /// <summary>Last edit timestamp.</summary>
    public DateTime? LastEditedAt { get; set; }
    /// <summary>Reviewer comments (if NeedsRevision).</summary>
    public string? ReviewerComments { get; set; }
}

/// <summary>Result of submitting sections for review.</summary>
public class SubmitResult
{
    /// <summary>Sections successfully submitted.</summary>
    public List<ProfileSectionType> SubmittedSections { get; set; } = [];
    /// <summary>Sections skipped with reasons.</summary>
    public List<SkippedSection> SkippedSections { get; set; } = [];
    /// <summary>Who submitted.</summary>
    public string SubmittedBy { get; set; } = string.Empty;
    /// <summary>When submitted.</summary>
    public DateTime SubmittedAt { get; set; }
}

/// <summary>Result of withdrawing sections from review.</summary>
public class WithdrawResult
{
    /// <summary>Sections successfully withdrawn.</summary>
    public List<ProfileSectionType> WithdrawnSections { get; set; } = [];
    /// <summary>Sections skipped with reasons.</summary>
    public List<SkippedSection> SkippedSections { get; set; } = [];
    /// <summary>Who withdrew.</summary>
    public string WithdrawnBy { get; set; } = string.Empty;
    /// <summary>When withdrawn.</summary>
    public DateTime WithdrawnAt { get; set; }
}

/// <summary>A section skipped during a batch operation.</summary>
public class SkippedSection
{
    /// <summary>Section type.</summary>
    public ProfileSectionType SectionType { get; set; }
    /// <summary>Reason for skipping.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Result of batch-approving sections.</summary>
public class BatchApproveResult
{
    /// <summary>Sections successfully approved.</summary>
    public List<ProfileSectionType> ApprovedSections { get; set; } = [];
    /// <summary>Sections skipped with reasons.</summary>
    public List<SkippedSection> SkippedSections { get; set; } = [];
    /// <summary>Count of approved sections.</summary>
    public int ApprovedCount { get; set; }
    /// <summary>Who approved.</summary>
    public string ReviewedBy { get; set; } = string.Empty;
    /// <summary>When approved.</summary>
    public DateTime ReviewedAt { get; set; }
}

/// <summary>Completeness metrics for dashboard display.</summary>
public class ProfileCompletenessResult
{
    /// <summary>System GUID.</summary>
    public string SystemId { get; set; } = string.Empty;
    /// <summary>Total mandatory sections (always 5).</summary>
    public int TotalSections { get; set; } = 5;
    /// <summary>Counts by governance status.</summary>
    public Dictionary<string, int> StatusCounts { get; set; } = new();
    /// <summary>Approved percentage based on mandatory sections.</summary>
    public int ApprovedPercentage { get; set; }
    /// <summary>Whether all 5 mandatory sections are Approved.</summary>
    public bool IsProfileComplete { get; set; }
    /// <summary>List of incomplete mandatory sections.</summary>
    public List<IncompleteSectionInfo> IncompleteSections { get; set; } = [];
    /// <summary>Whether a Mission Owner is assigned.</summary>
    public bool MissionOwnerAssigned { get; set; }
    /// <summary>Mission Owner display name.</summary>
    public string? MissionOwnerName { get; set; }
    /// <summary>Days since system registration.</summary>
    public int DaysSinceRegistration { get; set; }
}

/// <summary>Info about an incomplete section.</summary>
public class IncompleteSectionInfo
{
    /// <summary>Section type.</summary>
    public ProfileSectionType SectionType { get; set; }
    /// <summary>Current status.</summary>
    public SspSectionStatus Status { get; set; }
}

/// <summary>Profile todos for the Mission Owner's To Do panel.</summary>
public class ProfileTodosResult
{
    /// <summary>Whether the user has any profile tasks.</summary>
    public bool HasProfileTasks { get; set; }
    /// <summary>Sections in NotStarted or Draft status.</summary>
    public List<ProfileTodoItem> IncompleteSections { get; set; } = [];
    /// <summary>Sections in NeedsRevision status with ISSM feedback.</summary>
    public List<ProfileTodoItem> RevisionSections { get; set; } = [];
    /// <summary>Controls flagged for business context input.</summary>
    public List<FlaggedControlItem> FlaggedControls { get; set; } = [];
}

/// <summary>A single profile todo item.</summary>
public class ProfileTodoItem
{
    /// <summary>Section type.</summary>
    public ProfileSectionType SectionType { get; set; }
    /// <summary>Human-readable section name.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Current governance status.</summary>
    public SspSectionStatus Status { get; set; }
    /// <summary>ISSM feedback (for NeedsRevision).</summary>
    public string? ReviewerComments { get; set; }
}

/// <summary>A control flagged for business-context input.</summary>
public class FlaggedControlItem
{
    /// <summary>Control identifier, e.g., "AC-1".</summary>
    public string ControlId { get; set; } = string.Empty;
    /// <summary>Control title, e.g., "Access Control Policy and Procedures".</summary>
    public string ControlTitle { get; set; } = string.Empty;
    /// <summary>Whether the Mission Owner has already started a draft.</summary>
    public bool HasDraft { get; set; }
}

/// <summary>A pending review item for the cross-system review queue.</summary>
public class PendingReviewItem
{
    /// <summary>System GUID.</summary>
    public string SystemId { get; set; } = string.Empty;
    /// <summary>System display name.</summary>
    public string SystemName { get; set; } = string.Empty;
    /// <summary>Section type under review.</summary>
    public ProfileSectionType SectionType { get; set; }
    /// <summary>Who submitted.</summary>
    public string SubmittedBy { get; set; } = string.Empty;
    /// <summary>When submitted.</summary>
    public DateTime SubmittedAt { get; set; }
}
