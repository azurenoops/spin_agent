import apiClient from './client';

export interface TaskTicketLink {
  id: string;
  provider: 'Jira' | 'ServiceNow';
  externalRef: string | null;
  externalUrl: string | null;
  externalStatus: string | null;
  externalAssignee: string | null;
  lastSuccessfulSyncAt: string | null;
  state: 'Pending' | 'Uncertain' | 'Linked' | 'Unlinked';
  rowVersion: string;
  correlationKey: string;
  lastError: string | null;
}

export interface TaskTicketResponse {
  configured: boolean;
  canManage: boolean;
  mode: 'ManualPullOnly';
  webhooksSupported: false;
  bidirectionalSupported: false;
  link: TaskTicketLink | null;
}

const route = (systemId: string, taskId: string) =>
  `/systems/${encodeURIComponent(systemId)}/tasks/${encodeURIComponent(taskId)}/ticket`;

export async function getTaskTicket(systemId: string, taskId: string, signal?: AbortSignal): Promise<TaskTicketResponse> {
  return (await apiClient.get<TaskTicketResponse>(route(systemId, taskId), { signal })).data;
}
export async function createTaskTicket(systemId: string, taskId: string): Promise<TaskTicketResponse> {
  return (await apiClient.post<TaskTicketResponse>(`${route(systemId, taskId)}/create`, {})).data;
}
export async function linkTaskTicket(systemId: string, taskId: string, externalRef: string, rowVersion?: string): Promise<TaskTicketResponse> {
  return (await apiClient.post<TaskTicketResponse>(`${route(systemId, taskId)}/link`, { externalRef, rowVersion })).data;
}
export async function refreshTaskTicket(systemId: string, taskId: string, rowVersion: string): Promise<TaskTicketResponse> {
  return (await apiClient.post<TaskTicketResponse>(`${route(systemId, taskId)}/refresh`, { rowVersion })).data;
}
export async function unlinkTaskTicket(systemId: string, taskId: string, rowVersion: string): Promise<TaskTicketResponse> {
  return (await apiClient.post<TaskTicketResponse>(`${route(systemId, taskId)}/unlink`, { rowVersion })).data;
}
