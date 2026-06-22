/**
 * Feature 076 T011: OSCAL SSP Import Wizard
 *
 * 4-step wizard: Upload -> Validate -> Preview -> Done
 *
 * Endpoints:
 *   POST /api/systems/{systemId}/oscal/import/ssp?mode=preview
 *   POST /api/systems/{systemId}/oscal/import/ssp?mode=full
 */
import { useState, useCallback } from 'react';
import axios from 'axios';

// -- Types -------------------------------------------------------------------

interface OscalImportEntityCounts {
  controlsToCreate: number;
  controlsToUpdate: number;
  controlsToSkip: number;
  componentsToCreate: number;
  inventoryItemsToCreate: number;
}

interface OscalControlSummary {
  controlId: string;
  action: 'create' | 'update' | 'skip';
  existingNarrative: string | null;
  incomingNarrative: string | null;
}

interface OscalImportPreview {
  schemaValid: boolean;
  validationErrors: string[];
  validationWarnings: string[];
  detectedOscalVersion: string;
  counts: OscalImportEntityCounts;
  controlSummaries: OscalControlSummary[];
}

interface OscalImportRunResult {
  importRunId: string;
  controlsCreated: number;
  controlsUpdated: number;
  controlsSkipped: number;
  controlsFailed: number;
  warnings: string[];
  errors: string[];
}

interface OscalImportWizardProps {
  systemId: string;
  onClose: () => void;
  onImportComplete?: (runId: string) => void;
}

// -- Step types --------------------------------------------------------------

type WizardStep = 1 | 2 | 3 | 4;

const STEP_LABELS: Record<WizardStep, string> = {
  1: 'Upload',
  2: 'Validate',
  3: 'Preview',
  4: 'Done',
};

function WizardSteps({ current }: { current: WizardStep }) {
  return (
    <nav aria-label="Wizard steps" className="flex items-center gap-0 mb-6">
      {([1, 2, 3, 4] as WizardStep[]).map((step, i) => (
        <div key={step} className="flex items-center">
          {i > 0 && <div className={`h-0.5 w-8 ${current >= step ? 'bg-indigo-600' : 'bg-gray-200'}`} />}
          <div className={`flex h-8 w-8 items-center justify-center rounded-full text-sm font-medium ${
            current === step ? 'bg-indigo-600 text-white' :
            current > step ? 'bg-indigo-100 text-indigo-700' :
            'bg-gray-100 text-gray-500'
          }`}>
            {current > step ? '\u2713' : step}
          </div>
          <span className={`ml-1 mr-2 text-xs font-medium ${current >= step ? 'text-indigo-700' : 'text-gray-400'}`}>
            {STEP_LABELS[step]}
          </span>
        </div>
      ))}
    </nav>
  );
}

// -- Reusable spinner --------------------------------------------------------

const Spinner = () => (
  <svg className="h-4 w-4 animate-spin" fill="none" viewBox="0 0 24 24">
    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"/>
    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"/>
  </svg>
);

// -- Validation badge --------------------------------------------------------

export function ValidationBadge({ valid, errorCount, warningCount }: {
  valid: boolean; errorCount: number; warningCount: number;
}) {
  if (valid && warningCount === 0)
    return <span className="inline-flex items-center gap-1 rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800">\u2713 Valid OSCAL 1.1.2</span>;
  if (!valid || errorCount > 0)
    return <span className="inline-flex items-center gap-1 rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-800">\u2717 {errorCount} schema error{errorCount !== 1 ? 's' : ''}</span>;
  return <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">\u26a0 {warningCount} warning{warningCount !== 1 ? 's' : ''}</span>;
}

// -- Action badge for control summary ----------------------------------------

function ActionBadge({ action }: { action: string }) {
  const cls: Record<string, string> = {
    create: 'bg-green-100 text-green-800',
    update: 'bg-blue-100 text-blue-800',
    skip: 'bg-gray-100 text-gray-600',
  };
  return <span className={`inline-block rounded px-1.5 py-0.5 text-xs font-medium ${cls[action] ?? 'bg-gray-100 text-gray-600'}`}>{action}</span>;
}

// -- Main wizard component ---------------------------------------------------

export default function OscalImportWizard({ systemId, onClose, onImportComplete }: OscalImportWizardProps) {
  const [step, setStep] = useState<WizardStep>(1);
  const [file, setFile] = useState<File | null>(null);
  const [dragging, setDragging] = useState(false);
  const [fileError, setFileError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [preview, setPreview] = useState<OscalImportPreview | null>(null);
  const [runResult, setRunResult] = useState<OscalImportRunResult | null>(null);
  const [fatalError, setFatalError] = useState<string | null>(null);
  const [showWarnings, setShowWarnings] = useState(false);

  const validateFile = (f: File): string | null => {
    const ext = f.name.split('.').pop()?.toLowerCase();
    if (ext !== 'json') return `Only .json files are accepted. Got: .${ext ?? 'unknown'}. OSCAL XML is not supported.`;
    if (f.size === 0) return 'File is empty.';
    return null;
  };

  const handleFile = (f: File) => {
    const err = validateFile(f);
    if (err) { setFileError(err); setFile(null); return; }
    setFile(f);
    setFileError(f.size > 10 * 1024 * 1024 ? '\u26a0 File is larger than 10\u00a0MB \u2014 upload may be slow.' : null);
  };

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault(); setDragging(false);
    const f = e.dataTransfer.files[0];
    if (f) handleFile(f);
  }, []);

  const handleUploadAndPreview = async () => {
    if (!file) return;
    setLoading(true); setFatalError(null);
    try {
      const form = new FormData();
      form.append('file', file);
      const res = await axios.post<{ ok: boolean; data: OscalImportPreview }>(
        `/api/systems/${systemId}/oscal/import/ssp?mode=preview`,
        form, { headers: { 'Content-Type': 'multipart/form-data' } }
      );
      setPreview(res.data.data);
      setStep(2);
    } catch (err: unknown) {
      setFatalError((err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Upload failed.');
    } finally { setLoading(false); }
  };

  const handleCommit = async () => {
    if (!file) return;
    setLoading(true); setFatalError(null);
    try {
      const form = new FormData();
      form.append('file', file);
      const res = await axios.post<{ ok: boolean; data: OscalImportRunResult }>(
        `/api/systems/${systemId}/oscal/import/ssp?mode=full`,
        form, { headers: { 'Content-Type': 'multipart/form-data' } }
      );
      setRunResult(res.data.data);
      setStep(4);
      onImportComplete?.(res.data.data.importRunId);
    } catch (err: unknown) {
      setFatalError((err as { response?: { data?: { message?: string } } })?.response?.data?.message ?? 'Import failed.');
    } finally { setLoading(false); }
  };

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

          {/* Step 1 - Upload */}
          {step === 1 && (
            <div className="space-y-4">
              <p className="text-sm text-gray-600">Upload an OSCAL 1.1.2 SSP JSON file to preview and import.</p>
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
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
                </svg>
                <p className="text-sm font-medium text-gray-700">Drag & drop or click to select</p>
                <p className="text-xs text-gray-500 mt-1">OSCAL 1.1.2 SSP JSON only (.json)</p>
                <input id="oscal-file-input" type="file" accept=".json" className="hidden"
                  onChange={(e) => { const f = e.target.files?.[0]; if (f) handleFile(f); }} />
              </div>
              {file && (
                <p className="text-sm text-gray-700">
                  Selected: <span className="font-medium">{file.name}</span>{' '}
                  ({(file.size / 1024).toFixed(1)}\u00a0KB)
                </p>
              )}
              {fileError && (
                <div className={`rounded-lg border p-3 text-sm ${
                  fileError.startsWith('\u26a0') ? 'border-amber-200 bg-amber-50 text-amber-800' : 'border-red-200 bg-red-50 text-red-700'
                }`}>{fileError}</div>
              )}
              {fatalError && <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{fatalError}</div>}
            </div>
          )}

          {/* Step 2 - Validate */}
          {step === 2 && preview && (
            <div className="space-y-4">
              <div className="flex items-center gap-3">
                <ValidationBadge
                  valid={preview.schemaValid}
                  errorCount={preview.validationErrors.length}
                  warningCount={preview.validationWarnings.length}
                />
                <span className="text-sm text-gray-500">OSCAL {preview.detectedOscalVersion}</span>
              </div>
              {preview.validationErrors.length > 0 && (
                <div className="rounded-lg border border-red-200 bg-red-50 p-3">
                  <p className="text-sm font-medium text-red-800 mb-1">Schema errors \u2014 fix the source file and re-upload:</p>
                  <ul className="text-xs text-red-700 space-y-0.5 list-disc pl-4">
                    {preview.validationErrors.map((e, i) => <li key={i}>{e}</li>)}
                  </ul>
                </div>
              )}
              {preview.validationWarnings.length > 0 && (
                <div className="rounded-lg border border-amber-200 bg-amber-50 p-3">
                  <button
                    className="text-sm font-medium text-amber-800 flex items-center gap-1"
                    onClick={() => setShowWarnings((v) => !v)}
                  >
                    {showWarnings ? '\u25bc' : '\u25b6'} {preview.validationWarnings.length} advisory warning{preview.validationWarnings.length !== 1 ? 's' : ''}
                  </button>
                  {showWarnings && (
                    <ul className="mt-2 text-xs text-amber-700 space-y-0.5 list-disc pl-4">
                      {preview.validationWarnings.map((w, i) => <li key={i}>{w}</li>)}
                    </ul>
                  )}
                </div>
              )}
              {fatalError && <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{fatalError}</div>}
            </div>
          )}

          {/* Step 3 - Preview */}
          {step === 3 && preview && (
            <div className="space-y-4">
              <div className="grid grid-cols-4 gap-3">
                {[
                  { label: 'Create', value: preview.counts.controlsToCreate, color: 'text-green-700' },
                  { label: 'Update', value: preview.counts.controlsToUpdate, color: 'text-blue-700' },
                  { label: 'Skip', value: preview.counts.controlsToSkip, color: 'text-gray-600' },
                  { label: 'Components', value: preview.counts.componentsToCreate, color: 'text-indigo-700' },
                ].map(({ label, value, color }) => (
                  <div key={label} className="rounded-lg border border-gray-200 p-3 text-center">
                    <p className={`text-2xl font-bold ${color}`}>{value}</p>
                    <p className="text-xs text-gray-500 mt-0.5">{label}</p>
                  </div>
                ))}
              </div>
              {preview.controlSummaries.length > 0 && (
                <div className="max-h-48 overflow-y-auto rounded-lg border border-gray-200">
                  <table className="min-w-full text-xs">
                    <thead className="bg-gray-50 sticky top-0">
                      <tr>
                        <th className="px-3 py-2 text-left font-medium text-gray-600">Control</th>
                        <th className="px-3 py-2 text-left font-medium text-gray-600">Action</th>
                        <th className="px-3 py-2 text-left font-medium text-gray-600">Incoming narrative</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {preview.controlSummaries.map((s) => (
                        <tr key={s.controlId} className="hover:bg-gray-50">
                          <td className="px-3 py-2 font-mono font-medium text-gray-900">{s.controlId}</td>
                          <td className="px-3 py-2"><ActionBadge action={s.action} /></td>
                          <td className="px-3 py-2 text-gray-600 truncate max-w-xs">
                            {s.incomingNarrative?.slice(0, 80) ?? '\u2014'}{(s.incomingNarrative?.length ?? 0) > 80 ? '\u2026' : ''}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              {fatalError && <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700">{fatalError}</div>}
            </div>
          )}

          {/* Step 4 - Done */}
          {step === 4 && (
            <div className="space-y-4">
              {runResult ? (
                <>
                  <div className="flex items-center gap-2 text-green-700">
                    <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    <span className="font-medium">Import complete</span>
                  </div>
                  <div className="grid grid-cols-3 gap-3">
                    {[
                      { label: 'Created', value: runResult.controlsCreated },
                      { label: 'Updated', value: runResult.controlsUpdated },
                      { label: 'Skipped', value: runResult.controlsSkipped },
                    ].map(({ label, value }) => (
                      <div key={label} className="rounded-lg border border-gray-200 p-3 text-center">
                        <p className="text-xl font-bold text-gray-900">{value}</p>
                        <p className="text-xs text-gray-500">{label}</p>
                      </div>
                    ))}
                  </div>
                  {runResult.warnings.length > 0 && (
                    <div className="rounded-lg border border-amber-200 bg-amber-50 p-3">
                      <p className="text-xs font-medium text-amber-800 mb-1">Warnings:</p>
                      <ul className="text-xs text-amber-700 list-disc pl-4 space-y-0.5">
                        {runResult.warnings.map((w, i) => <li key={i}>{w}</li>)}
                      </ul>
                    </div>
                  )}
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
              <button onClick={onClose} className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50">Close</button>
            ) : (
              <>
                <button onClick={onClose} disabled={loading} className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 disabled:opacity-50">Cancel</button>
                {step === 1 && (
                  <button
                    onClick={() => void handleUploadAndPreview()}
                    disabled={!file || loading || (!!fileError && !fileError.startsWith('\u26a0'))}
                    className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                  >
                    {loading && <Spinner />}
                    {loading ? 'Validating...' : 'Upload & Validate'}
                  </button>
                )}
                {step === 2 && (
                  <button
                    onClick={() => preview?.validationErrors.length === 0 && setStep(3)}
                    disabled={!preview || preview.validationErrors.length > 0}
                    className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50"
                  >
                    Review Preview
                  </button>
                )}
                {step === 3 && (
                  <button
                    onClick={() => void handleCommit()}
                    disabled={loading}
                    className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 flex items-center gap-2"
                  >
                    {loading && <Spinner />}
                    {loading ? 'Importing...' : 'Commit Import'}
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
