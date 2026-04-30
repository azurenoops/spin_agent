import apiClient from './client';
import type { PaginatedResponse, PortfolioSystemSummary } from '../types/dashboard';

export interface RegisterSystemBody {
  name: string;
  systemType: string;
  missionCriticality: string;
  hostingEnvironment?: string;
  acronym?: string;
  description?: string;
  cloudEnvironment?: string;
  subscriptionIds?: string[];
}

interface RegisterSystemResponse {
  id: string;
  name: string;
  acronym?: string;
  systemType: string;
  missionCriticality: string;
  hostingEnvironment: string;
  currentRmfStep: string;
}

interface PortfolioParams {
  sortBy?: string;
  sortDir?: string;
  impactLevel?: string;
  rmfPhase?: string;
  cursor?: string;
  pageSize?: number;
}

export async function getPortfolio(
  params: PortfolioParams = {},
): Promise<PaginatedResponse<PortfolioSystemSummary>> {
  const { data } = await apiClient.get<PaginatedResponse<PortfolioSystemSummary>>(
    '/portfolio',
    { params },
  );
  return data;
}

export async function getPortfolioLegacy(
  params: PortfolioParams = {},
): Promise<PaginatedResponse<PortfolioSystemSummary>> {
  const { data } = await apiClient.get<PaginatedResponse<PortfolioSystemSummary>>(
    '/systems',
    { params },
  );
  return data;
}

export async function registerSystem(
  body: RegisterSystemBody,
): Promise<RegisterSystemResponse> {
  const { data } = await apiClient.post<RegisterSystemResponse>('/systems', body);
  return data;
}

export interface UpdateSystemBody {
  name?: string;
  acronym?: string;
  systemType?: string;
  missionCriticality?: string;
  hostingEnvironment?: string;
  description?: string;
}

export async function updateSystem(
  systemId: string,
  body: UpdateSystemBody,
): Promise<RegisterSystemResponse> {
  const { data } = await apiClient.put<RegisterSystemResponse>(`/systems/${systemId}`, body);
  return data;
}

export async function generateSystemDescription(
  name: string,
  systemType: string,
  missionCriticality: string,
  hostingEnvironment: string,
): Promise<string> {
  const { data } = await apiClient.post<{ description: string }>('/ai/system-description', {
    name,
    systemType,
    missionCriticality,
    hostingEnvironment,
  });
  return data.description;
}

// Feature 045: Re-export coverage KPI for Portfolio Risk Profile page
export { getCoverage } from './capabilities';
