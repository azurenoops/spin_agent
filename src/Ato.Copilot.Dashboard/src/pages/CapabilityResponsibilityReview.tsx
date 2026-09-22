import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useParams } from '../features/workspaces/workspaceNavigation';
import {
  confirmCapabilityResponsibilities, dispatchCapabilityResponsibilityImpacts, getCapabilityResponsibilities,
  isResponsibilityType, reconcileCapabilityResponsibilities, ResponsibilityApiError,
  type CapabilityResponsibilityAllocation, type CapabilityResponsibilityDispatchResponse,
  type CapabilityResponsibilityItem, type CapabilityResponsibilityResponse,
  type ConfirmCapabilityResponsibilitiesRequest, type ResponsibilityInheritanceType,
} from '../api/capabilityResponsibilities';

const button = 'rounded border border-indigo-600 px-3 py-2 text-sm text-indigo-700 hover:bg-indigo-50 disabled:cursor-not-allowed disabled:opacity-50';
const input = 'mt-1 block w-full rounded border border-gray-300 bg-white px-3 py-2 text-sm disabled:bg-gray-100';
const states: Record<string, { label: string; explanation: string }> = {
  MissingBaseline: { label: 'Missing baseline', explanation: 'Select a system baseline before reviewing this contribution.' },
  MissingAllocation: { label: 'Missing allocation', explanation: 'No explicit responsibility allocation has been confirmed.' },
  PendingReview: { label: 'Pending review', explanation: 'The provider source changed, is unavailable, or an overlapping source still needs review.' },
  ConflictingAllocations: { label: 'Conflicting allocations', explanation: 'Active source allocations disagree. No source takes automatic precedence.' },
  PreservedOverride: { label: 'Preserved override', explanation: 'An existing manual, imported or organization designation is preserved.' },
  OutsideBaseline: { label: 'Outside baseline', explanation: 'This control is not in the selected baseline and cannot be added by confirmation.' },
  Inactive: { label: 'Inactive', explanation: 'Subscription provenance is retained; this inactive contribution cannot be confirmed.' },
  Applied: { label: 'Applied', explanation: 'The current agreed allocation is applied to this system baseline.' },
  Ready: { label: 'Ready for reconciliation', explanation: 'Reviewed allocations are ready for explicit current-baseline reconciliation.' },
};
const editableStates = new Set(['MissingAllocation', 'PendingReview', 'ConflictingAllocations', 'PreservedOverride', 'Applied', 'Ready']);

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
  const mounted = useRef(true);
  const changing = useRef(false);
  const readController = useRef<AbortController | null>(null);
  const writeController = useRef<AbortController | null>(null);
  const base = `/systems/${encodeURIComponent(systemId)}`;

  const acceptPreview = useCallback((next: CapabilityResponsibilityResponse) => {
    setData(next);
    setGeneration(value => value + 1);
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
      if (mounted.current && !controller.signal.aborted) setLoadError(error instanceof Error ? error.message : 'Unable to read responsibility preview.');
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
    if (changing.current) return;
    if (data?.canConfirm !== true) {
      setActionError('Server-authorized ISSM or ISSO confirmation permission is required.');
      return;
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
    } catch (error) {
      if (!mounted.current || controller.signal.aborted) return;
      const message = error instanceof Error ? error.message : 'The responsibility operation failed.';
      if (error instanceof ResponsibilityApiError && error.status === 409) {
        setActionError(`${message} The baseline, source or review changed. Stale edits were cleared; review again before confirming.`);
        await load();
      } else if (error instanceof ResponsibilityApiError && (error.status === 403 || error.status === 404)) {
        setData(null);
        setLoadError(message);
      } else {
        setActionError(message);
      }
    } finally {
      changing.current = false;
      if (mounted.current && !controller.signal.aborted) setBusy(false);
    }
  };
  const confirm = (capabilityId: string, body: ConfirmCapabilityResponsibilitiesRequest) => mutate(async signal => {
    const next = await confirmCapabilityResponsibilities(systemId, capabilityId, body, signal);
    if (!mounted.current || signal.aborted) return;
    acceptPreview(next);
    setNotice('Selected allocations confirmed. Review the effective designations and any pending impacts below.');
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
  const groups = new Map<string, CapabilityResponsibilityItem[]>();
  for (const item of data?.items ?? []) {
    const group = groups.get(item.subscriptionId) ?? [];
    group.push(item);
    groups.set(item.subscriptionId, group);
  }

  return (
    <main className="space-y-6 p-4 sm:p-6">
      <header className="space-y-3">
        <Link to={`${base}/inheritance`} className="text-sm text-indigo-700 underline">Back to Control Inheritance</Link>
        <h1 className="text-2xl font-semibold">Subscription responsibility review</h1>
        <p className="text-sm text-gray-700">Review explicit per-control allocations for this system. A capability subscription or mapped control is not proof of inheritance.</p>
        <nav aria-label="Responsibility review navigation" className="flex flex-wrap gap-4 text-sm text-indigo-700">
          <Link to={`${base}/baseline`} className="underline">Select or review baseline</Link>
          <Link to={`${base}/capability-coverage`} className="underline">System capabilities</Link>
          <Link to={`${base}/narratives/review`} className="underline">Narrative Review</Link>
        </nav>
      </header>
      {actionError && <p role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm text-red-800">{actionError}</p>}
      {notice && <p role="status" className="rounded border border-green-300 bg-green-50 p-3 text-sm">{notice}</p>}
      {dispatch && <section aria-label="Review impact delivery" className="space-y-3 rounded border border-indigo-300 p-3 text-sm">
        <p role="status">Review impact delivery: {dispatch.delivered} delivered, {dispatch.pending} pending.
          {' '}Delivery is mark-only; no model generation or approval was requested.</p>
        {dispatch.deferred.length > 0 && <ul className="space-y-2">{dispatch.deferred.map(entry => (
          <li key={entry.impactId} className="break-words text-amber-900">
            {entry.controlId}: {entry.reason === 'MissingNarrative'
              ? 'missing narrative; create the required system narrative before retrying delivery.'
              : `delivery deferred (${entry.reason}).`}
            {' '}Impact {entry.impactId} remains pending.
          </li>
        ))}</ul>}
        {dispatch.proposalIds.length > 0 ? <ul className="space-y-2">{dispatch.proposalIds.map(id => (
          <li key={id}><Link className="break-all text-indigo-700 underline" to={`${base}/narratives/review?proposal=${encodeURIComponent(id)}`}>
            Review queued proposal {id}
          </Link></li>
        ))}</ul> : <p>No proposal IDs were returned by this delivery.</p>}
      </section>}
      {loading ? <p role="status">Loading responsibility preview…</p> : loadError ? (
        <div className="space-y-3">
          <p role="alert" className="text-red-800">{loadError}</p>
          <button className={button} onClick={() => { setActionError(null); void load(); }} disabled={busy}>Retry preview</button>
        </div>
      ) : data && <>
        <section className="space-y-3 rounded border bg-gray-50 p-4" aria-label="Responsibility prerequisites">
          <p className="break-all text-sm"><strong>System:</strong> {data.systemId}</p>
          <p className="break-all text-sm"><strong>Baseline:</strong> {data.baselineId ?? 'Not selected'}</p>
          {!data.baselineId && <p className="text-sm text-amber-900">Select a baseline before confirming allocations.</p>}
          {!data.canConfirm && <p className="text-sm text-amber-900">Read-only: an effective assigned ISSM or ISSO is required to confirm, reconcile or deliver review impacts.</p>}
          <div className="flex flex-wrap gap-3">
            <button type="button" className={button} disabled={busy || !data.canConfirm || !data.baselineId} onClick={() => { void reconcile(); }}>
              Reconcile current baseline
            </button>
            <button type="button" className={button} disabled={busy || !data.canConfirm || data.pendingImpacts.length === 0} onClick={() => { void deliver(); }}>
              Deliver pending review impacts
            </button>
          </div>
          <p className="text-xs text-gray-600">Reconciliation preserves unowned overrides. Impact delivery marks review work only; generation and approval remain separate authorized narrative actions.</p>
        </section>
        {data.items.length === 0 && <p>No subscription control contributions are available for this system.</p>}
        {[...groups].map(([subscriptionId, items]) => <SubscriptionReview key={`${generation}:${subscriptionId}`} items={items}
          baselineId={data.baselineId} canConfirm={data.canConfirm} busy={busy} onConfirm={confirm} systemId={systemId} />)}
        <section aria-label="Pending review impacts" className="space-y-3">
          <h2 className="text-lg font-semibold">Pending review impacts ({data.pendingImpacts.length})</h2>
          {data.pendingImpacts.length === 0 ? <p className="text-sm text-gray-600">No durable pending impacts reported.</p> : data.pendingImpacts.map(impact => (
            <article key={impact.id} className="space-y-2 rounded border p-4 text-sm">
              <h3 className="font-semibold">{impact.controlId}</h3>
              <p>{impact.reason}</p>
              <p className="break-all">Impact {impact.id} · Baseline {impact.baselineId} · {impact.createdAt}</p>
              <p className="break-all">State {impact.stateHash}</p>
              <details><summary className="cursor-pointer">Source and subscription provenance</summary>
                <pre className="mt-2 whitespace-pre-wrap break-all rounded bg-gray-50 p-3 text-xs">{impact.sourcesJson}</pre>
              </details>
            </article>
          ))}
        </section>
      </>}
    </main>
  );
}

interface AllocationDraft {
  inheritanceType: ResponsibilityInheritanceType | '';
  provider: string;
  customerResponsibility: string;
}
const emptyDraft: AllocationDraft = { inheritanceType: '', provider: '', customerResponsibility: '' };

function SubscriptionReview({ items, baselineId, canConfirm, busy, onConfirm, systemId }: {
  items: CapabilityResponsibilityItem[]; baselineId: string | null; canConfirm: boolean; busy: boolean; systemId: string;
  onConfirm: (capabilityId: string, body: ConfirmCapabilityResponsibilitiesRequest) => Promise<void>;
}) {
  const first = items[0]!;
  const [drafts, setDrafts] = useState<Record<string, AllocationDraft>>({});
  const [reviewed, setReviewed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const consistent = items.every(item => item.capabilityId === first.capabilityId
    && item.sourceRevision === first.sourceRevision && item.reviewRevision === first.reviewRevision);
  const editable = canConfirm && !!baselineId && consistent && items.some(item => editableStates.has(item.state));
  const selected: CapabilityResponsibilityAllocation[] = Object.entries(drafts).flatMap(([controlId, draft]) =>
    isResponsibilityType(draft.inheritanceType) ? [{
      controlId, inheritanceType: draft.inheritanceType, provider: draft.provider.trim() || null,
      customerResponsibility: draft.customerResponsibility.trim() || null,
    }] : []);
  const valid = selected.length > 0 && selected.length <= 100 && selected.every(allocation =>
    (allocation.inheritanceType === 'Customer' || !!allocation.provider)
    && (allocation.inheritanceType === 'Inherited' || !!allocation.customerResponsibility));
  const update = (controlId: string, change: Partial<AllocationDraft>) => {
    setDrafts(current => ({ ...current, [controlId]: { ...(current[controlId] ?? emptyDraft), ...change } }));
    setReviewed(false);
    setError(null);
  };
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!editable || !baselineId || !valid || !reviewed || busy) {
      setError('Review between 1 and 100 explicit allocations and complete every required field before confirming.');
      return;
    }
    void onConfirm(first.capabilityId, {
      baselineId, sourceRevision: first.sourceRevision, reviewRevision: first.reviewRevision, allocations: selected,
    });
  };

  return (
    <section aria-label={`Subscription ${first.subscriptionId}`} className="space-y-4 rounded border border-gray-300 p-4">
      <header className="space-y-2 text-sm">
        <h2 className="break-all text-lg font-semibold">Subscription {first.subscriptionId}</h2>
        <p className="break-all">Capability: <Link className="text-indigo-700 underline" to={`/capability-library/${encodeURIComponent(first.capabilityId)}?systemId=${encodeURIComponent(systemId)}`}>{first.capabilityId}</Link></p>
        <dl className="grid gap-2 sm:grid-cols-2">
          <div><dt className="font-medium">Provider component</dt><dd className="break-all">{first.componentId ?? 'Unavailable'}</dd></div>
          <div><dt className="font-medium">Provider profile</dt><dd className="break-all">{first.cspProfileId ?? 'Unavailable'}</dd></div>
          <div><dt className="font-medium">Current provider snapshot revision</dt><dd className="break-all">{first.sourceRevision}</dd></div>
          <div><dt className="font-medium">Displayed review revision</dt><dd className="break-all">{first.reviewRevision}</dd></div>
        </dl>
        {!consistent && <p role="alert">This subscription returned inconsistent revisions. Reload the preview before confirming.</p>}
      </header>
      <form onSubmit={submit} className="space-y-4">
        <fieldset disabled={busy} className="space-y-4">
          <legend className="sr-only">Explicit allocations for subscription {first.subscriptionId}</legend>
          {items.map(item => {
            const status = states[item.state];
            const draft = drafts[item.controlId] ?? emptyDraft;
            const key = `${first.subscriptionId}-${item.controlId}`;
            return <article key={item.controlId} className="space-y-3 rounded border bg-gray-50 p-4">
              <div className="flex flex-wrap items-center gap-3">
                <h3 className="font-semibold">{item.controlId}</h3>
                <span className="rounded border bg-white px-2 py-1 text-xs">{status?.label ?? `Unknown state: ${item.state}`}</span>
              </div>
              <p className="text-sm text-gray-700">{status?.explanation ?? 'Refresh or contact support. This unrecognized state cannot be confirmed.'}</p>
              <p className="text-sm">Effective designation: <strong>{item.effectiveInheritanceType ? `${item.effectiveInheritanceType} · ${item.designationSource ?? 'Source not reported'}` : 'Not designated'}</strong></p>
              {item.allocation && <div className="space-y-1 text-sm">
                <p>Previously confirmed: {item.allocation.inheritanceType} · Provider: {item.allocation.provider ?? 'None'}</p>
                <p>Customer responsibility: {item.allocation.customerResponsibility ?? 'None'}</p>
                <p className="break-all">Reviewed source: {item.reviewedSourceRevision ?? 'Not reported'}</p>
                <p>Confirmed by {item.confirmedBy ?? 'Not reported'} at {item.confirmedAt ?? 'Not reported'}</p>
              </div>}
              {editable && editableStates.has(item.state) && <div className="grid gap-3 sm:grid-cols-2">
                <label className="text-sm" htmlFor={`${key}-allocation`}>Allocation for {item.controlId}
                  <select id={`${key}-allocation`} className={input} value={draft.inheritanceType} onChange={event =>
                    update(item.controlId, { inheritanceType: isResponsibilityType(event.target.value) ? event.target.value : '' })}>
                    <option value="">Choose explicitly</option>
                    <option value="Inherited">Inherited</option><option value="Shared">Shared</option><option value="Customer">Customer</option>
                  </select>
                </label>
                <label className="text-sm" htmlFor={`${key}-provider`}>Provider for {item.controlId}
                  <input id={`${key}-provider`} aria-label={`Provider for ${item.controlId}`} className={input} maxLength={200} value={draft.provider} disabled={!draft.inheritanceType}
                    required={draft.inheritanceType === 'Inherited' || draft.inheritanceType === 'Shared'}
                    onChange={event => update(item.controlId, { provider: event.target.value })} />
                  <span className="text-xs text-gray-600">Required for Inherited and Shared.</span>
                </label>
                <label className="text-sm sm:col-span-2" htmlFor={`${key}-customer`}>Customer responsibility for {item.controlId}
                  <textarea id={`${key}-customer`} aria-label={`Customer responsibility for ${item.controlId}`} className={input} rows={3} maxLength={2000} value={draft.customerResponsibility}
                    disabled={!draft.inheritanceType} required={draft.inheritanceType === 'Shared' || draft.inheritanceType === 'Customer'}
                    onChange={event => update(item.controlId, { customerResponsibility: event.target.value })} />
                  <span className="text-xs text-gray-600">Required for Shared and Customer.</span>
                </label>
              </div>}
            </article>;
          })}
          {editable && <>
            <label className="flex items-start gap-2 text-sm">
              <input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} />
              I reviewed the provider revision and the selected allocations.
            </label>
            <p className="text-xs text-gray-600">{selected.length} selected; maximum 100 per confirmation. Unselected controls are not confirmed.</p>
            {error && <p role="alert" className="text-sm text-red-800">{error}</p>}
            <button type="submit" className={button} disabled={busy || !valid || !reviewed}>Confirm selected allocations</button>
          </>}
        </fieldset>
      </form>
    </section>
  );
}
