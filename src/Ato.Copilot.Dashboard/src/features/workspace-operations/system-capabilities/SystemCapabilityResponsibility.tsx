import { useEffect, useRef, useState, type FormEvent } from 'react';
import {
  isResponsibilityType, readProviderSnapshot, readReviewedProviderSnapshot,
  type CapabilityResponsibilityItem, type CapabilityResponsibilityResponse, type ProviderReviewSnapshot,
  type ResponsibilityInheritanceType,
} from '../../../api/capabilityResponsibilities';
import { Link } from '../../workspaces/workspaceNavigation';
import { buttonClass, errorClass, inputClass, message, warningClass } from '../workspaceUi';
import { workspaceCard } from '../CapabilityPresentation';
import { boundedRequest } from './systemCapabilityRequests';
import { confirmSystemCapabilityResponsibilities, type SystemCapabilityReviewEvidence } from './systemCapabilityResponsibilityRequests';

interface Props {
  tenantId: string;
  systemId: string;
  capabilityId: string;
  controlId: string;
  sourceRevision: string;
  reviewRevision: string | null;
  preview: CapabilityResponsibilityResponse;
  onChanged: (preview?: CapabilityResponsibilityResponse) => void;
}

export default function SystemCapabilityResponsibility(props: Props) {
  const { preview, systemId, capabilityId, controlId } = props;
  const items = preview.items.filter(item => item.capabilityId === capabilityId && item.controlId === controlId);
  if (preview.systemId !== systemId || items.length !== 1) {
    return <p role="alert" className={errorClass}>The responsibility preview does not match this system, capability and control.</p>;
  }
  const item = items[0]!;
  if (item.sourceRevision !== props.sourceRevision || item.reviewRevision !== props.reviewRevision)
    return <p role="alert" className={errorClass}>The control detail and responsibility revisions do not match. Refresh this capability before reviewing.</p>;
  return <ControlReview key={`${systemId}:${capabilityId}:${controlId}:${preview.baselineId}:${item.sourceRevision}:${item.reviewRevision}`}
    {...props} item={item} />;
}

const editableStates = new Set(['MissingAllocation', 'PendingReview', 'ConflictingAllocations', 'PreservedOverride', 'Applied', 'Ready']);

function Snapshot({ snapshot, title, revision }: { snapshot: ProviderReviewSnapshot | null; title: string; revision: string | null }) {
  return <section aria-label={title} className="min-w-0 space-y-2 rounded-lg border border-slate-200 p-4 text-sm dark:border-gray-700">
    <h3 className="font-semibold">{title}</h3>
    <p className="break-all text-xs text-slate-500 dark:text-gray-400">Revision: {revision ?? 'Not confirmed'}</p>
    {snapshot ? <>
      <p className="font-medium">{snapshot.Name}</p><p className="whitespace-pre-wrap">{snapshot.Description}</p>
      <p>{snapshot.Component.Name} · {snapshot.Component.Description}</p>
      <p>Mapped controls: {snapshot.Controls.join(', ') || 'None'}</p>
      <details><summary className="cursor-pointer text-indigo-700 dark:text-indigo-300">Exact redacted source snapshot</summary>
        <pre className="mt-2 whitespace-pre-wrap break-all text-xs">{JSON.stringify(snapshot, null, 2)}</pre>
      </details>
    </> : <p>{title.startsWith('Confirmed') ? 'No confirmed source snapshot is available.' : 'The source is unavailable.'}</p>}
  </section>;
}

function ControlReview({ tenantId, systemId, capabilityId, controlId, preview, item, onChanged }: Props & {
  item: CapabilityResponsibilityItem & SystemCapabilityReviewEvidence;
}) {
  const [allocation, setAllocation] = useState<ResponsibilityInheritanceType | ''>(item.allocation?.inheritanceType ?? '');
  const [provider, setProvider] = useState(item.allocation?.provider ?? '');
  const [duty, setDuty] = useState(item.allocation?.customerResponsibility ?? '');
  const [coverageReviewed, setCoverageReviewed] = useState(false);
  const [dutiesReviewed, setDutiesReviewed] = useState(false);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [invalidated, setInvalidated] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inFlight = useRef(false);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => controller.current?.abort(), []);
  let current: ProviderReviewSnapshot | null = null;
  let confirmed: ProviderReviewSnapshot | null = null;
  let snapshotError: string | null = null;
  try { current = readProviderSnapshot(item); confirmed = readReviewedProviderSnapshot(item); }
  catch (reason) { snapshotError = message(reason); }
  const mapped = current?.Controls.some(value => value.toUpperCase() === controlId.toUpperCase()) === true;
  const permitted = preview.canConfirm && !!preview.baselineId && item.sourceAvailable && mapped
    && !snapshotError && editableStates.has(item.state) && !invalidated;
  const valid = isResponsibilityType(allocation) && (allocation === 'Customer' || !!provider.trim())
    && (allocation === 'Inherited' || !!duty.trim()) && coverageReviewed && dutiesReviewed && !!note.trim() && note.trim().length <= 2000;
  const resetChecks = () => { setCoverageReviewed(false); setDutiesReviewed(false); };
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!permitted || !valid || !preview.baselineId || !isResponsibilityType(allocation) || inFlight.current) return;
    const baselineId = preview.baselineId;
    inFlight.current = true; setBusy(true); setError(null);
    const pending = new AbortController();
    controller.current = pending;
    try {
      const next = await boundedRequest(signal => confirmSystemCapabilityResponsibilities(tenantId, systemId, capabilityId, {
        baselineId, sourceRevision: item.sourceRevision, reviewRevision: item.reviewRevision,
        allocations: [{ controlId, inheritanceType: allocation, provider: provider.trim() || null, customerResponsibility: duty.trim() || null }],
        providerCoverageVerified: true, customerDutiesReviewed: true, reviewNotes: note.trim(),
      }, signal), pending.signal);
      if (!pending.signal.aborted) { resetChecks(); setNote(''); onChanged(next); }
    } catch (reason) {
      if (pending.signal.aborted) return;
      const status = (reason as { status?: number }).status;
      setError(`${message(reason)}${status === 409 ? ' The source, baseline or review changed. Reload and review again.' : ''}`);
      setInvalidated(true); resetChecks();
    } finally {
      inFlight.current = false;
      if (!pending.signal.aborted) setBusy(false);
    }
  }
  return <section aria-label={`Review ${controlId}`} className={`${workspaceCard} space-y-4`}>
    <h2 className="text-lg font-semibold">Review {controlId}</h2>
    <p className="text-sm">State: {item.state} · Effective allocation: {item.effectiveInheritanceType ?? 'Not designated'}</p>
    {(item.reviewNotes != null || item.providerCoverageVerified != null || item.customerDutiesReviewed != null) &&
      <section aria-label="Persisted responsibility review evidence" className="space-y-2 rounded border border-slate-200 p-3 text-sm dark:border-gray-700">
        <h3 className="font-semibold">Persisted review evidence</h3>
        <p>Provider coverage: {item.providerCoverageVerified === true ? 'Verified' : item.providerCoverageVerified === false ? 'Not verified' : 'Not recorded'}</p>
        <p>Customer duties: {item.customerDutiesReviewed === true ? 'Reviewed' : item.customerDutiesReviewed === false ? 'Not reviewed' : 'Not recorded'}</p>
        <p className="whitespace-pre-wrap">{item.reviewNotes ?? 'No review notes were recorded.'}</p>
      </section>}
    {item.reviewedSourceRevision && item.reviewNotes == null && item.providerCoverageVerified == null && item.customerDutiesReviewed == null &&
      <p className="text-sm text-slate-500 dark:text-gray-400">This historical review has no recorded acknowledgement checks or review notes.</p>}
    <div className="grid gap-3 md:grid-cols-2">
      <Snapshot snapshot={confirmed} title="Confirmed source snapshot" revision={item.reviewedSourceRevision} />
      <Snapshot snapshot={current} title="Available source snapshot" revision={item.sourceRevision} />
    </div>
    {confirmed && current && <details className="text-sm"><summary className="cursor-pointer font-semibold">Exact source changes</summary>
      <dl className="mt-3 space-y-2">
        <div><dt>Capability description before</dt><dd className="whitespace-pre-wrap">{confirmed.Description}</dd></div>
        <div><dt>Capability description now</dt><dd className="whitespace-pre-wrap">{current.Description}</dd></div>
        <div><dt>Added mappings</dt><dd>{current.Controls.filter(id => !confirmed!.Controls.includes(id)).join(', ') || 'None'}</dd></div>
        <div><dt>Removed mappings</dt><dd>{confirmed.Controls.filter(id => !current!.Controls.includes(id)).join(', ') || 'None'}</dd></div>
      </dl>
    </details>}
    {snapshotError && <p role="alert" className={errorClass}>{snapshotError}</p>}
    {error && <div role="alert" className={errorClass}><p>{error}</p><button type="button" className="mt-2 underline" onClick={() => onChanged()}>Refresh responsibilities</button></div>}
    {!preview.canConfirm && <p className={warningClass}>Read-only: an effective assigned ISSM or ISSO is required to confirm responsibility.</p>}
    {!preview.baselineId && <p className={warningClass}>Select a system baseline before confirmation.</p>}
    {!editableStates.has(item.state) && <p className={warningClass}>This contribution cannot be confirmed in its current state. Resolve it through the full system review.</p>}
    <form onSubmit={event => { void submit(event); }} className="space-y-3">
      <fieldset disabled={!permitted || busy} className="space-y-3">
        <legend className="mb-3 font-medium">Explicit allocation for {controlId}</legend>
        <label className="grid gap-1 text-sm">Responsibility allocation
          <select className={inputClass} value={allocation} onChange={event => {
            setAllocation(isResponsibilityType(event.target.value) ? event.target.value : ''); resetChecks();
          }}><option value="">Choose an allocation</option><option value="Inherited">Inherited</option><option value="Shared">Shared</option><option value="Customer">Customer</option></select>
        </label>
        <label className="grid gap-1 text-sm">Provider responsibility
          <textarea className={inputClass} value={provider} onChange={event => { setProvider(event.target.value); resetChecks(); }} />
        </label>
        <label className="grid gap-1 text-sm">Customer responsibility
          <textarea className={inputClass} value={duty} onChange={event => { setDuty(event.target.value); resetChecks(); }} />
        </label>
        <label className="flex gap-2 text-sm"><input type="checkbox" checked={coverageReviewed} onChange={event => setCoverageReviewed(event.target.checked)} />Provider coverage verified</label>
        <label className="flex gap-2 text-sm"><input type="checkbox" checked={dutiesReviewed} onChange={event => setDutiesReviewed(event.target.checked)} />Customer duties reviewed</label>
        <label className="grid gap-1 text-sm">Review notes (required)
          <textarea className={inputClass} maxLength={2000} value={note} onChange={event => setNote(event.target.value)} />
        </label>
      </fieldset>
      <button type="submit" className={buttonClass} disabled={!permitted || !valid || busy}>Confirm {controlId} responsibility</button>
      <p className="text-xs text-slate-500 dark:text-gray-400">Confirmation applies only to this control and the displayed source, review and baseline revisions. It does not approve a narrative or an ATO.</p>
    </form>
    <Link to={`/systems/${encodeURIComponent(systemId)}/inheritance/subscriptions`} className="text-sm text-indigo-700 underline dark:text-indigo-300">Open full system responsibility review</Link>
  </section>;
}
