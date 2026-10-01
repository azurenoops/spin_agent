import { useCallback, useEffect, useRef, useState } from 'react';
import {
  confirmCapabilityResponsibilities,
  dispatchCapabilityResponsibilityImpacts,
  getCapabilityResponsibilities,
  readProviderSnapshot,
  readReviewedProviderSnapshot,
  reconcileCapabilityResponsibilities,
  ResponsibilityApiError,
  type CapabilityResponsibilityDispatchResponse,
  type CapabilityResponsibilityItem,
  type CapabilityResponsibilityResponse,
  type ConfirmCapabilityResponsibilitiesRequest,
  type ProviderReviewSnapshot,
} from '../api/capabilityResponsibilities';
import ResponsibilityReviewPanel from '../components/ResponsibilityReviewPanel';
import {
  buttonClass,
  errorClass,
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
  const [blocked, setBlocked] = useState(false);
  const [generation, setGeneration] = useState(0);
  const [dispatch, setDispatch] = useState<CapabilityResponsibilityDispatchResponse | null>(null);
  const [selected, setSelected] = useState<{ subscriptionId: string; controlId: string } | null>(null);
  const [systemControl, setSystemControl] = useState('');
  const mounted = useRef(true);
  const changing = useRef(false);
  const readController = useRef<AbortController | null>(null);
  const writeController = useRef<AbortController | null>(null);
  const base = `/systems/${encodeURIComponent(systemId)}`;

  const acceptPreview = useCallback((next: CapabilityResponsibilityResponse) => {
    setData(next);
    setBlocked(false);
    setGeneration(value => value + 1);
    setSelected(current => current && (next.items.some(item =>
      item.subscriptionId === current.subscriptionId && item.controlId === current.controlId)
      || !current.subscriptionId && next.baselineControlIds?.includes(current.controlId)) ? current : null);
  }, []);
  const load = useCallback(async () => {
    readController.current?.abort();
    const controller = new AbortController();
    readController.current = controller;
    setLoading(true);
    setLoadError(null);
    setBlocked(true);
    try {
      const next = await getCapabilityResponsibilities(systemId, controller.signal);
      if (mounted.current && !controller.signal.aborted) { acceptPreview(next); setActionError(null); }
    } catch (error) {
      if (mounted.current && !controller.signal.aborted)
        setLoadError(`Preview refresh failed. ${error instanceof Error ? error.message : 'Unable to read responsibility preview.'}`);
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

  const mutate = async (label: string, operation: (signal: AbortSignal) => Promise<void>) => {
    if (changing.current || blocked) return false;
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
      const invalidInput = error instanceof ResponsibilityApiError && error.status === 400;
      if (error instanceof ResponsibilityApiError && (error.status === 401 || error.status === 403 || error.status === 404))
        setData(previous => previous ? { ...previous, canConfirm: false } : previous);
      setBlocked(!invalidInput && label !== 'Impact delivery');
      setActionError(`${label} failed. ${detail} ${invalidInput
        ? 'Correct the draft and review it again; no confirmation was accepted.'
        : error instanceof ResponsibilityApiError && error.status === 409
        ? 'The baseline, source or review changed. Your draft is preserved; refresh and review again.'
        : 'The saved outcome has not been verified. Your draft is preserved; refresh saved state before another write.'}`);
      return false;
    } finally {
      changing.current = false;
      if (mounted.current && !controller.signal.aborted) setBusy(false);
    }
  };
  const confirm = (capabilityId: string, body: ConfirmCapabilityResponsibilitiesRequest) => mutate('Confirmation', async signal => {
    const next = await confirmCapabilityResponsibilities(systemId, capabilityId, body, signal);
    if (!mounted.current || signal.aborted) return;
    acceptPreview(next);
    setNotice('Selected allocation confirmed. Review the effective designation and any pending impacts.');
  });
  const reconcile = () => mutate('Reconciliation', async signal => {
    const next = await reconcileCapabilityResponsibilities(systemId, signal);
    if (!mounted.current || signal.aborted) return;
    acceptPreview(next);
    setNotice('Current baseline reconciled. No narratives were generated or approved.');
  });
  const deliver = () => mutate('Impact delivery', async signal => {
    const result = await dispatchCapabilityResponsibilityImpacts(systemId, signal);
    if (!mounted.current || signal.aborted) return;
    setDispatch(result);
    await load();
  });

  const selectedItem = selected && data?.items.find(item =>
    item.subscriptionId === selected.subscriptionId && item.controlId === selected.controlId);
  const unallocatedSystemControls = data?.baselineControlIds?.filter(controlId =>
    !data.items.some(item => item.controlId === controlId)
    && !data.systemAllocations?.some(allocation => allocation.controlId === controlId)) ?? [];
  const reviewRequired = (data?.items.filter(item => item.state !== 'Applied').length ?? 0) + unallocatedSystemControls.length;
  const firstReviewable = data?.items.find(item => editableStates.has(item.state)) ?? data?.items[0];
  const firstSystemControl = unallocatedSystemControls[0] ?? data?.baselineControlIds?.[0];
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
      <button type="button" className={buttonClass} disabled={(!firstReviewable && !firstSystemControl) || loading}
        onClick={() => (firstReviewable || firstSystemControl) && setSelected({
          subscriptionId: firstReviewable?.subscriptionId ?? '',
          controlId: firstReviewable?.controlId ?? firstSystemControl!,
        })}>
        Review allocations
      </button>
    </header>

    {data && <div className="flex flex-wrap gap-x-6 gap-y-2 border-y border-gray-200 py-3 text-sm dark:border-gray-700">
      <span className="font-medium">{reviewRequired ? `Review required · ${reviewRequired} outstanding allocation${reviewRequired === 1 ? '' : 's'}` : 'No unresolved allocations reported'}</span>
      <span className="text-gray-600 dark:text-gray-300">{data.items.length} control contributions</span>
      <span className="text-gray-600 dark:text-gray-300">Baseline: {data.baselineName ?? 'Not selected'}</span>
    </div>}
    {data?.supportsResponsibilityDrafts && !!data.baselineControlIds?.length && <div className="flex flex-wrap items-end gap-3">
      <label className="grid gap-1 text-sm">Review any baseline control, including without a provider
        <select className="rounded border bg-white p-2 dark:bg-gray-900" value={systemControl}
          onChange={event => setSystemControl(event.target.value)}>
          <option value="">Select a control</option>
          {data.baselineControlIds.map(id => <option key={id}>{id}</option>)}
        </select>
      </label>
      <button type="button" className={secondaryButtonClass} disabled={!systemControl || busy}
        onClick={() => setSelected({ subscriptionId: '', controlId: systemControl })}>Review system control</button>
    </div>}

    {(loadError || actionError) && !selected && <div className="space-y-2">
      <p role="alert" className={errorClass}>{loadError ?? actionError}</p>
      <button className={secondaryButtonClass} onClick={() => { void load(); }} disabled={busy || loading}>Retry preview</button>
    </div>}
    {inconsistentSubscription && <p role="alert" className={errorClass}>
      Subscription {inconsistentSubscription} returned inconsistent revisions. Reload the preview before confirming.
    </p>}
    {notice && <p role="status" className="rounded border border-green-300 bg-green-50 p-3 text-sm text-green-900">{notice}</p>}
    {dispatch && <DeliveryResult dispatch={dispatch} base={base} />}
    {loading && <p role="status">Loading responsibility preview…</p>}
    {data && <>
      <div className="grid items-start gap-8 lg:grid-cols-[minmax(0,1fr)_280px]">
        <ResponsibilityMatrix data={data} base={base} onOpen={item =>
          setSelected({ subscriptionId: item.subscriptionId, controlId: item.controlId })}
          onOpenSystem={controlId => setSelected({ subscriptionId: '', controlId })} />
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
              <button type="button" className={secondaryButtonClass} disabled={busy || blocked || !data.canConfirm || !data.baselineId}
                onClick={() => { void reconcile(); }}>Reconcile current baseline</button>
              <button type="button" className={secondaryButtonClass}
                disabled={busy || blocked || !data.canConfirm || data.pendingImpacts.length === 0}
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

    {selected && data && (selectedItem || !selected.subscriptionId) && <ResponsibilityReviewPanel key={`${selected.subscriptionId}:${selected.controlId}`}
      item={selectedItem || null} controlId={selected.controlId} firstPass={data.supportsResponsibilityDrafts === true}
      onDraftConfirmed={() => { setNotice('Responsibility confirmed through authorized review. Check effective matrix and document contributions.'); void load(); }}
      baselineId={data.baselineId} canConfirm={data.canConfirm} busy={busy || loading}
      eligible={(!selectedItem || editableStates.has(selectedItem.state)) && !inconsistentSubscription}
      stateExplanation={selectedItem ? states[selectedItem.state]?.explanation ?? 'Unknown review state. Refresh before confirming.'
        : 'System-only preparation does not assume customer ownership or create a provider.'}
      generation={generation} blocked={blocked} onRefresh={() => { void load(); }}
      systemId={systemId} actionError={loadError ?? actionError} onClose={() => setSelected(null)} onConfirm={confirm} />}
  </div>;
}

function ResponsibilityMatrix({ data, base, onOpen, onOpenSystem }: {
  data: CapabilityResponsibilityResponse;
  base: string;
  onOpen: (item: CapabilityResponsibilityItem) => void;
  onOpenSystem: (controlId: string) => void;
}) {
  return <section aria-labelledby="responsibility-matrix-heading"
    className="overflow-hidden rounded-xl border border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-900">
    <div className="border-b border-gray-200 p-5 dark:border-gray-700">
      <h2 id="responsibility-matrix-heading" className="text-lg font-semibold">Responsibility matrix</h2>
      <p className="mt-1 text-sm text-gray-600 dark:text-gray-300">Review the provider capability, allocation, organization duty, and evidence for each control.</p>
    </div>
    {data.items.length || data.systemAllocations?.length ? <div className="overflow-x-auto"><table className="w-full text-left text-sm">
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
      })}
        {data.systemAllocations?.filter(allocation => !data.items.some(item => item.controlId === allocation.controlId))
          .map(allocation => <tr key={`system:${allocation.controlId}`}>
            <td className="px-4 py-4">{allocation.controlId}</td>
            <td className="px-4 py-4">{allocation.provider ?? 'System responsibility record'}
              <p className="text-xs">{allocation.customerResponsibility}</p></td>
            <td className="px-4 py-4">{allocation.inheritanceType}</td><td className="px-4 py-4">Recorded allocation</td>
            <td className="px-4 py-4"><button type="button" className={secondaryButtonClass}
              aria-label={`Open ${allocation.controlId} system responsibility`} onClick={() => onOpenSystem(allocation.controlId)}>Open →</button></td>
          </tr>)}
      </tbody>
    </table></div> : <p className="p-5 text-sm text-gray-600 dark:text-gray-300">No subscription control contributions are available for this system.</p>}
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
