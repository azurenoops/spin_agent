/**
 * T273 / T274 / T275: eMASS Import Wizard
 *
 * 4-step wizard: Upload → Preview → Log Review → Commit
 * Accessed via the "Import from eMASS" CTA in ImportedDocumentsView.
 *
 * Endpoints:
 *   POST   /api/onboarding/imports/emass/upload         → { sessionId }
 *   GET    /api/onboarding/imports/emass/{id}/preview   → paginated rows
 *   GET    /api/onboarding/imports/emass/{id}/log       → categorized log
 *   POST   /api/onboarding/imports/emass/{id}/commit    → long-running
 */
import { useState, useCallback, useEffect } from 'react';
import axios from 'axios';
import { useNavigate } from 'react-router-dom';

const emassApi = axios.create({ baseURL: '/api/onboarding/imports/emass' });

// ── Types ────────────────────────────────────────────────────────────────────

interface PreviewRow {
  rowIndex: number;
  controlId: string;
  controlName: string;
  status: string;
  [key: string]: unknown;
}

interface PreviewResponse {
  rows: PreviewRow[];
  total: number;
  parseStatus: 'Pending' | 'InProgress' | 'Complete' | 'Failed';
}

interface LogEntry {
  category: 'Error' | 'Warning' | 'Skipped' | 'Info';
  message: string;
  rowIndex?: number;
}

interface CommitStatus {
  status: 'Pending' | 'InProgress' | 'Complete' | 'Failed';
  recordCount?: number;
  warningCount?: number;
  errorCount?: number;
  message?: string;
}

// ── Step components ──────────────────────────────────────────────────────────

type WizardStep = 1 | 2 | 3 | 4;

const STEP_LABELS: Record<WizardStep, string> = {
  1: 'Upload',
  2: 'Preview',
  3: 'Review Log',
  4: 'Commit',
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

function StepUpload({ onNext }: { onNext: (sessionId: string) => void }) {
  const [file, setFile] = useState<File | null>(null);
  const [dragging, setDragging] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const validateFile = (f: File): string | null => {
    const ext = f.name.split('.').pop()?.toLowerCase();
    if (!['xlsx', 'csv'].includes(ext ?? '')) {
      return `Only .xlsx and .csv files are accepted. Got: .${ext ?? 'unknown'}`;
    }
    if (f.size === 0) return 'File is empty.';
    if (f.size > 50 * 1024 * 1024) return 'File exceeds 50 MB limit.';
    return null;
  };

  const handleFile = (f: File) => {
    const err = validateFile(f);
    if (err) { setError(err); setFile(null); return; }
    setError(null);
    setFile(f);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setDragging(false);
    const f = e.dataTransfer.files[0];
    if (f) handleFile(f);
  };

  const handleUpload = async () => {
    if (!file) return;
    setUploading(true);
    setError(null);
    try {
      const form = new FormData();
      form.append('file', file);
      const { data } = await emassApi.post<{ ok: boolean; data: { sessionId: string } }>('/upload', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      onNext(data.data.sessionId);
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Upload failed.');
    } finally {
      setUploading(false);
    }
  };

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-semibold">Step 1: Upload eMASS Export</h2>
      <p className="text-sm text-gray-500">
        Upload an eMASS export file (.xlsx or .csv). The file will be parsed server-side before you review.
      </p>

      <div
        onDragOver={(e) => { e.preventDefault(); setDragging(true); }}
        onDragLeave={() => setDragging(false)}
        onDrop={handleDrop}
        className={`flex flex-col items-center justify-center rounded-lg border-2 border-dashed p-10 transition-colors cursor-pointer ${
          dragging ? 'border-indigo-400 bg-indigo-50' : 'border-gray-300 hover:border-indigo-300'
        }`}
        onClick={() => document.getElementById('emass-file-input')?.click()}
        role="button"
        tabIndex={0}
        onKeyDown={(e) => e.key === 'Enter' && document.getElementById('emass-file-input')?.click()}
        aria-label="Drop or click to select eMASS export file"
      >
        <svg className="mb-3 h-10 w-10 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5m-13.5-9L12 3m0 0l4.5 4.5M12 3v13.5" />
        </svg>
        {file ? (
          <p className="text-sm font-medium text-indigo-700">{file.name} ({(file.size / 1024).toFixed(0)} KB)</p>
        ) : (
          <>
            <p className="text-sm font-medium text-gray-700">Drop file here or click to browse</p>
            <p className="mt-1 text-xs text-gray-400">.xlsx or .csv, max 50 MB</p>
          </>
        )}
      </div>

      <input
        id="emass-file-input"
        type="file"
        accept=".xlsx,.csv"
        className="hidden"
        onChange={(e) => { const f = e.target.files?.[0]; if (f) handleFile(f); }}
      />

      {error && (
        <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{error}</div>
      )}

      <div className="flex justify-end">
        <button
          disabled={!file || uploading}
          onClick={handleUpload}
          className="inline-flex items-center gap-2 rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          {uploading ? (
            <>
              <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
              Uploading…
            </>
          ) : 'Upload & Continue'}
        </button>
      </div>
    </div>
  );
}

// ── Step 2: Preview ──────────────────────────────────────────────────────────

const PREVIEW_PAGE_SIZE = 25;

function StepPreview({ sessionId, onNext }: { sessionId: string; onNext: () => void }) {
  const [data, setData] = useState<PreviewResponse | null>(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchPage = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const { data: resp } = await emassApi.get<{ ok: boolean; data: PreviewResponse }>(
        `/${sessionId}/preview?page=${p}&pageSize=${PREVIEW_PAGE_SIZE}`,
      );
      setData(resp.data);
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Failed to load preview.');
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  // Poll until parse is complete
  useEffect(() => {
    let cancelled = false;
    const poll = async () => {
      await fetchPage(page);
      if (!cancelled && data?.parseStatus === 'InProgress') {
        setTimeout(poll, 2000);
      }
    };
    void poll();
    return () => { cancelled = true; };
  }, [sessionId, page]); // eslint-disable-line react-hooks/exhaustive-deps

  const totalPages = Math.max(1, Math.ceil((data?.total ?? 0) / PREVIEW_PAGE_SIZE));

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">Step 2: Preview Import Data</h2>
        {data?.parseStatus === 'InProgress' && (
          <span className="inline-flex items-center gap-1.5 text-sm text-amber-700">
            <span className="h-3 w-3 animate-spin rounded-full border-2 border-amber-500 border-t-transparent" />
            Parsing…
          </span>
        )}
      </div>

      {error && <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{error}</div>}

      <div className="overflow-x-auto rounded border">
        <table className="w-full text-sm">
          <thead className="bg-gray-50 text-gray-600 text-xs uppercase tracking-wide">
            <tr>
              <th className="px-3 py-2 text-left">#</th>
              <th className="px-3 py-2 text-left">Control ID</th>
              <th className="px-3 py-2 text-left">Control Name</th>
              <th className="px-3 py-2 text-left">Status</th>
            </tr>
          </thead>
          <tbody>
            {loading && (
              <tr><td colSpan={4} className="px-3 py-6 text-center text-gray-500">Loading…</td></tr>
            )}
            {!loading && data?.rows.map((row) => (
              <tr key={row.rowIndex} className="border-t hover:bg-gray-50">
                <td className="px-3 py-2 text-gray-400">{row.rowIndex}</td>
                <td className="px-3 py-2 font-mono font-medium">{row.controlId}</td>
                <td className="px-3 py-2">{row.controlName}</td>
                <td className="px-3 py-2">
                  <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                    row.status === 'Ready' ? 'bg-green-100 text-green-700' :
                    row.status === 'Error' ? 'bg-red-100 text-red-700' :
                    'bg-gray-100 text-gray-600'
                  }`}>
                    {row.status}
                  </span>
                </td>
              </tr>
            ))}
            {!loading && (!data?.rows || data.rows.length === 0) && (
              <tr><td colSpan={4} className="px-3 py-6 text-center text-gray-500">No rows found.</td></tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="flex items-center justify-between text-sm text-gray-500">
        <span>{data?.total ?? 0} rows · page {page} / {totalPages}</span>
        <div className="flex gap-2">
          <button disabled={page <= 1} onClick={() => setPage((p) => p - 1)} className="rounded border px-2 py-1 disabled:opacity-50">Prev</button>
          <button disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} className="rounded border px-2 py-1 disabled:opacity-50">Next</button>
        </div>
      </div>

      <div className="flex justify-end">
        <button
          disabled={loading || data?.parseStatus === 'InProgress'}
          onClick={onNext}
          className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          Review Log →
        </button>
      </div>
    </div>
  );
}

// ── Step 3: Log Review ───────────────────────────────────────────────────────

type LogCategory = 'Error' | 'Warning' | 'Skipped' | 'Info';
const LOG_COLORS: Record<LogCategory, string> = {
  Error: 'bg-red-50 border-red-200 text-red-800',
  Warning: 'bg-amber-50 border-amber-200 text-amber-800',
  Skipped: 'bg-gray-50 border-gray-200 text-gray-600',
  Info: 'bg-blue-50 border-blue-200 text-blue-700',
};

function StepLogReview({ sessionId, onNext }: { sessionId: string; onNext: () => void }) {
  const [log, setLog] = useState<LogEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeCategory, setActiveCategory] = useState<LogCategory | ''>('');

  useEffect(() => {
    let cancelled = false;
    const poll = async () => {
      try {
        const { data } = await emassApi.get<{ ok: boolean; data: { entries: LogEntry[]; complete: boolean } }>(
          `/${sessionId}/log`,
        );
        if (!cancelled) {
          setLog(data.data.entries);
          setLoading(false);
          if (!data.data.complete) {
            setTimeout(poll, 2000);
          }
        }
      } catch (e: unknown) {
        if (!cancelled) {
          setError((e as Error).message ?? 'Failed to load log.');
          setLoading(false);
        }
      }
    };
    void poll();
    return () => { cancelled = true; };
  }, [sessionId]);

  const categories: LogCategory[] = ['Error', 'Warning', 'Skipped', 'Info'];
  const counts = categories.reduce((acc, cat) => {
    acc[cat] = log.filter((e) => e.category === cat).length;
    return acc;
  }, {} as Record<LogCategory, number>);

  const displayed = activeCategory ? log.filter((e) => e.category === activeCategory) : log;

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-semibold">Step 3: Review Import Log</h2>

      {/* Category tabs */}
      <div className="flex gap-2 flex-wrap">
        <button
          onClick={() => setActiveCategory('')}
          className={`rounded-full px-3 py-1 text-sm font-medium ${activeCategory === '' ? 'bg-indigo-600 text-white' : 'bg-gray-100 text-gray-700'}`}
        >
          All ({log.length})
        </button>
        {categories.map((cat) => (
          <button
            key={cat}
            onClick={() => setActiveCategory(cat)}
            className={`rounded-full px-3 py-1 text-sm font-medium ${activeCategory === cat ? 'bg-indigo-600 text-white' : 'bg-gray-100 text-gray-700'}`}
          >
            {cat} ({counts[cat]})
          </button>
        ))}
      </div>

      {error && <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{error}</div>}

      {loading ? (
        <div className="py-8 text-center text-sm text-gray-500">
          <span className="inline-block h-4 w-4 animate-spin rounded-full border-2 border-indigo-500 border-t-transparent mr-2" />
          Loading log…
        </div>
      ) : (
        <div className="max-h-80 overflow-y-auto space-y-1.5">
          {displayed.length === 0 && (
            <div className="py-4 text-center text-sm text-gray-500">No entries in this category.</div>
          )}
          {displayed.map((entry, i) => (
            <div key={i} className={`rounded border px-3 py-2 text-sm ${LOG_COLORS[entry.category]}`}>
              {entry.rowIndex != null && (
                <span className="mr-2 font-mono text-xs opacity-70">Row {entry.rowIndex}</span>
              )}
              {entry.message}
            </div>
          ))}
        </div>
      )}

      <div className="flex justify-end">
        <button
          disabled={loading}
          onClick={onNext}
          className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          Proceed to Commit →
        </button>
      </div>
    </div>
  );
}

// ── Step 4: Commit ───────────────────────────────────────────────────────────

function StepCommit({ sessionId }: { sessionId: string }) {
  const navigate = useNavigate();
  const [committing, setCommitting] = useState(false);
  const [status, setStatus] = useState<CommitStatus | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleCommit = async () => {
    setCommitting(true);
    setError(null);
    try {
      const { data } = await emassApi.post<{ ok: boolean; data: CommitStatus }>(`/${sessionId}/commit`);
      setStatus(data.data);
      // Poll if long-running
      if (data.data.status === 'InProgress' || data.data.status === 'Pending') {
        pollStatus();
      }
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Commit failed.');
      setCommitting(false);
    }
  };

  const pollStatus = useCallback(async () => {
    let attempts = 0;
    const poll = async () => {
      try {
        const { data } = await emassApi.get<{ ok: boolean; data: CommitStatus }>(`/${sessionId}/commit/status`);
        setStatus(data.data);
        if (data.data.status === 'InProgress' || data.data.status === 'Pending') {
          if (++attempts < 60) setTimeout(poll, 2000);
          else setError('Commit timed out. Check the imports list for final status.');
        } else {
          setCommitting(false);
        }
      } catch (e: unknown) {
        setError((e as Error).message ?? 'Status check failed.');
        setCommitting(false);
      }
    };
    await poll();
  }, [sessionId]);

  // Redirect on success
  useEffect(() => {
    if (status?.status === 'Complete') {
      const timer = setTimeout(() => {
        navigate('/admin/imported-documents', { state: { toast: 'eMASS import committed successfully.' } });
      }, 1500);
      return () => clearTimeout(timer);
    }
  }, [status, navigate]);

  return (
    <div className="space-y-4">
      <h2 className="text-lg font-semibold">Step 4: Commit Import</h2>
      <p className="text-sm text-gray-500">
        Review the summary below then commit to permanently import these records.
      </p>

      {status && (
        <div className="grid grid-cols-3 gap-3">
          <div className="rounded border bg-green-50 p-4 text-center">
            <div className="text-2xl font-bold text-green-700">{status.recordCount ?? '—'}</div>
            <div className="text-xs text-green-600">Records</div>
          </div>
          <div className="rounded border bg-amber-50 p-4 text-center">
            <div className="text-2xl font-bold text-amber-700">{status.warningCount ?? '—'}</div>
            <div className="text-xs text-amber-600">Warnings</div>
          </div>
          <div className="rounded border bg-red-50 p-4 text-center">
            <div className="text-2xl font-bold text-red-700">{status.errorCount ?? '—'}</div>
            <div className="text-xs text-red-600">Errors</div>
          </div>
        </div>
      )}

      {status?.status === 'Complete' && (
        <div className="rounded border border-green-300 bg-green-50 p-3 text-sm text-green-800">
          ✓ Import committed. Redirecting to Imported Documents…
        </div>
      )}

      {status?.status === 'Failed' && (
        <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          Import failed: {status.message ?? 'Unknown error.'}
        </div>
      )}

      {error && <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{error}</div>}

      <div className="flex justify-end gap-3">
        {(status?.status === 'Failed' || error) && (
          <button
            onClick={handleCommit}
            className="rounded border border-indigo-300 px-4 py-2 text-sm font-medium text-indigo-700 hover:bg-indigo-50"
          >
            Retry
          </button>
        )}
        <button
          disabled={committing || status?.status === 'Complete'}
          onClick={handleCommit}
          className="inline-flex items-center gap-2 rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
        >
          {committing ? (
            <>
              <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
              Committing…
            </>
          ) : 'Commit Import'}
        </button>
      </div>
    </div>
  );
}

// ── Main wizard ──────────────────────────────────────────────────────────────

interface EmassImportWizardProps {
  onClose: () => void;
}

export default function EmassImportWizard({ onClose }: EmassImportWizardProps) {
  const [step, setStep] = useState<WizardStep>(1);
  const [sessionId, setSessionId] = useState<string | null>(null);

  const handleUploadComplete = (id: string) => {
    setSessionId(id);
    setStep(2);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" onClick={onClose}>
      <div
        className="w-full max-w-3xl rounded-xl bg-white p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-4 flex items-center justify-between">
          <h1 className="text-xl font-bold text-gray-900">Import from eMASS</h1>
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
        {step === 2 && sessionId && <StepPreview sessionId={sessionId} onNext={() => setStep(3)} />}
        {step === 3 && sessionId && <StepLogReview sessionId={sessionId} onNext={() => setStep(4)} />}
        {step === 4 && sessionId && <StepCommit sessionId={sessionId} />}
      </div>
    </div>
  );
}
