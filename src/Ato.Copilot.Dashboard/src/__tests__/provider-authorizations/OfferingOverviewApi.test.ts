import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  createOfferingAssociation, getOfferingOverview, getBoundaryOverview, listDecisions,
  publishOfferingAssociation, reviewOfferingAssociation, updateOffering, withdrawOfferingAssociation,
} from '../../features/provider-authorizations/api';
import { offering } from './testData';
import { packageRequest } from '../../features/package-imports/request';
import { offeringOverview } from './overviewFixtures';

vi.mock('../../features/package-imports/request', () => ({ packageRequest: vi.fn() }));
beforeEach(() => vi.resetAllMocks());
describe('offering overview transport', () => {
  it.each([null, { ...offering, offeringId: 'foreign-offering' }, { ...offering, revision: 0 }, offering])(
    'rejects an identity write response that does not confirm its exact offering and next revision', async response => {
      // Arrange
      vi.mocked(packageRequest).mockResolvedValue(response);
      // Act / Assert
      await expect(updateOffering(offering.offeringId, { ...offering, expectedRevision: offering.revision }))
        .rejects.toThrow('persisted service identity');
    },
  );
  it('accepts the exact persisted identity response from a revision-fenced PATCH', async () => {
    // Arrange
    const updated = { ...offering, revision: offering.revision + 1 };
    vi.mocked(packageRequest).mockResolvedValue(updated);
    // Act / Assert
    expect(await updateOffering(offering.offeringId, { ...offering, expectedRevision: offering.revision })).toEqual(updated);
  });
  it.each([null, { offeringId: 'foreign-offering', offeringRevision: 1 }, { offeringId: 'offering-1', offeringRevision: 0 }])(
    'rejects unavailable or foreign capability/mission context', async response => {
      // Arrange
      vi.mocked(packageRequest).mockResolvedValue(response);
      // Act / Assert
      await expect(getBoundaryOverview('offering-1')).rejects.toThrow('requested offering');
    },
  );
  it('retains independent capability and mission pages and explicit workspace-safe offering identity', async () => {
    // Arrange
    const data = { offeringId: 'offering-1', offeringRevision: 3,
      capabilities: { items: [], page: 2, pageSize: 10, total: 11, published: 0, awaitingReview: 0 },
      missionSystems: { items: [], page: 3, pageSize: 10, total: 21 } };
    vi.mocked(packageRequest).mockResolvedValue(data);
    // Act / Assert
    expect(await getBoundaryOverview('offering-1', 2, 3)).toEqual(data);
    expect(packageRequest).toHaveBeenCalledWith({ url: '/api/csp/offerings/offering-1/boundary-overview',
      params: { capabilityPage: 2, missionPage: 3, pageSize: 10 }, signal: undefined });
  });
  it.each([{ revisions: [-1] }, { revisions: [1.2] }, { revisions: ['3'] }, { revisions: [3, 3] }, { revisions: null }])('rejects malformed canonical release revisions $revisions', async ({ revisions }) => {
    // Arrange
    const data = offeringOverview();
    vi.mocked(packageRequest).mockResolvedValue({ ...data, capabilities: { ...data.capabilities, publishedReleaseRevisions: revisions } });
    // Act / Assert
    await expect(getOfferingOverview(data.offeringId)).rejects.toThrow('canonical publication revisions');
  });
  it.each([-1, 1.2, '4'])('rejects malformed global retained totals %s', async count => {
    // Arrange
    const data = offeringOverview();
    vi.mocked(packageRequest).mockResolvedValue({ ...data, openFindingCount: count });
    // Act / Assert
    await expect(getOfferingOverview(data.offeringId)).rejects.toThrow('retained source and finding totals');
    vi.mocked(packageRequest).mockResolvedValue({ ...data, packages: { ...data.packages, sourceDocumentCount: count } });
    await expect(getOfferingOverview(data.offeringId)).rejects.toThrow('retained source and finding totals');
  });
  it.each([null, 0, 7])('preserves customer action count %s without converting unknown to zero', async count => {
    // Arrange
    const data = { ...offeringOverview(), customerActionCount: count };
    vi.mocked(packageRequest).mockResolvedValue(data);
    // Act
    const result = await getOfferingOverview(data.offeringId);
    // Assert
    expect(result.customerActionCount).toBe(count);
  });
  it.each([-1, 1.5, '7'])('rejects an invalid customer action count %s', async count => {
    // Arrange
    const data = { ...offeringOverview(), customerActionCount: count };
    vi.mocked(packageRequest).mockResolvedValue(data);
    // Act / Assert
    await expect(getOfferingOverview(data.offeringId)).rejects.toThrow('customer-action count');
  });
  it('uses independent bounded pages and forwards cancellation', async () => {
    // Arrange
    const signal = new AbortController().signal;
    const data = offeringOverview();
    vi.mocked(packageRequest).mockResolvedValue(data);
    // Act
    expect(await getOfferingOverview(data.offeringId, 2, 3, signal)).toEqual(data);
    // Assert
    expect(packageRequest).toHaveBeenCalledExactlyOnceWith({
      url: `/api/csp/offerings/${data.offeringId}/overview`, params: { authorizationPage: 2, packagePage: 3, pageSize: 10 }, signal,
    });
  });
  it.each([null, { ...offeringOverview(), offeringId: 'another-offering' }, { ...offeringOverview(), offeringRevision: 0 }])(
    'rejects unavailable or mismatched overview identity', async response => {
      // Arrange
      vi.mocked(packageRequest).mockResolvedValue(response);
      // Act / Assert
      await expect(getOfferingOverview('offering-1')).rejects.toThrow(/requested offering and current revision/);
    },
  );
  it('filters provider decisions only when explicitly requested, preserving mixed-kind lookup callers', async () => {
    // Arrange
    vi.mocked(packageRequest).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25 });
    // Act
    await listDecisions('offering-1', 1, undefined, 'ProviderDecision');
    await listDecisions('offering-1');
    // Assert
    expect(packageRequest).toHaveBeenNthCalledWith(1, expect.objectContaining({
      params: { page: 1, pageSize: 25, recordKind: 'ProviderDecision' },
    }));
    expect(packageRequest).toHaveBeenNthCalledWith(2, expect.objectContaining({ params: { page: 1, pageSize: 25 } }));
  });
  it('uses explicit revision-fenced association lifecycle requests', async () => {
    // Arrange
    vi.mocked(packageRequest).mockResolvedValue({});
    const input = {
      componentId: 'component-1', capabilityId: 'capability-1', capabilityReleaseId: 'release-1',
      offeringRevision: 4, sourceProvenance: [], providerCoverage: ['AC-2'], customerObligations: ['AC-2(1)'],
      contributionRole: 'Primary' as const,
    };

    // Act
    await createOfferingAssociation('offering-1', input);
    await reviewOfferingAssociation('offering-1', 'association-1', 2, true, 'Reviewed source and duties.');
    await publishOfferingAssociation('offering-1', 'association-1', 3);
    await withdrawOfferingAssociation('offering-1', 'association-1', 4, 'Release retired.');

    // Assert
    expect(packageRequest).toHaveBeenNthCalledWith(1, {
      method: 'POST', url: '/api/csp/offerings/offering-1/associations', data: input,
    });
    expect(packageRequest).toHaveBeenNthCalledWith(2, {
      method: 'POST', url: '/api/csp/offerings/offering-1/associations/association-1/review',
      data: { expectedRevision: 2, approve: true, rationale: 'Reviewed source and duties.' },
    });
    expect(packageRequest).toHaveBeenNthCalledWith(3, {
      method: 'POST', url: '/api/csp/offerings/offering-1/associations/association-1/publish',
      params: { expectedRevision: 3 },
    });
    expect(packageRequest).toHaveBeenNthCalledWith(4, {
      method: 'POST', url: '/api/csp/offerings/offering-1/associations/association-1/withdraw',
      data: { expectedRevision: 4, rationale: 'Release retired.' },
    });
  });
});
