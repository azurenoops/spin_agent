import { useCallback, useState } from 'react';
import { useJobPoller } from '../../../hooks/useJobPoller';

/**
 * Task #248 — JobStatusPanel
 *
 * Renders a live status card for a background job.  Uses `useJobPoller` to
 * poll `GET /api/onboarding/jobs/:jobId` and transitions through three visual
 * states:
 *
 *  1. **Loading / polling** — animated spinner with "Processing…" label.
 *  2. **Completed** (or `Succeeded`) — green success card.
 *  3. **Failed** (or `Cancelled`) — red error card with the backend's failure
 *     reason and a Retry button that re-mounts the poller for the same jobId.
 *
 * Wired into `TenantWizard` (see Task #248 notes in index.tsx) so the AO step
 * can surface extraction job status without blocking the wizard chrome.
 */

interface JobStatusPanelProps {
  /** Job ID to poll.  Pass `null` to render nothing. */
  jobId: string | null;
  /** Called once the job reaches a `Completed` / `Succeeded` state. */
  onComplete?: () => void;
  /** Called once the job reaches a `Failed` / `Cancelled` state. */
  onError?: (reason: string) => void;
}

export default function JobStatusPanel({
  jobId,
  onComplete,
  onError,
}: JobStatusPanelProps) {
  // `retryKey` is incremented to force the poller to restart from scratch when
  // the user clicks Retry after a failed job (same jobId, fresh poll cycle).
  const [retryKey, setRetryKey] = useState(0);
  const [failureReason, setFailureReason] = useState<string | undefined>();

  const handleTerminal = useCallback(
    (status: string, reason?: string) => {
      if (status === 'Completed' || status === 'Succeeded') {
        onComplete?.();
      } else {
        setFailureReason(reason);
        onError?.(reason ?? 'Job failed.');
      }
    },
    [onComplete, onError],
  );

  // Re-mount the poller by changing the key — this resets all poller state.
  const effectiveJobId = retryKey >= 0 ? jobId : null;

  const { status, loading, error } = useJobPoller({
    jobId: effectiveJobId,
    onTerminal: handleTerminal,
  });

  if (!jobId) return null;

  const isTerminalSuccess = status === 'Completed' || status === 'Succeeded';
  const isTerminalFailure = status === 'Failed' || status === 'Cancelled';
  const isPolling = !isTerminalSuccess && !isTerminalFailure;

  return (
    <div className="mt-4" aria-live="polite" aria-atomic="true">
      {/* ── Polling / Loading ── */}
      {isPolling && (
        <div className="flex items-center gap-3 rounded-lg border border-slate-200 bg-slate-50 px-4 py-3">
          <svg
            className="h-5 w-5 flex-shrink-0 animate-spin text-indigo-600"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth={2.5}
            aria-hidden="true"
          >
            <path strokeLinecap="round" d="M12 2a10 10 0 0 1 10 10" />
          </svg>
          <div className="min-w-0">
            <p className="text-sm font-medium text-slate-700">
              {loading ? 'Starting…' : `Processing… ${status ? `(${status})` : ''}`}
            </p>
            <p className="text-xs text-slate-500">This may take a few moments.</p>
          </div>
        </div>
      )}

      {/* ── Poll error (network / API failure) ── */}
      {error && isPolling && (
        <p role="alert" className="mt-2 text-xs text-rose-700">
          Could not fetch job status: {error}
        </p>
      )}

      {/* ── Success ── */}
      {isTerminalSuccess && (
        <div className="flex items-start gap-3 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3">
          <svg
            className="mt-0.5 h-5 w-5 flex-shrink-0 text-emerald-600"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth={2.5}
            aria-hidden="true"
          >
            <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
          </svg>
          <div>
            <p className="text-sm font-semibold text-emerald-900">Completed</p>
            <p className="text-xs text-emerald-700">The background job finished successfully.</p>
          </div>
        </div>
      )}

      {/* ── Failure ── */}
      {isTerminalFailure && (
        <div className="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3">
          <div className="flex items-start gap-3">
            <svg
              className="mt-0.5 h-5 w-5 flex-shrink-0 text-rose-600"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth={2.5}
              aria-hidden="true"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M12 9v4m0 4h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"
              />
            </svg>
            <div className="flex-1">
              <p className="text-sm font-semibold text-rose-900">Job {status ?? 'Failed'}</p>
              {failureReason && (
                <p className="mt-0.5 text-xs text-rose-700">{failureReason}</p>
              )}
            </div>
          </div>
          <button
            type="button"
            onClick={() => {
              setFailureReason(undefined);
              setRetryKey((k) => k + 1);
            }}
            className="mt-3 rounded-md border border-rose-300 bg-white px-3 py-1.5 text-xs font-medium text-rose-700 hover:bg-rose-50"
          >
            Retry
          </button>
        </div>
      )}
    </div>
  );
}
