using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Dtos.Dashboard;

public sealed record RemediationPermissions(bool CanManageRemediation, string? Reason,
    bool CanCreateTasks = false, bool CanMoveTasks = false, bool CanMoveAnyTasks = false);
public sealed record RemediationOwner(string Id, string Name);
public sealed record RemediationCounts(int Findings, int Tasks, int Poams, int OpenFindings, int OpenTasks,
    int OpenPoams, int OverdueTasks, int OverduePoams);
public sealed record RemediationProvenance(string SourceId, string SourceName, string SourceType, AssessmentPlanPin? Plan);
public sealed record RemediationHistory(string Id, string EventType, string? OldValue, string? NewValue,
    string Actor, DateTime At, string? Details);
public sealed record RemediationEvidence(string Id, string Name, string ContentHash, DateTime LinkedAt, string LinkedBy);
public sealed record RemediationFindingRow(string Id, string Title, string Description, string ControlId,
    string Severity, string Status, string Source, string AssessmentId, string? ImportRecordId, DateTime DiscoveredAt,
    string[] TaskIds, string[] PoamIds, string? DeviationId, RemediationProvenance? Provenance);
public sealed record RemediationTaskRow(string Id, string TaskNumber, string BoardId, string Title, string Description,
    string ControlId, string Severity, string Status, string? AssigneeId, string? AssigneeName, DateTime DueDate,
    string? FindingId, string[] PoamIds, Guid RowVersion, string VerificationStatus, string? VerificationNotes,
    string? VerifiedBy, DateTime? VerifiedAt, RemediationEvidence[] Evidence, RemediationHistory[] History,
    string[] AllowedTransitions, string[] AffectedResources, string? ValidationCriteria,
    string? RemediationScript, string? RemediationScriptType);
public sealed record RemediationMilestone(string Id, string Description, DateTime TargetDate, DateTime? CompletedDate, int Sequence);
public sealed record RemediationPoamRow(string Id, string PoamId, string Weakness, string WeaknessSource,
    string SecurityControlNumber, string CatSeverity, string Status, string PointOfContact, string? PocEmail,
    DateTime ScheduledCompletionDate, DateTime? ActualCompletionDate, string? FindingId, string[] TaskIds,
    Guid RowVersion, RemediationMilestone[] Milestones, RemediationHistory[] History, string? DeviationId,
    string? Comments, string? ResourcesRequired, decimal? CostEstimate, DateTime CreatedAt, DateTime? ModifiedAt);
public sealed record RemediationExceptionRow(string Id, string Type, string Status, string ControlId,
    string Justification, DateTime ExpirationDate, bool IsEffective, string? FindingId, string? PoamEntryId,
    string? ReviewedBy, string? ReviewerRole, DateTime? ReviewedAt, string? CompensatingControls);
public sealed record RemediationWorkspace(string SystemId, RemediationFindingRow[] Findings,
    RemediationTaskRow[] Tasks, RemediationPoamRow[] Poams, RemediationExceptionRow[] Exceptions,
    RemediationPermissions Permissions, RemediationCounts Counts, RemediationOwner[] Owners);
public sealed record CreateRemediationFinding(string RequestId, string Title, string Description, string ControlId, string Severity);
public sealed record CreateWorkspaceTask(string RequestId, string Title, string Description, string ControlId,
    string Severity, string? FindingId = null, DateTime? DueDate = null);
public sealed record UpdateWorkspaceTask(Guid RowVersion, string Title, string Description,
    DateTime DueDate, string? AssigneeId = null, string? AssigneeName = null);
public sealed record MoveWorkspaceTask(Guid RowVersion, string Status, string? Comment = null, bool SkipValidation = false);
public sealed record LinkTaskEvidence(Guid RowVersion, string EvidenceId);
public sealed record VerifyWorkspaceTask(Guid RowVersion, string Status, string Notes);
public sealed record RemediationPairRevision(Guid ExpectedPoamRevision, Guid ExpectedTaskRevision);
public sealed record LinkFindingTaskRequest(Guid RowVersion);
