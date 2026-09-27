import { useState, type ReactNode } from 'react';
import PageLayout from '../../components/layout/PageLayout';
import WorkspacePageHeader from '../../components/layout/WorkspacePageHeader';
import { Link, useLocation } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import { Pager, Status, useRemote } from '../workspace-operations/workspaceUi';
import * as api from '../provider-authorizations/api';
import type { Offering, OfferingOverviewData } from '../provider-authorizations/types';
import ProviderMonitoringPage from '../provider-authorizations/ProviderMonitoringPage';
import { scopeLabel } from '../provider-authorizations/scopes';
import { ProviderAdministration } from '../provider-authorizations/ProviderAdministration';
import { publishedReleaseLabel, sourceReviewLabel } from '../provider-authorizations/providerReadModels';
import { offeringEnvironments } from '../provider-authorizations/scopes';

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
  const location = useLocation();
  const monitoring = view === 'changes' && new URLSearchParams(location.search).get('tab') === 'monitoring';
  if (session?.target.kind !== 'csp' || !session.workspace.permissions.canAccessCsp) {
    return <p role="alert" className="p-6 text-red-700">Provider workspace access is required.</p>;
  }
  return <PageLayout title="Provider workspace">
    <div className="mx-auto w-full max-w-[1476px]">
      <p className="mb-5 text-xs text-slate-500">Provider / {view === 'overview' ? 'Overview' : view === 'changes' ? 'Changes' : view === 'missions' ? 'Mission systems' : 'Administration'}</p>
      {view === 'changes' && <nav aria-label="Provider change tasks" className="mb-6 flex gap-5 border-b border-slate-200 text-sm">
        <Link to="/provider-changes" aria-current={!monitoring ? 'page' : undefined}
          className={`border-b-2 py-3 ${!monitoring ? 'border-indigo-600 text-indigo-700' : 'border-transparent text-slate-500'}`}>Provider impact reviews</Link>
        <Link to="/provider-changes?tab=monitoring" aria-current={monitoring ? 'page' : undefined}
          className={`border-b-2 py-3 ${monitoring ? 'border-indigo-600 text-indigo-700' : 'border-transparent text-slate-500'}`}>Service monitoring</Link>
      </nav>}
      {view === 'administration' ? <Administration /> : monitoring ? <ProviderMonitoringPage /> : <OfferingWorkspace view={view} />}
    </div>
  </PageLayout>;
}

function OfferingWorkspace({ view }: { view: 'overview' | 'changes' | 'missions' }) {
  const { search } = useLocation();
  const [page, setPage] = useState(() => queryPage(search, 'offeringPage'));
  const [selectedId, setSelectedId] = useState(() => new URLSearchParams(search).get('offeringId') ?? '');
  const offerings = useRemote(signal => api.listOfferings(page, '', signal), [page]);
  const selected = selectedId ? offerings.data?.items.find(item => item.offeringId === selectedId) : offerings.data?.items[0];
  return <>
    <WorkspacePageHeader eyebrow="Provider operations" title={view === 'overview' ? 'Your provider workspace' : view === 'missions' ? 'Mission systems' : 'Changes requiring attention'}
      description={view === 'overview'
        ? 'Maintain the services and evidence Mission Owners use to prepare and sustain their ATO packages.'
        : view === 'missions' ? 'See exactly which service scopes and releases your customers use.'
          : 'Understand service changes, affected customers, and the action each owner needs to take.'}
      actions={selected && <Link className={primary} to={api.authorizationHref(selected.offeringId, view === 'overview' ? 'packages' : view === 'missions' ? 'inherited-coverage?task=allocations' : 'impact')}>
        {view === 'overview' ? 'Review source material' : view === 'missions' ? 'Assign service scope' : 'Review change impact'}
      </Link>} />
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
          : view === 'missions' ? <MissionSystems key={selected.offeringId} offering={selected} offeringPage={page} />
            : <Changes key={selected.offeringId} offering={selected} />}
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

function MissionSystems({ offering, offeringPage }: { offering: Offering; offeringPage: number }) {
  const { search } = useLocation();
  const [page, setPage] = useState(() => {
    const selectedFromUrl = new URLSearchParams(search).get('offeringId');
    return !selectedFromUrl || selectedFromUrl === offering.offeringId ? queryPage(search, 'missionPage') : 1;
  });
  const data = useRemote(signal => api.getBoundaryOverview(offering.offeringId, 1, page, signal), [offering.offeringId, page]);
  return <section className={panel}>
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <h2 className="font-semibold">Service relationships</h2>
      <Link to="/provider-oversight/systems" className={action}>Cross-organization oversight</Link>
    </div>
    <p className="mb-4 text-xs text-slate-500">Allocation, Mission Owner association, and capability adoption are distinct. An assigned service does not establish an authorization.</p>
    <Status loading={data.loading} error={data.error} retry={data.retry} />
    {data.data && <>
      {data.data.missionSystems.items.length === 0 ? <p className="text-sm text-slate-500">No mission systems are assigned to this offering.</p>
        : <div className="overflow-x-auto"><table className="w-full text-left text-xs">
          <thead className="bg-slate-50 text-[10px] uppercase tracking-wide text-slate-500 dark:bg-gray-800">
            <tr><th className="p-3">Mission system</th><th className="p-3">Association</th><th className="p-3">Applied capabilities</th><th className="p-3">Assigned scope</th><th className="p-3"><span className="sr-only">Actions</span></th></tr>
          </thead>
          <tbody>{data.data.missionSystems.items.map(system => <tr key={system.assignmentId} className="border-b border-slate-100 dark:border-gray-800">
            <td className="p-3 font-semibold">{system.systemName ?? system.systemId}</td>
            <td className="p-3">{system.associated ? system.relationshipState : 'Association pending'}</td>
            <td className="p-3">{system.adoptedCapabilityCount}</td>
            <td className="p-3">{system.assignedScopes.length ? <ul className="space-y-1">
              {system.assignedScopes.map((scope, index) => <li key={`${scopeLabel(scope)}:${index}`} className="max-w-sm break-all">{scopeLabel(scope)}</li>)}
            </ul> : 'No scope recorded'}</td>
            <td className="p-3"><Link className={action} aria-label={`View relationship for ${system.systemName ?? system.systemId}`}
              to={api.authorizationHref(offering.offeringId, `missions/${encodeURIComponent(system.assignmentId)}?missionPage=${page}&offeringPage=${offeringPage}`)}>View relationship</Link></td>
          </tr>)}</tbody>
        </table></div>}
      <Pager {...data.data.missionSystems} onPage={setPage} />
    </>}
  </section>;
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

function Changes({ offering }: { offering: Offering }) {
  const [page, setPage] = useState(1);
  const reviews = useRemote(signal => api.listImpactReviews(offering.offeringId, page, signal), [offering.offeringId, page]);
  return <section className={panel}>
    <h2 className="mb-3 font-semibold">Retained impact reviews</h2>
    <p className="mb-4 text-xs text-slate-500">Provider review and Mission Owner disposition are separate. These records do not establish continuous monitoring health.</p>
    <Status loading={reviews.loading} error={reviews.error} retry={reviews.retry} />
    {reviews.data && <>
      {reviews.data.items.length === 0 && <p className="text-sm text-slate-500">No impact reviews are recorded for this offering.</p>}
      {reviews.data.items.map(review => <div key={review.reviewId} className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 py-4 dark:border-gray-800">
        <Link to={`${api.authorizationHref(offering.offeringId, 'impact')}?reviewId=${encodeURIComponent(review.reviewId)}`} className="text-sm font-semibold text-indigo-700 dark:text-indigo-300">
          {review.title || `Impact review ${review.reviewId}`}
        </Link>
        <span className="rounded bg-slate-100 px-2 py-1 text-xs text-slate-600 dark:bg-gray-800 dark:text-slate-300">{review.stale ? 'Refresh review required' : review.disposition}</span>
      </div>)}
      <Pager {...reviews.data} onPage={setPage} />
    </>}
  </section>;
}

function Administration() {
  return <>
    <WorkspacePageHeader eyebrow="Provider operations" title="Provider administration" description="Manage the people and connections used to maintain provider records." />
    <ProviderAdministration />
    <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_290px]">
      <section className={panel}>
        <h2 className="mb-3 font-semibold">Workspace administration</h2>
        <Task title="Organizations and memberships" description="Use explicit membership and scoped role assignments. Directory lookup alone does not grant access."
          to="/organizations" action="Manage organizations" />
        <Task title="Provider setup" description="Review persisted provider identity, contacts, classification and source receipts."
          to="/onboarding/csp" action="Review provider setup" />
        <Task title="Audit history" description="Inspect retained access, review and publication records."
          to="/audit" action="View audit history" />
      </section>
      <Support title="Authority stays explicit"><p>Provider administration does not grant mission authorship, customer responsibility acceptance, or an authorization decision.</p></Support>
    </div>
  </>;
}
