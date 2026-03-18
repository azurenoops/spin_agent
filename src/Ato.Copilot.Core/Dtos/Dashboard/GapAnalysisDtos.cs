namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Top-level gap analysis response for a system's baseline coverage.
/// </summary>
public class GapAnalysisDto
{
    public required string SystemId { get; init; }
    public required string BaselineLevel { get; init; }
    public int TotalBaselineControls { get; init; }
    public int CoveredControls { get; init; }
    public int WaivedControls { get; init; }
    public int GapCount { get; init; }
    public double CoveragePercent { get; init; }
    public required List<GapFamilyBreakdownDto> FamilyBreakdown { get; init; }
    /// <summary>
    /// Per-boundary comparison summary. Populated when no boundaryDefinitionId filter is specified.
    /// </summary>
    public List<BoundaryComparisonItemDto>? BoundaryComparison { get; init; }
}

/// <summary>
/// Per-boundary coverage summary for the comparison table.
/// </summary>
public class BoundaryComparisonItemDto
{
    public required string BoundaryId { get; init; }
    public required string BoundaryName { get; init; }
    public required string BoundaryType { get; init; }
    public bool IsPrimary { get; init; }
    public int TotalControls { get; init; }
    public int CoveredControls { get; init; }
    public int WaivedControls { get; init; }
    public int GapCount { get; init; }
    public double CoveragePercent { get; init; }
}

/// <summary>
/// Per-family breakdown within the gap analysis.
/// </summary>
public class GapFamilyBreakdownDto
{
    public required string FamilyCode { get; init; }
    public required string FamilyName { get; init; }
    public int TotalControls { get; init; }
    public int CoveredControls { get; init; }
    public int WaivedControls { get; init; }
    public int GapCount { get; init; }
    public double CoveragePercent { get; init; }
    public bool IsBelow50 { get; init; }
    public required List<UnmappedControlDto> UnmappedControls { get; init; }
    public List<string> WaivedControlIds { get; init; } = [];
}

/// <summary>
/// A single control that has no capability mapping.
/// </summary>
public class UnmappedControlDto
{
    public required string ControlId { get; init; }
    public required string ControlTitle { get; init; }
}
