# HTTP API Contract — 072: Anti-Double-Entry (SPIN/eMASS Sync Status)

All endpoints use the standard ATO Copilot envelope:

```json
{ "status": "success|error", "data": { ... }, "metadata": { "executionTimeMs": N, "timestamp": "ISO" } }
```

---

## 1. GET `/api/systems/{systemId}/emass-sync-status`

Returns the eMASS sync status for a registered system, including per-field divergence rows.

### Authorization

- Roles: `ISSO`, `ISSM`
- `SCA` and `AO` receive **403 Forbidden** with error code `FORBIDDEN_ISSO_ISSM_REQUIRED`
- Any authenticated user with `systemId` not in their tenant receives **404**

### Path Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `systemId` | `string` (GUID) | Registered system ID |

### Response — 200 OK

```json
{
  "status": "success",
  "data": {
    "overallStatus": "InSync",
    "importDate": "2026-06-05T14:22:00Z",
    "divergedFieldCount": 0,
    "fields": [
      {
        "fieldName": "Name",
        "emassValue": "ACME Portal",
        "spinValue": "ACME Portal",
        "isDiverged": false,
        "importedAt": "2026-06-05T14:22:00Z",
        "importSessionId": "a1b2c3d4-0000-0000-0000-000000000001",
        "divergenceDetectedAt": null,
        "reconciledAt": null
      },
      {
        "fieldName": "DitprId",
        "emassValue": "12345",
        "spinValue": "12345",
        "isDiverged": false,
        "importedAt": "2026-06-05T14:22:00Z",
        "importSessionId": "a1b2c3d4-0000-0000-0000-000000000001",
        "divergenceDetectedAt": null,
        "reconciledAt": null
      },
      {
        "fieldName": "OverallLevel",
        "emassValue": "Moderate",
        "spinValue": "Moderate",
        "isDiverged": false,
        "importedAt": "2026-06-05T14:22:00Z",
        "importSessionId": "a1b2c3d4-0000-0000-0000-000000000001",
        "divergenceDetectedAt": null,
        "reconciledAt": null
      }
    ]
  },
  "metadata": { "executionTimeMs": 12, "timestamp": "2026-06-11T10:00:00Z" }
}
```

#### `overallStatus` Enum Values

| Value | Meaning |
|-------|---------|
| `InSync` | eMASS import exists; all tracked fields match eMASS values |
| `Diverged` | eMASS import exists; ≥1 field has `isDiverged = true` |
| `NotImported` | No `EmassImportSession` with `Status = Imported` for this system |

#### `EmassFieldStatusDto` Fields

| Field | Type | Description |
|-------|------|-------------|
| `fieldName` | `string` | One of: `Name`, `Acronym`, `DitprId`, `OverallLevel`, `BaselineType`, `SystemType` |
| `emassValue` | `string \| null` | Raw value from eMASS import column |
| `spinValue` | `string \| null` | Current SPIN field value (stringified) |
| `isDiverged` | `boolean` | True when spinValue ≠ emassValue (post-normalization) |
| `importedAt` | `string` (ISO 8601) | When this field was last imported from eMASS |
| `importSessionId` | `string` (GUID) | Which import session sourced this snapshot |
| `divergenceDetectedAt` | `string \| null` | When divergence was first detected |
| `reconciledAt` | `string \| null` | When field was last reconciled back to eMASS value |

### Response — 403 Forbidden

```json
{
  "status": "error",
  "error": {
    "code": "FORBIDDEN_ISSO_ISSM_REQUIRED",
    "message": "This operation requires ISSO or ISSM role."
  }
}
```

### Response — 404 Not Found

```json
{
  "status": "error",
  "error": {
    "code": "SYSTEM_NOT_FOUND",
    "message": "System 'SYS-001' not found in this tenant."
  }
}
```

---

## 2. POST `/api/systems/{systemId}/emass-sync-status/dismiss`

Soft-dismisses the eMASS sync banner warnings for the calling user on this system.
The sync status badge remains visible; only the inline field banners are suppressed.

### Authorization

- Roles: `ISSO`, `ISSM`
- `SCA` and `AO` receive **403 Forbidden**

### Path Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `systemId` | `string` (GUID) | Registered system ID |

### Request Body

_Empty body — no payload required._

### Response — 204 No Content

_(No response body.)_

### Response — 403 Forbidden

```json
{
  "status": "error",
  "error": {
    "code": "FORBIDDEN_ISSO_ISSM_REQUIRED",
    "message": "This operation requires ISSO or ISSM role."
  }
}
```

---

## 3. C# DTO Records

```csharp
// File: src/Ato.Copilot.Core/Models/Dto/EmassSyncDtos.cs
// or inline in EmassSyncEndpoints.cs

namespace Ato.Copilot.Mcp.Endpoints;

public enum SyncOverallStatus
{
    InSync,
    Diverged,
    NotImported,
}

public record EmassFieldStatusDto(
    string FieldName,
    string? EmassValue,
    string? SpinValue,
    bool IsDiverged,
    DateTimeOffset ImportedAt,
    Guid ImportSessionId,
    DateTimeOffset? DivergenceDetectedAt,
    DateTimeOffset? ReconciledAt
);

public record EmassSystemSyncStatusDto(
    SyncOverallStatus OverallStatus,
    DateTimeOffset? ImportDate,
    int DivergedFieldCount,
    IReadOnlyList<EmassFieldStatusDto> Fields
);
```

---

## 4. TypeScript Types

```typescript
// File: src/Ato.Copilot.Dashboard/src/features/emass-sync/api.ts

export type SyncOverallStatus = 'InSync' | 'Diverged' | 'NotImported';

export interface EmassFieldStatusDto {
  fieldName: string;
  emassValue: string | null;
  spinValue: string | null;
  isDiverged: boolean;
  importedAt: string;            // ISO 8601
  importSessionId: string;       // GUID
  divergenceDetectedAt: string | null;
  reconciledAt: string | null;
}

export interface EmassSystemSyncStatusDto {
  overallStatus: SyncOverallStatus;
  importDate: string | null;     // ISO 8601
  divergedFieldCount: number;
  fields: EmassFieldStatusDto[];
}

// API client functions
export async function getEmassSyncStatus(systemId: string): Promise<EmassSystemSyncStatusDto>;
export async function dismissEmassBanner(systemId: string): Promise<void>;
```

---

## 5. Error Codes Reference

| Code | HTTP Status | When |
|------|-------------|------|
| `FORBIDDEN_ISSO_ISSM_REQUIRED` | 403 | Caller is SCA or AO role |
| `SYSTEM_NOT_FOUND` | 404 | systemId not in caller's tenant (RLS) |
