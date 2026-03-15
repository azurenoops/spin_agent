import apiClient from './client';

export interface TrendDataPoint {
  date: string;
  complianceScore: number;
  catICount: number;
  catIICount: number;
  catIIICount: number;
  openPoamCount: number;
  overduePoamCount: number;
  narrativeCoverage: number;
  isSignificantDecline: boolean;
}

export interface TrendDataResponse {
  systemId: string;
  granularity: string;
  dataPoints: TrendDataPoint[];
}

interface TrendParams {
  startDate?: string;
  endDate?: string;
  granularity?: 'Daily' | 'Weekly' | 'Monthly' | 'Quarterly';
}

export async function getTrends(systemId: string, params?: TrendParams) {
  const { data } = await apiClient.get<TrendDataResponse>(
    `/systems/${systemId}/trends`,
    { params },
  );
  return data;
}
