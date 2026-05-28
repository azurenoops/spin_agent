import { useCallback, useEffect, useState } from 'react';
import axios from 'axios';
import type { MeResponse } from './types';

/**
 * Feature 051 T072 [US3] / T135 [US8] / T138 [US9] — minimal `useMe`
 * hook with an explicit `refetch()` (Phase 11.3) and an automatic
 * `'ato:tenant-changed'` refetch (Phase 12 / FR-030).
 *
 * A heavier React-Query-backed hook will replace this when later phases
 * need session-wide identity state, but the current single-fetch + naive
 * state model is enough for the tenant picker, impersonation banner,
 * and account menu.
 */
export interface UseMeResult {
  data: MeResponse | null;
  isLoading: boolean;
  error: Error | null;
  refetch: () => void;
}

export function useMe(): UseMeResult {
  const [data, setData] = useState<MeResponse | null>(null);
  const [isLoading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<Error | null>(null);
  // Bump to re-run the fetch effect; consumers call refetch() from
  // event handlers (e.g. the impersonation banner's Exit handler).
  const [tick, setTick] = useState(0);

  const refetch = useCallback(() => setTick((t) => t + 1), []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    void (async () => {
      try {
        const resp = await axios.get('/api/auth/me');
        if (cancelled) return;
        // Envelope: { status, data, metadata }
        const body = resp.data as { status?: string; data?: MeResponse };
        if (body?.status === 'success' && body.data) {
          setData(body.data);
          setError(null);
        } else {
          setError(new Error('Unexpected /api/auth/me envelope'));
        }
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err : new Error(String(err)));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [tick]);

  // FR-030 — when the tenant picker dispatches `'ato:tenant-changed'`,
  // every live `useMe()` consumer (impersonation banner, account menu,
  // tenant picker page) must re-pull `/me` so the SPA's view of the
  // effective tenant stays coherent. The `ImpersonationBanner` ALSO
  // listens to this event for legacy reasons (Phase 11.3) — the double
  // refetch is harmless (React batches the two `setTick` calls in the
  // same tick) and we are not regressing that wiring here.
  useEffect(() => {
    const handler = () => refetch();
    window.addEventListener('ato:tenant-changed', handler);
    return () => window.removeEventListener('ato:tenant-changed', handler);
  }, [refetch]);

  return { data, isLoading, error, refetch };
}
