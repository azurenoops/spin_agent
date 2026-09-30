import { useCallback, useEffect, useRef, useState } from 'react';
import {
  AlertTriangle, ArrowRight, CircleAlert, Clock3, FileText, Info, RefreshCw,
  Search, SlidersHorizontal,
} from 'lucide-react';
import { useSearchParams } from '../workspaces/workspaceNavigation';
import {
  getControlNarrativeDetail,
  getControlNarrativeWorkspace,
  type ControlNarrativeDetailResponse,
  type ControlNarrativeWorkspaceItem,
  type ControlNarrativeWorkspaceResponse,
  type NarrativeStatementKind,
} from '../../api/controlNarrativeWorkspace';
import ControlNarrativeDrawer from './ControlNarrativeDrawer';
import './ControlNarrativeWorkspace.css';

interface Props {
  systemId: string;
  onOpenEditor: () => void;
  onOpenLibrary: () => void;
  onReviewProposal: (proposalId: string) => void;
}

type WorkspaceView = 'needs-attention' | 'all' | 'approved';

function message(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (error && typeof error === 'object' && 'title' in error && typeof error.title === 'string') return error.title;
  return 'The narrative workspace could not be loaded.';
}

function StatementState({ label, state, stale }: { label: string; state: string; stale: boolean }) {
  const value = stale ? 'Update proposed' : state === 'NotStarted' ? 'Not recorded' : state;
  const tone = stale || /proposal|stale/i.test(value) ? 'warning'
    : /missing|revision/i.test(value) ? 'danger'
      : /approved/i.test(value) ? 'success'
        : /draft|review/i.test(value) ? 'draft' : 'muted';
  return <span className={`cnw-statement-state cnw-statement-${tone}`} aria-label={`${label} ${value}`}>
    {tone === 'warning' ? <Clock3 size={13} /> : tone === 'danger' ? <CircleAlert size={13} /> : <span className="cnw-state-dot" />}
    <span>{value}</span>
  </span>;
}

export default function ControlNarrativeWorkspace({ systemId, onOpenEditor, onOpenLibrary, onReviewProposal }: Props) {
  const [params, setParams] = useSearchParams();
  const [data, setData] = useState<ControlNarrativeWorkspaceResponse | null>(null);
  const [detail, setDetail] = useState<ControlNarrativeDetailResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState('');
  const [detailError, setDetailError] = useState('');
  const [revision, setRevision] = useState(0);
  const returnFocus = useRef<HTMLElement | null>(null);

  const view = (params.get('view') as WorkspaceView | null) ?? 'needs-attention';
  const search = params.get('search') ?? '';
  const family = params.get('family') ?? 'All';
  const status = params.get('status') ?? 'All';
  const selectedControl = params.get('control');
  const selectedStatement = params.get('statement') === 'technical' ? 'technical' : 'policy';
    const page = Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1);
    const pageSize = 25;

    useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError('');
    getControlNarrativeWorkspace(systemId, {
      view: view === 'all' ? 'all-controls' : view === 'approved' ? 'approved-statements' : 'needs-attention',
      search: search.trim() || undefined,
      family: family === 'All' ? undefined : family,
      status: status === 'All' ? undefined : status,
      page,
      pageSize,
    }, controller.signal)
      .then(setData)
      .catch(reason => { if (!controller.signal.aborted) setError(message(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, revision, view, search, family, status, page]);

  useEffect(() => {
    if (!selectedControl) {
      setDetail(null);
      setDetailError('');
      return;
    }
    const controller = new AbortController();
    setDetailLoading(true);
    setDetailError('');
    getControlNarrativeDetail(systemId, selectedControl, controller.signal)
      .then(setDetail)
      .catch(reason => { if (!controller.signal.aborted) setDetailError(message(reason)); })
      .finally(() => { if (!controller.signal.aborted) setDetailLoading(false); });
    return () => controller.abort();
  }, [systemId, selectedControl, revision]);

  const setParam = useCallback((key: string, value: string | null, replace = false) => {
    const next = new URLSearchParams(params);
    if (!value || value === 'All') next.delete(key); else next.set(key, value);
    if (['search', 'family', 'status', 'view'].includes(key)) next.delete('page');
    setParams(next, { replace });
  }, [params, setParams]);

  const visible = data?.items ?? [];
  const selectedTotal = data ? view === 'all' ? data.counts.allControls
    : view === 'approved' ? data.counts.approvedStatements : data.counts.needsAttention : 0;
  const pageCount = Math.max(1, Math.ceil(selectedTotal / pageSize));
  const proposedItem = data?.items.find(item => item.policy.proposalId || item.technical.proposalId);

  const openControl = (item: ControlNarrativeWorkspaceItem, event: React.MouseEvent<HTMLElement>) => {
    returnFocus.current = event.currentTarget;
    const next = new URLSearchParams(params);
    next.set('control', item.controlId);
    next.set('statement', !item.policy.hasContent ? 'policy' : 'technical');
    setParams(next);
  };
  const closeDrawer = useCallback(() => {
    const next = new URLSearchParams(params);
    next.delete('control'); next.delete('statement'); next.delete('drawerTab');
    setParams(next);
    window.setTimeout(() => returnFocus.current?.focus(), 0);
  }, [params, setParams]);

  return <section className="cnw-page" aria-labelledby="cnw-heading">
    <div className="cnw-context-heading">
      <strong>Control narratives <span>· A clearer next step</span></strong>
      <small>Current system records · Select a control to inspect details</small>
    </div>

    {error && <div className="cnw-error" role="alert"><AlertTriangle size={18} /><span>{error}</span>
      <button type="button" onClick={() => setRevision(value => value + 1)}><RefreshCw size={16} />Retry</button></div>}
    {loading && <p role="status" className="cnw-loading">Loading control statements…</p>}

    {data && !error && <div className="cnw-work-card">
      <header className="cnw-card-header">
        <div><h1 id="cnw-heading">Document how your controls work</h1>
          <p>Policy and technical statements for your system security plan.</p></div>
        <div className="cnw-scope-count"><strong>{data.counts.allControls}</strong><span>controls in scope</span></div>
        <details className="cnw-review-info"><summary><Info size={14} />Documentation &amp; review info</summary>
          <div><button type="button" onClick={onOpenLibrary}>Narrative sources</button>
            <button type="button" onClick={onOpenEditor}>Open full editor</button></div>
        </details>
      </header>
      {data.counts.proposedUpdates > 0 && <div className="cnw-notification">
        <Clock3 size={21} /><div><strong>{data.counts.proposedUpdates} proposed update{data.counts.proposedUpdates === 1 ? '' : 's'} need{data.counts.proposedUpdates === 1 ? 's' : ''} attention</strong>
          <p>Compare the proposed statement with its sources.</p></div>
        <button type="button" disabled={!proposedItem} onClick={event => proposedItem && openControl(proposedItem, event)}>
          View proposed update <ArrowRight size={15} /></button></div>}
      <div className="cnw-view-tabs" role="tablist" aria-label="Narrative views">
        <button type="button" role="tab" aria-selected={view === 'needs-attention'} onClick={() => setParam('view', 'needs-attention')}>Needs attention ({data.counts.needsAttention})</button>
        <button type="button" role="tab" aria-selected={view === 'all'} onClick={() => setParam('view', 'all')}>All controls ({data.counts.allControls})</button>
        <button type="button" role="tab" aria-selected={view === 'approved'} onClick={() => setParam('view', 'approved')}>Approved statements ({data.counts.approvedStatements})</button>
      </div>
      <div className="cnw-toolbar">
        <label className="cnw-search"><Search size={17} /><span className="sr-only">Search controls</span>
          <input aria-label="Search controls" value={search} placeholder="Search by control ID or name" onChange={event => setParam('search', event.target.value)} /></label>
        <label><span>Family</span><select value={family} onChange={event => setParam('family', event.target.value)}>
          <option>All</option>{['AC', 'AT', 'AU', 'CA', 'CM', 'CP', 'IA', 'IR', 'MA', 'MP', 'PE', 'PL', 'PM', 'PS', 'PT', 'RA', 'SA', 'SC', 'SI', 'SR']
            .map(value => <option key={value}>{value}</option>)}</select></label>
        <details className="cnw-more-filters"><summary><SlidersHorizontal size={16} />Filters</summary>
          <label><span>Implementation status</span><select value={status} onChange={event => setParam('status', event.target.value)}>
            <option>All</option><option>Implemented</option><option>PartiallyImplemented</option><option>Planned</option><option>NotApplicable</option>
          </select></label>
        </details>
      </div>
      {visible.length === 0 ? <div className="cnw-empty-state"><strong>No controls match this view.</strong>
        <p>Try another view or clear one of the filters.</p></div>
        : <div className="cnw-table-wrap"><table className="cnw-table">
          <thead><tr><th>Control</th><th>Policy statement</th><th>Technical statement</th><th>Next step</th></tr></thead>
          <tbody>{visible.map(item => <tr key={item.id} className={selectedControl === item.controlId ? 'cnw-selected-row' : undefined}>
            <td><button type="button" className="cnw-control-link" onClick={event => openControl(item, event)}>
              <FileText size={18} /><span><strong>{item.controlTitle}</strong><small>{item.controlId}</small></span></button></td>
            <td><StatementState label="Policy" state={item.policy.state} stale={item.policy.isStale} /></td>
            <td><StatementState label="Technical" state={item.technical.state} stale={item.technical.isStale} /></td>
            <td><button type="button" className="cnw-next-action" disabled={item.nextAction === 'Blocked'}
              title={item.nextActionReason ?? undefined} onClick={event => openControl(item, event)}>
              {item.nextActionLabel}<ArrowRight size={14} /></button>
              {item.nextActionReason && <small>{item.nextActionReason}</small>}</td>
          </tr>)}</tbody>
        </table></div>}
      <div className="cnw-list-footer"><span>Showing {visible.length} of {selectedTotal} records</span>
      {selectedTotal > pageSize && <nav className="cnw-pagination" aria-label="Narrative pages">
        <button type="button" disabled={page <= 1} onClick={() => setParam('page', String(page - 1))}>Previous</button>
        <span>Page {Math.min(page, pageCount)} of {pageCount}</span>
        <button type="button" disabled={page >= pageCount} onClick={() => setParam('page', String(page + 1))}>Next</button>
      </nav>}</div>
      <details className="cnw-ssp-help"><summary>How these statements contribute to your SSP</summary>
        <p>Policy and Technical statements supply control implementation documentation to the SSP document, OSCAL SSP, and eMASS export paths. Saving a statement does not prove the control is implemented, assessed, authorized, or ready for submission.</p>
        <p><strong>Current connection:</strong> approved versions are retained, but some export paths still render the current statement rather than consistently selecting the approved snapshot. Review generated documents before package preparation.</p>
      </details>
    </div>}
    {data && !error && <p className="cnw-separation-note"><Info size={14} />Statement approval and control implementation are tracked separately.</p>}

    {selectedControl && detailLoading && <div className="cnw-drawer-status" role="status">Loading control details…</div>}
    {selectedControl && detailError && <div className="cnw-drawer-status cnw-error" role="alert"><span>{detailError}</span>
      <button type="button" onClick={() => setRevision(value => value + 1)}>Retry</button>
      <button type="button" onClick={closeDrawer}>Close</button></div>}
    {detail && selectedControl && !detailLoading && !detailError && <ControlNarrativeDrawer detail={detail}
      initialStatement={selectedStatement}
      onStatementChange={(kind: NarrativeStatementKind) => setParam('statement', kind, true)}
      onClose={closeDrawer} onEdit={onOpenEditor} onViewSource={onOpenLibrary} onReviewProposal={onReviewProposal} />}
  </section>;
}
