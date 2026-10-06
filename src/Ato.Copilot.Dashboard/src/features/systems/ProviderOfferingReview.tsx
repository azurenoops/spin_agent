import { useRef, useState, type ReactNode } from 'react';
import type { SystemProviderScope } from '../../api/systemEnvironments';
import { collectProviderPages, listAllProviderRelationships, listApplicableProviderCapabilities } from '../provider-relationships/api';
import type { ApplicableProviderCapability, ProviderRelationship } from '../provider-relationships/types';
import { Link } from '../workspaces/workspaceNavigation';
import { buttonClass, secondaryButtonClass, Status, useRemote } from '../workspace-operations/workspaceUi';
import { offeringReviewPresentation, offeringWorkflowContext, prerequisiteLabel } from './providerOfferingPresentation';

const applicabilityLabel = (state: string) => ({
  Applicable: 'Published applicable', ReviewRequired: 'Review required', PendingReview: 'Pending review',
  NotApplicable: 'Not applicable', Blocked: 'Blocked', Unavailable: 'Unavailable',
})[state] ?? 'Unresolved';

export default function ProviderOfferingReview({ systemId, item, busy, source, renderRelationship, children }: {
  systemId: string; item: SystemProviderScope; busy: boolean; source: ReactNode; children: ReactNode;
  renderRelationship: (current: ProviderRelationship | undefined, onCancel: () => void) => ReactNode;
}) {
  const [review, setReview] = useState(false);
  const [reviewRecord, setReviewRecord] = useState<ProviderRelationship | null>(null);
  const [inspection, setInspection] = useState(false);
  const sourceDetails = useRef<HTMLDetailsElement>(null);
  const capabilityDetails = useRef<HTMLDetailsElement>(null);
  const reviewHeading = useRef<HTMLHeadingElement>(null);
  const relationship = useRemote(signal => listAllProviderRelationships(systemId, signal), [systemId, item.assignmentId]);
  const applicability = useRemote(signal => collectProviderPages(page => listApplicableProviderCapabilities(systemId, {
    page, assignmentId: item.assignmentId, offeringId: item.offeringId,
  }, signal), row => `${row.capabilityId}:${row.releaseId}`, row => row.assignmentId === item.assignmentId
    && row.offeringId === item.offeringId && row.assignmentRevision === item.assignmentVersion,
  'Applicability records', signal), [systemId, item.assignmentId, item.offeringId, item.assignmentVersion]);
  const current = relationship.data?.find(row => row.assignmentId === item.assignmentId && row.offeringId === item.offeringId);
  const stale = current && (current.assignmentRevision !== item.assignmentVersion || current.relationshipId !== item.relationshipId);
  const error = relationship.error ?? applicability.error
    ?? (stale ? 'The relationship changed. Close and reopen the captured offering before editing.' : null);
  const loading = relationship.loading || applicability.loading;
  const presentation = offeringReviewPresentation({ item, relationship: stale ? undefined : current,
    capabilities: applicability.data ?? undefined, loading, error });
  const context = offeringWorkflowContext(item);
  const base = `/systems/${encodeURIComponent(systemId)}`;
  const workflow = `${base}/profile/EnvironmentAndDeployment/hosting?${context}`;
  const responsibilities = `${base}/inheritance/subscriptions?${context}`;
  const reveal = (element: HTMLDetailsElement | null) => {
    if (!element) return;
    element.open = true; element.querySelector('summary')?.focus();
  };
  const startReview = () => {
    if (!current || current.canReviewRelationship !== true || error || loading || busy) return;
    setReviewRecord(value => value ?? current);
    setReview(true);
    requestAnimationFrame(() => reviewHeading.current?.focus());
  };
  const primary = presentation.action.kind === 'connect'
    ? busy ? <button type="button" className={buttonClass} disabled>{presentation.action.label}</button>
      : <Link className={buttonClass} to={workflow}>{presentation.action.label}</Link>
    : presentation.action.kind === 'responsibility'
      ? busy ? <button type="button" className={buttonClass} disabled>{presentation.action.label}</button>
        : <Link className={buttonClass} to={responsibilities}>{presentation.action.label}</Link>
      : <button type="button" className={buttonClass} disabled={busy || loading}
        onClick={() => {
          if (presentation.action.kind === 'relationship') startReview();
          else if (presentation.action.kind === 'refresh') { relationship.retry(); applicability.retry(); }
          else if (presentation.action.kind === 'source') reveal(sourceDetails.current);
          else if (presentation.action.kind === 'applicability') reveal(capabilityDetails.current);
          else setInspection(true);
        }}>{presentation.action.label}</button>;
  const count = item.publishedDuties?.totalCapabilities
    ?? (item.publishedDuties?.state === 'Available' ? item.publishedDuties.capabilities.length : null);
  const recordLink = <Link className="underline" to={responsibilities}>
    {item.responsibilityReview?.canReview === true ? 'Review responsibilities' : 'View responsibilities'}
  </Link>;
  return <div className="space-y-4 text-sm">
    <header className="space-y-1"><h3 className="text-xl font-semibold">{item.offeringName}</h3>
      <p className="font-medium">{presentation.overall}</p></header>
    <section aria-label="Next review action" className="space-y-3 rounded bg-indigo-50 p-4 dark:bg-indigo-950">
      <h4 className="font-semibold">Next: {presentation.action.label.toLowerCase()}</h4>
      <p>{presentation.reason}</p>
      <div className="flex flex-wrap">{primary}</div>
    </section>
    <Status loading={loading} error={error} />
    <dl aria-label="Review status" className="space-y-3">
      {([
        ['System relationship', presentation.relationship],
        ['Capability applicability', presentation.applicability],
        ['System adoption', presentation.adoption],
        ['Responsibility review', presentation.responsibility],
      ] as const).map(([label, value]) => <div key={label} className="flex flex-wrap justify-between gap-2 border-b pb-2">
        <dt className="font-medium">{label}</dt><dd>{value}</dd>
      </div>)}
    </dl>
    {reviewRecord && <section hidden={!review} className="space-y-3">
      <h4 ref={reviewHeading} tabIndex={-1} className="font-semibold">System relationship review</h4>
      <fieldset disabled={busy || !!error || loading}>{renderRelationship(reviewRecord, () => setReview(false))}</fieldset>
    </section>}
    {inspection && <section aria-label="Recorded review inspection" className="space-y-3 rounded border p-3">
      <p>{item.responsibilityReview?.reason ?? 'Inspect the canonical matrix for recorded system decisions.'}</p>
      {current?.reviewedAt && <p>Relationship reviewed: {current.reviewedAt}</p>}
      {recordLink}
      <button type="button" className={`${secondaryButtonClass} block`} onClick={() => reveal(sourceDetails.current)}>Inspect source and prerequisites</button>
    </section>}
    <details ref={capabilityDetails}><summary className="cursor-pointer">What&apos;s included · {count === null ? 'Count unavailable' : `${count} ${count === 1 ? 'capability' : 'capabilities'}`}</summary>
      <div className="space-y-3 pt-3">
        <p>Published scope and capability releases are read-only. Published applicability and duties are not system adoption or accepted responsibilities.</p>
        <OfferingCapabilities item={item} capabilities={applicability.data ?? []} />
        <Link className="block underline" to={workflow}>Review applicability and capability adoption</Link>
      </div>
    </details>
    <details ref={sourceDetails}><summary className="cursor-pointer">Source details and prerequisites</summary>
      <div className="space-y-3 pt-3">
        {source}
        {presentation.sourceChanged && <p role="alert">Current publication differs from the captured source. The selected releases have not been replaced.</p>}
        {!!presentation.blockers.length && <section aria-label="Shared source prerequisites" className="space-y-2">
          <h4 className="font-semibold">Shared source prerequisites</h4>
          {presentation.blockers.map(blocker => <div key={blocker.code}>
            <p>{blocker.label}</p>
            <p className="text-xs">Affected capabilities: {blocker.capabilityIds.map(id =>
              applicability.data?.find(row => row.capabilityId === id)?.capabilityName || 'Name unavailable').join(', ')}</p>
          </div>)}
        </section>}
        {!loading && !error && !current && <p>The exact relationship record is unavailable. Refresh the source before continuing.</p>}
        {!loading && !error && applicability.data?.length === 0 && <p>No applicable capability entries returned for this relationship. No applicability is inferred from scope selection.</p>}
        <p>{item.responsibilityReview?.reason ?? 'Responsibility review is unavailable here.'}</p>
        <p>{item.responsibilityReview?.canReview
          ? item.responsibilityReview.canConfirm ? 'Review and confirmation are available in the responsibility matrix.' : 'Review is available; confirmation is not currently permitted.'
          : 'View only. Responsibility review and confirmation are not permitted here.'}</p>
        {recordLink}
        {item.responsibilityReview?.canConfirm === true && <Link className="block underline" to={responsibilities}>Confirm responsibilities</Link>}
        <details><summary>Raw applicability diagnostics</summary>
          <div className="space-y-3 pt-3">{applicability.data?.map(entry => <article key={`${entry.capabilityId}:${entry.releaseId}`}>
            <p>{entry.capabilityName || 'Capability name unavailable'} · Release {entry.releaseRevision}</p>
            <p>Applicability: {entry.applicabilityState}</p>
            {entry.reasonCodes.map(code => <p key={code}>{code}</p>)}
            {entry.outstandingDecisions.map(code => <p key={code}>{code}</p>)}
            <p>{entry.canProposeAdoption ? 'The server permits proposing adoption in Security capabilities.' : 'The server does not currently permit proposing adoption.'}</p>
            <p>Release: {entry.releaseId}</p><p>Release snapshot: {entry.releaseSnapshotHash}</p>
            <p>Applicability context snapshot: {entry.applicability.snapshotHash}</p>
          </article>)}</div>
        </details>
      </div>
    </details>
    <details><summary className="cursor-pointer">Manage provider relationship</summary>
      <div className="space-y-3 pt-3">
        <button type="button" className={secondaryButtonClass} disabled={busy || !!error || loading
          || current?.canReviewRelationship !== true || !current.relationshipId}
          onClick={startReview}>Review provider relationship</button>
        {!item.relationshipId && <p>This selected scope has no recorded system relationship determination.</p>}
        {!current?.canReviewRelationship && <p>The server does not permit relationship review for this record and identity.</p>}
        {current?.canAssociate && !current.relationshipId && <Link className="block underline" to={workflow}>Associate provider relationship</Link>}
      </div>
    </details>
    {children}
  </div>;
}

function OfferingCapabilities({ item, capabilities }: { item: SystemProviderScope; capabilities: ApplicableProviderCapability[] }) {
  const [page, setPage] = useState(1);
  const duties = item.publishedDuties;
  const published = duties?.capabilities ?? [];
  const missing = duties?.unavailableCapabilities ?? [];
  const entries = [...published.map(source => ({ source, missing: null })), ...missing.map(source => ({ source: null, missing: source }))];
  const pageSize = 10;
  return <section aria-label="Published provider duties" className="space-y-3">
    {duties?.state !== 'Available' && <p role="status">{duties?.reason ?? 'Published duties are unavailable for this pinned scope. No duties have been inferred.'}</p>}
    {!!missing.length && <p>{missing.length} {missing.length === 1 ? 'capability has' : 'capabilities have'} missing published duty content.</p>}
    {!entries.length && duties?.totalCapabilities === 0 && <p>No capabilities are bound to this published scope.</p>}
    {entries.slice((page - 1) * pageSize, page * pageSize).map(({ source, missing: absent }) => {
      const entry = source ?? absent!;
      const name = entry.capabilityName?.trim() || 'Capability name unavailable';
      const current = capabilities.find(row => row.capabilityId === entry.capabilityId);
      return <details key={`${entry.capabilityId}:${entry.releaseId}`} className="rounded border p-3">
        <summary>{name}</summary>
        <article className="space-y-2 pt-3">
          <h5 className="font-semibold">{name}</h5>
          {source?.description && <p className="whitespace-pre-wrap">{source.description}</p>}
          {absent && <p>{absent.reason}</p>}
          <p>Published capability release: {entry.releaseRevision}</p>
          {source && ([
            ['Provider control responsibilities', source.providerControlIds],
            ['Shared control responsibilities', source.sharedControlIds],
            ['Customer control responsibilities', source.customerControlIds],
          ] as const).filter(([, controls]) => controls.length).map(([label, controls]) =>
            <p key={label}><strong>{label}:</strong> {controls.join(', ')}</p>)}
          {source && !source.providerControlIds.length && !source.sharedControlIds.length && !source.customerControlIds.length
            && <p>Published control-duty allocations are not recorded.</p>}
          {current && <><p>Current published applicability: {applicabilityLabel(current.applicabilityState)}</p>
            {current.releaseId !== entry.releaseId && <p>Current published release {current.releaseRevision} differs from this captured release.</p>}
            {current.reasonCodes.map(code => <p key={code}>{prerequisiteLabel(code)}</p>)}
            <p>{current.canProposeAdoption ? 'Adoption proposal permitted; not a responsibility decision.' : 'Adoption proposal is not currently permitted by the server.'}</p></>}
          <details><summary>Pinned published source</summary><div className="space-y-1 break-words pt-2">
            <p>Capability: {entry.capabilityId}</p><p>Release: {entry.releaseId}</p>
            {source && <><p>Release snapshot hash: {source.releaseSnapshotHash}</p><p>Content hash: {source.contentHash}</p>
              <p>Applicability context: {source.applicabilityContextId}</p></>}
          </div></details>
        </article>
      </details>;
    })}
    {entries.length > pageSize && <nav aria-label="Published capability pagination" className="flex flex-wrap items-center gap-3">
      <button type="button" className={secondaryButtonClass} disabled={page === 1} onClick={() => setPage(value => value - 1)}>Previous capabilities</button>
      <span>Page {page} of {Math.ceil(entries.length / pageSize)}</span>
      <button type="button" className={secondaryButtonClass} disabled={page * pageSize >= entries.length} onClick={() => setPage(value => value + 1)}>Next capabilities</button>
    </nav>}
    <p>Complete operational duty text and capability-specific exclusions are unavailable in this projection. An authorized provider source review is needed to inspect missing content; no duties or exclusions have been inferred.</p>
  </section>;
}
