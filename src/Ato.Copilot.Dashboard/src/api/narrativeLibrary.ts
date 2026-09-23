import apiClient from './client';

export interface ReferencePassage { controlId: string | null; narrativeType: string | null; content: string }
export interface NarrativeReference {
  id: string; referenceKey: string; title: string; scope: string; scopeId: string;
  sourceName: string; sourceSha256: string; version: number; revision: number; isPublished: boolean;
  createdAt: string; createdBy: string; publishedAt: string | null; publishedBy: string | null;
  passages: ReferencePassage[];
}
export interface NarrativeProposal {
  id: string; controlId: string; narrativeType: string; baseVersion: number; beforeContent: string;
  proposedContent: string; stateHash: string; provenance: Record<string, unknown>; conflicts: string[];
  missingEvidence: string[]; status: string; revision: number; createdAt: string; createdBy: string;
  reviewedAt: string | null; reviewedBy: string | null; reviewNote: string | null; acceptedVersion: number | null;
  isStale: boolean; canReview: boolean;
  changeSourceKind?: string | null; changeSourceId?: string | null; generationErrorCode?: string | null;
}
export interface NarrativeAccess {
  tenantId: string; systemName: string; canAuthor: boolean; canPublishShared: boolean;
  canGenerate?: boolean;
  capabilities: { id: string; name: string }[];
}
export type ReferenceLibraryTarget = { kind: 'system'; systemId: string } | { kind: 'organization' } | { kind: 'provider' };
export interface OrganizationLibraryAccess { tenantId: string; canPublishShared: boolean; capabilities: { id: string; name: string }[] }
export interface ProviderLibraryAccess { cspProfileId: string; displayName: string; canPublish: boolean; capabilities: { id: string; name: string }[] }
export interface ReferenceDraftUpdate { expectedRevision: number; scope: string; scopeId: string; passages: ReferencePassage[] }
export interface NarrativeSourceContext {
  sourceRevision: string; cause: string; baselineId: string; subscriptionId: string;
  cspProfileId: string | null; cspInheritedComponentId: string | null; cspCapabilityId: string | null;
  previousInheritanceType: string | null; currentInheritanceType: string | null;
}
export interface NarrativeImpactReceipt {
  id: string; impactId: string; recordedAt: string; sourceKind: string | null; sourceId: string | null;
  sourceActor: string | null; sourceContext: NarrativeSourceContext | null;
}
export interface NarrativeImpactReceiptPage { items: NarrativeImpactReceipt[]; totalCount: number; page: number; pageSize: number }
const root = (systemId: string) => `/systems/${encodeURIComponent(systemId)}/narrative-library`;
const config = () => ({ baseURL: (apiClient.defaults.baseURL ?? '/api/dashboard').replace(/\/dashboard\/?$/, '') });
const scopedRoot = (target: ReferenceLibraryTarget) => target.kind === 'system' ? root(target.systemId)
  : target.kind === 'provider' ? '/csp/narrative-library' : '/narrative-library';
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string' && value.length > 0;
const nullableText = (value: unknown) => value === null || typeof value === 'string';
const capabilities = (value: unknown): value is { id: string; name: string }[] =>
  Array.isArray(value) && value.every(item => object(item) && text(item.id) && typeof item.name === 'string');

export async function getOrganizationLibraryAccess(signal?: AbortSignal): Promise<OrganizationLibraryAccess> {
  const { data } = await apiClient.get<unknown>('/narrative-library/access', { ...config(), signal });
  if (!object(data) || !text(data.tenantId) || typeof data.canPublishShared !== 'boolean' || !capabilities(data.capabilities))
    throw new Error('Unexpected organization library access response.');
  return { tenantId: data.tenantId, canPublishShared: data.canPublishShared, capabilities: data.capabilities };
}
export async function getProviderLibraryAccess(signal?: AbortSignal): Promise<ProviderLibraryAccess> {
  const { data } = await apiClient.get<unknown>('/csp/narrative-library/access', { ...config(), signal });
  if (!object(data) || !text(data.cspProfileId) || !text(data.displayName) || typeof data.canPublish !== 'boolean' || !capabilities(data.capabilities))
    throw new Error('Unexpected provider library access response.');
  return { cspProfileId: data.cspProfileId, displayName: data.displayName, canPublish: data.canPublish, capabilities: data.capabilities };
}
function checkedReference(value: unknown, target: ReferenceLibraryTarget): NarrativeReference {
  const allowed = target.kind === 'provider' ? ['Provider', 'ProviderCapability']
    : target.kind === 'organization' ? ['Organization', 'Capability'] : ['System', 'Organization', 'Capability'];
  if (!isReference(value) || !allowed.includes(value.scope))
    throw new Error('Unexpected reference scope or response.');
  return value;
}
function isReference(value: unknown): value is NarrativeReference {
  return object(value)
    && ['id', 'referenceKey', 'title', 'scope', 'scopeId', 'sourceName', 'sourceSha256', 'createdAt', 'createdBy'].every(key => text(value[key]))
    && ['publishedAt', 'publishedBy'].every(key => nullableText(value[key]))
    && typeof value.revision === 'number' && Number.isInteger(value.revision) && value.revision > 0
    && typeof value.version === 'number' && Number.isInteger(value.version) && value.version > 0
    && typeof value.isPublished === 'boolean' && Array.isArray(value.passages)
    && value.passages.every(item => object(item) && nullableText(item.controlId) && nullableText(item.narrativeType) && typeof item.content === 'string');
}
function isReceipt(value: unknown): value is NarrativeImpactReceipt {
  if (!object(value) || !['id', 'impactId', 'recordedAt'].every(key => text(value[key]))
    || !['sourceKind', 'sourceId', 'sourceActor'].every(key => nullableText(value[key]))) return false;
  const source = value.sourceContext;
  return source === null || object(source)
    && ['sourceRevision', 'cause', 'baselineId', 'subscriptionId'].every(key => text(source[key]))
    && ['cspProfileId', 'cspInheritedComponentId', 'cspCapabilityId', 'previousInheritanceType', 'currentInheritanceType'].every(key => nullableText(source[key]));
}
export async function getScopedReferences(target: ReferenceLibraryTarget, signal?: AbortSignal): Promise<NarrativeReference[]> {
  const { data } = await apiClient.get<unknown>(scopedRoot(target), { ...config(), signal });
  if (!Array.isArray(data)) throw new Error('Unexpected reference list response.');
  return data.map(item => checkedReference(item, target));
}
export async function importScopedReference(target: ReferenceLibraryTarget, form: FormData): Promise<NarrativeReference> {
  const { data } = await apiClient.post<unknown>(`${scopedRoot(target)}/imports`, form, { ...config(), headers: { 'Content-Type': undefined } });
  const reference = checkedReference(data, target);
  if (reference.isPublished) throw new Error('The imported reference is not an unpublished draft.');
  return reference;
}
export async function updateScopedReference(target: ReferenceLibraryTarget, id: string, request: ReferenceDraftUpdate): Promise<NarrativeReference> {
  const { data } = await apiClient.patch<unknown>(`${scopedRoot(target)}/${encodeURIComponent(id)}`, request, config());
  const reference = checkedReference(data, target);
  if (reference.id !== id || reference.isPublished) throw new Error('The updated reference does not match the requested draft.');
  return reference;
}
export async function publishScopedReference(target: ReferenceLibraryTarget, id: string, expectedRevision: number,
  passages: ReferencePassage[]): Promise<NarrativeReference> {
  const { data } = await apiClient.post<unknown>(`${scopedRoot(target)}/${encodeURIComponent(id)}/publish`, { expectedRevision, reviewed: true, passages }, config());
  const reference = checkedReference(data, target);
  if (reference.id !== id || !reference.isPublished) throw new Error('The reference publication was not confirmed.');
  return reference;
}
export async function updateReferenceDraft(systemId: string, id: string, request: ReferenceDraftUpdate): Promise<NarrativeReference> {
  return updateScopedReference({ kind: 'system', systemId }, id, request);
}
export async function getImpactReceipts(systemId: string, proposalId: string, page = 1, signal?: AbortSignal): Promise<NarrativeImpactReceiptPage> {
  const { data } = await apiClient.get<unknown>(`${root(systemId)}/proposals/${encodeURIComponent(proposalId)}/impact-receipts`,
    { ...config(), signal, params: { page, pageSize: 50 } });
  if (!object(data) || !Array.isArray(data.items) || data.page !== page || data.pageSize !== 50
    || typeof data.totalCount !== 'number' || !Number.isInteger(data.totalCount) || data.totalCount < data.items.length)
    throw new Error('Unexpected delivery history response.');
  if (!data.items.every(isReceipt)) throw new Error('Unexpected delivery receipt or source context.');
  return { items: data.items, totalCount: data.totalCount, page, pageSize: 50 };
}

export async function getReferences(systemId: string): Promise<NarrativeReference[]> {
  return (await apiClient.get<NarrativeReference[]>(root(systemId), config())).data;
}
export async function getProposals(systemId: string): Promise<NarrativeProposal[]> {
  return (await apiClient.get<NarrativeProposal[]>(`${root(systemId)}/proposals`, config())).data;
}
function proposalIdentity(proposal: NarrativeProposal, id: string): NarrativeProposal {
  if (typeof proposal?.id !== 'string' || proposal.id.toLowerCase() !== id.toLowerCase())
    throw new Error('The returned proposal does not match the requested queued work.');
  return proposal;
}
export async function getProposalById(systemId: string, id: string): Promise<NarrativeProposal | null> {
  try {
    const { data } = await apiClient.get<NarrativeProposal>(`${root(systemId)}/proposals/${encodeURIComponent(id)}`, config());
    return proposalIdentity(data, id);
  } catch (error) {
    if (object(error) && (error.errorCode === 'NOT_FOUND'
      || object(error.error) && error.error.errorCode === 'NOT_FOUND'
      || object(error.response) && error.response.status === 404)) return null;
    throw error;
  }
}
export async function generateQueuedProposal(systemId: string, id: string, expectedRevision: number): Promise<NarrativeProposal> {
  const { data } = await apiClient.post<NarrativeProposal>(`${root(systemId)}/proposals/${encodeURIComponent(id)}/generate`,
    { expectedRevision }, config());
  return proposalIdentity(data, id);
}
export async function getNarrativeAccess(systemId: string): Promise<NarrativeAccess> {
  return (await apiClient.get<NarrativeAccess>(`${root(systemId)}/access`, config())).data;
}
export async function importReference(systemId: string, form: FormData): Promise<NarrativeReference> {
  return (await apiClient.post<NarrativeReference>(`${root(systemId)}/imports`, form,
    { ...config(), headers: { 'Content-Type': undefined } })).data;
}
export async function publishReference(systemId: string, id: string, expectedRevision: number, passages: ReferencePassage[]): Promise<NarrativeReference> {
  return (await apiClient.post<NarrativeReference>(`${root(systemId)}/${encodeURIComponent(id)}/publish`,
    { expectedRevision, reviewed: true, passages }, config())).data;
}
export async function generateProposal(systemId: string, controlId: string, narrativeType: string, expectedVersion: number): Promise<NarrativeProposal> {
  return (await apiClient.post<NarrativeProposal>(`${root(systemId)}/proposals`, { controlId, narrativeType, expectedVersion }, config())).data;
}
export async function reviewProposal(systemId: string, id: string, expectedRevision: number, decision: string, note: string): Promise<NarrativeProposal> {
  return (await apiClient.post<NarrativeProposal>(`${root(systemId)}/proposals/${encodeURIComponent(id)}/review`,
    { expectedRevision, decision, note }, config())).data;
}