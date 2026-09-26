import { useState, type ReactNode } from 'react';
import { Link, useLocation } from '../workspaces/workspaceNavigation';
import { buttonClass, secondaryButtonClass, surfaceClass, Pager, useRemote } from '../workspace-operations/workspaceUi';
import { DecisionPanel } from './DecisionPanel';
import { HostingPanel } from './HostingPanel';
import SetupDialog from '../workspace-operations/SetupDialog';
import * as api from './api';
import { getHostingScope, listHostingScopes } from './hostingApi';
import type { Offering } from './types';

type Task = 'hosting' | 'references' | 'capabilities' | 'missions' | 'responsibilities' | 'allocations';
type ReadState = { loading: boolean; error: string | null; retry: () => void };
const muted = 'text-sm text-slate-600 dark:text-slate-300';
const linkStyle = 'text-sm font-medium text-indigo-700 underline dark:text-indigo-300';
const actions: Record<Exclude<Task, 'allocations'>, string> = {
  hosting: 'Configure hosting', references: 'Add reference', capabilities: 'Review capabilities',
  missions: 'View associations', responsibilities: 'Review responsibilities',
};
const taskTitles: Record<Task, string> = {
  hosting: 'Configure Azure hosting', references: 'Add or review Microsoft references',
  capabilities: 'Review offering capabilities', missions: 'Mission system associations',
  responsibilities: 'Review responsibilities', allocations: 'Provider hosting allocation',
};
function TaskCard({ title, description, action, children }: {
  title: string; description: string; action: ReactNode; children: ReactNode;
}) {
  return <section aria-label={title} className={`${surfaceClass} min-w-0 space-y-4 p-5`}>
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0"><h2 className="text-lg font-semibold">{title}</h2><p className={`mt-1 ${muted}`}>{description}</p></div>{action}
    </div>{children}
  </section>;
}
function Availability({ title, state }: { title: string; state: ReadState }) {
  if (state.loading) return <p role="status" className={muted}>Loading {title.toLowerCase()}...</p>;
  if (!state.error) return null;
  return <div role="alert" className="space-y-2 rounded border border-red-300 bg-red-50 p-3 text-sm text-red-900 dark:bg-red-950 dark:text-red-100">
    <p className="font-semibold">{title} unavailable</p>
    <p>The required service could not load this offering&apos;s data. This does not mean no records exist. Dependent actions are unavailable until the read succeeds.</p>
    <button type="button" className="underline" onClick={state.retry}>Retry {title}</button>
    <details><summary className="cursor-pointer">Details</summary><p className="mt-2 break-words">{state.error}</p></details>
  </div>;
}
const ready = (state: ReadState) => !state.loading && !state.error;
const readLabel = (state: ReadState, value: string) => state.loading ? 'Checking...' : state.error ? 'Unavailable' : value;

export function HostingSetupPage({ offering: loadedOffering, onChanged }: { offering: Offering; onChanged: () => void }) {
  const location = useLocation();
  const requestedTask = new URLSearchParams(location.search).get('task');
  const [refreshedOffering, setRefreshedOffering] = useState<Offering | null>(null);
  const offering = refreshedOffering && refreshedOffering.revision > loadedOffering.revision ? refreshedOffering : loadedOffering;
  const [active, setActive] = useState<Task | null>(() =>
    requestedTask === 'hosting' || requestedTask === 'capabilities' || requestedTask === 'missions' ? requestedTask : null);
  const [pending, setPending] = useState(false);
  const [newReference, setNewReference] = useState(true);
  const [capabilityPage, setCapabilityPage] = useState(1);
  const [missionPage, setMissionPage] = useState(1);
  const hosting = useRemote(async signal => offering.currentHostingScopeRevisionId
    ? getHostingScope(offering.offeringId, offering.currentHostingScopeRevisionId, signal)
    : (await listHostingScopes(offering.offeringId, 1, signal), null),
  [offering.offeringId, offering.currentHostingScopeRevisionId, offering.revision]);
  const references = useRemote(signal => api.listMicrosoftReferences(offering.offeringId, 1, signal),
    [offering.offeringId, offering.revision]);
  const boundary = useRemote(signal => offering.currentBoundaryRevisionId
    ? api.getBoundary(offering.offeringId, offering.currentBoundaryRevisionId, signal) : Promise.resolve(null),
  [offering.offeringId, offering.currentBoundaryRevisionId, offering.revision]);
  const overview = useRemote(signal => api.getBoundaryOverview(offering.offeringId, capabilityPage, missionPage, signal),
    [offering.offeringId, offering.revision, capabilityPage, missionPage]);
  const states = { hosting, references, capabilities: overview, missions: overview, responsibilities: boundary };
  const next: Exclude<Task, 'allocations'> = !ready(hosting) || !hosting.data?.permittedScopes.length ? 'hosting'
    : !ready(references) || !references.data?.total ? 'references'
      : !ready(overview) || !overview.data?.capabilities.published ? 'capabilities'
        : !ready(boundary) || !boundary.data?.providerResponsibilities.length || !boundary.data?.customerResponsibilities.length
          ? 'responsibilities' : 'missions';
  const open = (task: Task) => { if (task === 'references') setNewReference(true); setActive(task); setPending(false); };
  const saved = () => {
    setActive(null); setPending(false); hosting.retry(); references.retry(); boundary.retry(); overview.retry(); onChanged();
  };
  const action = (task: Exclude<Task, 'allocations'>, primary = false) => <button type="button"
    className={primary ? buttonClass : secondaryButtonClass} disabled={pending || !ready(states[task])}
    onClick={() => open(task)}>{actions[task]}</button>;
  return <div className="space-y-5">
    <section aria-label="Suggested next step" className="space-y-4 rounded-lg bg-indigo-50 p-5 dark:bg-indigo-950">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div><h2 className="font-semibold">Suggested next step</h2><p className={muted}>Start here, or open one task below. These are setup records, not an authorization decision.</p></div>
        {action(next, true)}
      </div>
      <ul aria-label="Offering setup checklist" className="grid gap-2 text-sm sm:grid-cols-2">
        <li>Azure hosting: <strong>{readLabel(hosting, hosting.data?.permittedScopes.length ? 'Resources configured' : 'Needs configuration')}</strong></li>
        <li>Microsoft references: <strong>{readLabel(references, `${references.data?.total ?? 0} saved references`)}</strong></li>
        <li>Security capabilities: <strong>{readLabel(overview, `${overview.data?.capabilities.published ?? 0} published`)}</strong></li>
        <li>Shared responsibilities: <strong>{readLabel(boundary, boundary.data?.providerResponsibilities.length && boundary.data.customerResponsibilities.length ? 'Recorded for review' : 'Needs documentation')}</strong></li>
        <li>Mission systems: <strong>{readLabel(overview, `${overview.data?.missionSystems.total ?? 0} hosting assignments`)}</strong></li>
      </ul>
    </section>
    <TaskCard title="Azure hosting" description="Define the provider tenant, subscriptions and resources available through this offering." action={action('hosting')}>
      <Availability title="Azure hosting" state={hosting} />
      {ready(hosting) && <>{hosting.data ? <>
        <h3 className="font-semibold">{hosting.data.name}</h3>
        <p className={muted}>{hosting.data.permittedScopes.length} explicit resource scopes · {hosting.data.exclusions.length} exclusions</p>
        <details><summary className={`cursor-pointer ${linkStyle}`}>Details</summary>
          <dl className="mt-3 space-y-2 break-all text-sm">
            <dt>Hosting version</dt><dd>{hosting.data.snapshot.revision}</dd>
            <dt>Snapshot ID</dt><dd>{hosting.data.snapshot.revisionId}</dd><dt>Snapshot hash</dt><dd>{hosting.data.snapshot.snapshotHash}</dd>
          </dl>
          <ul className="mt-3 space-y-2 break-all text-sm">{hosting.data.permittedScopes.map((scope, index) => <li key={index}>
            {scope.cloud === 'AzureUSGovernment' ? 'Azure Government' : 'Azure Commercial'} · Tenant {scope.directoryTenantId} · Subscription {scope.subscriptionId}<br />{scope.resourceId}
          </li>)}</ul>
        </details>
      </> : <p className={muted}>Not configured</p>}
        <p className={muted}>Hosting describes available infrastructure. A hosting assignment allocates part of it to a specific mission system. Recording either does not create Azure resources or grant access.</p>
      </>}
    </TaskCard>
    <TaskCard title="Microsoft authorization references" description="Record Microsoft-issued authorization documents supporting this offering, such as an authorization letter, provisional authorization or applicable service assessment package." action={action('references')}>
      <Availability title="Microsoft authorization references" state={references} />
      {ready(references) && <p className={muted}>{references.data?.total ?? 0} references saved. These describe Microsoft&apos;s documented service scope, not the provider&apos;s own authorization decision. Use the exact document title, authority, dates, scope and retained source citations.</p>}
      {ready(references) && !!references.data?.total && <div className="space-y-3">
        <ul className="space-y-2 text-sm">{references.data.items.map(reference => <li key={reference.recordId}>
          <span className="font-medium">{reference.reference}</span> · {reference.metadataReviewState === 'Recorded' ? 'Metadata reviewed' : 'Awaiting metadata review'}
        </li>)}</ul>
        <button type="button" className={secondaryButtonClass} disabled={pending}
          onClick={() => { setNewReference(false); setActive('references'); }}>Review saved references</button>
      </div>}
    </TaskCard>
    <TaskCard title="Security capabilities" description="Review published capabilities available for mission-system selection, separately from unpublished source proposals." action={action('capabilities')}>
      <Availability title="Security capabilities" state={overview} />
      {overview.data && <p className={muted}>{overview.data.capabilities.published} published · {overview.data.capabilities.awaitingReview} awaiting review</p>}
    </TaskCard>
    <TaskCard title="Mission systems" description="View existing hosting allocations, Mission Owner associations and the capabilities adopted by each system." action={action('missions')}>
      <Availability title="Mission systems" state={overview} />
      {overview.data && <p className={muted}>{overview.data.missionSystems.total} hosting assignments. An allocation is not yet a Mission Owner association.</p>}
      <p className={muted}>Mission Owners use their organization workspace: Select system → choose existing CSP hosting scope → select applicable capabilities → review responsibilities → confirm associations.</p>
    </TaskCard>
    <TaskCard title="Shared responsibilities" description="Explain what the CSP provides and which duties remain with the Mission Owner." action={action('responsibilities')}>
      <Availability title="Shared responsibilities" state={boundary} />
      {ready(boundary) && <p className={muted}>{boundary.data ? `Recorded boundary: ${boundary.data.name}` : 'No boundary responsibilities recorded.'} Viewing this page does not accept duties for a mission system.</p>}
    </TaskCard>
    {active && <SetupDialog key={active} title={taskTitles[active]} description={`Offering: ${offering.name}`}
      busy={pending} onClose={() => setActive(null)}>
      <div className="space-y-4">
      <Availability title={taskTitles[active]} state={active === 'allocations' ? hosting : states[active]} />
      {active === 'hosting' && <fieldset disabled={!ready(hosting)}>
        <HostingPanel offering={offering} initialScope={hosting.data ?? undefined} task="scope" onPendingChange={setPending} onChanged={saved} />
      </fieldset>}
      {active === 'references' && <DecisionPanel offering={offering} inheritedOnly initialAction={newReference ? 'draft' : undefined}
        onPendingChange={setPending} onChanged={() => { references.retry(); onChanged(); }}
        onRefreshOffering={async () => {
          const current = await api.getOffering(offering.offeringId);
          if (current.offeringId !== offering.offeringId || !Number.isSafeInteger(current.revision) || current.revision < 1)
            throw new Error('The offering response did not identify the requested current revision.');
          setRefreshedOffering(current);
        }} />}
      {active === 'capabilities' && overview.data && <>
        <p className={muted}>A Mission Owner selects applicable published capabilities during association. Publication alone does not establish inheritance.</p>
        <ul className="space-y-3">{overview.data.capabilities.items.map(item => <li key={`${item.capabilityId}:${item.candidateId}`} className="space-y-1 border-b pb-3">
          <p className="font-medium">{item.name}</p><p className={muted}>{item.publicationState === 'Published' ? 'Published' : 'Source proposal - not available for adoption'}</p>
          {item.capabilityId && <Link className={linkStyle} to={`/security-capabilities/${encodeURIComponent(item.capabilityId)}`}>Open {item.name}</Link>}
        </li>)}</ul>
        {!overview.data.capabilities.total && <p className={muted}>No capability records linked to this offering.</p>}
        {overview.data.capabilities.total > overview.data.capabilities.pageSize && <Pager {...overview.data.capabilities} onPage={setCapabilityPage} />}
        <Link className={linkStyle} to={api.authorizationHref(offering.offeringId, 'packages')}>Review source proposals</Link>
      </>}
      {active === 'missions' && overview.data && <>
        <ul className="space-y-3">{overview.data.missionSystems.items.map(item => <li key={item.assignmentId} className="space-y-2 rounded border p-3">
          <h4 className="font-semibold">{item.systemName ?? 'Mission system (name unavailable)'}</h4>
          <p className="text-sm">{item.associated ? 'Associated' : 'Awaiting Mission Owner association'} · {item.adoptedCapabilityCount} adopted capabilities</p>
          <details><summary className={`cursor-pointer ${linkStyle}`}>Details</summary>
            <p className="break-all text-xs">System ID: {item.systemId}<br />Assignment ID: {item.assignmentId}</p>
          </details>
        </li>)}</ul>
        {!overview.data.missionSystems.total && <p className={muted}>No existing hosting allocations for mission systems. Mission Owners cannot create allocations through the association task.</p>}
        {overview.data.missionSystems.total > overview.data.missionSystems.pageSize && <Pager {...overview.data.missionSystems} onPage={setMissionPage} />}
        <details><summary className={`cursor-pointer ${linkStyle}`}>Provider allocation administration</summary>
          <p className={`my-3 ${muted}`}>For CSP administrators only: record an explicit allocation for an authorized customer system. This is separate from the Mission Owner&apos;s capability selection.</p>
          <button type="button" className={secondaryButtonClass} disabled={!ready(hosting) || !hosting.data} onClick={() => open('allocations')}>Manage hosting allocations</button>
        </details>
      </>}
      {active === 'allocations' && <fieldset disabled={!ready(hosting)}>
        <HostingPanel offering={offering} initialScope={hosting.data ?? undefined} task="allocations" onPendingChange={setPending} onChanged={saved} />
      </fieldset>}
      {active === 'responsibilities' && ready(boundary) && <>
        <div className="grid gap-5 md:grid-cols-2">{([
          ['CSP provides', boundary.data?.providerResponsibilities ?? []],
          ['Mission Owner remains responsible for', boundary.data?.customerResponsibilities ?? []],
        ] satisfies [string, string[]][]).map(([title, duties]) => <div key={title} className="space-y-2">
          <h4 className="font-semibold">{title}</h4>
          {duties.length ? <ul className="list-disc space-y-2 pl-5 text-sm">{duties.map((duty, i) => <li key={i}>{duty}</li>)}</ul>
            : <p className={muted}>Not documented. Missing duties are not a waiver.</p>}
        </div>)}</div>
        <Link className={linkStyle} to={api.authorizationHref(offering.offeringId, 'boundary')}>Review or edit the authorization boundary</Link>
      </>}
      <div className="flex justify-end">
        <button type="button" className={secondaryButtonClass} disabled={pending} onClick={() => setActive(null)}>Close task</button>
      </div>
      </div>
    </SetupDialog>}
  </div>;
}
