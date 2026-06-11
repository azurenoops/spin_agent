# Data Model: eMASS Workflow Sync & OSCAL 1.1.2 Upgrade

**Feature**: 071-emass-workflow-sync | **Date**: 2026-06-11

## Entity Relationship Overview

```
RegisteredSystem (existing)
 ├── EmassConflict[] (NEW) ──► linked per sync batch
 ├── AuthorizationPackage[] (existing, Feature 041)
 ├── PoamItem[] (existing)
 ├── ControlImplementation[] (existing)
 └── SspSection[] (existing)
```

**No new tables inherit from `AuthorizationPackage`.** `EmassConflict` is a flat, side-table entity — it references the system and optionally an entity type/ID, but does not subtype or extend package records.

---

## New Entities

### EmassConflict

Represents a single field-level difference detected between a SPIN value and a newer eMASS Excel import. Created during a round-trip sync run; updated only when the ISSO explicitly resolves it.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` (GUID) | PK, MaxLength(36) | Unique conflict ID |
| `RegisteredSystemId` | `string` | FK, Required, MaxLength(36) | System this conflict belongs to |
| `SyncBatchId` | `string` (GUID) | Required, MaxLength(36) | Groups all conflicts from one sync run |
| `EntityType` | `string` | Required, MaxLength(50) | Entity containing the conflicting field (e.g., `SystemInfo`, `PoamItem`, `ControlBaseline`) |
| `EntityId` | `string?` | MaxLength(36) | ID of the specific entity instance (null for system-level fields) |
| `FieldName` | `string` | Required, MaxLength(200) | Fully-qualified field name (e.g., `SystemInfo.SystemName`, `PoamItem.ScheduledCompletionDate`) |
| `SpinValue` | `string?` | MaxLength(4000) | Current value in SPIN (serialized as string) |
| `EmassValue` | `string?` | MaxLength(4000) | Value from the eMASS Excel import |
| `ConflictStatus` | `ConflictStatus` enum | Required | Lifecycle: `Unresolved`, `KeepSpin`, `AcceptEmass`, `Deferred` |
| `DetectedAt` | `DateTimeOffset` | Required | Timestamp of sync run |
| `ResolvedAt` | `DateTimeOffset?` | | Timestamp when ISSO resolved |
| `ResolvedBy` | `string?` | MaxLength(200) | User who resolved the conflict |
| `Notes` | `string?` | MaxLength(1000) | Optional ISSO notes on resolution |

**Indexes**:
- `IX_EmassConflict_SystemId_Status` on `(RegisteredSystemId, ConflictStatus)` — for status page queries
- `IX_EmassConflict_BatchId` on `SyncBatchId` — for batch operations

**Relationships**:
- Many-to-one with `RegisteredSystem` (cascade delete)
- No FK to `PoamItem`/`ControlImplementation` — `EntityId` is a soft reference to keep the model flexible

---

## New Enumerations

### ConflictStatus

```csharp
public enum ConflictStatus
{
    Unresolved = 0,   // Created by sync run; awaiting ISSO decision
    KeepSpin   = 1,   // ISSO chose to retain SPIN value (eMASS value discarded)
    AcceptEmass = 2,  // ISSO chose to accept eMASS value (SPIN updated)
    Deferred   = 3    // ISSO acknowledged but postponed decision
}
```

### EmassWorkflowOverallStatus

```csharp
public enum EmassWorkflowOverallStatus
{
    NeverExported  = 0,  // System has never been exported to eMASS
    UpToDate       = 1,  // All SPIN data exported; no pending changes
    PendingExport  = 2,  // SPIN data changed since last export; export needed
    HasConflicts   = 3   // Unresolved sync conflicts exist (takes priority)
}
```

### ReadinessGapSeverity

```csharp
public enum ReadinessGapSeverity
{
    Blocking  = 0,   // Export must not proceed until resolved
    Advisory  = 1    // Export may proceed with ISSO acknowledgment
}
```

---

## New DTOs (no DB persistence)

### EmassWorkflowStatus

Returned by `GET /api/systems/{id}/emass/status` and the `emass_get_workflow_status` MCP tool.

| Field | Type | Description |
|-------|------|-------------|
| `SystemId` | `string` | System identifier |
| `OverallStatus` | `EmassWorkflowOverallStatus` | Rolled-up status (HasConflicts > PendingExport > UpToDate > NeverExported) |
| `LastExportedAt` | `DateTimeOffset?` | Timestamp of most recent successful eMASS export (any type) |
| `LastSyncedAt` | `DateTimeOffset?` | Timestamp of most recent round-trip sync run |
| `UnresolvedConflictCount` | `int` | Count of `ConflictStatus = Unresolved` records |
| `ExportSummary` | `EmassExportCategorySummary[]` | Per-category breakdown (see below) |
| `ReadinessStatus` | `EmassReadinessSummary` | Quick summary: `IsReady`, `BlockingGapCount`, `AdvisoryGapCount` |

### EmassExportCategorySummary

| Field | Type | Description |
|-------|------|-------------|
| `Category` | `string` | `Controls`, `PoamItems`, `Artifacts`, `SystemInfo` |
| `ExportedCount` | `int` | Items included in last export |
| `PendingCount` | `int` | Items created/updated since last export |
| `LastExportedAt` | `DateTimeOffset?` | Category-level last export timestamp |

### EmassExportReadinessResult

Returned by `GET /api/systems/{id}/emass/readiness` and `emass_check_export_readiness` MCP tool.

| Field | Type | Description |
|-------|------|-------------|
| `SystemId` | `string` | System identifier |
| `IsReady` | `bool` | `true` only when zero Blocking gaps exist |
| `Gaps` | `ReadinessGap[]` | All detected gaps; may be empty |
| `CheckedAt` | `DateTimeOffset` | Timestamp of this check |

### ReadinessGap

| Field | Type | Description |
|-------|------|-------------|
| `FieldName` | `string` | Human-readable field name (e.g., `"DitprId"`) |
| `Description` | `string` | Why this gap prevents eMASS acceptance |
| `Severity` | `ReadinessGapSeverity` | `Blocking` or `Advisory` |
| `FixUrl` | `string?` | Relative dashboard URL where the ISSO can fix the gap |

### EmassConflictDto

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `string` | Conflict GUID |
| `EntityType` | `string` | Entity containing the field |
| `EntityId` | `string?` | Instance ID (if applicable) |
| `FieldName` | `string` | Fully-qualified field name |
| `SpinValue` | `string?` | Current SPIN value |
| `EmassValue` | `string?` | Value from eMASS import |
| `ConflictStatus` | `string` | String representation of `ConflictStatus` |
| `DetectedAt` | `DateTimeOffset` | When detected |
| `ResolvedAt` | `DateTimeOffset?` | When resolved |
| `ResolvedBy` | `string?` | Who resolved |

---

## Modified Entities

### RegisteredSystem (existing)

No schema changes. However, `EmassExportService` MUST read `DitprId` and `EmassId` from the existing `RegisteredSystem` record and:
1. Include them in eMASS Excel export columns
2. Include them in OSCAL `metadata.system-id` array under the appropriate schemes

These fields already exist on `RegisteredSystem` from the initial eMASS import; Feature 071 enforces their presence via the readiness check.

---

## OSCAL Schema Changes (non-DB)

### POA&M (1.0.6 → 1.1.2)

| OSCAL 1.0.6 Field | OSCAL 1.1.2 Field | Change |
|-------------------|-------------------|--------|
| `poam-items[].related-observations` | `poam-items[].related-findings` | Renamed (breaking) |
| *(absent)* | `import-ssp` | Added at root level |
| `metadata.oscal-version: "1.0.6"` | `metadata.oscal-version: "1.1.2"` | Version bump |

### Assessment Results (1.0.6 → 1.1.2)

| OSCAL 1.0.6 Field | OSCAL 1.1.2 Field | Change |
|-------------------|-------------------|--------|
| *(absent)* | `results[].reviewed-controls.control-selections` | Added |
| `results[].findings[].target.status` (string) | `results[].findings[].target.status.state` (object) | Structure change |
| *(absent)* | `import-ap` | Added at root level |
| `metadata.oscal-version: "1.0.6"` | `metadata.oscal-version: "1.1.2"` | Version bump |

Both upgraded builders MUST pass validation against the OSCAL 1.1.2 schema bundles added in Feature 041 T002.
