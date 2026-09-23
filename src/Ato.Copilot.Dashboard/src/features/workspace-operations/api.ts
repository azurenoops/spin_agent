import axios, { type AxiosRequestConfig, type AxiosResponse } from 'axios';
import type {
  CatalogQuery, OrganizationCapability, OrganizationCapabilityDetail, OrganizationCatalogItem,
  OrganizationDetail, PagedResult, ProviderCatalogItem, ProviderSubscriber, ProvisioningResult,
  CreateOrganizationResult, InlineLocalCapability, PublicationPreview, PublicationResult,
  CapabilitySetupOperation, SetupResult, WorkingRevision,
  OrganizationCatalogAddition, OrganizationCatalogAdditionResult,
  ProviderCatalogOverview, ProviderCapabilityDetail,
  InitialAdministrator, OrganizationCreationRequest,
} from './types';
import type { AddCspInheritedCapabilityRequest, CspInheritedCapability } from '../csp-inherited-components/api';
import { getPortfolioLegacy } from '../../api/portfolio';
import { getComponents, createComponent } from '../../api/components';
import { getSystemDetail } from '../../api/systemDetail';
export { getSystemWorkspaceAccess as getSetupSystemAccess } from '../workspaces/api';

export async function listSetupSystems(params: Parameters<typeof getPortfolioLegacy>[0], signal?: AbortSignal) {
  const result = await getPortfolioLegacy(params, signal);
  return { ...result, items: result.items.map(({ systemId, name, acronym }) => ({ systemId, name, acronym })) };
}

export async function getSetupSystem(systemId: string, signal?: AbortSignal) {
  const system = await getSystemDetail(systemId, signal);
  return { systemId: system.systemId, name: system.name };
}

export async function getSetupComponents(systemId: string, params: Parameters<typeof getComponents>[1], signal?: AbortSignal) {
  const result = await getComponents(systemId, params, signal);
  return { ...result, items: result.items.map(({ id, name, componentType, description }) => ({ id, name, componentType, description })) };
}

export async function createSetupComponent(systemId: string, body: Parameters<typeof createComponent>[1], signal?: AbortSignal) {
  const component = await createComponent(systemId, body, signal);
  return { id: component.id, name: component.name };
}

export class WorkspaceOperationError extends Error {
  constructor(message: string, public readonly status?: number, public readonly code?: string) {
    super(message);
  }
}

function unwrap<T>(response: AxiosResponse<unknown>): T {
  const body = response.data as {
    status?: string;
    data?: T;
    error?: { code?: string; errorCode?: string; message?: string };
  };
  if (body?.data !== undefined && body.status !== 'error') return body.data;
  throw new WorkspaceOperationError(
    body?.error?.message ?? 'Unexpected workspace operation response.',
    response.status,
    body?.error?.code ?? body?.error?.errorCode,
  );
}

async function request<T>(config: AxiosRequestConfig): Promise<T> {
  try {
    return unwrap<T>(await axios.request(config));
  } catch (error) {
    if (axios.isAxiosError(error)) {
      const detail = error.response?.data as { error?: { message?: string; code?: string; errorCode?: string } } | undefined;
      throw new WorkspaceOperationError(
        detail?.error?.message ?? error.message,
        error.response?.status,
        detail?.error?.code ?? detail?.error?.errorCode,
      );
    }
    throw error;
  }
}

function providerCapabilityPath(capabilityId: string) {
  return `/api/csp/catalog/capabilities/${encodeURIComponent(capabilityId)}`;
}

function organizationPath(tenantId: string) {
  return `/api/workspaces/organizations/${encodeURIComponent(tenantId)}`;
}

export function getOrganizationCatalogAccess(tenantId: string, signal?: AbortSignal) {
  return request<{ canManageCatalog: boolean }>({
    method: 'GET', url: `${organizationPath(tenantId)}/catalog-access`, signal,
  });
}

export function addOrganizationCatalogRecord(tenantId: string, body: OrganizationCatalogAddition) {
  return request<OrganizationCatalogAdditionResult>({
    method: 'POST', url: `${organizationPath(tenantId)}/catalog-additions`, data: body,
  });
}

export function listProviderCatalog(query: CatalogQuery, signal?: AbortSignal) {
  return request<PagedResult<ProviderCatalogItem>>({
    method: 'GET', url: '/api/csp/catalog', params: query, signal,
  });
}

export function getProviderCatalogOverview(page = 1, signal?: AbortSignal) {
  return request<ProviderCatalogOverview>({ method: 'GET', url: '/api/csp/catalog/overview', params: { page, pageSize: 25 }, signal });
}

export function getProviderCapability(capabilityId: string, signal?: AbortSignal) {
  return request<ProviderCapabilityDetail>({ method: 'GET', url: providerCapabilityPath(capabilityId), signal });
}

export function createProviderCapability(componentId: string, body: AddCspInheritedCapabilityRequest) {
  return request<CspInheritedCapability>({
    method: 'POST', url: `/api/csp/inherited-components/${encodeURIComponent(componentId)}/capabilities`, data: body,
  });
}

export function listProviderSubscribers(capabilityId: string, page = 1, signal?: AbortSignal) {
  return request<PagedResult<ProviderSubscriber>>({
    method: 'GET', url: `${providerCapabilityPath(capabilityId)}/subscribers`,
    params: { page, pageSize: 25 }, signal,
  });
}

export function getWorkingRevision(capabilityId: string, signal?: AbortSignal) {
  return request<WorkingRevision>({
    method: 'GET', url: `${providerCapabilityPath(capabilityId)}/working-revision`, signal,
  });
}

export function saveWorkingRevision(capabilityId: string, body: {
  expectedRevision: number; classification: string; serviceCategory: string;
  contributors: string[]; controlDuties: Record<string, string>;
}, signal?: AbortSignal) {
  return request<WorkingRevision>({
    method: 'PUT', url: `${providerCapabilityPath(capabilityId)}/working-revision`, data: body, signal,
  });
}

export function generatePublicationPreview(capabilityId: string, revision: number, signal?: AbortSignal) {
  return request<PublicationPreview>({
    method: 'POST', url: `${providerCapabilityPath(capabilityId)}/publication-previews`,
    data: { revision }, signal,
  });
}

export function approveWorkingRevision(
  capabilityId: string, revision: number, previewId: string, previewHash: string,
  signal?: AbortSignal,
) {
  return request<WorkingRevision>({
    method: 'POST', url: `${providerCapabilityPath(capabilityId)}/working-revision/approve`,
    data: { revision, previewId, previewHash }, signal,
  });
}

export function publishWorkingRevision(capabilityId: string, body: {
  revision: number; approvedRevision: number; previewId: string;
  previewHash: string; idempotencyKey: string;
}, signal?: AbortSignal) {
  return request<PublicationResult>({
    method: 'POST', url: `${providerCapabilityPath(capabilityId)}/publish`, data: body, signal,
  });
}

export function listOrganizations(query: {
  page: number; pageSize: number; search?: string; lifecycle?: string; onboarding?: string; review?: string;
}, signal?: AbortSignal) {
  return request<PagedResult<OrganizationCatalogItem>>({
    method: 'GET', url: '/api/csp/organizations', params: query, signal,
  });
}

export function getOrganization(tenantId: string, signal?: AbortSignal) {
  return request<OrganizationDetail>({
    method: 'GET', url: `/api/csp/organizations/${encodeURIComponent(tenantId)}`, signal,
  });
}

export function createOrganization(body: OrganizationCreationRequest, idempotencyKey: string) {
  return request<CreateOrganizationResult>({
    method: 'POST', url: '/api/csp/dashboard/tenants', data: body,
    headers: { 'Idempotency-Key': idempotencyKey },
  });
}

export async function getOrganizationCreation(idempotencyKey: string, signal?: AbortSignal) {
  try {
    return await request<CreateOrganizationResult>({
      method: 'GET', url: `/api/csp/organization-creations/${encodeURIComponent(idempotencyKey)}`, signal,
    });
  } catch (error) {
    if (error instanceof WorkspaceOperationError && error.status === 404 && error.code === 'ORGANIZATION_CREATION_NOT_FOUND') return null;
    throw error;
  }
}

export function beginOrganizationProvisioning(tenantId: string, idempotencyKey: string, signal?: AbortSignal) {
  return request<ProvisioningResult>({
    method: 'POST', url: `/api/csp/organizations/${encodeURIComponent(tenantId)}/provisioning`,
    headers: { 'Idempotency-Key': idempotencyKey }, signal,
  });
}

export function getOrganizationProvisioning(
  tenantId: string, idempotencyKey: string, signal?: AbortSignal,
) {
  return request<ProvisioningResult>({
    method: 'GET', url: `/api/csp/organizations/${encodeURIComponent(tenantId)}/provisioning`,
    params: { idempotencyKey }, signal,
  });
}

export async function getCurrentOrganizationProvisioning(
  tenantId: string, signal?: AbortSignal,
) {
  try {
    return await request<ProvisioningResult>({
      method: 'GET', url: `/api/csp/organizations/${encodeURIComponent(tenantId)}/provisioning/current`,
      signal,
    });
  } catch (error) {
    if (error instanceof WorkspaceOperationError && error.status === 404 && error.code === 'PROVISIONING_NOT_FOUND') return null;
    throw error;
  }
}

export function resumeOrganizationProvisioning(tenantId: string, operationId: string, body: InitialAdministrator, signal?: AbortSignal) {
  return request<ProvisioningResult>({
    method: 'PATCH',
    url: `/api/csp/organizations/${encodeURIComponent(tenantId)}/provisioning/${encodeURIComponent(operationId)}`,
    data: body, signal,
  });
}

export function listOrganizationCapabilities(tenantId: string, query: CatalogQuery, signal?: AbortSignal) {
  return request<PagedResult<OrganizationCapability>>({
    method: 'GET', url: `${organizationPath(tenantId)}/capabilities`, params: query, signal,
  });
}

export function getOrganizationCapability(
  tenantId: string, source: string, recordId: string, systemId?: string, signal?: AbortSignal,
  recordType?: string,
) {
  return request<OrganizationCapabilityDetail>({
    method: 'GET',
    url: `${organizationPath(tenantId)}/capabilities/${encodeURIComponent(source)}/${encodeURIComponent(recordId)}`,
    params: { systemId, recordType }, signal,
  });
}

export function completeCapabilitySetup(tenantId: string, body: {
  idempotencyKey: string; source: string; recordId: string; systemId: string | null;
  componentIds: string[]; subscribe: boolean; inlineLocalCapability?: InlineLocalCapability;
  preparedOperationId?: string;
}, signal?: AbortSignal) {
  return request<SetupResult>({
    method: 'POST', url: `${organizationPath(tenantId)}/capability-setups`, data: body, signal,
  });
}

export function prepareCapabilitySetup(tenantId: string, body: {
  idempotencyKey: string; source: string; recordId: string; systemId: string | null;
  componentIds: string[]; subscribe: boolean; inlineLocalCapability?: InlineLocalCapability;
}, signal?: AbortSignal) {
  return request<CapabilitySetupOperation>({
    method: 'POST', url: `${organizationPath(tenantId)}/capability-setups/prepare`, data: body, signal,
  });
}

export function getCapabilitySetup(tenantId: string, operationId: string, signal?: AbortSignal) {
  return request<CapabilitySetupOperation>({
    method: 'GET',
    url: `${organizationPath(tenantId)}/capability-setups/${encodeURIComponent(operationId)}`,
    signal,
  });
}

export function reviewNarrativeProposal(
  tenantId: string, source: string, recordId: string, proposalId: string,
  body: { systemId: string; expectedRevision: number; decision: string; note?: string },
) {
  return request<unknown>({
    method: 'POST',
    url: `${organizationPath(tenantId)}/capabilities/${encodeURIComponent(source)}/${encodeURIComponent(recordId)}/narrative-proposals/${encodeURIComponent(proposalId)}/review`,
    data: body,
  });
}
