import apiClient from './client';

export interface PackageJob {
  packageId: string;
  status: string;
  message: string;
}

export interface PackageDetail {
  packageId: string;
  systemId: string;
  status: string;
  evidenceMode: string;
  fileSize?: number;
  generatedAt?: string;
  completedAt?: string;
  expiresAt?: string;
  failureReason?: string;
}

/** Enqueue a package generation job. Returns packageId + status. */
export async function enqueuePackage(
  systemId: string,
  evidenceMode: 'inline' | 'linked' | 'full' = 'inline'
): Promise<PackageJob> {
  const res = await apiClient.post<PackageJob>(
    `/api/v1/systems/${systemId}/packages`,
    { evidenceMode }
  );
  return res.data;
}

/** Poll package status. */
export async function getPackageStatus(
  systemId: string,
  packageId: string
): Promise<PackageDetail> {
  const res = await apiClient.get<PackageDetail>(
    `/api/v1/systems/${systemId}/packages/${packageId}`
  );
  return res.data;
}

/** Trigger a direct PDF download by enqueuing then downloading when complete. */
export function getPackageDownloadUrl(systemId: string, packageId: string): string {
  return `/api/v1/systems/${systemId}/packages/${packageId}/download`;
}

/** Enqueue and return the download URL for an eMASS-formatted XLSX export. */
export async function enqueueEmassExport(systemId: string): Promise<PackageJob> {
  return enqueuePackage(systemId, 'full');
}
