# Internal Services Contract: OSCAL Validation (Epic #122)

## `IOscalSchemaValidationService`

**File**: `src/Ato.Copilot.Agents/Compliance/Services/IOscalSchemaValidationService.cs`

```csharp
/// <summary>
/// Validates OSCAL JSON documents against their bundled JSON schemas.
/// </summary>
public interface IOscalSchemaValidationService
{
    /// <summary>
    /// Validates the given OSCAL JSON string against the schema for the
    /// specified document type.
    /// </summary>
    /// <param name="oscalJson">
    ///   Serialized OSCAL document (UTF-8 JSON string).
    /// </param>
    /// <param name="documentType">
    ///   The OSCAL document type — determines which schema file is loaded.
    /// </param>
    /// <returns>
    ///   <see cref="OscalValidationResult"/> with <c>IsValid</c> true if
    ///   the document conforms to its schema, false otherwise.
    ///   <c>Errors</c> lists all constraint violations.
    ///   <c>Warnings</c> is empty (schema-only validation; business-rule
    ///   warnings are produced by the export service, not here).
    /// </returns>
    Task<OscalValidationResult> ValidateAsync(
        string oscalJson,
        OscalDocumentType documentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if the schema file for the given document type is
    /// available on disk / as an embedded resource.
    /// </summary>
    bool IsSchemaAvailable(OscalDocumentType documentType);
}
```

---

## `OscalDocumentType`

```csharp
public enum OscalDocumentType
{
    /// <summary>System Security Plan</summary>
    Ssp,

    /// <summary>Security Assessment Plan</summary>
    AssessmentPlan,

    /// <summary>Security Assessment Results</summary>
    AssessmentResults,

    /// <summary>Plan of Action and Milestones</summary>
    Poam
}
```

---

## `OscalSchemaValidationService` (implementation contract)

**File**: `src/Ato.Copilot.Agents/Compliance/Services/OscalSchemaValidationService.cs`

### Schema file resolution

```
OscalDocumentType.Ssp                → oscal_ssp_schema.json
OscalDocumentType.AssessmentPlan     → oscal_assessment-plan_schema.json
OscalDocumentType.AssessmentResults  → oscal_assessment-results_schema.json
OscalDocumentType.Poam               → oscal_poam_schema.json
```

All files are embedded in `Ato.Copilot.Agents` assembly resources under
`Compliance/Resources/oscal-schemas/`.

### Caching

Schemas are compiled and cached on first use per `OscalDocumentType`.
Cache key: `OscalDocumentType` enum value. Cache is `IMemoryCache` (DI).
TTL: indefinite (schemas do not change at runtime).

### Error mapping

`NJsonSchema` produces `ICollection<ValidationError>`. Each is mapped to
`OscalValidationEntry`:

```csharp
new OscalValidationEntry
{
    Code = "SCHEMA_VIOLATION",
    Message = error.ToString(),   // NJsonSchema human-readable message
    Path = error.Path             // JSON Pointer path
}
```

### Exception handling

If the schema file cannot be loaded (resource not found):
- Log at Error level.
- Return `OscalValidationResult` with:
  - `IsValid = false`
  - `Errors = [{ Code = "SCHEMA_FILE_NOT_FOUND", Message = "...", Path = null }]`
  - Do NOT throw.

If the JSON is not parseable (malformed):
- Return `OscalValidationResult` with:
  - `IsValid = false`
  - `Errors = [{ Code = "INVALID_JSON", Message = "...", Path = null }]`
  - Do NOT throw.

---

## `IOscalSspExportService` (updated signature)

```csharp
public interface IOscalSspExportService
{
    /// <summary>
    /// Exports the system's SSP as OSCAL JSON and validates the result
    /// against the OSCAL SSP schema.
    /// </summary>
    Task<OscalExportResult> ExportAsync(
        Guid systemId,
        CancellationToken cancellationToken = default);
}

public record OscalExportResult
{
    /// <summary>Serialized OSCAL JSON document.</summary>
    public required string Document { get; init; }

    /// <summary>
    /// Combined schema validation result and business-rule warnings.
    /// IsValid reflects schema conformance. Warnings reflect completeness.
    /// </summary>
    public required OscalValidationResult ValidationResult { get; init; }
}
```

---

## DI Registration

```csharp
// In Ato.Copilot.Agents DI configuration:
services.AddSingleton<IOscalSchemaValidationService, OscalSchemaValidationService>();
services.AddScoped<IOscalSspExportService, OscalSspExportService>();
```

`OscalSchemaValidationService` is `Singleton` because it holds cached compiled
schemas. `OscalSspExportService` is `Scoped` (it reads from the DB via a
scoped `DbContext`).

---

## Backward Compatibility

The update to `IOscalSspExportService.ExportAsync` changes the return type
from `string` (the raw document) to `OscalExportResult`. Existing callers
that only need the document update to `result.Document`. No callers outside
the API layer are expected; verify before merging.
