import apiClient from '../api/client';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ScanUploadResponse {
  importJobId: string;
  statusUrl: string;
  detectedFileType: string;
  fileName: string;
  fileSizeBytes: number;
  resultId?: string;
  message?: string;
}

export interface ScanImportStatusDto {
  id: string;
  status: 'Queued' | 'Processing' | 'Completed' | 'CompletedWithWarnings' | 'Failed' | 'Cancelled';
  processedCount: number;
  totalCount: number;
  errorMessage: string | null;
  cancelRequested: boolean;
  resultId?: string | null;
  warnings?: string[];
}

// ─── API calls ────────────────────────────────────────────────────────────────

/**
 * Upload a SCAP/STIG scan file for a system.
 * Returns immediately with a job ID — use ScanImportProgressBar to track progress.
 */
export async function uploadScan(
  systemId: string,
  file: File,
  capture?: { planId?: string | null; expectedPlanHash?: string | null; requestId: string },
): Promise<ScanUploadResponse> {
  const form = new FormData();
  form.append('file', file);
  if (capture?.planId) form.append('planId', capture.planId);
  if (capture?.expectedPlanHash) form.append('expectedPlanHash', capture.expectedPlanHash);
  if (capture?.requestId) form.append('requestId', capture.requestId);

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
  signal?: AbortSignal,
): Promise<ScanImportStatusDto> {
  const res = await apiClient.get<ScanImportStatusDto>(
    `/systems/${systemId}/scans/import/${importJobId}/status`,
    { signal },
  );
  return res.data;
}

/**
 * Request cancellation of an in-progress import job.
 */
export async function cancelScanImport(
  systemId: string,
  importJobId: string,
  signal?: AbortSignal,
): Promise<void> {
  await apiClient.delete(`/systems/${systemId}/scans/import/${importJobId}`, { signal });
}
