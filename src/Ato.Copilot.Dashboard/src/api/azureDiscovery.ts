import apiClient from './client';
import type {
  AzureDiscoveryResponse,
  ApplyDiscoveryRequest,
  ApplyDiscoveryResponse,
  DiscoverAzureRequest,
  DiscoveryResponse,
  ImportAzureRequest,
  ImportAzureResponse,
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

// ─── Component Library Azure Discovery (Feature 040) ─────────────────────────

export async function discoverAzureResourcesForComponents(
  request: DiscoverAzureRequest,
): Promise<DiscoveryResponse> {
  const { data } = await apiClient.post<DiscoveryResponse>(
    '/components/discover-azure',
    request,
  );
  return data;
}

export async function importAzureComponents(
  request: ImportAzureRequest,
): Promise<ImportAzureResponse> {
  const { data } = await apiClient.post<ImportAzureResponse>(
    '/components/import-azure',
    request,
  );
  return data;
}

// ─── Entra ID Discovery (Feature 040 — US9) ─────────────────────────────────

export interface EntraDiscoveryItem {
  entraObjectId: string;
  displayName: string;
  email?: string;
  kind: 'User' | 'Group';
  department?: string;
  jobTitle?: string;
  alreadyImported: boolean;
}

export interface EntraDiscoveryResponse {
  items: EntraDiscoveryItem[];
  partialFailure: boolean;
  failureMessage?: string;
}

export interface EntraImportRequest {
  people: { entraObjectId: string; displayName: string; email?: string; kind: string }[];
}

export interface EntraImportResponse {
  imported: number;
  skipped: number;
}

export async function discoverEntraIdUsers(): Promise<EntraDiscoveryResponse> {
  const { data } = await apiClient.post<EntraDiscoveryResponse>(
    '/components/discover-entra',
  );
  return data;
}

export async function importEntraIdPeople(
  request: EntraImportRequest,
): Promise<EntraImportResponse> {
  const { data } = await apiClient.post<EntraImportResponse>(
    '/components/import-entra',
    request,
  );
  return data;
}
