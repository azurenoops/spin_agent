import { useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import WorkspacePageHeader from '../../components/layout/WorkspacePageHeader';
import { Pager, Status, useQueryState, useRemote } from '../workspace-operations/workspaceUi';
import SetupDialog from '../workspace-operations/SetupDialog';
import { ProviderBadge } from '../provider-authorizations/ProviderPresentation';
import { ProviderMonitoringPanel } from '../provider-authorizations/ProviderMonitoringPage';
import { getProviderMonitoring, type ProviderMonitoringSource } from '../provider-authorizations/providerMonitoringApi';
import { impactOutcome, impactTitle } from '../provider-authorizations/impactPresentation';
import { readAllPages } from '../provider-authorizations/providerReadModels';
import * as api from '../provider-authorizations/api';
import type { ImpactReview, Offering } from '../provider-authorizations/types';

const pendingReview = (review: ImpactReview) => review.disposition === 'PendingReview' || review.disposition === 'RequestChanges';
const reviewHref = (offeringId: string, reviewId: string) =>
  `${api.authorizationHref(offeringId, 'impact')}?${new URLSearchParams({ reviewId, returnTo: 'changes' })}`;
interface QueueRow {
  key: string; name: string; summary: string; source: string; impact: string; state: string;
  action: string; href?: string; inspect?: ProviderMonitoringSource; pending: boolean;
}

export default function ProviderChangesPage() {
  const { params, set } = useQueryState();
  const selectedId = params.get('offeringId');
  const monitoring = params.get('tab') === 'monitoring';
  const offerings = useRemote(signal => readAllPages(page => api.listOfferings(page, '', signal), signal), []);
  const selected = selectedId ? offerings.data?.find(item => item.offeringId === selectedId) : offerings.data?.[0];
  return <div className="provider-workspace">
    {!monitoring && <div className="provider-page-head"><WorkspacePageHeader eyebrow="Provider operations" title="Changes requiring attention"
      description="Understand service changes, affected customers, and the action each owner needs to take." /></div>}
    <Status loading={offerings.loading} error={offerings.error} retry={offerings.retry} />
    {offerings.data && <label className="mb-5 block max-w-md text-xs text-slate-500">Service offering
      <select aria-label="Service offering" className="mt-2 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm"
        value={selected?.offeringId ?? ''} onChange={event => set({ offeringId: event.target.value, page: 1 })}>
        {!selected && <option value="">Select an offering</option>}
        {offerings.data.map(item => <option key={item.offeringId} value={item.offeringId}>{item.name}</option>)}
      </select>
    </label>}
    {selected ? <>
      <nav aria-label="Provider change tasks" className="provider-tabs">
        <Link to={`/provider-changes?${new URLSearchParams({ offeringId: selected.offeringId })}`} aria-current={!monitoring ? 'page' : undefined}>Change queue</Link>
        <Link to={`/provider-changes?${new URLSearchParams({ offeringId: selected.offeringId, tab: 'monitoring' })}`} aria-current={monitoring ? 'page' : undefined}>Service monitoring</Link>
      </nav>
      {monitoring ? <ProviderMonitoringPanel offeringId={selected.offeringId} /> : <ChangeQueue key={selected.offeringId} offering={selected} />}
    </> : offerings.data && <p role="status">{selectedId ? 'The selected offering is unavailable. Select an authorized offering; no substitute is chosen automatically.' : 'No provider offerings are available.'}</p>}
  </div>;
}

function ChangeQueue({ offering }: { offering: Offering }) {
  const [view, setView] = useState('attention');
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);
  const [inspected, setInspected] = useState<ProviderMonitoringSource | null>(null);
  const reviews = useRemote(signal => readAllPages(page => api.listImpactReviews(offering.offeringId, page, signal), signal), [offering.offeringId]);
  const overview = useRemote(signal => api.getOfferingOverview(offering.offeringId, 1, 1, signal), [offering.offeringId]);
  const missions = useRemote(signal => readAllPages(async page => (await api.getBoundaryOverview(offering.offeringId, 1, page, signal)).missionSystems, signal), [offering.offeringId]);
  const monitoring = useRemote(signal => getProviderMonitoring(offering.offeringId, signal), [offering.offeringId]);
  const evidence = useRemote(async signal => {
    const findings = await readAllPages(page => api.listFindings(offering.offeringId, page, signal), signal);
    return (await Promise.all(findings.map(finding => readAllPages(page => api.listFindingEvidence(offering.offeringId, finding.findingId, page, signal), signal)))).flat();
  }, [offering.offeringId]);
  const rows: QueueRow[] = (reviews.data ?? []).map(review => ({
    key: `review:${review.reviewId}`, name: impactTitle(review), summary: review.summary ?? 'Retained provider impact context.',
    source: 'Provider impact review', impact: review.affectedCounts ? `${review.affectedCounts.systems} recorded system targets` : 'Impact counts unavailable',
    state: pendingReview(review) && review.stale ? 'New assessment required' : impactOutcome(review.disposition),
    action: pendingReview(review) ? 'Review' : 'View', href: reviewHref(offering.offeringId, review.reviewId), pending: pendingReview(review),
  }));
  for (const item of evidence.data ?? []) rows.push({
    key: `evidence:${item.evidenceId}`, name: item.fileName, summary: item.description,
    source: 'Provider evidence', impact: 'Evidence relevance and access review',
    state: item.state, action: 'View', href: api.authorizationHref(offering.offeringId,
      `evidence/${encodeURIComponent(item.evidenceId)}?findingId=${encodeURIComponent(item.findingId)}&returnTo=changes`),
    pending: item.state === 'PendingReview',
  });
  const queuedEvidence = new Set((evidence.data ?? []).map(item => item.evidenceId));
  if (view !== 'history') for (const source of monitoring.data?.sources ?? []) {
    if (source.collectionHealth === 'Available' || (source.signal === 'EvidenceFreshness' && queuedEvidence.has(source.sourceId))) continue;
    rows.push({ key: `source:${source.signal}:${source.sourceId}`, name: source.name,
      summary: 'Recorded source availability; not a live collection-health assertion.',
      source: 'Monitoring source', impact: source.signal, state: source.collectionHealth, action: 'Inspect', inspect: source, pending: true });
  }
  const filtered = rows.filter(row => (view === 'all' || (view === 'history' ? !row.pending : row.pending))
    && `${row.name} ${row.summary} ${row.source}`.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()));
  const shown = filtered.slice((page - 1) * 15, page * 15);
  const pending = reviews.data?.filter(pendingReview);
  const first = pending?.[0];
  const sourceFacts = monitoring.data?.sources;
  const sourceHealth = !sourceFacts ? 'Unavailable' : !sourceFacts.length ? 'Not recorded'
    : sourceFacts.every(item => item.collectionHealth === 'Available') ? 'Available'
      : sourceFacts.some(item => item.collectionHealth === 'Available') ? 'Partial' : 'Needs attention';
  const states = [reviews, overview, missions, monitoring, evidence];
  const unknown = states.some(state => state.loading || state.error);
  const refresh = () => states.forEach(state => state.retry());
  return <>
    <dl className="provider-metrics">
      {[
        ['Provider reviews', reviews.loading ? 'Loading…' : reviews.error ? 'Unavailable' : pending?.length ?? 'Unavailable', 'Pending provider disposition'],
        ['Mission systems', missions.loading ? 'Loading…' : missions.error ? 'Unavailable' : new Set(missions.data?.map(item => item.systemId)).size, 'Allocated to this offering; not an impact total'],
        ['Customer reviews', overview.loading ? 'Loading…' : overview.error ? 'Unavailable' : overview.data?.customerActionCount ?? 'Not reported', 'Pending relationship / narrative actions'],
        ['Source health', monitoring.loading ? 'Loading…' : sourceHealth, 'Recorded source facts, not live collection'],
      ].map(([label, value, caption]) => <div key={label}><dt>{label}</dt><dd aria-label={String(label)}>{value}</dd><p>{caption}</p></div>)}
    </dl>
    <section className="provider-panel">
      <div className="provider-panel-head"><h2>Change queue</h2><div className="flex flex-wrap gap-2">
        {first && <Link className="provider-primary" to={reviewHref(offering.offeringId, first.reviewId)}>Review next pending change</Link>}
        <button type="button" className="provider-secondary" onClick={refresh}>Refresh queue</button>
      </div></div>
      <div className="mb-4 flex flex-wrap items-end gap-4">
        <label className="min-w-0 flex-1 text-xs">Search changes<input type="search" aria-label="Search changes" placeholder="Search change or source"
          className="mt-1 block w-full max-w-[380px] rounded border border-slate-300 px-3 py-2 text-sm" value={query}
          onChange={event => { setQuery(event.target.value); setPage(1); }} /></label>
        <label className="text-xs">Queue view<select aria-label="Queue view" className="mt-1 block rounded border border-slate-300 px-3 py-2 text-sm"
          value={view} onChange={event => { setView(event.target.value); setPage(1); }}>
          <option value="attention">Needs attention</option><option value="history">Reviewed history</option><option value="all">All records</option>
        </select></label>
      </div>
      {states.some(state => state.loading) && <p role="status" className="mb-4 text-sm text-slate-500">Loading change queue records…</p>}
      {states.map((state, index) => state.error && <Status key={index} loading={false} error={state.error} retry={state.retry} />)}
      <div className="provider-table-wrap"><table className="provider-table min-w-[760px] table-fixed" aria-label="Change queue">
        <colgroup><col style={{ width: '34%' }} /><col style={{ width: '18%' }} /><col style={{ width: '23%' }} /><col style={{ width: '15%' }} /><col style={{ width: '10%' }} /></colgroup>
        <thead><tr><th>Change</th><th>Source</th><th>Impact</th><th>State</th><th><span className="sr-only">Actions</span></th></tr></thead>
        <tbody>{shown.map(row => <tr key={row.key}>
          <td className="font-semibold">{row.name}<small title={row.summary}><span className="line-clamp-2">{row.summary}</span></small></td>
          <td>{row.source}<small>{offering.name}</small></td><td>{row.impact}</td>
          <td><ProviderBadge tone={row.pending ? 'attention' : 'neutral'}>{row.state}</ProviderBadge></td>
          <td>{row.href ? <Link className="provider-secondary" aria-label={`${row.action} ${row.name}`} to={row.href}>{row.action}</Link>
            : <button type="button" className="provider-secondary" aria-label={`Inspect ${row.name}`} onClick={() => setInspected(row.inspect ?? null)}>Inspect</button>}</td>
        </tr>)}</tbody>
      </table></div>
      {!filtered.length && !unknown && <p className="mt-4 text-sm">No records match this queue view. Historical reviews and source availability remain separate.</p>}
      {unknown && <p className="mt-4 text-xs">Some records are loading or unavailable. Visible records and metrics are not a complete assessment.</p>}
      {filtered.length > 15 && <Pager page={page} pageSize={15} total={filtered.length} onPage={setPage} />}
    </section>
    {inspected && <SetupDialog title="Inspect monitoring source" busy={false} onClose={() => setInspected(null)}
      description="Read-only recorded source facts; not live connector health or an authorization decision.">
      <h3 className="font-semibold">{inspected.name}</h3>
      <dl className="my-4 grid gap-2 text-sm"><dt>Signal</dt><dd>{inspected.signal}</dd><dt>Availability</dt><dd>{inspected.collectionHealth}</dd>
        <dt>Source revision</dt><dd className="break-all">{inspected.sourceRevision}</dd><dt>Recorded value</dt><dd>{inspected.value ?? 'Unavailable'}</dd>
        <dt>Source timestamp</dt><dd>{inspected.sourceTimestamp ?? 'Not recorded'}</dd></dl>
      <Link className="provider-secondary" to={`/provider-changes?${new URLSearchParams({ offeringId: offering.offeringId, tab: 'monitoring' })}`}>Open this offering's monitoring</Link>
    </SetupDialog>}
  </>;
}
