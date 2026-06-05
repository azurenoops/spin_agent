import { useCallback, useEffect, useRef, useState } from 'react';
import { onboarding } from '../api/onboardingApi';

/**
 * Task #248 — useJobPoller
 *
 * Custom hook that polls `GET /api/onboarding/jobs/:jobId` on a configurable
 * interval and stops automatically when the job reaches a terminal state
 * (`Completed` / `Failed` / `Succeeded` / `Cancelled`).
 *
 * Design notes:
 *  - Passes `jobId = null` to pause polling (e.g. before a job is launched).
 *  - Uses an in-flight flag to prevent concurrent duplicate requests.
 *  - Cleans up the interval on unmount or when `jobId` changes.
 *  - Calls the optional `onTerminal` callback exactly once per terminal event.
 */

export interface UseJobPollerOptions {
  /** Job ID to poll.  Pass `null` to disable polling. */
  jobId: string | null;
  /** Polling interval in milliseconds.  Defaults to 5 000. */
  intervalMs?: number;
  /**
   * Called once when the job enters a terminal state.
   * `status` is the terminal status string; `failureReason` is the backend's
   * `message` field when status is `'Failed'`.
   */
  onTerminal?: (status: string, failureReason?: string) => void;
}

export type JobPollerResult = {
  /** Last-known job status, or `null` if no poll has succeeded yet. */
  status: string | null;
  /** True while the first poll is outstanding (initial load state). */
  loading: boolean;
  /** Error message from the most-recent failed poll, or `null`. */
  error: string | null;
};

/** Terminal states that stop polling. */
const TERMINAL = new Set(['Completed', 'Succeeded', 'Failed', 'Cancelled']);

/**
 * Polls a background job on a fixed interval and stops on terminal states.
 *
 * @example
 * ```tsx
 * const { status, loading, error } = useJobPoller({
 *   jobId: batchJobId,
 *   onTerminal: (s, reason) => s === 'Failed' && showErrorToast(reason),
 * });
 * ```
 */
export function useJobPoller({
  jobId,
  intervalMs = 5_000,
  onTerminal,
}: UseJobPollerOptions): JobPollerResult {
  const [status, setStatus] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Stable refs so we don't recreate the interval on callback identity changes.
  const inFlightRef = useRef(false);
  const terminalFiredRef = useRef(false);
  const onTerminalRef = useRef(onTerminal);
  onTerminalRef.current = onTerminal;

  const poll = useCallback(async () => {
    if (!jobId || inFlightRef.current) return;
    inFlightRef.current = true;
    try {
      const dto = await onboarding.getJob(jobId);
      const s = dto.status as string;
      setStatus(s);
      setError(null);
      if (TERMINAL.has(s) && !terminalFiredRef.current) {
        terminalFiredRef.current = true;
        const reason = dto.message ?? undefined;
        onTerminalRef.current?.(s, reason);
      }
    } catch (err) {
      setError((err as Error).message ?? 'Failed to fetch job status.');
    } finally {
      inFlightRef.current = false;
    }
  }, [jobId]);

  useEffect(() => {
    if (!jobId) {
      // Reset when jobId is cleared.
      setStatus(null);
      setLoading(false);
      setError(null);
      inFlightRef.current = false;
      terminalFiredRef.current = false;
      return;
    }

    // Kick off an immediate first poll so the UI doesn't have to wait 5 s.
    setLoading(true);
    setStatus(null);
    setError(null);
    terminalFiredRef.current = false;

    let stopped = false;

    const runOnce = async () => {
      await poll();
      if (!stopped) setLoading(false);
    };
    void runOnce();

    const id = setInterval(async () => {
      // Stop the interval once we hit a terminal state.
      if (terminalFiredRef.current) {
        clearInterval(id);
        return;
      }
      await poll();
    }, intervalMs);

    return () => {
      stopped = true;
      clearInterval(id);
    };
  }, [jobId, intervalMs, poll]);

  return { status, loading, error };
}
