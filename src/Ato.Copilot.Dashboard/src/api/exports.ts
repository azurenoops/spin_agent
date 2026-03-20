import apiClient from './client';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface ExportSummary {
  exportId: string;
  format: string;
  status: string;
  fileSize: number | null;
  controlCount: number | null;
  generatedBy: string;
  generatedAt: string;
  completedAt: string | null;
  templateName: string | null;
}

export interface ExportDetail extends ExportSummary {
  systemId: string;
  contentHash: string | null;
  expiresAt: string;
}

export interface TemplateInfo {
  id: string;
  name: string;
  description: string | null;
  fileSize: number;
  isDefault: boolean;
  mergeFields: string[];
  uploadedBy: string;
  uploadedAt: string;
}

export interface CreateTemplateResponse {
  id: string;
  name: string;
  mergeFields: string[];
  isDefault: boolean;
  uploadedAt: string;
}

export interface UpdateTemplateResponse {
  id: string;
  name: string;
  description: string | null;
  updatedAt: string;
}

// ─── Export API Functions ────────────────────────────────────────────────────

export async function requestExport(
  systemId: string,
  format: string,
  templateId?: string,
): Promise<ExportSummary> {
  const { data } = await apiClient.post<ExportSummary>(
    `/systems/${systemId}/exports`,
    { format, templateId: templateId || undefined },
  );
  return data;
}

export async function listExports(
  systemId: string,
  options?: { format?: string; includeFailed?: boolean; limit?: number; offset?: number },
): Promise<{ items: ExportSummary[]; totalCount: number }> {
  const { data } = await apiClient.get<{ items: ExportSummary[]; totalCount: number }>(
    `/systems/${systemId}/exports`,
    { params: options },
  );
  return data;
}

export async function getExport(
  systemId: string,
  exportId: string,
): Promise<ExportDetail> {
  const { data } = await apiClient.get<ExportDetail>(
    `/systems/${systemId}/exports/${exportId}`,
  );
  return data;
}

export function downloadExportUrl(systemId: string, exportId: string): string {
  const baseURL = apiClient.defaults.baseURL ?? '/api/dashboard';
  return `${baseURL}/systems/${systemId}/exports/${exportId}/download`;
}

// ─── Standalone OSCAL Export URLs (Feature 041) ─────────────────────────────

export function oscalPoamUrl(systemId: string): string {
  return `/api/v1/systems/${systemId}/exports/oscal-poam`;
}

export function oscalAssessmentResultsUrl(systemId: string): string {
  return `/api/v1/systems/${systemId}/exports/oscal-assessment-results`;
}

export function oscalSapUrl(systemId: string): string {
  return `/api/v1/systems/${systemId}/exports/oscal-sap`;
}

// ─── Template API Functions ─────────────────────────────────────────────────

export async function listTemplates(
  options?: { limit?: number; offset?: number },
): Promise<{ items: TemplateInfo[]; totalCount: number }> {
  const { data } = await apiClient.get<{ items: TemplateInfo[]; totalCount: number }>(
    '/templates',
    { params: options },
  );
  return data;
}

export async function uploadTemplate(
  file: File,
  name: string,
  description?: string,
  isDefault?: boolean,
): Promise<CreateTemplateResponse> {
  const form = new FormData();
  form.append('file', file);
  form.append('name', name);
  if (description) form.append('description', description);
  if (isDefault) form.append('isDefault', 'true');

  const { data } = await apiClient.post<CreateTemplateResponse>('/templates', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  return data;
}

export async function deleteTemplate(templateId: string): Promise<void> {
  await apiClient.delete(`/templates/${templateId}`);
}

export async function renameTemplate(
  templateId: string,
  name?: string,
  description?: string,
): Promise<UpdateTemplateResponse> {
  const { data } = await apiClient.put<UpdateTemplateResponse>(
    `/templates/${templateId}`,
    { name, description },
  );
  return data;
}
