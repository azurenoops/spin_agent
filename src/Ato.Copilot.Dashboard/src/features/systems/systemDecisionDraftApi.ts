import apiClient from '../../api/client';

export interface ExternalDecisionInput {
  decisionType: string; decisionDate: string; expirationDate: string | null; residualRiskLevel: string;
  sourceEvidenceId: string; expectedSourceHash: string; issuingAuthority: string;
  baselinePackageId: string; expectedPackageHash: string; termsAndConditions: string;
  makeCurrent: boolean; expectedActiveDecisionId: string | null;
}
export interface ExternalDecisionRecord {
  id: string; decisionType: string; decisionDate: string; expirationDate: string | null; isActive: boolean;
  externalIssuingAuthority: string | null; sourceEvidenceId: string | null; sourceEvidenceHash: string | null;
  baselinePackageId: string | null; baselinePackageHash: string | null; recordedBy: string | null; recordedAt: string | null;
  termsAndConditions: string | null;
}
export interface ExternalDecisionContext {
  systemId: string; canRecord: boolean; activeDecisionId: string | null; page: number; pageSize: number;
  sourceTotal: number; packageTotal: number; recordTotal: number;
  sourceEvidence: { id: string; fileName: string | null; contentHash: string }[];
  completedPackages: { id: string; contentHash: string; generatedAt: string; purpose: number | string }[];
  records: ExternalDecisionRecord[];
}
export interface SapDraft {
  systemId: string; sapId: string | null; status: string; canEdit: boolean; canCreate: boolean;
  title?: string; assessmentLead?: string | null; scopeNotes?: string | null; assessmentApproach?: string | null; draftHash?: string;
}
export interface SapDraftUpdate { title: string; assessmentLead: string; scopeNotes: string; assessmentApproach: string; expectedContentHash: string }

export function documentActionError(reason: unknown): string {
  if (reason instanceof Error) return reason.message;
  if (reason && typeof reason === 'object' && 'error' in reason && typeof reason.error === 'string') return reason.error;
  return 'The document action could not be confirmed. Refresh and review the current record.';
}
export async function getExternalDecisionContext(systemId: string, page = 1, signal?: AbortSignal): Promise<ExternalDecisionContext> {
  const { data } = await apiClient.get<ExternalDecisionContext>(`/systems/${encodeURIComponent(systemId)}/authorization/record-context`, { params: { page }, signal });
  if (data?.systemId !== systemId || data.page !== page || typeof data.canRecord !== 'boolean'
    || !Array.isArray(data.sourceEvidence) || !Array.isArray(data.completedPackages) || !Array.isArray(data.records))
    throw new Error('The decision context does not match this system and page.');
  return data;
}
export async function recordExternalDecision(systemId: string, input: ExternalDecisionInput): Promise<{ id: string }> {
  const { data } = await apiClient.post<{ id: string }>(`/systems/${encodeURIComponent(systemId)}/authorization/records`, input);
  if (!data?.id) throw new Error('The external decision write was not confirmed.');
  return data;
}
export async function getSapDraft(systemId: string, signal?: AbortSignal): Promise<SapDraft> {
  const { data } = await apiClient.get<SapDraft>(`/systems/${encodeURIComponent(systemId)}/sap/draft`, { baseURL: '/api/v1', signal });
  if (data?.systemId !== systemId || typeof data.canEdit !== 'boolean' || typeof data.canCreate !== 'boolean')
    throw new Error('The assessment draft does not match the selected system.');
  return data;
}
export async function updateSapDraft(systemId: string, sapId: string, input: SapDraftUpdate): Promise<SapDraft> {
  const { data } = await apiClient.put<SapDraft>(`/systems/${encodeURIComponent(systemId)}/sap/${encodeURIComponent(sapId)}/draft`, input, { baseURL: '/api/v1' });
  if (data?.systemId !== systemId || data.sapId !== sapId || !data.draftHash) throw new Error('The saved assessment draft could not be verified.');
  return data;
}
