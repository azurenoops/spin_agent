# Dashboard API Contracts: System Intake Wizard

**Feature**: 042-system-intake-wizard  
**Date**: 2026-03-20

All endpoints use the base URL `/api/dashboard` and require `Authorization: Bearer <token>`.

---

## Existing Endpoints Used (No Changes)

### POST /systems
**Step 1**: Register a new system. Already exists.

**Request**:
```json
{
  "name": "ACME Portal",
  "systemType": "MajorApplication",
  "missionCriticality": "MissionEssential",
  "hostingEnvironment": "AzureGovernment",
  "acronym": "AP",
  "description": "Enterprise web application..."
}
```

**Response** `201 Created`:
```json
{
  "id": "abc-123",
  "name": "ACME Portal",
  "acronym": "AP",
  "systemType": "MajorApplication",
  "missionCriticality": "MissionEssential",
  "hostingEnvironment": "AzureGovernment",
  "currentRmfStep": "Prepare"
}
```

**Error** `409 Conflict`: `{ "message": "A system with this name already exists", "errorCode": "DUPLICATE_NAME" }`

---

### GET /capabilities
**Step 2**: Search/filter existing capabilities. Already exists.

**Query params**: `?search=MFA&category=IA&status=Operational&pageSize=50`

---

### POST /systems/{systemId}/components
**Step 3**: Create a system component. Already exists.

**Request**:
```json
{
  "name": "John Smith",
  "componentType": "Person",
  "subType": "Security Engineer",
  "description": "Lead security engineer",
  "owner": "IT Security",
  "personName": "John Smith",
  "email": "john.smith@agency.gov"
}
```

---

### POST /systems/{systemId}/boundary-definitions
**Step 4**: Create a boundary definition. Already exists.

**Request**:
```json
{
  "name": "Primary Cloud Boundary",
  "boundaryType": "Logical",
  "description": "Azure Gov subscription boundary",
  "isPrimary": true
}
```

---

### POST /systems/{systemId}/boundary-definitions/{bdId}/component-assignments
**Step 4**: Assign components to a boundary. Already exists.

---

### POST /systems/{systemId}/roles
**Step 5**: Assign an RMF role. Already exists.

**Request**:
```json
{
  "role": "Issm",
  "userDisplayName": "Jane Doe",
  "userId": "component-guid-of-jane"
}
```

---

### GET /systems/{systemId}/roles
**Step 6**: Fetch role assignments for verification. Already exists.

---

### POST /systems/{systemId}/categorization
**Step 7**: Set security categorization. Already exists.

**Request**:
```json
{
  "isNationalSecuritySystem": false,
  "justification": "System processes unclassified data",
  "informationTypes": [
    {
      "sp80060Id": "D.4.1",
      "name": "Access to Care",
      "category": "Services for Citizens",
      "confidentialityImpact": "Moderate",
      "integrityImpact": "Moderate",
      "availabilityImpact": "Moderate",
      "usesProvisional": true
    }
  ]
}
```

---

### GET /components?type=Person
**Step 5**: List org-wide Person components for role assignment picker. Already exists.

---

## New Endpoints

### POST /systems/{systemId}/capability-links
**Step 2**: Link capabilities to a system.

**Request**:
```json
{
  "capabilityIds": ["cap-guid-1", "cap-guid-2", "cap-guid-3"]
}
```

**Response** `200 OK`:
```json
{
  "linkedCount": 3,
  "items": [
    {
      "id": "link-guid-1",
      "systemId": "abc-123",
      "capabilityId": "cap-guid-1",
      "capabilityName": "Multi-Factor Authentication",
      "linkedAt": "2026-03-20T14:00:00Z"
    }
  ]
}
```

**Error** `404 Not Found`: `{ "message": "System not found", "errorCode": "SYSTEM_NOT_FOUND" }`  
**Error** `400 Bad Request`: `{ "message": "One or more capability IDs are invalid", "errorCode": "INVALID_CAPABILITY_IDS" }`

---

### GET /systems/{systemId}/capability-links
**Step 2**: Get linked capabilities for a system.

**Response** `200 OK`:
```json
{
  "items": [
    {
      "id": "link-guid-1",
      "capabilityId": "cap-guid-1",
      "capabilityName": "Multi-Factor Authentication",
      "provider": "Microsoft Entra ID",
      "category": "IA",
      "implementationStatus": "Operational",
      "linkedAt": "2026-03-20T14:00:00Z"
    }
  ],
  "totalCount": 3
}
```

---

### DELETE /systems/{systemId}/capability-links/{linkId}
**Step 2**: Remove a capability link during wizard editing.

**Response** `200 OK`:
```json
{
  "deletedId": "link-guid-1",
  "message": "Capability link removed"
}
```

---

## Modified DTOs

### PortfolioSystemSummary (Updated)
Add setup completion fields for the "Setup Incomplete" badge.

```typescript
interface PortfolioSystemSummary {
  // ... existing fields ...
  isSetupComplete: boolean;  // NEW: composite of below
  hasBoundary: boolean;      // NEW: has ≥1 boundary definition
  hasRoles: boolean;         // NEW: has ≥1 active role assignment
  hasCategorization: boolean; // NEW: has SecurityCategorization record
}
```

---

## SP 800-60 Reference Data (Static, Client-Side)

Served as a bundled JSON file. Not an API endpoint.

**Path**: `/src/data/sp800-60-information-types.json` (copied from agents resource)

```typescript
interface Sp80060InfoType {
  id: string;         // "D.1.1"
  name: string;       // "Education Administration"
  category: string;   // "Services for Citizens"
  confidentiality: string; // "Low" | "Moderate" | "High"
  integrity: string;
  availability: string;
}
```

---

## Error Response Envelope

All errors follow the existing dashboard error pattern:

```typescript
interface ErrorResponse {
  message: string;
  errorCode?: string;
  suggestion?: string;
}
```
