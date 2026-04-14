import apiClient from './client';
import type {
  BusinessContextDraftResponse,
  FlaggedControlItem,
} from '../types/dashboard';

// ─── Business Context Drafts ────────────────────────────────────────────────

export async function getBusinessContext(
  systemId: string,
  controlId: string,
): Promise<BusinessContextDraftResponse | null> {
  const { data } = await apiClient.get<BusinessContextDraftResponse | null>(
    `/systems/${encodeURIComponent(systemId)}/business-context/${encodeURIComponent(controlId)}`,
  );
  return data;
}

export async function saveBusinessContext(
  systemId: string,
  controlId: string,
  content: string,
): Promise<BusinessContextDraftResponse> {
  const { data } = await apiClient.put<BusinessContextDraftResponse>(
    `/systems/${encodeURIComponent(systemId)}/business-context/${encodeURIComponent(controlId)}`,
    { content },
  );
  return data;
}

// ─── Flagged Controls ───────────────────────────────────────────────────────

export async function getFlaggedControls(
  systemId: string,
): Promise<FlaggedControlItem[]> {
  const { data } = await apiClient.get<FlaggedControlItem[]>(
    `/systems/${encodeURIComponent(systemId)}/business-context/flagged-controls`,
  );
  return data;
}

export async function setControlFlag(
  systemId: string,
  controlId: string,
  isFlagged: boolean,
): Promise<void> {
  await apiClient.post(
    `/systems/${encodeURIComponent(systemId)}/business-context/flags`,
    { controlId, isFlagged },
  );
}
