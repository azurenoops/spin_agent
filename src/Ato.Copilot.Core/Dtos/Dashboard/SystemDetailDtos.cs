namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Full dashboard data for a single system.
/// </summary>
public class SystemDetailDto
{
    /// <summary>System identifier.</summary>
    public required string SystemId { get; init; }

    /// <summary>System name.</summary>
    public required string Name { get; init; }

    /// <summary>System acronym.</summary>
    public string? Acronym { get; init; }

    /// <summary>System type (e.g. MajorApplication).</summary>
    public required string SystemType { get; init; }

    /// <summary>Mission criticality designation.</summary>
    public required string MissionCriticality { get; init; }

    /// <summary>Hosting environment.</summary>
    public required string HostingEnvironment { get; init; }

    /// <summary>FIPS 199 impact level.</summary>
    public required string ImpactLevel { get; init; }

    /// <summary>Baseline level (Low/Moderate/High).</summary>
    public required string BaselineLevel { get; init; }

    /// <summary>Current RMF phase.</summary>
    public required string CurrentRmfPhase { get; init; }

    /// <summary>Progress through all 7 RMF phases.</summary>
    public required IReadOnlyList<RmfPhaseProgressDto> RmfPhaseProgress { get; init; }

    /// <summary>Key compliance metrics.</summary>
    public required KeyMetricsDto KeyMetrics { get; init; }

    /// <summary>Most recent dashboard activity events (max 10).</summary>
    public required IReadOnlyList<RecentActivityDto> RecentActivity { get; init; }

    /// <summary>FIPS 199 security categorization (null if not yet categorized).</summary>
    public CategorizationDto? Categorization { get; init; }
}

/// <summary>
/// FIPS 199 security categorization for a system.
/// </summary>
public class CategorizationDto
{
    public required string Confidentiality { get; init; }
    public required string Integrity { get; init; }
    public required string Availability { get; init; }
    public required string Overall { get; init; }
    public required string FormalNotation { get; init; }
    public required string DodImpactLevel { get; init; }
    public required IReadOnlyList<InfoTypeDto> InformationTypes { get; init; }
}

/// <summary>
/// SP 800-60 information type within a categorization.
/// </summary>
public class InfoTypeDto
{
    public required string Name { get; init; }
    public required string Confidentiality { get; init; }
    public required string Integrity { get; init; }
    public required string Availability { get; init; }
}

/// <summary>
/// Progress of a single RMF phase.
/// </summary>
public class RmfPhaseProgressDto
{
    /// <summary>Phase name.</summary>
    public required string Phase { get; init; }

    /// <summary>Phase ordinal (0-6).</summary>
    public int Ordinal { get; init; }

    /// <summary>Phase status: complete, current, upcoming.</summary>
    public required string Status { get; init; }

    /// <summary>Completion percentage (0-100).</summary>
    public double CompletionPercent { get; init; }
}

/// <summary>
/// Key compliance metrics for a system.
/// </summary>
public class KeyMetricsDto
{
    /// <summary>Overall compliance score (0-100).</summary>
    public double ComplianceScore { get; init; }

    /// <summary>Change since prior assessment.</summary>
    public double ComplianceScoreDelta { get; init; }

    /// <summary>Prior assessment score.</summary>
    public double PriorScore { get; init; }

    /// <summary>Total open POA&amp;M items.</summary>
    public int TotalOpenPoams { get; init; }

    /// <summary>Overdue POA&amp;Ms.</summary>
    public int OverduePoams { get; init; }

    /// <summary>Days until ATO expires.</summary>
    public int? AtoDaysRemaining { get; init; }

    /// <summary>ATO severity indicator.</summary>
    public required string AtoSeverity { get; init; }

    /// <summary>ATO expiration date.</summary>
    public DateTime? AtoExpirationDate { get; init; }

    /// <summary>ATO status.</summary>
    public required string AtoStatus { get; init; }

    /// <summary>Open CAT I findings.</summary>
    public int CatIFindings { get; init; }

    /// <summary>Open CAT II findings.</summary>
    public int CatIIFindings { get; init; }

    /// <summary>Open CAT III findings.</summary>
    public int CatIIIFindings { get; init; }

    /// <summary>Total open findings.</summary>
    public int TotalFindings { get; init; }

    /// <summary>Percentage of baseline controls with narratives.</summary>
    public double NarrativeCoverage { get; init; }

    /// <summary>Count of active (Pending or Approved) deviations for this system.</summary>
    public int ActiveDeviations { get; init; }
}

/// <summary>
/// Recent activity event for the dashboard feed.
/// </summary>
public class RecentActivityDto
{
    /// <summary>Activity identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Event type.</summary>
    public required string EventType { get; init; }

    /// <summary>Event timestamp (UTC).</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Who triggered the event.</summary>
    public required string Actor { get; init; }

    /// <summary>Human-readable summary.</summary>
    public required string Summary { get; init; }

    /// <summary>Type of related entity.</summary>
    public string? RelatedEntityType { get; init; }

    /// <summary>ID of related entity.</summary>
    public string? RelatedEntityId { get; init; }
}
