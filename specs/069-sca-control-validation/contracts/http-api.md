# HTTP API Contract — Spec 069: SCA Control Implementation Validation Link

**Base path:** `/api/systems/{systemId}/controls/{controlId}/validation`  
**Auth:** Bearer JWT — all endpoints require authentication  
**Tenant:** All responses are tenant-scoped; cross-tenant requests return 404

---

## 1. GET `/api/systems/{systemId}/controls/{controlId}/validation`

**Summary:** List all validation links for a control.  
**RBAC:** `Compliance.Reader` (minimum); `Compliance.Auditor` also allowed  
**Method:** `GET`

### Path Parameters

| Parameter | Type | Description |
|---|---|---|
| `systemId` | string (GUID or name) | Registered system identifier |
| `controlId` | string | NIST 800-53 control ID (e.g., `AC-2`, `SC-7(3)`) |

### Query Parameters

| Parameter | Type | Required | Description |
|---|---|---|---|
| `linkType` | string | No | Filter by link type: `AzureResource`, `ScanFinding`, `EvidenceArtifact`, `ExternalUrl` |
| `automated` | bool | No | If `true`, return only automated links; `false` = manual only; omit = all |

### Response — 200 OK

```json
{
  "systemId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "controlId": "AC-2",
  "total": 2,
  "links": [
    {
      "id": "a1b2c3d4-...",
      "linkType": "AzureResource",
      "linkTarget": "/subscriptions/abc/resourceGroups/rg-prod/providers/Microsoft.Authorization/roleAssignments/...",
      "description": "RBAC role assignment enforcing least-privilege access (AC-2 requirement)",
      "addedBy": "jane.assessor@agency.gov",
      "addedAt": "2026-06-11T14:30:00Z",
      "validatedAt": "2026-06-11T15:00:00Z",
      "isAutomated": false
    },
    {
      "id": "e5f6a7b8-...",
      "linkType": "ScanFinding",
      "linkTarget": "scan-2026061101-finding-3",
      "description": "IaC scan: AC-2.1 — RBAC enforced on storage account (nist-800-53-r5) — PASS",
      "addedBy": "iac-scan",
      "addedAt": "2026-06-11T12:00:00Z",
      "validatedAt": null,
      "isAutomated": true
    }
  ]
}
```

### Error Responses

| Code | Body | When |
|---|---|---|
| 401 | `{ "error": "Unauthorized" }` | No/invalid JWT |
| 403 | `{ "error": "Forbidden" }` | Authenticated but insufficient role |
| 404 | `{ "error": "System not found" }` | `systemId` does not exist for this tenant |
| 404 | `{ "error": "Control not found" }` | `controlId` has no `ControlImplementation` record |

> **Note:** 0 links returns 200 with `{ ..., "total": 0, "links": [] }` — not 404.

---

## 2. POST `/api/systems/{systemId}/controls/{controlId}/validation`

**Summary:** Add a manual validation link to a control.  
**RBAC:** `Compliance.Auditor` (SCA role required)  
**Method:** `POST`  
**Content-Type:** `application/json`

### Path Parameters

Same as GET above.

### Request Body

```json
{
  "linkType": "AzureResource",
  "linkTarget": "/subscriptions/abc/resourceGroups/rg-prod/providers/Microsoft.Compute/virtualMachines/vm-jump",
  "description": "Jump server with Conditional Access enforcing MFA (AC-2 requirement)"
}
```

| Field | Type | Required | Validation |
|---|---|---|---|
| `linkType` | string | Yes | Must be one of: `AzureResource`, `ScanFinding`, `EvidenceArtifact`, `ExternalUrl` |
| `linkTarget` | string | Yes | 1–2000 characters; `ExternalUrl` type must match URL pattern |
| `description` | string | No | Max 1000 characters |

### Response — 201 Created

```json
{
  "id": "new-link-guid",
  "linkType": "AzureResource",
  "linkTarget": "/subscriptions/abc/...",
  "description": "Jump server with Conditional Access...",
  "addedBy": "jane.assessor@agency.gov",
  "addedAt": "2026-06-11T14:30:00Z",
  "validatedAt": null,
  "isAutomated": false
}
```

`Location` header: `/api/systems/{systemId}/controls/{controlId}/validation/{id}`

### Error Responses

| Code | Body | When |
|---|---|---|
| 400 | `{ "error": "Invalid linkType" }` | Unknown `linkType` value |
| 400 | `{ "error": "linkTarget must be a valid URL" }` | `ExternalUrl` type with non-URL `linkTarget` |
| 409 | `{ "error": "DuplicateLink", "message": "A link with this target already exists for this control." }` | Duplicate `(ControlImplementationId, linkTarget)` |
| 401/403/404 | (same as GET) | |

---

## 3. DELETE `/api/systems/{systemId}/controls/{controlId}/validation/{linkId}`

**Summary:** Remove a validation link.  
**RBAC:** `Compliance.Auditor`  
**Method:** `DELETE`

### Path Parameters

| Parameter | Type | Description |
|---|---|---|
| `systemId` | string | Registered system identifier |
| `controlId` | string | NIST 800-53 control ID |
| `linkId` | string (GUID) | `ControlValidationLink` ID |

### Response — 204 No Content

Empty body.

### Error Responses

| Code | Body | When |
|---|---|---|
| 404 | `{ "error": "Link not found" }` | `linkId` does not exist for this control/tenant |
| 401/403 | (same as GET) | |

---

## RBAC Summary

| Endpoint | Compliance.Reader | Compliance.Auditor | Notes |
|---|---|---|---|
| `GET` | ✅ | ✅ | Read-only access for both |
| `POST` | ❌ | ✅ | SCA role required to add |
| `DELETE` | ❌ | ✅ | SCA role required to delete |

---

## Notes

- All timestamps are UTC ISO-8601 (`2026-06-11T14:30:00Z`)
- `addedBy` is the authenticated user's identity for manual links; `"iac-scan"` for automated links
- `validatedAt` is always `null` on creation; SCA sets it by separately confirming the link (future feature — V2)
- `isAutomated` is server-set; clients cannot override it via POST
