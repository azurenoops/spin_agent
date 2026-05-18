import { type ReactElement } from 'react';
import ControlCatalog from './ControlCatalog';
import RouteResolverFallback from '../components/layout/RouteResolverFallback';
import { useCspDashboardAvailable } from '../components/layout/useCspDashboardAvailable';
import { useImpersonationActive } from '../hooks/useImpersonationActive';

/**
 * Scope-aware resolver mounted at `/controls`. Mirrors `SystemsRoute` /
 * `ComponentsRoute` / `CapabilitiesRoute`.
 *
 * The NIST 800-53 control catalog is identical at both CSP and Org scopes
 * (the framework is global), so both branches render the same
 * `ControlCatalog`. The catalog itself reads its `scope` prop and
 * adjusts its `PageHero` (eyebrow "Controls · CSP scope" and
 * CSP-flavored description) so the user knows they are viewing it from
 * the cross-tenant scope. When impersonating, the prop defaults to
 * `org` and the page reads as the per-tenant view — no wrapper chrome
 * stacked on top.
 *
 * **No-flicker contract** (see `PortfolioRoute` for the full write-up):
 * impersonation short-circuits to the bare per-tenant catalog;
 * otherwise we render the `RouteResolverFallback` while the first-ever
 * CSP-Admin probe is in flight, then sessionStorage makes every
 * subsequent navigation render synchronously.
 *
 * Future enhancement (TODO): at CSP scope, augment the table with a
 * "CSP-inherited capabilities mapped" column sourced from
 * `CspInheritedCapability.MappedNistControlIds`. Tracked separately.
 */
export default function ControlsRoute(): ReactElement {
  const impersonating = useImpersonationActive();
  const cspAdminAvailable = useCspDashboardAvailable();

  if (impersonating) {
    return <ControlCatalog />;
  }
  if (cspAdminAvailable === null) {
    return <RouteResolverFallback title="Controls" />;
  }
  if (cspAdminAvailable === true) {
    return <ControlCatalog scope="csp" />;
  }
  return <ControlCatalog />;
}
