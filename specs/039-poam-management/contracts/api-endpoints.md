# API Endpoints Contract: POA&M Management (Feature 039)

**Base Route**: `/api/dashboard` (follows existing `DashboardEndpoints.cs` pattern)  
**Date**: 2026-03-18

## Endpoint Summary

| Method | Path | Description | FR |
|--------|------|-------------|-----|
| GET | `/api/dashboard/systems/{systemId}/poam` | List POA&M items (paginated, filtered) | FR-003, FR-004 |
| GET | `/api/dashboard/poam` | List POA&M items cross-system (org-level) | FR-001, FR-003 |
| GET | `/api/dashboard/systems/{systemId}/poam/metrics` | POA&M summary metrics | FR-002 |
| GET | `/api/dashboard/poam/metrics` | Cross-system POA&M metrics | FR-002 |
| GET | `/api/dashboard/poam/{poamId}` | Get POA&M item detail | FR-012 |
| POST | `/api/dashboard/systems/{systemId}/poam` | Create POA&M item | FR-001 |
| PUT | `/api/dashboard/poam/{poamId}` | Update POA&M item | FR-007, FR-008 |
| PUT | `/api/dashboard/poam/{poamId}/status` | Update POA&M status (lifecycle) | FR-007 |
| POST | `/api/dashboard/poam/bulk-status` | Bulk status update | FR-015 |
| POST | `/api/dashboard/poam/{poamId}/components` | Link components | FR-005 |
| DELETE | `/api/dashboard/poam/{poamId}/components` | Unlink components | FR-005 |
| POST | `/api/dashboard/poam/{poamId}/task` | Create remediation task from POA&M | FR-008a |
| POST | `/api/dashboard/poam/{poamId}/link-task` | Link existing task | FR-008b |
| DELETE | `/api/dashboard/poam/{poamId}/unlink-task` | Unlink task | FR-008b |
| GET | `/api/dashboard/systems/{systemId}/poam/trend` | Trend data | FR-009 |
| GET | `/api/dashboard/systems/{systemId}/poam/export` | Export POA&M data | FR-011 |
| POST | `/api/dashboard/systems/{systemId}/poam/bulk-create` | Bulk create from findings | FR-006+015 |
| GET | `/api/dashboard/systems/{systemId}/ticketing` | Get ticketing config | FR-010 |
| POST | `/api/dashboard/systems/{systemId}/ticketing` | Configure ticketing | FR-010 |
| POST | `/api/dashboard/poam/{poamId}/sync-ticket` | Sync single POA&M to ticket | FR-010 |
| POST | `/api/dashboard/systems/{systemId}/poam/bulk-sync` | Bulk sync tickets | FR-010+015 |

---

## Request/Response DTOs

### Pagination

```typescript
// Request query params
interface PoamListQuery {
  page?: number;           // Default: 1
  pageSize?: number;       // Default: 25, options: 25|50|100
  sortBy?: string;         // Default: "scheduledCompletionDate"
  sortDirection?: "asc" | "desc";  // Default: "asc"
  status?: string;         // Filter: Ongoing|Completed|Delayed|RiskAccepted
  catSeverity?: string;    // Filter: I|II|III
  overdue?: boolean;       // Filter: overdue items only
  componentId?: string;    // Filter: linked to component
  search?: string;         // Free-text search: control ID, weakness, component name
  systemId?: string;       // For cross-system view filtering
}

// Response
interface PaginatedPoamResponse {
  items: PoamListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
```

### POA&M List Item

```typescript
interface PoamListItem {
  id: string;
  controlId: string;
  weakness: string;
  catSeverity: "I" | "II" | "III";
  status: "Ongoing" | "Completed" | "Delayed" | "RiskAccepted";
  components: { id: string; name: string; type: string }[];
  poc: string;
  dueDate: string;          // ISO 8601
  daysRemaining: number;    // Negative if overdue
  milestoneProgress: { completed: number; total: number };
  deviationType: string | null;
  externalTicketRef: string | null;
  remediationTaskId: string | null;
  remediationTaskStatus: string | null;
  isOverdue: boolean;
  systemId: string;
  systemName: string;
}
```

### POA&M Detail

```typescript
interface PoamDetail extends PoamListItem {
  weaknessSource: string;
  pocEmail: string | null;
  resourcesRequired: string | null;
  costEstimate: number | null;
  scheduledCompletionDate: string;
  actualCompletionDate: string | null;
  comments: string | null;
  findingId: string | null;
  deviationId: string | null;
  createdAt: string;
  modifiedAt: string | null;
  createdBy: string | null;
  rowVersion: string;       // For optimistic concurrency
  milestones: PoamMilestoneDto[];
  history: PoamHistoryDto[];
  ticketSync: PoamTicketSyncDto | null;
}

interface PoamMilestoneDto {
  id: string;
  description: string;
  targetDate: string;
  completedDate: string | null;
  sequence: number;
  isOverdue: boolean;
}

interface PoamHistoryDto {
  id: string;
  eventType: string;
  oldValue: string | null;
  newValue: string | null;
  actingUserName: string;
  timestamp: string;
  details: string | null;
  cascadeOrigin: string | null;
}

interface PoamTicketSyncDto {
  externalTicketId: string;
  externalTicketUrl: string | null;
  syncStatus: "Synced" | "Pending" | "Conflict" | "Error";
  lastSyncAt: string;
  lastSyncError: string | null;
}
```

### Create POA&M

```typescript
// POST /api/dashboard/systems/{systemId}/poam
interface CreatePoamRequest {
  weakness: string;               // Required, max 2000
  weaknessSource: string;        // Required, max 100
  controlId: string;             // Required, max 20
  catSeverity: "I" | "II" | "III";
  poc: string;                   // Required, max 200
  pocEmail?: string;
  scheduledCompletionDate: string; // ISO 8601
  resourcesRequired?: string;
  costEstimate?: number;
  comments?: string;
  findingId?: string;
  componentIds?: string[];        // Link to components on creation
  milestones?: { description: string; targetDate: string }[];
}

// Response: 201 Created
interface CreatePoamResponse {
  id: string;
  // ... all PoamDetail fields
}
```

### Update POA&M Status (Lifecycle)

```typescript
// PUT /api/dashboard/poam/{poamId}/status
interface UpdatePoamStatusRequest {
  status: "Ongoing" | "Completed" | "Delayed" | "RiskAccepted";
  comments?: string;
  delayReason?: string;           // Required when status = "Delayed"
  revisedDate?: string;           // Required when status = "Delayed"
  deviationId?: string;          // Required when status = "RiskAccepted"
  cascadeToTask?: boolean;       // Default: false (UI handles confirmation separately)
  rowVersion: string;            // Optimistic concurrency
}

// Response: 200 OK
interface UpdatePoamStatusResponse {
  poam: PoamDetail;
  cascadeResult?: {
    taskId: string;
    taskUpdated: boolean;
    newTaskStatus: string;
  };
}
```

### Metrics

```typescript
// GET /api/dashboard/systems/{systemId}/poam/metrics
interface PoamMetrics {
  totalOpen: number;
  overdue: number;
  catICount: number;
  catIICount: number;
  catIIICount: number;
  expiringWithin30Days: number;
  avgDaysToClose: number;
  byStatus: { status: string; count: number }[];
}
```

### Trend Data

```typescript
// GET /api/dashboard/systems/{systemId}/poam/trend?period=monthly&start=2025-09-01&end=2026-03-01
interface PoamTrendResponse {
  openOverTime: { date: string; count: number }[];
  closureRate: { period: string; closed: number; opened: number }[];
  agingBreakdown: { bucket: string; catI: number; catII: number; catIII: number }[];
  timeToClose: { bucket: string; count: number }[];  // Histogram buckets
}
```

### Component Linkage

```typescript
// POST /api/dashboard/poam/{poamId}/components
interface LinkComponentsRequest {
  componentIds: string[];   // Required, at least 1
}

// DELETE /api/dashboard/poam/{poamId}/components
interface UnlinkComponentsRequest {
  componentIds: string[];   // Required, at least 1
}
```

### Remediation Task Operations

```typescript
// POST /api/dashboard/poam/{poamId}/task
interface CreateTaskFromPoamRequest {
  boardId: string;          // Required — target remediation board
  columnName?: string;      // Default: first column
}

// POST /api/dashboard/poam/{poamId}/link-task
interface LinkTaskRequest {
  taskId: string;           // Required — existing remediation task ID
}
```

### Bulk Operations

```typescript
// POST /api/dashboard/poam/bulk-status
interface BulkPoamStatusRequest {
  poamIds: string[];        // Required, 1-100
  status: string;           // Target status
  comments?: string;
  delayReason?: string;     // When status = "Delayed"
  revisedDate?: string;     // When status = "Delayed"
}

// Response
interface BulkPoamStatusResponse {
  succeeded: number;
  failed: number;
  results: { poamId: string; success: boolean; error?: string }[];
}

// POST /api/dashboard/systems/{systemId}/poam/bulk-create
interface BulkCreateFromFindingsRequest {
  findingIds: string[];     // Required
  componentIds?: string[];  // Optional — link all created POA&Ms to these components
  linkRemediationTasks?: boolean;  // Default: false
}

// Response
interface BulkCreateResponse {
  created: number;
  skippedDuplicates: number;
  results: { findingId: string; poamId?: string; status: "created" | "duplicate" | "error" }[];
}
```

### Export

```typescript
// GET /api/dashboard/systems/{systemId}/poam/export?format=emass_excel&status=Ongoing&catSeverity=I
// Query params: format (required), status, catSeverity, includeAll (bool)
// Response: File download (application/vnd.openxmlformats-officedocument.spreadsheetml.sheet | application/json | text/csv)
```

### Ticketing Integration

```typescript
// POST /api/dashboard/systems/{systemId}/ticketing
interface ConfigureTicketingRequest {
  provider: "jira" | "servicenow";
  baseUrl: string;
  projectKeyOrTableName: string;
  issueType?: string;
  authToken: string;           // Written to Key Vault, NOT stored in DB
  fieldMapping: Record<string, string>;
  syncEnabled?: boolean;       // Default: true
}

// POST /api/dashboard/poam/{poamId}/sync-ticket
interface SyncTicketRequest {
  direction?: "push" | "pull" | "bidirectional";  // Default: bidirectional
}
```

---

## Error Responses

All error responses follow the existing `ErrorResponse` schema:

```typescript
interface ErrorResponse {
  error: string;       // Human-readable message
  errorCode: string;   // Machine-readable code
  details?: string;    // Additional context
  suggestion?: string; // Corrective guidance
}
```

| Error Code | HTTP Status | Scenario |
|------------|-------------|----------|
| `POAM_NOT_FOUND` | 404 | POA&M item not found |
| `POAM_CONCURRENCY_CONFLICT` | 409 | `rowVersion` mismatch (another user modified) |
| `POAM_INVALID_TRANSITION` | 422 | Invalid lifecycle transition (e.g., Completed without finding validation) |
| `POAM_DELAY_REASON_REQUIRED` | 422 | Delayed status without reason/revised date |
| `POAM_DEVIATION_REQUIRED` | 422 | Risk Accepted without linked deviation |
| `POAM_DUPLICATE_COMPONENT` | 409 | Component already linked |
| `POAM_TASK_ALREADY_LINKED` | 409 | Task already linked to different POA&M |
| `TICKETING_CONNECTION_FAILED` | 502 | Cannot reach external ticketing system |
| `TICKETING_AUTH_FAILED` | 401 | Invalid ticketing credentials |
| `EXPORT_FORMAT_INVALID` | 400 | Unsupported export format |
