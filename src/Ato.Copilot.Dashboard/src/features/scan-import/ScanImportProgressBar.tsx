import { useState } from 'react';
import { getScanImportStatus, cancelScanImport, type ScanImportStatusDto } from '../../api/scanImport';
import { isProgressEvent, progressError, useJobProgress, useProgressSession, type ProgressSession } from '../../hooks/useJobProgress';
import ProgressTransportNotice from '../../components/ProgressTransportNotice';

// ─── Types ────────────────────────────────────────────────────────────────────

interface ImportProgressEvent {
  jobId: string;
  status: ScanImportStatusDto['status'];
  processedCount: number;
  totalCount: number;
  errorMessage: string | null;
  cancelRequested?: boolean;
}

interface Props {
  systemId: string;
  importJobId: string;
  onComplete?: (status: ScanImportStatusDto['status']) => void;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function statusLabel(status: ScanImportStatusDto['status']): string {
  switch (status) {
    case 'Queued': return 'Queued…';
    case 'Processing': return 'Processing…';
    case 'Completed': return 'Import complete';
    case 'Failed': return 'Import failed';
    case 'Cancelled': return 'Cancelled';
  }
}

function statusColor(status: ScanImportStatusDto['status']): string {
  switch (status) {
    case 'Completed': return 'bg-green-500';
    case 'Failed': return 'bg-red-500';
    case 'Cancelled': return 'bg-gray-400';
    default: return 'bg-indigo-500';
  }
}

// ─── Component ────────────────────────────────────────────────────────────────

/**
 * UF-005 — SCAP/STIG Import Progress Bar (spec-063 T-063-24).
 *
 * Subscribes to the /hubs/import-progress SignalR hub for real-time updates.
 * Capability-gated realtime and independently authorized REST status polling.
 */
export default function ScanImportProgressBar(props: Props) {
  const session = useProgressSession(props.systemId);
  return <ScanImportProgress key={`${session.key}:${props.importJobId}`} {...props} session={session} />;
}

function parseProgress(payload: unknown, importJobId: string): ImportProgressEvent {
  if (!isProgressEvent(payload, 'jobId', importJobId)
    || (payload.status !== 'Queued' && payload.status !== 'Processing' && payload.status !== 'Completed'
      && payload.status !== 'Failed' && payload.status !== 'Cancelled')
    || typeof payload.processedCount !== 'number' || !Number.isSafeInteger(payload.processedCount) || payload.processedCount < 0
    || typeof payload.totalCount !== 'number' || !Number.isSafeInteger(payload.totalCount) || payload.totalCount < 0
    || (payload.errorMessage !== null && typeof payload.errorMessage !== 'string')) {
    throw new Error('Unexpected scan import status response.');
  }
  return {
    jobId: importJobId, status: payload.status, processedCount: payload.processedCount,
    totalCount: payload.totalCount, errorMessage: payload.errorMessage,
    ...(typeof payload.cancelRequested === 'boolean' ? { cancelRequested: payload.cancelRequested } : {}),
  };
}

function ScanImportProgress({ systemId, importJobId, onComplete, session }: Props & { session: ProgressSession }) {
  const [progress, setProgress] = useState<ImportProgressEvent>({
    jobId: importJobId,
    status: 'Queued',
    processedCount: 0,
    totalCount: 0,
    errorMessage: null,
  });

  const [cancelError, setCancelError] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState(false);
  const [cancelRequested, setCancelRequested] = useState(false);

  const isDone = progress.status === 'Completed' || progress.status === 'Failed' || progress.status === 'Cancelled';

  const monitor = useJobProgress<ImportProgressEvent>({
    session, jobId: importJobId, hubPath: '/hubs/import-progress', events: ['ImportProgress'],
    matchesEvent: payload => isProgressEvent(payload, 'jobId', importJobId),
    subscribe: connection => connection.invoke('JoinImportGroup', importJobId),
    poll: async signal => {
      const status = await getScanImportStatus(systemId, importJobId, signal);
      return parseProgress({ ...status, jobId: status.id }, importJobId);
    },
    onEvent: (_name, payload) => parseProgress(payload, importJobId),
    isTerminal: event => event.status === 'Completed' || event.status === 'Failed' || event.status === 'Cancelled',
    onStatus: (event, terminal) => {
      setProgress(event);
      if (event.cancelRequested !== undefined) setCancelRequested(event.cancelRequested);
      if (terminal) onComplete?.(event.status);
    },
  });

  const percent = progress.totalCount > 0
    ? Math.min(100, Math.round((progress.processedCount / progress.totalCount) * 100))
    : progress.status === 'Completed' ? 100 : 0;

  const handleCancel = async () => {
    if (!session.ready || !session.isCurrent()) { setCancelError('Authenticated workspace context is required.'); return; }
    const request = session.request();
    setCancelling(true);
    setCancelError(null);
    try {
      await cancelScanImport(systemId, importJobId, request.signal);
      if (!request.isCurrent()) return;
      setCancelRequested(true);
      monitor.retry();
    } catch (reason) {
      if (request.isCurrent()) setCancelError(progressError(reason));
    } finally {
      if (request.isCurrent()) setCancelling(false);
      request.complete();
    }
  };

  return (
    <div className="space-y-2">
      <ProgressTransportNotice monitor={monitor} />
      {cancelError && <p role="alert" className="text-xs text-red-600">{cancelError}</p>}
      {/* Status row */}
      <div className="flex items-center justify-between text-sm">
        <span className={`font-medium ${
          progress.status === 'Failed' ? 'text-red-600'
          : progress.status === 'Completed' ? 'text-green-700'
          : 'text-gray-700'
        }`}>
          {statusLabel(progress.status)}
        </span>
        <span className="text-gray-500">
          {progress.totalCount > 0
            ? `${progress.processedCount.toLocaleString()} / ${progress.totalCount.toLocaleString()} rules`
            : progress.status === 'Processing' ? 'Processing…' : ''}
        </span>
      </div>

      {/* Progress bar */}
      <div className="h-2 w-full overflow-hidden rounded-full bg-gray-200">
        <div
          className={`h-full rounded-full transition-all duration-300 ${statusColor(progress.status)}`}
          style={{ width: `${percent}%` }}
        />
      </div>

      {/* Error message */}
      {progress.errorMessage && (
        <p role="alert" className="text-xs text-red-600">{progress.errorMessage}</p>
      )}

      {/* Cancel button */}
      {!isDone && (
        <button
          type="button"
          disabled={!session.ready || cancelling || cancelRequested}
          onClick={() => void handleCancel()}
          className="text-xs text-gray-500 hover:text-red-600 hover:underline"
        >
          {cancelRequested ? 'Cancellation requested' : cancelling ? 'Requesting cancellation...' : 'Cancel import'}
        </button>
      )}
    </div>
  );
}
