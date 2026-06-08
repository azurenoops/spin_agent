# Frontend Types — Spec 067: Azure Resource Discovery

**Epic:** #215 | **Owner:** Oracle

---

## Existing Types (Verified in Codebase)

### `onboardingApi.ts` — Subscription Types

```typescript
// Already defined — do not redeclare
export type AzureEnvironment = 'AzureCloud' | 'AzureUSGovernment';
export type SubscriptionStatus = 'Selected' | 'Unavailable';

export interface AzureSubscriptionRegistrationDto {
  id: string;
  subscriptionId: string;
  displayName: string;
  environment: AzureEnvironment;
  status: SubscriptionStatus;
  lastSeenVisibleAt?: string | null;
}

export interface AzureSubscriptionInfoDto {
  subscriptionId: string;
  displayName: string;
  tenantId: string;
  environment: AzureEnvironment;
}
```

### `types/dashboard.ts` — Discovery Types

```typescript
// Already defined — do not redeclare
export interface DiscoveredResource {
  resourceId: string;
  name: string;
  resourceType: string;
  subscriptionId: string;
  resourceGroup?: string;
  location?: string;
  alreadyImported: boolean;
}

export interface DiscoveryResponse {
  resources: DiscoveredResource[];
  cursor?: string | null;
  totalCount?: number;
}

export interface ImportAzureResponse {
  imported: number;
  skipped: number;
  componentIds: string[];
}
```

---

## New Types to Add

### `api/components.ts` — Entra Discovery Types

```typescript
// ADD: Entra identity discovery types
export interface DiscoveredEntraIdentity {
  entraObjectId: string;
  displayName: string;
  email: string;
  kind: 'User' | 'Group';
  department?: string | null;
  jobTitle?: string | null;
  alreadyImported: boolean;
}

export interface EntraDiscoveryResponse {
  items: DiscoveredEntraIdentity[];
  partialFailure: boolean;
  failureMessage?: string | null;
}

export interface ImportEntraRequest {
  people: { entraObjectId: string }[];
}

export interface ImportEntraResponse {
  imported: number;
  skipped: number;
}
```

### `api/components.ts` — System-Level Azure Discovery Types

```typescript
// ADD: System-level Azure discovery
export interface AzureDiscoverySummary {
  resources: DiscoveredResource[];
  cursor?: string | null;
  totalCount?: number;
}

export interface AzureDiscoveryApplyResponse {
  applied: number;
  skipped: number;
  boundaryDefinitionsCreated: number;
}
```

---

## API Client Functions to Add

### `api/components.ts`

```typescript
// ADD: Entra discovery
export async function discoverEntraUsers(): Promise<EntraDiscoveryResponse> {
  const { data } = await apiClient.post<EntraDiscoveryResponse>(
    '/components/discover-entra',
  );
  return data;
}

export async function importEntraComponents(
  request: ImportEntraRequest,
): Promise<ImportEntraResponse> {
  const { data } = await apiClient.post<ImportEntraResponse>(
    '/components/import-entra',
    request,
  );
  return data;
}

// ADD: System-level Azure discovery
export async function getSystemAzureDiscovery(
  systemId: string,
  params?: { resourceGroup?: string; resourceType?: string; search?: string; cursor?: string },
): Promise<AzureDiscoverySummary> {
  const { data } = await apiClient.get<AzureDiscoverySummary>(
    `/systems/${systemId}/azure-discovery`,
    { params },
  );
  return data;
}

export async function applySystemAzureDiscovery(
  systemId: string,
): Promise<AzureDiscoveryApplyResponse> {
  const { data } = await apiClient.post<AzureDiscoveryApplyResponse>(
    `/systems/${systemId}/azure-discovery/apply`,
  );
  return data;
}
```

---

## Component Prop Contracts

### `AzureSettingsPage`

```typescript
// No external props — page manages its own state
// Internal state:
interface AzureSettingsPageState {
  registrations: AzureSubscriptionRegistrationDto[];
  loading: boolean;
  error: string | null;
  success: string | null;
  registerInput: string;       // comma/newline-separated subscription IDs
  registering: boolean;
  registerError: string | null;
  pendingDelete: AzureSubscriptionRegistrationDto | null;
  deleting: boolean;
}
```

### `ComponentInventory` — Azure Discovery State Extension

```typescript
// ADD to existing ComponentInventory state:
interface AzureDiscoveryStateExtension {
  registeredSubs: AzureSubscriptionRegistrationDto[];
  selectedSubscriptionId: string;  // replaces discoverSubscription free-text
  // Entra discovery sub-section
  entraItems: DiscoveredEntraIdentity[];
  entraLoading: boolean;
  entraError: string | null;
  entraFeatureDisabled: boolean;
  selectedEntraIds: Set<string>;
  // System-level discovery
  systemDiscoverySummary: AzureDiscoverySummary | null;
  systemDiscoveryLoading: boolean;
  applyingDiscovery: boolean;
}
```
