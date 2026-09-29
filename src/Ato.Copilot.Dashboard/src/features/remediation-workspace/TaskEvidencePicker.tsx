import { useEffect, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { evidenceError, getEvidenceCatalog, type EvidenceCatalog } from '../../api/evidenceCatalog';
import { systemPanel, systemSecondaryAction } from '../systems/SystemTaskPresentation';

export default function TaskEvidencePicker({ systemId, busy, onLink }: {
  systemId: string; busy: boolean; onLink: (id: string) => Promise<void>;
}) {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [data, setData] = useState<EvidenceCatalog | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError(null); setData(null);
    getEvidenceCatalog(systemId, { view: 'system', search, source: 'Manual', family: '', category: '',
      dateFrom: '', dateTo: '', sortBy: 'recordedAt', sortOrder: 'desc', page, pageSize: 20 }, controller.signal)
      .then(value => {
        if (controller.signal.aborted) return;
        const unavailable = value.sources.find(source => source.source === 'system' && source.state !== 'available');
        if (unavailable) setError(unavailable.message ?? 'System evidence is unavailable. Retry with current access.');
        else setData(value);
      })
      .catch(reason => { if (!controller.signal.aborted) setError(evidenceError(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, search, page, attempt]);
  return <section className={`${systemPanel} rw-detail`}><h3>Link retained corrective evidence</h3>
    <label>Find system evidence<input type="search" value={search} disabled={busy}
      onChange={event => { setSearch(event.target.value); setPage(1); }} /></label>
    <p className="rw-muted">Choose an existing system artifact. Linking retains its name and hash and resets previous task verification.
      Provider summaries and automated assessment evidence are not accepted by this task-link contract.</p>
    {loading && <p role="status">Loading evidence choices…</p>}
    {error && <div role="alert" className="rw-error"><p>{error}</p><button type="button" disabled={busy}
      className={systemSecondaryAction} onClick={() => setAttempt(value => value + 1)}>Retry evidence choices</button></div>}
    {data && <>
      {!data.items.some(item => item.source === 'Manual') && <p>No matching system artifacts.</p>}
      {data.items.filter(item => item.source === 'Manual').map(item => <article key={item.id} className="rw-actions">
        <span>{item.name}</span><button className={systemSecondaryAction} type="button" disabled={busy}
          aria-label={`Link ${item.name}`} onClick={() => void onLink(item.recordId)}>Link evidence</button></article>)}
      {data.totalCount !== null && data.totalCount > 20 && <div className="rw-actions">
        <button type="button" className={systemSecondaryAction} disabled={busy || page === 1} onClick={() => setPage(page - 1)}>Previous evidence</button>
        <span>Page {page} of {Math.ceil(data.totalCount / 20)}</span>
        <button type="button" className={systemSecondaryAction} disabled={busy || page * 20 >= data.totalCount} onClick={() => setPage(page + 1)}>Next evidence</button>
      </div>}
    </>}
    <Link className="rw-card-link" to={`/systems/${systemId}/evidence`}>Open evidence repository</Link>
  </section>;
}
