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
import { attachAuthInterceptor } from '../auth/interceptors';
import { getMsalInstance, DEFAULT_API_SCOPES } from '../auth/msalInstance';

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

if (import.meta.env.DEV) {
  tenancyClient.interceptors.request.use((config) => {
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
}

// Feature 051 T053: MSAL bearer injection (silent renewal + 401 retry).
attachAuthInterceptor(tenancyClient, getMsalInstance, DEFAULT_API_SCOPES);

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
 *
 * Feature 051 T135 [US8] / FR-029 (analysis C6) — BEFORE issuing the
 * request, capture the URL the caller is on so the Exit handler can
 * return them there. We do this here (rather than in each call site)
 * so every entry point into impersonation — OrgsTable, CspSystemsPage,
 * TenantPicker, future flows — inherits the behavior for free.
 */
export async function startImpersonation(
  tenantId: string,
  displayName: string,
): Promise<ImpersonationResponse> {
  // Capture the pre-impersonation URL synchronously, BEFORE any network
  // I/O. Use dynamic import so this module remains side-effect-free for
  // consumers that do not need the helper at module load time, and so
  // there is no risk of an undefined `window` in SSR scenarios (the SPA
  // never SSRs today but the contract should be robust).
  if (typeof window !== 'undefined') {
    try {
      const { setPreImpersonationUrl } = await import('../auth/preImpersonationUrl');
      const here = window.location.pathname + window.location.search + window.location.hash;
      setPreImpersonationUrl(here);
    } catch {
      // Best-effort — a failure here only means the Exit handler will
      // fall back to the persona-default landing, not a functional break.
    }
  }

  const { data } = await tenancyClient.post<Envelope<ImpersonationResponse>>(
    `/tenants/${encodeURIComponent(tenantId)}/impersonate`,
  );
  const result = unwrap(data);
  writeImpersonation({
    tenantId: result.impersonatedTenantId,
    displayName,
    expiresAt: result.expiresAt,
  });
  // Feature 051 T135 — fan out a tenant-changed event so the new
  // ImpersonationBanner (and any other tenant-aware component) can
  // refetch /me without polling.
  if (typeof window !== 'undefined') {
    try {
      window.dispatchEvent(new CustomEvent('ato:tenant-changed'));
    } catch {
      // ignore (no window in tests)
    }
  }
  return result;
}

/**
 * DELETE /api/tenants/impersonation — clears the cookie and the local mirror.
 * Returns silently when no impersonation is in progress (idempotent).
 *
 * Feature 051 T135 [US8] also fires an `ato:tenant-changed` event so the
 * new ImpersonationBanner (and other tenant-aware components) refetch
 * /me promptly after the scope flips back to the home tenant.
 */
export async function endImpersonation(): Promise<void> {
  try {
    await tenancyClient.delete('/tenants/impersonation');
  } finally {
    // Always clear local state, even if the network call fails — otherwise
    // the dashboard would keep showing the banner indefinitely.
    writeImpersonation(null);
    if (typeof window !== 'undefined') {
      try {
        window.dispatchEvent(new CustomEvent('ato:tenant-changed'));
      } catch {
        // ignore (no window in tests)
      }
    }
  }
}

/** Test/debug helper. Not exported via the package's public surface. */
export const __internal = {
  IMPERSONATION_KEY,
  IMPERSONATION_EVENT,
  writeImpersonation,
};
