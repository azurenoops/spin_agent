import apiClient from './client';
import type { SystemComponentDto, CreateComponentRequest, DeleteComponentResponse } from '../types/dashboard';

interface ComponentParams {
  type?: string;
  status?: string;
  search?: string;
  cursor?: string;
  pageSize?: number;
}

interface ComponentInventoryResponse {
  systemId: string;
  summary: { personCount: number; placeCount: number; thingCount: number; totalCount: number };
  items: SystemComponentDto[];
  nextCursor: string | null;
  totalCount: number;
}

export async function getComponents(systemId: string, params?: ComponentParams) {
  const { data } = await apiClient.get<ComponentInventoryResponse>(
    `/systems/${systemId}/components`,
    { params },
  );
  return data;
}

export async function createComponent(systemId: string, request: CreateComponentRequest) {
  const { data } = await apiClient.post<SystemComponentDto>(
    `/systems/${systemId}/components`,
    request,
  );
  return data;
}

export async function updateComponent(id: string, request: CreateComponentRequest) {
  const { data } = await apiClient.put<SystemComponentDto>(`/components/${id}`, request);
  return data;
}

export async function deleteComponent(id: string): Promise<DeleteComponentResponse> {
  const { data } = await apiClient.delete<DeleteComponentResponse>(`/components/${id}`);
  return data;
}

export async function generateComponentDescription(
  name: string,
  componentType: string,
  subType?: string,
): Promise<string> {
  const { data } = await apiClient.post<{ description: string }>('/ai/component-description', {
    name,
    componentType,
    subType: subType || undefined,
  });
  return data.description;
}
