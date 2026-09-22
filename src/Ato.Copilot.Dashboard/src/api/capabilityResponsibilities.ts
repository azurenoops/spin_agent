import apiClient from './client';

export type ResponsibilityInheritanceType = 'Inherited' | 'Shared' | 'Customer';
export interface CapabilityResponsibilityAllocation {
  controlId: string;
  inheritanceType: ResponsibilityInheritanceType;
  provider: string | null;
  customerResponsibility: string | null;
}
export interface ConfirmCapabilityResponsibilitiesRequest {
  baselineId: string;
  sourceRevision: string;
  reviewRevision: string;
  allocations: CapabilityResponsibilityAllocation[];
}
export interface CapabilityResponsibilityItem {
  subscriptionId: string;
  capabilityId: string;
  componentId: string | null;
  cspProfileId: string | null;
  controlId: string;
  sourceRevision: string;
  reviewRevision: string;
  state: string;
  reviewedSourceRevision: string | null;
  confirmedBy: string | null;
  confirmedAt: string | null;
  allocation: CapabilityResponsibilityAllocation | null;
  effectiveInheritanceType: string | null;
  designationSource: string | null;
}
export interface CapabilityResponsibilityImpact {
  id: string;
  baselineId: string;
  controlId: string;
  stateHash: string;
  reason: string;
  sourcesJson: string;
  createdAt: string;
}
export interface CapabilityResponsibilityResponse {
  systemId: string;
  baselineId: string | null;
  canConfirm: boolean;
  items: CapabilityResponsibilityItem[];
  pendingImpacts: CapabilityResponsibilityImpact[];
}
export interface CapabilityResponsibilityDispatchResponse {
  delivered: number;
  pending: number;
  proposalIds: string[];
  deferred: { impactId: string; controlId: string; reason: string }[];
}

export class ResponsibilityApiError extends Error {
  constructor(message: string, public readonly status?: number) { super(message); }
}

function record(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' ? value as Record<string, unknown> : null;
}
const text = (value: unknown): value is string => typeof value === 'string' && value.length > 0;
const nullableText = (value: unknown): boolean => value === null || typeof value === 'string';
export function isResponsibilityType(value: unknown): value is ResponsibilityInheritanceType {
  return value === 'Inherited' || value === 'Shared' || value === 'Customer';
}

function validItem(value: unknown): boolean {
  const item = record(value);
  if (!item || !['subscriptionId', 'capabilityId', 'controlId', 'sourceRevision', 'reviewRevision', 'state'].every(key => text(item[key]))
    || !['componentId', 'cspProfileId', 'reviewedSourceRevision', 'confirmedBy', 'confirmedAt', 'effectiveInheritanceType', 'designationSource']
      .every(key => nullableText(item[key]))) return false;
  const allocation = record(item.allocation);
  return item.allocation === null || !!allocation && allocation.controlId === item.controlId
    && isResponsibilityType(allocation.inheritanceType)
    && nullableText(allocation.provider) && nullableText(allocation.customerResponsibility);
}

function validatePreview(value: unknown, systemId: string): CapabilityResponsibilityResponse {
  const data = record(value);
  if (!data || data.systemId !== systemId || !nullableText(data.baselineId) || typeof data.canConfirm !== 'boolean'
    || !Array.isArray(data.items) || !data.items.every(validItem)
    || !Array.isArray(data.pendingImpacts) || !data.pendingImpacts.every(value => {
      const impact = record(value);
      return impact && ['id', 'baselineId', 'controlId', 'stateHash', 'reason', 'sourcesJson', 'createdAt'].every(key => text(impact[key]));
    })) throw new ResponsibilityApiError('The responsibility preview is incomplete or does not match the selected system.');
  return value as CapabilityResponsibilityResponse;
}

function errorFrom(reason: unknown): ResponsibilityApiError {
  if (reason instanceof ResponsibilityApiError) return reason;
  const error = record(reason);
  const response = record(error?.response);
  const body = record(response?.data) ?? error;
  const status = response?.status ?? body?.status;
  const message = body?.title ?? body?.detail ?? body?.error ?? body?.message;
  return new ResponsibilityApiError(typeof message === 'string' ? message : 'The responsibility request failed. Refresh the preview and retry.',
    typeof status === 'number' ? status : undefined);
}

async function request<T>(operation: () => Promise<T>): Promise<T> {
  try { return await operation(); }
  catch (error) { throw errorFrom(error); }
}
const root = (systemId: string) => `/systems/${encodeURIComponent(systemId)}/capability-subscriptions`;

export function getCapabilityResponsibilities(systemId: string, signal?: AbortSignal) {
  return request(async () => validatePreview((await apiClient.get<unknown>(`${root(systemId)}/responsibilities`, { signal })).data, systemId));
}
export function confirmCapabilityResponsibilities(systemId: string, capabilityId: string,
  body: ConfirmCapabilityResponsibilitiesRequest, signal?: AbortSignal) {
  return request(async () => validatePreview((await apiClient.put<unknown>(
    `${root(systemId)}/${encodeURIComponent(capabilityId)}/responsibilities`, body, { signal })).data, systemId));
}
export function reconcileCapabilityResponsibilities(systemId: string, signal?: AbortSignal) {
  return request(async () => validatePreview((await apiClient.post<unknown>(`${root(systemId)}/reconcile`, undefined, { signal })).data, systemId));
}
export function dispatchCapabilityResponsibilityImpacts(systemId: string, signal?: AbortSignal): Promise<CapabilityResponsibilityDispatchResponse> {
  return request(async () => {
    const data = record((await apiClient.post<unknown>(`${root(systemId)}/review-impacts/dispatch`, undefined, { signal })).data);
    if (!data || typeof data.delivered !== 'number' || !Number.isInteger(data.delivered) || data.delivered < 0
      || typeof data.pending !== 'number' || !Number.isInteger(data.pending) || data.pending < 0
      || !Array.isArray(data.proposalIds) || !data.proposalIds.every(text) || !Array.isArray(data.deferred)) {
      throw new ResponsibilityApiError('The responsibility dispatch response is incomplete.');
    }
    const deferred = data.deferred.map(value => {
      const entry = record(value);
      if (!entry || !text(entry.impactId) || !text(entry.controlId) || !text(entry.reason)) {
        throw new ResponsibilityApiError('The responsibility dispatch response is incomplete.');
      }
      return { impactId: entry.impactId, controlId: entry.controlId, reason: entry.reason };
    });
    return { delivered: data.delivered, pending: data.pending, proposalIds: data.proposalIds, deferred };
  });
}
