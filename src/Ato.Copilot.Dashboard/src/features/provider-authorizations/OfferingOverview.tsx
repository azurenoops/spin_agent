import { useEffect, useState, type ReactNode } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { buttonClass, errorClass, Pager, secondaryButtonClass, surfaceClass, useRemote } from '../workspace-operations/workspaceUi';
import SetupDialog from '../workspace-operations/SetupDialog';
import { stateLabel } from '../package-imports/PackageReceipts';
import { DecisionPanel } from './DecisionPanel';
import * as api from './api';
import type { ExternalDecision, Offering, OfferingOverviewData } from './types';

const muted = 'text-sm text-slate-600 dark:text-slate-300';
const linkClass = 'text-sm font-medium text-indigo-700 underline dark:text-indigo-300';
function Card({ title, description, children }: { title: string; description: string; children: ReactNode }) {
  return <section aria-label={title} className={`${surfaceClass} min-w-0 space-y-4 rounded-lg p-5`}>
    <div><h2 className="text-lg font-semibold">{title}</h2><p className={`mt-1 ${muted}`}>{description}</p></div>
    {children}
  </section>;
}
function Fact({ label, children }: { label: string; children: ReactNode }) {
  return <div className="min-w-0"><dt className="text-xs font-medium text-slate-500 dark:text-slate-400">{label}</dt>
    <dd className="whitespace-pre-wrap break-words text-sm">{children}</dd></div>;
}
function Authorization({ record }: { record: ExternalDecision }) {
  return <article className="space-y-3 border-t border-slate-200 pt-4 dark:border-gray-700">
    <div><h3 className="font-semibold">{record.reference}</h3>
      <p className={muted}>{record.metadataReviewState === 'Recorded' ? 'Recorded decision'
        : record.metadataReviewState === 'Unconfirmed' ? 'Draft details - awaiting human review' : 'Rejected metadata'}</p>
    </div>
    <dl className="grid gap-3 sm:grid-cols-2">
      <Fact label="Issuer">{record.issuingAuthority ?? 'Not recorded in this record'}</Fact>
      <Fact label="Decision as stated">{record.decisionAsStated ?? 'Not recorded in this record'}</Fact>
      <Fact label="Issued">{record.issuedOn ?? 'Not recorded in this record'}</Fact>
      <Fact label="Effective">{record.effectiveOn ?? 'Not recorded in this record'}</Fact>
      <Fact label="Expiration">{record.expiresOn ?? (record.expiryBasis === 'NoExpiryStated' ? 'No expiry stated in source' : 'Not recorded in this record')}</Fact>
      <Fact label="Standing">{record.currentStanding === 'CurrentAsRecorded'
        ? 'Current as recorded, not independently verified' : stateLabel(record.currentStanding)}</Fact>
      <Fact label="Authorized scope as stated">{record.scopeStatement}</Fact>
    </dl>
    <div><h4 className="text-sm font-medium">Conditions</h4>
      {record.conditions.length ? <ul className="list-disc space-y-1 pl-5 text-sm">{record.conditions.map((condition, index) => <li key={index}>{condition}</li>)}</ul>
        : <p className={muted}>No conditions recorded. Check the source documents.</p>}</div>
    <div><h4 className="text-sm font-medium">Supporting documents</h4>
      {record.citations.length ? <ul className="space-y-2 text-sm">{record.citations.map((citation, index) => <li key={index} className="break-words">
        <span className="font-medium">{citation.archivePath}</span> - {citation.locator}
        <details><summary className={`cursor-pointer ${linkClass}`}>Source excerpt</summary><p className="mt-1 whitespace-pre-wrap">{citation.quote}</p></details>
      </li>)}</ul> : <p className={muted}>No supporting citations recorded.</p>}</div>
    <details><summary className={`cursor-pointer ${linkClass}`}>Details and review history</summary>
      <p className="mt-2 text-sm">Recorded by: {record.recordedBy ?? 'Not yet reviewed'}<br />Recorded at: {record.recordedAt ?? 'Not yet reviewed'}</p>
      <p className="break-all text-xs">Record: {record.recordId}<br />Revision: {record.revisionId}<br />Snapshot: {record.snapshotHash}</p>
      <p className={muted}>Open Review authorization records for immutable versions and lifecycle actions.</p>
    </details>
  </article>;
}

function NextAction({ data, offering, reviewRecords }: { data: OfferingOverviewData; offering: Offering; reviewRecords: () => void }) {
  const source = data.packages.preferredAuthorizationReview;
  let label: string;
  let explanation: string;
  let href: string | undefined;
  const packages = api.authorizationHref(offering.offeringId, 'packages');
  if (data.authorizations.unconfirmed > 0) {
    label = 'Review authorization records';
    explanation = 'Check the draft against its supporting documents before recording the existing external decision.';
  } else if (source) {
    label = 'Review extracted authorization details';
    explanation = `Check the issuer, scope and dates extracted from ${source.packageName}. Reviewing source details does not record a decision or issue an ATO.`;
    href = `${api.authorizationHref(offering.offeringId, `packages/${encodeURIComponent(source.packageId)}`)}?type=${source.type}`;
  } else if (!data.packages.total) {
    label = 'Add an authorization package';
    explanation = 'Start with the authorization documents so SPin Agent can propose source-backed details for your review.';
    href = api.authorizationHref(offering.offeringId, 'import');
  } else if (data.packages.needsAttention || data.packages.processing) {
    label = 'Review package analysis';
    explanation = 'Check retained progress and any unfinished or unsupported source content. No analysis is restarted by opening this overview.';
    href = packages;
  } else if (data.capabilities.awaitingReview || data.capabilities.awaitingApproval) {
    label = 'Review capabilities';
    explanation = 'Review proposed protections and their responsibilities, then follow the existing approval and publication checks.';
    href = `${api.authorizationHref(offering.offeringId, 'inherited-coverage')}?task=capabilities`;
  } else if (!data.hosting.configured) {
    label = 'Configure hosting';
    explanation = 'Identify the provider environment and resources available through this offering.';
    href = `${api.authorizationHref(offering.offeringId, 'inherited-coverage')}?task=hosting`;
  } else if (data.packages.awaitingReview) {
    label = 'Review package analysis';
    explanation = 'Source records still need review. Reviewed claims and published capabilities are different outcomes.';
    href = packages;
  } else {
    label = 'View associations';
    explanation = 'Review which mission systems have associated this hosting scope and which published capabilities they selected.';
    href = `${api.authorizationHref(offering.offeringId, 'inherited-coverage')}?task=missions`;
  }
  return <section aria-label="Next action" className="space-y-3 rounded-lg border border-indigo-200 bg-indigo-50 p-5 dark:border-indigo-800 dark:bg-indigo-950">
    <h2 className="font-semibold">Next action</h2><p className={muted}>{explanation}</p>
    {href ? <Link className={`${buttonClass} inline-block`} to={href}>{label}</Link>
      : <button type="button" className={buttonClass} onClick={reviewRecords}>{label}</button>}
  </section>;
}

export function OfferingOverview({ offering: loadedOffering }: { offering: Offering }) {
  const [offering, setOffering] = useState(loadedOffering);
  const [authorizationPage, setAuthorizationPage] = useState(1);
  const [packagePage, setPackagePage] = useState(1);
  const [dialog, setDialog] = useState<'manual' | 'records' | null>(null);
  const [pending, setPending] = useState(false);
  const remote = useRemote(signal => api.getOfferingOverview(offering.offeringId, authorizationPage, packagePage, signal),
    [offering.offeringId, loadedOffering.revision, authorizationPage, packagePage]);
  const data = remote.data;
  useEffect(() => {
    if (data) setOffering(current => ({ ...current, revision: data.offeringRevision }));
  }, [data]);
  useEffect(() => {
    if (!data?.packages.processing || dialog) return;
    const timer = window.setTimeout(remote.retry, 5000);
    return () => window.clearTimeout(timer);
  }, [data, dialog, remote.retry]);
  const reviewRecords = () => setDialog('records');
  return <div className="space-y-5">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h2 className="text-xl font-semibold">Offering overview</h2>
        <p className={`mt-1 ${muted}`}>Review the source package, record its existing decision, and prepare reusable protections for Mission Owners.</p></div>
      <button type="button" className={secondaryButtonClass} disabled={remote.loading || pending} onClick={remote.retry}>Refresh overview</button>
    </div>
    <dl className="grid gap-4 rounded-lg bg-slate-50 p-5 text-sm dark:bg-gray-950 sm:grid-cols-3">
      <Fact label="ATO package">Uploaded documents SPin Agent analyzes.</Fact>
      <Fact label="Authorization record">The existing external decision described in those documents.</Fact>
      <Fact label="Security capabilities">Reusable protections you review and publish for Mission Owners.</Fact>
    </dl>
    {remote.loading && <p role="status" className={muted}>Loading offering overview...</p>}
    {remote.error && <div role="alert" className={`${errorClass} space-y-2`}>
      <h3 className="font-semibold">Offering overview unavailable</h3>
      <p>The required service could not load this offering. This does not mean no packages, authorizations or associations exist.</p>
      <button type="button" className="underline" onClick={remote.retry}>Retry overview</button>
      <details><summary className="cursor-pointer">Details</summary><p>{remote.error}</p></details>
    </div>}
    {data && <>
      <NextAction data={data} offering={offering} reviewRecords={reviewRecords} />
      <div className="grid items-start gap-5 xl:grid-cols-2">
        <Card title="Authorization" description="Record who authorized this offering, what the decision covers, and its dates and conditions.">
          {!data.authorizations.total ? <div className="space-y-2"><p className="font-semibold">Not recorded</p>
            <p className={muted}>No authorization decision is recorded here. This does not mean this offering has no ATO or that no package has been uploaded.</p></div>
            : <p className={muted}>{data.authorizations.recorded} recorded decisions; {data.authorizations.unconfirmed} drafts awaiting review; {data.authorizations.rejected} rejected records.</p>}
          {data.authorizations.items.map(record => <Authorization key={record.recordId} record={record} />)}
          <div className="flex flex-wrap gap-3">
            {data.authorizations.total > 0 && <button type="button" className={secondaryButtonClass} onClick={reviewRecords}>Review authorization records</button>}
            <button type="button" className={secondaryButtonClass} onClick={() => setDialog('manual')}>Record authorization manually</button>
          </div>
          <p className={muted}>Recording preserves reviewed source metadata. SPin Agent does not issue an ATO or independently verify external authority.</p>
          {data.authorizations.total > data.authorizations.pageSize && <Pager {...data.authorizations} onPage={setAuthorizationPage} />}
        </Card>
        <Card title="Package analysis" description="Uploaded source documents, retained analysis progress and records that still need review.">
          <p className={muted}>Source packages: {data.packages.total}. Extracted records awaiting review: {data.packages.awaitingReview}.</p>
          {!data.packages.total && <p className={muted}>No source packages associated with this offering.</p>}
          <ul className="space-y-4">{data.packages.items.map(({ package: item, awaitingReview }) => <li key={item.packageId} className="min-w-0 space-y-2 border-t pt-3 dark:border-gray-700">
            <h3 className="break-words font-semibold">{item.name}</h3>
            <p className={muted}>Analysis: {stateLabel(item.processingState)}. Publication: {stateLabel(item.publicationState)}.</p>
            <p className={muted}>{item.coverage.processed} of {item.coverage.total} source entries processed; {item.coverage.pending} pending; {item.coverage.failed + item.coverage.unreadable + item.coverage.unsupported} exceptions; {item.coverage.excluded} excluded.</p>
            {item.analysisProgress && <p className={muted}>{item.analysisProgress.completedSegments} of {item.analysisProgress.totalSegments} source segments analyzed</p>}
            <p className={muted}>{awaitingReview} extracted records awaiting review. Unfinished or excluded content is not verified coverage.</p>
            <Link className={linkClass} to={api.authorizationHref(offering.offeringId, `packages/${encodeURIComponent(item.packageId)}`)}>Review files and analysis</Link>
            {item.lastError && <details><summary className={`cursor-pointer ${linkClass}`}>Analysis issue</summary><p className="break-words text-sm">{item.lastError}</p></details>}
          </li>)}</ul>
          {data.packages.total > data.packages.pageSize && <Pager {...data.packages} onPage={setPackagePage} />}
          <Link className={linkClass} to={api.authorizationHref(offering.offeringId, 'packages')}>All package versions</Link>
        </Card>
        <Card title="Security capabilities" description="Reusable protections proposed from this offering and those already published for Mission Owner selection.">
          <dl className="grid grid-cols-3 gap-3">{([
            ['Proposed', data.capabilities.proposed], ['Awaiting approval', data.capabilities.awaitingApproval], ['Published', data.capabilities.published],
          ] as const).map(([label, value]) => <div key={label}>
            <dt className="text-xs text-slate-600 dark:text-slate-300">{label}</dt><dd data-metric={label} className="text-2xl font-semibold">{value}</dd>
          </div>)}</dl>
          <p className={muted}>{data.capabilities.awaitingReview} proposals still need review. Awaiting approval is the reviewed subset of unpublished proposals, not an additional total. {data.capabilities.archived} archived.</p>
          <Link className={linkClass} to={`${api.authorizationHref(offering.offeringId, 'inherited-coverage')}?task=capabilities`}>Review capabilities</Link>
        </Card>
        <Card title="Hosting and mission systems" description="The environment the CSP provides and the mission systems that have associated it.">
          <p className="font-semibold">{data.hosting.configured ? data.hosting.name : 'Hosting not configured'}</p>
          <p className={muted}>{data.hosting.scopeCount} configured resource scopes</p>
          <p className="text-sm font-medium">{data.hosting.associatedSystemCount} associated mission systems</p>
          <p className={muted}>{data.hosting.assignmentCount} hosting allocations</p>
          <p className={muted}>Association is not approval of workload coverage. Mission Owners must separately select applicable capabilities and review responsibilities.</p>
          <div className="flex flex-wrap gap-4">
            <Link className={linkClass} to={`${api.authorizationHref(offering.offeringId, 'inherited-coverage')}?task=hosting`}>Configure hosting</Link>
            <Link className={linkClass} to={`${api.authorizationHref(offering.offeringId, 'inherited-coverage')}?task=missions`}>View associations</Link>
          </div>
        </Card>
      </div>
      <details className={`${surfaceClass} p-4`}><summary className={`cursor-pointer ${linkClass}`}>Offering details</summary>
        <p className="mt-2 break-all text-sm">Offering ID: {offering.offeringId}<br />Revision: {data.offeringRevision}<br />Lifecycle: {offering.lifecycle}</p>
      </details>
    </>}
    {dialog && <SetupDialog busy={pending} onClose={() => setDialog(null)}
      title={dialog === 'manual' ? 'Record an existing authorization' : 'Review authorization records'}
      description="Document an existing external decision. This does not issue a new ATO. Save draft details, then explicitly review the source metadata before recording.">
      <DecisionPanel offering={offering} initialAction={dialog === 'manual' ? 'draft' : undefined}
        onPendingChange={setPending} onChanged={() => { setPending(false); remote.retry(); }}
        onRefreshOffering={async () => {
          const current = await api.getOffering(offering.offeringId);
          if (current.offeringId !== offering.offeringId || !Number.isSafeInteger(current.revision) || current.revision < 1)
            throw new Error('The offering response did not identify the requested current revision.');
          setOffering(current);
        }} />
    </SetupDialog>}
  </div>;
}
