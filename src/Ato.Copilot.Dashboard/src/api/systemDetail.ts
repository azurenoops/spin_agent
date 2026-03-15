import apiClient from './client';
import type {
  SystemDetailResponse,
  HeatmapResponse,
  HeatmapControlsResponse,
} from '../types/dashboard';

export async function getSystemDetail(systemId: string): Promise<SystemDetailResponse> {
  const { data } = await apiClient.get<SystemDetailResponse>(`/systems/${systemId}`);
  return data;
}

export async function getHeatmap(systemId: string): Promise<HeatmapResponse> {
  const { data } = await apiClient.get<HeatmapResponse>(`/systems/${systemId}/heatmap`);
  return data;
}

export async function getHeatmapControls(
  systemId: string,
  familyCode: string,
): Promise<HeatmapControlsResponse> {
  const { data } = await apiClient.get<HeatmapControlsResponse>(
    `/systems/${systemId}/heatmap/${familyCode}/controls`,
  );
  return data;
}
