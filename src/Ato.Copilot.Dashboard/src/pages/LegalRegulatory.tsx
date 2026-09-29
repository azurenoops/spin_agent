import { useCallback, useEffect, useRef, useState } from 'react';
import { FileText, Plus, Search } from 'lucide-react';
import { Link, useLocation, useParams, useSearchParams } from '../features/workspaces/workspaceNavigation';
import { getPolicyWorkspace, policyError, type PolicyQuery, type PolicyReference, type PolicyWorkspace } from '../api/policyWorkspace';
import AddPolicyDrawer from '../features/policies/AddPolicyDrawer';
import PolicyReferenceDrawer from '../features/policies/PolicyReferenceDrawer';
import '../features/policies/PolicyWorkspace.css';

export default function LegalRegulatory() {
  const { id } = useParams<{ id: string }>();
  return id ? <PolicyWorkspacePage key={id} systemId={id} /> : <p role="alert">Select a system to view policy references.</p>;
}
function PolicyWorkspacePage({ systemId }: { systemId: string }) {
  const [params, setParams] = useSearchParams();
  const location = useLocation();
  const [data, setData] = useState<PolicyWorkspace | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const [notice, setNotice] = useState('');
  const [loadedKey, setLoadedKey] = useState('');
  const returnFocus = useRef<HTMLElement | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const adding = params.get('policyAction') === 'add';
  const selected = params.get('reference');
  const open = adding || !!selected;
  const query: PolicyQuery = {
    search: params.get('search') ?? '', status: params.get('status') ?? '', sourceChanged: params.get('sourceChanged') ?? '',
    page: Math.max(1, Math.floor(Number(params.get('page')) || 1)), pageSize: 25,
  };
  const queryKey = JSON.stringify(query);
  const current = queryKey === loadedKey ? data : null;
  const refresh = useCallback(() => setRevision(value => value + 1), []);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError(null);
    const requestQuery: PolicyQuery = JSON.parse(queryKey);
    getPolicyWorkspace(systemId, requestQuery, controller.signal)
      .then(value => { if (!controller.signal.aborted) { setData(value); setLoadedKey(queryKey); } })
      .catch(reason => { if (!controller.signal.aborted) setError(policyError(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, queryKey, revision]);
  const change = (key: string, value: string) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value); else next.delete(key);
    if (key !== 'page') next.delete('page');
    setParams(next, { replace: key === 'search' });
  };
  const show = (key: 'policyAction' | 'reference', value: string, element: HTMLElement) => {
    returnFocus.current = element; setNotice('');
    const next = new URLSearchParams(params); next.delete('policyAction'); next.delete('policySource'); next.delete('reference'); next.set(key, value);
    setParams(next);
  };
  const close = useCallback(() => {
    const next = new URLSearchParams(location.search); next.delete('policyAction'); next.delete('policySource'); next.delete('reference');
    setParams(next, { replace: true });
  }, [location.search, setParams]);
  const previousOpen = useRef(open);
  useEffect(() => {
    if (previousOpen.current && !open) (returnFocus.current?.isConnected ? returnFocus.current : heading.current)?.focus({ preventScroll: true });
    previousOpen.current = open;
  }, [open]);
  const added = (reference: PolicyReference) => {
    const next = new URLSearchParams(params); next.delete('policyAction'); next.delete('policySource'); next.set('reference', reference.id);
    setParams(next, { replace: true }); setNotice('System reference added. No applicability approval was recorded.'); refresh();
  };
  const canAssign = !loading && !error && current?.permissions.canAssign === true;
  const addButton = <button type="button" className="pw-primary" disabled={!canAssign}
    onClick={event => { if (canAssign) show('policyAction', 'add', event.currentTarget); }}>
    <Plus size={17} />Add policy</button>;
  return <section className={`pw-page${open ? ' pw-open' : ''}`} aria-labelledby="policy-heading">
    <header className="pw-page-header"><div><h1 ref={heading} tabIndex={-1} id="policy-heading">Policies for this system</h1>
      <p>Keep the policies you rely on and explain why they apply.</p></div>
      <Link to="/components?type=Policy">Organization policy library ↗</Link>
    </header>
    {notice && <p className="pw-notice" role="status">{notice}</p>}
    {loading && <p className="pw-loading" role="status">Loading system policy references…</p>}
    {error && <div className="pw-error" role="alert"><p>{error}</p><button type="button" onClick={refresh}>Retry policies</button></div>}
    {data && data.unfilteredTotal > 0 && <div className="pw-toolbar">
      <label className="pw-search"><Search size={16} /><input aria-label="Search policy references"
        placeholder="Search policy references…" value={query.search} onChange={e => change('search', e.target.value)} /></label>
      <label className="pw-filter">Source status<select value={query.status} onChange={e => change('status', e.target.value)}>
        <option value="">All statuses</option><option>Active</option><option>Planned</option><option>Decommissioned</option></select></label>
      <label className="pw-filter">Source updates<select value={query.sourceChanged} onChange={e => change('sourceChanged', e.target.value)}>
        <option value="">All sources</option><option value="true">Changed</option><option value="false">Unchanged</option></select></label>
      {addButton}
    </div>}
    {current && !loading && !error && <>
      {current.unfilteredTotal === 0 ? <>
        <div className="pw-empty"><div className="pw-empty-icon"><FileText size={45} /></div>
          <h2>No policies linked yet</h2><p>Choose a policy from your organization library to get started.</p>{addButton}
          {!current.permissions.canAssign && <p className="pw-hint">{current.permissions.assignReason}</p>}
        </div>
        <ol className="pw-howto"><li><span>1</span><div><strong>Choose a source</strong><p>Select a policy from your organization library.</p></div></li>
          <li><span>2</span><div><strong>Explain applicability</strong><p>Record why it applies to this system.</p></div></li>
          <li><span>3</span><div><strong>Save system reference</strong><p>Keep the policy version and your rationale.</p></div></li></ol>
      </> : <div className="pw-list-card">
        {!current.permissions.canAssign && <p className="pw-hint">{current.permissions.assignReason}</p>}
        {current.items.length ? <div className="pw-table-scroll"><table aria-label="System policy references"><thead><tr>
          <th>Policy &amp; retained source</th><th>Why it applies</th><th>Source status</th><th>Applicability review</th><th>Next step</th>
        </tr></thead><tbody>{current.items.map(reference => <tr key={reference.id} className={selected === reference.id ? 'pw-selected' : ''}>
          <td><strong>{reference.name}</strong><small>{reference.retainedVersionLabel ?? (reference.retention === 'Indirect' ? 'Through a linked capability' : 'No retained source version')}</small>
            {reference.sourceChanged && <span className="pw-change">Source changed</span>}</td>
          <td><p className="pw-prose">{reference.rationale ?? 'Rationale not recorded'}</p></td>
          <td><span className="pw-pill">{reference.sourceStatus ?? 'Unavailable'} source</span></td>
          <td><small>Not recorded<br />Review workflow unavailable</small></td>
          <td><button type="button" className="pw-link-button" aria-label={`View reference ${reference.name}`}
            onClick={event => show('reference', reference.id, event.currentTarget)}>View reference →</button></td>
        </tr>)}</tbody></table></div> : <div className="pw-no-results"><h2>No matching policy references.</h2>
          <p>Adjust the filters or return to the first page.</p><button type="button" onClick={() => {
            const next = new URLSearchParams(params); ['search', 'status', 'sourceChanged', 'page'].forEach(key => next.delete(key)); setParams(next);
          }}>Clear filters</button></div>}
        <div className="pw-list-footer"><span>{current.totalCount} matching {current.totalCount === 1 ? 'reference' : 'references'}</span>
          {current.items.length > 0 && current.totalCount > current.pageSize && <nav className="pw-pager" aria-label="Policy pages">
            <button type="button" disabled={query.page <= 1} onClick={() => change('page', String(query.page - 1))}>Previous</button>
            <span>{query.page} of {Math.ceil(current.totalCount / current.pageSize)}</span>
            <button type="button" disabled={query.page * current.pageSize >= current.totalCount} onClick={() => change('page', String(query.page + 1))}>Next</button>
          </nav>}
        </div>
      </div>}
    </>}
    <details className="pw-ssp"><summary><span>How policy references support your SSP<small>Source versions and system rationale stay traceable.</small></span></summary>
      <p>These references document which policy sources your system relies on and why. An Active source is not approval of system applicability,
        control implementation, narrative approval, or an authorization decision.</p>
      <p>Policy records can appear as generic component inventory in SSP/OSCAL output. Linking a policy does not currently inject it into implementation narratives
        or supporting-reference citations. The retained source and rationale do not yet flow through every SSP/eMASS output.
        Review the actual generated documents; saving a reference does not establish submission readiness.</p>
      <p>Applicability review is not implemented in the current policy-assignment contract. No review approval is implied by linking.</p>
    </details>
    {adding && current && !error && <AddPolicyDrawer key={systemId} systemId={systemId} systemName={current.systemName}
      permissions={current.permissions} initialSourceId={params.get('policySource')} onClose={close} onAdded={added} />}
    {selected && !adding && <PolicyReferenceDrawer key={`${systemId}:${selected}`} systemId={systemId} id={selected}
      onClose={close} onChanged={refresh} />}
  </section>;
}
