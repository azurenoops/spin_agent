import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import {
  confirmCapabilityResponsibilities,
  dispatchCapabilityResponsibilityImpacts,
  getCapabilityResponsibilities,
  isResponsibilityType,
  readProviderSnapshot,
  readReviewedProviderSnapshot,
  reconcileCapabilityResponsibilities,
  ResponsibilityApiError,
  type CapabilityResponsibilityDispatchResponse,
  type CapabilityResponsibilityItem,
  type CapabilityResponsibilityResponse,
  type ConfirmCapabilityResponsibilitiesRequest,
  type ProviderReviewSnapshot,
  type ResponsibilityInheritanceType,
} from '../api/capabilityResponsibilities';
import SetupDialog from '../features/workspace-operations/SetupDialog';
import {
  buttonClass,
  errorClass,
  inputClass,
  secondaryButtonClass,
  warningClass,
} from '../features/workspace-operations/workspaceUi';
import { Link, useParams } from '../features/workspaces/workspaceNavigation';

const states: Record<string, { label: string; explanation: string }> = {
  MissingBaseline: { label: 'Missing baseline', explanation: 'Select a system baseline before reviewing this contribution.' },
  MissingAllocation: { label: 'Missing allocation', explanation: 'No explicit responsibility allocation has been confirmed.' },
  PendingReview: { label: 'Pending review', explanation: 'The provider source changed, is unavailable, or an overlapping source still needs review.' },
  ConflictingAllocations: { label: 'Conflicting allocations', explanation: 'Active source allocations disagree. No source takes automatic precedence.' },
  PreservedOverride: { label: 'Preserved override', explanation: 'An existing manual, imported or organization designation is preserved.' },
  OutsideBaseline: { label: 'Outside baseline', explanation: 'This control is not in the selected baseline and cannot be added by confirmation.' },
  Inactive: { label: 'Inactive', explanation: 'Subscription provenance is retained; this inactive contribution cannot be confirmed.' },
  Applied: { label: 'Source reviewed', explanation: 'The current agreed allocation is applied to this system baseline.' },
  Ready: { label: 'Ready for reconciliation', explanation: 'Reviewed allocations are ready for explicit current-baseline reconciliation.' },
};
const editableStates = new Set(['MissingAllocation', 'PendingReview', 'ConflictingAllocations', 'PreservedOverride', 'Applied', 'Ready']);
const linkClass = 'text-indigo-700 underline dark:text-indigo-300';

export default function CapabilityResponsibilityReview() {
  const { id } = useParams<{ id: string }>();
  if (!id) return <p role="alert">Select a system before reviewing subscription responsibilities.</p>;
  return <SystemResponsibilityReview key={id} systemId={id} />;
}

function SystemResponsibilityReview({ systemId }: { systemId: string }) {
  const [data, setData] = useState<CapabilityResponsibilityResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [generation, setGeneration] = useState(0);
  const [dispatch, setDispatch] = useState<CapabilityResponsibilityDispatchResponse | null>(null);
  const [selected, setSelected] = useState<{ subscriptionId: string; controlId: string } | null>(null);
  const mounted = useRef(true);
  const changing = useRef(false);
  const readController = useRef<AbortController | null>(null);
  const writeController = useRef<AbortController | null>(null);
  const base = `/systems/${encodeURIComponent(systemId)}`;

  const acceptPreview = useCallback((next: CapabilityResponsibilityResponse) => {
    setData(next);
    setGeneration(value => value + 1);
    setSelected(current => current && next.items.some(item =>
      item.subscriptionId === current.subscriptionId && item.controlId === current.controlId) ? current : null);
  }, []);
  const load = useCallback(async () => {
    readController.current?.abort();
    const controller = new AbortController();
    readController.current = controller;
    setLoading(true);
    setLoadError(null);
    setData(null);
    try {
      const next = await getCapabilityResponsibilities(systemId, controller.signal);
      if (mounted.current && !controller.signal.aborted) acceptPreview(next);
    } catch (error) {
      if (mounted.current && !controller.signal.aborted)
        setLoadError(error instanceof Error ? error.message : 'Unable to read responsibility preview.');
    } finally {
      if (mounted.current && !controller.signal.aborted) setLoading(false);
    }
  }, [systemId, acceptPreview]);

  useEffect(() => {
    mounted.current = true;
    void load();
    return () => {
      mounted.current = false;
      readController.current?.abort();
      writeController.current?.abort();
    };
  }, [load]);

  const mutate = async (operation: (signal: AbortSignal) => Promise<void>) => {
    if (changing.current) return false;
    if (data?.canConfirm !== true) {
      setActionError('Server-authorized ISSM or ISSO confirmation permission is required.');
      return false;
    }
    changing.current = true;
    setBusy(true);
    setActionError(null);
    setNotice(null);
    setDispatch(null);
    const controller = new AbortController();
    writeController.current = controller;
    try {
      await operation(controller.signal);
      return true;
    } catch (error) {
      if (!mounted.current || controller.signal.aborted) return false;
      const detail = error instanceof Error ? error.message : 'The responsibility operation failed.';
      if (error instanceof ResponsibilityApiError && error.status === 409) {
        setActionError(`${detail} The baseline, source or review changed. Stale edits were cleared; review again before confirming.`);
        setSelected(null);
        await load();
      } else if (error instanceof ResponsibilityApiError && (error.status === 403 || error.status === 404)) {
        setSelected(null);
        setData(null);
        setLoadError(detail);
      } else {
        setActionError(detail);
      }
      return false;
    } finally {
      changing.current = false;
      if (mounted.current && !controller.signal.aborted) setBusy(false);
    }
  };
  const confirm = (capabilityId: string, body: ConfirmCapabilityResponsibilitiesRequest) => mutate(async signal => {
    const next = await confirmCapabilityResponsibilities(systemId, capabilityId, body, signal);
    if (!mounted.current || signal.aborted) return;
    acceptPreview(next);
    setNotice('Selected allocation confirmed. Review the effective designation and any pending impacts.');
  });
  const reconcile = () => mutate(async signal => {
    const next = await reconcileCapabilityResponsibilities(systemId, signal);
    if (!mounted.current || signal.aborted) return;
    acceptPreview(next);
    setNotice('Current baseline reconciled. No narratives were generated or approved.');
  });
  const deliver = () => mutate(async signal => {
    const result = await dispatchCapabilityResponsibilityImpacts(systemId, signal);
    if (!mounted.current || signal.aborted) return;
    setDispatch(result);
    await load();
  });

  const selectedItem = selected && data?.items.find(item =>
    item.subscriptionId === selected.subscriptionId && item.controlId === selected.controlId);
  const reviewRequired = data?.items.filter(item => item.state !== 'Applied').length ?? 0;
  const firstReviewable = data?.items.find(item => editableStates.has(item.state)) ?? data?.items[0];
  const inconsistentSubscription = data && [...new Set(data.items.map(item => item.subscriptionId))].find(subscriptionId => {
    const items = data.items.filter(item => item.subscriptionId === subscriptionId);
    const first = items[0];
    return first && items.some(item => item.capabilityId !== first.capabilityId
      || item.sourceRevision !== first.sourceRevision || item.reviewRevision !== first.reviewRevision
      || item.sourceAvailable !== first.sourceAvailable || item.sourceSnapshotJson !== first.sourceSnapshotJson);
  });

  return <div className="min-w-0 space-y-6">
    <header className="flex flex-wrap items-start justify-between gap-4">
      <div>
        <p className="text-xs font-semibold uppercase tracking-wider text-gray-500">System control ownership</p>
        <h1 className="mt-2 text-2xl font-semibold">Control responsibilities</h1>
        <p className="mt-2 max-w-3xl text-sm text-gray-600 dark:text-gray-300">
          Confirm what the provider delivers and what your team must implement. A capability subscription or mapped control is not proof of inheritance.
        </p>
      </div>
      <button type="button" className={buttonClass} disabled={!firstReviewable || loading}
        onClick={() => firstReviewable && setSelected({
          subscriptionId: firstReviewable.subscriptionId,
          controlId: firstReviewable.controlId,
        })}>
        Review allocations
      </button>
    </header>

    {data && <div className="flex flex-wrap gap-x-6 gap-y-2 border-y border-gray-200 py-3 text-sm dark:border-gray-700">
      <span className="font-medium">{reviewRequired ? `Draft · ${reviewRequired} review required` : 'Applied · No pending reviews'}</span>
      <span className="text-gray-600 dark:text-gray-300">{data.items.length} control contributions</span>
      <span className="text-gray-600 dark:text-gray-300">Baseline: {data.baselineName ?? 'Not selected'}</span>
    </div>}

    {actionError && <p role="alert" className={errorClass}>{actionError}</p>}
    {inconsistentSubscription && <p role="alert" className={errorClass}>
      Subscription {inconsistentSubscription} returned inconsistent revisions. Reload the preview before confirming.
    </p>}
    {notice && <p role="status" className="rounded border border-green-300 bg-green-50 p-3 text-sm text-green-900">{notice}</p>}
    {dispatch && <DeliveryResult dispatch={dispatch} base={base} />}
    {loading ? <p role="status">Loading responsibility preview…</p> : loadError ? <div className="space-y-3">
      <p role="alert" className={errorClass}>{loadError}</p>
      <button className={secondaryButtonClass} onClick={() => { setActionError(null); void load(); }} disabled={busy}>Retry preview</button>
    </div> : data && <>
      <div className="grid items-start gap-8 lg:grid-cols-[minmax(0,1fr)_280px]">
        <ResponsibilityMatrix data={data} base={base} onOpen={item =>
          setSelected({ subscriptionId: item.subscriptionId, controlId: item.controlId })} />
        <aside aria-label="Responsibility supporting actions" className="space-y-5">
          <section className="border-l-2 border-indigo-200 pl-4">
            <p className="text-xs font-semibold uppercase tracking-wider text-gray-500">Used in your package</p>
            <h2 className="mt-2 text-sm font-semibold">CRM · Provider / shared / customer responsibilities</h2>
            <p className="mt-1 text-sm text-gray-600 dark:text-gray-300">
              Reviewed records supply the CRM and SSP. Draft edits do not replace the approved baseline.
            </p>
            <Link className={`${secondaryButtonClass} mt-3 inline-flex`} to={`${base}/documents#ssp-sections`}>Preview contribution →</Link>
          </section>
          <section className="border-l-2 border-indigo-200 pl-4">
            <p className="text-xs font-semibold uppercase tracking-wider text-gray-500">Review & ownership</p>
            <h2 className="mt-2 text-sm font-semibold">Keep the next action clear</h2>
            <p className="mt-1 text-sm text-gray-600 dark:text-gray-300">
              {reviewRequired ? `${reviewRequired} control ${reviewRequired === 1 ? 'allocation requires' : 'allocations require'} review.` : 'No control allocations require review.'}
            </p>
            {!data.baselineId && <p className={`${warningClass} mt-3 text-sm`}>
              Select a baseline before confirming allocations.{' '}
              <Link className={linkClass} to={`${base}/baseline`}>Select or review baseline</Link>
            </p>}
            {!data.canConfirm && <p className={`${warningClass} mt-3 text-sm`}>Read-only: an effective assigned ISSM or ISSO is required.</p>}
            <div className="mt-3 flex flex-col items-start gap-2">
              <button type="button" className={secondaryButtonClass} disabled={busy || !data.canConfirm || !data.baselineId}
                onClick={() => { void reconcile(); }}>Reconcile current baseline</button>
              <button type="button" className={secondaryButtonClass}
                disabled={busy || !data.canConfirm || data.pendingImpacts.length === 0}
                onClick={() => { void deliver(); }}>Deliver pending review impacts</button>
            </div>
          </section>
          <section className="border-l-2 border-indigo-200 pl-4">
            <p className="text-xs font-semibold uppercase tracking-wider text-gray-500">Related work</p>
            <Link className={`${secondaryButtonClass} mt-2 inline-flex`} to={base}>View package readiness →</Link>
          </section>
          <p className="text-xs text-gray-500">Confirmation applies only to the displayed source, review and baseline revisions. It does not approve a narrative or an ATO.</p>
        </aside>
      </div>
      <PendingImpacts items={data.pendingImpacts} />
    </>}

    {selectedItem && data && <ResponsibilityDrawer key={`${generation}:${selectedItem.subscriptionId}:${selectedItem.controlId}`}
      item={selectedItem} baselineId={data.baselineId} canConfirm={data.canConfirm} busy={busy}
      systemId={systemId} actionError={actionError} onClose={() => setSelected(null)} onConfirm={confirm} />}
  </div>;
}

function ResponsibilityMatrix({ data, base, onOpen }: {
  data: CapabilityResponsibilityResponse;
  base: string;
  onOpen: (item: CapabilityResponsibilityItem) => void;
}) {
  return <section aria-labelledby="responsibility-matrix-heading"
    className="overflow-hidden rounded-xl border border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-900">
    <div className="border-b border-gray-200 p-5 dark:border-gray-700">
      <h2 id="responsibility-matrix-heading" className="text-lg font-semibold">Responsibility matrix</h2>
      <p className="mt-1 text-sm text-gray-600 dark:text-gray-300">Review the provider capability, allocation, organization duty, and evidence for each control.</p>
    </div>
    {data.items.length ? <div className="overflow-x-auto"><table className="w-full text-left text-sm">
      <thead className="bg-gray-50 text-xs text-gray-600 dark:bg-gray-800 dark:text-gray-300">
        <tr>{['Control', 'Provider capability', 'Allocation', 'Review', ''].map((label, index) =>
          <th key={`${label}:${index}`} scope="col" className="px-4 py-3 font-medium">{label}</th>)}</tr>
      </thead>
      <tbody className="divide-y divide-gray-100 dark:divide-gray-800">{data.items.map(item => {
        const state = states[item.state];
        const allocation = item.effectiveInheritanceType ?? item.allocation?.inheritanceType ?? 'Not confirmed';
        let snapshot: ProviderReviewSnapshot | null = null;
        try {
          snapshot = readProviderSnapshot(item) ?? readReviewedProviderSnapshot(item);
        } catch {
          snapshot = null;
        }
        return <tr key={`${item.subscriptionId}:${item.controlId}`}>
          <td className="px-4 py-4 align-top">
            <p className="font-medium">{item.controlId}</p>
          </td>
          <td className="min-w-72 px-4 py-4 align-top">
            <p className="font-medium">{snapshot?.Name ?? 'Provider capability unavailable'}</p>
            <p className="mt-1 text-xs text-gray-500">
              {[item.providerName, snapshot?.Component.Name].filter(Boolean).join(' · ') || 'Provider details unavailable'}
            </p>
            <p className="mt-2 text-xs">{item.allocation?.customerResponsibility
              ? `Your duty: ${item.allocation.customerResponsibility}`
              : 'Your duties need review.'}</p>
            <Link className={`${linkClass} mt-2 inline-block text-xs`} aria-label={`Review evidence for ${item.controlId}`} to={`${base}/evidence`}>Review evidence</Link>
          </td>
          <td className="px-4 py-4 align-top">{allocation}</td>
          <td className="px-4 py-4 align-top">
            <span className="text-xs font-medium">{state?.label ?? `Unknown state: ${item.state}`}</span>
            {!item.sourceAvailable && <p className="mt-1 text-xs text-amber-800">Source unavailable</p>}
          </td>
          <td className="px-4 py-4 align-top">
            <button type="button" className={secondaryButtonClass} aria-label={`Open ${item.controlId} responsibility`}
              onClick={() => onOpen(item)}>Open →</button>
          </td>
        </tr>;
      })}</tbody>
    </table></div> : <p className="p-5 text-sm text-gray-600 dark:text-gray-300">No subscription control contributions are available for this system.</p>}
  </section>;
}

interface AllocationDraft {
  inheritanceType: ResponsibilityInheritanceType | '';
  provider: string;
  customerResponsibility: string;
}
const emptyDraft: AllocationDraft = { inheritanceType: '', provider: '', customerResponsibility: '' };

function ResponsibilityDrawer({ item, baselineId, canConfirm, busy, systemId, actionError, onClose, onConfirm }: {
  item: CapabilityResponsibilityItem;
  baselineId: string | null;
  canConfirm: boolean;
  busy: boolean;
  systemId: string;
  actionError: string | null;
  onClose: () => void;
  onConfirm: (capabilityId: string, body: ConfirmCapabilityResponsibilitiesRequest) => Promise<boolean>;
}) {
  const [draft, setDraft] = useState<AllocationDraft>(() => item.allocation ? {
    inheritanceType: item.allocation.inheritanceType,
    provider: item.allocation.provider ?? '',
    customerResponsibility: item.allocation.customerResponsibility ?? '',
  } : emptyDraft);
  const [reviewed, setReviewed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  let snapshot: ProviderReviewSnapshot | null = null;
  let reviewedSnapshot: ProviderReviewSnapshot | null = null;
  let snapshotError: string | null = null;
  try {
    snapshot = readProviderSnapshot(item);
    reviewedSnapshot = readReviewedProviderSnapshot(item);
  } catch (reason) {
    snapshotError = reason instanceof Error ? reason.message : 'The provider snapshot is malformed.';
  }
  const currentlyMapped = snapshot?.Controls.some(control => control.toUpperCase() === item.controlId.toUpperCase()) === true;
  const editable = canConfirm && !!baselineId && !snapshotError && item.sourceAvailable && snapshot !== null
    && editableStates.has(item.state) && currentlyMapped;
  const valid = isResponsibilityType(draft.inheritanceType)
    && (draft.inheritanceType === 'Customer' || !!draft.provider.trim())
    && (draft.inheritanceType === 'Inherited' || !!draft.customerResponsibility.trim())
    && reviewed;
  const update = (change: Partial<AllocationDraft>) => {
    setDraft(current => ({ ...current, ...change }));
    setReviewed(false);
    setError(null);
  };
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!editable || !baselineId || !valid || !isResponsibilityType(draft.inheritanceType) || busy) {
      setError('Choose an explicit allocation, complete every required duty, and acknowledge the displayed revisions.');
      return;
    }
    const succeeded = await onConfirm(item.capabilityId, {
      baselineId,
      sourceRevision: item.sourceRevision,
      reviewRevision: item.reviewRevision,
      allocations: [{
        controlId: item.controlId,
        inheritanceType: draft.inheritanceType,
        provider: draft.provider.trim() || null,
        customerResponsibility: draft.customerResponsibility.trim() || null,
      }],
    });
    if (succeeded) onClose();
  };

  return <SetupDialog title={`Review ${item.controlId} responsibility`} description="Review the provider source and confirm this system's explicit control allocation."
    placement="right" expanded busy={busy} onClose={onClose}>
    <div className="space-y-6">
      <section className="space-y-2">
        <div className="flex flex-wrap items-center gap-2">
          <span className="rounded border border-gray-200 bg-gray-50 px-2 py-1 text-xs font-medium dark:border-gray-700 dark:bg-gray-800">
            {states[item.state]?.label ?? `Unknown state: ${item.state}`}
          </span>
          <span className="text-xs text-gray-500">Effective: {item.effectiveInheritanceType ?? 'Not designated'}</span>
        </div>
        <p className="text-sm text-gray-600 dark:text-gray-300">{states[item.state]?.explanation ?? 'This state cannot be confirmed until the preview is refreshed.'}</p>
        <dl className="grid gap-2 text-sm sm:grid-cols-2">
          <div><dt className="font-medium">Provider capability</dt><dd>{snapshot?.Name ?? 'Unavailable'}</dd></div>
          <div><dt className="font-medium">Provider component</dt><dd>{snapshot?.Component.Name ?? 'Unavailable'}</dd></div>
          <div>
            <dt className="font-medium">Provider profile</dt>
            <dd>{item.providerName ?? 'Unavailable'}</dd>
          </div>
        </dl>
        <details className="text-sm">
          <summary className="cursor-pointer font-medium text-gray-600 dark:text-gray-300">Technical revision details</summary>
          <dl className="mt-3 grid gap-2 rounded border border-gray-200 bg-gray-50 p-3 text-xs dark:border-gray-700 dark:bg-gray-800 sm:grid-cols-2">
            <div><dt className="font-medium">Provider release fingerprint</dt><dd className="break-all">{item.sourceRevision}</dd></div>
            <div><dt className="font-medium">Review fingerprint</dt><dd className="break-all">{item.reviewRevision}</dd></div>
            <div><dt className="font-medium">Component ID</dt><dd className="break-all">{item.componentId ?? 'Unavailable'}</dd></div>
            <div><dt className="font-medium">Provider profile ID</dt><dd className="break-all">{item.cspProfileId ?? 'Unavailable'}</dd></div>
          </dl>
        </details>
      </section>

      {item.allocation && <section className="space-y-2 rounded-lg border border-gray-200 p-4 text-sm dark:border-gray-700">
        <h3 className="font-semibold">Previously confirmed allocation</h3>
        <p>{item.allocation.inheritanceType} · Provider: {item.allocation.provider ?? 'None'}</p>
        <p>Customer responsibility: {item.allocation.customerResponsibility ?? 'None'}</p>
        <p className="text-xs text-gray-500">Reviewed source {item.reviewedSourceRevision ?? 'not reported'} · Confirmed by {item.confirmedBy ?? 'not reported'} at {item.confirmedAt ?? 'not reported'}</p>
      </section>}

      {snapshotError && <p role="alert" className={errorClass}>{snapshotError}</p>}
      {!item.sourceAvailable && <p className={warningClass}>Provider source unavailable. Historical review remains visible, but confirmation is disabled.</p>}
      {snapshot && <ProviderSnapshotView snapshot={snapshot} label="Current provider snapshot" />}
      {reviewedSnapshot && !snapshot && <ProviderSnapshotView snapshot={reviewedSnapshot}
        label={`Reviewed provider snapshot for ${item.controlId}`} />}
      {reviewedSnapshot && snapshot && <details className="text-sm">
        <summary className="cursor-pointer font-medium">Compare reviewed and current provider snapshots for {item.controlId}</summary>
        <div className="mt-3 grid gap-3">
          <ProviderSnapshotView snapshot={reviewedSnapshot} label={`Reviewed provider snapshot for ${item.controlId}`} />
          <ProviderSnapshotView snapshot={snapshot} label={`Current provider snapshot for ${item.controlId}`} />
        </div>
      </details>}
      {snapshot && !currentlyMapped && <p className={warningClass}>{item.controlId} is no longer mapped by the current provider snapshot. Historical provenance is retained; confirmation is disabled.</p>}
      {!canConfirm && <p className={warningClass}>Read-only: an effective assigned ISSM or ISSO is required to confirm responsibility.</p>}
      {!baselineId && <p className={warningClass}>Select a system baseline before confirmation.</p>}
      {actionError && <p role="alert" className={errorClass}>{actionError}</p>}

      <form onSubmit={event => { void submit(event); }} className="space-y-4">
        <fieldset disabled={!editable || busy} className="space-y-4">
          <legend className="font-semibold">Explicit allocation for {item.controlId}</legend>
          <label className="grid gap-1 text-sm">Allocation for {item.controlId}
            <select className={inputClass} value={draft.inheritanceType} onChange={event => {
              const inheritanceType = isResponsibilityType(event.target.value) ? event.target.value : '';
              update({
                inheritanceType,
                provider: inheritanceType === 'Customer' || inheritanceType === ''
                  ? ''
                  : draft.provider || item.providerName || '',
                customerResponsibility: inheritanceType === 'Inherited' || inheritanceType === ''
                  ? ''
                  : draft.customerResponsibility,
              });
            }}>
              <option value="">Choose explicitly</option>
              <option value="Inherited">Inherited</option>
              <option value="Shared">Shared</option>
              <option value="Customer">Customer</option>
            </select>
          </label>
          <label className="grid gap-1 text-sm">Provider for {item.controlId}
            <input aria-label={`Provider for ${item.controlId}`} className={inputClass} maxLength={200} value={draft.provider} disabled={!draft.inheritanceType}
              onChange={event => update({ provider: event.target.value })} />
            <span className="text-xs text-gray-500">Required for Inherited and Shared.</span>
          </label>
          <label className="grid gap-1 text-sm">Customer responsibility for {item.controlId}
            <textarea aria-label={`Customer responsibility for ${item.controlId}`} className={inputClass} rows={4} maxLength={2000} value={draft.customerResponsibility} disabled={!draft.inheritanceType}
              onChange={event => update({ customerResponsibility: event.target.value })} />
            <span className="text-xs text-gray-500">Required for Shared and Customer.</span>
          </label>
          <label className="flex items-start gap-2 text-sm">
            <input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} />
            I reviewed the provider revision and the selected allocations.
          </label>
        </fieldset>
        {error && <p role="alert" className={errorClass}>{error}</p>}
        <div className="flex flex-wrap gap-3">
          <button type="submit" className={buttonClass} disabled={!editable || !valid || busy}>Confirm selected allocations</button>
          <Link className={secondaryButtonClass} to={`/systems/${encodeURIComponent(systemId)}/evidence`}>Review evidence</Link>
        </div>
      </form>
    </div>
  </SetupDialog>;
}

function ProviderSnapshotView({ snapshot, label }: {
  snapshot: ProviderReviewSnapshot;
  label: string;
}) {
  return <section aria-label={label} className="space-y-2 rounded-lg border border-gray-200 p-4 text-sm dark:border-gray-700">
    <h3 className="font-semibold">{label}</h3>
    <p className="font-medium">{snapshot.Name}</p>
    <p>{snapshot.Description}</p>
    <p>{snapshot.Component.Name} · {snapshot.Component.Description}</p>
    <p>Mapped controls: {snapshot.Controls.length ? snapshot.Controls.join(', ') : 'None'}</p>
    <p>Artifact reference: {snapshot.Component.SourceArtifactReference ?? 'Not supplied'}</p>
    <details><summary className={`cursor-pointer ${linkClass}`}>View redacted snapshot JSON</summary>
      <pre className="mt-2 whitespace-pre-wrap break-all text-xs">{JSON.stringify(snapshot, null, 2)}</pre>
    </details>
  </section>;
}

function DeliveryResult({ dispatch, base }: { dispatch: CapabilityResponsibilityDispatchResponse; base: string }) {
  return <section aria-label="Review impact delivery" className="space-y-3 rounded border border-indigo-300 p-3 text-sm">
    <p role="status">Review impact delivery: {dispatch.delivered} delivered, {dispatch.pending} pending. Delivery is mark-only; no model generation or approval was requested.</p>
    {dispatch.deferred.length > 0 && <ul className="space-y-2">{dispatch.deferred.map(entry => <li key={entry.impactId} className="text-amber-900">
      {entry.controlId}: {entry.reason === 'MissingNarrative' ? 'missing narrative; create the required system narrative before retrying delivery.' : `delivery deferred (${entry.reason}).`}
      {' '}Impact {entry.impactId} remains pending.
    </li>)}</ul>}
    {dispatch.proposalIds.map(id => <Link key={id} className={`block break-all ${linkClass}`}
      to={`${base}/narratives/review?proposal=${encodeURIComponent(id)}`}>Review queued proposal {id}</Link>)}
  </section>;
}

function PendingImpacts({ items }: { items: CapabilityResponsibilityResponse['pendingImpacts'] }) {
  return <section aria-label="Pending review impacts" className="space-y-3">
    <h2 className="text-lg font-semibold">Pending review impacts ({items.length})</h2>
    {items.length === 0 ? <p className="text-sm text-gray-600 dark:text-gray-300">No durable pending impacts reported.</p> : items.map(impact =>
      <details key={impact.id} className="rounded border border-gray-200 p-4 text-sm dark:border-gray-700">
        <summary className="cursor-pointer font-medium">{impact.controlId} · {impact.reason}</summary>
        <div className="mt-3 space-y-2">
          <p className="break-all">Impact {impact.id} · Baseline {impact.baselineId} · {impact.createdAt}</p>
          <p className="break-all">State {impact.stateHash}</p>
          <pre className="whitespace-pre-wrap break-all rounded bg-gray-50 p-3 text-xs dark:bg-gray-800">{impact.sourcesJson}</pre>
        </div>
      </details>)}
  </section>;
}
