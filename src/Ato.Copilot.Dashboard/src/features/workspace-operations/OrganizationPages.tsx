import { useEffect, useState } from 'react';
import { ArrowLeft, Plus, Search, ShieldCheck } from 'lucide-react';
import PageHero from '../../components/layout/PageHero';
import PageLayout from '../../components/layout/PageLayout';
import SupportWorkspaceButton from '../workspaces/SupportWorkspaceButton';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import { Link } from '../workspaces/workspaceNavigation';
import * as api from './api';
import type { OrganizationCatalogItem, OrganizationDetail } from './types';
import {
  errorClass, inputClass, moveTabFocus, Pager, secondaryButtonClass, Status,
  useQueryState, useRemote, warningClass,
} from './workspaceUi';

const cardClass = 'rounded-lg border border-slate-200 bg-white p-5 shadow-sm dark:border-gray-700 dark:bg-gray-900';
const mutedClass = 'text-slate-500 dark:text-gray-400';
const linkClass = 'font-medium text-indigo-700 hover:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-600 dark:text-indigo-300';
const heroActionClass = 'inline-flex items-center justify-center gap-2 rounded-md border border-white bg-white px-4 py-2 text-sm font-medium text-indigo-700 shadow-sm hover:bg-indigo-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white';
const tableHeadClass = 'border-b border-slate-200 text-left text-xs font-medium text-slate-500 dark:border-gray-700 dark:text-gray-400';

function Badge({ children, tone = 'neutral' }: {
  children: string; tone?: 'neutral' | 'green' | 'amber';
}) {
  const colors = {
    neutral: 'border-slate-200 bg-slate-50 text-slate-600 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-300',
    green: 'border-emerald-100 bg-emerald-50 text-emerald-700 dark:border-emerald-900 dark:bg-emerald-950 dark:text-emerald-200',
    amber: 'border-amber-100 bg-amber-50 text-amber-800 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200',
  };
  return <span className={`inline-flex rounded border px-2 py-0.5 text-xs font-medium ${colors[tone]}`}>{children}</span>;
}

function accountTone(lifecycle: string) {
  return lifecycle.toLowerCase() === 'active' ? 'green' : 'neutral';
}

function onboardingLabel(value: string) {
  return ({ Pending: 'Pending', InWizard: 'In progress', Active: 'Complete' } as Record<string, string>)[value] ?? value;
}

function reviewLabel(value: string) {
  return ({ pending: 'Awaiting source review', awaitingreview: 'Awaiting source review',
    completed: 'Review complete', current: 'Review complete', notrequired: 'No source review required',
    none: 'No source review required' } as Record<string, string>)[value.toLowerCase()] ?? value;
}

function awaitingReview(value: string) {
  return ['pending', 'awaitingreview'].includes(value.toLowerCase());
}

function initials(name: string) {
  return name.trim().split(/\s+/).slice(0, 2).map(word => word[0] ?? '').join('').toUpperCase();
}

function FilterSelect({ name, label, value, options, onChange }: {
  name: string; label: string; value: string; options: [string, string][]; onChange: (value: string) => void;
}) {
  return <label className="grid gap-1 text-xs font-medium text-slate-600 dark:text-gray-300">{label}
    <select aria-label={name} className={inputClass} value={value} onChange={event => onChange(event.target.value)}>
      {!options.some(([key]) => key === value) && <option value={value}>{value}</option>}
      {options.map(([key, text]) => <option key={key} value={key}>{text}</option>)}
    </select>
  </label>;
}

function OrganizationFootnote() {
  return <footer className={`mt-7 border-t border-slate-100 pt-4 text-xs dark:border-gray-800 ${mutedClass}`}>
    SPIN · Security Posture Intelligence Navigator
  </footer>;
}

export function Organizations() {
  const session = useWorkspaceSession();
  if (session?.target.kind !== 'csp' || !session.workspace.permissions.canAccessCsp) {
    return <p role="alert" className={errorClass}>Provider access is required.</p>;
  }
  return <OrganizationsContent />;
}

function OrganizationsContent() {
  const { params, set } = useQueryState();
  const requestedPage = Number(params.get('page') ?? 1);
  const query = {
    page: Number.isSafeInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1,
    pageSize: 25, search: params.get('search') || undefined,
    lifecycle: params.get('lifecycle') || undefined, onboarding: params.get('onboarding') || undefined,
    review: params.get('review') || undefined,
  };
  const state = useRemote(signal => api.listOrganizations(query, signal), Object.values(query));
  const [search, setSearch] = useState(query.search ?? '');
  useEffect(() => setSearch(params.get('search') ?? ''), [params]);
  const partial = !!state.data?.aggregateState && state.data.aggregateState !== 'Available';
  const items = state.data?.items ?? [];
  const summaries = [
    { label: 'Organizations total', caption: query.search || query.lifecycle || query.onboarding || query.review
      ? 'Matching organizations' : 'Organizations', value: partial ? 'Unavailable' : state.data?.total },
    { label: 'Active on this page', caption: 'Active · this page',
      value: items.filter(item => item.lifecycle.toLowerCase() === 'active').length },
    { label: 'Awaiting source review on this page', caption: 'Awaiting source review · this page',
      value: items.filter(item => awaitingReview(item.reviewState)).length },
    { label: 'Onboarding on this page', caption: 'Onboarding · this page',
      value: items.filter(item => ['pending', 'inwizard'].includes(item.onboarding.toLowerCase())).length },
  ];

  return <PageLayout title="Organizations">
    <PageHero eyebrow="Provider oversight" title="Organizations"
      description="Manage hosted organizations, track adoption, and coordinate provider changes."
      actions={<Link className={heroActionClass} to="/organizations/new"><Plus size={16} aria-hidden="true" />Add organization</Link>} />
    <div className="space-y-5 text-slate-800 dark:text-gray-100">
      <Status loading={state.loading} error={state.error} retry={state.retry} />
      {partial && <p role="alert" className={warningClass}>
        Organization totals are unavailable or partial: {state.data?.aggregateState}
      </p>}
      {state.data && <section aria-label="Organization summary" className="border-b border-slate-200 pb-5 dark:border-gray-700">
        <dl className="flex flex-wrap gap-x-9 gap-y-4">
          {summaries.map(item => <div key={item.label} aria-label={item.label} className="flex items-baseline gap-2">
            <dd className="order-first text-2xl font-semibold">{item.value}</dd>
            <dt className={`text-xs ${mutedClass}`}>{item.caption}</dt>
          </div>)}
        </dl>
      </section>}
      <form className="flex flex-wrap items-end justify-between gap-3" onSubmit={event => {
        event.preventDefault(); set({ search: search.trim(), page: 1 });
      }}>
        <div className="flex w-full gap-2 sm:w-auto sm:min-w-80">
          <label className="relative min-w-0 flex-1">
            <span className="sr-only">Search</span>
            <Search size={16} aria-hidden="true" className={`pointer-events-none absolute left-3 top-3 ${mutedClass}`} />
            <input className={`${inputClass} w-full pl-9`} value={search} placeholder="Search by organization name"
              onChange={event => setSearch(event.target.value)} />
          </label>
          <button type="submit" className={secondaryButtonClass}>Apply</button>
        </div>
        <div className="flex flex-wrap items-end gap-3">
          <FilterSelect name="lifecycle" label="Account status" value={params.get('lifecycle') ?? ''}
            options={[['', 'All organizations'], ['Active', 'Active'], ['Suspended', 'Suspended'], ['Disabled', 'Disabled']]}
            onChange={value => set({ lifecycle: value, page: 1 })} />
          <details open={!!query.onboarding || !!query.review} className="text-sm">
            <summary className={`cursor-pointer py-2 ${linkClass}`}>More filters</summary>
            <div className="mt-2 flex flex-wrap gap-3">
              <FilterSelect name="onboarding" label="Onboarding" value={params.get('onboarding') ?? ''}
                options={[['', 'All onboarding states'], ['Pending', 'Pending'], ['InWizard', 'In progress'], ['Active', 'Complete']]}
                onChange={value => set({ onboarding: value, page: 1 })} />
              <FilterSelect name="review" label="Source review" value={params.get('review') ?? ''}
                options={[['', 'All review states'], ['Pending', 'Awaiting source review'], ['Completed', 'Review complete'], ['NotRequired', 'Not required']]}
                onChange={value => set({ review: value, page: 1 })} />
            </div>
          </details>
        </div>
      </form>
      {state.data && (items.length ? <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white dark:border-gray-700 dark:bg-gray-900">
        <table aria-label="Organizations" className="w-full min-w-[680px] text-left text-sm">
          <thead className="bg-slate-50 dark:bg-gray-950"><tr className={tableHeadClass}>
            {['Organization', 'Account', 'Systems', 'Provider adoption', 'Action'].map(heading =>
              <th key={heading} scope="col" className="px-4 py-3">{heading}</th>)}
          </tr></thead>
          <tbody className="divide-y divide-slate-200 dark:divide-gray-700">
            {items.map(org => <OrganizationRow key={org.id} organization={org} />)}
          </tbody>
        </table>
      </div> : <p className={cardClass}>
        {query.search ? 'No organizations match the current filters.' : 'No organizations are available.'}
      </p>)}
      {state.data && <div className={`text-xs ${mutedClass}`}>
        <div className="flex flex-wrap justify-between gap-2">
          <span>Showing {items.length} {partial ? 'returned organizations' : `of ${state.data.total} organizations`}</span>
          <span>Account status is separate from system authorization.</span>
        </div>
        {partial && <p className="mt-2">Reported pagination totals may be incomplete.</p>}
        <Pager {...state.data} onPage={page => set({ page })} />
      </div>}
    </div>
    <OrganizationFootnote />
  </PageLayout>;
}

function OrganizationRow({ organization: org }: { organization: OrganizationCatalogItem }) {
  const path = `/organizations/${encodeURIComponent(org.id)}`;
  return <tr>
    <td className="max-w-80 px-4 py-5">
      <div className="flex items-center gap-3">
        <span aria-hidden="true" className="grid h-9 w-9 shrink-0 place-items-center rounded-lg bg-indigo-50 text-xs font-semibold text-indigo-700 dark:bg-indigo-950 dark:text-indigo-300">
          {initials(org.displayName)}
        </span>
        <div className="min-w-0">
          <Link className={linkClass} to={path}>{org.displayName}</Link>
          <p className={`mt-1 break-all text-xs ${mutedClass}`}>{org.id}</p>
        </div>
      </div>
    </td>
    <td className="px-4 py-5">
      <Badge tone={accountTone(org.lifecycle)}>{org.lifecycle}</Badge>
      {org.onboarding !== 'Active' && <p className={`mt-1 text-xs ${mutedClass}`}>Onboarding: {onboardingLabel(org.onboarding)}</p>}
    </td>
    <td className="whitespace-nowrap px-4 py-5 text-xs">{org.systemCount} registered</td>
    <td className="px-4 py-5">
      <p className="mb-1 text-xs">{org.distinctAdoptionCount === null ? 'Adoption unavailable'
        : `${org.distinctAdoptionCount} ${org.distinctAdoptionCount === 1 ? 'capability' : 'capabilities'}`}</p>
      <Badge tone={awaitingReview(org.reviewState) ? 'amber' : 'neutral'}>{reviewLabel(org.reviewState)}</Badge>
    </td>
    <td className="px-4 py-5">
      <Link className={`${secondaryButtonClass} inline-flex whitespace-nowrap text-xs`} to={path}>View organization</Link>
    </td>
  </tr>;
}

export function OrganizationDetailView({ tenantId }: { tenantId: string }) {
  const session = useWorkspaceSession();
  if (session?.target.kind !== 'csp' || !session.workspace.permissions.canAccessCsp) {
    return <p role="alert" className={errorClass}>Provider access is required.</p>;
  }
  return <OrganizationDetailContent key={tenantId} tenantId={tenantId} />;
}

function OrganizationDetailContent({ tenantId }: { tenantId: string }) {
  const { params, set } = useQueryState();
  const requestedTab = params.get('tab');
  const tab = requestedTab === 'subscriptions' || requestedTab === 'activity' ? requestedTab : 'overview';
  const state = useRemote(signal => api.getOrganization(tenantId, signal), [tenantId]);
  const provisioning = useRemote(signal => api.getCurrentOrganizationProvisioning(tenantId, signal), [tenantId]);
  const provisioningError = provisioning.error ?? (provisioning.data && !provisioning.data.idempotencyKey
    ? 'The current enrollment operation has no stable recovery key.' : null);
  const detail = state.data;
  const tabs = [
    { id: 'overview', label: 'Overview & systems' },
    { id: 'subscriptions', label: 'Provider subscriptions' },
    { id: 'activity', label: 'Provider activity' },
  ];
  return <PageLayout title={detail?.displayName ?? 'Organization relationship'}>
    <PageHero eyebrow="Provider oversight" title={detail?.displayName ?? 'Organization'}
      description="Provider-facing account, system summaries and subscription status."
      actions={detail && <span className="[&>span>button]:rounded-md [&>span>button]:border-white [&>span>button]:bg-white [&>span>button]:px-4 [&>span>button]:py-2 [&>span>button]:text-sm [&>span>button]:font-medium [&>span>button]:text-indigo-700">
        <SupportWorkspaceButton tenantId={tenantId} tenantName={detail.displayName} />
      </span>} />
    <div className="space-y-5 text-slate-800 dark:text-gray-100">
      <Link className={`${linkClass} inline-flex items-center gap-1.5 text-xs`} to="/organizations">
        <ArrowLeft size={14} aria-hidden="true" />All organizations
      </Link>
      <Status loading={state.loading} error={state.error} retry={state.retry} />
      {detail && <>
        <div role="tablist" aria-label="Organization detail sections"
          className="flex flex-wrap gap-x-6 border-b border-slate-200 dark:border-gray-700" onKeyDown={moveTabFocus}>
          {tabs.map(item => <button type="button" key={item.id} id={`organization-tab-${item.id}`} role="tab"
            aria-selected={tab === item.id} aria-controls={`organization-panel-${item.id}`} tabIndex={tab === item.id ? 0 : -1}
            className={`border-b-2 px-0 py-3 text-sm font-medium focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-600 ${
              tab === item.id ? 'border-indigo-600 text-indigo-700 dark:border-indigo-400 dark:text-indigo-300'
                : 'border-transparent text-slate-500 hover:text-indigo-700 dark:text-gray-400'}`}
            onClick={() => set({ tab: item.id })}>{item.label}</button>)}
        </div>
        <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1.75fr)_minmax(250px,1fr)]">
          <section role="tabpanel" id={`organization-panel-${tab}`} aria-labelledby={`organization-tab-${tab}`}
            tabIndex={0} className="min-w-0 space-y-4">
            {tab === 'overview' && <OrganizationOverview detail={detail} />}
            {tab === 'subscriptions' && <OrganizationSubscriptions detail={detail} />}
            {tab === 'activity' && <OrganizationActivity detail={detail} />}
          </section>
          <aside aria-label="Organization context" className="space-y-4">
            <section className={cardClass}>
              <h2 className="text-base font-semibold">Organization profile</h2>
              <dl className="mt-4 space-y-4 text-xs">
                <div><dt className={mutedClass}>Organization identifier</dt><dd className="mt-1 break-all">{detail.id}</dd></div>
                <div><dt className={mutedClass}>Account status</dt><dd className="mt-1"><Badge tone={accountTone(detail.lifecycle)}>{detail.lifecycle}</Badge></dd></div>
                <div><dt className={mutedClass}>Onboarding</dt><dd className="mt-1">{onboardingLabel(detail.onboarding)}</dd></div>
                <div><dt className={mutedClass}>Organization contact</dt><dd className="mt-1">Not provided in this summary.</dd></div>
                <div><dt className={mutedClass}>Provider relationship</dt><dd className="mt-1">
                  Hosted organization · {detail.subscriptions.filter(item => item.isActive).length} active provider subscriptions
                </dd></div>
              </dl>
              <div className="mt-5">
                <Status loading={provisioning.loading} error={provisioningError} retry={provisioning.retry} />
                {provisioning.data?.idempotencyKey
                  ? <Link className={`${secondaryButtonClass} inline-flex`} to={`/organizations/${encodeURIComponent(tenantId)}/provisioning?key=${encodeURIComponent(provisioning.data.idempotencyKey)}`}>
                      Enrollment status
                    </Link>
                  : !provisioning.loading && !provisioningError
                    ? <Link className={`${secondaryButtonClass} inline-flex`} to={`/organizations/${encodeURIComponent(tenantId)}/provisioning`}>
                        Start enrollment
                      </Link>
                    : null}
              </div>
            </section>
            <section className={cardClass}>
              <h2 className="flex items-center gap-2 text-sm font-semibold"><ShieldCheck size={16} aria-hidden="true" />Support access</h2>
              <p className={`mt-2 text-xs leading-relaxed ${mutedClass}`}>Open a separate, audited session for authorized troubleshooting.</p>
              <div className="my-3 [&>span>button]:w-full [&>span>button]:py-2">
                <SupportWorkspaceButton tenantId={tenantId} tenantName={detail.displayName} />
              </div>
              <p className={`text-xs leading-relaxed ${mutedClass}`}>Viewing this page does not enter the organization workspace.</p>
            </section>
          </aside>
        </div>
      </>}
    </div>
    <OrganizationFootnote />
  </PageLayout>;
}

function OrganizationOverview({ detail }: { detail: OrganizationDetail }) {
  return <>
    <section className={cardClass}>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-base font-semibold">Systems</h2><Badge>Authorized summary view</Badge>
      </div>
      <div className="overflow-x-auto">
        <table aria-label="Organization systems" className="w-full min-w-[360px] text-left text-xs">
          <thead><tr className={`${tableHeadClass} text-[10px] uppercase tracking-wide`}>
            <th scope="col" className="px-2 pb-2">System</th>
            <th scope="col" className="px-2 pb-2">RMF phase</th>
            <th scope="col" className="px-2 pb-2">System authorization</th>
          </tr></thead>
          <tbody className="divide-y divide-slate-200 dark:divide-gray-700">
            {detail.systems.map(system => <tr key={system.id}>
              <td className="px-2 py-3">{system.name}
                {!system.isActive && <span className={`mt-1 block ${mutedClass}`}>Inactive inventory record</span>}</td>
              <td className="px-2 py-3">{system.rmfPhase}</td>
              <td className={`px-2 py-3 ${mutedClass}`}>Not provided</td>
            </tr>)}
            {!detail.systems.length && <tr><td colSpan={3} className="px-2 py-4">No systems are registered for this organization.</td></tr>}
          </tbody>
        </table>
      </div>
      <p className={`mt-4 border-l-4 border-indigo-300 py-1 pl-3 text-xs leading-relaxed dark:border-indigo-500 ${mutedClass}`}>
        Each system retains its own authorization decision. Provider baseline adoption does not grant an ATO.
        Authorization decisions are not included in this provider summary.
      </p>
    </section>
    <section className={cardClass}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-base font-semibold">Provider changes to review</h2><Badge tone="amber">Review details unavailable</Badge>
      </div>
      <p className={`mt-3 text-xs leading-relaxed ${mutedClass}`}>
        This provider summary does not include system-level source review details. The organizations list shows the available organization-level review state.
      </p>
    </section>
  </>;
}

function OrganizationSubscriptions({ detail }: { detail: OrganizationDetail }) {
  return <section className={cardClass}>
    <h2 className="text-base font-semibold">Provider subscriptions</h2>
    <p className={`mt-2 text-xs ${mutedClass}`}>Subscription and confirmed source revision records; these do not imply authorization.</p>
    {detail.subscriptions.length ? <div className="mt-4 overflow-x-auto">
      <table aria-label="Provider subscriptions" className="w-full min-w-[430px] text-left text-xs">
        <thead><tr className={tableHeadClass}>
          {['Capability identifier', 'System', 'Source revision', 'Status'].map(label =>
            <th key={label} scope="col" className="px-2 pb-3">{label}</th>)}
        </tr></thead>
        <tbody className="divide-y divide-slate-200 dark:divide-gray-700">
          {detail.subscriptions.map(subscription => <tr key={subscription.id}>
            <td className="break-all px-2 py-3">{subscription.capabilityId}</td>
            <td className="px-2 py-3">{detail.systems.find(system => system.id === subscription.systemId)?.name ?? subscription.systemId}</td>
            <td className="break-all px-2 py-3">{subscription.sourceRevision ?? 'Not confirmed'}</td>
            <td className="px-2 py-3"><Badge tone={subscription.isActive ? 'green' : 'neutral'}>{subscription.isActive ? 'Active' : 'Inactive'}</Badge></td>
          </tr>)}
        </tbody>
      </table>
    </div> : <p className={`mt-4 text-sm ${mutedClass}`}>No provider subscriptions are available.</p>}
  </section>;
}

function OrganizationActivity({ detail }: { detail: OrganizationDetail }) {
  return <section className={cardClass}>
    <h2 className="text-base font-semibold">Provider activity</h2>
    <p className={`mt-2 text-xs ${mutedClass}`}>Latest activity available in this provider summary (up to 50 records).</p>
    {detail.activity.length ? <ul className="mt-4 divide-y divide-slate-200 dark:divide-gray-700">
      {detail.activity.map((item, index) => <li key={`${item.occurredAt}:${index}`} className="flex flex-wrap items-center justify-between gap-2 py-3 text-xs">
        <div><p className="font-medium">{item.action}</p>
          <time dateTime={item.occurredAt} className={`mt-1 block ${mutedClass}`}>{new Date(item.occurredAt).toLocaleString()}</time>
        </div><Badge>{item.outcome}</Badge>
      </li>)}
    </ul> : <p className={`mt-4 text-sm ${mutedClass}`}>No provider activity is available.</p>}
  </section>;
}
