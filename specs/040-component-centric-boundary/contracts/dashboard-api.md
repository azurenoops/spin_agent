# Dashboard API Contracts: Component-Centric Boundary Model

**Feature**: 040-component-centric-boundary  
**Date**: 2026-03-19  
**Base Path**: `/api/dashboard`

---

## 1. Boundary Component Assignment Endpoints

### 1.1 List Components for a Boundary

```
GET /systems/{systemId}/boundary-definitions/{boundaryId}/components
```

**Query Parameters**:
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `search` | string | — | Text search on component name |
| `type` | string | — | Filter by ComponentType: Person, Place, Thing |
| `scope` | string | — | Filter by scope: InScope, Excluded |
| `page` | int | 1 | 1-based page number |
| `pageSize` | int | 50 | Results per page (max: 200) |

**Response** `200 OK`:
```json
{
  "items": [
    {
      "assignmentId": "guid",
      "componentId": "guid",
      "componentName": "SQL Database - prod",
      "componentType": "Thing",
      "subType": "Azure SQL Database",
      "isInScope": true,
      "exclusionRationale": null,
      "inheritanceProvider": null,
      "azureResourceId": "/subscriptions/.../Microsoft.Sql/servers/prod-sql",
      "azureResourceType": "Microsoft.Sql/servers",
      "azureResourceGroup": "rg-prod",
      "azureLocation": "usgovvirginia",
      "createdAt": "2026-03-19T12:00:00Z",
      "createdBy": "user@gov.mil"
    }
  ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 50
}
```

### 1.2 Assign Component to Boundary

```
POST /systems/{systemId}/boundary-definitions/{boundaryId}/components
```

**Request Body**:
```json
{
  "componentId": "guid",
  "isInScope": true,
  "exclusionRationale": null,
  "inheritanceProvider": "Azure CSP"
}
```

**Validation**:
- `componentId` must reference an existing SystemComponent.
- If `isInScope` is `false`, `exclusionRationale` must be non-empty.
- Duplicate `componentId` + `boundaryId` returns `409 Conflict`.

**Response** `201 Created`:
```json
{
  "assignmentId": "guid",
  "componentId": "guid",
  "componentName": "SQL Database - prod",
  "isInScope": true,
  "exclusionRationale": null,
  "inheritanceProvider": "Azure CSP",
  "createdAt": "2026-03-19T12:00:00Z"
}
```

### 1.3 Update Boundary Component Assignment

```
PUT /systems/{systemId}/boundary-definitions/{boundaryId}/components/{assignmentId}
```

**Request Body**:
```json
{
  "isInScope": false,
  "exclusionRationale": "Managed by external CSP per FedRAMP inheritance",
  "inheritanceProvider": "Azure Gov CSP"
}
```

**Validation**:
- If `isInScope` is `false`, `exclusionRationale` must be non-empty.

**Response** `200 OK`: Updated assignment DTO (same shape as 1.2 response).

### 1.4 Remove Component from Boundary

```
DELETE /systems/{systemId}/boundary-definitions/{boundaryId}/components/{assignmentId}
```

**Response** `200 OK`:
```json
{
  "deleted": true,
  "componentRetained": true,
  "message": "Assignment removed. Component remains in the library."
}
```

---

## 2. Boundary Lock Endpoints

### 2.1 Acquire Lock

```
POST /systems/{systemId}/boundary-definitions/{boundaryId}/lock
```

**Request Body**:
```json
{
  "userId": "user@gov.mil",
  "userDisplayName": "Jane Smith"
}
```

**Response** `200 OK` (lock acquired):
```json
{
  "locked": true,
  "lockedBy": "Jane Smith",
  "lockedAt": "2026-03-19T12:00:00Z",
  "expiresAt": "2026-03-19T12:05:00Z"
}
```

**Response** `409 Conflict` (already locked):
```json
{
  "locked": true,
  "lockedBy": "John Doe",
  "lockedAt": "2026-03-19T11:58:00Z",
  "expiresAt": "2026-03-19T12:03:00Z",
  "message": "This boundary is currently being updated by John Doe."
}
```

### 2.2 Release Lock

```
DELETE /systems/{systemId}/boundary-definitions/{boundaryId}/lock
```

**Response** `200 OK`:
```json
{ "released": true }
```

### 2.3 Check Lock Status

```
GET /systems/{systemId}/boundary-definitions/{boundaryId}/lock
```

**Response** `200 OK`:
```json
{
  "locked": false,
  "lockedBy": null,
  "lockedAt": null,
  "expiresAt": null
}
```

---

## 3. Azure Discovery Endpoints (Component Library)

### 3.1 Discover Azure Resources for Component Library

```
POST /components/discover-azure
```

**Request Body**:
```json
{
  "subscriptionId": "sub-guid",
  "resourceGroupFilter": null,
  "resourceTypeFilter": null,
  "searchFilter": null,
  "cursor": null
}
```

**Response** `200 OK`:
```json
{
  "resources": [
    {
      "resourceId": "/subscriptions/.../resourceGroups/rg-prod/providers/Microsoft.Compute/virtualMachines/vm-web-01",
      "name": "vm-web-01",
      "type": "Microsoft.Compute/virtualMachines",
      "resourceGroup": "rg-prod",
      "location": "usgovvirginia",
      "alreadyImported": false
    }
  ],
  "nextCursor": "skip-token-or-null",
  "totalCount": 87,
  "failedResourceGroups": ["rg-restricted"]
}
```

### 3.2 Import Discovered Resources as Components (Org-Wide)

```
POST /components/import-azure
```

**Request Body**:
```json
{
  "resources": [
    {
      "resourceId": "/subscriptions/.../Microsoft.Compute/virtualMachines/vm-web-01",
      "name": "vm-web-01",
      "type": "Microsoft.Compute/virtualMachines",
      "resourceGroup": "rg-prod",
      "location": "usgovvirginia"
    }
  ]
}
```

**Response** `200 OK`:
```json
{
  "imported": 3,
  "skipped": 1,
  "skippedDetails": [
    { "resourceId": "...", "reason": "Already exists as component" }
  ],
  "components": [
    {
      "id": "guid",
      "name": "vm-web-01",
      "componentType": "Thing",
      "azureResourceId": "/subscriptions/..."
    }
  ]
}
```

---

## 4. Azure Discovery Endpoints (System-Level)

### 4.1 Discover Azure Resources for System

```
POST /systems/{systemId}/components/discover-azure
```

Same request/response shape as 3.1, but scoped to the system's configured Azure subscription. Response includes `existsInOrgLibrary` flag per resource.

**Response** `200 OK`:
```json
{
  "resources": [
    {
      "resourceId": "/subscriptions/...",
      "name": "vm-web-01",
      "type": "Microsoft.Compute/virtualMachines",
      "resourceGroup": "rg-prod",
      "location": "usgovvirginia",
      "alreadyImported": false,
      "existsInOrgLibrary": true,
      "orgLibraryComponentId": "component-guid-1"
    }
  ],
  "nextCursor": null,
  "totalCount": 12,
  "failedResourceGroups": []
}
```

### 4.2 Import Discovered Resources as System Components

```
POST /systems/{systemId}/components/import-azure
```

**Request Body**:
```json
{
  "resources": [
    {
      "resourceId": "...",
      "name": "vm-web-01",
      "type": "Microsoft.Compute/virtualMachines",
      "resourceGroup": "rg-prod",
      "location": "usgovvirginia"
    }
  ],
  "assignExistingOrgComponents": ["component-guid-1"]
}
```

The `assignExistingOrgComponents` array handles the case where a resource already exists in the org library — instead of creating a duplicate, the existing org component is assigned to this system via `ComponentSystemAssignment`.

**Response** `200 OK`:
```json
{
  "imported": 2,
  "assignedFromOrg": 1,
  "skipped": 0,
  "components": [...]
}
```

---

## 5. Component Risk Summary Endpoints

### 5.1 Per-Component Risk Summary for Assessment

```
GET /systems/{systemId}/assessments/{assessmentId}/component-risks
```

**Response** `200 OK`:
```json
{
  "componentRisks": [
    {
      "componentId": "guid",
      "componentName": "SQL Database - prod",
      "componentType": "Thing",
      "openFindingCount": 5,
      "highestSeverity": "High",
      "overdueRemediationCount": 2
    }
  ],
  "unlinkedFindingCount": 3,
  "totalFindingCount": 48
}
```

### 5.2 Findings by Component

```
GET /systems/{systemId}/assessments/{assessmentId}/findings?componentId={componentId}
```

Adds optional `componentId` query parameter to existing findings endpoint. When `componentId=unlinked`, returns findings with null ComponentId.

---

## 6. DTO Types (TypeScript)

```typescript
// ─── Boundary Component Assignment ──────────────────────────────

export interface BoundaryComponentDto {
  assignmentId: string;
  componentId: string;
  componentName: string;
  componentType: ComponentType;
  subType: string | null;
  isInScope: boolean;
  exclusionRationale: string | null;
  inheritanceProvider: string | null;
  azureResourceId: string | null;
  azureResourceType: string | null;
  azureResourceGroup: string | null;
  azureLocation: string | null;
  createdAt: string;
  createdBy: string;
}

export interface AssignComponentRequest {
  componentId: string;
  isInScope: boolean;
  exclusionRationale?: string | null;
  inheritanceProvider?: string | null;
}

export interface UpdateAssignmentRequest {
  isInScope: boolean;
  exclusionRationale?: string | null;
  inheritanceProvider?: string | null;
}

// ─── Lock ────────────────────────────────────────────────────────

export interface BoundaryLockStatus {
  locked: boolean;
  lockedBy: string | null;
  lockedAt: string | null;
  expiresAt: string | null;
  message?: string;
}

// ─── Azure Discovery ────────────────────────────────────────────

export interface DiscoverAzureRequest {
  subscriptionId: string;
  resourceGroupFilter?: string | null;
  resourceTypeFilter?: string | null;
  searchFilter?: string | null;
  cursor?: string | null;
}

export interface DiscoveredResource {
  resourceId: string;
  name: string;
  type: string;
  resourceGroup: string;
  location: string;
  alreadyImported: boolean;
  existsInOrgLibrary?: boolean;
  orgLibraryComponentId?: string | null;
}

export interface DiscoveryResponse {
  resources: DiscoveredResource[];
  nextCursor: string | null;
  totalCount: number;
  failedResourceGroups: string[];
}

export interface ImportAzureRequest {
  resources: Omit<DiscoveredResource, 'alreadyImported' | 'existsInOrgLibrary'>[];
  assignExistingOrgComponents?: string[];
}

export interface ImportAzureResponse {
  imported: number;
  assignedFromOrg?: number;
  skipped: number;
  skippedDetails?: { resourceId: string; reason: string }[];
  components: SystemComponentDto[];
}

// ─── Component Risk Summary ─────────────────────────────────────

export interface ComponentRiskSummary {
  componentId: string;
  componentName: string;
  componentType: ComponentType;
  openFindingCount: number;
  highestSeverity: string;
  overdueRemediationCount: number;
}

export interface AssessmentComponentRisks {
  componentRisks: ComponentRiskSummary[];
  unlinkedFindingCount: number;
  totalFindingCount: number;
}
```
