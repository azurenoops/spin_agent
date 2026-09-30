import apiClient from './client';

export type NarrativeStatementKind = 'policy' | 'technical';

export interface NarrativeStatementSummary {
  state: string;
  hasContent: boolean;
  hasApprovedContent: boolean;
  proposalId: string | null;
  proposalStatus: string | null;
  isStale: boolean;
}

export interface ControlNarrativeWorkspaceItem {
  id: string;
  controlId: string;
  controlTitle: string;
  family: string;
  implementationStatus: string;
  currentVersion: number;
  approvalStatus: string;
  policy: NarrativeStatementSummary;
  technical: NarrativeStatementSummary;
  nextAction: string;
  nextActionLabel: string;
  nextActionReason: string | null;
}

export interface ControlNarrativeWorkspaceResponse {
  systemId: string;
  counts: {
    needsAttention: number;
    allControls: number;
    approvedStatements: number;
    proposedUpdates: number;
  };
  items: ControlNarrativeWorkspaceItem[];
  permissions: {
    canAuthor: boolean;
    canReview: boolean;
    canManageEvidence: boolean;
  };
}

export interface NarrativeProposalDetail {
  id: string;
  narrativeType: string;
  beforeContent: string;
  proposedContent: string;
  status: string;
  revision: number;
  isStale: boolean;
  canReview: boolean;
  createdAt: string;
  createdBy: string;
  cause: string;
  sourceId: string | null;
  provenance: Record<string, unknown>;
  conflicts: string[];
  missingEvidence: string[];
  reviewReason: string | null;
}

export interface NarrativeStatementDetail {
  currentContent: string | null;
  approvedContent: string | null;
  state: string;
}

export interface NarrativeHistoryItem {
  versionNumber: number;
  status: string;
  authoredBy: string | null;
  authoredAt: string;
  changeReason: string | null;
  reviews: { decision: string; reviewedBy: string; reviewedAt: string; comments: string | null }[];
}

export interface ResponsibilityDependency {
  id: string;
  inheritanceType: string;
  provider: string | null;
  customerResponsibility: string | null;
  source: string | null;
}

export interface ControlNarrativeDetailResponse {
  systemId: string;
  id: string;
  controlId: string;
  controlTitle: string;
  family: string;
  implementationStatus: string;
  approvalStatus: string;
  currentVersion: number;
  statements: {
    policy: NarrativeStatementDetail;
    technical: NarrativeStatementDetail;
  };
  proposals: NarrativeProposalDetail[];
  responsibilities: ResponsibilityDependency[];
  history: NarrativeHistoryItem[];
  permissions: {
    canAuthor: boolean;
    authorReason: string | null;
    canReview: boolean;
    reviewReason: string | null;
    canManageEvidence: boolean;
    evidenceReason: string | null;
  };
}

const object = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string' && value.length > 0;
const nullableText = (value: unknown): value is string | null => value === null || typeof value === 'string';
const count = (value: unknown): value is number => Number.isInteger(value) && Number(value) >= 0;

function isStatementSummary(value: unknown): value is NarrativeStatementSummary {
  return object(value) && text(value.state)
    && typeof value.hasContent === 'boolean'
    && typeof value.hasApprovedContent === 'boolean'
    && nullableText(value.proposalId)
    && nullableText(value.proposalStatus)
    && typeof value.isStale === 'boolean';
}

function isItem(value: unknown): value is ControlNarrativeWorkspaceItem {
  return object(value)
    && ['id', 'controlId', 'controlTitle', 'family', 'implementationStatus', 'approvalStatus', 'nextAction', 'nextActionLabel']
      .every(key => text(value[key]))
    && nullableText(value.nextActionReason)
    && count(value.currentVersion)
    && isStatementSummary(value.policy)
    && isStatementSummary(value.technical);
}

function checkedWorkspace(value: unknown, systemId: string): ControlNarrativeWorkspaceResponse {
  if (!object(value) || value.systemId !== systemId || !object(value.counts) || !object(value.permissions)
    || !count(value.counts.needsAttention) || !count(value.counts.allControls)
    || !count(value.counts.approvedStatements) || !count(value.counts.proposedUpdates)
    || !Array.isArray(value.items) || !value.items.every(isItem)
    || typeof value.permissions.canAuthor !== 'boolean'
    || typeof value.permissions.canReview !== 'boolean'
    || typeof value.permissions.canManageEvidence !== 'boolean') {
    throw new Error('Unexpected or mismatched narrative workspace response.');
  }
  return value as unknown as ControlNarrativeWorkspaceResponse;
}

function isProposal(value: unknown): value is NarrativeProposalDetail {
  return object(value)
    && ['id', 'narrativeType', 'beforeContent', 'proposedContent', 'status', 'createdAt', 'createdBy'].every(key => typeof value[key] === 'string')
    && count(value.revision)
    && typeof value.isStale === 'boolean'
    && typeof value.canReview === 'boolean'
    && text(value.cause) && nullableText(value.sourceId)
    && object(value.provenance)
    && Array.isArray(value.conflicts) && value.conflicts.every(item => typeof item === 'string')
    && Array.isArray(value.missingEvidence) && value.missingEvidence.every(item => typeof item === 'string')
    && nullableText(value.reviewReason);
}

function isStatementDetail(value: unknown): value is NarrativeStatementDetail {
  return object(value) && nullableText(value.currentContent) && nullableText(value.approvedContent)
    && text(value.state);
}

function checkedDetail(value: unknown, systemId: string, controlId: string): ControlNarrativeDetailResponse {
  if (!object(value) || value.systemId !== systemId || value.controlId !== controlId
    || !['id', 'controlTitle', 'family', 'implementationStatus', 'approvalStatus'].every(key => text(value[key]))
    || !count(value.currentVersion)
    || !object(value.statements) || !isStatementDetail(value.statements.policy) || !isStatementDetail(value.statements.technical)
    || !Array.isArray(value.proposals) || !value.proposals.every(isProposal)
    || !Array.isArray(value.responsibilities) || !Array.isArray(value.history)
    || !object(value.permissions)
    || typeof value.permissions.canAuthor !== 'boolean' || !nullableText(value.permissions.authorReason)
    || typeof value.permissions.canReview !== 'boolean' || !nullableText(value.permissions.reviewReason)
    || typeof value.permissions.canManageEvidence !== 'boolean' || !nullableText(value.permissions.evidenceReason)) {
    throw new Error('Unexpected or mismatched narrative detail response.');
  }
  return value as unknown as ControlNarrativeDetailResponse;
}

export async function getControlNarrativeWorkspace(
  systemId: string,
  params?: {
    view?: 'needs-attention' | 'all-controls' | 'approved-statements';
    search?: string;
    family?: string;
    status?: string;
    page?: number;
    pageSize?: number;
  },
  signal?: AbortSignal,
): Promise<ControlNarrativeWorkspaceResponse> {
  const { data } = await apiClient.get<unknown>(
    `/systems/${encodeURIComponent(systemId)}/narrative-workspace`,
    { params, signal },
  );
  return checkedWorkspace(data, systemId);
}

export async function getControlNarrativeDetail(
  systemId: string,
  controlId: string,
  signal?: AbortSignal,
): Promise<ControlNarrativeDetailResponse> {
  const { data } = await apiClient.get<unknown>(
    `/systems/${encodeURIComponent(systemId)}/narrative-workspace/${encodeURIComponent(controlId)}`,
    { signal },
  );
  return checkedDetail(data, systemId, controlId);
}
