import { packageRequest } from '../package-imports/request';
import type { Page } from './types';

export interface EvidenceShareTarget {
  assignmentId: string; assignmentRevision: number; targetTenantId: string; systemId: string; systemName: string;
}
export interface EvidenceShare {
  shareId: string; providerId: string; offeringId: string; evidenceId: string; assignmentId: string;
  targetTenantId: string; systemId: string; version: number; previousVersionId: string | null;
  summary: string; sourceSha256: string; contentHash: string; approvedBy: string; approvedAt: string;
  revision: number; revokedAt: string | null;
  evidenceRevision?: number; assignmentRevision?: number; permission?: 'ApprovedSummaryOnly'; privateAttachmentAccess?: false;
}
const root = (id: string) => `/api/csp/offerings/${encodeURIComponent(id)}`;
const mission = (system: string) => `/api/dashboard/systems/${encodeURIComponent(system)}/provider-evidence`;
export const listShareTargets = (offering: string, page = 1, signal?: AbortSignal) =>
  packageRequest<Page<EvidenceShareTarget>>({ url: `${root(offering)}/evidence-share-targets`, params: { page, pageSize: 25 }, signal });
export const listEvidenceShares = (offering: string, evidence: string, assignmentId: string, page = 1, signal?: AbortSignal) =>
  packageRequest<Page<EvidenceShare>>({ url: `${root(offering)}/evidence/${encodeURIComponent(evidence)}/shares`,
    params: { assignmentId: assignmentId || undefined, page, pageSize: 25 }, signal });
export const approveEvidenceShare = (offering: string, evidence: string, data: {
  assignmentId: string; expectedAssignmentRevision: number; expectedEvidenceRevision: number;
  summary: string; previousVersionId: string | null; version: number;
}, key: string) => packageRequest<EvidenceShare>({ url: `${root(offering)}/evidence/${encodeURIComponent(evidence)}/shares`,
  method: 'POST', data, headers: { 'Idempotency-Key': key } });
export const revokeEvidenceShare = (offering: string, share: string, data: { expectedRevision: number; rationale: string }, key: string) =>
  packageRequest<EvidenceShare>({ url: `${root(offering)}/evidence-shares/${encodeURIComponent(share)}/revoke`,
    method: 'POST', data, headers: { 'Idempotency-Key': key } });
export const listMissionEvidence = (system: string, page = 1, signal?: AbortSignal) =>
  packageRequest<Page<EvidenceShare>>({ url: mission(system), params: { page, pageSize: 25 }, signal });
export const summaryUrl = (system: string, share: string) => `${mission(system)}/${encodeURIComponent(share)}/content`;
