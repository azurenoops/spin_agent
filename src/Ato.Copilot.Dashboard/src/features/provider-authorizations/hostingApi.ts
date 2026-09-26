import { packageRequest } from '../package-imports/request';
import { offeringPath } from './api';
import type { AzureScope, Citation, Page, SnapshotRef } from './types';
import type { HostingAssignment, HostingAssignmentInput, HostingScopeInput, HostingScopeRevision } from './hostingTypes';

const object = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object';
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0;
const positive = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const nullableId = (value: unknown) => value === null || text(value);
const array = <T>(value: unknown, valid: (item: unknown) => item is T): value is T[] => Array.isArray(value) && value.every(valid);
const snapshot = (value: unknown): value is SnapshotRef => object(value)
  && text(value.revisionId) && positive(value.revision) && text(value.snapshotHash);
const scope = (value: unknown): value is AzureScope => object(value)
  && (value.cloud === 'AzureCloud' || value.cloud === 'AzureUSGovernment')
  && text(value.directoryTenantId) && text(value.subscriptionId) && text(value.resourceId);
const citation = (value: unknown): value is Citation => object(value)
  && text(value.packageId) && text(value.artifactId) && text(value.archivePath) && text(value.locator) && text(value.quote);
function scopeRevision(value: unknown): value is HostingScopeRevision {
  return object(value) && text(value.offeringId) && positive(value.offeringRevision) && snapshot(value.snapshot)
    && nullableId(value.impactReviewId) && nullableId(value.predecessorRevisionId) && text(value.name)
    && array(value.permittedScopes, scope) && array(value.citations, citation)
    && Array.isArray(value.exclusions) && value.exclusions.every(item => object(item) && scope(item.scope) && text(item.rationale));
}
function assignment(value: unknown): value is HostingAssignment {
  return object(value) && text(value.assignmentId) && positive(value.revision) && text(value.offeringId)
    && text(value.systemId) && snapshot(value.hostingScope) && array(value.assignedScopes, scope) && text(value.relationshipState);
}
function hostingPage<T extends { offeringId: string }>(value: unknown, id: string, page: number, valid: (item: unknown) => item is T): Page<T> {
  if (!object(value) || !array(value.items, valid) || value.page !== page || !positive(value.pageSize)
    || typeof value.total !== 'number' || !Number.isSafeInteger(value.total) || value.total < value.items.length
    || value.items.length > value.pageSize || value.items.some(item => item.offeringId !== id)) {
    throw new Error('The server did not return a complete hosting page for this offering. Retry the read; no scope or allocation can be inferred.');
  }
  return { items: value.items, page, pageSize: value.pageSize, total: value.total };
}
const scopeKey = (value: AzureScope) => [
  value.cloud, value.directoryTenantId.trim().toLowerCase(), value.subscriptionId.trim().toLowerCase(),
  value.resourceId.trim().replace(/\/+$/, '').toLowerCase(),
].join('|');
const sameScopes = (left: AzureScope[], right: AzureScope[]) =>
  JSON.stringify(left.map(scopeKey).sort()) === JSON.stringify(right.map(scopeKey).sort());
const sameMaterial = <T>(left: T[], right: T[], key: (value: T) => string) =>
  JSON.stringify(left.map(key).sort()) === JSON.stringify(right.map(key).sort());
const citationKey = (value: Citation) => JSON.stringify([
  value.packageId.trim().toLowerCase(), value.artifactId.trim().toLowerCase(),
  value.archivePath.trim(), value.locator.trim(), value.quote.trim(),
]);

export async function listHostingScopes(id: string, page = 1, signal?: AbortSignal) {
  const data = await packageRequest<unknown>({
    url: `${offeringPath(id)}/hosting-scope-revisions`, params: { page, pageSize: 25 }, signal,
  });
  return hostingPage(data, id, page, scopeRevision);
}
export async function getHostingScope(id: string, revisionId: string, signal?: AbortSignal) {
  const data = await packageRequest<unknown>({
    url: `${offeringPath(id)}/hosting-scope-revisions/${encodeURIComponent(revisionId)}`, signal,
  });
  if (!scopeRevision(data) || data.offeringId !== id || data.snapshot.revisionId !== revisionId) {
    throw new Error('The server did not return the requested hosting snapshot. Reload before configuring this offering.');
  }
  return data;
}
export async function listHostingAssignments(id: string, page = 1, signal?: AbortSignal) {
  const data = await packageRequest<unknown>({
    url: `${offeringPath(id)}/hosting-assignments`, params: { page, pageSize: 25 }, signal,
  });
  return hostingPage(data, id, page, assignment);
}
export async function createHostingScope(id: string, data: HostingScopeInput, key: string): Promise<HostingScopeRevision> {
  const receipt = await packageRequest<unknown>({
    method: 'POST', url: `${offeringPath(id)}/hosting-scope-revisions`, data, headers: { 'Idempotency-Key': key },
  });
  if (!scopeRevision(receipt) || receipt.offeringId !== id || receipt.predecessorRevisionId !== data.predecessorRevisionId
    || receipt.name !== data.name.trim() || !sameScopes(receipt.permittedScopes, data.permittedScopes)
    || !sameMaterial(receipt.exclusions, data.exclusions, item => JSON.stringify([scopeKey(item.scope), item.rationale.trim()]))
    || !sameMaterial(receipt.citations, data.citations, citationKey)) {
    throw new Error('The hosting scope receipt is incomplete or does not match this intent. Keep inputs and retry the same operation.');
  }
  return receipt;
}
export async function createHostingAssignment(id: string, data: HostingAssignmentInput, key: string): Promise<HostingAssignment> {
  const receipt = await packageRequest<unknown>({
    method: 'POST', url: `${offeringPath(id)}/hosting-assignments`, data, headers: { 'Idempotency-Key': key },
  });
  if (!assignment(receipt) || receipt.offeringId !== id || receipt.systemId !== data.systemId.trim()
    || receipt.hostingScope.revisionId !== data.hostingScopeRevisionId || !sameScopes(receipt.assignedScopes, data.assignedScopes)
    || receipt.relationshipState !== 'Undetermined') {
    throw new Error('The hosting assignment receipt is incomplete or does not match this intent. Keep target IDs and retry the same operation.');
  }
  return receipt;
}
