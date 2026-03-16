namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>Full roadmap DTO for dashboard API responses.</summary>
public class RoadmapDto
{
    /// <summary>Roadmap identifier.</summary>
    public required string RoadmapId { get; init; }

    /// <summary>System identifier.</summary>
    public required string SystemId { get; init; }

    /// <summary>System display name.</summary>
    public required string SystemName { get; init; }

    /// <summary>Roadmap lifecycle status.</summary>
    public required string Status { get; init; }

    /// <summary>Baseline level at time of generation.</summary>
    public required string BaselineLevel { get; init; }

    /// <summary>Total number of gaps.</summary>
    public int TotalGaps { get; init; }

    /// <summary>Total estimated effort in person-days.</summary>
    public double TotalEstimatedEffortDays { get; init; }

    /// <summary>Total weighted risk points.</summary>
    public double TotalRiskPoints { get; init; }

    /// <summary>Overall completion percentage.</summary>
    public double OverallCompletionPercent { get; init; }

    /// <summary>Roadmap phases.</summary>
    public required List<RoadmapPhaseDto> Phases { get; init; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>UTC last modification timestamp.</summary>
    public DateTime UpdatedAt { get; init; }
}

/// <summary>Phase summary DTO within a roadmap response.</summary>
public class RoadmapPhaseDto
{
    /// <summary>Phase identifier.</summary>
    public required string PhaseId { get; init; }

    /// <summary>Phase display name.</summary>
    public required string Name { get; init; }

    /// <summary>Sort order.</summary>
    public int DisplayOrder { get; init; }

    /// <summary>Estimated effort in person-days.</summary>
    public double EstimatedEffortDays { get; init; }

    /// <summary>Weighted risk points.</summary>
    public double RiskPoints { get; init; }

    /// <summary>Phase risk reduction percentage.</summary>
    public double RiskReductionPercent { get; init; }

    /// <summary>Target start week (1-based).</summary>
    public int? TargetStartWeek { get; init; }

    /// <summary>Target end week.</summary>
    public int? TargetEndWeek { get; init; }

    /// <summary>Phase lifecycle status.</summary>
    public required string Status { get; init; }

    /// <summary>Count of completed items.</summary>
    public int CompletedItemCount { get; init; }

    /// <summary>Count of total items.</summary>
    public int TotalItemCount { get; init; }

    /// <summary>Items in this phase (when includeItems=true).</summary>
    public List<RoadmapItemDto>? Items { get; init; }
}

/// <summary>Individual gap item DTO within a phase.</summary>
public class RoadmapItemDto
{
    /// <summary>Item identifier.</summary>
    public required string ItemId { get; init; }

    /// <summary>NIST control identifier.</summary>
    public required string ControlId { get; init; }

    /// <summary>Control title.</summary>
    public required string ControlTitle { get; init; }

    /// <summary>Control family.</summary>
    public required string ControlFamily { get; init; }

    /// <summary>Gap type.</summary>
    public required string GapType { get; init; }

    /// <summary>Severity level.</summary>
    public required string Severity { get; init; }

    /// <summary>Severity-based risk points.</summary>
    public double RiskPoints { get; init; }

    /// <summary>Estimated effort in person-days.</summary>
    public double EstimatedEffortDays { get; init; }

    /// <summary>Assigned role.</summary>
    public required string AssignedRole { get; init; }

    /// <summary>Control IDs this item depends on.</summary>
    public List<string>? DependsOn { get; init; }

    /// <summary>Item lifecycle status.</summary>
    public required string Status { get; init; }

    /// <summary>Linked Kanban task ID (null if not linked).</summary>
    public string? LinkedTaskId { get; init; }
}

/// <summary>Progress metrics DTO for dashboard API.</summary>
public class RoadmapProgressDto
{
    /// <summary>Roadmap identifier.</summary>
    public required string RoadmapId { get; init; }

    /// <summary>System display name.</summary>
    public required string SystemName { get; init; }

    /// <summary>Overall completion percentage.</summary>
    public double OverallCompletionPercent { get; init; }

    /// <summary>Number of completed items.</summary>
    public int ItemsCompleted { get; init; }

    /// <summary>Total number of items.</summary>
    public int ItemsTotal { get; init; }

    /// <summary>Risk curve data points.</summary>
    public required List<RiskCurvePointDto> RiskCurve { get; init; }

    /// <summary>Per-phase progress metrics.</summary>
    public required List<PhaseProgressDto> PhaseProgress { get; init; }
}

/// <summary>Single point on the risk reduction curve.</summary>
public class RiskCurvePointDto
{
    /// <summary>Week number (0 = start).</summary>
    public int Week { get; init; }

    /// <summary>Remaining risk points.</summary>
    public double RiskPoints { get; init; }

    /// <summary>Cumulative risk reduction percentage.</summary>
    public double RiskReductionPercent { get; init; }
}

/// <summary>Per-phase progress metrics for dashboard.</summary>
public class PhaseProgressDto
{
    /// <summary>Phase display name.</summary>
    public required string Name { get; init; }

    /// <summary>Phase display order.</summary>
    public int DisplayOrder { get; init; }

    /// <summary>Phase completion percentage.</summary>
    public double CompletionPercent { get; init; }

    /// <summary>Phase lifecycle status.</summary>
    public required string Status { get; init; }

    /// <summary>Actual risk reduction percentage.</summary>
    public double ActualRiskReductionPercent { get; init; }

    /// <summary>Whether the phase is overdue.</summary>
    public bool IsOverdue { get; init; }

    /// <summary>Days past target end date.</summary>
    public int DaysOverdue { get; init; }
}
