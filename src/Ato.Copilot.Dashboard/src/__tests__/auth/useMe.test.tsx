import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { MeResponse } from '../../features/auth/types';

// ─── Hoisted mocks ──────────────────────────────────────────────────────
//
// We replace `axios` with a stub whose `get` is a vi.fn() so we can assert
// on call count + arguments. Tests reset the mock between cases.

const { getMock } = vi.hoisted(() => ({ getMock: vi.fn() }));

vi.mock('axios', () => ({
  default: { get: getMock },
  get: getMock,
}));

import { useMe } from '../../features/auth/useMe';

// ─── Fixtures ───────────────────────────────────────────────────────────

function makeMe(overrides: Partial<MeResponse> = {}): MeResponse {
  const home = {
    id: '11111111-1111-1111-1111-111111111111',
    displayName: 'Acme Home',
    status: 'Active' as const,
  };
  return {
    oid: 'oid-1',
    displayName: 'Jane Doe',
    persona: 'SystemOwner',
    homeTenant: home,
    effectiveTenant: home,
    isImpersonating: false,
    impersonation: null,
    pimRoles: [],
    isCspAdmin: false,
    isSocAnalyst: false,
    tenantMemberships: [home],
    ...overrides,
  };
}

function envelope(data: MeResponse) {
  return { data: { status: 'success', data } };
}

beforeEach(() => {
  getMock.mockReset();
});

// ─── Tests ──────────────────────────────────────────────────────────────
//
// These tests pin the EXISTING `useMe` contract returned by Phase 5/11:
//   { data: MeResponse | null, isLoading: boolean, error: Error | null,
//     refetch: () => void }
// They are NOT a request to migrate to React Query — the Phase 5 note
// in `useMe.ts` is explicit that a heavier React-Query rewrite happens
// later. Phase 12 (T138) only adds the `'ato:tenant-changed'` listener.

describe('useMe', () => {
  it('fetches GET /api/auth/me on mount', async () => {
    // Arrange
    getMock.mockResolvedValue(envelope(makeMe()));

    // Act
    renderHook(() => useMe());

    // Assert
    await waitFor(() => {
      expect(getMock).toHaveBeenCalledWith('/api/auth/me');
    });
  });

  it('returns { data, isLoading, error, refetch } shape', async () => {
    // Arrange
    getMock.mockResolvedValue(envelope(makeMe()));

    // Act
    const { result } = renderHook(() => useMe());

    // Assert — drain the in-flight effect first.
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current).toHaveProperty('data');
    expect(result.current).toHaveProperty('isLoading');
    expect(result.current).toHaveProperty('error');
    expect(typeof result.current.refetch).toBe('function');
  });

  it('exposes data=null + isLoading=true while the request is in flight', async () => {
    // Arrange — never-resolving promise so the loading state is observable.
    let resolveFetch: ((v: { data: { status: string; data: MeResponse } }) => void) | null =
      null;
    getMock.mockReturnValue(
      new Promise<{ data: { status: string; data: MeResponse } }>((r) => {
        resolveFetch = r;
      }),
    );

    // Act
    const { result } = renderHook(() => useMe());

    // Assert — synchronous initial state before the effect resolves.
    expect(result.current.isLoading).toBe(true);
    expect(result.current.data).toBeNull();
    expect(result.current.error).toBeNull();

    // Cleanup so the pending promise does not leak into other tests.
    await act(async () => {
      resolveFetch?.(envelope(makeMe()));
    });
  });

  it('populates data + clears loading after a successful fetch', async () => {
    // Arrange
    const me = makeMe({ displayName: 'CSP Admin', persona: 'CspAdmin' });
    getMock.mockResolvedValue(envelope(me));

    // Act
    const { result } = renderHook(() => useMe());

    // Assert
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.data).toEqual(me);
    expect(result.current.error).toBeNull();
  });

  it('surfaces the error and clears loading after a failed fetch', async () => {
    // Arrange
    const boom = new Error('boom');
    getMock.mockRejectedValue(boom);

    // Act
    const { result } = renderHook(() => useMe());

    // Assert
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.data).toBeNull();
    expect(result.current.error).toBeInstanceOf(Error);
    expect(result.current.error?.message).toBe('boom');
  });

  it('refetch() re-issues GET /api/auth/me', async () => {
    // Arrange
    getMock.mockResolvedValue(envelope(makeMe()));
    const { result } = renderHook(() => useMe());
    await waitFor(() => expect(getMock).toHaveBeenCalledTimes(1));

    // Act
    act(() => {
      result.current.refetch();
    });

    // Assert
    await waitFor(() => expect(getMock).toHaveBeenCalledTimes(2));
  });

  it('does NOT share cache across consumer remounts — each instance fetches on mount', async () => {
    // The existing hook is a plain useState + useEffect with no shared
    // module-level cache. This test pins that semantic so a future
    // refactor cannot silently introduce cross-component caching that
    // breaks consumers like the impersonation banner.

    // Arrange
    getMock.mockResolvedValue(envelope(makeMe()));

    // Act — first consumer mounts and unmounts cleanly.
    const first = renderHook(() => useMe());
    await waitFor(() => expect(getMock).toHaveBeenCalledTimes(1));
    first.unmount();

    // Act — a brand-new consumer mounts.
    renderHook(() => useMe());

    // Assert
    await waitFor(() => expect(getMock).toHaveBeenCalledTimes(2));
  });

  it("refetches when the window dispatches 'ato:tenant-changed'", async () => {
    // FR-030 — when the tenant picker fires `ato:tenant-changed`, every
    // live `useMe()` consumer must re-pull `/me` so the impersonation
    // banner, account menu, and tenant picker page stay coherent.

    // Arrange
    getMock.mockResolvedValue(envelope(makeMe()));
    renderHook(() => useMe());
    await waitFor(() => expect(getMock).toHaveBeenCalledTimes(1));

    // Act
    await act(async () => {
      window.dispatchEvent(
        new CustomEvent('ato:tenant-changed', { detail: { tenantId: 'x' } }),
      );
    });

    // Assert
    await waitFor(() => expect(getMock).toHaveBeenCalledTimes(2));
  });
});
