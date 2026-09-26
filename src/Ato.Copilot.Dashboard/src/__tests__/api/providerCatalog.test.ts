import { beforeEach, describe, expect, it, vi } from 'vitest';
import axios from 'axios';
import { createProviderCapability, getProviderCapability, getProviderCatalogOverview, getWorkingRevision, listProviderCatalog } from '../../features/workspace-operations/api';

vi.mock('axios', async importOriginal => {
  const actual = await importOriginal<typeof import('axios')>();
  return { ...actual, default: { ...actual.default, request: vi.fn() } };
});
beforeEach(() => vi.resetAllMocks());

describe('provider catalog mock API contracts', () => {
  it.each(['code', 'errorCode'])('preserves the working-revision error discriminator from %s', async key => {
    // Arrange
    vi.mocked(axios.request).mockRejectedValue({
      isAxiosError: true, message: 'Request failed',
      response: { status: 404, data: { error: { [key]: 'WORKING_REVISION_NOT_FOUND', message: 'Working revision was not found.' } } },
    });
    // Act
    const result = getWorkingRevision('new-capability');
    // Assert
    await expect(result).rejects.toMatchObject({ status: 404, code: 'WORKING_REVISION_NOT_FOUND' });
  });

  it('fetches a capability directly instead of scanning catalog pages', async () => {
    // Arrange
    const controller = new AbortController();
    vi.mocked(axios.request).mockResolvedValue({ data: { data: { capability: { capabilityId: 'cap/id' } } } });
    // Act
    await getProviderCapability('cap/id', controller.signal);
    // Assert
    expect(axios.request).toHaveBeenCalledExactlyOnceWith({
      method: 'GET', url: '/api/csp/catalog/capabilities/cap%2Fid', signal: controller.signal,
    });
  });

  it('loads provider source overview without organization or system scope parameters', async () => {
    // Arrange
    vi.mocked(axios.request).mockResolvedValue({ data: { data: { providerName: 'Provider' } } });
    // Act
    await getProviderCatalogOverview();
    // Assert
    expect(axios.request).toHaveBeenCalledExactlyOnceWith({ method: 'GET', url: '/api/csp/catalog/overview', params: { page: 1, pageSize: 25 }, signal: undefined });
  });

  it('preserves component filtering and server paging', async () => {
    // Arrange
    const query = { grouping: 'capability' as const, componentId: 'component-id', page: 3, pageSize: 25 };
    vi.mocked(axios.request).mockResolvedValue({ data: { data: { items: [], total: 70, page: 3, pageSize: 25 } } });
    // Act
    await listProviderCatalog(query);
    // Assert
    expect(axios.request).toHaveBeenCalledWith({ method: 'GET', url: '/api/csp/catalog', params: query, signal: undefined });
  });

  it('creates via the existing provider endpoint without silently marking coverage approved', async () => {
    // Arrange
    const body = { name: 'Monitoring', description: 'Provider monitoring', mappedNistControlIds: [], markMappedImmediately: false };
    vi.mocked(axios.request).mockResolvedValue({ data: { status: 'success', data: { id: 'created-id', status: 'NeedsReview' } } });
    // Act
    const created = await createProviderCapability('component-id', body);
    // Assert
    expect(created.status).toBe('NeedsReview');
    expect(axios.request).toHaveBeenCalledWith({ method: 'POST', url: '/api/csp/inherited-components/component-id/capabilities', data: body });
  });
});
