import { type ReactElement } from 'react';
import CapabilityLibrary from './CapabilityLibrary';
import CspCapabilitiesPage from '../features/csp-inherited-components/CspCapabilitiesPage';
import RouteResolverFallback from '../components/layout/RouteResolverFallback';
import { useCspDashboardAvailable } from '../components/layout/useCspDashboardAvailable';
import { useImpersonationActive } from '../hooks/useImpersonationActive';
import { useWorkspaceTarget } from '../features/workspaces/workspaceNavigation';
import { Navigate, useLocation } from '../features/workspaces/workspaceNavigation';

/**
 * Scope-aware resolver mounted at `/capabilities`. Mirrors `ComponentsRoute`.
 * Explicit workspace context takes precedence over the legacy rules below.
 *
 * CSP-Admin in `MultiTenant` mode and not impersonating ⇒ flat
 * `CspCapabilitiesPage` showing canonical CSP-inherited capabilities sourced
 * from CSP-uploaded ATO documents. Every other case ⇒ the per-tenant
 * `CapabilityLibrary` (organization-wide capabilities authored / mapped
 * inside the tenant).
 *
 * **No-flicker contract** (see `PortfolioRoute` for the full write-up):
 * impersonation short-circuits to per-tenant; otherwise we render the
 * `RouteResolverFallback` while the first-ever CSP-Admin probe is in
 * flight rather than default-rendering `CapabilityLibrary` and then
 * swapping. After the first probe, sessionStorage answers synchronously.
 */
export default function CapabilitiesRoute(): ReactElement {
  const workspace = useWorkspaceTarget();
  const location = useLocation();
  const impersonating = useImpersonationActive();
  const cspAdminAvailable = useCspDashboardAvailable(!workspace);

  if (workspace) {
    const search = new URLSearchParams(location.search);
    if (!search.has('grouping')) search.set('grouping', 'capability');
    return <Navigate replace to={{
      pathname: '/security-capabilities',
      search: search.toString() ? `?${search}` : '',
      hash: location.hash,
    }} />;
  }

  if (impersonating) {
    return <CapabilityLibrary />;
  }
  if (cspAdminAvailable === null) {
    return <RouteResolverFallback title="Capabilities" />;
  }
  if (cspAdminAvailable === true) {
    return <CspCapabilitiesPage />;
  }
  return <CapabilityLibrary />;
}
