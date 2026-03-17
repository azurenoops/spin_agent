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
