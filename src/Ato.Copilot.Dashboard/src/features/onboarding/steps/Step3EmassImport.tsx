import { useEffect, useState } from 'react';
import { onboarding } from '../api/onboardingApi';
import type {
  EmassCommitDecision,
  EmassImportLogDto,
  EmassParseResultDto,
  WizardJobStatusDto,
} from '../api/onboardingApi';
import BackgroundJobProgress from '../components/BackgroundJobProgress';

/**
 * Step 4 — eMASS bulk import (User Story 3 / FR-030..FR-038).
 *
 * Flow: upload (.xlsx | .zip) → server parses in background → operator
 * reviews preview table + chooses Skip/Merge/Overwrite per system → commit
 * job runs in background → final per-system log surfaced for download.
 */
interface Props {
  onSaved?: () => void;
}

type Stage = 'idle' | 'uploaded' | 'previewing' | 'previewed' | 'committing' | 'completed';

export default function Step3EmassImport({ onSaved }: Props) {
  const [file, setFile] = useState<File | null>(null);
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [parseJobId, setParseJobId] = useState<string | null>(null);
  const [commitJobId, setCommitJobId] = useState<string | null>(null);
  const [preview, setPreview] = useState<EmassParseResultDto | null>(null);
  const [decisions, setDecisions] = useState<Record<string, EmassCommitDecision>>({});
  const [log, setLog] = useState<EmassImportLogDto | null>(null);
  const [stage, setStage] = useState<Stage>('idle');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // After parse job completes, fetch preview.
  useEffect(() => {
    if (!sessionId || stage !== 'uploaded') return;
    let cancelled = false;
    const interval = window.setInterval(async () => {
      try {
        const job: WizardJobStatusDto = await onboarding.getJob(parseJobId!);
        if (cancelled) return;
        if (job.status === 'Succeeded') {
          window.clearInterval(interval);
          const p = await onboarding.getEmassPreview(sessionId);
          if (cancelled) return;
          setPreview(p);
          const initial: Record<string, EmassCommitDecision> = {};
          p.systems.forEach((s) => {
            initial[s.systemIdentifier] = s.malformedReason ? 'Skip' : 'Merge';
          });
          setDecisions(initial);
          setStage('previewed');
        } else if (job.status === 'Failed' || job.status === 'Cancelled') {
          window.clearInterval(interval);
          setError(job.errorCode || 'Parse failed');
          setStage('idle');
        }
      } catch {
        // ignore — keep polling
      }
    }, 1500);
    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [sessionId, stage, parseJobId]);

  // After commit job completes, fetch log.
  useEffect(() => {
    if (!sessionId || stage !== 'committing' || !commitJobId) return;
    let cancelled = false;
    const interval = window.setInterval(async () => {
      try {
        const job = await onboarding.getJob(commitJobId);
        if (cancelled) return;
        if (job.status === 'Succeeded' || job.status === 'Failed') {
          window.clearInterval(interval);
          const l = await onboarding.getEmassLog(sessionId);
          if (cancelled) return;
          setLog(l);
          setStage('completed');
          if (job.status === 'Succeeded') onSaved?.();
        }
      } catch {
        // ignore
      }
    }, 1500);
    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [sessionId, stage, commitJobId, onSaved]);

  async function handleUpload() {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      const result = await onboarding.uploadEmass(file);
      setSessionId(result.sessionId);
      setParseJobId(result.parseJobId);
      setStage('uploaded');
    } catch (e: unknown) {
      const err = e as { errorCode?: string; message?: string; suggestion?: string };
      setError(`${err.errorCode ?? 'UPLOAD_FAILED'}: ${err.message ?? 'Upload failed'}${
        err.suggestion ? ` — ${err.suggestion}` : ''
      }`);
    } finally {
      setBusy(false);
    }
  }

  async function handleCommit() {
    if (!sessionId || !preview) return;
    setBusy(true);
    setError(null);
    try {
      const instructions = preview.systems.map((s) => ({
        systemIdentifier: s.systemIdentifier,
        decision: decisions[s.systemIdentifier] ?? 'Skip',
      }));
      const result = await onboarding.commitEmass(sessionId, instructions);
      setCommitJobId(result.commitJobId);
      setStage('committing');
    } catch (e: unknown) {
      const err = e as { errorCode?: string; message?: string };
      setError(`${err.errorCode ?? 'COMMIT_FAILED'}: ${err.message ?? 'Commit failed'}`);
    } finally {
      setBusy(false);
    }
  }

  function downloadLog() {
    if (!log) return;
    const blob = new Blob([JSON.stringify(log, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `emass-import-${log.sessionId}.json`;
    a.click();
    URL.revokeObjectURL(url);
  }

  return (
    <section className="space-y-6">
      <header>
        <h2 className="text-xl font-semibold">Step 5 — eMASS Bulk Import</h2>
        <p className="text-sm text-gray-600">
          Upload an eMASS export (.xlsx) or package (.zip) to register multiple systems at once.
          You can review per-system results and choose Skip / Merge / Overwrite before committing.
        </p>
      </header>

      {error && (
        <div role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm text-red-800">
          {error}
        </div>
      )}

      {(stage === 'idle' || stage === 'uploaded') && (
        <div className="space-y-3">
          <label className="block text-sm font-medium">eMASS file</label>
          <input
            type="file"
            accept=".xlsx,.zip"
            disabled={busy || stage === 'uploaded'}
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            className="block w-full rounded border border-gray-300 p-2 text-sm"
          />
          <button
            type="button"
            onClick={handleUpload}
            disabled={busy || !file || stage === 'uploaded'}
            className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
          >
            {stage === 'uploaded' ? 'Parsing…' : 'Upload & parse'}
          </button>
          {stage === 'uploaded' && parseJobId && (
            <BackgroundJobProgress jobId={parseJobId} />
          )}
        </div>
      )}

      {stage === 'previewed' && preview && (
        <div className="space-y-3">
          <h3 className="font-medium">Preview ({preview.systems.length} systems)</h3>
          <div className="overflow-x-auto rounded border border-gray-200">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-3 py-2 text-left">Identifier</th>
                  <th className="px-3 py-2 text-left">Name</th>
                  <th className="px-3 py-2 text-right">Controls</th>
                  <th className="px-3 py-2 text-right">POA&Ms</th>
                  <th className="px-3 py-2 text-left">Decision</th>
                  <th className="px-3 py-2 text-left">Notes</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {preview.systems.map((s) => (
                  <tr key={s.systemIdentifier}>
                    <td className="px-3 py-2 font-mono">{s.systemIdentifier}</td>
                    <td className="px-3 py-2">{s.systemName}</td>
                    <td className="px-3 py-2 text-right">{s.controlCount}</td>
                    <td className="px-3 py-2 text-right">{s.poamCount}</td>
                    <td className="px-3 py-2">
                      <select
                        className="rounded border border-gray-300 p-1 text-sm"
                        value={decisions[s.systemIdentifier] ?? 'Skip'}
                        disabled={!!s.malformedReason}
                        onChange={(e) =>
                          setDecisions((d) => ({
                            ...d,
                            [s.systemIdentifier]: e.target.value as EmassCommitDecision,
                          }))
                        }
                      >
                        <option value="Skip">Skip</option>
                        <option value="Merge">Merge</option>
                        <option value="Overwrite">Overwrite</option>
                      </select>
                    </td>
                    <td className="px-3 py-2 text-xs text-amber-700">
                      {s.malformedReason ?? ''}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <button
            type="button"
            onClick={handleCommit}
            disabled={busy}
            className="rounded bg-green-600 px-4 py-2 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
          >
            Commit selected
          </button>
        </div>
      )}

      {stage === 'committing' && commitJobId && (
        <BackgroundJobProgress jobId={commitJobId} />
      )}

      {stage === 'completed' && log && (
        <div className="space-y-3">
          <h3 className="font-medium">Import complete ({log.entries.length} entries)</h3>
          <ul className="space-y-1 text-sm">
            {log.entries.map((e, i) => (
              <li key={i} className="flex items-baseline gap-2">
                <span
                  className={[
                    'inline-block w-20 rounded px-2 py-0.5 text-center text-xs font-medium',
                    e.outcome === 'Failed'
                      ? 'bg-red-100 text-red-800'
                      : e.outcome === 'Skipped'
                        ? 'bg-gray-100 text-gray-700'
                        : 'bg-green-100 text-green-800',
                  ].join(' ')}
                >
                  {e.outcome}
                </span>
                <span className="font-mono">{e.systemIdentifier}</span>
                <span>{e.systemName}</span>
                {e.reason && <span className="text-xs text-red-700">{e.reason}</span>}
              </li>
            ))}
          </ul>
          <button
            type="button"
            onClick={downloadLog}
            className="rounded border border-gray-300 px-3 py-1 text-sm hover:bg-gray-50"
          >
            Download log
          </button>
        </div>
      )}
    </section>
  );
}
