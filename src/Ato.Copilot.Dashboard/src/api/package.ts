import axios from 'axios';
import { attachAuthInterceptor } from '../features/auth/interceptors';
import { getMsalInstance, DEFAULT_API_SCOPES } from '../features/auth/msalInstance';

// ─── V1 API Client ──────────────────────────────────────────────────────────

const v1Client = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
});

// Feature 051 T053: MSAL bearer injection (silent renewal + 401 retry).
attachAuthInterceptor(v1Client, getMsalInstance, DEFAULT_API_SCOPES);

// ─── Types ──────────────────────────────────────────────────────────────────

export type PackagePurpose = 'Legacy' | 'InitialSubmission' | 'AuthorizedBaselineArchive' | 'ChangeSubmission';

export interface RetainedPackageSelection {
  baselinePackageId: string;
  baselineContentHash: string;
  authorizationDecisionId: string;
  expectedDecisionSnapshotHash?: string;
  expectedSourceContextHash?: string;
  changePreviewId?: string;
  changeContentHash?: string;
}

export interface PackageContextOptions {
  baselines: { id: string; purpose: string; generatedAt: string; contentHash: string }[];
  decisions: { id: string; decisionType: string; decisionDate: string; issuer: string; snapshotHash: string }[];
  previews: { id: string; generatedAt: string; contentHash: string }[];
}

export async function getPackageContextOptions(systemId: string, signal?: AbortSignal): Promise<PackageContextOptions> {
  const { data } = await v1Client.get<PackageContextOptions>(`/systems/${encodeURIComponent(systemId)}/packages/context-options`, { signal });
  if (!data || !Array.isArray(data.baselines) || !Array.isArray(data.decisions) || !Array.isArray(data.previews)) {
    throw new Error('Retained package context options are unavailable from this server.');
  }
  if (data.baselines.some(item => !item || typeof item.id !== 'string' || typeof item.contentHash !== 'string'
    || typeof item.purpose !== 'string' || typeof item.generatedAt !== 'string')
    || data.decisions.some(item => !item || typeof item.id !== 'string' || typeof item.snapshotHash !== 'string'
      || typeof item.decisionType !== 'string' || typeof item.decisionDate !== 'string' || typeof item.issuer !== 'string')
    || data.previews.some(item => !item || typeof item.id !== 'string' || typeof item.contentHash !== 'string'
      || typeof item.generatedAt !== 'string')) {
    throw new Error('Retained context options are missing source identities or hashes.');
  }
  return data;
}

function requireConfirmedPurpose(response: { purpose?: PackagePurpose } | null | undefined, requested?: PackagePurpose) {
  if (requested && requested !== 'Legacy' && response?.purpose !== requested) {
    throw new Error('The server did not confirm the requested package purpose. Refresh history before retrying generation and verify the API version.');
  }
}

export interface PackageSummary {
  purpose?: PackagePurpose;
  packageId: string;
  status: string;
  artifactCount: number;
  validationPassed: boolean | null;
  validationErrorCount: number;
  validationWarningCount: number;
  fileSize: number | null;
  generatedBy: string;
  generatedAt: string;
  completedAt: string | null;
  expiresAt: string;
}

export interface PackageArtifact {
  artifactId: string;
  type: string;
  format: string;
  fileName: string;
  fileSize: number | null;
  oscalVersion: string | null;
  schemaValid: boolean | null;
  generatedAt: string;
}

export interface ValidationFinding {
  severity: string;
  category: string;
  artifactType: string | null;
  description: string;
  remediation: string | null;
}

export interface PackageValidation {
  isValid: boolean;
  errorCount: number;
  warningCount: number;
  findings: ValidationFinding[];
}

export interface PackageDetail {
  sourceContextHash?: string | null;
  purpose?: PackagePurpose;
  packageId: string;
  systemId: string;
  status: string;
  evidenceMode: string;
  artifacts: PackageArtifact[];
  validation: PackageValidation | null;
  fileSize: number | null;
  failureReason: string | null;
  failedArtifactType: string | null;
  generatedBy: string;
  generatedAt: string;
  completedAt: string | null;
  expiresAt: string;
}

export interface PackageListResponse {
  items: PackageSummary[];
  totalCount: number;
  limit: number;
  offset: number;
}

export interface GeneratePackageResponse {
  sourceContextHash?: string | null;
  purpose?: PackagePurpose;
  packageId: string;
  status: string;
  message: string;
}

export interface ReadinessResult {
  sourceContextHash?: string | null;
  purpose?: PackagePurpose;
  isValid: boolean;
  errorCount: number;
  warningCount: number;
  validatedAt: string;
  findings: ValidationFinding[];
}

// ─── SAR Types ──────────────────────────────────────────────────────────────

export interface SarSummary {
  sarId: string;
  systemId: string;
  title: string;
  status: string;
  totalControlsAssessed: number;
  totalControlsPending: number;
  satisfiedCount: number;
  notSatisfiedCount: number;
  createdBy: string;
  createdAt: string;
}

export interface SarExportResponse {
  blob: Blob;
  filename: string;
}

// ─── Package API Functions ──────────────────────────────────────────────────

export async function generatePackage(
  systemId: string,
  evidenceMode: 'Embedded' | 'ManifestOnly' = 'Embedded',
  signal?: AbortSignal,
  purpose?: PackagePurpose,
  retainedContext?: RetainedPackageSelection,
): Promise<GeneratePackageResponse> {
  const { data } = await v1Client.post<GeneratePackageResponse>(
    `/systems/${systemId}/packages`,
    { evidenceMode, includeEvidence: true, ...(purpose ? { purpose } : {}), ...(retainedContext ? { retainedContext } : {}) },
    { signal },
  );
  requireConfirmedPurpose(data, purpose);
  return data;
}

export async function getPackageDetail(
  systemId: string,
  packageId: string,
  signal?: AbortSignal,
): Promise<PackageDetail> {
  const { data } = await v1Client.get<PackageDetail>(
    `/systems/${systemId}/packages/${packageId}`,
    { signal },
  );
  return data;
}

export async function listPackages(
  systemId: string,
  options?: { limit?: number; offset?: number; includeFailed?: boolean },
): Promise<PackageListResponse> {
  const { data } = await v1Client.get<PackageListResponse>(
    `/systems/${systemId}/packages`,
    { params: options },
  );
  return data;
}

export function downloadPackageUrl(systemId: string, packageId: string): string {
  return `/api/v1/systems/${systemId}/packages/${packageId}/download`;
}

export async function validatePackage(
  systemId: string,
  signal?: AbortSignal,
  purpose?: PackagePurpose,
  retainedContext?: RetainedPackageSelection,
): Promise<ReadinessResult> {
  const { data } = await v1Client.post<ReadinessResult>(
    `/systems/${systemId}/packages/validate`,
    retainedContext,
    { signal, ...(purpose ? { params: { purpose } } : {}) },
  );
  requireConfirmedPurpose(data, purpose);
  return data;
}

// ─── SAR API Functions ──────────────────────────────────────────────────────

export async function createSar(
  systemId: string,
  title: string,
): Promise<SarSummary> {
  const { data } = await v1Client.post<SarSummary>(
    `/systems/${systemId}/sar`,
    { title },
  );
  return data;
}

export async function exportSar(
  systemId: string,
  sarId: string,
): Promise<string> {
  return `/api/v1/systems/${systemId}/sar/${sarId}/export`;
}
