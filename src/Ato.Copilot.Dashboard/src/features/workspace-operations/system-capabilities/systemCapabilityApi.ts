import { WorkspaceOperationError, workspaceRequest } from '../workspaceRequest';
import type {
  SystemCapabilityAccess, SystemCapabilityDetail, SystemCapabilityItem, SystemCapabilityOperation,
  SystemCapabilityPage, SystemCapabilityPrepared, SystemCapabilityQuery, SystemCapabilityRecordKey,
  SystemCapabilitySelection, SystemCapabilitySource,
  SystemComponentPlacementOptions, SystemComponentPlacementResult,
} from './systemCapabilityTypes';

function base(tenantId: string, systemId: string) {
  return `/api/workspaces/organizations/${encodeURIComponent(tenantId)}/systems/${encodeURIComponent(systemId)}/security-capabilities`;
}

function recordPath(tenantId: string, systemId: string, key: SystemCapabilityRecordKey) {
  return `${base(tenantId, systemId)}/${key.source}/${key.recordType}/${encodeURIComponent(key.recordId)}`;
}

function invalidResponse(): never {
  throw new WorkspaceOperationError('The system capability service returned incomplete or mismatched data. Reload to try again.',
    502, 'INVALID_SYSTEM_CAPABILITY_RESPONSE');
}

function validAccess(value: SystemCapabilityAccess | undefined) {
  const keys = ['canRead', 'canManage', 'canReviewResponsibilities', 'canManageEvidence', 'canAuthorNarratives', 'canReviewNarratives'] as const;
  return value && value.canRead && keys.every(key => typeof value[key] === 'boolean');
}

function validItem(value: SystemCapabilityItem | undefined) {
  return value && ['local', 'provider'].includes(value.source)
    && ['capability', 'component'].includes(value.recordType)
    && typeof value.recordId === 'string' && value.recordId.length > 0
    && typeof value.name === 'string' && typeof value.sourceRevision === 'string'
    && typeof value.isApplied === 'boolean' && typeof value.isAvailable === 'boolean'
    && Array.isArray(value.components) && Array.isArray(value.capabilities) && Array.isArray(value.placements)
    && Array.isArray(value.controlIds) && Number.isInteger(value.reviewRequiredCount);
}

function validateOperation(value: SystemCapabilityOperation, tenantId: string, systemId: string, operationId?: string) {
  if (!value || value.tenantId?.toLowerCase() !== tenantId.toLowerCase()
    || value.systemId?.toLowerCase() !== systemId.toLowerCase()
    || typeof value.operationId !== 'string' || !value.operationId
    || (operationId !== undefined && value.operationId !== operationId)
    || !['Setup', 'Removal'].includes(value.kind)
    || !['Prepared', 'Partial', 'Completed', 'InProgress'].includes(value.state)
    || !Number.isInteger(value.revision) || value.revision < 1
    || !Array.isArray(value.selections) || !Array.isArray(value.plannedWrites) || !Array.isArray(value.outcomes)) invalidResponse();
  return value;
}

function validatePrepared(value: SystemCapabilityPrepared, tenantId: string, systemId: string, kind: 'Setup' | 'Removal') {
  if (!value || typeof value.existing !== 'boolean') invalidResponse();
  validateOperation(value.operation, tenantId, systemId);
  if (value.operation.kind !== kind) invalidResponse();
  return value;
}

export async function listSystemCapabilities(
  tenantId: string, systemId: string, query: SystemCapabilityQuery, signal?: AbortSignal,
) {
  const result = await workspaceRequest<SystemCapabilityPage>({
    method: 'GET', url: base(tenantId, systemId), params: query, signal,
  });
  if (!result || !validAccess(result.permissions) || !Array.isArray(result.items) || !result.items.every(validItem)
    || !Array.isArray(result.boundaries) || !Number.isInteger(result.total) || result.total < 0
    || !Number.isInteger(result.page) || result.page < 1 || !Number.isInteger(result.pageSize) || result.pageSize < 1
    || (result.scope === 'applied' && result.items.some(item => !item.isApplied))
    || result.scope !== (query.scope ?? 'applied') || result.grouping !== (query.grouping ?? 'capability')) invalidResponse();
  return result;
}

export async function getSystemCapability(
  tenantId: string, systemId: string, key: SystemCapabilityRecordKey, signal?: AbortSignal,
) {
  const result = await workspaceRequest<SystemCapabilityDetail>({
    method: 'GET', url: recordPath(tenantId, systemId, key), signal,
  });
  if (!result || !validItem(result.item) || !validAccess(result.permissions)
    || result.item.source !== key.source || result.item.recordType !== key.recordType || result.item.recordId !== key.recordId
    || !Array.isArray(result.controls) || !Array.isArray(result.evidence) || !Array.isArray(result.narratives)
    || result.evidence.some(evidence => typeof evidence.openUrl !== 'string' || !evidence.openUrl.startsWith('/api/')
      || evidence.openUrl.includes('\\') || evidence.openUrl.includes('://'))
    || typeof result.relationshipRevision !== 'string') invalidResponse();
  return result;
}

export async function prepareSystemCapabilitySetup(
  tenantId: string, systemId: string, body: { idempotencyKey: string; selections: SystemCapabilitySelection[] },
) {
  const result = await workspaceRequest<SystemCapabilityPrepared>({
    method: 'POST', url: `${base(tenantId, systemId)}/setups/prepare`, data: body,
  });
  return validatePrepared(result, tenantId, systemId, 'Setup');
}

export async function getSystemCapabilityOperation(tenantId: string, systemId: string, operationId: string, signal?: AbortSignal) {
  const result = await workspaceRequest<SystemCapabilityOperation>({
    method: 'GET', url: `${base(tenantId, systemId)}/setups/${encodeURIComponent(operationId)}`, signal,
  });
  return validateOperation(result, tenantId, systemId, operationId);
}

export async function completeSystemCapabilityOperation(
  tenantId: string, systemId: string, operationId: string, body: { expectedRevision: number },
) {
  const result = await workspaceRequest<SystemCapabilityOperation>({
    method: 'POST', url: `${base(tenantId, systemId)}/setups/${encodeURIComponent(operationId)}/complete`, data: body,
  });
  return validateOperation(result, tenantId, systemId, operationId);
}

export async function prepareSystemCapabilityRemoval(
  tenantId: string, systemId: string, source: SystemCapabilitySource, recordId: string,
  body: { idempotencyKey: string; sourceRevision: string; relationshipRevision: string },
) {
  const result = await workspaceRequest<SystemCapabilityPrepared>({
    method: 'POST', url: `${recordPath(tenantId, systemId, { source, recordType: 'capability', recordId })}/removals/prepare`, data: body,
  });
  return validatePrepared(result, tenantId, systemId, 'Removal');
}

export function reviewSystemCapabilityNarrative(
  tenantId: string, systemId: string, source: SystemCapabilitySource, recordId: string, proposalId: string,
  body: { expectedRevision: number; decision: string; note?: string },
) {
  return workspaceRequest<unknown>({
    method: 'POST',
    url: `${recordPath(tenantId, systemId, { source, recordType: 'capability', recordId })}/narrative-proposals/${encodeURIComponent(proposalId)}/review`,
    data: body,
  });
}

type ComponentKey = SystemCapabilityRecordKey & { recordType: 'component' };

export async function getSystemComponentPlacements(tenantId: string, systemId: string, key: ComponentKey, signal?: AbortSignal) {
  const result = await workspaceRequest<SystemComponentPlacementOptions>({
    method: 'GET', url: `${recordPath(tenantId, systemId, key)}/placements`, signal,
  });
  if (!result || result.source !== key.source || result.recordId !== key.recordId
    || typeof result.sourceRevision !== 'string' || typeof result.relationshipRevision !== 'string'
    || typeof result.canAssignBoundary !== 'boolean' || !Array.isArray(result.boundaries)
    || !Array.isArray(result.placements) || result.placements.some(placement =>
      !placement || typeof placement.canUnassign !== 'boolean' || typeof placement.id !== 'string' || typeof placement.revision !== 'string')) invalidResponse();
  return result;
}

function validatePlacementResult(result: SystemComponentPlacementResult, key: ComponentKey, action: 'Assigned' | 'Unassigned') {
  if (!result || result.source !== key.source || result.recordId !== key.recordId || result.action !== action
    || typeof result.relationshipRevision !== 'string' || typeof result.placementId !== 'string'
    || typeof result.boundaryId !== 'string') invalidResponse();
  return result;
}

export async function assignSystemComponentBoundary(tenantId: string, systemId: string, key: ComponentKey,
  body: { boundaryId: string; sourceRevision: string; relationshipRevision: string }, signal?: AbortSignal) {
  const result = validatePlacementResult(await workspaceRequest<SystemComponentPlacementResult>({
    method: 'POST', url: `${recordPath(tenantId, systemId, key)}/placements/assign`, data: body, signal,
  }), key, 'Assigned');
  if (result.boundaryId !== body.boundaryId) invalidResponse();
  return result;
}

export async function unassignSystemComponentBoundary(tenantId: string, systemId: string, key: ComponentKey, placementId: string,
  body: { sourceRevision: string; relationshipRevision: string; placementRevision: string }, signal?: AbortSignal) {
  const result = validatePlacementResult(await workspaceRequest<SystemComponentPlacementResult>({
    method: 'POST', url: `${recordPath(tenantId, systemId, key)}/placements/${encodeURIComponent(placementId)}/unassign`, data: body, signal,
  }), key, 'Unassigned');
  if (result.placementId !== placementId) invalidResponse();
  return result;
}
