# Frontend TypeScript Types — 059 Orphaned Endpoints

All types below live in
`src/Ato.Copilot.Dashboard/src/features/<domain>/types.ts`.

---

## Shared

```typescript
/** Standard paginated response envelope used across all list endpoints. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

/** Standard API response envelope. */
export interface ApiEnvelope<T> {
  status: 'success' | 'error';
  data: T;
  metadata: {
    executionTimeMs: number;
    timestamp: string; // ISO 8601
  };
}
```

---

## Audit (`features/audit/types.ts`)

```typescript
export interface AuditLogEntry {
  id: string;           // Guid
  tenantId: string;     // Guid
  actorTenantId: string;
  userId: string;       // AAD OID
  action: string;       // e.g. "Capability.Create"
  entityType: string;
  entityId: string;
  timestamp: string;    // ISO 8601
  metadataJson: string | null;
}

export interface AuditQueryParams {
  tenantId?: string;
  actorTenantId?: string;
  actorOid?: string;
  action?: string;
  from?: string;        // ISO 8601
  to?: string;          // ISO 8601
  page?: number;
  pageSize?: number;
}
```

---

## Admin Migration (`features/admin/types.ts`)

```typescript
export interface MigrationTablePreview {
  tableName: string;
  rowCount: number;
  requiresOverride: boolean;
}

export interface MigrationPreviewResult {
  tables: MigrationTablePreview[];
}

export interface TenantOverrideDto {
  tableName: string;
  rowIdPrefix?: string;
  tenantId: string; // Guid
}

export interface ExecuteMigrationRequest {
  defaultTenantId: string; // Guid
  installRls: boolean;
  overrides?: TenantOverrideDto[];
}
```

---

## Notifications (`features/notifications/types.ts`)

```typescript
export interface Notification {
  id: string;
  type: string;
  title: string;
  body: string;
  isRead: boolean;
  createdAt: string; // ISO 8601
}

export interface NotificationSummary {
  unreadCount: number;
}

/** Preferences is a free-form key→boolean map to stay resilient
 *  to new server-side preference keys without a frontend deploy. */
export type NotificationPreferences = Record<string, boolean>;

export interface NotificationPreferencesResponse {
  preferences: NotificationPreferences;
}
```

---

## Deployment (`features/tenancy/types.ts` — already partially exists)

```typescript
// Already defined in features/tenancy/api.ts — reproduced here for reference.
export type DeploymentMode = 'SingleTenant' | 'MultiTenant';

export interface DeploymentModeResponse {
  mode: DeploymentMode;
  defaultTenantId: string | null;
}
```
