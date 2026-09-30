import { useEffect, useRef, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { workspaceErrorMessage } from '../workspaces/api';
import { uploadSystemSource, type SystemSourceReceipt } from './systemSetupApi';

export default function SystemSourcePanel({ tenantId, systemId, kind, sources, onChanged, onPendingChange, disabled = false }: {
  tenantId: string; systemId: string; kind: 'emass' | 'ssp-pdf'; sources: SystemSourceReceipt[];
  onChanged: () => void | Promise<void>; onPendingChange: (pending: boolean) => void;
  disabled?: boolean;
}) {
  const [file, setFile] = useState<File>();
  const [receipt, setReceipt] = useState<SystemSourceReceipt>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const alive = useRef(true);
  const pending = useRef<{ key: string; file: File } | undefined>(undefined);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; };
  }, []);
  async function upload() {
    if ((!file && !pending.current) || busy || disabled) return;
    if (file && file.size > 4 * 1024 * 1024) { setError('Supported source files must be 4 MiB or smaller.'); return; }
    const intent = pending.current ?? { key: crypto.randomUUID(), file: file! };
    pending.current = intent;
    onPendingChange(true); setBusy(true); setError('');
    try {
      const result = await uploadSystemSource(tenantId, systemId, kind, intent.file, intent.key);
      if (!alive.current) return;
      setReceipt(result); pending.current = undefined; setFile(undefined); onPendingChange(false);
      await onChanged();
    } catch (reason) {
      if (!alive.current) return;
      setError(workspaceErrorMessage(reason));
      const status = reason && typeof reason === 'object' && 'status' in reason ? reason.status : undefined;
      if (status === 400 || status === 409 || status === 413 || status === 415) {
        pending.current = undefined; onPendingChange(false);
      }
    } finally { if (alive.current) setBusy(false); }
  }
  const records = new Map(sources.map(source => [source.sessionId, source]));
  if (receipt) records.set(receipt.sessionId, receipt);
  return <div className="mt-4 space-y-4">
    <p className="text-sm">Retain one digital SSP PDF or single-system XLSX, up to 4 MiB. Analysis proposes identity fields only; unsupported fields remain visible. No system facts change on upload.</p>
    {error && <p role="alert" className="rounded bg-red-50 p-3 text-red-800">{error}</p>}
    <label className="block text-sm font-medium">Source file<input type="file" className="mt-2 block w-full"
      accept={kind === 'emass' ? '.xlsx' : '.pdf'} disabled={disabled || busy || Boolean(pending.current)}
      onChange={event => setFile(event.target.files?.[0])} /></label>
    <p className="text-xs text-slate-600">A selected file is not saved until its receipt is confirmed. Save & finish later does not upload a selected file.</p>
    <button type="button" disabled={disabled || busy || (!file && !pending.current)} onClick={() => void upload()}
      className="rounded-lg bg-purple-700 px-4 py-2 text-sm text-white disabled:opacity-50">
      {pending.current && !busy ? 'Retry original source request' : busy ? 'Retaining source…' : 'Retain source for review'}
    </button>
    {[...records.values()].map(source => <article key={source.sessionId} className="rounded-lg border border-purple-200 p-4">
      <p className="font-medium">{source.fileName} · {source.receiptState}</p>
      <p className="text-sm">Analysis: {source.analysisState} · Review: {source.reviewState}</p>
      <p className="mt-2 break-all font-mono text-xs">{source.sha256}</p>
      {source.error && <p role="alert" className="mt-2 text-sm text-red-800">{source.error}</p>}
      <Link className="mt-3 inline-block text-sm text-purple-700 underline"
        to={`/systems/${encodeURIComponent(systemId)}/setup?source=${encodeURIComponent(source.kind)}&receipt=${encodeURIComponent(source.sessionId)}`}>
        Review source fields
      </Link>
    </article>)}
  </div>;
}
