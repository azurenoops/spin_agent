import { useEffect, useRef, useState } from 'react';
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
 * Epic #207 / Task #245: migrated primary detection to BroadcastChannel
 * ('ato-login') so the mechanism is decoupled from MSAL's internal
 * localStorage key naming ('msal.account.keys.*'). The storage-event
 * fallback is retained for iframe / restricted contexts where
 * BroadcastChannel is unavailable.
 *
 * Channel messages:  { type: 'login_complete' }
 *
 * Fallback: when `'BroadcastChannel' in window` is false, falls back to the
 * original storage-event listener on msal.account.keys.* keys.
 */
export function useLoginRaceListener(opts: UseLoginRaceListenerOptions): void {
  const { instance } = useMsal();
  const { onLoginCompletedInAnotherTab } = opts;
  const channelRef = useRef<BroadcastChannel | null>(null);

  useEffect(() => {
    if ('BroadcastChannel' in window) {
      // Primary path — BroadcastChannel
      const channel = new BroadcastChannel('ato-login');
      channelRef.current = channel;

      channel.onmessage = (event: MessageEvent<{ type?: string }>) => {
        if (event.data?.type === 'login_complete') {
          // Double-check MSAL cache to distinguish login from logout cross-tab
          const accounts = instance.getAllAccounts();
          if (accounts.length > 0) {
            onLoginCompletedInAnotherTab();
          }
        }
      };

      return () => {
        channel.close();
        channelRef.current = null;
      };
    } else {
      // Fallback path — localStorage storage events (original implementation)
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
    }
  }, [instance, onLoginCompletedInAnotherTab]);
}
