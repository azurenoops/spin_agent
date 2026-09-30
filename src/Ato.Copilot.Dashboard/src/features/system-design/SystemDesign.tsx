import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from '../workspaces/workspaceNavigation';
import { getSspPreview, type SspPreview } from '../../api/exports';
import {
  decideDesignProposal, getApprovedSystemDesign, getDesignLayout, getSystemDesign, getSystemDesignHistory,
  buildSystemDesign, reconcileSystemDesign, reviewSystemDesign, saveDesignLayout, saveSystemDesign,
  type ApprovedSystemDesign, type DesignEdge, type DesignHistoryEntry, type DesignLayout, type DesignNode,
  type DesignProposal, type DesignProposalDecisionRequest, type DesignReviewRequest, type SystemDesignGraph,
} from '../../api/systemDesign';
import SystemTaskNavigation from '../systems/SystemTaskNavigation';
import SetupDialog from '../workspace-operations/SetupDialog';
import UnsavedDesignGuard from './UnsavedDesignGuard';
import DesignCanvas from './DesignCanvas';
import DesignRecordEditor from './DesignRecordEditor';
import { isArchitectureNode, isRecordedAssociation, pageRecords, relationshipLabel, relationshipOrigin, type DesignView } from './graphAdapter';
import './systemDesign.css';

const views: { value: DesignView; label: string }[] = [
  { value: 'Context', label: 'System context' }, { value: 'Boundary', label: 'Authorization boundary' },
  { value: 'Network', label: 'Network architecture' }, { value: 'DataFlows', label: 'Data flows' },
];
type Action = 'save' | 'build' | 'reconcile' | DesignReviewRequest['action'];
const actionLabels: Record<Action, string> = {
  save: 'save draft', build: 'build from recorded information', reconcile: 'reconcile changes', submit: 'submit for review', withdraw: 'withdraw',
  approve: 'approve', request_revision: 'request revision', derive_draft: 'start working revision',
};
function errorMessage(error: unknown): string {
  if (typeof error === 'object' && error !== null && 'error' in error && typeof error.error === 'string' && error.error.trim()) return error.error;
  if (typeof error === 'object' && error !== null && 'response' in error) {
    const response = error.response as { status?: number; data?: { error?: string; message?: string } };
    return response.data?.error || response.data?.message || (response.status === 403 ? 'Access denied. Your permissions may have changed.'
      : response.status === 409 ? 'This revision or its source records changed. Your local edits are retained. Reload and compare before retrying.' : 'The request failed. Your unsaved work is retained.');
  }
  return error instanceof Error ? error.message : 'The request failed. Your unsaved work is retained.';
}
function statusLabel(value: string) { return value.replace(/([a-z])([A-Z])/g, '$1 $2'); }
function detailLabel(value: string) {
  return statusLabel(value).replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2').replace(/^./, letter => letter.toUpperCase());
}
const contributionLabels: Record<string, string> = {
  Mission: 'Mission', Users: 'Users', Environment: 'Environment & hosting', Data: 'Data',
  InventoryBoundary: 'Inventory & boundary', PPSinterconnections: 'Ports & interconnections',
};
const contributionDescriptions: Record<string, string> = {
  Mission: 'Identity and purpose', Users: 'Actors and access', Environment: 'Hosting, zones and tiers',
  Data: 'Information types and sensitivity', InventoryBoundary: 'Recorded components and scope', PPSinterconnections: 'Connections and protection',
};
function isNode(record: DesignNode | DesignEdge): record is DesignNode { return 'label' in record; }
function SourceLink({ url, children }: { url?: string | null; children: React.ReactNode }) {
  if (url?.startsWith('https://')) return <a href={url} target="_blank" rel="noopener noreferrer">{children}</a>;
  if (!url || !url.startsWith('/') || url.startsWith('//')) return <span>{children} · Source link unavailable</span>;
  return <Link to={url}>{children}</Link>;
}
function RecordPager({ page, total, onPage }: { page: number; total: number; onPage: (page: number) => void }) {
  if (total <= 50) return null;
  return <div className="sd-pagination"><span>{page * 50 + 1}–{Math.min((page + 1) * 50, total)} of {total} records</span>
    <button className="sd-button" disabled={page === 0} onClick={() => onPage(page - 1)}>Previous page</button>
    <button className="sd-button" disabled={(page + 1) * 50 >= total} onClick={() => onPage(page + 1)}>Next page</button></div>;
}
export default function SystemDesign({ systemId }: { systemId: string }) {
  const [searchParams, setSearchParams] = useSearchParams();
  const view = views.find(item => item.value === searchParams.get('designView'))?.value ?? 'Context';
  const setView = (next: DesignView) => setSearchParams(current => {
    const updated = new URLSearchParams(current); updated.set('designView', next); return updated;
  }, { replace: true });
  const [graph, setGraph] = useState<SystemDesignGraph | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [layoutDirty, setLayoutDirty] = useState(false);
  const [layout, setLayout] = useState<DesignLayout | null>(null);
  const [layoutError, setLayoutError] = useState('');
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  const [editing, setEditing] = useState<DesignNode | DesignEdge | null>(null);
  const [proposalEditing, setProposalEditing] = useState<DesignProposal | null>(null);
  const [action, setAction] = useState<Action | null>(null);
  const [decision, setDecision] = useState<{ proposal: DesignProposal; action: DesignProposalDecisionRequest['action']; record?: DesignNode | DesignEdge } | null>(null);
  const [reason, setReason] = useState('');
  const [panel, setPanel] = useState<'compare' | 'package' | 'proposals' | 'history' | null>(null);
  const [baseline, setBaseline] = useState<ApprovedSystemDesign | null>(null);
  const [history, setHistory] = useState<DesignHistoryEntry[]>([]);
  const [sspPreview, setSspPreview] = useState<SspPreview | null>(null);
  const [panelLoading, setPanelLoading] = useState(false);
  const [panelError, setPanelError] = useState('');
  const [recordType, setRecordType] = useState<'nodes' | 'edges' | 'sources'>('nodes');
  const [page, setPage] = useState(0);
  const [gapPage, setGapPage] = useState(0);
  const [panelPage, setPanelPage] = useState(0);
  const [leaveView, setLeaveView] = useState<DesignView | null>(null);
  const [latest, setLatest] = useState<SystemDesignGraph | null>(null);
  const [adding, setAdding] = useState<'element' | 'external' | null>(null);
  const [sourceSearch, setSourceSearch] = useState('');
  const [sourcePage, setSourcePage] = useState(0);
  const [removing, setRemoving] = useState<string | null>(null);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [inspectorExpanded, setInspectorExpanded] = useState(false);
  const writing = useRef(false);
  const request = useRef(0);
  const layoutRequest = useRef(0);
  const load = useCallback(async () => {
    const version = ++request.current;
    setLoading(true);
    setError('');
    try {
      const data = await getSystemDesign(systemId);
      if (version !== request.current) return;
      setGraph(data); setDirty(false);
    } catch (failure) { if (version === request.current) setError(errorMessage(failure)); }
    finally { if (version === request.current) setLoading(false); }
  }, [systemId]);
  useEffect(() => { void load(); return () => { request.current++; }; }, [load]);
  const loadLayout = useCallback(async () => {
    const version = ++layoutRequest.current;
    setLayout(null); setLayoutError('');
    try {
      const data = await getDesignLayout(systemId, view);
      if (version !== layoutRequest.current) return;
      setLayout(data); setLayoutDirty(false);
    } catch (failure) { if (version === layoutRequest.current) setLayoutError(errorMessage(failure)); }
  }, [systemId, view]);
  useEffect(() => { void loadLayout(); return () => { layoutRequest.current++; }; }, [loadLayout]);
  const runMutation = async (work: () => Promise<SystemDesignGraph>, success: string) => {
    if (writing.current) return;
    writing.current = true; setBusy(true); setError(''); setMessage('');
    try {
      const result = await work();
      setGraph(result); setDirty(false); setAction(null); setDecision(null); setMessage(success);
    } catch (failure) { setError(errorMessage(failure)); }
    finally { writing.current = false; setBusy(false); }
  };
  const performAction = () => {
    if (!graph || !action || !reason.trim()) return;
    const command = action;
    void runMutation(() => command === 'save'
      ? saveSystemDesign(systemId, { expectedRevision: graph.revision, nodes: graph.nodes, edges: graph.edges, groups: graph.groups, reason: reason.trim() })
      : command === 'build' ? buildSystemDesign(systemId, { expectedRevision: graph.revision, reason: reason.trim() })
      : command === 'reconcile' ? reconcileSystemDesign(systemId, { expectedRevision: graph.revision, reason: reason.trim() })
        : reviewSystemDesign(systemId, { expectedRevision: graph.revision, action: command, reason: reason.trim() }),
    command === 'save' ? 'Draft saved. Approved baseline unchanged.'
      : command === 'build' ? 'Recorded relationships assembled in the working design. Review source changes and remaining gaps; no approval or authorization was granted.'
        : 'Server confirmed the design action. Review the updated governance state.');
  };
  const openAction = (next: Action) => {
    setReason(next === 'build' ? 'Assemble architecture from current source-recorded relationships; preserve reviewed and manual decisions.' : '');
    setAction(next);
  };
  const openPanel = async (next: typeof panel) => {
    setPanel(next); setPanelError(''); setPanelPage(0);
    if (next !== 'compare' && next !== 'history' && next !== 'package') return;
    setPanelLoading(true);
    try {
      const [approved, entries] = await Promise.all([getApprovedSystemDesign(systemId), getSystemDesignHistory(systemId)]);
      setBaseline(approved); setHistory(entries);
      if (next === 'package') setSspPreview(await getSspPreview(systemId, undefined, 'working'));
    } catch (failure) { setPanelError(errorMessage(failure)); }
    finally { setPanelLoading(false); }
  };
  const applyRecord = (record: DesignNode | DesignEdge) => {
    if (proposalEditing) {
      setDecision({ proposal: proposalEditing, action: 'edit_accept', record });
      setReason(''); setProposalEditing(null); setEditing(null); return;
    }
    record = { ...record, reviewState: 'Unapproved', projectionStatus: 'Draft' };
    setGraph(current => current && (isNode(record) ? { ...current, nodes: current.nodes.some(item => item.id === record.id)
      ? current.nodes.map(item => item.id === record.id ? record : item) : [...current.nodes, record] }
      : { ...current, edges: current.edges.some(item => item.id === record.id)
        ? current.edges.map(item => item.id === record.id ? record : item) : [...current.edges, record] }));
    setDirty(true); setSelected(record.id); setEditing(null);
  };
  const savePresentation = async () => {
    if (!layout || writing.current) return;
    writing.current = true; setBusy(true); setLayoutError('');
    try {
      setLayout(await saveDesignLayout(systemId, layout.version, layout)); setLayoutDirty(false);
      setMessage('Presentation saved separately. Compliance records and approved baseline unchanged.');
    } catch (failure) { setLayoutError(errorMessage(failure)); }
    finally { writing.current = false; setBusy(false); }
  };
  const startConnection = (sourceId = '', targetId = '') => {
    if (!graph?.actions.canEdit || busy) return;
    setEditing({ id: `flow:${crypto.randomUUID()}`, sourceNodeId: sourceId, targetNodeId: targetId,
      relationshipType: 'DataFlow', origin: 'UserAuthored', direction: 'Outbound',
      boundaryCrossing: 'Unknown', reviewState: 'Draft', projectionStatus: 'Working' });
  };
  const proposeElement = (category: string) => {
    if (!graph?.actions.canEdit || busy) return;
    setPaletteOpen(false);
    setEditing({ id: `design:${crypto.randomUUID()}`, label: '', kind: category === 'External system' ? 'ExternalSystem' : 'DesignComponent',
      boundaryDisposition: 'Undetermined', reviewState: 'Draft', projectionStatus: 'Working',
      sspImpact: 'System description and architecture', properties: { componentType: category } });
  };
  const requestRemoval = (recordId: string) => {
    if (!graph?.actions.canEdit || busy) return;
    if (graph.nodes.some(node => node.id === recordId && (node.kind === 'System' || node.source?.type === 'RegisteredSystem'))) {
      setError('The canonical system identity cannot be removed. Use Hide from diagram to change its presentation.');
      return;
    }
    setRemoving(recordId);
  };
  const removeFromDraft = () => {
    if (!graph?.actions.canEdit || busy || !removing) return;
    setGraph(current => {
      if (!current) return current;
      const node = current.nodes.some(item => item.id === removing);
      return { ...current, nodes: current.nodes.filter(item => item.id !== removing),
        edges: current.edges.filter(item => item.id !== removing && (!node || item.sourceNodeId !== removing && item.targetNodeId !== removing)),
        groups: current.groups.map(group => ({ ...group, nodeIds: group.nodeIds.filter(id => id !== removing) })) };
    });
    setDirty(true); setSelected(null); setRemoving(null);
    setMessage('Removed from the working draft only. Save draft to retain the change; canonical records and approved baselines remain unchanged.');
  };
  if (loading && !graph) return <div className="sd-workspace"><p role="status">Loading System design and governed sources…</p></div>;
  if (!graph) return <div className="sd-workspace"><h1>System design</h1><SystemTaskNavigation definitionOnly />
    <div role="alert" className="sd-notice">{error || 'System design unavailable.'}</div><button className="sd-button" onClick={() => void load()}>Retry</button></div>;
  const active = graph.nodes.find(node => node.id === selected) ?? graph.edges.find(edge => edge.id === selected);
  const architectureNodes = graph.nodes.filter(isArchitectureNode);
  const sourceRecords = graph.nodes.filter(node => !isArchitectureNode(node));
  const matching = (recordType === 'nodes' ? architectureNodes : recordType === 'sources' ? sourceRecords : graph.edges).filter(record => {
    const text = isNode(record) ? `${record.label} ${record.kind} ${record.source?.id ?? ''}` : `${record.purpose ?? ''} ${record.relationshipType} ${record.sourceNodeId} ${record.targetNodeId}`;
    return (!search || text.toLowerCase().includes(search.toLowerCase())) && (!filter || !isNode(record) || record.boundaryDisposition === filter);
  });
  const totalPages = Math.max(1, Math.ceil(matching.length / 50));
  const effectivePage = Math.min(page, totalPages - 1);
  const records = pageRecords(matching, effectivePage);
  const visibleGapPage = Math.min(gapPage, Math.max(0, Math.ceil(graph.gaps.length / 50) - 1));
  const visibleGaps = pageRecords(graph.gaps, visibleGapPage);
  const pending = graph.proposals.filter(proposal => !['Accepted', 'Rejected', 'Deferred'].includes(proposal.state)).length;
  const nodeLabel = (id: string) => graph.nodes.find(node => node.id === id)?.label ?? `Unresolved endpoint: ${id}`;
  const writeDisabled = busy || loading;
  const savedOnly = writeDisabled || dirty || layoutDirty;
  const candidates = graph.proposals.filter(proposal => proposal.originalNode?.source
    && proposal.state === 'Pending' && proposal.kind === 'Added'
    && !graph.nodes.some(node => node.id === proposal.originalNode?.id)
    && (adding !== 'external' || proposal.originalNode.kind.toLowerCase().includes('external'))
    && `${proposal.originalNode.label} ${proposal.originalNode.source.type}`.toLowerCase().includes(sourceSearch.toLowerCase()));
  return <div className="sd-workspace">
    <UnsavedDesignGuard dirty={dirty || layoutDirty || !!editing} />
    <header className="sd-page-heading"><div><p className="sd-eyebrow">System workspace / System definition</p>
      <h1>System definition</h1><p className="sd-muted">Define the system once and reuse reviewed information throughout the authorization package.</p></div>
      <button className="sd-button" onClick={() => void openPanel('history')}>View history</button></header>
    <SystemTaskNavigation definitionOnly />
    <header className="sd-header"><div>
      <h2>System design</h2><p className="sd-muted">Architecture assembled from recorded system relationships. Review what is known and resolve what is missing.</p></div>
      <div className="sd-actions"><button className="sd-button" onClick={() => void openPanel('compare')}>Compare to baseline</button>
        <button className="sd-button" onClick={() => void openPanel('package')}>Preview review package</button>
        {graph.actions.canEdit && graph.governanceStatus !== 'Approved' && <button className="sd-button sd-primary" disabled={writeDisabled} onClick={() => openAction('save')}>Save draft</button>}
        {graph.actions.canDeriveDraft && <button className="sd-button sd-primary" disabled={savedOnly} onClick={() => openAction('derive_draft')}>Start working revision</button>}
        {graph.actions.canWithdraw && <button className="sd-button" disabled={savedOnly} onClick={() => openAction('withdraw')}>Withdraw</button>}
        {graph.actions.canReview && <><button className="sd-button" disabled={savedOnly} onClick={() => openAction('request_revision')}>Request revision</button>
          <button className="sd-button sd-primary" disabled={savedOnly} onClick={() => openAction('approve')}>Approve</button></>}
      </div>
    </header>
    {error && !action && !decision && <div role="alert" className="sd-notice">{error}
      {!dirty && <button className="sd-button" disabled={busy} onClick={() => void load()}>Retry</button>}
      {dirty && <><p>Local draft retained. Review the latest saved revision before explicitly discarding or reapplying corrections.</p>
        <button className="sd-button" disabled={busy} onClick={async () => {
          setBusy(true);
          try { setLatest(await getSystemDesign(systemId)); } catch (failure) { setError(errorMessage(failure)); }
          finally { setBusy(false); }
        }}>Review latest server revision</button></>}
    </div>}
    {message && <p role="status" className="sd-success">{message}</p>}
    {(dirty || layoutDirty) && <p role="status" className="sd-notice">Unsaved {dirty ? 'design changes' : ''}{dirty && layoutDirty ? ' and ' : ''}{layoutDirty ? 'presentation changes' : ''}. Save before review or navigating away.
      {dirty && ` Completeness, gaps and readiness describe saved revision v${graph.revision}; local corrections have not yet been validated.`}</p>}
    {graph.sourcesStale && <div className="sd-notice" role="status">Canonical sources changed since this revision. Reconcile and review conflicts before approval.</div>}
    {!graph.actions.canEdit && <p className="sd-notice">Read-only access. Available review actions, if any, are authorized by the server.</p>}
    <div className="sd-governance-grid"><section className="sd-governance" aria-label="Design governance">
      <div><h2>Design status</h2><span className={`sd-badge sd-${graph.governanceStatus.toLowerCase()}`}>{statusLabel(graph.governanceStatus)}</span>
        <p>Approved baseline <strong>{graph.approvedRevision ? `v${graph.approvedRevision}` : 'Not approved'}</strong> · Working revision <strong>v{graph.revision}</strong></p>
        <p className="sd-muted">Last editor: {graph.lastEditor || 'Not recorded'} · Reviewer: {graph.reviewer || 'Not recorded'}</p>
        <p className="sd-muted">Source synchronization: {graph.synchronizedAt || 'Not recorded'}</p>
        {graph.reviewerComments && <p>Reviewer comments: {graph.reviewerComments}</p>}
        <button className="sd-text-button" onClick={() => void openPanel('history')}>Version history</button>
      </div>
      <div className="sd-metrics">
        {[['Completeness', `${graph.completenessPercentage}%`], ['Design elements', architectureNodes.length], ['Open gaps', graph.gaps.length], ['Pending changes', pending]].map(([label, value]) =>
          <div key={label}><strong>{value}</strong><span>{label}</span></div>)}
      </div>
    </section><section className="sd-review-gate" aria-label="Next review gate">
      <h2>Next review gate</h2><p>{graph.gaps.length
        ? `Review ${graph.gaps.length} open ${graph.gaps.length === 1 ? 'gap' : 'gaps'}, then preview the saved SSP content.`
        : 'Preview the saved SSP content before requesting review.'}</p>
      {graph.governanceStatus === 'Approved' && <p>The approved design baseline is read-only. Use an authorized working revision for changes.</p>}
      <div className="sd-actions">
        {graph.actions.canSubmit && <button className="sd-button sd-primary" disabled={savedOnly} onClick={() => openAction('submit')}>Submit for review</button>}
        <a className="sd-button" href="#design-gaps">Review gaps</a><button className="sd-button" onClick={() => void openPanel('package')}>Review SSP content</button>
      </div>
      {!graph.actions.canSubmit && graph.governanceStatus !== 'Approved' && <p className="sd-muted">{graph.governanceStatus === 'NotStarted' && graph.actions.canEdit
        ? 'Save the first draft to start the review workflow.'
        : 'Submission is unavailable for your current permissions or this revision’s status.'}</p>}
    </section></div>
    {graph.approvedRevision && graph.baselineChanges.length > 0 && <details open className="sd-notice sd-baseline-notice">
      <summary>Changes from approved baseline v{graph.approvedRevision}</summary>
      <p>{graph.baselineChanges.length} retained changes. The approved baseline remains unchanged while working changes are reviewed.</p>
      <ul>{graph.baselineChanges.slice(0, 3).map((change, index) => <li key={`${change.recordId}-${index}`}>{change.kind}: {
        graph.nodes.find(node => node.id === change.recordId)?.label ?? graph.edges.find(edge => edge.id === change.recordId)?.purpose ?? change.recordId
      }</li>)}</ul>
      <button className="sd-text-button" onClick={() => void openPanel('compare')}>Review all baseline changes</button>
    </details>}
    <section className="sd-panel" aria-label="System definition contributions">
      <div className="sd-section-heading"><div><h2>System definition contributions</h2><p className="sd-muted">Source records supply descriptions and source-backed relationships without becoming architecture boxes.</p></div>
        {graph.actions.canReconcile && <button className="sd-button sd-primary" disabled={savedOnly} onClick={() => openAction('build')}>Build from recorded information</button>}
        {graph.actions.canReconcile && <button className="sd-button" disabled={savedOnly} onClick={() => openAction('reconcile')}>Reconcile changes</button>}
        <button className="sd-button" onClick={() => void openPanel('proposals')}>Review proposals ({graph.proposals.length})</button></div>
      <div className="sd-contributions">{graph.contributions.map(contribution => <SourceLink key={contribution.section} url={contribution.resolutionUrl}>
        <strong>{contributionLabels[contribution.section] ?? contribution.section}</strong><span>{contribution.recordCount} records · {statusLabel(contribution.state)}</span>
        <small title={contribution.explanation}>{contributionDescriptions[contribution.section] ?? contribution.explanation}</small>
      </SourceLink>)}
        {!graph.contributions.some(item => item.section === 'Azure') && <SourceLink url={`/systems/${encodeURIComponent(systemId)}/profile/EnvironmentAndDeployment`}>
          <strong>Azure observations</strong><span>{graph.nodes.filter(node => node.kind === 'AzureResource').length} represented · {graph.proposals.filter(proposal =>
            proposal.originalNode?.kind === 'AzureResource' && !['Accepted', 'Rejected', 'Deferred'].includes(proposal.state)).length} proposed</span>
          <small>{statusLabel(graph.discoveryState)} · Observation is not approval.</small>
        </SourceLink>}
      </div>
      <p className="sd-muted">
        Recorded membership, user access, hosting and containment are different from data flows.
        Uncertain endpoints and missing protections remain gaps; no AI or name-based guessing is used.
      </p>
      <p className="sd-muted">Azure discovery: {statusLabel(graph.discoveryState)} · Monitoring: {statusLabel(graph.monitoringState)}.
        Attachment does not establish boundary inclusion, accepted inheritance, monitoring health, cATO readiness or authorization.</p>
    </section>
    <div className="sd-viewbar"><div className="sd-view-selector" role="group" aria-label="Diagram view">
      {views.map(item => <button key={item.value} className="sd-button" aria-pressed={view === item.value} disabled={busy}
        onClick={() => { if (view === item.value) return; if (layoutDirty) setLeaveView(item.value); else setView(item.value); }}>{item.label}</button>)}
    </div><div className="sd-actions">
      <label className="sd-search">Search design<input type="search" value={search} onChange={event => { setSearch(event.target.value); setPage(0); }} placeholder="Name, type or source" /></label>
      <label className="sd-search">Boundary filter<select value={filter} onChange={event => { setFilter(event.target.value); setPage(0); }}>
        <option value="">All dispositions</option><option value="InBoundary">In boundary</option><option value="OutOfBoundary">Out of boundary</option><option value="Undetermined">Undetermined</option>
      </select></label>
    </div></div>
    <div className="sd-main-grid"><section className="sd-panel sd-diagram"><div className="sd-section-heading"><h2>{views.find(item => item.value === view)?.label}</h2>
      {graph.actions.canEdit && <div className="sd-actions" aria-label="Design canvas actions">
        <button type="button" className="sd-button" disabled={busy} onClick={() => setPaletteOpen(true)}>Add element</button>
        <button type="button" className="sd-button" disabled={busy || architectureNodes.length < 1}
          onClick={() => startConnection(active && isNode(active) && isArchitectureNode(active) ? active.id : '')}>{active && isNode(active) && isArchitectureNode(active) ? 'Connect selected element' : 'Connect elements'}</button>
        <button className="sd-button" disabled={!layoutDirty || busy} onClick={() => void savePresentation()}>Save presentation</button>
      </div>}</div>
      {layoutError && <div role="alert" className="sd-notice">{layoutError}<button className="sd-button"
        onClick={() => { if (layoutDirty) void savePresentation(); else void loadLayout(); }}>{layoutDirty ? 'Retry save presentation' : 'Retry presentation'}</button></div>}
      {!layout && !layoutError && <p role="status">Loading saved presentation…</p>}
      {layout && <DesignCanvas graph={graph} layout={layout} view={view} search={search} filter={filter} selected={selected}
        editable={graph.actions.canEdit && !busy} pristine={!layoutDirty} onSelect={setSelected}
        onConnect={startConnection} onRemove={requestRemoval}
        onInitialPositions={positions => setLayout(current => !current || Object.keys(positions).length === Object.keys(current.positions).length
          && Object.entries(positions).every(([id, point]) => current.positions[id]?.x === point.x && current.positions[id]?.y === point.y)
          ? current : { ...current, positions })}
        onLayout={value => { setLayout(value); if (graph.actions.canEdit) setLayoutDirty(true); }} />}
    </section>
    <aside className="sd-panel sd-inspector" aria-label="Selected element inspector"><h2>Selected element</h2>
      {!active ? <p className="sd-muted">Select a graph element, relationship or structured record to inspect its source and SSP impact.</p>
        : <div className="sd-inspector-content" key={active.id}>
          <div className="sd-inspector-heading">
            <h3>{isNode(active) ? active.label : active.purpose || `${nodeLabel(active.sourceNodeId)} → ${nodeLabel(active.targetNodeId)}`}</h3>
            <p className="sd-muted">{isNode(active) ? detailLabel(active.kind) : relationshipLabel(active)}</p>
            <div className="sd-actions">
              <span className="sd-badge">{statusLabel(active.reviewState)}</span>
              {isNode(active) && <span className="sd-badge">{statusLabel(active.boundaryDisposition)}</span>}
            </div>
            {isNode(active) && !isArchitectureNode(active) && <p className="sd-muted">This source record contributes descriptions or validation. It is not a component to connect on the architecture canvas.</p>}
          </div>
          {graph.actions.canEdit && <div className="sd-inspector-actions" aria-label="Selected record actions">
            {(isNode(active) || !isRecordedAssociation(active)) && <button type="button" className="sd-button sd-primary" aria-label="Edit selected record" disabled={busy} onClick={() => setEditing(active)}>Edit</button>}
            {isNode(active) && <button type="button" className="sd-button" aria-label="Rename selected element" disabled={busy} onClick={() => setEditing(active)}>Rename</button>}
            <button type="button" className="sd-button sd-remove-action" aria-label={isNode(active) ? 'Remove selected element' : 'Remove selected connection'}
              disabled={busy || isNode(active) && (active.kind === 'System' || active.source?.type === 'RegisteredSystem')}
              onClick={() => requestRemoval(active.id)}>Remove</button>
          </div>}
          {!isNode(active) && isRecordedAssociation(active) && <p className="sd-muted">
            Built from an explicit source record, not an inferred network flow. Correct its facts in the source workflow; design review does not grant access or coverage.
          </p>}
          <dl className="sd-inspector-facts">{(isNode(active)
            ? [['Environment', active.environment], ['Network zone', active.networkZone], ['Provider', active.provider], ['SSP contribution', active.sspImpact]]
            : [['From', nodeLabel(active.sourceNodeId)], ['To', nodeLabel(active.targetNodeId)], ['Purpose', active.purpose],
              ['Protection', active.protection], ['PPS', [active.port, active.protocol, active.service].filter(Boolean).join(' / ')]])
            .filter(([, value]) => typeof value === 'string' && value.trim())
            .map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>
          <div className="sd-inspector-source">
            {active.source ? <><p className="sd-muted">Source: {detailLabel(active.source.type)}</p>
              <SourceLink url={active.source.resolutionUrl}>Open source record</SourceLink></>
              : <p className="sd-muted">No canonical source attached. Proposed design record; review required.</p>}
          </div>
          <button type="button" className="sd-button sd-details-action" onClick={() => setInspectorExpanded(true)}>View full details</button>
          {layout && <details className="sd-inspector-options"><summary>Diagram display options</summary>
            <div className="sd-inspector-options-content">
            {isNode(active) ? <button className="sd-button" disabled={busy} onClick={() => {
              setLayout({ ...layout, visibility: { ...layout.visibility, [active.id]: layout.visibility[active.id] === false } });
              if (graph.actions.canEdit) setLayoutDirty(true);
            }}>{layout.visibility[active.id] === false ? 'Show in diagram' : 'Hide from diagram'}</button>
              : <label className="sd-field">Edge routing<select disabled={busy} value={layout.edgeRouting[active.id] ?? 'default'} onChange={event => {
                setLayout({ ...layout, edgeRouting: { ...layout.edgeRouting, [active.id]: event.target.value } });
                if (graph.actions.canEdit) setLayoutDirty(true);
              }}>{['default', 'straight', 'step', 'smoothstep'].map(route => <option key={route}>{route}</option>)}</select></label>}
            <p className="sd-muted">Display only; recorded meaning is unchanged.{!graph.actions.canEdit && ' View-only preferences are not saved.'}</p>
            </div>
          </details>}
        </div>}
    </aside></div>
    {inspectorExpanded && active && <SetupDialog title="Element details" placement="right" expanded busy={false} onClose={() => setInspectorExpanded(false)}
      description={isNode(active) ? active.label : `${nodeLabel(active.sourceNodeId)} → ${nodeLabel(active.targetNodeId)}`}>
      <div className="sd-record-details">
        <h3 className="font-semibold">Recorded design information</h3>
        <dl>{Object.entries(active).filter(([key]) => !['source', 'properties'].includes(key)).map(([key, value]) =>
          <div key={key}><dt>{detailLabel(key)}</dt><dd>{typeof value === 'string' && value.trim() ? value : 'Not recorded'}</dd></div>)}</dl>
        {!isNode(active) && <p>Relationship origin: {relationshipOrigin(active).label}</p>}
        {active.source && <section className="space-y-3">
          <h3 className="font-semibold">Source provenance</h3>
          <dl>{Object.entries(active.source).map(([key, value]) => <div key={key}><dt>{detailLabel(key)}</dt><dd>{String(value ?? 'Not recorded')}</dd></div>)}</dl>
          <SourceLink url={active.source.resolutionUrl}>Open source record</SourceLink>
        </section>}
        {isNode(active) && Object.keys(active.properties).length > 0 && <details>
          <summary>Source properties ({Object.keys(active.properties).length})</summary>
          <dl>{Object.entries(active.properties).map(([key, value]) => <div key={key}><dt>{detailLabel(key)}</dt><dd>{value || 'Not recorded'}</dd></div>)}</dl>
        </details>}
      </div>
    </SetupDialog>}
    <div className="sd-bottom-grid"><section id="design-gaps" className="sd-panel" aria-label="Design gaps"><h2>Design gaps ({graph.gaps.length})</h2>
      {!graph.gaps.length ? <p>No gaps reported by the server. This alone does not constitute approval.</p>
        : <div className="sd-table-scroll"><table aria-label="Design gaps"><thead><tr><th>Priority</th><th>Gap</th><th>Affected output</th><th>Owner</th><th>Action</th></tr></thead>
          <tbody>{visibleGaps.map(gap => <tr key={gap.id}><td><span className="sd-badge">{gap.severity}</span></td>
            <td>{gap.explanation}<p><button className="sd-text-button" onClick={() => setSelected(gap.recordId)}>Inspect affected record</button></p></td>
            <td>{gap.sspImpact} · {gap.view}</td><td>{gap.owner || 'Unassigned'}</td><td><SourceLink url={gap.resolutionUrl}>Resolve at source</SourceLink></td>
          </tr>)}</tbody></table></div>}
      <RecordPager page={visibleGapPage} total={graph.gaps.length} onPage={setGapPage} />
    </section>
    <section className="sd-panel" aria-label="SSP output readiness"><h2>SSP output readiness</h2><p className="sd-badge">{statusLabel(graph.sspReadiness)}</p>
      <p>Approved baseline {graph.approvedRevision ? `v${graph.approvedRevision}` : 'not available'}. Draft corrections do not silently replace approved SSP / OSCAL output.</p>
      {graph.sspReadiness !== 'Ready' && visibleGaps.map(gap => <p key={gap.id}><SourceLink url={gap.resolutionUrl}>{gap.sspImpact}: {gap.explanation}</SourceLink></p>)}
      {graph.gaps.length > 50 && <p>Showing the current gap page. All {graph.gaps.length} source-linked gaps are accessible in Design gaps.</p>}
      <SourceLink url={`/systems/${encodeURIComponent(systemId)}/documents/preview?contribution=SystemDesign`}>Open SSP contribution preview</SourceLink>
      <p className="sd-muted">Package preparation and export are separate from eMASS submission and the Authorizing Official's decision.</p>
    </section></div>
    <section className="sd-panel" aria-label="Structured design records"><div className="sd-section-heading"><div><h2>Structured design records</h2>
      <p className="sd-muted">The same governed elements and relationships, fully accessible without dragging.</p></div>
      <div className="sd-actions"><button className="sd-button" aria-pressed={recordType === 'nodes'} onClick={() => { setRecordType('nodes'); setPage(0); }}>Elements ({architectureNodes.length})</button>
        <button className="sd-button" aria-pressed={recordType === 'edges'} onClick={() => { setRecordType('edges'); setPage(0); }}>Relationships ({graph.edges.length})</button>
        <button className="sd-button" aria-pressed={recordType === 'sources'} onClick={() => { setRecordType('sources'); setPage(0); }}>Source records ({sourceRecords.length})</button>
        {graph.actions.canEdit && graph.governanceStatus !== 'Approved' && <>
          <button className="sd-button" disabled={savedOnly} onClick={() => { setAdding('element'); setSourceSearch(''); setSourcePage(0); }}>Add existing element</button>
          <button className="sd-button" disabled={savedOnly} onClick={() => { setAdding('external'); setSourceSearch(''); setSourcePage(0); }}>Add external system</button>
        </>}
        {graph.actions.canEdit && graph.governanceStatus !== 'Approved' && <button className="sd-button" disabled={busy || architectureNodes.length < 2}
          onClick={() => setEditing({ id: `flow-${crypto.randomUUID()}`, sourceNodeId: '', targetNodeId: '', relationshipType: 'DataFlow', direction: 'Outbound',
            purpose: '', boundaryCrossing: 'Unknown', reviewState: 'Unapproved', projectionStatus: 'Draft' })}>Add data flow</button>}
      </div></div>
      <div className="sd-table-scroll"><table aria-label="Design records"><thead><tr><th>{recordType === 'edges' ? 'Relationship' : recordType === 'sources' ? 'Source record' : 'Element'}</th><th>Type / boundary</th><th>Provenance / review</th><th>Action</th></tr></thead>
        <tbody>{records.map(record => <tr key={record.id}><td>{isNode(record) ? record.label : `${nodeLabel(record.sourceNodeId)} → ${nodeLabel(record.targetNodeId)}`}</td>
          <td>{isNode(record) ? `${record.kind} · ${record.boundaryDisposition}` : `${record.relationshipType} · ${record.purpose || 'Purpose not recorded'}`}</td>
          <td>{isNode(record) ? record.source?.provenance || 'Design draft' : relationshipOrigin(record).label} · {record.reviewState}</td><td><button className="sd-text-button"
            aria-label={`Open ${isNode(record) ? record.label : `relationship ${record.id}`}`} onClick={() => setSelected(record.id)}>Inspect</button></td></tr>)}</tbody></table></div>
      {!matching.length && <p>No records match this search and filter. {graph.nodes.length + graph.edges.length} total records remain in the design.</p>}
      <div className="sd-pagination"><span>{matching.length ? effectivePage * 50 + 1 : 0}–{Math.min((effectivePage + 1) * 50, matching.length)} of {matching.length} {recordType === 'edges' ? 'relationships' : recordType === 'sources' ? 'source records' : 'elements'}</span>
        <button className="sd-button" disabled={effectivePage === 0} onClick={() => setPage(effectivePage - 1)}>Previous records</button>
        <button className="sd-button" disabled={effectivePage + 1 === totalPages} onClick={() => setPage(effectivePage + 1)}>Next records</button></div>
    </section>
    {(action || decision) && <SetupDialog title={action ? `Confirm ${actionLabels[action]}` : `Proposal: ${decision!.action.replace('_', ' and ')}`}
      description="The server validates permissions, source freshness and revision. Your reason is retained with the decision." busy={busy}
      onClose={() => { if (!busy) { setAction(null); setDecision(null); } }}>
      <form onSubmit={event => { event.preventDefault(); if (decision) void runMutation(() => decideDesignProposal(systemId, decision.proposal.id, {
        expectedRevision: graph.revision, action: decision.action, reason: reason.trim(),
        ...(decision.record ? isNode(decision.record) ? { node: decision.record } : { edge: decision.record } : {}),
      }), 'Proposal decision retained. Approved baseline unchanged.'); else performAction(); }}>
        {error && <p role="alert" className="sd-notice">{error}</p>}
        {action === 'build' && <p className="sd-notice">
          Assemble explicit recorded relationships into the saved working revision. Existing design decisions and approved baselines stay unchanged;
          conflicting source updates are staged for review. This does not discover Azure resources, infer data flows or approve controls.
        </p>}
        <label className="sd-field">Reason<textarea required value={reason} onChange={event => setReason(event.target.value)} /></label>
        <div className="sd-actions"><button type="button" className="sd-button" disabled={busy} onClick={() => { setAction(null); setDecision(null); }}>Cancel</button>
          <button className="sd-button sd-primary" disabled={busy || !reason.trim()} type="submit">{action ? `Confirm ${actionLabels[action]}` : 'Confirm proposal decision'}</button></div>
      </form>
    </SetupDialog>}
    {paletteOpen && <SetupDialog title="Add design element" busy={busy} onClose={() => setPaletteOpen(false)}
      description="Choose a proposed architectural element or an existing canonical record. New design elements are unreviewed and do not create Azure resources or establish boundary inclusion.">
      <div className="sd-palette">{['Application', 'API', 'Service', 'Database', 'Storage', 'Network device', 'Identity provider', 'Actor group', 'External system'].map(category =>
        <button type="button" className="sd-button" key={category} onClick={() => proposeElement(category)}>{category}</button>)}</div>
      <button type="button" className="sd-button" onClick={() => { setPaletteOpen(false); setAdding('element'); setSourceSearch(''); setSourcePage(0); }}>Choose canonical record</button>
    </SetupDialog>}
    {removing && <SetupDialog title="Remove from working design" busy={busy} onClose={() => setRemoving(null)}
      description="Canonical records and approved baselines are not deleted. Required source contributions removed from the graph remain validation gaps until reconciled.">
      <p>{nodeLabel(removing)}</p>
      <p>{graph.edges.filter(edge => edge.sourceNodeId === removing || edge.targetNodeId === removing).length} connected relationship(s) will also be removed from this draft.</p>
      <div className="sd-actions"><button type="button" className="sd-button" onClick={() => setRemoving(null)}>Cancel</button>
        <button type="button" className="sd-button sd-primary" onClick={removeFromDraft}>Remove from draft</button></div>
    </SetupDialog>}
    {editing && <DesignRecordEditor key={editing.id} node={isNode(editing) ? editing : undefined} edge={!isNode(editing) ? editing : undefined}
      nodes={graph.nodes} edges={graph.edges} onApply={applyRecord} onClose={() => { setEditing(null); setProposalEditing(null); }} />}
    {adding && <SetupDialog title={adding === 'external' ? 'Add external system' : 'Add existing design element'} busy={busy}
      description="Choose an authoritative source staged by server reconciliation. Existing projected records are not duplicated; arbitrary source IDs are not accepted here."
      onClose={() => setAdding(null)}>
      <label className="sd-field">Find canonical source<input type="search" value={sourceSearch} onChange={event => { setSourceSearch(event.target.value); setSourcePage(0); }} /></label>
      <p>{candidates.length} staged source additions. {graph.nodes.length} elements are already represented in the graph.</p>
      {(graph.availableNodes ?? []).filter(node => (adding !== 'external' || node.kind === 'ExternalSystem')
        && `${node.label} ${node.kind}`.toLowerCase().includes(sourceSearch.toLowerCase())).map(node =>
        <article key={node.id} className="sd-notice">
          <strong>{node.label}</strong><p>{node.kind} · {node.source?.reviewState ?? 'Unreviewed'}</p>
          <button type="button" className="sd-button" onClick={() => { setAdding(null); setEditing(node); }}>Add {node.label} to draft</button>
        </article>)}
      <ul className="sd-gap-list">{pageRecords(candidates, sourcePage).map(proposal => <li key={proposal.id}>
        <h3>{proposal.originalNode!.label}</h3><p>{proposal.originalNode!.kind} · {proposal.originalNode!.source!.type} · Version {proposal.originalNode!.source!.version}</p>
        <p>{proposal.originalNode!.source!.provenance} · {proposal.originalNode!.reviewState} · Boundary: {proposal.originalNode!.boundaryDisposition}</p>
        <SourceLink url={proposal.originalNode!.source!.resolutionUrl}>View authoritative source</SourceLink>
        {graph.actions.canReconcile && <button className="sd-button" aria-label={`Review addition: ${proposal.originalNode!.label}`} disabled={savedOnly}
          onClick={() => { setAdding(null); setReason(''); setDecision({ proposal, action: 'accept' }); }}>Review addition</button>}
      </li>)}</ul>
      <RecordPager page={sourcePage} total={candidates.length} onPage={setSourcePage} />
      {!candidates.length && <p className="sd-notice">No matching staged additions were returned. Record missing sources in their canonical workflow, then return and reconcile. Discovery or source availability is not inferred from an empty list.</p>}
      <p><SourceLink url={`/systems/${encodeURIComponent(systemId)}/${adding === 'external' ? 'profile/PortsProtocolsAndServices' : 'boundaries'}`}>
        {adding === 'external' ? 'Record an external system / interconnection at its source' : 'Manage canonical inventory and boundary records'}
      </SourceLink></p>
      {graph.actions.canReconcile && <button className="sd-button" disabled={savedOnly} onClick={() => { setAdding(null); openAction('reconcile'); }}>Reconcile canonical sources</button>}
      {adding === 'external' && <div className="sd-notice">
        <p>For an external system not yet in the canonical register, record a proposed design element. It remains undetermined until reviewed and still requires a canonical interconnection before approving a boundary-crossing flow.</p>
        <button type="button" className="sd-button" onClick={() => {
          setAdding(null); setEditing({ id: `external:${crypto.randomUUID()}`, label: '', kind: 'ExternalSystem',
            boundaryDisposition: 'Undetermined', reviewState: 'Draft', projectionStatus: 'Working', sspImpact: 'Data flows and interconnections', properties: {} });
        }}>Propose external system</button>
      </div>}
    </SetupDialog>}
    {leaveView && <SetupDialog title="Unsaved presentation changes" description="Save this view's presentation before switching, or explicitly discard it." busy={busy} onClose={() => setLeaveView(null)}>
      <div className="sd-actions"><button className="sd-button" onClick={() => setLeaveView(null)}>Keep this view</button>
        <button className="sd-button" onClick={() => { setView(leaveView); setLeaveView(null); setLayoutDirty(false); }}>Discard presentation and switch</button></div>
    </SetupDialog>}
    {latest && <SetupDialog title="Resolve revision conflict" description="The local draft has not been overwritten. Compare it with the server, then choose whether to keep editing or explicitly discard it."
      busy={false} onClose={() => setLatest(null)}>
      <div className="sd-comparison"><section><h3>Local revision v{graph.revision}</h3><pre>{JSON.stringify({ nodes: graph.nodes, edges: graph.edges }, null, 2)}</pre></section>
        <section><h3>Server revision v{latest.revision}</h3><pre>{JSON.stringify({ nodes: latest.nodes, edges: latest.edges }, null, 2)}</pre></section></div>
      <div className="sd-actions"><button className="sd-button" onClick={() => setLatest(null)}>Keep local draft</button>
        <button className="sd-button" onClick={() => { setGraph(latest); setLatest(null); setDirty(false); setError(''); setMessage('Latest saved revision loaded. Local design edits were explicitly discarded.'); }}>Discard local draft and load latest</button></div>
    </SetupDialog>}
    {panel && <SetupDialog title={panel === 'compare' ? 'Compare approved baseline and working revision' : panel === 'package' ? 'Review package preview' : panel === 'history' ? 'Design version history' : 'Reconcile source proposals'}
      description={`System: ${graph.systemName} · Working revision v${graph.revision}. Observations and draft changes are not authorization decisions.`}
      busy={panelLoading} onClose={() => setPanel(null)}>
      {panelLoading && <p role="status">Loading retained review records…</p>}
      {panelError && <div role="alert" className="sd-notice">{panelError}<button className="sd-button" onClick={() => void openPanel(panel)}>Retry review records</button></div>}
      {panel === 'compare' && !panelLoading && !panelError && <>
        {dirty && <p className="sd-notice">The server baseline comparison describes the saved revision. Unsaved local edits are not included in this change list; save them before requesting review.</p>}
        <div className="sd-comparison"><section><h3>Approved baseline {baseline ? `v${baseline.revision}` : 'not available'}</h3>
          <p>{baseline ? `Approved by ${baseline.approvedBy} at ${baseline.approvedAt}` : 'No immutable approved design has been returned.'}</p>
          {baseline && <p>Snapshot hash: <code>{baseline.snapshotHash}</code></p>}</section>
          <section><h3>Working revision v{graph.revision}</h3><p>{statusLabel(graph.governanceStatus)} · {graph.nodes.length} elements · {graph.edges.length} relationships</p></section></div>
        <table aria-label="Baseline changes"><thead><tr><th>Change</th><th>Record</th><th>Approved before</th><th>Working after</th></tr></thead>
          <tbody>{pageRecords(graph.baselineChanges, panelPage).map((change, index) => <tr key={`${change.recordId}-${index}`}><td>{change.kind}</td><td>{change.recordId}</td>
            <td><pre>{change.before || 'Absent'}</pre></td><td><pre>{change.after || 'Absent'}</pre></td></tr>)}</tbody></table>
        <RecordPager page={panelPage} total={graph.baselineChanges.length} onPage={setPanelPage} />
        {!graph.baselineChanges.length && <p>No baseline changes reported.</p>}
      </>}
      {panel === 'package' && !panelLoading && !panelError && <><p>Readiness: {statusLabel(graph.sspReadiness)} · Approved baseline: {baseline ? `v${baseline.revision}` : 'Missing'}</p>
        <p>Review the saved working design and its generated contribution below. Unsaved edits are not included. This preview does not submit, approve, or replace an SSP export.</p>
        <SourceLink url={`/systems/${encodeURIComponent(systemId)}/documents/preview?contribution=SystemDesign`}>Open generated SSP contribution</SourceLink>
        <p><SourceLink url={`/systems/${encodeURIComponent(systemId)}/documents/preview?source=approved&contribution=SystemDesign`}>Preview approved SSP output</SourceLink></p>
        {sspPreview && <><h3>Exact working-source SSP / OSCAL preview</h3>
          <p>Generated: {sspPreview.generatedAt} · Source state: {sspPreview.sourceState}</p><p>Content hash: <code>{sspPreview.contentHash}</code></p>
          {sspPreview.sourceGaps.map(gap => <p key={gap.code} className="sd-notice">{gap.code}: {gap.message}</p>)}
          <details><summary>Generated narrative and structured output</summary><pre>{sspPreview.content}</pre></details>
          <details><summary>SSP source manifest</summary><pre>{JSON.stringify(sspPreview.sourceManifest, null, 2)}</pre></details></>}
        <details><summary>Working revision structured records and provenance</summary><pre>{JSON.stringify(graph, null, 2)}</pre></details>
        <details><summary>Approved structured baseline</summary><pre>{JSON.stringify(baseline, null, 2)}</pre></details></>}
      {panel === 'history' && !panelLoading && !panelError && <><ul className="sd-gap-list">{pageRecords(history, panelPage).map((entry, index) => <li key={`${entry.revision}-${index}`}>
        <strong>v{entry.revision} · {entry.action} · {entry.governanceStatus}</strong><p>{entry.actor} · {entry.at}</p><p>{entry.reason}</p></li>)}
        {!history.length && <li>No retained history returned.</li>}</ul><RecordPager page={panelPage} total={history.length} onPage={setPanelPage} /></>}
      {panel === 'proposals' && <><ul className="sd-gap-list">{pageRecords(graph.proposals, panelPage).map(proposal => <li key={proposal.id}>
        <h3>{proposal.originalNode?.label || proposal.originalEdge?.purpose || proposal.recordId}</h3><p>{proposal.kind} · {proposal.state}</p>
        {proposal.conflictsWithHigherPrecedence && <p className="sd-notice">Conflicts with a higher-precedence reviewed decision. Inspect the original and explain any alteration.</p>}
        <p>Source fingerprint: <code>{proposal.sourceFingerprint}</code></p>
        <p>{proposal.reason || 'No decision reason recorded'}{proposal.actor && ` · ${proposal.actor}`}{proposal.decidedAt && ` · ${proposal.decidedAt}`}</p>
        <details><summary>Original proposal and retained result</summary><pre>{JSON.stringify(proposal, null, 2)}</pre></details>
        <SourceLink url={(proposal.originalNode?.source ?? proposal.originalEdge?.source)?.resolutionUrl}>View source</SourceLink>
        {graph.actions.canReconcile && <div className="sd-actions">
          {(proposal.state === 'Pending' ? ['accept', 'edit_accept', 'reject', 'defer'] as const
            : proposal.state === 'Rejected' || proposal.state === 'Deferred' ? ['recover'] as const : []).map(next => <button className="sd-button" key={next}
            disabled={savedOnly || next === 'accept' && proposal.conflictsWithHigherPrecedence || next === 'edit_accept' && !proposal.originalNode && !proposal.originalEdge}
            title={next === 'accept' && proposal.conflictsWithHigherPrecedence ? 'A higher-precedence conflict requires Edit and accept with a rationale.' : undefined}
            onClick={() => {
              if (next === 'edit_accept') { setProposalEditing(proposal); setEditing(proposal.resultNode ?? proposal.resultEdge ?? proposal.originalNode ?? proposal.originalEdge ?? null); setPanel(null); }
              else { setReason(''); setDecision({ proposal, action: next }); setPanel(null); }
            }}>{({ accept: 'Accept', edit_accept: 'Edit and accept', reject: 'Reject', defer: 'Defer', recover: 'Recover proposal' })[next]}</button>)}
        </div>}
      </li>)}{!graph.proposals.length && <li>No source proposals returned. Discovery state: {graph.discoveryState}.</li>}</ul><RecordPager page={panelPage} total={graph.proposals.length} onPage={setPanelPage} /></>}
    </SetupDialog>}
  </div>;
}
