import { useEffect, useState, type ReactNode } from 'react';
import { useMsal } from '@azure/msal-react';
import axios from 'axios';
import { DEFAULT_API_SCOPES } from './msalInstance';

/**
 * Feature 051 T051 [US1] — gate component for protected routes.
 *
 * Probes `GET /api/auth/me` to determine whether the caller is
 * authenticated. The server-side check covers BOTH authentication
 * paths:
 *   - **MSAL bearer** (US1): the request interceptor injects the
 *     token, `CacAuthenticationMiddleware` validates it, `/me`
 *     returns 200.
 *   - **Simulation cookies** (Development only — US7): the server
 *     reads the `ato-simulation` cookie (HttpOnly, JS-invisible) and
 *     promotes the simulated identity, `/me` returns 200.
 *
 * On 401 the SPA falls back to MSAL `loginRedirect` with the deep
 * link state so the post-login callback can return the user to the
 * page they requested. On any other error the user lands on the
 * login page.
 *
 * Original Phase 3 implementation used `useIsAuthenticated()` from
 * `@azure/msal-react` which only inspects MSAL's local account
 * cache — that misses the simulation path because simulation
 * cookies live in the server-side session, not in MSAL state.
 * Probing the server is the only check that honors BOTH auth modes
 * uniformly.
 */
export default function RequireAuth({ children }: { children: ReactNode }) {
  const { instance } = useMsal();
  const [state, setState] = useState<'probing' | 'authenticated' | 'redirecting'>('probing');

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        // The MSAL request interceptor injects the bearer when an
        // account exists; with no account it sends the request
        // unauthenticated. Either way the server's response is the
        // source of truth.
        await axios.get('/api/auth/me');
        if (!cancelled) setState('authenticated');
      } catch (err: unknown) {
        if (cancelled) return;
        const status =
          err && typeof err === 'object' && 'response' in err
            ? (err as { response?: { status?: number } }).response?.status
            : undefined;

        if (status === 401 || status === 403) {
          // Not authenticated AND not a simulation session. Punt to
          // Entra via MSAL with the deep-link state preserved.
          setState('redirecting');
          const deepLink =
            window.location.pathname + window.location.search + window.location.hash;
          void instance.loginRedirect({
            scopes: DEFAULT_API_SCOPES,
            state: deepLink,
          });
        } else {
          // Network / server error — show the login page so the user
          // can retry rather than spinning indefinitely.
          setState('redirecting');
          window.location.assign('/login?reason=network_error');
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [instance]);

  if (state !== 'authenticated') {
    return null;
  }
  return <>{children}</>;
}
