import type { ApplicableProviderCapability, PagedResult, ProviderRelationship } from './types';

const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === 'object' && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0;
const integer = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const strings = (value: unknown) => Array.isArray(value) && value.every(item => typeof item === 'string');
const scope = (value: unknown) => record(value) && ['AzureCloud', 'AzureUSGovernment'].includes(String(value.cloud))
  && ['directoryTenantId', 'subscriptionId', 'resourceId'].every(key => text(value[key]));
const scopes = (value: unknown) => Array.isArray(value) && value.every(scope);
const snapshot = (value: unknown) => record(value) && text(value.revisionId) && integer(value.revision) && text(value.snapshotHash);
const nullableText = (value: unknown) => value === null || typeof value === 'string';

export function readPage<T>(value: unknown, valid: (item: unknown) => boolean, label: string): PagedResult<T> {
  if (!record(value) || !Array.isArray(value.items) || !value.items.every(valid)
    || !integer(value.page) || value.page < 1 || !integer(value.pageSize) || value.pageSize < 1
    || !integer(value.total)) {
    throw new Error(`The ${label} response is incomplete. Retry before continuing.`);
  }
  return value as unknown as PagedResult<T>;
}

export function relationshipPage(value: unknown): PagedResult<ProviderRelationship> {
  return readPage(value, item => record(item)
    && (item.relationshipId === null || text(item.relationshipId))
    && ['assignmentId', 'offeringId', 'systemId', 'state'].every(key => text(item[key]))
    && integer(item.revision) && integer(item.assignmentRevision)
    && scopes(item.assignedScopes) && typeof item.reviewRequired === 'boolean' && typeof item.canAssociate === 'boolean'
    && ['offeringName', 'providerName', 'systemName', 'hostingScopeName'].every(key => nullableText(item[key])), 'provider relationship');
}

export function capabilityPage(value: unknown): PagedResult<ApplicableProviderCapability> {
  return readPage(value, item => record(item)
    && ['capabilityId', 'releaseId', 'releaseSnapshotHash', 'offeringId',
      'assignmentId', 'applicabilityPreviewHash', 'applicabilityState',
      'authorizationRelationship'].every(key => text(item[key]))
    && nullableText(item.capabilityName) && nullableText(item.offeringName)
    && integer(item.releaseRevision) && integer(item.assignmentRevision) && snapshot(item.applicability)
    && ['reasonCodes', 'providerCoverage', 'sharedDuties', 'customerDuties', 'outstandingDecisions'].every(key => strings(item[key]))
    && ['canProposeAdoption', 'canConfirmResponsibilities', 'relationshipReviewRequired'].every(key => typeof item[key] === 'boolean')
    && Array.isArray(item.sourceReferences) && item.sourceReferences.every(source => record(source)
      && ['referenceId', 'title', 'locator'].every(key => text(source[key])) && typeof source.canReadContent === 'boolean'),
  'published capability');
}
