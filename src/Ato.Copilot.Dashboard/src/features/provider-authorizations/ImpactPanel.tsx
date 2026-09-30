import { useEffect, useState, type ReactNode } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { buttonClass, inputClass, message, Pager, secondaryButtonClass, Status, surfaceClass, useRemote, warningClass } from '../workspace-operations/workspaceUi';
import { PackageImportError } from '../package-imports/request';
import { Field, MutationForm } from './forms';
import * as api from './api';
import { getImpactDetails, type ImpactAffectedItem, type ImpactDetails } from './changeImpactApi';
import { ImpactContextEditor, type ImpactSource } from './ImpactContextEditor';
import { impactNeedsAction, impactOutcome, impactTitle } from './impactPresentation';
import type { ImpactInput, ImpactPreview, ImpactReview, Offering, Page } from './types';

type Disposition = 'AcceptForPublication' | 'RequestChanges' | 'Reject';
const isDisposition = (value: string): value is Disposition =>
  value === 'AcceptForPublication' || value === 'RequestChanges' || value === 'Reject';
const timestamp = (value: string | null | undefined) => value ? new Date(value).toLocaleString() : 'Not recorded';

function AffectedItems({ title, data, onPage, disabled }: {
  title: string; data: Page<ImpactAffectedItem>; onPage: (page: number) => void; disabled: boolean;
}) {
  return <section aria-label={title} className={`${surfaceClass} min-w-0 space-y-3 p-5`}>
    <h3 className="text-lg font-semibold">{title} <span className="text-sm font-normal">({data.total})</span></h3>
    <p className="text-sm text-slate-600 dark:text-slate-300">Recorded relationships at review time, shown with currently available names. This is not proof of a coverage change or a mission authorization decision.</p>
    {!data.items.length ? <p className="text-sm">None recorded in this review. This does not establish that no systems or responsibilities could be affected.</p>
      : <div className="overflow-x-auto"><table aria-label={title} className="w-full text-left text-sm">
        <thead className="bg-slate-50 text-xs text-slate-600 dark:bg-gray-800 dark:text-slate-300"><tr>
          <th scope="col" className="p-3">{title === 'Affected mission systems' ? 'System' : 'Capability'}</th>
          <th scope="col" className="p-3">Recorded relationship</th><th scope="col" className="p-3">Review action</th>
        </tr></thead>
        <tbody className="divide-y divide-slate-200 dark:divide-gray-700">{data.items.map((item, index) =>
          <tr key={`${item.kind}:${item.recordId}:${index}`} className="align-top">
            <th scope="row" className="max-w-xs break-words p-3 font-medium">{item.name ?? 'Name unavailable'}
              <details className="mt-2 text-xs font-normal"><summary className="cursor-pointer">Record details</summary>
                <dl className="mt-2 break-all"><dt>Record</dt><dd>{item.recordId}</dd><dt>Relationship type</dt><dd>{item.kind}</dd></dl>
              </details>
            </th>
            <td className="max-w-sm whitespace-pre-wrap break-words p-3">{item.summary}</td>
            <td className="max-w-xs break-words p-3"><p>{item.reviewState}</p>
              <p className="mt-2 text-slate-600 dark:text-slate-300">Review needed before relying on the proposed change.</p></td>
          </tr>)}</tbody>
      </table></div>}
    {data.total > data.pageSize && <fieldset disabled={disabled}><Pager {...data} onPage={onPage} /></fieldset>}
  </section>;
}

function ReviewContent({ details, capabilityPage, systemPage, disabled, actions }: {
  details: ImpactDetails | null; capabilityPage: (page: number) => void; systemPage: (page: number) => void; disabled: boolean;
  actions: ReactNode;
}) {
  const review = details?.review;
  const reviewerIsIdentifier = !!review?.reviewedBy && /^[0-9a-f]{8}-[0-9a-f-]{27}$/i.test(review.reviewedBy);
  return <div className="grid min-w-0 gap-6 xl:grid-cols-[minmax(0,1fr)_20rem]">
    <div className="min-w-0 space-y-5">
    {details && review && <>
    <section aria-label="What changes" className={`${surfaceClass} min-w-0 space-y-4 p-5`}>
      <h3 className="text-lg font-semibold">What changes</h3>
      <p className="text-sm text-slate-600 dark:text-slate-300">These are the retained change records, shown with currently available names and summaries. Before-and-after values were not retained; this is not a semantic comparison of releases.</p>
      {details.changes.length > 0 ? <ul className="grid gap-3">{details.changes.map(item => <li key={`${item.kind}:${item.recordId}`}
        className="min-w-0 space-y-2 rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm dark:border-amber-800 dark:bg-amber-950/30">
        <p className="text-xs font-semibold uppercase tracking-wide">Retained {item.kind} change · Revision {item.expectedRevision}</p>
        <p className="break-words font-semibold">{item.name ?? 'Change name unavailable'}</p>
        <p className="whitespace-pre-wrap break-words">{item.summary}</p>
        <details><summary className="cursor-pointer">Exact change record</summary>
          <dl className="mt-2 break-all text-xs"><dt>Record ID</dt><dd>{item.recordId}</dd>
            <dt>Proposed snapshot hash</dt><dd>{item.proposedSnapshotHash}</dd></dl>
        </details>
      </li>)}</ul> : <p className="text-sm">Item-level changes were not retained for this review.</p>}
      <section aria-label="Retained review context" className="space-y-3 border-t border-slate-200 pt-4 dark:border-gray-700">
        <h4 className="font-semibold">Retained review context</h4>
        <p className="text-sm">Exact source identifiers from this review—not substituted with current versions.</p>
        {details.context ? <dl className="grid gap-2 break-all text-sm sm:grid-cols-[auto_minmax(0,1fr)]">
          <dt>Offering revision</dt><dd>{details.context.expectedOfferingRevision}</dd>
          <dt>Boundary revision</dt><dd>{details.context.boundaryRevisionId}</dd>
          <dt>Hosting scope revision</dt><dd>{details.context.hostingScopeRevisionId ?? 'Not selected'}</dd>
          <dt>Authorization revisions</dt><dd>{details.context.authorizationRevisionIds.join(', ') || 'None selected; coverage remains unestablished.'}</dd>
          <dt>Package versions</dt><dd>{details.context.packageVersionIds.join(', ') || 'None selected'}</dd>
        </dl> : <p className="text-sm">Exact source selections were not retained for this review.</p>}
      </section>
      <details className="text-sm"><summary className="cursor-pointer font-medium">Details</summary>
        <dl className="mt-3 grid gap-1 break-all text-xs"><dt>Review ID</dt><dd>{review.reviewId}</dd>
          <dt>Review revision</dt><dd>{review.revision}</dd><dt>Context snapshot</dt><dd>{review.contextSnapshotHash}</dd>
          <dt>Created</dt><dd>{timestamp(details.createdAt)}</dd></dl>
        {details.context && <pre className="mt-3 whitespace-pre-wrap break-all text-xs">{JSON.stringify(details.context, null, 2)}</pre>}
      </details>
    </section>
    <AffectedItems title="Affected mission systems" data={details.affectedSystems} onPage={systemPage} disabled={disabled} />
    <AffectedItems title="Affected capabilities" data={details.affectedCapabilities} onPage={capabilityPage} disabled={disabled} />
    </>}
    </div>
    <aside aria-label="Provider review outcome" className="min-w-0 space-y-5">
    {details && review && <section aria-label="Review outcome" className="min-w-0 space-y-3 border-l-2 border-violet-200 pl-4 dark:border-violet-800">
      <h3 className="font-semibold">Review outcome</h3><p className="font-medium">{impactOutcome(review.disposition)}</p>
      <dl className="grid gap-1 text-sm"><dt>Reviewed by</dt><dd className="break-words">
        {review.reviewedBy ? reviewerIsIdentifier ? 'Reviewer name unavailable; identity retained in Details.' : review.reviewedBy : 'Awaiting a reviewer'}</dd>
        <dt>Reviewed on</dt><dd>{timestamp(review.reviewedAt)}</dd><dt>Decision rationale</dt>
        <dd className="whitespace-pre-wrap break-words">{details.rationale ?? 'No decision rationale recorded.'}</dd></dl>
      {reviewerIsIdentifier && <details className="text-xs"><summary className="cursor-pointer">Details</summary><p className="break-all">{review.reviewedBy}</p></details>}
    </section>}
    {actions}
    {details && review && <section aria-label="Required action" className={`${surfaceClass} min-w-0 space-y-3 p-5`}>
      <h3 className="text-lg font-semibold">Required action</h3>
      {review.stale && <p className={warningClass}>Changes detected since this review. Update the impact review before using it for publication.</p>}
      {details.blockers.length ? <ul className="list-disc space-y-2 pl-5 text-sm">{details.blockers.map((blocker, index) =>
        <li key={`${blocker.code}:${index}`} className="break-words">{blocker.message}</li>)}</ul>
        : <p className="text-sm">No blockers were recorded in this review. Review the affected relationships and responsibilities before deciding; the publication service checks the exact context again.</p>}
      <p className="text-sm">Acceptance records your impact decision only. It does not publish capabilities, establish workload coverage, or issue a mission ATO.</p>
    </section>}
    <section className="space-y-2 border-l-2 border-violet-200 pl-4 text-sm dark:border-violet-800">
      <h3 className="font-semibold">Change-to-document mapping</h3>
      <p>Review the implementation statement, customer duty, and supporting artifacts against these retained records. Specific document changes must be evaluated by each mission.</p>
    </section>
    <section className="space-y-2 border-l-2 border-violet-200 pl-4 text-sm dark:border-violet-800">
      <h3 className="font-semibold">Reauthorization</h3>
      <p>The appropriate authority determines whether reassessment or reauthorization is needed. A provider impact review is not a mission authorization decision.</p>
    </section>
    </aside>
  </div>;
}

export function ImpactPanel({ offering: suppliedOffering, source, initialReviewId, publicationHref }: {
  offering: Offering; onChanged: () => void; source?: ImpactSource; initialReviewId?: string; publicationHref?: string;
}) {
  const [refreshedOffering, setRefreshedOffering] = useState<Offering | null>(null);
  const offering = refreshedOffering && refreshedOffering.offeringId === suppliedOffering.offeringId
    && refreshedOffering.revision >= suppliedOffering.revision ? refreshedOffering : suppliedOffering;
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string | null>(initialReviewId ?? null);
  const [editor, setEditor] = useState<{ key: number; context?: ImpactInput | null; source?: ImpactSource } | null>(
    source ? { key: 0, source } : null);
  const [preview, setPreview] = useState<{ value: ImpactPreview; offeringRevision: number; input: ImpactInput } | null>(null);
  const [disposition, setDisposition] = useState<Disposition | ''>('');
  const [rationale, setRationale] = useState('');
  const [pendingPreview, setPendingPreview] = useState(false);
  const [pendingReview, setPendingReview] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [invalidated, setInvalidated] = useState(false);
  const [outcome, setOutcome] = useState<ImpactReview | null>(null);
  const [now, setNow] = useState(Date.now());
  const [capabilityPage, setCapabilityPage] = useState(1);
  const [systemPage, setSystemPage] = useState(1);
  const remote = useRemote(signal => api.listImpactReviews(offering.offeringId, page, signal), [offering.offeringId, page, offering.revision]);
  const details = useRemote(signal => selected
    ? getImpactDetails(offering.offeringId, selected, capabilityPage, systemPage, signal) : Promise.resolve(null),
  [offering.offeringId, selected, capabilityPage, systemPage, offering.revision, preview?.value.previewId, outcome?.revision]);
  const busy = pendingPreview || pendingReview || refreshing;
  const visibleDetails = details.data && preview ? {
    ...details.data, blockers: [...details.data.blockers, ...preview.value.blockers.filter(blocker =>
      !details.data?.blockers.some(item => item.code === blocker.code && item.message === blocker.message))],
  } : details.data;
  useEffect(() => {
    if (!preview) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [preview]);
  const stale = invalidated || !!details.data?.review.stale || (preview !== null && (preview.offeringRevision !== offering.revision
    || !Number.isFinite(Date.parse(preview.value.expiresAt)) || Date.parse(preview.value.expiresAt) <= now));
  const changed = () => { setPreview(null); setInvalidated(false); setOutcome(null); setDisposition(''); };
  const startReview = async (context?: ImpactInput | null) => {
    if (busy) return;
    setRefreshing(true); setRefreshError(null);
    try {
      const current = await api.getOffering(offering.offeringId);
      setRefreshedOffering(current); changed();
      if (!context) setRationale('');
      setEditor(previous => ({ key: (previous?.key ?? 0) + 1, context }));
    } catch (reason) { setRefreshError(`Current offering unavailable. ${message(reason)} Your current review is retained.`); }
    finally { setRefreshing(false); }
  };
  const submitPreview = async (input: ImpactInput, key: string) => {
    const value = await api.previewImpact(offering.offeringId, input, key);
    setNow(Date.now()); setPreview({ value, offeringRevision: input.expectedOfferingRevision, input });
    setSelected(value.reviewId); setCapabilityPage(1); setSystemPage(1); setInvalidated(false);
    setOutcome(null); setDisposition(''); remote.retry();
  };
  const submitReview = async () => {
    if (!preview || stale || !details.data || details.error || !disposition || !rationale.trim()
      || (disposition === 'AcceptForPublication' && (preview.value.blockers.length || details.data.blockers.length))) {
      throw new PackageImportError('A current impact assessment, explicit decision and rationale are required. Resolve blockers before acceptance.', 422);
    }
    try {
      const result = await api.reviewImpact(offering.offeringId, preview.value, disposition, rationale);
      setOutcome(result); setPendingReview(false); setPreview(null); setEditor(null); remote.retry();
    } catch (reason) {
      if (reason instanceof PackageImportError && [401, 403, 404, 409].includes(reason.status ?? 0)) setInvalidated(true);
      throw reason;
    }
  };
  const reviewCards = (reviews: ImpactReview[], title: string) => reviews.length > 0 && <section aria-label={title} className="space-y-3">
    <h3 className="font-semibold">{title}</h3>
    <div className="grid gap-3 lg:grid-cols-2">{reviews.map(review => <article key={review.reviewId} className={`${surfaceClass} min-w-0 space-y-3 p-4`}>
      <h4 className="break-words font-semibold">{impactTitle(review)}</h4>
      <p className="break-words text-sm">{review.summary ?? 'Review the recorded change, affected relationships and publication decision.'}</p>
      <p className="text-sm">{impactOutcome(review.disposition)}</p>
      {review.stale && <p className="text-sm font-medium text-amber-800 dark:text-amber-300">Changes detected since this review</p>}
      {review.affectedCounts && <p className="text-sm">{review.affectedCounts.capabilities} capabilities · {review.affectedCounts.systems} mission systems recorded</p>}
      <p className="text-xs text-slate-600 dark:text-slate-300">{review.reviewedAt ? `Reviewed ${timestamp(review.reviewedAt)}` : `Created ${timestamp(review.createdAt)}`}</p>
      <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => {
        changed(); setRationale(''); setEditor(null); setSelected(review.reviewId); setCapabilityPage(1); setSystemPage(1);
      }}>Review changes</button>
    </article>)}</div>
  </section>;
  return <section className="min-w-0 space-y-6">
    <header className={`${surfaceClass} min-w-0 space-y-4 p-5`}>
      <h2 className="text-xl font-semibold">{selected ? visibleDetails?.title ?? 'Review the proposed change' : 'Change impact'}</h2>
      {selected ? <p>Trace the proposed service change to customer duties and system documentation.</p> : <>
      <p>If I change this offering, which security capabilities and mission systems could be affected?</p>
      <p className="text-sm text-slate-600 dark:text-slate-300">Start from a revised package, boundary or saved capability. Review what is recorded, assess the affected relationships, then decide whether the change can proceed to publication.</p>
      <ol className="flex flex-wrap gap-x-6 gap-y-2 text-sm"><li>1. Review changes</li><li>2. Assess impact</li><li>3. Approve publication separately</li></ol>
      </>}
      {!editor && <button type="button" className={buttonClass} disabled={busy || remote.loading || !!remote.error}
        onClick={() => void startReview()}>{selected ? 'Start a different change' : 'Review a proposed change'}</button>}
    </header>
    <Status loading={remote.loading || refreshing} error={remote.error ? `Change reviews unavailable. ${remote.error}` : null} retry={remote.retry} />
    {refreshError && <p role="alert" className={warningClass}>{refreshError}</p>}
    {editor && <details open={!preview} className={`${surfaceClass} min-w-0 space-y-4 p-5`}>
      <summary className="cursor-pointer font-semibold">Change selection</summary>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm">Nothing is accepted or published by selecting these records.</p>
        <button type="button" disabled={busy} className={secondaryButtonClass} onClick={() => { changed(); setEditor(null); }}>Close change selection</button>
      </div>
      <ImpactContextEditor key={editor.key} offering={offering} initialContext={editor.context} source={editor.source}
        disabled={pendingReview || refreshing || remote.loading || !!remote.error} onChanged={changed}
        onPendingChange={setPendingPreview} onPreview={submitPreview} onReload={input => void startReview(input)} />
    </details>}
    {selected && <section aria-label="Selected impact review" className="min-w-0 space-y-4">
      <Status loading={details.loading} error={details.error ? `Impact analysis unavailable. ${details.error} No conclusion about affected systems can be made.` : null} retry={details.retry} />
      {visibleDetails && <>
        <section aria-label="Proposed change" className="min-w-0 space-y-2 rounded-lg border border-amber-200 bg-amber-50 p-4 dark:border-amber-800 dark:bg-amber-950/30">
          <h3 className="font-semibold">Proposed change</h3>
          <p className="whitespace-pre-wrap break-words text-sm">{visibleDetails.summary}</p>
          <p className="text-xs">Retained assessment · {impactOutcome(visibleDetails.review.disposition)}</p>
        </section>
        {editor && !preview && <p className="text-sm">Previous assessment shown below. Assess the selected changes again before recording a new decision.</p>}
      </>}
        <ReviewContent details={visibleDetails} capabilityPage={setCapabilityPage} systemPage={setSystemPage} disabled={busy} actions={<>
        {visibleDetails && !preview && !editor && <div className="space-y-3">
          {visibleDetails.context ? <button type="button" className={secondaryButtonClass} disabled={busy}
            onClick={() => void startReview(visibleDetails.context)}>
            Prepare fresh impact assessment
          </button> : <p className="text-sm">Exact source selections were not retained. Start a new review from the revised package, boundary or capability.</p>}
          <p className="text-sm">This saved review can be read, but its approval session cannot be restored. Prepare a fresh assessment and explicitly review its source selections before recording a new decision.</p>
        </div>}
      {preview && <section aria-label="Record review decision" className={`${surfaceClass} min-w-0 space-y-4 p-5`}>
        <h3 className="text-lg font-semibold">Record provider impact review</h3>
        <p className="text-sm">Review the retained changes, sources and affected relationships before deciding. Acceptance does not publish the change.</p>
        {stale && <p className={warningClass}>Changes detected or this assessment expired. Review the selected versions and assess impact again. Your rationale is retained.</p>}
        {stale && <button type="button" className={secondaryButtonClass} disabled={busy}
          onClick={() => void startReview(preview.input)}>Update impact review</button>}
        <MutationForm label="Record provider impact review" onPendingChange={setPendingReview} submit={submitReview} onSaved={() => undefined}
          disabled={pendingPreview || refreshing || (!pendingReview && (stale || details.loading || !!details.error || !details.data))}
          submitDisabled={!disposition || !rationale.trim() || (disposition === 'AcceptForPublication'
            && !!(preview.value.blockers.length || details.data?.blockers.length))}>
          <label className="grid gap-1 text-sm">Review decision<select className={inputClass} value={disposition}
            onChange={event => setDisposition(isDisposition(event.target.value) ? event.target.value : '')}>
            <option value="">Choose a decision</option><option value="AcceptForPublication">Accept impact for publication</option>
            <option value="RequestChanges">Request changes</option><option value="Reject">Reject change</option>
          </select></label>
          <Field label="Review rationale" value={rationale} onChange={setRationale} multiline required maxLength={4000} />
        </MutationForm>
      </section>}
        </>} />
      {outcome && <div role="status" className={`${surfaceClass} space-y-3 p-4`}>
        <p>Review decision saved. No publication was performed.</p>
        {outcome.disposition === 'AcceptForPublication' && !outcome.stale && publicationHref
          && <Link to={publicationHref} className={`${secondaryButtonClass} inline-block`}>Continue to publication review</Link>}
      </div>}
    </section>}
    {remote.data && <details open={!selected} className={`${surfaceClass} min-w-0 space-y-4 p-5`}>
      <summary className="cursor-pointer font-semibold">Review history</summary>
      <section aria-label="Recorded impact reviews" className="space-y-4">
      <h2 className="text-lg font-semibold">Track impact reviews</h2>
      {!remote.data.items.length && <p>No impact reviews recorded. Start from a proposed change to assess its impact.</p>}
      {reviewCards(remote.data.items.filter(impactNeedsAction), 'Active reviews')}
      {reviewCards(remote.data.items.filter(review => !impactNeedsAction(review)), 'Previous outcomes')}
      <fieldset disabled={busy}><Pager {...remote.data} onPage={setPage} /></fieldset>
      </section>
    </details>}
  </section>;
}
