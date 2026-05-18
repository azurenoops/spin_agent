import { useEffect, useState } from 'react';
import { getCspDashboardSummary, isUnavailable } from '../../features/csp-dashboard/api';

/**
 * Visibility probe for the cross-tenant CSP operational dashboard
 * (Feature 048 / US8). Performs a single `GET /api/csp/dashboard/summary`
 * probe on mount.
 *
 *   - 200 → caller is `CSP.Admin`, deployment is `MultiTenant`, and CSP
 *     onboarding is `Active`. The cross-tenant view is reachable.
 *   - 404 (`SINGLE_TENANT_MODE`) / 401|403 (`NOT_CSP_ADMIN`) /
 *     503 (`CSP_ONBOARDING_INCOMPLETE`) / network unreachable → not reachable.
 *
 * **sessionStorage cache** — the result is mirrored to
 * `sessionStorage[CACHE_KEY]` so subsequent route mounts in the same tab
 * bootstrap synchronously and skip the in-flight `null` window. This is
 * what eliminates the per-route-nav flicker where the per-tenant page
 * renders for one frame before the resolver swaps to the CSP page (see
 * `PortfolioRoute` / `SystemsRoute` / `ComponentsRoute` /
 * `CapabilitiesRoute` / `ControlsRoute`). The cache survives a tab
 * refresh during the same session but is dropped when the tab closes —
 * exactly the right TTL for an auth-derived flag.
 *
 * Returns `null` only while the FIRST probe (in this tab) is in flight.
 * After that, the cached value is the synchronous initial state for
 * every subsequent mount.
 *
 * Used by `PortfolioRoute` (to pick `CspDashboardPage` vs
 * `PortfolioRiskProfile` at `/`) and historically by the top-nav
 * "CSP Dashboard" link, which has since been retired in favor of the
 * scope-aware `/` resolver.
 */
const CACHE_KEY = 'ato:csp-admin-available-v1';

function readCached(): boolean | null {
  if (typeof window === 'undefined') return null;
  try {
    const raw = window.sessionStorage.getItem(CACHE_KEY);
    if (raw === 'true') return true;
    if (raw === 'false') return false;
    return null;
  } catch {
    return null;
  }
}

function writeCached(value: boolean): void {
  if (typeof window === 'undefined') return;
  try {
    window.sessionStorage.setItem(CACHE_KEY, value ? 'true' : 'false');
  } catch {
    // sessionStorage may be disabled (private mode, quota, etc.). Failing
    // to cache only costs us a re-probe on the next mount; never throw.
  }
}

export function useCspDashboardAvailable(): boolean | null {
  // Synchronous bootstrap from sessionStorage so the first paint of a
  // remounted route component already has the correct boolean — no
  // null-window, no flash of the wrong page.
  const [available, setAvailable] = useState<boolean | null>(() => readCached());

  useEffect(() => {
    // If the cache already answered us, skip the network round-trip.
    // We trust the cache for the lifetime of the tab; impersonation
    // toggles do not change CSP-Admin membership, so no invalidation
    // hook is needed here.
    if (available !== null) return;

    let cancelled = false;
    getCspDashboardSummary()
      .then((result) => {
        if (cancelled) return;
        const next = !isUnavailable(result);
        writeCached(next);
        setAvailable(next);
      })
      .catch(() => {
        if (cancelled) return;
        writeCached(false);
        setAvailable(false);
      });
    return () => {
      cancelled = true;
    };
  }, [available]);

  return available;
}
