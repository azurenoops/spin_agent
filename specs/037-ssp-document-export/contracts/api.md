# API Contracts: SSP Export Endpoints

**Feature**: 037-ssp-document-export  
**Base Path**: `/api/dashboard`  
**Auth**: Bearer token (JWT)

---

## Error Response Envelope (Constitution VII)

All error responses (4xx, 5xx) MUST use this standard envelope:

```json
{
  "message": "Human-readable error description",
  "errorCode": "EXPORT_SYSTEM_NOT_FOUND",
  "suggestion": "Verify the system ID exists and you have access to it."
}
```

| Field | Type | Description |
|-------|------|-------------|
| message | `string` | Human-readable error description |
| errorCode | `string` | Machine-readable error code (SCREAMING_SNAKE_CASE) |
| suggestion | `string` | Corrective guidance for the user |

Error codes used in this feature:
- `EXPORT_INVALID_FORMAT` — format not one of docx, pdf, json
- `EXPORT_SYSTEM_NOT_FOUND` — systemId does not exist
- `EXPORT_NOT_FOUND` — exportId does not exist
- `EXPORT_NOT_COMPLETED` — export is still in progress
- `EXPORT_FILE_EXPIRED` — file was cleaned up by retention
- `EXPORT_FORBIDDEN` — user role not authorized
- `TEMPLATE_INVALID_FILE` — uploaded file is not valid .docx
- `TEMPLATE_TOO_LARGE` — template exceeds 10 MB
- `TEMPLATE_NAME_CONFLICT` — template name already exists
- `TEMPLATE_NOT_FOUND` — templateId does not exist
- `TEMPLATE_DELETE_BLOCKED` — cannot delete the only default template

---

## SSP Export Endpoints

### POST /systems/{systemId}/exports

**Description**: Request a new SSP export. Enqueues an async background job and returns immediately.  
**RBAC**: ISSM, ISSO, AO only (FR-019)

**Path Parameters**:
| Param | Type | Description |
|-------|------|-------------|
| systemId | `Guid` | System to export |

**Request Body** (`application/json`):
```json
{
  "format": "docx",
  "templateId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

| Field | Type | Required | Validation | Description |
|-------|------|----------|------------|-------------|
| format | `string` | Yes | `docx`, `pdf`, `json` | Export format |
| templateId | `Guid?` | No | Must exist in SspTemplates | Custom template (docx only; ignored for pdf/json) |

**Response 202 Accepted**:
```json
{
  "exportId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "status": "Pending",
  "format": "docx",
  "systemId": "10c551a9-e750-47b5-901c-4faaac6b6899",
  "generatedAt": "2026-03-17T14:30:00Z"
}
```

**Response 400**: Invalid format or templateId not found  
**Response 403**: User role not authorized  
**Response 404**: System not found

---

### GET /systems/{systemId}/exports

**Description**: List export history for a system, ordered by most recent first.  
**RBAC**: ISSM, ISSO, AO

**Path Parameters**:
| Param | Type | Description |
|-------|------|-------------|
| systemId | `Guid` | System to query |

**Query Parameters**:
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| format | `string?` | all | Filter by format |
| limit | `int` | 20 | Max results (1-100) |
| offset | `int` | 0 | Pagination offset |

**Response 200**:
```json
{
  "items": [
    {
      "exportId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "format": "docx",
      "status": "Completed",
      "fileSize": 245760,
      "controlCount": 325,
      "generatedBy": "issm@example.com",
      "generatedAt": "2026-03-17T14:30:00Z",
      "completedAt": "2026-03-17T14:30:42Z",
      "templateName": "DoD SSP Template v2"
    }
  ],
  "total": 12
}
```

---

### GET /systems/{systemId}/exports/{exportId}

**Description**: Get status and metadata for a specific export.  
**RBAC**: ISSM, ISSO, AO

**Response 200**:
```json
{
  "exportId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "systemId": "10c551a9-e750-47b5-901c-4faaac6b6899",
  "format": "docx",
  "status": "Completed",
  "fileSize": 245760,
  "contentHash": "a1b2c3d4e5f6...",
  "controlCount": 325,
  "generatedBy": "issm@example.com",
  "generatedAt": "2026-03-17T14:30:00Z",
  "completedAt": "2026-03-17T14:30:42Z",
  "templateName": "DoD SSP Template v2",
  "expiresAt": "2026-04-16T14:30:00Z"
}
```

**Response 404**: Export not found

---

### GET /systems/{systemId}/exports/{exportId}/download

**Description**: Download the exported file. Returns the binary content with appropriate Content-Type.  
**RBAC**: ISSM, ISSO, AO

**Response 200**: File stream  
- `Content-Type`: `application/vnd.openxmlformats-officedocument.wordprocessingml.document` (docx), `application/pdf` (pdf), `application/json` (json)
- `Content-Disposition`: `attachment; filename="SSP-{SystemAcronym}-{date}.{ext}"`
- `Content-Length`: file size in bytes

**Response 404**: Export not found or file expired  
**Response 409**: Export not yet completed

---

## Template Management Endpoints

### GET /templates

**Description**: List all active SSP templates with pagination.  
**RBAC**: Any authenticated user (read-only list)

**Query Parameters**:
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| limit | `int` | 50 | Max results (1-100) |
| offset | `int` | 0 | Pagination offset |

**Response 200**:
```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "name": "DoD SSP Template v2",
      "description": "Standard DoD SSP with all appendices",
      "fileSize": 524288,
      "isDefault": true,
      "mergeFields": ["SystemName", "SystemAcronym", "SecurityCategorization", "..."],
      "uploadedBy": "admin@example.com",
      "uploadedAt": "2026-02-01T10:00:00Z"
    }
  ],
  "total": 3
}
```

---

### POST /templates

**Description**: Upload a new DOCX template. Validates the file is a valid .docx with recognized merge fields.  
**RBAC**: ISSM, Administrator only (FR-019)  
**Content-Type**: `multipart/form-data`

**Form Fields**:
| Field | Type | Required | Validation | Description |
|-------|------|----------|------------|-------------|
| file | `IFormFile` | Yes | .docx, ≤ 10 MB, valid ZIP with word/document.xml | Template file |
| name | `string` | Yes | max 200, unique | Template display name |
| description | `string` | No | max 1000 | Optional description |
| isDefault | `bool` | No | default false | Set as default template |

**Response 201**:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "DoD SSP Template v2",
  "mergeFields": ["SystemName", "SystemAcronym", "SecurityCategorization"],
  "isDefault": false,
  "uploadedAt": "2026-03-17T15:00:00Z"
}
```

**Response 400**: Invalid file (not .docx, exceeds 10 MB, missing required merge fields)  
**Response 403**: User role not authorized  
**Response 409**: Template name already exists

---

### DELETE /templates/{templateId}

**Description**: Soft-delete a template (sets `IsActive = false`). Cannot delete the default template if it's the only one.  
**RBAC**: ISSM, Administrator only

**Response 204**: Template deactivated  
**Response 403**: User role not authorized  
**Response 404**: Template not found  
**Response 409**: Cannot delete the only default template

---

### PUT /templates/{templateId}

**Description**: Rename or update a template's metadata (name, description). Does not replace the template file.  
**RBAC**: ISSM, Administrator only

**Path Parameters**:
| Param | Type | Description |
|-------|------|-------------|
| templateId | `Guid` | Template to update |

**Request Body** (`application/json`):
```json
{
  "name": "Updated Template Name",
  "description": "Updated description"
}
```

| Field | Type | Required | Validation | Description |
|-------|------|----------|------------|-------------|
| name | `string` | No | max 200, unique among active | New display name |
| description | `string` | No | max 1000 | New description |

**Response 200**:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Updated Template Name",
  "description": "Updated description",
  "updatedAt": "2026-03-17T16:00:00Z"
}
```

**Response 403**: User role not authorized  
**Response 404**: Template not found  
**Response 409**: Template name already exists

---

## SignalR Events

**Hub**: `/hubs/notifications` (existing)

### SspExportReady

Sent to the requesting user's group (`user:{userId}`) when an export completes.

```json
{
  "type": "SspExportReady",
  "exportId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "systemId": "10c551a9-e750-47b5-901c-4faaac6b6899",
  "format": "docx",
  "fileSize": 245760,
  "controlCount": 325,
  "downloadUrl": "/api/dashboard/systems/{systemId}/exports/{exportId}/download"
}
```

### SspExportFailed

Sent to the requesting user's group when an export fails.

```json
{
  "type": "SspExportFailed",
  "exportId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "systemId": "10c551a9-e750-47b5-901c-4faaac6b6899",
  "format": "docx",
  "errorMessage": "Template merge failed: missing required field 'SystemName'"
}
```

### SspExportProgress

Sent periodically during long-running exports.

```json
{
  "type": "SspExportProgress",
  "exportId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "step": "Rendering PDF",
  "progress": 65
}
```
