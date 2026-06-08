/**
 * T276 / T277 / T278 / T279: SSP PDF Import Wizard
 *
 * 4-step wizard: Upload → AI Extraction Review → Corrections → Commit
 * Accessed via the "Import SSP PDF" CTA in ImportedDocumentsView.
 *
 * Endpoints:
 *   POST  /api/onboarding/imports/ssp-pdf/upload                          → { sessionId, batchId }
 *   GET   /api/onboarding/imports/ssp-pdf/batches/{batchId}/summary       → batch status
 *   GET   /api/onboarding/imports/ssp-pdf/{sessionId}/extraction          → AI extraction results
 *   PUT   /api/onboarding/imports/ssp-pdf/{sessionId}/corrections         → save corrections
 *   POST  /api/onboarding/imports/ssp-pdf/{sessionId}/import              → commit
 */
import { useState, useCallback, useEffect } from 'react';
import axios from 'axios';
import { useNavigate } from 'react-router-dom';

const sspApi = axios.create({ baseURL: '/api/onboarding/imports/ssp-pdf' });

// ── Types ────────────────────────────────────────────────────────────────────

type BatchStatus = 'Pending' | 'InProgress' | 'Complete' | 'Failed';
type ConfidenceLevel = 'High' | 'Medium' | 'Low';

interface BatchSummary {
  batchId: string;
  status: BatchStatus;
  fileCount: number;
  processedCount: number;
  failedCount: number;
  sessions: Array<{ sessionId: string; fileName: string; status: string }>;
}

interface ExtractionField {
  fieldName: string;
  value: string;
  confidence: ConfidenceLevel;
  aiReason?: string;
}

interface ExtractionSection {
  sectionName: string;
  fields: ExtractionField[];
}

interface ExtractionResult {
  sessionId: string;
  sections: ExtractionSection[];
}

interface CommitResult {
  status: 'Pending' | 'InProgress' | 'Complete' | 'Failed';
  totalItems?: number;
  correctedItems?: number;
  acceptedItems?: number;
  message?: string;
}

// ── Wizard steps nav ─────────────────────────────────────────────────────────

type WizardStep = 1 | 2 | 3 | 4;

const STEP_LABELS: Record<WizardStep, string> = {
  1: 'Upload',
  2: 'AI Extraction',
  3: 'Corrections',
  4: 'Commit',
};

const CONFIDENCE_BADGE: Record<ConfidenceLevel, string> = {
  High: 'bg-green-100 text-green-700 border-green-200',
  Medium: 'bg-yellow-100 text-yellow-700 border-yellow-200',
  Low: 'bg-red-100 text-red-700 border-red-200',
};

function WizardSteps({ current }: { current: WizardStep }) {
  return (
    <nav aria-label="Import steps" className="flex items-center gap-0 mb-6">
      {([1, 2, 3, 4] as WizardStep[]).map((step, i) => (
        <div key={step} className="flex items-center">
          {i > 0 && <div className={`h-0.5 w-8 ${current >= step ? 'bg-indigo-600' : 'bg-gray-200'}`} />}
          <div className={`flex h-8 w-8 items-center justify-center rounded-full text-sm font-medium ${
            current === step ? 'bg-indigo-600 text-white' :
            current > step ? 'bg-indigo-100 text-indigo-700' :
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

// ── Step 1: Upload ───────────────────────────────────────────────────────────

const MAX_FILES = 10;
const MAX_FILE_SIZE_MB = 25;

function StepUpload({ onNext }: { onNext: (sessionId: string, batchId: string) => void }) {
  const [files, setFiles] = useState<File[]>([]);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [batchSummary, setBatchSummary] = useState<BatchSummary | null>(null);

  const handleFilesChange = (incoming: FileList | null) => {
    if (!incoming) return;
    const arr = Array.from(incoming);
    const errors: string[] = [];

    const filtered = arr.filter((f) => {
      if (!f.name.toLowerCase().endsWith('.pdf')) {
        errors.push(`${f.name}: only PDF files accepted.`);
        return false;
      }
      if (f.size > MAX_FILE_SIZE_MB * 1024 * 1024) {
        errors.push(`${f.name}: exceeds ${MAX_FILE_SIZE_MB} MB limit.`);
        return false;
      }
      return true;
    });

    if (errors.length > 0) setError(errors.join(' '));
    else setError(null);

    const combined = [...files, ...filtered].slice(0, MAX_FILES);
    setFiles(combined);
  };

  const removeFile = (idx: number) => {
    setFiles((prev) => prev.filter((_, i) => i !== idx));
  };

  const handleUpload = async () => {
    if (files.length === 0) return;
    setUploading(true);
    setError(null);

    try {
      const form = new FormData();
      files.forEach((f) => form.append('files', f));
      const { data } = await sspApi.post<{ ok: boolean; data: { sessionId: string; batchId: string } }>(
        '/upload', form, { headers: { 'Content-Type': 'multipart/form-data' } },
      );
      const { sessionId, batchId } = data.data;

      // Poll batch summary until complete
      let attempts = 0;
      const poll = async (): Promise<void> => {
        const { data: summaryResp } = await sspApi.get<{ ok: boolean; data: BatchSummary }>(
          `/batches/${batchId}/summary`,
        );
        setBatchSummary(summaryResp.data);
        if (summaryResp.data.status === 'InProgress' || summaryResp.data.status === 'Pending') {
          if (++attempts < 60) {
            await new Promise((r) => setTimeout(r, 2000));
            return poll();
          }
          throw new Error('Batch processing timed out.');
        }
        if (summaryResp.data.status === 'Failed') {
          throw new Error('Batch processing failed.');
        }
        onNext(sessionId, batchId);
      };

      await poll();
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Upload failed.');
    } finally {
      setUploading(false);
    }
  };

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-semibold">Step 1: Upload SSP PDFs</h2>
      <p className="text-sm text-gray-500">Upload up to {MAX_FILES} PDFs. AI will extract SSP fields for your review.</p>

      <label
        htmlFor="ssp-pdf-input"
        className="flex cursor-pointer flex-col items-center justify-center rounded-lg border-2 border-dashed border-gray-300 p-8 hover:border-indigo-300 transition-colors"
      >
        <svg className="mb-3 h-9 w-9 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5m-13.5-9L12 3m0 0l4.5 4.5M12 3v13.5" />
        </svg>
        <p className="text-sm font-medium text-gray-700">Click to select PDFs</p>
        <p className="mt-1 text-xs text-gray-400">Up to {MAX_FILES} files, {MAX_FILE_SIZE_MB} MB each</p>
      </label>
      <input
        id="ssp-pdf-input"
        type="file"
        accept=".pdf"
        multiple
        className="hidden"
        onChange={(e) => handleFilesChange(e.target.files)}
      />

      {files.length > 0 && (
        <ul className="divide-y rounded border text-sm">
          {files.map((f, i) => (
            <li key={i} className="flex items-center justify-between px-3 py-2">
              <span className="truncate max-w-xs">{f.name}</span>
              <div className="flex items-center gap-3">
                <span className="text-xs text-gray-400">{(f.size / 1024).toFixed(0)} KB</span>
                <button onClick={() => removeFile(i)} className="text-gray-400 hover:text-red-600" aria-label="Remove">✕</button>
              </div>
            </li>
          ))}
        </ul>
      )}

      {batchSummary && batchSummary.status === 'InProgress' && (
        <div className="flex items-center gap-2 text-sm text-amber-700">
          <span className="h-3 w-3 animate-spin rounded-full border-2 border-amber-500 border-t-transparent" />
          Processing {batchSummary.processedCount} / {batchSummary.fileCount} files…
        </div>
      )}

      {error && <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{error}</div>}

      <div className="flex justify-end">
        <button
          disabled={files.length === 0 || uploading}
          onClick={handleUpload}
          className="inline-flex items-center gap-2 rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          {uploading ? (
            <><span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />Processing…</>
          ) : `Upload ${files.length > 0 ? `(${files.length})` : ''}`}
        </button>
      </div>
    </div>
  );
}

// ── Step 2: AI Extraction Review ─────────────────────────────────────────────

function StepExtraction({ sessionId, onNext }: { sessionId: string; onNext: () => void }) {
  const [extraction, setExtraction] = useState<ExtractionResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [openSections, setOpenSections] = useState<Set<string>>(new Set());

  useEffect(() => {
    let cancelled = false;
    sspApi.get<{ ok: boolean; data: ExtractionResult }>(`/${sessionId}/extraction`)
      .then(({ data }) => {
        if (!cancelled) {
          setExtraction(data.data);
          // Expand sections with low-confidence fields by default
          const lowSections = new Set(
            data.data.sections
              .filter((s) => s.fields.some((f) => f.confidence === 'Low'))
              .map((s) => s.sectionName),
          );
          setOpenSections(lowSections.size > 0 ? lowSections : new Set([data.data.sections[0]?.sectionName ?? '']));
        }
      })
      .catch((e: unknown) => {
        if (!cancelled) setError((e as Error).message ?? 'Failed to load extraction.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [sessionId]);

  const toggleSection = (name: string) => {
    setOpenSections((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  };

  if (loading) {
    return (
      <div className="py-10 text-center text-sm text-gray-500">
        <span className="inline-block h-5 w-5 animate-spin rounded-full border-2 border-indigo-500 border-t-transparent mr-2" />
        Loading AI extraction…
      </div>
    );
  }

  if (error) {
    return <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{error}</div>;
  }

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-semibold">Step 2: AI Extraction Review</h2>
      <p className="text-sm text-gray-500">
        Review AI-extracted fields. Sections with <span className="font-medium text-red-600">red</span> badges have low-confidence fields that may need correction.
      </p>

      <div className="space-y-2 max-h-96 overflow-y-auto pr-1">
        {extraction?.sections.map((section) => {
          const hasLow = section.fields.some((f) => f.confidence === 'Low');
          const isOpen = openSections.has(section.sectionName);
          return (
            <div key={section.sectionName} className={`rounded border ${hasLow ? 'border-red-200' : 'border-gray-200'}`}>
              <button
                onClick={() => toggleSection(section.sectionName)}
                className="flex w-full items-center justify-between px-4 py-3 text-left hover:bg-gray-50 transition-colors"
              >
                <div className="flex items-center gap-2">
                  <span className="font-medium text-sm text-gray-900">{section.sectionName}</span>
                  {hasLow && (
                    <span className="rounded-full bg-red-100 px-2 py-0.5 text-xs font-medium text-red-700">
                      Low confidence
                    </span>
                  )}
                </div>
                <svg className={`h-4 w-4 text-gray-400 transition-transform ${isOpen ? 'rotate-90' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                </svg>
              </button>

              {isOpen && (
                <div className="border-t px-4 py-3 space-y-2">
                  {section.fields.map((field) => (
                    <div key={field.fieldName} className={`flex items-start justify-between gap-3 rounded p-2 ${field.confidence === 'Low' ? 'bg-red-50' : ''}`}>
                      <div className="flex-1 min-w-0">
                        <div className="text-xs font-medium text-gray-500 uppercase tracking-wide mb-0.5">{field.fieldName}</div>
                        <div className="text-sm text-gray-900 break-words">{field.value || <span className="text-gray-400 italic">empty</span>}</div>
                      </div>
                      <span className={`shrink-0 rounded border px-2 py-0.5 text-xs font-medium ${CONFIDENCE_BADGE[field.confidence]}`}>
                        {field.confidence}
                      </span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          );
        })}
      </div>

      <div className="flex justify-end">
        <button
          onClick={onNext}
          className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
        >
          Proceed to Corrections →
        </button>
      </div>
    </div>
  );
}

// ── Step 3: Corrections ──────────────────────────────────────────────────────

interface DirtyFields {
  [sectionName: string]: {
    [fieldName: string]: string;
  };
}

function StepCorrections({ sessionId, onNext }: { sessionId: string; onNext: () => void }) {
  const [extraction, setExtraction] = useState<ExtractionResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dirty, setDirty] = useState<DirtyFields>({});
  const [validationErrors, setValidationErrors] = useState<string[]>([]);

  useEffect(() => {
    let cancelled = false;
    sspApi.get<{ ok: boolean; data: ExtractionResult }>(`/${sessionId}/extraction`)
      .then(({ data }) => { if (!cancelled) setExtraction(data.data); })
      .catch((e: unknown) => { if (!cancelled) setError((e as Error).message ?? 'Failed to load.'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [sessionId]);

  const getValue = (sectionName: string, fieldName: string, originalValue: string): string => {
    return dirty[sectionName]?.[fieldName] ?? originalValue;
  };

  const handleChange = (sectionName: string, fieldName: string, value: string) => {
    setDirty((prev) => ({
      ...prev,
      [sectionName]: { ...(prev[sectionName] ?? {}), [fieldName]: value },
    }));
  };

  const isDirty = (sectionName: string, fieldName: string, originalValue: string): boolean => {
    const dirtyVal = dirty[sectionName]?.[fieldName];
    return dirtyVal !== undefined && dirtyVal !== originalValue;
  };

  const validate = (): boolean => {
    const errors: string[] = [];
    // Required-field validation: mark any field with empty value that had non-empty original
    extraction?.sections.forEach((section) => {
      section.fields.forEach((field) => {
        const val = getValue(section.sectionName, field.fieldName, field.value);
        if (!val.trim() && field.value.trim()) {
          errors.push(`${section.sectionName} › ${field.fieldName} cannot be empty.`);
        }
      });
    });
    setValidationErrors(errors);
    return errors.length === 0;
  };

  const handleSave = async () => {
    if (!validate()) return;
    setSaving(true);
    setError(null);
    try {
      // Build corrections payload — only changed fields
      const corrections: { sectionName: string; fieldName: string; correctedValue: string }[] = [];
      for (const [sectionName, fields] of Object.entries(dirty)) {
        for (const [fieldName, value] of Object.entries(fields)) {
          const original = extraction?.sections
            .find((s) => s.sectionName === sectionName)
            ?.fields.find((f) => f.fieldName === fieldName)?.value ?? '';
          if (value !== original) {
            corrections.push({ sectionName, fieldName, correctedValue: value });
          }
        }
      }
      await sspApi.put(`/${sessionId}/corrections`, { corrections });
      onNext();
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Save failed.');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="py-10 text-center text-sm text-gray-500">
        <span className="inline-block h-5 w-5 animate-spin rounded-full border-2 border-indigo-500 border-t-transparent mr-2" />
        Loading…
      </div>
    );
  }

  const totalDirty = Object.values(dirty).reduce((acc, fields) => acc + Object.keys(fields).length, 0);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">Step 3: Corrections</h2>
        {totalDirty > 0 && (
          <span className="inline-flex items-center rounded-full bg-amber-100 px-2.5 py-0.5 text-xs font-medium text-amber-800">
            {totalDirty} unsaved change{totalDirty !== 1 ? 's' : ''}
          </span>
        )}
      </div>
      <p className="text-sm text-gray-500">Edit any fields inline. Only changed fields will be sent.</p>

      {validationErrors.length > 0 && (
        <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          <p className="font-medium mb-1">Please fix these errors:</p>
          <ul className="list-disc list-inside space-y-0.5">
            {validationErrors.map((e, i) => <li key={i}>{e}</li>)}
          </ul>
        </div>
      )}

      {error && <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{error}</div>}

      <div className="max-h-96 overflow-y-auto space-y-3 pr-1">
        {extraction?.sections.map((section) => (
          <div key={section.sectionName} className="rounded border border-gray-200">
            <div className="bg-gray-50 px-4 py-2 text-xs font-semibold uppercase tracking-wide text-gray-600 rounded-t border-b">
              {section.sectionName}
            </div>
            <div className="divide-y px-4">
              {section.fields.map((field) => {
                const val = getValue(section.sectionName, field.fieldName, field.value);
                const changed = isDirty(section.sectionName, field.fieldName, field.value);
                return (
                  <div key={field.fieldName} className="py-3">
                    <label className="flex items-center gap-2 text-xs font-medium text-gray-500 uppercase tracking-wide mb-1.5">
                      {field.fieldName}
                      {changed && (
                        <span className="rounded-full bg-amber-100 px-1.5 py-0.5 text-amber-700 text-xs normal-case font-normal">
                          edited
                        </span>
                      )}
                      <span className={`ml-auto rounded border px-1.5 py-0.5 text-xs ${CONFIDENCE_BADGE[field.confidence]}`}>
                        {field.confidence}
                      </span>
                    </label>
                    <textarea
                      value={val}
                      onChange={(e) => handleChange(section.sectionName, field.fieldName, e.target.value)}
                      rows={val.length > 120 ? 4 : 2}
                      className="w-full rounded border border-gray-200 px-3 py-2 text-sm focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-400 resize-none"
                    />
                  </div>
                );
              })}
            </div>
          </div>
        ))}
      </div>

      <div className="flex justify-end">
        <button
          disabled={saving}
          onClick={handleSave}
          className="inline-flex items-center gap-2 rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          {saving ? (
            <><span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />Saving…</>
          ) : 'Save and Continue →'}
        </button>
      </div>
    </div>
  );
}

// ── Step 4: Commit ───────────────────────────────────────────────────────────

function StepCommit({ sessionId }: { sessionId: string }) {
  const navigate = useNavigate();
  const [committing, setCommitting] = useState(false);
  const [result, setResult] = useState<CommitResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleCommit = async () => {
    setCommitting(true);
    setError(null);
    try {
      const { data } = await sspApi.post<{ ok: boolean; data: CommitResult }>(`/${sessionId}/import`);
      setResult(data.data);
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Commit failed.');
      setCommitting(false);
    }
  };

  useEffect(() => {
    if (result?.status === 'Complete') {
      const t = setTimeout(() => {
        navigate('/admin/imported-documents', { state: { toast: 'SSP PDF import completed.' } });
      }, 1500);
      return () => clearTimeout(t);
    }
  }, [result, navigate]);

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-semibold">Step 4: Commit Import</h2>
      <p className="text-sm text-gray-500">Review the summary and commit to finalize the SSP PDF import.</p>

      {result && (
        <div className="grid grid-cols-3 gap-3">
          <div className="rounded border bg-gray-50 p-4 text-center">
            <div className="text-2xl font-bold text-gray-800">{result.totalItems ?? '—'}</div>
            <div className="text-xs text-gray-500">Total Items</div>
          </div>
          <div className="rounded border bg-amber-50 p-4 text-center">
            <div className="text-2xl font-bold text-amber-700">{result.correctedItems ?? '—'}</div>
            <div className="text-xs text-amber-600">Corrected</div>
          </div>
          <div className="rounded border bg-green-50 p-4 text-center">
            <div className="text-2xl font-bold text-green-700">{result.acceptedItems ?? '—'}</div>
            <div className="text-xs text-green-600">Accepted as-is</div>
          </div>
        </div>
      )}

      {result?.status === 'Complete' && (
        <div className="rounded border border-green-300 bg-green-50 p-3 text-sm text-green-800">
          ✓ SSP PDF import committed. Redirecting…
        </div>
      )}

      {result?.status === 'Failed' && (
        <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          Import failed: {result.message ?? 'Unknown error.'}
        </div>
      )}

      {error && <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{error}</div>}

      <div className="flex justify-end gap-3">
        {(result?.status === 'Failed' || error) && (
          <button
            onClick={handleCommit}
            className="rounded border border-indigo-300 px-4 py-2 text-sm font-medium text-indigo-700 hover:bg-indigo-50"
          >
            Retry
          </button>
        )}
        <button
          disabled={committing || result?.status === 'Complete'}
          onClick={handleCommit}
          className="inline-flex items-center gap-2 rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          {committing ? (
            <><span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />Committing…</>
          ) : 'Commit Import'}
        </button>
      </div>
    </div>
  );
}

// ── Main wizard ──────────────────────────────────────────────────────────────

interface SspPdfImportWizardProps {
  onClose: () => void;
}

export default function SspPdfImportWizard({ onClose }: SspPdfImportWizardProps) {
  const [step, setStep] = useState<WizardStep>(1);
  const [sessionId, setSessionId] = useState<string | null>(null);

  const handleUploadComplete = (sid: string, _batchId: string) => {
    setSessionId(sid);
    setStep(2);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div
        className="w-full max-w-3xl rounded-xl bg-white p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-4 flex items-center justify-between">
          <h1 className="text-xl font-bold text-gray-900">Import SSP PDF</h1>
          <button
            onClick={onClose}
            className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-700"
            aria-label="Close wizard"
          >
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <WizardSteps current={step} />

        {step === 1 && <StepUpload onNext={handleUploadComplete} />}
        {step === 2 && sessionId && <StepExtraction sessionId={sessionId} onNext={() => setStep(3)} />}
        {step === 3 && sessionId && <StepCorrections sessionId={sessionId} onNext={() => setStep(4)} />}
        {step === 4 && sessionId && <StepCommit sessionId={sessionId} />}
      </div>
    </div>
  );
}
