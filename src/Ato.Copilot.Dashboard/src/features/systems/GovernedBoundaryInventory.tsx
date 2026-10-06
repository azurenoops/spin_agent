import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { getSystemDesign, reconcileSystemDesign, reviewSystemDesign, saveSystemDesign,
  type DesignNode, type SystemDesignGraph } from '../../api/systemDesign';
import DesignRecordEditor from '../system-design/DesignRecordEditor';
import UnsavedDesignGuard from '../system-design/UnsavedDesignGuard';
import { isNetworkComponent } from '../system-design/graphAdapter';
import SetupDialog from '../workspace-operations/SetupDialog';
import { SystemTaskColumns, systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';
import '../system-design/systemDesign.css';

const text = (value?: string | null) => value?.trim() || '';
const owner = (node: DesignNode) => text(node.deploymentOwner) || text(node.properties.Owner);
const environment = (node: DesignNode) => text(node.environment) || text(node.properties.Environment);
const type = (node: DesignNode) => text(node.properties.SubType) || text(node.properties.ComponentType)
  || text(node.properties.componentType) || (node.kind === 'ExternalSystem' ? 'External system' : 'Type not recorded');
const sourceLabel = (node: DesignNode) => !node.source ? 'Manual design record'
  : node.source.type === 'BoundaryComponentAssignment' || node.source.type === 'CspInheritedComponent'
    ? 'Provider-linked record'
    : node.source.type === 'InventoryItem' ? 'Hardware/software inventory'
      : node.source.type === 'SystemComponent' ? 'System inventory' : 'Recorded source';
const components = (graph: SystemDesignGraph) => graph.nodes.filter(node => isNetworkComponent(node)
  && node.kind !== 'System' && node.source?.type !== 'RegisteredSystem');
const documented = (node: DesignNode) => [text(node.label), type(node) !== 'Type not recorded', environment(node),
  ['InBoundary', 'OutOfBoundary'].includes(node.boundaryDisposition), owner(node), text(node.boundaryRationale)].filter(Boolean).length;
const conflicting = (node: DesignNode) => node.boundaryDisposition === 'InBoundary'
  && (['SharedService', 'SeparatelyAuthorized'].includes(node.boundaryRelationship ?? '')
    || node.kind === 'ExternalSystem' && node.source?.type === 'SystemInterconnection');
const dispositionLabel = (node: DesignNode) => node.boundaryDisposition === 'InBoundary'
  ? `Included in this system${conflicting(node) ? ' — conflicts with recorded relationship' : ''}`
  : node.boundaryDisposition === 'OutOfBoundary' ? 'Outside this system'
    : node.boundaryDisposition === 'Undetermined' ? 'Needs confirmation'
      : `Unknown recorded decision: ${node.boundaryDisposition || '(empty)'}`;
const statusLabel = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2');
const governanceLabel = (value: SystemDesignGraph['governanceStatus']) => value === 'Approved' ? 'Reviewed (design approved)' : statusLabel(value);
const safeSourceUrl = (url?: string) => !!url && url.startsWith('/') && !url.startsWith('//') && !/[\\\u0000-\u0020]/.test(url);
function failureMessage(failure: unknown, retainingEdits = false): string {
  const retained = retainingEdits ? ' Your local edits are retained. Review the saved revision and source records in System design before retrying.' : '';
  if (typeof failure === 'object' && failure !== null) {
    if ('error' in failure && typeof failure.error === 'string') return failure.error + retained;
    if ('response' in failure) {
      const response = failure.response as { status?: number; data?: { error?: string } };
      if (response.status === 409) return 'The saved revision or its sources changed. Your local edits are retained. Reload and compare in System design before retrying.';
      if (response.data?.error) return response.data.error + retained;
    }
  }
  return failure instanceof Error ? failure.message + retained : 'Inventory request failed.' + retained;
}

export default function GovernedBoundaryInventory({ systemId, children, recordStatus, renderHeading }: {
  systemId: string; children: ReactNode; recordStatus?: ReactNode; renderHeading?: (actions: ReactNode) => ReactNode;
}) {
  const [graph, setGraph] = useState<SystemDesignGraph | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState<DesignNode | null>(null);
  const [inspecting, setInspecting] = useState<DesignNode | null>(null);
  const [adding, setAdding] = useState(false);
  const [action, setAction] = useState<'save' | 'reconcile' | 'derive_draft' | null>(null);
  const [reason, setReason] = useState('');
  const writing = useRef(false);
  const request = useRef(0);
  const load = useCallback(async () => {
    const sequence = ++request.current;
    setLoading(true); setError('');
    try {
      const result = await getSystemDesign(systemId);
      if (sequence === request.current) setGraph(result);
    } catch (failure) { if (sequence === request.current) setError(failureMessage(failure)); }
    finally { if (sequence === request.current) setLoading(false); }
  }, [systemId]);
  useEffect(() => { void load(); return () => { request.current++; }; }, [load]);
  const editable = graph?.actions.canEdit === true && !['Approved', 'UnderReview'].includes(graph.governanceStatus);
  const open = (node: DesignNode) => { if (editable) setEditing(node); else setInspecting(node); };
  const openAction = (next: typeof action) => { setReason(''); setError(''); setAction(next); };
  const perform = async () => {
    if (!graph || !action || !reason.trim() || writing.current) return;
    writing.current = true; setBusy(true); setError(''); setMessage('');
    try {
      const command = { expectedRevision: graph.revision, reason: reason.trim() };
      const result = action === 'save'
        ? await saveSystemDesign(systemId, { ...command, nodes: graph.nodes, edges: graph.edges, groups: graph.groups })
        : action === 'reconcile' ? await reconcileSystemDesign(systemId, command)
          : await reviewSystemDesign(systemId, { ...command, action: 'derive_draft' });
      setGraph(result); setDirty(false); setAction(null);
      setMessage(action === 'save' ? 'Scope draft saved. Reviewed baseline unchanged.'
        : action === 'reconcile' ? 'Source changes reconciled into proposals. Review them in System design; no component was automatically approved.'
          : 'Working revision started. Approved baseline unchanged.');
    } catch (failure) { setError(failureMessage(failure, true)); }
    finally { writing.current = false; setBusy(false); }
  };
  const designUrl = `/systems/${systemId}/profile/SystemDesign?designView=Boundary`;
  const saveActions = <div className="flex flex-wrap gap-2">
    {graph?.actions.canDeriveDraft && <button type="button" disabled={busy || dirty} className={systemSecondaryAction}
      onClick={() => openAction('derive_draft')}>Start working revision</button>}
    <button type="button" className={systemPrimaryAction} disabled={!editable || !dirty || busy}
      onClick={() => openAction('save')}>{busy && action === 'save' ? 'Saving...' : 'Save scope draft'}</button>
    <Link className={systemSecondaryAction} to={designUrl}>Review scope changes</Link>
  </div>;
  const heading = renderHeading?.(saveActions);
  if (loading && !graph) return <>{heading}{recordStatus}<p role="status">Loading components &amp; system scope...</p>{children}</>;
  if (!graph) return <>{heading}{recordStatus}<p role="alert">{error || 'Inventory unavailable.'}
    <button type="button" className="ml-3 underline" onClick={() => void load()}>Retry inventory</button></p>{children}</>;
  const records = components(graph);
  const editorNodes = [...graph.nodes, ...(graph.availableNodes ?? []).filter(node =>
    node.kind === 'BoundaryDefinition' && !graph.nodes.some(record => record.id === node.id))];
  const unknown = records.filter(node => !['InBoundary', 'OutOfBoundary'].includes(node.boundaryDisposition) || conflicting(node));
  const incomplete = records.filter(node => documented(node) < 6);
  const firstUnknown = unknown[0];
  const firstIncomplete = incomplete[0];
  const consumed = (node: DesignNode) => graph.edges.some(edge => edge.relationshipType === 'UsesService' && edge.targetNodeId === node.id);
  const externalRecords = records.filter(node => ['SharedService', 'SeparatelyAuthorized'].includes(node.boundaryRelationship ?? '')
    || node.source?.type === 'SystemInterconnection' || node.kind === 'ExternalSystem'
    || node.boundaryDisposition === 'OutOfBoundary' && consumed(node));
  const pending = graph.proposals.filter(proposal => proposal.state === 'Pending' && proposal.originalNode
    && components({ ...graph, nodes: [proposal.originalNode] }).length > 0).length;
  const candidates = components({ ...graph, nodes: graph.availableNodes ?? [] })
    .filter(node => !graph.nodes.some(record => record.id === node.id));
  const awaiting = new Set([
    ...candidates.map(node => node.id),
    ...graph.proposals.filter(proposal => proposal.state === 'Pending' && proposal.originalNode
      && components({ ...graph, nodes: [proposal.originalNode] }).length > 0).map(proposal => proposal.originalNode!.id),
  ]).size;
  const stats = [
    [records.filter(node => node.boundaryDisposition === 'InBoundary' && !conflicting(node)).length, 'Included in this system'],
    [records.filter(node => node.boundaryDisposition === 'OutOfBoundary').length, 'Outside this system'],
    [unknown.length, 'Needs confirmation'], [awaiting, 'Awaiting reconciliation review'],
  ] as const;
  return <section className="scope-inventory space-y-5" aria-label="Governed components & system scope">
    {heading}
    <UnsavedDesignGuard dirty={dirty || !!editing} title="Unsaved inventory changes"
      description="Leaving discards unsaved inventory annotations. Saved sources and the approved design baseline remain unchanged." />
    {recordStatus}
    <div role="status" aria-label="Inventory review status"
      className="flex flex-wrap justify-between gap-3 rounded-lg border border-[#dedaf5] bg-[#f1effc] px-4 py-3 text-xs dark:border-slate-700 dark:bg-slate-800">
      <span>{governanceLabel(graph.governanceStatus)} · Working revision {graph.revision}{dirty ? ' · Unsaved changes' : ''}</span>
      <span>Saving a draft does not change the reviewed baseline or establish authorization.</span>
    </div>
    <SystemTaskColumns stretch support={<>
      <section className="boundary-support">
        <h2 className="text-[10px] font-semibold uppercase tracking-wider text-slate-500">Used in your package</h2>
        <h3 className="text-sm font-semibold">SSP · Reviewed system definition</h3>
        <p>Reviewed design records supply the SSP and native package. Draft annotations do not replace the approved baseline or alter canonical assets.</p>
        <Link className={systemSecondaryAction} to={`/systems/${systemId}/documents/preview`}>Preview contribution</Link>
      </section>
      <section className="boundary-support">
        <h2 className="text-[10px] font-semibold uppercase tracking-wider text-slate-500">Next scope work</h2>
        <p>{unknown.length} decisions need confirmation; {records.filter(node => !text(node.boundaryRationale)).length} rationale records
          and {records.filter(node => !owner(node)).length} operator records are missing.</p>
        <p>{incomplete.length} components need documentation attention. Review unresolved decisions and security responsibilities before submitting the design.</p>
        {firstUnknown && <button type="button" className="block text-left text-sm text-indigo-600 underline" disabled={busy}
          onClick={() => open(firstUnknown)}>Resolve {unknown.length} scope decision{unknown.length === 1 ? '' : 's'}</button>}
        {firstIncomplete && <button type="button" className="block text-left text-sm text-indigo-600 underline" disabled={busy}
          onClick={() => open(firstIncomplete)}>Complete {firstIncomplete.label}</button>}
        <p>{graph.sourcesStale ? 'Source records changed. Reconcile and review before relying on this inventory.' : 'Source change status is recorded in System design.'}</p>
        <Link className="text-sm text-indigo-600 underline" to={designUrl}>Review {pending} component proposals</Link>
        {candidates.length > 0 && <button type="button" className="block text-left text-sm text-indigo-600 underline" disabled={!editable || busy}
          onClick={() => setAdding(true)}>Review {candidates.length} source candidates</button>}
      </section>
      <section className="boundary-support">
        <h2 className="text-[10px] font-semibold uppercase tracking-wider text-slate-500">Review &amp; ownership</h2>
        <h3 className="text-sm font-semibold">Keep the next action clear</h3>
        <p>Authorized authors stage changes; authorized reviewers review the whole design in System design. A CSP link is not accepted inheritance or an ATO.</p>
        <p>{graph.approvedRevision != null ? `Approved baseline: version ${graph.approvedRevision}` : 'No approved design baseline recorded.'}</p>
        <Link className={systemSecondaryAction} to={designUrl}>Review system definition</Link>
      </section>
    </>}>
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div><h2 className="text-lg font-semibold">What belongs to this system?</h2>
          <p className="text-sm text-slate-600 dark:text-slate-300">Your authorization boundary defines the system you are preparing for ATO review. Include your application, API, and other system-managed components. Record external services and their connections separately.</p>
          <p className="mt-2 text-sm text-slate-600 dark:text-slate-300">An app and its API usually belong to the same system scope—not separate authorization boundaries.</p></div>
        {!renderHeading && saveActions}
      </header>
      {children}
      {!editable && <p className="text-sm text-slate-600 dark:text-slate-300">Scope editing is read-only for this revision or your current permissions.
        {graph.governanceStatus === 'UnderReview' ? ' This design is under review.' : ''}
        {graph.governanceStatus === 'Approved' ? ' The reviewed baseline stays locked until an authorized editor starts a working revision.' : ''}</p>}
      {error && !action && <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}
        {!dirty && <button type="button" disabled={busy} className="ml-3 underline" onClick={() => void load()}>Retry inventory</button>}</p>}
      {message && <p role="status" className="text-sm text-green-700">{message}</p>}
      <div className="space-y-4 min-w-0">
        <div className="flex flex-wrap justify-end gap-2">
          <button type="button" className={systemSecondaryAction} disabled={!graph.actions.canReconcile || busy || dirty || graph.governanceStatus === 'Approved'}
            onClick={() => openAction('reconcile')}>Reconcile discoveries</button>
          <button type="button" className={systemPrimaryAction} disabled={!editable || busy} onClick={() => setAdding(true)}>Add component</button>
        </div>
        <div className="grid grid-cols-2 gap-3 md:grid-cols-4">{stats.map(([count, label]) =>
          <div key={label} className="rounded-lg border border-slate-200 bg-white px-4 py-3 dark:border-slate-700 dark:bg-slate-900">
            <p className="text-2xl font-semibold text-indigo-600">{count}</p><p className="text-xs text-slate-500">{label}</p>
          </div>)}</div>
        <div className="overflow-x-auto rounded-lg border border-slate-100 dark:border-slate-700">
          <table aria-label="Components & system scope" className="w-full text-left text-xs">
            <thead className="bg-slate-50 dark:bg-slate-800"><tr>{['Component', 'Type / source', 'Environment', 'System scope decision', 'Operator / manager', 'Review', 'Open'].map(label =>
              <th scope="col" className="px-3 py-3 text-[10px] uppercase tracking-wide text-slate-500" key={label}>{label}</th>)}</tr></thead>
            <tbody className="divide-y divide-slate-100 bg-white dark:divide-slate-700 dark:bg-slate-900">
              {records.map(node => <tr key={node.id}>
                <td className="px-3 py-3 break-words">{node.label}</td>
                <td className="px-3 py-3">{type(node)} · {sourceLabel(node)}</td>
                <td className="px-3 py-3">{environment(node) || 'Not recorded'}</td>
                <td className="px-3 py-3">{dispositionLabel(node)}
                  {node.boundaryDisposition === 'OutOfBoundary' && !externalRecords.includes(node)
                    && <p className="mt-1 text-slate-500">Excluded component; use not recorded</p>}</td>
                <td className="px-3 py-3">{owner(node) || 'Not recorded'}</td>
                <td className="px-3 py-3"><span className="rounded bg-slate-100 px-2 py-1 text-slate-700 dark:bg-slate-800 dark:text-slate-200">
                  Design: {governanceLabel(graph.governanceStatus)}</span>
                  {node.source && <p className="mt-2 text-slate-500">Source: {statusLabel(node.source.reviewState)}</p>}
                  {documented(node) < 6 && <p className="mt-1 text-amber-700">Documentation missing</p>}</td>
                <td className="px-3 py-3"><button type="button" className={systemSecondaryAction} disabled={busy}
                  aria-label={`Open ${node.label}`} onClick={() => open(node)}>Open</button></td>
              </tr>)}
              {!records.length && <tr><td colSpan={7} className="px-4 py-6 text-slate-500">No computing components in the working design. Reconcile recorded sources or add a documented manual component.</td></tr>}
            </tbody>
          </table>
        </div>
        <section aria-label="External systems & shared services" className="space-y-3 rounded-lg border border-slate-200 p-4 dark:border-slate-700">
          <h2 className="font-semibold">External systems &amp; shared services</h2>
          <p className="text-sm">These support your system but are not automatically inside its authorization boundary. Document the connection, responsibility split, and supporting source records.</p>
          {externalRecords.map(node => <article key={node.id} className="space-y-1 border-t border-slate-200 pt-3 text-sm dark:border-slate-700">
            <h3 className="font-semibold">{node.label}</h3>
            <p>{dispositionLabel(node)} · {consumed(node) ? 'Recorded service consumption' : 'Consumption not recorded; confirm use'}</p>
            <p>Rationale: {text(node.boundaryRationale) || 'Not recorded'}</p>
            <p>Responsibility split: {text(node.securityResponsibility) || 'Not recorded'}</p>
            <p>Operator / manager: {owner(node) || 'Not recorded'}</p>
            {node.boundaryRelationship === 'SeparatelyAuthorized' && <p>Separately authorized is recorded information, not verified coverage without supporting evidence.</p>}
            {graph.edges.filter(edge => edge.sourceNodeId === node.id || edge.targetNodeId === node.id).map(edge =>
              <p key={edge.id}>Recorded connection: {edge.relationshipType} · {edge.purpose || 'Purpose not recorded'}
                {' · '}{edge.interconnectionId ? `Interconnection ${edge.interconnectionId} · ${edge.agreementStatus || 'Agreement not recorded'}` : 'No canonical interconnection linked'}</p>)}
            {node.source && <p>Source: {node.source.provenance} · Review: {statusLabel(node.source.reviewState)}
              {safeSourceUrl(node.source.resolutionUrl) && <Link className="ml-2 text-indigo-600 underline" to={node.source.resolutionUrl}>Open supporting source for {node.label}</Link>}</p>}
            <button type="button" className={systemSecondaryAction} disabled={busy} onClick={() => open(node)}>Inspect service {node.label}</button>
          </article>)}
          {!externalRecords.length && <p className="text-sm text-slate-500">No external systems or shared-service use recorded. Excluded components without recorded service use remain in the component register.</p>}
          <Link className={systemSecondaryAction} to={`/systems/${systemId}/profile/PortsProtocolsAndServices`}>Document connections &amp; responsibility split</Link>
          <Link className={systemSecondaryAction} to={`/systems/${systemId}/security-capabilities`}>Review provider sources &amp; responsibilities</Link>
        </section>
      </div>
    </SystemTaskColumns>
    {adding && <SetupDialog title="Add inventory component" busy={false} onClose={() => setAdding(false)}
      description="Choose a recorded CSP or organization source, or document a manual component. Applying stages a draft only; scope and approval are not inferred.">
      <div className="space-y-3">
        {candidates.map(node => <article key={node.id} className="rounded border border-slate-200 p-3">
          <h3 className="font-semibold">{node.label}</h3>
          <p className="text-xs text-slate-500">{type(node)} · {sourceLabel(node)} · {dispositionLabel(node)}</p>
          <button type="button" className={systemSecondaryAction} onClick={() => { setAdding(false); setEditing(node); }}
            aria-label={`Use recorded component ${node.label}`}>Use recorded component</button>
        </article>)}
        {!candidates.length && <p className="text-sm text-slate-500">No additional component sources returned. Reconcile recorded sources when they change.</p>}
        <button type="button" className={systemPrimaryAction} onClick={() => { setAdding(false); setEditing({
          id: `design:${crypto.randomUUID()}`, label: '', kind: 'DesignComponent', boundaryDisposition: 'Undetermined',
          reviewState: 'Unapproved', projectionStatus: 'Draft', sspImpact: 'System description and architecture', properties: {},
        }); }}>Add manually</button>
      </div>
    </SetupDialog>}
    {editing && <DesignRecordEditor inventoryOnly key={editing.id} node={editing} nodes={editorNodes} edges={graph.edges} groups={graph.groups}
      onClose={() => setEditing(null)} onApply={record => {
        if (!editable || !('label' in record)) return;
        const node = { ...record, reviewState: 'Unapproved', projectionStatus: 'Draft' };
        const scope = editorNodes.find(item => item.kind === 'BoundaryDefinition' && item.source?.id === node.boundaryDefinitionId);
        setGraph(current => {
          if (!current) return current;
          const nodes = current.nodes.some(item => item.id === node.id)
            ? current.nodes.map(item => item.id === node.id ? node : item) : [...current.nodes, node];
          if (scope && !nodes.some(item => item.id === scope.id)) nodes.push(scope);
          return { ...current, nodes };
        });
        setDirty(true); setEditing(null);
      }} />}
    {inspecting && <SetupDialog title={`Component ${inspecting.label}`} busy={false} onClose={() => setInspecting(null)}
      description="Read-only design record. Source approval does not establish component authorization.">
      <dl className="space-y-3">{[
        ['Component', inspecting.label], ['Environment', environment(inspecting)], ['System scope decision', dispositionLabel(inspecting)],
        ['Operator / manager', owner(inspecting)], ['Inclusion / exclusion rationale', inspecting.boundaryRationale],
        ['Security responsibility', inspecting.securityResponsibility], ['Source', inspecting.source?.provenance],
      ].map(([label, value]) => <div key={label}><dt className="font-semibold">{label}</dt><dd className="whitespace-pre-wrap break-all">{value || 'Not recorded'}</dd></div>)}</dl>
      <details className="mt-4 rounded border p-3"><summary>Advanced scope details</summary>
        <dl className="mt-3 space-y-3">{[
        ['Design record ID', inspecting.id], ['Named definition association', inspecting.boundaryDefinitionId],
        ['Recorded scope relationship', inspecting.boundaryRelationship], ['Network / trust zone', inspecting.networkZone],
        ['Source ID', inspecting.source?.id], ['Source version', inspecting.source?.version],
        ['External authorization reference', inspecting.externalAuthorizationReference],
      ].map(([label, value]) => <div key={label}><dt className="font-semibold">{label}</dt><dd className="whitespace-pre-wrap break-all">{value || 'Not recorded'}</dd></div>)}</dl>
      </details>
    </SetupDialog>}
    {action && <SetupDialog title={action === 'save' ? 'Save inventory draft' : action === 'reconcile' ? 'Reconcile recorded sources' : 'Start working inventory revision'}
      busy={busy} onClose={() => { if (!busy) setAction(null); }}
      description="This updates the governed System design revision. It does not approve records, change canonical assets or authorize the system.">
      <form onSubmit={event => { event.preventDefault(); void perform(); }} className="space-y-4">
        {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
        <label className="block text-sm">Reason for change<textarea required maxLength={2000} disabled={busy} value={reason}
          onChange={event => setReason(event.target.value)} className="mt-2 w-full rounded border p-2" /></label>
        <div className="flex flex-wrap justify-end gap-2"><button type="button" disabled={busy} className={systemSecondaryAction}
          onClick={() => setAction(null)}>Cancel</button>
          <button type="submit" disabled={busy || !reason.trim()} className={systemPrimaryAction}>
            {action === 'save' ? 'Confirm save' : action === 'reconcile' ? 'Confirm reconciliation' : 'Confirm working revision'}</button></div>
      </form>
    </SetupDialog>}
  </section>;
}
