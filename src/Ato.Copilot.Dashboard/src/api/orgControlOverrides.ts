import axios from 'axios';
import { attachAuthInterceptor } from '../features/auth/interceptors';
import { getMsalInstance, DEFAULT_API_SCOPES } from '../features/auth/msalInstance';

/**
 * Dedicated axios client for the per-org tenancy API surface
 * (Feature 048 follow-up — user ask #2). Wraps `/api/orgs/*` endpoints
 * with the MSAL Bearer interceptor + simulated-role propagation.
 */
const orgsClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL_ORGS || '/api/orgs',
  headers: { 'Content-Type': 'application/json' },
});

// Feature 051 T053: MSAL bearer injection (silent renewal + 401 retry).
attachAuthInterceptor(orgsClient, getMsalInstance, DEFAULT_API_SCOPES);

orgsClient.interceptors.request.use((config) => {
  // Mirror apiClient's dev-only role spoofing so write endpoints reach
  // the OnboardingAdministratorRequirement policy in dev mode.
  try {
    const raw = localStorage.getItem('ato-dashboard-settings');
    if (raw) {
      const settings = JSON.parse(raw) as { role?: string };
      if (settings.role) {
        config.headers['X-Simulated-Role'] = settings.role;
      }
    }
  } catch {
    // ignore parse errors
  }
  return config;
});

interface ApiEnvelope<T> {
  ok: boolean;
  data?: T;
  errorCode?: string;
  message?: string;
  suggestion?: string;
}

export type OrgControlImplementationStatus =
  | 'Implemented'
  | 'PartiallyImplemented'
  | 'Planned'
  | 'NotApplicable';

export type OrgControlInheritanceApplicability =
  | 'FullyInherited'
  | 'Hybrid'
  | 'NotApplicableToThisSystem';

export interface OrgControlOverrideDto {
  id: string;
  controlId: string;
  implementationStatus: OrgControlImplementationStatus | null;
  inheritanceApplicability: OrgControlInheritanceApplicability | null;
  justification: string | null;
  createdAt: string;
  createdBy: string;
  updatedAt: string;
  updatedBy: string;
}

export interface OrgControlOverrideRequest {
  implementationStatus: OrgControlImplementationStatus | null;
  inheritanceApplicability: OrgControlInheritanceApplicability | null;
  justification: string | null;
}

/**
 * Fetch every org-level override for the active tenant. Returns an empty
 * array when none exist.
 */
export async function listOrgControlOverrides(): Promise<OrgControlOverrideDto[]> {
  const { data } = await orgsClient.get<ApiEnvelope<OrgControlOverrideDto[]>>('/control-overrides');
  return data.data ?? [];
}

export async function getOrgControlOverride(controlId: string): Promise<OrgControlOverrideDto | null> {
  try {
    const { data } = await orgsClient.get<ApiEnvelope<OrgControlOverrideDto>>(
      `/control-overrides/${controlId}`,
    );
    return data.data ?? null;
  } catch (err: any) {
    if (err?.response?.status === 404) return null;
    throw err;
  }
}

export async function upsertOrgControlOverride(
  controlId: string,
  body: OrgControlOverrideRequest,
): Promise<OrgControlOverrideDto> {
  const { data } = await orgsClient.put<ApiEnvelope<OrgControlOverrideDto>>(
    `/control-overrides/${controlId}`,
    body,
  );
  if (!data.data) throw new Error(data.message ?? 'Upsert failed');
  return data.data;
}

export async function deleteOrgControlOverride(controlId: string): Promise<void> {
  await orgsClient.delete(`/control-overrides/${controlId}`);
}

// ─── SCA Review Workflow (Issue #244) ────────────────────────────────────────

export type OrgControlOverrideApprovalStatus = 'Pending' | 'Approved' | 'Rejected';

export interface OrgControlOverrideReviewRequest {
  comment?: string;
}

/**
 * Approve a pending org control override. Requires SecurityControlAssessor role.
 * Calls POST /api/orgs/control-overrides/{controlId}/approve
 */
export async function approveOrgControlOverride(
  controlId: string,
  comment?: string,
): Promise<OrgControlOverrideDto> {
  const { data } = await orgsClient.post<ApiEnvelope<OrgControlOverrideDto>>(
    `/control-overrides/${controlId}/approve`,
    { comment: comment ?? null },
  );
  if (!data.data) throw new Error(data.message ?? 'Approval failed');
  return data.data;
}

/**
 * Reject a pending org control override. Requires SecurityControlAssessor role.
 * Calls POST /api/orgs/control-overrides/{controlId}/reject
 */
export async function rejectOrgControlOverride(
  controlId: string,
  comment: string,
): Promise<OrgControlOverrideDto> {
  const { data } = await orgsClient.post<ApiEnvelope<OrgControlOverrideDto>>(
    `/control-overrides/${controlId}/reject`,
    { comment },
  );
  if (!data.data) throw new Error(data.message ?? 'Rejection failed');
  return data.data;
}

/**
 * Fetch the override for a single control id, or null when none exists
 * (the underlying GET returns 404; we coerce that to null because the
 * UI's natural state for "no override" is just absence).
 */
export async function getOrgControlOverride(
  controlId: string,
): Promise<OrgControlOverrideDto | null> {
  try {
    const { data } = await orgsClient.get<ApiEnvelope<OrgControlOverrideDto>>(
      `/control-overrides/${encodeURIComponent(controlId)}`,
    );
    return data.data ?? null;
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 404) return null;
    throw err;
  }
}

/**
 * Upsert the override. Passing both override fields as null deletes the
 * override entirely (the row reverts to CSP defaults), in which case the
 * server returns `{ ok: true, data: null }`.
 */
export async function upsertOrgControlOverride(
  controlId: string,
  payload: OrgControlOverrideRequest,
): Promise<OrgControlOverrideDto | null> {
  const { data } = await orgsClient.put<ApiEnvelope<OrgControlOverrideDto | null>>(
    `/control-overrides/${encodeURIComponent(controlId)}`,
    payload,
  );
  if (!data.ok) {
    const msg = data.message ?? data.errorCode ?? 'Save failed';
    throw new Error(msg);
  }
  return data.data ?? null;
}

/**
 * Delete the override (idempotent). Returns true when a row was removed,
 * false when no override existed for the control.
 */
export async function deleteOrgControlOverride(controlId: string): Promise<boolean> {
  try {
    await orgsClient.delete(`/control-overrides/${encodeURIComponent(controlId)}`);
    return true;
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 404) return false;
    throw err;
  }
}
