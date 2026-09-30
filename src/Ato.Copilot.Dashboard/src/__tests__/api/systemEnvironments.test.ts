import { beforeEach, describe, expect, it, vi } from 'vitest';
import client from '../../api/client';
import { packageRequest } from '../../features/package-imports/request';
import {
  applySystemEnvironment, discoverEnvironmentResources, getSystemEnvironments, getEnvironmentChoices,
  previewEnvironmentScope, commitEnvironmentScope,
  getSystemProviderScopeChoices, addSystemProviderScope, applySystemEnvironments,
  previewEnvironmentHostingLink, commitEnvironmentHostingLink,
  listProviderEnvironmentAllocations, recordProviderEnvironmentAllocation,
} from '../../api/systemEnvironments';

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
vi.mock('../../features/package-imports/request', () => ({ packageRequest: vi.fn() }));
const permissions = { canManageEnvironments: true, canCheckAccess: true, canRunAssessments: false,
  canManageMonitoring: false, canRegisterSubscriptions: false };
const selection = { source: 'OrganizationOwned' as const, registrationId: 'registration', allocationId: null,
  expectedAllocationVersion: null };
const apply = { expectedVersion: 2, selection, discoveryToken: 'discovered', resourceIds: ['/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm'],
  exclusions: [], sharedDependencyResourceIds: [], reuseHostingAssignmentId: null };
beforeEach(() => vi.resetAllMocks());
describe('canonical environment transport', () => {
  it('keeps independent provider-scope and optional-link requests separate from atomic subscription apply', async () => {
    // Arrange
    const workspace = { systemId: 'a', version: 3, permissions, attachments: [], legacyReferences: [], providerScopes: [], hostingLinks: [] };
    vi.mocked(client.get).mockResolvedValue({ data: { systemId: 'a', version: 2, canManage: true, choices: [] } });
    vi.mocked(client.post).mockResolvedValue({ data: workspace });
    const provider = { expectedVersion: 2, offeringId: 'offering', expectedOfferingVersion: 1, hostingScopeRevisionId: 'scope' };
    // Act
    await getSystemProviderScopeChoices('a');
    await addSystemProviderScope('a', provider, 'scope-key');
    const explicitLink = { ...apply, reuseHostingAssignmentId: 'existing-assignment' };
    await applySystemEnvironments('a', { expectedVersion: 2, items: [explicitLink] }, 'bulk-key');
    // Assert
    expect(client.get).toHaveBeenCalledWith('/systems/a/environments/provider-scope-choices', { signal: undefined });
    expect(client.post).toHaveBeenCalledWith('/systems/a/environments/provider-scopes', provider, { headers: { 'Idempotency-Key': 'scope-key' } });
    expect(client.post).toHaveBeenCalledWith('/systems/a/environments/apply-batch', { expectedVersion: 2, items: [explicitLink] }, { headers: { 'Idempotency-Key': 'bulk-key' } });
  });
  it('previews and commits optional links without changing underlying scope or attachment requests', async () => {
    // Arrange
    const body = { expectedVersion: 2, attachmentId: 'attachment', expectedAttachmentVersion: 1,
      assignmentId: 'assignment', expectedAssignmentVersion: 1, action: 'Link' as const, rationale: 'Document this optional association.' };
    vi.mocked(client.post).mockResolvedValueOnce({ data: { previewId: 'preview', systemId: 'a', allocationId: null,
      expectedVersion: 2, expiresAt: '2026-10-01', systems: [], requiresScopeReview: false, warnings: [] } })
      .mockResolvedValueOnce({ data: { systemId: 'a', version: 3, permissions, attachments: [], legacyReferences: [] } });
    // Act
    const preview = await previewEnvironmentHostingLink('a', body);
    await commitEnvironmentHostingLink('a', { expectedVersion: 2, previewId: preview.previewId, rationale: body.rationale, acknowledgeImpact: true }, 'link-key');
    // Assert
    expect(client.post).toHaveBeenNthCalledWith(1, '/systems/a/environments/hosting-links/preview', body);
    expect(vi.mocked(client.post).mock.calls[1]?.[0]).toBe('/systems/a/environments/hosting-links/commit');
  });
  it('encodes system identity and rejects a cross-system result', async () => {
    // Arrange
    vi.mocked(client.get).mockResolvedValue({ data: { systemId: 'other', version: 2, permissions, attachments: [], legacyReferences: [] } });
    // Act / Assert
    await expect(getSystemEnvironments('a/b')).rejects.toThrow('system');
    expect(client.get).toHaveBeenCalledWith('/systems/a%2Fb/environments', { signal: undefined });
  });
  it('does not infer permission flags or a successful empty list from a malformed response', async () => {
    // Arrange
    vi.mocked(client.get).mockResolvedValue({ data: { systemId: 'a', version: 0, attachments: [] } });
    // Act / Assert
    await expect(getSystemEnvironments('a')).rejects.toThrow();
  });
  it('accepts a missing-registration handoff only when authorized by the server', async () => {
    // Arrange
    vi.mocked(client.get).mockResolvedValue({ data: { systemId: 'a', version: 0, permissions, choices: [], registrationHref: '' } });
    // Act / Assert
    expect((await getEnvironmentChoices('a')).registrationHref).toBe('');
    vi.mocked(client.get).mockResolvedValue({ data: { systemId: 'a', version: 0, permissions, choices: [],
      registrationHref: '/settings/azure-subscriptions' } });
    await expect(getEnvironmentChoices('a')).rejects.toThrow('choices');
  });
  it('rejects optimistic aggregate access when a required telemetry source is denied', async () => {
    // Arrange
    const source = { sourceId: 'policy', kind: 'Access', state: 'Denied', required: true,
      attemptedAt: '2026-09-29T20:00:00Z', lastSucceededAt: null, reason: 'Synthetic denial',
      errorCode: 'AZURE_ACCESS_DENIED', sourceRevision: null, evidenceReference: null };
    vi.mocked(client.get).mockResolvedValue({ data: { systemId: 'a', version: 1, permissions, legacyReferences: [],
      attachments: [{ attachmentId: 'attachment', systemId: 'a', scope: { resourceIds: [] },
        assessmentAccess: { state: 'Available', checkedAt: source.attemptedAt, reason: null, sources: [source] },
        monitoringAccess: { state: 'NotChecked', checkedAt: null, reason: null, sources: [] },
        monitoring: { configured: false, enabled: false, health: 'NotEvaluated', sources: [] },
        readiness: { state: 'NotChecked', checkedAt: null, reason: null } }] } });
    // Act / Assert
    await expect(getSystemEnvironments('a')).rejects.toThrow('source');
  });
  it('preserves the caller replay key and version across identical retries', async () => {
    // Arrange
    vi.mocked(client.post).mockResolvedValue({ data: { systemId: 'a', version: 3, permissions, attachments: [], legacyReferences: [] } });
    // Act
    await applySystemEnvironment('a', apply, 'stable-key');
    await applySystemEnvironment('a', apply, 'stable-key');
    // Assert
    expect(client.post).toHaveBeenNthCalledWith(1, '/systems/a/environments/apply', apply,
      { headers: { 'Idempotency-Key': 'stable-key' } });
    expect(vi.mocked(client.post).mock.calls[1]).toEqual(vi.mocked(client.post).mock.calls[0]);
  });
  it('rejects missing replay keys before making a mutation', async () => {
    // Arrange / Act / Assert
    await expect(applySystemEnvironment('a', apply, ' ')).rejects.toThrow('replay');
    expect(client.post).not.toHaveBeenCalled();
  });
  it('does not turn a discovery failure into empty resources', async () => {
    // Arrange
    const denied = { errorCode: 'ENVIRONMENT_DISCOVERY_UNAVAILABLE', error: 'Access denied' };
    vi.mocked(client.post).mockRejectedValue(denied);
    // Act / Assert
    await expect(discoverEnvironmentResources('a', { expectedVersion: 2, selection })).rejects.toBe(denied);
  });
  it('rejects discovery responses for a different registration', async () => {
    // Arrange
    vi.mocked(client.post).mockResolvedValue({ data: { systemId: 'a', discoveryToken: 'token',
      selection: { ...selection, registrationId: 'other' }, resources: [], expiresAt: '2026-10-01', discoveredAt: '2026-09-29' } });
    // Act / Assert
    await expect(discoverEnvironmentResources('a', { expectedVersion: 2, selection })).rejects.toThrow('selection');
  });
  it('uses provider authority transport and rejects another offering', async () => {
    // Arrange
    vi.mocked(packageRequest).mockResolvedValue({ offeringId: 'other', canManage: true, allocations: [] });
    // Act / Assert
    await expect(listProviderEnvironmentAllocations('offering')).rejects.toThrow('offering');
    expect(packageRequest).toHaveBeenCalledWith({ url: '/api/csp/offerings/offering/environment-allocations', signal: undefined });
  });
  it('sends provenance, consumer and real registration without inventing a subscription registration', async () => {
    // Arrange
    const body = { expectedOfferingVersion: 1, registrationId: 'registration', consumerTenantId: 'consumer',
      hostingScopeRevisionId: 'release', permittedResourceScopes: ['/subscriptions/sub/resourceGroups/rg'],
      startsAt: '2026-09-29T00:00:00Z', expiresAt: null,
      provenance: { source: 'FAST', externalId: 'reference', sourceRevision: '2',
        reconciliationState: 'Verified', evidenceReference: 'retained-document', recordedAt: '2026-09-29T00:00:00Z' } };
    vi.mocked(packageRequest).mockResolvedValue({ offeringId: 'offering', allocationId: 'allocation', version: 1 });
    // Act
    await recordProviderEnvironmentAllocation('offering', body, 'stable-key');
    // Assert
    expect(packageRequest).toHaveBeenCalledWith({ method: 'POST', url: '/api/csp/offerings/offering/environment-allocations',
      data: body, headers: { 'Idempotency-Key': 'stable-key' } });
  });
  it('uses explicit pending-scope review through existing preview and acknowledged commit routes', async () => {
    // Arrange
    const review = { expectedVersion: 2, expectedAttachmentVersion: 1, discoveryToken: 'current-discovery',
      resourceIds: apply.resourceIds, exclusions: [], sharedDependencyResourceIds: [],
      rationale: 'Review exact pending resources without changing the recorded boundary.', reviewPendingScope: true };
    const preview = { previewId: 'preview', systemId: 'a', allocationId: null, expectedVersion: 2,
      expiresAt: '2026-09-29T21:00:00Z', systems: [], requiresScopeReview: false, warnings: ['Boundary unchanged'] };
    vi.mocked(client.post).mockResolvedValueOnce({ data: preview }).mockResolvedValueOnce({
      data: { systemId: 'a', version: 3, permissions, attachments: [], legacyReferences: [] } });
    // Act
    await previewEnvironmentScope('a', 'attachment', review);
    const commit = { expectedVersion: 2, previewId: preview.previewId, rationale: review.rationale, acknowledgeImpact: true };
    await commitEnvironmentScope('a', 'attachment', commit, 'review-replay-key');
    // Assert
    expect(client.post).toHaveBeenNthCalledWith(1, '/systems/a/environments/attachment/scope-preview', review);
    expect(client.post).toHaveBeenNthCalledWith(2, '/systems/a/environments/attachment/scope-commit', commit,
      { headers: { 'Idempotency-Key': 'review-replay-key' } });
  });
});
