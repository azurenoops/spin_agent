import apiClient from '../../api/client';

export interface SystemHistoryEntry {
  id: string;
  eventType: string;
  timestamp: string;
  actor: string;
  summary: string;
  relatedEntityType: string | null;
  relatedEntityId: string | null;
}
export interface SystemHistoryResponse {
  systemId: string;
  source: 'DashboardActivity';
  items: SystemHistoryEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
}
export interface SystemHistoryQuery { page: number; pageSize: number; eventType?: string; from?: string; to?: string }

export async function listSystemHistory(systemId: string, query: SystemHistoryQuery, signal?: AbortSignal): Promise<SystemHistoryResponse> {
  const { data } = await apiClient.get<SystemHistoryResponse>(`/systems/${encodeURIComponent(systemId)}/history`, { params: query, signal });
  if (!data || data.systemId !== systemId || data.source !== 'DashboardActivity' || !Array.isArray(data.items)
    || !Number.isInteger(data.totalCount) || data.totalCount < 0 || data.page !== query.page
    || data.pageSize !== query.pageSize || data.items.length > data.pageSize
    || data.items.some(item => !item || ['id', 'eventType', 'timestamp', 'actor', 'summary']
      .some(key => typeof item[key as keyof SystemHistoryEntry] !== 'string'))
    || new Set(data.items.map(item => item.id)).size !== data.items.length) {
    throw new Error('The retained history response does not match the selected system and page.');
  }
  return data;
}
