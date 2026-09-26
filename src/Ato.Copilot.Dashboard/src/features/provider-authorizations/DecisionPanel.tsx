import { useEffect, useRef, useState, type ReactNode } from 'react';
import {
  createDecision, lifecycleDecision, listBoundaries, listDecisionHistory, listDecisions,
  recordDecision, reviseDecision, listMicrosoftReferences,
} from './api';
import { CitationFields, compact, Field, Lines, MutationForm } from './forms';
import type { Citation, ExternalDecision, ExternalDecisionInput, Offering } from './types';
import { PackageImportError } from '../package-imports/request';
import {
  errorClass, inputClass, message, Pager, secondaryButtonClass, Status, surfaceClass,
  useRemote, warningClass,
} from '../workspace-operations/workspaceUi';

type Props = {
  offering: Offering; inheritedOnly?: boolean; onChanged: () => void;
  initialAction?: Action; onPendingChange?: (pending: boolean) => void;
  onRefreshOffering?: () => Promise<void>;
};
type Action = 'draft' | 'record' | 'lifecycle';
type CandidateEntry = { packageId: string; candidateId: string; revision: string };
const expiryLabels = {
  DateStated: 'Expiry date stated in source',
  NoExpiryStated: 'No expiry stated in source',
  NotRecorded: 'Expiry basis not recorded',
};
const kindLabels = {
  ProviderDecision: 'Provider decision',
  InheritedMicrosoftReference: 'Inherited Microsoft reference',
};
const validCitations = (citations: Citation[]) => citations.length <= 100
  && citations.every(citation => Object.values(citation).every(value => value.trim().length > 0));
const exactSnapshot = (record: ExternalDecision) =>
  Number.isSafeInteger(record.revision) && record.revision > 0 && !!record.revisionId && !!record.snapshotHash;
const validDate = (date: string | null) => !date || (/^\d{4}-\d{2}-\d{2}$/.test(date)
  && !Number.isNaN(Date.parse(date)) && new Date(date).toISOString().slice(0, 10) === date);
function requireValid(valid: boolean, reason: string): asserts valid {
  if (!valid) throw new PackageImportError(reason, 422);
}

function Value({ label, children }: { label: string; children: ReactNode }) {
  return <div className="min-w-0"><dt className="text-xs font-semibold text-slate-600">{label}</dt>
    <dd className="whitespace-pre-wrap break-words">{children}</dd></div>;
}

function SourceSnapshot({ decision }: { decision: ExternalDecision }) {
  return <div className="space-y-3">
    <dl className="grid gap-3 text-sm sm:grid-cols-2">
      <Value label="Record kind">{kindLabels[decision.recordKind]}</Value>
      <Value label="Reference">{decision.reference}</Value>
      <Value label="Record ID">{decision.recordId}</Value>
      <Value label="Revision">{decision.revision}</Value>
      <Value label="Revision ID">{decision.revisionId}</Value>
      <Value label="Snapshot hash">{decision.snapshotHash}</Value>
      <Value label="Boundary revision ID">{decision.boundaryRevisionId}</Value>
      <Value label="Issuing authority as stated">{decision.issuingAuthority ?? 'Not recorded'}</Value>
      <Value label="Decision as stated">{decision.decisionAsStated ?? 'Not recorded'}</Value>
      <Value label="Issue date">{decision.issuedOn ?? 'Not recorded'}</Value>
      <Value label="Effective date">{decision.effectiveOn ?? 'Not recorded'}</Value>
      <Value label="Expiry date">{decision.expiresOn ?? 'Not recorded'}</Value>
      <Value label="Expiry basis">{expiryLabels[decision.expiryBasis]}</Value>
      <Value label="Scope statement">{decision.scopeStatement}</Value>
      <Value label="Conditions">{decision.conditions.length ? decision.conditions.join('\n') : 'No conditions recorded'}</Value>
      <Value label="Metadata review">{decision.metadataReviewState}</Value>
      <Value label="Standing">{decision.currentStanding === 'CurrentAsRecorded'
        ? 'Current as recorded (not independently verified)' : decision.currentStanding}</Value>
      <Value label="Recorded by">{decision.recordedBy ?? 'Not recorded'}</Value>
      <Value label="Recorded at">{decision.recordedAt ?? 'Not recorded'}</Value>
      <Value label="Impact review required">{decision.impactReviewRequired ? 'Yes' : 'No'}</Value>
    </dl>
    <h4 className="font-semibold">Immutable source candidate references</h4>
    {decision.sourceCandidateRefs.length ? <ul className="space-y-2 text-sm">
      {decision.sourceCandidateRefs.map((candidate, index) => <li key={index} className="break-words">
        Package: {candidate.packageId}; candidate: {candidate.candidateId}; revision: {candidate.revision}
      </li>)}
    </ul> : <p className="text-sm">No source candidate references recorded.</p>}
    <h4 className="font-semibold">Immutable source citations</h4>
    <p className="text-xs">References do not grant permission to download protected sources.</p>
    {decision.citations.length ? decision.citations.map((citation, index) =>
      <dl key={index} className="grid gap-2 rounded border p-3 text-sm sm:grid-cols-2">
        <Value label="Package ID">{citation.packageId}</Value>
        <Value label="Artifact ID">{citation.artifactId}</Value>
        <Value label="Archive path">{citation.archivePath}</Value>
        <Value label="Locator">{citation.locator}</Value>
        <Value label="Source quote">{citation.quote}</Value>
      </dl>) : <p className="text-sm">No source citations recorded.</p>}
  </div>;
}

function DecisionHistory({ offeringId, decision }: { offeringId: string; decision: ExternalDecision }) {
  const [page, setPage] = useState(1);
  const history = useRemote(signal => listDecisionHistory(offeringId, decision.recordId, page, signal),
    [offeringId, decision.recordId, decision.revision, decision.metadataReviewState, decision.currentStanding, page]);
  return <section aria-label="Decision history" className={`${surfaceClass} space-y-4 p-4`}>
    <h3 className="font-semibold">Immutable decision history</h3>
    <Status loading={history.loading} error={history.error} retry={history.retry} />
    {history.data && <>
      {history.data.items.length ? history.data.items.map(item =>
        <article key={item.revisionId} className="space-y-3 border-b pb-4">
          <h4 className="font-semibold">Historical revision {item.revision}</h4>
          <SourceSnapshot decision={item} />
        </article>) : <p>No history revisions returned.</p>}
      <Pager {...history.data} onPage={setPage} />
    </>}
  </section>;
}

function CandidateFields({ value, onChange }: { value: CandidateEntry[]; onChange: (value: CandidateEntry[]) => void }) {
  return <fieldset className="space-y-3 rounded border p-3">
    <legend className="text-sm font-semibold">Optional source candidate references</legend>
    <p className="text-xs">Enter exact retained package/candidate identities and revisions. Import proposals are not verified authority.</p>
    {value.map((candidate, index) => <div key={index} className="grid gap-2 sm:grid-cols-3">
      <Field label={`Candidate package ID ${index + 1}`} value={candidate.packageId} required
        onChange={packageId => onChange(value.map((item, row) => row === index ? { ...item, packageId } : item))} />
      <Field label={`Candidate ID ${index + 1}`} value={candidate.candidateId} required
        onChange={candidateId => onChange(value.map((item, row) => row === index ? { ...item, candidateId } : item))} />
      <Field label={`Candidate revision ${index + 1}`} value={candidate.revision} required type="number"
        onChange={revision => onChange(value.map((item, row) => row === index ? { ...item, revision } : item))} />
      <button type="button" className={secondaryButtonClass} onClick={() => onChange(value.filter((_, row) => row !== index))}>
        Remove source candidate {index + 1}
      </button>
    </div>)}
    <button type="button" className={secondaryButtonClass} disabled={value.length >= 100}
      onClick={() => onChange([...value, { packageId: '', candidateId: '', revision: '' }])}>Add source candidate</button>
  </fieldset>;
}

function BoundarySelector({ offeringId, value, onChange }: { offeringId: string; value: string; onChange: (id: string) => void }) {
  const [page, setPage] = useState(1);
  const boundaries = useRemote(signal => listBoundaries(offeringId, page, signal), [offeringId, page]);
  const selected = boundaries.data?.items.find(item => item.boundaryRevisionId === value);
  return <section aria-label="Boundary choices" className="space-y-3">
    <Status loading={boundaries.loading} error={boundaries.error} retry={boundaries.retry} />
    <label className="grid gap-1 text-sm">Boundary revision
      <select className={inputClass} value={value} required onChange={event => onChange(event.target.value)}>
        <option value="">Select an exact boundary revision</option>
        {value && !selected && <option value={value}>Retained boundary revision: {value}</option>}
        {boundaries.data?.items.map(item => <option key={item.boundaryRevisionId} value={item.boundaryRevisionId}>
          {item.name} — version {item.version} — {item.boundaryRevisionId}
        </option>)}
      </select>
    </label>
    {selected && <div className="break-words text-sm">
      <p>Boundary snapshot: {selected.snapshotHash}</p><p>{selected.scopeStatement}</p>
    </div>}
    <p className="text-xs">Selection binds this immutable boundary revision, not hosting allocation or mission coverage.</p>
    {boundaries.data && <Pager {...boundaries.data} onPage={setPage} />}
  </section>;
}

function emptyInput(kind: ExternalDecisionInput['recordKind']): ExternalDecisionInput {
  return {
    recordKind: kind, boundaryRevisionId: '', sourceCandidateRefs: [], reference: '', issuingAuthority: null,
    decisionAsStated: null, issuedOn: null, effectiveOn: null, expiresOn: null, expiryBasis: 'NotRecorded',
    scopeStatement: '', conditions: [], citations: [],
  };
}
function sourceInput(decision: ExternalDecision): ExternalDecisionInput {
  const { recordKind, boundaryRevisionId, sourceCandidateRefs, reference, issuingAuthority, decisionAsStated,
    issuedOn, effectiveOn, expiresOn, expiryBasis, scopeStatement, conditions, citations } = decision;
  return { recordKind, boundaryRevisionId, sourceCandidateRefs, reference, issuingAuthority, decisionAsStated,
    issuedOn, effectiveOn, expiresOn, expiryBasis, scopeStatement, conditions, citations };
}

function DraftFields({ offeringId, input, setInput, candidates, setCandidates }: {
  offeringId: string; input: ExternalDecisionInput; setInput: (input: ExternalDecisionInput) => void;
  candidates: CandidateEntry[]; setCandidates: (items: CandidateEntry[]) => void;
}) {
  return <>
    <p className="text-sm">Draft kind: {kindLabels[input.recordKind]}. Saving creates unconfirmed metadata, not a recorded decision.</p>
    <BoundarySelector offeringId={offeringId} value={input.boundaryRevisionId}
      onChange={boundaryRevisionId => setInput({ ...input, boundaryRevisionId })} />
    <Field label="Reference" value={input.reference} required onChange={reference => setInput({ ...input, reference })} />
    <Field label="Issuing authority as stated" value={input.issuingAuthority ?? ''}
      onChange={issuingAuthority => setInput({ ...input, issuingAuthority: issuingAuthority || null })} />
    <Field label="Decision as stated" value={input.decisionAsStated ?? ''}
      onChange={decisionAsStated => setInput({ ...input, decisionAsStated: decisionAsStated || null })} />
    <div className="grid gap-3 sm:grid-cols-3">
      <Field label="Issue date as stated" type="date" value={input.issuedOn ?? ''}
        onChange={issuedOn => setInput({ ...input, issuedOn: issuedOn || null })} />
      <Field label="Effective date as stated" type="date" value={input.effectiveOn ?? ''}
        onChange={effectiveOn => setInput({ ...input, effectiveOn: effectiveOn || null })} />
      <Field label="Expiry date as stated" type="date" value={input.expiresOn ?? ''} required={input.expiryBasis === 'DateStated'}
        onChange={expiresOn => setInput({ ...input, expiresOn: expiresOn || null })} />
    </div>
    <label className="grid gap-1 text-sm">Expiry basis
      <select className={inputClass} value={input.expiryBasis} onChange={event => {
        const expiryBasis = event.target.value;
        if (expiryBasis === 'DateStated' || expiryBasis === 'NoExpiryStated' || expiryBasis === 'NotRecorded') {
          setInput({ ...input, expiryBasis });
        }
      }}>{Object.entries(expiryLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select>
    </label>
    <p className="text-xs">Missing dates stay unknown. “No expiry stated” is a source statement, not indefinite validity.</p>
    <Field label="Scope statement" value={input.scopeStatement} required multiline maxLength={8000}
      onChange={scopeStatement => setInput({ ...input, scopeStatement })} />
    <Lines label="Conditions" values={input.conditions} onChange={conditions => setInput({ ...input, conditions })} />
    <CandidateFields value={candidates} onChange={setCandidates} />
    <CitationFields value={input.citations} onChange={citations => setInput({ ...input, citations })} />
  </>;
}

async function currentDecision(offeringId: string, original: ExternalDecision, signal: AbortSignal) {
  let page = 1;
  while (!signal.aborted) {
    const result = await listDecisions(offeringId, page, signal);
    const found = result.items.find(item => item.recordId === original.recordId);
    if (found) {
      requireValid(found.offeringId === offeringId && found.recordKind === original.recordKind && exactSnapshot(found),
        'Refreshed decision does not match the selected offering, kind or exact snapshot.');
      return found;
    }
    if (result.page * result.pageSize >= result.total) break;
    requireValid(result.page === page && result.pageSize > 0, 'Invalid decision pagination; refresh could not locate the exact record.');
    page++;
  }
  throw new PackageImportError('The selected decision is unavailable. Inputs retained; no replacement record was selected.', 404);
}

function DecisionEditor({ offering, kind, original, action, onSaved, onCancel, onRefresh, onPendingChange, unavailable }: {
  offering: Offering; kind: ExternalDecisionInput['recordKind']; original: ExternalDecision | null; action: Action;
  onSaved: (decision: ExternalDecision) => void; onCancel: () => void; onRefresh: () => void | Promise<void>;
  onPendingChange?: (pending: boolean) => void; unavailable?: boolean;
}) {
  const [base, setBase] = useState(original);
  const [offeringRevision, setOfferingRevision] = useState(offering.revision);
  const [input, setInput] = useState(() => original ? sourceInput(original) : emptyInput(kind));
  const [candidates, setCandidates] = useState<CandidateEntry[]>(() =>
    (original?.sourceCandidateRefs ?? []).map(item => ({ ...item, revision: String(item.revision) })));
  const [rationale, setRationale] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [lifecycle, setLifecycle] = useState<'' | 'Withdrawn' | 'Superseded'>('');
  const [effectiveOn, setEffectiveOn] = useState('');
  const [replacementRevisionId, setReplacementRevisionId] = useState('');
  const [citations, setCitations] = useState<Citation[]>([]);
  const [phase, setPhase] = useState<'idle' | 'pending' | 'uncertain'>('idle');
  const [stale, setStale] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [refreshError, setRefreshError] = useState<string | null>(null);
  const [refreshed, setRefreshed] = useState<ExternalDecision | null>(null);
  const refreshController = useRef<AbortController | null>(null);
  const result = useRef<ExternalDecision | null>(null);
  useEffect(() => () => refreshController.current?.abort(), []);

  const refresh = async () => {
    refreshController.current?.abort();
    const controller = new AbortController();
    refreshController.current = controller;
    setRefreshing(true); setRefreshError(null); setRefreshed(null);
    try {
      await onRefresh();
      if (base) {
        const latest = await currentDecision(offering.offeringId, base, controller.signal);
        if (!controller.signal.aborted) setRefreshed(latest);
      }
    } catch (reason) {
      if (!controller.signal.aborted) setRefreshError(message(reason));
    } finally { if (!controller.signal.aborted) setRefreshing(false); }
  };

  const perform = async (key: string) => {
    requireValid(!stale, 'Refresh current records and explicitly review the new revision before retrying.');
    requireValid(Number.isSafeInteger(offeringRevision) && offeringRevision > 0, 'An exact offering revision is required.');
    if (base) requireValid(exactSnapshot(base) && base.offeringId === offering.offeringId, 'An exact matching decision snapshot is required.');
    if (action === 'draft') {
      requireValid(!!input.boundaryRevisionId && !!input.reference.trim() && !!input.scopeStatement.trim(), 'Select a boundary and supply a reference and bounded scope.');
      requireValid([input.issuedOn, input.effectiveOn, input.expiresOn].every(validDate), 'Use valid source-stated date-only values.');
      requireValid(input.expiryBasis === 'DateStated' ? !!input.expiresOn : !input.expiresOn, 'Expiry basis must agree with the retained expiry date. No date is discarded automatically.');
      requireValid(compact(input.conditions).length <= 100 && validCitations(input.citations), 'Provide complete source citations and at most 100 conditions.');
      requireValid(candidates.length <= 100 && candidates.every(item => !!item.packageId.trim() && !!item.candidateId.trim()
        && /^[1-9]\d*$/.test(item.revision) && Number.isSafeInteger(Number(item.revision))), 'Source candidate references require exact IDs and positive integer revisions.');
      const data = { ...input, conditions: compact(input.conditions),
        sourceCandidateRefs: candidates.map(item => ({ ...item, revision: Number(item.revision) })) };
      return base ? reviseDecision(offering.offeringId, base.recordId, { ...data, expectedRevision: base.revision })
        : createDecision(offering.offeringId, { ...data, expectedOfferingRevision: offeringRevision }, key);
    }
    requireValid(!!base, 'Select an existing decision first.');
    if (action === 'record') {
      requireValid(base.metadataReviewState === 'Unconfirmed' && confirmed && !!rationale.trim()
        && !!base.issuingAuthority?.trim() && !!base.reference.trim() && !!base.scopeStatement.trim()
        && !!base.boundaryRevisionId && base.citations.length > 0 && validCitations(base.citations),
      'Recording requires explicit human review, rationale, source authority/reference/scope and supporting citations.');
      return recordDecision(offering.offeringId, base, rationale);
    }
    requireValid(base.metadataReviewState === 'Recorded' && !['Withdrawn', 'Superseded'].includes(base.currentStanding),
      'Lifecycle events require a recorded decision that has not already been withdrawn or superseded.');
    requireValid(!!lifecycle && !!effectiveOn && validDate(effectiveOn) && !!rationale.trim()
      && citations.length > 0 && validCitations(citations), 'Select an action, source-stated effective date, rationale and supporting source evidence.');
    requireValid(lifecycle !== 'Superseded' || (!!replacementRevisionId.trim() && replacementRevisionId !== base.revisionId),
      'Supersession requires an explicit different eligible replacement revision.');
    return (await lifecycleDecision(offering.offeringId, base.recordId, {
      expectedRevision: base.revision, kind: lifecycle, effectiveOn, rationale, citations,
      ...(lifecycle === 'Superseded' ? { replacementRevisionId } : {}),
    }, key)).record;
  };

  const submit = async (key: string) => {
    setPhase('pending');
    try { result.current = await perform(key); setPhase('idle'); }
    catch (reason) {
      const known = reason instanceof PackageImportError && [400, 401, 403, 404, 409, 422].includes(reason.status ?? 0);
      setPhase(known ? 'idle' : 'uncertain');
      if (reason instanceof PackageImportError && reason.status === 409) setStale(true);
      throw reason;
    }
  };
  return <section aria-label="Decision editor" className={`${surfaceClass} space-y-4 p-4`}>
    <h3 className="font-semibold">{action === 'draft' ? base ? 'Successor draft' : kind === 'ProviderDecision'
      ? 'Authorization details for review' : 'New unconfirmed draft' : action === 'record' ? 'Human metadata review' : 'Append lifecycle event'}</h3>
    <p className="text-sm">Prior source metadata and history are immutable. A successor draft does not overwrite the recorded revision.</p>
    {base && <details><summary className="cursor-pointer">Source snapshot being reviewed</summary><SourceSnapshot decision={base} /></details>}
    {stale && <div className={`${warningClass} space-y-3`}>
      <p>Stale revision. Inputs retained; refresh and explicitly adopt the current snapshot before retrying.</p>
      <button type="button" className={secondaryButtonClass} disabled={refreshing || phase !== 'idle'} onClick={() => void refresh()}>Refresh current records</button>
      <Status loading={refreshing} error={refreshError} />
      {refreshed && <SourceSnapshot decision={refreshed} />}
      {(refreshed || (!base && offering.revision !== offeringRevision)) && <button type="button" className={secondaryButtonClass}
        disabled={refreshing || phase !== 'idle'} onClick={() => {
          if (refreshed) setBase(refreshed);
          setOfferingRevision(offering.revision); setConfirmed(false); setStale(false); setRefreshed(null);
        }}>Use refreshed revision with retained inputs</button>}
      {!base && <p className="text-sm">Submitted offering revision: {offeringRevision}. Current offering revision: {offering.revision}.</p>}
    </div>}
    <MutationForm label={action === 'draft' ? base ? 'Save successor draft' : kind === 'InheritedMicrosoftReference' ? 'Save reference draft' : 'Save draft'
      : action === 'record' ? 'Record external metadata' : 'Save lifecycle event'}
      submit={submit} onSaved={() => { if (result.current) onSaved(result.current); }} disabled={stale || unavailable} onPendingChange={onPendingChange}>
      {action === 'draft' ? <DraftFields offeringId={offering.offeringId} input={input} setInput={setInput}
        candidates={candidates} setCandidates={setCandidates} /> : action === 'record' ? <>
        <Field label="Review rationale" value={rationale} onChange={setRationale} required multiline />
        <label className="flex items-start gap-2 text-sm">
          <input type="checkbox" required checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />
          I reviewed this exact source metadata and boundary snapshot.
        </label>
        <p className="text-sm">Recording retains human-reviewed metadata; it does not independently authenticate the source or its signature.</p>
      </> : <>
        <label className="grid gap-1 text-sm">Lifecycle action<select className={inputClass} value={lifecycle} required
          onChange={event => {
            const value = event.target.value;
            if (value === '' || value === 'Withdrawn' || value === 'Superseded') setLifecycle(value);
          }}>
          <option value="">Select a lifecycle action</option><option value="Withdrawn">Withdrawn</option><option value="Superseded">Superseded</option>
        </select></label>
        <Field label="Lifecycle effective date" type="date" value={effectiveOn} onChange={setEffectiveOn} required />
        {lifecycle === 'Superseded' && <Field label="Replacement revision ID" value={replacementRevisionId} onChange={setReplacementRevisionId} required />}
        <Field label="Lifecycle rationale" value={rationale} onChange={setRationale} required multiline />
        <CitationFields value={citations} onChange={setCitations} />
        <p className="text-xs">Replacement eligibility and authority are checked server-side. Historical associations remain retained for impact review.</p>
      </>}
    </MutationForm>
    <button type="button" className={secondaryButtonClass} disabled={phase !== 'idle' || refreshing} onClick={onCancel}>Cancel editing</button>
  </section>;
}

function DecisionWorkspace({ offering, inheritedOnly = false, onChanged, initialAction, onPendingChange, onRefreshOffering }: Props) {
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<ExternalDecision | null>(null);
  const [action, setAction] = useState<Action | null>(initialAction ?? null);
  const [showHistory, setShowHistory] = useState(false);
  const kind = inheritedOnly ? 'InheritedMicrosoftReference' : 'ProviderDecision';
  const decisions = useRemote(signal => inheritedOnly
    ? listMicrosoftReferences(offering.offeringId, page, signal) : listDecisions(offering.offeringId, page, signal, 'ProviderDecision'),
  [offering.offeringId, page, inheritedOnly]);
  const refresh = () => { decisions.retry(); onChanged(); };
  return <section className="min-w-0 space-y-4">
    <h2 className="text-lg font-semibold">{inheritedOnly ? 'Microsoft reference records' : 'Existing authorization records'}</h2>
    {!inheritedOnly && <p className={warningClass}>SPIN does not issue a mission ATO or independently verify external authorization.
      Provider decisions and inherited Microsoft references are separate records. Hosting allocation is not covered workload status.</p>
    }
    <section aria-label="Decision list" className={`${surfaceClass} space-y-3 p-4`}>
      <Status loading={decisions.loading} error={decisions.error} retry={decisions.retry} />
      {decisions.data && <>
        <p className="text-xs">{inheritedOnly ? 'Microsoft reference records; totals include only this record kind.' : 'Provider authorization records only; inherited Microsoft references are listed separately.'}</p>
        <ul className="space-y-3">{decisions.data.items.filter(item => item.recordKind === kind).map(item =>
          <li key={item.recordId} className="flex flex-wrap items-center justify-between gap-3 border-b pb-3">
            <span className="break-words text-sm">{item.reference} · {item.metadataReviewState} · {item.currentStanding}</span>
            <button type="button" className={secondaryButtonClass} disabled={action !== null} onClick={() => {
              setSelected(item); setShowHistory(false);
            }}>Open {item.reference}</button>
          </li>)}</ul>
        {!decisions.data.items.some(item => item.recordKind === kind) && <p>No decisions on this page.</p>}
        <fieldset disabled={action !== null}><Pager {...decisions.data} onPage={setPage} /></fieldset>
      </>}
      <button type="button" className={secondaryButtonClass} disabled={action !== null || decisions.loading || !!decisions.error}
        onClick={() => { setSelected(null); setShowHistory(false); setAction('draft'); }}>{inheritedOnly ? 'Add reference' : 'Add authorization record'}</button>
      <button type="button" className={secondaryButtonClass} disabled={action !== null || decisions.loading}
        onClick={() => { setSelected(null); setShowHistory(false); refresh(); }}>Refresh decisions</button>
    </section>
    {selected && <section aria-label="Selected decision" className={`${surfaceClass} space-y-4 p-4`}>
      <h3 className="font-semibold">Selected immutable decision snapshot</h3>
      <p className="font-medium">{selected.reference}</p>
      <details><summary className="cursor-pointer font-medium">Details</summary><SourceSnapshot decision={selected} /></details>
      {!exactSnapshot(selected) && <p role="alert" className={errorClass}>Exact revision and snapshot are unavailable. Refresh before making changes.</p>}
      {action === null && <div className="flex flex-wrap gap-2">
        <button type="button" className={secondaryButtonClass} disabled={!exactSnapshot(selected)} onClick={() => setAction('draft')}>Revise draft</button>
        <button type="button" className={secondaryButtonClass} disabled={!exactSnapshot(selected) || selected.metadataReviewState !== 'Unconfirmed'}
          onClick={() => setAction('record')}>Review metadata</button>
        <button type="button" className={secondaryButtonClass}
          disabled={!exactSnapshot(selected) || selected.metadataReviewState !== 'Recorded' || ['Withdrawn', 'Superseded'].includes(selected.currentStanding)}
          onClick={() => setAction('lifecycle')}>Withdraw or supersede</button>
        <button type="button" className={secondaryButtonClass} onClick={() => setShowHistory(value => !value)}>{showHistory ? 'Hide history' : 'View history'}</button>
      </div>}
    </section>}
    {action && <DecisionEditor key={`${selected?.recordId ?? 'new'}:${action}`} offering={offering} kind={kind} original={selected}
      action={action} onRefresh={() => { decisions.retry(); return onRefreshOffering ? onRefreshOffering() : onChanged(); }}
      onCancel={() => setAction(null)} onPendingChange={onPendingChange}
      unavailable={decisions.loading || !!decisions.error}
      onSaved={saved => { setSelected(saved); setAction(null); refresh(); }} />}
    {selected && showHistory && <DecisionHistory key={selected.recordId} offeringId={offering.offeringId} decision={selected} />}
  </section>;
}

export function DecisionPanel(props: Props) {
  return <DecisionWorkspace key={`${props.offering.offeringId}:${!!props.inheritedOnly}`} {...props} />;
}
