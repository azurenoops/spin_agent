import { useEffect, useRef, useState } from 'react';
import axios from 'axios';
import { getMsalInstance } from './msalInstance';

/**
 * Feature 051 T058 [US2] — idle-sign-out timer per FR-007/FR-007a and
 * `contracts/frontend-types.md § 4.1`.
 *
 * Implementation contract (per `research.md § R10`):
 * - A single chained {@link setTimeout} reschedules at each activity
 *   event (NOT {@link setInterval}, which would fire continuously
 *   regardless of activity).
 * - User-activity event sources: `mousemove`, `keydown`, `touchstart`,
 *   `click`, and the custom `ato:user-input` event dispatched by the
 *   axios response interceptor on every NON-silent-renewal 2xx.
 * - Silent-renewal user-input events (detail.source === 'silent-renewal')
 *   MUST NOT reset the timer (FR-007a). Today the interceptor never
 *   dispatches the event on the renewal path; this guard is
 *   defence-in-depth against future regressions.
 * - 60 seconds before idle expiry, a `'ato:idle-warning'` event is
 *   dispatched so {@link IdleWarningModal} can render and
 *   {@link useIdleFormStateBackup} can persist snapshots.
 * - On expiry: send `POST /api/auth/signout {"reason":"idle_timeout"}`,
 *   then call `msalInstance.logoutRedirect({...})` with a
 *   `?reason=idle_timeout` query so the `/login` page can surface the
 *   right copy.
 * - When the tab is hidden the timer does NOT fire (modern browsers
 *   clamp timers anyway). The first activity on visibilitychange
 *   re-arms the timer.
 */
export interface UseIdleTimerResult {
  /** Seconds remaining until idle sign-out. */
  remainingSeconds: number;
  /** Reset the timer (used by the modal "Stay signed in" button). */
  reset: () => void;
}

const WARNING_LEAD_SECONDS = 60;
const ACTIVITY_EVENTS = ['mousemove', 'keydown', 'touchstart', 'click'] as const;

export function useIdleTimer(timeoutMinutes: number): UseIdleTimerResult {
  const [remainingSeconds, setRemainingSeconds] = useState(timeoutMinutes * 60);
  const expiryTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const warningTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const expiredRef = useRef(false);

  useEffect(() => {
    const totalMs = Math.max(1, timeoutMinutes) * 60_000;
    const warningMs = Math.max(0, totalMs - WARNING_LEAD_SECONDS * 1_000);

    const clearTimers = () => {
      if (expiryTimeoutRef.current !== null) {
        clearTimeout(expiryTimeoutRef.current);
        expiryTimeoutRef.current = null;
      }
      if (warningTimeoutRef.current !== null) {
        clearTimeout(warningTimeoutRef.current);
        warningTimeoutRef.current = null;
      }
    };

    const handleExpiry = async () => {
      if (expiredRef.current) return;
      expiredRef.current = true;
      try {
        await axios.post('/api/auth/signout', { reason: 'idle_timeout' });
      } catch {
        // Best-effort — server may be unreachable, but we still sign out
        // client-side so the user is not left in a logged-in shell.
      }
      // Dev-simulation sessions (US7) have NO MSAL account. Calling
      // logoutRedirect() in that case bounces the browser to Entra's
      // logout endpoint and ends the user's real Microsoft session.
      // Only call MSAL logout when MSAL actually holds an account.
      try {
        const msal = getMsalInstance();
        if (msal.getAllAccounts().length > 0) {
          await msal.logoutRedirect({
            postLogoutRedirectUri: '/login?reason=idle_timeout',
          });
        } else {
          window.location.href = '/login?reason=idle_timeout';
        }
      } catch {
        // No MSAL instance configured (test envs, SSR) — silently no-op.
      }
    };

    const arm = () => {
      clearTimers();
      if (typeof document !== 'undefined' && document.visibilityState === 'hidden') {
        return;
      }
      warningTimeoutRef.current = setTimeout(() => {
        window.dispatchEvent(
          new CustomEvent('ato:idle-warning', {
            detail: { secondsUntilSignOut: WARNING_LEAD_SECONDS },
          }),
        );
      }, warningMs);
      expiryTimeoutRef.current = setTimeout(() => {
        void handleExpiry();
      }, totalMs);
      setRemainingSeconds(Math.floor(totalMs / 1_000));
    };

    const onActivity = () => {
      arm();
    };

    const onUserInput = (e: Event) => {
      // Defence-in-depth: ignore events tagged as silent-renewal so a
      // background token refresh cannot reset the inactivity clock
      // (FR-007a). The axios interceptor today only dispatches the
      // event on NON-renewal 2xx, but a future change to that
      // contract MUST NOT break this guarantee.
      const detail = (e as CustomEvent<{ source?: string } | undefined>).detail;
      if (detail?.source === 'silent-renewal') return;
      arm();
    };

    const onVisibility = () => {
      if (document.visibilityState === 'visible') {
        arm();
      } else {
        clearTimers();
      }
    };

    ACTIVITY_EVENTS.forEach((name) =>
      window.addEventListener(name, onActivity, { passive: true }),
    );
    window.addEventListener('ato:user-input', onUserInput as EventListener);
    document.addEventListener('visibilitychange', onVisibility);

    arm();

    return () => {
      clearTimers();
      ACTIVITY_EVENTS.forEach((name) =>
        window.removeEventListener(name, onActivity),
      );
      window.removeEventListener('ato:user-input', onUserInput as EventListener);
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, [timeoutMinutes]);

  const reset = () => {
    // Dispatch a synthetic activity event — the existing listener
    // chain will pick it up and re-arm. This keeps the reset path
    // observable in tests and lets external callers (e.g.
    // IdleWarningModal's "Stay signed in" button) share the wiring.
    window.dispatchEvent(
      new CustomEvent('ato:user-input', { detail: { source: 'manual-reset' } }),
    );
  };

  return { remainingSeconds, reset };
}
