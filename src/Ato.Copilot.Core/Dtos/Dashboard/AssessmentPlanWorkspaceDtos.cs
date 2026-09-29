using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Dtos.Dashboard;

public sealed record AssessmentPlanWorkspace(
    string SystemId, string SystemName, string? BaselineLevel, int BaselineControlCount,
    AssessmentPlanDetail? Plan, IReadOnlyList<AssessmentPlanSummary> Plans,
    IReadOnlyList<AssessmentLeadOption> LeadOptions, IReadOnlyList<AssessmentPlanningTask> Tasks,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> FinalizationBlockers,
    AssessmentPlanPermissions Permissions);

public sealed record AssessmentPlanSummary(
    string Id, string Title, string Status, long Revision, DateTime GeneratedAt, DateTime? FinalizedAt);

public sealed record AssessmentPlanDetail(
    string Id, string Title, string Status, long Revision, string ContentHash,
    DateTime GeneratedAt, DateTime? UpdatedAt, DateTime? FinalizedAt,
    string? AssessmentLead, string? AssessmentLeadId, string? ScopeNotes, string? AssessmentApproach,
    string? RulesOfEngagement, DateTime? ScheduleStart, DateTime? ScheduleEnd, int ScopeCount,
    IReadOnlyList<AssessmentPlanControl> Controls, IReadOnlyList<SapTeamMemberInput> TeamMembers);

public sealed record AssessmentPlanControl(
    string ControlId, string Title, string Family, bool Included, string? ExclusionRationale,
    IReadOnlyList<string> Methods, string? MethodRationale, IReadOnlyList<string> Objectives);

public sealed record AssessmentLeadOption(string Id, string Name, string Kind, string? Organization);
public sealed record AssessmentPlanningTask(string Key, string Title, string Description,
    bool Complete, bool Required, string ActionLabel);
public sealed record AssessmentPlanPermissions(bool CanCreatePlan, bool CanEditPlan, bool CanFinalizePlan,
    string? CreateReason, string? EditReason, string? FinalizeReason);
public sealed record AssessmentPlanPreview(string SystemId, string SapId, long Revision, string ContentHash, string Content);
public sealed record CreateAssessmentPlanRequest(string RequestId, string? PreviousPlanId = null, string? ExpectedContentHash = null);
public sealed record FinalizeAssessmentPlanRequest(string ExpectedContentHash, long ExpectedRevision);
public sealed record UpdateAssessmentPlanRequest(
    string Task, string ExpectedContentHash, long ExpectedRevision,
    string? Title = null, string? AssessmentLeadId = null, string? ScopeNotes = null,
    List<string>? IncludedControlIds = null, Dictionary<string, string>? ExclusionReasons = null,
    string? AssessmentApproach = null, string? RulesOfEngagement = null,
    DateTime? ScheduleStart = null, DateTime? ScheduleEnd = null,
    List<SapTeamMemberInput>? TeamMembers = null, List<SapMethodOverrideInput>? MethodOverrides = null);
