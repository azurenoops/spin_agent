import { useState, type ReactNode } from 'react';
import { Link, useLocation } from '../workspaces/workspaceNavigation';
import { buttonClass, secondaryButtonClass, Pager, useRemote } from '../workspace-operations/workspaceUi';
import { DecisionPanel } from './DecisionPanel';
import { HostingPanel } from './HostingPanel';
import SetupDialog from '../workspace-operations/SetupDialog';
import * as api from './api';
import { getHostingScope, listHostingScopes } from './hostingApi';
import type { Offering } from './types';
import { ProviderBadge, ProviderFact, ProviderPanel, ProviderSupport } from './ProviderPresentation';
import { OfferingCapabilities } from './OfferingCapabilities';
import { HostingContextSummary, HostingScopeIdentity } from './HostingContextSummary';
import { managementArrangements, serviceModels } from './OfferingIdentity';
import { offeringEnvironments, scopeLabel } from './scopes';

type Task = 'hosting' | 'references' | 'capabilities' | 'missions' | 'responsibilities' | 'allocations';
type ReadState = { loading: boolean; error: string | null; retry: () => void };
const muted = 'text-sm text-slate-600 dark:text-slate-300';
const linkStyle = 'text-sm font-medium text-indigo-700 underline dark:text-indigo-300';
const actions: Record<Exclude<Task, 'allocations'>, string> = {
  hosting: 'Configure hosting', references: 'Add reference', capabilities: 'Review capabilities',
  missions: 'View associations', responsibilities: 'Review responsibilities',
};
const taskTitles: Record<Task, string> = {
  hosting: 'Configure Azure hosting', references: 'Add or review upstream provider references',
  capabilities: 'Review offering capabilities', missions: 'Mission system associations',
  responsibilities: 'Review responsibilities', allocations: 'Provider hosting allocation',
};
function TaskCard({ title, description, action, children }: {
  title: string; description: string; action: ReactNode; children: ReactNode;
}) {
  return <section aria-label={title} className="provider-panel min-w-0 space-y-4">
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
  if (requestedTask === 'capabilities') return <OfferingCapabilities offering={loadedOffering} />;
  return <ServiceScopeSetup offering={loadedOffering} onChanged={onChanged} />;
}

function ServiceScopeSetup({ offering: loadedOffering, onChanged }: { offering: Offering; onChanged: () => void }) {
  const location = useLocation();
  const requestedTask = new URLSearchParams(location.search).get('task');
  const [refreshedOffering, setRefreshedOffering] = useState<Offering | null>(null);
  const offering = refreshedOffering && refreshedOffering.revision > loadedOffering.revision ? refreshedOffering : loadedOffering;
  const [active, setActive] = useState<Task | null>(() =>
    requestedTask === 'hosting' || requestedTask === 'capabilities' || requestedTask === 'missions' || requestedTask === 'allocations' ? requestedTask : null);
  const [pending, setPending] = useState(false);
  const [newReference, setNewReference] = useState(true);
  const [capabilityPage, setCapabilityPage] = useState(1);
  const [missionPage, setMissionPage] = useState(1);
  const hosting = useRemote(async signal => offering.currentHostingScopeRevisionId
    ? getHostingScope(offering.offeringId, offering.currentHostingScopeRevisionId, signal)
    : (await listHostingScopes(offering.offeringId, 1, signal), null),
  [offering.offeringId, offering.currentHostingScopeRevisionId, offering.revision]);
  const serviceHosted = hosting.data?.permittedScopes.some(scope => scope.kind === 'Service')
    || offering.environments.every(environment => environment === 'Microsoft365DoD' || environment === 'ManualService');
  const hostingTitle = serviceHosted ? 'Service relationship' : 'Azure hosting';
  const taskTitle = (task: Task) => task === 'hosting' && serviceHosted ? 'Configure service relationship' : taskTitles[task];
  const references = useRemote(signal => api.listInheritedProviderReferences(offering.offeringId, 1, signal),
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
    onClick={() => open(task)}>{task === 'hosting' && serviceHosted ? 'Configure service relationship' : actions[task]}</button>;
  return <div className="provider-grid"><div className="space-y-5">
    <Link className="provider-primary" to={api.authorizationHref(offering.offeringId, 'inherited-coverage/propose')}>Edit proposed scope</Link>
    <ProviderPanel title="Service boundary" action={<Link className="provider-secondary" to={api.authorizationHref(offering.offeringId, 'boundary')}>Review boundary</Link>}>
      <Availability title="Service boundary" state={boundary} />
      {ready(boundary) && <div className="space-y-4">
        <dl><ProviderFact label="Service model">{offering.serviceModel ? serviceModels[offering.serviceModel] : 'Not recorded'}</ProviderFact>
          <ProviderFact label="Management arrangement">{offering.managementArrangement ? managementArrangements[offering.managementArrangement] : 'Not recorded'}</ProviderFact>
          <ProviderFact label="Environment">{offering.environments.map(cloud => offeringEnvironments[cloud]).join(', ')}</ProviderFact>
          <ProviderFact label="Recorded boundary">{boundary.data ? `${boundary.data.name} · v${boundary.data.version}` : 'Not recorded'}</ProviderFact></dl>
        {boundary.data && <><p className={muted}>{boundary.data.scopeStatement}</p>
          <div><h3 className="font-semibold">Included services</h3><p className={muted}>{boundary.data.services.join(', ') || 'No services stated. Missing scope is not universal coverage.'}</p></div>
          <div><h3 className="font-semibold">Excluded work</h3>{boundary.data.exclusions.length ? <ul className="list-disc pl-5 text-sm">{boundary.data.exclusions.map((item, index) => <li key={index}>{item.description}</li>)}</ul>
            : <p className={muted}>No explicit exclusions recorded. Review the source before inferring coverage.</p>}</div></>}
      </div>}
    </ProviderPanel>
    <ProviderPanel title={serviceHosted ? 'Technical service scope' : 'Technical hosting scope'} action={action('hosting')}>
      <Availability title="Technical hosting scope" state={hosting} />
      {ready(hosting) && hosting.data && <HostingContextSummary offeringName={offering.name} scope={hosting.data} />}
      {ready(hosting) && <>{hosting.data?.permittedScopes.length ? <div className="provider-table-wrap"><table className="provider-table" aria-label="Technical hosting scope">
        <thead><tr><th>Scope</th><th>Use</th><th>Status</th></tr></thead>
        <tbody>{hosting.data.permittedScopes.map((scope, index) => <tr key={index}>
          <td><HostingScopeIdentity scope={scope} /></td><td>Provider service boundary</td><td><ProviderBadge tone="neutral">Recorded</ProviderBadge></td>
        </tr>)}</tbody></table></div> : <p>No technical scope recorded. Define eligible resources or an explicit manual service relationship.</p>}</>}
      <Availability title="Customer allocations" state={overview} />
      {!!overview.data?.missionSystems.items.length && <div className="provider-table-wrap mt-4"><table className="provider-table" aria-label="Customer service allocations">
        <thead><tr><th>Allocated scope</th><th>Customer use</th><th><span className="sr-only">Actions</span></th></tr></thead>
        <tbody>{overview.data.missionSystems.items.map(mission => <tr key={mission.assignmentId}>
          <td>{mission.assignedScopes.length ? mission.assignedScopes.map((scope, index) => <HostingScopeIdentity key={index} scope={scope} />) : 'Scope not reported'}</td>
          <td>{mission.systemName || 'System name unavailable'}<small>{mission.associated ? 'Associated' : 'Awaiting Mission Owner association'}</small></td>
          <td><Link to={api.authorizationHref(offering.offeringId, `missions/${encodeURIComponent(mission.assignmentId)}`)}>View customer</Link></td>
        </tr>)}</tbody></table>
        {overview.data.missionSystems.total > overview.data.missionSystems.pageSize && <Pager {...overview.data.missionSystems} onPage={setMissionPage} />}
      </div>}
    </ProviderPanel>
    <details className="provider-record-details"><summary>Scope setup and administration</summary><div className="space-y-5">
    <section aria-label="Suggested next step" className="space-y-4 rounded-lg bg-indigo-50 p-5 dark:bg-indigo-950">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div><h2 className="font-semibold">Suggested next step</h2><p className={muted}>Start here, or open one task below. These are setup records, not an authorization decision.</p></div>
        {action(next, true)}
      </div>
      <ul aria-label="Offering setup checklist" className="grid gap-2 text-sm sm:grid-cols-2">
        <li>{hostingTitle}: <strong>{readLabel(hosting, hosting.data?.permittedScopes.length ? serviceHosted ? 'Service recorded' : 'Resources configured' : 'Needs configuration')}</strong></li>
        <li>Upstream provider references: <strong>{readLabel(references, `${references.data?.total ?? 0} saved references`)}</strong></li>
        <li>Security capabilities: <strong>{readLabel(overview, `${overview.data?.capabilities.published ?? 0} published`)}</strong></li>
        <li>Shared responsibilities: <strong>{readLabel(boundary, boundary.data?.providerResponsibilities.length && boundary.data.customerResponsibilities.length ? 'Recorded for review' : 'Needs documentation')}</strong></li>
        <li>Mission systems: <strong>{readLabel(overview, `${overview.data?.missionSystems.total ?? 0} hosting assignments`)}</strong></li>
      </ul>
    </section>
    <TaskCard title={hostingTitle} description={serviceHosted
      ? 'Record the exact manual service instance and optional service tenant reference. No Azure IDs or live connector are implied.'
      : 'Define the provider tenant, subscriptions and resources available through this offering.'} action={action('hosting')}>
      <Availability title={hostingTitle} state={hosting} />
      {ready(hosting) && <>{hosting.data ? <>
        <h3 className="font-semibold">{offering.name} · Scope revision {hosting.data.snapshot.revision}</h3>
        <p className={muted}>{hosting.data.permittedScopes.length} explicit {serviceHosted ? 'service instances' : 'resource scopes'} · {hosting.data.exclusions.length} exclusions</p>
        <details><summary className={`cursor-pointer ${linkStyle}`}>Details</summary>
          <dl className="mt-3 space-y-2 break-all text-sm">
            <dt>Exact recorded scope name</dt><dd>{hosting.data.name}</dd>
            <dt>Hosting version</dt><dd>{hosting.data.snapshot.revision}</dd>
            <dt>Snapshot ID</dt><dd>{hosting.data.snapshot.revisionId}</dd><dt>Snapshot hash</dt><dd>{hosting.data.snapshot.snapshotHash}</dd>
          </dl>
          <ul className="mt-3 space-y-2 break-all text-sm">{hosting.data.permittedScopes.map((scope, index) => <li key={index}>
            {scope.kind === 'Service' ? scopeLabel(scope) : <>{offeringEnvironments[scope.cloud]} · Tenant {scope.directoryTenantId} · Subscription {scope.subscriptionId}<br />{scope.resourceId}</>}
          </li>)}</ul>
        </details>
      </> : <p className={muted}>Not configured</p>}
        <p className={muted}>{serviceHosted ? 'A manually documented service relationship identifies the service a mission consumes. It does not configure a connector or grant access.' : 'Hosting describes available infrastructure. A hosting assignment allocates part of it to a specific mission system. Recording either does not create Azure resources or grant access.'}</p>
      </>}
    </TaskCard>
    <TaskCard title="Upstream provider authorization references" description="Record upstream service authorization documents while keeping provider identity separate from the issuing authority and authorization category." action={action('references')}>
      <Availability title="Upstream provider authorization references" state={references} />
      {ready(references) && <p className={muted}>{references.data?.total ?? 0} references saved. These describe an upstream provider&apos;s documented scope, not this offering&apos;s own authorization decision. Use the exact document title, provider identity, authority, dates, scope and retained citations. Legacy Microsoft records remain readable.</p>}
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
    </div></details>
    {active && <SetupDialog key={active} title={taskTitle(active)} description={`Offering: ${offering.name}`}
      busy={pending} onClose={() => setActive(null)}>
      <div className="space-y-4">
      <Availability title={taskTitle(active)} state={active === 'allocations' ? hosting : states[active]} />
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
      {active === 'allocations' && ready(hosting) && <fieldset>
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
  </div><ProviderSupport><ProviderPanel title="Boundary and hosting"><p>The authorization boundary describes the recorded service. A hosting allocation identifies the exact part a mission consumes.</p></ProviderPanel>
    <ProviderPanel title="Versioned changes"><p>Review the exact proposed scope and affected systems before publication. Existing mission associations retain their selected versions.</p><Link className="provider-secondary mt-3" to={api.authorizationHref(offering.offeringId, 'impact')}>Review change impact</Link></ProviderPanel></ProviderSupport></div>;
}
