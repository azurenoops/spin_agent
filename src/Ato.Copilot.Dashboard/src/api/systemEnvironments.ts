import apiClient from './client';
import { packageRequest } from '../features/package-imports/request';
import type { ProviderScope } from '../features/provider-authorizations/types';

export type EnvironmentSource = 'ProviderAllocation' | 'OrganizationOwned';
export type EnvironmentAccessState = 'NotChecked' | 'Available' | 'Denied' | 'Unavailable' | 'Stale' | 'Blocked';
export type EnvironmentScopeReviewState = 'PendingReview' | 'Reviewed' | 'ReconciliationRequired';
export type EnvironmentAttachmentState = 'Attached' | 'Detached' | 'ReconciliationRequired';
export type EnvironmentAllocationState = 'Scheduled' | 'Active' | 'Expired' | 'Withdrawn' | 'Replaced';
export interface EnvironmentRegistration {
  registrationId: string; ownerTenantId: string; subscriptionId: string; directoryTenantId: string;
  cloud: 'AzureCloud' | 'AzureUSGovernment'; displayName: string; status: string; lastVerifiedAt: string;
}
export interface EnvironmentProvenance {
  source: string; externalId: string | null; sourceRevision: string | null;
  reconciliationState: string; evidenceReference: string | null; recordedAt: string;
}
export interface EnvironmentPermissionFlags {
  canManageEnvironments: boolean; canCheckAccess: boolean; canRunAssessments: boolean;
  canManageMonitoring: boolean; canRegisterSubscriptions: boolean;
}
export interface EnvironmentSourceCheck {
  sourceId: string; kind: 'Access' | 'Collection'; state: EnvironmentAccessState | 'Healthy' | 'Degraded' | 'Unsupported';
  required: boolean; attemptedAt: string | null; lastSucceededAt: string | null;
  reason: string | null; errorCode: string | null; sourceRevision: string | null; evidenceReference: string | null;
}
export interface EnvironmentCheckState {
  state: EnvironmentAccessState; checkedAt: string | null; reason: string | null; sources?: EnvironmentSourceCheck[];
}
export interface EnvironmentMonitoringState {
  configured: boolean; enabled: boolean; health: 'NotEvaluated' | 'Healthy' | 'Degraded' | 'Unavailable' | 'Stale';
  evaluatedAt: string | null; reason: string | null;
  sources?: EnvironmentSourceCheck[];
}
export interface EnvironmentResource { resourceId: string; name: string; resourceType: string; resourceGroup: string; location: string | null }
export interface EnvironmentExcludedResource { resourceId: string; rationale: string }
export interface EnvironmentScope {
  revisionId: string; version: number; reviewState: EnvironmentScopeReviewState; resourceIds: string[];
  exclusions: EnvironmentExcludedResource[]; sharedDependencyResourceIds: string[]; discoveredAt: string;
  reviewedBy?: string | null; reviewedAt?: string | null;
}
export interface EnvironmentSourceSelection {
  source: EnvironmentSource; registrationId: string; allocationId: string | null; expectedAllocationVersion: number | null;
}
export interface EnvironmentChoice {
  choiceId: string; source: EnvironmentSource; registration: EnvironmentRegistration;
  allocationId: string | null; allocationVersion: number | null; offeringId: string | null; offeringName: string | null;
  hostingScopeRevisionId: string | null; allocationState: EnvironmentAllocationState | null; startsAt: string | null; expiresAt: string | null;
  provenance: EnvironmentProvenance; eligible: boolean; ineligibleReason: string | null;
  providerName?: string | null; consumerName?: string | null; hostingScopeName?: string | null;
}
export interface SystemEnvironmentAttachment {
  attachmentId: string; systemId: string; version: number; source: EnvironmentSource; registration: EnvironmentRegistration;
  allocationId: string | null; allocationVersion: number | null; offeringId: string | null; offeringName: string | null;
  hostingAssignmentId: string | null; hostingReviewState: string; attachmentState: EnvironmentAttachmentState; scope: EnvironmentScope;
  assessmentAccess: EnvironmentCheckState; monitoringAccess: EnvironmentCheckState; monitoring: EnvironmentMonitoringState;
  readiness: EnvironmentCheckState; provenance: EnvironmentProvenance; updatedAt: string;
  providerName?: string | null; consumerName?: string | null; hostingScopeName?: string | null;
  hostingScopeRevisionId?: string | null;
  allocationState?: EnvironmentAllocationState | null;
  allocationStartsAt?: string | null; allocationExpiresAt?: string | null;
}
export interface LegacyEnvironmentReference { referenceId: string; kind: string; displayName: string; reconciliationState: string; reason: string }
export interface SystemEnvironmentsResponse {
  systemId: string; version: number; permissions: EnvironmentPermissionFlags;
  attachments: SystemEnvironmentAttachment[]; legacyReferences: LegacyEnvironmentReference[];
  providerScopes?: SystemProviderScope[];
  hostingLinks?: EnvironmentHostingLink[];
}
export interface SystemProviderScope {
  providerId?: string; hostingScopeRevision?: number;
  publishedDuties?: ProviderScopePublishedDuties;
  responsibilityReview?: ProviderScopeResponsibilityReview;
  assignmentId: string; assignmentVersion: number; relationshipId: string | null;
  offeringId: string; offeringName: string; providerName: string | null;
  hostingScopeRevisionId: string; hostingScopeName: string; state: 'Active' | 'Removed';
  relationshipState: string; reviewRequired: boolean; assignedScopes: ProviderScope[];
  selectionVersion: number;
}
export interface SystemProviderScopeChoice {
  providerId: string; hostingScopeRevision: number;
  publishedDuties?: ProviderScopePublishedDuties;
  offeringId: string; offeringVersion: number; offeringName: string; providerName: string | null;
  hostingScopeRevisionId: string; hostingScopeName: string; permittedScopes: ProviderScope[];
  exclusions: { scope: ProviderScope; rationale: string }[]; eligibilitySource: string;
}
export interface ProviderScopePublishedDuties {
  state: 'Available' | 'Unavailable'; capabilities: ProviderScopeCapabilityDuties[]; reason: string | null;
}
export interface ProviderScopeCapabilityDuties {
  capabilityId: string; capabilityName: string; description: string | null;
  releaseId: string; releaseRevision: number; releaseSnapshotHash: string; contentHash: string;
  applicabilityContextId: string; providerControlIds: string[]; sharedControlIds: string[]; customerControlIds: string[];
}
export interface ProviderScopeResponsibilityReview {
  state: 'Unavailable' | 'NotAdopted' | 'ReviewRequired' | 'Reviewed' | 'Removed';
  canReview: boolean; canConfirm: boolean; reason: string | null;
}
export interface SystemProviderScopeChoicesResponse {
  systemId: string; version: number; canManage: boolean; choices: SystemProviderScopeChoice[];
}
export interface AddSystemProviderScopeRequest {
  expectedVersion: number; offeringId: string; expectedOfferingVersion: number; hostingScopeRevisionId: string;
}
export interface PreviewProviderScopeRemovalRequest {
  expectedVersion: number; expectedAssignmentVersion: number; expectedSelectionVersion: number; rationale: string;
}
export interface EnvironmentHostingLink {
  linkId: string; attachmentId: string; assignmentId: string; version: number; state: 'Linked' | 'Unlinked';
  source: 'Explicit' | 'RetainedLegacy'; updatedAt: string;
}
export interface PreviewEnvironmentHostingLinkRequest {
  expectedVersion: number; attachmentId: string; expectedAttachmentVersion: number;
  assignmentId: string; expectedAssignmentVersion: number; action: 'Link' | 'Unlink'; rationale: string;
}
export interface ApplySystemEnvironmentsRequest { expectedVersion: number; items: ApplySystemEnvironmentRequest[] }
export interface EnvironmentChoicesResponse {
  systemId: string; version: number; permissions: EnvironmentPermissionFlags; choices: EnvironmentChoice[]; registrationHref: string;
}
export interface DiscoverEnvironmentResourcesRequest { expectedVersion: number; selection: EnvironmentSourceSelection }
export interface EnvironmentDiscoveryResponse {
  systemId: string; discoveryToken: string; expiresAt: string; selection: EnvironmentSourceSelection;
  resources: EnvironmentResource[]; discoveredAt: string;
}
export interface ApplySystemEnvironmentRequest {
  expectedVersion: number; selection: EnvironmentSourceSelection; discoveryToken: string; resourceIds: string[];
  exclusions: EnvironmentExcludedResource[]; sharedDependencyResourceIds: string[];
  /** Optional existing active system assignment; initial apply links atomically, never creates provider hosting. */
  reuseHostingAssignmentId: string | null;
}
export interface EnvironmentScopeChangeRequest {
  expectedVersion: number; expectedAttachmentVersion: number; discoveryToken: string; resourceIds: string[];
  exclusions: EnvironmentExcludedResource[]; sharedDependencyResourceIds: string[]; rationale: string;
  reviewPendingScope?: boolean;
}
export interface EnvironmentImpactSystem {
  systemId: string; systemName: string; attachmentId: string; attachmentVersion: number;
  selectedResourceCount: number; assessmentAffected: boolean; monitoringAffected: boolean;
}
export interface EnvironmentImpactPreview {
  previewId: string; systemId: string | null; allocationId: string | null; expectedVersion: number;
  expiresAt: string; systems: EnvironmentImpactSystem[]; requiresScopeReview: boolean; warnings: string[];
  blockers?: string[]; canCommit?: boolean;
}
export interface CommitEnvironmentChangeRequest { expectedVersion: number; previewId: string; rationale: string; acknowledgeImpact: boolean }
export interface PreviewEnvironmentDetachRequest { expectedVersion: number; expectedAttachmentVersion: number; rationale: string }
export interface CheckEnvironmentAccessRequest { expectedVersion: number; purpose: 'Assessment' | 'Monitoring' }
export interface EnvironmentAccessResponse { systemId: string; version: number; attachments: SystemEnvironmentAttachment[] }
export interface ProviderEnvironmentAllocation {
  allocationId: string; version: number; offeringId: string; offeringName: string; consumerTenantId: string; consumerName: string;
  registration: EnvironmentRegistration; hostingScopeRevisionId: string; permittedResourceScopes: string[];
  state: EnvironmentAllocationState; startsAt: string; expiresAt: string | null; provenance: EnvironmentProvenance; systemCount: number;
  providerName?: string | null; hostingScopeName?: string | null;
}
export interface ProviderAllocationConsumer { tenantId: string; name: string }
export interface ProviderAllocationHostingScope {
  revisionId: string; name: string; permittedResourceScopes: string[]; excludedResourceScopes: string[];
}
export interface ProviderAllocationChoicesResponse {
  offeringId: string; offeringVersion: number; canManage: boolean; registrations: EnvironmentRegistration[];
  consumers: ProviderAllocationConsumer[]; releasedScopes: ProviderAllocationHostingScope[]; registrationHref: string;
}
export interface ProviderAllocationsResponse { offeringId: string; canManage: boolean; allocations: ProviderEnvironmentAllocation[] }
export interface RecordProviderAllocationRequest {
  expectedOfferingVersion: number; registrationId: string; consumerTenantId: string; hostingScopeRevisionId: string;
  permittedResourceScopes: string[]; startsAt: string; expiresAt: string | null; provenance: EnvironmentProvenance;
}
export interface ProviderAllocationUsageResponse { allocationId: string; version: number; systems: EnvironmentImpactSystem[] }
export interface PreviewAllocationChangeRequest {
  expectedVersion: number; action: 'Withdraw' | 'Replace'; replacementAllocationId: string | null; rationale: string;
}

const systemRoot = (id: string) => `/systems/${encodeURIComponent(id)}/environments`;
const attachmentRoot = (systemId: string, id: string) => `${systemRoot(systemId)}/${encodeURIComponent(id)}`;
const providerRoot = (id: string) => `/api/csp/offerings/${encodeURIComponent(id)}/environment-allocations`;
function keyHeader(key: string) {
  if (!key.trim()) throw new Error('A stable replay key is required for an environment change.');
  return { 'Idempotency-Key': key };
}
function assertSystem(value: { systemId: string; version: number }, id: string) {
  if (!value || value.systemId !== id || !Number.isSafeInteger(value.version) || value.version < 0)
    throw new Error('The environment response did not identify this system and version.');
}
function assertPermissions(value: EnvironmentPermissionFlags) {
  if (!value || ['canManageEnvironments', 'canCheckAccess', 'canRunAssessments', 'canManageMonitoring', 'canRegisterSubscriptions']
    .some(key => typeof value[key as keyof EnvironmentPermissionFlags] !== 'boolean'))
    throw new Error('Environment permissions could not be confirmed.');
}
function assertWorkspace(value: SystemEnvironmentsResponse, id: string) {
  assertSystem(value, id);
  assertPermissions(value.permissions);
  if (!Array.isArray(value.attachments) || !Array.isArray(value.legacyReferences)
    || value.attachments.some(x => x.systemId !== id || !x.attachmentId || !x.scope
      || !Array.isArray(x.scope.resourceIds) || !x.assessmentAccess || !x.monitoringAccess || !x.monitoring || !x.readiness))
    throw new Error('The system environments and independent source states could not be confirmed.');
  value.attachments.forEach(assertAttachmentSources);
  return value;
}
function assertAttachmentSources(value: SystemEnvironmentAttachment) {
  if ([value.assessmentAccess.sources, value.monitoringAccess.sources, value.monitoring.sources]
    .some(sources => sources !== undefined && !Array.isArray(sources)))
    throw new Error('The required source-check collection could not be confirmed.');
  for (const access of [value.assessmentAccess, value.monitoringAccess]) {
    const required = access.sources?.filter(source => source.required);
    if (access.state === 'Available' && (!required?.length
      || required.some(source => source.kind !== 'Access' || source.state !== 'Available' || !source.lastSucceededAt)))
      throw new Error('Available access is not supported by successful required source checks.');
  }
  const required = value.monitoring.sources?.filter(source => source.required);
  if (value.monitoring.health === 'Healthy' && (!required?.length
    || required.some(source => source.kind !== 'Collection' || source.state !== 'Healthy' || !source.lastSucceededAt)))
    throw new Error('Healthy monitoring is not supported by required collection source evidence.');
}
function assertOffering(value: { offeringId: string }, id: string) {
  if (!value || value.offeringId !== id) throw new Error('The allocation response did not identify this offering.');
}
function assertPreview(value: EnvironmentImpactPreview, expectedVersion: number, systemId?: string, allocationId?: string) {
  if (!value?.previewId || value.expectedVersion !== expectedVersion || !Array.isArray(value.systems)
    || !Array.isArray(value.warnings) || (systemId !== undefined && value.systemId !== systemId)
    || (allocationId !== undefined && value.allocationId !== allocationId))
    throw new Error('The impact preview did not identify this target and version.');
  return value;
}
export async function getSystemEnvironments(systemId: string, signal?: AbortSignal): Promise<SystemEnvironmentsResponse> {
  const { data } = await apiClient.get<SystemEnvironmentsResponse>(systemRoot(systemId), { signal });
  return assertWorkspace(data, systemId);
}
export async function getSystemProviderScopeChoices(systemId: string, signal?: AbortSignal): Promise<SystemProviderScopeChoicesResponse> {
  const { data } = await apiClient.get<SystemProviderScopeChoicesResponse>(`${systemRoot(systemId)}/provider-scope-choices`, { signal });
  assertSystem(data, systemId);
  if (typeof data.canManage !== 'boolean' || !Array.isArray(data.choices))
    throw new Error('Eligible provider scopes could not be confirmed for this system.');
  return data;
}
export async function addSystemProviderScope(systemId: string, body: AddSystemProviderScopeRequest, key: string): Promise<SystemEnvironmentsResponse> {
  const { data } = await apiClient.post<SystemEnvironmentsResponse>(`${systemRoot(systemId)}/provider-scopes`, body, { headers: keyHeader(key) });
  return assertWorkspace(data, systemId);
}
export async function previewSystemProviderScopeRemoval(systemId: string, assignmentId: string, body: PreviewProviderScopeRemovalRequest): Promise<EnvironmentImpactPreview> {
  const { data } = await apiClient.post<EnvironmentImpactPreview>(
    `${systemRoot(systemId)}/provider-scopes/${encodeURIComponent(assignmentId)}/remove-preview`, body);
  return assertPreview(data, body.expectedVersion, systemId);
}
export async function removeSystemProviderScope(systemId: string, assignmentId: string, body: CommitEnvironmentChangeRequest, key: string): Promise<SystemEnvironmentsResponse> {
  const { data } = await apiClient.post<SystemEnvironmentsResponse>(
    `${systemRoot(systemId)}/provider-scopes/${encodeURIComponent(assignmentId)}/remove`, body, { headers: keyHeader(key) });
  return assertWorkspace(data, systemId);
}
export async function previewEnvironmentHostingLink(systemId: string, body: PreviewEnvironmentHostingLinkRequest): Promise<EnvironmentImpactPreview> {
  const { data } = await apiClient.post<EnvironmentImpactPreview>(`${systemRoot(systemId)}/hosting-links/preview`, body);
  return assertPreview(data, body.expectedVersion, systemId);
}
export async function commitEnvironmentHostingLink(systemId: string, body: CommitEnvironmentChangeRequest, key: string): Promise<SystemEnvironmentsResponse> {
  const { data } = await apiClient.post<SystemEnvironmentsResponse>(`${systemRoot(systemId)}/hosting-links/commit`, body, { headers: keyHeader(key) });
  return assertWorkspace(data, systemId);
}
export async function applySystemEnvironments(systemId: string, body: ApplySystemEnvironmentsRequest, key: string): Promise<SystemEnvironmentsResponse> {
  const { data } = await apiClient.post<SystemEnvironmentsResponse>(`${systemRoot(systemId)}/apply-batch`, body, { headers: keyHeader(key) });
  return assertWorkspace(data, systemId);
}
export async function getEnvironmentChoices(systemId: string, signal?: AbortSignal): Promise<EnvironmentChoicesResponse> {
  const { data } = await apiClient.get<EnvironmentChoicesResponse>(`${systemRoot(systemId)}/choices`, { signal });
  assertSystem(data, systemId);
  assertPermissions(data.permissions);
  if (!Array.isArray(data.choices) || typeof data.registrationHref !== 'string'
    || (!data.permissions.canRegisterSubscriptions && data.registrationHref !== ''))
    throw new Error('Environment choices could not be confirmed.');
  return data;
}
export async function discoverEnvironmentResources(systemId: string, body: DiscoverEnvironmentResourcesRequest): Promise<EnvironmentDiscoveryResponse> {
  const { data } = await apiClient.post<EnvironmentDiscoveryResponse>(`${systemRoot(systemId)}/discover`, body);
  if (data?.systemId !== systemId || !data.discoveryToken || !Array.isArray(data.resources)
    || !data.selection || data.selection.source !== body.selection.source
    || data.selection.registrationId !== body.selection.registrationId
    || data.selection.allocationId !== body.selection.allocationId
    || data.selection.expectedAllocationVersion !== body.selection.expectedAllocationVersion)
    throw new Error('The discovery response did not identify this system and source selection.');
  return data;
}
export async function applySystemEnvironment(systemId: string, body: ApplySystemEnvironmentRequest, key: string): Promise<SystemEnvironmentsResponse> {
  const { data } = await apiClient.post<SystemEnvironmentsResponse>(`${systemRoot(systemId)}/apply`, body, { headers: keyHeader(key) });
  return assertWorkspace(data, systemId);
}
export async function previewEnvironmentScope(systemId: string, attachmentId: string, body: EnvironmentScopeChangeRequest): Promise<EnvironmentImpactPreview> {
  const { data } = await apiClient.post<EnvironmentImpactPreview>(`${attachmentRoot(systemId, attachmentId)}/scope-preview`, body);
  return assertPreview(data, body.expectedVersion, systemId);
}
export async function commitEnvironmentScope(systemId: string, attachmentId: string, body: CommitEnvironmentChangeRequest, key: string): Promise<SystemEnvironmentsResponse> {
  const { data } = await apiClient.post<SystemEnvironmentsResponse>(`${attachmentRoot(systemId, attachmentId)}/scope-commit`, body, { headers: keyHeader(key) });
  return assertWorkspace(data, systemId);
}
export async function previewEnvironmentDetach(systemId: string, attachmentId: string, body: PreviewEnvironmentDetachRequest): Promise<EnvironmentImpactPreview> {
  const { data } = await apiClient.post<EnvironmentImpactPreview>(`${attachmentRoot(systemId, attachmentId)}/detach-preview`, body);
  return assertPreview(data, body.expectedVersion, systemId);
}
export async function detachSystemEnvironment(systemId: string, attachmentId: string, body: CommitEnvironmentChangeRequest, key: string): Promise<SystemEnvironmentsResponse> {
  const { data } = await apiClient.post<SystemEnvironmentsResponse>(`${attachmentRoot(systemId, attachmentId)}/detach`, body, { headers: keyHeader(key) });
  return assertWorkspace(data, systemId);
}
export async function checkEnvironmentAccess(systemId: string, body: CheckEnvironmentAccessRequest): Promise<EnvironmentAccessResponse> {
  const { data } = await apiClient.post<EnvironmentAccessResponse>(`${systemRoot(systemId)}/check-access`, body);
  assertSystem(data, systemId);
  if (!Array.isArray(data.attachments) || data.attachments.some(x => x.systemId !== systemId || !x.assessmentAccess || !x.monitoringAccess || !x.monitoring))
    throw new Error('The independent environment access results could not be confirmed.');
  data.attachments.forEach(assertAttachmentSources);
  return data;
}
export async function getProviderAllocationChoices(offeringId: string, signal?: AbortSignal): Promise<ProviderAllocationChoicesResponse> {
  const data = await packageRequest<ProviderAllocationChoicesResponse>({ url: `${providerRoot(offeringId)}/choices`, signal });
  assertOffering(data, offeringId);
  if (typeof data.canManage !== 'boolean' || !Array.isArray(data.registrations) || !Array.isArray(data.consumers) || !Array.isArray(data.releasedScopes))
    throw new Error('Provider allocation choices could not be confirmed.');
  return data;
}
export async function listProviderEnvironmentAllocations(offeringId: string, signal?: AbortSignal): Promise<ProviderAllocationsResponse> {
  const data = await packageRequest<ProviderAllocationsResponse>({ url: providerRoot(offeringId), signal });
  assertOffering(data, offeringId);
  if (typeof data.canManage !== 'boolean' || !Array.isArray(data.allocations)
    || data.allocations.some(x => x.offeringId !== offeringId))
    throw new Error('Provider allocations could not be confirmed.');
  return data;
}
export async function recordProviderEnvironmentAllocation(offeringId: string, body: RecordProviderAllocationRequest, key: string): Promise<ProviderEnvironmentAllocation> {
  const data = await packageRequest<ProviderEnvironmentAllocation>({ method: 'POST', url: providerRoot(offeringId), data: body, headers: keyHeader(key) });
  assertOffering(data, offeringId);
  if (!data.allocationId || !Number.isSafeInteger(data.version) || data.version < 1)
    throw new Error('The saved allocation could not be confirmed.');
  return data;
}
export async function getProviderAllocationUsage(offeringId: string, allocationId: string, signal?: AbortSignal): Promise<ProviderAllocationUsageResponse> {
  const data = await packageRequest<ProviderAllocationUsageResponse>({ url: `${providerRoot(offeringId)}/${encodeURIComponent(allocationId)}/usage`, signal });
  if (data?.allocationId !== allocationId || !Array.isArray(data.systems)) throw new Error('Allocation usage could not be confirmed.');
  return data;
}
export async function previewProviderAllocationChange(offeringId: string, allocationId: string, body: PreviewAllocationChangeRequest): Promise<EnvironmentImpactPreview> {
  const data = await packageRequest<EnvironmentImpactPreview>({ method: 'POST',
    url: `${providerRoot(offeringId)}/${encodeURIComponent(allocationId)}/impact-preview`, data: body });
  return assertPreview(data, body.expectedVersion, undefined, allocationId);
}
export async function commitProviderAllocationChange(offeringId: string, allocationId: string, body: CommitEnvironmentChangeRequest, key: string): Promise<ProviderEnvironmentAllocation> {
  const data = await packageRequest<ProviderEnvironmentAllocation>({ method: 'POST',
    url: `${providerRoot(offeringId)}/${encodeURIComponent(allocationId)}/change`, data: body, headers: keyHeader(key) });
  assertOffering(data, offeringId);
  if (data.allocationId !== allocationId) throw new Error('The changed allocation could not be confirmed.');
  return data;
}
