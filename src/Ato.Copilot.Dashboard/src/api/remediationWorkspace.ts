import apiClient from './client';
import { getPoamWorkspace, type PoamWorkspace } from './poamWorkspace';

export interface RemediationSource {
  resultId: string | null; name: string; planId: string | null; planRevision: number | null;
}
export interface RemediationPermissions {
  canCreateFinding: boolean; canCreateTask: boolean; canManageRemediation: boolean; canVerify: boolean;
  reason: string | null;
  canMoveTasks?: boolean; canMoveAnyTasks?: boolean;
}
export interface RemediationFinding {
  id: string; title: string; description: string; controlId: string; severity: string; status: string;
  ownerName: string | null; workStatus: string; readyToVerify: boolean; isClosed: boolean;
  source: RemediationSource | null; taskIds: string[]; poamIds: string[]; revision: string;
}
export interface LinkedRemediationTask {
  id: string; taskNumber: string; title: string; description: string; controlId: string; severity: string;
  status: string; assigneeId: string | null; assigneeName: string | null; dueDate: string | null;
  findingId: string | null; poamIds: string[]; rowVersion: string; affectedResources: string[];
  validationCriteria: string | null; remediationScript: string | null; remediationScriptType: string | null;
  verificationStatus?: string; verificationNotes?: string | null; evidence?: RemediationEvidence[];
  history?: RemediationHistory[]; allowedTransitions?: string[];
}
export interface RemediationPoamReference {
  id: string; weakness: string; controlId: string; status: string; owner: string;
  scheduledCompletionDate: string; rowVersion: string; taskIds: string[];
}
export interface RemediationExceptionReference {
  id: string; type: string; status: string; controlId: string; justification: string;
  decisionAuthority: string | null; conditions: string | null; expiresAt: string | null; reviewedAt: string | null;
  reviewerId?: string | null;
}
export interface RemediationEvidence {
  id: string; name: string; collectedAt: string | null; hash: string | null; downloadUrl: string | null;
  linkedAt?: string;
}
export interface RemediationHistory {
  at: string; actor: string | null; action: string; description: string;
}
export interface RemediationWorkspace {
  systemId: string; findings: RemediationFinding[]; tasks: LinkedRemediationTask[];
  poams: RemediationPoamReference[]; owners: { id: string; name: string }[];
  permissions: RemediationPermissions; warnings: string[];
  exceptions?: (RemediationExceptionReference & { findingId: string | null; poamEntryId: string | null })[];
}
export interface RemediationFindingDetail {
  systemId: string; finding: RemediationFinding; tasks: LinkedRemediationTask[];
  poams: RemediationPoamReference[]; exceptions: RemediationExceptionReference[];
  evidence: RemediationEvidence[]; history: RemediationHistory[];
  permissions: RemediationPermissions; verificationBlockers: string[];
}
export interface RemediationTaskDetail {
  systemId: string; task: LinkedRemediationTask; poams: RemediationPoamReference[];
  evidence: RemediationEvidence[]; history: RemediationHistory[];
  permissions: RemediationPermissions; allowedTransitions: string[]; verificationBlockers: string[];
}
export interface RemediationTaskRequest {
  title: string; description: string; controlId: string; severity: string; assigneeId: string | null;
  dueDate: string | null; affectedResources: string[]; validationCriteria: string | null;
  findingId?: string | null; expectedRowVersion?: string; operationId?: string;
  assigneeName?: string | null;
}

export function remediationWorkspaceError(reason: unknown): string {
  if (reason && typeof reason === 'object') {
    if ('error' in reason) {
      const error = reason.error;
      if (typeof error === 'string') return error;
      if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string') return error.message;
    }
    if ('response' in reason && reason.response && typeof reason.response === 'object' && 'data' in reason.response)
      return remediationWorkspaceError(reason.response.data);
  }
  return reason instanceof Error ? reason.message : 'The remediation request could not be confirmed. Refresh the retained records before retrying.';
}
const root = (systemId: string) => `/systems/${encodeURIComponent(systemId)}/remediation-workspace`;
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string';
const nullableText = (value: unknown) => value === null || text(value);
const strings = (value: unknown): value is string[] => Array.isArray(value) && value.every(text);
function records(value: unknown, validate: (item: unknown) => boolean): boolean {
  if (!Array.isArray(value) || !value.every(item => record(item) && text(item.id) && item.id.length > 0 && validate(item))) return false;
  return new Set(value.map(item => item.id)).size === value.length;
}
function finding(value: unknown): boolean {
  return record(value) && ['id', 'title', 'description', 'controlId', 'severity', 'status', 'workStatus', 'revision'].every(key => text(value[key]))
    && nullableText(value.ownerName) && typeof value.readyToVerify === 'boolean' && typeof value.isClosed === 'boolean'
    && strings(value.taskIds) && strings(value.poamIds)
    && (value.source === null || record(value.source) && text(value.source.name) && nullableText(value.source.resultId)
      && nullableText(value.source.planId) && (value.source.planRevision === null || Number.isInteger(value.source.planRevision)));
}
function task(value: unknown): boolean {
  return record(value) && ['id', 'taskNumber', 'title', 'description', 'controlId', 'severity', 'status', 'rowVersion'].every(key => text(value[key]))
    && ['assigneeId', 'assigneeName', 'dueDate', 'findingId', 'validationCriteria', 'remediationScript', 'remediationScriptType'].every(key => nullableText(value[key]))
    && strings(value.poamIds) && strings(value.affectedResources);
}
function poam(value: unknown): boolean {
  return record(value) && ['id', 'weakness', 'controlId', 'status', 'owner', 'scheduledCompletionDate', 'rowVersion'].every(key => text(value[key]))
    && strings(value.taskIds);
}
function evidence(value: unknown): boolean {
  return record(value) && text(value.id) && text(value.name)
    && ['collectedAt', 'hash', 'downloadUrl'].every(key => nullableText(value[key]));
}
function exception(value: unknown): boolean {
  return record(value) && ['id', 'type', 'status', 'controlId', 'justification'].every(key => text(value[key]))
    && ['decisionAuthority', 'conditions', 'expiresAt', 'reviewedAt'].every(key => nullableText(value[key]));
}
function history(value: unknown): boolean {
  return Array.isArray(value) && value.every(item => record(item) && text(item.at) && nullableText(item.actor)
    && text(item.action) && text(item.description));
}
function permissions(value: unknown): value is RemediationPermissions {
  return record(value) && ['canCreateFinding', 'canCreateTask', 'canManageRemediation', 'canVerify'].every(key => typeof value[key] === 'boolean')
    && nullableText(value.reason);
}
function scoped(value: unknown, systemId: string): asserts value is Record<string, unknown> {
  if (!record(value) || value.systemId !== systemId || !permissions(value.permissions))
    throw new Error('The remediation API did not return authorized records for this system. Refresh after the server is available.');
}
export async function getRemediationWorkspace(systemId: string, signal?: AbortSignal): Promise<RemediationWorkspace> {
  const raw = await getPoamWorkspace(systemId, signal);
  const data = projectRemediationWorkspace(raw);
  scoped(data, systemId);
  if (!records(data.findings, finding) || !records(data.tasks, task) || !records(data.poams, poam)
    || !records(data.owners, value => record(value) && text(value.name)) || !strings(data.warnings))
    throw new Error('The remediation queue response is incomplete. No counts can be confirmed.');
  return data;
}
export async function getRemediationFinding(systemId: string, id: string, signal?: AbortSignal): Promise<RemediationFindingDetail> {
  const workspace = await getRemediationWorkspace(systemId, signal);
  const item = workspace.findings.find(item => item.id === id);
  if (!item) throw new Error('Finding details are unavailable or belong to a different record.');
  const tasks = workspace.tasks.filter(task => item.taskIds.includes(task.id));
  const evidenceItems = Array.from(new Map(tasks.flatMap(task => task.evidence ?? []).map(item => [item.id, item])).values());
  const data: RemediationFindingDetail = { systemId, finding: item, tasks,
    poams: workspace.poams.filter(poam => item.poamIds.includes(poam.id)),
    exceptions: (workspace.exceptions ?? []).filter(exception => exception.findingId === id || item.poamIds.includes(exception.poamEntryId ?? '')),
    evidence: evidenceItems, history: tasks.flatMap(task => task.history ?? []), permissions: workspace.permissions,
    verificationBlockers: ['Record verification on the applicable corrective task. Finding disposition remains a separate assessment or exception decision.'] };
  scoped(data, systemId);
  if (data.finding?.id !== id || !finding(data.finding) || !records(data.tasks, task) || !records(data.poams, poam)
    || !records(data.evidence, evidence) || !history(data.history) || !records(data.exceptions, exception)
    || !strings(data.verificationBlockers)) throw new Error('Finding details are incomplete or belong to a different record.');
  return data;
}
export async function getRemediationTask(systemId: string, id: string, signal?: AbortSignal): Promise<RemediationTaskDetail> {
  const workspace = await getRemediationWorkspace(systemId, signal);
  const item = workspace.tasks.find(item => item.id === id);
  if (!item) throw new Error('Task details are unavailable or belong to a different record.');
  const data: RemediationTaskDetail = { systemId, task: item, poams: workspace.poams.filter(poam => item.poamIds.includes(poam.id)),
    evidence: item.evidence ?? [], history: item.history ?? [], permissions: workspace.permissions,
    allowedTransitions: item.allowedTransitions ?? [], verificationBlockers: [] };
  scoped(data, systemId);
  if (data.task?.id !== id || !task(data.task) || !records(data.poams, poam) || !records(data.evidence, evidence)
    || !history(data.history) || !strings(data.allowedTransitions) || !strings(data.verificationBlockers))
    throw new Error('Task details are incomplete or belong to a different record.');
  return data;
}
export async function createRemediationFinding(systemId: string, request: {
  operationId: string; title: string; description: string; controlId: string; severity: string;
}): Promise<void> {
  const { data } = await apiClient.post<unknown>(`${root(systemId)}/findings`, { requestId: request.operationId,
    title: request.title, description: request.description, controlId: request.controlId, severity: request.severity });
  confirmed(data);
}
export async function createRemediationTask(systemId: string, request: RemediationTaskRequest): Promise<void> {
  const { data } = await apiClient.post<unknown>(`${root(systemId)}/tasks`, { requestId: request.operationId, title: request.title,
    description: request.description, controlId: request.controlId, severity: request.severity,
    findingId: request.findingId, dueDate: request.dueDate });
  confirmed(data);
}
export async function saveRemediationTask(systemId: string, id: string, request: RemediationTaskRequest): Promise<void> {
  const { data } = await apiClient.put<unknown>(`${root(systemId)}/tasks/${encodeURIComponent(id)}`, { rowVersion: request.expectedRowVersion,
    title: request.title, description: request.description, dueDate: request.dueDate,
    assigneeId: request.assigneeId, assigneeName: request.assigneeName });
  confirmed(data, id);
}
export async function moveRemediationTask(systemId: string, id: string, request: {
  expectedRowVersion: string; status: string; comment: string;
}): Promise<void> {
  const { data } = await apiClient.post<unknown>(`${root(systemId)}/tasks/${encodeURIComponent(id)}/move`, { rowVersion: request.expectedRowVersion, status: request.status, comment: request.comment });
  confirmed(data, id);
}
export async function linkRemediationPoamTask(systemId: string, poamId: string, taskId: string, request: {
  expectedPoamRevision: string; expectedTaskRevision: string;
}): Promise<void> {
  const { data } = await apiClient.put<unknown>(`${root(systemId)}/poams/${encodeURIComponent(poamId)}/tasks/${encodeURIComponent(taskId)}`, request);
  confirmedPair(data, poamId, taskId);
}
export async function unlinkRemediationPoamTask(systemId: string, poamId: string, taskId: string, request: {
  expectedPoamRevision: string; expectedTaskRevision: string;
}): Promise<void> {
  const { data } = await apiClient.delete<unknown>(`${root(systemId)}/poams/${encodeURIComponent(poamId)}/tasks/${encodeURIComponent(taskId)}`, { data: request });
  confirmedPair(data, poamId, taskId);
}
export async function verifyRemediationTask(systemId: string, id: string, request: {
  rowVersion: string; status: 'Passed' | 'Failed'; notes: string;
}): Promise<void> {
  const { data } = await apiClient.post<unknown>(`${root(systemId)}/tasks/${encodeURIComponent(id)}/verify`, request);
  confirmed(data, id);
}
export async function linkRemediationEvidence(systemId: string, id: string, request: { rowVersion: string; evidenceId: string }): Promise<void> {
  const { data } = await apiClient.post<unknown>(`${root(systemId)}/tasks/${encodeURIComponent(id)}/evidence`, request);
  confirmed(data, id);
}
export async function linkRemediationFindingTask(systemId: string, findingId: string, taskId: string, rowVersion: string): Promise<void> {
  const { data } = await apiClient.put<unknown>(`${root(systemId)}/findings/${encodeURIComponent(findingId)}/tasks/${encodeURIComponent(taskId)}`, { rowVersion });
  if (!record(data) || data.findingId !== findingId || data.taskId !== taskId)
    throw new Error('The finding-to-task link could not be confirmed. Refresh before retrying.');
}
function confirmed(data: unknown, id?: string) {
  if (!record(data) || !text(data.id) || !data.id || id !== undefined && data.id !== id)
    throw new Error('The saved record could not be confirmed. Refresh before retrying the same operation.');
}
function confirmedPair(data: unknown, poamId: string, taskId: string) {
  if (!record(data) || data.poamId !== poamId || data.taskId !== taskId)
    throw new Error('The relationship change could not be confirmed. Refresh before retrying.');
}

function projectRemediationWorkspace(raw: PoamWorkspace): RemediationWorkspace {
  const permissions: RemediationPermissions = {
    canCreateFinding: raw.permissions.canManageRemediation, canCreateTask: raw.permissions.canCreateTasks,
    canManageRemediation: raw.permissions.canManageRemediation, canVerify: raw.permissions.canManageRemediation,
    canMoveTasks: raw.permissions.canMoveTasks, canMoveAnyTasks: raw.permissions.canMoveAnyTasks, reason: raw.permissions.reason,
  };
  const tasks: LinkedRemediationTask[] = raw.tasks.map(task => {
    if (!strings(task.affectedResources) || !nullableText(task.validationCriteria)
      || !nullableText(task.remediationScript) || !nullableText(task.remediationScriptType))
      throw new Error('Retained task scope and guidance could not be loaded. Refresh after the API is updated.');
    return {
    ...task, affectedResources: task.affectedResources, validationCriteria: task.validationCriteria ?? null,
    remediationScript: task.remediationScript ?? null, remediationScriptType: task.remediationScriptType ?? null,
    evidence: task.evidence.map(item => ({ id: item.id, name: item.name, collectedAt: null,
      linkedAt: item.linkedAt, hash: item.contentHash, downloadUrl: null })),
    history: task.history.map(item => ({ at: item.at, actor: item.actor, action: item.eventType,
      description: item.details ?? [item.oldValue, item.newValue].filter(Boolean).join(' → ') })),
  }; });
  const findings: RemediationFinding[] = raw.findings.map(item => {
    const linked = tasks.filter(task => item.taskIds.includes(task.id));
    const ready = linked.some(task => task.status === 'InReview' && task.verificationStatus !== 'Passed');
    const closed = ['Remediated', 'Accepted', 'FalsePositive'].includes(item.status);
    const names = Array.from(new Set(linked.map(task => task.assigneeName).filter((name): name is string => !!name)));
    return { id: item.id, title: item.title, description: item.description, controlId: item.controlId,
      severity: item.severity, status: item.status, taskIds: item.taskIds, poamIds: item.poamIds, revision: '',
      ownerName: linked.length && linked.every(task => task.assigneeId) ? names.join(', ') || 'Assigned owner' : null,
      workStatus: !linked.length ? 'Not assigned' : ready ? 'Ready to verify' : linked.every(task => task.status === 'Done')
        ? 'Work completed' : linked.some(task => task.status === 'Blocked') ? 'Blocked' : 'Corrective work',
      readyToVerify: !closed && ready, isClosed: closed,
      source: item.provenance ? { name: item.provenance.sourceName, planId: item.provenance.plan?.id ?? null,
        planRevision: item.provenance.plan?.revision ?? null, resultId: item.provenance.sourceType === 'Manual' ? null
          : `${item.importRecordId ? 'import' : 'assessment'}:${item.provenance.sourceId}` } : null };
  });
  const poams: RemediationPoamReference[] = raw.poams.map(item => {
    if (!text(item.pointOfContact) || !text(item.scheduledCompletionDate)) throw new Error('The POA&M reference is missing its retained owner or due date.');
    return { id: item.id, weakness: item.weakness, controlId: item.securityControlNumber, status: item.status,
      owner: item.pointOfContact, scheduledCompletionDate: item.scheduledCompletionDate, rowVersion: item.rowVersion, taskIds: item.taskIds };
  });
  if (!Array.isArray(raw.owners) || !records(raw.owners, value => record(value) && text(value.name)))
    throw new Error('Authorized task owners could not be loaded. Refresh the workspace.');
  return { systemId: raw.systemId, permissions, findings, tasks, poams,
    owners: raw.owners,
    exceptions: raw.exceptions.map(item => {
      if (!nullableText(item.reviewedBy) || !nullableText(item.reviewerRole) || !nullableText(item.reviewedAt) || !nullableText(item.compensatingControls))
        throw new Error('Exception review information could not be loaded.');
      return { ...item, reviewerId: item.reviewedBy ?? null,
        decisionAuthority: [raw.owners?.find(owner => owner.id === item.reviewedBy)?.name, item.reviewerRole].filter(Boolean).join(' · ') || null,
        conditions: item.compensatingControls ?? null, reviewedAt: item.reviewedAt ?? null, expiresAt: item.expirationDate };
    }),
    warnings: [
      'Finding verification is recorded on corrective tasks. It does not automatically change finding disposition or close a POA&M.',
      ...(raw.exceptions.some(item => item.status === 'Approved' && !item.isEffective) ? ['An approved exception is no longer effective. Stored disposition may await the expiration worker.'] : []),
    ] };
}
