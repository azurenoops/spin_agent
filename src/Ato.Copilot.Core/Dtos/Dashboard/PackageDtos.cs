using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Request to validate an OSCAL artifact against its JSON schema.
/// </summary>
public record ValidateOscalRequest
{
    public string Model { get; init; } = string.Empty;
    public string? Content { get; init; }
}

/// <summary>
/// Internal job record for the Channel-based producer-consumer queue.
/// </summary>
public record PackageExportJob(
    string PackageId,
    string SystemId,
    EvidenceMode EvidenceMode,
    string GeneratedBy
);

/// <summary>
/// Request to generate a new authorization package for a system.
/// </summary>
public record GeneratePackageRequest
{
    public EvidenceMode EvidenceMode { get; init; }
    public bool IncludeEvidence { get; init; } = true;
}

/// <summary>
/// Summary DTO for package list responses.
/// </summary>
public record PackageResponse
{
    public string PackageId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int ArtifactCount { get; init; }
    public bool? ValidationPassed { get; init; }
    public int ValidationErrorCount { get; init; }
    public int ValidationWarningCount { get; init; }
    public long? FileSize { get; init; }
    public string GeneratedBy { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Paginated list of packages for a system.
/// </summary>
public record PackageListResponse
{
    public required IReadOnlyList<PackageResponse> Items { get; init; }
    public int TotalCount { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
}

/// <summary>
/// Detailed response for a single package including artifacts and validation.
/// </summary>
public record PackageDetailResponse
{
    public string PackageId { get; init; } = string.Empty;
    public string SystemId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string EvidenceMode { get; init; } = string.Empty;
    public required IReadOnlyList<PackageArtifactDto> Artifacts { get; init; }
    public PackageValidationDto? Validation { get; init; }
    public long? FileSize { get; init; }
    public string? FailureReason { get; init; }
    public string? FailedArtifactType { get; init; }
    public string GeneratedBy { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Individual artifact within a package detail response.
/// </summary>
public record PackageArtifactDto
{
    public string ArtifactId { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long? FileSize { get; init; }
    public string? OscalVersion { get; init; }
    public bool? SchemaValid { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>
/// Validation summary within a package detail response.
/// </summary>
public record PackageValidationDto
{
    public bool IsValid { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public required IReadOnlyList<ValidationFindingDto> Findings { get; init; }
}

/// <summary>
/// Individual finding within a validation response.
/// </summary>
public record ValidationFindingDto
{
    public string Severity { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? ArtifactType { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? Remediation { get; init; }
}

/// <summary>
/// Readiness checklist item for pre-submission validation.
/// </summary>
public record ReadinessChecklistItem
{
    public string Artifact { get; init; } = string.Empty;
    public bool Ready { get; init; }
    public string Detail { get; init; } = string.Empty;
}

/// <summary>
/// Cross-reference check result for package validation.
/// </summary>
public record CrossReferenceCheckDto
{
    public string Check { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string Detail { get; init; } = string.Empty;
}

/// <summary>
/// Full validation response for the validate endpoint.
/// </summary>
public record PackageReadinessResponse
{
    public bool IsReady { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public required IReadOnlyList<ReadinessChecklistItem> Checklist { get; init; }
    public required IReadOnlyList<CrossReferenceCheckDto> CrossReferenceChecks { get; init; }
}
