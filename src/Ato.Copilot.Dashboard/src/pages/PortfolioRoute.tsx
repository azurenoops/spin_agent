import type { ReactElement } from 'react';
import PortfolioRiskProfile from './PortfolioRiskProfile';
import CspDashboardPage from '../features/csp-dashboard/CspDashboardPage';
import RouteResolverFallback from '../components/layout/RouteResolverFallback';
import { useCspDashboardAvailable } from '../components/layout/useCspDashboardAvailable';
import { useImpersonationActive } from '../hooks/useImpersonationActive';

/**
 * Scope-aware landing page mounted at `/`.
 *
 * Resolves at render time based on three signals:
 *
 *   1. `useCspDashboardAvailable()` — `true` only when the deployment is
 *      `MultiTenant`, CSP onboarding (US7) is `Active`, and the caller is in
 *      the `CSP.Admin` group. (Probes `GET /api/csp/dashboard/summary`,
 *      mirrored to sessionStorage so subsequent route mounts answer
 *      synchronously — see `useCspDashboardAvailable` for cache details.)
 *   2. `useImpersonationActive()` — `true` while a CSP-Admin has an active
 *      tenant impersonation cookie. Synchronous (sessionStorage-backed),
 *      so we can short-circuit before waiting on the CSP-Admin probe.
 *   3. Default — everyone else lands on the per-tenant
 *      `PortfolioRiskProfile`, which already scopes itself to the active
 *      tenant (home tenant or the impersonated tenant).
 *
 * Resulting matrix:
 *
 *   | Deployment   | CSP-Admin | Impersonating | Page rendered           |
 *   |--------------|-----------|---------------|-------------------------|
 *   | SingleTenant | n/a       | n/a           | PortfolioRiskProfile    |
 *   | MultiTenant  | no        | n/a           | PortfolioRiskProfile    |
 *   | MultiTenant  | yes       | no            | CspDashboardPage        |
 *   | MultiTenant  | yes       | yes           | PortfolioRiskProfile    |
 *
 * **No-flicker contract**: when impersonation is active we render the
 * per-tenant page immediately (no need to wait on the probe —
 * impersonation-on always means per-tenant). When impersonation is off
 * and the probe is in its first-ever in-flight window (`null`), we
 * render `RouteResolverFallback` instead of defaulting to per-tenant
 * — otherwise CSP-Admins flash the per-tenant page for one frame
 * before the resolver swaps in `CspDashboardPage`. After the first
 * probe resolves, `useCspDashboardAvailable()` reads from
 * `sessionStorage` synchronously on every subsequent mount, so the
 * fallback only ever shows once per tab session.
 */
export default function PortfolioRoute(): ReactElement {
  const impersonating = useImpersonationActive();
  const cspAdminAvailable = useCspDashboardAvailable();

  if (impersonating) {
    return <PortfolioRiskProfile />;
  }
  if (cspAdminAvailable === null) {
    return <RouteResolverFallback title="Portfolio" />;
  }
  if (cspAdminAvailable === true) {
    return <CspDashboardPage />;
  }
  return <PortfolioRiskProfile />;
}
