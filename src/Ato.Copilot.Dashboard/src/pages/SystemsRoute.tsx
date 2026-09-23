import { type ReactElement } from 'react';
import PortfolioDashboard from './PortfolioDashboard';
import CspSystemsPage from '../features/csp-dashboard/CspSystemsPage';
import RouteResolverFallback from '../components/layout/RouteResolverFallback';
import { useCspDashboardAvailable } from '../components/layout/useCspDashboardAvailable';
import { useImpersonationActive } from '../hooks/useImpersonationActive';
import { useWorkspaceTarget } from '../features/workspaces/workspaceNavigation';

/**
 * Scope-aware resolver mounted at `/systems`. Mirrors `PortfolioRoute` for
 * explicit workspace context. The compatibility matrix below applies only
 * when no explicit workspace is mounted.
 * `/`: when the caller is a CSP-Admin in `MultiTenant` mode and is NOT
 * currently impersonating, render the cross-tenant `CspSystemsPage`; in
 * every other case render the per-tenant `PortfolioDashboard` (which
 * already scopes itself to the active tenant — home tenant for non-admins,
 * the impersonated tenant when a CSP-Admin is drilling in).
 *
 * Resulting matrix:
 *
 *   | Deployment   | CSP-Admin | Impersonating | Page rendered      |
 *   |--------------|-----------|---------------|--------------------|
 *   | SingleTenant | n/a       | n/a           | PortfolioDashboard |
 *   | MultiTenant  | no        | n/a           | PortfolioDashboard |
 *   | MultiTenant  | yes       | no            | CspSystemsPage     |
 *   | MultiTenant  | yes       | yes           | PortfolioDashboard |
 *
 * **No-flicker contract** (see `PortfolioRoute` for the full write-up):
 * impersonation short-circuits to the per-tenant page; otherwise we wait
 * for the first-ever probe via `RouteResolverFallback` rather than
 * default-rendering per-tenant and then swapping. After the first probe
 * the sessionStorage cache makes subsequent navigations instant.
 */
export default function SystemsRoute(): ReactElement {
  const workspace = useWorkspaceTarget();
  const impersonating = useImpersonationActive();
  const cspAdminAvailable = useCspDashboardAvailable(!workspace);

  if (workspace?.kind === 'csp') return <CspSystemsPage />;
  if (workspace?.kind === 'organization') return <PortfolioDashboard />;

  if (impersonating) {
    return <PortfolioDashboard />;
  }
  if (cspAdminAvailable === null) {
    return <RouteResolverFallback title="Systems" />;
  }
  if (cspAdminAvailable === true) {
    return <CspSystemsPage />;
  }
  return <PortfolioDashboard />;
}
