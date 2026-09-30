import { useEffect, useState, type FormEvent } from 'react';
import { Link, useParams, useSearchParams } from '../workspaces/workspaceNavigation';
import SetupDialog from '../workspace-operations/SetupDialog';
import { listSystemHistory, type SystemHistoryEntry, type SystemHistoryResponse } from './systemHistoryApi';
import { SystemTaskColumns, SystemTaskHeading, SystemTaskSupport, systemPanel, systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';

export default function SystemHistoryPage() {
  const { id } = useParams<{ id: string }>();
  return id ? <History key={id} systemId={id} /> : <p role="alert">Select a system to inspect retained history.</p>;
}

function History({ systemId }: { systemId: string }) {
  const [search, setSearch] = useSearchParams();
  const pageValue = Number(search.get('page') ?? 1);
  const page = Number.isSafeInteger(pageValue) && pageValue > 0 ? pageValue : 1;
  const eventFilter = search.get('eventType') ?? '';
  const fromFilter = search.get('from') ?? '';
  const toFilter = search.get('to') ?? '';
  const [eventType, setEventType] = useState(eventFilter);
  const [from, setFrom] = useState(fromFilter);
  const [to, setTo] = useState(toFilter);
  const [data, setData] = useState<SystemHistoryResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [selected, setSelected] = useState<SystemHistoryEntry | null>(null);
  useEffect(() => { setEventType(eventFilter); setFrom(fromFilter); setTo(toFilter); }, [eventFilter, fromFilter, toFilter]);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError(null); setData(null); setSelected(null);
    void listSystemHistory(systemId, { page, pageSize: 25, eventType: eventFilter || undefined,
      from: fromFilter || undefined, to: toFilter ? `${toFilter}T23:59:59.999Z` : undefined }, controller.signal)
      .then(result => { if (!controller.signal.aborted) setData(result); })
      .catch(reason => {
        if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message
          : reason && typeof reason === 'object' && 'error' in reason && typeof reason.error === 'string'
            ? reason.error : 'System history is unavailable.');
      }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, page, eventFilter, fromFilter, toFilter, attempt]);

  const apply = (event: FormEvent) => {
    event.preventDefault();
    const query = new URLSearchParams();
    if (eventType.trim()) query.set('eventType', eventType.trim());
    if (from) query.set('from', from);
    if (to) query.set('to', to);
    setSearch(query);
  };
  const turnPage = (next: number) => { const query = new URLSearchParams(search); query.set('page', String(next)); setSearch(query); };
  const input = 'mt-1 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm dark:border-slate-600 dark:bg-slate-900';

  return <>
    <SystemTaskHeading title="Activity & decision history" description="Trace recorded system activity back to its actors and retained record references." />
    <SystemTaskColumns support={<>
      <SystemTaskSupport title="Contributes to"><p>Audit trail / Package provenance</p></SystemTaskSupport>
      <SystemTaskSupport title="Retained activity scope">
        <p>This view reads the system&apos;s dashboard activity ledger. It is not a complete platform or provider audit log.</p>
        <p>Record references are shown as retained. A reference is not proof that its current content matches a historical version.</p>
      </SystemTaskSupport>
      <SystemTaskSupport title="Related records">
        <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/authorize`}>Recorded authorization decisions</Link>
        <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/documents?tab=exports`}>Retained export packages</Link>
      </SystemTaskSupport>
    </>}>
      <form onSubmit={apply} className={`${systemPanel} grid gap-4 sm:grid-cols-2`}>
        <label className="text-sm">Event type<input maxLength={50} className={input} value={eventType} onChange={event => setEventType(event.target.value)} placeholder="Exact recorded event type" /></label>
        <div className="grid grid-cols-2 gap-3">
          <label className="text-sm">From (UTC)<input className={input} type="date" value={from} onChange={event => setFrom(event.target.value)} /></label>
          <label className="text-sm">Through (UTC)<input className={input} type="date" value={to} onChange={event => setTo(event.target.value)} /></label>
        </div>
        <div className="flex flex-wrap items-center gap-3 sm:col-span-2">
          <button className={systemPrimaryAction} type="submit">Filter history</button>
          <button className={systemSecondaryAction} type="button" onClick={() => { setEventType(''); setFrom(''); setTo(''); setSearch(new URLSearchParams()); }}>Clear filters</button>
          {data && <p className="text-xs text-slate-500">{data.totalCount} retained {data.totalCount === 1 ? 'event' : 'events'}</p>}
        </div>
      </form>
      <section className={systemPanel} aria-label="Retained system activity">
        {loading && <p role="status" className="text-sm text-slate-500">Loading retained history…</p>}
        {error && <div className="space-y-3 text-sm text-amber-900"><p role="alert">{error}</p>
          <button className={systemSecondaryAction} type="button" onClick={() => setAttempt(value => value + 1)}>Retry history</button></div>}
        {!loading && !error && data && <>
          {data.items.length === 0 ? <p className="text-sm text-slate-500">No retained activity matches these filters.</p>
            : <ol className="space-y-6">{data.items.map(entry => <li key={entry.id} className="flex gap-4">
              <span aria-hidden="true" className="mt-1 flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-indigo-50 text-indigo-700">•</span>
              <article className="min-w-0 flex-1 border-b border-slate-100 pb-5 dark:border-slate-700">
                <time dateTime={entry.timestamp} className="text-xs font-medium uppercase tracking-wide text-slate-500">{new Date(entry.timestamp).toLocaleString()}</time>
                <h2 className="mt-2 break-words text-sm font-semibold">{entry.summary}</h2>
                <p className="mt-1 break-words text-sm text-slate-500">{entry.actor} · {entry.eventType}</p>
                <button type="button" aria-label={`View record: ${entry.summary}`} className={`mt-3 ${systemSecondaryAction}`} onClick={() => setSelected(entry)}>View record →</button>
              </article>
            </li>)}</ol>}
          <nav aria-label="History pages" className="mt-6 flex flex-wrap items-center justify-between gap-3">
            <button type="button" className={systemSecondaryAction} disabled={page <= 1} onClick={() => turnPage(page - 1)}>Previous</button>
            <span className="text-sm text-slate-500">Page {page} of {Math.max(1, Math.ceil(data.totalCount / 25))}</span>
            <button type="button" className={systemSecondaryAction} disabled={page * 25 >= data.totalCount} onClick={() => turnPage(page + 1)}>Next</button>
          </nav>
        </>}
      </section>
    </SystemTaskColumns>
    {selected && <SetupDialog title="Retained activity record" placement="right" busy={false} onClose={() => setSelected(null)}>
      <p className="mb-5 text-sm">{selected.summary}</p>
      <dl className="space-y-4 text-sm">{[
        ['Event ID', selected.id], ['Event type', selected.eventType], ['Actor', selected.actor], ['Recorded at', selected.timestamp],
        ['Related record type', selected.relatedEntityType ?? 'Not recorded'], ['Related record ID', selected.relatedEntityId ?? 'Not recorded'],
      ].map(([label, value]) => <div key={label}><dt className="text-xs text-slate-500">{label}</dt><dd className="mt-1 break-all">{value}</dd></div>)}</dl>
    </SetupDialog>}
  </>;
}
