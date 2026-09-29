import apiClient from './client';

export interface PoamWorkspaceEvidence { id: string; name: string; contentHash: string; linkedAt: string; linkedBy: string }
export interface PoamWorkspaceHistory { id: string; eventType: string; oldValue: string | null; newValue: string | null; actor: string; at: string; details: string | null }
export interface PoamWorkspaceTask {
  id: string; taskNumber: string; boardId: string; title: string; description: string; controlId: string;
  severity: string; status: string; assigneeId: string | null; assigneeName: string | null; dueDate: string;
  findingId: string | null; poamIds: string[]; rowVersion: string; verificationStatus: string;
  verificationNotes: string | null; verifiedBy: string | null; verifiedAt: string | null;
  evidence: PoamWorkspaceEvidence[]; history: PoamWorkspaceHistory[]; allowedTransitions: string[];
  affectedResources?: string[]; validationCriteria?: string | null; remediationScript?: string | null; remediationScriptType?: string | null;
}
export interface PoamWorkspaceFinding {
  id: string; title: string; description: string; controlId: string; severity: string; status: string; source: string;
  assessmentId: string; importRecordId: string | null; discoveredAt: string; taskIds: string[]; poamIds: string[]; deviationId: string | null;
  provenance: { sourceId: string; sourceName: string; sourceType: string; plan: { id: string; revision: number; hash: string; title: string; status: string } | null } | null;
}
export interface PoamWorkspaceException {
  id: string; type: string; status: string; controlId: string; justification: string; expirationDate: string;
  isEffective: boolean; findingId: string | null; poamEntryId: string | null;
  reviewedBy?: string | null; reviewerRole?: string | null; reviewedAt?: string | null; compensatingControls?: string | null;
}
export interface PoamWorkspaceMilestone {
  id: string; description: string; targetDate: string; completedDate: string | null; sequence: number;
}
export interface PoamWorkspacePoam {
  id: string; poamId: string; weakness: string; securityControlNumber: string; status: string; taskIds: string[];
  findingId: string | null; deviationId: string | null; rowVersion: string;
  pointOfContact?: string; scheduledCompletionDate?: string; weaknessSource?: string; catSeverity?: string;
  pocEmail?: string | null; actualCompletionDate?: string | null; milestones?: PoamWorkspaceMilestone[];
  history?: PoamWorkspaceHistory[]; comments?: string | null; resourcesRequired?: string | null;
  costEstimate?: number | null; createdAt?: string; modifiedAt?: string | null;
}
export interface PoamWorkspace {
  systemId: string; findings: PoamWorkspaceFinding[]; tasks: PoamWorkspaceTask[];
  poams: PoamWorkspacePoam[];
  exceptions: PoamWorkspaceException[];
  owners?: { id: string; name: string }[];
  permissions: { canManageRemediation: boolean; reason: string | null; canCreateTasks: boolean; canMoveTasks: boolean; canMoveAnyTasks: boolean };
}
const root = (systemId: string) => `/systems/${encodeURIComponent(systemId)}/remediation-workspace`;
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const ids = (value: unknown): value is string[] => Array.isArray(value) && value.every(id => typeof id === 'string');
export async function getPoamWorkspace(systemId: string, signal?: AbortSignal): Promise<PoamWorkspace> {
  const { data } = await apiClient.get<PoamWorkspace>(root(systemId), { signal });
  if (!data || data.systemId !== systemId || !Array.isArray(data.findings) || !Array.isArray(data.tasks)
    || !Array.isArray(data.poams) || !Array.isArray(data.exceptions) || typeof data.permissions?.canManageRemediation !== 'boolean')
    throw new Error('Connected records could not be verified for this system. Refresh the workspace.');
  if (!data.findings.every(item => record(item) && ['id', 'title', 'description', 'controlId', 'severity', 'source'].every(key => typeof item[key] === 'string') && ids(item.poamIds))
    || !data.poams.every(item => record(item) && typeof item.id === 'string' && ids(item.taskIds))
    || !data.tasks.every(item => record(item) && ['id', 'taskNumber', 'title', 'status', 'verificationStatus'].every(key => typeof item[key] === 'string')
      && ids(item.poamIds) && Array.isArray(item.evidence) && item.evidence.every(e => record(e) && ['id', 'name', 'contentHash'].every(key => typeof e[key] === 'string')))
    || !data.exceptions.every(item => record(item) && ['id', 'type', 'status', 'justification', 'expirationDate'].every(key => typeof item[key] === 'string') && typeof item.isEffective === 'boolean'))
    throw new Error('Connected work is incomplete. Refresh before changing relationships.');
  return data;
}
export interface PoamTaskLinkRevisions { expectedPoamRevision?: string; expectedTaskRevision?: string }
export async function linkPoamWorkspaceTask(systemId: string, poamId: string, taskId: string, revisions?: PoamTaskLinkRevisions): Promise<void> {
  const path = `${root(systemId)}/poams/${encodeURIComponent(poamId)}/tasks/${encodeURIComponent(taskId)}`;
  if (revisions) await apiClient.put(path, revisions);
  else await apiClient.put(path);
}
export async function unlinkPoamWorkspaceTask(systemId: string, poamId: string, taskId: string, revisions?: PoamTaskLinkRevisions): Promise<void> {
  const path = `${root(systemId)}/poams/${encodeURIComponent(poamId)}/tasks/${encodeURIComponent(taskId)}`;
  if (revisions) await apiClient.delete(path, { data: revisions });
  else await apiClient.delete(path);
}
export interface CreatePoamWorkspaceTaskRequest {
  requestId: string; title: string; description: string; controlId: string; severity: string; findingId?: string; dueDate?: string;
}
export async function createPoamWorkspaceTask(systemId: string, request: CreatePoamWorkspaceTaskRequest): Promise<{ id: string }> {
  const { data } = await apiClient.post<{ id: string }>(`${root(systemId)}/tasks`, request);
  if (typeof data?.id !== 'string' || !data.id) throw new Error('Task creation could not be confirmed. Refresh before retrying with the same request.');
  return data;
}
