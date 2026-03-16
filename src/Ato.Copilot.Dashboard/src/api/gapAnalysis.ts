import apiClient from './client';
import type { GapAnalysisResponse } from '../types/dashboard';

export async function getGapAnalysis(systemId: string, boundaryDefinitionId?: string) {
  const { data } = await apiClient.get<GapAnalysisResponse>(`/systems/${systemId}/gaps`, {
    params: boundaryDefinitionId ? { boundaryDefinitionId } : undefined,
  });
  return data;
}
