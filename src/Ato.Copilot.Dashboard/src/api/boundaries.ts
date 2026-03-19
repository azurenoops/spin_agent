import apiClient from './client';
import type {
  BoundaryDefinitionDto,
  CreateBoundaryDefinitionRequest,
  DeleteBoundaryDefinitionResponse,
  BoundaryComponentDto,
  AssignComponentRequest,
  UpdateAssignmentRequest,
  BoundaryLockStatus,
} from '../types/dashboard';
import type { OrgComponentDto } from './components';

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

// ─── Boundary Components ─────────────────────────────────────────────────

export async function fetchBoundaryComponents(
  definitionId: string,
): Promise<OrgComponentDto[]> {
  const { data } = await apiClient.get<{ items: OrgComponentDto[]; totalCount: number }>(
    `/boundary-definitions/${definitionId}/components`,
  );
  return data.items;
}

export async function assignComponentToBoundary(
  componentId: string,
  registeredSystemId: string,
  authorizationBoundaryDefinitionId: string,
): Promise<void> {
  await apiClient.post(`/components/${componentId}/assignments`, {
    registeredSystemId,
    authorizationBoundaryDefinitionId,
  });
}

export async function removeComponentFromBoundary(
  componentId: string,
  assignmentId: string,
): Promise<void> {
  await apiClient.delete(`/components/${componentId}/assignments/${assignmentId}`);
}

// ─── Boundary Component Assignment API (Feature 040 — US3) ──────────────

export interface BoundaryComponentListResponse {
  items: BoundaryComponentDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export async function listBoundaryComponents(
  systemId: string,
  boundaryId: string,
  params?: { search?: string; type?: string; scope?: string; page?: number; pageSize?: number },
): Promise<BoundaryComponentListResponse> {
  const { data } = await apiClient.get<BoundaryComponentListResponse>(
    `/systems/${systemId}/boundary-definitions/${boundaryId}/components`,
    { params },
  );
  return data;
}

export async function assignComponent(
  systemId: string,
  boundaryId: string,
  request: AssignComponentRequest,
): Promise<BoundaryComponentDto> {
  const { data } = await apiClient.post<BoundaryComponentDto>(
    `/systems/${systemId}/boundary-definitions/${boundaryId}/components`,
    request,
  );
  return data;
}

export async function updateAssignment(
  systemId: string,
  boundaryId: string,
  assignmentId: string,
  request: UpdateAssignmentRequest,
): Promise<BoundaryComponentDto> {
  const { data } = await apiClient.put<BoundaryComponentDto>(
    `/systems/${systemId}/boundary-definitions/${boundaryId}/components/${assignmentId}`,
    request,
  );
  return data;
}

export async function removeAssignment(
  systemId: string,
  boundaryId: string,
  assignmentId: string,
): Promise<void> {
  await apiClient.delete(
    `/systems/${systemId}/boundary-definitions/${boundaryId}/components/${assignmentId}`,
  );
}

export async function acquireLock(
  systemId: string,
  boundaryId: string,
  userId: string,
  userDisplayName: string,
): Promise<BoundaryLockStatus> {
  const { data } = await apiClient.post<BoundaryLockStatus>(
    `/systems/${systemId}/boundary-definitions/${boundaryId}/lock`,
    { userId, userDisplayName },
  );
  return data;
}

export async function releaseLock(
  systemId: string,
  boundaryId: string,
): Promise<void> {
  await apiClient.delete(
    `/systems/${systemId}/boundary-definitions/${boundaryId}/lock`,
  );
}

export async function checkLockStatus(
  systemId: string,
  boundaryId: string,
): Promise<BoundaryLockStatus> {
  const { data } = await apiClient.get<BoundaryLockStatus>(
    `/systems/${systemId}/boundary-definitions/${boundaryId}/lock`,
  );
  return data;
}
