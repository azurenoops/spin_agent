import type { Configuration } from '@azure/msal-browser';
import type { LoginConfig } from './types';

/**
 * Feature 051 § 3.1 — translate the server's `LoginConfig.msal` block into
 * the `Configuration` object MSAL.js consumes when constructing a
 * `PublicClientApplication`.
 *
 * The cache lives in `localStorage` (NOT sessionStorage) so the
 * cross-tab login-race coordination (research § R11) can rely on the
 * `storage` event firing for sibling tabs.
 */
export function buildMsalConfig(login: LoginConfig): Configuration {
  return {
    auth: {
      clientId: login.msal.clientId,
      authority: login.msal.authority,
      redirectUri: login.msal.redirectUri,
      postLogoutRedirectUri: login.msal.postLogoutRedirectUri,
      // We handle deep-link preservation ourselves via `state`.
      navigateToLoginRequestUrl: false,
    },
    cache: {
      cacheLocation: 'localStorage',
      storeAuthStateInCookie: false,
    },
    // Native broker (Windows WAM) is intentionally not enabled — we depend
    // on the standard MSAL.js redirect flow uniformly across the dashboard.
  };
}
