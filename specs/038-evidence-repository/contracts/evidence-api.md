# API Contract: Evidence Repository

**Feature**: 038-evidence-repository | **Date**: 2026-03-18
**Base URL**: `/api/dashboard`

All endpoints follow the existing `DashboardEndpoints.cs` Minimal API pattern. Responses use standard JSON. File uploads use `multipart/form-data`.

---

## Endpoints

### Evidence Artifact CRUD

#### `POST /systems/{systemId}/evidence`

Upload an evidence file and attach it to a control implementation or security capability.

**Content-Type**: `multipart/form-data`

| Form Field | Type | Required | Description |
|-----------|------|----------|-------------|
| `file` | File | Yes | Evidence file (max 25 MB, allowlisted types) |
| `controlImplementationId` | string | Conditional | Target control (required if no `securityCapabilityId`) |
| `securityCapabilityId` | string | Conditional | Target capability (required if no `controlImplementationId`) |
| `description` | string | No | User-provided description (max 2000 chars) |
| `artifactCategory` | string | Yes | One of: `Screenshot`, `ScanResult`, `ConfigurationExport`, `PolicyDocument`, `AuditLog`, `TestResult`, `Other` |
| `collectionMethod` | string | No | One of: `Manual`, `AutomatedScan`, `ApiExport`, `Other`. Default: `Manual` |

**Response**: `201 Created`
```json
{
  "id": "a1b2c3d4-...",
  "fileName": "firewall-config.pdf",
  "contentType": "application/pdf",
  "fileSizeBytes": 524288,
  "artifactCategory": "ConfigurationExport",
  "collectionMethod": "Manual",
  "contentHash": "e3b0c44298fc1c149afb...",
  "uploadedBy": "john.doe",
  "uploadedAt": "2026-03-18T14:30:00Z",
  "controlImplementationId": "ctrl-impl-123",
  "securityCapabilityId": null,
  "description": "Firewall ruleset export from Palo Alto"
}
```

**Errors**:
- `400` — Invalid file type, zero-byte file, file too large, missing required fields
- `404` — System, control implementation, or capability not found

---

#### `GET /systems/{systemId}/evidence`

List all evidence artifacts for a system (paginated). Returns both user-uploaded (`EvidenceArtifact`) and automated (`ComplianceEvidence`) records in a unified response.

**Query Parameters**:

| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `page` | int | 1 | Page number |
| `pageSize` | int | 50 | Items per page (max 100) |
| `search` | string | | Filter by filename, controlId, or description |
| `controlFamily` | string | | Filter by control family prefix (e.g., "AC") |
| `category` | string | | Filter by artifact category |
| `source` | string | | Filter by source: `manual`, `automated`, or omit for both |
| `dateFrom` | string | | ISO 8601 date, filter evidence uploaded/collected after this date |
| `dateTo` | string | | ISO 8601 date, filter evidence uploaded/collected before this date |
| `sortBy` | string | `uploadedAt` | Sort field: `uploadedAt`, `fileName`, `controlId`, `category` |
| `sortOrder` | string | `desc` | `asc` or `desc` |

**Response**: `200 OK`
```json
{
  "items": [
    {
      "id": "a1b2c3d4-...",
      "source": "Manual",
      "fileName": "firewall-config.pdf",
      "contentType": "application/pdf",
      "fileSizeBytes": 524288,
      "artifactCategory": "ConfigurationExport",
      "controlId": "AC-4",
      "controlImplementationId": "ctrl-impl-123",
      "securityCapabilityId": null,
      "description": "Firewall ruleset export",
      "uploadedBy": "john.doe",
      "uploadedAt": "2026-03-18T14:30:00Z",
      "contentHash": "e3b0c44298fc1c149afb..."
    },
    {
      "id": "x9y8z7w6-...",
      "source": "Automated",
      "fileName": null,
      "contentType": null,
      "fileSizeBytes": null,
      "artifactCategory": "PolicyCompliance",
      "controlId": "AC-2",
      "controlImplementationId": null,
      "securityCapabilityId": null,
      "description": "Automated evidence collection for AC-2",
      "uploadedBy": "ATO Copilot (automated)",
      "uploadedAt": "2026-03-17T10:00:00Z",
      "contentHash": "a7ffc6f8bf1ed7667..."
    }
  ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 50
}
```

---

#### `GET /systems/{systemId}/evidence/{evidenceId}`

Get detail for a single evidence artifact (user-uploaded or automated).

**Response**: `200 OK`
```json
{
  "id": "a1b2c3d4-...",
  "source": "Manual",
  "fileName": "firewall-config.pdf",
  "contentType": "application/pdf",
  "fileSizeBytes": 524288,
  "storagePath": "evidence/sys-123/a1b2c3d4/firewall-config.pdf",
  "artifactCategory": "ConfigurationExport",
  "collectionMethod": "Manual",
  "controlId": "AC-4",
  "controlImplementationId": "ctrl-impl-123",
  "securityCapabilityId": null,
  "capabilityName": null,
  "description": "Firewall ruleset export",
  "uploadedBy": "john.doe",
  "uploadedAt": "2026-03-18T14:30:00Z",
  "contentHash": "e3b0c44298fc1c149afb...",
  "versions": [
    {
      "id": "v1-...",
      "fileName": "firewall-config-old.pdf",
      "fileSizeBytes": 480000,
      "replacedBy": "jane.smith",
      "replacedAt": "2026-03-17T09:00:00Z",
      "isFilePurged": false
    }
  ]
}
```

**Errors**: `404` — Evidence not found

---

#### `GET /systems/{systemId}/evidence/{evidenceId}/download`

Download the evidence file.

**Response**: `200 OK` with `Content-Disposition: attachment; filename="original-name.pdf"` and appropriate `Content-Type`.

**Errors**:
- `404` — Evidence not found or is automated (no file to download)

---

#### `PUT /systems/{systemId}/evidence/{evidenceId}`

Replace an evidence artifact's file. Creates an `EvidenceVersion` record for the old file.

**Content-Type**: `multipart/form-data`

| Form Field | Type | Required | Description |
|-----------|------|----------|-------------|
| `file` | File | Yes | New evidence file |
| `description` | string | No | Updated description |

**Response**: `200 OK` — Updated artifact DTO (same shape as POST response)

**Errors**: `400`, `404`

---

#### `DELETE /systems/{systemId}/evidence/{evidenceId}`

Soft-delete an evidence artifact.

**Response**: `204 No Content`

**Errors**: `404`

---

### Evidence Counts & Summary

#### `GET /systems/{systemId}/evidence/summary`

Get evidence summary statistics for the system.

**Response**: `200 OK`
```json
{
  "totalCount": 42,
  "manualCount": 30,
  "automatedCount": 12,
  "controlsWithEvidence": 85,
  "totalControls": 120,
  "coveragePercentage": 70.8
}
```

---

### Control-Level Evidence

#### `GET /systems/{systemId}/controls/{controlId}/evidence`

Get all evidence for a specific control, including capability-inherited evidence.

**Response**: `200 OK`
```json
{
  "direct": [
    { "id": "...", "source": "Manual", "fileName": "screenshot.png", ... }
  ],
  "inherited": [
    {
      "id": "...",
      "source": "Manual",
      "fileName": "fw-config.pdf",
      "inheritedFromCapability": "Boundary Protection",
      "securityCapabilityId": "cap-456",
      ...
    }
  ],
  "automated": [
    { "id": "...", "source": "Automated", "description": "Policy snapshot for AC-4", ... }
  ]
}
```

---

### Automated Evidence Collection Trigger

#### `POST /systems/{systemId}/controls/{controlId}/collect-evidence`

Trigger automated evidence collection for a specific control using the existing `EvidenceStorageService`.

**Request Body**: None (control ID and subscription ID derived from system context)

**Response**: `200 OK`
```json
{
  "evidenceId": "ce-new-123",
  "controlId": "AC-2",
  "evidenceType": "PolicyComplianceSnapshot",
  "collectedAt": "2026-03-18T14:35:00Z",
  "contentHash": "b94d27b9934d3e08a52..."
}
```

**Errors**:
- `404` — System or control not found
- `502` — Azure service unavailable (error snapshot still recorded)

---

### Version History

#### `GET /systems/{systemId}/evidence/{evidenceId}/versions`

Get version history for an evidence artifact.

**Response**: `200 OK`
```json
[
  {
    "id": "v1-...",
    "fileName": "firewall-config-v1.pdf",
    "fileSizeBytes": 480000,
    "contentHash": "abc123...",
    "replacedBy": "jane.smith",
    "replacedAt": "2026-03-17T09:00:00Z",
    "purgeAfter": "2027-03-17T09:00:00Z",
    "isFilePurged": false
  }
]
```

#### `GET /systems/{systemId}/evidence/{evidenceId}/versions/{versionId}/download`

Download a previous version's file (if not purged).

**Response**: `200 OK` with file content, or `410 Gone` if file has been purged.
