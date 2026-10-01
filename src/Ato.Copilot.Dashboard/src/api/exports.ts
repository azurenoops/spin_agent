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
  previewOnly?: boolean;
  design?: DocumentSourceReference | null;
  designArtifacts?: DocumentSourceReference[] | null;
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
  sourceState: 'CurrentWorkingData' | 'ApprovedSources';
  canGenerate?: boolean;
}

export type AdditionalDocumentType = 'sap' | 'sar' | 'poam';
export type DocumentRecordReference = Omit<DocumentSourceReference, 'versionId'> & { versionId: string | null };

export interface AdditionalDocumentPreview extends SspPreview {
  documentType: AdditionalDocumentType;
  systemName: string;
  available: true;
  documentStatus?: string | null;
  sourceRecords?: DocumentRecordReference[];
}

export interface UnavailableDocumentPreview {
  systemId: string;
  systemName: string;
  documentType: AdditionalDocumentType;
  available: false;
  reasonCode: string;
  message: string;
}

export async function getAdditionalDocumentPreview(
  systemId: string, documentType: AdditionalDocumentType, signal?: AbortSignal,
): Promise<AdditionalDocumentPreview | UnavailableDocumentPreview> {
  const { data } = await apiClient.get<AdditionalDocumentPreview | UnavailableDocumentPreview>(
    `/systems/${encodeURIComponent(systemId)}/documents/${documentType}/preview`, { signal },
  );
  if (!data || data.systemId !== systemId || data.documentType !== documentType || typeof data.systemName !== 'string') {
    throw new Error('The server returned a differently scoped document preview.');
  }
  if (data.available === false) {
    if (typeof data.reasonCode !== 'string' || !data.reasonCode || typeof data.message !== 'string' || !data.message.trim()) {
      throw new Error('The server returned an incomplete document preview availability state.');
    }
    return data;
  }
  if (data.available !== true || data.canGenerate !== false) throw new Error('The server returned an invalid read-only document preview.');
  checkedPreview(data, systemId);
  if (data.sourceRecords !== undefined && (!Array.isArray(data.sourceRecords) || data.sourceRecords.some(item =>
    !item || typeof item.kind !== 'string' || typeof item.recordId !== 'string'
      || item.versionId !== null && typeof item.versionId !== 'string' || typeof item.contentHash !== 'string'))) {
    throw new Error('The server returned invalid document preview source records.');
  }
  return data;
}

export type SspPreviewSource = 'working' | 'approved';

function sspPreviewUrl(systemId: string, source: SspPreviewSource) {
  return `/systems/${encodeURIComponent(systemId)}/documents/ssp/preview${source === 'approved' ? '?source=approved' : ''}`;
}

export async function getSspPreview(
  systemId: string, signal?: AbortSignal, source: SspPreviewSource = 'working',
): Promise<SspPreview> {
  const { data } = await apiClient.get<SspPreview>(
    sspPreviewUrl(systemId, source), { signal },
  );
  return checkedPreview(data, systemId, source);
}

function checkedPreview(data: SspPreview, systemId: string, source: SspPreviewSource = 'working'): SspPreview {
  const validReference = (item: DocumentSourceReference) => item && typeof item.kind === 'string'
    && typeof item.recordId === 'string' && typeof item.versionId === 'string' && typeof item.contentHash === 'string';
  if (data?.systemId !== systemId || data.format !== 'json' || data.contentType !== 'application/json'
    || data.isPreview !== true || data.sourceState !== (source === 'approved' ? 'ApprovedSources' : 'CurrentWorkingData')
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
  if (data.sourceManifest?.design && !validReference(data.sourceManifest.design)
    || data.sourceManifest?.designArtifacts != null && (!Array.isArray(data.sourceManifest.designArtifacts)
      || data.sourceManifest.designArtifacts.some(item => !validReference(item)))) {
    throw new Error('The server returned invalid design source or artifact pins.');
  }
  if (data.canGenerate !== undefined && typeof data.canGenerate !== 'boolean'
    || data.sourceManifest?.scope === 'WorkingProfilePreview' && (data.sourceManifest.previewOnly !== true || data.canGenerate !== false)) {
    throw new Error('The server returned an invalid working-preview export authority.');
  }
  if (source === 'approved' && (!data.sourceManifest || data.sourceManifest.previewOnly === true
    || data.sourceManifest.scope === 'WorkingProfilePreview'
    || data.sourceManifest.profiles.some(item => item.kind === 'WorkingProfile')
    || typeof data.canGenerate !== 'boolean')) {
    throw new Error('The server returned working or unverified sources for an approved-source preview.');
  }
  return data;
}

export async function retainSspPreview(
  systemId: string, key: string, signal?: AbortSignal, source: SspPreviewSource = 'working',
): Promise<SspPreview> {
  const { data } = await apiClient.post<SspPreview>(
    sspPreviewUrl(systemId, source), {},
    { headers: { 'Idempotency-Key': key }, signal },
  );
  const result = checkedPreview(data, systemId, source);
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
