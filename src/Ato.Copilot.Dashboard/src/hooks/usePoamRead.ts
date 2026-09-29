import { useCallback, useEffect, useState } from 'react';
import { poamErrorMessage } from '../utils/poamErrors';

/** Scope changes immediately hide stale data and abort in-flight requests. */
export function usePoamRead<T>(fetcher: (signal: AbortSignal) => Promise<T>) {
  const [revision, setRevision] = useState(0);
  const [state, setState] = useState<{ fetcher: typeof fetcher; data: T | null; loading: boolean; error: Error | null }>({
    fetcher, data: null, loading: true, error: null,
  });
  useEffect(() => {
    const controller = new AbortController();
    let running = false;
    const load = async () => {
      if (running || controller.signal.aborted) return;
      running = true;
      setState(previous => ({ fetcher, data: previous.fetcher === fetcher ? previous.data : null, loading: true, error: null }));
      try {
        const data = await fetcher(controller.signal);
        if (!controller.signal.aborted) setState({ fetcher, data, loading: false, error: null });
      } catch (error) {
        if (!controller.signal.aborted) setState(previous => ({
          ...previous, loading: false, error: new Error(poamErrorMessage(error)),
        }));
      } finally {
        running = false;
      }
    };
    void load();
    const interval = setInterval(() => { if (!document.hidden) void load(); }, Number(import.meta.env.VITE_POLL_INTERVAL_MS || 15000));
    const visible = () => { if (!document.hidden) void load(); };
    document.addEventListener('visibilitychange', visible);
    return () => {
      controller.abort();
      clearInterval(interval);
      document.removeEventListener('visibilitychange', visible);
    };
  }, [fetcher, revision]);
  const refresh = useCallback(() => setRevision(value => value + 1), []);
  return state.fetcher === fetcher ? { ...state, refresh } : { data: null, loading: true, error: null, refresh };
}
