import { useEffect } from 'react';
import { useMsal } from '@azure/msal-react';
import { useNavigate } from 'react-router-dom';
import axios from 'axios';
import { useLoginRaceListener } from './useLoginRaceListener';
import type { MeResponse } from './types';

/**
 * Feature 051 T050 [US1] — handles the MSAL redirect callback. Awaits
 * `handleRedirectPromise`; on success, navigates to the deep-link
 * (carried as `state`) or to `/` if absent. On failure, navigates to
 * `/login/error` with the inferred `errorClass`.
 *
 * T073 [US3] addendum: after the auth handshake resolves, fetch
 * `/api/auth/me`. If the user has more than one tenant membership OR
 * is a CSP-Admin, route to `/login/select-tenant` (carrying the
 * intended deep link in `location.state`) instead of going straight
 * to the deep link. Falls back to the simple navigate path if the
 * `/me` fetch fails so we don't strand the user.
 *
 * Defensive: also mounts {@link useLoginRaceListener} (T053c) so that if
 * a sibling tab finishes sign-in WHILE this callback is still resolving,
 * we don't end up dispatching two navigations on top of each other —
 * the listener advances us to `/` and the `handleRedirectPromise`
 * resolution becomes a no-op against an unmounted component.
 */
export default function LoginCallbackPage() {
  const { instance } = useMsal();
  const navigate = useNavigate();

  useLoginRaceListener({
    onLoginCompletedInAnotherTab: () => navigate('/', { replace: true }),
  });

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const result = await instance.handleRedirectPromise();
        if (cancelled) return;

        // Epic #207 / Task #245 — broadcast login_complete so sibling tabs
        // (still on /login) can advance without requiring the user to click
        // "Sign in" a second time. BroadcastChannel is preferred; the storage-
        // event fallback in useLoginRaceListener handles browsers without it.
        if (result?.account && 'BroadcastChannel' in window) {
          try {
            const ch = new BroadcastChannel('ato-login');
            ch.postMessage({ type: 'login_complete' });
            ch.close();
          } catch {
            // Non-critical — best effort broadcast.
          }
        }

        const target =
          (result?.state && typeof result.state === 'string' ? result.state : '') ||
          '/';
        // T073 [US3]: route through the picker when there's > 1 membership
        // or the caller is a CSP-Admin. Failure to fetch `/me` (network,
        // 401 from a stale bearer, etc.) falls back to the simple path so
        // single-tenant users are not blocked.
        let routeToPicker = false;
        try {
          const meResp = await axios.get('/api/auth/me');
          const body = meResp.data as { status?: string; data?: MeResponse };
          const me = body?.status === 'success' ? body.data : null;
          if (me) {
            routeToPicker =
              (me.tenantMemberships?.length ?? 0) > 1 || me.isCspAdmin === true;
          }
        } catch {
          // Best-effort — leave routeToPicker false so we still navigate.
        }
        if (cancelled) return;
        if (routeToPicker) {
          navigate('/login/select-tenant', {
            replace: true,
            state: { deepLink: target },
          });
        } else {
          navigate(target, { replace: true });
        }
      } catch (err) {
        if (cancelled) return;
        const errorClass = inferErrorClass(err);
        const correlationId = inferCorrelationId(err);
        const params = new URLSearchParams({ errorClass });
        if (correlationId) params.set('correlationId', correlationId);
        navigate(`/login/error?${params.toString()}`, { replace: true });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [instance, navigate]);

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50">
      <div className="text-center">
        <div className="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600 mb-4" />
        <p className="text-sm text-gray-700">Signing you in…</p>
      </div>
    </div>
  );
}

/**
 * Best-effort mapping of MSAL error messages to the canonical
 * `ErrorClass` taxonomy (`contracts/frontend-types.md § 2`). US4
 * (Phase 6) will refine this — for now we cover the common cases and
 * fall back to a generic `NetworkFailure`.
 */
function inferErrorClass(err: unknown): string {
  const msg = err instanceof Error ? err.message : String(err);
  if (/AADSTS50105|AADSTS50057|disabled/i.test(msg)) return 'AccountDisabled';
  if (/AADSTS500|conditional access/i.test(msg)) return 'ConditionalAccessBlock';
  if (/AADSTS500.+mfa|mfa.+failed/i.test(msg)) return 'MfaFailure';
  if (/clock|skew|time/i.test(msg)) return 'ClockSkew';
  if (/network|fetch|timeout|offline/i.test(msg)) return 'NetworkFailure';
  return 'NetworkFailure';
}

function inferCorrelationId(err: unknown): string | null {
  if (!err) return null;
  const obj = err as { correlationId?: string };
  return typeof obj.correlationId === 'string' ? obj.correlationId : null;
}
