import { useMemo, useState, type ReactNode } from 'react';
import { ArrowRight, FileSearch, LayoutGrid, List, Search } from 'lucide-react';
import { systemPanel, systemSecondaryAction } from '../systems/SystemTaskPresentation';
import './RemediationWorkspace.css';

export interface FindingQueueItem {
  id: string; title: string; controlId: string; severity: string; disposition: string;
  owner: string | null; workStatus: string; readyToVerify: boolean; closed: boolean;
  sourceName: string | null; planRevision: number | null;
}
type QueueView = 'all' | 'owner' | 'verify' | 'closed';
const pageSize = 25;
const nextAction = (item: FindingQueueItem) => item.closed ? 'View history'
  : item.readyToVerify ? 'Review evidence' : !item.owner ? 'Assign work' : 'View work';

export default function FindingsQueue({ items, onOpen, emptyAction, initialView = 'list' }: {
  items: FindingQueueItem[]; onOpen: (id: string) => void; emptyAction?: ReactNode; initialView?: 'list' | 'board';
}) {
  const [view, setView] = useState<QueueView>('all');
  const [layout, setLayout] = useState(initialView);
  const [search, setSearch] = useState('');
  const [severity, setSeverity] = useState('');
  const [page, setPage] = useState(1);
  const views: { key: QueueView; label: string; count: number }[] = [
    { key: 'all', label: 'All findings', count: items.length },
    { key: 'owner', label: 'Needs owner', count: items.filter(i => !i.closed && !i.owner).length },
    { key: 'verify', label: 'Ready to verify', count: items.filter(i => !i.closed && i.readyToVerify).length },
    { key: 'closed', label: 'Closed', count: items.filter(i => i.closed).length },
  ];
  const filtered = useMemo(() => items.filter(item => {
    if (view === 'owner' && (item.closed || item.owner)) return false;
    if (view === 'verify' && (item.closed || !item.readyToVerify)) return false;
    if (view === 'closed' && !item.closed) return false;
    return (!severity || severity === item.severity)
      && `${item.title} ${item.controlId} ${item.owner ?? ''} ${item.sourceName ?? ''}`.toLowerCase().includes(search.trim().toLowerCase());
  }), [items, view, severity, search]);
  const pages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const currentPage = Math.min(page, pages);
  const visible = filtered.slice((currentPage - 1) * pageSize, currentPage * pageSize);
  const reset = () => { setSearch(''); setSeverity(''); setView('all'); setPage(1); };
  if (!items.length) return <section className={`${systemPanel} rw-empty`}>
    <FileSearch aria-hidden="true" size={30} />
    <h2>No findings recorded</h2>
    <p>Review assessment observations or document a manually identified weakness.</p>
    <p>An empty queue does not establish that the system passed its assessment.</p>
    {emptyAction}
  </section>;
  return <section className="rw-queue" aria-label="Findings work queue">
    <div className="rw-tabs" role="group" aria-label="Finding views">{views.map(tab =>
      <button type="button" key={tab.key} aria-pressed={view === tab.key}
        onClick={() => { setView(tab.key); setPage(1); }}>{tab.label} ({tab.count})</button>)}</div>
    <div className="rw-toolbar">
      <label className="rw-search"><Search size={17} aria-hidden="true" /><span className="sr-only">Find by title, control or owner</span>
        <input type="search" placeholder="Find by title, control or owner…" value={search}
          onChange={event => { setSearch(event.target.value); setPage(1); }} /></label>
      <label><span className="sr-only">Severity</span><select value={severity}
        onChange={event => { setSeverity(event.target.value); setPage(1); }}>
        <option value="">All severities</option>{Array.from(new Set(items.map(i => i.severity))).sort().map(value =>
          <option key={value}>{value}</option>)}</select></label>
      <div className="rw-layout" role="group" aria-label="Queue layout">
        <button type="button" aria-pressed={layout === 'list'} onClick={() => setLayout('list')}><List size={16} aria-hidden="true" />List</button>
        <button type="button" aria-pressed={layout === 'board'} onClick={() => setLayout('board')}><LayoutGrid size={16} aria-hidden="true" />Board</button>
      </div>
    </div>
    {!filtered.length ? <div className={`${systemPanel} rw-empty`}><h3>No matching findings</h3>
      <button type="button" className={systemSecondaryAction} onClick={reset}>Clear filters</button></div>
      : layout === 'list' ? <div className="rw-table-wrap"><table className="rw-table">
        <thead><tr><th>Finding</th><th>Priority</th><th>Work status</th><th>Owner</th><th>Next step</th></tr></thead>
        <tbody>{visible.map(item => <tr key={item.id}>
          <td><button type="button" className="rw-title" onClick={() => onOpen(item.id)}>{item.title}</button>
            <p>{item.controlId || 'Control not recorded'}</p>
            <small>{item.sourceName ?? 'Source not recorded'}{item.planRevision !== null ? ` · Plan revision ${item.planRevision}` : ''}</small></td>
          <td><span className={`rw-status ${['Critical', 'High', 'CatI'].includes(item.severity) ? 'rw-attention' : ''}`}>{item.severity}</span></td>
          <td><span className="rw-status">{item.workStatus}</span><small>Disposition: <span>{item.disposition}</span></small></td>
          <td>{item.owner ?? 'Unassigned'}</td>
          <td><button type="button" className={systemSecondaryAction} onClick={() => onOpen(item.id)}>
            {nextAction(item)}<ArrowRight size={14} aria-hidden="true" /></button></td>
        </tr>)}</tbody>
      </table></div> : <div className="rw-board" aria-label="Findings board">
        {[
          { title: 'Needs work', rows: visible.filter(i => !i.closed && !i.readyToVerify) },
          { title: 'Ready to verify', rows: visible.filter(i => !i.closed && i.readyToVerify) },
          { title: 'Closed', rows: visible.filter(i => i.closed) },
        ].map(column => <section className={systemPanel} key={column.title}><h3>{column.title}</h3>
          {!column.rows.length && <p className="rw-muted">No matching findings on this page.</p>}
          {column.rows.map(item => <button type="button" key={item.id} className="rw-board-card" onClick={() => onOpen(item.id)}>
            <strong>{item.title}</strong><span>{item.controlId} · {item.severity}</span>
            <span>{item.owner ?? 'Unassigned'} · {item.workStatus}</span>
            <small>Finding: {item.disposition}</small>
          </button>)}</section>)}
      </div>}
    {filtered.length > 0 && <div className="rw-pagination">
      <span>{(currentPage - 1) * pageSize + 1}–{Math.min(currentPage * pageSize, filtered.length)} of {filtered.length} findings</span>
      {pages > 1 && <div><button type="button" aria-label="Previous page" disabled={currentPage === 1}
        className={systemSecondaryAction} onClick={() => setPage(currentPage - 1)}>Previous</button>
        <span>Page {currentPage} of {pages}</span>
        <button type="button" aria-label="Next page" disabled={currentPage === pages}
          className={systemSecondaryAction} onClick={() => setPage(currentPage + 1)}>Next</button></div>}
    </div>}
  </section>;
}
