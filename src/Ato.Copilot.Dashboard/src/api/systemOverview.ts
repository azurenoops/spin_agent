import api from './client';
import type { DraftSource } from './responsibilityDrafts';
import type { PackageReadinessAction, PackageReadinessCheck, PackageReadinessPage, PackageReadinessWorkspace } from './packageReadiness';
import { packageSourceHref } from '../features/systems/packageReadinessNavigation';

export interface OverviewFinding {
  id: string; severity: 'Error' | 'Warning'; category: string; artifactType: string | null;
  description: string; remediation: string | null; controlId: string | null; recordId: string | null;
}
export interface OverviewWorkGroup {
  id: string; title: string; category: string; owner: PackageReadinessCheck['recordedOwner'];
  action: PackageReadinessAction; rmfPhases: string[]; documents: string[]; controls: string[];
  total: number; blocking: number; warnings: number; priorityReason: string | null;
  findings: PackageReadinessPage<OverviewFinding>;
}
export interface OverviewWork {
  systemId: string; purpose: 'InitialSubmission'; selectionHash: string; runId: string; findingsAvailable: boolean;
  counts: { total: number; blocking: number; warnings: number }; actorPersonId: string | null;
  groups: PackageReadinessPage<OverviewWorkGroup>; recommendedGroupId: string | null;
}
export interface OverviewWorkQuery {
  limit?: number; offset?: number; mine?: boolean; groupId?: string; findingOffset?: number;
}
export interface OverviewExplanation {
  systemId: string; runId: string; groupId: string; sourceHash: string; content: string;
  sources: DraftSource[]; questions: string[]; origin: 'AI proposed';
}
export type OverviewAiMode = 'Explain' | 'SuggestNextAction' | 'MapRequirements' | 'DraftResponses';
export function overviewError(reason: unknown): string {
  if (reason && typeof reason === 'object' && 'error' in reason && typeof reason.error === 'string') return reason.error;
  return reason instanceof Error ? reason.message : 'The overview request could not be verified. Previously loaded records are retained.';
}
function valid(assertion: unknown): asserts assertion {
  if (!assertion) throw new Error('The overview service returned incomplete or mismatched records. Reload to verify saved state.');
}
const count = (value: number) => Number.isSafeInteger(value) && value >= 0;
const texts = (value: string[]) => Array.isArray(value) && value.every(item => typeof item === 'string');
const root = (id: string) => `/systems/${encodeURIComponent(id)}/package-readiness`;
export async function getOverviewWork(id: string, runId: string, query: OverviewWorkQuery = {}, signal?: AbortSignal): Promise<OverviewWork> {
  const limit = query.limit ?? 10;
  const offset = query.offset ?? 0;
  valid(count(limit) && limit > 0 && limit <= 50 && count(offset) && count(query.findingOffset ?? 0));
  const { data } = await api.get<OverviewWork>(`${root(id)}/runs/${encodeURIComponent(runId)}/work`, {
    params: { purpose: 'InitialSubmission', ...query, limit, offset }, signal,
  });
  valid(data && data.systemId === id && data.runId === runId && data.purpose === 'InitialSubmission'
    && typeof data.findingsAvailable === 'boolean' && typeof data.selectionHash === 'string'
    && (data.actorPersonId === null || typeof data.actorPersonId === 'string')
    && data.counts && Object.values(data.counts).every(count)
    && data.counts.total === data.counts.blocking + data.counts.warnings
    && data.groups && Array.isArray(data.groups.items) && count(data.groups.totalCount)
    && data.groups.items.length <= limit && data.groups.limit === limit && data.groups.offset === offset);
  const ids = new Set<string>();
  const findingIds = new Set<string>();
  for (const group of data.groups.items) {
    valid(group && typeof group.id === 'string' && !ids.has(group.id)
      && [group.title, group.category].every(value => typeof value === 'string')
      && [group.total, group.blocking, group.warnings].every(count) && group.total === group.blocking + group.warnings
      && texts(group.rmfPhases) && texts(group.documents) && texts(group.controls)
      && group.action && typeof group.action.canView === 'boolean'
      && (!group.action.canView || typeof group.action.path === 'string' && packageSourceHref(id, group.action.path, '') !== null)
      && group.findings && group.findings.totalCount === group.total && group.findings.limit === 20
      && count(group.findings.offset) && Array.isArray(group.findings.items) && group.findings.items.length <= 20);
    ids.add(group.id);
    if (group.owner) valid([group.owner.personId, group.owner.displayName, group.owner.assignmentId].every(value => typeof value === 'string' && !!value));
    valid(!query.mine || group.owner?.personId === data.actorPersonId);
    for (const finding of group.findings.items) {
      valid(finding && typeof finding.id === 'string' && !findingIds.has(finding.id)
        && ['Error', 'Warning'].includes(finding.severity) && typeof finding.description === 'string');
      findingIds.add(finding.id);
    }
  }
  if (data.findingsAvailable && !query.mine && !query.groupId && offset === 0 && data.groups.items.length === data.groups.totalCount) {
    valid(data.groups.items.reduce((total, group) => total + group.total, 0) === data.counts.total
      && data.groups.items.reduce((total, group) => total + group.blocking, 0) === data.counts.blocking
      && data.groups.items.reduce((total, group) => total + group.warnings, 0) === data.counts.warnings);
  }
  return data;
}
export async function confirmOverviewPhase(id: string, input: { phase: string; expectedPhase: string; notes: string }, signal?: AbortSignal) {
  const { data } = await api.post<PackageReadinessWorkspace['rmf']>(`${root(id)}/rmf-phase`, input, { signal });
  valid(data && data.phase === input.phase && data.confirmed === true && typeof data.source === 'string'
    && typeof data.actor === 'string' && typeof data.recordedAt === 'string' && Number.isFinite(Date.parse(data.recordedAt)));
  return data;
}
export async function explainOverviewGroup(id: string, runId: string, input: {
  groupId: string; controlId: string | null; scopeId: string | null; mode?: OverviewAiMode;
}, signal?: AbortSignal): Promise<OverviewExplanation> {
  const { data } = await api.post<OverviewExplanation>(`${root(id)}/runs/${encodeURIComponent(runId)}/work/explain`, input, { signal });
  valid(data && data.systemId === id && data.runId === runId && data.groupId === input.groupId
    && data.origin === 'AI proposed' && typeof data.content === 'string' && !!data.content
    && typeof data.sourceHash === 'string' && texts(data.questions) && Array.isArray(data.sources)
    && data.sources.every(source => source && ['id', 'title', 'origin', 'version', 'content'].every(key => typeof source[key as keyof DraftSource] === 'string')
      && (source.href === null || typeof source.href === 'string' && source.href.startsWith(`/systems/${encodeURIComponent(id)}/`))));
  return data;
}
