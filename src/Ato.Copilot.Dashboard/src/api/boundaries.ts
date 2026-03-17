import apiClient from './client';
import type {
  BoundaryDefinitionDto,
  CreateBoundaryDefinitionRequest,
  DeleteBoundaryDefinitionResponse,
} from '../types/dashboard';

export interface BoundaryResourceDto {
  id: string;
  resourceId: string;
  resourceType: string;
  resourceName: string | null;
  isInBoundary: boolean;
  exclusionRationale: string | null;
  inheritanceProvider: string | null;
}

export interface AddBoundaryResourceBody {
  resourceId: string;
  resourceType: string;
  resourceName?: string;
  inheritanceProvider?: string;
}

interface BoundaryListResponse {
  items: BoundaryDefinitionDto[];
  totalCount: number;
}

export async function fetchBoundaryDefinitions(
  systemId: string,
): Promise<BoundaryDefinitionDto[]> {
  const { data } = await apiClient.get<BoundaryListResponse>(
    `/systems/${systemId}/boundary-definitions`,
  );
  return data.items;
}

export async function createBoundaryDefinition(
  systemId: string,
  request: CreateBoundaryDefinitionRequest,
): Promise<BoundaryDefinitionDto> {
  const { data } = await apiClient.post<BoundaryDefinitionDto>(
    `/systems/${systemId}/boundary-definitions`,
    request,
  );
  return data;
}

export async function updateBoundaryDefinition(
  id: string,
  request: CreateBoundaryDefinitionRequest,
): Promise<BoundaryDefinitionDto> {
  const { data } = await apiClient.put<BoundaryDefinitionDto>(
    `/boundary-definitions/${id}`,
    request,
  );
  return data;
}

export async function deleteBoundaryDefinition(
  id: string,
): Promise<DeleteBoundaryDefinitionResponse> {
  const { data } = await apiClient.delete<DeleteBoundaryDefinitionResponse>(
    `/boundary-definitions/${id}`,
  );
  return data;
}

export async function fetchBoundaryResources(
  definitionId: string,
): Promise<BoundaryResourceDto[]> {
  const { data } = await apiClient.get<{ items: BoundaryResourceDto[]; totalCount: number }>(
    `/boundary-definitions/${definitionId}/resources`,
  );
  return data.items;
}

export async function addBoundaryResource(
  definitionId: string,
  body: AddBoundaryResourceBody,
): Promise<void> {
  await apiClient.post(`/boundary-definitions/${definitionId}/resources`, body);
}

export async function deleteBoundaryResource(
  definitionId: string,
  resourceEntryId: string,
): Promise<void> {
  await apiClient.delete(`/boundary-definitions/${definitionId}/resources/${resourceEntryId}`);
}
