import axios from 'axios';

/**
 * Feature 048 / US4 — Tenant onboarding wizard API client.
 *
 * Backed by the seven endpoints defined in
 * `specs/048-tenant-isolation/contracts/tenant-onboarding.openapi.yaml`.
 * Returns the inner `data` payload for each call (the server already
 * standardizes responses with the shared `{ status, data, metadata }`
 * envelope).
 */

const tenantApi = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL_ONBOARDING_TENANT || '/api/onboarding/tenant',
  headers: { 'Content-Type': 'application/json' },
});

tenantApi.interceptors.request.use((config) => {
  const token = localStorage.getItem('auth_token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

export type TenantWizardStep =
  | 'Tenant.LegalEntity'
  | 'Tenant.HqAddress'
  | 'Tenant.Classification'
  | 'Tenant.Ao'
  | 'Tenant.PrimaryPoc'
  | 'Org.Profile'
  | 'Submitted';

export type TenantOnboardingState = 'Pending' | 'InWizard' | 'Active';

export interface TenantOnboardingProgress {
  tenantId: string;
  currentStep: TenantWizardStep;
  completedSteps: TenantWizardStep[];
  onboardingState: TenantOnboardingState;
  firstOrganizationId: string | null;
}

export interface LegalEntityRequest {
  legalEntityName: string;
  doDComponent?: string | null;
  timeZone?: string | null;
}

export interface HqAddressRequest {
  hqAddressLine1: string;
  hqAddressLine2?: string | null;
  hqCity: string;
  hqStateOrProvince: string;
  hqPostalCode: string;
  hqCountry: string;
}

export type ClassificationLevel = 'Unclassified' | 'CUI' | 'Secret';

export interface ClassificationRequest {
  defaultClassificationLevel: ClassificationLevel;
}

export interface AoRequest {
  authorizingOfficialName: string;
  authorizingOfficialEmail: string;
}

export interface PrimaryPocRequest {
  primaryPocName: string;
  primaryPocEmail: string;
  primaryPocPhone?: string | null;
}

export interface OrgProfileRequest {
  name: string;
  description?: string | null;
}

interface Envelope<T> {
  status: 'success' | 'error';
  data?: T;
  error?: { errorCode: string; message: string };
}

async function unwrap<T>(promise: Promise<{ data: Envelope<T> }>): Promise<T> {
  const resp = await promise;
  if (resp.data.status !== 'success' || !resp.data.data) {
    const err = resp.data.error;
    const e = new Error(err?.message ?? 'Tenant wizard request failed.');
    (e as Error & { errorCode?: string }).errorCode = err?.errorCode;
    throw e;
  }
  return resp.data.data;
}

export const tenantWizard = {
  getState: () => unwrap<TenantOnboardingProgress>(tenantApi.get('/state')),
  submitLegalEntity: (req: LegalEntityRequest) =>
    unwrap<TenantOnboardingProgress>(tenantApi.post('/legal-entity', req)),
  submitHqAddress: (req: HqAddressRequest) =>
    unwrap<TenantOnboardingProgress>(tenantApi.post('/hq-address', req)),
  submitClassification: (req: ClassificationRequest) =>
    unwrap<TenantOnboardingProgress>(tenantApi.post('/classification', req)),
  submitAo: (req: AoRequest) => unwrap<TenantOnboardingProgress>(tenantApi.post('/ao', req)),
  submitPrimaryPoc: (req: PrimaryPocRequest) =>
    unwrap<TenantOnboardingProgress>(tenantApi.post('/primary-poc', req)),
  submitOrgProfile: (req: OrgProfileRequest) =>
    unwrap<TenantOnboardingProgress>(tenantApi.post('/org-profile', req)),
  submitFinal: () => unwrap<TenantOnboardingProgress>(tenantApi.post('/submit', {})),
};
