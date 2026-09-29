import { useEffect, useRef, useState } from 'react';
import { listAllProviderRelationships, previewProviderRelationship, reviewProviderRelationship } from './api';
import type { ProviderRelationship, RelationshipPreview } from './types';
import { buttonClass, secondaryButtonClass } from '../workspace-operations/workspaceUi';
import { ScopeDetails } from './MissionTaskPresentation';

export default function ProviderScopeReview({ systemId, item, onRecorded, onBusyChange, onCancel }: {
  systemId: string; item: ProviderRelationship; onRecorded: (value: ProviderRelationship) => void;
  onBusyChange: (busy: boolean) => void; onCancel: () => void;
}) {
  const [determination, setDetermination] = useState<'' | 'Undetermined' | 'SeparateBoundaryConsumer'>('');
  const [rationale, setRationale] = useState('');
  const [preview, setPreview] = useState<RelationshipPreview | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const writing = useRef(false);
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);
  const start = () => { writing.current = true; setBusy(true); onBusyChange(true); setError(null); };
  const finish = () => {
    writing.current = false;
    if (mounted.current) { setBusy(false); onBusyChange(false); }
  };
  const prepare = async () => {
    if (writing.current || !determination || !rationale.trim()) return;
    start(); setPreview(null); setConfirmed(false);
    try {
      const current = (await listAllProviderRelationships(systemId)).find(row => row.relationshipId === item.relationshipId);
      if (!current || current.canReviewRelationship !== true || current.state === 'ExplicitlyCoveredByRecordedScope') {
        throw new Error('The relationship or your review permission changed. Refresh the scope before continuing.');
      }
      if (current.assignmentRevision !== item.assignmentRevision) throw new Error('The allocation changed. Close and reopen the scope to review its current revision.');
      if (!mounted.current) return;
      const result = await previewProviderRelationship(systemId, current.relationshipId!, {
        expectedRevision: current.revision, expectedAssignmentRevision: current.assignmentRevision,
        relationshipState: determination, rationale: rationale.trim(), evidence: [],
      });
      if (mounted.current) setPreview(result);
    } catch (reason) {
      if (mounted.current) setError(reason instanceof Error ? reason.message : 'Could not prepare the relationship review.');
    } finally { finish(); }
  };
  const record = async () => {
    if (writing.current || !preview || !preview.canReview || preview.blockers.length || !confirmed) return;
    start();
    try {
      const result = await reviewProviderRelationship(systemId, item.relationshipId!, {
        expectedRevision: preview.revision, previewId: preview.previewId, previewHash: preview.previewHash, rationale: rationale.trim(),
      });
      if (result.assignmentId !== item.assignmentId || result.state !== determination) throw new Error('The server did not confirm the selected scope determination. Refresh saved records.');
      if (mounted.current) onRecorded(result);
    } catch (reason) {
      if (mounted.current) {
        setError(`${reason instanceof Error ? reason.message : 'The review could not be confirmed.'} Prepare a fresh review before retrying.`);
        setPreview(null); setConfirmed(false);
      }
    } finally { finish(); }
  };
  const label = determination === 'SeparateBoundaryConsumer' ? 'Separate boundary consumer' : 'Undetermined — review still required';
  return <div className="space-y-4 text-sm">
    <p className="font-semibold">{item.providerName} · {item.offeringName}</p>
    <p>System: {item.systemName ?? systemId}. Allocation revision: {item.assignmentRevision}.</p>
    <ScopeDetails scopes={item.assignedScopes} />
    <p>This records the authorization relationship for this mission and provider allocation. It is separate from copying a deployment description.</p>
    <p className="rounded bg-amber-50 p-3 text-amber-900">Do not assume the provider’s authorization covers this mission. A covered-scope determination requires an assigned AO and exact authorization, boundary and source evidence.</p>
    {error && <p role="alert" className="rounded border border-red-200 bg-red-50 p-3 text-red-800">{error}</p>}
    {!preview ? <>
      <label className="block">Relationship determination
        <select aria-label="Relationship determination" className="mt-1 block w-full rounded border p-2" value={determination} disabled={busy}
          onChange={event => {
            const value = event.target.value;
            if (value === '' || value === 'Undetermined' || value === 'SeparateBoundaryConsumer') setDetermination(value);
          }}>
          <option value="">Choose a determination</option>
          <option value="SeparateBoundaryConsumer">Separate boundary consumer</option>
          <option value="Undetermined">Undetermined — keep review required</option>
        </select>
      </label>
      <p className="text-xs text-slate-500">Separate boundary consumer means the mission maintains its own authorization boundary while consuming this provider service. It does not grant inherited controls.</p>
      <label className="block">Review rationale
        <textarea className="mt-1 block w-full rounded border p-3" rows={4} maxLength={4000} required disabled={busy}
          value={rationale} onChange={event => setRationale(event.target.value)} />
      </label>
      <button type="button" className={buttonClass} disabled={busy || !determination || !rationale.trim()} onClick={() => void prepare()}>
        {busy ? 'Preparing…' : 'Prepare review'}
      </button>
    </> : <>
      <h3 className="font-semibold">Confirm relationship review</h3>
      <p>Determination: {label}</p>
      <p className="whitespace-pre-wrap break-words">Rationale: {rationale}</p>
      <p>Relationship revision: {preview.revision}. Allocation revision: {item.assignmentRevision}.</p>
      {!!preview.blockers.length && <div role="alert" className="space-y-2 rounded bg-amber-50 p-3 text-amber-900">
        <p>Resolve these source blockers before recording a review:</p>
        {preview.blockers.map((blocker, index) => <p key={index} className="break-words">{typeof blocker === 'string' ? blocker : JSON.stringify(blocker)}</p>)}
      </div>}
      {!preview.canReview && <p role="alert">The server did not permit recording this preview. Refresh and review the source context.</p>}
      <label className="flex items-start gap-2"><input type="checkbox" disabled={busy || !preview.canReview || !!preview.blockers.length}
        checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />I confirm this determination for the selected scope. This does not authorize the mission system.</label>
      <button type="button" className={buttonClass} disabled={busy || !confirmed || !preview.canReview || !!preview.blockers.length} onClick={() => void record()}>
        {busy ? 'Recording…' : 'Record relationship review'}
      </button>
      <button type="button" className={`${secondaryButtonClass} ml-2`} disabled={busy} onClick={() => { setPreview(null); setConfirmed(false); }}>Revise determination</button>
    </>}
    <button type="button" className={`${secondaryButtonClass} block`} disabled={busy} onClick={onCancel}>Cancel</button>
  </div>;
}
