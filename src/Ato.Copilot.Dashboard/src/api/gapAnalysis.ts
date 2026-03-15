import apiClient from './client';
import type { GapAnalysisResponse } from '../types/dashboard';

export async function getGapAnalysis(systemId: string) {
  const { data } = await apiClient.get<GapAnalysisResponse>(`/systems/${systemId}/gaps`);
  return data;
}
