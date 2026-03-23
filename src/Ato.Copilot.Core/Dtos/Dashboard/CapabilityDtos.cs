namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Read DTO for a security capability list/detail item.
/// </summary>
public class SecurityCapabilityDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required string Category { get; init; }
    public required string CategoryName { get; init; }
    public required string Description { get; init; }
    public required string ImplementationStatus { get; init; }
    public required string Owner { get; init; }
    public int MappedControlCount { get; init; }
    public int SystemsUsingCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ModifiedAt { get; init; }
    public List<LinkedComponentDto>? LinkedComponents { get; init; }
    public int? SystemCount { get; init; }
}

/// <summary>
/// Component linked to a capability (badge display).
/// </summary>
public class LinkedComponentDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ComponentType { get; init; }
}

/// <summary>
/// Request body for creating a new security capability.
/// </summary>
public class CreateCapabilityRequest
{
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string ImplementationStatus { get; init; }
    public required string Owner { get; init; }
}

/// <summary>
/// Response for capability update, including narrative propagation counts.
/// </summary>
public class UpdateCapabilityResponse : SecurityCapabilityDto
{
    public int NarrativesUpdated { get; init; }
    public int NarrativesSkipped { get; init; }
    public Dictionary<string, int>? NarrativesByBoundary { get; init; }
}

/// <summary>
/// Response for capability deletion.
/// </summary>
public class DeleteCapabilityResponse
{
    public required string DeletedId { get; init; }
    public int AffectedNarratives { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Impact preview for capability changes — shows how many narratives would be regenerated.
/// </summary>
public class CapabilityImpactPreview
{
    public int TotalNarratives { get; init; }
    public int TotalSystems { get; init; }
    public int CustomSkipped { get; init; }
    public List<CapabilitySystemImpactDto> BySystem { get; init; } = [];
}

/// <summary>
/// Per-system breakdown for capability impact preview.
/// </summary>
public class CapabilitySystemImpactDto
{
    public required string SystemId { get; init; }
    public string? SystemName { get; init; }
    public int NarrativeCount { get; init; }
    public int CustomSkipped { get; init; }
}

// ─── Capability Coverage DTOs ────────────────────────────────────────────

/// <summary>
/// Full capability coverage response for a system.
/// </summary>
public class CapabilityCoverageResponse
{
    public required string SystemId { get; init; }
    public string? SystemName { get; init; }
    public List<CapabilityCoverageDto> Capabilities { get; init; } = [];
    public required CoverageSummaryDto Summary { get; init; }
}

/// <summary>
/// Individual capability in the coverage view with linked components and narrative status.
/// </summary>
public class CapabilityCoverageDto
{
    public required string CapabilityId { get; init; }
    public required string CapabilityName { get; init; }
    public required string Provider { get; init; }
    public required string Category { get; init; }
    public required string ImplementationStatus { get; init; }
    public string? Owner { get; init; }
    public required string Role { get; init; }
    public int MappedControlCount { get; init; }
    public required NarrativeStatusDto NarrativeStatus { get; init; }
    public List<CoverageComponentDto> Components { get; init; } = [];
}

/// <summary>
/// Narrative status breakdown for a capability in the coverage view.
/// </summary>
public class NarrativeStatusDto
{
    public int Populated { get; init; }
    public int Custom { get; init; }
    public int Empty { get; init; }
    public int AiGenerated { get; init; }
}

/// <summary>
/// Component information in the coverage view.
/// </summary>
public class CoverageComponentDto
{
    public required string ComponentId { get; init; }
    public required string Name { get; init; }
    public required string ComponentType { get; init; }
    public string? Owner { get; init; }
    public required string Status { get; init; }
    public string? BoundaryName { get; init; }
    public string? BoundaryDefinitionId { get; init; }
}

/// <summary>
/// Summary counts for the coverage view.
/// </summary>
public class CoverageSummaryDto
{
    public int TotalCapabilities { get; init; }
    public int TotalMappedControls { get; init; }
    public int TotalNarrativesPopulated { get; init; }
    public int TotalNarrativesCustom { get; init; }
    public int TotalNarrativesEmpty { get; init; }
    public double CoveragePercent { get; init; }
}

/// <summary>
/// Result of a bulk narrative regeneration for a capability.
/// </summary>
public class BulkRegenerateResult
{
    public int TotalControls { get; init; }
    public int Regenerated { get; init; }
    public int SkippedCustom { get; init; }
    public int Failed { get; init; }
    public List<string> RegeneratedControlIds { get; init; } = [];
}
