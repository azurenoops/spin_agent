import apiClient from './client';
import type {
  InheritanceListResponse,
  InheritanceListQuery,
  SetInheritanceRequest,
  SetInheritanceResponse,
  CrmResult,
  CrmExportFormat,
  CrmExportLayout,
  AuditHistoryResponse,
  CspProfilesResponse,
  ApplyProfileRequest,
  ApplyProfilePreview,
  ApplyProfileResult,
  ImportPreview,
  ImportApplyRequest,
  ImportApplyResult,
  OrgDefaultsQuery,
  OrgDefaultsListResult,
  OrgDerivationResult,
  RevertResult,
} from '../types/inheritance';

// ─── Phase 1: Core CRUD + CRM ──────────────────────────────────────────────

export async function listInheritance(
  systemId: string,
  query?: InheritanceListQuery,
): Promise<InheritanceListResponse> {
  const params: Record<string, string | number> = {};
  if (query?.family) params.family = query.family;
  if (query?.inheritanceType) params.inheritanceType = query.inheritanceType;
  if (query?.search) params.search = query.search;
  if (query?.page) params.page = query.page;
  if (query?.pageSize) params.pageSize = query.pageSize;
  if (query?.source) params.source = query.source;
  if (query?.sortBy) params.sortBy = query.sortBy;
  if (query?.sortDirection) params.sortDirection = query.sortDirection;

  const { data } = await apiClient.get<InheritanceListResponse>(
    `/systems/${systemId}/inheritance`,
    { params },
  );
  return data;
}

export async function setInheritance(
  systemId: string,
  request: SetInheritanceRequest,
): Promise<SetInheritanceResponse> {
  const { data } = await apiClient.put<SetInheritanceResponse>(
    `/systems/${systemId}/inheritance`,
    request,
  );
  return data;
}

export async function getCrm(systemId: string): Promise<CrmResult> {
  const { data } = await apiClient.get<CrmResult>(
    `/systems/${systemId}/inheritance/crm`,
  );
  return data;
}

export async function exportCrm(
  systemId: string,
  format: CrmExportFormat,
  layout: CrmExportLayout = 'custom',
): Promise<Blob> {
  const { data } = await apiClient.get(
    `/systems/${systemId}/inheritance/crm/export`,
    {
      params: { format, layout },
      responseType: 'blob',
    },
  );
  return data;
}

export async function getAudit(
  systemId: string,
  controlId: string,
): Promise<AuditHistoryResponse> {
  const { data } = await apiClient.get<AuditHistoryResponse>(
    `/systems/${systemId}/inheritance/${controlId}/audit`,
  );
  return data;
}

// ─── Phase 2: CSP Profiles ─────────────────────────────────────────────────

export async function getProfiles(
  systemId: string,
): Promise<CspProfilesResponse> {
  const { data } = await apiClient.get<CspProfilesResponse>(
    `/systems/${systemId}/inheritance/csp-profiles`,
  );
  return data;
}

export async function applyProfile(
  systemId: string,
  request: ApplyProfileRequest,
): Promise<ApplyProfilePreview | ApplyProfileResult> {
  const { data } = await apiClient.post<ApplyProfilePreview | ApplyProfileResult>(
    `/systems/${systemId}/inheritance/apply-profile`,
    request,
  );
  return data;
}

// ─── Phase 3: CRM Import ───────────────────────────────────────────────────

export async function importPreview(
  systemId: string,
  file: File,
): Promise<ImportPreview> {
  const formData = new FormData();
  formData.append('file', file);

  const { data } = await apiClient.post<ImportPreview>(
    `/systems/${systemId}/inheritance/import/preview`,
    formData,
    { headers: { 'Content-Type': 'multipart/form-data' } },
  );
  return data;
}

export async function importApply(
  systemId: string,
  request: ImportApplyRequest,
): Promise<ImportApplyResult> {
  const { data } = await apiClient.post<ImportApplyResult>(
    `/systems/${systemId}/inheritance/import/apply`,
    request,
  );
  return data;
}

// ─── Phase 4: Org-Level Inheritance Defaults ────────────────────────────────

export async function getOrgDefaults(
  query?: OrgDefaultsQuery,
): Promise<OrgDefaultsListResult> {
  const params: Record<string, string | number> = {};
  if (query?.family) params.family = query.family;
  if (query?.inheritanceType) params.inheritanceType = query.inheritanceType;
  if (query?.page) params.page = query.page;
  if (query?.pageSize) params.pageSize = query.pageSize;

  const { data } = await apiClient.get<OrgDefaultsListResult>(
    '/inheritance/org-defaults',
    { params },
  );
  return data;
}

export async function deriveOrgDefaults(): Promise<OrgDerivationResult> {
  const { data } = await apiClient.post<OrgDerivationResult>(
    '/inheritance/org-defaults/derive',
  );
  return data;
}

export async function revertToOrgDefaults(
  systemId: string,
  controlIds: string[],
  revertedBy?: string,
): Promise<RevertResult> {
  const { data } = await apiClient.post<RevertResult>(
    `/systems/${systemId}/inheritance/revert-to-org-defaults`,
    { controlIds, revertedBy },
  );
  return data;
}
