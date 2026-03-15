namespace Ato.Copilot.Mcp.Dtos.Dashboard;

/// <summary>
/// Summary of a registered system for portfolio-level dashboard display.
/// </summary>
public class PortfolioSystemSummaryDto
{
    /// <summary>System identifier.</summary>
    public required string SystemId { get; init; }

    /// <summary>System name.</summary>
    public required string Name { get; init; }

    /// <summary>FIPS 199 impact level (Low, Moderate, High).</summary>
    public required string ImpactLevel { get; init; }

    /// <summary>Current RMF lifecycle phase.</summary>
    public required string CurrentRmfPhase { get; init; }

    /// <summary>Overall compliance score (0-100).</summary>
    public double ComplianceScore { get; init; }

    /// <summary>Change in score since prior assessment (negative = decline).</summary>
    public double ComplianceScoreDelta { get; init; }

    /// <summary>ATO expiration date (null if no ATO).</summary>
    public DateTime? AtoExpirationDate { get; init; }

    /// <summary>ATO status (Active, Expired, None).</summary>
    public required string AtoStatus { get; init; }

    /// <summary>Days remaining until ATO expires (null if no ATO).</summary>
    public int? AtoDaysRemaining { get; init; }

    /// <summary>ATO severity indicator: green (&gt;90d), yellow (30-90d), red (&lt;30d), expired, none.</summary>
    public required string AtoSeverity { get; init; }

    /// <summary>Total open POA&amp;M items.</summary>
    public int OpenPoamCount { get; init; }

    /// <summary>POA&amp;M items past scheduled completion date.</summary>
    public int OverduePoamCount { get; init; }

    /// <summary>Open CAT I findings count.</summary>
    public int CatICounts { get; init; }

    /// <summary>Open CAT II findings count.</summary>
    public int CatIICounts { get; init; }

    /// <summary>Open CAT III findings count.</summary>
    public int CatIIICounts { get; init; }
}

/// <summary>
/// Query parameters for the portfolio endpoint.
/// </summary>
public class PortfolioQuery : PaginationQuery
{
    /// <summary>Sort column: name, impactLevel, rmfPhase, complianceScore, atoExpiration, openPoamCount.</summary>
    public string? SortBy { get; init; }

    /// <summary>Sort direction: asc or desc.</summary>
    public string? SortDir { get; init; }

    /// <summary>Optional filter by impact level (Low, Moderate, High).</summary>
    public string? ImpactLevel { get; init; }

    /// <summary>Optional filter by RMF phase.</summary>
    public string? RmfPhase { get; init; }
}
