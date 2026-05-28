import axios, { type AxiosInstance } from 'axios';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { attachAuthInterceptor } from '../../features/auth/interceptors';

// ─── MSAL mock — minimal IPublicClientApplication surface ───────────────

interface MsalMock {
  getAllAccounts: ReturnType<typeof vi.fn>;
  acquireTokenSilent: ReturnType<typeof vi.fn>;
  loginRedirect: ReturnType<typeof vi.fn>;
}

function buildMsalMock(opts?: { hasAccount?: boolean }): MsalMock {
  const hasAccount = opts?.hasAccount ?? true;
  return {
    getAllAccounts: vi.fn(() => (hasAccount ? [{ homeAccountId: 'oid-1' }] : [])),
    acquireTokenSilent: vi.fn(async () => ({ accessToken: 'token-123' })),
    loginRedirect: vi.fn(async () => undefined),
  };
}

// ─── Tiny in-memory request adapter so we can drive axios without HTTP ──

interface PlannedResponse {
  status: number;
  data?: unknown;
}

function buildAxios(plan: PlannedResponse[], capture?: { last?: import('axios').InternalAxiosRequestConfig }): AxiosInstance {
  const instance = axios.create();
  let call = 0;
  instance.defaults.adapter = async (config) => {
    if (capture) capture.last = config;
    const next = plan[call] ?? plan[plan.length - 1];
    call += 1;
    if (!next) {
      throw new Error('Adapter plan exhausted');
    }
    const response = {
      data: next.data ?? {},
      status: next.status,
      statusText: 'OK',
      headers: {},
      config,
    };
    if (next.status >= 400) {
      const err = new axios.AxiosError(
        `Status ${next.status}`,
        String(next.status),
        config,
        null,
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        response as any,
      );
      throw err;
    }
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return response as any;
  };
  return instance;
}

// ─── Helpers ────────────────────────────────────────────────────────────

function listenForUserInput(): { count: number; cleanup: () => void } {
  let count = 0;
  const handler = () => {
    count += 1;
  };
  window.addEventListener('ato:user-input', handler);
  return {
    get count() {
      return count;
    },
    cleanup: () => window.removeEventListener('ato:user-input', handler),
  };
}

describe('attachAuthInterceptor', () => {
  beforeEach(() => {
    // Reset URL fixture for deep-link assertions.
    window.history.replaceState({}, '', '/dashboard/systems?id=123');
  });

  it('dispatches ato:user-input on a non-renewal 2xx response', async () => {
    // Arrange
    const ax = buildAxios([{ status: 200 }]);
    const msal = buildMsalMock();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    attachAuthInterceptor(ax, msal as any, ['api://ato-copilot/.default']);
    const events = listenForUserInput();

    // Act
    await ax.get('/api/anything');

    // Assert
    expect(events.count).toBe(1);
    events.cleanup();
  });

  it('does NOT dispatch ato:user-input on the silent-renewal retry path', async () => {
    // Arrange — first call 401, second call 200 (silent renewal retry).
    const ax = buildAxios([{ status: 401 }, { status: 200 }]);
    const msal = buildMsalMock();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    attachAuthInterceptor(ax, msal as any, ['api://ato-copilot/.default']);
    const events = listenForUserInput();

    // Act
    const response = await ax.get('/api/anything');

    // Assert — retry succeeded but the response interceptor saw the
    // _silentRenewal flag and skipped the dispatch.
    expect(response.status).toBe(200);
    expect(events.count).toBe(0);
    expect(msal.acquireTokenSilent).toHaveBeenCalled();
    expect(msal.loginRedirect).not.toHaveBeenCalled();
    events.cleanup();
  });

  it('attaches an Authorization Bearer header on each request', async () => {
    // Arrange
    const capture: { last?: import('axios').InternalAxiosRequestConfig } = {};
    const ax = buildAxios([{ status: 200 }], capture);
    const msal = buildMsalMock();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    attachAuthInterceptor(ax, msal as any, ['api://ato-copilot/.default']);

    // Act
    await ax.get('/api/anything');

    // Assert — the adapter sees the fully-composed config AFTER my
    // auth interceptor has set the header (request interceptors run LIFO
    // and the adapter is the terminal step in the chain).
    const observedAuth = (capture.last?.headers as Record<string, string> | undefined)?.[
      'Authorization'
    ];
    expect(observedAuth).toBe('Bearer token-123');
    expect(msal.acquireTokenSilent).toHaveBeenCalledTimes(1);
  });

  it('a single 401 triggers a silent-renewal retry without loginRedirect', async () => {
    // Arrange — 401 then 200.
    const ax = buildAxios([{ status: 401 }, { status: 200 }]);
    const msal = buildMsalMock();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    attachAuthInterceptor(ax, msal as any, ['api://ato-copilot/.default']);

    // Act
    const response = await ax.get('/api/anything');

    // Assert — request succeeds without a loginRedirect. The retry
    // re-enters the request interceptor (request #1 + response-handler
    // renewal + retry request) so acquireTokenSilent is called at least
    // 2 times; the exact count is an implementation detail of the
    // request-interceptor chain.
    expect(response.status).toBe(200);
    expect(msal.acquireTokenSilent.mock.calls.length).toBeGreaterThanOrEqual(2);
    expect(msal.loginRedirect).not.toHaveBeenCalled();
  });

  it('a second 401 triggers loginRedirect with the deep-link state', async () => {
    // Arrange — 401 + 401 + 401 (no plan terminator).
    const ax = buildAxios([{ status: 401 }, { status: 401 }]);
    const msal = buildMsalMock();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    attachAuthInterceptor(ax, msal as any, ['api://ato-copilot/.default']);

    // Act
    await expect(ax.get('/api/anything')).rejects.toBeDefined();

    // Assert
    expect(msal.loginRedirect).toHaveBeenCalledTimes(1);
    const arg = msal.loginRedirect.mock.calls[0]?.[0];
    expect(arg).toBeDefined();
    expect(arg.scopes).toEqual(['api://ato-copilot/.default']);
    expect(arg.state).toBe('/dashboard/systems?id=123');
  });

  it('does NOT attach Authorization when no MSAL account is present', async () => {
    // Arrange
    const ax = buildAxios([{ status: 200 }]);
    const msal = buildMsalMock({ hasAccount: false });
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    attachAuthInterceptor(ax, msal as any, ['api://ato-copilot/.default']);

    let observedAuth: string | undefined;
    ax.interceptors.request.use((cfg) => {
      observedAuth = (cfg.headers as Record<string, string> | undefined)?.['Authorization'];
      return cfg;
    });

    // Act
    await ax.get('/api/anything');

    // Assert — no account means we silently skip token acquisition; the
    // request goes out unauthenticated and the server can decide.
    expect(observedAuth).toBeUndefined();
    expect(msal.acquireTokenSilent).not.toHaveBeenCalled();
  });
});
