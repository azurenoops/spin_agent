import { useEffect } from 'react';
import { useMsal } from '@azure/msal-react';

export interface UseLoginRaceListenerOptions {
  /**
   * Called when a SIBLING tab finishes a sign-in. The callback typically
   * navigates the waiting tab forward to its deep link without forcing
   * the user to click "Sign in" twice.
   */
  onLoginCompletedInAnotherTab: () => void;
}

/**
 * Feature 051 T053b [US1] — listen for cross-tab MSAL sign-in completion.
 *
 * MSAL stores its accounts in `localStorage` (configured in
 * `msalConfig.ts`), so when a sibling tab completes sign-in the browser
 * fires a `storage` event with `key === 'msal.account.keys.0'` (or a
 * similar `msal.account.keys.*`). We detect that, double-check that
 * `getAllAccounts()` now returns a non-empty list (a logout fires the
 * same key but with empty accounts), and surface the event so the
 * waiting tab can advance per FR-016's deep-link contract.
 *
 * Per `contracts/frontend-types.md § 4.2` and `research.md § R11`.
 */
export function useLoginRaceListener(opts: UseLoginRaceListenerOptions): void {
  const { instance } = useMsal();
  const { onLoginCompletedInAnotherTab } = opts;

  useEffect(() => {
    const handler = (e: StorageEvent) => {
      if (!e.key) return;
      if (!e.key.startsWith('msal.account.keys')) return;
      const accounts = instance.getAllAccounts();
      if (accounts.length > 0) {
        onLoginCompletedInAnotherTab();
      }
    };
    window.addEventListener('storage', handler);
    return () => {
      window.removeEventListener('storage', handler);
    };
  }, [instance, onLoginCompletedInAnotherTab]);
}
