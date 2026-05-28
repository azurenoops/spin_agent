import { useEffect, useRef, useState } from 'react';
import axios from 'axios';
import { getMsalInstance } from './msalInstance';
import { purgeUnsavedChanges } from './useIdleFormStateBackup';

// TODO(US9): full menu — name, persona, home tenant, active PIM role,
// settings link, etc. This stub only fulfils US2's "explicit sign-out"
// requirement so the rest of Phase 4 can ship; US9 (T140) replaces it
// with the full account dropdown.

/**
 * Feature 051 T062 [US2] — minimal header dropdown stub. Only renders a
 * "Sign Out" item that:
 *   1. Calls `POST /api/auth/signout` (default reason "manual" ⇒ server
 *      writes a `SignOut` audit row).
 *   2. Calls `msalInstance.logoutRedirect(...)` with a `?reason=signed_out`
 *      query so the login page can render the correct post-sign-out copy.
 *
 * IMPORTANT (FR-008): on explicit sign-out we MUST purge any
 * idle-saved form snapshots so they do not surface on the next
 * sign-in. Idle sign-out (handled in `useIdleTimer`) MUST NOT call
 * purge — that is the whole point of the FR-008 "restore" flow.
 */
export interface AccountMenuProps {
  /** Authenticated user's oid; required so we can scope the unsaved-changes purge. */
  oid?: string;
  /** Optional display name for the trigger. Falls back to "Account". */
  displayName?: string;
}

export default function AccountMenu({ oid, displayName }: AccountMenuProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  // Close the menu on outside click.
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

  const handleSignOut = async () => {
    setOpen(false);

    // FR-008 — explicit sign-out clears unsaved snapshots so the next
    // session does NOT see a "Restore" prompt. Idle sign-out (handled
    // in useIdleTimer) intentionally does NOT call purge — that path
    // preserves the snapshot for restore-on-next-login.
    if (oid) {
      try {
        purgeUnsavedChanges(oid);
      } catch {
        // Best-effort — failure here must not block sign-out.
      }
    }

    try {
      await axios.post('/api/auth/signout');
    } catch {
      // Best-effort — proceed with logoutRedirect even if the server
      // call failed so we don't strand the user in an authenticated
      // shell.
    }

    try {
      const msal = getMsalInstance();
      await msal.logoutRedirect({
        postLogoutRedirectUri: '/login?reason=signed_out',
      });
    } catch {
      // No MSAL instance — fall back to a hard navigation.
      window.location.href = '/login?reason=signed_out';
    }
  };

  const label = displayName?.trim() || 'Account';
  const initials = label
    .split(/\s+/)
    .map((p) => p[0])
    .join('')
    .slice(0, 2)
    .toUpperCase();

  return (
    <div ref={containerRef} className="relative ml-2">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={open}
        title={label}
        className="flex h-8 w-8 items-center justify-center rounded-full bg-indigo-600 text-sm font-medium text-white hover:ring-2 hover:ring-indigo-300 focus:outline-none focus:ring-2 focus:ring-indigo-500"
      >
        {initials}
      </button>
      {open && (
        <div
          role="menu"
          className="absolute right-0 z-40 mt-2 w-48 rounded-md border border-gray-200 bg-white py-1 shadow-lg"
        >
          <div className="px-4 py-2 text-sm text-gray-700">
            <div className="font-medium text-gray-900">{label}</div>
            {oid && <div className="text-xs text-gray-500 truncate">{oid}</div>}
          </div>
          <div className="my-1 border-t border-gray-100" />
          <button
            type="button"
            role="menuitem"
            data-testid="account-menu-sign-out"
            onClick={() => {
              void handleSignOut();
            }}
            className="block w-full px-4 py-2 text-left text-sm text-gray-700 hover:bg-gray-50"
          >
            Sign Out
          </button>
        </div>
      )}
    </div>
  );
}
