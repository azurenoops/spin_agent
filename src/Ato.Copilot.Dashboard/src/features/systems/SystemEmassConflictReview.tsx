import { useEffect, useState } from 'react';
import type { ConflictStatus, EmassConflict } from '../../api/emass-status';
import { systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';

type Resolution = Exclude<ConflictStatus, 'Unresolved'>;
export default function SystemEmassConflictReview({ conflicts, onResolve }: {
  conflicts: EmassConflict[]; onResolve: (id: string, resolution: Resolution, rationale?: string) => Promise<void>;
}) {
  const [selectedId, setSelectedId] = useState('');
  const [resolution, setResolution] = useState<Resolution | ''>('');
  const [rationale, setRationale] = useState('');
  const [bulkRationale, setBulkRationale] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const selected = conflicts.find(item => item.id === selectedId) ?? conflicts[0];
  useEffect(() => { setResolution(''); setRationale(''); setError(null); }, [selected?.id, selected?.spinValue, selected?.emassValue]);
  const record = async () => {
    if (!selected || !resolution || busy || resolution === 'AcceptEmass' && !rationale.trim()) return;
    setBusy(true);
    setError(null);
    try { await onResolve(selected.id, resolution, rationale.trim() || undefined); }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Resolution failed. Review the error before retrying.'); }
    finally { setBusy(false); }
  };
  const acceptPage = async () => {
    if (busy || !bulkRationale.trim() || !window.confirm(`Accept eMASS values for these ${conflicts.length} displayed conflicts?`)) return;
    setBusy(true);
    setError(null);
    try { for (const conflict of conflicts) await onResolve(conflict.id, 'AcceptEmass', bulkRationale.trim()); }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Bulk resolution stopped. Completed decisions remain retained; review before retrying.'); }
    finally { setBusy(false); }
  };
  if (!selected) return <p className="p-5 text-sm text-slate-500">No unresolved conflicts.</p>;
  return <div className="space-y-5 p-5">
    <label className="block text-sm font-medium">Conflict to review
      <select value={selected.id} disabled={busy} onChange={event => setSelectedId(event.target.value)}
        className="mt-2 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 dark:border-slate-600 dark:bg-slate-900">
        {conflicts.map(item => <option key={item.id} value={item.id}>{item.entityId ?? item.entityType} · {item.fieldName}</option>)}
      </select>
    </label>
    <h3 className="font-semibold">{selected.entityId ?? selected.entityType} · {selected.fieldName}</h3>
    <div className="grid gap-4 md:grid-cols-2">
      <section className="min-w-0 rounded-lg border border-slate-200 bg-slate-50 p-4 dark:border-slate-700 dark:bg-slate-800">
        <h4 className="text-sm font-semibold">SPIN exported value</h4>
        <p className="mt-3 whitespace-pre-wrap break-words text-sm">{selected.spinValue ?? 'Empty'}</p>
      </section>
      <section className="min-w-0 rounded-lg border border-indigo-200 bg-indigo-50 p-4 dark:border-indigo-800 dark:bg-indigo-950">
        <h4 className="text-sm font-semibold">Returned eMASS value</h4>
        <p className="mt-3 whitespace-pre-wrap break-words text-sm">{selected.emassValue ?? 'Empty'}</p>
      </section>
    </div>
    <label className="block text-sm font-medium">Resolution
      <select value={resolution} disabled={busy} onChange={event => setResolution(event.target.value as Resolution | '')}
        className="mt-2 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 dark:border-slate-600 dark:bg-slate-900">
        <option value="">Choose a reviewed resolution</option>
        <option value="KeepSpin">Keep SPIN value</option><option value="AcceptEmass">Accept returned value</option><option value="Deferred">Defer for further review</option>
      </select>
    </label>
    <label className="block text-sm font-medium">Resolution rationale
      <textarea aria-label="Resolution rationale" value={rationale} disabled={busy} maxLength={1000}
        required={resolution === 'AcceptEmass'} onChange={event => setRationale(event.target.value)}
        className="mt-2 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 dark:border-slate-600 dark:bg-slate-900" />
    </label>
    <p className="text-xs text-slate-500">Required when accepting a returned value. The rationale, authenticated reviewer and original field difference are retained in the audit history; reconciliation does not record an external receipt.</p>
    {selected.rationale && <p className="text-sm text-slate-600">Last recorded rationale: {selected.rationale}</p>}
    <p className="text-xs text-slate-500">Detected {new Date(selected.detectedAt).toLocaleString()}</p>
    {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
    <button type="button" disabled={!resolution || busy || resolution === 'AcceptEmass' && !rationale.trim()} onClick={() => void record()} className={systemPrimaryAction}>{busy ? 'Recording…' : 'Record resolution'}</button>
    <details className="rounded-lg border border-slate-200 p-3 text-sm dark:border-slate-700">
      <summary className="cursor-pointer font-medium">Bulk resolution for this page</summary>
      <p className="my-3 text-slate-500">Review every displayed difference before accepting all returned values. Other pages are unaffected.</p>
      <label className="mb-3 block font-medium">Bulk acceptance rationale
        <textarea aria-label="Bulk acceptance rationale" value={bulkRationale} disabled={busy} required maxLength={1000}
          onChange={event => setBulkRationale(event.target.value)}
          className="mt-2 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 dark:border-slate-600 dark:bg-slate-900" />
      </label>
      <button type="button" disabled={busy || !bulkRationale.trim()} onClick={() => void acceptPage()} className={systemSecondaryAction}>Accept all eMASS</button>
    </details>
  </div>;
}
