/**
 * Legacy provider-profile state read for branding and guards. Guided setup
 * mutations are owned by providerSetupApi; the legacy HTTP routes remain
 * server-side compatibility surfaces, not a second Dashboard wizard engine.
 *
 * Wire types mirror specs/048-tenant-isolation/contracts/csp-onboarding.openapi.yaml.
 */

import axios, { type AxiosError } from 'axios';
import { attachAuthInterceptor } from '../auth/interceptors';
import { getMsalInstance, DEFAULT_API_SCOPES } from '../auth/msalInstance';

// ---------------------------------------------------------------------------
// Wire types
// ---------------------------------------------------------------------------

export interface Envelope<T> {
  status: 'success' | 'error';
  data?: T;
  metadata: { executionTimeMs: number; timestamp: string; tool?: string | null };
  error?: { errorCode: string; message: string; suggestion?: string };
}

export type CspOnboardingState = 'Pending' | 'InWizard' | 'Active';
export type CspOnboardingStep =
  | 'Identity'
  | 'SupportContact'
  | 'Classification'
  | 'Review'
  | 'Complete';
export type ClassificationFloor = 'Unclassified' | 'CUI' | 'Secret';

export interface CspOnboardingStateDto {
  cspProfileId: string | null;
  onboardingState: CspOnboardingState;
  currentStep: CspOnboardingStep;
  identity?: {
    legalEntityName?: string | null;
    displayName?: string | null;
    logoUrl?: string | null;
  };
  supportContact?: {
    primarySupportEmail?: string | null;
    supportPhone?: string | null;
  };
  classification?: {
    defaultClassificationFloor?: ClassificationFloor | null;
  };
  onboardingCompletedAt?: string | null;
}

// ---------------------------------------------------------------------------
// Axios client (rooted at /api — sibling to the /api/dashboard client)
// ---------------------------------------------------------------------------

const cspClient = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,
});

if (import.meta.env.DEV) {
  cspClient.interceptors.request.use((config) => {
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
attachAuthInterceptor(cspClient, getMsalInstance, DEFAULT_API_SCOPES);
function unwrap<T>(envelope: Envelope<T>): T {
  if (envelope.status !== 'success' || envelope.data === undefined) {
    const err = envelope.error;
    throw Object.assign(new Error(err?.message ?? 'CSP onboarding API error'), {
      errorCode: err?.errorCode,
      suggestion: err?.suggestion,
    });
  }
  return envelope.data;
}

/**
 * `CspOnboardingUnavailable` — dashboard signals "deployment is in
 * SingleTenant mode" by surfacing this sentinel result instead of a thrown
 * error so callers (route guards, header logo) can render defensively.
 */
export interface UnavailableState {
  unavailable: true;
  reason: 'SINGLE_TENANT_MODE' | 'NOT_CSP_ADMIN' | 'NETWORK_UNREACHABLE';
}

export type CspStateResult = CspOnboardingStateDto | UnavailableState;

export function isUnavailable(s: CspStateResult): s is UnavailableState {
  return (s as UnavailableState).unavailable === true;
}

/**
 * GET /api/csp/onboarding/state. Returns either the live state or an
 * `UnavailableState` sentinel for SingleTenant / non-CSP-Admin callers.
 * Never throws on 401/403/404 — those are normal in SingleTenant mode or
 * when the dashboard is loaded by a non-CSP user.
 */
export async function getCspOnboardingState(): Promise<CspStateResult> {
  try {
    const { data } = await cspClient.get<Envelope<CspOnboardingStateDto>>(
      '/csp/onboarding/state',
    );
    return unwrap(data);
  } catch (err) {
    const ax = err as AxiosError;
    if (ax.response?.status === 404) return { unavailable: true, reason: 'SINGLE_TENANT_MODE' };
    if (ax.response?.status === 401 || ax.response?.status === 403) {
      return { unavailable: true, reason: 'NOT_CSP_ADMIN' };
    }
    if (ax.response === undefined) return { unavailable: true, reason: 'NETWORK_UNREACHABLE' };
    throw err;
  }
}
