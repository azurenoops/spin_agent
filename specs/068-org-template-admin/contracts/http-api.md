# HTTP API Contract — Spec 068: Org Templates & Narrative Seed Admin

**Epic:** #222 — Org Templates & Narrative Seed Admin UI
**Base path group:** `/api/onboarding`
**Auth policy:** `OnboardingAdministratorRequirement.PolicyName` (required on all endpoints)
**Anti-forgery:** Disabled on this group (`.DisableAntiforgery()`)
**Envelope format:** All JSON responses share a standard envelope (see § Error Envelope).

---

## Table of Contents

1. [Common Types](#1-common-types)
2. [Error Envelope](#2-error-envelope)
3. [Organization Document Templates](#3-organization-document-templates)
   - [GET /api/onboarding/templates](#31-list-templates)
   - [POST /api/onboarding/templates/upload](#32-upload-template)
   - [GET /api/onboarding/templates/{id}](#33-get-template)
   - [PATCH /api/onboarding/templates/{id}](#34-patch-template-metadata)
   - [DELETE /api/onboarding/templates/{id}](#35-delete-template)
   - [GET /api/onboarding/templates/{id}/download](#36-download-template-file)
   - [POST /api/onboarding/templates/{id}/replace](#37-replace-template-file)
   - [POST /api/onboarding/templates/{id}/default](#38-mark-template-as-default)
   - [DELETE /api/onboarding/templates/{id}/default/clear](#39-clear-template-default)
4. [Narrative Seed Documents](#4-narrative-seed-documents)
   - [GET /api/onboarding/narrative-seeds](#41-list-narrative-seeds)
   - [POST /api/onboarding/narrative-seeds](#42-upload-narrative-seed)
   - [DELETE /api/onboarding/narrative-seeds/{id}](#43-delete-narrative-seed)

---

## 1. Common Types

### TemplateType (string enum)
| Value | Description |
|-------|-------------|
| `Ssp` | System Security Plan |
| `Sar` | Security Assessment Report |
| `Sap` | Security Assessment Plan |
| `Crm` | Customer Responsibility Matrix |
| `HwSwInventory` | Hardware/Software Inventory |

### FileFormat (string enum)
| Value |
|-------|
| `Docx` |
| `Xlsx` |

### ValidationStatus (string enum)
| Value |
|-------|
| `Pending` |
| `Compliant` |
| `FlaggedNonCompliant` |

### TemplateStatus (string enum)
| Value |
|-------|
| `Active` |
| `Superseded` |
| `Deleted` |

### IndexingStatus (string enum)
| Value |
|-------|
| `Pending` |
| `Indexed` |
| `Failed` |

### SeedStatus (string enum)
| Value |
|-------|
| `Active` |
| `Deleted` |

---

### OrganizationDocumentTemplate (response shape)

```jsonc
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",       // GUID string
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6", // GUID string
  "templateType": "Ssp",                               // TemplateType
  "label": "FedRAMP SSP Master Template",
  "version": "2024-Q4",
  "originalFileName": "fedramp-ssp-master-v2024Q4.docx",
  // storageBlobKey is NEVER included in API responses — backend only
  "fileFormat": "Docx",                                // FileFormat
  "fileSizeBytes": 204800,
  "contentChecksumSha256": "e3b0c44298fc1c149afb...",
  "isDefault": true,
  "validationStatus": "Compliant",                     // ValidationStatus
  "validationWarnings": null,                          // null | JSON string (array of warning strings)
  "status": "Active",                                  // TemplateStatus
  "createdAt": "2025-01-15T10:00:00Z",
  "createdBy": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "updatedAt": "2025-06-01T12:00:00Z",
  "updatedBy": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "deletedAt": null                                    // null | ISO 8601 string
}
```

> **Note:** `validationWarnings` is stored and returned as a raw JSON string (e.g., `"[\"Missing cover page\",\"Outdated header\"]"`). Clients should `JSON.parse()` this field before display.

---

### NarrativeSeedDocument (response shape)

```jsonc
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "label": "AC Policy Narrative",
  "tags": "[\"policy\",\"ac\"]",                       // JSON string — clients must JSON.parse()
  "evidenceArtifactId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "indexingStatus": "Indexed",                         // IndexingStatus
  "indexJobId": null,                                  // null | GUID string
  "indexedAt": "2025-06-01T08:00:00Z",                 // null | ISO 8601 string
  "indexedChunkCount": 42,                             // null | number
  "indexingError": null,                               // null | string
  "status": "Active",                                  // SeedStatus
  "createdAt": "2025-01-15T10:00:00Z",
  "createdBy": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "updatedAt": "2025-06-01T12:00:00Z",
  "updatedBy": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "deletedAt": null
}
```

> **Note:** `tags` is a raw JSON string in the DB and returned as-is on the wire. Clients should `JSON.parse()` for display.

---

## 2. Error Envelope

All error responses use the following JSON shape:

```jsonc
{
  "ok": false,
  "errorCode": "TEMPLATE_WRONG_FORMAT",     // machine-readable code (see tables below)
  "message": "Human-readable description.", // always present
  "suggestion": "Optional fix hint."        // may be null or absent
}
```

Success responses always include `"ok": true` plus a `"data"` key.

---

## 3. Organization Document Templates

Route group: `MapGroup("/api/onboarding/templates")`
Auth: `RequireAuthorization(OnboardingAdministratorRequirement.PolicyName)`

---

### 3.1 List Templates

```
GET /api/onboarding/templates
```

**Authentication:** Onboarding Administrator
**Content-Type:** N/A (no body)

#### Query Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `templateType` | `TemplateType` string | No | — | Filter by template type. Omit to return all types. |
| `includeDeleted` | `boolean` | No | `false` | When `true`, includes templates with `status == "Deleted"`. |

#### Success Response — `200 OK`

```jsonc
{
  "ok": true,
  "data": [
    { /* OrganizationDocumentTemplate */ },
    { /* OrganizationDocumentTemplate */ }
  ]
}
```

Returns an empty array `[]` when no templates match.

#### Error Responses

| HTTP | `errorCode` | Condition |
|------|------------|-----------|
| `401` | — | Missing or invalid auth token |
| `403` | — | Authenticated user lacks `OnboardingAdministrator` role |

---

### 3.2 Upload Template

```
POST /api/onboarding/templates/upload
```

**Authentication:** Onboarding Administrator
**Content-Type:** `multipart/form-data`

#### Request Parts

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `templateType` | string (`TemplateType`) | **Yes** | Type of the template. |
| `label` | string | **Yes** | Human-readable label for the template. |
| `version` | string | **Yes** | Version string (e.g., `"2024-Q4"`). |
| `file` | binary file | **Yes** | The template file (.docx or .xlsx). |
| `isDefault` | boolean | No | If `true`, marks this template as the default for its type. Defaults to `false`. |

#### Success Response — `201 Created`

```jsonc
{
  "ok": true,
  "data": {
    "template": { /* OrganizationDocumentTemplate */ },
    "warnings": ["Missing cover page field", "Outdated footer macro"] // string[], may be empty
  }
}
```

#### Error Responses

| HTTP | `errorCode` | Condition |
|------|------------|-----------|
| `413` | `TEMPLATE_TOO_LARGE` | File exceeds maximum allowed size |
| `415` | `TEMPLATE_WRONG_FORMAT` | File format not supported (not .docx or .xlsx) |
| `400` | — | Missing required fields (`templateType`, `label`, `version`, `file`) |
| `401` | — | Missing or invalid auth token |
| `403` | — | Insufficient permissions |

---

### 3.3 Get Template

```
GET /api/onboarding/templates/{id}
```

**Authentication:** Onboarding Administrator

#### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | GUID | **Yes** | Template ID. |

#### Success Response — `200 OK`

```jsonc
{
  "ok": true,
  "data": { /* OrganizationDocumentTemplate */ }
}
```

#### Error Responses

| HTTP | `errorCode` | Condition |
|------|------------|-----------|
| `404` | — | No template with this ID exists in the tenant |
| `401` | — | Missing or invalid auth token |
| `403` | — | Insufficient permissions |

---

### 3.4 Patch Template Metadata

```
PATCH /api/onboarding/templates/{id}
```

**Authentication:** Onboarding Administrator
**Content-Type:** `application/json`

> Updates only metadata fields (`label`, `version`). To update file content, use [Replace Template File](#37-replace-template-file). `templateType` cannot be changed after creation.

#### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | GUID | **Yes** | Template ID. |

#### Request Body

```jsonc
{
  "label": "Updated Label",   // optional string — omit to leave unchanged
  "version": "2025-Q1"       // optional string — omit to leave unchanged
}
```

At least one field must be present (both may be included simultaneously).

#### Success Response — `200 OK`

```jsonc
{
  "ok": true,
  "data": { /* OrganizationDocumentTemplate with updated fields */ }
}
```

#### Error Responses

| HTTP | `errorCode` | Condition |
|------|------------|-----------|
| `404` | — | Template not found |
| `400` | — | Request body is empty or malformed |
| `401` | — | Missing or invalid auth token |
| `403` | — | Insufficient permissions |

---

### 3.5 Delete Template

```
DELETE /api/onboarding/templates/{id}
```

**Authentication:** Onboarding Administrator

> Soft-deletes the template (sets `status = "Deleted"`, records `deletedAt`). Templates marked as default are protected from deletion.

#### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | GUID | **Yes** | Template ID. |

#### Success Response — `204 No Content`

No body.

#### Error Responses

| HTTP | `errorCode` | Condition |
|------|------------|-----------|
| `409` | `TEMPLATE_DEFAULT_PROTECTED` | Cannot delete a template that is currently the default; clear its default status first |
| `404` | — | Template not found |
| `401` | — | Missing or invalid auth token |
| `403` | — | Insufficient permissions |

---

### 3.6 Download Template File

```
GET /api/onboarding/templates/{id}/download
```

**Authentication:** Onboarding Administrator

> Returns the raw template file. Clients **must** use the blob URL pattern — do **not** link to blob storage directly.

#### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | GUID | **Yes** | Template ID. |

#### Success Response — `200 OK`

| Header | Value |
|--------|-------|
| `Content-Type` | `application/octet-stream` |
| `Content-Disposition` | `attachment; filename="<OriginalFileName>"` |

Body: raw binary file stream.

#### Error Responses

| HTTP | `errorCode` | Condition |
|------|------------|-----------|
| `404` | — | Template not found |
| `401` | — | Missing or invalid auth token |
| `403` | — | Insufficient permissions |

---

### 3.7 Replace Template File

```
POST /api/onboarding/templates/{id}/replace
```

**Authentication:** Onboarding Administrator
**Content-Type:** `multipart/form-data`

> Replaces the binary content of an existing template. All dependent artifacts (e.g., draft SSPs using this template) are flagged.

#### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | GUID | **Yes** | Template ID. |

#### Request Parts

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `file` | binary file | **Yes** | The replacement file (.docx or .xlsx). |
| `version` | string | No | New version string; if omitted the existing version is retained. |

#### Success Response — `200 OK`

```jsonc
{
  "ok": true,
  "data": {
    "template": { /* OrganizationDocumentTemplate with new file metadata */ },
    "dependentsFlagged": 3  // number of dependent artifacts flagged for review
  }
}
```

#### Error Responses

| HTTP | `errorCode` | Condition |
|------|------------|-----------|
| `413` | `TEMPLATE_TOO_LARGE` | Replacement file exceeds maximum size |
| `404` | — | Template not found |
| `401` | — | Missing or invalid auth token |
| `403` | — | Insufficient permissions |

---

### 3.8 Mark Template as Default

```
POST /api/onboarding/templates/{id}/default
```

**Authentication:** Onboarding Administrator

> Sets this template as the default for its `templateType`. Any previously default template of the same type is automatically demoted.

#### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | GUID | **Yes** | Template ID. |

#### Request Body

None.

#### Success Response — `200 OK`

```jsonc
{
  "ok": true,
  "data": { /* OrganizationDocumentTemplate with isDefault: true */ }
}
```

#### Error Responses

| HTTP | `errorCode` | Condition |
|------|------------|-----------|
| `404` | — | Template not found |
| `401` | — | Missing or invalid auth token |
| `403` | — | Insufficient permissions |

---

### 3.9 Clear Template Default

```
DELETE /api/onboarding/templates/{id}/default/clear
```

**Authentication:** Onboarding Administrator

> Removes the default designation from this template. After this call `isDefault` will be `false`. No other template is automatically promoted.

#### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | GUID | **Yes** | Template ID. |

#### Success Response — `204 No Content`

No body.

#### Error Responses

| HTTP | `errorCode` | Condition |
|------|------------|-----------|
| `404` | — | Template not found |
| `401` | — | Missing or invalid auth token |
| `403` | — | Insufficient permissions |

---

## 4. Narrative Seed Documents

Route group: `MapGroup("/api/onboarding/narrative-seeds")`
Auth: `RequireAuthorization(OnboardingAdministratorRequirement.PolicyName)`

---

### 4.1 List Narrative Seeds

```
GET /api/onboarding/narrative-seeds
```

**Authentication:** Onboarding Administrator
**Content-Type:** N/A (no body)

#### Query Parameters

None. Returns all seeds for the tenant (active only by default; deleted seeds are excluded unless an admin-level flag is added in a future iteration).

#### Success Response — `200 OK`

```jsonc
{
  "ok": true,
  "data": [
    { /* NarrativeSeedDocument */ },
    { /* NarrativeSeedDocument */ }
  ]
}
```

Returns an empty array `[]` when no seeds exist.

#### Error Responses

| HTTP | `errorCode` | Condition |
|------|------------|-----------|
| `401` | — | Missing or invalid auth token |
| `403` | — | Insufficient permissions |

---

### 4.2 Upload Narrative Seed

```
POST /api/onboarding/narrative-seeds
```

**Authentication:** Onboarding Administrator
**Content-Type:** `multipart/form-data`

> File is uploaded, stored, and an async indexing job is queued. The response returns immediately with a `jobId` that can be used to track indexing progress.

#### Request Parts

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `label` | string | **Yes** | Human-readable label for the seed document. |
| `tags` | string[] | No | Array of tag strings submitted as repeated form fields (e.g., `tags=policy&tags=ac`). |
| `file` | binary file | **Yes** | The PDF seed document. |

#### Success Response — `202 Accepted`

```jsonc
{
  "ok": true,
  "data": {
    "document": { /* NarrativeSeedDocument with indexingStatus: "Pending" */ },
    "jobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6" // GUID string | null — async indexing job ID
  }
}
```

> `jobId` may be `null` if the indexing queue is not applicable (e.g., feature-flagged off).

#### Error Responses

| HTTP | `errorCode` | Condition |
|------|------------|-----------|
| `413` | `SspPdfUnreadable` | File too large or PDF could not be parsed |
| `400` | — | Missing required fields (`label`, `file`) |
| `401` | — | Missing or invalid auth token |
| `403` | — | Insufficient permissions |

---

### 4.3 Delete Narrative Seed

```
DELETE /api/onboarding/narrative-seeds/{id}
```

**Authentication:** Onboarding Administrator

> Soft-deletes the seed document. If the seed has active citations in any wizard, deletion is blocked unless `confirmCitations=true` is passed to acknowledge the forced removal.

#### Path Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | GUID | **Yes** | Seed document ID. |

#### Query Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `confirmCitations` | boolean | No | `false` | Pass `true` to confirm deletion even when active citations exist. |

#### Success Response — `204 No Content`

No body.

#### Error Responses

| HTTP | `errorCode` | Condition |
|------|------------|-----------|
| `409` | `WIZARD_NARRATIVE_SEED_HAS_CITATIONS` | Seed has active citations and `confirmCitations` was not `true` |
| `404` | — | Seed not found |
| `401` | — | Missing or invalid auth token |
| `403` | — | Insufficient permissions |

---

## 5. Error Code Reference

| `errorCode` | HTTP | Endpoint(s) | Description |
|-------------|------|-------------|-------------|
| `TEMPLATE_WRONG_FORMAT` | `415` | Upload Template | File is not a supported format (.docx / .xlsx) |
| `TEMPLATE_TOO_LARGE` | `413` | Upload Template, Replace Template File | File exceeds the maximum allowed size |
| `TEMPLATE_DEFAULT_PROTECTED` | `409` | Delete Template | Cannot delete a template currently marked as default |
| `SspPdfUnreadable` | `413` | Upload Narrative Seed | Seed PDF is too large or could not be parsed |
| `WIZARD_NARRATIVE_SEED_HAS_CITATIONS` | `409` | Delete Narrative Seed | Seed has active citations; pass `confirmCitations=true` to force |

---

## 6. Implementation Notes

### Security
- All endpoints are protected by `OnboardingAdministratorRequirement.PolicyName`. There is no public or read-only access variant.
- Anti-forgery tokens are disabled on this group (file upload compatibility).

### File Handling
- The download endpoint streams from internal blob storage. **Clients must consume the `Content-Disposition` filename and use blob URLs** — do not construct direct blob-storage URLs.
- `storageBlobKey` is a backend-only field and is **never serialized** to API responses.

### Tags Encoding
- `NarrativeSeedDocument.tags` is stored and returned as a raw JSON string (e.g., `"[\"policy\",\"ac\"]"`). Frontend clients are responsible for `JSON.parse()` before display.

### Validation Warnings
- `OrganizationDocumentTemplate.validationWarnings` is similarly a raw JSON string when non-null. Parse before rendering.

### Async Indexing
- Uploading a narrative seed returns `202 Accepted`. The `indexingStatus` field begins as `Pending` and transitions to `Indexed` or `Failed` asynchronously. Poll or use a push channel to track completion.
