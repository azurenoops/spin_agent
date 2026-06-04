# Data Model: OSCAL SSP Export + CI Schema Validation (Epic #122)

## No new database entities required

This feature adds validation result types, DTO wrappers, and CI tooling.
No EF Core migrations are needed.

---

## New C# Types (non-entity)

### `OscalValidationResult`

```csharp
// File: src/Ato.Copilot.Agents/Compliance/Models/OscalValidationResult.cs

public record OscalValidationResult
{
    /// <summary>
    /// True if the document conforms to its OSCAL JSON schema.
    /// Null if validation could not be performed (e.g., schema file missing).
    /// </summary>
    public bool? IsValid { get; init; }

    /// <summary>
    /// Schema constraint violations. Non-empty means IsValid == false.
    /// </summary>
    public List<OscalValidationEntry> Errors { get; init; } = [];

    /// <summary>
    /// Business-rule completeness warnings. Non-empty does NOT mean IsValid == false.
    /// </summary>
    public List<OscalValidationEntry> Warnings { get; init; } = [];

    public static OscalValidationResult Valid() =>
        new() { IsValid = true };

    public static OscalValidationResult Invalid(IEnumerable<OscalValidationEntry> errors) =>
        new() { IsValid = false, Errors = errors.ToList() };
}

public record OscalValidationEntry
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Path { get; init; }    // JSON Pointer path, e.g. "/system-security-plan/metadata/title"
}
```

### `OscalWarningCodes`

```csharp
// File: src/Ato.Copilot.Agents/Compliance/Models/OscalWarningCodes.cs

public static class OscalWarningCodes
{
    public const string NoSecurityCategorization  = "NO_SECURITY_CATEGORIZATION";
    public const string NoControlBaseline         = "NO_CONTROL_BASELINE";
    public const string NoRmfRoleAssignments      = "NO_RMF_ROLE_ASSIGNMENTS";
    public const string NoNarratives              = "NO_NARRATIVES";
    public const string SchemaFileNotFound        = "SCHEMA_FILE_NOT_FOUND";
    public const string ValidationServiceUnavailable = "VALIDATION_SERVICE_UNAVAILABLE";
}
```

### `OscalExportResult`

```csharp
// File: src/Ato.Copilot.Agents/Compliance/Services/OscalSspExportService.cs
// (inner type or companion file)

public record OscalExportResult
{
    /// <summary>Serialized OSCAL JSON document.</summary>
    public required string Document { get; init; }

    /// <summary>Validation result from schema check + business rules.</summary>
    public required OscalValidationResult ValidationResult { get; init; }
}
```

### `OscalDocumentType` (enum)

```csharp
public enum OscalDocumentType
{
    Ssp,
    AssessmentPlan,
    AssessmentResults,
    Poam
}
```

---

## New API DTOs

### `OscalValidationStatusDto`

```csharp
public record OscalValidationStatusDto
{
    public bool? IsValid { get; init; }
    public List<ValidationEntryDto> Errors { get; init; } = [];
    public List<ValidationEntryDto> Warnings { get; init; } = [];
}

public record ValidationEntryDto
{
    public string Code { get; init; }
    public string Message { get; init; }
    public string? Path { get; init; }
}
```

### `OscalExportResponse` (API envelope)

```csharp
public record OscalExportResponse
{
    /// <summary>
    /// The OSCAL document. Embedded as a JSON object (not a string)
    /// for clients that want to parse inline.
    /// </summary>
    public required JsonDocument Document { get; init; }

    public required OscalValidationStatusDto ValidationStatus { get; init; }
}
```

---

## Schema files (read-only, existing)

| Schema | File |
|--------|------|
| SSP | `src/Ato.Copilot.Agents/Compliance/Resources/oscal-schemas/oscal_ssp_schema.json` |
| Assessment Plan | `oscal_assessment-plan_schema.json` |
| Assessment Results | `oscal_assessment-results_schema.json` |
| POA&M | `oscal_poam_schema.json` |

No schema file changes in this feature. Schemas are consumed as-is by both
`OscalSchemaValidationService.cs` (runtime) and `validate-oscal-exports.mjs` (CI).
