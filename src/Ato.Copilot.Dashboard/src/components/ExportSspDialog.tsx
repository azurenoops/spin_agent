import { useState, useEffect, useRef, useCallback } from 'react';
import { requestExport, downloadExportUrl, listTemplates } from '../api/exports';
import type { ExportSummary, TemplateInfo } from '../api/exports';
import * as signalR from '@microsoft/signalr';

interface ExportSspDialogProps {
  systemId: string;
  onClose: () => void;
  onExportComplete?: () => void;
}

type ExportStatus = 'idle' | 'submitting' | 'processing' | 'completed' | 'failed';

export default function ExportSspDialog({ systemId, onClose, onExportComplete }: ExportSspDialogProps) {
  const [format, setFormat] = useState<'docx' | 'pdf' | 'json'>('docx');
  const [templateId, setTemplateId] = useState<string>('');
  const [templates, setTemplates] = useState<TemplateInfo[]>([]);
  const [status, setStatus] = useState<ExportStatus>('idle');
  const [progressStep, setProgressStep] = useState('');
  const [progressPct, setProgressPct] = useState(0);
  const [exportId, setExportId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  // Close on Escape
  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && status !== 'processing') onClose();
    };
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [onClose, status]);

  // Load templates when format is docx
  useEffect(() => {
    if (format === 'docx') {
      listTemplates({ limit: 50 })
        .then(res => setTemplates(res.items))
        .catch(() => setTemplates([]));
    }
  }, [format]);

  // Set up SignalR listener for export progress
  const setupSignalR = useCallback((eid: string) => {
    const hubUrl = (import.meta.env.VITE_API_BASE_URL || '').replace('/api/dashboard', '') + '/hubs/notifications';
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => localStorage.getItem('auth_token') ?? '',
      })
      .withAutomaticReconnect()
      .build();

    connection.on('SspExportProgress', (payload: { exportId: string; step: string; percentage: number }) => {
      if (payload.exportId === eid) {
        setProgressStep(payload.step);
        setProgressPct(payload.percentage);
      }
    });

    connection.on('SspExportReady', (payload: { exportId: string; format: string }) => {
      if (payload.exportId === eid) {
        setStatus('completed');
        setProgressPct(100);
        setProgressStep('Complete');
        onExportComplete?.();
      }
    });

    connection.on('SspExportFailed', (payload: { exportId: string; error: string }) => {
      if (payload.exportId === eid) {
        setStatus('failed');
        setError(payload.error);
      }
    });

    connection
      .start()
      .then(() => connection.invoke('RegisterUser', 'dashboard-user'))
      .catch(() => {
        // SignalR connection optional — user can poll instead
      });

    connectionRef.current = connection;
  }, [onExportComplete]);

  // Clean up SignalR on unmount
  useEffect(() => {
    return () => {
      connectionRef.current?.stop();
    };
  }, []);

  const handleExport = async () => {
    setStatus('submitting');
    setError(null);
    try {
      const result: ExportSummary = await requestExport(
        systemId,
        format,
        format === 'docx' && templateId ? templateId : undefined,
      );
      setExportId(result.exportId);
      setStatus('processing');
      setProgressStep('Queued');
      setProgressPct(10);
      setupSignalR(result.exportId);
    } catch (err: unknown) {
      setStatus('failed');
      setError(err instanceof Error ? err.message : 'Export request failed');
    }
  };

  const handleBackdrop = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget && status !== 'processing') onClose();
  };

  const formatLabel: Record<string, string> = {
    docx: 'Word (.docx)',
    pdf: 'PDF (.pdf)',
    json: 'OSCAL JSON (.json)',
  };

  const formatIcon: Record<string, string> = {
    docx: '📄',
    pdf: '📕',
    json: '🔗',
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
      onClick={handleBackdrop}
    >
      <div
        ref={dialogRef}
        className="w-full max-w-md rounded-xl bg-white shadow-2xl border border-gray-200 overflow-hidden"
        role="dialog"
        aria-labelledby="export-dialog-title"
      >
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 bg-gray-50 border-b border-gray-200">
          <h2 id="export-dialog-title" className="text-base font-semibold text-gray-900">
            Export SSP Document
          </h2>
          {status !== 'processing' && (
            <button
              onClick={onClose}
              className="text-gray-400 hover:text-gray-600 transition-colors"
              aria-label="Close"
            >
              <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          )}
        </div>

        {/* Body */}
        <div className="px-5 py-4 space-y-4">
          {/* Format selection */}
          {(status === 'idle' || status === 'submitting') && (
            <>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-2">Export Format</label>
                <div className="space-y-2">
                  {(['docx', 'pdf', 'json'] as const).map((f) => (
                    <label
                      key={f}
                      className={`flex items-center gap-3 p-3 rounded-lg border cursor-pointer transition-colors ${
                        format === f ? 'border-blue-500 bg-blue-50' : 'border-gray-200 hover:bg-gray-50'
                      }`}
                    >
                      <input
                        type="radio"
                        name="format"
                        value={f}
                        checked={format === f}
                        onChange={() => setFormat(f)}
                        className="text-blue-600 focus:ring-blue-500"
                      />
                      <span className="text-lg">{formatIcon[f]}</span>
                      <span className="text-sm font-medium text-gray-900">{formatLabel[f]}</span>
                    </label>
                  ))}
                </div>
              </div>

              {/* Template selector (docx only) */}
              {format === 'docx' && templates.length > 0 && (
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Template</label>
                  <select
                    value={templateId}
                    onChange={(e) => setTemplateId(e.target.value)}
                    className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-1 focus:ring-blue-500"
                  >
                    <option value="">Default template</option>
                    {templates.map((t) => (
                      <option key={t.id} value={t.id}>
                        {t.name} {t.isDefault ? '(default)' : ''}
                      </option>
                    ))}
                  </select>
                </div>
              )}
            </>
          )}

          {/* Progress */}
          {(status === 'processing' || status === 'completed') && (
            <div className="space-y-3">
              <div className="flex items-center gap-2">
                {status === 'processing' && (
                  <svg className="h-5 w-5 text-blue-500 animate-spin" viewBox="0 0 24 24" fill="none">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                    <path
                      className="opacity-75"
                      fill="currentColor"
                      d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
                    />
                  </svg>
                )}
                {status === 'completed' && (
                  <svg className="h-5 w-5 text-green-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                )}
                <span className={`text-sm font-medium ${status === 'completed' ? 'text-green-700' : 'text-gray-700'}`}>
                  {progressStep}
                </span>
              </div>
              <div className="w-full bg-gray-200 rounded-full h-2">
                <div
                  className={`h-2 rounded-full transition-all duration-500 ${
                    status === 'completed' ? 'bg-green-500' : 'bg-blue-500'
                  }`}
                  style={{ width: `${progressPct}%` }}
                />
              </div>
              {status === 'completed' && exportId && (
                <a
                  href={downloadExportUrl(systemId, exportId)}
                  className="inline-flex items-center gap-2 mt-2 px-4 py-2 bg-green-600 text-white text-sm font-medium rounded-lg hover:bg-green-700 transition-colors"
                  download
                >
                  <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                  </svg>
                  Download {formatLabel[format]}
                </a>
              )}
            </div>
          )}

          {/* Error */}
          {status === 'failed' && error && (
            <div className="rounded-lg bg-red-50 border border-red-200 p-3">
              <p className="text-sm text-red-700">{error}</p>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-2 px-5 py-3 bg-gray-50 border-t border-gray-200">
          {status === 'idle' && (
            <>
              <button
                onClick={onClose}
                className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={handleExport}
                className="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700"
              >
                Export
              </button>
            </>
          )}
          {status === 'submitting' && (
            <button
              disabled
              className="px-4 py-2 text-sm font-medium text-white bg-blue-400 rounded-lg cursor-not-allowed"
            >
              Submitting...
            </button>
          )}
          {(status === 'completed' || status === 'failed') && (
            <button
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
            >
              Close
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
