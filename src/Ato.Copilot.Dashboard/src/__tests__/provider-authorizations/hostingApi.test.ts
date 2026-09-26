import axios from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as api from '../../features/provider-authorizations/hostingApi';
import type { HostingAssignment, HostingScopeRevision } from '../../features/provider-authorizations/hostingTypes';
import type { AzureScope } from '../../features/provider-authorizations/types';

vi.mock('axios', () => ({ default: { request: vi.fn(), isAxiosError: vi.fn(() => false) } }));
const scope: AzureScope = {
  cloud: 'AzureUSGovernment', directoryTenantId: '11111111-1111-1111-1111-111111111111',
  subscriptionId: '22222222-2222-2222-2222-222222222222',
  resourceId: '/subscriptions/22222222-2222-2222-2222-222222222222/resourceGroups/synthetic',
};
const revision: HostingScopeRevision = {
  offeringId: 'offering/a', offeringRevision: 8, snapshot: { revisionId: 'hosting-a', revision: 2, snapshotHash: 'hash-a' },
  impactReviewId: 'impact-a', predecessorRevisionId: 'hosting-old', name: 'Technical scope',
  permittedScopes: [scope], exclusions: [], citations: [],
};
const assignment: HostingAssignment = {
  assignmentId: 'assignment-a', revision: 1, offeringId: revision.offeringId, systemId: 'system-a',
  hostingScope: revision.snapshot, assignedScopes: [scope], relationshipState: 'Undetermined',
};
const scopeInput = {
  expectedOfferingRevision: 7, predecessorRevisionId: 'hosting-old', name: 'Technical scope',
  permittedScopes: [scope], exclusions: [], citations: [],
};
const assignmentInput = {
  targetTenantId: '33333333-3333-3333-3333-333333333333', systemId: 'system-a',
  hostingScopeRevisionId: 'hosting-a', assignedScopes: [scope], references: [],
};
function reply(data: unknown) {
  vi.mocked(axios.request).mockResolvedValue({ status: 200, data: { status: 'success', data } });
}
beforeEach(() => vi.clearAllMocks());

describe('hosting transport grounded in IProviderHostingService', () => {
  it('reads the exact current hosting snapshot with cancellation and encoded identities', async () => {
    // Arrange
    reply(revision);
    const signal = new AbortController().signal;
    // Act
    const result = await api.getHostingScope('offering/a', 'hosting-a', signal);
    // Assert
    expect(result).toEqual(revision);
    expect(axios.request).toHaveBeenCalledWith(expect.objectContaining({
      url: '/api/csp/offerings/offering%2Fa/hosting-scope-revisions/hosting-a', signal,
    }));
  });
  it.each([null, { ...revision, offeringId: 'wrong' }, { ...revision, snapshot: { ...revision.snapshot, revisionId: 'wrong' } }])
    ('rejects an incomplete or mismatched exact hosting snapshot: %j', async data => {
      // Arrange
      reply(data);
      // Act
      const request = api.getHostingScope('offering/a', 'hosting-a');
      // Assert
      await expect(request).rejects.toThrow();
    });
  it.each([
    ['scopes', 'hosting-scope-revisions', api.listHostingScopes, revision],
    ['assignments', 'hosting-assignments', api.listHostingAssignments, assignment],
  ] as const)('reads paged %s through the authenticated envelope with cancellation', async (_name, route, read, item) => {
    // Arrange
    const data = { items: [item], page: 3, pageSize: 25, total: 61 };
    const signal = new AbortController().signal;
    reply(data);
    // Act
    const result = await read('offering/a', 3, signal);
    // Assert
    expect(result).toEqual(data);
    expect(axios.request).toHaveBeenCalledWith(expect.objectContaining({
      url: `/api/csp/offerings/offering%2Fa/${route}`, params: { page: 3, pageSize: 25 }, signal,
    }));
  });

  it('posts exact scope revision material and stable operation identity', async () => {
    // Arrange
    reply(revision);
    // Act
    const result = await api.createHostingScope('offering/a', scopeInput, 'scope-key');
    // Assert
    expect(result).toEqual(revision);
    expect(axios.request).toHaveBeenCalledWith({
      method: 'POST', url: '/api/csp/offerings/offering%2Fa/hosting-scope-revisions',
      data: scopeInput, headers: { 'Idempotency-Key': 'scope-key' },
    });
  });

  it('posts technical allocation without fabricated coverage, grants, or review flags', async () => {
    // Arrange
    reply(assignment);
    // Act
    const result = await api.createHostingAssignment('offering/a', assignmentInput, 'allocation-key');
    // Assert
    expect(result).toEqual(assignment);
    expect(axios.request).toHaveBeenCalledWith({
      method: 'POST', url: '/api/csp/offerings/offering%2Fa/hosting-assignments',
      data: assignmentInput, headers: { 'Idempotency-Key': 'allocation-key' },
    });
  });

  it.each([null, {}, { ...revision, snapshot: { revisionId: 'a' } },
    { ...revision, offeringId: 'different' }, { ...revision, predecessorRevisionId: 'wrong' },
    { ...revision, permittedScopes: undefined }])('rejects incomplete or mismatched scope receipts: %j', async data => {
    // Arrange
    reply(data);
    // Act
    const request = api.createHostingScope('offering/a', scopeInput, 'same-key');
    // Assert
    await expect(request).rejects.toThrow(/scope.*receipt/i);
  });

  it('rejects a receipt that silently drops explicit exclusions and citations', async () => {
    // Arrange
    reply(revision);
    const input = { ...scopeInput, exclusions: [{ scope, rationale: 'Outside allocation' }],
      citations: [{ packageId: 'package-a', artifactId: 'artifact-a', archivePath: 'source.pdf', locator: 'p2', quote: 'Limited scope.' }] };
    // Act
    const request = api.createHostingScope('offering/a', input, 'retain-key');
    // Assert
    await expect(request).rejects.toThrow(/scope.*receipt/i);
  });

  it('accepts normalized Azure scope casing and trailing slash without changing the submitted intent', async () => {
    // Arrange
    reply({ ...assignment, assignedScopes: [{ ...scope, resourceId: `${scope.resourceId.toUpperCase()}/` }] });
    // Act
    const result = await api.createHostingAssignment('offering/a', assignmentInput, 'normalization-key');
    // Assert
    expect(result.assignmentId).toBe('assignment-a');
    expect(axios.request).toHaveBeenCalledWith(expect.objectContaining({ data: assignmentInput }));
  });

  it('matches a trimmed customer system ID in the durable receipt without an endless uncertain retry', async () => {
    // Arrange
    reply(assignment);
    const input = { ...assignmentInput, systemId: ' system-a ' };
    // Act
    const result = await api.createHostingAssignment('offering/a', input, 'trimmed-target-key');
    // Assert
    expect(result.systemId).toBe('system-a');
    expect(axios.request).toHaveBeenCalledWith(expect.objectContaining({ data: input }));
  });

  it('accepts a retained assignment relationship state on reads without treating it as a new grant', async () => {
    // Arrange
    reply({ items: [{ ...assignment, relationshipState: 'SeparateBoundaryConsumer' }], page: 1, pageSize: 25, total: 1 });
    // Act
    const result = await api.listHostingAssignments('offering/a');
    // Assert
    expect(result.items[0]?.relationshipState).toBe('SeparateBoundaryConsumer');
  });

  it.each([null, {}, { ...assignment, systemId: 'wrong' }, { ...assignment, offeringId: 'different' },
    { ...assignment, hostingScope: { ...assignment.hostingScope, revisionId: 'wrong' } },
    { ...assignment, assignedScopes: [] }, { ...assignment, relationshipState: 'ExplicitlyCoveredByRecordedScope' }])(
    'rejects incomplete or mismatched assignment receipts: %j', async data => {
      // Arrange
      reply(data);
      // Act
      const request = api.createHostingAssignment('offering/a', assignmentInput, 'same-key');
      // Assert
      await expect(request).rejects.toThrow(/assignment.*receipt/i);
    });

  it.each([{}, { items: [], page: 1, pageSize: 0, total: 0 },
    { items: [revision], page: 1, pageSize: 25, total: 0 },
    { items: [{ ...revision, offeringId: 'other' }], page: 1, pageSize: 25, total: 1 }])(
    'does not turn malformed or cross-offering reads into empty state: %j', async data => {
      // Arrange
      reply(data);
      // Act
      const request = api.listHostingScopes('offering/a');
      // Assert
      await expect(request).rejects.toThrow(/hosting.*page/i);
    });

  it('preserves server denial guidance instead of returning a success-shaped fallback', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ status: 403, data: {
      status: 'error', error: { errorCode: 'PROVIDER_SCOPE_REQUIRED', message: 'Access denied.', suggestion: 'Select your provider workspace.' },
    } });
    // Act
    const request = api.listHostingAssignments('offering/a');
    // Assert
    await expect(request).rejects.toMatchObject({ status: 403, code: 'PROVIDER_SCOPE_REQUIRED', message: 'Access denied. Select your provider workspace.' });
  });
});
