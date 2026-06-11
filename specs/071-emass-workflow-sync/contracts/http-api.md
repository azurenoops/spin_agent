# HTTP API Contracts: eMASS Workflow Sync & OSCAL 1.1.2 Upgrade

**Feature**: 071-emass-workflow-sync | **Date**: 2026-06-11  
**Base path**: `/api/systems/{systemId}/emass`  
**Auth**: Bearer JWT — roles enforced per endpoint  
**Envelope**: All responses follow `{ "data": {...}, "meta": {...}, "errors": [...] }`

---

## GET /api/systems/{systemId}/emass/status

Returns the ISSO's complete eMASS workflow status for a system.

**RBAC**: ISSO, ISSM, AO  
**Performance**: Must respond < 500 ms (NFR-002)

### Request

```http
GET /api/systems/abc123/emass/status
Authorization: Bearer <token>
```

### Response 200

```json
{
  "data": {
    "systemId": "abc123",
    "overallStatus": "HasConflicts",
    "lastExportedAt": "2026-05-15T14:30:00Z",
    "lastSyncedAt": "2026-06-01T09:00:00Z",
    "unresolvedConflictCount": 3,
    "exportSummary": [
      {
        "category": "Controls",
        "exportedCount": 325,
        "pendingCount": 12,
        "lastExportedAt": "2026-05-15T14:30:00Z"
      },
      {
        "category": "PoamItems",
        "exportedCount": 48,
        "pendingCount": 5,
        "lastExportedAt": "2026-05-15T14:30:00Z"
      },
      {
        "category": "Artifacts",
        "exportedCount": 6,
        "pendingCount": 0,
        "lastExportedAt": "2026-05-15T14:30:00Z"
      }
    ],
    "readinessStatus": {
      "isReady": false,
      "blockingGapCount": 1,
      "advisoryGapCount": 2
    }
  },
  "meta": { "checkedAt": "2026-06-11T10:00:00Z" },
  "errors": []
}
```

### Response 404

```json
{
  "data": null,
  "meta": {},
  "errors": [{ "code": "SYSTEM_NOT_FOUND", "message": "System abc123 not found." }]
}
```

---

## GET /api/systems/{systemId}/emass/readiness

Returns the export readiness check result with all gaps identified.

**RBAC**: ISSO, ISSM, AO

### Response 200 — Not Ready

```json
{
  "data": {
    "systemId": "abc123",
    "isReady": false,
    "checkedAt": "2026-06-11T10:00:00Z",
    "gaps": [
      {
        "fieldName": "DitprId",
        "description": "DITPR System ID is required by eMASS to match the import to the correct system record.",
        "severity": "Blocking",
        "fixUrl": "/systems/abc123/settings#identifiers"
      },
      {
        "fieldName": "Ssp.ApprovedSections",
        "description": "At least one SSP section must be in Approved status before export.",
        "severity": "Advisory",
        "fixUrl": "/systems/abc123/ssp"
      }
    ]
  },
  "meta": {},
  "errors": []
}
```

### Response 200 — Ready

```json
{
  "data": {
    "systemId": "abc123",
    "isReady": true,
    "checkedAt": "2026-06-11T10:00:00Z",
    "gaps": []
  },
  "meta": {},
  "errors": []
}
```

---

## POST /api/systems/{systemId}/emass/sync

Uploads an eMASS Excel export file, diffs it against SPIN state, and creates `EmassConflict` records for every changed field. **Does not modify SPIN data.**

**RBAC**: ISSO, ISSM  
**Content-Type**: `multipart/form-data`  
**Max file size**: 50 MB

### Request

```http
POST /api/systems/abc123/emass/sync
Authorization: Bearer <token>
Content-Type: multipart/form-data

--boundary
Content-Disposition: form-data; name="file"; filename="emass-export.xlsx"
Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet

[binary Excel data]
--boundary--
```

### Response 200 — Sync completed

```json
{
  "data": {
    "batchId": "b1234567-...",
    "systemId": "abc123",
    "conflictsCreated": 7,
    "identicalFields": 518,
    "skippedUnresolved": 0,
    "syncedAt": "2026-06-11T10:05:00Z"
  },
  "meta": {},
  "errors": []
}
```

### Response 409 — Unresolved conflicts from prior batch

```json
{
  "data": {
    "priorBatchId": "a9876543-...",
    "unresolvedConflictCount": 3
  },
  "meta": {},
  "errors": [
    {
      "code": "UNRESOLVED_CONFLICTS",
      "message": "3 conflicts from the prior sync batch are unresolved. Resolve or acknowledge them before running a new sync.",
      "hint": "Pass 'acknowledgeUnresolved=true' query parameter to proceed anyway."
    }
  ]
}
```

**Override**: `POST /api/systems/{id}/emass/sync?acknowledgeUnresolved=true` — proceeds despite unresolved conflicts.

### Response 400 — Wrong system in file

```json
{
  "data": null,
  "meta": {},
  "errors": [
    {
      "code": "SYSTEM_ID_MISMATCH",
      "message": "The eMASS ID in the uploaded file (98765) does not match this system's EmassId (12345)."
    }
  ]
}
```

---

## GET /api/systems/{systemId}/emass/conflicts

Returns paginated list of sync conflicts, optionally filtered by status.

**RBAC**: ISSO, ISSM, AO

### Query Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `status` | string | `Unresolved` | Filter: `Unresolved`, `KeepSpin`, `AcceptEmass`, `Deferred`, `All` |
| `batchId` | string | (all batches) | Filter by sync batch |
| `limit` | int | 50 | Max records to return |
| `offset` | int | 0 | Pagination offset |

### Response 200

```json
{
  "data": [
    {
      "id": "c1111111-...",
      "entityType": "SystemInfo",
      "entityId": null,
      "fieldName": "SystemInfo.SystemName",
      "spinValue": "My System (SPIN)",
      "emassValue": "My System - Production",
      "conflictStatus": "Unresolved",
      "detectedAt": "2026-06-11T10:05:00Z",
      "resolvedAt": null,
      "resolvedBy": null
    }
  ],
  "meta": {
    "total": 7,
    "limit": 50,
    "offset": 0
  },
  "errors": []
}
```

---

## PUT /api/systems/{systemId}/emass/conflicts/{conflictId}

Resolves a single conflict. If `AcceptEmass` is chosen, SPIN data is updated with the eMASS value.

**RBAC**: ISSO, ISSM (not AO)

### Request Body

```json
{
  "resolution": "AcceptEmass",
  "notes": "Confirmed with system owner — eMASS name is correct."
}
```

**resolution values**: `KeepSpin` | `AcceptEmass` | `Deferred`

### Response 200

```json
{
  "data": {
    "id": "c1111111-...",
    "conflictStatus": "AcceptEmass",
    "resolvedAt": "2026-06-11T10:15:00Z",
    "resolvedBy": "isso@example.com",
    "spinValueAfter": "My System - Production"
  },
  "meta": {},
  "errors": []
}
```

### Response 403

```json
{
  "data": null,
  "meta": {},
  "errors": [{ "code": "FORBIDDEN", "message": "AO role cannot resolve conflicts. ISSO or ISSM role required." }]
}
```

---

## Error Codes

| Code | HTTP | Description |
|------|------|-------------|
| `SYSTEM_NOT_FOUND` | 404 | System ID does not exist or user lacks access |
| `CONFLICT_NOT_FOUND` | 404 | Conflict ID does not exist for this system |
| `UNRESOLVED_CONFLICTS` | 409 | Prior sync batch has unresolved conflicts |
| `SYSTEM_ID_MISMATCH` | 400 | eMASS ID in file does not match system record |
| `INVALID_EXCEL_FORMAT` | 400 | File is not a valid eMASS Excel export |
| `FORBIDDEN` | 403 | Caller role insufficient for this operation |
| `CONFLICT_ALREADY_RESOLVED` | 409 | Conflict is already in a terminal state (KeepSpin/AcceptEmass) |
