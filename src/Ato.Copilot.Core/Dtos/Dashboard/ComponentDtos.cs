namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Read DTO for a system component (Person, Place, or Thing).
/// </summary>
public class SystemComponentDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ComponentType { get; init; }
    public string? SubType { get; init; }
    public string? Description { get; init; }
    public string? Owner { get; init; }
    public string? PersonName { get; init; }
    public string? Email { get; init; }
    public required string Status { get; init; }
    public string? BoundaryDefinitionId { get; init; }
    public string? BoundaryDefinitionName { get; init; }
    public List<LinkedCapabilityDto> LinkedCapabilities { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime? ModifiedAt { get; init; }
}

/// <summary>
/// Lightweight capability reference for component listings.
/// </summary>
public class LinkedCapabilityDto
{
    public required string CapabilityId { get; init; }
    public required string CapabilityName { get; init; }
}

/// <summary>
/// Request body for creating/updating a component.
/// </summary>
public class CreateComponentRequest
{
    public required string Name { get; init; }
    public required string ComponentType { get; init; }
    public string? SubType { get; init; }
    public string? Description { get; init; }
    public string? Owner { get; init; }
    public string? PersonName { get; init; }
    public string? Email { get; init; }
    public required string Status { get; init; }
    public string? BoundaryDefinitionId { get; init; }
    public List<string> LinkedCapabilityIds { get; init; } = [];
}

/// <summary>
/// Summary counts by component type for the inventory header.
/// </summary>
public class ComponentSummaryDto
{
    public int PersonCount { get; init; }
    public int PlaceCount { get; init; }
    public int ThingCount { get; init; }
    public int PolicyCount { get; init; }
    public int TotalCount { get; init; }
}

/// <summary>
/// Response for component deletion.
/// </summary>
public class DeleteComponentResponse
{
    public required string DeletedId { get; init; }
    public List<FlaggedCapabilityDto> FlaggedCapabilities { get; init; } = [];
}

/// <summary>
/// A capability flagged for review after component deletion.
/// </summary>
public class FlaggedCapabilityDto
{
    public required string CapabilityId { get; init; }
    public required string CapabilityName { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Response envelope for the component inventory list endpoint.
/// </summary>
public class ComponentInventoryResponse
{
    public required string SystemId { get; init; }
    public required ComponentSummaryDto Summary { get; init; }
    public required IReadOnlyList<SystemComponentDto> Items { get; init; }
    public string? NextCursor { get; init; }
    public int TotalCount { get; init; }
}

/// <summary>
/// Query parameters for the components list endpoint.
/// </summary>
public class ComponentQuery : PaginationQuery
{
    public string? Type { get; init; }
    public string? Status { get; init; }
    public string? Search { get; init; }
    public string? BoundaryDefinitionId { get; init; }
}

// ─── Org-Wide Component Library (Feature 036) ────────────────────────────

/// <summary>
/// Paginated response for org-wide component listing.
/// </summary>
public class OrgComponentListResponse
{
    public required IReadOnlyList<OrgComponentDto> Items { get; init; }
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary>
/// Read DTO for an org-wide component with system assignments.
/// </summary>
public class OrgComponentDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ComponentType { get; init; }
    public string? SubType { get; init; }
    public string? Description { get; init; }
    public string? Owner { get; init; }
    public string? PersonName { get; init; }
    public string? Email { get; init; }
    public required string Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime? ModifiedAt { get; init; }
    public List<SystemAssignmentDto> SystemAssignments { get; init; } = [];
    public List<LinkedCapabilityDto> CapabilityLinks { get; init; } = [];
}

/// <summary>
/// System assignment details for an org-wide component.
/// </summary>
public class SystemAssignmentDto
{
    public required string Id { get; init; }
    public required string RegisteredSystemId { get; init; }
    public string? SystemName { get; init; }
    public string? BoundaryDefinitionId { get; init; }
    public string? BoundaryName { get; init; }
}

/// <summary>
/// Request body for assigning a component to a system.
/// </summary>
public class AssignComponentRequest
{
    public required string RegisteredSystemId { get; init; }
    public string? AuthorizationBoundaryDefinitionId { get; init; }
}

/// <summary>
/// Query parameters for the org-wide components list endpoint.
/// </summary>
public class OrgComponentQuery
{
    public string? Search { get; init; }
    public string? Type { get; init; }
    public string? Status { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}

/// <summary>
/// Impact preview for component changes.
/// </summary>
public class ComponentImpactPreview
{
    public int TotalNarratives { get; init; }
    public int TotalSystems { get; init; }
    public int CustomSkipped { get; init; }
    public List<SystemImpactDto> BySystem { get; init; } = [];
}

/// <summary>
/// Per-system breakdown for impact preview.
/// </summary>
public class SystemImpactDto
{
    public required string SystemId { get; init; }
    public string? SystemName { get; init; }
    public int NarrativeCount { get; init; }
    public int CustomSkipped { get; init; }
}
