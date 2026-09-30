import { useEffect, useRef, useState } from 'react';
import apiClient from '../../api/client';
import SetupDialog from '../workspace-operations/SetupDialog';
import { systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';

const labels = { Operational: 'Operational', UnderDevelopment: 'Under development',
  Disposed: 'Disposed', MajorModification: 'Major modification' };
type Status = keyof typeof labels;
interface RecordState { systemId: string; operationalStatus: Status | null; canManage: boolean }
function valid(value: RecordState, systemId: string) {
  return value && value.systemId === systemId && typeof value.canManage === 'boolean'
    && (value.operationalStatus === null || Object.hasOwn(labels, value.operationalStatus));
}
export default function SystemOperationalStatus({ systemId }: { systemId: string }) {
  const [data, setData] = useState<RecordState | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [open, setOpen] = useState(false);
  const [selection, setSelection] = useState<Status | ''>('');
  const [busy, setBusy] = useState(false);
  const writing = useRef(false);
  const root = `/systems/${encodeURIComponent(systemId)}/operational-status`;
  useEffect(() => {
    const controller = new AbortController();
    setData(null); setError(null);
    void apiClient.get<RecordState>(root, { signal: controller.signal }).then(({ data: value }) => {
      if (controller.signal.aborted) return;
      if (!valid(value, systemId)) throw new Error('The operational status could not be verified for this system.');
      setData(value);
    }).catch(reason => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Operational status unavailable.'); });
    return () => controller.abort();
  }, [root, systemId, attempt]);
  const save = async () => {
    if (!selection || !data?.canManage || writing.current) return;
    writing.current = true; setBusy(true); setError(null);
    try {
      const { data: result } = await apiClient.put<RecordState>(root, { operationalStatus: selection });
      if (!valid(result, systemId) || result.operationalStatus !== selection) throw new Error('The selected operational status was not confirmed.');
      setData(result); setOpen(false);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The operational status could not be saved.');
    } finally { writing.current = false; setBusy(false); }
  };
  return <section className="sm:col-span-2">
    <p className="text-xs text-slate-500">Recorded operational status</p>
    <p className="mt-1 text-sm">{data ? data.operationalStatus ? labels[data.operationalStatus] : 'Not recorded' : error ? 'Unavailable' : 'Loading…'}</p>
    {data?.canManage && <button type="button" className="mt-1 text-xs text-indigo-700 underline"
      onClick={() => { setSelection(data.operationalStatus ?? ''); setError(null); setOpen(true); }}>Manage operational status</button>}
    {error && !open && <p role="alert" className="mt-2 text-xs text-red-700">{error}
      <button className="ml-2 underline" onClick={() => setAttempt(value => value + 1)}>Retry status</button></p>}
    {open && <SetupDialog placement="right" title="Record operational status" busy={busy} onClose={() => setOpen(false)}
      description="Record the actual operational state used by the SSP. This is separate from RMF phase, package readiness and AO authorization. No value is selected automatically.">
      <form onSubmit={event => { event.preventDefault(); void save(); }} className="space-y-4">
        {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
        <label className="block text-sm">Operational status
          <select autoFocus required aria-label="Operational status" disabled={busy} value={selection}
            className="mt-2 block w-full rounded border border-slate-300 bg-white p-3 dark:bg-slate-900"
            onChange={event => {
              const value = event.target.value;
              if (value === '' || value === 'Operational' || value === 'UnderDevelopment' || value === 'Disposed' || value === 'MajorModification') setSelection(value);
            }}>
            <option value="">Select the actual status</option>
            {Object.entries(labels).map(([key, value]) => <option key={key} value={key}>{value}</option>)}
          </select>
        </label>
        <p className="text-xs text-slate-500">The change is audited and may make an earlier readiness result stale. Disposed records metadata; it does not delete the system.</p>
        <div className="flex gap-3"><button type="submit" disabled={busy || !selection} className={systemPrimaryAction}>Save operational status</button>
          <button type="button" disabled={busy} className={systemSecondaryAction} onClick={() => setOpen(false)}>Cancel</button></div>
      </form>
    </SetupDialog>}
  </section>;
}
