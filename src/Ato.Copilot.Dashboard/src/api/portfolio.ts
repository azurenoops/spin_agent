import apiClient from './client';
import type { PaginatedResponse, PortfolioSystemSummary } from '../types/dashboard';

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
