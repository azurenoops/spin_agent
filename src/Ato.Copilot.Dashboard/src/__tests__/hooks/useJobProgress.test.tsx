import { act, cleanup, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AccountInfo } from '@azure/msal-browser';
import type { UseMeResult } from '../../features/auth/useMe';
import type { NotificationCapabilities } from '../../features/notifications/capabilities';
import { useJobProgress, useProgressSession } from '../../hooks/useJobProgress';
import { workspaceSession } from '../helpers/domainPermissions';

interface Status { id: string; status: 'Processing' | 'Completed' }
const mocks = vi.hoisted(() => ({
  me: { data: null, isLoading: false, error: null, refetch: () => {} } as UseMeResult,
  capabilities: vi.fn<(signal?: AbortSignal) => Promise<NotificationCapabilities>>(),
  poll: vi.fn<(signal: AbortSignal) => Promise<Status>>(),
  onStatus: vi.fn(),
  accounts: [] as AccountInfo[],
  addEventCallback: vi.fn<(callback: () => void) => string>(),
  removeEventCallback: vi.fn(),
  acquireBearer: vi.fn<() => Promise<string>>(),
  withUrl: vi.fn(), start: vi.fn<() => Promise<void>>(), stop: vi.fn<() => Promise<void>>(),
  invoke: vi.fn<(...args: unknown[]) => Promise<void>>(),
  onreconnecting: vi.fn<(callback: () => void) => void>(),
  onreconnected: vi.fn<(callback: () => Promise<void>) => void>(),
  onclose: vi.fn<(callback: () => void) => void>(),
  handlers: new Map<string, (payload: unknown) => void>(),
}));
vi.mock('../../features/auth/useMe', () => ({ useMe: () => mocks.me }));
vi.mock('../../features/auth/msalInstance', () => ({
  acquireBearer: mocks.acquireBearer,
  getMsalInstance: () => ({
    getAllAccounts: () => mocks.accounts,
    getActiveAccount: () => mocks.accounts[0] ?? null,
    addEventCallback: mocks.addEventCallback,
    removeEventCallback: mocks.removeEventCallback,
  }),
}));
vi.mock('../../features/notifications/capabilities', () => ({ getNotificationCapabilities: mocks.capabilities }));
vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl(...args: unknown[]) { mocks.withUrl(...args); return this; }
    withAutomaticReconnect() { return this; }
    build() {
      return {
        start: mocks.start, stop: mocks.stop, invoke: mocks.invoke,
        on: (event: string, callback: (payload: unknown) => void) => mocks.handlers.set(event, callback),
        onreconnecting: mocks.onreconnecting, onreconnected: mocks.onreconnected, onclose: mocks.onclose,
      };
    }
  },
}));

const account: AccountInfo = {
  homeAccountId: 'synthetic-home', localAccountId: 'actor-a', tenantId: 'directory-a',
  environment: 'login.microsoftonline.com', username: 'synthetic@example.test',
};
const cookieCapabilities: NotificationCapabilities = {
  recipientId: 'actor-a', rest: { available: true, reasonCode: null },
  realtime: { available: false, authentication: 'bearer', cookieSessionSupported: false,
    reasonCode: 'REALTIME_BEARER_REQUIRED', hubPaths: ['/hubs/package'] },
  fallback: { transport: 'rest-polling', pollIntervalSeconds: 30 },
};
const readyCapabilities: NotificationCapabilities = {
  ...cookieCapabilities, realtime: { ...cookieCapabilities.realtime, available: true, reasonCode: null },
};

function deferred<T>() {
  let resolve!: (result: T) => void;
  const promise = new Promise<T>(accept => { resolve = accept; });
  return { promise, resolve };
}

function useMonitor(jobId = 'job-a', pollIntervalMs?: number) {
  const session = useProgressSession('system-a');
  const monitor = useJobProgress<Status>({
    session, jobId, hubPath: '/hubs/package', pollIntervalMs, events: ['Progress'],
    matchesEvent: value => value !== null && typeof value === 'object' && 'id' in value && value.id === jobId,
    subscribe: connection => connection.invoke('SubscribeToPackage', jobId),
    poll: mocks.poll, isTerminal: value => value.status === 'Completed', onStatus: mocks.onStatus,
  });
  return { session, monitor };
}

beforeEach(() => {
  vi.resetAllMocks();
  mocks.handlers.clear();
  mocks.accounts = [];
  mocks.me = { data: workspaceSession('system-a').identity, isLoading: false, error: null, refetch: vi.fn() };
  mocks.capabilities.mockResolvedValue(cookieCapabilities);
  mocks.poll.mockResolvedValue({ id: 'job-a', status: 'Processing' });
  mocks.addEventCallback.mockReturnValue('account-observer');
  mocks.start.mockResolvedValue(undefined);
  mocks.stop.mockResolvedValue(undefined);
  mocks.invoke.mockResolvedValue(undefined);
  mocks.acquireBearer.mockResolvedValue('synthetic-test-bearer');
  window.history.replaceState({}, '', '/workspaces/organizations/11111111-1111-1111-1111-111111111111/systems/system-a');
});
afterEach(() => { cleanup(); vi.useRealTimers(); });

describe('scoped progress transport', () => {
  it('polls a cookie-only authenticated job without requiring an MSAL account or starting a hub', async () => {
    // Arrange
    const { result } = renderHook(useMonitor);
    // Act
    await waitFor(() => expect(mocks.poll).toHaveBeenCalledTimes(1));
    // Assert
    expect(result.current.session.ready).toBe(true);
    expect(result.current.monitor.transportMessage).toMatch(/REALTIME_BEARER_REQUIRED.*poll/i);
    expect(mocks.withUrl).not.toHaveBeenCalled();
    expect(mocks.capabilities).toHaveBeenCalledWith(expect.any(AbortSignal));
  });

  it('uses job polling cadence independently of personal notification fallback and stops at completion', async () => {
    // Arrange
    vi.useFakeTimers();
    const { unmount } = renderHook(useMonitor);
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    mocks.poll.mockResolvedValue({ id: 'job-a', status: 'Completed' });
    // Act
    await act(async () => { await vi.advanceTimersByTimeAsync(4_999); });
    expect(mocks.poll).toHaveBeenCalledTimes(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    expect(mocks.poll).toHaveBeenCalledTimes(2);
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    // Assert
    expect(mocks.poll).toHaveBeenCalledTimes(2);
    expect(mocks.onStatus.mock.calls.filter(([, terminal]) => terminal)).toEqual([[{ id: 'job-a', status: 'Completed' }, true]]);
    unmount();
  });

  it('does not use a personal-notification REST denial as a progress-resource authorization decision', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue({ ...cookieCapabilities,
      rest: { available: false, reasonCode: 'NOTIFICATION_RECIPIENT_AMBIGUOUS' },
      fallback: { transport: 'none', pollIntervalSeconds: null },
    });
    // Act
    renderHook(useMonitor);
    // Assert
    await waitFor(() => expect(mocks.poll).toHaveBeenCalledTimes(1));
    expect(mocks.start).not.toHaveBeenCalled();
  });

  it('preserves the three-second package-shortcut polling cadence', async () => {
    // Arrange
    vi.useFakeTimers();
    renderHook(() => useMonitor('job-a', 3000));
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    // Act
    await act(async () => { await vi.advanceTimersByTimeAsync(2_999); });
    expect(mocks.poll).toHaveBeenCalledTimes(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    // Assert
    expect(mocks.poll).toHaveBeenCalledTimes(2);
  });

  it.each([null, { ...workspaceSession('system-a').identity, oid: '' }])('does not start work without a resolved server actor', data => {
    // Arrange
    mocks.me.data = data;
    // Act
    const { result } = renderHook(useMonitor);
    // Assert
    expect(result.current.session.ready).toBe(false);
    expect(mocks.capabilities).not.toHaveBeenCalled();
    expect(mocks.poll).not.toHaveBeenCalled();
    expect(mocks.start).not.toHaveBeenCalled();
  });

  it('rejects a capability recipient different from the pinned authenticated actor', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue({ ...readyCapabilities, recipientId: 'other-actor' });
    // Act
    const { result } = renderHook(useMonitor);
    // Assert
    await waitFor(() => expect(result.current.monitor.error).toMatch(/identity/i));
    expect(mocks.poll).not.toHaveBeenCalled();
    expect(mocks.start).not.toHaveBeenCalled();
  });

  it('preserves scoped hub selectors and the token factory only after realtime capability approval', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue(readyCapabilities);
    // Act
    renderHook(useMonitor);
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalledWith('SubscribeToPackage', 'job-a'));
    // Assert
    expect(mocks.withUrl).toHaveBeenCalledWith(expect.stringContaining('workspaceTenantId=11111111-1111-1111-1111-111111111111'),
      { accessTokenFactory: expect.any(Function) });
    expect(mocks.poll).toHaveBeenCalledWith(expect.any(AbortSignal));
  });

  it('does not connect a hub missing from the declared supported paths', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue({ ...readyCapabilities, realtime: { ...readyCapabilities.realtime, hubPaths: ['/hubs/notifications'] } });
    // Act
    const { result } = renderHook(useMonitor);
    await waitFor(() => expect(mocks.poll).toHaveBeenCalled());
    // Assert
    expect(result.current.monitor.transportMessage).toMatch(/not supported/i);
    expect(mocks.start).not.toHaveBeenCalled();
  });

  it('exposes a connection failure while continuing authorized status polling', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue(readyCapabilities);
    mocks.start.mockRejectedValue(new Error('Realtime negotiation failed.'));
    // Act
    const { result } = renderHook(useMonitor);
    // Assert
    await waitFor(() => expect(result.current.monitor.error).toContain('Realtime negotiation failed.'));
    expect(mocks.poll).toHaveBeenCalled();
    expect(result.current.monitor.transportMessage).toMatch(/poll/i);
  });

  it('exposes status errors and permits an explicit retry', async () => {
    // Arrange
    mocks.poll.mockRejectedValueOnce(new Error('Job status forbidden.'));
    const { result } = renderHook(useMonitor);
    await waitFor(() => expect(result.current.monitor.error).toContain('Job status forbidden.'));
    // Act
    await act(async () => { result.current.monitor.retry(); });
    // Assert
    await waitFor(() => expect(mocks.poll).toHaveBeenCalledTimes(2));
    expect(result.current.monitor.error).toBeNull();
  });

  it.each([401, 403, 404])('stops automatic polling and surfaces HTTP %s without retrying forever', async status => {
    // Arrange
    vi.useFakeTimers();
    mocks.poll.mockRejectedValue({ error: `HTTP ${status}: job unavailable` });
    const { result } = renderHook(() => useMonitor());
    await act(async () => { await vi.advanceTimersByTimeAsync(0); });
    // Act
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    // Assert
    expect(mocks.poll).toHaveBeenCalledTimes(1);
    expect(result.current.monitor.error).toContain(`HTTP ${status}`);
    expect(mocks.onStatus).not.toHaveBeenCalled();
  });

  it('cancels pending requests and ignores their results after a workspace change', async () => {
    // Arrange
    const pending = deferred<Status>();
    mocks.poll.mockReturnValueOnce(pending.promise);
    const { rerender } = renderHook(useMonitor);
    await waitFor(() => expect(mocks.poll).toHaveBeenCalledTimes(1));
    const signal = mocks.poll.mock.calls[0]?.[0];
    // Act
    window.history.replaceState({}, '', '/workspaces/organizations/22222222-2222-2222-2222-222222222222/systems/system-a');
    rerender();
    await act(async () => pending.resolve({ id: 'job-a', status: 'Completed' }));
    // Assert
    expect(signal?.aborted).toBe(true);
    expect(mocks.onStatus).not.toHaveBeenCalledWith({ id: 'job-a', status: 'Completed' }, true);
  });

  it('revalidates capabilities when the selected account changes without a root rerender', async () => {
    // Arrange
    renderHook(useMonitor);
    await waitFor(() => expect(mocks.poll).toHaveBeenCalledTimes(1));
    mocks.capabilities.mockResolvedValue(readyCapabilities);
    mocks.accounts = [account];
    const callback = mocks.addEventCallback.mock.calls[0]?.[0];
    if (!callback) throw new Error('Expected an active-account observer.');
    // Act
    await act(async () => callback());
    // Assert
    await waitFor(() => expect(mocks.start).toHaveBeenCalledTimes(1));
    expect(mocks.capabilities).toHaveBeenCalledTimes(2);
  });

  it('ignores a captured event callback after scope changes', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue(readyCapabilities);
    const { rerender } = renderHook(useMonitor);
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalled());
    const oldHandler = mocks.handlers.get('Progress');
    if (!oldHandler) throw new Error('Expected a progress event handler.');
    window.history.replaceState({}, '', '/workspaces/organizations/22222222-2222-2222-2222-222222222222/systems/system-a');
    rerender();
    await waitFor(() => expect(mocks.poll).toHaveBeenCalledTimes(2));
    const count = mocks.poll.mock.calls.length;
    // Act
    await act(async () => oldHandler({ id: 'job-a', status: 'Completed' }));
    // Assert
    expect(mocks.poll).toHaveBeenCalledTimes(count);
  });

  it('does not join after unmount while connection start is pending', async () => {
    // Arrange
    const starting = deferred<void>();
    mocks.capabilities.mockResolvedValue(readyCapabilities);
    mocks.start.mockReturnValue(starting.promise);
    const { unmount } = renderHook(useMonitor);
    await waitFor(() => expect(mocks.start).toHaveBeenCalled());
    // Act
    unmount();
    expect(mocks.stop).not.toHaveBeenCalled();
    await act(async () => starting.resolve(undefined));
    // Assert
    expect(mocks.invoke).not.toHaveBeenCalled();
    expect(mocks.stop).toHaveBeenCalledTimes(1);
  });

  it('rechecks server capabilities before resubscribing a reconnected hub', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue(readyCapabilities);
    const { result } = renderHook(useMonitor);
    await waitFor(() => expect(mocks.invoke).toHaveBeenCalledTimes(1));
    mocks.capabilities.mockResolvedValue(cookieCapabilities);
    const callback = mocks.onreconnected.mock.calls[0]?.[0];
    if (!callback) throw new Error('Expected a reconnect handler.');
    // Act
    await act(async () => { await callback(); });
    // Assert
    expect(mocks.invoke).toHaveBeenCalledTimes(1);
    expect(result.current.monitor.transportMessage).toContain('REALTIME_BEARER_REQUIRED');
    expect(mocks.stop).toHaveBeenCalled();
  });

  it('invalidates prior-job callbacks in the render-to-effect cleanup window', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue(readyCapabilities);
    let delivery: ((payload: unknown) => void) | undefined;
    const { rerender } = renderHook(({ jobId }) => {
      const result = useMonitor(jobId);
      if (jobId === 'job-b' && delivery) {
        const deliver = delivery;
        delivery = undefined;
        deliver({ id: 'job-b' });
      }
      return result;
    }, { initialProps: { jobId: 'job-a' } });
    await waitFor(() => expect(mocks.poll).toHaveBeenCalledTimes(1));
    delivery = mocks.handlers.get('Progress');
    // Act
    rerender({ jobId: 'job-b' });
    // Assert
    await waitFor(() => expect(mocks.poll).toHaveBeenCalledTimes(2));
    expect(mocks.capabilities).toHaveBeenCalledTimes(2);
  });

  it('rejects an obsolete token factory before acquiring a bearer for a different active account', async () => {
    // Arrange
    mocks.capabilities.mockResolvedValue(readyCapabilities);
    renderHook(useMonitor);
    await waitFor(() => expect(mocks.withUrl).toHaveBeenCalled());
    const options: unknown = mocks.withUrl.mock.calls[0]?.[1];
    if (!options || typeof options !== 'object' || !('accessTokenFactory' in options)
      || typeof options.accessTokenFactory !== 'function') throw new Error('Expected a token factory.');
    const factory = options.accessTokenFactory;
    // Act
    mocks.accounts = [account];
    const token = factory();
    // Assert
    await expect(token).rejects.toThrow(/changed/i);
    expect(mocks.acquireBearer).not.toHaveBeenCalled();
  });

  it('surfaces capability errors without attempting a hub or job status request', async () => {
    // Arrange
    mocks.capabilities.mockRejectedValue(new Error('Capabilities forbidden.'));
    // Act
    const { result } = renderHook(useMonitor);
    // Assert
    await waitFor(() => expect(result.current.monitor.error).toBe('Capabilities forbidden.'));
    expect(mocks.start).not.toHaveBeenCalled();
    expect(mocks.poll).not.toHaveBeenCalled();
  });

  it('does not repeat terminal callbacks when a late cancellation or refresh retries completed progress', async () => {
    // Arrange
    mocks.poll.mockResolvedValue({ id: 'job-a', status: 'Completed' });
    const { result } = renderHook(useMonitor);
    await waitFor(() => expect(mocks.onStatus).toHaveBeenCalledTimes(1));
    // Act
    await act(async () => result.current.monitor.retry());
    // Assert
    expect(mocks.onStatus).toHaveBeenCalledTimes(1);
    expect(mocks.poll).toHaveBeenCalledTimes(1);
  });
});
