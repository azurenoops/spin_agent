import { useEffect, useState } from 'react';
import { getImpactReceipts, type NarrativeImpactReceiptPage } from '../../api/narrativeLibrary';
import { narrativeErrorMessage } from './referenceLibraryUtils';

export default function NarrativeImpactHistory({ systemId, proposalId, creationTrigger }: {
  systemId: string; proposalId: string; creationTrigger: unknown;
}) {
  return <History key={`${systemId}:${proposalId}`} systemId={systemId} proposalId={proposalId} creationTrigger={creationTrigger} />;
}
function History({ systemId, proposalId, creationTrigger }: { systemId: string; proposalId: string; creationTrigger: unknown }) {
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [state, setState] = useState<{ key: string; data: NarrativeImpactReceiptPage | null; error: string }>({ key: '', data: null, error: '' });
  const key = `${page}:${revision}`;
  useEffect(() => {
    const controller = new AbortController();
    setState({ key, data: null, error: '' });
    getImpactReceipts(systemId, proposalId, page, controller.signal).then(data => {
      if (!controller.signal.aborted) setState({ key, data, error: '' });
    }).catch(error => { if (!controller.signal.aborted) setState({ key, data: null, error: narrativeErrorMessage(error) }); });
    return () => controller.abort();
  }, [systemId, proposalId, page, key]);
  const current = state.key === key ? state : { data: null, error: '' };
  return <>
    <section className="nw-sources" aria-label="Proposal creation trigger">
      <h3>Proposal creation trigger</h3>
      <p className="nw-muted">Captured when this proposal was created; this is not a chronological delivery history.</p>
      {creationTrigger == null ? <p>No creation trigger recorded.</p> : <pre>{JSON.stringify(creationTrigger, null, 2)}</pre>}
    </section>
    <section className="nw-sources" aria-label="Source delivery history">
      <h3>Source delivery history</h3>
      <p className="nw-muted">Immutable receipts. Recording time is not the original provider event time. Missing historical fields are not inferred.</p>
      {current.error ? <div role="alert">{current.error}<button onClick={() => setRevision(value => value + 1)}>Retry delivery history</button></div>
        : !current.data ? <p role="status">Loading delivery history...</p>
        : current.data.items.length === 0 ? <p>No delivery receipts recorded.</p>
        : current.data.items.map(item => <article key={item.id} className="nw-reference">
          <h4>{item.impactId}</h4><p>Delivery recorded at {item.recordedAt}</p>
          <dl><dt>Source kind</dt><dd>{item.sourceKind ?? 'Not recorded'}</dd><dt>Source ID</dt><dd>{item.sourceId ?? 'Not recorded'}</dd>
            <dt>Source actor</dt><dd>{item.sourceActor ?? 'Not recorded'}</dd></dl>
          {item.sourceContext ? <pre>{JSON.stringify(item.sourceContext, null, 2)}</pre> : <p>Source context: Not recorded</p>}
        </article>)}
      <nav aria-label="Delivery history pages" className="flex items-center gap-3">
        <button disabled={!current.data || page <= 1} onClick={() => setPage(value => value - 1)}>Previous receipt page</button>
        <span>Page {page}{current.data ? ` / ${Math.max(1, Math.ceil(current.data.totalCount / current.data.pageSize))}` : ''}</span>
        <button disabled={!current.data || page * current.data.pageSize >= current.data.totalCount} onClick={() => setPage(value => value + 1)}>Next receipt page</button>
      </nav>
    </section>
  </>;
}
