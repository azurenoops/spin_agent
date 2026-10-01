import { useEffect, useState } from 'react';
import { Navigate, useLocation } from '../../workspaces/workspaceNavigation';
import { tenantWizard, type TenantOnboardingProgress } from './api';

/**
 * Feature 048 / US4 — Tenant onboarding route guard.
 *
 * Wraps the application's main route tree. On mount it calls
 * <c>GET /api/onboarding/tenant/state</c>:
 *
 *  - If the server responds with the {@link TenantOnboardingProgress}
 *    envelope and <c>onboardingState !== 'Active'</c>, the guard
 *    renders a <c>Navigate</c> to <c>/onboarding/tenant</c> (FR-054).
 *  - On 401/403 (CSP-Admin without an effective tenant, simulated-role
 *    bypass, etc.) the guard becomes inert so the underlying app can
 *    still render.
 *  - While the request is in flight, children render unchanged so the
 *    dashboard does not flash a blank page.
 *
 * The guard intentionally does *not* poll — once the wizard completes
 * and the user lands back on the app, a subsequent app-load picks up
 * <c>onboardingState === 'Active'</c> via this same hook.
 */
export default function TenantOnboardingGuard({ children }: { children: React.ReactNode }) {
  const [progress, setProgress] = useState<TenantOnboardingProgress | null>(null);
  const [checked, setChecked] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const location = useLocation();

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();
    (async () => {
      try {
        const next = await tenantWizard.getState(controller.signal);
        if (!cancelled) setProgress(next);
      } catch (reason) {
        if (!cancelled) setError((reason as Error).message);
      } finally {
        if (!cancelled) setChecked(true);
      }
    })();
    return () => {
      cancelled = true;
      controller.abort();
    };
  }, []);

  if (!checked) return <>{children}</>;
  if (!progress) return <>{error && <p role="status" className="bg-amber-50 p-3 text-sm text-amber-900">Tenant activation status is unavailable: {error}</p>}{children}</>;
  if (progress.onboardingState === 'Active') return <>{children}</>;

  // Don't redirect when already on the wizard route to avoid a render loop.
  if (location.pathname === '/onboarding' || location.pathname.startsWith('/onboarding/tenant')
    || location.pathname === '/setup' || location.pathname === '/setup/resume') return <>{children}</>;

  return <Navigate to="/onboarding/tenant" replace />;
}
