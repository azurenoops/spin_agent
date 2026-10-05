import apiClient from './client';

export function systemDesignErrorMessage(reason: unknown): string {
  if (reason && typeof reason === 'object' && 'error' in reason && typeof reason.error === 'string') return reason.error;
  if (reason && typeof reason === 'object' && 'response' in reason && reason.response && typeof reason.response === 'object'
    && 'data' in reason.response && reason.response.data && typeof reason.response.data === 'object'
    && 'error' in reason.response.data && typeof reason.response.data.error === 'string') return reason.response.data.error;
  return reason instanceof Error ? reason.message : 'The request failed. Your entered data is retained; reload saved state before retrying.';
}
async function designRequest<T>(operation: () => Promise<T>): Promise<T> {
  try { return await operation(); }
  catch (reason) { throw new Error(systemDesignErrorMessage(reason)); }
}
export interface DesignSource { type: string; id: string; version: string; provenance: string; reviewState: string; precedence: number; resolutionUrl: string; sourceTenantId?: string | null }
export interface DesignNode {
  id: string; label: string; kind: string; source?: DesignSource | null;
  diagramRole?: 'Architecture' | 'SourceRecord';
  boundaryDisposition: string; environment?: string | null; networkZone?: string | null; provider?: string | null;
  boundaryDefinitionId?: string | null; boundaryRationale?: string | null; securityResponsibility?: string | null;
  boundaryRelationship?: string | null; externalAuthorizationReference?: string | null;
  dataFlowRole?: string | null; functionDescription?: string | null; dataRetention?: string | null; disposalMethod?: string | null;
  networkRole?: string | null; networkSegment?: string | null; networkAddress?: string | null; hostingImpactLevel?: string | null;
  sacaZone?: string | null; sacaRole?: string | null; deploymentScopeNodeId?: string | null;
  deploymentOwner?: string | null; deploymentEvidenceReference?: string | null; deploymentSecurityFunctions?: string | null;
  projectionStatus: string; reviewState: string; sspImpact: string; properties: Record<string, string | null>;
}
export interface DesignEdge {
  id: string; sourceNodeId: string; targetNodeId: string; relationshipType: string; direction: string;
  origin?: 'VerifiedCanonical' | 'UserAuthored' | 'AzureObserved' | 'Imported' | 'AiSuggested' | 'Undetermined';
  purpose?: string | null; informationType?: string | null; classification?: string | null;
  informationTypeId?: string | null; lifecycleStage?: string | null;
  protocolStack?: string | null; standardsReference?: string | null; connectionMedium?: string | null; securityControlReferences?: string | null;
  port?: string | null; protocol?: string | null; service?: string | null; protection?: string | null;
  ppsEntryId?: string | null;
  encryptionState?: string | null; boundaryCrossing: string; interconnectionId?: string | null;
  agreementStatus?: string | null; source?: DesignSource | null; reviewState: string; projectionStatus: string;
}
export interface DesignGroup { id: string; label: string; kind: string; nodeIds: string[] }
export interface DesignGap { id: string; severity: string; explanation: string; recordId: string; view: string; sspImpact: string; owner: string; resolutionUrl: string }
export interface DesignContribution { section: string; recordCount: number; state: string; explanation: string; resolutionUrl: string }
export interface DesignChange { kind: string; recordId: string; before?: string | null; after?: string | null }
export interface ComponentScopeUse {
  source: 'local' | 'provider'; componentId: string; name: string; sourceRevision: string;
  decision: 'Included' | 'Excluded' | 'NeedsConfirmation'; boundaryId: string | null; boundaryName: string | null; usage: string;
  wordingBasis?: { draftId: string; revision: number; sourceHash: string; origin: string; originalWording: string; userEdited: boolean; sourceIds: string[] } | null;
}
export interface SaveComponentScopeRequest {
  expectedRevision: number; source: 'local' | 'provider'; componentId: string; sourceRevision: string;
  decision: ComponentScopeUse['decision']; boundaryId: string | null; usage: string;
  wordingDraftId?: string; wordingDraftRevision?: number;
}
export interface DesignProposal {
  id: string; kind: string; recordId: string; sourceFingerprint: string; state: string;
  originalNode?: DesignNode | null; originalEdge?: DesignEdge | null; resultNode?: DesignNode | null; resultEdge?: DesignEdge | null;
  actor?: string | null; decidedAt?: string | null; reason?: string | null; conflictsWithHigherPrecedence: boolean;
}
export interface SystemDesignGraph {
  tenantId: string; systemId: string; systemName: string; revision: number; approvedRevision?: number | null;
  governanceStatus: 'NotStarted' | 'Draft' | 'UnderReview' | 'Approved' | 'NeedsRevision';
  sourceFingerprint: string; sourcesStale: boolean; synchronizedAt?: string | null;
  lastEditor?: string | null; reviewer?: string | null; reviewerComments?: string | null;
  nodes: DesignNode[]; edges: DesignEdge[]; groups: DesignGroup[]; gaps: DesignGap[]; proposals: DesignProposal[];
  componentScopes?: ComponentScopeUse[];
  availableNodes?: DesignNode[];
  contributions: DesignContribution[]; baselineChanges: DesignChange[];
  actions: { canEdit: boolean; canSubmit: boolean; canWithdraw: boolean; canReview: boolean; canReconcile: boolean; canDeriveDraft?: boolean };
  completenessPercentage: number; sspReadiness: 'Ready' | 'NeedsRevision' | 'Blocked' | 'Missing' | 'Unapproved';
  discoveryState: string; monitoringState: string;
}
export interface SaveSystemDesignRequest { expectedRevision: number; nodes: DesignNode[]; edges: DesignEdge[]; groups: DesignGroup[]; reason: string }
export interface DesignRevisionRequest { expectedRevision: number; reason: string }
export interface DesignReviewRequest extends DesignRevisionRequest { action: 'submit' | 'withdraw' | 'approve' | 'request_revision' | 'derive_draft' }
export interface DesignProposalDecisionRequest extends DesignRevisionRequest { action: 'accept' | 'edit_accept' | 'reject' | 'defer' | 'recover'; node?: DesignNode; edge?: DesignEdge }
export interface DesignHistoryEntry { revision: number; action: string; actor: string; at: string; reason: string; governanceStatus: string; sourceFingerprint: string }
export interface DesignLayout {
  version: number; view: string; positions: Record<string, { x: number; y: number }>;
  collapsedGroups: string[]; edgeRouting: Record<string, string>; visibility: Record<string, boolean>;
  viewport: { x: number; y: number; zoom: number };
}
export interface ApprovedSystemDesign { graph: SystemDesignGraph; revision: number; approvedBy: string; approvedAt: string; snapshotHash: string; sourceFingerprint: string; sourcesStale: boolean }
const path = (id: string) => `/systems/${encodeURIComponent(id)}/design`;
export const getSystemDesign = (id: string, signal?: AbortSignal) => designRequest(async () => (await apiClient.get<SystemDesignGraph>(path(id), { signal })).data);
export const saveComponentScope = (id: string, body: SaveComponentScopeRequest, signal?: AbortSignal) => designRequest(async () => {
  const result = (await apiClient.put<SystemDesignGraph>(`${path(id)}/component-scope`, body, { signal })).data;
  const scope = Array.isArray(result?.componentScopes) ? result.componentScopes.find(value => value?.source === body.source && value.componentId === body.componentId) : undefined;
  if (!result || result.systemId !== id || result.governanceStatus !== 'Draft' || result.revision !== body.expectedRevision + 1
    || !scope || scope.decision !== body.decision || scope.boundaryId !== body.boundaryId
    || scope.usage !== body.usage.trim() || scope.sourceRevision !== body.sourceRevision
    || body.wordingDraftId && (scope.wordingBasis?.draftId !== body.wordingDraftId || scope.wordingBasis.revision !== body.wordingDraftRevision))
    throw new Error('The server did not confirm the requested scope draft. Your entered data is retained; reload saved state before retrying.');
  return result;
});
export const saveSystemDesign = async (id: string, body: SaveSystemDesignRequest) => (await apiClient.put<SystemDesignGraph>(path(id), body)).data;
export const reviewSystemDesign = async (id: string, body: DesignReviewRequest) => (await apiClient.post<SystemDesignGraph>(`${path(id)}/review`, body)).data;
export const reconcileSystemDesign = async (id: string, body: DesignRevisionRequest) => (await apiClient.post<SystemDesignGraph>(`${path(id)}/reconcile`, body)).data;
export const buildSystemDesign = async (id: string, body: DesignRevisionRequest) => (await apiClient.post<SystemDesignGraph>(`${path(id)}/build`, body)).data;
export const decideDesignProposal = async (id: string, proposalId: string, body: DesignProposalDecisionRequest) =>
  (await apiClient.post<SystemDesignGraph>(`${path(id)}/proposals/${encodeURIComponent(proposalId)}/decision`, body)).data;
export const getSystemDesignHistory = async (id: string) => (await apiClient.get<DesignHistoryEntry[]>(`${path(id)}/history`)).data;
export const getSystemDesignRevision = async (id: string, revision: number) => (await apiClient.get<SystemDesignGraph>(`${path(id)}/history/${revision}`)).data;
export const getApprovedSystemDesign = (id: string, signal?: AbortSignal) => designRequest(async () => (await apiClient.get<ApprovedSystemDesign | null>(`${path(id)}/approved`, { signal })).data);
export const getDesignLayout = async (id: string, view: string) => (await apiClient.get<DesignLayout>(`${path(id)}/layout/${encodeURIComponent(view)}`)).data;
export const saveDesignLayout = async (id: string, expectedVersion: number, layout: DesignLayout) =>
  (await apiClient.put<DesignLayout>(`${path(id)}/layout`, { expectedVersion, layout })).data;
