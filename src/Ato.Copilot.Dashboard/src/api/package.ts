import axios from 'axios';

// ─── V1 API Client ──────────────────────────────────────────────────────────

const v1Client = axios.create({
  baseURL: '/api/v1',
  headers: { 'Content-Type': 'application/json' },
});

v1Client.interceptors.request.use((config) => {
  const token = localStorage.getItem('auth_token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// ─── Types ──────────────────────────────────────────────────────────────────

export interface PackageSummary {
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
  packageId: string;
  status: string;
  message: string;
}

export interface ReadinessResult {
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
): Promise<GeneratePackageResponse> {
  const { data } = await v1Client.post<GeneratePackageResponse>(
    `/systems/${systemId}/packages`,
    { evidenceMode, includeEvidence: true },
  );
  return data;
}

export async function getPackageDetail(
  systemId: string,
  packageId: string,
): Promise<PackageDetail> {
  const { data } = await v1Client.get<PackageDetail>(
    `/systems/${systemId}/packages/${packageId}`,
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
): Promise<ReadinessResult> {
  const { data } = await v1Client.post<ReadinessResult>(
    `/systems/${systemId}/packages/validate`,
  );
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
