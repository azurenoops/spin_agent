import apiClient from './client';
import type { Roadmap, RoadmapProgress } from '../types/dashboard';

export async function fetchRoadmap(
  systemId: string,
  includeItems = true,
): Promise<Roadmap> {
  const { data } = await apiClient.get<Roadmap>(
    `/systems/${systemId}/roadmap`,
    { params: { includeItems } },
  );
  return data;
}

export async function fetchRoadmapProgress(
  systemId: string,
): Promise<RoadmapProgress> {
  const { data } = await apiClient.get<RoadmapProgress>(
    `/systems/${systemId}/roadmap/progress`,
  );
  return data;
}
