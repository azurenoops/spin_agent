import apiClient from './client';
import type { SystemComponentDto, CreateComponentRequest, DeleteComponentResponse, DiscoverAzureRequest, DiscoveryResponse, ImportAzureResponse } from '../types/dashboard';

interface ComponentParams {
  type?: string;
  status?: string;
  search?: string;
  cursor?: string;
  pageSize?: number;
}

interface ComponentInventoryResponse {
  systemId: string;
  summary: { personCount: number; placeCount: number; thingCount: number; policyCount: number; totalCount: number };
  items: SystemComponentDto[];
  nextCursor: string | null;
  totalCount: number;
}

// ─── System-Scoped (existing) ────────────────────────────────────────────

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

// ─── Org-Wide Component Library (Feature 036) ────────────────────────────

export interface OrgComponentDto {
  id: string;
  name: string;
  componentType: string;
  subType?: string;
  description?: string;
  owner?: string;
  personName?: string;
  email?: string;
  status: string;
  createdAt: string;
  createdBy?: string;
  modifiedAt?: string;
  systemAssignments: SystemAssignmentDto[];
  capabilityLinks: { capabilityId: string; capabilityName: string }[];
}

export interface SystemAssignmentDto {
  id: string;
  registeredSystemId: string;
  systemName?: string;
  boundaryDefinitionId?: string;
  boundaryName?: string;
}

export interface OrgComponentListResponse {
  items: OrgComponentDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

interface OrgComponentParams {
  search?: string;
  type?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}

interface AssignComponentRequest {
  registeredSystemId: string;
  authorizationBoundaryDefinitionId?: string;
}

export async function listComponents(params?: OrgComponentParams) {
  const { data } = await apiClient.get<OrgComponentListResponse>('/components', { params });
  return data;
}

export async function getComponentById(id: string) {
  const { data } = await apiClient.get<OrgComponentDto>(`/components/${id}`);
  return data;
}

export async function createOrgComponent(request: CreateComponentRequest) {
  const { data } = await apiClient.post<OrgComponentDto>('/components', request);
  return data;
}

export async function updateOrgComponent(id: string, request: CreateComponentRequest) {
  const { data } = await apiClient.put<OrgComponentDto>(`/components/${id}`, request);
  return data;
}

export async function deleteOrgComponent(id: string) {
  const { data } = await apiClient.delete<DeleteComponentResponse>(`/components/${id}`);
  return data;
}

export async function assignToSystem(componentId: string, request: AssignComponentRequest) {
  const { data } = await apiClient.post<SystemAssignmentDto>(
    `/components/${componentId}/assignments`,
    request,
  );
  return data;
}

export async function removeAssignment(componentId: string, assignmentId: string) {
  await apiClient.delete(`/components/${componentId}/assignments/${assignmentId}`);
}

export interface ComponentImpactPreview {
  totalNarratives: number;
  totalSystems: number;
  customSkipped: number;
  bySystem: { systemId: string; systemName: string | null; narrativeCount: number; customSkipped: number }[];
}

export async function getComponentImpactPreview(id: string) {
  const { data } = await apiClient.get<ComponentImpactPreview>(
    `/components/${id}/impact-preview`,
  );
  return data;
}

// ─── System-Level Azure Discovery (Feature 040 — US2) ───────────────────

export interface ImportSystemAzureRequest {
  resources: { resourceId: string; name: string; type: string; resourceGroup: string; location: string }[];
  assignExistingOrgComponents?: string[];
}

export async function discoverSystemAzureResources(
  systemId: string,
  request: DiscoverAzureRequest,
): Promise<DiscoveryResponse> {
  const { data } = await apiClient.post<DiscoveryResponse>(
    `/systems/${systemId}/components/discover-azure`,
    request,
  );
  return data;
}

export async function importSystemAzureComponents(
  systemId: string,
  request: ImportSystemAzureRequest,
): Promise<ImportAzureResponse> {
  const { data } = await apiClient.post<ImportAzureResponse>(
    `/systems/${systemId}/components/import-azure`,
    request,
  );
  return data;
}

// ─── Component Risk Summary (Feature 040 US6) ──────────────────────────────

export async function getAssessmentComponentRisks(
  systemId: string,
  assessmentId: string,
): Promise<import('../types/dashboard').AssessmentComponentRisks> {
  const { data } = await apiClient.get<import('../types/dashboard').AssessmentComponentRisks>(
    `/systems/${systemId}/assessments/${assessmentId}/component-risks`,
  );
  return data;
}

export async function getAssessmentFindings(
  systemId: string,
  assessmentId: string,
  componentId?: string,
): Promise<{ items: unknown[]; totalCount: number }> {
  const params: Record<string, string> = {};
  if (componentId) params.componentId = componentId;
  const { data } = await apiClient.get<{ items: unknown[]; totalCount: number }>(
    `/systems/${systemId}/assessments/${assessmentId}/findings`,
    { params },
  );
  return data;
}

export async function resolveFindingComponents(
  systemId: string,
): Promise<{ linkedCount: number }> {
  const { data } = await apiClient.post<{ linkedCount: number }>(
    `/systems/${systemId}/resolve-finding-components`,
  );
  return data;
}

export async function relinkComponentFindings(
  systemId: string,
  componentId: string,
): Promise<{ linkedCount: number }> {
  const { data } = await apiClient.post<{ linkedCount: number }>(
    `/systems/${systemId}/components/${componentId}/relink-findings`,
  );
  return data;
}
