namespace Ato.Copilot.Core.Models.Kanban;

/// <summary>
/// Kanban column status for a remediation task.
/// Represents the task's position in the workflow.
/// </summary>
public enum TaskStatus
{
    /// <summary>New findings awaiting triage.</summary>
    Backlog = 0,
    /// <summary>Triaged and ready to work.</summary>
    ToDo = 1,
    /// <summary>Currently being remediated.</summary>
    InProgress = 2,
    /// <summary>Remediation applied, awaiting validation.</summary>
    InReview = 3,
    /// <summary>Cannot proceed due to dependency or issue.</summary>
    Blocked = 4,
    /// <summary>Validated and closed.</summary>
    Done = 5
}

/// <summary>
/// Type of history event recorded on a remediation task.
/// </summary>
public enum HistoryEventType
{
    /// <summary>Task created (from assessment or manual).</summary>
    Created = 0,
    /// <summary>Task moved between columns.</summary>
    StatusChanged = 1,
    /// <summary>Task assigned or unassigned.</summary>
    Assigned = 2,
    /// <summary>Comment posted.</summary>
    CommentAdded = 3,
    /// <summary>Comment edited.</summary>
    CommentEdited = 4,
    /// <summary>Comment deleted.</summary>
    CommentDeleted = 5,
    /// <summary>Remediation script executed.</summary>
    RemediationAttempt = 6,
    /// <summary>Validation scan performed.</summary>
    ValidationRun = 7,
    /// <summary>Due date modified.</summary>
    DueDateChanged = 8,
    /// <summary>Severity level changed.</summary>
    SeverityChanged = 9,
    RelationshipLinked = 10,
    RelationshipUnlinked = 11,
    DetailsUpdated = 12,
    EvidenceLinked = 13,
    VerificationRecorded = 14
}

/// <summary>
/// Type of notification event that can be dispatched to users.
/// </summary>
public enum NotificationEventType
{
    /// <summary>A task was assigned to the user.</summary>
    TaskAssigned = 0,
    /// <summary>A task's status changed.</summary>
    StatusChanged = 1,
    /// <summary>A comment was added to a task.</summary>
    CommentAdded = 2,
    /// <summary>A task is overdue.</summary>
    TaskOverdue = 3,
    /// <summary>A task was closed.</summary>
    TaskClosed = 4,
    /// <summary>The user was @mentioned in a comment.</summary>
    Mentioned = 5,
    /// <summary>A PIM role was activated.</summary>
    PimRoleActivated = 6,
    /// <summary>A high-privilege PIM activation requires approval.</summary>
    PimApprovalRequired = 7,
    /// <summary>A PIM role or session is about to expire.</summary>
    PimRoleExpiring = 8,
    /// <summary>A high-privilege role activation warning for security leads.</summary>
    PimHighPrivilegeWarning = 9
}

/// <summary>
/// Notification delivery channel type.
/// </summary>
public enum NotificationChannelType
{
    /// <summary>Email notification via SMTP.</summary>
    Email = 0,
    /// <summary>Microsoft Teams notification via incoming webhook.</summary>
    Teams = 1,
    /// <summary>Slack notification via incoming webhook.</summary>
    Slack = 2
}
