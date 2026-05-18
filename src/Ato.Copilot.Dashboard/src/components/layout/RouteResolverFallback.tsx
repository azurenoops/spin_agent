import type { ReactElement } from 'react';
import PageLayout from './PageLayout';

/**
 * Neutral loading shell rendered by the scope-aware route resolvers
 * (`PortfolioRoute`, `SystemsRoute`, `ComponentsRoute`,
 * `CapabilitiesRoute`, `ControlsRoute`) while the first-ever
 * `useCspDashboardAvailable()` probe in this tab session is in flight.
 *
 * Why this exists: without it, the resolvers default to the per-tenant
 * page during the `null` window and then swap to the CSP page once the
 * probe resolves to `true` — a visible flicker for CSP-Admins. After
 * the first probe completes, `useCspDashboardAvailable()` mirrors its
 * result into `sessionStorage`, so every subsequent route mount in the
 * same tab bootstraps synchronously and never sees this shell.
 *
 * The shell intentionally renders inside `PageLayout` so the chrome
 * (header, nav, impersonation banner, side panel) stays stable; only
 * the main content shows the loading hint. This avoids a layout shift
 * when the resolved page mounts.
 */
export default function RouteResolverFallback({
  title,
}: {
  title: string;
}): ReactElement {
  return (
    <PageLayout title={title}>
      <div
        className="flex h-32 items-center justify-center text-sm text-gray-500"
        role="status"
        aria-live="polite"
        data-testid="route-resolver-fallback"
      >
        Loading{title ? ` ${title.toLowerCase()}` : ''}…
      </div>
    </PageLayout>
  );
}
