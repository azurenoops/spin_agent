import apiClient from './client';

export interface PolicySource {
  id: string; name: string; description: string | null; subType: string | null;
  status: string; owner: string | null; revision: string; versionLabel: string;
  modifiedAt: string | null; alreadyLinked: boolean; relatedControls: string[];
}
export interface PolicyReference {
  id: string; policyId: string; name: string; rationale: string | null;
  retainedVersionLabel: string | null; sourceStatus: string | null; sourceChanged: boolean;
  retention: 'Retained' | 'LegacyUnretained' | 'Indirect'; revision: number;
  canEdit: boolean; canRemove: boolean; actionReason: string | null;
}
export interface PolicyWorkspace {
  systemId: string; systemName: string; items: PolicyReference[]; totalCount: number; unfilteredTotal: number;
  page: number; pageSize: number;
  permissions: { canAssign: boolean; assignReason: string | null; canCreateLibrary: boolean; createReason: string | null };
}
export interface PolicyLibrary {
  systemId: string; items: PolicySource[]; totalCount: number; page: number; pageSize: number;
}
export interface PolicyReferenceDetail {
  systemId: string; systemName: string; reference: PolicyReference;
  retainedSource: PolicySource | null; currentSource: PolicySource | null;
  relatedControls: string[]; reviewMessage: string;
  history: { id: string; action: string; actor: string | null; at: string; description: string }[];
  removalImpact: string[];
}
export interface PolicyQuery { search: string; status: string; sourceChanged: string; page: number; pageSize: number }
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v);
const text = (v: unknown): v is string => typeof v === 'string';
const nullableText = (v: unknown) => v === null || text(v);
const count = (v: unknown) => typeof v === 'number' && Number.isInteger(v) && v >= 0;
const texts = (v: unknown): v is string[] => Array.isArray(v) && v.every(text);
function isSource(v: unknown): v is PolicySource {
  return object(v) && ['id', 'name', 'status', 'revision', 'versionLabel'].every(k => text(v[k]))
    && ['description', 'subType', 'owner', 'modifiedAt'].every(k => nullableText(v[k]))
    && typeof v.alreadyLinked === 'boolean' && texts(v.relatedControls);
}
function isReference(v: unknown): v is PolicyReference {
  return object(v) && ['id', 'policyId', 'name'].every(k => text(v[k]))
    && ['rationale', 'retainedVersionLabel', 'sourceStatus', 'actionReason'].every(k => nullableText(v[k]))
    && ['Retained', 'LegacyUnretained', 'Indirect'].includes(String(v.retention)) && count(v.revision)
    && ['canEdit', 'canRemove', 'sourceChanged'].every(k => typeof v[k] === 'boolean');
}
function isWorkspace(v: unknown): v is PolicyWorkspace {
  return object(v) && text(v.systemId) && text(v.systemName)
    && ['totalCount', 'unfilteredTotal', 'page', 'pageSize'].every(k => count(v[k]))
    && Array.isArray(v.items) && v.items.every(isReference)
    && object(v.permissions) && typeof v.permissions.canAssign === 'boolean' && typeof v.permissions.canCreateLibrary === 'boolean'
    && nullableText(v.permissions.assignReason) && nullableText(v.permissions.createReason);
}
function isLibrary(v: unknown): v is PolicyLibrary {
  return object(v) && text(v.systemId) && ['totalCount', 'page', 'pageSize'].every(k => count(v[k]))
    && Array.isArray(v.items) && v.items.every(isSource);
}
function isDetail(v: unknown): v is PolicyReferenceDetail {
  return object(v) && text(v.systemId) && text(v.systemName) && isReference(v.reference)
    && (v.retainedSource === null || isSource(v.retainedSource))
    && (v.currentSource === null || isSource(v.currentSource)) && texts(v.relatedControls)
    && text(v.reviewMessage) && texts(v.removalImpact) && Array.isArray(v.history)
    && v.history.every(h => object(h) && ['id', 'action', 'at', 'description'].every(k => text(h[k])) && nullableText(h.actor));
}
const root = (id: string) => `/systems/${encodeURIComponent(id)}/policy-workspace`;
const parameters = (query: object) => Object.fromEntries(Object.entries(query).filter(([, value]) => value !== ''));
export async function getPolicyWorkspace(system: string, query: PolicyQuery, signal?: AbortSignal): Promise<PolicyWorkspace> {
  const { data } = await apiClient.get<unknown>(root(system), { params: parameters(query), signal });
  if (!isWorkspace(data) || data.systemId !== system) throw new Error('Unexpected or mismatched policy workspace response.');
  return data;
}
export async function getPolicyLibrary(system: string, query: { search: string; page: number; pageSize: number }, signal?: AbortSignal): Promise<PolicyLibrary> {
  const { data } = await apiClient.get<unknown>(`${root(system)}/library`, { params: parameters(query), signal });
  if (!isLibrary(data) || data.systemId !== system) throw new Error('Unexpected or mismatched policy library response.');
  return data;
}
export async function getPolicySource(system: string, id: string, signal?: AbortSignal): Promise<PolicySource> {
  const { data } = await apiClient.get<unknown>(`${root(system)}/sources/${encodeURIComponent(id)}`, { signal });
  if (!isSource(data) || data.id !== id) throw new Error('Unexpected or mismatched policy source response.');
  return data;
}
export async function createPolicySource(system: string, request: { name: string; subType: string; description: string }): Promise<PolicySource> {
  const { data } = await apiClient.post<unknown>(`${root(system)}/library`, request);
  if (!isSource(data)) throw new Error('The library save returned an unexpected response. Refresh before retrying creation.');
  return data;
}
export async function addPolicyReference(system: string, request: { policyId: string; expectedSourceRevision: string; rationale: string }): Promise<PolicyReference> {
  const { data } = await apiClient.post<unknown>(`${root(system)}/references`, request);
  if (!isReference(data) || data.policyId !== request.policyId) throw new Error('Unexpected policy assignment response. Refresh before retrying.');
  return data;
}
export async function getPolicyReference(system: string, id: string, signal?: AbortSignal): Promise<PolicyReferenceDetail> {
  const { data } = await apiClient.get<unknown>(`${root(system)}/references/${encodeURIComponent(id)}`, { signal });
  if (!isDetail(data) || data.systemId !== system || data.reference.id !== id) throw new Error('Unexpected or mismatched policy reference response.');
  return data;
}
export async function editPolicyReference(system: string, id: string, request: { expectedRevision: number; rationale: string }): Promise<PolicyReference> {
  const { data } = await apiClient.patch<unknown>(`${root(system)}/references/${encodeURIComponent(id)}`, request);
  if (!isReference(data) || data.id !== id) throw new Error('Unexpected policy update response. Refresh before retrying.');
  return data;
}
export async function unlinkPolicyReference(system: string, id: string, revision: number): Promise<void> {
  await apiClient.delete(`${root(system)}/references/${encodeURIComponent(id)}`, { params: { expectedRevision: revision } });
}
export function policyError(error: unknown): string {
  if (object(error)) {
    if (text(error.error)) return error.error;
    if (object(error.error) && text(error.error.message)) return error.error.message;
    if (object(error.response) && object(error.response.data)) return policyError(error.response.data);
    if (object(error.response)) {
      if (error.response.status === 404) return 'This policy record is unavailable or not accessible in the current workspace.';
      if (error.response.status === 403) return 'Your current workspace does not authorize this policy action.';
      if (error.response.status === 409) return 'The policy source or reference changed. Refresh before retrying.';
    }
  }
  return error instanceof Error ? error.message : 'The policy operation failed. Refresh the current record and retry.';
}
