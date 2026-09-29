import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import { getEvidenceCatalog, getEvidenceCatalogDetail, linkCatalogEvidence, type EvidenceCatalogQuery } from '../../api/evidenceCatalog';

vi.mock('../../api/client', () => ({ default: { get: vi.fn(), post: vi.fn() } }));
const query: EvidenceCatalogQuery = { view: 'all', search: '', family: '', category: '', source: '',
  dateFrom: '', dateTo: '', sortBy: 'uploadedAt', sortOrder: 'desc', page: 1, pageSize: 25 };
const response = { systemId: 'system-a', items: [], totalCount: 0, availableCount: 0, page: 1, pageSize: 25,
  counts: { all: 0, system: 0, provider: 0, missingLinks: 0 }, sources: [
    { source: 'system', state: 'available', message: null }, { source: 'provider', state: 'available', message: null },
  ], permissions: { canUpload: false, uploadReason: 'Denied' } };
beforeEach(() => vi.clearAllMocks());
describe('Evidence catalog transport', () => {
  it('passes cancellation and filters and accepts explicit unknown counts', async () => {
    // Arrange
    const controller = new AbortController();
    vi.mocked(apiClient.get).mockResolvedValue({ data: { ...response, totalCount: null, counts: { ...response.counts, all: null, provider: null } } });
    // Act
    const data = await getEvidenceCatalog('system-a', query, controller.signal);
    // Assert
    expect(data.totalCount).toBeNull();
    expect(apiClient.get).toHaveBeenCalledWith('/systems/system-a/evidence-catalog', {
      params: { view: 'all', sortBy: 'uploadedAt', sortOrder: 'desc', page: 1, pageSize: 25 },
      signal: controller.signal,
    });
  });
  it.each([
    { ...response, systemId: 'system-b' },
    { ...response, counts: {} },
    { ...response, permissions: { canUpload: 'true' } },
    { ...response, items: [{ id: 'record-a' }] },
  ])('rejects mismatched or malformed data rather than rendering zero', async value => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: value });
    // Act / Assert
    await expect(getEvidenceCatalog('system-a', query)).rejects.toThrow('Unexpected or mismatched');
  });
  it('rejects detail without the requested identity', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: { systemId: 'system-b' } });
    // Act / Assert
    await expect(getEvidenceCatalogDetail('system-a', 'artifact:a')).rejects.toThrow('Unexpected or mismatched');
  });
  it('qualifies the exact link mutation and passes the retained hash', async () => {
    // Arrange
    vi.mocked(apiClient.post).mockResolvedValue({ data: {} });
    // Act
    await linkCatalogEvidence('system-a', 'artifact:a', 'AC-2', 'retained-hash');
    // Assert
    expect(apiClient.post).toHaveBeenCalledWith('/systems/system-a/evidence-catalog/artifact%3Aa/links',
      { controlId: 'AC-2', expectedHash: 'retained-hash' });
  });
});
