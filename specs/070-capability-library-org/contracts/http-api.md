# HTTP API Contract — 070: Capability Library (Org Scope)

All endpoints follow the existing ATO Copilot envelope pattern:

```json
{
  "status": "success" | "error",
  "data": <T>,
  "metadata": { "executionTimeMs": number, "timestamp": "ISO-8601", "tool": null },
  "error": { "errorCode": string, "message": string, "suggestion": string } | null
}
```

Authentication: Bearer JWT (MSAL). All 4 endpoints require an authenticated caller. Role
constraints are noted per endpoint.

---

## 1. `GET /api/capability-library`

Browse the org-safe CSP capability catalog. Returns only `Mapped` capabilities whose parent
component has `Status = Published`.

### Authorization
Any authenticated tenant user. No role restriction.

### Query Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `page` | integer | No | 1-based page index. Default: `1`. |
| `pageSize` | integer | No | Items per page. Default: `20`. Max: `100`. |
| `search` | string | No | Case-insensitive match against `capabilityName` or any value in `mappedNistControlIds`. |
| `provider` | string | No | Filter by `componentName` (partial, case-insensitive). |
| `controlFamily` | string | No | 2-char NIST family prefix (e.g., `AC`, `AU`, `IA`). Returns capabilities where at least one control ID starts with this prefix. |

### Response `200 OK`

```json
{
  "status": "success",
  "data": {
    "items": [
      {
        "id": "22222222-0000-0000-0000-000000000001",
        "capabilityName": "MFA Enforcement",
        "componentId": "11111111-0000-0000-0000-000000000001",
        "componentName": "Azure Active Directory",
        "componentType": "Identity",
        "description": "Enforces MFA for all users via Conditional Access policies.",
        "mappedNistControlIds": ["IA-2", "IA-2(1)", "IA-5"],
        "mappingConfidence": 0.97
      }
    ],
    "page": 1,
    "pageSize": 20,
    "total": 42
  },
  "metadata": { "executionTimeMs": 18, "timestamp": "2026-06-08T00:00:00Z", "tool": null }
}
```

### `CapabilityLibraryItemDto` Fields

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| `id` | `string` (UUID) | No | `CspInheritedCapability.Id` |
| `capabilityName` | `string` | No | `CspInheritedCapability.Name` |
| `componentId` | `string` (UUID) | No | `CspInheritedComponent.Id` |
| `componentName` | `string` | No | `CspInheritedComponent.Name` (CSP provider label) |
| `componentType` | `string` | No | `CspInheritedComponent.ComponentType` enum value |
| `description` | `string` | Yes | `CspInheritedCapability.Description` |
| `mappedNistControlIds` | `string[]` | No | NIST control IDs (may be empty array) |
| `mappingConfidence` | `number` | Yes | `[0.0, 1.0]`; null if not computed |

**Fields intentionally OMITTED** from this projection (admin-only): `mappedBy`, `createdBy`,
`reviewedBy`, `reviewerNote`, `mappingFailureReason`, `rowVersion`, `importedBy`, `sourceFileName`.

### Error Responses

| HTTP | Error Code | When |
|------|-----------|------|
| `401` | `UNAUTHORIZED` | No valid Bearer token |
| `400` | `INVALID_PAGINATION` | `pageSize > 100` or `page < 1` |

---

## 2. `POST /api/systems/{systemId}/capability-subscriptions`

Subscribe a capability to a registered system.

### Authorization
Roles: `ISSO`, `ISSM` only. Other roles → 403.

### Path Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `systemId` | string | Registered system ID (string PK of `RegisteredSystem`). |

### Request Body

```json
{
  "capabilityId": "22222222-0000-0000-0000-000000000001"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `capabilityId` | `string` (UUID) | Yes | `CspInheritedCapability.Id` to subscribe. |

### Response `201 Created` (new subscription)

```json
{
  "status": "success",
  "data": {
    "id": "33333333-0000-0000-0000-000000000001",
    "tenantId": "aaaaaaaa-0000-0000-0000-000000000001",
    "systemId": "SYS-001",
    "capabilityId": "22222222-0000-0000-0000-000000000001",
    "capabilityName": "MFA Enforcement",
    "mappedNistControlIds": ["IA-2", "IA-2(1)", "IA-5"],
    "subscribedAt": "2026-06-08T12:00:00Z",
    "subscribedBy": "isso-user-oid@example.com",
    "status": "Active"
  },
  "metadata": { "executionTimeMs": 22, "timestamp": "2026-06-08T12:00:00Z", "tool": null }
}
```

### Response `200 OK` (idempotent — already subscribed)

Same shape as 201 but returning the existing `CapabilitySubscription` row. HTTP 200 (not 201 or 409).

### `CapabilitySubscriptionDto` Fields

| Field | Type | Nullable | Description |
|-------|------|----------|-------------|
| `id` | `string` (UUID) | No | `CapabilitySubscription.Id` (server-generated) |
| `tenantId` | `string` (UUID) | No | Caller's tenant |
| `systemId` | `string` | No | System the capability is subscribed to |
| `capabilityId` | `string` (UUID) | No | `CspInheritedCapabilityId` |
| `capabilityName` | `string` | No | Denormalized name from `CspInheritedCapability` |
| `mappedNistControlIds` | `string[]` | No | From `CspInheritedCapability` |
| `subscribedAt` | `string` (ISO-8601) | No | UTC timestamp |
| `subscribedBy` | `string` | No | Actor OID |
| `status` | `"Active"` \| `"Cancelled"` | No | Subscription lifecycle |
| `cancelledAt` | `string` (ISO-8601) | Yes | Set only when status=Cancelled |
| `cancelledBy` | `string` | Yes | Set only when status=Cancelled |

### Error Responses

| HTTP | Error Code | When |
|------|-----------|------|
| `401` | `UNAUTHORIZED` | No valid Bearer token |
| `403` | `FORBIDDEN_ISSO_ISSM_REQUIRED` | Caller is not ISSO or ISSM |
| `404` | `SYSTEM_NOT_FOUND` | `systemId` not found in caller's tenant |
| `404` | `CAPABILITY_NOT_FOUND` | `capabilityId` does not exist |
| `422` | `CAPABILITY_NOT_MAPPED` | Capability `Status != Mapped` |
| `422` | `CAPABILITY_COMPONENT_NOT_PUBLISHED` | Parent component `Status != Published` |

---

## 3. `GET /api/systems/{systemId}/capability-subscriptions`

List active capability subscriptions for a system.

### Authorization
Any authenticated tenant member. No role restriction.

### Path Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `systemId` | string | Registered system ID. |

### Response `200 OK`

```json
{
  "status": "success",
  "data": [
    {
      "id": "33333333-0000-0000-0000-000000000001",
      "tenantId": "aaaaaaaa-0000-0000-0000-000000000001",
      "systemId": "SYS-001",
      "capabilityId": "22222222-0000-0000-0000-000000000001",
      "capabilityName": "MFA Enforcement",
      "mappedNistControlIds": ["IA-2", "IA-2(1)", "IA-5"],
      "subscribedAt": "2026-06-08T12:00:00Z",
      "subscribedBy": "isso-user-oid@example.com",
      "status": "Active"
    }
  ],
  "metadata": { "executionTimeMs": 8, "timestamp": "2026-06-08T12:05:00Z", "tool": null }
}
```

Returns only `Status = Active` subscriptions. Cancelled subscriptions are excluded.  
Returns empty array `[]` (not 404) when no active subscriptions exist.

### Error Responses

| HTTP | Error Code | When |
|------|-----------|------|
| `401` | `UNAUTHORIZED` | No valid Bearer token |
| `404` | `SYSTEM_NOT_FOUND` | `systemId` not found in caller's tenant |

---

## 4. `DELETE /api/systems/{systemId}/capability-subscriptions/{capabilityId}`

Unsubscribe a capability from a system (soft-delete).

### Authorization
Roles: `ISSO`, `ISSM` only.

### Path Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `systemId` | string | Registered system ID. |
| `capabilityId` | string (UUID) | `CspInheritedCapabilityId` of the subscription to cancel. |

### Response `204 No Content`

Empty body. The `CapabilitySubscription` row is soft-deleted (`Status = Cancelled`).

### Error Responses

| HTTP | Error Code | When |
|------|-----------|------|
| `401` | `UNAUTHORIZED` | No valid Bearer token |
| `403` | `FORBIDDEN_ISSO_ISSM_REQUIRED` | Caller is not ISSO or ISSM |
| `404` | `SYSTEM_NOT_FOUND` | `systemId` not found in caller's tenant |
| `404` | `SUBSCRIPTION_NOT_FOUND` | No Active subscription for this (systemId, capabilityId) triple |

---

## 5. Error Envelope Shape

All errors use the standard envelope with `status = "error"`:

```json
{
  "status": "error",
  "data": null,
  "metadata": { "executionTimeMs": 3, "timestamp": "2026-06-08T12:00:00Z", "tool": null },
  "error": {
    "errorCode": "CAPABILITY_NOT_FOUND",
    "message": "CspInheritedCapability '22222222-0000-0000-0000-000000000099' was not found.",
    "suggestion": "Verify the capabilityId is from GET /api/capability-library."
  }
}
```

---

## 6. Audit Log Actions Written

| Endpoint | `AuditLogEntry.Action` |
|----------|------------------------|
| POST subscribe | `"CapabilitySubscription.Subscribe"` |
| DELETE unsubscribe | `"CapabilitySubscription.Unsubscribe"` |
