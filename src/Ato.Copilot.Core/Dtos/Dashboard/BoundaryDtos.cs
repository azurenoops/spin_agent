namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>API response DTO for an authorization boundary definition.</summary>
public record BoundaryDefinitionDto(
    string Id,
    string RegisteredSystemId,
    string Name,
    string BoundaryType,
    string? Description,
    bool IsPrimary,
    int ResourceCount,
    int ComponentCount,
    decimal CoveragePercent,
    DateTime CreatedAt);

/// <summary>API input DTO for creating or updating a boundary definition.</summary>
public record CreateBoundaryDefinitionRequest(
    string Name,
    string BoundaryType,
    string? Description);

/// <summary>Gap analysis comparison DTO for a single boundary.</summary>
public record BoundaryComparisonDto(
    string BoundaryId,
    string BoundaryName,
    int TotalControls,
    int CoveredControls,
    int GapCount,
    decimal CoveragePercent,
    int ResourceCount,
    int ComponentCount);

/// <summary>API response DTO for the result of deleting a boundary definition.</summary>
public record DeleteBoundaryDefinitionResponse(
    string DeletedId,
    int ReassignedComponents,
    int ReassignedMappings,
    int ReassignedResources,
    string PrimaryBoundaryId);

/// <summary>Azure Resource Graph discovered resource DTO.</summary>
public record AzureDiscoveredResourceDto(
    string ResourceId,
    string Name,
    string Type,
    string ResourceGroup,
    string Location,
    bool AlreadyInBoundary);

/// <summary>Suggested boundary from Azure resource group discovery.</summary>
public record AzureSuggestedBoundaryDto(
    string ResourceGroupName,
    string BoundaryType,
    int ResourceCount,
    bool AlreadyExists,
    List<AzureDiscoveredResourceDto> Resources);

/// <summary>Request body for adding a resource to a boundary definition.</summary>
public class AddBoundaryResourceRequest
{
    public required string ResourceId { get; init; }
    public required string ResourceType { get; init; }
    public string? ResourceName { get; init; }
    public string? InheritanceProvider { get; init; }
}
