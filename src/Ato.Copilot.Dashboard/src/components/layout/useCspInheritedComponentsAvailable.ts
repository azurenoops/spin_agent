import { useEffect, useState } from 'react';
import { isUnavailable, listCspInheritedComponents } from '../../features/csp-inherited-components/api';

/**
 * Feature 048 / US9 / T215 — visibility probe for the
 * `/csp/inherited-components` nav link. Performs a single
 * `GET /api/csp/inherited-components?pageSize=1` probe on mount.
 *
 *   - 200 → MultiTenant deployment with CSP onboarding `Active`. The
 *     link is shown to every authenticated user (FR-104), regardless of
 *     `CSP.Admin` membership. Non-admins simply see only `Published` rows.
 *   - 404 (`SINGLE_TENANT_MODE`) / 503 (`CSP_ONBOARDING_INCOMPLETE`) /
 *     network unreachable → link hidden.
 *
 * Returns `null` while the probe is in flight to avoid a flash of the
 * link during initial page paint.
 */
export function useCspInheritedComponentsAvailable(): boolean | null {
  const [available, setAvailable] = useState<boolean | null>(null);

  useEffect(() => {
    let cancelled = false;
    listCspInheritedComponents({ pageSize: 1 })
      .then((result) => {
        if (cancelled) return;
        setAvailable(!isUnavailable(result));
      })
      .catch(() => {
        if (!cancelled) setAvailable(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return available;
}
