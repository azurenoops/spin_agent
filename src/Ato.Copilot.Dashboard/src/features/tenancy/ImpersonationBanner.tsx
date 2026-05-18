/**
 * Feature 048 (T075): Persistent banner shown while a CSP-Admin is
 * impersonating a tenant.
 *
 * The HttpOnly impersonation cookie cannot be read from JS, so we mirror the
 * scope locally in `sessionStorage` (managed by `api.ts::startImpersonation`
 * / `endImpersonation`). The banner self-hides when:
 *   - no impersonation state is recorded, or
 *   - the recorded `expiresAt` has passed.
 *
 * Dismissal calls `DELETE /api/tenants/impersonation`, clears the local
 * mirror, and forces a full reload so dependent caches re-fetch under the
 * caller's home tenant scope.
 */

import { useCallback, useEffect, useState } from 'react';
import {
  endImpersonation,
  type ImpersonationState,
  onImpersonationChanged,
  readImpersonation,
} from './api';

function formatExpiry(expiresAt: string): string {
  const ms = new Date(expiresAt).getTime() - Date.now();
  if (!Number.isFinite(ms) || ms <= 0) return 'expired';
  const totalMinutes = Math.floor(ms / 60_000);
  if (totalMinutes < 1) return 'less than a minute';
  if (totalMinutes < 60) return `${totalMinutes}m`;
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  return minutes === 0 ? `${hours}h` : `${hours}h ${minutes}m`;
}

export default function ImpersonationBanner() {
  const [state, setState] = useState<ImpersonationState | null>(() => readImpersonation());
  const [dismissing, setDismissing] = useState(false);

  // Re-read on cross-component impersonation events.
  useEffect(() => onImpersonationChanged(() => setState(readImpersonation())), []);

  // Live-update the "expires in …" label every 30s and self-clear on expiry.
  useEffect(() => {
    if (!state) return;
    const tick = () => {
      const fresh = readImpersonation();
      setState(fresh);
    };
    const interval = window.setInterval(tick, 30_000);
    return () => window.clearInterval(interval);
  }, [state]);

  const handleDismiss = useCallback(async () => {
    setDismissing(true);
    try {
      await endImpersonation();
      setState(null);
      // Reload so per-route data refetches under the home tenant scope.
      window.location.reload();
    } catch {
      // endImpersonation already clears local state in its `finally` block;
      // surface a soft hint by leaving the banner in place would be wrong,
      // so we still clear here.
      setState(null);
    } finally {
      setDismissing(false);
    }
  }, []);

  if (!state) return null;

  return (
    <div
      role="status"
      aria-live="polite"
      data-testid="impersonation-banner"
      className="flex flex-shrink-0 items-center justify-between gap-3 border-b border-purple-300 bg-purple-50 px-6 py-2 text-sm text-purple-900"
    >
      <div className="flex min-w-0 items-center gap-2">
        <svg
          className="h-4 w-4 flex-shrink-0 text-purple-600"
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          strokeWidth={1.8}
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M15.75 6a3.75 3.75 0 1 1-7.5 0 3.75 3.75 0 0 1 7.5 0ZM4.501 20.118a7.5 7.5 0 0 1 14.998 0A17.933 17.933 0 0 1 12 21.75c-2.676 0-5.216-.584-7.499-1.632Z"
          />
        </svg>
        <span className="truncate">
          <span className="font-semibold">CSP.Admin impersonation active</span>
          <span className="mx-2 text-purple-400">·</span>
          <span>
            Acting as{' '}
            <span className="font-semibold">{state.displayName}</span>
          </span>
          <span className="mx-2 text-purple-400">·</span>
          <span className="text-purple-700">expires in {formatExpiry(state.expiresAt)}</span>
        </span>
      </div>
      <button
        type="button"
        onClick={handleDismiss}
        disabled={dismissing}
        className="inline-flex flex-shrink-0 items-center gap-1.5 rounded-md border border-purple-300 bg-white px-3 py-1 text-xs font-medium text-purple-700 transition-colors hover:bg-purple-100 disabled:cursor-not-allowed disabled:opacity-60"
      >
        {dismissing ? (
          <>
            <svg className="h-3 w-3 animate-spin" viewBox="0 0 24 24" fill="none">
              <circle
                className="opacity-25"
                cx="12"
                cy="12"
                r="10"
                stroke="currentColor"
                strokeWidth={4}
              />
              <path
                className="opacity-75"
                fill="currentColor"
                d="M4 12a8 8 0 0 1 8-8v4a4 4 0 0 0-4 4H4z"
              />
            </svg>
            Ending…
          </>
        ) : (
          <>
            <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
            </svg>
            End impersonation
          </>
        )}
      </button>
    </div>
  );
}
