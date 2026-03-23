# API Contracts: Dashboard Endpoints

**Feature**: 041-emass-package | **Date**: 2026-03-19

All endpoints are under the `/api/v1` prefix. Authentication required. RBAC enforced per endpoint.

---

## Authorization Package Endpoints

### POST `/api/v1/systems/{systemId}/packages`

Generate a new authorization package for the specified system. Enqueues a background job and returns immediately.

**Roles**: ISSM, AO

**Request Body**:
```json
{
  "evidenceMode": "embedded" | "manifest-only",
  "includeEvidence": true
}
```

**Response** (202 Accepted):
```json
{
  "packageId": "uuid",
  "systemId": "uuid",
  "status": "Pending",
  "generatedBy": "user@example.com",
  "generatedAt": "2026-03-19T10:00:00Z",
  "message": "Package generation has been queued. Monitor progress via SignalR or poll the status endpoint."
}
```

**Error Responses**:
- 400: Readiness check failed (missing artifacts, SSP sections not approved, SAR not approved)
- 403: Insufficient role
- 404: System not found

**400 Response (Readiness Failure)**:
```json
{
  "status": "error",
  "errorCode": "PACKAGE_NOT_READY",
  "message": "System is not ready for package generation.",
  "readinessChecklist": [
    { "artifact": "OSCAL SSP", "ready": true, "detail": "13/13 sections approved" },
    { "artifact": "OSCAL POA&M", "ready": true, "detail": "5 active items" },
    { "artifact": "OSCAL Assessment Results", "ready": true, "detail": "325 controls assessed" },
    { "artifact": "OSCAL SAP", "ready": false, "detail": "No finalized SAP found" },
    { "artifact": "SAR", "ready": false, "detail": "SAR is in Draft status, Approved required" },
    { "artifact": "Evidence", "ready": true, "detail": "42 artifacts linked, 3 controls without evidence (warning)" }
  ]
}
```

---

### GET `/api/v1/systems/{systemId}/packages`

List package generation history for a system.

**Roles**: ISSM, AO, ISSO

**Query Parameters**:
- `limit` (int, default: 25, max: 100)
- `offset` (int, default: 0)
- `includeFailed` (bool, default: false)

**Response** (200):
```json
{
  "items": [
    {
      "packageId": "uuid",
      "status": "Completed",
      "artifactCount": 6,
      "validationPassed": true,
      "validationErrorCount": 0,
      "validationWarningCount": 2,
      "fileSize": 15728640,
      "generatedBy": "user@example.com",
      "generatedAt": "2026-03-19T10:00:00Z",
      "completedAt": "2026-03-19T10:01:30Z",
      "expiresAt": "2026-04-18T10:00:00Z"
    }
  ],
  "totalCount": 5,
  "limit": 25,
  "offset": 0
}
```

---

### GET `/api/v1/systems/{systemId}/packages/{packageId}`

Get detailed information about a specific package.

**Roles**: ISSM, AO, ISSO

**Response** (200):
```json
{
  "packageId": "uuid",
  "systemId": "uuid",
  "status": "Completed",
  "evidenceMode": "embedded",
  "artifacts": [
    {
      "artifactId": "uuid",
      "type": "OscalSsp",
      "format": "json",
      "fileName": "oscal-ssp.json",
      "fileSize": 524288,
      "oscalVersion": "1.1.2",
      "schemaValid": true,
      "generatedAt": "2026-03-19T10:00:15Z"
    },
    {
      "artifactId": "uuid",
      "type": "Sar",
      "format": "docx",
      "fileName": "security-assessment-report.docx",
      "fileSize": 1048576,
      "oscalVersion": null,
      "schemaValid": null,
      "generatedAt": "2026-03-19T10:01:00Z"
    }
  ],
  "validation": {
    "isValid": true,
    "errorCount": 0,
    "warningCount": 2,
    "findings": [
      {
        "severity": "Warning",
        "category": "EvidenceCoverage",
        "artifactType": "EvidenceManifest",
        "description": "Control SI-4 has no associated evidence artifacts.",
        "remediation": "Upload scan results or configuration evidence for SI-4 via the Evidence Repository."
      }
    ]
  },
  "fileSize": 15728640,
  "generatedBy": "user@example.com",
  "generatedAt": "2026-03-19T10:00:00Z",
  "completedAt": "2026-03-19T10:01:30Z",
  "expiresAt": "2026-04-18T10:00:00Z"
}
```

---

### GET `/api/v1/systems/{systemId}/packages/{packageId}/download`

Download the package ZIP archive.

**Roles**: ISSM, AO

**Response** (200): Binary stream (`application/zip`)
- `Content-Disposition: attachment; filename="auth-package-{systemName}-{date}.zip"`

**Error Responses**:
- 404: Package not found or expired
- 409: Package generation not yet complete

---

### POST `/api/v1/systems/{systemId}/packages/validate`

Run pre-submission validation without generating a package. Returns readiness checklist.

**Roles**: ISSM, AO, ISSO

**Response** (200):
```json
{
  "isReady": false,
  "errorCount": 1,
  "warningCount": 3,
  "checklist": [
    { "artifact": "OSCAL SSP", "ready": true, "detail": "All 13 sections approved, OSCAL 1.1.2 schema valid" },
    { "artifact": "OSCAL POA&M", "ready": true, "detail": "8 active items, OSCAL 1.1.2 schema valid" },
    { "artifact": "OSCAL Assessment Results", "ready": true, "detail": "325 controls assessed" },
    { "artifact": "OSCAL SAP", "ready": false, "detail": "No finalized SAP exists for this system" },
    { "artifact": "SAR", "ready": true, "detail": "Approved on 2026-03-15" },
    { "artifact": "Evidence", "ready": true, "detail": "42 artifacts, 3 controls without evidence (warning)" }
  ],
  "crossReferenceChecks": [
    { "check": "OSCAL Version Consistency", "passed": true, "detail": "All artifacts use OSCAL 1.1.2" },
    { "check": "Control ID Consistency", "passed": true, "detail": "325 controls match across SSP/POA&M/AR" },
    { "check": "SSP Section Completeness", "passed": true, "detail": "13/13 sections Approved" },
    { "check": "POA&M Reference Integrity", "passed": true, "detail": "All findings referenced exist" }
  ]
}
```

---

## Security Assessment Report (SAR) Endpoints

### POST `/api/v1/systems/{systemId}/sar`

Generate a new SAR from existing assessment data. Auto-populates findings sections and creates editable narrative sections.

**Roles**: SCA, ISSM, AO

**Request Body**:
```json
{
  "title": "Security Assessment Report — ACME System — FY26 Q2",
  "sapId": "uuid-of-governing-sap"
}
```

**Response** (201 Created):
```json
{
  "sarId": "uuid",
  "systemId": "uuid",
  "title": "Security Assessment Report — ACME System — FY26 Q2",
  "status": "Draft",
  "totalControlsAssessed": 310,
  "totalControlsPending": 15,
  "satisfiedCount": 295,
  "notSatisfiedCount": 15,
  "sections": [
    { "sectionType": "ExecutiveSummary", "title": "Executive Summary", "isAutoGenerated": true, "hasContent": true },
    { "sectionType": "AssessmentScope", "title": "Assessment Scope & Methodology", "isAutoGenerated": true, "hasContent": true },
    { "sectionType": "FindingsSummary", "title": "Findings Summary", "isAutoGenerated": true, "hasContent": true },
    { "sectionType": "FindingDetails", "title": "Individual Finding Details", "isAutoGenerated": true, "hasContent": true },
    { "sectionType": "Recommendations", "title": "Recommendations", "isAutoGenerated": false, "hasContent": false }
  ],
  "createdBy": "assessor@example.com",
  "createdAt": "2026-03-19T10:00:00Z"
}
```

**Error Responses**:
- 400: No assessment data exists for this system
- 403: Insufficient role
- 404: System not found

---

### GET `/api/v1/systems/{systemId}/sar/{sarId}`

Get SAR details and section contents.

**Roles**: SCA, ISSM, AO, ISSO

---

### PUT `/api/v1/systems/{systemId}/sar/{sarId}/sections/{sectionType}`

Edit a SAR narrative section. Only allowed when SAR is in `NotStarted` or `Draft` status.

**Roles**: SCA, ISSM, AO

**Request Body**:
```json
{
  "content": "## Executive Summary\n\nThe assessment of ACME System identified..."
}
```

**Response** (200):
```json
{
  "sectionType": "ExecutiveSummary",
  "content": "...",
  "modifiedBy": "assessor@example.com",
  "modifiedAt": "2026-03-19T11:00:00Z"
}
```

**Error Responses**:
- 400: Section is read-only (FindingsSummary, FindingDetails)
- 409: SAR is in UnderReview or Approved status (not editable)

---

### POST `/api/v1/systems/{systemId}/sar/{sarId}/submit`

Submit SAR for review (Draft → UnderReview).

**Roles**: SCA, ISSM

---

### POST `/api/v1/systems/{systemId}/sar/{sarId}/review`

Approve or request revision of SAR.

**Roles**: ISSM, AO

**Request Body**:
```json
{
  "decision": "approve" | "request_revision",
  "comments": "Optional reviewer comments"
}
```

---

### GET `/api/v1/systems/{systemId}/sar/{sarId}/export`

Download SAR as Word document.

**Roles**: SCA, ISSM, AO

**Response** (200): Binary stream (`application/vnd.openxmlformats-officedocument.wordprocessingml.document`)

---

## Standalone OSCAL Export Endpoints

### GET `/api/v1/systems/{systemId}/exports/oscal-poam`

Download OSCAL 1.1.2 POA&M as standalone JSON file.

**Roles**: ISSM, AO

**Response** (200): JSON stream (`application/json`)
- `Content-Disposition: attachment; filename="oscal-poam-{systemName}.json"`

---

### GET `/api/v1/systems/{systemId}/exports/oscal-assessment-results`

Download OSCAL 1.1.2 Assessment Results as standalone JSON file.

**Roles**: ISSM, AO

**Response** (200): JSON stream (`application/json`)
- `Content-Disposition: attachment; filename="oscal-assessment-results-{systemName}.json"`

---

### GET `/api/v1/systems/{systemId}/exports/oscal-sap`

Download OSCAL 1.1.2 Assessment Plan as standalone JSON file.

**Roles**: ISSM, AO

**Response** (200): JSON stream (`application/json`)
- `Content-Disposition: attachment; filename="oscal-assessment-plan-{systemName}.json"`

---

### POST `/api/v1/systems/{systemId}/exports/validate-oscal`

Validate a single OSCAL artifact against the NIST JSON Schema.

**Roles**: ISSM, AO, SCA

**Request Body**:
```json
{
  "model": "ssp" | "poam" | "assessment-results" | "assessment-plan",
  "content": "{ ... OSCAL JSON string ... }"
}
```

**Response** (200):
```json
{
  "isValid": true,
  "oscalVersion": "1.1.2",
  "model": "ssp",
  "schemaErrors": [],
  "structuralWarnings": ["by-component references component UUID '...' not found"]
}
```

---

## SignalR Hub: PackageHub

**Hub Path**: `/hubs/package`

**Server → Client Events**:

| Event | Payload | When |
|-------|---------|------|
| `PackageStatusChanged` | `{ packageId, systemId, status, message }` | Status transitions |
| `PackageArtifactGenerated` | `{ packageId, artifactType, fileName, fileSize }` | Each artifact completes |
| `PackageValidationComplete` | `{ packageId, isValid, errorCount, warningCount }` | Validation finishes |
| `PackageComplete` | `{ packageId, fileSize, downloadUrl }` | Package ready for download |
| `PackageFailed` | `{ packageId, failedArtifact, errorMessage, remediation }` | Generation failure |

**Client → Server**: None (subscribe by joining system group via existing pattern).
