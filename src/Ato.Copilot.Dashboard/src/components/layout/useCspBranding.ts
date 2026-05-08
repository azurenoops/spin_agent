import { useEffect, useState } from 'react';
import { getCspOnboardingState, isUnavailable } from '../../features/csp-onboarding/api';

export interface CspBranding {
  /** Trimmed display name of the hosting CSP, or null in SingleTenant /
   *  pre-onboarding. */
  displayName: string | null;
  /** Validated logo URL or null. */
  logoUrl: string | null;
  /** True once the probe has completed (success OR explicit unavailability). */
  ready: boolean;
}

const DEFAULT: CspBranding = { displayName: null, logoUrl: null, ready: false };

/**
 * `useCspBranding` — Feature 048 / US7 / T170.
 *
 * Reads `/api/csp/onboarding/state` once on mount and exposes the active
 * CSP's `displayName` + `logoUrl` for header rendering. In `SingleTenant`
 * mode (or for non-CSP-Admin callers, or while the wizard is incomplete),
 * the hook returns `displayName === null` so the caller can fall back to
 * the default "Security Posture Intelligence Navigator" branding.
 *
 * Network failures are silently treated as "no CSP branding". Never throws.
 */
export function useCspBranding(): CspBranding {
  const [state, setState] = useState<CspBranding>(DEFAULT);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const next = await getCspOnboardingState();
        if (cancelled) return;
        if (isUnavailable(next)) {
          setState({ displayName: null, logoUrl: null, ready: true });
          return;
        }
        // Only show CSP branding once onboarding is finalized.
        if (next.onboardingState !== 'Active') {
          setState({ displayName: null, logoUrl: null, ready: true });
          return;
        }
        setState({
          displayName: next.identity?.displayName?.trim() || null,
          logoUrl: next.identity?.logoUrl?.trim() || null,
          ready: true,
        });
      } catch {
        if (!cancelled) setState({ displayName: null, logoUrl: null, ready: true });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}
