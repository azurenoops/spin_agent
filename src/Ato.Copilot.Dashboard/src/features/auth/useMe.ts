import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import axios from 'axios';
import type { MeResponse } from './types';

export interface UseMeResult {
  data: MeResponse | null;
  isLoading: boolean;
  error: Error | null;
  refetch: () => void;
}

export const MeContext = createContext<UseMeResult | undefined>(undefined);

export function useOptionalMe(): UseMeResult | undefined {
  return useContext(MeContext);
}

export function useMe(): UseMeResult {
  const shared = useOptionalMe();
  const standalone = useMeRequest(shared === undefined);
  return shared ?? standalone;
}

export function useMeRequest(enabled = true, contextKey = 'legacy'): UseMeResult {
  const [tick, setTick] = useState(0);
  const requestKey = `${contextKey}:${tick}:${enabled ? 'active' : 'inactive'}`;
  const [state, setState] = useState<Omit<UseMeResult, 'refetch'> & { key: string }>({
    key: requestKey, data: null, isLoading: enabled, error: null,
  });
  const refetch = useCallback(() => setTick((t) => t + 1), []);

  useEffect(() => {
    if (!enabled) {
      setState({ key: requestKey, data: null, isLoading: false, error: null });
      return;
    }
    let cancelled = false;
    setState({ key: requestKey, data: null, isLoading: true, error: null });
    void (async () => {
      try {
        const resp = await axios.get<{ status?: string; data?: MeResponse }>('/api/auth/me');
        if (cancelled) return;
        const body = resp.data;
        if (body?.status === 'success' && body.data) {
          setState({ key: requestKey, data: body.data, isLoading: false, error: null });
        } else {
          setState({
            key: requestKey, data: null, isLoading: false,
            error: new Error('Unexpected /api/auth/me envelope'),
          });
        }
      } catch (err) {
        if (cancelled) return;
        setState({
          key: requestKey, data: null, isLoading: false,
          error: err instanceof Error ? err : new Error(String(err)),
        });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [enabled, requestKey]);

  useEffect(() => {
    if (!enabled) return;
    const handler = () => refetch();
    window.addEventListener('ato:tenant-changed', handler);
    return () => window.removeEventListener('ato:tenant-changed', handler);
  }, [enabled, refetch]);

  if (!enabled || state.key !== requestKey) {
    return { data: null, isLoading: enabled, error: null, refetch };
  }
  return { data: state.data, isLoading: state.isLoading, error: state.error, refetch };
}
