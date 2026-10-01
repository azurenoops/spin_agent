import apiClient from './client';

export interface DesignSource { type: string; id: string; version: string; provenance: string; reviewState: string; precedence: number; resolutionUrl: string; sourceTenantId?: string | null }
export interface DesignNode {
  id: string; label: string; kind: string; source?: DesignSource | null;
  diagramRole?: 'Architecture' | 'SourceRecord';
  boundaryDisposition: string; environment?: string | null; networkZone?: string | null; provider?: string | null;
  projectionStatus: string; reviewState: string; sspImpact: string; properties: Record<string, string | null>;
}
export interface DesignEdge {
  id: string; sourceNodeId: string; targetNodeId: string; relationshipType: string; direction: string;
  origin?: 'VerifiedCanonical' | 'UserAuthored' | 'AzureObserved' | 'Imported' | 'AiSuggested' | 'Undetermined';
  purpose?: string | null; informationType?: string | null; classification?: string | null;
  port?: string | null; protocol?: string | null; service?: string | null; protection?: string | null;
  ppsEntryId?: string | null;
  encryptionState?: string | null; boundaryCrossing: string; interconnectionId?: string | null;
  agreementStatus?: string | null; source?: DesignSource | null; reviewState: string; projectionStatus: string;
}
export interface DesignGroup { id: string; label: string; kind: string; nodeIds: string[] }
export interface DesignGap { id: string; severity: string; explanation: string; recordId: string; view: string; sspImpact: string; owner: string; resolutionUrl: string }
export interface DesignContribution { section: string; recordCount: number; state: string; explanation: string; resolutionUrl: string }
export interface DesignChange { kind: string; recordId: string; before?: string | null; after?: string | null }
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
export const getSystemDesign = async (id: string) => (await apiClient.get<SystemDesignGraph>(path(id))).data;
export const saveSystemDesign = async (id: string, body: SaveSystemDesignRequest) => (await apiClient.put<SystemDesignGraph>(path(id), body)).data;
export const reviewSystemDesign = async (id: string, body: DesignReviewRequest) => (await apiClient.post<SystemDesignGraph>(`${path(id)}/review`, body)).data;
export const reconcileSystemDesign = async (id: string, body: DesignRevisionRequest) => (await apiClient.post<SystemDesignGraph>(`${path(id)}/reconcile`, body)).data;
export const buildSystemDesign = async (id: string, body: DesignRevisionRequest) => (await apiClient.post<SystemDesignGraph>(`${path(id)}/build`, body)).data;
export const decideDesignProposal = async (id: string, proposalId: string, body: DesignProposalDecisionRequest) =>
  (await apiClient.post<SystemDesignGraph>(`${path(id)}/proposals/${encodeURIComponent(proposalId)}/decision`, body)).data;
export const getSystemDesignHistory = async (id: string) => (await apiClient.get<DesignHistoryEntry[]>(`${path(id)}/history`)).data;
export const getSystemDesignRevision = async (id: string, revision: number) => (await apiClient.get<SystemDesignGraph>(`${path(id)}/history/${revision}`)).data;
export const getApprovedSystemDesign = async (id: string) => (await apiClient.get<ApprovedSystemDesign | null>(`${path(id)}/approved`)).data;
export const getDesignLayout = async (id: string, view: string) => (await apiClient.get<DesignLayout>(`${path(id)}/layout/${encodeURIComponent(view)}`)).data;
export const saveDesignLayout = async (id: string, expectedVersion: number, layout: DesignLayout) =>
  (await apiClient.put<DesignLayout>(`${path(id)}/layout`, { expectedVersion, layout })).data;
