import { useState, type ReactNode } from 'react';
import PageLayout from '../../components/layout/PageLayout';
import WorkspacePageHeader from '../../components/layout/WorkspacePageHeader';
import { Link, useLocation } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import { Pager, Status, useRemote } from '../workspace-operations/workspaceUi';
import * as api from '../provider-authorizations/api';
import type { Offering, OfferingBoundaryMission, OfferingOverviewData } from '../provider-authorizations/types';
import ProviderChangesPage from './ProviderChangesPage';
import { ProviderAdministration } from '../provider-authorizations/ProviderAdministration';
import { publishedReleaseLabel, sourceReviewLabel } from '../provider-authorizations/providerReadModels';
import { offeringEnvironments } from '../provider-authorizations/scopes';
import { readAllPages } from '../provider-authorizations/providerReadModels';
import { ProviderBadge } from '../provider-authorizations/ProviderPresentation';
import SetupDialog from '../workspace-operations/SetupDialog';
import { allocationScopeName, ProviderAllocationForm } from '../provider-authorizations/ProviderAllocationForm';
import { MissionReleaseSummary, ProviderMissionHandoff } from '../provider-authorizations/ProviderMissionHandoff';

type View = 'overview' | 'changes' | 'missions' | 'administration';
const panel = 'min-w-0 rounded-[10px] border border-slate-200 bg-white p-[22px] dark:border-gray-700 dark:bg-gray-900';
const action = 'inline-flex rounded-[7px] border border-slate-200 bg-white px-3 py-2 text-xs font-semibold text-indigo-700 hover:bg-indigo-50 dark:border-gray-700 dark:bg-gray-900 dark:text-indigo-300';
const primary = 'inline-flex rounded-[7px] bg-[#5143d7] px-4 py-2.5 text-xs font-semibold text-white hover:bg-indigo-700';
const queryPage = (search: string, name: string) => {
  const value = Number(new URLSearchParams(search).get(name));
  return Number.isSafeInteger(value) && value > 0 ? value : 1;
};

export default function ProviderWorkspacePage({ view = 'overview' }: { view?: View }) {
  const session = useWorkspaceSession();
  if (session?.target.kind !== 'csp' || !session.workspace.permissions.canAccessCsp) {
    return <p role="alert" className="p-6 text-red-700">Provider workspace access is required.</p>;
  }
  return <PageLayout title="Provider workspace">
    <div className={`mx-auto w-full max-w-[1476px]${view === 'missions' ? ' provider-workspace' : ''}`}>
      <p className="mb-5 text-xs text-slate-500">Provider / {view === 'overview' ? 'Overview' : view === 'changes' ? 'Changes' : view === 'missions' ? 'Mission systems' : 'Administration'}</p>
      {view === 'administration' ? <Administration /> : view === 'changes' ? <ProviderChangesPage /> : <OfferingWorkspace view={view} />}
    </div>
  </PageLayout>;
}

function OfferingWorkspace({ view }: { view: 'overview' | 'missions' }) {
  const { search } = useLocation();
  const [page, setPage] = useState(() => queryPage(search, 'offeringPage'));
  const [selectedId, setSelectedId] = useState(() => new URLSearchParams(search).get('offeringId') ?? '');
  const [assignOpen, setAssignOpen] = useState(false);
  const [assignPending, setAssignPending] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const [notice, setNotice] = useState('');
  const offerings = useRemote(signal => api.listOfferings(page, '', signal), [page]);
  const selected = selectedId ? offerings.data?.items.find(item => item.offeringId === selectedId) : offerings.data?.items[0];
  return <>
    <WorkspacePageHeader eyebrow="Provider operations" title={view === 'overview' ? 'Your provider workspace' : 'Mission systems'}
      description={view === 'overview'
        ? 'Maintain the services and evidence Mission Owners use to prepare and sustain their ATO packages.'
        : 'See exactly which service scopes and releases your customers use.'}
      actions={selected && (view === 'missions' ? <button className="provider-primary" onClick={() => { setNotice(''); setAssignOpen(true); }}>Assign service scope</button>
        : <Link className={primary} to={api.authorizationHref(selected.offeringId, view === 'overview' ? 'packages' : 'impact')}>
          {view === 'overview' ? 'Review source material' : 'Review change impact'}
        </Link>)} />
    <Status loading={offerings.loading} error={offerings.error} retry={offerings.retry} />
    {offerings.data && <>
      {!selected ? <section className={panel}>
        <h2 className="mb-2 font-semibold">{selectedId ? 'Selected offering unavailable on this page' : 'No service offerings recorded'}</h2>
        <p className="mb-4 text-sm text-slate-500">{selectedId ? 'No other service was selected automatically. Choose from the current offering page or change pages.' : 'Define a service before reviewing its sources, releases, or customer relationships.'}</p>
        {selectedId ? <button className={action} onClick={() => setSelectedId('')}>Choose from current offering page</button>
          : <Link to="/authorizations/create" className={primary}>Create offering</Link>}
      </section> : <>
        {(offerings.data.total > 1 || view !== 'overview') && <label className="mb-5 block max-w-md text-xs text-slate-600 dark:text-slate-300">Service offering
          <select value={selected.offeringId} onChange={event => setSelectedId(event.target.value)}
            className="mt-2 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm text-slate-800 dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100">
            {offerings.data.items.map(item => <option key={item.offeringId} value={item.offeringId}>{item.name}</option>)}
          </select>
        </label>}
        {view === 'overview' ? <OfferingOverview key={selected.offeringId} offering={selected}>
          <OfferingTable offerings={offerings.data.items} />
        </OfferingOverview>
          : <>
            {notice && <p role="status" className="provider-banner">{notice}</p>}
            <MissionSystems key={selected.offeringId} offering={selected} offeringPage={page} refresh={refresh} />
            {assignOpen && <SetupDialog title="Assign service scope" busy={assignPending} onClose={() => setAssignOpen(false)}
              description="Select a named customer, mission system and permitted service scope. This does not provision cloud access.">
              <ProviderAllocationForm key={selected.offeringId} offering={selected} onPendingChange={setAssignPending} onSaved={() => {
                setAssignOpen(false); setRefresh(value => value + 1); setNotice('Service assignment recorded. Mission association, capability adoption and responsibility review remain separate.');
              }} />
              <div className="mt-4 flex justify-end"><button type="button" className="provider-secondary" disabled={assignPending} onClick={() => setAssignOpen(false)}>Cancel</button></div>
            </SetupDialog>}
          </>}
      </>}
      {offerings.data.total > offerings.data.pageSize && <Pager {...offerings.data} onPage={next => { setPage(next); setSelectedId(''); }} />}
    </>}
  </>;
}

function OfferingTable({ offerings }: { offerings: Offering[] }) {
  return <section className={panel}>
          <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
            <h2 className="font-semibold">Service offerings</h2><Link to="/authorizations" className={action}>View all offerings</Link>
          </div>
          <div className="overflow-x-auto"><table className="w-full text-left text-xs">
            <thead className="bg-slate-50 text-[10px] uppercase tracking-wide text-slate-500 dark:bg-gray-800">
              <tr><th className="p-3">Offering</th><th className="p-3">Published release</th><th className="p-3">Source status</th><th className="p-3"><span className="sr-only">Actions</span></th></tr>
            </thead>
            <tbody>{offerings.map(item => <tr key={item.offeringId} className="border-b border-slate-100 last:border-0 dark:border-gray-800">
              <td className="p-3 font-semibold">{item.name}<small className="mt-1 block font-normal text-slate-500">{item.environments.map(environment => offeringEnvironments[environment]).join(' · ')}</small></td>
              <OfferingSourceCells offering={item} />
              <td className="p-3"><Link className={action} to={api.authorizationHref(item.offeringId)}>Open offering<span className="sr-only"> {item.name}</span></Link></td>
            </tr>)}</tbody>
          </table></div>
        </section>;
}

function OfferingSourceCells({ offering }: { offering: Offering }) {
  const remote = useRemote(signal => api.getOfferingOverview(offering.offeringId, 1, 1, signal), [offering.offeringId, offering.revision]);
  return <>
    <td className="p-3">{remote.data ? remote.data.capabilities.published > 0
      ? <>{publishedReleaseLabel(remote.data) ?? `${remote.data.capabilities.published} capabilities published`}<small className="mt-1 block text-slate-500">{publishedReleaseLabel(remote.data)
        ? `${remote.data.capabilities.published} capabilities published` : 'Release version not reported'}</small></>
      : 'None published' : remote.loading ? 'Checking…' : 'Unavailable'}</td>
    <td className="p-3">{remote.data ? sourceReviewLabel(remote.data) : remote.loading ? 'Checking…' : 'Unavailable'}
      {remote.error && <button className="mt-1 block text-indigo-700 underline" onClick={remote.retry}>Retry source status</button>}
    </td>
  </>;
}

function MissionSystems({ offering, offeringPage, refresh }: { offering: Offering; offeringPage: number; refresh: number }) {
  const { search } = useLocation();
  const [page, setPage] = useState(() => {
    const selectedFromUrl = new URLSearchParams(search).get('offeringId');
    return !selectedFromUrl || selectedFromUrl === offering.offeringId ? queryPage(search, 'missionPage') : 1;
  });
  const [query, setQuery] = useState('');
  const [handoff, setHandoff] = useState<OfferingBoundaryMission | null>(null);
  const data = useRemote(async signal => {
    const first = (await api.getBoundaryOverview(offering.offeringId, 1, page, signal)).missionSystems;
    if (!query.trim()) return first;
    const items = await readAllPages(async next => next === page ? first
      : (await api.getBoundaryOverview(offering.offeringId, 1, next, signal)).missionSystems, signal);
    return { ...first, page: 1, total: items.length, items };
  }, [offering.offeringId, page, query, refresh]);
  const needle = query.trim().toLocaleLowerCase();
  const items = data.data?.items.filter(item => !needle || `${item.systemName ?? item.systemId} ${item.targetTenantName ?? ''}`.toLocaleLowerCase().includes(needle)) ?? [];
  return <>
  <section className="provider-panel">
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <h2 className="font-semibold">Customer service relationships</h2>
      <Link to="/provider-oversight/systems" className={action}>Cross-organization oversight</Link>
    </div>
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <input type="search" aria-label="Search mission system or organization" placeholder="Search mission system or organization"
        className="w-full max-w-[380px] rounded-md border border-slate-300 bg-white px-3 py-2 text-sm" value={query}
        onChange={event => { setQuery(event.target.value); setPage(1); }} />
      {data.data && <ProviderBadge tone="neutral">{needle ? items.length : data.data.total} service relationship{(needle ? items.length : data.data.total) === 1 ? '' : 's'}</ProviderBadge>}
    </div>
    <Status loading={data.loading} error={data.error} retry={data.retry} />
    {data.data && <>
      {items.length === 0 ? <p className="text-sm text-slate-500">{needle ? 'No mission systems or organizations match this search.' : 'No mission systems are assigned to this offering.'}</p>
        : <div className="provider-table-wrap"><table className="provider-table min-w-[780px]" aria-label="Customer service relationships">
          <thead className="bg-slate-50 text-[10px] uppercase tracking-wide text-slate-500 dark:bg-gray-800">
            <tr><th>Mission system</th><th>Service / scope</th><th>Association</th><th>Capability release</th><th><span className="sr-only">Actions</span></th></tr>
          </thead>
          <tbody>{items.map(system => <tr key={system.assignmentId}>
            <td className="font-semibold">{system.systemName ?? system.systemId}<small>{system.targetTenantName ?? 'Organization name unavailable'}</small></td>
            <td>{offering.name}<small>{system.assignedScopes.length ? system.assignedScopes.map(allocationScopeName).join(', ') : 'No scope recorded'}</small></td>
            <td><span title={`Recorded relationship state: ${system.relationshipState}`}><ProviderBadge tone={!system.associated || system.relationshipState === 'ReviewRequired' ? 'attention' : 'success'}>
              {!system.associated ? 'Awaiting MO' : system.relationshipState === 'ReviewRequired' ? 'Review required' : 'Associated'}
            </ProviderBadge></span></td>
            <td><MissionReleaseSummary relationship={system} /></td>
            <td><Link className="provider-secondary" aria-label={`View relationship for ${system.systemName ?? system.systemId}`}
              to={api.authorizationHref(offering.offeringId, `missions/${encodeURIComponent(system.assignmentId)}?missionPage=${page}&offeringPage=${offeringPage}`)}>View relationship</Link></td>
          </tr>)}</tbody>
        </table></div>}
      {!needle && data.data.total > data.data.pageSize && <Pager {...data.data} onPage={setPage} />}
    </>}
  </section>
  <div className="provider-banner provider-banner-release mt-5">
    <div><strong>Association, adoption, and responsibilities have different states.</strong><p>A hosting allocation does not apply capabilities or accept customer duties.</p></div>
    <button type="button" className="provider-secondary" disabled={!items.length} onClick={() => setHandoff(items[0] ?? null)}>Preview Systems handoff</button>
  </div>
  {handoff && <SetupDialog title="Preview Systems handoff" busy={false} onClose={() => setHandoff(null)}
    description="Read-only provider-side preview. No customer workspace access or role change occurs.">
    <label className="mb-4 block text-sm">Mission service relationship<select className="mt-1 block w-full rounded border p-2" value={handoff.assignmentId}
      onChange={event => setHandoff(items.find(item => item.assignmentId === event.target.value) ?? null)}>
      {items.map(item => <option key={item.assignmentId} value={item.assignmentId}>{item.systemName ?? item.systemId} · {item.targetTenantName ?? 'Organization unavailable'}</option>)}
    </select></label>
    <ProviderMissionHandoff offeringName={offering.name} relationship={handoff} />
  </SetupDialog>}
  </>;
}

function Metric({ label, value, caption }: { label: string; value: number | string; caption: string }) {
  return <div className="rounded-[9px] border border-slate-200 bg-white p-4 dark:border-gray-700 dark:bg-gray-900">
    <span className="text-[11px] text-slate-500">{label}</span>
    <strong aria-label={label} className="my-1 block text-2xl font-semibold tracking-tight">{value}</strong>
    <small className="text-[11px] text-slate-500">{caption}</small>
  </div>;
}

function OfferingOverview({ offering, children }: { offering: Offering; children: ReactNode }) {
  const overview = useRemote(signal => api.getOfferingOverview(offering.offeringId, 1, 1, signal), [offering.offeringId]);
  return <>
    <Status loading={overview.loading} error={overview.error} retry={overview.retry} />
    {overview.data && <OverviewContent offering={offering} data={overview.data}>{children}</OverviewContent>}
  </>;
}

function OverviewContent({ offering, data, children }: { offering: Offering; data: OfferingOverviewData; children: ReactNode }) {
  return <>
    <div className="mb-6 grid grid-cols-2 gap-3 xl:grid-cols-4">
      <Metric label="Published capabilities" value={data.capabilities.published} caption={offering.name} />
      <Metric label="Needs your review" value={data.packages.awaitingReview} caption="Source candidates awaiting review" />
      <Metric label="Mission systems" value={data.hosting.associatedSystemCount} caption="Associated with this offering" />
      <Metric label="Customer actions" value={data.customerActionCount ?? 'Unavailable'}
        caption={data.customerActionCount == null ? 'Customer review totals could not be verified' : 'Relationship and narrative reviews awaiting action'} />
    </div>
    <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_290px]">
      <div className="min-w-0 space-y-5"><section className={panel}>
        <h2 className="mb-3 text-base font-semibold">Focus for today</h2>
        <Task title="Review source material" description={`${data.packages.needsAttention} packages need attention; ${data.packages.processing} are processing.`}
          to={api.authorizationHref(offering.offeringId, 'packages')} action={`Review ${offering.name}`} />
        <Task title="Review reusable implementations" description={`${data.capabilities.awaitingApproval} capabilities await approval. Publication does not apply them to a mission.`}
          to="/security-capabilities" action="Review capabilities" />
        <Task title="Review customer service associations" description={`${data.hosting.assignmentCount} service assignments; ${data.hosting.associatedSystemCount} associated systems.`}
          to={api.authorizationHref(offering.offeringId, 'inherited-coverage')} action="View service scope" />
      </section>{children}</div>
      <aside className="space-y-6">
        <Support title="Provider perspective"><p>Maintain what your service supplies. Mission Owners complete and review what their individual systems require.</p></Support>
        <Support title="Contributes to the system package"><ol className="list-decimal space-y-1 pl-4">
          <li>Service and boundary descriptions</li><li>Reusable control implementations</li><li>Evidence and responsibilities</li><li>Ongoing change notices</li>
        </ol></Support>
        <Support title="Retained activity"><Link to="/audit" className="text-indigo-700 dark:text-indigo-300">View audit history</Link></Support>
      </aside>
    </div>
  </>;
}

function Task({ title, description, to, action: label }: { title: string; description: string; to: string; action: string }) {
  return <div className="flex flex-col justify-between gap-3 border-b border-slate-100 py-4 last:border-0 dark:border-gray-800 sm:flex-row sm:items-center">
    <div><h3 className="text-[13px] font-semibold">{title}</h3><p className="mt-1 text-xs text-slate-500">{description}</p></div>
    <Link to={to} className={`${action} shrink-0 self-start`}>{label}</Link>
  </div>;
}

function Support({ title, children }: { title: string; children: ReactNode }) {
  return <section className="border-l-2 border-indigo-200 pl-4 text-xs leading-relaxed text-slate-500 dark:border-indigo-800">
    <h2 className="mb-2 text-[13px] font-semibold text-slate-700 dark:text-slate-200">{title}</h2>{children}
  </section>;
}

function Administration() {
  return <ProviderAdministration />;
}
