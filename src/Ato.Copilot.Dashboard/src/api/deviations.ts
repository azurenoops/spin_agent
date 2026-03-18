import apiClient from './client';
import type {
  DeviationListResponse,
  DeviationSummary,
  DeviationDetail,
  CreateDeviationRequest,
  ReviewDeviationRequest,
  RevokeDeviationRequest,
  ExtendDeviationRequest,
} from '../types/dashboard';

export interface DeviationListParams {
  type?: string;
  status?: string;
  severity?: string;
  search?: string;
  expiringWithinDays?: number;
  page?: number;
  pageSize?: number;
}

export async function getDeviations(systemId: string, params?: DeviationListParams) {
  const { data } = await apiClient.get<DeviationListResponse>(
    `/systems/${systemId}/deviations`, { params });
  return data;
}

export async function getDeviationSummary(systemId: string) {
  const { data } = await apiClient.get<DeviationSummary>(
    `/systems/${systemId}/deviations/summary`);
  return data;
}

export async function getDeviationDetail(deviationId: string) {
  const { data } = await apiClient.get<DeviationDetail>(
    `/deviations/${deviationId}`);
  return data;
}

export async function createDeviation(systemId: string, request: CreateDeviationRequest) {
  const { data } = await apiClient.post(`/systems/${systemId}/deviations`, request);
  return data;
}

export async function reviewDeviation(deviationId: string, request: ReviewDeviationRequest, reviewerRole?: string) {
  const { data } = await apiClient.put(
    `/deviations/${deviationId}/review`, request, { params: { reviewerRole } });
  return data;
}

export async function revokeDeviation(deviationId: string, request: RevokeDeviationRequest) {
  const { data } = await apiClient.put(`/deviations/${deviationId}/revoke`, request);
  return data;
}

export async function extendDeviation(deviationId: string, request: ExtendDeviationRequest) {
  const { data } = await apiClient.put(`/deviations/${deviationId}/extend`, request);
  return data;
}
