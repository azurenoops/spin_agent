import { useEffect, useRef, useState } from 'react';
import { inputClass, Pager, secondaryButtonClass, Status, surfaceClass, useQueryState, useRemote } from '../workspace-operations/workspaceUi';
import { getPackageCandidates } from './api';
import { stateLabel } from './PackageReceipts';
import type { PackageCandidate, PackageSelection } from './types';
import { claimKinds } from './claims';

interface Props {
  packageId: string; revision: number; selected: PackageSelection[]; disabled: boolean;
  onSelect: (candidate: PackageCandidate, selected: boolean) => void;
  onReview: (candidate: PackageCandidate) => void; onRevisionChanged: () => void;
}

export function PackageCandidates({ packageId, revision, selected, disabled, onSelect, onReview, onRevisionChanged }: Props) {
  const { params } = useQueryState();
  const [page, setPage] = useState(() => Math.max(1, Number(params.get('page')) || 1));
  const [type, setType] = useState(() => params.get('type') ?? '');
  const [reviewState, setReviewState] = useState(() => params.get('reviewState') ?? '');
  const linkedCandidate = params.get('candidate');
  const openedCandidate = useRef<string | null>(null);
  const remote = useRemote(signal => getPackageCandidates(packageId, {
    page, pageSize: 25, type: type || undefined, reviewState: reviewState || undefined,
  }, signal), [packageId, page, type, reviewState, revision]);
  useEffect(() => {
    if (remote.data?.items.some(candidate => selected.some(item => item.candidateId === candidate.candidateId && item.revision !== candidate.revision))) onRevisionChanged();
  }, [remote.data, selected, onRevisionChanged]);
  useEffect(() => {
    if (disabled || !linkedCandidate || openedCandidate.current === linkedCandidate) return;
    const candidate = remote.data?.items.find(item => item.candidateId === linkedCandidate);
    if (candidate) { openedCandidate.current = linkedCandidate; onReview(candidate); }
  }, [remote.data, linkedCandidate, disabled, onReview]);

  return <section aria-label="Candidate records" className="space-y-3">
    <h2 className="text-lg font-semibold">Review extracted records</h2>
    <div className="flex flex-wrap gap-3">
      <label className="text-sm">Candidate type<select className={`${inputClass} ml-2`} disabled={disabled} value={type} onChange={event => { setType(event.target.value); setPage(1); }}>
        <option value="">All types</option>{['Component', 'Capability', 'ControlMapping', 'Responsibility', 'AuthorizationReference', ...claimKinds].map(value => <option key={value} value={value}>{stateLabel(value)}</option>)}
      </select></label>
      <label className="text-sm">Review state<select className={`${inputClass} ml-2`} disabled={disabled} value={reviewState} onChange={event => { setReviewState(event.target.value); setPage(1); }}>
        <option value="">All states</option>{['NeedsReview', 'Reviewed', 'Rejected', 'Approved', 'Published'].map(value => <option key={value} value={value}>{stateLabel(value)}</option>)}
      </select></label>
    </div>
    <p className="text-sm text-slate-600 dark:text-slate-300">Open a record to check it against its sources. Reviewed components and capabilities can then be selected for publication.</p>
    <details className="text-xs text-slate-500"><summary className="cursor-pointer">What can I publish?</summary><p className="mt-2">Select up to 100 reviewed components and capabilities with their dependencies, including selections across pages. Mappings, responsibilities, authorization references and claims support your review; they are not published separately.</p></details>
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && <>
      {!remote.data.items.length && <p className={`${surfaceClass} p-4`}>No candidates match these filters.</p>}
      {remote.data.items.map(candidate => {
        const eligible = ['Component', 'Capability'].includes(candidate.type)
          && ['Reviewed', 'Approved'].includes(candidate.reviewState) && !candidate.publishedRecordId;
        return <article key={candidate.candidateId} className={`${surfaceClass} flex min-w-0 flex-wrap items-start gap-3 p-4`}>
          <input className="mt-1" type="checkbox" aria-label={`Select ${candidate.name} revision ${candidate.revision}`}
            disabled={disabled || !eligible} checked={selected.some(item => item.candidateId === candidate.candidateId)}
            onChange={event => onSelect(candidate, event.target.checked)} />
          <div className="min-w-0 flex-1">
            <h3 className="break-words font-semibold">{candidate.name}</h3>
            <p className="text-sm text-gray-600">{stateLabel(candidate.type)} · {stateLabel(candidate.reviewState)} · Revision {candidate.revision} · {candidate.publishedRecordId ? 'Published record' : 'Unpublished import'}</p>
            <p className="text-xs text-gray-500">{candidate.citations.length} citations · {candidate.contributorIds.length} dependencies · {candidate.duplicateMatches.length} duplicate matches</p>
            <details className="mt-1 text-xs text-gray-500"><summary className="cursor-pointer">Record details</summary><code className="block break-all">{candidate.candidateId}</code></details>
          </div>
          <button type="button" className={secondaryButtonClass} disabled={disabled} aria-label={`Review ${candidate.name}`} onClick={() => onReview(candidate)}>Review record</button>
        </article>;
      })}
      <Pager {...remote.data} onPage={setPage} />
    </>}
  </section>;
}
