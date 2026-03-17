# API Endpoint Contracts: Boundary-Scoped Model

**Feature**: 033-boundary-scoped-model  
**Date**: 2026-03-15

## New Endpoints

### Boundary Definition CRUD

#### `GET /api/systems/{systemId}/boundary-definitions`

List boundary definitions for a system.

**Response** `200 OK`:
```json
{
  "items": [
    {
      "id": "guid",
      "registeredSystemId": "guid",
      "name": "Eagle Eye — Primary",
      "boundaryType": "Logical",
      "description": "Default authorization boundary.",
      "isPrimary": true,
      "resourceCount": 12,
      "componentCount": 8,
      "coveragePercent": 74.5,
      "createdAt": "2026-03-15T00:00:00Z"
    }
  ],
  "totalCount": 2
}
```

---

#### `POST /api/systems/{systemId}/boundary-definitions`

Create a new boundary definition.

**Request**:
```json
{
  "name": "Eagle Eye Dev/Test",
  "boundaryType": "Logical",
  "description": "Development and testing environment."
}
```

**Response** `201 Created`:
```json
{
  "id": "guid",
  "registeredSystemId": "guid",
  "name": "Eagle Eye Dev/Test",
  "boundaryType": "Logical",
  "description": "Development and testing environment.",
  "isPrimary": false,
  "resourceCount": 0,
  "componentCount": 0,
  "coveragePercent": 0,
  "createdAt": "2026-03-15T12:00:00Z"
}
```

**Errors**:
- `409 Conflict`: Boundary with this name already exists for the system.
- `404 Not Found`: System not found.

---

#### `PUT /api/boundary-definitions/{id}`

Update a boundary definition's name, type, or description.

**Request**:
```json
{
  "name": "Eagle Eye Production",
  "boundaryType": "Hybrid",
  "description": "Updated description."
}
```

**Response** `200 OK`: Updated `BoundaryDefinitionDto`.

**Errors**:
- `409 Conflict`: Name already taken within the system.
- `404 Not Found`: Boundary definition not found.

---

#### `DELETE /api/boundary-definitions/{id}`

Delete a non-Primary boundary definition. Orphaned components and mappings are auto-reassigned to the Primary boundary.

**Response** `200 OK`:
```json
{
  "deletedId": "guid",
  "reassignedComponents": 3,
  "reassignedMappings": 5,
  "reassignedResources": 7,
  "primaryBoundaryId": "guid"
}
```

**Errors**:
- `400 Bad Request`: Cannot delete the Primary boundary.
- `404 Not Found`: Boundary definition not found.

---

### Azure Resource Discovery (US8)

#### `GET /api/systems/{systemId}/azure-discovery`

Discover Azure resources from the system's subscription.

**Query params**:
- `resourceGroup` (optional): Filter by resource group name.
- `resourceType` (optional): Filter by Azure resource type.
- `search` (optional): Text search on resource name.
- `cursor` (optional): Pagination skip token.

**Response** `200 OK`:
```json
{
  "suggestedBoundaries": [
    {
      "resourceGroupName": "rg-eagleeye-prod",
      "boundaryType": "Logical",
      "resourceCount": 15,
      "alreadyExists": false,
      "resources": [
        {
          "resourceId": "/subscriptions/.../resourceGroups/rg-eagleeye-prod/providers/Microsoft.Compute/virtualMachines/vm-web-01",
          "name": "vm-web-01",
          "type": "Microsoft.Compute/virtualMachines",
          "resourceGroup": "rg-eagleeye-prod",
          "location": "usgovvirginia",
          "alreadyInBoundary": false
        }
      ]
    }
  ],
  "nextCursor": "skip-token-or-null",
  "totalResourceCount": 42
}
```

**Errors**:
- `401 Unauthorized`: Azure credentials unavailable.
- `403 Forbidden`: Insufficient RBAC permissions on subscription.

#### `POST /api/systems/{systemId}/azure-discovery/apply`

Apply Azure discovery suggestions: create boundaries and/or components.

**Request**:
```json
{
  "boundaries": [
    {
      "resourceGroupName": "rg-eagleeye-prod",
      "name": "Eagle Eye Production",
      "boundaryType": "Logical",
      "description": "Auto-discovered from Azure resource group."
    }
  ],
  "components": [
    {
      "boundaryDefinitionId": "guid-or-null-for-new",
      "resourceId": "/subscriptions/.../vm-web-01",
      "name": "vm-web-01",
      "subType": "Microsoft.Compute/virtualMachines"
    }
  ]
}
```

**Response** `200 OK`:
```json
{
  "boundariesCreated": 1,
  "componentsCreated": 3,
  "skipped": 0
}
```

---

## Modified Endpoints

### Gap Analysis

#### `GET /api/systems/{systemId}/gap-analysis`

**New query param**: `boundaryDefinitionId` (optional)
- If provided: Returns gap analysis scoped to the specified boundary (boundary-specific + org-wide mappings).
- If omitted: Returns system-wide gap analysis (current behavior, union of all mappings).

**New response field** (when `boundaryDefinitionId` is omitted):
```json
{
  "...existing fields...",
  "boundaryComparison": [
    {
      "boundaryId": "guid",
      "boundaryName": "Production",
      "totalControls": 325,
      "coveredControls": 240,
      "gapCount": 85,
      "coveragePercent": 73.8,
      "resourceCount": 12,
      "componentCount": 8
    }
  ]
}
```

---

### Components

#### `GET /api/systems/{systemId}/components`

**New query param**: `boundaryDefinitionId` (optional)
- If provided: Returns components assigned to the specified boundary.
- If omitted: Returns all components (current behavior).

**New response field** on each `SystemComponentDto`:
```json
{
  "...existing fields...",
  "boundaryDefinitionId": "guid-or-null",
  "boundaryDefinitionName": "Production"
}
```

#### `POST /api/systems/{systemId}/components`

**New request field** on `CreateComponentRequest`:
```json
{
  "...existing fields...",
  "boundaryDefinitionId": "guid (optional — defaults to Primary)"
}
```

---

### Capability Mappings

#### `POST /api/capabilities/{capabilityId}/mappings`

**New request field** on each mapping:
```json
{
  "controlId": "AC-2",
  "role": "Primary",
  "registeredSystemId": "guid",
  "boundaryDefinitionId": "guid (optional — null = org-wide)"
}
```

**New response field** on `CreateMappingsResponse`:
```json
{
  "created": 3,
  "warnings": [],
  "narrativesGenerated": 3,
  "narrativesByBoundary": {
    "Production": 2,
    "Dev/Test": 1
  }
}
```

#### `GET /api/capabilities/{capabilityId}/mappings`

**New response field** on each `CapabilityMappingDto`:
```json
{
  "...existing fields...",
  "boundaryDefinitionId": "guid-or-null",
  "boundaryDefinitionName": "Production (or null for org-wide)"
}
```
