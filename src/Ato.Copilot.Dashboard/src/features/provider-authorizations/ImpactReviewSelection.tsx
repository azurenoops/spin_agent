import { useState } from 'react';
import { errorClass, Pager, secondaryButtonClass, Status, useRemote } from '../workspace-operations/workspaceUi';
import { Link } from '../workspaces/workspaceNavigation';
import { changeImpactHref, listImpactReviews } from './api';
import { OfferingPicker } from './OfferingIntake';
import { impactOutcome, impactTitle } from './impactPresentation';

export const impactReviewIds = (value: string) => value.split('\n').map(item => item.trim()).filter(Boolean);
export function impactSelectionError(value: string): string | null {
  const ids = impactReviewIds(value);
  if (ids.length > 100) return 'Select at most 100 change impact reviews.';
  if (new Set(ids.map(id => id.toLowerCase())).size !== ids.length) return 'Each change impact review must be selected only once.';
  return null;
}
export function ImpactReviewSelection({ value, onChange, disabled, offeringId, capabilityId, packageVersionId, packageId, boundaryRevisionId }: {
  value: string; onChange: (value: string) => void; disabled: boolean; offeringId?: string;
  capabilityId?: string; packageVersionId?: string; packageId?: string; boundaryRevisionId?: string;
}) {
  const [chosenOffering, setChosenOffering] = useState('');
  const [names, setNames] = useState<Record<string, string>>({});
  const [page, setPage] = useState(1);
  const selectedOffering = offeringId ?? chosenOffering;
  const selected = impactReviewIds(value);
  const error = impactSelectionError(value);
  const remote = useRemote(signal => selectedOffering ? listImpactReviews(selectedOffering, page, signal) : Promise.resolve(null), [selectedOffering, page]);
  return <section aria-label="Change impact for publication" className="min-w-0 space-y-3 text-sm">
    <h3 className="font-semibold">Change impact</h3>
    <p>Review the effect of the saved change before publication. Select accepted, current reviews by name; the server verifies that they match every affected offering and the exact proposed change.</p>
    {!offeringId && <fieldset disabled={disabled} className="min-w-0 space-y-2">
      <OfferingPicker selected={chosenOffering} disabled={disabled} onSelect={id => { setChosenOffering(id); setPage(1); }} />
      <p className="text-xs text-slate-600 dark:text-slate-300">Choose a linked offering to review its impact. Unrelated legacy catalog drafts do not require an offering association. Reviews selected from other offerings are retained.</p>
    </fieldset>}
    {selected.length > 0 && <ul aria-label="Selected impact reviews" className="space-y-2">{selected.map((id, index) =>
      <li key={id} className="space-y-1 rounded border border-slate-200 p-2 dark:border-gray-700">
        <p className="break-words">{names[id] ?? remote.data?.items.find(item => item.reviewId === id)?.title ?? 'Selected review; name unavailable'}</p>
        <button type="button" className="text-indigo-700 underline dark:text-indigo-300" disabled={disabled}
          onClick={() => onChange(selected.filter(item => item !== id).join('\n'))}>Remove selected review {index + 1}</button>
        <details className="text-xs"><summary className="cursor-pointer">Details</summary><p className="break-all">{id}</p></details>
      </li>)}</ul>}
    {selectedOffering && <>
      <Status loading={remote.loading} error={remote.error ? `Impact reviews unavailable. ${remote.error}` : null} retry={remote.retry} />
      {remote.data && <fieldset disabled={disabled} className="min-w-0 space-y-3">
        <legend className="mb-2 font-medium">Available reviews</legend>
        {!remote.data.items.length && <p>No impact reviews recorded for this offering. Start with Review changes.</p>}
        {remote.data.items.map(review => {
          const eligible = review.disposition === 'AcceptForPublication' && !review.stale;
          return <label key={review.reviewId} className="flex items-start gap-2">
            <input type="checkbox" className="mt-1" checked={selected.includes(review.reviewId)}
              disabled={!selected.includes(review.reviewId) && (!eligible || selected.length >= 100)}
              onChange={event => {
                setNames(previous => ({ ...previous, [review.reviewId]: impactTitle(review) }));
                onChange((event.target.checked ? [...selected, review.reviewId] : selected.filter(id => id !== review.reviewId)).join('\n'));
              }} />
            <span className="min-w-0 break-words"><span className="font-medium">{impactTitle(review)}</span>
              <span className="block text-xs">{review.stale ? 'Changes detected since this review' : impactOutcome(review.disposition)}</span></span>
          </label>;
        })}
        {remote.data.total > remote.data.pageSize && <Pager {...remote.data} onPage={setPage} />}
        <button type="button" className={secondaryButtonClass} onClick={remote.retry}>Refresh impact reviews</button>
      </fieldset>}
      {!disabled && <Link className="inline-block font-semibold text-indigo-700 underline dark:text-indigo-300"
        to={changeImpactHref(selectedOffering, { capabilityId, packageVersionId, packageId, boundaryRevisionId })}>Review changes</Link>}
    </>}
    {error && <p role="alert" className={errorClass}>{error}</p>}
  </section>;
}
