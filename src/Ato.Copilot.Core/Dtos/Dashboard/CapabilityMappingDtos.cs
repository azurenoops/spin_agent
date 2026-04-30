namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Read DTO for a single capability-to-control mapping.
/// </summary>
public class CapabilityMappingDto
{
    public required string Id { get; init; }
    public required string ControlId { get; init; }
    public string? ControlTitle { get; init; }
    public string? ControlFamily { get; init; }
    public required string Role { get; init; }
    public string? RegisteredSystemId { get; init; }
    public string? RegisteredSystemName { get; init; }
    public string? BoundaryDefinitionId { get; init; }
    public string? BoundaryDefinitionName { get; init; }
    public required string NarrativeStatus { get; init; }
    public bool IsManuallyCustomized { get; init; }
}

/// <summary>
/// Request to create one or more capability-to-control mappings.
/// </summary>
public class CreateMappingsRequest
{
    public required List<CreateMappingItem> Mappings { get; init; }
}

/// <summary>
/// A single mapping item within a bulk create request.
/// </summary>
public class CreateMappingItem
{
    public required string ControlId { get; init; }
    public required string Role { get; init; }
    public string? RegisteredSystemId { get; init; }
    public string? BoundaryDefinitionId { get; init; }
}

/// <summary>
/// Response after creating mappings, including warnings and narrative generation count.
/// </summary>
public class CreateMappingsResponse
{
    public int Created { get; init; }
    public List<MappingWarning> Warnings { get; init; } = [];
    public int NarrativesGenerated { get; init; }
}

/// <summary>
/// Request to update an existing capability-to-control mapping.
/// Any null field is left unchanged.
/// </summary>
public class UpdateMappingRequest
{
    public string? ControlId { get; init; }
    public string? Role { get; init; }
    public string? RegisteredSystemId { get; init; }
    public string? BoundaryDefinitionId { get; init; }
}

/// <summary>
/// Warning about a potential conflict (e.g., duplicate primary role).
/// </summary>
public class MappingWarning
{
    public required string ControlId { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Envelope for the GET /capabilities/{id}/mappings response.
/// </summary>
public class CapabilityMappingsResponse
{
    public required string CapabilityId { get; init; }
    public required string CapabilityName { get; init; }
    public required List<CapabilityMappingDto> Mappings { get; init; }
    public int TotalMappings { get; init; }
}

/// <summary>
/// Query parameters for the capabilities list endpoint.
/// </summary>
public class CapabilityQuery : PaginationQuery
{
    public string? Search { get; init; }
    public string? Category { get; init; }
    public string? Status { get; init; }
}
