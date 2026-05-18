import { useEffect, useState } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
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
  const location = useLocation();

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const next = await tenantWizard.getState();
        if (!cancelled) setProgress(next);
      } catch {
        // 401/403/network → leave inert; app may still be usable for CSP admins.
      } finally {
        if (!cancelled) setChecked(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  if (!checked) return <>{children}</>;
  if (!progress) return <>{children}</>;
  if (progress.onboardingState === 'Active') return <>{children}</>;

  // Don't redirect when already on the wizard route to avoid a render loop.
  if (location.pathname.startsWith('/onboarding/tenant')) return <>{children}</>;

  return <Navigate to="/onboarding/tenant" replace />;
}
