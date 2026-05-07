import { useEffect, useRef, useState } from 'react';
import { onboarding } from '../api/onboardingApi';
import type { WizardJobStatusDto } from '../api/onboardingApi';

/**
 * `BackgroundJobProgress` — subscribes to SignalR `WizardJobStatus` events for
 * a wizard background job and falls back to polling
 * `/api/onboarding/jobs/{jobId}` after `pollingFallbackSeconds` of silence
 * (contracts/progress-events.md "Polling fallback" — FR-066).
 *
 * The SignalR connection is provided by the host application via the
 * `onSubscribe` callback, which should call
 * `connection.on('WizardJobStatus', handler)` and
 * `connection.invoke('SubscribeToWizardJob', jobId)`.
 */
interface Props {
  jobId: string;
  pollingFallbackSeconds?: number;
  onSubscribe?: (jobId: string, onEvent: (e: WizardJobStatusDto) => void) => () => void;
}

export default function BackgroundJobProgress({
  jobId,
  pollingFallbackSeconds = 10,
  onSubscribe,
}: Props) {
  const [status, setStatus] = useState<WizardJobStatusDto | null>(null);
  const lastEventAtRef = useRef<number>(Date.now());

  // Initial fetch — guarantees a baseline even if SignalR hasn't pushed yet.
  useEffect(() => {
    let cancelled = false;
    void onboarding.getJob(jobId).then(s => {
      if (!cancelled) setStatus(s);
    }).catch(() => { /* ignore — polling will retry */ });
    return () => { cancelled = true; };
  }, [jobId]);

  // SignalR subscription.
  useEffect(() => {
    if (!onSubscribe) return;
    const unsubscribe = onSubscribe(jobId, (evt) => {
      lastEventAtRef.current = Date.now();
      setStatus(evt);
    });
    return unsubscribe;
  }, [jobId, onSubscribe]);

  // Polling fallback — kicks in after `pollingFallbackSeconds` of silence.
  useEffect(() => {
    if (status && (status.status === 'Succeeded' || status.status === 'Failed' || status.status === 'Cancelled')) {
      return;
    }
    const interval = window.setInterval(async () => {
      const silentMs = Date.now() - lastEventAtRef.current;
      if (silentMs < pollingFallbackSeconds * 1000) return;
      try {
        const next = await onboarding.getJob(jobId);
        setStatus(next);
        lastEventAtRef.current = Date.now();
      } catch { /* ignore */ }
    }, 2000);
    return () => window.clearInterval(interval);
  }, [jobId, pollingFallbackSeconds, status]);

  if (!status) {
    return (
      <div role="status" aria-live="polite" className="text-sm text-gray-600">
        Waiting for job {jobId}...
      </div>
    );
  }

  return (
    <div role="status" aria-live="polite" className="rounded border border-gray-200 p-3 text-sm">
      <div className="flex items-baseline justify-between">
        <span className="font-semibold">{status.jobType}</span>
        <span
          className={[
            'inline-flex items-center px-2 py-0.5 rounded text-xs font-medium',
            badgeClass(status.status),
          ].join(' ')}
        >
          {status.status}
        </span>
      </div>
      {status.percent != null && (
        <div className="mt-2 h-2 w-full rounded-full bg-gray-200">
          <div
            className="h-2 rounded-full bg-indigo-600 transition-all"
            style={{ width: `${Math.max(0, Math.min(100, status.percent))}%` }}
          />
        </div>
      )}
      {status.message && (
        <p className="mt-2 text-gray-700">{status.message}</p>
      )}
      {status.status === 'Failed' && status.errorCode && (
        <p className="mt-2 text-red-700">
          <strong>{status.errorCode}</strong>
          {status.suggestion && <span className="block text-red-600">{status.suggestion}</span>}
        </p>
      )}
    </div>
  );
}

function badgeClass(s: WizardJobStatusDto['status']): string {
  switch (s) {
    case 'Queued': return 'bg-gray-100 text-gray-800';
    case 'InProgress': return 'bg-indigo-100 text-indigo-800';
    case 'Succeeded': return 'bg-green-100 text-green-800';
    case 'Failed': return 'bg-red-100 text-red-800';
    case 'Cancelled': return 'bg-yellow-100 text-yellow-800';
  }
}
