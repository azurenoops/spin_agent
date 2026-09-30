import apiClient from './client';

export type EvidenceView = 'all' | 'system' | 'provider';
export interface EvidenceControl {
  controlId: string;
  title: string | null;
  kind: string;
}
export interface EvidenceCatalogItem {
  id: string;
  recordId: string;
  source: 'Manual' | 'Automated' | 'Provider';
  name: string;
  sourceLabel: string;
  recordedAt: string | null;
  category: string | null;
  controls: EvidenceControl[];
  linksKnown: boolean;
}
export interface EvidenceCatalog {
  systemId: string;
  items: EvidenceCatalogItem[];
  totalCount: number | null;
  availableCount: number;
  page: number;
  pageSize: number;
  counts: { all: number | null; system: number | null; provider: number | null; missingLinks: number | null };
  sources: { source: 'system' | 'provider'; state: 'available' | 'denied' | 'unavailable'; message: string | null }[];
  permissions: { canUpload: boolean; uploadReason: string | null };
}
export interface EvidenceCatalogDetail {
  systemId: string;
  item: EvidenceCatalogItem;
  description: string | null;
  owner: string | null;
  recordedBy: string | null;
  version: string | null;
  contentHash: string | null;
  contentType: string | null;
  fileSizeBytes: number | null;
  availability: 'FileAvailable' | 'SummaryOnly' | 'Unavailable' | 'Unknown';
  availabilityReason: string | null;
  summary: string | null;
  review: string | null;
  currency: string | null;
  relevance: string | null;
  provenance: { label: string; value: string }[];
  history: { id: string; label: string; at: string | null; actor: string | null;
    fileName: string | null; downloadUrl: string | null }[];
  permissions: {
    canDownload: boolean;
    downloadUrl: string | null;
    canReplace: boolean;
    canDelete: boolean;
    canCollect: boolean;
    canLink: boolean;
    manageReason: string | null;
    linkReason: string | null;
  };
}
export interface EvidenceCatalogQuery {
  view: EvidenceView;
  search: string;
  family: string;
  category: string;
  source: string;
  dateFrom: string;
  dateTo: string;
  sortBy: string;
  sortOrder: 'asc' | 'desc';
  page: number;
  pageSize: number;
}
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const text = (v: unknown): v is string => typeof v === 'string';
const nullableText = (v: unknown) => v === null || text(v);
const count = (v: unknown) => typeof v === 'number' && Number.isInteger(v) && v >= 0;
const nullableCount = (v: unknown) => v === null || count(v);

function isItem(v: unknown): v is EvidenceCatalogItem {
  return object(v) && text(v.id) && text(v.recordId) && text(v.name) && text(v.sourceLabel)
    && ['Manual', 'Automated', 'Provider'].includes(String(v.source))
    && v.id === `${v.source === 'Manual' ? 'artifact' : v.source === 'Automated' ? 'automated' : 'provider'}:${v.recordId}`
    && nullableText(v.recordedAt) && nullableText(v.category) && typeof v.linksKnown === 'boolean'
    && Array.isArray(v.controls) && v.controls.every(c => object(c)
      && text(c.controlId) && nullableText(c.title) && text(c.kind));
}
function isCatalog(v: unknown): v is EvidenceCatalog {
  return object(v) && text(v.systemId) && Array.isArray(v.items) && v.items.every(isItem)
    && nullableCount(v.totalCount) && count(v.availableCount) && count(v.page) && count(v.pageSize)
    && object(v.counts) && ['all', 'system', 'provider', 'missingLinks'].every(k => nullableCount(v.counts && object(v.counts) ? v.counts[k] : undefined))
    && Array.isArray(v.sources) && v.sources.every(s => object(s)
      && ['system', 'provider'].includes(String(s.source))
      && ['available', 'denied', 'unavailable'].includes(String(s.state)) && nullableText(s.message))
    && object(v.permissions) && typeof v.permissions.canUpload === 'boolean' && nullableText(v.permissions.uploadReason);
}
function isDetail(v: unknown): v is EvidenceCatalogDetail {
  if (!object(v) || !text(v.systemId) || !isItem(v.item) || !object(v.permissions)) return false;
  return ['description', 'owner', 'recordedBy', 'version', 'contentHash', 'contentType',
    'availabilityReason', 'summary', 'review', 'currency', 'relevance'].every(k => nullableText(v[k]))
    && nullableCount(v.fileSizeBytes) && ['FileAvailable', 'SummaryOnly', 'Unavailable', 'Unknown'].includes(String(v.availability))
    && ['canDownload', 'canReplace', 'canDelete', 'canCollect', 'canLink'].every(k => object(v.permissions) && typeof v.permissions[k] === 'boolean')
    && ['downloadUrl', 'manageReason', 'linkReason'].every(k => object(v.permissions) && nullableText(v.permissions[k]))
    && Array.isArray(v.provenance) && v.provenance.every(p => object(p) && text(p.label) && text(p.value))
    && Array.isArray(v.history) && v.history.every(h => object(h) && text(h.id) && text(h.label)
      && ['at', 'actor', 'fileName', 'downloadUrl'].every(k => nullableText(h[k])));
}
const root = (system: string) => `/systems/${encodeURIComponent(system)}/evidence-catalog`;

export async function getEvidenceCatalog(systemId: string, query: EvidenceCatalogQuery, signal?: AbortSignal): Promise<EvidenceCatalog> {
  const params = Object.fromEntries(Object.entries(query).filter(([, value]) => value !== ''));
  const { data } = await apiClient.get<unknown>(root(systemId), { params, signal });
  if (!isCatalog(data) || data.systemId !== systemId) throw new Error('Unexpected or mismatched evidence catalog response.');
  return data;
}
export async function getEvidenceCatalogDetail(systemId: string, id: string, signal?: AbortSignal): Promise<EvidenceCatalogDetail> {
  const { data } = await apiClient.get<unknown>(`${root(systemId)}/${encodeURIComponent(id)}`, { signal });
  if (!isDetail(data) || data.systemId !== systemId || data.item.id !== id)
    throw new Error('Unexpected or mismatched evidence detail response.');
  return data;
}
export async function linkCatalogEvidence(systemId: string, id: string, controlId: string, expectedHash: string): Promise<void> {
  await apiClient.post(`${root(systemId)}/${encodeURIComponent(id)}/links`, { controlId, expectedHash });
}
export function evidenceError(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (object(error)) {
    if (text(error.error)) return error.error;
    if (object(error.error) && text(error.error.message)) return error.error.message;
    if (text(error.message)) return error.message;
    if (text(error.detail)) return error.detail;
    if (object(error.response) && object(error.response.data)) return evidenceError(error.response.data);
  }
  return 'Evidence request failed. Retry or check your current workspace access.';
}
