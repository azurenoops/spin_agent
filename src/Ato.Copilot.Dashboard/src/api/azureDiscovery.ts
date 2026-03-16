import apiClient from './client';
import type {
  AzureDiscoveryResponse,
  ApplyDiscoveryRequest,
  ApplyDiscoveryResponse,
} from '../types/dashboard';

export async function discoverAzureResources(
  systemId: string,
  params?: {
    resourceGroup?: string;
    resourceType?: string;
    search?: string;
    cursor?: string;
  },
): Promise<AzureDiscoveryResponse> {
  const { data } = await apiClient.get<AzureDiscoveryResponse>(
    `/systems/${systemId}/azure-discovery`,
    { params },
  );
  return data;
}

export async function applyAzureDiscovery(
  systemId: string,
  request: ApplyDiscoveryRequest,
): Promise<ApplyDiscoveryResponse> {
  const { data } = await apiClient.post<ApplyDiscoveryResponse>(
    `/systems/${systemId}/azure-discovery/apply`,
    request,
  );
  return data;
}
