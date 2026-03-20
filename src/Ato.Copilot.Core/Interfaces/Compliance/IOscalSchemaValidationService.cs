namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Validates OSCAL JSON documents against official NIST OSCAL 1.1.2 JSON schemas.
/// Uses bundled schema files for offline/air-gapped operation.
/// </summary>
public interface IOscalSchemaValidationService
{
    Task<OscalSchemaValidationResult> ValidateAsync(
        string oscalJson,
        string modelType,
        CancellationToken cancellationToken = default);

    Task<OscalSchemaValidationResult> ValidateForSystemAsync(
        string systemId,
        string modelType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of OSCAL JSON Schema validation.
/// </summary>
public record OscalSchemaValidationResult
{
    public bool IsValid { get; init; }
    public string ModelType { get; init; } = string.Empty;
    public string SchemaVersion { get; init; } = "1.1.2";
    public List<OscalSchemaViolation> Violations { get; init; } = new();
}

/// <summary>
/// A specific JSON Schema violation with property path and details.
/// </summary>
public record OscalSchemaViolation
{
    public string JsonPath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? ExpectedFormat { get; init; }
    public string? ActualValue { get; init; }
}
