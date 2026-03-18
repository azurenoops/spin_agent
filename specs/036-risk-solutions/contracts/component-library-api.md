# API Contract: Org-Wide Component Library

**Feature**: 036-risk-solutions | **Base Path**: `/api/dashboard`

## New Endpoints

### 1. List All Components (Org-Wide)

```
GET /api/dashboard/components?search={search}&type={type}&status={status}&page={page}&pageSize={pageSize}
```

**Query Parameters**:
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| search | string? | null | Filter by name (contains, case-insensitive) |
| type | string? | null | Filter by ComponentType: `Person`, `Place`, `Thing` |
| status | string? | null | Filter by ComponentStatus: `Active`, `Planned`, `Decommissioned` |
| page | int | 1 | Page number (1-based) |
| pageSize | int | 50 | Items per page (max 200) |

**Response** `200 OK`:
```json
{
  "items": [
    {
      "id": "guid",
      "name": "Microsoft Entra ID",
      "componentType": "Thing",
      "subType": "Identity Provider",
      "description": "Cloud IAM platform...",
      "owner": "ISSO — John Smith",
      "status": "Active",
      "createdAt": "2026-03-17T00:00:00Z",
      "createdBy": "admin@example.com",
      "modifiedAt": null,
      "systemAssignments": [
        {
          "id": "guid",
          "registeredSystemId": "guid",
          "systemName": "Eagle Eye",
          "boundaryDefinitionId": "guid",
          "boundaryName": "Production"
        }
      ],
      "capabilityLinks": [
        {
          "securityCapabilityId": "guid",
          "capabilityName": "Multi-Factor Authentication"
        }
      ]
    }
  ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 50
}
```

---

### 2. Get Component by ID

```
GET /api/dashboard/components/{componentId}
```

**Response** `200 OK`: Same shape as a single item in the list response.

**Response** `404 Not Found`: Component does not exist.

---

### 3. Create Component (Org-Wide)

```
POST /api/dashboard/components
```

**Request Body**:
```json
{
  "name": "Microsoft Entra ID",
  "componentType": "Thing",
  "subType": "Identity Provider",
  "description": "Cloud IAM platform...",
  "owner": "ISSO — John Smith",
  "status": "Active"
}
```

**Response** `201 Created`: Full component object (same shape as GET).

**Validation**:
- `name`: required, max 200 chars
- `componentType`: required, must be `Person` | `Place` | `Thing`
- `status`: required, must be `Active` | `Planned` | `Decommissioned`

---

### 4. Update Component

```
PUT /api/dashboard/components/{componentId}
```

**Request Body**: Same as POST.

**Response** `200 OK`: Updated component object.

**Side Effects**: If `name`, `description`, or `owner` changed, triggers cascade narrative regeneration (see [narrative-generation-api.md](narrative-generation-api.md)).

---

### 5. Delete Component

```
DELETE /api/dashboard/components/{componentId}
```

**Response** `204 No Content`: Component deleted, capability links removed, system assignments removed.

**Side Effects**: Affected narratives regenerated without this component's context.

---

### 6. Assign Component to System

```
POST /api/dashboard/components/{componentId}/assignments
```

**Request Body**:
```json
{
  "registeredSystemId": "guid",
  "authorizationBoundaryDefinitionId": "guid"
}
```

**Response** `201 Created`:
```json
{
  "id": "guid",
  "systemComponentId": "guid",
  "registeredSystemId": "guid",
  "systemName": "Eagle Eye",
  "authorizationBoundaryDefinitionId": "guid",
  "boundaryName": "Production",
  "createdAt": "2026-03-17T00:00:00Z",
  "createdBy": "admin@example.com"
}
```

**Validation**:
- `registeredSystemId`: required, must reference valid system
- `authorizationBoundaryDefinitionId`: optional; if provided, must reference a boundary belonging to the specified system
- Unique constraint: (componentId, systemId, boundaryDefinitionId)

**Response** `409 Conflict`: Assignment already exists for this component+system+boundary combination.

---

### 7. Remove System Assignment

```
DELETE /api/dashboard/components/{componentId}/assignments/{assignmentId}
```

**Response** `204 No Content`: Assignment removed.

**Side Effects**: Narratives for the affected system regenerated without this component's boundary context.

---

### 8. List Components for a System (System-Scoped View)

Existing endpoint, modified behavior:

```
GET /api/dashboard/systems/{systemId}/components
```

**Changed Behavior**: Now returns components assigned to this system via `ComponentSystemAssignment` (instead of directly via `RegisteredSystemId`). Response shape unchanged for backward compatibility.

---

## Migration Endpoints

### 9. Component Impact Preview

```
GET /api/dashboard/components/{componentId}/impact-preview
```

**Response** `200 OK`:
```json
{
  "totalNarratives": 243,
  "totalSystems": 3,
  "customSkipped": 12,
  "bySystem": [
    {
      "systemId": "guid",
      "systemName": "Eagle Eye",
      "narrativeCount": 81,
      "customSkipped": 4
    }
  ]
}
```

**Purpose**: Shows how many narratives would be regenerated if this component is modified. Called by the UI before saving edits.
