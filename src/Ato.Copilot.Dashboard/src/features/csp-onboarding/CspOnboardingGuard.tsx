import { useEffect, useState, type ReactElement, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { getCspOnboardingState, isUnavailable } from './api';

interface CspOnboardingGuardProps {
  children: ReactNode;
}

/**
 * `CspOnboardingGuard` — Feature 048 / US7 / T169.
 *
 * Wraps the entire dashboard. On every route load, calls
 * `GET /api/csp/onboarding/state`. If the deployment is `MultiTenant` and
 * `OnboardingState !== 'Active'`, the guard redirects to `/onboarding/csp`.
 *
 * Behavior:
 *  - `SingleTenant` mode (404) → guard is inert, renders children.
 *  - Non-CSP-Admin caller (401/403) → guard is inert (the API gate already
 *    blocks them and there's nothing for them to do here).
 *  - Already on `/onboarding/csp` → renders children (don't redirect-loop).
 *  - State `Active` → renders children.
 *  - State `Pending` / `InWizard` → redirects to `/onboarding/csp`.
 *
 * Network failures are treated as "render children" so a transient outage
 * doesn't lock users out of the dashboard.
 */
export default function CspOnboardingGuard({ children }: CspOnboardingGuardProps): ReactElement {
  const navigate = useNavigate();
  const location = useLocation();
  const [checked, setChecked] = useState(false);
  const [redirecting, setRedirecting] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const next = await getCspOnboardingState();
        if (cancelled) return;
        if (isUnavailable(next)) {
          // SingleTenant or non-CSP-Admin — no redirect.
          return;
        }
        if (next.onboardingState !== 'Active' && !location.pathname.startsWith('/onboarding/csp')) {
          setRedirecting(true);
          navigate('/onboarding/csp', { replace: true });
        }
      } finally {
        if (!cancelled) setChecked(true);
      }
    })();
    return () => {
      cancelled = true;
    };
    // We intentionally only re-check on initial mount. Per-route re-checks
    // would 503 the dashboard during the user's first onboarding click —
    // the wizard itself drives state transitions, not the guard.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // While the very first state probe is in flight, show a thin top-of-page
  // spinner instead of flashing the dashboard chrome.
  if (!checked && !redirecting) {
    return (
      <div className="flex h-screen items-center justify-center">
        <div className="text-sm text-gray-500">Checking CSP onboarding…</div>
      </div>
    );
  }

  return <>{children}</>;
}
