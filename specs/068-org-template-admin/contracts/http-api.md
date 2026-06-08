# HTTP API Contracts — Spec 068: Org Template & Narrative Seed Admin

**Epic:** #222 | **Owner:** Oracle  
**Note:** All endpoints are pre-existing. No new endpoints introduced by this spec.

---

## Template Endpoints

**Base URL:** `/api/onboarding/templates`  
**Auth:** `OnboardingAdministratorRequirement` (Admin role required for all)

---

### GET /api/onboarding/templates/

List all templates for the current tenant. Optionally filter by type.

**Query params:** `templateType?` (Ssp | Sar | Sap | Crm | HwSwInventory), `includeDeleted?` (boolean, default false)

**Response 200 OK**
```json
{
  "ok": true,
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "tenantId": "...",
      "templateType": "Ssp",
      "label": "Org SSP Template v2",
      "version": "v2.0",
      "originalFileName": "org-ssp-template-v2.docx",
      "fileFormat": "Docx",
      "fileSizeBytes": 524288,
      "isDefault": true,
      "validationStatus": "Compliant",
      "validationWarnings": null,
      "status": "Active",
      "createdAt": "2026-03-15T10:00:00Z",
      "updatedAt": "2026-05-01T14:30:00Z"
    }
  ]
}
```

---

### POST /api/onboarding/templates/upload

Upload a new template document.

**Request:** `multipart/form-data`

| Field | Type | Required | Notes |
|---|---|---|---|
| `templateType` | string | ✅ | One of: Ssp, Sar, Sap, Crm, HwSwInventory |
| `label` | string | ✅ | Display name |
| `version` | string | ❌ | e.g. "v1.0", "2026-Q2" |
| `isDefault` | boolean | ❌ | If true, sets this as default for the type |
| (file part) | binary | ✅ | .docx for most types; .xlsx for Crm, HwSwInventory |

**Response 201 Created**
```json
{
  "ok": true,
  "data": {
    "template": { /* OrganizationDocumentTemplateDto */ },
    "warnings": ["Missing placeholder: {ControlTitle}"]
  }
}
```

**Response 400** `TEMPLATE_WRONG_FORMAT` — unknown templateType or wrong file format  
**Response 413** `TEMPLATE_TOO_LARGE` — file exceeds configured limit

---

### PATCH /api/onboarding/templates/{id}

Update template label or version.

**Request Body**
```json
{
  "label": "Updated Label",
  "version": "v2.1"
}
```

**Response 200 OK** — updated `OrganizationDocumentTemplateDto`  
**Response 404** — not found (or cross-tenant isolation)

---

### DELETE /api/onboarding/templates/{id}

Soft-delete a template.

**Response 204 No Content**  
**Response 404** — not found

---

### POST /api/onboarding/templates/{id}/replace

Replace the template's file content (preserves metadata).

**Request:** `multipart/form-data`

| Field | Type | Required | Notes |
|---|---|---|---|
| `version` | string | ❌ | New version string |
| (file part) | binary | ✅ | Replacement file |

**Response 200 OK**
```json
{
  "ok": true,
  "data": {
    "template": { /* updated template */ },
    "dependentsFlagged": 3,
    "suggestedReRunDependencyIds": ["uuid1", "uuid2", "uuid3"]
  }
}
```

**Response 404** — not found  
**Response 413** — file too large

---

### POST /api/onboarding/templates/{id}/default

Set a template as the default for its type. Atomically clears prior default.

**Request:** No body.

**Response 200 OK** — updated template with `isDefault: true`  
**Response 404** — not found

---

### DELETE /api/onboarding/templates/{id}/default/clear

Clear the default flag from a template.

**Response 204 No Content**  
**Response 404** — not found

---

### GET /api/onboarding/templates/{id}/download

Download the template file (proxied through API — do NOT use storage blob keys directly).

**Response 200 OK** — binary file stream; `Content-Disposition: attachment; filename="original-filename.docx"`  
**Response 404** — not found

---

## Narrative Seed Endpoints

**Base URL:** `/api/onboarding/narrative-seeds`  
**Auth:** `OnboardingAdministratorRequirement` (Admin role required)

---

### GET /api/onboarding/narrative-seeds/

List all active narrative seeds for the current tenant.

**Response 200 OK**
```json
{
  "ok": true,
  "data": [
    {
      "id": "3fa85f64-...",
      "tenantId": "...",
      "label": "NIST 800-53 Org Control Guidance",
      "tags": "[\"ac\",\"sc\",\"si\"]",
      "indexingStatus": "Indexed",
      "indexJobId": null,
      "status": "Active",
      "createdAt": "2026-04-10T09:00:00Z",
      "updatedAt": "2026-04-10T09:00:05Z"
    }
  ]
}
```

**Note:** `tags` is a JSON string array — use `tryFormatTags()` helper in frontend for display.

---

### POST /api/onboarding/narrative-seeds/

Upload a new narrative seed document.

**Request:** `multipart/form-data`

| Field | Type | Required | Notes |
|---|---|---|---|
| `label` | string | ✅ | Display name |
| `tags` | string[] | ❌ | Multiple `tags` form fields OR comma-separated |
| (file part) | binary | ✅ | Any format; stored as evidence artifact |

**Response 202 Accepted**
```json
{
  "ok": true,
  "data": {
    "document": {
      "id": "...",
      "label": "NIST 800-53 Org Control Guidance",
      "tags": "[\"ac\",\"sc\"]",
      "indexingStatus": "Pending",
      "status": "Active",
      "createdAt": "2026-06-08T12:00:00Z",
      "updatedAt": "2026-06-08T12:00:00Z"
    },
    "indexJobId": null
  }
}
```

**Note:** `indexJobId` is currently `null` (stub handler). After Task #314, this may return a job ID for tracking.

**Response 400** `SSP_PDF_UNREADABLE` — label missing or file missing  
**Response 413** — file exceeds narrative seed size limit

---

### DELETE /api/onboarding/narrative-seeds/{id}

Soft-delete a narrative seed.

**Query param:** `confirmCitations` (boolean, default false)

**Response 204 No Content** — deleted  
**Response 404** — not found  
**Response 409** `WIZARD_NARRATIVE_SEED_HAS_CITATIONS` — seed has citations and `confirmCitations` was not set:

```json
{
  "ok": false,
  "errorCode": "WIZARD_NARRATIVE_SEED_HAS_CITATIONS",
  "message": "This document is referenced by existing narrative citations.",
  "suggestion": "Pass ?confirmCitations=true to acknowledge the cascade and proceed."
}
```

**Frontend handling:** If `IndexingStatus === 'Indexed'`, always pass `?confirmCitations=true` after explicit user confirmation. The 409 should only occur if the UI logic is incorrect.

---

## Standard Error Envelope

```json
{
  "ok": false,
  "errorCode": "ERROR_CODE",
  "message": "Human-readable description",
  "suggestion": "Optional remediation hint"
}
```

Common codes for this spec:
- `TEMPLATE_WRONG_FORMAT` — wrong file type or unknown TemplateType
- `TEMPLATE_TOO_LARGE` — file exceeds configured limit
- `WIZARD_NARRATIVE_SEED_HAS_CITATIONS` — delete blocked pending confirmation
