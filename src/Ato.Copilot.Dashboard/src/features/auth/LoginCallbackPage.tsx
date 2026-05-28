import { useEffect } from 'react';
import { useMsal } from '@azure/msal-react';
import { useNavigate } from 'react-router-dom';
import { useLoginRaceListener } from './useLoginRaceListener';

/**
 * Feature 051 T050 [US1] — handles the MSAL redirect callback. Awaits
 * `handleRedirectPromise`; on success, navigates to the deep-link
 * (carried as `state`) or to `/` if absent. On failure, navigates to
 * `/login/error` with the inferred `errorClass`.
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
        const target =
          (result?.state && typeof result.state === 'string' ? result.state : '') ||
          '/';
        navigate(target, { replace: true });
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
