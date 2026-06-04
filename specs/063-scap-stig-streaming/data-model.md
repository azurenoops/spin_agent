# Data Model: SCAP/STIG Parser Streaming (063)

## Existing Entities (no schema changes required)

### `ScanImportRecords` (existing)
Tracks the lifecycle of a single scan import job.

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `Guid` | PK |
| `SystemId` | `Guid` | FK → `Systems.Id` |
| `Status` | `string` | `Queued | Processing | Completed | Failed | Cancelled` |
| `FileName` | `string(500)` | Original uploaded file name |
| `FileType` | `string(50)` | `CKL | Nessus | XCCDF` |
| `TotalCount` | `int?` | Total findings in file (set after initial parse pass or estimate) |
| `ProcessedCount` | `int` | Updated after each batch — used for progress reporting |
| `ErrorMessage` | `string?` | Set when `Status = Failed` |
| `CreatedAt` | `DateTimeOffset` | Job creation timestamp |
| `CompletedAt` | `DateTimeOffset?` | Set when terminal status reached |
| `CreatedBy` | `Guid` | FK → `Users.Id` |
| `CancelRequested` | `bool` | **New column** — set by DELETE endpoint; checked between batches |

> **Migration required**: Add `CancelRequested bit NOT NULL DEFAULT 0` and
> `ProcessedCount int NOT NULL DEFAULT 0` if not already present.

### `ScanImportFindings` (existing)
Individual finding rows produced by an import job.

| Column | Type | Notes |
|--------|------|-------|
| `Id` | `Guid` | PK |
| `ScanImportRecordId` | `Guid` | FK → `ScanImportRecords.Id` |
| `RuleId` | `string(200)` | STIG rule ID (e.g. `SV-204423r603261_rule`) |
| `HostName` | `string(500)` | Target host |
| `Status` | `string(50)` | `Open | NotAFinding | Not_Reviewed | Not_Applicable` |
| `Severity` | `string(20)` | `high | medium | low | informational` |
| `FindingDetails` | `string?` | Checker output |
| `Comments` | `string?` | Auditor comments |
| `CreatedAt` | `DateTimeOffset` | Row creation time |

Upsert key: `(ScanImportRecordId, RuleId, HostName)` — unique index required.

---

## New DTOs

### `ScanImportFindingDto` (parser output, in-memory only)

```csharp
public record ScanImportFindingDto(
    string RuleId,
    string HostName,
    string Status,
    string Severity,
    string? FindingDetails,
    string? Comments,
    string? PluginName,   // Nessus-specific
    string? PluginOutput  // Nessus-specific
);
```

### `ScanImportJobDto` (REST response)

```csharp
public record ScanImportJobDto(
    Guid ImportJobId,
    string Status,           // Queued | Processing | Completed | Failed | Cancelled
    string FileName,
    string FileType,
    int ProcessedCount,
    int? TotalCount,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt
);
```

### `StartScanImportResponseDto` (202 response body)

```csharp
public record StartScanImportResponseDto(
    Guid ImportJobId,
    string Status   // always "Queued"
);
```

### `ImportProgressMessage` (SignalR broadcast payload)

```csharp
public record ImportProgressMessage(
    Guid ImportJobId,
    string Status,        // Processing | Completed | Failed | Cancelled
    int ProcessedCount,
    int? TotalCount,      // null until determined
    double? PercentComplete, // null until TotalCount known
    string? ErrorMessage,
    DateTimeOffset Timestamp
);
```

---

## Configuration

### `ScanImportOptions`

Bound from `"ScanImport"` configuration section.

```csharp
public class ScanImportOptions
{
    public const string SectionName = "ScanImport";

    /// <summary>Number of findings per EF SaveChangesAsync batch. Default: 500.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>Maximum file upload size in bytes. Default: 268435456 (256 MB).</summary>
    public long MaxFileSizeBytes { get; set; } = 268_435_456;
}
```

`appsettings.json` addition:
```json
"ScanImport": {
  "BatchSize": 500,
  "MaxFileSizeBytes": 268435456
}
```
