import { useEffect, useRef, useState } from 'react';
import { getExchangeExports, getExchangeHistory, recordExchange } from '../../api/emass-exchanges';
import type { ExchangeExport, ExchangeHistory, ExchangeOutcome } from '../../api/emass-exchanges';
import SetupDialog from '../workspace-operations/SetupDialog';

const outcomes: Record<ExchangeOutcome, string> = {
  TransferRecorded: 'Transfer recorded', ReceiptRecorded: 'Receipt recorded',
  ImportAccepted: 'Import accepted', ImportRejected: 'Import rejected', PartialImport: 'Partial import',
};
const inputClass = 'mt-1 w-full rounded border border-slate-300 bg-white p-2 text-sm dark:border-slate-600 dark:bg-slate-900';

function message(error: unknown): string {
  if (error && typeof error === 'object' && 'errors' in error) {
    const errors = (error as { errors?: { message?: string }[] }).errors;
    if (errors?.[0]?.message) return errors[0].message;
  }
  return error instanceof Error ? error.message : 'The manual exchange request failed.';
}

export default function EmassExchangeHistory({ systemId }: { systemId: string }) {
  return <ExchangeHistoryForSystem key={systemId} systemId={systemId} />;
}

function ExchangeHistoryForSystem({ systemId }: { systemId: string }) {
  const [history, setHistory] = useState<ExchangeHistory | null>(null);
  const [exports, setExports] = useState<ExchangeExport[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saved, setSaved] = useState(false);
  const [open, setOpen] = useState(false);
  const [packageId, setPackageId] = useState('');
  const [outcome, setOutcome] = useState<ExchangeOutcome>('ReceiptRecorded');
  const [workflow, setWorkflow] = useState('');
  const [reference, setReference] = useState('');
  const [eventTime, setEventTime] = useState('');
  const [notes, setNotes] = useState('');
  const [supersedesId, setSupersedesId] = useState('');
  const retry = useRef<{ payload: string; key: string } | null>(null);
  const mounted = useRef(true);
  const selectedExport = exports.find(item => item.packageId === packageId);

  async function load() {
    setLoading(true);
    try {
      const [nextHistory, nextExports] = await Promise.all([getExchangeHistory(systemId), getExchangeExports(systemId)]);
      if (!mounted.current) return;
      setHistory(nextHistory);
      setExports(nextExports);
      setError(null);
    } catch (reason) {
      if (mounted.current) setError(message(reason));
    } finally {
      if (mounted.current) setLoading(false);
    }
  }

  useEffect(() => {
    mounted.current = true;
    void load();
    return () => { mounted.current = false; };
  }, [systemId]);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!history?.canRecord || !selectedExport || busy || loading) return;
    const parsedTime = new Date(eventTime);
    if (!eventTime || Number.isNaN(parsedTime.getTime())) { setError('A valid event time is required.'); return; }
    const body = {
      packageId: selectedExport.packageId, packageHash: selectedExport.packageHash,
      exportGeneratedAt: selectedExport.exportGeneratedAt, outcome, receivingWorkflow: workflow,
      externalReference: reference, occurredAt: parsedTime.toISOString(), notes,
      expectedVersion: history.version, supersedesId: supersedesId || null,
    };
    const payload = JSON.stringify(body);
    if (retry.current?.payload !== payload) retry.current = { payload, key: crypto.randomUUID() };
    setBusy(true);
    setSaved(false);
    setError(null);
    try {
      await recordExchange(systemId, { ...body, idempotencyKey: retry.current.key });
      if (!mounted.current) return;
      setSaved(true);
      setOpen(false);
      setReference('');
      setEventTime('');
      setNotes('');
      setSupersedesId('');
      retry.current = null;
      await load();
    } catch (reason) {
      if (mounted.current) setError(message(reason));
    } finally {
      if (mounted.current) setBusy(false);
    }
  }

  return <section className="rounded-lg border border-slate-200 bg-white p-5 dark:border-slate-700 dark:bg-slate-900">
    <h2 className="text-lg font-semibold">Manual exchange history</h2>
    <p className="mt-2 text-sm text-slate-600 dark:text-slate-300">
      This records human-observed external outcomes; it does not connect to eMASS. Export, download and workbook comparison
      do not prove receipt, import acceptance or authorization. The authenticated recorder is retained automatically.
    </p>
    {error && !open && <div role="alert" className="mt-3 text-sm text-red-700">{error}</div>}
    {!open && <button type="button" disabled={busy || loading} onClick={() => void load()} className="mt-3 text-sm text-indigo-700 underline dark:text-indigo-300">
      Reload exchange history
    </button>}
    {loading && <p role="status" className="mt-3 text-sm">Loading manual exchange history…</p>}
    {saved && <p role="status" className="mt-3 text-sm text-green-700">Manual observation recorded. No authorization decision was changed.</p>}
    {history?.items.length === 0 && <p className="mt-4 text-sm text-slate-500">No manual exchange observations recorded.</p>}
    {!!history?.items.length && <div className="mt-4 overflow-x-auto">
      <table className="min-w-full text-left text-sm">
        <caption className="sr-only">Immutable manual observations of exported package transfer and receiving outcomes</caption>
        <thead><tr>{['Observation', 'Retained export', 'Receiving workflow / reference', 'Provenance', 'Correction / notes']
          .map(label => <th key={label} scope="col" className="border-b p-3">{label}</th>)}</tr></thead>
        <tbody>{history.items.map(item => <tr key={item.id}>
          <td className="border-b p-3"><strong>{outcomes[item.outcome]}</strong><div className="text-xs">#{item.version} · {item.id}</div></td>
          <td className="max-w-56 break-all border-b p-3">{item.packageId}<div className="mt-1 font-mono text-xs">{item.packageHash}</div>
            <div className="mt-1 text-xs">Version: {new Date(item.exportGeneratedAt).toLocaleString()}</div></td>
          <td className="border-b p-3">{item.receivingWorkflow}<div>{item.externalReference}</div></td>
          <td className="border-b p-3"><div>Event: {new Date(item.occurredAt).toLocaleString()}</div><div>{item.recordedBy}</div>
            <div className="text-xs">Recorded: {new Date(item.recordedAt).toLocaleString()}</div></td>
          <td className="border-b p-3">{item.supersedesId && <div>Corrects {item.supersedesId}</div>}{item.notes || '—'}</td>
        </tr>)}</tbody>
      </table>
    </div>}
    {history?.canRecord && <button type="button" disabled={busy || loading} onClick={() => { setError(null); setSaved(false); setOpen(true); }}
      className="ml-4 mt-3 rounded bg-indigo-600 px-4 py-2 text-sm text-white disabled:opacity-50">Record external observation</button>}
    {open && history && <SetupDialog title="Record external observation" busy={busy || loading} onClose={() => setOpen(false)}
      description="Record a human-observed outcome against an exact retained export. This does not send anything to eMASS or issue authorization.">
    <form onSubmit={event => void submit(event)} className="space-y-4">
      {error && <div className="space-y-2 text-sm text-red-700"><p role="alert">{error}</p>
        <button type="button" disabled={busy || loading} className="underline" onClick={() => void load()}>Reload exchange history</button>
      </div>}
      {!exports.length && <p className="text-sm text-amber-800">No completed package with a retained hash is available. Generate a package first.</p>}
      {!history.canRecord && <p role="alert">Recording permission is no longer available. Your entered values have not been saved.</p>}
      <fieldset disabled={busy || loading || !exports.length || !history.canRecord} className="grid gap-4 sm:grid-cols-2">
        <label className="text-sm">Retained export<select required aria-label="Retained export" className={inputClass} value={packageId}
          onChange={event => { setPackageId(event.target.value); setSupersedesId(''); }}>
          <option value="">Select a retained package</option>
          {exports.map(item => <option key={item.packageId} value={item.packageId}>
            {item.purpose} · {new Date(item.exportGeneratedAt).toLocaleString()} · {item.packageId}
          </option>)}
        </select></label>
        <label className="text-sm">Observed outcome<select aria-label="Observed outcome" className={inputClass}
          value={outcome} onChange={event => setOutcome(event.target.value as ExchangeOutcome)}>
          {Object.entries(outcomes).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
        </select></label>
        {selectedExport && <p className="break-all font-mono text-xs sm:col-span-2">Retained hash: {selectedExport.packageHash}</p>}
        <label className="text-sm">Receiving workflow<input required maxLength={200} aria-label="Receiving workflow" className={inputClass}
          value={workflow} onChange={event => setWorkflow(event.target.value)} /></label>
        <label className="text-sm">External reference<input required maxLength={500} aria-label="External reference" className={inputClass}
          value={reference} onChange={event => setReference(event.target.value)} /></label>
        <label className="text-sm">Event time<input required type="datetime-local" aria-label="Event time" className={inputClass}
          value={eventTime} onChange={event => setEventTime(event.target.value)} /><span className="text-xs text-slate-500">Your local time; retained as UTC.</span></label>
        <label className="text-sm">Correction of<select aria-label="Correction of" className={inputClass} value={supersedesId}
          onChange={event => setSupersedesId(event.target.value)}>
          <option value="">New observation (not a correction)</option>
          {history.items.filter(item => item.packageId === packageId && !history.items.some(other => other.supersedesId === item.id))
            .map(item => <option key={item.id} value={item.id}>#{item.version}: {outcomes[item.outcome]} · {item.externalReference}</option>)}
        </select></label>
        <label className="text-sm sm:col-span-2">Notes / correction reason<textarea required={!!supersedesId} maxLength={4000}
          aria-label="Notes / correction reason" className={inputClass} value={notes} onChange={event => setNotes(event.target.value)} /></label>
        <button type="submit" disabled={!selectedExport} className="rounded bg-indigo-600 px-4 py-2 text-sm text-white disabled:opacity-50 sm:col-span-2">
          {busy ? 'Recording observation…' : 'Record observation'}
        </button>
      </fieldset>
      <div className="flex justify-end"><button type="button" disabled={busy || loading} onClick={() => setOpen(false)}
        className="rounded border px-4 py-2 text-sm">Cancel</button></div>
    </form>
    </SetupDialog>}
  </section>;
}
