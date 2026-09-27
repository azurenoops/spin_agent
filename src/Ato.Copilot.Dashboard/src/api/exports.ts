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
  sourcePreviewId?: string | null;
  sourceManifest?: DocumentSourceManifest | null;
}

export interface DocumentSourceReference {
  kind: string;
  recordId: string;
  versionId: string;
  contentHash: string;
}

export interface DocumentSourceManifest {
  scope: string;
  profiles: DocumentSourceReference[];
  providerSources: DocumentSourceReference[];
  narratives: DocumentSourceReference[];
  otherSources: string;
}

export interface SspPreview {
  previewId?: string | null;
  sourceManifest?: DocumentSourceManifest | null;
  systemId: string;
  format: 'json';
  contentType: 'application/json';
  content: string;
  contentHash: string;
  generatedAt: string;
  sourceGaps: { code: string; message: string }[];
  isPreview: true;
  sourceState: 'CurrentWorkingData';
}

export async function getSspPreview(systemId: string, signal?: AbortSignal): Promise<SspPreview> {
  const { data } = await apiClient.get<SspPreview>(
    `/systems/${encodeURIComponent(systemId)}/documents/ssp/preview`, { signal },
  );
  return checkedPreview(data, systemId);
}

function checkedPreview(data: SspPreview, systemId: string): SspPreview {
  if (data?.systemId !== systemId || data.format !== 'json' || data.contentType !== 'application/json'
    || data.isPreview !== true || data.sourceState !== 'CurrentWorkingData'
    || typeof data.content !== 'string' || typeof data.contentHash !== 'string'
    || typeof data.generatedAt !== 'string' || !Array.isArray(data.sourceGaps)
    || data.sourceGaps.some(gap => !gap || typeof gap.code !== 'string' || typeof gap.message !== 'string')) {
    throw new Error('The server returned an invalid or differently scoped document preview.');
  }
  if (data.sourceManifest && (!Array.isArray(data.sourceManifest.profiles)
    || !Array.isArray(data.sourceManifest.providerSources) || !Array.isArray(data.sourceManifest.narratives)
    || typeof data.sourceManifest.scope !== 'string' || typeof data.sourceManifest.otherSources !== 'string'
    || [...data.sourceManifest.profiles, ...data.sourceManifest.providerSources, ...data.sourceManifest.narratives]
      .some(item => !item || typeof item.kind !== 'string' || typeof item.recordId !== 'string'
        || typeof item.versionId !== 'string' || typeof item.contentHash !== 'string'))) {
    throw new Error('The server returned an invalid document source manifest.');
  }
  return data;
}

export async function retainSspPreview(systemId: string, key: string, signal?: AbortSignal): Promise<SspPreview> {
  const { data } = await apiClient.post<SspPreview>(
    `/systems/${encodeURIComponent(systemId)}/documents/ssp/preview`, {},
    { headers: { 'Idempotency-Key': key }, signal },
  );
  const result = checkedPreview(data, systemId);
  if (typeof result.previewId !== 'string' || !result.previewId) throw new Error('The server did not return a retained preview identity.');
  return result;
}

export async function requestPreviewExport(systemId: string, previewId: string, key: string, signal?: AbortSignal): Promise<ExportSummary> {
  const { data } = await apiClient.post<ExportSummary>(
    `/systems/${encodeURIComponent(systemId)}/exports`, { format: 'json', sourcePreviewId: previewId },
    { headers: { 'Idempotency-Key': key }, signal },
  );
  return data;
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
  signal?: AbortSignal,
): Promise<ExportSummary> {
  const { data } = await apiClient.post<ExportSummary>(
    `/systems/${systemId}/exports`,
    { format, templateId: templateId || undefined },
    { signal },
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
  signal?: AbortSignal,
): Promise<ExportDetail> {
  const { data } = await apiClient.get<ExportDetail>(
    `/systems/${systemId}/exports/${exportId}`,
    { signal },
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
  signal?: AbortSignal,
): Promise<{ items: TemplateInfo[]; totalCount: number }> {
  const { data } = await apiClient.get<{ items: TemplateInfo[]; totalCount: number }>(
    '/templates',
    { params: options, signal },
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
