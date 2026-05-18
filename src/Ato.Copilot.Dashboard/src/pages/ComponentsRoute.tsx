import { type ReactElement } from 'react';
import ComponentLibrary from './ComponentLibrary';
import CspInheritedComponentsPage from '../features/csp-inherited-components/CspInheritedComponentsPage';
import RouteResolverFallback from '../components/layout/RouteResolverFallback';
import { useCspDashboardAvailable } from '../components/layout/useCspDashboardAvailable';
import { useImpersonationActive } from '../hooks/useImpersonationActive';

/**
 * Scope-aware resolver mounted at `/components`. Mirrors `SystemsRoute` /
 * `PortfolioRoute`.
 *
 *   | Deployment   | CSP-Admin | Impersonating | Page rendered                |
 *   |--------------|-----------|---------------|------------------------------|
 *   | SingleTenant | n/a       | n/a           | ComponentLibrary (org-scoped)|
 *   | MultiTenant  | no        | n/a           | ComponentLibrary             |
 *   | MultiTenant  | yes       | no            | CspInheritedComponentsPage   |
 *   | MultiTenant  | yes       | yes           | ComponentLibrary             |
 *
 * The standalone `/csp/inherited-components` route stays as a permalink for
 * existing bookmarks, but the top-nav "CSP Inherited" link is folded into
 * `/components` per the user's directive — at CSP scope, `/components` IS
 * the CSP-inherited library.
 *
 * **No-flicker contract** (see `PortfolioRoute` for the full write-up):
 * impersonation short-circuits; otherwise we render the
 * `RouteResolverFallback` for the first-ever probe rather than
 * default-rendering `ComponentLibrary` and then swapping.
 */
export default function ComponentsRoute(): ReactElement {
  const impersonating = useImpersonationActive();
  const cspAdminAvailable = useCspDashboardAvailable();

  if (impersonating) {
    return <ComponentLibrary />;
  }
  if (cspAdminAvailable === null) {
    return <RouteResolverFallback title="Components" />;
  }
  if (cspAdminAvailable === true) {
    return <CspInheritedComponentsPage />;
  }
  return <ComponentLibrary />;
}
