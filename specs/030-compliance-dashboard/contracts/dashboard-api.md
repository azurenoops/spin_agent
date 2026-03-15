# API Contracts: Visual Compliance Dashboard

**Feature**: 030-compliance-dashboard
**Base URL**: `/api/dashboard`
**Auth**: Bearer token (same as MCP server); RBAC via RmfRoleAssignment
**Pagination**: Cursor-based, max page size 100

---

## Common Types

### PaginatedResponse\<T\>

```json
{
  "items": [],
  "nextCursor": "string | null",
  "totalCount": 0
}
```

### ErrorResponse

```json
{
  "error": "string",
  "errorCode": "string",
  "details": "string | null",
  "suggestion": "string | null"
}
```

Per Constitution Principle VII, all error responses MUST include:
- `error`: Human-readable message
- `errorCode`: Machine-readable code (e.g., `SYSTEM_NOT_FOUND`, `CAPABILITY_NAME_DUPLICATE`, `VALIDATION_FAILED`)
- `details`: Additional context (nullable)
- `suggestion`: Corrective guidance (e.g., "Check the system ID and try again")

---

## 1. Portfolio

### GET /api/dashboard/portfolio

Returns summary metrics for all systems accessible to the authenticated user.

**Query Parameters**:
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| sortBy | string | "name" | Column: name, impactLevel, rmfPhase, complianceScore, atoExpiration, openPoamCount |
| sortDir | string | "asc" | "asc" or "desc" |
| impactLevel | string? | | Filter: Low, Moderate, High |
| rmfPhase | string? | | Filter: Prepare, Categorize, Select, Implement, Assess, Authorize, Monitor |
| cursor | string? | | Pagination cursor |
| pageSize | int | 50 | 1-100 |

**Response** `200 OK`:

```json
{
  "items": [
    {
      "systemId": "guid",
      "name": "string",
      "impactLevel": "Moderate",
      "currentRmfPhase": "Implement",
      "complianceScore": 78.5,
      "complianceScoreDelta": -2.3,
      "atoExpirationDate": "2026-09-15T00:00:00Z",
      "atoStatus": "Active",
      "atoDaysRemaining": 185,
      "atoSeverity": "green",
      "openPoamCount": 12,
      "overduePoamCount": 3,
      "catICounts": 2,
      "catIICounts": 5,
      "catIIICounts": 8
    }
  ],
  "nextCursor": "string | null",
  "totalCount": 15
}
```

`atoSeverity` values: `"green"` (>90d), `"yellow"` (30-90d), `"red"` (<30d), `"expired"`, `"none"` (no ATO).

---

## 2. System Detail

### GET /api/dashboard/systems/{systemId}

Returns full dashboard data for a single system.

**Response** `200 OK`:

```json
{
  "systemId": "guid",
  "name": "string",
  "impactLevel": "Moderate",
  "baselineLevel": "Moderate",
  "currentRmfPhase": "Implement",
  "rmfPhaseProgress": [
    {
      "phase": "Prepare",
      "ordinal": 0,
      "status": "complete",
      "completionPercent": 100.0
    },
    {
      "phase": "Implement",
      "ordinal": 3,
      "status": "current",
      "completionPercent": 62.5
    }
  ],
  "keyMetrics": {
    "complianceScore": 78.5,
    "complianceScoreDelta": -2.3,
    "priorScore": 80.8,
    "totalOpenPoams": 12,
    "overduePoams": 3,
    "atoDaysRemaining": 185,
    "atoSeverity": "green",
    "atoExpirationDate": "2026-09-15T00:00:00Z",
    "atoStatus": "Active",
    "catIFindings": 2,
    "catIIFindings": 5,
    "catIIIFindings": 8,
    "totalFindings": 15,
    "narrativeCoverage": 62.5
  },
  "recentActivity": [
    {
      "id": "guid",
      "eventType": "AssessmentCompleted",
      "timestamp": "2026-03-10T14:30:00Z",
      "actor": "john.doe",
      "summary": "Completed assessment for AC family (15 controls)",
      "relatedEntityType": "ComplianceAssessment",
      "relatedEntityId": "guid"
    }
  ]
}
```

**Error** `404`: System not found or user lacks access.

---

### GET /api/dashboard/systems/{systemId}/heatmap

Returns control family compliance data for heatmap rendering.

**Response** `200 OK`:

```json
{
  "systemId": "guid",
  "baselineLevel": "Moderate",
  "families": [
    {
      "familyCode": "AC",
      "familyName": "Access Control",
      "totalControls": 25,
      "assessedControls": 20,
      "satisfiedControls": 16,
      "compliancePercent": 80.0,
      "severity": "green"
    }
  ]
}
```

`severity`: `"green"` (>=80%), `"yellow"` (50-79%), `"red"` (<50%), `"gray"` (not assessed).

---

### GET /api/dashboard/systems/{systemId}/heatmap/{familyCode}/controls

Returns individual controls within a specific control family for drill-down from the heatmap.

**Response** `200 OK`:

```json
{
  "systemId": "guid",
  "familyCode": "AC",
  "familyName": "Access Control",
  "controls": [
    {
      "controlId": "AC-2",
      "controlTitle": "Account Management",
      "complianceStatus": "Satisfied",
      "hasNarrative": true,
      "isManuallyCustomized": false,
      "securityCapabilityName": "Access Management Policy"
    }
  ]
}
```

`complianceStatus`: `"Satisfied"` | `"OtherThanSatisfied"` | `"NotAssessed"`.

**Error** `404`: System or family not found.

---

### GET /api/dashboard/systems/{systemId}/trends

Returns time-series compliance snapshots.

**Query Parameters**:
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| startDate | DateTime | 90 days ago | ISO 8601 |
| endDate | DateTime | now | ISO 8601 |
| granularity | string | "daily" | daily, weekly, monthly, quarterly |

**Response** `200 OK`:

```json
{
  "systemId": "guid",
  "granularity": "daily",
  "dataPoints": [
    {
      "date": "2026-03-01",
      "complianceScore": 75.0,
      "catICount": 3,
      "catIICount": 8,
      "catIIICount": 12,
      "openPoamCount": 15,
      "overduePoamCount": 2,
      "narrativeCoverage": 58.0,
      "isSignificantDecline": false
    }
  ]
}
```

`isSignificantDecline`: true when score drops >5% from prior data point.

---

## 3. Security Capabilities

### GET /api/dashboard/capabilities

**Query Parameters**:
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| search | string? | | Search name, description, provider |
| category | string? | | NIST family code (AC, AU, etc.) |
| status | string? | | Planned, InProgress, Implemented, Deprecated |
| cursor | string? | | Pagination cursor |
| pageSize | int | 50 | 1-100 |

**Response** `200 OK`:

```json
{
  "items": [
    {
      "id": "guid",
      "name": "Multi-Factor Authentication",
      "provider": "Microsoft Entra ID",
      "category": "IA",
      "categoryName": "Identification and Authentication",
      "description": "string",
      "implementationStatus": "Implemented",
      "owner": "Identity Team",
      "mappedControlCount": 12,
      "systemsUsingCount": 5,
      "createdAt": "2026-01-15T10:00:00Z",
      "modifiedAt": "2026-03-01T08:30:00Z"
    }
  ],
  "nextCursor": "string | null",
  "totalCount": 42
}
```

---

### POST /api/dashboard/capabilities

**Request Body**:

```json
{
  "name": "Multi-Factor Authentication",
  "provider": "Microsoft Entra ID",
  "category": "IA",
  "description": "Enforces phishing-resistant MFA...",
  "implementationStatus": "Implemented",
  "owner": "Identity Team"
}
```

**Validation**:
- `name`: Required, max 200, unique
- `provider`: Required, max 200
- `category`: Required, must be valid NIST family code
- `description`: Required, max 8000
- `implementationStatus`: Required, valid enum value
- `owner`: Required, max 200

**Response** `201 Created`:

```json
{
  "id": "guid",
  "name": "Multi-Factor Authentication",
  "provider": "Microsoft Entra ID",
  "category": "IA",
  "categoryName": "Identification and Authentication",
  "description": "Enforces phishing-resistant MFA...",
  "implementationStatus": "Implemented",
  "owner": "Identity Team",
  "mappedControlCount": 0,
  "systemsUsingCount": 0,
  "createdAt": "2026-03-14T10:00:00Z",
  "modifiedAt": null
}
```

**Error** `409`: Capability with same name already exists.
**Error** `400`: Validation failure.

---

### PUT /api/dashboard/capabilities/{id}

Same request body as POST. Returns `200 OK` with updated capability.

When description or provider changes, triggers narrative propagation:
- Narratives where `ControlImplementation.SecurityCapabilityId == id` AND `IsManuallyCustomized == false` are regenerated
- Customized narratives get a `DashboardActivity` event: "Upstream capability changed — review available"
- Response includes `narrativesUpdated` count

**Response** `200 OK`:

```json
{
  "id": "guid",
  "...": "...(same as GET item)",
  "narrativesUpdated": 45,
  "narrativesSkipped": 3
}
```

---

### DELETE /api/dashboard/capabilities/{id}

**Response** `200 OK`:

```json
{
  "deletedId": "guid",
  "affectedNarratives": 45,
  "message": "Capability deleted. 45 control narratives flagged for review."
}
```

Affected `ControlImplementation` records: `SecurityCapabilityId` set to null, flagged for review via `DashboardActivity`.

---

### GET /api/dashboard/capabilities/{id}/mappings

Returns all control mappings for a capability.

**Response** `200 OK`:

```json
{
  "capabilityId": "guid",
  "capabilityName": "Multi-Factor Authentication",
  "mappings": [
    {
      "id": "guid",
      "controlId": "IA-2",
      "controlTitle": "Identification and Authentication (Organizational Users)",
      "controlFamily": "IA",
      "role": "Primary",
      "registeredSystemId": null,
      "registeredSystemName": null,
      "narrativeStatus": "Populated",
      "isManuallyCustomized": false
    }
  ],
  "totalMappings": 12
}
```

`narrativeStatus`: `"Populated"` | `"Empty"` | `"Customized"`.

---

### POST /api/dashboard/capabilities/{id}/mappings

Adds control mappings, triggers narrative generation for each.

**Request Body**:

```json
{
  "mappings": [
    {
      "controlId": "AC-2",
      "role": "Primary",
      "registeredSystemId": null
    },
    {
      "controlId": "AC-7",
      "role": "Supporting",
      "registeredSystemId": "guid"
    }
  ]
}
```

**Validation**:
- `controlId`: Must exist in NistControl table
- `role`: Primary, Supporting, or Shared
- `registeredSystemId`: When not null, must exist and user must have access
- Duplicate primary warning: if a Primary mapping already exists for (controlId, registeredSystemId)

**Response** `201 Created`:

```json
{
  "created": 2,
  "warnings": [
    {
      "controlId": "AC-2",
      "message": "Another capability 'Access Management Policy' already claims Primary role for AC-2"
    }
  ],
  "narrativesGenerated": 2
}
```

---

## 4. Gap Analysis

### GET /api/dashboard/systems/{systemId}/gaps

Returns coverage analysis for the system's baseline.

**Response** `200 OK`:

```json
{
  "systemId": "guid",
  "baselineLevel": "Moderate",
  "totalBaselineControls": 325,
  "coveredControls": 150,
  "gapCount": 175,
  "coveragePercent": 46.2,
  "familyBreakdown": [
    {
      "familyCode": "AC",
      "familyName": "Access Control",
      "totalControls": 25,
      "coveredControls": 18,
      "gapCount": 7,
      "coveragePercent": 72.0,
      "isBelow50": false,
      "unmappedControls": [
        {
          "controlId": "AC-4",
          "controlTitle": "Information Flow Enforcement"
        }
      ]
    }
  ]
}
```

---

## 5. System Components

### GET /api/dashboard/systems/{systemId}/components

**Query Parameters**:
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| type | string? | | Person, Place, Thing |
| status | string? | | Active, Planned, Decommissioned |
| search | string? | | Search name, description |
| cursor | string? | | Pagination cursor |
| pageSize | int | 50 | 1-100 |

**Response** `200 OK`:

```json
{
  "systemId": "guid",
  "summary": {
    "personCount": 5,
    "placeCount": 3,
    "thingCount": 12,
    "totalCount": 20
  },
  "items": [
    {
      "id": "guid",
      "name": "Microsoft Defender for Cloud",
      "componentType": "Thing",
      "subType": "Security Tool",
      "description": "Cloud security posture management...",
      "owner": "Security Operations",
      "status": "Active",
      "linkedCapabilities": [
        {
          "capabilityId": "guid",
          "capabilityName": "Cloud Security Monitoring"
        }
      ],
      "createdAt": "2026-02-01T10:00:00Z",
      "modifiedAt": null
    }
  ],
  "nextCursor": "string | null",
  "totalCount": 20
}
```

---

### POST /api/dashboard/systems/{systemId}/components

**Request Body**:

```json
{
  "name": "Microsoft Defender for Cloud",
  "componentType": "Thing",
  "subType": "Security Tool",
  "description": "Cloud security posture management...",
  "owner": "Security Operations",
  "status": "Active",
  "linkedCapabilityIds": ["guid1", "guid2"]
}
```

**Validation**:
- `name`: Required, max 200
- `componentType`: Required, Person/Place/Thing
- `subType`: Optional, max 100
- `description`: Optional, max 2000
- `owner`: Optional, max 200
- `status`: Required, Active/Planned/Decommissioned
- `linkedCapabilityIds`: Each must exist in SecurityCapability table

**Response** `201 Created`: Returns created component (same shape as GET item).

---

### PUT /api/dashboard/components/{id}

Same request body as POST. Returns `200 OK` with updated component.

---

### DELETE /api/dashboard/components/{id}

**Response** `200 OK`:

```json
{
  "deletedId": "guid",
  "flaggedCapabilities": [
    {
      "capabilityId": "guid",
      "capabilityName": "Cloud Security Monitoring",
      "message": "Linked component removed — review capability"
    }
  ]
}
```

On deletion of an Active component linked to capabilities: creates `DashboardActivity` events flagging each linked capability for review.
