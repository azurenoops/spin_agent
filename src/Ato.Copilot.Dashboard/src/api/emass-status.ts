import apiClient from './client';

export type EmassWorkflowOverallStatus = 'NeverExported' | 'UpToDate' | 'PendingExport' | 'HasConflicts';
export type ReadinessGapSeverity = 'Blocking' | 'Advisory';
export type ConflictStatus = 'Unresolved' | 'KeepSpin' | 'AcceptEmass' | 'Deferred';

export interface EmassExportCategorySummary {
  category: string;
  exportedCount: number;
  pendingCount: number;
  lastExportedAt: string | null;
}

export interface EmassWorkflowStatus {
  systemId: string;
  overallStatus: EmassWorkflowOverallStatus;
  lastExportedAt: string | null;
  lastSyncedAt: string | null;
  unresolvedConflictCount: number;
  exportSummary: EmassExportCategorySummary[];
  readinessStatus: {
    isReady: boolean;
    blockingGapCount: number;
    advisoryGapCount: number;
  };
}

export interface ReadinessGap {
  fieldName: string;
  description: string;
  severity: ReadinessGapSeverity;
  fixUrl: string | null;
}

export interface EmassExportReadinessResult {
  systemId: string;
  isReady: boolean;
  gaps: ReadinessGap[];
  checkedAt: string;
}

export interface EmassConflict {
  id: string;
  entityType: string;
  entityId: string | null;
  fieldName: string;
  spinValue: string | null;
  emassValue: string | null;
  conflictStatus: ConflictStatus;
  detectedAt: string;
  resolvedAt: string | null;
  resolvedBy: string | null;
}

export interface EmassSyncResult {
  batchId: string;
  systemId: string;
  conflictsCreated: number;
  identicalFields: number;
  skippedUnresolved: number;
  syncedAt: string;
}

interface ApiEnvelope<T, TMeta = Record<string, never>> {
  data: T;
  meta: TMeta;
  errors: Array<{ code: string; message: string }>;
}

export interface ConflictPage {
  items: EmassConflict[];
  total: number;
  limit: number;
  offset: number;
}

export async function getEmassStatus(systemId: string): Promise<EmassWorkflowStatus> {
  const response = await apiClient.get<ApiEnvelope<EmassWorkflowStatus>>(`/systems/${systemId}/emass/status`);
  return response.data.data;
}

export async function getEmassReadiness(systemId: string): Promise<EmassExportReadinessResult> {
  const response = await apiClient.get<ApiEnvelope<EmassExportReadinessResult>>(`/systems/${systemId}/emass/readiness`);
  return response.data.data;
}

export async function uploadEmassSync(
  systemId: string,
  file: File,
  acknowledgeUnresolved = false,
): Promise<EmassSyncResult> {
  const form = new FormData();
  form.append('file', file);
  const response = await apiClient.post<ApiEnvelope<EmassSyncResult>>(
    `/systems/${systemId}/emass/sync`,
    form,
    {
      params: { acknowledgeUnresolved },
      headers: { 'Content-Type': 'multipart/form-data' },
    },
  );
  return response.data.data;
}

export async function getEmassConflicts(
  systemId: string,
  params: { status?: ConflictStatus | 'All'; batchId?: string; limit?: number; offset?: number } = {},
): Promise<ConflictPage> {
  const response = await apiClient.get<ApiEnvelope<EmassConflict[], { total: number; limit: number; offset: number }>>(
    `/systems/${systemId}/emass/conflicts`,
    { params },
  );
  return { items: response.data.data, ...response.data.meta };
}

export async function resolveConflict(
  systemId: string,
  conflictId: string,
  resolution: Exclude<ConflictStatus, 'Unresolved'>,
): Promise<EmassConflict> {
  const response = await apiClient.put<ApiEnvelope<EmassConflict>>(
    `/systems/${systemId}/emass/conflicts/${conflictId}`,
    { resolution },
  );
  return response.data.data;
}