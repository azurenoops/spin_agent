namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Response DTO for compliance trend data.
/// </summary>
public class TrendDataDto
{
    /// <summary>System this trend data belongs to.</summary>
    public required string SystemId { get; init; }

    /// <summary>Aggregation granularity used (Daily, Weekly, Monthly, Quarterly).</summary>
    public required string Granularity { get; init; }

    /// <summary>Ordered data points for the trend chart.</summary>
    public required IReadOnlyList<TrendDataPointDto> DataPoints { get; init; }
}

/// <summary>
/// Single data point in the trend time-series.
/// </summary>
public class TrendDataPointDto
{
    /// <summary>Date of this data point (aggregation period start).</summary>
    public DateTime Date { get; init; }

    /// <summary>Compliance score (0–100) at this point.</summary>
    public double ComplianceScore { get; init; }

    /// <summary>Open CAT I findings.</summary>
    public int CatICount { get; init; }

    /// <summary>Open CAT II findings.</summary>
    public int CatIICount { get; init; }

    /// <summary>Open CAT III findings.</summary>
    public int CatIIICount { get; init; }

    /// <summary>Total open POA&amp;M items.</summary>
    public int OpenPoamCount { get; init; }

    /// <summary>Overdue POA&amp;M items.</summary>
    public int OverduePoamCount { get; init; }

    /// <summary>Narrative coverage percentage (0–100).</summary>
    public double NarrativeCoverage { get; init; }

    /// <summary>True if score dropped more than 5% from the previous point.</summary>
    public bool IsSignificantDecline { get; init; }
}

/// <summary>
/// Query parameters for the trends endpoint.
/// </summary>
public class TrendQuery
{
    /// <summary>Start date for the trend range (inclusive).</summary>
    public DateTime? StartDate { get; init; }

    /// <summary>End date for the trend range (inclusive).</summary>
    public DateTime? EndDate { get; init; }

    /// <summary>Aggregation granularity: Daily, Weekly, Monthly, Quarterly. Defaults to Daily.</summary>
    public string? Granularity { get; init; }
}
