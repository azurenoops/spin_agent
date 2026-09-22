import { downloadPackageUrl, generatePackage, getPackageDetail, type PackageDetail } from './package';

export interface PackageJob {
  packageId: string;
  status: string;
  message: string;
}


/** Enqueue a package generation job. Returns packageId + status. */
export async function enqueuePackage(
  systemId: string,
  evidenceMode: 'inline' | 'linked' | 'full' = 'inline',
  signal?: AbortSignal,
): Promise<PackageJob> {
  // Legacy shortcut names map to the two evidence modes accepted by the v1 API.
  return generatePackage(systemId, evidenceMode === 'linked' ? 'ManifestOnly' : 'Embedded', signal);
}

/** Poll package status. */
export async function getPackageStatus(
  systemId: string,
  packageId: string,
  signal?: AbortSignal,
): Promise<PackageDetail> {
  return getPackageDetail(systemId, packageId, signal);
}

/** Trigger a direct PDF download by enqueuing then downloading when complete. */
export function getPackageDownloadUrl(systemId: string, packageId: string): string {
  return downloadPackageUrl(systemId, packageId);
}

/** Enqueue and return the download URL for an eMASS-formatted XLSX export. */
export async function enqueueEmassExport(systemId: string): Promise<PackageJob> {
  return enqueuePackage(systemId, 'full');
}
