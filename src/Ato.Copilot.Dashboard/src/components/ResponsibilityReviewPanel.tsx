import { useEffect, useId, useRef, useState } from 'react';
import {
  isResponsibilityType, readProviderSnapshot, readReviewedProviderSnapshot,
  type CapabilityResponsibilityItem, type ConfirmCapabilityResponsibilitiesRequest,
  type ProviderReviewSnapshot, type ResponsibilityInheritanceType,
} from '../api/capabilityResponsibilities';
import SetupDialog from '../features/workspace-operations/SetupDialog';
import { buttonClass, errorClass, inputClass, secondaryButtonClass, warningClass } from '../features/workspace-operations/workspaceUi';
import { Link } from '../features/workspaces/workspaceNavigation';
import ResponsibilityFirstPass from './ResponsibilityFirstPass';
import { useResponsibilityFirstPass } from './useResponsibilityFirstPass';
import { responsibilityFields, type ResponsibilityField, type ResponsibilityValue, type ResponsibilityValues } from '../api/responsibilityDrafts';

interface Props {
  item?: CapabilityResponsibilityItem | null;
  controlId?: string;
  firstPass?: boolean;
  onDraftConfirmed?: () => void;
  baselineId: string | null;
  canConfirm: boolean;
  eligible: boolean;
  stateExplanation: string;
  busy: boolean;
  blocked: boolean;
  generation: number;
  systemId: string;
  actionError: string | null;
  onRefresh: () => void;
  onClose: () => void;
  onConfirm: (capabilityId: string, body: ConfirmCapabilityResponsibilitiesRequest) => Promise<boolean>;
}
const choices = [
  { value: 'Shared', title: 'Provider and my team', detail: 'Document each side of the work.' },
  { value: 'Inherited', title: 'Provider covers the control', detail: 'Verify coverage of the complete applicable scope.' },
  { value: 'Customer', title: 'My team implements the control', detail: 'Do not rely on provider coverage for this allocation.' },
  { value: '', title: 'I need more information', detail: 'Keep this draft unconfirmed; existing saved records stay unchanged.' },
] as const;

export default function ResponsibilityReviewPanel({
  item, controlId: requestedControl, firstPass = false, onDraftConfirmed, baselineId, canConfirm, eligible, stateExplanation, busy, blocked, generation,
  systemId, actionError, onRefresh, onClose, onConfirm,
}: Props) {
  const id = useId();
  const controlId = item?.controlId ?? requestedControl!;
  const [allocation, setAllocation] = useState<ResponsibilityInheritanceType | ''>(item?.allocation?.inheritanceType ?? '');
  const [fields, setFields] = useState({
    provider: item?.allocation?.provider ?? item?.providerName ?? '',
    customer: item?.allocation?.customerResponsibility ?? '',
    providerDuties: '', scope: '', exclusions: '', source: '', basis: '', information: '',
  });
  const [reviewing, setReviewing] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const edited = useRef(new Set<ResponsibilityField>());
  const [origins, setOrigins] = useState<Partial<Record<ResponsibilityField, ResponsibilityValue>>>({});
  const currentValues: ResponsibilityValues = { ...fields, allocation: allocation || 'NeedsConfirmation' };
  const prepared = useResponsibilityFirstPass(systemId, controlId, firstPass, currentValues, (suggested, force) => {
    for (const key of responsibilityFields) {
      if (!force && edited.current.has(key)) continue;
      const value = suggested[key];
      if (key === 'allocation') setAllocation(isResponsibilityType(value.value) ? value.value : '');
      else setFields(previous => ({ ...previous, [key]: value.value }));
      setOrigins(previous => ({ ...previous, [key]: value }));
      if (value.userEdited) edited.current.add(key); else edited.current.delete(key);
    }
    setAcknowledged(false);
  });
  const summary = useRef<HTMLHeadingElement>(null);
  const reviewButton = useRef<HTMLButtonElement>(null);
  const previousGeneration = useRef(generation);
  useEffect(() => {
    if (previousGeneration.current !== generation) {
      previousGeneration.current = generation;
      setReviewing(false);
      setAcknowledged(false);
    }
  }, [generation]);
  useEffect(() => { if (reviewing) summary.current?.focus(); }, [reviewing]);
  let current: ProviderReviewSnapshot | null = null;
  let historical: ProviderReviewSnapshot | null = null;
  let snapshotError: string | null = null;
  try {
    if (item) { current = readProviderSnapshot(item); historical = readReviewedProviderSnapshot(item); }
  } catch (reason) {
    snapshotError = reason instanceof Error ? reason.message : 'The provider snapshot is malformed.';
  }
  const mapped = current?.Controls.some(control => control.toUpperCase() === controlId.toUpperCase()) === true;
  const sourceReady = firstPass ? !!prepared.context && !!baselineId
    : !!baselineId && eligible && item?.sourceAvailable && mapped && !snapshotError;
  const providerAllocation = allocation === 'Shared' || allocation === 'Inherited';
  const entries: [string, string][] = [
    ...(providerAllocation ? [['Provider duties', fields.providerDuties] as [string, string]] : []),
    ...(allocation === 'Inherited' ? [
      ['Applicable scope', fields.scope], ['Exclusions', fields.exclusions], ['Supporting source', fields.source],
    ] as [string, string][] : []),
    ['Basis for this allocation', fields.basis],
  ];
  const notes = entries.map(([label, value]) => `${label}: ${value.trim()}`).join('\n\n');
  const valid = allocation === '' ? !!fields.information.trim() : !!fields.basis.trim()
    && (providerAllocation ? !!fields.provider.trim() && !!fields.providerDuties.trim() : true)
    && (allocation === 'Inherited'
      ? !!fields.scope.trim() && !!fields.exclusions.trim() && !!fields.source.trim()
      : !!fields.customer.trim())
    && notes.length <= 2000;
  const sourceText = `${current?.Description ?? ''} ${current?.Component.Description ?? ''}`;
  const synthetic = /\b(synthetic|demonstration|demo only)\b/i.test(sourceText);
  const change = (key: keyof typeof fields, value: string) => {
    setFields(previous => ({ ...previous, [key]: value }));
    edited.current.add(key);
    setAcknowledged(false);
  };
  const provenance = (key: ResponsibilityField) => origins[key] && <span className="text-xs text-indigo-700 dark:text-indigo-300">
    {edited.current.has(key) && !origins[key]!.sourceIds.length ? 'Your entry · source not yet linked'
      : `${origins[key]!.origin}${edited.current.has(key) ? ' · your correction' : ''}`}
    {origins[key]!.sourceIds.length > 0 && ` · ${origins[key]!.sourceIds.join(', ')}`}
  </span>;
  const textField = (key: keyof typeof fields, label: string, hint?: string) =>
    <label className="grid gap-1 text-sm" key={key}>{label}
      <textarea aria-label={label} aria-describedby={hint ? `${id}-${key}-hint` : undefined}
        className={inputClass} rows={3} maxLength={key === 'customer' ? 2000 : 1200} value={fields[key]}
        onChange={event => change(key, event.target.value)} />
      {hint && <span id={`${id}-${key}-hint`} className="text-xs text-gray-500 dark:text-gray-400">{hint}</span>}
      {firstPass && provenance(key)}
    </label>;
  async function confirm() {
    if (!canConfirm || !sourceReady || blocked || busy || !valid || !reviewing || !acknowledged
      || !baselineId || !isResponsibilityType(allocation)) return;
    if (firstPass) {
      if (await prepared.confirm(notes)) { onDraftConfirmed?.(); onClose(); }
      return;
    }
    if (!item) return;
    const succeeded = await onConfirm(item.capabilityId, {
      baselineId, sourceRevision: item.sourceRevision, reviewRevision: item.reviewRevision,
      allocations: [{ controlId: item.controlId, inheritanceType: allocation,
        provider: allocation === 'Customer' ? null : fields.provider.trim(),
        customerResponsibility: fields.customer.trim() || null }],
      providerCoverageVerified: true, customerDutiesReviewed: true, reviewNotes: notes,
    });
    if (succeeded) onClose();
  }
  const footer = <div className="space-y-2">
    {actionError && <p role="alert" className={`${errorClass} text-sm`}>{actionError}</p>}
    <p className="text-xs text-gray-500 dark:text-gray-400">
      {blocked ? 'Saved state needs verification. Your draft is preserved.'
        : firstPass && prepared.saved && prepared.matchesSaved
          ? prepared.saved.status === 'Accepted' ? 'Accepted review recorded · edits require a new proposed draft'
            : `Saved proposed draft · revision ${prepared.saved.revision}`
          : 'Unsaved draft · confirmation is a separate action'}
    </p>
    <div className="flex flex-wrap gap-2">
      {reviewing ? <>
        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => {
          setReviewing(false); setAcknowledged(false);
          requestAnimationFrame(() => reviewButton.current?.focus());
        }}>Back to edit</button>
        {allocation !== '' && <button type="button" className={buttonClass}
          disabled={busy || blocked || !canConfirm || !sourceReady || !valid || !acknowledged || firstPass && !prepared.canConfirm}
          onClick={() => { void confirm(); }}>Confirm responsibility</button>}
      </> : <button type="button" ref={reviewButton} className={buttonClass}
        disabled={busy || !canConfirm || !valid || (allocation !== '' && (!sourceReady || blocked))}
        onClick={() => { setReviewing(true); setAcknowledged(false); }}>
        {allocation === '' ? 'Review information gap' : 'Review allocation'}
      </button>}
      {(blocked || snapshotError) && <button type="button" className={secondaryButtonClass} disabled={busy}
        onClick={onRefresh}>Refresh saved state</button>}
      {firstPass && <button type="button" className={secondaryButtonClass}
        disabled={prepared.busy || !prepared.context?.canPrepare}
        onClick={() => { void prepared.save(); }}>Save proposed draft</button>}
    </div>
    {firstPass && !prepared.matchesSaved && <p className="text-xs">Save your proposed draft before confirming. AI acceptance is not required.</p>}
  </div>;
  return <SetupDialog title={`Review responsibility ${controlId}`}
    description="Decide what the provider covers and what your team must do."
    placement="right" expanded busy={busy} onClose={onClose} footer={footer}>
    <div className="space-y-5">
      <section aria-label="Saved responsibility" className="space-y-1 text-sm">
        <p className="font-medium">Last verified saved allocation: {item?.allocation?.inheritanceType ??
          (prepared.context?.sourceValues.allocation.sourceIds.includes('responsibility') ? prepared.context.sourceValues.allocation.value : 'Not confirmed')}</p>
        <p>Effective in baseline: {item?.effectiveInheritanceType ??
          (prepared.context?.sourceValues.allocation.sourceIds.includes('responsibility') ? prepared.context.sourceValues.allocation.value : 'Not designated')}</p>
        <p className="text-gray-600 dark:text-gray-300">{stateExplanation}</p>
        {item?.allocation && <details>
          <summary className="cursor-pointer">Saved duties & review history</summary>
          <p>Provider: {item.allocation.provider ?? 'None'}</p>
          <p className="whitespace-pre-wrap">Customer duties: {item.allocation.customerResponsibility ?? 'Not recorded'}</p>
          <p>Confirmed by {item.confirmedBy ?? 'Not recorded'} at {item.confirmedAt ?? 'Not recorded'}</p>
          <p className="whitespace-pre-wrap">{item.reviewNotes ?? 'No review notes were recorded.'}</p>
        </details>}
      </section>
      {!canConfirm && <p className={warningClass}>Read-only: an effective assigned ISSM or ISSO is required to confirm responsibility.</p>}
      {!baselineId && <p className={warningClass}>Select a system baseline before confirmation.</p>}
      {snapshotError && <p role="alert" className={errorClass}>{snapshotError}</p>}
      {item && !item.sourceAvailable && <p className={warningClass}>Provider source unavailable. Historical review remains visible; confirm only against verified current sources.</p>}
      {current && !mapped && <p className={warningClass}>{controlId} is no longer mapped by this source; confirmation is disabled.</p>}
      {item?.reviewedSourceRevision && item.reviewedSourceRevision !== item.sourceRevision &&
        <p className={warningClass}>The provider source has changed since the saved review. Recheck coverage before confirming.</p>}
      {synthetic && <p className={warningClass}>Source text describes synthetic or demonstration content. It cannot establish real provider coverage.</p>}
      {firstPass && <ResponsibilityFirstPass state={prepared} />}
      {firstPass && item && <p className="text-sm text-gray-600 dark:text-gray-300">
        Matrix contribution: {item.providerName ?? 'Provider name unavailable'} · {current?.Name ?? 'Capability unavailable'} · {current?.Component.Name ?? 'Component unavailable'}
      </p>}
      {item && !firstPass && <ProviderSource item={item} current={current} historical={historical} systemId={systemId} />}
      {reviewing ? <section aria-label="Draft review" className="space-y-3 text-sm">
        <h3 tabIndex={-1} ref={summary} className="font-semibold">Review before confirming</h3>
        {allocation === '' ? <>
          <p className="whitespace-pre-wrap">{fields.information}</p>
          <p>{firstPass ? 'Save this information gap as a proposed draft. Accepted responsibility remains unchanged.'
            : 'No server draft or proposal is saved. This information gap remains only in the open panel. Closing or leaving this page discards it; the saved allocation stays unchanged.'}</p>
        </> : <>
          <p className="font-medium">Proposed allocation: {allocation}</p>
          {providerAllocation && <p>Provider: {fields.provider}</p>}
          <dl className="space-y-2">{entries.map(([label, value]) => <div key={label}>
            <dt className="font-medium">{label}</dt><dd className="whitespace-pre-wrap">{value}</dd>
          </div>)}
            <div><dt className="font-medium">Customer duties</dt><dd className="whitespace-pre-wrap">{fields.customer || 'None recorded'}</dd></div>
          </dl>
          <p>Confirmation records your review and reconciles eligible agreed allocations into the current baseline.
            Conflicting sources and existing overrides may prevent application. Approved narratives remain unchanged.</p>
          <label className="flex items-start gap-2">
            <input type="checkbox" checked={acknowledged} disabled={!canConfirm || blocked || busy}
              onChange={event => setAcknowledged(event.target.checked)} />
            I verified provider coverage for this allocation (including no provider reliance for Customer)
            and reviewed local duties against the displayed source and baseline.
          </label>
        </>}
      </section> : <fieldset disabled={!canConfirm || busy || prepared.context?.canPrepare === false} className="min-w-0 space-y-4">
        <legend className="mb-3 font-semibold">Who does what for this control?</legend>
        <div className="space-y-2">{choices.map(choice => <label key={choice.value}
          className={`flex cursor-pointer items-start gap-3 rounded-lg border p-3 text-sm ${allocation === choice.value
            ? 'border-indigo-500 bg-indigo-50 dark:bg-indigo-950' : 'border-gray-200 dark:border-gray-700'}`}>
          <input type="radio" name={`${id}-allocation`} value={choice.value} checked={allocation === choice.value}
            onChange={() => { setAllocation(choice.value); edited.current.add('allocation'); setAcknowledged(false); }} />
          <span className="min-w-0 flex-1"><strong>{choice.title}</strong><span className="block text-xs text-gray-600 dark:text-gray-300">{choice.detail}</span></span>
          {choice.value && <span className="text-xs">{choice.value}</span>}
        </label>)}</div>
        {firstPass && provenance('allocation')}
        {allocation === '' ? textField('information', 'Information needed', 'Describe the missing scope, evidence or ownership decision. This is not saved to the server.') : <>
          {providerAllocation && <label className="grid gap-1 text-sm">Provider for {controlId}
            <input aria-label={`Provider for ${controlId}`} className={inputClass} maxLength={200} value={fields.provider} onChange={event => change('provider', event.target.value)} />
            {firstPass && provenance('provider')}
          </label>}
          {providerAllocation && textField('providerDuties', 'Provider duties', 'Describe verified coverage, not unrelated capabilities.')}
          {allocation === 'Inherited' && <>
            {textField('scope', 'Applicable scope', 'Identify the system resources covered by the provider.')}
            {textField('exclusions', 'Exclusions', 'Record exclusions, or explicitly state none after review.')}
            {textField('source', 'Supporting source', 'Name the evidence record and version you actually reviewed.')}
          </>}
          {textField('customer', allocation === 'Inherited' ? 'Local operational duties (optional)' : 'Customer duties',
            'Retain local configuration, uncovered resources, owners and evidence tasks.')}
          {textField('basis', 'Basis for this allocation', 'Explain the reviewed scope, evidence and any limitations.')}
          <p className="text-xs text-gray-500 dark:text-gray-400">Review context: {notes.length}/2000 characters.</p>
          {notes.length > 2000 && <p role="alert" className={errorClass}>Shorten the review context to 2000 characters before confirming.</p>}
        </>}
      </fieldset>}
      <p className="text-xs text-gray-600 dark:text-gray-300">
        This work supports the responsibility matrix, CRM and SSP preparation. A local draft is not accepted inheritance.
        Confirmation does not satisfy a control, approve a narrative, submit to eMASS or authorize a system.
      </p>
    </div>
  </SetupDialog>;
}

function ProviderSource({ item, current, historical, systemId }: {
  item: CapabilityResponsibilityItem; current: ProviderReviewSnapshot | null;
  historical: ProviderReviewSnapshot | null; systemId: string;
}) {
  const snapshot = current ?? historical;
  return <section aria-label="Provider contribution" className="space-y-2 rounded-lg border border-gray-200 bg-gray-50 p-4 text-sm dark:border-gray-700 dark:bg-gray-800">
    <h3 className="font-semibold">Provider contribution</h3>
    <p className="font-medium">{item.providerName ?? 'Provider name unavailable'} · {snapshot?.Name ?? 'Capability unavailable'}</p>
    <p>{snapshot?.Component.Name ?? 'Component unavailable'}{!current && historical ? ' (historical)' : ''}</p>
    <p className="text-amber-800 dark:text-amber-200">System scope and evidence sufficiency are not verified by a mapping.
      {snapshot?.Component.SourceArtifactReference ? ' The original evidence is redacted in this preview.' : ' No supporting artifact reference is supplied.'}</p>
    <details>
      <summary className="cursor-pointer font-medium text-indigo-700 dark:text-indigo-300">Review provider scope & evidence</summary>
      <div className="mt-3 space-y-3">
        <p className="whitespace-pre-wrap">{snapshot?.Description ?? 'No current source description is available.'}</p>
        {snapshot?.Component.Description !== snapshot?.Description && <p className="whitespace-pre-wrap">{snapshot?.Component.Description}</p>}
        <p>Mappings: {snapshot?.Controls.join(', ') || 'Unavailable'}. A mapping is not accepted inheritance.</p>
        <p>Scope, exclusions and retention must be verified in the supporting source; this preview supplies no independently verified values.</p>
        <Link className="text-indigo-700 underline dark:text-indigo-300" to={`/systems/${encodeURIComponent(systemId)}/evidence`}>Review evidence</Link>
        <details>
          <summary className="cursor-pointer font-medium">Technical source details</summary>
          <div className="mt-2 space-y-2 break-all text-xs">
            <p>Current source revision: <span>{item.sourceRevision}</span></p>
            <p>Review revision: {item.reviewRevision}</p>
            <p>Previously reviewed revision: {item.reviewedSourceRevision ?? 'Not confirmed'}</p>
            <p>Component: {item.componentId ?? 'Unavailable'} · Profile: {item.cspProfileId ?? 'Unavailable'}</p>
            <p>Current redacted snapshot</p><pre className="whitespace-pre-wrap">{JSON.stringify(current, null, 2)}</pre>
            <p>Previously reviewed redacted snapshot</p><pre className="whitespace-pre-wrap">{JSON.stringify(historical, null, 2)}</pre>
          </div>
        </details>
      </div>
    </details>
  </section>;
}
