/**
 * Issue #419 — OSCAL SSP Import Wizard (contract-aligned revision)
 *
 * Aligned to the approved API contract in:
 *   specs/077-enhanced-evidence-automation/oscal-api-contract.md
 *
 * Endpoints:
 *   POST   /api/v1/systems/import/oscal-ssp                        — upload + session
 *   POST   /api/v1/systems/import/oscal-ssp/{sessionId}/commit     — commit session
 *
 * Session/preview object shape from contract:
 *   { sessionId, parseStatus, oscalVersion, validationStatus: { isValid, errors[], warnings[] },
 *     preview: { systemTitle, dateAuthorized, securityLevel, controlCount, componentCount, userCount },
 *     expiresAt }
 *
 * File guards:
 *   > 10 MB → amber warning toast (non-blocking)
 *   > 50 MB → hard block (contract §Notes-1)
 *
 * Commit gate:
 *   Blocked when validationStatus.errors.length > 0 (contract §Notes-commit-block)
 */
import { useState, useCallback } from 'react';
import apiClient from '../../api/client';

// ---------------------------------------------------------------------------
// Contract types
// ---------------------------------------------------------------------------

interface OscalValidationError {
  code: string;
  message: string;
  path: string;
}

interface OscalValidationWarning {
  code: string;
  message: string;
  path: string;
}

interface OscalValidationStatus {
  isValid: boolean;
  errors: OscalValidationError[];
  warnings: OscalValidationWarning[];
}

interface OscalImportSessionPreview {
  systemTitle: string;
  dateAuthorized: string;
  securityLevel: string;
  controlCount: number;
  componentCount: number;
  userCount: number;
}

/** Response from POST /api/v1/systems/import/oscal-ssp */
interface OscalImportSessionResponse {
  sessionId: string;
  parseStatus: 'Parsing' | 'Complete' | 'Failed';
  oscalVersion: string;
  documentType: string;
  validationStatus: OscalValidationStatus;
  preview: OscalImportSessionPreview;
  expiresAt: string;
}

/** Request body for POST /api/v1/systems/import/oscal-ssp/{sessionId}/commit */
interface OscalCommitRequest {
  targetSystemId: string | null;
  conflictResolution: 'merge' | 'overwrite';
  createNewSystem: boolean;
}

/** Response from POST .../commit */
interface OscalCommitResponse {
  systemId: string;
  systemTitle: string;
  controlsImported: number;
  componentsImported: number;
  isNewSystem: boolean;
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface OscalImportWizardProps {
  systemId?: string;            // optional: target for merge/overwrite. absent → create new
  onClose: () => void;
  onImportComplete?: (systemId: string, isNewSystem: boolean) => void;
}

// ---------------------------------------------------------------------------
// Wizard step type
// ---------------------------------------------------------------------------

type WizardStep = 1 | 2 | 3 | 4;

const STEP_LABELS: Record<WizardStep, string> = {
  1: 'Upload',
  2: 'Parse & Validate',
  3: 'Preview',
  4: 'Commit',
};

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

function WizardSteps({ current }: { current: WizardStep }) {
  return (
    <nav aria-label="Wizard steps" className="flex items-center gap-0 mb-6">
      {([1, 2, 3, 4] as WizardStep[]).map((step, i) => (
        <div key={step} className="flex items-center">
          {i > 0 && <div className={`h-0.5 w-8 ${current >= step ? 'bg-indigo-600' : 'bg-gray-200'}`} />}
          <div className={`flex h-8 w-8 items-center justify-center rounded-full text-sm font-medium ${
            current === step ? 'bg-indigo-600 text-white' :
            current > step  ? 'bg-indigo-100 text-indigo-700' :
                              'bg-gray-100 text-gray-500'
          }`}>
            {current > step ? '✓' : step}
          </div>
          <span className={`ml-1 mr-2 text-xs font-medium ${current >= step ? 'text-indigo-700' : 'text-gray-400'}`}>
            {STEP_LABELS[step]}
          </span>
        </div>
      ))}
    </nav>
  );
}

const Spinner = () => (
  <svg className="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">
    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"/>
    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"/>
  </svg>
);

/** Shared validation status badge — also exported for use in ExportSspDialog */
export function ValidationBadge({
  isValid,
  errorCount,
  warningCount,
}: {
  isValid: boolean;
  errorCount: number;
  warningCount: number;
}) {
  if (isValid && warningCount === 0)
    return <span className="inline-flex items-center gap-1 rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800">✓ Valid OSCAL 1.1.2</span>;
  if (!isValid || errorCount > 0)
    return <span className="inline-flex items-center gap-1 rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-800">✗ {errorCount} error{errorCount !== 1 ? 's' : ''}</span>;
  return <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">⚠ {warningCount} warning{warningCount !== 1 ? 's' : ''}</span>;
}

// ---------------------------------------------------------------------------
// Main wizard
// ---------------------------------------------------------------------------

export default function OscalImportWizard({ systemId, onClose, onImportComplete }: OscalImportWizardProps) {
  const [step, setStep] = useState<WizardStep>(1);
  const [file, setFile] = useState<File | null>(null);
  const [dragging, setDragging] = useState(false);
  const [fileError, setFileError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [session, setSession] = useState<OscalImportSessionResponse | null>(null);
  const [commitResult, setCommitResult] = useState<OscalCommitResponse | null>(null);
  const [fatalError, setFatalError] = useState<string | null>(null);
  const [showWarnings, setShowWarnings] = useState(false);
  const [conflictResolution, setConflictResolution] = useState<'merge' | 'overwrite'>('merge');

  // -- File guard -----------------------------------------------------------

  const MB50 = 50 * 1024 * 1024;
  const MB10 = 10 * 1024 * 1024;

  const validateFile = (f: File): { blocking: boolean; message: string } | null => {
    const ext = f.name.split('.').pop()?.toLowerCase();
    if (ext !== 'json')
      return { blocking: true, message: `Only .json files are accepted. Got: .${ext ?? 'unknown'}. OSCAL XML is not supported.` };
    if (f.size === 0)
      return { blocking: true, message: 'File is empty.' };
    if (f.size > MB50)
      return { blocking: true, message: `File exceeds the 50 MB limit (${(f.size / 1024 / 1024).toFixed(1)} MB). Reduce file size before uploading.` };
    if (f.size > MB10)
      return { blocking: false, message: `⚠ File is larger than 10 MB (${(f.size / 1024 / 1024).toFixed(1)} MB) — upload may be slow.` };
    return null;
  };

  const handleFile = useCallback((f: File) => {
    const result = validateFile(f);
    if (result?.blocking) {
      setFileError(result.message);
      setFile(null);
      return;
    }
    setFile(f);
    setFileError(result?.message ?? null);   // non-blocking warning
  }, []);   // eslint-disable-line react-hooks/exhaustive-deps -- validateFile is pure, stable across renders

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragging(false);
    const f = e.dataTransfer.files[0];
    if (f) handleFile(f);
  }, [handleFile]);

  // -- Step 1 → 2 : Upload --------------------------------------------------

  const handleUpload = async () => {
    if (!file) return;
    setLoading(true);
    setFatalError(null);
    try {
      const form = new FormData();
      form.append('file', file);
      if (systemId) form.append('systemId', systemId);

      const res = await apiClient.post<OscalImportSessionResponse>(
        '/api/v1/systems/import/oscal-ssp',
        form,
        { headers: { 'Content-Type': 'multipart/form-data' } },
      );
      setSession(res.data);
      setStep(2);
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
        ?? 'Upload failed — check file format and try again.';
      setFatalError(msg);
    } finally {
      setLoading(false);
    }
  };

  // -- Step 3 → 4 : Commit --------------------------------------------------

  const handleCommit = async () => {
    if (!session) return;
    setLoading(true);
    setFatalError(null);
    try {
      const body: OscalCommitRequest = {
        targetSystemId: systemId ?? null,
        conflictResolution,
        createNewSystem: !systemId,
      };
      const res = await apiClient.post<OscalCommitResponse>(
        `/api/v1/systems/import/oscal-ssp/${session.sessionId}/commit`,
        body,
      );
      setCommitResult(res.data);
      setStep(4);
      onImportComplete?.(res.data.systemId, res.data.isNewSystem);
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number; data?: { message?: string } } })?.response?.status;
      if (status === 409) {
        setFatalError('Commit blocked — the session has validation errors that must be resolved before committing.');
      } else if (status === 404) {
        setFatalError('Session expired (sessions time out after 30 minutes). Please re-upload the file.');
      } else {
        setFatalError(
          (err as { response?: { data?: { message?: string } } })?.response?.data?.message
          ?? 'Commit failed.',
        );
      }
    } finally {
      setLoading(false);
    }
  };

  // -- Helpers --------------------------------------------------------------

  const hasErrors   = (session?.validationStatus.errors.length  ?? 0) > 0;
  const hasWarnings = (session?.validationStatus.warnings.length ?? 0) > 0;
  const commitBlocked = hasErrors;

  const formatDate = (iso: string) => {
    try {
      return new Date(iso).toLocaleString(undefined, {
        year: 'numeric', month: 'long', day: 'numeric',
        hour: '2-digit', minute: '2-digit',
      });
    } catch { return iso; }
  };

  // -- Render ---------------------------------------------------------------

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm">
      <div className="w-full max-w-2xl rounded-xl bg-white shadow-2xl border border-gray-200 overflow-hidden">

        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 bg-gray-50 border-b border-gray-200">
          <h2 className="text-base font-semibold text-gray-900">Import OSCAL SSP</h2>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600" aria-label="Close">
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Body */}
        <div className="px-6 py-5">
          <WizardSteps current={step} />

          {/* ─── Step 1 — Upload ─────────────────────────────────────────── */}
          {step === 1 && (
            <div className="space-y-4">
              <p className="text-sm text-gray-600">
                Upload an OSCAL 1.1.2 SSP JSON file to start an import session.
              </p>

              {/* Drop zone */}
              <div
                className={`flex flex-col items-center justify-center rounded-lg border-2 border-dashed p-8 cursor-pointer transition-colors ${
                  dragging ? 'border-indigo-400 bg-indigo-50' : 'border-gray-300 hover:border-indigo-300'
                }`}
                onDragOver={(e) => { e.preventDefault(); setDragging(true); }}
                onDragLeave={() => setDragging(false)}
                onDrop={handleDrop}
                onClick={() => document.getElementById('oscal-file-input')?.click()}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => { if (e.key === 'Enter') document.getElementById('oscal-file-input')?.click(); }}
              >
                <svg className="h-10 w-10 text-gray-400 mb-2" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
                    d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
                </svg>
                <p className="text-sm font-medium text-gray-700">Drag & drop or click to select</p>
                <p className="text-xs text-gray-500 mt-1">OSCAL 1.1.2 SSP JSON only (.json) · max 50 MB</p>
                <input
                  id="oscal-file-input"
                  type="file"
                  accept=".json"
                  className="hidden"
                  onChange={(e) => { const f = e.target.files?.[0]; if (f) handleFile(f); }}
                />
              </div>

              {file && !fileError?.startsWith('File exceeds') && !fileError?.startsWith('Only') && !fileError?.startsWith('File is empty') && (
                <p className="text-sm text-gray-700">
                  Selected: <span className="font-medium">{file.name}</span>{' '}
                  ({(file.size / 1024).toFixed(1)}&nbsp;KB)
                </p>
              )}

              {/* Inline file feedback */}
              {fileError && (
                <div className={`rounded-lg border p-3 text-sm ${
                  fileError.startsWith('⚠')
                    ? 'border-amber-200 bg-amber-50 text-amber-800'
                    : 'border-red-200 bg-red-50 text-red-700'
                }`}>
                  {fileError}
                </div>
              )}

              {fatalError && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                  {fatalError}
                </div>
              )}
            </div>
          )}

          {/* ─── Step 2 — Parse & Validate ───────────────────────────────── */}
          {step === 2 && session && (
            <div className="space-y-4">
              {/* Version + badge row */}
              <div className="flex items-center gap-3">
                <ValidationBadge
                  isValid={session.validationStatus.isValid}
                  errorCount={session.validationStatus.errors.length}
                  warningCount={session.validationStatus.warnings.length}
                />
                <span className="text-sm text-gray-500">
                  OSCAL {session.oscalVersion} · {session.documentType}
                </span>
              </div>

              {/* Blocking errors */}
              {session.validationStatus.errors.length > 0 && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3">
                  <p className="text-sm font-medium text-red-800 mb-2">
                    Schema errors — fix the source file and re-upload:
                  </p>
                  <ul className="text-xs text-red-700 space-y-1 list-disc pl-4">
                    {session.validationStatus.errors.map((e, i) => (
                      <li key={i}>
                        <span className="font-mono text-red-600">[{e.code}]</span> {e.message}
                        {e.path && <span className="block text-red-500 mt-0.5">↳ {e.path}</span>}
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {/* Non-blocking warnings */}
              {session.validationStatus.warnings.length > 0 && (
                <div className="rounded-lg border border-amber-200 bg-amber-50 p-3">
                  <button
                    className="text-sm font-medium text-amber-800 flex items-center gap-1"
                    onClick={() => setShowWarnings((v) => !v)}
                  >
                    {showWarnings ? '▼' : '▶'}{' '}
                    {session.validationStatus.warnings.length} advisory warning
                    {session.validationStatus.warnings.length !== 1 ? 's' : ''} (non-blocking)
                  </button>
                  {showWarnings && (
                    <ul className="mt-2 text-xs text-amber-700 space-y-1 list-disc pl-4">
                      {session.validationStatus.warnings.map((w, i) => (
                        <li key={i}>
                          <span className="font-mono text-amber-600">[{w.code}]</span> {w.message}
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              )}

              {/* Session TTL notice */}
              <p className="text-xs text-gray-400">
                Session expires at{' '}
                <span className="font-medium">{formatDate(session.expiresAt)}</span>
                {' '}— commit before then or re-upload.
              </p>

              {fatalError && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                  {fatalError}
                </div>
              )}
            </div>
          )}

          {/* ─── Step 3 — Preview ────────────────────────────────────────── */}
          {step === 3 && session && (
            <div className="space-y-4">
              {/* System metadata */}
              <div className="rounded-lg border border-gray-200 bg-gray-50 p-4 space-y-2">
                <h3 className="text-sm font-semibold text-gray-900">{session.preview.systemTitle}</h3>
                <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-xs text-gray-600">
                  <div>
                    <dt className="inline font-medium text-gray-700">Date authorized: </dt>
                    <dd className="inline">{session.preview.dateAuthorized || '—'}</dd>
                  </div>
                  <div>
                    <dt className="inline font-medium text-gray-700">Security level: </dt>
                    <dd className="inline capitalize">{session.preview.securityLevel}</dd>
                  </div>
                </dl>
              </div>

              {/* Count cards */}
              <div className="grid grid-cols-3 gap-3">
                {[
                  { label: 'Controls',   value: session.preview.controlCount,   color: 'text-indigo-700' },
                  { label: 'Components', value: session.preview.componentCount, color: 'text-blue-700'   },
                  { label: 'Users',      value: session.preview.userCount,      color: 'text-gray-700'   },
                ].map(({ label, value, color }) => (
                  <div key={label} className="rounded-lg border border-gray-200 p-3 text-center">
                    <p className={`text-2xl font-bold ${color}`}>{value}</p>
                    <p className="text-xs text-gray-500 mt-0.5">{label}</p>
                  </div>
                ))}
              </div>

              {/* Warnings summary (if any) */}
              {hasWarnings && (
                <div className="rounded-lg border border-amber-200 bg-amber-50 p-3">
                  <p className="text-xs font-medium text-amber-800">
                    {session.validationStatus.warnings.length} advisory warning
                    {session.validationStatus.warnings.length !== 1 ? 's' : ''} present — commit will proceed.
                  </p>
                </div>
              )}

              {/* Conflict resolution — only when merging into an existing system */}
              {systemId && (
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-2">Conflict resolution</label>
                  <div className="space-y-2">
                    {(['merge', 'overwrite'] as const).map((opt) => (
                      <label key={opt} className={`flex items-start gap-3 p-3 rounded-lg border cursor-pointer transition-colors ${
                        conflictResolution === opt ? 'border-indigo-500 bg-indigo-50' : 'border-gray-200 hover:bg-gray-50'
                      }`}>
                        <input
                          type="radio"
                          name="conflictResolution"
                          value={opt}
                          checked={conflictResolution === opt}
                          onChange={() => setConflictResolution(opt)}
                          className="mt-0.5 text-indigo-600"
                        />
                        <span>
                          <span className="text-sm font-medium text-gray-900 capitalize">{opt}</span>
                          <span className="block text-xs text-gray-500 mt-0.5">
                            {opt === 'merge'
                              ? 'Import new controls; preserve existing narratives where no incoming value.'
                              : 'Overwrite all existing values with the imported file.'}
                          </span>
                        </span>
                      </label>
                    ))}
                  </div>
                </div>
              )}

              {fatalError && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                  {fatalError}
                </div>
              )}
            </div>
          )}

          {/* ─── Step 4 — Commit result ──────────────────────────────────── */}
          {step === 4 && (
            <div className="space-y-4">
              {commitResult ? (
                <>
                  <div className="flex items-center gap-2 text-green-700">
                    <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    <span className="font-medium">
                      {commitResult.isNewSystem ? 'System created' : 'System updated'} — {commitResult.systemTitle}
                    </span>
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    {[
                      { label: 'Controls imported',   value: commitResult.controlsImported   },
                      { label: 'Components imported', value: commitResult.componentsImported },
                    ].map(({ label, value }) => (
                      <div key={label} className="rounded-lg border border-gray-200 p-3 text-center">
                        <p className="text-2xl font-bold text-gray-900">{value}</p>
                        <p className="text-xs text-gray-500 mt-0.5">{label}</p>
                      </div>
                    ))}
                  </div>
                </>
              ) : fatalError ? (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">
                  <p className="font-medium mb-1">Import failed</p>
                  <p>{fatalError}</p>
                </div>
              ) : null}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex justify-between px-6 py-3 bg-gray-50 border-t border-gray-200">
          <div>
            {step > 1 && step < 4 && (
              <button
                onClick={() => setStep((s) => (s - 1) as WizardStep)}
                disabled={loading}
                className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50"
              >
                Back
              </button>
            )}
          </div>
          <div className="flex gap-2">
            {step === 4 ? (
              <button
                onClick={onClose}
                className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
              >
                Close
              </button>
            ) : (
              <>
                <button
                  onClick={onClose}
                  disabled={loading}
                  className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50"
                >
                  Cancel
                </button>

                {/* Step 1: Upload */}
                {step === 1 && (
                  <button
                    onClick={() => void handleUpload()}
                    disabled={!file || loading || (!!fileError && !fileError.startsWith('⚠'))}
                    className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                  >
                    {loading && <Spinner />}
                    {loading ? 'Uploading...' : 'Upload File'}
                  </button>
                )}

                {/* Step 2: Advance to preview (disabled if errors) */}
                {step === 2 && (
                  <button
                    onClick={() => !commitBlocked && setStep(3)}
                    disabled={commitBlocked}
                    title={commitBlocked ? 'Fix all schema errors before proceeding' : undefined}
                    className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50"
                  >
                    Review Preview
                  </button>
                )}

                {/* Step 3: Commit */}
                {step === 3 && (
                  <button
                    onClick={() => void handleCommit()}
                    disabled={loading || commitBlocked}
                    title={commitBlocked ? 'Commit blocked — validation errors present' : undefined}
                    className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                  >
                    {loading && <Spinner />}
                    {loading ? 'Committing...' : 'Commit Import'}
                  </button>
                )}
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
