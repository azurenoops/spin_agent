import { useState, type ReactNode } from 'react';
import { Pencil } from 'lucide-react';
import { Link } from '../workspaces/workspaceNavigation';
import { Pager, secondaryButtonClass, Status, surfaceClass, useRemote } from '../workspace-operations/workspaceUi';
import { BoundaryEditor } from './OfferingIntake';
import SetupDialog from '../workspace-operations/SetupDialog';
import * as api from './api';
import type { ProviderScope, OfferingEnvironment, Offering, OfferingBoundaryOverview } from './types';
import { offeringEnvironments, scopeLabel, missionRelationshipLabels } from './scopes';
import { ProviderBadge, ProviderPanel } from './ProviderPresentation';

const cloudName = (cloud: OfferingEnvironment) => offeringEnvironments[cloud];
const linkClass = 'text-sm font-medium text-indigo-700 underline underline-offset-2 dark:text-indigo-300';
const mutedClass = 'text-sm text-slate-600 dark:text-slate-300';

function Card({ title, description, children, action }: { title: string; description: string; children: ReactNode; action?: ReactNode }) {
  return <section aria-label={title} className={`${surfaceClass} min-w-0 space-y-4 p-5`}>
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h2 className="text-lg font-semibold">{title}</h2><p className={`mt-1 ${mutedClass}`}>{description}</p></div>
      {action}
    </div>
    {children}
  </section>;
}

function Statements({ values, empty, label }: { values: string[]; empty: string; label?: string }) {
  return values.length ? <ul aria-label={label} className="list-disc space-y-2 break-words pl-5 text-sm">
    {values.map((value, index) => <li key={index}>{value}</li>)}
  </ul> : <p className={mutedClass}>{empty}</p>;
}

function RecordedDuties({ values, empty }: { values: string[]; empty: string }) {
  return values.length === 1 ? <p className="break-words">{values[0]}</p> : <Statements values={values} empty={empty} />;
}

function ResourceScopes({ scopes }: { scopes: ProviderScope[] }) {
  return <ul className="space-y-3">{scopes.map((scope, index) => <li key={index} className="rounded border border-slate-200 p-3 dark:border-gray-700">
    {scope.kind === 'Service' ? <p className="break-words text-sm">{scopeLabel(scope)} · Manually documented relationship</p> : <dl className="grid gap-3 break-all text-sm sm:grid-cols-2">
      <div><dt className={mutedClass}>Cloud</dt><dd>{cloudName(scope.cloud)}</dd></div>
      <div><dt className={mutedClass}>Directory tenant</dt><dd>{scope.directoryTenantId}</dd></div>
      <div><dt className={mutedClass}>Subscription</dt><dd>{scope.subscriptionId}</dd></div>
      <div className="sm:col-span-2"><dt className={mutedClass}>Resource group or resource scope</dt><dd>{scope.resourceId}</dd></div>
    </dl>}
  </li>)}</ul>;
}

function CapabilityRecords({ offering, data, onPage }: {
  offering: Offering; data: OfferingBoundaryOverview['capabilities']; onPage: (page: number) => void;
}) {
  return <>
    <dl className="grid grid-cols-3 gap-3">
      {[[data.total, 'Linked records'], [data.awaitingReview, 'Awaiting review'], [data.published, 'Published']].map(([count, label]) =>
        <div key={label} className="rounded-lg bg-slate-50 p-3 dark:bg-gray-800"><dt className={mutedClass}>{label}</dt><dd className="mt-1 text-2xl font-semibold">{count}</dd></div>)}
    </dl>
    <p className={mutedClass}>Source proposals are not published capabilities. Publication does not establish authorization or mission inheritance.</p>
    {!data.items.length ? <p className={mutedClass}>No linked capability records.</p> : <ul className="divide-y divide-slate-200 dark:divide-gray-700">
      {data.items.map(item => <li key={`${item.capabilityId ?? ''}:${item.candidateId ?? ''}`} className="space-y-2 py-3">
        <div className="flex flex-wrap justify-between gap-2"><h3 className="break-words font-medium">{item.name}</h3>
          <span className="rounded bg-slate-100 px-2 py-1 text-xs text-slate-700 dark:bg-gray-800 dark:text-slate-200">{item.publicationState === 'Published' ? 'Published' : item.reviewState === 'NeedsReview' ? 'Awaiting review' : item.reviewState}</span>
        </div>
        <p className={mutedClass}>{!item.boundaryRevisionId ? 'Boundary context not recorded'
          : item.boundaryRevisionId === offering.currentBoundaryRevisionId ? 'Linked to this boundary version'
            : 'Different boundary version - review applicability before use.'}</p>
        <div className="flex flex-wrap gap-4">
          {item.packageId && <Link className={linkClass} to={api.authorizationHref(offering.offeringId, `packages/${item.packageId}`)}>Review source proposal</Link>}
          {item.capabilityId && <Link className={linkClass} to={`/security-capabilities/${encodeURIComponent(item.capabilityId)}`}>
            {item.publicationState === 'Published' ? 'Open published capability' : 'Open capability'}
          </Link>}
        </div>
      </li>)}
    </ul>}
    {data.total > data.pageSize && <Pager {...data} onPage={onPage} />}
  </>;
}

const relationshipLabels = missionRelationshipLabels;

function MissionRecords({ data, onPage }: { data: OfferingBoundaryOverview['missionSystems']; onPage: (page: number) => void }) {
  return <>
    <p className={mutedClass}>Hosting assignment, Mission Owner association and capability adoption are separate steps. None alone grants system authorization.</p>
    {!data.items.length ? <p className={mutedClass}>No hosting assignments recorded.</p> : <>
      <p className={mutedClass}>{data.total} recorded hosting {data.total === 1 ? 'assignment' : 'assignments'}</p>
      <ul className="space-y-3">{data.items.map(item => <li key={item.assignmentId} className="space-y-2 rounded border border-slate-200 p-4 dark:border-gray-700">
        <h3 className="font-semibold">{item.systemName ?? 'Mission system'}</h3>
        <p className="break-all text-xs text-slate-500 dark:text-slate-400">System ID: {item.systemId}</p>
        <p className="text-sm">{item.associated ? 'Mission system associated' : 'Mission Owner association pending'} · {item.adoptedCapabilityCount} adopted capabilities</p>
        <p className={mutedClass}>{relationshipLabels[item.relationshipState] ?? item.relationshipState}</p>
        <details><summary className={`cursor-pointer ${linkClass}`}>Assigned hosting scope</summary>
          <div className="mt-3"><ResourceScopes scopes={item.assignedScopes} /></div>
        </details>
      </li>)}</ul>
    </>}
    {data.total > data.pageSize && <Pager {...data} onPage={onPage} />}
  </>;
}

function BoundaryHistory({ offering }: { offering: Offering }) {
  const [page, setPage] = useState(1);
  const history = useRemote(signal => api.listBoundaries(offering.offeringId, page, signal), [offering.offeringId, offering.revision, page]);
  return <div className="mt-4 space-y-3">
    <p className={mutedClass}>Saving preserves previous versions. Offering revision {offering.revision}.</p>
    <Status loading={history.loading} error={history.error} retry={history.retry} />
    {history.data && <>
      {!history.data.items.length && <p className={mutedClass}>No boundary versions recorded.</p>}
      <ol className="space-y-3">{history.data.items.map(item => <li key={item.boundaryRevisionId} className="space-y-2 rounded border border-slate-200 p-4 dark:border-gray-700">
        <h3 className="font-semibold">Version {item.version} · {item.name}{item.boundaryRevisionId === offering.currentBoundaryRevisionId ? ' (current)' : ''}</h3>
        <p className={mutedClass}>Recorded {new Date(item.createdAt).toLocaleString()}</p>
        <dl className="text-xs"><dt className={mutedClass}>Snapshot hash</dt><dd className="break-all font-mono">{item.snapshotHash}</dd></dl>
        <details><summary className={`cursor-pointer ${linkClass}`}>Scope and responsibilities in this version</summary>
          <div className="mt-3 space-y-3">
            <p className="whitespace-pre-wrap break-words text-sm">{item.scopeStatement}</p>
            <Statements values={item.services} empty="No services recorded." label="Previous services" />
            <ResourceScopes scopes={item.includedScopes} />
            <Statements values={item.exclusions.map(value => `${value.description} - ${value.rationale}${value.scope ? ` (${scopeLabel(value.scope)})` : ''}`)} empty="No exclusions recorded." label="Previous exclusions" />
            <Statements values={item.providerResponsibilities} empty="No CSP responsibilities recorded." label="Previous CSP responsibilities" />
            <Statements values={item.customerResponsibilities} empty="No Mission Owner responsibilities recorded." label="Previous Mission Owner responsibilities" />
            <Statements values={item.componentSnapshotIds} empty="No component snapshots recorded." label="Component snapshot references" />
          </div>
        </details>
      </li>)}</ol>
      {history.data.total > history.data.pageSize && <Pager {...history.data} onPage={setPage} />}
    </>}
  </div>;
}

export function BoundaryPage({ offering, refresh }: { offering: Offering; refresh: () => void }) {
  const [editing, setEditing] = useState(false);
  const [pending, setPending] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [capabilityPage, setCapabilityPage] = useState(1);
  const [missionPage, setMissionPage] = useState(1);
  const current = useRemote(signal => offering.currentBoundaryRevisionId
    ? api.getBoundary(offering.offeringId, offering.currentBoundaryRevisionId, signal) : Promise.resolve(null),
  [offering.offeringId, offering.currentBoundaryRevisionId, offering.revision]);
  const linked = useRemote(signal => api.getBoundaryOverview(offering.offeringId, capabilityPage, missionPage, signal),
    [offering.offeringId, offering.revision, capabilityPage, missionPage]);
  const boundary = current.data;
  const scopeStatement = boundary?.scopeStatement ?? '';
  const firstSentence = scopeStatement.split(/(?<=\.)\s+|\r?\n/)[0] ?? '';
  const excerpt = scopeStatement.length > 320 && firstSentence.trim().length > 0 && firstSentence.length <= 320 ? firstSentence : scopeStatement;
  const staleContext = !!linked.data && linked.data.offeringRevision !== offering.revision;
  const unavailable = current.loading || !!current.error;
  return <div className="offering-scope space-y-5">
    <ProviderPanel title="Service scope"
      description={boundary && !unavailable && <p className="offering-scope-version">Recorded boundary version {boundary.version} · Working recorded scope</p>}
      action={<div className="offering-scope-actions">
      <ProviderBadge tone="neutral">Recorded, not re-approved here</ProviderBadge>
      <button type="button" className="provider-secondary inline-flex gap-2" disabled={unavailable || staleContext}
        onClick={() => setEditing(true)}><Pencil size={15} aria-hidden="true" />Edit boundary</button>
    </div>}>
      <Status loading={current.loading} error={current.error} retry={current.retry} />
      {staleContext && <p role="alert" className="offering-scope-note">
        Offering context changed since this page loaded. <button type="button" className="provider-text" onClick={refresh}>Refresh offering context</button> before editing.
      </p>}
      {boundary && !unavailable && <>
        <p className="offering-scope-statement">{excerpt || 'No scope statement recorded.'}</p>
        {excerpt !== scopeStatement && <p className="text-xs mt-2">Scope excerpt; read the complete recorded statement in source provenance.</p>}
        {boundary.services.length ? <ul aria-label="Included services" className="offering-scope-services">
          {boundary.services.map((service, index) => <li key={index}>{service}</li>)}
        </ul> : <p className="my-4">No services recorded.</p>}
        <div className="offering-scope-note"><strong>Not included:</strong>
          <RecordedDuties values={boundary.exclusions.map(value => value.description)} empty="No explicit exclusions recorded; this does not expand the included scope." />
        </div>
      </>}
      {!current.loading && !current.error && !boundary && <p className={mutedClass}>No boundary recorded. Review the package, then use Edit boundary to record the supported scope.</p>}
      {!unavailable && <details className="mt-4"><summary className="provider-text">Hosting identity & source provenance</summary>
        <dl className="offering-scope-facts">
          <div><dt>Provider offering</dt><dd>{offering.name}</dd></div>
          <div><dt>Recorded cloud environment</dt><dd>{offering.environments.map(cloudName).join(', ') || 'Not recorded'}</dd></div>
          <div><dt>Boundary / service scope</dt><dd>{boundary?.name ?? 'Not recorded'}</dd></div>
          {boundary && <div><dt>Snapshot hash</dt><dd>{boundary.snapshotHash}</dd></div>}
        </dl>
        {boundary && <><h3 className="font-semibold">Complete recorded scope statement</h3><p className="whitespace-pre-wrap break-words mt-2">{scopeStatement || 'No scope statement recorded.'}</p></>}
        <h3 className="mt-4 mb-2 font-semibold">Tenant and resource scope</h3>
        {boundary?.includedScopes.length ? <ResourceScopes scopes={boundary.includedScopes} />
          : <p>No tenant, subscription or resource identifiers recorded. This is not universal resource coverage.</p>}
        <h3 className="mt-4 mb-2 font-semibold">Exclusion rationales</h3>
        <Statements values={boundary?.exclusions.map(value => `${value.description} - ${value.rationale}${value.scope ? ` (${scopeLabel(value.scope)})` : ''}`) ?? []}
          empty="No explicit exclusions recorded; this does not expand the included scope." />
        <h3 className="mt-4 mb-2 font-semibold">Source citations</h3>
        {boundary?.citations.length ? <ul className="space-y-2 text-xs">{boundary.citations.map((citation, index) => <li key={index}>
          <Link className={linkClass} to={api.authorizationHref(offering.offeringId, `packages/${encodeURIComponent(citation.packageId)}`)}>{citation.archivePath}</Link> · {citation.locator}
          <details><summary className="provider-text">Source excerpt</summary><p className="whitespace-pre-wrap break-words">{citation.quote}</p></details>
        </li>)}</ul> : <p>No supporting source citations recorded. Resource identity alone is not reviewed service coverage.</p>}
        <p className="mt-3 text-xs">The offering name and cloud environment identify the provider service. The boundary describes its recorded coverage; they are not interchangeable.</p>
        <div className="offering-scope-links mt-3">
          <Link className="provider-text" to={api.authorizationHref(offering.offeringId, 'packages')}>Review source packages</Link>
          <Link className="provider-text" to={api.authorizationHref(offering.offeringId, 'inherited-coverage')}>Manage hosting context</Link>
          {boundary && <Link className="provider-text" to={api.changeImpactHref(offering.offeringId,
            { boundaryRevisionId: boundary.boundaryRevisionId })}>Review changes</Link>}
        </div>
      </details>}
    </ProviderPanel>
    {editing && <SetupDialog title="Edit boundary" description={`Offering: ${offering.name}`} busy={pending} onClose={() => setEditing(false)}>
      <div className="space-y-4">
        <p className={mutedClass}>Saving creates a new version and preserves previous versions. Review affected authorization and capability context before publication.</p>
        <BoundaryEditor offering={offering} predecessor={boundary ?? undefined} onPendingChange={setPending}
          onSaved={() => { setEditing(false); setPending(false); current.retry(); linked.retry(); refresh(); }} />
        <button type="button" disabled={pending} className={secondaryButtonClass} onClick={() => setEditing(false)}>Cancel editing</button>
      </div>
    </SetupDialog>}
    <div className="offering-scope-duties">
      <ProviderPanel title="Provider duties">
        {unavailable ? <p>Responsibilities are unavailable until the current scope loads.</p>
          : <RecordedDuties values={boundary?.providerResponsibilities ?? []} empty="No CSP responsibilities recorded." />}
        <Link className="provider-text mt-5 inline-block" to={api.authorizationHref(offering.offeringId, 'inherited-coverage?task=capabilities')}>Explore duties by capability →</Link>
      </ProviderPanel>
      <ProviderPanel title="Customer duties">
        {unavailable ? <p>Responsibilities are unavailable until the current scope loads.</p>
          : <RecordedDuties values={boundary?.customerResponsibilities ?? []} empty="No Mission Owner responsibilities recorded." />}
        <p className="offering-scope-note mt-5">Shared control references do not mean the customer’s part is complete.</p>
      </ProviderPanel>
    </div>
    <p className="offering-scope-context">Recorded boundary duties are working context, not immutable published duty allocations. Mission Owners must review their system-specific applicability; association does not accept duties automatically.</p>
    <Status loading={linked.loading} error={linked.error} retry={linked.retry} />
    <details><summary className="provider-text">Supporting records, versions & workflow</summary>
    <div className="space-y-4 mt-4">
    <details className="provider-panel"><summary className="provider-text">Linked capabilities and mission records</summary>
    <Card title="Security capabilities" description="Which source proposals and published capabilities are linked to this offering?"
      action={<Link className={linkClass} to="/security-capabilities">Open capability catalog</Link>}>
      {linked.data && <CapabilityRecords offering={offering} data={linked.data.capabilities} onPage={setCapabilityPage} />}
      <Link className={linkClass} to={api.authorizationHref(offering.offeringId, 'packages')}>Review and publish package capabilities</Link>
    </Card>
    <Card title="Mission systems" description="Which systems have assigned hosting scope, associated with this offering or adopted its capabilities?"
      action={<Link className={linkClass} to={api.authorizationHref(offering.offeringId, 'inherited-coverage?task=missions')}>Manage hosting assignments</Link>}>
      {linked.data && <MissionRecords data={linked.data.missionSystems} onPage={setMissionPage} />}
    </Card>
    </details>
    <details className="provider-panel"><summary className="provider-text">Documentation and review workflow</summary>
      <ol aria-label="Offering workflow" className="mt-3 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {[
          ['Review the package', 'Check the source documents and extracted claims.'],
          ['Confirm the boundary', 'Record the services, resources and responsibilities.'],
          ['Review and publish capabilities', 'Approve the exact reusable capabilities before release.'],
          ['Mission Owners associate systems', 'Select applicable published capabilities and review customer duties.'],
        ].map(([title, description], index) => <li key={title} className="flex min-w-0 gap-3">
          <span aria-hidden="true" className="font-semibold">{index + 1}</span>
          <div><p className="text-sm font-semibold">{title}</p><p className={`mt-1 ${mutedClass}`}>{description}</p></div>
        </li>)}
      </ol>
    </details>
    <details className={`${surfaceClass} p-5`} open={historyOpen} onToggle={event => setHistoryOpen(event.currentTarget.open)}>
      <summary className="cursor-pointer font-semibold">Version history</summary>
      {historyOpen && <BoundaryHistory offering={offering} />}
    </details>
    </div>
    </details>
    <p className={mutedClass}>A recorded boundary does not authorize mission workloads or grant inherited controls. Hosting connectivity, publication and mission authorization remain separate.</p>
  </div>;
}
