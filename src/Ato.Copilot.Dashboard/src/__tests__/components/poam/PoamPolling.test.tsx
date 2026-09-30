import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { usePoamList } from '../../../hooks/usePoam';
import { listPoamItems } from '../../../api/poam';
import type { PaginatedPoamResponse } from '../../../types/poam';

vi.mock('../../../api/poam', () => ({ listPoamItems: vi.fn() }));

describe('POA&M scoped reads', () => {
  beforeEach(() => vi.clearAllMocks());

  it('preserves server envelope errors instead of displaying object coercion', async () => {
    // Arrange
    vi.mocked(listPoamItems).mockRejectedValue({ error: 'System access was revoked.', errorCode: 'FORBIDDEN' });
    // Act
    const { result } = renderHook(() => usePoamList('system-a'));
    // Assert
    await waitFor(() => expect(result.current.error?.message).toBe('System access was revoked.'));
  });

  it('aborts old scope and never publishes its late result', async () => {
    // Arrange
    let finishOld!: (value: PaginatedPoamResponse) => void;
    const oldResult = new Promise<PaginatedPoamResponse>(resolve => { finishOld = resolve; });
    const current = { items: [], totalCount: 0, page: 1, pageSize: 25, totalPages: 0 };
    vi.mocked(listPoamItems).mockImplementation(id => id === 'old' ? oldResult : Promise.resolve(current));
    const { result, rerender } = renderHook(({ id }) => usePoamList(id), { initialProps: { id: 'old' } });
    // Act
    rerender({ id: 'new' });
    await waitFor(() => expect(result.current.data).toEqual(current));
    await act(async () => finishOld({ ...current, totalCount: 99 }));
    // Assert
    expect(result.current.data?.totalCount).toBe(0);
    const firstCall = vi.mocked(listPoamItems).mock.calls[0];
    expect(firstCall?.[2]?.aborted).toBe(true);
  });
});
