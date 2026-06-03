# HTTP API Contract — 059 Orphaned Endpoints

All endpoints below are already implemented in `Ato.Copilot.Mcp`. This document
catalogues them for frontend consumers. No new backend endpoints are introduced
by this epic.

---

## Audit Query — `AuditQueryEndpoints`

### `GET /api/audit`

**Auth**: CSP-Admin only. Returns `403 FORBIDDEN_NOT_CSP_ADMIN` for all other roles.

**Query parameters**

| Param | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `tenantId` | `Guid` | No | — | Filter by target tenant |
| `actorTenantId` | `Guid` | No | — | Filter by actor's home tenant |
| `actorOid` | `string` | No | — | Filter by actor OID (exact match) |
| `action` | `string` | No | — | Filter by action string (exact match) |
| `from` | `DateTime` (ISO 8601) | No | — | Inclusive lower timestamp bound |
| `to` | `DateTime` (ISO 8601) | No | — | Inclusive upper timestamp bound |
| `page` | `int` | No | `1` | Must be ≥ 1 |
| `pageSize` | `int` | No | `50` | Clamped to `[1, 200]` |

**Response `200 OK`**

```json
{
  "status": "success",
  "data": {
    "items": [
      {
        "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "tenantId": "...",
        "actorTenantId": "...",
        "userId": "aad-oid-string",
        "action": "Capability.Create",
        "entityType": "CspInheritedCapability",
        "entityId": "...",
        "timestamp": "2026-06-01T12:34:56Z",
        "metadataJson": "{}"
      }
    ],
    "page": 1,
    "pageSize": 50,
    "total": 142
  },
  "metadata": { "executionTimeMs": 12, "timestamp": "2026-06-01T12:34:56Z" }
}
```

**Error responses**

| Code | `code` field | Condition |
|------|-------------|-----------|
| `400` | `INVALID_PAGINATION` | `page < 1` or `pageSize < 1` |
| `403` | `FORBIDDEN_NOT_CSP_ADMIN` | Caller is not CSP-Admin |

---

## Admin Migration — `AdminMigrationEndpoints`

### `GET /api/admin/migrate-to-multitenant/preview`

**Auth**: CSP-Admin only.

**Response `200 OK`**

```json
{
  "status": "success",
  "data": {
    "tables": [
      { "tableName": "Systems", "rowCount": 14, "requiresOverride": false },
      { "tableName": "Capabilities", "rowCount": 203, "requiresOverride": false }
    ]
  },
  "metadata": { "executionTimeMs": 45, "timestamp": "..." }
}
```

---

### `POST /api/admin/migrate-to-multitenant`

**Auth**: CSP-Admin only.

**Request body**

```json
{
  "defaultTenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "installRls": true,
  "overrides": [
    {
      "tableName": "Systems",
      "rowIdPrefix": "legacy-",
      "tenantId": "..."
    }
  ]
}
```

**Response `200 OK`** — migration applied.

**Error responses**

| Code | `code` field | Condition |
|------|-------------|-----------|
| `400` | `INVALID_REQUEST` | `defaultTenantId` is empty or missing |
| `403` | `FORBIDDEN_NOT_CSP_ADMIN` | Caller is not CSP-Admin |

---

## Notifications — `NotificationEndpoints`

### `GET /api/notifications`

**Auth**: Authenticated user. Returns notifications for the current user.

**Query parameters**: `page`, `pageSize` (same pagination contract as audit).

**Response `200 OK`**

```json
{
  "status": "success",
  "data": {
    "items": [
      {
        "id": "...",
        "type": "capabilityReview",
        "title": "Capability needs review",
        "body": "...",
        "isRead": false,
        "createdAt": "2026-06-01T10:00:00Z"
      }
    ],
    "page": 1, "pageSize": 50, "total": 3
  }
}
```

---

### `GET /api/notifications/summary`

**Response `200 OK`**: `{ "unreadCount": 3 }`

---

### `POST /api/notifications/mark-read`

**Body**: `{ "ids": ["guid1", "guid2"] }`
**Response**: `204 No Content`

---

### `POST /api/notifications/mark-all-read`

**Body**: `{}` (empty)
**Response**: `204 No Content`

---

### `GET /api/notifications/preferences`

**Response `200 OK`**

```json
{
  "status": "success",
  "data": {
    "preferences": {
      "capabilityReview": true,
      "systemAlerts": true,
      "weeklyDigest": false
    }
  }
}
```

---

### `PUT /api/notifications/preferences`

**Body**: same shape as preferences `data` object above.
**Response**: `200 OK` with updated preferences.

---

## Deployment — `DeploymentEndpoints`

### `GET /api/deployment/mode`

**Auth**: Anonymous (intentionally — called before login to determine login surface).

**Response `200 OK`**

```json
{
  "status": "success",
  "data": {
    "mode": "SingleTenant",
    "defaultTenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
  },
  "metadata": { "executionTimeMs": 1, "timestamp": "..." }
}
```

`defaultTenantId` is `null` when `mode` is `"MultiTenant"`.
