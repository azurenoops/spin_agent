import { useState, useEffect, useRef, useCallback } from 'react';

/**
 * Polls a data-fetching function at a configurable interval.
 * Returns { data, loading, refresh } for convenience.
 * Pauses when the tab is hidden and resumes on focus.
 */
export function usePolling<T>(
  fetcher: () => Promise<T>,
  intervalMs?: number,
  enabled?: boolean,
): { data: T | null; loading: boolean; refresh: () => void };

/**
 * Polls a fire-and-forget callback at a configurable interval.
 * Caller manages its own state.
 */
export function usePolling(
  callback: () => void | Promise<void>,
  intervalMs?: number,
  enabled?: boolean,
): void;

export function usePolling<T = void>(
  callback: () => T | Promise<T>,
  intervalMs?: number,
  enabled = true,
): { data: T | null; loading: boolean; refresh: () => void } | void {
  const interval = intervalMs ?? Number(import.meta.env.VITE_POLL_INTERVAL_MS || '15000');
  const savedCallback = useRef(callback);
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const isDataFetcher = useRef(false);

  useEffect(() => {
    savedCallback.current = callback;
  }, [callback]);

  const tick = useCallback(async () => {
    try {
      const result = await savedCallback.current();
      if (result !== undefined) {
        isDataFetcher.current = true;
        setData(result as T);
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!enabled) return;

    tick();

    let timerId: ReturnType<typeof setInterval> | null = null;

    const start = () => {
      if (timerId === null) {
        timerId = setInterval(tick, interval);
      }
    };

    const stop = () => {
      if (timerId !== null) {
        clearInterval(timerId);
        timerId = null;
      }
    };

    const handleVisibility = () => {
      if (document.hidden) {
        stop();
      } else {
        tick();
        start();
      }
    };

    start();
    document.addEventListener('visibilitychange', handleVisibility);

    return () => {
      stop();
      document.removeEventListener('visibilitychange', handleVisibility);
    };
  }, [interval, enabled, tick]);

  return { data, loading, refresh: tick };
}
