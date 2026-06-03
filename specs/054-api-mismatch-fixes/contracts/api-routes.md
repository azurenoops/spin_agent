# Phase 1: API Route Contracts — API Mismatch Fixes

**Branch**: `054-api-mismatch-fixes`
**Date**: 2026-06-03

This document defines every HTTP contract changed or added by this feature. All endpoints follow the existing MCP envelope schema (Constitution § UX Standards): `{ status, data, metadata, warnings?, error? }`.

---

## Route Inventory

| Method | Path | Status | Change |
|---|---|---|---|
| POST | `/api/dashboard/systems/{id}/inheritance/apply-profile` | **NEW** | GAP-001 — missing registration; causes 404 |
| POST | `/api/dashboard/systems/{id}/inheritance/import/preview` | **NEW** | GAP-002 — missing registration; causes 404 |
| POST | `/api/dashboard/systems/{id}/inheritance/import/apply` | **NEW** | GAP-002 — missing registration; causes 404 |
| PUT | `/api/dashboard/systems/{id}/remediation/poam/bulk-status` | **NEW (replaces broken route)** | GAP-003 — was `POST /api/dashboard/poam/bulk-status` (wrong verb + missing prefix) |
| PUT | `/api/dashboard/systems/{systemId}/poam/{poamId}/status` | **MODIFIED** | GAP-004 — was `/api/dashboard/poam/{poamId}/status` (missing systemId prefix) |
| POST | `/api/dashboard/chat` (or SSE endpoint) | **MODIFIED** | GAP-014 — now accepts `multipart/form-data` when attachments present |

The old broken route `POST /api/dashboard/poam/bulk-status` is **removed** — it was never reachable from the frontend and must not silently accept requests.

---

## GAP-001: `POST /api/dashboard/systems/{id}/inheritance/apply-profile`

**Status**: NEW (was missing — 404)
**GitHub Issue**: #141

**Path params**:

| Param | Type | Description |
|---|---|---|
| `id` | `string` (GUID) | System ID, validated against authenticated tenant |

**Request body** (`application/json`):

```json
{
  "profileId": "nist-800-53-rev5-moderate"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `profileId` | `string` | Yes | The control profile to apply (e.g. `nist-800-53-rev5-moderate`) |

**Authentication**: Any authenticated tenant member whose tenant owns system `{id}`.

**Response 200 OK**:

```json
{
  "status": "success",
  "data": {
    "systemId": "...",
    "profileId": "nist-800-53-rev5-moderate",
    "appliedControls": 110,
    "appliedAt": "2026-06-03T12:00:00Z"
  },
  "metadata": {
    "toolName": "DashboardEndpoints.ApplyProfile",
    "executionTimeMs": 45,
    "timestamp": "2026-06-03T12:00:00Z"
  }
}
```

**Response 400 Bad Request** — missing or unknown `profileId`:

```json
{
  "status": "error",
  "error": {
    "code": "INVALID_PROFILE_ID",
    "message": "Unknown profile 'nist-999'. Valid values: nist-800-53-rev5-low, nist-800-53-rev5-moderate, nist-800-53-rev5-high."
  },
  "metadata": { "..." }
}
```

**Response 404 Not Found** — `{id}` does not belong to the authenticated tenant:

```json
{
  "status": "error",
  "error": {
    "code": "SYSTEM_NOT_FOUND",
    "message": "System '...' not found for this tenant."
  }
}
```

**Response 5xx** — service failure; standard MCP envelope error.

---

## GAP-002a: `POST /api/dashboard/systems/{id}/inheritance/import/preview`

**Status**: NEW (was missing — 404)
**GitHub Issue**: #142

**Path params**:

| Param | Type | Description |
|---|---|---|
| `id` | `string` (GUID) | Target system ID, validated against authenticated tenant |

**Request body** (`application/json`):

```json
{
  "sourceSystemId": "00000000-0000-0000-0000-000000000001"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `sourceSystemId` | `string` (GUID) | Yes | System whose controls will be previewed for import |

**Authentication**: Any authenticated tenant member whose tenant owns both `{id}` and `sourceSystemId`.

**Response 200 OK**:

```json
{
  "status": "success",
  "data": {
    "targetSystemId": "...",
    "sourceSystemId": "...",
    "controls": [
      { "controlId": "AC-1", "title": "Policy and Procedures", "status": "Implemented" },
      { "controlId": "AC-2", "title": "Account Management", "status": "Inherited" }
    ],
    "summary": {
      "total": 42,
      "alreadyInherited": 10,
      "new": 32
    }
  },
  "metadata": { "toolName": "DashboardEndpoints.ImportPreview", "executionTimeMs": 38 }
}
```

**Response 400** — invalid `sourceSystemId`:

```json
{
  "status": "error",
  "error": { "code": "INVALID_SOURCE_SYSTEM", "message": "sourceSystemId must be a valid GUID." }
}
```

**Response 404** — source or target system not found for this tenant.

**Response 5xx** — service failure.

---

## GAP-002b: `POST /api/dashboard/systems/{id}/inheritance/import/apply`

**Status**: NEW (was missing — 404)
**GitHub Issue**: #142

**Path params**: same as preview (`id` = target system ID).

**Request body** (`application/json`):

```json
{
  "sourceSystemId": "00000000-0000-0000-0000-000000000001",
  "controlIds": ["AC-1", "AC-2"]
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `sourceSystemId` | `string` (GUID) | Yes | Source system to import from |
| `controlIds` | `string[]` | Yes | Control IDs to apply; must be non-empty |

**Authentication**: Any authenticated tenant member whose tenant owns both systems.

**Response 200 OK**:

```json
{
  "status": "success",
  "data": {
    "targetSystemId": "...",
    "applied": 2,
    "skipped": 0
  },
  "metadata": { "toolName": "DashboardEndpoints.ImportApply", "executionTimeMs": 62 }
}
```

**Response 400** — empty `controlIds` or invalid body:

```json
{
  "status": "error",
  "error": { "code": "INVALID_REQUEST", "message": "controlIds must not be empty." }
}
```

**Response 404** — source or target system not found for this tenant.

**Response 5xx** — service failure (apply is NOT idempotent by default; partial application details logged server-side).

---

## GAP-003: `PUT /api/dashboard/systems/{id}/remediation/poam/bulk-status`

**Status**: NEW (replaces broken `POST /api/dashboard/poam/bulk-status`)
**GitHub Issue**: #143

**Path params**:

| Param | Type | Description |
|---|---|---|
| `id` | `string` (GUID) | System ID, validated against authenticated tenant |

**Request body** (`application/json`):

```json
{
  "poamIds": ["poam-uuid-1", "poam-uuid-2"],
  "status": "Closed",
  "closedDate": "2026-06-03"
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `poamIds` | `string[]` | Yes | One or more POAM IDs to update; must be non-empty |
| `status` | `string` | Yes | New status: `Open`, `Closed`, `OnHold`, `PendingRisk` |
| `closedDate` | `string` (ISO 8601 date) | When `status = Closed` | Required when closing |

**HTTP verb**: `PUT` (idempotent — repeated calls with the same status produce the same result).

**Authentication**: Any authenticated tenant member whose tenant owns system `{id}`.

**Response 200 OK**:

```json
{
  "status": "success",
  "data": {
    "systemId": "...",
    "updated": 2,
    "notFound": []
  },
  "metadata": { "toolName": "DashboardEndpoints.BulkUpdatePoamStatus", "executionTimeMs": 31 }
}
```

If some `poamIds` are not found for this system/tenant, they appear in `notFound` (not a 404 — bulk operations are partial-success-tolerant):

```json
{
  "status": "success",
  "data": { "updated": 1, "notFound": ["poam-uuid-2"] }
}
```

**Response 400** — empty `poamIds`, unknown `status` value, or missing `closedDate` when status = `Closed`:

```json
{
  "status": "error",
  "error": {
    "code": "INVALID_REQUEST",
    "message": "status must be one of: Open, Closed, OnHold, PendingRisk.",
    "suggestion": "Provide a valid status value."
  }
}
```

**Response 404** — `{id}` (system) not found for this tenant.

**Removed**: `POST /api/dashboard/poam/bulk-status` — this route is **deleted** and must return 404 after the fix.

---

## GAP-004: `PUT /api/dashboard/systems/{systemId}/poam/{poamId}/status`

**Status**: MODIFIED (was `/api/dashboard/poam/{poamId}/status` — missing systemId prefix)
**GitHub Issue**: #144

**Path params**:

| Param | Type | Description |
|---|---|---|
| `systemId` | `string` (GUID) | System ID — required for tenant RLS enforcement |
| `poamId` | `string` (GUID) | POAM entry ID |

**Request body** (`application/json`):

```json
{
  "status": "Closed",
  "closedDate": "2026-06-03",
  "comments": "Remediated via patch deployment."
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `status` | `string` | Yes | `Open`, `Closed`, `OnHold`, `PendingRisk` |
| `closedDate` | `string` (ISO 8601 date) | When `status = Closed` | Required when closing |
| `comments` | `string` | No | Optional audit comment |

**Authentication**: Any authenticated tenant member whose tenant owns system `{systemId}`.

**Tenant Isolation (critical)**: `systemId` is forwarded to `IPoamService.UpdateStatusAsync(systemId, poamId, status)`. The service applies the existing `[TenantScoped]` global query filter via `AtoCopilotContext`. A request where `systemId` belongs to a different tenant returns 404 (not 403) to avoid information disclosure.

**Response 200 OK**:

```json
{
  "status": "success",
  "data": {
    "systemId": "...",
    "poamId": "...",
    "status": "Closed",
    "closedDate": "2026-06-03",
    "updatedAt": "2026-06-03T12:00:00Z"
  },
  "metadata": { "toolName": "DashboardEndpoints.UpdatePoamStatus", "executionTimeMs": 18 }
}
```

**Response 400** — invalid status value or missing `closedDate`:

```json
{
  "status": "error",
  "error": {
    "code": "INVALID_REQUEST",
    "message": "closedDate is required when status is 'Closed'."
  }
}
```

**Response 404** — `{systemId}` or `{poamId}` not found for this tenant (both map to 404 for isolation).

---

## GAP-014: Chat endpoint — multipart file attachment support

**Status**: MODIFIED (was `application/json` only; file bytes silently dropped)
**GitHub Issue**: #145

**Endpoint**: The existing chat SSE endpoint (path TBD from `DashboardEndpoints.cs` — confirm exact route during implementation).

### Request — text-only (unchanged path)

When no attachments, behavior is identical to today:

```http
POST /api/dashboard/chat/...
Content-Type: application/json

{
  "message": "What controls does this system inherit?",
  "systemId": "..."
}
```

### Request — with attachments (NEW path)

When `attachments.length > 0`, the frontend sends `multipart/form-data`:

```http
POST /api/dashboard/chat/...
Content-Type: multipart/form-data; boundary=----BoundaryXYZ

------BoundaryXYZ
Content-Disposition: form-data; name="message"
Content-Type: application/json

{"message":"Analyze this document","systemId":"..."}
------BoundaryXYZ
Content-Disposition: form-data; name="file"; filename="ssp-draft.pdf"
Content-Type: application/pdf

<binary file bytes>
------BoundaryXYZ--
```

Multiple files produce multiple `file` parts with the same field name `"file"`.

### Backend binding

The chat handler gains an additional binding:

```csharp
// Existing: reads JSON body
// New: when Content-Type is multipart/form-data, bind:
IFormFileCollection files = context.Request.Form.Files;
// Forward files to MCP tool call alongside the message payload.
```

**Non-regression invariant**: When `Content-Type: application/json`, the handler behaves exactly as before. The multipart branch is entered only when `Content-Type` starts with `multipart/form-data`.

### Response (unchanged shape)

The SSE stream response shape is unchanged. File processing results appear in the streamed AI response content, not in the HTTP response headers or status.

**Response 400** — file too large (if a per-file size limit is enforced):

```json
{
  "status": "error",
  "error": {
    "code": "ATTACHMENT_TOO_LARGE",
    "message": "File 'ssp-draft.pdf' exceeds the 10 MB attachment limit."
  }
}
```

**Response 415 Unsupported Media Type** — if an unsupported file type is rejected (only if the MCP tool enforces a type allow-list).

---

## Error Code Reference

| Code | HTTP Status | Description |
|---|---|---|
| `SYSTEM_NOT_FOUND` | 404 | System ID not found for authenticated tenant |
| `INVALID_PROFILE_ID` | 400 | Unknown profile ID in apply-profile request |
| `INVALID_SOURCE_SYSTEM` | 400 | Invalid or non-GUID sourceSystemId |
| `INVALID_REQUEST` | 400 | Missing required field or invalid enum value |
| `ATTACHMENT_TOO_LARGE` | 400 | File exceeds server size limit |
| `UNAUTHORIZED` | 401 | Missing or invalid authentication token |
| `FORBIDDEN` | 403 | Authenticated but insufficient permissions for operation |
