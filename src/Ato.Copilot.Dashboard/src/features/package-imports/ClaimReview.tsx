import { useState, type ReactNode } from 'react';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import { Field, MutationForm } from '../provider-authorizations/forms';
import { errorClass, inputClass, secondaryButtonClass, surfaceClass, warningClass } from '../workspace-operations/workspaceUi';
import { packageArtifactUrl, reviewPackageClaim } from './api';
import { stateLabel } from './PackageReceipts';
import type { ClaimResolution, ClaimReviewInput } from './claims';
import type { PackageCandidate } from './types';

function ClaimValue({ value }: { value: unknown }): ReactNode {
  if (value === null || value === undefined) return <span className="text-slate-500">Not stated</span>;
  if (Array.isArray(value)) return value.length ? <ul className="list-inside list-disc">{value.map((item, index) => <li key={index}><ClaimValue value={item} /></li>)}</ul> : <span>None stated</span>;
  if (typeof value === 'object') return <dl>{Object.entries(value).map(([key, item]) => <div key={key}><dt className="text-xs text-slate-500">{stateLabel(key)}</dt><dd><ClaimValue value={item} /></dd></div>)}</dl>;
  return <span className="break-words whitespace-pre-wrap">{String(value)}</span>;
}

export function ClaimReview({ packageId, candidate, onCancel, onSaved, onReload }: {
  packageId: string; candidate: PackageCandidate; onCancel: () => void; onSaved: () => void; onReload: () => void;
}) {
  const claim = candidate.claim;
  const [action, setAction] = useState<ClaimReviewInput['action']>('Reviewed');
  const [rationale, setRationale] = useState('');
  const [pending, setPending] = useState(false);
  const [resolutions, setResolutions] = useState<ClaimResolution[]>([]);
  const changeResolution = (index: number, fields: Partial<ClaimResolution>) => setResolutions(current => {
    const existing = current.find(item => item.relationshipIndex === index);
    return [...current.filter(item => item.relationshipIndex !== index), {
      relationshipIndex: index, targetKind: '', targetId: '', expectedTargetRevision: 1, ...existing, ...fields,
    }];
  });
  return <section aria-label="Source claim review" className={`${surfaceClass} space-y-4 p-5`}>
    <h2 className="text-lg font-semibold">Review source claim</h2>
    <p className={warningClass}>Review confirms source metadata only. It does not record an external decision, establish a boundary, create or close a finding, or publish inventory.</p>
    <p className="text-sm">{candidate.name} · {stateLabel(candidate.type)} · Exact revision {candidate.revision}</p>
    {!claim ? <p role="alert" className={errorClass}>The typed source claim is unavailable. Reload before reviewing; no inventory editor can substitute for this claim.</p> : <>
      {(['authorizationDecision', 'boundary', 'assessmentFinding', 'poamItem'] as const).map(section => claim[section] && <section key={section} className="rounded border p-3">
        <h3 className="font-semibold">{stateLabel(section)}</h3><ClaimValue value={claim[section]} />
      </section>)}
      {!!claim.qualifications.length && <section><h3 className="font-semibold">Source limitations</h3><ul className="list-inside list-disc text-sm">{claim.qualifications.map((value, index) => <li key={index}>{value}</li>)}</ul></section>}
      <p className="break-words text-xs text-slate-600">Explicit source aliases: {claim.sourceAliases.join(', ') || 'None stated'}</p>
      <section className="space-y-3"><h3 className="font-semibold">Immutable supporting citations</h3>
        {candidate.citations.map((citation, index) => <blockquote key={index} className="border-l-2 border-indigo-200 pl-3 text-sm">
          <p className="break-words whitespace-pre-wrap">{citation.quote}</p>
          <p className="break-all text-xs text-slate-500">Citation {index + 1}: {citation.archivePath} · {citation.locator}</p>
          <p className="break-words text-xs text-slate-500">Supports: {claim.fieldSources.filter(binding => binding.citationIndexes.includes(index)).map(binding => binding.field).join(', ') || 'No field binding reported'}</p>
          <AuthenticatedDownload className="text-indigo-700 underline" url={packageArtifactUrl(packageId, citation.artifactId)} fileName={citation.archivePath}>Download cited source</AuthenticatedDownload>
        </blockquote>)}
      </section>
      <MutationForm label={action === 'Reviewed' ? 'Record claim review' : 'Reject source claim'} onSaved={onSaved} onPendingChange={setPending}
        submitDisabled={!rationale.trim() || resolutions.some(item => !item.targetId.trim() || !item.targetKind.trim() || !Number.isSafeInteger(item.expectedTargetRevision) || item.expectedTargetRevision < 1)}
        submit={key => reviewPackageClaim(packageId, candidate.candidateId, {
          expectedCandidateRevision: candidate.revision, action, rationale, resolutions,
        }, key)}>
        <fieldset className="space-y-3"><legend className="font-semibold">Source relationships</legend>
          {!claim.relationships.length && <p className="text-sm text-slate-600">No relationships stated.</p>}
          {claim.relationships.map((relationship, index) => {
            const resolution = resolutions.find(item => item.relationshipIndex === index);
            return <div key={index} className="space-y-2 rounded border p-3 text-sm">
              <p className="break-all">{stateLabel(relationship.kind)} → {relationship.targetSourceId} · {relationship.resolution}</p>
              {relationship.targetEntryId && <p className="break-all text-xs">Source entry: {relationship.targetEntryId}</p>}
              <label className="flex items-center gap-2"><input type="checkbox" checked={!!resolution} onChange={event => event.target.checked
                ? changeResolution(index, {}) : setResolutions(current => current.filter(item => item.relationshipIndex !== index))} />Resolve this relationship to an exact retained record</label>
              {resolution && <div className="grid gap-2 sm:grid-cols-2">
                <Field label={`Target kind ${index + 1}`} value={resolution.targetKind} onChange={value => changeResolution(index, { targetKind: value })} required />
                <Field label={`Target ID ${index + 1}`} value={resolution.targetId} onChange={value => changeResolution(index, { targetId: value })} required />
                <Field label={`Expected target revision ${index + 1}`} type="number" value={String(resolution.expectedTargetRevision)} onChange={value => changeResolution(index, { expectedTargetRevision: Number(value) })} required />
              </div>}
            </div>;
          })}
          <p className="text-xs text-slate-600">Unresolved or ambiguous relationships remain explicit blockers. The server validates target ownership, kind and exact revision.</p>
        </fieldset>
        <label className="grid gap-1 text-sm">Claim review decision<select className={inputClass} value={action} onChange={event => setAction(event.target.value === 'Rejected' ? 'Rejected' : 'Reviewed')}>
          <option value="Reviewed">Reviewed source metadata</option><option value="Rejected">Reject proposal</option>
        </select></label>
        <Field label="Claim review rationale" value={rationale} onChange={setRationale} required multiline />
      </MutationForm>
    </>}
    <div className="flex flex-wrap gap-2"><button type="button" disabled={pending} className={secondaryButtonClass} onClick={onCancel}>Close claim review</button>
      <button type="button" disabled={pending} className={secondaryButtonClass} onClick={onReload}>Reload current source claim</button></div>
  </section>;
}
