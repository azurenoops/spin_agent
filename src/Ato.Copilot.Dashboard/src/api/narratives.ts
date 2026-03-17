import apiClient from './client';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface NarrativeListItem {
  id: string;
  controlId: string;
  family: string;
  narrative: string | null;
  implementationStatus: string;
  approvalStatus: string;
  authoredBy: string | null;
  authoredAt: string | null;
  version: number;
  isAutoPopulated: boolean;
  aiSuggested: boolean;
}

export interface BulkNarrativeUpdateRequest {
  controlIds: string[];
  implementationStatus?: string;
  approvalStatus?: string;
  updatedBy?: string;
}

export interface BulkNarrativeUpdateResponse {
  updatedCount: number;
  controlIds: string[];
}

// ─── API ────────────────────────────────────────────────────────────────────

export async function getNarratives(
  systemId: string,
  params?: { family?: string; status?: string; search?: string },
): Promise<NarrativeListItem[]> {
  const { data } = await apiClient.get<NarrativeListItem[]>(
    `/systems/${encodeURIComponent(systemId)}/narratives`,
    { params },
  );
  return data;
}

export async function bulkUpdateNarratives(
  systemId: string,
  request: BulkNarrativeUpdateRequest,
): Promise<BulkNarrativeUpdateResponse> {
  const { data } = await apiClient.put<BulkNarrativeUpdateResponse>(
    `/systems/${encodeURIComponent(systemId)}/narratives/bulk-update`,
    request,
  );
  return data;
}
