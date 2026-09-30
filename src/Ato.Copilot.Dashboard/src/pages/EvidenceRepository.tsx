import { useCallback, useEffect, useRef, useState } from 'react';
import { useLocation, useParams, useSearchParams } from 'react-router-dom';
import { ArrowRight, FileText, Info, Plus, RefreshCw, Search, SlidersHorizontal, UsersRound } from 'lucide-react';
import { evidenceError, getEvidenceCatalog, type EvidenceCatalog, type EvidenceCatalogItem, type EvidenceCatalogQuery, type EvidenceView } from '../api/evidenceCatalog';
import EvidenceUploadDialog from '../components/EvidenceUploadDialog';
import EvidenceCatalogDrawer from '../features/evidence/EvidenceCatalogDrawer';
import '../features/evidence/EvidenceCatalog.css';

const views: { id: EvidenceView; label: string }[] = [
  { id: 'all', label: 'All evidence' }, { id: 'system', label: 'System evidence' }, { id: 'provider', label: 'Provider shared' },
];
const families = ['AC', 'AT', 'AU', 'CA', 'CM', 'CP', 'IA', 'IR', 'MA', 'MP', 'PE', 'PL', 'PM', 'PS', 'PT', 'RA', 'SA', 'SC', 'SI', 'SR'];
const categories = ['Screenshot', 'ScanResult', 'ConfigurationExport', 'PolicyDocument', 'AuditLog', 'TestResult', 'Other',
  'Configuration', 'PolicyCompliance', 'ResourceCompliance', 'SecurityAssessment', 'ActivityLog', 'Inventory'];
export function evidenceDate(value: string | null, exact = false): string {
  if (!value) return 'Date not recorded';
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return 'Date unavailable';
  return exact ? date.toLocaleString() : date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}

export default function EvidenceRepository() {
  const { id } = useParams<{ id: string }>();
  return id ? <EvidenceCatalogPage key={id} systemId={id} /> : <p role="alert">Select a system to view evidence.</p>;
}

function EvidenceCatalogPage({ systemId }: { systemId: string }) {
  const [params, setParams] = useSearchParams();
  const location = useLocation();
  const [data, setData] = useState<EvidenceCatalog | null>(null);
  const [loadedQuery, setLoadedQuery] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  const [showUpload, setShowUpload] = useState(false);
  const [guidance, setGuidance] = useState(false);
  const [filters, setFilters] = useState(false);
  const returnFocus = useRef<HTMLElement | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const contribution = useRef<HTMLDetailsElement>(null);
  const selected = params.get('evidence');
  const view: EvidenceView = params.get('view') === 'provider' ? 'provider' : params.get('view') === 'system' ? 'system' : 'all';
  const query: EvidenceCatalogQuery = {
    view, search: params.get('search') ?? '', family: params.get('family') ?? '',
    category: params.get('category') ?? '', source: params.get('source') ?? '',
    dateFrom: params.get('dateFrom') ?? '', dateTo: params.get('dateTo') ?? '',
    sortBy: params.get('sortBy') ?? 'uploadedAt', sortOrder: params.get('sortOrder') === 'asc' ? 'asc' : 'desc',
    page: Math.max(1, Number(params.get('page')) || 1), pageSize: 25,
  };
  const queryKey = JSON.stringify(query);
  const current = loadedQuery === queryKey ? data : null;
  const refresh = useCallback(() => setRevision(value => value + 1), []);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    setData(null);
    const requestQuery: EvidenceCatalogQuery = JSON.parse(queryKey);
    getEvidenceCatalog(systemId, requestQuery, controller.signal)
      .then(value => {
        if (!controller.signal.aborted) { setData(value); setLoadedQuery(queryKey); }
      })
      .catch(reason => { if (!controller.signal.aborted) setError(evidenceError(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, queryKey, revision]);

  const change = (key: string, value: string) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value); else next.delete(key);
    if (key !== 'page') next.delete('page');
    setParams(next, { replace: key === 'search' });
  };
  const open = (item: EvidenceCatalogItem, target?: HTMLElement, tab = 'overview') => {
    returnFocus.current = target ?? heading.current;
    const next = new URLSearchParams(params);
    next.set('evidence', item.id);
    next.set('detailTab', tab);
    setParams(next);
  };
  const close = useCallback(() => {
    const next = new URLSearchParams(location.search);
    next.delete('evidence'); next.delete('detailTab');
    setParams(next, { replace: true });
  }, [location.search, setParams]);
  const priorSelected = useRef(selected);
  useEffect(() => {
    if (priorSelected.current && !selected) (returnFocus.current?.isConnected ? returnFocus.current : heading.current)?.focus();
    priorSelected.current = selected;
  }, [selected]);

  const missing = current?.items.find(item => item.linksKnown && item.controls.length === 0 && item.source !== 'Provider');
  const activeSources = current?.sources.filter(source => view === 'all' || source.source === view) ?? [];
  const unavailable = activeSources.some(source => source.state !== 'available');
  const hasFilters = Boolean(query.search || query.family || query.category || query.source || query.dateFrom || query.dateTo);
  const totalPages = current ? Math.ceil(current.availableCount / query.pageSize) : 0;
  const canUpload = current?.permissions.canUpload === true && !loading;

  return <section className={`ew-page${selected ? ' ew-open' : ''}`} aria-labelledby="evidence-heading">
    <header className="ew-heading">
      <div><h1 id="evidence-heading" ref={heading} tabIndex={-1}>Evidence</h1>
        <p>Find supporting records and see what still needs attention.</p></div>
      <div className="ew-heading-actions">
        <button type="button" className="ew-text-button" onClick={() => {
          if (contribution.current) { contribution.current.open = true; contribution.current.scrollIntoView({ block: 'nearest' }); }
        }}>About evidence</button>
        <button type="button" className="ew-primary" disabled={!canUpload} onClick={() => { if (canUpload) setShowUpload(true); }}>
          <Plus size={15} /> Upload evidence
        </button>
        {!canUpload && <small>{current?.permissions.uploadReason ?? (loading ? 'Checking upload access…' : 'Upload access unavailable')}</small>}
      </div>
    </header>
    {missing && (current?.counts.missingLinks ?? 0) > 0 && <div className="ew-attention">
      <Info size={17} /><span>{current?.counts.missingLinks} {current?.counts.missingLinks === 1 ? 'record needs' : 'records need'} a control link</span>
      <button type="button" className="ew-text-button" onClick={event => open(missing, event.currentTarget, 'controls')}>View record <ArrowRight size={15} /></button>
    </div>}
    <div className="ew-catalog">
      <div className="ew-tabs" role="tablist" aria-label="Evidence sources">
        {views.map(tab => <button key={tab.id} type="button" role="tab" aria-selected={view === tab.id}
          onClick={() => change('view', tab.id)}>
          {tab.label} ({current ? current.counts[tab.id] ?? 'unavailable' : loading ? 'loading…' : 'unavailable'})
        </button>)}
      </div>
      <div className="ew-toolbar">
        <label className="ew-search"><Search size={17} aria-hidden="true" />
          <input aria-label="Search evidence or control" placeholder="Search evidence or control…" value={query.search}
            onChange={event => change('search', event.target.value)} /></label>
        <button type="button" aria-expanded={filters} aria-controls="evidence-filters" onClick={() => setFilters(value => !value)}>
          <SlidersHorizontal size={16} /> Filters{hasFilters ? ' · active' : ''}
        </button>
        <button type="button" aria-label="Refresh evidence" title="Refresh" disabled={loading} onClick={refresh}><RefreshCw size={17} /></button>
      </div>
      {filters && <div id="evidence-filters" className="ew-filters">
        <label>Control family<select value={query.family} onChange={e => change('family', e.target.value)}>
          <option value="">All families</option>{families.map(f => <option key={f}>{f}</option>)}</select></label>
        <label>Category<select value={query.category} onChange={e => change('category', e.target.value)}>
          <option value="">All categories</option>{categories.map(c => <option key={c} value={c}>{c.replace(/([a-z])([A-Z])/g, '$1 $2')}</option>)}</select></label>
        <label>Source<select value={query.source} onChange={e => change('source', e.target.value)}>
          <option value="">All sources</option><option value="Manual">System upload</option><option value="Automated">Automated collection</option>
          <option value="Provider">Provider shared</option></select></label>
        <label>From date<input type="date" value={query.dateFrom} onChange={e => change('dateFrom', e.target.value)} /></label>
        <label>Through date<input type="date" value={query.dateTo} onChange={e => change('dateTo', e.target.value)} /></label>
        <label>Sort by<select value={query.sortBy} onChange={e => change('sortBy', e.target.value)}>
          <option value="uploadedAt">Record date</option><option value="fileName">Evidence name</option>
          <option value="controlId">Control</option><option value="category">Category</option></select></label>
        <label>Order<select value={query.sortOrder} onChange={e => change('sortOrder', e.target.value)}>
          <option value="desc">Descending</option><option value="asc">Ascending</option></select></label>
        <button type="button" onClick={() => {
          const next = new URLSearchParams(params);
          ['search', 'family', 'category', 'source', 'dateFrom', 'dateTo', 'sortBy', 'sortOrder', 'page'].forEach(key => next.delete(key));
          setParams(next);
        }}>Clear filters</button>
      </div>}
      {error && <div className="ew-error" role="alert"><p>{error}</p><button type="button" onClick={refresh}>Retry evidence</button></div>}
      {current?.sources.filter(source => source.state !== 'available').map(source => <div className="ew-error" role="alert" key={source.source}>
        <p>{source.message ?? `${source.source === 'provider' ? 'Provider' : 'System'} evidence ${source.state === 'denied' ? 'access denied' : 'unavailable'}.`}
          {' '}Totals for this source are unavailable.</p><button type="button" onClick={refresh}>Retry {source.source} evidence</button>
      </div>)}
      {loading && <p className="ew-state" role="status">Loading evidence…</p>}
      {!loading && current && current.items.length > 0 && <>
        <div className="ew-table-scroll"><table aria-label="Evidence catalog"><thead><tr>
          <th>Evidence</th><th>Source</th><th>Linked controls</th><th>Next step</th>
        </tr></thead><tbody>{current.items.map(item => <tr key={item.id} className={selected === item.id ? 'ew-selected' : ''}>
          <td><button type="button" className="ew-record" aria-label={`View evidence ${item.name}`} onClick={e => open(item, e.currentTarget)}>
            <FileText size={24} aria-hidden="true" /><span><strong>{item.name}</strong>
              <small>{item.source === 'Automated' ? 'Collected' : item.source === 'Provider' ? 'Shared' : 'Uploaded'} {evidenceDate(item.recordedAt)}</small></span>
          </button></td>
          <td>{item.sourceLabel}</td>
          <td><div className="ew-control-tags">{item.controls.slice(0, 2).map(control => <span key={control.controlId} className="ew-tag">{control.controlId}</span>)}
            {item.controls.length > 2 && <span className="ew-tag">+{item.controls.length - 2}</span>}
            {!item.controls.length && <span className={item.linksKnown ? 'ew-unlinked' : 'ew-muted'}>{item.linksKnown ? 'Not linked' : 'Not shared'}</span>}</div></td>
          <td><button type="button" className="ew-text-button" onClick={e => open(item, e.currentTarget, item.linksKnown && !item.controls.length ? 'controls' : 'overview')}>
            {item.linksKnown && !item.controls.length ? 'View linking gap' : 'View evidence'} <ArrowRight size={15} /></button></td>
        </tr>)}</tbody></table></div>
        <div className="ew-record-footer">
          <small>{current.totalCount === null
            ? `${current.availableCount} available ${current.availableCount === 1 ? 'record' : 'records'} · partial results`
            : `${current.totalCount} ${current.totalCount === 1 ? 'record' : 'records'}`}</small>
          {totalPages > 1 && <nav aria-label="Evidence pages">
            <button type="button" disabled={query.page <= 1} onClick={() => change('page', String(query.page - 1))}>Previous</button>
            <span>{query.page} of {totalPages}</span>
            <button type="button" disabled={query.page >= totalPages} onClick={() => change('page', String(query.page + 1))}>Next</button>
          </nav>}
        </div>
      </>}
      {!loading && current && !current.items.length && !unavailable && <div className="ew-empty">
        {current.availableCount > 0 ? <><h2>No records on this page.</h2>
          <p>The catalog may have changed since this page was selected.</p>
          <button type="button" onClick={() => change('page', '1')}>Return to first page</button>
        </> : view === 'provider' && !hasFilters ? <>
          <UsersRound size={30} /><h2>No provider evidence shared yet.</h2>
          <p>Only records explicitly shared with this system appear here.</p>
          <div><button type="button" className="ew-primary" onClick={refresh}><RefreshCw size={15} />Refresh access</button>
            <button type="button" onClick={() => setGuidance(true)}>Sharing guidance</button></div>
        </> : <><FileText size={30} /><h2>{hasFilters ? 'No matching evidence.' : 'No evidence yet.'}</h2>
          <p>{hasFilters ? 'Try another search or clear your filters.' : 'Upload a supporting record or collect evidence through an authorized control workflow.'}</p></>}
      </div>}
      {guidance && <section className="ew-guidance" aria-label="Sharing guidance"><h2>Sharing guidance</h2>
        <p>A provider must explicitly approve a retained summary for this organization and system through an active provider relationship.
          Refresh access after that approval. Private attachments remain restricted; sharing is not assessment acceptance.</p>
        <button type="button" onClick={() => setGuidance(false)}>Close guidance</button></section>}
      <details className="ew-contribution" ref={contribution}>
        <summary>How evidence supports your SSP and assessment</summary>
        <p>Evidence documents how a control operates; assessors still review its currency, relevance, and sufficiency.
          File availability and provider sharing approval do not establish that a control passed assessment.</p>
        <p>SSP exports can retain approved provider-summary references and version hashes. Authorization-package preparation can
          include uploaded artifact manifests and files. Assessment snapshots separately retain automated evidence hashes and human determinations.</p>
        <p>Catalog control links are not automatically SSP supporting citations. These sources do not yet share one complete
          evidence-version path through every SSP, assessment, and eMASS output. Uploading or linking evidence does not establish package readiness,
          eMASS submission, or an authorization decision.</p>
      </details>
    </div>
    <p className="ew-footnote"><Info size={16} />Provider records show sharing scope and whether a file or only a summary is available.</p>
    {selected && <EvidenceCatalogDrawer key={`${systemId}:${selected}`} systemId={systemId} id={selected}
      tab={params.get('detailTab') ?? 'overview'} onTab={tab => {
        const next = new URLSearchParams(params); next.set('detailTab', tab); setParams(next, { replace: true });
      }} onClose={close} onChanged={refresh} />}
    {showUpload && <EvidenceUploadDialog key={systemId} systemId={systemId} onClose={() => setShowUpload(false)}
      onUploaded={() => { setShowUpload(false); refresh(); }} />}
  </section>;
}
