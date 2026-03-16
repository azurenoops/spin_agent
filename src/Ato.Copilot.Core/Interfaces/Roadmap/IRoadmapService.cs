using Ato.Copilot.Core.Models.Roadmap;

namespace Ato.Copilot.Core.Interfaces.Roadmap;

/// <summary>
/// Service interface for implementation roadmap operations.
/// Registered as Scoped — one instance per request for DB context alignment.
/// </summary>
public interface IRoadmapService
{
    /// <summary>Generates a phased implementation roadmap from gap analysis data.</summary>
    Task<ImplementationRoadmap> GenerateRoadmapAsync(
        string systemId,
        string createdBy,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the active roadmap for a system with optional item details.</summary>
    Task<ImplementationRoadmap?> GetRoadmapAsync(
        string systemId,
        bool includeItems = true,
        CancellationToken cancellationToken = default);

    /// <summary>Gets progress metrics for a system's active roadmap.</summary>
    Task<RoadmapProgressResult?> GetRoadmapProgressAsync(
        string systemId,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a roadmap item — move between phases, change role, update effort.</summary>
    Task<ImplementationRoadmap> UpdateRoadmapAsync(
        string systemId,
        RoadmapUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a Kanban board from a roadmap's items.</summary>
    Task<BoardFromRoadmapResult> CreateBoardFromRoadmapAsync(
        string systemId,
        string createdBy,
        CancellationToken cancellationToken = default);

    /// <summary>Syncs a roadmap item's status from a linked Kanban task status change.</summary>
    Task SyncRoadmapItemStatusAsync(
        string roadmapItemId,
        Models.Kanban.TaskStatus newTaskStatus,
        CancellationToken cancellationToken = default);

    /// <summary>Exports the active roadmap as a PDF byte array.</summary>
    Task<byte[]> ExportRoadmapPdfAsync(
        string systemId,
        CancellationToken cancellationToken = default);
}

/// <summary>Progress metrics for a roadmap.</summary>
public class RoadmapProgressResult
{
    /// <summary>The roadmap ID.</summary>
    public required string RoadmapId { get; init; }

    /// <summary>System display name.</summary>
    public required string SystemName { get; init; }

    /// <summary>Overall completion percentage.</summary>
    public double OverallCompletionPercent { get; init; }

    /// <summary>Number of completed items.</summary>
    public int ItemsCompleted { get; init; }

    /// <summary>Total number of items.</summary>
    public int ItemsTotal { get; init; }

    /// <summary>Projected total risk reduction percentage.</summary>
    public double ProjectedRiskReduction { get; init; }

    /// <summary>Actual risk reduction based on current gap analysis.</summary>
    public double ActualRiskReduction { get; init; }

    /// <summary>Risk curve data points.</summary>
    public required List<RiskCurvePoint> RiskCurve { get; init; }

    /// <summary>Per-phase progress.</summary>
    public required List<PhaseProgress> PhaseProgress { get; init; }

    /// <summary>Count of new gaps not tracked by the roadmap.</summary>
    public int UntrackedGaps { get; init; }
}

/// <summary>A single point on the risk reduction curve.</summary>
public class RiskCurvePoint
{
    /// <summary>Week number (0 = start).</summary>
    public int Week { get; init; }

    /// <summary>Remaining risk points at this week.</summary>
    public double RiskPoints { get; init; }

    /// <summary>Cumulative risk reduction percentage at this week.</summary>
    public double RiskReductionPercent { get; init; }
}

/// <summary>Per-phase progress metrics.</summary>
public class PhaseProgress
{
    /// <summary>Phase display name.</summary>
    public required string Name { get; init; }

    /// <summary>Phase display order.</summary>
    public int DisplayOrder { get; init; }

    /// <summary>Phase completion percentage.</summary>
    public double CompletionPercent { get; init; }

    /// <summary>Number of completed items.</summary>
    public int ItemsCompleted { get; init; }

    /// <summary>Total items in the phase.</summary>
    public int ItemsTotal { get; init; }

    /// <summary>Phase lifecycle status.</summary>
    public required string Status { get; init; }

    /// <summary>Whether the phase is overdue.</summary>
    public bool IsOverdue { get; init; }

    /// <summary>Days past target end date (0 if not overdue).</summary>
    public int DaysOverdue { get; init; }

    /// <summary>Projected risk reduction percent for this phase.</summary>
    public double ProjectedRiskReductionPercent { get; init; }

    /// <summary>Actual risk reduction percent for this phase.</summary>
    public double ActualRiskReductionPercent { get; init; }
}

/// <summary>Request payload for roadmap update operations.</summary>
public class RoadmapUpdateRequest
{
    /// <summary>Move an item to a different phase.</summary>
    public MoveItemRequest? MoveItem { get; init; }

    /// <summary>Update effort estimate for an item.</summary>
    public UpdateEffortRequest? UpdateEffort { get; init; }

    /// <summary>Update role assignment for an item.</summary>
    public UpdateRoleRequest? UpdateRole { get; init; }

    /// <summary>Merge two phases.</summary>
    public MergePhasesRequest? MergePhases { get; init; }

    /// <summary>Split a phase.</summary>
    public SplitPhaseRequest? SplitPhase { get; init; }
}

/// <summary>Move item to a different phase.</summary>
public class MoveItemRequest
{
    /// <summary>Control ID to move.</summary>
    public required string ControlId { get; init; }

    /// <summary>Target phase display order.</summary>
    public int TargetPhaseOrder { get; init; }
}

/// <summary>Update effort estimate.</summary>
public class UpdateEffortRequest
{
    /// <summary>Control ID to update.</summary>
    public required string ControlId { get; init; }

    /// <summary>New effort in person-days.</summary>
    public double EffortDays { get; init; }
}

/// <summary>Update role assignment.</summary>
public class UpdateRoleRequest
{
    /// <summary>Control ID to update.</summary>
    public required string ControlId { get; init; }

    /// <summary>New assigned role.</summary>
    public required string AssignedRole { get; init; }
}

/// <summary>Merge two phases.</summary>
public class MergePhasesRequest
{
    /// <summary>Source phase display order (will be removed).</summary>
    public int SourcePhaseOrder { get; init; }

    /// <summary>Target phase display order (receives items).</summary>
    public int TargetPhaseOrder { get; init; }
}

/// <summary>Split a phase.</summary>
public class SplitPhaseRequest
{
    /// <summary>Phase display order to split.</summary>
    public int PhaseOrder { get; init; }

    /// <summary>Split after this item index (0-based).</summary>
    public int SplitAfterItemIndex { get; init; }
}

/// <summary>Result of creating a Kanban board from a roadmap.</summary>
public class BoardFromRoadmapResult
{
    /// <summary>Created board ID.</summary>
    public required string BoardId { get; init; }

    /// <summary>Board display name.</summary>
    public required string BoardName { get; init; }

    /// <summary>Number of tasks created.</summary>
    public int TasksCreated { get; init; }

    /// <summary>Roadmap ID.</summary>
    public required string RoadmapId { get; init; }

    /// <summary>Number of phases mapped.</summary>
    public int PhasesMapped { get; init; }
}
