/**
 * Feature 048 (T074): Axios wrappers for the tenants administration +
 * impersonation surface (`/api/tenants`).
 *
 * The dashboard's existing `apiClient` is hard-pinned at `/api/dashboard`,
 * so we declare a sibling client rooted at `/api` for the cross-cutting
 * tenants endpoints. Auth interceptor logic mirrors `client.ts`.
 *
 * Visibility rules per FR-041 / FR-042:
 *  - `SingleTenant` deployments: `/api/deployment/mode` returns `SingleTenant`
 *    (or 404 until T084 is implemented). UI consumers must hide the picker.
 *  - `MultiTenant` deployments: only callers in the `CSP.Admin` group will
 *    receive `200` from `GET /api/tenants`; everyone else gets 401/403.
 */

import axios, { type AxiosError } from 'axios';

// ---------------------------------------------------------------------------
// Wire types (mirror specs/048-tenant-isolation/contracts/tenants.openapi.yaml)
// ---------------------------------------------------------------------------

export interface Envelope<T> {
  status: 'success' | 'error';
  data?: T;
  metadata: { executionTimeMs: number; timestamp: string; tool?: string | null };
  error?: { errorCode: string; message: string; suggestion?: string };
}

export type TenantStatus = 'Active' | 'Suspended' | 'Disabled';
export type OnboardingState = 'Pending' | 'InWizard' | 'Active';
export type DeploymentMode = 'SingleTenant' | 'MultiTenant';

export interface Tenant {
  id: string;
  entraTenantId: string | null;
  displayName: string;
  legalEntityName: string | null;
  doDComponent: string | null;
  primaryPocName: string | null;
  primaryPocEmail: string | null;
  status: TenantStatus;
  onboardingState: OnboardingState;
  createdAt: string;
  createdBy: string;
  updatedAt: string | null;
  updatedBy: string | null;
}

export interface ImpersonationResponse {
  impersonatedTenantId: string;
  expiresAt: string;
}

export interface DeploymentModeResponse {
  mode: DeploymentMode;
  defaultTenantId?: string;
}

// ---------------------------------------------------------------------------
// Local impersonation-state mirror (HttpOnly cookie cannot be read from JS)
// ---------------------------------------------------------------------------

const IMPERSONATION_KEY = 'ato-impersonation';
const IMPERSONATION_EVENT = 'ato:impersonation-changed';

export interface ImpersonationState {
  tenantId: string;
  displayName: string;
  expiresAt: string;
}

export function readImpersonation(): ImpersonationState | null {
  try {
    const raw = sessionStorage.getItem(IMPERSONATION_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as ImpersonationState;
    if (!parsed.tenantId || !parsed.expiresAt) return null;
    if (new Date(parsed.expiresAt).getTime() <= Date.now()) {
      sessionStorage.removeItem(IMPERSONATION_KEY);
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

function writeImpersonation(state: ImpersonationState | null): void {
  try {
    if (state) {
      sessionStorage.setItem(IMPERSONATION_KEY, JSON.stringify(state));
    } else {
      sessionStorage.removeItem(IMPERSONATION_KEY);
    }
  } catch {
    // sessionStorage may be unavailable (Safari private mode); ignore.
  }
  try {
    window.dispatchEvent(new CustomEvent(IMPERSONATION_EVENT));
  } catch {
    // window may be unavailable in tests; ignore.
  }
}

/**
 * Subscribe to local impersonation-state changes. The callback fires after
 * `startImpersonation` / `endImpersonation` resolve successfully.
 */
export function onImpersonationChanged(handler: () => void): () => void {
  if (typeof window === 'undefined') return () => undefined;
  window.addEventListener(IMPERSONATION_EVENT, handler);
  return () => window.removeEventListener(IMPERSONATION_EVENT, handler);
}

// ---------------------------------------------------------------------------
// Axios client (rooted at /api — sibling to the /api/dashboard client)
// ---------------------------------------------------------------------------

const tenancyClient = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true, // impersonation cookie is HttpOnly + cross-path
});

tenancyClient.interceptors.request.use((config) => {
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
    throw Object.assign(new Error(err?.message ?? 'Tenancy API error'), {
      errorCode: err?.errorCode,
      suggestion: err?.suggestion,
    });
  }
  return envelope.data;
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Returns deployment mode. Falls back to `SingleTenant` if the endpoint is
 * unreachable (e.g. T084 has not yet been deployed) so callers can render
 * defensively without a try/catch.
 */
export async function getDeploymentMode(): Promise<DeploymentModeResponse> {
  try {
    const { data } = await tenancyClient.get<Envelope<DeploymentModeResponse>>(
      '/deployment/mode',
    );
    return unwrap(data);
  } catch (err) {
    const ax = err as AxiosError;
    if (ax.response?.status === 404 || ax.response === undefined) {
      // Endpoint not yet deployed — assume SingleTenant.
      return { mode: 'SingleTenant' };
    }
    throw err;
  }
}

export interface ListTenantsParams {
  status?: TenantStatus;
  page?: number;
  pageSize?: number;
}

export interface ListTenantsResult {
  items: Tenant[];
  total: number;
}

/**
 * GET /api/tenants — CSP-Admin only. Caller should treat 401/403 as
 * "user is not CSP-Admin" and hide the picker rather than surface an error.
 */
export async function listTenants(params?: ListTenantsParams): Promise<ListTenantsResult> {
  const { data } = await tenancyClient.get<Envelope<ListTenantsResult>>('/tenants', {
    params,
  });
  return unwrap(data);
}

/**
 * POST /api/tenants/{tenantId}/impersonate — issues an HttpOnly cookie and
 * mirrors the active tenant in `sessionStorage` so the dashboard can render
 * the impersonation banner.
 */
export async function startImpersonation(
  tenantId: string,
  displayName: string,
): Promise<ImpersonationResponse> {
  const { data } = await tenancyClient.post<Envelope<ImpersonationResponse>>(
    `/tenants/${encodeURIComponent(tenantId)}/impersonate`,
  );
  const result = unwrap(data);
  writeImpersonation({
    tenantId: result.impersonatedTenantId,
    displayName,
    expiresAt: result.expiresAt,
  });
  return result;
}

/**
 * DELETE /api/tenants/impersonation — clears the cookie and the local mirror.
 * Returns silently when no impersonation is in progress (idempotent).
 */
export async function endImpersonation(): Promise<void> {
  try {
    await tenancyClient.delete('/tenants/impersonation');
  } finally {
    // Always clear local state, even if the network call fails — otherwise
    // the dashboard would keep showing the banner indefinitely.
    writeImpersonation(null);
  }
}

/** Test/debug helper. Not exported via the package's public surface. */
export const __internal = {
  IMPERSONATION_KEY,
  IMPERSONATION_EVENT,
  writeImpersonation,
};
