namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Summary of a registered system for portfolio-level dashboard display.
/// </summary>
public class PortfolioSystemSummaryDto
{
    /// <summary>System identifier.</summary>
    public required string SystemId { get; init; }

    /// <summary>System name.</summary>
    public required string Name { get; init; }

    /// <summary>System acronym.</summary>
    public string? Acronym { get; init; }

    /// <summary>System type.</summary>
    public required string SystemType { get; init; }

    /// <summary>Mission criticality.</summary>
    public required string MissionCriticality { get; init; }

    /// <summary>Hosting environment.</summary>
    public required string HostingEnvironment { get; init; }

    /// <summary>System description.</summary>
    public string? Description { get; init; }

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

/// <summary>
/// Request body for registering a new system via the dashboard.
/// </summary>
public class RegisterSystemRequest
{
    public required string Name { get; init; }
    public required string SystemType { get; init; }
    public required string MissionCriticality { get; init; }
    public string? HostingEnvironment { get; init; }
    public string? Acronym { get; init; }
    public string? Description { get; init; }
    public string? CloudEnvironment { get; init; }
    public List<string>? SubscriptionIds { get; init; }
}

/// <summary>
/// Request body for updating a system via the dashboard.
/// </summary>
public class UpdateSystemRequest
{
    public string? Name { get; init; }
    public string? Acronym { get; init; }
    public string? SystemType { get; init; }
    public string? MissionCriticality { get; init; }
    public string? HostingEnvironment { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Request body for assigning an RMF role via the dashboard.
/// </summary>
public class AssignRoleRequest
{
    public required string Role { get; init; }
    public required string UserDisplayName { get; init; }
    public string? UserId { get; init; }
}

public class GenerateComponentDescriptionRequest
{
    public required string Name { get; init; }
    public required string ComponentType { get; init; }
    public string? SubType { get; init; }
}

public class GenerateCapabilityDescriptionRequest
{
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public string? Category { get; init; }
}

public class GenerateSystemDescriptionRequest
{
    public required string Name { get; init; }
    public required string SystemType { get; init; }
    public required string MissionCriticality { get; init; }
    public required string HostingEnvironment { get; init; }
}
