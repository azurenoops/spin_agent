import type { AxiosRequestConfig } from 'axios';
import apiClient from '../../api/client';
import type {
  ApplicableCapabilitiesQuery, AssociatedRelationship, AssociateRelationshipInput,
  CapabilityAdoption, CapabilityAdoptionInput, RelationshipPreview, RelationshipPreviewInput,
  RelationshipReviewInput, SystemHostingAllocation, ProviderRelationship,
  PagedResult,
} from './types';
import { capabilityPage, relationshipPage } from './validation';

interface ErrorDetail {
  message?: string;
  errorCode?: string;
  code?: string;
  suggestion?: string;
}

interface ErrorEnvelope extends ErrorDetail {
  error?: ErrorDetail;
}

export class ProviderRelationshipError extends Error {
  constructor(message: string, public readonly code?: string, public readonly status?: number) {
    super(message);
    this.name = 'ProviderRelationshipError';
  }
}

function requestError(reason: unknown): ProviderRelationshipError {
  if (reason instanceof ProviderRelationshipError) return reason;
  const value = reason as (ErrorEnvelope & {
    response?: { data?: ErrorEnvelope; status?: number };
  }) | null;
  const envelope = value?.response?.data ?? value;
  const detail = envelope?.error ?? envelope;
  return new ProviderRelationshipError(
    [detail?.message ?? 'Unable to complete the provider relationship request.', detail?.suggestion].filter(Boolean).join(' '),
    detail?.errorCode ?? detail?.code,
    value?.response?.status,
  );
}

async function request<T>(config: AxiosRequestConfig): Promise<T> {
  try {
    const response = await apiClient.request<ErrorEnvelope & { status?: string; data?: T }>(config);
    if (response.data?.status === 'success' && response.data.data !== undefined) return response.data.data;
    throw requestError({
      response: {
        status: response.status,
        data: { ...response.data, message: response.data?.message ?? 'The server did not return provider relationship data.' },
      },
    });
  } catch (reason) {
    throw requestError(reason);
  }
}

const systemPath = (systemId: string) => `/systems/${encodeURIComponent(systemId)}`;
const relationshipPath = (systemId: string, relationshipId: string) =>
  `${systemPath(systemId)}/provider-relationships/${encodeURIComponent(relationshipId)}`;

export async function listProviderRelationships(systemId: string, page = 1, signal?: AbortSignal) {
  return relationshipPage(await request<unknown>({
    method: 'GET', url: `${systemPath(systemId)}/provider-relationships`,
    params: { page, pageSize: 25 }, signal,
  }));
}

export async function listSystemHostingAllocations(systemId: string, page = 1, signal?: AbortSignal) {
  const result = await listProviderRelationships(systemId, page, signal);
  return { ...result, items: result.items.map(toHostingAllocation) };
}

function toHostingAllocation(item: ProviderRelationship): SystemHostingAllocation {
  return {
    relationshipId: item.relationshipId,
    assignmentId: item.assignmentId, revision: item.assignmentRevision,
    offeringId: item.offeringId, offeringName: item.offeringName ?? 'Offering name unavailable',
    providerName: item.providerName, hostingScopeName: item.hostingScopeName,
    systemId: item.systemId, systemName: item.systemName ?? 'System name unavailable',
    assignedScopes: item.assignedScopes, canAssociate: item.canAssociate,
  };
}

export async function listAllSystemHostingAllocations(systemId: string, signal?: AbortSignal) {
  return (await listAllProviderRelationships(systemId, signal)).map(toHostingAllocation);
}

export async function listAllProviderRelationships(systemId: string, signal?: AbortSignal) {
  return collectProviderPages(page => listProviderRelationships(systemId, page, signal),
    item => item.assignmentId, item => item.systemId.toLowerCase() === systemId.toLowerCase(), 'Hosting allocations', signal);
}

export async function collectProviderPages<T>(read: (page: number) => Promise<PagedResult<T>>,
  key: (item: T) => string, valid: (item: T) => boolean, label: string, signal?: AbortSignal): Promise<T[]> {
  const first = await read(1);
  if (first.page !== 1) {
    throw new ProviderRelationshipError(`${label} are incomplete or do not match this system. Refresh and review again.`);
  }
  const items = [...first.items];
  let page = 1;
  while (items.length < first.total) {
    signal?.throwIfAborted();
    const next = await read(++page);
    if (next.total !== first.total || next.page !== page || next.pageSize !== first.pageSize || next.items.length === 0) {
      throw new ProviderRelationshipError(`${label} changed while loading. Refresh and review again.`);
    }
    items.push(...next.items);
  }
  if (items.length !== first.total || new Set(items.map(key)).size !== items.length || items.some(item => !valid(item))) {
    throw new ProviderRelationshipError(`${label} are incomplete or do not match this system. Refresh and review again.`);
  }
  return items;
}

export async function associateProviderRelationship(systemId: string, data: AssociateRelationshipInput, key: string) {
  const result = await request<AssociatedRelationship>({
    method: 'POST', url: `${systemPath(systemId)}/provider-relationships`,
    data, headers: { 'Idempotency-Key': key },
  });
  if (!result?.relationshipId || result.assignmentId !== data.assignmentId
    || !Number.isSafeInteger(result.revision)
    || !['Undetermined', 'SeparateBoundaryConsumer', 'ExplicitlyCoveredByRecordedScope'].includes(result.state)) {
    throw new ProviderRelationshipError('The relationship write was not confirmed by a valid response. Retry the same operation.');
  }
  return result;
}

export async function previewProviderRelationship(systemId: string, relationshipId: string, data: RelationshipPreviewInput) {
  const result = await request<RelationshipPreview>({
    method: 'POST', url: `${relationshipPath(systemId, relationshipId)}/previews`, data,
  });
  if (!result || typeof result.previewId !== 'string' || !result.previewId
    || typeof result.previewHash !== 'string' || !result.previewHash
    || !Number.isSafeInteger(result.revision) || result.revision <= data.expectedRevision
    || typeof result.contextSnapshotHash !== 'string' || !result.contextSnapshotHash
    || !Array.isArray(result.blockers) || typeof result.canReview !== 'boolean') {
    throw new ProviderRelationshipError('The server did not confirm a valid relationship review preview. Refresh the scope and prepare a new review.');
  }
  return result;
}

export async function reviewProviderRelationship(systemId: string, relationshipId: string, data: RelationshipReviewInput) {
  const result = await request<unknown>({
    method: 'POST', url: `${relationshipPath(systemId, relationshipId)}/review`, data,
  });
  const confirmed = relationshipPage({ items: [result], page: 1, pageSize: 1, total: 1 }).items[0]!;
  if (confirmed.systemId !== systemId || confirmed.relationshipId !== relationshipId || confirmed.revision <= data.expectedRevision
    || !confirmed.reviewedAt || !confirmed.reviewedBy) {
    throw new ProviderRelationshipError('The server did not confirm the selected relationship review. Refresh saved scope records before retrying.');
  }
  return confirmed;
}

export async function listApplicableProviderCapabilities(systemId: string, query: ApplicableCapabilitiesQuery, signal?: AbortSignal) {
  return capabilityPage(await request<unknown>({
    method: 'GET', url: `${systemPath(systemId)}/applicable-provider-capabilities`,
    params: { ...query, pageSize: 25 }, signal,
  }));
}

export async function proposeProviderCapabilityAdoption(systemId: string, data: CapabilityAdoptionInput, key: string) {
  const result = await request<CapabilityAdoption>({
    method: 'POST', url: `${systemPath(systemId)}/provider-capability-adoptions`,
    data, headers: { 'Idempotency-Key': key },
  });
  if (!result?.adoptionSnapshotId || !result.subscription?.id
    || result.releaseId !== data.releaseId || result.contextSnapshotHash !== data.contextSnapshotHash) {
    throw new ProviderRelationshipError('The capability adoption was not confirmed by a valid response. Retry the same operation.');
  }
  return result;
}
