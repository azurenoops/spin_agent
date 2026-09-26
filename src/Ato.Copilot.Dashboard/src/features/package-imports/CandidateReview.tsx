import { useEffect, useRef, useState } from 'react';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import { buttonClass, errorClass, inputClass, message, secondaryButtonClass, surfaceClass, warningClass } from '../workspace-operations/workspaceUi';
import { editPackageCandidate, packageArtifactUrl } from './api';
import { PackageImportError } from './request';
import type { EditPackageCandidate, PackageAuthorizationReference, PackageCandidate } from './types';

interface Props {
  packageId: string; candidate: PackageCandidate; onSaved: (candidate: PackageCandidate) => void;
  onCancel: () => void; onReload: () => void;
}
const componentTypes = ['Infrastructure', 'Platform', 'Service', 'Identity', 'Network', 'Storage', 'Compute'];
const duties = ['Provider', 'Shared', 'Customer'];

export function CandidateReview({ packageId, candidate, onSaved, onCancel, onReload }: Props) {
  const [draft, setDraft] = useState(candidate);
  const [mappings, setMappings] = useState(Object.entries(candidate.controlDuties).map(([controlId, duty]) => ({ controlId, duty })));
  const [contributors, setContributors] = useState(candidate.contributorIds.join('\n'));
  const [acknowledged, setAcknowledged] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const submitting = useRef(false);
  const nameInput = useRef<HTMLInputElement>(null);
  const isReference = candidate.type === 'AuthorizationReference';
  const nameMaxLength = isReference ? 2000 : 256;
  const reference = draft.authorizationReference ?? { reference: '', issuer: null, issuedAt: null, expiresAt: null };
  useEffect(() => {
    const invoker = document.activeElement;
    nameInput.current?.focus();
    return () => { if (invoker instanceof HTMLElement && invoker.isConnected) invoker.focus(); };
  }, []);

  const update = (values: Partial<PackageCandidate>) => { setDraft(previous => ({ ...previous, ...values })); setAcknowledged(false); };
  const updateReference = (values: Partial<PackageAuthorizationReference>) => update({ authorizationReference: { ...reference, ...values } });
  const submit = async (reviewAction: EditPackageCandidate['reviewAction']) => {
    if (submitting.current) return;
    setError(null);
    const ids = contributors.split(/[\s,]+/).filter(Boolean);
    const controls = mappings.map(row => row.controlId.trim().toUpperCase());
    if (!draft.name.trim()) { setError('A candidate name is required.'); return; }
    if (draft.name.trim().length > nameMaxLength) { setError(`Candidate name must be at most ${nameMaxLength} characters.`); return; }
    if (!isReference && !componentTypes.includes(draft.componentType)) { setError('Select a valid inventory component type.'); return; }
    if (isReference) {
      if (reviewAction === 'Reviewed' && !reference.reference.trim()) { setError('Authorization reference is required before marking reviewed.'); return; }
      if (reference.reference.trim().length > 2000 || (reference.issuer?.trim().length ?? 0) > 500) {
        setError('Authorization reference must be at most 2,000 characters and issuing authority at most 500 characters.'); return;
      }
      const dates = [reference.issuedAt, reference.expiresAt].map(value => value?.trim() || null);
      if (dates.some(value => value && (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})$/i.test(value) || !Number.isFinite(Date.parse(value))))) {
        setError('Use valid ISO-8601 timestamps with Z or a timezone offset, or leave the date empty.'); return;
      }
      if (dates[0] && dates[1] && Date.parse(dates[1]) < Date.parse(dates[0])) { setError('Expiration cannot be earlier than issuance.'); return; }
    }
    if ((reviewAction === 'Rejected' || draft.duplicateResolution) && !draft.rationale?.trim()) { setError('A review rationale is required for rejection or duplicate resolution.'); return; }
    if (controls.some(id => !id) || mappings.some(row => !duties.includes(row.duty))) { setError('Each control mapping needs a control ID and responsibility.'); return; }
    if (new Set(controls).size !== controls.length) { setError('Duplicate control ID. Resolve overlapping mappings before saving.'); return; }
    if (ids.some(id => !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id))) { setError('Contributor IDs must be complete record identifiers.'); return; }
    if (ids.length > 100 || new Set(ids).size !== ids.length) { setError('Select at most 100 distinct contributor IDs.'); return; }
    if (reviewAction === 'Reviewed' && !acknowledged) { setError('Review and acknowledge the source evidence first.'); return; }
    submitting.current = true;
    setBusy(true);
    try {
      const saved = await editPackageCandidate(packageId, candidate.candidateId, {
        expectedRevision: candidate.revision, name: draft.name.trim(), description: draft.description.trim(),
        componentType: isReference ? null : draft.componentType, classification: draft.classification.trim(), serviceCategory: draft.serviceCategory.trim(),
        controlDuties: Object.fromEntries(mappings.map((row, index) => [controls[index], row.duty])),
        contributorIds: ids, reviewAction, rationale: draft.rationale?.trim() || null, duplicateResolution: draft.duplicateResolution,
        ...(isReference ? { authorizationReference: {
          reference: reference.reference.trim(), issuer: reference.issuer?.trim() || null,
          issuedAt: reference.issuedAt?.trim() || null, expiresAt: reference.expiresAt?.trim() || null,
        } } : {}),
      });
      onSaved(saved);
    } catch (reason) {
      setError(message(reason));
      setConflict(reason instanceof PackageImportError && reason.status === 409);
    } finally { submitting.current = false; setBusy(false); }
  };

  return <section className={`${surfaceClass} space-y-5 p-5`} aria-label={`Review ${candidate.name}`}>
    <header className="flex flex-wrap justify-between gap-3">
      <div><h2 className="text-lg font-semibold">Review candidate</h2><p className="text-sm text-gray-600">{candidate.type} · Revision {candidate.revision} · {candidate.reviewState}</p></div>
      <button type="button" disabled={busy} className={secondaryButtonClass} onClick={onCancel}>Close review</button>
    </header>
    <p className={warningClass}>Source-backed proposals are not verified authorization decisions. Review evidence, duplicates and responsibilities before approval.
      {candidate.confidence !== null && <span className="block">Confidence {Math.round(candidate.confidence * 100)}% is advisory, never approval.</span>}</p>
    <section aria-label="Source citations" className="space-y-3">
      <h3 className="font-semibold">Supporting evidence</h3>
      {!candidate.citations.length && <p role="alert" className={errorClass}>No source citations. The server will block approval.</p>}
      {candidate.citations.map((citation, index) => <blockquote key={`${citation.artifactId}-${index}`} className="min-w-0 border-l-4 border-indigo-400 bg-indigo-50 p-4 text-sm">
        <p className="break-words whitespace-pre-wrap">{citation.quote}</p>
        <footer className="mt-2 break-all text-xs text-gray-600">{citation.archivePath} · {citation.locator}</footer>
        <AuthenticatedDownload className="mt-2 font-medium text-indigo-700 underline" url={packageArtifactUrl(packageId, citation.artifactId)} fileName={citation.archivePath}>Download cited source</AuthenticatedDownload>
      </blockquote>)}
    </section>
    <form onSubmit={event => { event.preventDefault(); void submit('NeedsReview'); }} className="space-y-4">
      <fieldset disabled={busy || conflict || !!candidate.publishedRecordId} className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="text-sm">Name<input ref={nameInput} className={`${inputClass} mt-1 w-full`} maxLength={nameMaxLength} value={draft.name} onChange={event => update({ name: event.target.value })} /></label>
          {!isReference && <>
          <label className="text-sm">Component type<select className={`${inputClass} mt-1 w-full`} value={draft.componentType} onChange={event => update({ componentType: event.target.value })}>
            {componentTypes.map(type => <option key={type}>{type}</option>)}
          </select></label>
          <label className="text-sm">Classification<input className={`${inputClass} mt-1 w-full`} maxLength={100} value={draft.classification} onChange={event => update({ classification: event.target.value })} /></label>
          <label className="text-sm">Service category<input className={`${inputClass} mt-1 w-full`} maxLength={100} value={draft.serviceCategory} onChange={event => update({ serviceCategory: event.target.value })} /></label>
          </>}
        </div>
        <label className="block text-sm">Description<textarea className={`${inputClass} mt-1 w-full`} rows={3} maxLength={2000} value={draft.description} onChange={event => update({ description: event.target.value })} /></label>
        {isReference ? <section className="space-y-3" aria-label="Source authorization metadata">
          <h3 className="font-semibold">{candidate.reviewState === 'Reviewed' ? 'Reviewed reference' : 'Proposed reference'}</h3>
          <p className="text-sm text-gray-600">Record only what the cited source states. Reviewing this reference does not verify an authorization or grant a mission-system ATO.</p>
          <label className="block text-sm">Authorization reference<textarea className={`${inputClass} mt-1 w-full`} rows={3} maxLength={2000} value={reference.reference} onChange={event => updateReference({ reference: event.target.value })} /></label>
          <label className="block text-sm">Issuing authority<input className={`${inputClass} mt-1 w-full`} maxLength={500} value={reference.issuer ?? ''} onChange={event => updateReference({ issuer: event.target.value || null })} /></label>
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="text-sm">Issued at (ISO-8601)<input className={`${inputClass} mt-1 w-full`} value={reference.issuedAt ?? ''} placeholder="2026-01-01T00:00:00Z" onChange={event => updateReference({ issuedAt: event.target.value || null })} /></label>
            <label className="text-sm">Expires at (ISO-8601)<input className={`${inputClass} mt-1 w-full`} value={reference.expiresAt ?? ''} placeholder="2027-01-01T00:00:00Z" onChange={event => updateReference({ expiresAt: event.target.value || null })} /></label>
          </div>
        </section> : <>
        <section className="space-y-2" aria-label="Control mappings and responsibilities">
          <h3 className="font-semibold">Control mappings and responsibilities</h3>
          {mappings.map((row, index) => <div key={index} className="grid items-end gap-2 sm:grid-cols-[1fr_1fr_auto]">
            <label className="text-sm">Control ID {index + 1}<input className={`${inputClass} mt-1 w-full`} maxLength={50} value={row.controlId} onChange={event => {
              setMappings(previous => previous.map((item, i) => i === index ? { ...item, controlId: event.target.value } : item)); setAcknowledged(false);
            }} /></label>
            <label className="text-sm">Responsibility {index + 1}<select className={`${inputClass} mt-1 w-full`} value={row.duty} onChange={event => {
              setMappings(previous => previous.map((item, i) => i === index ? { ...item, duty: event.target.value } : item)); setAcknowledged(false);
            }}><option value="">Select responsibility</option>{duties.map(duty => <option key={duty}>{duty}</option>)}</select></label>
            <button type="button" className={secondaryButtonClass} aria-label={`Remove control mapping ${index + 1}`} onClick={() => { setMappings(previous => previous.filter((_, i) => i !== index)); setAcknowledged(false); }}>Remove</button>
          </div>)}
          <button type="button" className={secondaryButtonClass} disabled={mappings.length >= 500} onClick={() => { setMappings(previous => [...previous, { controlId: '', duty: '' }]); setAcknowledged(false); }}>Add control mapping</button>
        </section>
        <label className="block text-sm">Contributor IDs (one per line)<textarea className={`${inputClass} mt-1 w-full font-mono`} rows={3} value={contributors} onChange={event => { setContributors(event.target.value); setAcknowledged(false); }} /></label>
        {!!candidate.unresolvedDependencies?.length && <div className={warningClass}>
          <p className="text-sm font-medium">Source dependency references requiring explicit contributor mapping</p>
          <ul className="mt-1 text-xs">{candidate.unresolvedDependencies.map(reference => <li key={reference} className="break-all">{reference}</li>)}</ul>
        </div>}
        <p className="text-xs text-gray-600">Use package component candidate IDs or eligible published component IDs. Include unpublished dependencies in the approval selection. The server verifies the full dependency set.</p>
        <section aria-label="Duplicate resolution" className="space-y-2">
          <h3 className="font-semibold">Duplicate and overlapping records</h3>
          {candidate.duplicateMatches.length ? <ul className="space-y-2">{candidate.duplicateMatches.map(duplicate => <li key={duplicate.recordId} className="break-words text-sm">
            {duplicate.name} · {duplicate.type} · {duplicate.published ? 'Published record' : 'Staged candidate'}<code className="block break-all text-xs">{duplicate.recordId}</code>
          </li>)}</ul> : <p className="text-sm text-gray-600">No duplicate matches returned. The approval preview rechecks overlap.</p>}
          <label className="block text-sm">Duplicate resolution<select className={`${inputClass} mt-1 w-full`} value={draft.duplicateResolution ?? ''} onChange={event => update({ duplicateResolution: event.target.value || null })}>
            <option value="">Not resolved / no duplicate</option><option value="KeepSeparate">Keep separate with rationale</option>
            {candidate.type === 'Component' && <option value="ReusePublished">Reuse an eligible published component</option>}
          </select></label>
          {draft.duplicateResolution === 'ReusePublished' && <p className={warningClass}>Set exactly one contributor ID to the published component to reuse. No published record will be overwritten.</p>}
        </section>
        </>}
        <label className="block text-sm">Review rationale<textarea className={`${inputClass} mt-1 w-full`} rows={3} maxLength={2000} value={draft.rationale ?? ''} onChange={event => update({ rationale: event.target.value })} /></label>
        <label className="flex items-start gap-2 text-sm"><input type="checkbox" className="mt-1" checked={acknowledged} onChange={event => setAcknowledged(event.target.checked)} />I reviewed the sources, mappings, responsibilities, duplicates and dependencies.</label>
        <div className="flex flex-wrap gap-3">
          <button className={secondaryButtonClass} type="submit">Save changes</button>
          <button className={buttonClass} type="button" disabled={!acknowledged || !candidate.citations.length} onClick={() => void submit('Reviewed')}>Mark reviewed</button>
          <button className={secondaryButtonClass} type="button" onClick={() => void submit('Rejected')}>Reject candidate</button>
        </div>
      </fieldset>
      <p className="text-xs text-gray-600">Saving edits resets review and invalidates prior approval. Marking reviewed is not approval or publication.</p>
      {busy && <p role="status">Saving review...</p>}
      {error && <p role="alert" className={errorClass}>{error}</p>}
      {conflict && <button type="button" className={secondaryButtonClass} onClick={onReload}>Reload current revision</button>}
    </form>
  </section>;
}
