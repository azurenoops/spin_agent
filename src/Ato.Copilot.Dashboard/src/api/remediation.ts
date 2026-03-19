import apiClient from './client';

// ─── Types ──────────────────────────────────────────────────────────────────

export interface PoamMilestone {
  id: string;
  description: string;
  targetDate: string;
  completedDate: string | null;
  sequence: number;
  isOverdue: boolean;
}

export interface PoamItem {
  id: string;
  registeredSystemId: string;
  systemName: string | null;
  weakness: string;
  weaknessSource: string;
  controlId: string;
  catSeverity: string;
  pointOfContact: string;
  pocEmail: string | null;
  resourcesRequired: string | null;
  costEstimate: number | null;
  scheduledCompletionDate: string;
  actualCompletionDate: string | null;
  status: string;
  comments: string | null;
  findingId: string | null;
  remediationTaskId: string | null;
  deviationId: string | null;
  createdAt: string;
  isOverdue: boolean;
  daysRemaining: number | null;
  milestones: PoamMilestone[];
  milestoneProgress: { total: number; completed: number };
}

export interface SystemBreakdown {
  systemId: string;
  systemName: string;
  open: number;
  overdue: number;
  catI: number;
}

export interface RemediationSummary {
  totalPoams: number;
  openCount: number;
  overdueCount: number;
  completedCount: number;
  riskAcceptedCount: number;
  delayedCount: number;
  avgDaysToClose: number;
  severityBreakdown: {
    catI: number;
    catII: number;
    catIII: number;
    catIPercent: number;
    catIIPercent: number;
    catIIIPercent: number;
  };
  aging: {
    days0To30: number;
    days31To60: number;
    days61To90: number;
    days90Plus: number;
  };
  bySystem: SystemBreakdown[];
  tasksByStatus: {
    backlog: number;
    todo: number;
    inProgress: number;
    inReview: number;
    blocked: number;
    done: number;
  };
  totalTasks: number;
  poams: PoamItem[];
}

export interface RemediationTask {
  id: string;
  taskNumber: string;
  boardId: string;
  boardName: string | null;
  title: string;
  description: string;
  controlId: string;
  controlFamily: string;
  severity: string;
  catSeverity: string | null;
  status: string;
  assigneeId: string | null;
  assigneeName: string | null;
  dueDate: string;
  createdAt: string;
  updatedAt: string;
  findingId: string | null;
  poamItemId: string | null;
  remediationScript: string | null;
  remediationScriptType: string | null;
  validationCriteria: string | null;
  isOverdue: boolean;
  affectedResourceCount: number;
  componentId: string | null;
  componentName: string | null;
}

export interface RemediationTasksResponse {
  items: RemediationTask[];
  totalCount: number;
}

// ─── API Functions ──────────────────────────────────────────────────────────

export async function getRemediationSummary(systemId?: string): Promise<RemediationSummary> {
  const params: Record<string, string> = {};
  if (systemId) params.systemId = systemId;
  const { data } = await apiClient.get<RemediationSummary>('/remediation/summary', { params });
  return data;
}

export async function getRemediationTasks(filters?: {
  systemId?: string;
  status?: string;
  severity?: string;
  overdueOnly?: boolean;
}): Promise<RemediationTasksResponse> {
  const params: Record<string, string | boolean> = {};
  if (filters?.systemId) params.systemId = filters.systemId;
  if (filters?.status) params.status = filters.status;
  if (filters?.severity) params.severity = filters.severity;
  if (filters?.overdueOnly) params.overdueOnly = true;
  const { data } = await apiClient.get<RemediationTasksResponse>('/remediation/tasks', { params });
  return data;
}

export async function updatePoamStatus(
  systemId: string,
  poamId: string,
  status: string,
  comments?: string,
): Promise<{ id: string; status: string; modifiedAt: string }> {
  const { data } = await apiClient.put(`/systems/${systemId}/poam/${poamId}/status`, { status, comments });
  return data;
}

export async function moveTask(
  taskId: string,
  status: string,
): Promise<{ id: string; taskNumber: string; previousStatus: string; newStatus: string; updatedAt: string }> {
  const { data } = await apiClient.put(`/remediation/tasks/${taskId}/move`, { status });
  return data;
}

export async function bulkUpdatePoamStatus(
  poamIds: string[],
  status: string,
): Promise<{ updated: number; status: string }> {
  const { data } = await apiClient.put('/remediation/poam/bulk-status', { poamIds, status });
  return data;
}
