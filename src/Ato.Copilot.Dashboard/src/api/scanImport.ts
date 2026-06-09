import apiClient from '../api/client';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ScanUploadResponse {
  importJobId: string;
  statusUrl: string;
  detectedFileType: string;
  fileName: string;
  fileSizeBytes: number;
}

export interface ScanImportStatusDto {
  id: string;
  status: 'Queued' | 'Processing' | 'Completed' | 'Failed' | 'Cancelled';
  processedCount: number;
  totalCount: number;
  errorMessage: string | null;
  cancelRequested: boolean;
}

// ─── API calls ────────────────────────────────────────────────────────────────

/**
 * Upload a SCAP/STIG scan file for a system.
 * Returns immediately with a job ID — use ScanImportProgressBar to track progress.
 */
export async function uploadScan(
  systemId: string,
  file: File,
): Promise<ScanUploadResponse> {
  const form = new FormData();
  form.append('file', file);

  const res = await apiClient.post<ScanUploadResponse>(
    `/systems/${systemId}/scans/import`,
    form,
    { headers: { 'Content-Type': 'multipart/form-data' } },
  );
  return res.data;
}

/**
 * Poll the status of an in-progress import job.
 * Used as a fallback when SignalR is unavailable.
 */
export async function getScanImportStatus(
  systemId: string,
  importJobId: string,
): Promise<ScanImportStatusDto> {
  const res = await apiClient.get<ScanImportStatusDto>(
    `/systems/${systemId}/scans/import/${importJobId}/status`,
  );
  return res.data;
}

/**
 * Request cancellation of an in-progress import job.
 */
export async function cancelScanImport(
  systemId: string,
  importJobId: string,
): Promise<void> {
  await apiClient.delete(`/systems/${systemId}/scans/import/${importJobId}`);
}
