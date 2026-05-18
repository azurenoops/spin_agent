/**
 * Feature 048 (T185): Axios wrappers for the CSP cross-tenant operational
 * dashboard surface (`/api/csp/dashboard/*`). CSP-Admin only; available
 * only in `MultiTenant` deployments after CSP onboarding (US7) is `Active`.
 *
 * Wire types mirror specs/048-tenant-isolation/contracts/csp-dashboard.openapi.yaml
 * and the actual server projection in `Ato.Copilot.Mcp.Endpoints.Csp.CspDashboardEndpoints`.
 */

import axios, { type AxiosError } from 'axios';

// ---------------------------------------------------------------------------
// Wire types
// ---------------------------------------------------------------------------

export interface Envelope<T> {
  status: 'success' | 'error';
  data?: T;
  metadata: { executionTimeMs: number; timestamp: string; tool?: string | null };
  error?: { errorCode: string; message: string; suggestion?: string };
}

export type TenantStatus = 'Active' | 'Suspended' | 'Disabled';
export type OnboardingState = 'Pending' | 'InWizard' | 'Active';
export type AtoDecisionStatus = 'Authorized' | 'InProcess' | 'Denied';
export type AtoDecisionType = 'ATO' | 'IATO' | 'IATT' | 'ATC' | 'Denial';
export type FindingSeverity = 'Critical' | 'High' | 'Moderate' | 'Low';

export interface AtoStatusCounts {
  authorized: number;
  inProcess: number;
  denied: number;
}

export interface FindingSeverityCounts {
  critical: number;
  high: number;
  moderate: number;
  low: number;
}

export interface TenantCounts {
  active: number;
  suspended: number;
  disabled: number;
  total: number;
}

export interface SummaryResponse {
  tenantCounts: TenantCounts;
  disabledTenantCount: number;
  organizationCount: number;
  systemCount: number;
  atoStatusCounts: AtoStatusCounts;
  openFindingsBySeverity: FindingSeverityCounts;
  openPoamCount: number;
  openDeviationCount: number;
  generatedAt: string;
}

export interface TenantSummary {
  tenantId: string;
  displayName: string;
  status: TenantStatus;
  onboardingState: OnboardingState;
  organizationCount: number;
  systemCount: number;
  atoStatusCounts: AtoStatusCounts;
  openFindingCount: number;
  openPoamCount: number;
  openDeviationCount: number;
  lastActivityTimestamp: string | null;
}

export interface TenantsPage {
  items: TenantSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface AtoRow {
  decisionId: string;
  tenantId: string;
  orgDisplayName: string;
  systemId: string;
  systemName: string;
  decisionStatus: AtoDecisionStatus;
  decisionType: AtoDecisionType;
  decisionDate: string;
  expirationDate: string | null;
  isActive: boolean;
}

export interface AtosPage {
  items: AtoRow[];
  page: number;
  pageSize: number;
  totalCount: number;
}

// ─── Systems (cross-tenant per-system list) ───────────────────────────────
// Mirrors specs/048-tenant-isolation/contracts/csp-dashboard.openapi.yaml
// SystemRow / SystemsPage. atoSeverity is the same dashboard token used by
// PortfolioSystemSummary so the same badge component renders both surfaces.

export type CspSystemAtoStatus = 'None' | 'Active' | 'Expired';
export type CspSystemAtoSeverity = 'none' | 'green' | 'yellow' | 'red' | 'expired';

export interface SystemRow {
  systemId: string;
  name: string;
  acronym: string | null;
  tenantId: string;
  orgDisplayName: string;
  impactLevel: string;
  currentRmfPhase: string;
  complianceScore: number;
  atoExpirationDate: string | null;
  atoStatus: CspSystemAtoStatus;
  atoDaysRemaining: number | null;
  atoSeverity: CspSystemAtoSeverity;
  openPoamCount: number;
  overduePoamCount: number;
}

export interface SystemsPage {
  items: SystemRow[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export type SystemsSortField =
  | 'name'
  | 'orgDisplayName'
  | 'impactLevel'
  | 'rmfPhase'
  | 'complianceScore'
  | 'atoExpiration'
  | 'openPoamCount';

export interface ListSystemsParams {
  page?: number;
  pageSize?: number;
  impactLevel?: 'Low' | 'Moderate' | 'High';
  rmfPhase?:
    | 'Prepare'
    | 'Categorize'
    | 'Select'
    | 'Implement'
    | 'Assess'
    | 'Authorize'
    | 'Monitor';
  sort?: SystemsSortField;
  order?: 'asc' | 'desc';
}

export type TenantsSortField =
  | 'displayName'
  | 'status'
  | 'openFindingCount'
  | 'lastActivityTimestamp';

export interface ListTenantsParams {
  page?: number;
  pageSize?: number;
  status?: TenantStatus;
  sort?: TenantsSortField;
  order?: 'asc' | 'desc';
}

export interface ListAtosParams {
  page?: number;
  pageSize?: number;
  decisionStatus?: AtoDecisionStatus;
  decisionType?: AtoDecisionType;
  since?: string;
  until?: string;
}

// ---------------------------------------------------------------------------
// Sentinel result for unavailable states (single-tenant / not CSP-Admin /
// onboarding incomplete / network unreachable).  Mirrors the same pattern
// used by csp-onboarding/api.ts so callers can render defensively without
// try/catch noise on every page load.
// ---------------------------------------------------------------------------

export type UnavailableReason =
  | 'SINGLE_TENANT_MODE'
  | 'NOT_CSP_ADMIN'
  | 'CSP_ONBOARDING_INCOMPLETE'
  | 'NETWORK_UNREACHABLE';

export interface UnavailableState {
  unavailable: true;
  reason: UnavailableReason;
}

export type SummaryResult = SummaryResponse | UnavailableState;
export type TenantsResult = TenantsPage | UnavailableState;
export type AtosResult = AtosPage | UnavailableState;
export type SystemsResult = SystemsPage | UnavailableState;

export function isUnavailable<T>(r: T | UnavailableState): r is UnavailableState {
  return (r as UnavailableState).unavailable === true;
}

// ---------------------------------------------------------------------------
// Axios client (rooted at /api — sibling to the /api/dashboard client)
// ---------------------------------------------------------------------------

const cspDashboardClient = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,
});

cspDashboardClient.interceptors.request.use((config) => {
  const token = localStorage.getItem('auth_token');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  try {
    const raw = localStorage.getItem('ato-dashboard-settings');
    if (raw) {
      const settings = JSON.parse(raw) as { role?: string };
      if (settings.role) config.headers['X-Simulated-Role'] = settings.role;
    }
  } catch {
    // ignore — corrupt settings entry is non-fatal here
  }
  return config;
});

function unwrap<T>(envelope: Envelope<T>): T {
  if (envelope.status !== 'success' || envelope.data === undefined) {
    const err = envelope.error;
    throw Object.assign(new Error(err?.message ?? 'CSP dashboard API error'), {
      errorCode: err?.errorCode,
      suggestion: err?.suggestion,
    });
  }
  return envelope.data;
}

function toUnavailable(err: unknown): UnavailableState | null {
  const ax = err as AxiosError<Envelope<unknown>>;
  if (ax.response === undefined) return { unavailable: true, reason: 'NETWORK_UNREACHABLE' };
  const status = ax.response.status;
  const code = ax.response.data?.error?.errorCode;
  if (status === 404 || code === 'SINGLE_TENANT_MODE')
    return { unavailable: true, reason: 'SINGLE_TENANT_MODE' };
  if (status === 401 || status === 403 || code === 'FORBIDDEN_NOT_CSP_ADMIN')
    return { unavailable: true, reason: 'NOT_CSP_ADMIN' };
  if (status === 503 || code === 'CSP_ONBOARDING_INCOMPLETE')
    return { unavailable: true, reason: 'CSP_ONBOARDING_INCOMPLETE' };
  return null;
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * GET /api/csp/dashboard/summary. Returns either the live cross-tenant
 * summary or an `UnavailableState` sentinel for the four "expected" no-op
 * states (single-tenant / not CSP-Admin / onboarding incomplete /
 * unreachable). Other errors propagate so the page boundary can render an
 * error UI.
 */
export async function getCspDashboardSummary(): Promise<SummaryResult> {
  try {
    const { data } = await cspDashboardClient.get<Envelope<SummaryResponse>>(
      '/csp/dashboard/summary',
    );
    return unwrap(data);
  } catch (err) {
    const sentinel = toUnavailable(err);
    if (sentinel) return sentinel;
    throw err;
  }
}

/** GET /api/csp/dashboard/tenants. Same unavailable semantics as summary. */
export async function getCspDashboardTenants(
  params?: ListTenantsParams,
): Promise<TenantsResult> {
  try {
    const { data } = await cspDashboardClient.get<Envelope<TenantsPage>>(
      '/csp/dashboard/tenants',
      { params },
    );
    return unwrap(data);
  } catch (err) {
    const sentinel = toUnavailable(err);
    if (sentinel) return sentinel;
    throw err;
  }
}

/** GET /api/csp/dashboard/atos. Same unavailable semantics as summary. */
export async function getCspDashboardAtos(
  params?: ListAtosParams,
): Promise<AtosResult> {
  try {
    const { data } = await cspDashboardClient.get<Envelope<AtosPage>>(
      '/csp/dashboard/atos',
      { params },
    );
    return unwrap(data);
  } catch (err) {
    const sentinel = toUnavailable(err);
    if (sentinel) return sentinel;
    throw err;
  }
}

/** GET /api/csp/dashboard/systems. Same unavailable semantics as summary. */
export async function getCspDashboardSystems(
  params?: ListSystemsParams,
): Promise<SystemsResult> {
  try {
    const { data } = await cspDashboardClient.get<Envelope<SystemsPage>>(
      '/csp/dashboard/systems',
      { params },
    );
    return unwrap(data);
  } catch (err) {
    const sentinel = toUnavailable(err);
    if (sentinel) return sentinel;
    throw err;
  }
}

// ---------------------------------------------------------------------------
// Provision a new mission-owner organization (tenant). Used by the
// `+ Create org` action on the CSP Portfolio page.
// ---------------------------------------------------------------------------

export interface CreateCspTenantRequest {
  displayName: string;
  legalEntityName?: string;
  primaryPocName?: string;
  primaryPocEmail?: string;
}

export interface CreateCspTenantResponse {
  tenantId: string;
  displayName: string;
  status: TenantStatus;
  onboardingState: string;
  createdAt: string;
  createdBy: string;
}

/**
 * POST /api/csp/dashboard/tenants — provision a new organization.
 *
 * Errors are surfaced as Error objects with `errorCode`/`message` so the
 * caller can present them inline (e.g. duplicate name → `VALIDATION_FAILED`).
 * Unavailable states (single-tenant / not CSP-Admin) are NOT folded into
 * a sentinel here — the button that calls this is only rendered when the
 * sibling GET surfaces work, so an unavailable response is a real bug.
 */
export async function createCspDashboardTenant(
  body: CreateCspTenantRequest,
): Promise<CreateCspTenantResponse> {
  const { data } = await cspDashboardClient.post<Envelope<CreateCspTenantResponse>>(
    '/csp/dashboard/tenants',
    body,
  );
  return unwrap(data);
}
