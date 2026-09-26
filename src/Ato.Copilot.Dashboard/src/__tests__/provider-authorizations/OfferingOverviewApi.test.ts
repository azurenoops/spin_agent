import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getOfferingOverview, listDecisions } from '../../features/provider-authorizations/api';
import { packageRequest } from '../../features/package-imports/request';
import { offeringOverview } from './overviewFixtures';

vi.mock('../../features/package-imports/request', () => ({ packageRequest: vi.fn() }));
beforeEach(() => vi.resetAllMocks());
describe('offering overview transport', () => {
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
});
