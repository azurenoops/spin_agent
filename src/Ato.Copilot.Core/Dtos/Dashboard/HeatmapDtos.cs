namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Heatmap data for control family compliance visualization.
/// </summary>
public class HeatmapDto
{
    /// <summary>System identifier.</summary>
    public required string SystemId { get; init; }

    /// <summary>Baseline level.</summary>
    public required string BaselineLevel { get; init; }

    /// <summary>Per-family compliance data.</summary>
    public required IReadOnlyList<HeatmapFamilyDto> Families { get; init; }
}

/// <summary>
/// Compliance data for a single NIST control family.
/// </summary>
public class HeatmapFamilyDto
{
    /// <summary>NIST family code (e.g., AC, AU).</summary>
    public required string FamilyCode { get; init; }

    /// <summary>Family name (e.g., Access Control).</summary>
    public required string FamilyName { get; init; }

    /// <summary>Total controls in baseline for this family.</summary>
    public int TotalControls { get; init; }

    /// <summary>Controls that have been assessed.</summary>
    public int AssessedControls { get; init; }

    /// <summary>Controls determined Satisfied.</summary>
    public int SatisfiedControls { get; init; }

    /// <summary>Compliance percentage (0-100).</summary>
    public double CompliancePercent { get; init; }

    /// <summary>Severity: green (&gt;=80%), yellow (50-79%), red (&lt;50%), gray (not assessed).</summary>
    public required string Severity { get; init; }
}

/// <summary>
/// Drill-down data for individual controls in a family.
/// </summary>
public class HeatmapControlsDto
{
    /// <summary>System identifier.</summary>
    public required string SystemId { get; init; }

    /// <summary>Family code.</summary>
    public required string FamilyCode { get; init; }

    /// <summary>Family name.</summary>
    public required string FamilyName { get; init; }

    /// <summary>Controls in this family.</summary>
    public required IReadOnlyList<HeatmapControlDto> Controls { get; init; }
}

/// <summary>
/// Individual control within a heatmap drill-down.
/// </summary>
public class HeatmapControlDto
{
    /// <summary>Control ID (e.g., AC-2).</summary>
    public required string ControlId { get; init; }

    /// <summary>Control title.</summary>
    public required string ControlTitle { get; init; }

    /// <summary>Compliance status.</summary>
    public required string ComplianceStatus { get; init; }

    /// <summary>Whether a narrative exists for this control.</summary>
    public bool HasNarrative { get; init; }

    /// <summary>Whether the narrative was manually customized.</summary>
    public bool IsManuallyCustomized { get; init; }

    /// <summary>Name of linked security capability (null if none).</summary>
    public string? SecurityCapabilityName { get; init; }

    /// <summary>DoD CAT severity when OtherThanSatisfied (null otherwise).</summary>
    public string? CatSeverity { get; init; }

    /// <summary>POA&M status for this control (null if no POA&M exists).</summary>
    public string? PoamStatus { get; init; }
}
