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

export async function saveNarrative(
  systemId: string,
  controlId: string,
  narrative: string,
): Promise<void> {
  await apiClient.patch(
    `/systems/${encodeURIComponent(systemId)}/controls/${encodeURIComponent(controlId)}/narrative`,
    { narrative },
  );
}

export async function regenerateNarrative(
  systemId: string,
  controlId: string,
  options?: { sourceUrls?: string[] },
): Promise<string | null> {
  const params: Record<string, string> = {};
  if (options?.sourceUrls && options.sourceUrls.length > 0) {
    params.sourceUrls = JSON.stringify(options.sourceUrls);
  }

  const { data } = await apiClient.post<{ narrative: string }>(
    `/systems/${encodeURIComponent(systemId)}/controls/${encodeURIComponent(controlId)}/regenerate-ai`,
    {},
    { params },
  );
  return data.narrative;
}

export interface AvailableControl {
  id: string;
  family: string;
  title: string;
}

export async function getAvailableControls(
  systemId: string,
  search?: string,
): Promise<AvailableControl[]> {
  const params: Record<string, string> = {};
  if (search) params.search = search;
  const { data } = await apiClient.get<AvailableControl[]>(
    `/systems/${encodeURIComponent(systemId)}/available-controls`,
    { params },
  );
  return data;
}

export interface CreateNarrativeRequest {
  controlId: string;
  narrative?: string;
  implementationStatus?: string;
}

export async function createNarrative(
  systemId: string,
  request: CreateNarrativeRequest,
): Promise<NarrativeListItem> {
  const { data } = await apiClient.post<NarrativeListItem>(
    `/systems/${encodeURIComponent(systemId)}/narratives`,
    request,
  );
  return data;
}
