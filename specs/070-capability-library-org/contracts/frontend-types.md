# Frontend TypeScript Types — 070: Capability Library (Org Scope)

**File location:** `src/Ato.Copilot.Dashboard/src/features/capability-library/api.ts`

All types in this file are new. Do not modify `src/features/csp-inherited-components/api.ts`.
The `Envelope<T>` type is re-imported from the CSP module or redeclared locally.

---

## 1. Wire Types (DTOs mirroring HTTP API)

```typescript
// ---------------------------------------------------------------------------
// Re-use the shared Envelope type from csp-inherited-components/api.ts
// or import it from a shared types location if one exists.
// ---------------------------------------------------------------------------
import type { Envelope } from '../csp-inherited-components/api';

// ---------------------------------------------------------------------------
// CapabilityLibraryItem — org-safe projection of a CspInheritedCapability.
// Maps to CapabilityLibraryItemDto on the server.
// ---------------------------------------------------------------------------

/**
 * Component type values from CspInheritedComponent.ComponentType.
 * Mirrors server-side CspComponentType enum.
 */
export type CspComponentType =
  | 'Infrastructure'
  | 'Platform'
  | 'Service'
  | 'Identity'
  | 'Network'
  | 'Storage'
  | 'Compute';

/**
 * Org-safe projection of a Published CSP capability.
 * Returned by GET /api/capability-library items array.
 *
 * INTENTIONALLY OMITS admin fields: mappedBy, createdBy, reviewedBy,
 * reviewerNote, mappingFailureReason, rowVersion.
 */
export interface CapabilityLibraryItem {
  /** CspInheritedCapability.Id (UUID) */
  id: string;
  /** Human-readable capability name (max 256 chars) */
  capabilityName: string;
  /** CspInheritedComponent.Id (UUID) */
  componentId: string;
  /** CSP provider / component name (e.g., "Azure Active Directory") */
  componentName: string;
  /** Component category */
  componentType: CspComponentType;
  /** Optional free-text description of what the capability covers */
  description?: string | null;
  /** NIST 800-53 Rev 5 control IDs covered by this capability */
  mappedNistControlIds: string[];
  /**
   * AI confidence score in [0.0, 1.0]. Null when mapping was authored by a
   * human (MappedBy = User) or when confidence was not computed.
   */
  mappingConfidence?: number | null;
}

// ---------------------------------------------------------------------------
// CapabilityLibraryPage — paginated response from GET /api/capability-library
// ---------------------------------------------------------------------------

export interface CapabilityLibraryPage {
  items: CapabilityLibraryItem[];
  /** 1-based current page index */
  page: number;
  /** Items per page (default 20, max 100) */
  pageSize: number;
  /** Total matching items across all pages */
  total: number;
}

// ---------------------------------------------------------------------------
// CapabilitySubscriptionDto — a subscription row returned by POST/GET endpoints
// Maps to CapabilitySubscriptionDto on the server.
// ---------------------------------------------------------------------------

export type CapabilitySubscriptionStatus = 'Active' | 'Cancelled';

export interface CapabilitySubscriptionDto {
  /** CapabilitySubscription.Id (server-generated UUID) */
  id: string;
  tenantId: string;
  systemId: string;
  capabilityId: string;
  /** Denormalized from CspInheritedCapability.Name at time of subscription */
  capabilityName: string;
  /** Denormalized NIST control IDs at time of subscription */
  mappedNistControlIds: string[];
  /** ISO-8601 UTC timestamp */
  subscribedAt: string;
  subscribedBy: string;
  status: CapabilitySubscriptionStatus;
  /** ISO-8601 UTC timestamp; null when status = Active */
  cancelledAt?: string | null;
  /** Actor OID who cancelled; null when status = Active */
  cancelledBy?: string | null;
}

// ---------------------------------------------------------------------------
// Request types
// ---------------------------------------------------------------------------

/**
 * Body for POST /api/systems/{systemId}/capability-subscriptions.
 * Only capabilityId is required — server stamps all other fields.
 */
export interface SubscribeCapabilityRequest {
  capabilityId: string;
}

// ---------------------------------------------------------------------------
// Query parameter types
// ---------------------------------------------------------------------------

export interface ListCapabilityLibraryParams {
  page?: number;
  pageSize?: number;
  /** Free-text search across capability name and control IDs */
  search?: string;
  /** Filter by component name (partial match) */
  provider?: string;
  /** 2-char NIST family prefix, e.g., "AC", "AU", "IA" */
  controlFamily?: string;
}
```

---

## 2. API Client Function Signatures

```typescript
// ---------------------------------------------------------------------------
// Axios client — rooted at /api, with MSAL silent renewal
// ---------------------------------------------------------------------------

import axios from 'axios';
import { attachAuthInterceptor } from '../auth/interceptors';
import { getMsalInstance, DEFAULT_API_SCOPES } from '../auth/msalInstance';

const libraryClient = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,
});
attachAuthInterceptor(libraryClient, getMsalInstance, DEFAULT_API_SCOPES);

// ---------------------------------------------------------------------------
// Function signatures (implementations in api.ts)
// ---------------------------------------------------------------------------

/**
 * GET /api/capability-library
 * Lists Published+Mapped capabilities with optional filters.
 */
export declare function listCapabilityLibrary(
  params?: ListCapabilityLibraryParams,
): Promise<CapabilityLibraryPage>;

/**
 * GET /api/capability-library/:id
 * Returns a single capability by ID.
 * Throws if not found (404) or not Published+Mapped.
 */
export declare function getCapabilityLibraryItem(
  capabilityId: string,
): Promise<CapabilityLibraryItem>;

/**
 * POST /api/systems/{systemId}/capability-subscriptions
 * Subscribes a capability to a system.
 *
 * - Returns the new subscription on 201 Created.
 * - Returns the existing subscription on 200 OK (idempotent).
 * - Throws on 403 (not ISSO/ISSM), 404 (system/capability not found), 422 (not Mapped/Published).
 */
export declare function subscribeCapability(
  systemId: string,
  request: SubscribeCapabilityRequest,
): Promise<CapabilitySubscriptionDto>;

/**
 * GET /api/systems/{systemId}/capability-subscriptions
 * Lists Active subscriptions for a system.
 * Returns empty array when none exist (never throws 404 for empty list).
 */
export declare function listSystemSubscriptions(
  systemId: string,
): Promise<CapabilitySubscriptionDto[]>;

/**
 * DELETE /api/systems/{systemId}/capability-subscriptions/{capabilityId}
 * Soft-deletes (cancels) a subscription.
 * Returns void on 204.
 * Throws on 403, 404.
 */
export declare function unsubscribeCapability(
  systemId: string,
  capabilityId: string,
): Promise<void>;
```

---

## 3. Component Prop Types

### `CapabilityLibraryPage.tsx` internal types

```typescript
/** State managed inside CapabilityLibraryPage */
interface CapabilityLibraryPageState {
  items: CapabilityLibraryItem[];
  page: number;
  pageSize: number;
  total: number;
  loading: boolean;
  error: string | null;
  // Filter state
  searchText: string;
  providerFilter: string;
  controlFamilyFilter: string;
}
```

### `CapabilityLibraryCard` sub-component props

```typescript
interface CapabilityLibraryCardProps {
  item: CapabilityLibraryItem;
  /** Whether the calling user has ISSO or ISSM role */
  canSubscribe: boolean;
  /** System currently in context (undefined = no system selected) */
  systemId?: string;
  /** Fired after a successful subscribe; parent should refetch or update list */
  onSubscribeSuccess?: (subscription: CapabilitySubscriptionDto) => void;
  onDetailClick?: (capabilityId: string) => void;
}
```

### `CapabilityDetailPage.tsx` internal types

```typescript
interface CapabilityDetailPageState {
  item: CapabilityLibraryItem | null;
  subscription: CapabilitySubscriptionDto | null;
  loading: boolean;
  error: string | null;
  showUnsubscribeDialog: boolean;
  actionLoading: boolean;
}
```

---

## 4. NIST Family Constants (Frontend)

```typescript
/**
 * NIST 800-53 Rev 5 control family codes.
 * Used for the controlFamily filter dropdown.
 * Mirrors the NIST_FAMILIES map in existing CapabilityLibrary.tsx.
 */
export const NIST_FAMILIES: Record<string, string> = {
  AC: 'Access Control',
  AT: 'Awareness and Training',
  AU: 'Audit and Accountability',
  CA: 'Assessment, Authorization, and Monitoring',
  CM: 'Configuration Management',
  CP: 'Contingency Planning',
  IA: 'Identification and Authentication',
  IR: 'Incident Response',
  MA: 'Maintenance',
  MP: 'Media Protection',
  PE: 'Physical and Environmental Protection',
  PL: 'Planning',
  PM: 'Program Management',
  PS: 'Personnel Security',
  PT: 'PII Processing and Transparency',
  RA: 'Risk Assessment',
  SA: 'System and Services Acquisition',
  SC: 'System and Communications Protection',
  SI: 'System and Information Integrity',
  SR: 'Supply Chain Risk Management',
};
```
