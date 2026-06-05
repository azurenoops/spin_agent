/**
 * Feature 048 (T167–T170): Axios wrappers for the CSP-onboarding wizard
 * (`/api/csp/onboarding/*`). Singleton onboarding flow for the hosting CSP
 * itself in `MultiTenant` deployments. Available only to `CSP.Admin`
 * callers; hidden in `SingleTenant` deployments (every endpoint 404s).
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

export interface IdentityRequest {
  legalEntityName: string;
  displayName: string;
  logoUrl?: string | null;
}

export interface SupportContactRequest {
  primarySupportEmail: string;
  supportPhone?: string | null;
}

export interface ClassificationRequest {
  defaultClassificationFloor: ClassificationFloor;
}

export interface SubmitResponse {
  cspProfileId: string;
  onboardingState: CspOnboardingState;
  onboardingCompletedAt: string;
}

// ---------------------------------------------------------------------------
// Axios client (rooted at /api — sibling to the /api/dashboard client)
// ---------------------------------------------------------------------------

const cspClient = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,
});

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

export async function postCspOnboardingIdentity(
  payload: IdentityRequest,
): Promise<CspOnboardingStateDto> {
  const { data } = await cspClient.post<Envelope<CspOnboardingStateDto>>(
    '/csp/onboarding/identity',
    payload,
  );
  return unwrap(data);
}

export async function postCspOnboardingSupport(
  payload: SupportContactRequest,
): Promise<CspOnboardingStateDto> {
  const { data } = await cspClient.post<Envelope<CspOnboardingStateDto>>(
    '/csp/onboarding/support',
    payload,
  );
  return unwrap(data);
}

export async function postCspOnboardingClassification(
  payload: ClassificationRequest,
): Promise<CspOnboardingStateDto> {
  const { data } = await cspClient.post<Envelope<CspOnboardingStateDto>>(
    '/csp/onboarding/classification',
    payload,
  );
  return unwrap(data);
}

export async function postCspOnboardingSubmit(): Promise<SubmitResponse> {
  const { data } = await cspClient.post<Envelope<SubmitResponse>>(
    '/csp/onboarding/submit',
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Feature 048 / US9 / T211 — ATO documents step (FR-099..FR-103)
// ---------------------------------------------------------------------------

export type AtoSourceFormat =
  | 'Pdf'
  | 'Docx'
  | 'OscalJson'
  | 'Xlsx'
  | 'EmassZip'
  | 'Manual';

/** Per-file entry returned by `POST /csp/onboarding/atos/upload`. */
export interface AtoUploadFileResult {
  fileName: string;
  sourceFormat: AtoSourceFormat;
  parsedSuccessfully: boolean;
  parseError?: string | null;
  componentsExtracted: number;
  capabilitiesMapped: number;
  capabilitiesNeedsReview: number;
}

/** Aggregated response shape from `POST /csp/onboarding/atos/upload`. */
export interface AtoUploadResponse {
  documentsAccepted: number;
  componentsExtracted: number;
  capabilitiesMapped: number;
  capabilitiesNeedsReview: number;
  aiMappingAvailable: boolean;
  aiMappingFailureReason?: string | null;
  files: AtoUploadFileResult[];
}

/** Per-document entry inside `AtoStepState.documents`. */
export interface AtoStepStateDocument {
  fileName: string;
  sourceFormat: AtoSourceFormat;
  componentsExtracted: number;
  capabilitiesMapped: number;
  capabilitiesNeedsReview: number;
}

/** Response shape from `GET /csp/onboarding/atos/state`. */
export interface AtoStepState {
  cspProfileId?: string | null;
  documentsUploaded: number;
  componentsExtracted: number;
  capabilitiesMapped: number;
  capabilitiesNeedsReview: number;
  aiMappingAvailable: boolean;
  aiMappingFailureReason?: string | null;
  files?: AtoStepStateDocument[];
  documents?: AtoStepStateDocument[];
}

/**
 * Uploads one or more ATO source documents (PDF SSP, DOCX, OSCAL JSON, XLSX,
 * eMASS ZIP) during the CSP-onboarding wizard's ATO Documents step.
 * Multipart, max 50 MB per file (enforced both client-side and server-side).
 */
export async function postCspOnboardingAtosUpload(
  files: File[],
): Promise<AtoUploadResponse> {
  const form = new FormData();
  for (const f of files) form.append('files', f, f.name);
  const { data } = await cspClient.post<Envelope<AtoUploadResponse>>(
    '/csp/onboarding/atos/upload',
    form,
    { headers: { 'Content-Type': 'multipart/form-data' } },
  );
  return unwrap(data);
}

/**
 * Returns the running tally for the ATO Documents step. Re-entrant — safe to
 * call on every step mount so the user sees up-to-date totals after a
 * mid-wizard refresh.
 */
export async function getCspOnboardingAtosState(): Promise<AtoStepState> {
  const { data } = await cspClient.get<Envelope<AtoStepState>>(
    '/csp/onboarding/atos/state',
  );
  return unwrap(data);
}
