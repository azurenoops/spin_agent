import { act, cleanup, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AccountInfo } from '@azure/msal-browser';
import type { MeResponse } from '../../features/auth/types';
import { useNotifications } from '../../hooks/useNotifications';

const mocks = vi.hoisted(() => ({
  get: vi.fn(), post: vi.fn(), useMe: vi.fn(), getActiveAccount: vi.fn(),
  getAllAccounts: vi.fn(), acquireTokenSilent: vi.fn(),
  withUrl: vi.fn(), start: vi.fn(), stop: vi.fn(), invoke: vi.fn(), on: vi.fn(),
  onclose: vi.fn(), onreconnecting: vi.fn(), onreconnected: vi.fn(),
}));
vi.mock('../../api/client', () => ({ default: { get: mocks.get, post: mocks.post } }));
vi.mock('../../features/auth/useMe', () => ({ useMe: mocks.useMe }));
vi.mock('../../features/auth/msalInstance', () => ({
  getMsalInstance: () => mocks, DEFAULT_API_SCOPES: ['api/read'],
}));
vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl(...args: unknown[]) { mocks.withUrl(...args); return this; }
    withAutomaticReconnect() { return this; }
    build() { return mocks; }
  },
}));

const account: AccountInfo = {
  homeAccountId: 'home', environment: 'login.microsoftonline.com',
  tenantId: 'directory', localAccountId: 'actor', username: 'synthetic',
};
const identity: MeResponse = {
  oid: 'actor', directoryTenantId: 'directory', displayName: 'Synthetic user', persona: 'ISSO',
  homeTenant: null, effectiveTenant: null, isImpersonating: false, impersonation: null,
  pimRoles: [], isCspAdmin: false, isSocAnalyst: false, tenantMemberships: [],
  workspace: {
    kind: 'organization', tenantId: 'organization-a', mode: 'ordinary', personId: 'person',
    displayName: 'Organization A', roles: ['ISSO'],
    permissions: { canAccessCsp: false, canManageMemberships: false, canManageOrganization: false },
  },
};
const capabilities = {
  recipientId: 'actor',
  rest: { available: true, reasonCode: null },
  realtime: {
    available: false, authentication: 'bearer', cookieSessionSupported: false,
    reasonCode: 'REALTIME_BEARER_REQUIRED', hubPaths: ['/hubs/notifications'],
  },
  fallback: { transport: 'rest-polling', pollIntervalSeconds: 30 },
};
const item = {
  id: 'one', alertId: 'alert', channel: 'InApp', subject: 'Synthetic notice', body: null,
  isRead: false, readAt: null, sentAt: '2026-01-01T00:00:00Z', alertTitle: null, alertSeverity: null,
};

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(r => { resolve = r; });
  return { promise, resolve };
}
function authenticated(data: MeResponse | null = identity) {
  mocks.useMe.mockReturnValue({ data, isLoading: false, error: null, refetch: vi.fn() });
}
function realtimeReady() {
  mocks.getActiveAccount.mockReturnValue(account);
  mocks.get.mockImplementation(async (url: string) => ({
    data: url.endsWith('/capabilities') ? { ...capabilities, realtime: { ...capabilities.realtime, available: true, reasonCode: null } }
      : url.endsWith('/summary') ? { unreadCount: 1, totalCount: 1 } : { items: [item] },
  }));
}

describe('notification session and workspace transport', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    authenticated();
    mocks.getActiveAccount.mockReturnValue(null);
    mocks.getAllAccounts.mockReturnValue([]);
    mocks.start.mockResolvedValue(undefined);
    mocks.stop.mockResolvedValue(undefined);
    mocks.invoke.mockResolvedValue(undefined);
    mocks.post.mockResolvedValue({ data: { updated: 1 } });
    mocks.get.mockImplementation(async (url: string) => ({
      data: url.endsWith('/capabilities') ? structuredClone(capabilities)
        : url.endsWith('/summary') ? { unreadCount: 1, totalCount: 1 } : { items: [item] },
    }));
    window.history.replaceState({}, '', '/workspaces/organizations/11111111-1111-1111-1111-111111111111');
  });
  afterEach(() => { cleanup(); vi.useRealTimers(); });

  it('loads cookie-session notifications without an MSAL account or a caller-selected user id', async () => {
    // Arrange
    const { result } = renderHook(() => useNotifications());
    // Act
    await waitFor(() => expect(result.current.notifications).toEqual([item]));
    // Assert
    expect(mocks.get.mock.calls[0]?.[0]).toBe('/notifications/capabilities');
    expect(mocks.get).toHaveBeenCalledWith('/notifications', expect.objectContaining({
      params: { limit: 50 }, signal: expect.any(AbortSignal),
    }));
    expect(result.current.transportMessage).toMatch(/real-time.*unavailable/i);
    expect(mocks.start).not.toHaveBeenCalled();
  });

  it('polls the authorized REST fallback at exactly the declared interval', async () => {
    // Arrange
    vi.useFakeTimers();
    const { result, unmount } = renderHook(() => useNotifications());
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(result.current.notifications).toHaveLength(1);
    const before = mocks.get.mock.calls.length;
    // Act
    await act(async () => { await vi.advanceTimersByTimeAsync(29_999); });
    expect(mocks.get).toHaveBeenCalledTimes(before);
    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    // Assert
    expect(mocks.get.mock.calls.filter(([url]) => url === '/notifications')).toHaveLength(2);
    unmount();
    const after = mocks.get.mock.calls.length;
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(mocks.get).toHaveBeenCalledTimes(after);
  });

  it('does not fetch data or connect when REST is unavailable for the selected context', async () => {
    // Arrange
    mocks.get.mockResolvedValue({ data: {
      ...capabilities, rest: { available: false, reasonCode: 'ORGANIZATION_WORKSPACE_REQUIRED' },
      fallback: { transport: 'none', pollIntervalSeconds: null },
    } });
    // Act
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(result.current.loading).toBe(false));
    // Assert
    expect(mocks.get).toHaveBeenCalledTimes(1);
    expect(result.current.transportMessage).toMatch(/organization/i);
    expect(mocks.start).not.toHaveBeenCalled();
  });

  it('does nothing without a resolved authenticated server identity', () => {
    // Arrange
    authenticated(null);
    // Act
    const { result } = renderHook(() => useNotifications('forged'));
    // Assert
    expect(mocks.get).not.toHaveBeenCalled();
    expect(result.current.notifications).toEqual([]);
  });

  it('rejects a capability recipient mismatch without fetching private data', async () => {
    // Arrange
    mocks.get.mockResolvedValue({ data: { ...capabilities, recipientId: 'different-actor' } });
    // Act
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(result.current.loading).toBe(false));
    // Assert
    expect(result.current.error).toMatch(/identity/i);
    expect(mocks.get).toHaveBeenCalledTimes(1);
  });

  it('clears old data and ignores a late response after a workspace switch', async () => {
    // Arrange
    const pending = deferred<{ data: { items: typeof item[] } }>();
    mocks.get.mockImplementation(async (url: string) => url === '/notifications' ? pending.promise : ({
      data: url.endsWith('/capabilities') ? capabilities : { unreadCount: 1, totalCount: 1 },
    }));
    const { result, rerender } = renderHook(() => useNotifications());
    await waitFor(() => expect(mocks.get).toHaveBeenCalledWith('/notifications', expect.anything()));
    const signal = mocks.get.mock.calls.find(([url]) => url === '/notifications')?.[1].signal as AbortSignal;
    // Act
    authenticated({ ...identity, workspace: { ...identity.workspace!, tenantId: 'organization-b' } });
    mocks.get.mockImplementation(async (url: string) => ({
      data: url.endsWith('/capabilities') ? capabilities
        : url.endsWith('/summary') ? { unreadCount: 0, totalCount: 0 } : { items: [] },
    }));
    rerender();
    expect(result.current.notifications).toEqual([]);
    await act(async () => { pending.resolve({ data: { items: [item] } }); });
    // Assert
    expect(signal.aborted).toBe(true);
    expect(result.current.notifications).toEqual([]);
    expect(result.current.unreadCount).toBe(0);
  });

  it('surfaces a REST failure instead of showing a successful empty result', async () => {
    // Arrange
    mocks.get.mockRejectedValue({ status: 'error', error: { message: 'Membership revoked' } });
    // Act
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(result.current.loading).toBe(false));
    // Assert
    expect(result.current.error).toBe('Membership revoked');
  });

  it('marks cookie-session notifications read using the server-bound actor', async () => {
    // Arrange
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(result.current.notifications).toHaveLength(1));
    // Act
    await act(async () => { await result.current.markAllAsRead(); });
    // Assert
    expect(mocks.post).toHaveBeenCalledWith('/notifications/mark-all-read', null,
      expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });

  it('surfaces a failed mutation without optimistically changing read status', async () => {
    // Arrange
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(result.current.notifications).toHaveLength(1));
    mocks.post.mockRejectedValue(new Error('Read update denied'));
    // Act
    await act(async () => { await result.current.markAsRead(['one']); });
    // Assert
    expect(result.current.notifications[0]?.isRead).toBe(false);
    expect(result.current.error).toBe('Read update denied');
  });

  it('connects only after bearer readiness and pins the token factory to that account', async () => {
    // Arrange
    mocks.getActiveAccount.mockReturnValue(account);
    mocks.acquireTokenSilent.mockResolvedValue({ accessToken: 'synthetic-token' });
    mocks.get.mockImplementation(async (url: string) => ({
      data: url.endsWith('/capabilities') ? { ...capabilities, realtime: { ...capabilities.realtime, available: true, reasonCode: null } }
        : url.endsWith('/summary') ? { unreadCount: 1, totalCount: 1 } : { items: [item] },
    }));
    // Act
    renderHook(() => useNotifications());
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalledWith('RegisterUser', 'actor'));
    const factory = mocks.withUrl.mock.calls[0]?.[1].accessTokenFactory as () => Promise<string>;
    // Assert
    expect(mocks.withUrl.mock.calls[0]?.[0]).toContain('workspaceMode=ordinary');
    await expect(factory()).resolves.toBe('synthetic-token');
    mocks.getActiveAccount.mockReturnValue({ ...account, localAccountId: 'other' });
    await expect(factory()).rejects.toThrow(/identity/i);
  });

  it('rejects a token acquired after the account changes', async () => {
    // Arrange
    realtimeReady();
    const token = deferred<{ accessToken: string }>();
    mocks.acquireTokenSilent.mockReturnValue(token.promise);
    renderHook(() => useNotifications());
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalled());
    const factory = mocks.withUrl.mock.calls[0]?.[1].accessTokenFactory as () => Promise<string>;
    // Act
    const result = factory();
    mocks.getActiveAccount.mockReturnValue({ ...account, tenantId: 'another-directory' });
    token.resolve({ accessToken: 'stale-token' });
    // Assert
    await expect(result).rejects.toThrow(/identity changed during token acquisition/i);
  });

  it('surfaces start failure while retaining the authorized polling fallback', async () => {
    // Arrange
    vi.useFakeTimers();
    realtimeReady();
    mocks.start.mockRejectedValue(new Error('Connection failed'));
    const { result } = renderHook(() => useNotifications());
    // Act
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    // Assert
    expect(result.current.error).toBe('Connection failed');
    expect(result.current.transportMessage).toContain('REST polling');
    expect(result.current.notifications).toHaveLength(1);
    expect(mocks.stop).toHaveBeenCalledTimes(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(30_000); });
    expect(mocks.get.mock.calls.filter(([url]) => url === '/notifications')).toHaveLength(2);
  });

  it('waits for pending startup before cleanup and ignores late events after unmount', async () => {
    // Arrange
    realtimeReady();
    const start = deferred<void>();
    mocks.start.mockReturnValue(start.promise);
    const { unmount } = renderHook(() => useNotifications());
    await waitFor(() => expect(mocks.start).toHaveBeenCalled());
    const event = mocks.on.mock.calls.find(([name]) => name === 'NewNotification')?.[1] as () => void;
    const before = mocks.get.mock.calls.length;
    // Act
    unmount();
    expect(mocks.stop).not.toHaveBeenCalled();
    await act(async () => { start.resolve(); event(); });
    // Assert
    expect(mocks.stop).toHaveBeenCalledTimes(1);
    expect(mocks.invoke).not.toHaveBeenCalled();
    expect(mocks.get).toHaveBeenCalledTimes(before);
  });

  it('revalidates readiness and registration on reconnect', async () => {
    // Arrange
    realtimeReady();
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalledTimes(1));
    const reconnecting = mocks.onreconnecting.mock.calls[0]?.[0] as () => void;
    const reconnected = mocks.onreconnected.mock.calls[0]?.[0] as () => Promise<void>;
    // Act
    act(reconnecting);
    expect(result.current.transportMessage).toMatch(/reconnecting/i);
    await act(reconnected);
    // Assert
    expect(mocks.invoke).toHaveBeenCalledTimes(2);
    expect(mocks.get.mock.calls.filter(([url]) => url === '/notifications/capabilities')).toHaveLength(2);
    expect(result.current.transportMessage).toBeNull();
  });

  it('stops the old hub and clears data when membership is revoked', async () => {
    // Arrange
    realtimeReady();
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalled());
    const oldClose = mocks.onclose.mock.calls[0]?.[0] as () => void;
    mocks.get.mockRejectedValue(new Error('Membership revoked'));
    // Act
    await act(async () => { await result.current.refresh(); oldClose(); });
    // Assert
    expect(result.current.error).toBe('Membership revoked');
    expect(result.current.notifications).toEqual([]);
    expect(result.current.transportMessage).toBeNull();
    expect(mocks.stop).toHaveBeenCalledTimes(1);
  });

  it.each([
    { ...capabilities, rest: null },
    { ...capabilities, fallback: { transport: 'rest-polling', pollIntervalSeconds: 0 } },
    { ...capabilities, fallback: { transport: 'rest-polling', pollIntervalSeconds: -1 } },
  ])('rejects malformed capability or polling payloads', async (data) => {
    // Arrange
    mocks.get.mockResolvedValue({ data });
    // Act
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(result.current.loading).toBe(false));
    // Assert
    expect(result.current.error).toMatch(/unexpected/i);
    expect(mocks.get).toHaveBeenCalledTimes(1);
    expect(mocks.start).not.toHaveBeenCalled();
  });

  it('refetches after a mutation even if a pre-mutation poll was still pending', async () => {
    // Arrange
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(result.current.notifications).toHaveLength(1));
    const pending = deferred<{ data: { items: typeof item[] } }>();
    mocks.get.mockImplementation(async (url: string) => url === '/notifications' ? pending.promise : ({
      data: url.endsWith('/capabilities') ? capabilities : { unreadCount: 1, totalCount: 1 },
    }));
    let poll!: Promise<void>;
    act(() => { poll = result.current.refresh(); });
    await waitFor(() => expect(mocks.get.mock.calls.filter(([url]) => url === '/notifications')).toHaveLength(2));
    let mutation!: Promise<void>;
    act(() => { mutation = result.current.markAsRead(['one']); });
    mocks.get.mockImplementation(async (url: string) => ({
      data: url.endsWith('/capabilities') ? capabilities
        : url.endsWith('/summary') ? { unreadCount: 0, totalCount: 1 }
          : { items: [{ ...item, isRead: true }] },
    }));
    // Act
    await act(async () => { pending.resolve({ data: { items: [item] } }); await poll; await mutation; });
    // Assert
    expect(result.current.notifications[0]?.isRead).toBe(true);
    expect(result.current.unreadCount).toBe(0);
  });

  it('recovers after a failed request only on a fresh authorized capability response', async () => {
    // Arrange
    mocks.get.mockRejectedValueOnce(new Error('Temporary outage'));
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(result.current.error).toBe('Temporary outage'));
    // Act
    await act(async () => { await result.current.refresh(); });
    // Assert
    expect(result.current.error).toBeNull();
    expect(result.current.notifications).toEqual([item]);
  });

  it('rejects a caller-selected actor even when the session is authenticated', async () => {
    // Arrange
    const { result } = renderHook(() => useNotifications('forged'));
    // Act
    await waitFor(() => expect(result.current.loading).toBe(false));
    await act(async () => { await result.current.refresh(); });
    // Assert
    expect(result.current.error).toMatch(/identity/i);
    expect(mocks.get).not.toHaveBeenCalled();
  });

  it('rejects mark-read before capability authorization and makes no write', async () => {
    // Arrange
    mocks.get.mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useNotifications());
    // Act
    await act(async () => { await result.current.markAsRead(['one']); });
    // Assert
    expect(result.current.error).toMatch(/not been authorized/i);
    expect(mocks.post).not.toHaveBeenCalled();
  });

  it('surfaces unknown errors and logs failed socket cleanup', async () => {
    // Arrange
    realtimeReady();
    const { result, unmount } = renderHook(() => useNotifications());
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalled());
    mocks.post.mockRejectedValue('unexpected failure');
    mocks.stop.mockRejectedValue(new Error('Socket cleanup failed'));
    const log = vi.spyOn(console, 'error').mockImplementation(() => {});
    // Act
    await act(async () => { await result.current.markAllAsRead(); });
    expect(result.current.error).toMatch(/unable to load or update/i);
    await act(async () => { unmount(); });
    // Assert
    expect(log).toHaveBeenCalledWith(expect.stringContaining('Unable to stop'), expect.any(Error));
    log.mockRestore();
  });

  it('reports reconnect registration failure and stops the unauthorized connection', async () => {
    // Arrange
    realtimeReady();
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalled());
    mocks.invoke.mockRejectedValue(new Error('Registration denied'));
    // Act
    await act(async () => { await mocks.onreconnected.mock.calls[0]?.[0](); });
    expect(result.current.error).toBe('Registration denied');
    act(() => mocks.onclose.mock.calls[0]?.[0]());
    // Assert
    expect(result.current.transportMessage).toMatch(/unavailable/i);
    expect(mocks.stop).toHaveBeenCalledOnce();
  });

  it('does not poll when the server declares no fallback', async () => {
    // Arrange
    vi.useFakeTimers();
    mocks.get.mockImplementation(async (url: string) => ({
      data: url.endsWith('/capabilities') ? { ...capabilities, fallback: { transport: 'none', pollIntervalSeconds: null } }
        : url.endsWith('/summary') ? { unreadCount: 1, totalCount: 1 } : { items: [item] },
    }));
    const { result } = renderHook(() => useNotifications());
    // Act
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    // Assert
    expect(result.current.notifications).toEqual([item]);
    expect(mocks.get.mock.calls.filter(([url]) => url === '/notifications')).toHaveLength(1);
    expect(result.current.transportMessage).not.toMatch(/every.*seconds/i);
  });

  it('displays unbound-recipient unavailability without a CSP-specific message', async () => {
    // Arrange
    mocks.get.mockResolvedValue({ data: {
      ...capabilities, rest: { available: false, reasonCode: 'NOTIFICATION_RECIPIENT_AMBIGUOUS' },
      fallback: { transport: 'none', pollIntervalSeconds: null },
    } });
    // Act
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(result.current.loading).toBe(false));
    // Assert
    expect(result.current.transportMessage).toMatch(/unavailable for this workspace identity/i);
  });

  it('ignores capabilities completing after unmount', async () => {
    // Arrange
    const pending = deferred<{ data: typeof capabilities }>();
    mocks.get.mockReturnValue(pending.promise);
    const { unmount } = renderHook(() => useNotifications());
    await waitFor(() => expect(mocks.get).toHaveBeenCalled());
    // Act
    unmount();
    await act(async () => { pending.resolve({ data: capabilities }); });
    // Assert
    expect(mocks.get).toHaveBeenCalledTimes(1);
    expect(mocks.start).not.toHaveBeenCalled();
  });

  it('suspends fallback polling while realtime is healthy and resumes on reconnect', async () => {
    // Arrange
    vi.useFakeTimers();
    realtimeReady();
    renderHook(() => useNotifications());
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    expect(mocks.invoke).toHaveBeenCalledOnce();
    // Act
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    // Assert
    expect(mocks.get.mock.calls.filter(([url]) => url === '/notifications')).toHaveLength(1);
    act(() => mocks.onreconnecting.mock.calls[0]?.[0]());
    await act(async () => { await vi.advanceTimersByTimeAsync(30_000); });
    expect(mocks.get.mock.calls.filter(([url]) => url === '/notifications')).toHaveLength(2);
    await act(async () => { await mocks.onreconnected.mock.calls[0]?.[0](); });
    const calls = mocks.get.mock.calls.length;
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(mocks.get).toHaveBeenCalledTimes(calls);
  });

  it('resumes polling after a healthy connection closes', async () => {
    // Arrange
    vi.useFakeTimers();
    realtimeReady();
    const { result } = renderHook(() => useNotifications());
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    // Act
    act(() => mocks.onclose.mock.calls[0]?.[0]());
    // Assert
    expect(result.current.transportMessage).toMatch(/disconnected/i);
    await act(async () => { await vi.advanceTimersByTimeAsync(30_000); });
    expect(mocks.get.mock.calls.filter(([url]) => url === '/notifications')).toHaveLength(2);
  });

  it.each([
    { ...capabilities.realtime, authentication: 'cookie' },
    { ...capabilities.realtime, hubPaths: null },
    { ...capabilities.realtime, cookieSessionSupported: undefined },
  ])('rejects malformed transport metadata through the shared contract', async (realtime) => {
    // Arrange
    mocks.get.mockResolvedValue({ data: { ...capabilities, realtime } });
    // Act
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(result.current.loading).toBe(false));
    // Assert
    expect(result.current.error).toMatch(/unexpected notification capabilities/i);
    expect(mocks.get).toHaveBeenCalledTimes(1);
    expect(mocks.start).not.toHaveBeenCalled();
  });

  it('uses authorized REST fallback when the personal notification hub is not advertised', async () => {
    // Arrange
    mocks.getActiveAccount.mockReturnValue(account);
    mocks.get.mockImplementation(async (url: string) => ({
      data: url.endsWith('/capabilities') ? {
        ...capabilities, realtime: { ...capabilities.realtime, available: true, hubPaths: ['/hubs/package'] },
      } : url.endsWith('/summary') ? { unreadCount: 1, totalCount: 1 } : { items: [item] },
    }));
    // Act
    const { result } = renderHook(() => useNotifications());
    await waitFor(() => expect(result.current.notifications).toEqual([item]));
    // Assert
    expect(mocks.start).not.toHaveBeenCalled();
    expect(result.current.transportMessage).toMatch(/not advertised/i);
  });
});
