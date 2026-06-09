import { useState, useEffect, useRef, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import { getScanImportStatus, cancelScanImport, type ScanImportStatusDto } from '../../api/scanImport';

// ─── Types ────────────────────────────────────────────────────────────────────

interface ImportProgressEvent {
  jobId: string;
  status: ScanImportStatusDto['status'];
  processedCount: number;
  totalCount: number;
  errorMessage: string | null;
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
 * Falls back to polling GET /systems/:id/scans/import/:id/status every 5s
 * when SignalR is unavailable.
 */
export default function ScanImportProgressBar({ systemId, importJobId, onComplete }: Props) {
  const [progress, setProgress] = useState<ImportProgressEvent>({
    jobId: importJobId,
    status: 'Queued',
    processedCount: 0,
    totalCount: 0,
    errorMessage: null,
  });

  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const pollingRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const isDone = progress.status === 'Completed' || progress.status === 'Failed' || progress.status === 'Cancelled';

  const handleProgressEvent = useCallback((event: ImportProgressEvent) => {
    setProgress(event);
    if (event.status === 'Completed' || event.status === 'Failed' || event.status === 'Cancelled') {
      onComplete?.(event.status);
    }
  }, [onComplete]);

  // Poll fallback
  const startPolling = useCallback(() => {
    if (pollingRef.current) return;
    pollingRef.current = setInterval(async () => {
      try {
        const status = await getScanImportStatus(systemId, importJobId);
        handleProgressEvent({
          jobId: importJobId,
          status: status.status,
          processedCount: status.processedCount,
          totalCount: status.totalCount,
          errorMessage: status.errorMessage,
        });
        if (status.status === 'Completed' || status.status === 'Failed' || status.status === 'Cancelled') {
          if (pollingRef.current) clearInterval(pollingRef.current);
        }
      } catch {
        // best-effort
      }
    }, 5000);
  }, [systemId, importJobId, handleProgressEvent]);

  // SignalR connection
  useEffect(() => {
    const baseUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined)
      ?.replace('/api/dashboard', '') ?? '';
    const hubUrl = `${baseUrl}/hubs/import-progress`;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();

    connection.on('ImportProgress', (event: ImportProgressEvent) => {
      handleProgressEvent(event);
    });

    connection
      .start()
      .then(() => connection.invoke('JoinImportGroup', importJobId))
      .catch(() => {
        // SignalR unavailable — fall back to polling
        startPolling();
      });

    connectionRef.current = connection;

    return () => {
      void connection.invoke('LeaveImportGroup', importJobId).catch(() => {});
      void connection.stop();
      if (pollingRef.current) clearInterval(pollingRef.current);
    };
  }, [importJobId, handleProgressEvent, startPolling]);

  // Clean up polling if done
  useEffect(() => {
    if (isDone && pollingRef.current) {
      clearInterval(pollingRef.current);
      pollingRef.current = null;
    }
  }, [isDone]);

  const percent = progress.totalCount > 0
    ? Math.round((progress.processedCount / progress.totalCount) * 100)
    : progress.status === 'Completed' ? 100 : 0;

  const handleCancel = async () => {
    try {
      await cancelScanImport(systemId, importJobId);
    } catch {
      // best-effort
    }
  };

  return (
    <div className="space-y-2">
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
        <p className="text-xs text-red-600">{progress.errorMessage}</p>
      )}

      {/* Cancel button */}
      {!isDone && (
        <button
          type="button"
          onClick={() => void handleCancel()}
          className="text-xs text-gray-500 hover:text-red-600 hover:underline"
        >
          Cancel import
        </button>
      )}
    </div>
  );
}
