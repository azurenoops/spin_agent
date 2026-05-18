using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Kanban;

// ─── Concurrency Base ────────────────────────────────────────────────────────

/// <summary>
/// Abstract base class for entities requiring optimistic concurrency control.
/// The RowVersion is regenerated on every save via AtoCopilotContext.SaveChangesAsync override.
/// </summary>
public abstract class ConcurrentEntity
{
    /// <summary>Optimistic concurrency token (Guid-based, per research R-001).</summary>
    public Guid RowVersion { get; set; } = Guid.NewGuid();
}

// ─── Persistent Entities ─────────────────────────────────────────────────────

/// <summary>
/// Represents a Kanban board grouping remediation tasks for a subscription.
/// </summary>
[TenantScoped]
public class RemediationBoard : ConcurrentEntity
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique board identifier (GUID format).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Board name (e.g., "Q1 2026 Audit").</summary>
    public string Name { get; set; } = "";

    /// <summary>Azure subscription ID this board is scoped to.</summary>
    public string SubscriptionId { get; set; } = "";

    /// <summary>Assessment that generated this board (optional FK).</summary>
    public string? AssessmentId { get; set; }

    /// <summary>User who created the board.</summary>
    public string Owner { get; set; } = "";

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC last-modified timestamp.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether board is archived.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Counter for sequential REM-NNN generation (starts at 1).</summary>
    public int NextTaskNumber { get; set; } = 1;

    /// <summary>Child remediation tasks.</summary>
    public List<RemediationTask> Tasks { get; set; } = new();
}

/// <summary>
/// Represents a single remediation work item (Kanban card).
/// </summary>
[TenantScoped]
public class RemediationTask : ConcurrentEntity
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique task identifier (GUID format).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Human-readable ID (e.g., REM-001).</summary>
    public string TaskNumber { get; set; } = "";

    /// <summary>Parent board FK.</summary>
    public string BoardId { get; set; } = "";

    /// <summary>Task title (e.g., "AC-2.1: Enable MFA").</summary>
    public string Title { get; set; } = "";

    /// <summary>Detailed finding description.</summary>
    public string Description { get; set; } = "";

    /// <summary>NIST control reference (e.g., AC-2.1).</summary>
    public string ControlId { get; set; } = "";

    /// <summary>Two-letter control family (e.g., "AC").</summary>
    public string ControlFamily { get; set; } = "";

    /// <summary>Task severity level (reuses existing FindingSeverity).</summary>
    public FindingSeverity Severity { get; set; } = FindingSeverity.Medium;

    /// <summary>Current Kanban column.</summary>
    public TaskStatus Status { get; set; } = TaskStatus.Backlog;

    /// <summary>Assigned user identifier.</summary>
    public string? AssigneeId { get; set; }

    /// <summary>Assigned user display name.</summary>
    public string? AssigneeName { get; set; }

    /// <summary>SLA-derived or manual due date.</summary>
    public DateTime DueDate { get; set; } = DateTime.UtcNow.AddDays(30);

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC last-modified timestamp.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Azure resource IDs affected by this finding.</summary>
    public List<string> AffectedResources { get; set; } = new();

    /// <summary>PowerShell/CLI script if available for remediation.</summary>
    public string? RemediationScript { get; set; }

    /// <summary>Script language identifier: "AzureCli", "PowerShell", "Bicep", "Terraform". Used for syntax highlighting.</summary>
    public string? RemediationScriptType { get; set; }

    /// <summary>How to verify the fix.</summary>
    public string? ValidationCriteria { get; set; }

    /// <summary>FK to ComplianceFinding.Id for traceability.</summary>
    public string? FindingId { get; set; }

    /// <summary>FK to ComplianceAlert.AlertId when task was created from an alert.</summary>
    public string? LinkedAlertId { get; set; }

    // ─── New Properties (Feature 015 — US8: POA&M Link) ─────────────────────

    /// <summary>Optional FK to PoamItem for formal POA&amp;M tracking.</summary>
    public string? PoamItemId { get; set; }

    /// <summary>Optional FK to RoadmapItem for bi-directional Kanban sync (Feature 031).</summary>
    public string? RoadmapItemId { get; set; }

    /// <summary>User who created the task.</summary>
    public string CreatedBy { get; set; } = "";

    /// <summary>Prevents repeat overdue notifications.</summary>
    public DateTime? LastOverdueNotifiedAt { get; set; }

    /// <summary>Child comments.</summary>
    public List<TaskComment> Comments { get; set; } = new();

    /// <summary>Child history entries.</summary>
    public List<TaskHistoryEntry> History { get; set; } = new();

    /// <summary>Navigation to parent board.</summary>
    public RemediationBoard? Board { get; set; }

    // ─── New Navigation (Feature 039 — POA&M Bidirectional Sync) ────────────

    /// <summary>Navigation to linked POA&amp;M item for bidirectional sync support.</summary>
    public PoamItem? PoamItem { get; set; }
}

/// <summary>
/// A threaded comment on a remediation task.
/// Supports single-level threading, @mentions, edit/delete windows, and soft deletion.
/// </summary>
[TenantScoped]
public class TaskComment
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique comment identifier (GUID format).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Parent task FK.</summary>
    public string TaskId { get; set; } = "";

    /// <summary>Comment author user ID.</summary>
    public string AuthorId { get; set; } = "";

    /// <summary>Comment author display name.</summary>
    public string AuthorName { get; set; } = "";

    /// <summary>Comment text (Markdown, max 4000 chars).</summary>
    public string Content { get; set; } = "";

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of last edit.</summary>
    public DateTime? EditedAt { get; set; }

    /// <summary>Whether comment has been edited.</summary>
    public bool IsEdited { get; set; }

    /// <summary>Soft delete flag (preserves audit trail).</summary>
    public bool IsDeleted { get; set; }

    /// <summary>True for auto-generated comments (validation results, status changes).</summary>
    public bool IsSystemComment { get; set; }

    /// <summary>Parent comment ID for single-level threading.</summary>
    public string? ParentCommentId { get; set; }

    /// <summary>@mentioned user IDs extracted from content.</summary>
    public List<string> Mentions { get; set; } = new();

    /// <summary>Navigation to parent task.</summary>
    public RemediationTask? Task { get; set; }
}

/// <summary>
/// An immutable record of a change to a remediation task.
/// INSERT-only — no UPDATE or DELETE operations (except cascade from parent).
/// </summary>
[TenantScoped]
public class TaskHistoryEntry
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique history entry identifier (GUID format).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Parent task FK.</summary>
    public string TaskId { get; set; } = "";

    /// <summary>Type of change.</summary>
    public HistoryEventType EventType { get; set; }

    /// <summary>Previous value (e.g., old status).</summary>
    public string? OldValue { get; set; }

    /// <summary>New value (e.g., new status).</summary>
    public string? NewValue { get; set; }

    /// <summary>User who made the change.</summary>
    public string ActingUserId { get; set; } = "";

    /// <summary>Display name of acting user.</summary>
    public string ActingUserName { get; set; } = "";

    /// <summary>UTC timestamp of the change.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Additional context (blocker reason, validation results, error messages).</summary>
    public string? Details { get; set; }

    /// <summary>Navigation to parent task.</summary>
    public RemediationTask? Task { get; set; }
}

// ─── Ephemeral Models (IAgentStateManager) ───────────────────────────────────

/// <summary>
/// A named filter combination stored per user in IAgentStateManager.
/// </summary>
public class SavedView
{
    /// <summary>View name (e.g., "My Critical Items").</summary>
    public string Name { get; set; } = "";

    /// <summary>User who created the view.</summary>
    public string OwnerId { get; set; } = "";

    /// <summary>Board this view applies to.</summary>
    public string BoardId { get; set; } = "";

    /// <summary>Filter criteria.</summary>
    public ViewFilters Filters { get; set; } = new();

    /// <summary>When the view was saved.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Filter criteria for a saved view.
/// </summary>
public class ViewFilters
{
    /// <summary>Filter by assignee.</summary>
    public string? AssigneeId { get; set; }

    /// <summary>Filter by severity levels.</summary>
    public List<FindingSeverity>? Severities { get; set; }

    /// <summary>Filter by control families.</summary>
    public List<string>? ControlFamilies { get; set; }

    /// <summary>Filter by Kanban columns.</summary>
    public List<TaskStatus>? Statuses { get; set; }

    /// <summary>Filter by due date range start.</summary>
    public DateTime? DueDateFrom { get; set; }

    /// <summary>Filter by due date range end.</summary>
    public DateTime? DueDateTo { get; set; }

    /// <summary>Filter by creation date range start.</summary>
    public DateTime? CreatedFrom { get; set; }

    /// <summary>Filter by creation date range end.</summary>
    public DateTime? CreatedTo { get; set; }

    /// <summary>Filter for overdue tasks only.</summary>
    public bool? IsOverdue { get; set; }
}

/// <summary>
/// Per-user notification preferences stored in IAgentStateManager.
/// </summary>
public class NotificationConfig
{
    /// <summary>User identifier.</summary>
    public string UserId { get; set; } = "";

    /// <summary>Notification channel type.</summary>
    public NotificationChannelType ChannelType { get; set; }

    /// <summary>Email address or webhook URL.</summary>
    public string ChannelAddress { get; set; } = "";

    /// <summary>Which events trigger notifications.</summary>
    public List<NotificationEventType> EnabledEvents { get; set; } = new();

    /// <summary>Master enable/disable toggle.</summary>
    public bool IsEnabled { get; set; }
}

// ─── Enrichment Result Models (Feature 012) ─────────────────────────────────

/// <summary>
/// Result of enriching a single task with remediation script and validation criteria.
/// Not persisted to database — used as a return value from ITaskEnrichmentService.
/// </summary>
public class TaskEnrichmentResult
{
    /// <summary>Task GUID.</summary>
    public string TaskId { get; set; } = "";

    /// <summary>Human-readable task number (e.g., REM-001).</summary>
    public string TaskNumber { get; set; } = "";

    /// <summary>Whether a remediation script was generated or updated.</summary>
    public bool ScriptGenerated { get; set; }

    /// <summary>Whether validation criteria was generated or updated.</summary>
    public bool ValidationCriteriaGenerated { get; set; }

    /// <summary>How the content was generated: "AI" or "Template".</summary>
    public string GenerationMethod { get; set; } = "";

    /// <summary>Script type used (e.g., "AzureCli").</summary>
    public string? ScriptType { get; set; }

    /// <summary>Error message if enrichment failed for this task.</summary>
    public string? Error { get; set; }

    /// <summary>Whether enrichment was skipped (task already had script and force=false, or finding was null).</summary>
    public bool Skipped { get; set; }
}

/// <summary>
/// Aggregate result of enriching all tasks on a board.
/// Not persisted to database — used as a return value from ITaskEnrichmentService.
/// </summary>
public class BoardEnrichmentResult
{
    /// <summary>Board GUID.</summary>
    public string BoardId { get; set; } = "";

    /// <summary>Number of tasks that received new enrichment content.</summary>
    public int TasksEnriched { get; set; }

    /// <summary>Number of tasks skipped (already had content).</summary>
    public int TasksSkipped { get; set; }

    /// <summary>Number of tasks where enrichment failed.</summary>
    public int TasksFailed { get; set; }

    /// <summary>Total tasks on the board.</summary>
    public int TotalTasks { get; set; }

    /// <summary>Wall-clock duration of the enrichment operation.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Per-task enrichment results.</summary>
    public List<TaskEnrichmentResult> Results { get; set; } = new();
}
