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

export interface CapabilityImpactPreview {
  totalNarratives: number;
  totalSystems: number;
  customSkipped: number;
  bySystem: { systemId: string; systemName: string | null; narrativeCount: number; customSkipped: number }[];
}

export async function getCapabilityImpactPreview(id: string) {
  const { data } = await apiClient.get<CapabilityImpactPreview>(
    `/capabilities/${id}/impact-preview`,
  );
  return data;
}

export interface CapabilityCoverageResponse {
  systemId: string;
  systemName: string | null;
  capabilities: CapabilityCoverageDto[];
  summary: CoverageSummaryDto;
}

export interface CapabilityCoverageDto {
  capabilityId: string;
  capabilityName: string;
  provider: string;
  category: string;
  implementationStatus: string;
  owner: string | null;
  role: string;
  mappedControlCount: number;
  narrativeStatus: { populated: number; custom: number; empty: number; aiGenerated: number };
  components: CoverageComponentDto[];
}

export interface CoverageComponentDto {
  componentId: string;
  name: string;
  componentType: string;
  owner: string | null;
  status: string;
  boundaryName: string | null;
  boundaryDefinitionId: string | null;
}

export interface CoverageSummaryDto {
  totalCapabilities: number;
  totalMappedControls: number;
  totalNarrativesPopulated: number;
  totalNarrativesCustom: number;
  totalNarrativesEmpty: number;
  coveragePercent: number;
}

export async function getCapabilityCoverage(systemId: string) {
  const { data } = await apiClient.get<CapabilityCoverageResponse>(
    `/systems/${systemId}/capability-coverage`,
  );
  return data;
}

export interface BulkRegenerateResult {
  totalControls: number;
  regenerated: number;
  skippedCustom: number;
  failed: number;
  regeneratedControlIds: string[];
}

export async function bulkRegenerateNarratives(systemId: string, capabilityId: string) {
  const { data } = await apiClient.post<BulkRegenerateResult>(
    `/systems/${systemId}/capabilities/${capabilityId}/bulk-regenerate`,
  );
  return data;
}

// ─── Feature 045: Capabilities Hub Import/Coverage/Linking ──────────────────

import type {
  CspImportRequest,
  CspImportResult,
  CspImportPreview,
  CrmImportResult,
  CrmImportPreview,
  CrmColumnMapping,
  CoverageResponse,
  LinkComponentCapabilitiesRequest,
  LinkComponentCapabilitiesResponse,
} from '../types/capabilities';
import type { CspProfile, CspProfilesResponse } from '../types/inheritance';

export async function getCspProfiles(): Promise<CspProfile[]> {
  const { data } = await apiClient.get<CspProfilesResponse>('/capabilities/csp-profiles');
  return data.profiles;
}

export async function importCspProfile(request: CspImportRequest): Promise<CspImportResult | CspImportPreview> {
  const { data } = await apiClient.post<CspImportResult | CspImportPreview>(
    '/capabilities/import/csp-profile',
    request,
  );
  return data;
}

export async function importCrm(
  file: File,
  columnMapping: CrmColumnMapping,
  conflictResolution: string = 'skip',
  dryRun: boolean = false,
): Promise<CrmImportResult | CrmImportPreview> {
  const formData = new FormData();
  formData.append('file', file);
  formData.append('columnMapping', JSON.stringify(columnMapping));
  formData.append('conflictResolution', conflictResolution);
  formData.append('dryRun', String(dryRun));
  const { data } = await apiClient.post<CrmImportResult | CrmImportPreview>(
    '/capabilities/import/crm',
    formData,
    { headers: { 'Content-Type': 'multipart/form-data' } },
  );
  return data;
}

export async function getCoverage(
  includePerSystem: boolean = false,
  includePerFamily: boolean = true,
): Promise<CoverageResponse> {
  const { data } = await apiClient.get<CoverageResponse>('/capabilities/coverage', {
    params: { includePerSystem, includePerFamily },
  });
  return data;
}

export async function linkComponentCapabilities(
  componentId: string,
  request: LinkComponentCapabilitiesRequest,
): Promise<LinkComponentCapabilitiesResponse> {
  const { data } = await apiClient.post<LinkComponentCapabilitiesResponse>(
    `/components/${componentId}/capabilities`,
    request,
  );
  return data;
}

export async function unlinkComponentCapability(
  componentId: string,
  capabilityId: string,
): Promise<void> {
  await apiClient.delete(`/components/${componentId}/capabilities/${capabilityId}`);
}
