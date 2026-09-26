import { useEffect, useState, type ReactNode } from 'react';
import { useMsal } from '@azure/msal-react';
import { useNavigate } from 'react-router-dom';
import axios from 'axios';
import { DEFAULT_API_SCOPES } from './msalInstance';
import { useOptionalMe, type UseMeResult } from './useMe';
import { WorkspaceStatus } from '../workspaces/WorkspaceBoundary';
import { workspaceErrorMessage } from '../workspaces/api';
import { isStaleSimulationSession } from './authErrors';

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
export default function RequireAuth({ children, scoped = false }: { children: ReactNode; scoped?: boolean }) {
  const shared = useOptionalMe();
  return shared ? <SharedAuthentication identity={shared} scoped={scoped}>{children}</SharedAuthentication>
    : <LegacyRequireAuth>{children}</LegacyRequireAuth>;
}

function SharedAuthentication({ identity, scoped, children }: {
  identity: UseMeResult; scoped: boolean; children: ReactNode;
}) {
  const { instance } = useMsal();
  const navigate = useNavigate();
  const status = (identity.error as { response?: { status?: number } } | null)?.response?.status;
  const staleSimulation = isStaleSimulationSession(identity.error);
  useEffect(() => {
    if (status === 401) {
      if (staleSimulation) {
        navigate('/login?reason=simulation_identity_changed', { replace: true });
      } else {
        void instance.loginRedirect({
          scopes: DEFAULT_API_SCOPES,
          state: window.location.pathname + window.location.search + window.location.hash,
        });
      }
    } else if (status === 403 && !scoped) {
      navigate('/login/error?errorClass=NoTenantAssignment', { replace: true });
    }
  }, [status, scoped, staleSimulation, instance, navigate]);
  if (identity.isLoading) return <WorkspaceStatus loading message="Checking authentication..." />;
  if (status === 401 || (status === 403 && !scoped)) return null;
  if (identity.error) return <WorkspaceStatus message={workspaceErrorMessage(identity.error)} onRetry={identity.refetch} />;
  if (!identity.data) return <WorkspaceStatus message="Your authenticated identity could not be resolved." onRetry={identity.refetch} />;
  return <>{children}</>;
}

function LegacyRequireAuth({ children }: { children: ReactNode }) {
  const { instance } = useMsal();
  const navigate = useNavigate();
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

        if (status === 401) {
          if (isStaleSimulationSession(err)) {
            setState('redirecting');
            navigate('/login?reason=simulation_identity_changed', { replace: true });
            return;
          }
          // Not authenticated. Punt to Entra via MSAL with the deep-link
          // state preserved so the post-login callback returns the user
          // to the page they requested.
          setState('redirecting');
          const deepLink =
            window.location.pathname + window.location.search + window.location.hash;
          void instance.loginRedirect({
            scopes: DEFAULT_API_SCOPES,
            state: deepLink,
          });
        } else if (status === 403) {
          // Authenticated Entra identity but no SPIN Agent tenant assignment.
          // Redirecting back to MSAL login would cause an infinite loop
          // (user re-authenticates → gets 403 again → loops forever).
          // Navigate to the error page instead so the user gets actionable copy.
          setState('redirecting');
          navigate('/login/error?errorClass=NoTenantAssignment', { replace: true });
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

  if (state === 'probing') {
    return (
      <div className="flex min-h-screen items-center justify-center bg-gray-50">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-indigo-600 border-t-transparent" />
      </div>
    );
  }
  if (state === 'redirecting') return null;
  return <>{children}</>;
}
