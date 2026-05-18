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
export type CspInheritedCapabilityStatus = 'Mapped' | 'NeedsReview';
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
  createdAt?: string;
  updatedAt?: string;
}

export interface CspInheritedComponentsPage {
  items: CspInheritedComponent[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
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
 * `mappedBy = 'User'` and `status = 'Mapped'` so a future remap respects it.
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
