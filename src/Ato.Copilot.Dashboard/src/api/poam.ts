import apiClient from './client';
import type {
  PaginatedPoamResponse,
  PoamDetail,
  PoamMetrics,
  PoamListQuery,
  CreatePoamRequest,
  BulkCreateFromFindingsRequest,
  BulkCreateResponse,
  BulkPoamStatusRequest,
  BulkPoamStatusResponse,
  UpdatePoamStatusRequest,
  UpdatePoamStatusResponse,
  LinkComponentsRequest,
  UnlinkComponentsRequest,
  CreateTaskFromPoamRequest,
  LinkTaskRequest,
  PoamTrendResponse,
  ConfigureTicketingRequest,
  SyncTicketRequest,
} from '../types/poam';

// ─── POA&M CRUD ─────────────────────────────────────────────────────────────

export async function listPoamItems(
  systemId: string,
  query?: PoamListQuery,
): Promise<PaginatedPoamResponse> {
  const params: Record<string, string | number | boolean> = {};
  if (query?.page) params.page = query.page;
  if (query?.pageSize) params.pageSize = query.pageSize;
  if (query?.sortBy) params.sortBy = query.sortBy;
  if (query?.sortDirection) params.sortDirection = query.sortDirection;
  if (query?.status) params.status = query.status;
  if (query?.catSeverity) params.catSeverity = query.catSeverity;
  if (query?.overdue) params.overdue = query.overdue;
  if (query?.componentId) params.componentId = query.componentId;
  if (query?.search) params.search = query.search;
  const { data } = await apiClient.get<PaginatedPoamResponse>(`/systems/${systemId}/poam`, { params });
  return data;
}

export async function listPoamItemsCrossSystem(
  query?: PoamListQuery,
): Promise<PaginatedPoamResponse> {
  const params: Record<string, string | number | boolean> = {};
  if (query?.page) params.page = query.page;
  if (query?.pageSize) params.pageSize = query.pageSize;
  if (query?.sortBy) params.sortBy = query.sortBy;
  if (query?.sortDirection) params.sortDirection = query.sortDirection;
  if (query?.status) params.status = query.status;
  if (query?.catSeverity) params.catSeverity = query.catSeverity;
  if (query?.overdue) params.overdue = query.overdue;
  if (query?.componentId) params.componentId = query.componentId;
  if (query?.search) params.search = query.search;
  if (query?.systemId) params.systemId = query.systemId;
  const { data } = await apiClient.get<PaginatedPoamResponse>('/poam', { params });
  return data;
}

export async function getPoamDetail(poamId: string): Promise<PoamDetail> {
  const { data } = await apiClient.get<PoamDetail>(`/poam/${poamId}`);
  return data;
}

export async function createPoamItem(
  systemId: string,
  request: CreatePoamRequest,
): Promise<PoamDetail> {
  const { data } = await apiClient.post<PoamDetail>(`/systems/${systemId}/poam`, request);
  return data;
}

export async function updatePoamItem(
  poamId: string,
  request: Record<string, unknown>,
): Promise<PoamDetail> {
  const { data } = await apiClient.put<PoamDetail>(`/poam/${poamId}`, request);
  return data;
}

export async function deletePoamItem(poamId: string): Promise<void> {
  await apiClient.delete(`/poam/${poamId}`);
}

// ─── Metrics & Trends ───────────────────────────────────────────────────────

export async function getPoamMetrics(systemId: string): Promise<PoamMetrics> {
  const { data } = await apiClient.get<PoamMetrics>(`/systems/${systemId}/poam/metrics`);
  return data;
}

export async function getPoamMetricsCrossSystem(): Promise<PoamMetrics> {
  const { data } = await apiClient.get<PoamMetrics>('/poam/metrics');
  return data;
}

export async function getPoamTrend(
  systemId: string,
  period?: string,
  start?: string,
  end?: string,
): Promise<PoamTrendResponse> {
  const params: Record<string, string> = {};
  if (period) params.period = period;
  if (start) params.start = start;
  if (end) params.end = end;
  const { data } = await apiClient.get<PoamTrendResponse>(`/systems/${systemId}/poam/trend`, { params });
  return data;
}

export async function exportTrendPdf(
  systemId: string,
  period?: string,
  start?: string,
  end?: string,
): Promise<void> {
  const params: Record<string, string> = {};
  if (period) params.period = period;
  if (start) params.startDate = start;
  if (end) params.endDate = end;
  const response = await apiClient.get(`/systems/${systemId}/poam/trend/export`, {
    params,
    responseType: 'blob',
  });
  const url = window.URL.createObjectURL(new Blob([response.data]));
  const link = document.createElement('a');
  link.href = url;
  link.download = `poam-trend-${systemId}-${new Date().toISOString().slice(0, 10)}.pdf`;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  window.URL.revokeObjectURL(url);
}

// ─── Status Updates ─────────────────────────────────────────────────────────

export async function updatePoamStatus(
  poamId: string,
  request: UpdatePoamStatusRequest,
): Promise<UpdatePoamStatusResponse> {
  const { data } = await apiClient.put<UpdatePoamStatusResponse>(`/poam/${poamId}/status`, request);
  return data;
}

export async function bulkUpdatePoamStatus(
  request: BulkPoamStatusRequest,
): Promise<BulkPoamStatusResponse> {
  const { data } = await apiClient.post<BulkPoamStatusResponse>('/poam/bulk-status', request);
  return data;
}

// ─── Component Linkage ──────────────────────────────────────────────────────

export async function linkComponents(
  poamId: string,
  request: LinkComponentsRequest,
): Promise<void> {
  await apiClient.post(`/poam/${poamId}/components`, request);
}

export async function unlinkComponents(
  poamId: string,
  request: UnlinkComponentsRequest,
): Promise<void> {
  await apiClient.delete(`/poam/${poamId}/components`, { data: request });
}

// ─── Remediation Task Operations ────────────────────────────────────────────

export async function createTaskFromPoam(
  poamId: string,
  request: CreateTaskFromPoamRequest,
): Promise<{ taskId: string }> {
  const { data } = await apiClient.post<{ taskId: string }>(`/poam/${poamId}/task`, request);
  return data;
}

export async function linkTask(poamId: string, request: LinkTaskRequest): Promise<void> {
  await apiClient.post(`/poam/${poamId}/link-task`, request);
}

export async function unlinkTask(poamId: string): Promise<void> {
  await apiClient.delete(`/poam/${poamId}/unlink-task`);
}

// ─── Bulk Create ────────────────────────────────────────────────────────────

export async function bulkCreateFromFindings(
  systemId: string,
  request: BulkCreateFromFindingsRequest,
): Promise<BulkCreateResponse> {
  const { data } = await apiClient.post<BulkCreateResponse>(`/systems/${systemId}/poam/bulk-create`, request);
  return data;
}

// ─── Export ─────────────────────────────────────────────────────────────────

export async function exportPoam(
  systemId: string,
  format: string,
  status?: string,
  catSeverity?: string,
  includeAll?: boolean,
): Promise<Blob> {
  const params: Record<string, string | boolean> = { format };
  if (status) params.status = status;
  if (catSeverity) params.catSeverity = catSeverity;
  if (includeAll) params.includeAll = includeAll;
  const { data } = await apiClient.get(`/systems/${systemId}/poam/export`, {
    params,
    responseType: 'blob',
  });
  return data;
}

// ─── Ticketing ──────────────────────────────────────────────────────────────

export async function getTicketingConfig(
  systemId: string,
): Promise<Record<string, unknown>> {
  const { data } = await apiClient.get(`/systems/${systemId}/ticketing`);
  return data;
}

export async function configureTicketing(
  systemId: string,
  request: ConfigureTicketingRequest,
): Promise<Record<string, unknown>> {
  const { data } = await apiClient.post(`/systems/${systemId}/ticketing`, request);
  return data;
}

export async function syncTicket(
  poamId: string,
  request?: SyncTicketRequest,
): Promise<Record<string, unknown>> {
  const { data } = await apiClient.post(`/poam/${poamId}/sync-ticket`, request ?? {});
  return data;
}
