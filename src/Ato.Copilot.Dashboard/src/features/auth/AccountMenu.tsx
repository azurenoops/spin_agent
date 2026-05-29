import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import axios from 'axios';
import { getMsalInstance } from './msalInstance';
import { purgeUnsavedChanges } from './useIdleFormStateBackup';
import { useMe } from './useMe';
import type { PimRoleAssignment } from './types';

/**
 * Feature 051 T140 [US9] — header dropdown. Replaces the Phase-4 T062
 * sign-out stub with the full account menu (displayName, persona, home
 * tenant, active PIM roles, sign-out) per spec § US9 + FR-030 / FR-031.
 *
 * Sign-out (Phase 4 invariant — DO NOT REGRESS):
 *   1. purgeUnsavedChanges(oid)              — FR-008, sync
 *   2. POST /api/auth/signout                — server audit row
 *   3. msalInstance.logoutRedirect({...?reason=signed_out})
 *
 * Identity binding:
 *   useMe() is the source of truth (persona, homeTenant, pimRoles).
 *   The `oid` + `displayName` props remain as MSAL-bootstrap fallbacks
 *   so the header trigger renders sensibly while /me is still in flight.
 *
 * Accessibility (FR-031 / analysis C8):
 *   - Trigger has `aria-label="Account menu"`, `aria-haspopup="menu"`,
 *     `aria-expanded` toggled on open/close.
 *   - Menu container has `role="menu"`; interactive children have
 *     `role="menuitem"`.
 *   - Esc closes the menu and returns focus to the trigger.
 *   - Focus is trapped: Tab from the last menuitem wraps to the first;
 *     Shift+Tab from the first wraps to the last.
 *   - A polite live region (`role="status" aria-live="polite"`) outside
 *     the menu announces the active PIM role's expiry so screen readers
 *     pick up the initial state and 1-minute countdown updates.
 *
 * Countdowns re-render every 60 seconds — the surface shows minutes/hours,
 * not seconds, so a 1-second ticker would be wasted work.
 */
export interface AccountMenuProps {
  /** Authenticated user's oid; required so we can scope the unsaved-changes purge. */
  oid?: string;
  /** Optional display name for the trigger. Falls back to "Account". */
  displayName?: string;
}

/** Format a positive ms-until-expiry into "expires in Nm" or "expires in Hh Mm". */
function formatCountdown(msUntil: number): string {
  if (msUntil <= 0) return 'expired';
  // Round to the nearest minute; clamp to ≥1m so any sub-minute remainder
  // still surfaces as "1m" rather than the misleading "0m".
  const totalMin = Math.max(1, Math.round(msUntil / 60_000));
  if (totalMin < 60) return `expires in ${totalMin}m`;
  const h = Math.floor(totalMin / 60);
  const m = totalMin % 60;
  return m === 0 ? `expires in ${h}h` : `expires in ${h}h ${m}m`;
}

function filterActiveRoles(
  roles: PimRoleAssignment[],
  nowMs: number,
): PimRoleAssignment[] {
  return roles.filter((r) => {
    const t = Date.parse(r.expiresAt);
    return Number.isFinite(t) && t > nowMs;
  });
}

export default function AccountMenu({ oid: oidProp, displayName: displayNameProp }: AccountMenuProps) {
  const { data: me } = useMe();
  const [open, setOpen] = useState(false);
  const [nowMs, setNowMs] = useState<number>(() => Date.now());

  const containerRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  // Re-render every 60s so countdowns advance and expired rows drop out
  // without needing a refetch of /me. We deliberately do NOT tick every
  // second — the menu surfaces minutes/hours only.
  useEffect(() => {
    const id = window.setInterval(() => setNowMs(Date.now()), 60_000);
    return () => window.clearInterval(id);
  }, []);

  // Close on outside click.
  useEffect(() => {
    if (!open) return;
    const onClick = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', onClick);
    return () => document.removeEventListener('mousedown', onClick);
  }, [open]);

  // Auto-focus the first menuitem when the menu opens so keyboard users
  // can immediately drive Tab / Shift+Tab through the trap.
  useEffect(() => {
    if (!open || !menuRef.current) return;
    const first = menuRef.current.querySelector<HTMLElement>('[role="menuitem"]');
    first?.focus();
  }, [open]);

  // Identity — useMe is preferred; the MSAL bootstrap props fill in
  // before /me resolves and keep the trigger label sane.
  const oid = me?.oid ?? oidProp;
  const displayName = (me?.displayName ?? displayNameProp ?? 'Account').trim() || 'Account';
  const persona = me?.persona ?? null;
  const homeTenantName = me?.homeTenant?.displayName ?? null;

  const activeRoles = useMemo(
    () => (me?.pimRoles ? filterActiveRoles(me.pimRoles, nowMs) : []),
    [me?.pimRoles, nowMs],
  );

  // Active-role announcement for the live region. Single string covers
  // the most recently-elevated role; downstream phases may extend this
  // to multiple roles if needed.
  const liveText = useMemo(() => {
    if (activeRoles.length === 0) return '';
    const r = activeRoles[0]!;
    return `${r.name} ${formatCountdown(Date.parse(r.expiresAt) - nowMs)}`;
  }, [activeRoles, nowMs]);

  const handleSignOut = useCallback(async () => {
    setOpen(false);

    // FR-008 — explicit sign-out purges unsaved snapshots so the next
    // sign-in does NOT see a Restore prompt. Idle sign-out (handled in
    // useIdleTimer) intentionally does NOT call purge — that path
    // preserves the snapshot for restore-on-next-login.
    if (oid) {
      try {
        purgeUnsavedChanges(oid);
      } catch {
        // Best-effort — purge failure must not block sign-out.
      }
    }

    try {
      await axios.post('/api/auth/signout');
    } catch {
      // Best-effort — proceed with logoutRedirect even if the server
      // call failed so we don't strand the user in an authenticated shell.
    }

    // Dev-simulation sessions (US7) have NO MSAL account — they're a
    // server-side cookie cleared by the POST above. Calling
    // logoutRedirect() in that case would bounce the browser to
    // login.microsoftonline.com/.../oauth2/v2.0/logout (because the SPA
    // app registration still has a postLogoutRedirectUri) and "sign out"
    // the user's real Entra session, which is not what a simulated
    // sign-out should do. So: only invoke MSAL logout when MSAL knows
    // about an account.
    try {
      const msal = getMsalInstance();
      const hasMsalAccount = msal.getAllAccounts().length > 0;
      if (hasMsalAccount) {
        await msal.logoutRedirect({
          postLogoutRedirectUri: '/login?reason=signed_out',
        });
      } else {
        window.location.href = '/login?reason=signed_out';
      }
    } catch {
      // No MSAL instance — fall back to a hard navigation.
      window.location.href = '/login?reason=signed_out';
    }
  }, [oid]);

  // FR-031 focus trap + Escape handler. Implemented inline (no new
  // dependency per the Phase-12 constraints).
  const handleMenuKeyDown = useCallback((e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Escape') {
      e.preventDefault();
      setOpen(false);
      triggerRef.current?.focus();
      return;
    }
    if (e.key !== 'Tab') return;

    const menu = menuRef.current;
    if (!menu) return;
    const items = menu.querySelectorAll<HTMLElement>('[role="menuitem"]');
    if (items.length === 0) return;
    const first = items[0]!;
    const last = items[items.length - 1]!;
    const active = document.activeElement as HTMLElement | null;

    if (e.shiftKey && active === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && active === last) {
      e.preventDefault();
      first.focus();
    }
  }, []);

  const initials = displayName
    .split(/\s+/)
    .map((p) => p[0])
    .filter(Boolean)
    .join('')
    .slice(0, 2)
    .toUpperCase();

  return (
    <div ref={containerRef} className="relative ml-2">
      {/* Live region — rendered OUTSIDE the menu so screen readers catch
          the initial announcement on mount and subsequent 60-second
          countdown updates regardless of whether the menu is open.
          `aria-live="polite"` defers the announcement until the SR is
          idle so it does not interrupt page reading. */}
      <div role="status" aria-live="polite" className="sr-only">
        {liveText}
      </div>

      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label="Account menu"
        title={displayName}
        className="flex h-8 w-8 items-center justify-center rounded-full bg-indigo-600 text-sm font-medium text-white hover:ring-2 hover:ring-indigo-300 focus:outline-none focus:ring-2 focus:ring-indigo-500"
      >
        {initials || 'A'}
      </button>

      {open && (
        <div
          ref={menuRef}
          role="menu"
          aria-label="Account"
          onKeyDown={handleMenuKeyDown}
          className="absolute right-0 z-40 mt-2 w-72 rounded-md border border-gray-200 bg-white py-1 shadow-lg"
        >
          {/* Identity header — informational; not a menuitem. */}
          <div className="px-4 py-3 text-sm text-gray-700">
            <div className="font-semibold text-gray-900 truncate">{displayName}</div>
            {persona && (
              <div className="mt-0.5 text-xs text-gray-500">{persona}</div>
            )}
            {homeTenantName && (
              <div className="mt-1 text-xs text-gray-500">
                <span className="text-gray-400">Home tenant: </span>
                <span className="text-gray-700">{homeTenantName}</span>
              </div>
            )}
            {oid && (
              <div
                className="mt-1 text-[10px] text-gray-400 truncate"
                title={oid}
              >
                {oid}
              </div>
            )}
          </div>

          {activeRoles.length > 0 && (
            <>
              <div className="my-1 border-t border-gray-100" role="separator" />
              <div className="px-4 pb-1 pt-2 text-[10px] font-semibold uppercase tracking-wide text-gray-400">
                Active PIM roles
              </div>
              {/* WCAG: role="menuitem" children require a role="menu" /
                  role="group" parent (aria-required-parent). Using
                  <ul>/<li> here would ALSO violate the "list must only
                  contain <li>" rule (axe-core/list) because <li role=...>
                  breaks the implicit list semantics. A nested role="group"
                  satisfies both rules and keeps the visual layout. */}
              <div role="group" aria-label="Active PIM roles" className="px-2 pb-1">
                {activeRoles.map((r) => {
                  const ms = Date.parse(r.expiresAt) - nowMs;
                  return (
                    <div
                      key={r.name}
                      role="menuitem"
                      tabIndex={0}
                      className="flex items-center justify-between rounded px-2 py-1 text-xs text-gray-700 focus:bg-gray-50 focus:outline-none"
                    >
                      <span className="truncate">{r.name}</span>
                      <span className="ml-2 flex-shrink-0 text-gray-500">
                        {formatCountdown(ms)}
                      </span>
                    </div>
                  );
                })}
              </div>
            </>
          )}

          <div className="my-1 border-t border-gray-100" role="separator" />
          <button
            type="button"
            role="menuitem"
            data-testid="account-menu-sign-out"
            onClick={() => {
              void handleSignOut();
            }}
            className="block w-full px-4 py-2 text-left text-sm text-gray-700 hover:bg-gray-50 focus:bg-gray-50 focus:outline-none"
          >
            Sign Out
          </button>
        </div>
      )}
    </div>
  );
}
