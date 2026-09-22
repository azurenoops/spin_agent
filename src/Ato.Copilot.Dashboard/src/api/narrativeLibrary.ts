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
  capabilities: { id: string; name: string }[];
}
const root = (systemId: string) => `/systems/${encodeURIComponent(systemId)}/narrative-library`;
const config = () => ({ baseURL: (apiClient.defaults.baseURL ?? '/api/dashboard').replace(/\/dashboard\/?$/, '') });

export async function getReferences(systemId: string): Promise<NarrativeReference[]> {
  return (await apiClient.get<NarrativeReference[]>(root(systemId), config())).data;
}
export async function getProposals(systemId: string): Promise<NarrativeProposal[]> {
  return (await apiClient.get<NarrativeProposal[]>(`${root(systemId)}/proposals`, config())).data;
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