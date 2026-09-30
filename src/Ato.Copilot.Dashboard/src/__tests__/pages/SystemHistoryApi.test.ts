import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import { listSystemHistory } from '../../features/systems/systemHistoryApi';
vi.mock('../../api/client', () => ({ default: { get: vi.fn() } }));
const response = { systemId: 'a', source: 'DashboardActivity', items: [], totalCount: 0, page: 1, pageSize: 25 };
describe('System history API binding', () => {
  beforeEach(() => vi.clearAllMocks());
  it('binds query and cancellation to the selected system endpoint', async () => {
    // Arrange
    vi.mocked(apiClient.get).mockResolvedValue({ data: response });
    const signal = new AbortController().signal;
    const query = { page: 1, pageSize: 25, eventType: 'ProfileReviewed' };
    // Act
    expect(await listSystemHistory('a', query, signal)).toEqual(response);
    // Assert
    expect(apiClient.get).toHaveBeenCalledWith('/systems/a/history', { params: query, signal });
  });
  it.each([{ systemId: 'b' }, { source: 'PlatformAudit' }, { page: 2 }, { pageSize: 200 }, { totalCount: -1 }])(
    'rejects history whose source/system/page is not confirmed: %o', async invalid => {
      // Arrange
      vi.mocked(apiClient.get).mockResolvedValue({ data: { ...response, ...invalid } });
      // Act / Assert
      await expect(listSystemHistory('a', { page: 1, pageSize: 25 })).rejects.toThrow('does not match');
    },
  );
});
