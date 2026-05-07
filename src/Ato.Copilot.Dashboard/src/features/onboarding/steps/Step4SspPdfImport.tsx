import { useEffect, useMemo, useState } from 'react';
import { onboarding } from '../api/onboardingApi';
import type {
  SspPdfBatchEntryDto,
  SspPdfExtractionResultDto,
  SspPdfFieldCorrectionDto,
  SspPdfSessionSummaryDto,
} from '../api/onboardingApi';
import BackgroundJobProgress from '../components/BackgroundJobProgress';

/**
 * Step 5 — SSP PDF batch ingestion (User Story 4 / FR-040..FR-046).
 *
 * Workflow: select 1..N PDFs → server queues one extraction job per PDF →
 * per-card status updates → admin opens a card to review extraction +
 * correct low-confidence fields → admin imports the system with PDF
 * provenance metadata.
 */
interface Props {
  onSaved?: () => void;
}

export default function Step4SspPdfImport({ onSaved }: Props) {
  const [files, setFiles] = useState<File[]>([]);
  const [batchId, setBatchId] = useState<string | null>(null);
  const [batch, setBatch] = useState<SspPdfBatchEntryDto[]>([]);
  const [summary, setSummary] = useState<SspPdfSessionSummaryDto[]>([]);
  const [openSession, setOpenSession] = useState<string | null>(null);
  const [extraction, setExtraction] = useState<SspPdfExtractionResultDto | null>(null);
  const [corrections, setCorrections] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Poll batch summary every 2s until all sessions are terminal.
  useEffect(() => {
    if (!batchId) return;
    let cancelled = false;
    const interval = window.setInterval(async () => {
      try {
        const next = await onboarding.getSspPdfBatch(batchId);
        if (cancelled) return;
        setSummary(next);
        const allDone = next.every((s) =>
          ['Extracted', 'Imported', 'Rejected', 'Failed'].includes(s.status),
        );
        if (allDone) window.clearInterval(interval);
      } catch {
        // ignore
      }
    }, 2000);
    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [batchId]);

  // Load extraction when a session card is opened.
  useEffect(() => {
    if (!openSession) return;
    let cancelled = false;
    void onboarding
      .getSspPdfExtraction(openSession)
      .then((r) => {
        if (cancelled) return;
        setExtraction(r);
        const initial: Record<string, string> = {};
        r.fields.forEach((f) => {
          initial[f.name] = f.value ?? '';
        });
        setCorrections(initial);
      })
      .catch((e: unknown) => {
        const err = e as { errorCode?: string; message?: string };
        setError(`${err.errorCode ?? 'EXTRACT_NOT_READY'}: ${err.message ?? 'Extraction not ready'}`);
      });
    return () => {
      cancelled = true;
    };
  }, [openSession]);

  const counts = useMemo(() => {
    const c: Record<string, number> = {};
    summary.forEach((s) => (c[s.status] = (c[s.status] ?? 0) + 1));
    return c;
  }, [summary]);

  async function handleUpload() {
    if (files.length === 0) return;
    setBusy(true);
    setError(null);
    try {
      const result = await onboarding.uploadSspPdfBatch(files);
      setBatchId(result.batchId);
      setBatch(result.sessions);
    } catch (e: unknown) {
      const err = e as { errorCode?: string; message?: string };
      setError(`${err.errorCode ?? 'UPLOAD_FAILED'}: ${err.message ?? 'Upload failed'}`);
    } finally {
      setBusy(false);
    }
  }

  async function saveCorrections() {
    if (!openSession) return;
    setBusy(true);
    setError(null);
    try {
      const list: SspPdfFieldCorrectionDto[] = Object.entries(corrections).map(
        ([fieldName, value]) => ({
          fieldName,
          value: value === '' ? null : value,
        }),
      );
      await onboarding.putSspPdfCorrections(openSession, list);
    } catch (e: unknown) {
      const err = e as { errorCode?: string; message?: string };
      setError(`${err.errorCode ?? 'SAVE_FAILED'}: ${err.message ?? 'Save failed'}`);
    } finally {
      setBusy(false);
    }
  }

  async function importSystem() {
    if (!openSession) return;
    setBusy(true);
    setError(null);
    try {
      await saveCorrections();
      await onboarding.importSspPdfSystem(openSession);
      setOpenSession(null);
      // Refresh summary so the imported card moves to "Imported".
      if (batchId) setSummary(await onboarding.getSspPdfBatch(batchId));
      onSaved?.();
    } catch (e: unknown) {
      const err = e as { errorCode?: string; message?: string };
      setError(`${err.errorCode ?? 'IMPORT_FAILED'}: ${err.message ?? 'Import failed'}`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="space-y-6">
      <header>
        <h2 className="text-xl font-semibold">Step 6 — SSP PDF batch import</h2>
        <p className="text-sm text-gray-600">
          Upload one or more digital SSP PDFs (NIST framework only). Password-
          protected, image-only, or non-NIST PDFs are rejected with a specific reason.
        </p>
      </header>

      {error && (
        <div role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm text-red-800">
          {error}
        </div>
      )}

      {!batchId && (
        <div className="space-y-3">
          <label className="block text-sm font-medium">SSP PDFs</label>
          <input
            type="file"
            accept="application/pdf,.pdf"
            multiple
            onChange={(e) => setFiles(Array.from(e.target.files ?? []))}
            className="block w-full rounded border border-gray-300 p-2 text-sm"
          />
          <button
            type="button"
            disabled={busy || files.length === 0}
            onClick={handleUpload}
            className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
          >
            Upload {files.length} file(s)
          </button>
        </div>
      )}

      {batchId && (
        <div className="space-y-4">
          <h3 className="font-medium">Batch progress</h3>
          <p className="text-sm text-gray-600">
            {Object.entries(counts)
              .map(([k, v]) => `${k}: ${v}`)
              .join(' · ')}
          </p>

          <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
            {summary.map((s) => {
              const entry = batch.find((b) => b.sessionId === s.sessionId);
              return (
                <article
                  key={s.sessionId}
                  className="rounded border border-gray-200 p-3"
                >
                  <header className="flex items-baseline justify-between">
                    <span className="font-mono text-sm">{s.originalFileName}</span>
                    <span
                      className={[
                        'rounded px-2 py-0.5 text-xs font-medium',
                        s.status === 'Imported'
                          ? 'bg-green-100 text-green-800'
                          : s.status === 'Rejected' || s.status === 'Failed'
                            ? 'bg-red-100 text-red-800'
                            : s.status === 'Extracted'
                              ? 'bg-indigo-100 text-indigo-800'
                              : 'bg-gray-100 text-gray-700',
                      ].join(' ')}
                    >
                      {s.status}
                    </span>
                  </header>
                  {s.rejectReason && (
                    <p className="mt-2 text-xs text-red-700">{s.rejectReason}</p>
                  )}
                  {entry && s.status === 'Extracting' && (
                    <div className="mt-2">
                      <BackgroundJobProgress jobId={entry.extractJobId} />
                    </div>
                  )}
                  {s.status === 'Extracted' && (
                    <button
                      type="button"
                      onClick={() => setOpenSession(s.sessionId)}
                      className="mt-2 rounded border border-gray-300 px-2 py-1 text-xs hover:bg-gray-50"
                    >
                      Review &amp; import
                    </button>
                  )}
                </article>
              );
            })}
          </div>
        </div>
      )}

      {openSession && extraction && (
        <div className="rounded border border-gray-200 p-4">
          <h3 className="font-medium">Review extracted fields</h3>
          <p className="text-xs text-gray-600">
            Edit any field before import. Low-confidence fields are highlighted.
          </p>
          <table className="mt-3 min-w-full divide-y divide-gray-200 text-sm">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-3 py-2 text-left">Field</th>
                <th className="px-3 py-2 text-left">Value</th>
                <th className="px-3 py-2 text-left">Confidence</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {extraction.fields.map((f) => (
                <tr key={f.name}>
                  <td className="px-3 py-2 font-mono">{f.name}</td>
                  <td className="px-3 py-2">
                    <input
                      type="text"
                      value={corrections[f.name] ?? ''}
                      onChange={(e) =>
                        setCorrections((c) => ({ ...c, [f.name]: e.target.value }))
                      }
                      className={[
                        'w-full rounded border p-1',
                        f.confidence === 'Low'
                          ? 'border-amber-400 bg-amber-50'
                          : 'border-gray-300',
                      ].join(' ')}
                    />
                  </td>
                  <td className="px-3 py-2 text-xs">{f.confidence}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="mt-3 flex gap-2">
            <button
              type="button"
              onClick={importSystem}
              disabled={busy}
              className="rounded bg-green-600 px-3 py-1 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
            >
              Import as system
            </button>
            <button
              type="button"
              onClick={() => setOpenSession(null)}
              className="rounded border border-gray-300 px-3 py-1 text-sm hover:bg-gray-50"
            >
              Close
            </button>
          </div>
        </div>
      )}
    </section>
  );
}
