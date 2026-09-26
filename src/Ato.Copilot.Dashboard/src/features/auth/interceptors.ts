import { CanceledError, type AxiosInstance, type InternalAxiosRequestConfig, type AxiosResponse, type AxiosError } from 'axios';
import type { IPublicClientApplication } from '@azure/msal-browser';
import { msalAccountKey, selectMsalAccount } from './accountSelection';
import { assertWorkspaceRequestCurrent, captureWorkspaceRequest } from '../workspaces/workspaceTransport';
import { isStaleSimulationSession } from './authErrors';

/**
 * Internal flag we hang off the axios config to mark requests that are
 * retried as part of a silent token-renewal so the response interceptor
 * does NOT dispatch `ato:user-input` (which would mistakenly reset the
 * idle timer, per research § R10).
 */
const SILENT_RENEWAL = '_silentRenewal' as const;

type FlaggedConfig = InternalAxiosRequestConfig & {
  [SILENT_RENEWAL]?: boolean;
  _authAccountKey?: string;
};

function assertRequestAccount(config: FlaggedConfig | undefined, msal: IPublicClientApplication | null): void {
  if (config?._authAccountKey === undefined || config._authAccountKey === msalAccountKey(msal)) return;
  const error = new CanceledError('Authenticated account changed while the request was in progress.');
  error.config = config;
  throw error;
}

/**
 * Feature 051 § 3.3 — wire the MSAL.js access-token acquisition into
 * every axios call:
 *
 * 1. Request: acquire a token for the active account, or the first cached
 *    account only when no active account is selected.
 *    On success set `Authorization: Bearer <token>`. With no account
 *    available, leave the header unset (the request will 401 — which the
 *    response interceptor turns into a `loginRedirect`).
 * 2. Response 2xx: dispatch `ato:user-input` UNLESS the request was tagged
 *    `_silentRenewal = true`. The dispatched event is what `useIdleTimer`
 *    consumes — silent renewals MUST NOT reset the idle timer (FR-007a).
 * 3. Response 401: on the FIRST 401 for a request, tag it
 *    `_silentRenewal = true` and retry once. A second 401 means the user
 *    cannot be silently renewed → call `msal.loginRedirect({ scopes,
 *    state: <deep link> })` and reject the promise.
 */
export function attachAuthInterceptor(
  axiosInstance: AxiosInstance,
  msalOrGetter: IPublicClientApplication | (() => IPublicClientApplication),
  scopes: string[],
): void {
  // Resolve the MSAL instance lazily (per-request) so feature `api.ts`
  // modules can attach this interceptor at import time even when MSAL
  // has not been initialised yet (Vitest test runners, SSR, etc.). The
  // resolver throws when truly missing; we treat that as "no account".
  const resolveMsal = (): IPublicClientApplication | null => {
    try {
      return typeof msalOrGetter === 'function' ? msalOrGetter() : msalOrGetter;
    } catch {
      return null;
    }
  };

  axiosInstance.interceptors.request.use(async (config: FlaggedConfig) => {
    captureWorkspaceRequest(axiosInstance, config);
    const msal = resolveMsal();
    if (config._authAccountKey === undefined) config._authAccountKey = msalAccountKey(msal);
    else assertRequestAccount(config, msal);
    if (!msal) return config;
    const account = selectMsalAccount(msal);
    if (!account) {
      return config;
    }
    try {
      const result = await msal.acquireTokenSilent({ scopes, account });
      const token = result.accessToken;
      if (token) {
        config.headers = config.headers ?? ({} as InternalAxiosRequestConfig['headers']);
        // Axios header object is typed as a Record but at runtime accepts string assignment.
        (config.headers as Record<string, string>)['Authorization'] = `Bearer ${token}`;
      }
    } catch {
      // Swallow — the response interceptor handles a downstream 401 by
      // doing a `loginRedirect`. Throwing here would also produce a 401
      // path, but with worse telemetry.
    }
    assertWorkspaceRequestCurrent(config);
    assertRequestAccount(config, resolveMsal());
    return config;
  });

  axiosInstance.interceptors.response.use(
    (response: AxiosResponse) => {
      assertWorkspaceRequestCurrent(response.config);
      const cfg = response.config as FlaggedConfig;
      assertRequestAccount(cfg, resolveMsal());
      if (cfg[SILENT_RENEWAL] !== true) {
        window.dispatchEvent(
          new CustomEvent('ato:user-input', { detail: { source: 'api-success' } }),
        );
      }
      return response;
    },
    async (error: AxiosError) => {
      assertWorkspaceRequestCurrent(error.config);
      const cfg = error.config as FlaggedConfig | undefined;
      const status = error.response?.status;
      const msal = resolveMsal();
      assertRequestAccount(cfg, msal);

      if (status === 401 && isStaleSimulationSession(error)) {
        return Promise.reject(error);
      }

      if (status === 401 && cfg && cfg[SILENT_RENEWAL] !== true && msal) {
        // First 401 — try a single silent-renewal retry.
        cfg[SILENT_RENEWAL] = true;
        const account = selectMsalAccount(msal);
        if (account) {
          try {
            const result = await msal.acquireTokenSilent({ scopes, account });
            assertRequestAccount(cfg, resolveMsal());
            const token = result.accessToken;
            if (token) {
              cfg.headers = cfg.headers ?? ({} as InternalAxiosRequestConfig['headers']);
              (cfg.headers as Record<string, string>)['Authorization'] = `Bearer ${token}`;
              return axiosInstance.request(cfg);
            }
          } catch (renewalError) {
            if (renewalError instanceof CanceledError) throw renewalError;
            // Fall through to loginRedirect.
          }
        }

        // No account or silent renewal failed → loginRedirect with the
        // deep link preserved as `state`.
        const deepLink = window.location.pathname + window.location.search + window.location.hash;
        await msal.loginRedirect({ scopes, state: deepLink });
        return Promise.reject(error);
      }

      if (status === 401 && cfg && cfg[SILENT_RENEWAL] === true && msal) {
        // Second 401 — silent renewal cannot rescue us; redirect.
        const deepLink = window.location.pathname + window.location.search + window.location.hash;
        await msal.loginRedirect({ scopes, state: deepLink });
        return Promise.reject(error);
      }

      return Promise.reject(error);
    },
  );
}
