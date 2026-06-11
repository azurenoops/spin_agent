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

    // ─── Frontend-facing per-control gap shape (US4 / GapAnalysis.tsx) ───────

    /// <summary>Total number of per-control gap items (equals Items.Count).</summary>
    public int TotalGaps { get; set; }

    /// <summary>Count of items with Severity == "Critical".</summary>
    public int CriticalCount { get; set; }

    /// <summary>Count of items with Severity == "High".</summary>
    public int HighCount { get; set; }

    /// <summary>Count of items with Severity == "Moderate".</summary>
    public int ModerateCount { get; set; }

    /// <summary>Count of items with Severity == "Low".</summary>
    public int LowCount { get; set; }

    /// <summary>Per-control gap items for the Gap Analysis dashboard table.</summary>
    public List<GapItemDto> Items { get; set; } = [];
}

/// <summary>
/// A single per-control gap item surfaced on the Gap Analysis dashboard page.
/// </summary>
public class GapItemDto
{
    /// <summary>NIST control identifier (e.g. "AC-2").</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Human-readable control title.</summary>
    public string ControlTitle { get; set; } = string.Empty;

    /// <summary>
    /// Reason this control is a gap:
    ///   NoNarrative   — ControlImplementation exists but Narrative is empty/null
    ///   NoEvidence    — Narrative present but no evidence attachments
    ///   FailingFinding — Open/failing scan finding exists for this control
    ///   NotImplemented — No ControlImplementation record at all
    /// </summary>
    public string GapType { get; set; } = string.Empty;

    /// <summary>
    /// Derived from NistControl.ImpactLevel:
    ///   High      → Critical
    ///   Moderate  → High
    ///   Low       → Moderate
    ///   (unknown) → Low
    /// </summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>Short human-readable explanation of the gap.</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>Responsible owner from the linked SecurityCapability, if any.</summary>
    public string? Responsible { get; set; }

    /// <summary>NIST control family code (e.g. "AC").</summary>
    public string Family { get; set; } = string.Empty;
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
