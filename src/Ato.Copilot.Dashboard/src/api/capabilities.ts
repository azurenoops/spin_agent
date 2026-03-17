import apiClient from './client';
import type {
  PaginatedResponse,
  SecurityCapabilityDto,
  CreateCapabilityRequest,
  UpdateCapabilityResponse,
  CapabilityMappingDto,
  CreateMappingsRequest,
  CreateMappingsResponse,
} from '../types/dashboard';

interface CapabilityParams {
  search?: string;
  category?: string;
  status?: string;
  cursor?: string;
  pageSize?: number;
}

export async function getCapabilities(params?: CapabilityParams) {
  const { data } = await apiClient.get<PaginatedResponse<SecurityCapabilityDto>>(
    '/capabilities',
    { params },
  );
  return data;
}

export async function getCapability(id: string) {
  const { data } = await apiClient.get<SecurityCapabilityDto>(`/capabilities/${id}`);
  return data;
}

export async function createCapability(request: CreateCapabilityRequest) {
  const { data } = await apiClient.post<SecurityCapabilityDto>('/capabilities', request);
  return data;
}

export async function updateCapability(id: string, request: CreateCapabilityRequest) {
  const { data } = await apiClient.put<UpdateCapabilityResponse>(
    `/capabilities/${id}`,
    request,
  );
  return data;
}

export async function deleteCapability(id: string) {
  const { data } = await apiClient.delete<{ deletedId: string; affectedNarratives: number; message: string }>(
    `/capabilities/${id}`,
  );
  return data;
}

export async function getCapabilityMappings(id: string) {
  const { data } = await apiClient.get<{
    capabilityId: string;
    capabilityName: string;
    mappings: CapabilityMappingDto[];
    totalMappings: number;
  }>(`/capabilities/${id}/mappings`);
  return data;
}

export async function createCapabilityMappings(id: string, request: CreateMappingsRequest) {
  const { data } = await apiClient.post<CreateMappingsResponse>(
    `/capabilities/${id}/mappings`,
    request,
  );
  return data;
}

export async function generateCapabilityDescription(
  name: string,
  provider: string,
  category?: string,
): Promise<string> {
  const { data } = await apiClient.post<{ description: string }>('/ai/capability-description', {
    name,
    provider,
    category: category || undefined,
  });
  return data.description;
}
