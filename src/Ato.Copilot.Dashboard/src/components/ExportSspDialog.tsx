import { useState, useEffect, useRef, useCallback } from 'react';
import { requestExport, getExport, downloadExportUrl, listTemplates } from '../api/exports';
import type { ExportDetail, TemplateInfo } from '../api/exports';
// Wave 6 GAP-018
import apiClient from '../api/client';
import { enqueuePackage, getPackageStatus, getPackageDownloadUrl } from '../api/packages';
import type { PackageDetail } from '../api/package';
import { downloadAuthenticatedFile } from '../api/downloads';
import { ValidationBadge } from '../features/oscal';
import AuthenticatedDownload from './AuthenticatedDownload';
import { isProgressEvent, progressError, useJobProgress, useProgressSession, type ProgressSession } from '../hooks/useJobProgress';
import ProgressTransportNotice from './ProgressTransportNotice';

// ── Contract types for OSCAL SSP export ─────────────────────────────────────
// GET /api/v1/systems/{systemId}/exports/oscal-ssp
interface OscalExportValidationStatus {
  isValid: boolean;
  errors: { code: string; message: string; path: string }[];
  warnings: { code: string; message: string; path: string }[];
}
interface OscalExportSummary {
  oscalVersion: string;
  generatedAt: string;
  validationStatus: OscalExportValidationStatus;
  stats: { controlCount: number; componentCount: number; inventoryItemCount: number };
  downloadUrl: string;
}
// ────────────────────────────────────────────────────────────────────────────

interface ExportSspDialogProps {
  systemId: string;
  onClose: () => void;
  onExportComplete?: () => void;
}

type ExportStatus = 'idle' | 'submitting' | 'processing' | 'completed' | 'failed';

export default function ExportSspDialog(props: ExportSspDialogProps) {
  const session = useProgressSession(props.systemId);
  return <ExportSspContent key={session.key} {...props} session={session} />;
}

function ExportSspContent({ systemId, onClose, onExportComplete, session }: ExportSspDialogProps & { session: ProgressSession }) {
  const [format, setFormat] = useState<'docx' | 'pdf'>('docx');
  const [templateId, setTemplateId] = useState<string>('');
  const [templates, setTemplates] = useState<TemplateInfo[]>([]);
  const [status, setStatus] = useState<ExportStatus>('idle');
  const [progressStep, setProgressStep] = useState('');
  const [progressPct, setProgressPct] = useState<number | null>(null);
  const hasRealtimeProgress = useRef(false);
  const [exportId, setExportId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  // Wave 6 GAP-018
  const [oscalExporting, setOscalExporting] = useState<string | null>(null);
  const [oscalError, setOscalError] = useState<string | null>(null);
  // #419: OSCAL SSP export summary (validation + stats from contract GET endpoint)
  const [oscalSspSummary, setOscalSspSummary] = useState<OscalExportSummary | null>(null);
  const [oscalSspSummaryLoading, setOscalSspSummaryLoading] = useState(false);
  // #180: PDF/XLSX/eMASS package export state
  const [pkgExporting, setPkgExporting] = useState<string | null>(null);
  const [pkgError, setPkgError] = useState<string | null>(null);
  const [pkgJobId, setPkgJobId] = useState<string | null>(null);

  const handleOscalDownload = useCallback(async (artifactType: 'oscal-poam' | 'oscal-assessment-results' | 'oscal-sap') => {
    if (!session.ready || !session.isCurrent()) { setOscalError('Authenticated workspace context is required.'); return; }
    const request = session.request();
    setOscalExporting(artifactType);
    setOscalError(null);
    try {
      await downloadAuthenticatedFile(`/api/v1/systems/${systemId}/exports/${artifactType}`, `${artifactType}-${systemId}.json`, request.signal);
    } catch (e: unknown) {
      if (request.isCurrent()) setOscalError(`${artifactType} export failed: ${progressError(e)}`);
    } finally {
      if (request.isCurrent()) setOscalExporting(null);
      request.complete();
    }
  }, [systemId, session.key, session.ready]);

  /**
   * #419 contract: two-phase OSCAL SSP export.
   *   1. GET /api/v1/systems/{id}/exports/oscal-ssp  → validate + surface stats
   *   2. GET /api/v1/systems/{id}/exports/oscal-ssp/download  → stream file
   *
   * Calling this function when oscalSspSummary is already loaded skips step 1
   * and goes straight to download; re-validates only when summary is stale.
   */
  const handleOscalSspDownload = useCallback(async () => {
    if (!session.ready || !session.isCurrent()) { setOscalError('Authenticated workspace context is required.'); return; }
    const request = session.request();
    setOscalExporting('oscal-ssp');
    setOscalError(null);
    try {
      // Step 1 — fetch validation + stats if not already loaded
      let summary = oscalSspSummary;
      if (!summary) {
        setOscalSspSummaryLoading(true);
        const metaRes = await apiClient.get<OscalExportSummary>(
          `/api/v1/systems/${systemId}/exports/oscal-ssp`,
          { baseURL: new URL(apiClient.defaults.baseURL ?? '/api/dashboard', window.location.origin).origin, signal: request.signal },
        );
        if (!request.isCurrent()) return;
        summary = metaRes.data;
        setOscalSspSummary(summary);
        setOscalSspSummaryLoading(false);
      }

      // Step 2 — stream download
      await downloadAuthenticatedFile(
        `/api/v1/systems/${systemId}/exports/oscal-ssp/download`,
        `oscal-ssp-${systemId}.json`, request.signal,
      );
    } catch (e: unknown) {
      if (!request.isCurrent()) return;
      setOscalSspSummaryLoading(false);
      setOscalError(`OSCAL SSP export failed: ${progressError(e)}`);
    } finally {
      if (request.isCurrent()) setOscalExporting(null);
      request.complete();
    }
  }, [systemId, oscalSspSummary, session.key, session.ready]);

  const handlePackageExport = useCallback(async (format: 'pdf' | 'xlsx' | 'emass-xlsx') => {
    if (!session.ready || !session.isCurrent()) { setPkgError('Authenticated workspace context is required.'); return; }
    const request = session.request();
    setPkgJobId(null);
    setPkgExporting(format);
    setPkgError(null);
    try {
      const job = await enqueuePackage(systemId, format === 'emass-xlsx' ? 'full' : 'inline', request.signal);
      if (!request.isCurrent()) return;
      if (!job || typeof job.packageId !== 'string' || !job.packageId.trim()) {
        throw new Error('Unexpected package generation response.');
      }
      setPkgJobId(job.packageId);
    } catch (e: unknown) {
      if (!request.isCurrent()) return;
      setPkgError(`${format} export failed: ${progressError(e)}`);
      setPkgExporting(null);
    } finally {
      request.complete();
    }
  }, [systemId, session.key, session.ready]);

  const downloadCompletedPackage = async (job: PackageDetail) => {
    if (!session.isCurrent()) return;
    const request = session.request();
    try {
      await downloadAuthenticatedFile(getPackageDownloadUrl(systemId, job.packageId),
        `ato-package-${pkgExporting}-${systemId.slice(0, 8)}.zip`, request.signal);
    } catch (reason) {
      if (request.isCurrent()) setPkgError(progressError(reason));
    } finally {
      if (request.isCurrent()) setPkgExporting(null);
      request.complete();
    }
  };

  const packageMonitor = useJobProgress<PackageDetail>({
    session, jobId: pkgJobId, hubPath: '/hubs/package', pollIntervalMs: 3000,
    events: ['PackageStatusChanged', 'PackageArtifactGenerated', 'PackageComplete', 'PackageFailed'],
    matchesEvent: payload => isProgressEvent(payload, 'packageId', pkgJobId),
    subscribe: connection => connection.invoke('SubscribeToPackage', pkgJobId),
    poll: async signal => {
      if (!pkgJobId) throw new Error('No package is selected.');
      const result = await getPackageStatus(systemId, pkgJobId, signal);
      if (result.packageId !== pkgJobId || result.systemId !== systemId
        || !['Pending', 'Generating', 'Validating', 'Completed', 'Failed'].includes(result.status)) {
        throw new Error('Unexpected package status response.');
      }
      return result;
    },
    isTerminal: result => result.status === 'Completed' || result.status === 'Failed',
    onStatus: result => {
      if (result.status === 'Completed') void downloadCompletedPackage(result);
      else if (result.status === 'Failed') {
        setPkgError(result.failureReason ?? 'Package generation failed.');
        setPkgExporting(null);
      }
    },
  });

  const monitor = useJobProgress<ExportDetail>({
    session, jobId: exportId, hubPath: '/hubs/notifications',
    events: ['SspExportProgress', 'SspExportReady', 'SspExportFailed'],
    matchesEvent: payload => isProgressEvent(payload, 'exportId', exportId),
    subscribe: (connection, capabilities) => connection.invoke('RegisterUser', capabilities.recipientId),
    poll: async signal => {
      if (!exportId) throw new Error('No export is selected.');
      const result = await getExport(systemId, exportId, signal);
      if (result.exportId !== exportId || result.systemId !== systemId
        || !['Pending', 'Processing', 'Completed', 'Failed'].includes(result.status)) {
        throw new Error('Unexpected export status response.');
      }
      return result;
    },
    onEvent: (name, payload) => {
      if (!isProgressEvent(payload, 'exportId', exportId)) return;
      if (name === 'SspExportProgress') {
        if (typeof payload.step !== 'string' || typeof payload.percentage !== 'number' || !Number.isFinite(payload.percentage)) {
          throw new Error('Unexpected export progress event.');
        }
        setProgressStep(payload.step);
        hasRealtimeProgress.current = true;
        const percentage = Math.max(0, Math.min(100, payload.percentage));
        setProgressPct(previous => Math.max(previous ?? 0, percentage));
      } else if (name === 'SspExportFailed' && typeof payload.error === 'string') {
        setError(payload.error);
      }
      return undefined;
    },
    isTerminal: result => result.status === 'Completed' || result.status === 'Failed',
    onStatus: result => {
      if (result.status === 'Completed') {
        setStatus('completed');
        setProgressPct(100);
        setProgressStep('Complete');
        onExportComplete?.();
      } else if (result.status === 'Failed') {
        setStatus('failed');
        setError(previous => previous ?? 'Export failed. The status endpoint does not include a failure reason.');
      } else {
        if (!hasRealtimeProgress.current) setProgressStep(result.status);
      }
    },
  });

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
    if (format !== 'docx' || !session.ready) return;
    const request = session.request();
    listTemplates({ limit: 50 }, request.signal)
      .then(result => { if (request.isCurrent()) setTemplates(result.items); })
      .catch(reason => { if (request.isCurrent()) setError(`Unable to load templates: ${progressError(reason)}`); })
      .finally(request.complete);
    return () => { request.cancel(); request.complete(); };
  }, [format, session.key, session.ready]);

  const handleExport = async () => {
    if (!session.ready || !session.isCurrent()) { setError('Authenticated workspace context is required.'); return; }
    const request = session.request();
    setStatus('submitting');
    setError(null);
    try {
      const result = await requestExport(
        systemId,
        format,
        format === 'docx' && templateId ? templateId : undefined,
        request.signal,
      );
      if (!request.isCurrent()) return;
      if (!result || typeof result.exportId !== 'string' || !result.exportId.trim()) {
        throw new Error('Unexpected export request response.');
      }
      setExportId(result.exportId);
      setStatus('processing');
      setProgressStep('Queued');
      hasRealtimeProgress.current = false;
      setProgressPct(null);
    } catch (err: unknown) {
      if (!request.isCurrent()) return;
      setStatus('failed');
      setError(progressError(err));
    } finally {
      request.complete();
    }
  };

  const handleBackdrop = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget && status !== 'processing') onClose();
  };

  const formatLabel: Record<string, string> = {
    docx: 'Word (.docx)',
    pdf: 'PDF (.pdf)',
    // json removed — OSCAL SSP now has its own dedicated section (Issue #419)
  };

  const formatIcon: Record<string, string> = {
    docx: '📄',
    pdf: '📕',
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
          <ProgressTransportNotice monitor={monitor} onClose={onClose} />
          {pkgJobId && <ProgressTransportNotice monitor={packageMonitor} onClose={onClose} />}
          {error && status !== 'failed' && <p role="alert">{error}</p>}
          {/* Format selection */}
          {(status === 'idle' || status === 'submitting') && (
            <>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-2">Export Format</label>
                <div className="space-y-2">
                  {(['docx', 'pdf'] as const).map((f) => (
                    <label
                      key={f}
                      className={`flex items-center gap-3 p-3 rounded-lg border cursor-pointer transition-colors ${
                        format === f ? 'border-indigo-500 bg-indigo-50' : 'border-gray-200 hover:bg-gray-50'
                      }`}
                    >
                      <input
                        type="radio"
                        name="format"
                        value={f}
                        checked={format === f}
                        onChange={() => setFormat(f)}
                        className="text-indigo-600 focus:ring-indigo-500"
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
                    className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
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
                  <svg role="status" aria-label="Export processing" className="h-5 w-5 text-indigo-500 animate-spin" viewBox="0 0 24 24" fill="none">
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
              {progressPct !== null && <div role="progressbar" aria-label="SSP export progress"
                aria-valuemin={0} aria-valuemax={100} aria-valuenow={progressPct}
                className="w-full bg-gray-200 rounded-full h-2">
                <div
                  className={`h-2 rounded-full transition-all duration-500 ${
                    status === 'completed' ? 'bg-green-500' : 'bg-indigo-500'
                  }`}
                  style={{ width: `${progressPct}%` }}
                />
              </div>}
              {progressPct === null && <p className="text-xs text-gray-500">Detailed progress is unavailable from the status endpoint.</p>}
              {status === 'completed' && exportId && (
                <AuthenticatedDownload
                  url={downloadExportUrl(systemId, exportId)}
                  fileName={`ssp-${systemId}.${format}`}
                  className="inline-flex items-center gap-2 mt-2 px-4 py-2 bg-green-600 text-white text-sm font-medium rounded-lg hover:bg-green-700 transition-colors"
                >
                  <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                  </svg>
                  Download {formatLabel[format]}
                </AuthenticatedDownload>
              )}
            </div>
          )}

          {/* Error */}
          {status === 'failed' && error && (
            <div role="alert" className="rounded-lg bg-red-50 border border-red-200 p-3">
              <p className="text-sm text-red-700">{error}</p>
            </div>
          )}

          {/* #180: ATO Package exports — PDF, XLSX, eMASS */}
          {(status === 'idle' || status === 'completed') && (
            <div className="border-t border-gray-100 pt-4">
              <p className="mb-2 text-sm font-medium text-gray-700">ATO Package Downloads</p>
              <div className="space-y-2">
                {([
                  { fmt: 'pdf' as const, label: 'ATO Package PDF', icon: '📕', tooltip: 'Full ATO package as PDF for submission' },
                  { fmt: 'xlsx' as const, label: 'ATO Package XLSX', icon: '📊', tooltip: 'Full ATO package as Excel workbook' },
                  { fmt: 'emass-xlsx' as const, label: 'eMASS-Ready XLSX', icon: '🏛️', tooltip: 'eMASS-formatted XLSX for direct eMASS import' },
                ]).map(({ fmt, label, icon, tooltip }) => (
                  <button
                    key={fmt}
                    onClick={() => void handlePackageExport(fmt)}
                    disabled={!session.ready || pkgExporting !== null}
                    title={tooltip}
                    className="flex w-full items-center gap-3 rounded-lg border border-gray-200 p-3 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                  >
                    <span className="text-base">{icon}</span>
                    <span className="flex-1 text-left">{label}</span>
                    {pkgExporting === fmt ? (
                      <svg className="h-4 w-4 animate-spin text-indigo-500" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                      </svg>
                    ) : (
                      <svg className="h-4 w-4 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                      </svg>
                    )}
                  </button>
                ))}
              </div>
              {pkgError && <div role="alert" className="mt-2 rounded border border-red-200 bg-red-50 p-2 text-xs text-red-700">{pkgError}</div>}
            </div>
          )}

          {/* Wave 7 #419: OSCAL Documents — SSP first-class, supplemental artifacts below */}
          {(status === 'idle' || status === 'completed') && (
            <div className="border-t border-gray-100 pt-4">
              <p className="mb-3 text-sm font-medium text-gray-700">OSCAL Documents</p>

              {/* OSCAL SSP — primary, first-class card */}
              <div className="mb-3 rounded-lg border border-indigo-100 bg-indigo-50 p-4">
                <div className="flex items-start justify-between gap-3">
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 mb-1">
                      <span className="text-sm font-semibold text-gray-900">OSCAL SSP</span>
                      <span className="inline-flex items-center rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600">
                        OSCAL 1.1.2
                      </span>
                      {/* #419: live validation badge — real data when summary loaded, skeleton otherwise */}
                      {oscalSspSummary ? (
                        <ValidationBadge
                          isValid={oscalSspSummary.validationStatus.isValid}
                          errorCount={oscalSspSummary.validationStatus.errors.length}
                          warningCount={oscalSspSummary.validationStatus.warnings.length}
                        />
                      ) : oscalSspSummaryLoading ? (
                        <span className="inline-flex items-center rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-400 animate-pulse">
                          Validating…
                        </span>
                      ) : null}
                    </div>
                    <p className="text-xs text-gray-500">
                      System Security Plan — primary OSCAL artifact for NIST SP 800-53 compliance
                    </p>
                    {/* Inline stats once loaded */}
                    {oscalSspSummary && (
                      <div className="flex gap-4 mt-2">
                        {[
                          { label: 'Controls',   v: oscalSspSummary.stats.controlCount       },
                          { label: 'Components', v: oscalSspSummary.stats.componentCount      },
                          { label: 'Inventory',  v: oscalSspSummary.stats.inventoryItemCount  },
                        ].map(({ label, v }) => (
                          <span key={label} className="text-xs text-gray-600">
                            <span className="font-semibold text-gray-800">{v}</span> {label}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                  <button
                    onClick={() => void handleOscalSspDownload()}
                    disabled={!session.ready || oscalExporting !== null}
                    className="flex-shrink-0 inline-flex items-center gap-2 rounded-lg border border-indigo-300 bg-white px-3 py-2 text-sm font-medium text-indigo-700 hover:bg-indigo-50 disabled:opacity-50"
                  >
                    {oscalExporting === 'oscal-ssp' ? (
                      <svg className="h-4 w-4 animate-spin text-indigo-500" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                      </svg>
                    ) : (
                      <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                      </svg>
                    )}
                    Download
                  </button>
                </div>
              </div>

              {/* Supplemental OSCAL artifacts */}
              <div className="space-y-2">
                {([
                  { type: 'oscal-poam' as const, label: 'OSCAL POA&M', icon: '📋' },
                  { type: 'oscal-assessment-results' as const, label: 'OSCAL Assessment Results', icon: '📊' },
                  { type: 'oscal-sap' as const, label: 'OSCAL SAP', icon: '📝' },
                ] as const).map(({ type, label, icon }) => (
                  <button key={type} onClick={() => void handleOscalDownload(type)} disabled={!session.ready || oscalExporting !== null}
                    className="flex w-full items-center gap-3 rounded-lg border border-gray-200 p-3 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50">
                    <span className="text-base">{icon}</span>
                    <span className="flex-1 text-left">{label}</span>
                    <span className="text-xs text-gray-400">OSCAL 1.1.2</span>
                    {oscalExporting === type ? (
                      <svg className="h-4 w-4 animate-spin text-indigo-500" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                      </svg>
                    ) : (
                      <svg className="h-4 w-4 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                      </svg>
                    )}
                  </button>
                ))}
              </div>
              {oscalError && <div role="alert" className="mt-2 rounded border border-red-200 bg-red-50 p-2 text-xs text-red-700">{oscalError}</div>}
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
                disabled={!session.ready}
                className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700"
              >
                Export
              </button>
            </>
          )}
          {status === 'submitting' && (
            <button
              disabled
              className="px-4 py-2 text-sm font-medium text-white bg-indigo-400 rounded-lg cursor-not-allowed"
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
