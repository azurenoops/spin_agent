import apiClient from './client';

export interface CatalogSourceStatus {
  identifier: string; name: string; definitionVersion: string; sourceAvailable: boolean;
  sourceVersion: string | null; sourceUri: string | null; capturedAt: string | null;
}
export interface CatalogSourceManagementStatus {
  isPlatformAdministrator: boolean; canManageSources: boolean; managementPath: string;
  reason: string | null; sources: CatalogSourceStatus[];
}
export interface SourceBackfillResult {
  captured: { identifier: string; changed: boolean }[];
  failures: { identifier: string; message: string; errorCode: string }[];
}

const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string';
const nullableText = (value: unknown) => value === null || text(value);
function status(value: unknown): value is CatalogSourceManagementStatus {
  return object(value) && typeof value.isPlatformAdministrator === 'boolean' && typeof value.canManageSources === 'boolean'
    && value.managementPath === '/workspaces/csp/controls' && nullableText(value.reason)
    && Array.isArray(value.sources) && value.sources.every(source => object(source)
      && ['identifier', 'name', 'definitionVersion'].every(key => text(source[key]))
      && ['sourceVersion', 'sourceUri', 'capturedAt'].every(key => nullableText(source[key]))
      && typeof source.sourceAvailable === 'boolean');
}

export async function getCatalogSourceManagement(signal?: AbortSignal): Promise<CatalogSourceManagementStatus> {
  const { data } = await apiClient.get<unknown>('/frameworks/source-management', { signal });
  if (!status(data)) throw new Error('Unexpected catalog source management response.');
  return data;
}
export async function captureCatalogSource(identifier: string): Promise<void> {
  await apiClient.post(`/frameworks/${encodeURIComponent(identifier)}/source`);
}
export async function backfillCatalogSources(): Promise<SourceBackfillResult> {
  const { data } = await apiClient.post<SourceBackfillResult>('/frameworks/backfill-sources');
  if (!object(data) || !Array.isArray(data.captured) || !data.captured.every(item =>
    object(item) && text(item.identifier) && typeof item.changed === 'boolean')
    || !Array.isArray(data.failures) || !data.failures.every(item =>
      object(item) && text(item.identifier) && text(item.message) && text(item.errorCode)))
    throw new Error('Unexpected catalog backfill response.');
  return data;
}
