/**
 * Feature 048 / US9 / T215: Axios wrappers for the CSP-inherited
 * components surface (`/api/csp/inherited-components/*`).
 *
 * Wire types mirror specs/048-tenant-isolation/contracts/csp-inherited-components.openapi.yaml.
 *
 * Read-only endpoints (`GET *`) are reachable by every authenticated user
 * across every tenant — non-CSP-Admins simply see only `Status = Published`
 * components. Write endpoints (`POST/PATCH/DELETE`) require role
 * `CSP.Admin` and surface a 403 envelope otherwise. In `SingleTenant`
 * deployments the entire surface returns 404 (`SINGLE_TENANT_MODE`).
 */

import axios, { type AxiosError } from 'axios';
import type { AtoUploadResponse } from '../csp-onboarding/api';

// ---------------------------------------------------------------------------
// Wire types
// ---------------------------------------------------------------------------

export interface Envelope<T> {
  status: 'success' | 'error';
  data?: T;
  metadata: { executionTimeMs: number; timestamp: string; tool?: string | null };
  error?: { errorCode: string; message: string; suggestion?: string };
}

export type CspInheritedComponentStatus = 'Draft' | 'Published' | 'Archived';
export type CspInheritedCapabilityStatus = 'Mapped' | 'NeedsReview' | 'Archived';
export type CspSourceFormat =
  | 'Pdf'
  | 'Docx'
  | 'OscalJson'
  | 'Xlsx'
  | 'EmassZip'
  | 'Manual';
export type CspComponentType =
  | 'Infrastructure'
  | 'Platform'
  | 'Service'
  | 'Identity'
  | 'Network'
  | 'Storage'
  | 'Compute';

export interface CspInheritedComponent {
  id: string;
  cspProfileId: string;
  name: string;
  description?: string;
  componentType: CspComponentType;
  sourceFileName?: string | null;
  sourceFormat: CspSourceFormat;
  sourceArtifactReference?: string | null;
  status: CspInheritedComponentStatus;
  importedAt: string;
  importedBy?: string;
  updatedAt?: string;
  updatedBy?: string;
  capabilityMappedCount?: number;
  capabilityNeedsReviewCount?: number;
  /** Optimistic concurrency token returned by the server. Required on PATCH. */
  rowVersion?: string;
}

export interface CspInheritedCapability {
  id: string;
  componentId: string;
  name: string;
  description?: string;
  mappedNistControlIds: string[];
  mappingConfidence?: number | null;
  status: CspInheritedCapabilityStatus;
  mappingFailureReason?: string | null;
  mappedBy: 'User' | 'AI';
  reviewedBy?: string | null;
  reviewedAt?: string | null;
  reviewerNote?: string | null;
  createdAt?: string;
  createdBy?: string;
  updatedAt?: string;
  /** Optimistic concurrency token returned by the server. Required on PATCH. */
  rowVersion?: string | null;
}

export interface CspInheritedComponentsPage {
  items: CspInheritedComponent[];
  page: number;
  pageSize: number;
  /**
   * Wire format is `total` (not `totalItems` / `totalPages`). Server
   * does not return `totalPages` — callers compute it from
   * `Math.ceil(total / pageSize)` if needed.
   */
  total: number;
}

export interface CspInheritedComponentPatchRequest {
  name?: string;
  description?: string;
  componentType?: CspComponentType;
}

export interface CapabilityReviewRequest {
  mappedNistControlIds: string[];
  reviewerNote?: string;
}

export interface CspInheritedCapabilityPatchRequest {
  name: string;
  description: string;
  mappedNistControlIds: string[];
}

export interface RemapResponse {
  componentId: string;
  capabilitiesMapped: number;
  capabilitiesNeedsReview: number;
  capabilitiesAdded?: number;
  capabilitiesUpdated?: number;
}

export interface ListComponentsParams {
  page?: number;
  pageSize?: number;
  status?: CspInheritedComponentStatus;
  search?: string;
}

// ---------------------------------------------------------------------------
// Axios client (rooted at /api)
// ---------------------------------------------------------------------------

const cspClient = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,
});

cspClient.interceptors.request.use((config) => {
  const token = localStorage.getItem('auth_token');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  try {
    const raw = localStorage.getItem('ato-dashboard-settings');
    if (raw) {
      const settings = JSON.parse(raw) as { role?: string };
      if (settings.role) config.headers['X-Simulated-Role'] = settings.role;
    }
  } catch {
    // ignore
  }
  return config;
});

function unwrap<T>(envelope: Envelope<T>): T {
  if (envelope.status !== 'success' || envelope.data === undefined) {
    const err = envelope.error;
    throw Object.assign(new Error(err?.message ?? 'CSP inherited-components API error'), {
      errorCode: err?.errorCode,
      suggestion: err?.suggestion,
    });
  }
  return envelope.data;
}

// ---------------------------------------------------------------------------
// Visibility probe sentinel — mirrors the csp-onboarding API surface.
// ---------------------------------------------------------------------------

export interface UnavailableState {
  unavailable: true;
  reason: 'SINGLE_TENANT_MODE' | 'CSP_ONBOARDING_INCOMPLETE' | 'NETWORK_UNREACHABLE';
}

export type ListComponentsResult = CspInheritedComponentsPage | UnavailableState;

export function isUnavailable<T>(s: T | UnavailableState): s is UnavailableState {
  return (s as UnavailableState | undefined)?.unavailable === true;
}

// ---------------------------------------------------------------------------
// Endpoints
// ---------------------------------------------------------------------------

/**
 * `GET /csp/inherited-components` — paginated list. Returns an
 * `UnavailableState` sentinel for SingleTenant deployments and for tenants
 * where CSP onboarding is not yet `Active`. All other errors throw.
 */
export async function listCspInheritedComponents(
  params: ListComponentsParams = {},
): Promise<ListComponentsResult> {
  try {
    const { data } = await cspClient.get<Envelope<CspInheritedComponentsPage>>(
      '/csp/inherited-components',
      { params },
    );
    return unwrap(data);
  } catch (err) {
    const ax = err as AxiosError<Envelope<unknown>>;
    if (ax.response?.status === 404) return { unavailable: true, reason: 'SINGLE_TENANT_MODE' };
    if (ax.response?.status === 503) {
      return { unavailable: true, reason: 'CSP_ONBOARDING_INCOMPLETE' };
    }
    if (ax.response === undefined) return { unavailable: true, reason: 'NETWORK_UNREACHABLE' };
    throw err;
  }
}

/** `GET /csp/inherited-components/{componentId}`. */
export async function getCspInheritedComponent(
  componentId: string,
): Promise<CspInheritedComponent> {
  const { data } = await cspClient.get<Envelope<CspInheritedComponent>>(
    `/csp/inherited-components/${encodeURIComponent(componentId)}`,
  );
  return unwrap(data);
}

/** `PATCH /csp/inherited-components/{componentId}` — CSP-Admin only. */
export async function patchCspInheritedComponent(
  componentId: string,
  payload: CspInheritedComponentPatchRequest,
  rowVersion?: string,
): Promise<CspInheritedComponent> {
  const { data } = await cspClient.patch<Envelope<CspInheritedComponent>>(
    `/csp/inherited-components/${encodeURIComponent(componentId)}`,
    payload,
    rowVersion ? { headers: { 'If-Match': rowVersion } } : undefined,
  );
  return unwrap(data);
}

/** `DELETE /csp/inherited-components/{componentId}` — soft-delete. */
export async function archiveCspInheritedComponent(componentId: string): Promise<void> {
  await cspClient.delete(`/csp/inherited-components/${encodeURIComponent(componentId)}`);
}

/** `POST /csp/inherited-components/{componentId}/publish` — Draft → Published. */
export async function publishCspInheritedComponent(
  componentId: string,
): Promise<CspInheritedComponent> {
  const { data } = await cspClient.post<Envelope<CspInheritedComponent>>(
    `/csp/inherited-components/${encodeURIComponent(componentId)}/publish`,
  );
  return unwrap(data);
}

/** `POST /csp/inherited-components/{componentId}/remap` — re-run AI mapper. */
export async function remapCspInheritedComponent(
  componentId: string,
  options?: { replaceMapped?: boolean; confidenceThresholdOverride?: number },
): Promise<RemapResponse> {
  const { data } = await cspClient.post<Envelope<RemapResponse>>(
    `/csp/inherited-components/${encodeURIComponent(componentId)}/remap`,
    options ?? {},
  );
  return unwrap(data);
}

/** `GET /csp/inherited-components/{componentId}/capabilities`. */
export async function listCspInheritedCapabilities(
  componentId: string,
  status?: CspInheritedCapabilityStatus,
): Promise<CspInheritedCapability[]> {
  const { data } = await cspClient.get<Envelope<CspInheritedCapability[]>>(
    `/csp/inherited-components/${encodeURIComponent(componentId)}/capabilities`,
    { params: status ? { status } : undefined },
  );
  return unwrap(data);
}

/**
 * `PATCH /csp/inherited-components/{componentId}/capabilities/{capabilityId}/review`
 * — resolve a NeedsReview capability (CSP-Admin).
 */
export async function reviewCspInheritedCapability(
  componentId: string,
  capabilityId: string,
  payload: CapabilityReviewRequest,
): Promise<CspInheritedCapability> {
  const { data } = await cspClient.patch<Envelope<CspInheritedCapability>>(
    `/csp/inherited-components/${encodeURIComponent(componentId)}/capabilities/${encodeURIComponent(capabilityId)}/review`,
    payload,
  );
  return unwrap(data);
}

/**
 * `PATCH /csp/inherited-components/{componentId}/capabilities/{capabilityId}`
 * — generic edit for a single capability (CSP-Admin). A NeedsReview row
 * implicitly transitions to Mapped on edit. `rowVersion` is sent as the
 * `If-Match` header for optimistic concurrency.
 */
export async function patchCspInheritedCapability(
  componentId: string,
  capabilityId: string,
  payload: CspInheritedCapabilityPatchRequest,
  rowVersion?: string | null,
): Promise<CspInheritedCapability> {
  const { data } = await cspClient.patch<Envelope<CspInheritedCapability>>(
    `/csp/inherited-components/${encodeURIComponent(componentId)}/capabilities/${encodeURIComponent(capabilityId)}`,
    payload,
    rowVersion ? { headers: { 'If-Match': rowVersion } } : undefined,
  );
  return unwrap(data);
}

/**
 * `DELETE /csp/inherited-components/{componentId}/capabilities/{capabilityId}`
 * — soft-delete a capability (CSP-Admin). Sets `status = Archived` so the row
 * is hidden from non-CSP-Admin readers but auditable in the catalog.
 */
export async function archiveCspInheritedCapability(
  componentId: string,
  capabilityId: string,
): Promise<void> {
  await cspClient.delete(
    `/csp/inherited-components/${encodeURIComponent(componentId)}/capabilities/${encodeURIComponent(capabilityId)}`,
  );
}

/**
 * `POST /csp/inherited-components/import` — CSP-Admin only. Same multipart
 * shape as the wizard upload; intended for use after CSP onboarding has
 * completed.
 */
export async function importCspInheritedComponents(
  files: File[],
): Promise<AtoUploadResponse> {
  const form = new FormData();
  for (const f of files) form.append('files', f, f.name);
  const { data } = await cspClient.post<Envelope<AtoUploadResponse>>(
    '/csp/inherited-components/import',
    form,
    { headers: { 'Content-Type': 'multipart/form-data' } },
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Manual create surface (no file upload — CSP-Admin authors a row directly)
// ---------------------------------------------------------------------------

export interface CreateCspInheritedComponentRequest {
  name: string;
  description: string;
  componentType: CspComponentType;
}

export interface AddCspInheritedCapabilityRequest {
  name: string;
  description: string;
  mappedNistControlIds: string[];
  /**
   * Feature 050 FR-001 — when `true`, the capability is created with
   * `status = "Mapped"`, `reviewedBy = <caller oid>`, and
   * `reviewerNote = "Mapped on create by creator."`. The server writes a
   * second `Reviewed` history row alongside the `Created` row in the same
   * transaction. Default (`false` or omitted) leaves the row in
   * `NeedsReview` so it surfaces in the review queue.
   */
  markMappedImmediately?: boolean;
}

/**
 * `POST /csp/inherited-components` — CSP-Admin only. Authors a CSP-inherited
 * component row WITHOUT going through the multipart import pipeline. The
 * server stamps `sourceFormat = 'Manual'` and `status = 'Published'` so the
 * row is immediately visible to every hosted tenant.
 */
export async function createCspInheritedComponent(
  payload: CreateCspInheritedComponentRequest,
): Promise<CspInheritedComponent> {
  const { data } = await cspClient.post<Envelope<CspInheritedComponent>>(
    '/csp/inherited-components',
    payload,
  );
  return unwrap(data);
}

/**
 * `POST /csp/inherited-components/{componentId}/capabilities` — CSP-Admin
 * only. Adds a capability to an existing component. The server stamps
 * `mappedBy = 'User'`. Per Feature 050 FR-001 the default
 * `status = 'NeedsReview'` (caller must explicitly opt in via
 * `markMappedImmediately = true` to skip the review step).
 */
export async function addCspInheritedCapability(
  componentId: string,
  payload: AddCspInheritedCapabilityRequest,
): Promise<CspInheritedCapability> {
  const { data } = await cspClient.post<Envelope<CspInheritedCapability>>(
    `/csp/inherited-components/${encodeURIComponent(componentId)}/capabilities`,
    payload,
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Feature 050 / US2 — reparent a capability (POST .../move)
// ---------------------------------------------------------------------------

/**
 * Request body for `POST .../capabilities/{capabilityId}/move`.
 * Mirrors the wire contract in
 * `specs/050-csp-capability-lifecycle/contracts/http-api.md § 2.2`.
 */
export interface ReparentCspInheritedCapabilityRequest {
  /**
   * Destination component id. MUST be different from the current parent
   * and MUST exist in the caller's tenant and not be Archived.
   */
  targetComponentId: string;
}

/**
 * `POST /csp/inherited-components/{componentId}/capabilities/{capabilityId}/move`
 * — CSP-Admin only. Reparents a capability to a different non-archived
 * component in the caller's tenant. Resets `status = NeedsReview`, clears
 * reviewer metadata, writes one `Moved` history event.
 *
 * The `If-Match` header is REQUIRED (Feature 050 FR-002 / FR-012 — reparent
 * is too destructive to allow last-write-wins). A stale stamp surfaces as
 * a 412 ROW_VERSION_MISMATCH envelope.
 */
export async function reparentCspInheritedCapability(
  componentId: string,
  capabilityId: string,
  payload: ReparentCspInheritedCapabilityRequest,
  ifMatchRowVersion: string,
): Promise<CspInheritedCapability> {
  const { data } = await cspClient.post<Envelope<CspInheritedCapability>>(
    `/csp/inherited-components/${encodeURIComponent(componentId)}/capabilities/${encodeURIComponent(capabilityId)}/move`,
    payload,
    { headers: { 'If-Match': ifMatchRowVersion } },
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Feature 050 / US3 — capability history (GET .../history)
// ---------------------------------------------------------------------------

/**
 * One of six lifecycle events on a CSP-inherited capability. Mirrors
 * the C# enum `CapabilityHistoryEventType`.
 */
export type CapabilityHistoryEventType =
  | 'Created'
  | 'Edited'
  | 'Reviewed'
  | 'Moved'
  | 'Archived'
  | 'Unarchived';

/**
 * Structured metadata payload. Shape varies by `eventType` — consumers
 * MUST pattern-match on `eventType` before reading fields. Full shape
 * table lives in `specs/050-csp-capability-lifecycle/data-model.md § 1.4`.
 */
export type CapabilityHistoryEventMetadata =
  | null
  | {
      /** 'Moved' */
      fromComponentId?: string;
      toComponentId?: string;
      /** 'Created' | 'Edited' | 'Archived' (Remap-originated rows) */
      remapRunId?: string;
      source?: 'Remap' | 'Import';
      /** 'Created' (manual add with markMappedImmediately override) */
      markedMappedImmediately?: boolean;
      /** 'Reviewed' */
      reviewerNote?: string;
      /** 'Edited' (manual) */
      fields?: ReadonlyArray<string>;
    };

export interface CapabilityHistoryEvent {
  id: string;
  eventType: CapabilityHistoryEventType;
  actorOid: string;
  /** ISO-8601 UTC timestamp the row was written server-side. */
  occurredAt: string;
  /** Human-readable description, ≤ 500 chars. */
  summary: string;
  metadata: CapabilityHistoryEventMetadata;
}

export interface CapabilityHistoryPage {
  items: ReadonlyArray<CapabilityHistoryEvent>;
  page: number;
  pageSize: number;
  total: number;
}

export interface ListCapabilityHistoryParams {
  /** 1-based page index; default 1, min 1. */
  page?: number;
  /** Default 50; clamped to [1, 200] server-side. */
  pageSize?: number;
}

/**
 * `GET /csp/inherited-components/{componentId}/capabilities/{capabilityId}/history`
 * — CSP-Admin only. Paginated reverse-chronological audit-trail feed for one
 * capability, scoped to caller's tenant. Empty history is a 200 with
 * `items: []` (NOT a 404). Server sets `Cache-Control: no-store` so the
 * dashboard refetches on every drawer open.
 */
export async function listCapabilityHistory(
  componentId: string,
  capabilityId: string,
  params: ListCapabilityHistoryParams = {},
): Promise<CapabilityHistoryPage> {
  const { data } = await cspClient.get<Envelope<CapabilityHistoryPage>>(
    `/csp/inherited-components/${encodeURIComponent(componentId)}/capabilities/${encodeURIComponent(capabilityId)}/history`,
    { params },
  );
  return unwrap(data);
}
