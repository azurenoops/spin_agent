import { useEffect, useMemo, useRef, useState } from 'react';
import { Link } from '../../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../../workspaces/WorkspaceBoundary';
import { useSystemContext } from '../../../components/layout/SystemLayout';
import { buttonClass, errorClass, inputClass, message, Pager, secondaryButtonClass, Status, useQueryState, useRemote, warningClass } from '../workspaceUi';
import { ComponentIcon, StateBadge, workspaceCard } from '../CapabilityPresentation';
import * as api from './systemCapabilityApi';
import { boundedRequest, sameScope } from './systemCapabilityRequests';

type Page = Awaited<ReturnType<typeof api.listSystemCapabilities>>;
type Item = Page['items'][number];
type Operation = Awaited<ReturnType<typeof api.getSystemCapabilityOperation>>;
type Selection = Operation['selections'][number];
type Chosen = { item: Item; selection: Selection; supports: Item[] };
type Draft = { version: 1; identity: string; savedAt: number; idempotencyKey: string; chosen: Chosen[] };
const recordKey = (item: { source: string; recordId: string }) => `${item.source}:${item.recordId}`;
const systemWideOnly = (component: Item['components'][number]) => component.source === 'local' && component.componentType === 'Person';

export default function SystemCapabilitySetup(props: { tenantId: string; systemId: string }) {
  const session = useWorkspaceSession();
  const identity = JSON.stringify([session?.identity.directoryTenantId, session?.identity.oid,
    session?.workspace.mode, props.tenantId.toLowerCase(), props.systemId.toLowerCase()]);
  if (!session?.identity.directoryTenantId || !session.identity.oid)
    return <p role="alert" className={errorClass}>Your actor identity is unavailable. Reload the workspace before setting up capabilities.</p>;
  return <Setup key={identity} {...props} identity={identity} />;
}

function validDraftItem(item: Item | undefined): item is Item {
  return !!item && ['local', 'provider'].includes(item.source) && item.recordType === 'capability'
    && typeof item.recordId === 'string' && !!item.recordId && typeof item.name === 'string'
    && typeof item.sourceName === 'string' && typeof item.sourceRevision === 'string'
    && Array.isArray(item.controlIds) && item.controlIds.every(control => typeof control === 'string')
    && Array.isArray(item.components) && item.components.every(component =>
      component && ['local', 'provider'].includes(component.source) && typeof component.recordId === 'string'
      && typeof component.name === 'string' && typeof component.sourceName === 'string'
      && typeof component.componentType === 'string' && Array.isArray(component.placements)
      && component.placements.every(placement => placement && typeof placement.state === 'string'
        && (placement.boundaryName === null || typeof placement.boundaryName === 'string')));
}

function validChosen(entry: Chosen | undefined) {
  if (!entry || !validDraftItem(entry.item) || !entry.selection || !Array.isArray(entry.supports)
    || !entry.supports.every(item => validDraftItem(item) && item.source === 'local')) return false;
  const selection = entry.selection;
  const components = new Map([entry.item, ...entry.supports].flatMap(item =>
    item.components.map(component => [recordKey(component), component] as const)));
  return recordKey(entry.item) === recordKey(selection) && selection.sourceRevision === entry.item.sourceRevision
    && Array.isArray(selection.placements) && selection.placements.every(placement => {
      if (!placement || !['local', 'provider'].includes(placement.source) || typeof placement.componentId !== 'string') return false;
      const component = components.get(recordKey({ source: placement.source, recordId: placement.componentId }));
      return !!component && (!systemWideOnly(component) || placement.boundaryId === null)
        && (typeof placement.boundaryId === 'string' || placement.source === 'local' && placement.boundaryId === null);
    })
    && Array.isArray(selection.supportingCapabilities) && selection.supportingCapabilities.length === entry.supports.length
    && selection.supportingCapabilities.every(support => support && entry.supports.some(item =>
      item.recordId === support.recordId && item.sourceRevision === support.sourceRevision));
}

function readDraft(key: string, identity: string): Draft | null {
  const text = sessionStorage.getItem(key);
  if (!text) return null;
  const value = JSON.parse(text) as Draft;
  if (!value || value.version !== 1 || value.identity !== identity || typeof value.idempotencyKey !== 'string'
    || !Array.isArray(value.chosen) || value.chosen.length > 50 || !Number.isFinite(value.savedAt)
    || value.chosen.some(entry => !validChosen(entry)))
    throw new Error('The saved setup draft is invalid or belongs to a different actor or scope.');
  if (Date.now() - value.savedAt > 86400000) throw new Error('The saved setup draft expired after 24 hours. Discard it and reselect current sources.');
  return value;
}

function Setup({ tenantId, systemId, identity }: { tenantId: string; systemId: string; identity: string }) {
  const session = useWorkspaceSession();
  const { detail: system, setPageContext } = useSystemContext();
  const systemName = system.systemId === systemId ? system.name : systemId;
  const { params, set } = useQueryState();
  const operationId = params.get('operationId');
  const storageKey = `system-capability-setup:${encodeURIComponent(identity)}`;
  const [chosen, setChosen] = useState<Chosen[]>([]);
  const [hydrated, setHydrated] = useState(false);
  const [draftError, setDraftError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [uncertain, setUncertain] = useState(false);
  const [stale, setStale] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [savedOperation, setSavedOperation] = useState<Operation | null>(null);
  const [search, setSearch] = useState(params.get('search') ?? '');
  const key = useRef<string>(crypto.randomUUID());
  const active = useRef(true);
  const pending = useRef<AbortController | null>(null);
  const submitting = useRef(false);
  const base = `/systems/${encodeURIComponent(systemId)}/security-capabilities`;
  const pageNumber = Math.max(1, Number(params.get('page')) || 1);
  const source = params.get('source') === 'local' ? 'local' : params.get('source') === 'provider' ? 'provider' : undefined;
  const requestedId = params.get('recordId');
  const catalog = useRemote(signal => boundedRequest(inner => api.listSystemCapabilities(tenantId, systemId,
    { scope: 'available', grouping: 'capability', page: pageNumber, pageSize: 25, source, search: params.get('search') ?? undefined }, inner), signal),
  [tenantId, systemId, source, pageNumber, params.get('search')]);
  const requested = useRemote(async signal => {
    if (!requestedId || operationId) return null;
    if (!source) throw new Error('The requested capability needs an explicit organization or provider source.');
    const detail = await boundedRequest(inner => api.getSystemCapability(tenantId, systemId,
      { source, recordType: 'capability', recordId: requestedId }, inner), signal);
    if (!detail.permissions.canRead || !detail.permissions.canManage || !detail.item.isAvailable)
      throw new Error('The requested capability is not eligible for setup with your current permissions.');
    if (detail.item.isApplied) throw new Error('The requested capability is already applied to this system.');
    return detail.item;
  }, [tenantId, systemId, source, requestedId, operationId]);
  const recovered = useRemote(async signal => {
    if (!operationId) return null;
    const next = await boundedRequest(inner => api.getSystemCapabilityOperation(tenantId, systemId, operationId, inner), signal);
    if (!sameScope(next, tenantId, systemId, 'Setup')) throw new Error('The saved operation does not match this actor, tenant, system and setup scope.');
    return next;
  }, [tenantId, systemId, operationId]);
  const operation = savedOperation?.operationId === operationId
    && (!recovered.data || savedOperation.revision >= recovered.data.revision) ? savedOperation : recovered.data;
  const canManage = catalog.data?.permissions.canRead === true && catalog.data.permissions.canManage === true && !catalog.loading && !catalog.error;
  const step = operationId ? 3 : params.get('step') === '2' && chosen.length > 0 ? 2 : 1;
  const completed = operation?.state === 'Completed';
  const locked = busy || !canManage || !!draftError || !hydrated || !!operationId && recovered.loading
    || !!requestedId && requested.loading;
  const organizationName = session?.workspace.displayName ?? tenantId;
  const context = useMemo(() => <section className={`${workspaceCard} space-y-4`}>
    <h2 className="font-semibold">System context</h2><p className="text-sm">Target: {systemName}</p><p className="text-sm">{organizationName}</p>
    <p>{operation ? operation.selections.length : chosen.length} selected capabilities</p>
    {!operation && <ul className="space-y-2 text-sm">{chosen.map(entry => <li key={recordKey(entry.item)}>{entry.item.name} · {entry.item.sourceName}</li>)}</ul>}
    {operation && <p className="text-sm">{operation.outcomes.filter(outcome => outcome.state === 'Completed').length} completed write checkpoints · {operation.state}</p>}
    <p className="text-xs text-slate-500 dark:text-gray-400">Provider records remain read-only. Control review and narrative approval are separate from applicability.</p>
  </section>, [systemName, organizationName, operation, chosen]);
  useEffect(() => { setPageContext?.(context); return () => setPageContext?.(null); }, [setPageContext, context]);
  useEffect(() => {
    active.current = true;
    try {
      if (!operationId) {
        const draft = readDraft(storageKey, identity);
        if (draft) { setChosen(draft.chosen); key.current = draft.idempotencyKey; }
      }
    } catch (reason) { setDraftError(`Draft recovery unavailable: ${message(reason)}`); }
    setHydrated(true);
    return () => { active.current = false; pending.current?.abort(); };
  }, [identity, storageKey]);
  useEffect(() => { setSearch(params.get('search') ?? ''); }, [params]);
  useEffect(() => { setAcknowledged(false); }, [operation?.operationId, operation?.revision]);
  useEffect(() => {
    if (!completed) return;
    try { sessionStorage.removeItem(storageKey); }
    catch (reason) { setDraftError(`Server setup completed, but its local recovery draft could not be cleared: ${message(reason)}`); }
  }, [completed, storageKey]);
  const remember = (next: Chosen[]) => {
    try {
      const draft: Draft = { version: 1, identity, savedAt: Date.now(), idempotencyKey: key.current, chosen: next };
      sessionStorage.setItem(storageKey, JSON.stringify(draft));
      setChosen(next); setDraftError(null);
      return true;
    } catch (reason) { setDraftError(`Cannot save recovery state: ${message(reason)}. No setup request was sent.`); return false; }
  };
  const update = (next: Chosen[]) => {
    key.current = crypto.randomUUID();
    setError(null); setUncertain(false); setStale(false); setAcknowledged(false); remember(next);
  };
  useEffect(() => {
    const item = requested.data;
    if (!requestedId || !item || item.source !== source || item.recordId !== requestedId
      || !hydrated || draftError || !canManage || operationId) return;
    if (chosen.some(entry => recordKey(entry.item) === recordKey(item))) {
      set({ recordId: null });
      return;
    }
    if (chosen.length >= 50) { setError('The requested capability cannot be added because the selection limit is 50.'); return; }
    key.current = crypto.randomUUID();
    if (remember([...chosen, { item, supports: [], selection: {
      source: item.source, recordId: item.recordId, sourceRevision: item.sourceRevision, placements: [], supportingCapabilities: [],
    } }])) set({ recordId: null });
  }, [requested.data, requestedId, source, hydrated, draftError, canManage, operationId, chosen]);
  async function act(action: (signal: AbortSignal) => Promise<void>) {
    if (submitting.current) return;
    submitting.current = true; setBusy(true); setError(null);
    const controller = new AbortController(); pending.current = controller;
    try { await action(controller.signal); }
    catch (reason) {
      if (active.current && !controller.signal.aborted) {
        setError(message(reason));
        const failure = reason as { status?: number; code?: string };
        if (failure.status === 403 || failure.status === 404
          || ['STALE_SOURCE', 'STALE_RELATIONSHIP', 'STALE_BASELINE', 'SETUP_INTENT_CONFLICT'].includes(failure.code ?? '')) setStale(true);
      }
    }
    finally { submitting.current = false; if (active.current) setBusy(false); }
  }
  const prepare = () => {
    if (locked || !chosen.length || !remember(chosen)) return;
    void act(async signal => {
      const prepared = await boundedRequest(() => api.prepareSystemCapabilitySetup(tenantId, systemId,
        { idempotencyKey: key.current, selections: chosen.map(entry => entry.selection) }), signal);
      if (signal.aborted) return;
      if (!sameScope(prepared.operation, tenantId, systemId, 'Setup')) throw new Error('The prepared operation does not match the selected system.');
      setSavedOperation(prepared.operation); setUncertain(false); setAcknowledged(false);
      set({ step: 3, operationId: prepared.operation.operationId, page: null, source: null, search: null });
    });
  };
  async function refresh(signal: AbortSignal) {
    if (!operationId) return;
    const next = await boundedRequest(inner => api.getSystemCapabilityOperation(tenantId, systemId, operationId, inner), signal);
    if (signal.aborted) return;
    if (!sameScope(next, tenantId, systemId, 'Setup')) throw new Error('The saved operation does not match this scope.');
    setSavedOperation(next); setUncertain(false); setAcknowledged(false);
  }
  const complete = () => {
    if (locked || !operation || !acknowledged || completed || uncertain || stale || operation.state === 'InProgress') return;
    void act(async signal => {
      setUncertain(true);
      try {
        const next = await boundedRequest(() => api.completeSystemCapabilityOperation(tenantId, systemId, operation.operationId,
          { expectedRevision: operation.revision }), signal);
        if (signal.aborted) return;
        if (!sameScope(next, tenantId, systemId, 'Setup')) throw new Error('The result does not match this setup scope.');
        setSavedOperation(next); setUncertain(false); setAcknowledged(false);
      } catch (reason) {
        if (signal.aborted) return;
        const failure = reason as { status?: number; code?: string };
        if (failure.status === 403 || failure.status === 404
          || ['STALE_SOURCE', 'STALE_RELATIONSHIP', 'STALE_BASELINE', 'SETUP_INTENT_CONFLICT'].includes(failure.code ?? '')) setStale(true);
        try { await refresh(signal); }
        catch (reloadError) { throw new Error(`${message(reason)} Saved outcome refresh failed: ${message(reloadError)}`); }
        throw reason;
      }
    });
  };
  return <div className="space-y-6">
    <Link to={base} className="text-sm text-indigo-700 underline dark:text-indigo-300">Back to system capabilities</Link>
    <header className="rounded-xl border border-indigo-100 bg-indigo-50/60 p-5 dark:border-indigo-900 dark:bg-indigo-950/40">
      <p className="text-xs font-semibold uppercase tracking-wide text-indigo-700 dark:text-indigo-300">System applicability · {session?.workspace.displayName}</p>
      <h1 className="mt-2 text-2xl font-semibold">Add security capabilities to {systemName}</h1>
      <p className="mt-2 text-sm text-slate-600 dark:text-gray-300">Choose existing library capabilities. The target system is locked to this route; provider source records stay read-only.</p>
    </header>
    <ol aria-label="Setup progress" className="flex flex-wrap gap-4 border-b border-slate-200 pb-4 text-sm dark:border-gray-700">
      {['Select capabilities', 'Review applicability', 'Review and add'].map((label, index) => <li key={label} aria-current={step === index + 1 ? 'step' : undefined}
        className={step === index + 1 ? 'font-semibold text-indigo-700 dark:text-indigo-300' : 'text-slate-500 dark:text-gray-400'}>{index + 1}. {label}</li>)}
    </ol>
    <Status loading={catalog.loading || !hydrated} error={catalog.error} retry={catalog.retry} />
    {requestedId && !operationId && <Status loading={requested.loading} error={requested.error} retry={requested.retry} />}
    {operationId && <Status loading={!operation && recovered.loading} error={recovered.error} retry={recovered.retry} />}
    {error && <p role="alert" className={errorClass}>{error}</p>}
    {draftError && <div role="alert" className={errorClass}><p>{draftError}</p><button type="button" className="mt-2 underline" onClick={() => {
      try { sessionStorage.removeItem(storageKey); setChosen([]); key.current = crypto.randomUUID(); setDraftError(null); set({ step: 1 }); }
      catch (reason) { setDraftError(`Cannot clear the saved draft: ${message(reason)}`); }
    }}>Discard local draft</button></div>}
    {catalog.data && !canManage && <p className={warningClass}>System setup permission is required. Library read access does not authorize changes.</p>}
    {step === 1 && catalog.data && <div className={`grid items-start gap-5 ${setPageContext ? '' : 'lg:grid-cols-[minmax(0,1fr)_260px]'}`}>
      <section className={`${workspaceCard} space-y-4`}>
        <form onSubmit={event => { event.preventDefault(); set({ search: search.trim(), page: 1 }); }} className="flex flex-wrap gap-3">
          <label className="grid flex-1 gap-1 text-sm">Search library capabilities<input className={inputClass} value={search} onChange={event => setSearch(event.target.value)} maxLength={200} /></label>
          <label className="grid gap-1 text-sm">Source<select className={inputClass} value={source ?? ''} onChange={event => set({ source: event.target.value, page: 1 })}>
            <option value="">All sources</option><option value="local">Organization</option><option value="provider">Provider</option></select></label>
          <button type="submit" className={secondaryButtonClass}>Search</button>
        </form>
        <div className="grid gap-3 sm:grid-cols-2">{catalog.data.items.map(item => {
          const selected = chosen.some(entry => recordKey(entry.item) === recordKey(item));
          return <label key={recordKey(item)} className={`rounded-lg border p-4 ${selected ? 'border-indigo-500 bg-indigo-50 dark:bg-indigo-950' : 'border-slate-200 dark:border-gray-700'}`}>
            <input type="checkbox" aria-label={`Select ${item.name}`} checked={selected} disabled={locked || item.isApplied || !item.isAvailable || (!selected && chosen.length >= 50)}
              onChange={event => update(event.target.checked ? [...chosen, { item, supports: [], selection: {
                source: item.source, recordId: item.recordId, sourceRevision: item.sourceRevision, placements: [], supportingCapabilities: [],
              } }] : chosen.filter(entry => recordKey(entry.item) !== recordKey(item)))} />
            <h2 className="mt-2 font-semibold">{item.name}</h2><p className="text-sm">{item.sourceName}</p>
            <div className="my-2"><StateBadge>{item.isApplied ? 'Already applied' : item.source === 'provider' ? 'Provider source · Read-only' : 'Organization library'}</StateBadge></div>
            <p className="text-xs text-slate-500 dark:text-gray-400">{item.components.length} contributors · {item.controlIds.length} mapped controls</p>
            {!item.isAvailable && <p className="mt-2 text-sm">This source is not currently available.</p>}
          </label>;
        })}</div>
        {!catalog.data.items.length && <p>No eligible capabilities match these filters.</p>}
        <Pager page={catalog.data.page} pageSize={catalog.data.pageSize} total={catalog.data.total} onPage={page => set({ page })} />
      </section>
      <aside className={`${workspaceCard} space-y-4`}><h2 className="font-semibold">Selection summary</h2>
        <p>{chosen.length} capabilities selected</p><p className="text-sm">Target: {systemName}</p>
        <ul className="space-y-2 text-sm">{chosen.map(entry => <li key={recordKey(entry.item)}>{entry.item.name} · {entry.item.sourceName}</li>)}</ul>
        <p className="text-xs text-slate-500 dark:text-gray-400">Selecting a library record does not grant or confirm inheritance.</p>
        <button type="button" className={buttonClass} disabled={locked || !chosen.length} onClick={() => set({ step: 2 })}>Continue to applicability</button>
      </aside>
    </div>}
    {step === 2 && catalog.data && <section className="space-y-4"><h2 className="text-xl font-semibold">Review system applicability</h2>
      {chosen.map(entry => <Applicability key={recordKey(entry.item)} tenantId={tenantId} systemId={systemId} chosen={entry}
        boundaries={catalog.data!.boundaries} disabled={locked} onChange={next => update(chosen.map(value => recordKey(value.item) === recordKey(next.item) ? next : value))} />)}
      <p className={warningClass}>Applicability does not confirm inheritance. Responsibility review and narrative acceptance remain separate authorized actions.</p>
      <div className="flex gap-3"><button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => set({ step: 1 })}>Back</button>
        <button type="button" className={buttonClass} disabled={locked} onClick={prepare}>Continue to review</button></div>
    </section>}
    {step === 3 && operation && <section className="space-y-4">
      <h2 className="text-xl font-semibold">{completed ? `Capabilities added to ${systemName}` : 'Review changes before adding'}</h2>
      <p className="text-sm">Saved plan: {operation.state}</p>
      <OperationPlan operation={operation} records={chosen.flatMap(entry => [entry.item, ...entry.supports])}
        boundaries={catalog.data?.boundaries} systemName={systemName} />
      {operation.lastError && <p className={warningClass}>{operation.lastError}</p>}
      <p className={warningClass}>Library records remain shared. Control responsibility review, narrative proposal acceptance and any authorization decision remain pending separate workflows.</p>
      {stale && <p className={warningClass}>This saved plan is stale or access changed. Saved changes are retained. Return to the system to review current sources and placements before preparing new work.</p>}
      {!completed && !uncertain && !stale && <label className="flex gap-2 text-sm"><input type="checkbox" checked={acknowledged} disabled={locked}
        onChange={event => setAcknowledged(event.target.checked)} />I reviewed the exact saved plan for this system.</label>}
      <div className="flex flex-wrap gap-3">
        {!completed && !uncertain && !stale && operation.state !== 'InProgress' && <button type="button" className={buttonClass} disabled={locked || !acknowledged} onClick={complete}>
          {operation.state === 'Partial' ? 'Retry unfinished changes' : 'Add to system'}</button>}
        {(uncertain || operation.state === 'InProgress') && <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => { void act(refresh); }}>Refresh saved outcomes</button>}
        {completed && <Link className={buttonClass} to={`${base}?view=capability`}>View system capabilities</Link>}
        <Link className={secondaryButtonClass} to={`/systems/${encodeURIComponent(systemId)}/inheritance/subscriptions`}>Review responsibilities</Link>
      </div>
      {operation.state === 'InProgress' && <p role="status">The server is processing this operation. Refresh its saved outcomes; do not create another operation.</p>}
      <p className="text-xs text-slate-500 dark:text-gray-400">Refresh or return using this URL to recover the same operation. A retry applies only unfinished server work; it cannot replace this plan.</p>
    </section>}
    <p className="text-xs text-slate-500 dark:text-gray-400">Unsubmitted drafts are scoped to this actor, organization, system and browser tab for 24 hours. Server operations are authoritative. Leaving or cancelling never deletes shared or already-saved data.</p>
    {!busy && <Link to={base} className="inline-block text-sm text-indigo-700 underline dark:text-indigo-300">{completed ? 'Back to system' : 'Cancel and return to system'}</Link>}
  </div>;
}

function Applicability({ tenantId, systemId, chosen, boundaries, disabled, onChange }: {
  tenantId: string; systemId: string; chosen: Chosen; boundaries: Page['boundaries']; disabled: boolean; onChange: (next: Chosen) => void;
}) {
  const [supportOpen, setSupportOpen] = useState(false);
  const components = [...new Map([chosen.item, ...chosen.supports].flatMap(item => item.components).map(component => [recordKey(component), component])).values()];
  const togglePlacement = (source: 'local' | 'provider', componentId: string, boundaryId: string | null, checked: boolean) => {
    const previous = chosen.selection.placements.filter(value => !(value.source === source && value.componentId === componentId && value.boundaryId === boundaryId));
    onChange({ ...chosen, selection: { ...chosen.selection, placements: checked ? [...previous, { source, componentId, boundaryId }] : previous } });
  };
  return <article className={`${workspaceCard} space-y-4`}>
    <header><h3 className="text-lg font-semibold">{chosen.item.name}</h3><p className="text-sm">{chosen.item.sourceName} · {chosen.item.source === 'provider' ? 'Provider source is read-only' : 'Organization library'}</p>
      <p className="mt-1 break-all text-xs text-slate-500 dark:text-gray-400">Exact source revision: {chosen.selection.sourceRevision}</p></header>
    <fieldset disabled={disabled} className="space-y-3"><legend className="mb-2 font-medium">Component placements in this system</legend>
      {components.map(component => <div key={recordKey(component)} className="rounded-lg border border-slate-200 p-3 dark:border-gray-700">
        <div className="mb-3 flex items-center gap-3"><ComponentIcon type={component.componentType} /><div><h4 className="font-medium">{component.name}</h4>
          <p className="text-xs">{component.componentType}{component.subType ? ` · ${component.subType}` : ''} · {component.sourceName}</p></div></div>
        <p className="mb-2 text-xs text-slate-500 dark:text-gray-400">Existing: {component.placements.length ? component.placements.map(value => value.boundaryName ?? value.state).join(', ') : 'Unassigned'}. Existing placements are retained.</p>
        {systemWideOnly(component) && <p className="mb-2 text-sm">Person contributors can only be placed system-wide, not in an authorization boundary.</p>}
        <div className="flex flex-wrap gap-3">{[...(component.source === 'local' ? [{ id: null, name: 'System-wide' }] : []), ...boundaries].map(boundary =>
          <label key={boundary.id ?? 'system-wide'} className="flex gap-2 text-sm"><input type="checkbox" aria-label={`Place ${component.name} in ${boundary.name}`}
            disabled={systemWideOnly(component) && boundary.id !== null}
            checked={chosen.selection.placements.some(value => value.source === component.source && value.componentId === component.recordId && value.boundaryId === boundary.id)}
            onChange={event => togglePlacement(component.source, component.recordId, boundary.id, event.target.checked)} />{boundary.name}</label>)}</div>
        {component.source === 'provider' && !boundaries.length && <p className="text-sm">No authorized boundary is available. This provider contributor remains unassigned.</p>}
      </div>)}
      {!components.length && <p className="text-sm text-slate-500 dark:text-gray-400">No persisted contributors are available for placement.</p>}
    </fieldset>
    {chosen.item.source === 'provider' && <section className="rounded-lg bg-indigo-50/70 p-3 dark:bg-indigo-950/40">
      <h4 className="font-medium">Supporting organization capabilities</h4><p className="my-2 text-xs">Support is a system relationship. Local contributors are never added to provider-authored metadata.</p>
      <ul className="mb-3 space-y-2">{chosen.supports.map(item => <li key={recordKey(item)} className="flex justify-between gap-3 text-sm">{item.name}
        <button type="button" className="underline" disabled={disabled} onClick={() => {
          const supports = chosen.supports.filter(value => value.recordId !== item.recordId);
          const remainingComponents = new Set([chosen.item, ...supports].flatMap(value => value.components.map(recordKey)));
          onChange({ ...chosen, supports, selection: { ...chosen.selection,
            supportingCapabilities: supports.map(value => ({ recordId: value.recordId, sourceRevision: value.sourceRevision })),
            placements: chosen.selection.placements.filter(value => remainingComponents.has(`${value.source}:${value.componentId}`)),
          } });
        }}>Remove support selection</button></li>)}</ul>
      <button type="button" className={secondaryButtonClass} disabled={disabled} onClick={() => setSupportOpen(value => !value)}>Choose supporting organization capabilities</button>
      {supportOpen && <SupportPicker tenantId={tenantId} systemId={systemId} disabled={disabled} selected={chosen.supports} onSelect={item => {
        const supports = [...chosen.supports, item];
        onChange({ ...chosen, supports, selection: { ...chosen.selection, supportingCapabilities: supports.map(value => ({ recordId: value.recordId, sourceRevision: value.sourceRevision })) } });
      }} />}
    </section>}
    <p className="text-sm">Mapped controls: {chosen.item.controlIds.join(', ') || 'None recorded'} · Responsibility review remains separate.</p>
  </article>;
}

function SupportPicker({ tenantId, systemId, selected, disabled, onSelect }: {
  tenantId: string; systemId: string; selected: Item[]; disabled: boolean; onSelect: (item: Item) => void;
}) {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const candidates = useRemote(signal => boundedRequest(inner => api.listSystemCapabilities(tenantId, systemId,
    { scope: 'available', grouping: 'capability', source: 'local', page, pageSize: 25, search }, inner), signal), [tenantId, systemId, page, search]);
  const permitted = candidates.data?.permissions.canRead === true && candidates.data.permissions.canManage === true;
  return <div className="mt-4 space-y-3"><label className="grid gap-1 text-sm">Search organization support
    <input className={inputClass} value={search} maxLength={200} onChange={event => { setSearch(event.target.value); setPage(1); }} /></label>
    <Status loading={candidates.loading} error={candidates.error} retry={candidates.retry} />
    {candidates.data && !permitted && <p className={warningClass}>Current permission does not allow supporting capability changes.</p>}
    {candidates.data?.items.map(item => <button key={recordKey(item)} type="button" className={`${secondaryButtonClass} mr-2`}
      disabled={disabled || !permitted || !item.isAvailable || selected.some(value => value.recordId === item.recordId) || selected.length >= 50}
      onClick={() => onSelect(item)}>Support with {item.name}</button>)}
    {candidates.data && <Pager page={candidates.data.page} pageSize={candidates.data.pageSize} total={candidates.data.total} onPage={setPage} />}
  </div>;
}

const writeLabels: Record<string, string> = {
  'system-link': 'Link organization capability',
  'system-unlink': 'Unlink organization capability',
  subscription: 'Subscribe provider capability',
  unsubscribe: 'Unsubscribe provider capability',
  'support-link': 'Link supporting organization capability',
  'component-placement': 'Place component',
  'control-implementation': 'Create control implementation',
  'responsibility-reconciliation': 'Refresh responsibility review',
  'narrative-change': 'Queue narrative review',
};

export function OperationPlan({ operation, records = [], boundaries = [], systemName }: {
  operation: Operation; records?: readonly Item[]; boundaries?: Page['boundaries']; systemName?: string;
}) {
  const reviewedRecords = records.filter(record => operation.selections.some(selection =>
    selection.source === record.source && selection.recordId === record.recordId && selection.sourceRevision === record.sourceRevision
    || record.source === 'local' && selection.supportingCapabilities.some(support =>
      support.recordId === record.recordId && support.sourceRevision === record.sourceRevision)));
  return <div className={`${workspaceCard} space-y-4`}>
    <h3 className="font-semibold">Exact persisted changes</h3>
    <ul className="divide-y divide-slate-200 text-sm dark:divide-gray-700">{operation.plannedWrites.map(write => {
      const outcome = operation.outcomes.find(value => value.writeKind === write.writeKind && value.writeId === write.writeId);
      const record = reviewedRecords.find(value => value.source === write.source && value.recordId === write.recordId);
      const component = write.writeKind === 'component-placement' ? reviewedRecords.flatMap(value => value.components)
        .find(value => value.source === write.source && value.recordId === write.componentId) : undefined;
      const support = write.writeKind === 'support-link' ? reviewedRecords
        .find(value => value.source === 'local' && value.recordId === write.componentId) : undefined;
      const name = write.writeKind === 'responsibility-reconciliation' ? systemName
        : write.writeKind === 'control-implementation' ? `Control ${write.recordId}` : component?.name ?? record?.name;
      const boundary = boundaries.find(value => value.id === write.boundaryId)?.name;
      return <li key={`${write.writeKind}:${write.writeId}`} className="space-y-1 py-3">
        <div className="flex flex-wrap items-center gap-2"><span className="font-medium">{write.displayLabel || writeLabels[write.writeKind] || 'Unrecognized saved change'}</span>
          <StateBadge tone={outcome?.state === 'Completed' ? 'green' : 'neutral'}>{outcome?.state ?? (write.alreadyExists ? 'Already exists · retained' : 'Planned')}</StateBadge></div>
        {!write.displayLabel && <>{name ? <p className="break-words font-medium">{name}</p>
          : <p className="text-xs text-slate-600 dark:text-gray-300">Reviewed names are unavailable for this saved change. Inspect Details for its exact identifiers.</p>}
        <p className="text-xs">{component?.sourceName ?? record?.sourceName ?? (write.source === 'provider' ? 'Provider source' : 'Organization source')}</p>
        {write.writeKind === 'support-link' && <p className="text-xs">Organization support: {support?.name ?? 'Name unavailable; see Details'}</p>}
        {write.writeKind === 'component-placement' && <p className="text-xs">Boundary: {write.boundaryId === null ? 'System-wide' : boundary ?? 'Name unavailable; see Details'}</p>}</>}
        {!!write.controlIds?.length && <p className="text-xs">Affected controls: {write.controlIds.join(', ')}</p>}
        {!!write.narrativeTypes?.length && <p className="text-xs">Narrative types: {write.narrativeTypes.join(', ')}</p>}
        {outcome?.error && outcome.error !== operation.lastError && <p className="text-red-700 dark:text-red-300">{outcome.error}</p>}
        <details className="text-xs"><summary className="cursor-pointer">Details</summary>
          <dl className="mt-2 grid gap-1 break-all">
            <dt>Write kind</dt><dd>{write.writeKind}</dd>
            <dt>Write ID</dt><dd>{write.writeId}</dd>
            <dt>Source</dt><dd>{write.source}</dd>
            <dt>Record ID</dt><dd>{write.recordId}</dd>
            {write.componentId && <><dt>{write.writeKind === 'support-link' ? 'Supporting capability ID' : 'Component ID'}</dt><dd>{write.componentId}</dd></>}
            {write.boundaryId && <><dt>Boundary ID</dt><dd>{write.boundaryId}</dd></>}
          </dl>
        </details>
      </li>;
    })}</ul>
    <details className="text-sm"><summary className="cursor-pointer">Operation details and exact source revisions</summary>
      <dl className="mt-2 grid gap-1 break-all text-xs">
        <dt>Operation ID</dt><dd>{operation.operationId}</dd>
        <dt>Revision</dt><dd>{operation.revision}</dd>
        <dt>Idempotency key</dt><dd>{operation.idempotencyKey}</dd>
      </dl>
      <pre className="mt-3 whitespace-pre-wrap break-all text-xs">{JSON.stringify(operation.selections, null, 2)}</pre>
    </details>
  </div>;
}
