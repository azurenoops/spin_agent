# API Contracts: Control Inheritance & Customer Responsibility Matrix

**Feature**: 043-control-inheritance  
**Date**: 2026-03-20  
**Base Path**: `/api/dashboard`

All endpoints follow the existing `DashboardEndpoints.cs` Minimal API pattern with shared `ErrorResponse` envelope for errors.

---

## Phase 1 Endpoints

### GET /systems/{systemId}/inheritance

List all inheritance designations for a system with optional filtering and pagination.

**Parameters**:
| Param | In | Type | Required | Description |
|-------|-----|------|----------|-------------|
| systemId | path | string | yes | System GUID |
| family | query | string | no | Filter by control family (e.g., "AC", "PE") |
| inheritanceType | query | string | no | Filter by type: "Inherited", "Shared", "Customer", "Undesignated" |
| search | query | string | no | Search control ID or provider text |
| page | query | int | no | Page number (default: 1) |
| pageSize | query | int | no | Items per page (default: 50, max: 200) |
| sortBy | query | string | no | Sort field: "controlId" (default), "family", "inheritanceType", "setAt" |
| sortDirection | query | string | no | "asc" (default) or "desc" |

**Response 200**:
```json
{
  "items": [
    {
      "id": "guid",
      "controlId": "AC-2",
      "family": "AC",
      "inheritanceType": "Shared",
      "provider": "Azure Government (FedRAMP High)",
      "customerResponsibility": "Customer manages application-level user accounts...",
      "setBy": "dashboard-user",
      "setAt": "2026-03-20T15:30:00Z"
    }
  ],
  "totalItems": 325,
  "page": 1,
  "pageSize": 50,
  "summary": {
    "totalControls": 325,
    "inheritedCount": 95,
    "sharedCount": 45,
    "customerCount": 30,
    "undesignatedCount": 155,
    "inheritancePercentage": 43.1
  }
}
```

**Response 404**: System not found or no baseline
```json
{
  "error": "System or baseline not found",
  "errorCode": "BASELINE_NOT_FOUND",
  "suggestion": "Ensure the system has a control baseline configured"
}
```

---

### PUT /systems/{systemId}/inheritance

Set inheritance designation for one or more controls (used for both single and bulk updates).

**Request Body**:
```json
{
  "designations": [
    {
      "controlId": "AC-2",
      "inheritanceType": "Shared",
      "provider": "Azure Government (FedRAMP High)",
      "customerResponsibility": "Customer manages application-level user accounts..."
    }
  ],
  "changeSource": "Manual"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| designations | array | yes | 1+ designation updates |
| designations[].controlId | string | yes | NIST control ID |
| designations[].inheritanceType | string | yes | "Inherited", "Shared", or "Customer" |
| designations[].provider | string | conditional | Required for Inherited and Shared |
| designations[].customerResponsibility | string | no | Recommended for Shared |
| changeSource | string | no | "Manual" (default), "BulkUpdate", "ProfileApply", "CrmImport" |

**Response 200**:
```json
{
  "controlsUpdated": 15,
  "inheritedCount": 110,
  "sharedCount": 45,
  "customerCount": 30,
  "skippedControls": ["ZZ-99"],
  "summary": {
    "totalControls": 325,
    "inheritedCount": 110,
    "sharedCount": 45,
    "customerCount": 30,
    "undesignatedCount": 140,
    "inheritancePercentage": 47.7
  }
}
```

**Response 400**: Invalid input
```json
{
  "error": "Invalid inheritance type 'Unknown'",
  "errorCode": "INVALID_INPUT"
}
```

---

### GET /systems/{systemId}/inheritance/crm

Generate the Customer Responsibility Matrix for a system.

**Parameters**:
| Param | In | Type | Required | Description |
|-------|-----|------|----------|-------------|
| systemId | path | string | yes | System GUID |

**Response 200**:
```json
{
  "systemId": "guid",
  "systemName": "ACME Portal",
  "baselineLevel": "High",
  "totalControls": 325,
  "inheritedControls": 110,
  "sharedControls": 45,
  "customerControls": 30,
  "undesignatedControls": 140,
  "inheritancePercentage": 47.7,
  "familyGroups": [
    {
      "family": "AC",
      "familyName": "Access Control",
      "controls": [
        {
          "controlId": "AC-1",
          "inheritanceType": "Shared",
          "provider": "Azure Government (FedRAMP High)",
          "customerResponsibility": "Customer develops organization-specific policies."
        },
        {
          "controlId": "AC-2",
          "inheritanceType": "Shared",
          "provider": "Azure Government (FedRAMP High)",
          "customerResponsibility": "Customer manages application-level user accounts."
        }
      ]
    }
  ]
}
```

---

### GET /systems/{systemId}/inheritance/crm/export

Export CRM as CSV or Excel with format selection.

**Parameters**:
| Param | In | Type | Required | Description |
|-------|-----|------|----------|-------------|
| systemId | path | string | yes | System GUID |
| format | query | string | yes | "csv" or "excel" |
| layout | query | string | no | "custom" (default), "fedramp", or "emass" |

**Response 200**: Binary file download
- CSV: `Content-Type: text/csv`, filename: `crm-{systemId}-{date}.csv`
- Excel: `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, filename: `crm-{systemId}-{date}.xlsx`

**Response 400**: Invalid format
```json
{
  "error": "Unsupported export format: pdf",
  "errorCode": "EXPORT_FORMAT_INVALID"
}
```

---

### GET /systems/{systemId}/inheritance/{controlId}/audit

Get audit history for a specific control's inheritance designation.

**Parameters**:
| Param | In | Type | Required | Description |
|-------|-----|------|----------|-------------|
| systemId | path | string | yes | System GUID |
| controlId | path | string | yes | NIST control ID (e.g., "AC-2") |

**Response 200**:
```json
{
  "controlId": "AC-2",
  "entries": [
    {
      "id": "guid",
      "actor": "dashboard-user",
      "previousInheritanceType": null,
      "newInheritanceType": "Inherited",
      "previousProvider": null,
      "newProvider": "Azure Government (FedRAMP High)",
      "previousCustomerResponsibility": null,
      "newCustomerResponsibility": null,
      "changeSource": "ProfileApply",
      "timestamp": "2026-03-20T14:00:00Z"
    },
    {
      "id": "guid",
      "actor": "dashboard-user",
      "previousInheritanceType": "Inherited",
      "newInheritanceType": "Shared",
      "previousProvider": "Azure Government (FedRAMP High)",
      "newProvider": "Azure Government (FedRAMP High)",
      "previousCustomerResponsibility": null,
      "newCustomerResponsibility": "Customer configures app-level settings...",
      "changeSource": "Manual",
      "timestamp": "2026-03-20T15:30:00Z"
    }
  ]
}
```

---

## Phase 2 Endpoints

### GET /systems/{systemId}/inheritance/csp-profiles

List available CSP inheritance profiles.

**Response 200**:
```json
{
  "profiles": [
    {
      "profileId": "azure-gov-fedramp-high",
      "name": "Azure Government (FedRAMP High)",
      "provider": "Azure Government (FedRAMP High)",
      "baselineLevel": "High",
      "description": "Pre-built inheritance profile based on Microsoft Azure Government FedRAMP High CRM.",
      "controlCount": 325,
      "version": "2026-03"
    }
  ]
}
```

---

### POST /systems/{systemId}/inheritance/apply-profile

Preview and apply a CSP inheritance profile.

**Request Body**:
```json
{
  "profileId": "azure-gov-fedramp-high",
  "conflictResolution": "skip",
  "preview": true
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| profileId | string | yes | CSP profile identifier |
| conflictResolution | string | no | "skip" (default) or "overwrite" |
| preview | bool | no | true = return preview only, false = apply (default: false) |

**Response 200 (preview=true)**:
```json
{
  "preview": true,
  "profileName": "Azure Government (FedRAMP High)",
  "matchedControls": 310,
  "unmatchedControls": 15,
  "willSetInherited": 120,
  "willSetShared": 140,
  "willSetCustomer": 50,
  "willSkipExisting": 25,
  "conflicts": 25
}
```

**Response 200 (preview=false)**:
```json
{
  "applied": true,
  "controlsUpdated": 285,
  "controlsSkipped": 25,
  "summary": {
    "totalControls": 325,
    "inheritedCount": 120,
    "sharedCount": 140,
    "customerCount": 50,
    "undesignatedCount": 15,
    "inheritancePercentage": 80.0
  }
}
```

---

## Phase 3 Endpoints

### POST /systems/{systemId}/inheritance/import/preview

Upload a CRM file and return parsed columns for mapping.

**Request**: `multipart/form-data` with `file` field (CSV or Excel)

**Response 200**:
```json
{
  "fileName": "azure-crm.csv",
  "fileType": "csv",
  "totalRows": 325,
  "detectedColumns": ["Control Number", "Responsible Role", "CSP Name", "Customer Description"],
  "suggestedMapping": {
    "controlId": "Control Number",
    "inheritanceType": "Responsible Role",
    "provider": "CSP Name",
    "customerResponsibility": "Customer Description"
  },
  "sampleRows": [
    { "Control Number": "AC-1", "Responsible Role": "Shared", "CSP Name": "Azure Government", "Customer Description": "..." }
  ],
  "previewToken": "temp-token-guid"
}
```

---

### POST /systems/{systemId}/inheritance/import/apply

Apply a mapped CRM import.

**Request Body**:
```json
{
  "previewToken": "temp-token-guid",
  "columnMapping": {
    "controlId": "Control Number",
    "inheritanceType": "Responsible Role",
    "provider": "CSP Name",
    "customerResponsibility": "Customer Description"
  },
  "conflictResolution": "skip"
}
```

**Response 200**:
```json
{
  "applied": true,
  "controlsImported": 280,
  "controlsSkipped": 20,
  "controlsNotFound": 5,
  "notFoundControlIds": ["ZZ-1", "ZZ-2", "ZZ-3", "ZZ-4", "ZZ-5"],
  "duplicatesOverwritten": 0,
  "summary": {
    "totalControls": 325,
    "inheritedCount": 150,
    "sharedCount": 100,
    "customerCount": 30,
    "undesignatedCount": 45,
    "inheritancePercentage": 76.9
  }
}
```

---

## Common Types

### ErrorResponse
```json
{
  "error": "string",
  "errorCode": "string",
  "suggestion": "string (optional)"
}
```

### InheritanceSummary
Included in list and update responses:
```json
{
  "totalControls": 325,
  "inheritedCount": 110,
  "sharedCount": 45,
  "customerCount": 30,
  "undesignatedCount": 140,
  "inheritancePercentage": 47.7
}
```
