import { type ReactElement } from 'react';
import CapabilityLibrary from './CapabilityLibrary';
import CspCapabilitiesPage from '../features/csp-inherited-components/CspCapabilitiesPage';
import RouteResolverFallback from '../components/layout/RouteResolverFallback';
import { useCspDashboardAvailable } from '../components/layout/useCspDashboardAvailable';
import { useImpersonationActive } from '../hooks/useImpersonationActive';

/**
 * Scope-aware resolver mounted at `/capabilities`. Mirrors `ComponentsRoute`.
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
  const impersonating = useImpersonationActive();
  const cspAdminAvailable = useCspDashboardAvailable();

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
