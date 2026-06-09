import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMe } from './useMe';
import {
  clearPreImpersonationUrl,
  getPreImpersonationUrl,
} from './preImpersonationUrl';
import { endImpersonation } from '../tenancy/api';

/**
 * Feature 051 T134 [US8] — sticky banner shown while the dashboard user
 * is impersonating a customer tenant via the existing Feature 048 cookie
 * flow. Self-hides when:
 *
 *   - `useMe().data.isImpersonating === false`, or
 *   - `me.impersonation` is null.
 *
 * Key behaviors:
 *   - Real-time countdown computed from `expiresAt - now`, refreshed
 *     every second under a single `setInterval` (FR-027).
 *   - Auto-end detection: when `expiresAt` slips into the past, the banner
 *     shows "Impersonation ended automatically" and refetches `/me` so
 *     the server-side cleanup path (the `/me` handler clears the stale
 *     impersonation cookie and writes `ImpersonationEnd(expired)` per
 *     Phase 11.2 T132) runs promptly (FR-028).
 *   - Exit click — calls the Feature 048 DELETE endpoint, refetches
 *     `/me`, AND navigates to the pre-impersonation URL stored in
 *     sessionStorage (FR-029 / analysis C6). When no URL is stored,
 *     falls back to the SPA root (the persona-default landing).
 *   - Accessible — `role="status"` + `aria-live="polite"` per FR-039.
 *   - Color contrast: yellow-100 background + yellow-900 text exceeds
 *     WCAG AA 4.5:1.
 *
 * Layout: sticky at the top of the authenticated app shell so every
 * route inherits it without needing per-page wiring.
 */

function formatCountdown(msRemaining: number): string {
  if (!Number.isFinite(msRemaining) || msRemaining <= 0) return '00:00';
  const totalSeconds = Math.floor(msRemaining / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return hours > 0
    ? `${pad(hours)}:${pad(minutes)}:${pad(seconds)}`
    : `${pad(minutes)}:${pad(seconds)}`;
}

export default function ImpersonationBanner() {
  const { data: me, refetch } = useMe();
  const navigate = useNavigate();
  const [exiting, setExiting] = useState(false);
  const [showConfirm, setShowConfirm] = useState(false);
  // Track "now" in state so the countdown re-renders deterministically;
  // a counter would work too but explicitly storing the timestamp keeps
  // the test surface easy to reason about under fake timers.
  const [now, setNow] = useState<number>(() => Date.now());
  // Guard against double-refetch when the banner sees an already-
  // expired cookie on first render.
  const autoEndRefetched = useRef(false);

  const expiresAtMs = useMemo(() => {
    if (!me?.impersonation?.expiresAt) return null;
    const t = new Date(me.impersonation.expiresAt).getTime();
    return Number.isFinite(t) ? t : null;
  }, [me?.impersonation?.expiresAt]);

  const isImpersonating = me?.isImpersonating === true && me.impersonation !== null;

  // Per-second tick so the countdown updates without prop churn.
  useEffect(() => {
    if (!isImpersonating) return;
    const id = window.setInterval(() => setNow(Date.now()), 1_000);
    return () => window.clearInterval(id);
  }, [isImpersonating]);

  // Auto-end detection: when expiresAt is in the past, fire a single
  // refetch so the server clears the cookie + writes the audit row.
  // We do NOT call endImpersonation() here — the server is the source of
  // truth for cookie deletion, and /me already does the right thing.
  useEffect(() => {
    if (!isImpersonating || expiresAtMs === null) return;
    if (expiresAtMs <= now && !autoEndRefetched.current) {
      autoEndRefetched.current = true;
      refetch();
    }
  }, [isImpersonating, expiresAtMs, now, refetch]);

  // Refetch on the cross-component "tenant changed" event so when the
  // CSP-Admin starts impersonation from another component, this banner
  // picks up the new /me state without a full reload.
  useEffect(() => {
    const handler = () => refetch();
    window.addEventListener('ato:tenant-changed', handler);
    return () => window.removeEventListener('ato:tenant-changed', handler);
  }, [refetch]);

  const handleConfirm = useCallback(async () => {
    if (exiting) return;
    setExiting(true);
    // Read + clear the pre-impersonation URL BEFORE any await so the
    // navigation target is captured even if the user races a second
    // impersonation start. Fall back to the SPA root when absent
    // (the persona-default landing) per FR-029.
    const returnUrl = getPreImpersonationUrl();
    clearPreImpersonationUrl();
    try {
      await endImpersonation();
    } catch {
      // endImpersonation already clears its local mirror in its finally;
      // we still refetch so the banner unmounts.
    } finally {
      refetch();
      setExiting(false);
      setShowConfirm(false);
      navigate(returnUrl ?? '/');
    }
  }, [exiting, refetch, navigate]);

  if (!me || !isImpersonating || me.impersonation === null) return null;

  const tenant = me.impersonation.impersonatedTenant;
  const expired = expiresAtMs !== null && expiresAtMs <= now;
  const countdown = expiresAtMs === null
    ? '--:--'
    : formatCountdown(expiresAtMs - now);

  return (
    <div
      role="status"
      aria-live="polite"
      data-testid="impersonation-banner-051"
      className="relative sticky top-0 z-50 flex items-center justify-between gap-3 border-l-4 border-yellow-500 bg-yellow-100 px-4 py-3 text-sm text-yellow-900 shadow-sm"
    >
      <div className="flex min-w-0 items-center gap-2">
        <svg
          className="h-4 w-4 flex-shrink-0 text-yellow-700"
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          strokeWidth={1.8}
          aria-hidden="true"
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            d="M12 9v3.75m0 3.75h.008v.008H12V16.5Zm0-13.5a9 9 0 1 1 0 18 9 9 0 0 1 0-18Z"
          />
        </svg>
        <span className="truncate">
          {expired ? (
            <strong>Impersonation ended automatically.</strong>
          ) : (
            <>
              <strong>Impersonating</strong>{' '}
              <span className="font-semibold">{tenant.displayName}</span>
              <span className="mx-2 text-yellow-700">·</span>
              <span aria-label={`Time remaining ${countdown}`}>
                {countdown} remaining
              </span>
            </>
          )}
        </span>
      </div>
      <button
        type="button"
        onClick={() => setShowConfirm(true)}
        disabled={exiting}
        className="inline-flex flex-shrink-0 items-center gap-1.5 rounded-md border border-yellow-600 bg-white px-3 py-1 text-xs font-semibold text-yellow-900 transition-colors hover:bg-yellow-50 disabled:cursor-not-allowed disabled:opacity-60"
      >
        {exiting ? 'Exiting…' : 'Exit'}
      </button>

      {showConfirm && (
        <div
          data-testid="impersonation-exit-confirm"
          role="dialog"
          aria-modal="true"
          className="absolute right-0 top-full mt-1 w-72 rounded-lg border border-yellow-300 bg-white p-4 shadow-xl z-50"
        >
          <p className="text-sm font-medium text-gray-900">
            Exit impersonation of{' '}
            <span className="font-semibold">{tenant.displayName}</span>?
          </p>
          <p className="mt-1 text-xs text-gray-500">
            You will be returned to your CSP-Admin view.
          </p>
          <div className="mt-3 flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setShowConfirm(false)}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
            >
              Stay
            </button>
            <button
              type="button"
              data-testid="impersonation-exit-confirm-btn"
              onClick={() => void handleConfirm()}
              disabled={exiting}
              className="rounded-md bg-yellow-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-yellow-700 disabled:opacity-60"
            >
              {exiting ? 'Exiting…' : 'Exit'}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
