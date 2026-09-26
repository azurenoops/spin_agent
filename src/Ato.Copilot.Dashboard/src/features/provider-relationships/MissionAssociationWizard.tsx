import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useLocation, useParams } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import { buttonClass, secondaryButtonClass, errorClass, useRemote, message } from '../workspace-operations/workspaceUi';
import MissionSystemPicker from './MissionSystemPicker';
import { capabilityName, CapabilityResponsibilities, ReadStatus, ScopeDetails, TaskFrame } from './MissionTaskPresentation';
import * as api from './api';
import { canPlanAdoption, MissionReviewRequiredError, prepareAdoption } from './adoptionPreparation';
import type { ApplicableProviderCapability, CapabilityAdoptionInput, PagedResult, SystemHostingAllocation } from './types';

const choiceClass = 'flex items-start gap-3 rounded border border-slate-300 p-4';
const identity = (item: ApplicableProviderCapability) => `${item.capabilityId}:${item.releaseId}`;

export default function MissionAssociationWizard({ hostingOnly = false, environmentEntry = false }: { hostingOnly?: boolean; environmentEntry?: boolean }) {
  const session = useWorkspaceSession();
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  if (session?.workspace.kind !== 'organization' || session.workspace.mode !== 'ordinary') {
    return <main className="p-6"><h1 className="text-2xl font-semibold">Mission association task</h1>
      <p role="alert">Choose an authorized organization workspace to associate an existing system. Provider workspace access does not grant customer system access.</p>
      <Link to="/login/select-tenant" className="underline">Choose a workspace</Link>
    </main>;
  }
  if (id && (session.systemAccess?.systemId.toLowerCase() !== id.toLowerCase()
    || session.systemAccess.permissions.canRead !== true)) {
    return <p role="alert" className="p-6">Authorized access to this system is required before association.</p>;
  }
  if (id && location.pathname.replace(/\/$/, '').endsWith('/provider-relationships')) {
    return <TaskFrame step={0} withinSystem>
      <section className="space-y-4" aria-labelledby="mission-task-entry">
        <h2 id="mission-task-entry" className="text-xl font-semibold">Connect existing hosting to this system</h2>
        <p>Choose an existing hosting allocation, inspect published capabilities and duties, and confirm the hosting association.
          An authorized ISSM/ISSO must separately confirm capability subscriptions after a fresh review.</p>
        <p>Opening this task creates no allocations, access grants, subscriptions or responsibility acceptances.</p>
        <Link to={`/systems/${encodeURIComponent(id)}/provider-relationships/setup`} className={`inline-block ${buttonClass}`}>
          Start guided association
        </Link>
      </section>
    </TaskFrame>;
  }
  return <AssociationTask key={`${session.workspace.tenantId}:${id ?? 'picker'}:${hostingOnly}:${environmentEntry}`}
    systemId={id} hostingOnly={hostingOnly} environmentEntry={environmentEntry} />;
}

function AssociationTask({ systemId, hostingOnly, environmentEntry }: { systemId?: string; hostingOnly: boolean; environmentEntry: boolean }) {
  const location = useLocation();
  const [step, setStep] = useState(0);
  const [systemName, setSystemName] = useState('');
  const [allocation, setAllocation] = useState<SystemHostingAllocation | null>(null);
  const [selected, setSelected] = useState<ApplicableProviderCapability[]>([]);
  const [attempted, setAttempted] = useState(false);
  const [plan, setPlan] = useState<ConfirmationPlan | null>(null);
  useEffect(() => {
    if (!plan || (!plan.reviewPending && plan.relationshipDone && plan.capabilities.every(item => item.done))) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [plan]);
  const chooseHosting = useCallback((name: string) => { setSystemName(name); setStep(1); }, []);
  const reviewRemaining = () => {
    if (!plan) return;
    if (!plan.relationshipDone) {
      setPlan(null);
      setStep(1);
      return;
    }
    const completed = plan.capabilities.filter(item => item.done);
    setSelected(plan.capabilities.filter(item => !item.done).map(item => item.reviewed));
    setPlan({ ...plan, capabilities: completed, reviewPending: true,
      subscriptionReviewRequired: false, awaitingSubscriptionConfirmation: false });
    setStep(2);
  };
  const reviewingDuties = environmentEntry && plan && !plan.reviewPending && plan.relationshipDone
    && plan.capabilities.length > 0 && plan.capabilities.every(item => item.done);
  return <TaskFrame step={reviewingDuties ? 5 : step} withinSystem={!!systemId} hostingOnly={hostingOnly} environmentEntry={environmentEntry}>
    {(hostingOnly || environmentEntry) && systemId && <nav aria-label="Hosting navigation" className="flex flex-wrap gap-4">
      <Link className="underline" to={`/systems/${encodeURIComponent(systemId)}/profile/EnvironmentAndDeployment`}>Back to Environment</Link>
      <Link className="underline" to={`/systems/${encodeURIComponent(systemId)}/security-capabilities/add`}>Add security capabilities</Link>
    </nav>}
    {step > 0 && <p className="text-lg font-semibold">{systemName}{allocation ? ` · ${allocation.offeringName}` : ''}</p>}
    {plan?.reviewPending && <p role="status">Hosting relationship retained. Previously recorded capabilities:
      {' '}{plan.capabilities.map(item => item.name).join(', ') || 'none'}. Review only the remaining work.</p>}
    {step === 0 && <MissionSystemPicker systemId={systemId} onContinue={chooseHosting} locked={hostingOnly || environmentEntry}
      autoContinue={environmentEntry || (!systemName && location.state?.chooseHosting === true)} />}
    {step === 1 && systemId && <HostingChoices systemId={systemId} selected={allocation} hostingOnly={hostingOnly} providerFirst={environmentEntry}
      onSelect={value => {
        if (allocation?.assignmentId !== value.assignmentId || allocation.revision !== value.revision) setSelected([]);
        setAllocation(value);
      }} onBack={() => setStep(0)} onContinue={() => setStep(hostingOnly ? 4 : 2)} />}
    {step === 2 && systemId && allocation && <CapabilityChoices systemId={systemId} allocation={allocation}
      selected={selected} completedIds={plan?.capabilities.filter(item => item.done).map(item => item.body.capabilityId) ?? []}
      allowAssociationOnly={!plan && !allocation.relationshipId && allocation.canAssociate}
      onSelect={setSelected} onBack={plan ? undefined : () => setStep(1)} onContinue={() => setStep(3)} />}
    {step === 3 && <section className="space-y-5" aria-labelledby="responsibility-step">
      <h2 id="responsibility-step" className="text-xl font-semibold">Review responsibilities</h2>
      <p>This review does not accept control duties. Provider coverage, shared work, and customer work below are proposals from the published source, not effective inheritance.</p>
      {selected.length === 0 && <p>No capability subscriptions are included. Only the existing allocation's hosting relationship will be recorded; capability selection and subscription remain separate.</p>}
      {selected.map(item => <CapabilityResponsibilities key={identity(item)} item={item} />)}
      <div className="flex gap-3"><button type="button" className={secondaryButtonClass} onClick={() => setStep(2)}>Back</button>
        <button type="button" className={buttonClass} onClick={() => setStep(4)}>Continue to confirmation</button></div>
    </section>}
    {step === 4 && systemId && allocation && <ConfirmAssociations systemId={systemId} systemName={systemName}
      allocation={allocation} selected={selected} hostingOnly={hostingOnly} environmentEntry={environmentEntry}
      onBack={() => setStep(hostingOnly ? 1 : 3)} onAttempt={() => setAttempted(true)}
      plan={plan} setPlan={setPlan} onReviewRemaining={reviewRemaining} />}
    {!attempted && <p className="text-sm text-slate-600 dark:text-slate-300">No changes are saved before final confirmation. This task uses existing hosting allocations only.</p>}
  </TaskFrame>;
}

function Paging({ page, data, onPage, noun }: {
  page: number; data: PagedResult<unknown> | null; onPage: (value: number) => void; noun: string;
}) {
  if (!data || data.total <= data.pageSize) return null;
  return <nav aria-label={`${noun} pages`} className="flex flex-wrap items-center gap-3">
    <button type="button" className={secondaryButtonClass} disabled={page === 1} onClick={() => onPage(page - 1)}>Previous {noun}</button>
    <span>Page {page} of {Math.ceil(data.total / data.pageSize)}</span>
    <button type="button" className={secondaryButtonClass} disabled={page * data.pageSize >= data.total} onClick={() => onPage(page + 1)}>More {noun}</button>
  </nav>;
}

function HostingChoices({ systemId, selected, onSelect, onBack, onContinue, hostingOnly, providerFirst }: {
  systemId: string; selected: SystemHostingAllocation | null; onSelect: (value: SystemHostingAllocation) => void;
  onBack: () => void; onContinue: () => void; hostingOnly: boolean; providerFirst: boolean;
}) {
  const [page, setPage] = useState(1);
  const [provider, setProvider] = useState<string | null>(selected?.providerName ?? null);
  const [choosingProvider, setChoosingProvider] = useState(providerFirst);
  const state = useRemote(async signal => {
    if (!providerFirst) return api.listSystemHostingAllocations(systemId, page, signal);
    const items = await api.listAllSystemHostingAllocations(systemId, signal);
    return { items, page: 1, pageSize: Math.max(1, items.length), total: items.length };
  }, [systemId, page, providerFirst]);
  // Provider names filter the display only; all writes remain assignment-ID/revision bound.
  const providerLabel = (item: SystemHostingAllocation) => item.providerName ?? 'Provider name not recorded';
  const providers = [...new Set(state.data?.items.map(providerLabel) ?? [])].sort();
  const items = state.data?.items.filter(item => !providerFirst || providerLabel(item) === provider);
  const current = items?.find(item => item.assignmentId === selected?.assignmentId);
  const selectable = (item: SystemHostingAllocation) => !!item.relationshipId || item.canAssociate;
  if (choosingProvider) return <section aria-labelledby="provider-step" className="space-y-4">
    <h2 id="provider-step" className="text-xl font-semibold">Choose provider</h2>
    <p>Providers with hosting already allocated to this system are shown. Choosing a provider does not grant access or create a hosting allocation.</p>
    <ReadStatus state={state} name="providers and hosting scopes" />
    <fieldset className="space-y-3"><legend className="sr-only">Providers with allocated hosting</legend>
      {providers.map(name => <label key={name} className={choiceClass}>
        <input type="radio" name="provider" checked={provider === name} onChange={() => setProvider(name)} />
        <span>{name}</span>
      </label>)}
    </fieldset>
    {state.data?.total === 0 && <p>No existing hosting allocations are available for this system. Contact your hosting administrator; organization capabilities remain available without a provider.</p>}
    <button type="button" className={buttonClass} disabled={state.loading || !!state.error || !provider || !providers.includes(provider)}
      onClick={() => setChoosingProvider(false)}>Choose hosting scope</button>
  </section>;
  return <section className="space-y-4" aria-labelledby="hosting-step">
    <h2 id="hosting-step" className="text-xl font-semibold">Choose CSP hosting scope</h2>
    <p>Only existing allocations for this system are shown. This task cannot request allocations or grant access to cloud resources.</p>
    <ReadStatus state={state} name="hosting scopes" />
    <fieldset className="space-y-3"><legend className="sr-only">Existing hosting allocations</legend>
      {items?.map(item => <div key={item.assignmentId} className="space-y-2">
        <label className={choiceClass}>
          <input type="radio" name="hosting" disabled={!selectable(item)} checked={selected?.assignmentId === item.assignmentId}
            onChange={() => onSelect(item)} />
          <span><span className="block font-semibold">{item.offeringName}</span>
            {item.hostingScopeName && <span className="block">{item.hostingScopeName}</span>}
            {item.providerName && <span className="block text-sm">{item.providerName}</span>}
            <span className="block text-sm">{item.systemName} · {item.assignedScopes.length} assigned scope{item.assignedScopes.length === 1 ? '' : 's'}</span>
            {item.relationshipId && <span className="block text-sm">Hosting already associated. No new hosting association will be created.</span>}
            {!selectable(item) && <span className="block text-sm">Association is unavailable. An eligible role and current hosting context are required.</span>}
          </span>
        </label>
        <ScopeDetails scopes={item.assignedScopes} />
        <details className="text-sm"><summary>Allocation details</summary><p className="break-all">Allocation: {item.assignmentId} · Revision {item.revision}</p></details>
      </div>)}
    </fieldset>
    {state.data?.items.length === 0 && <p>No existing hosting allocations are available for this system. Contact your hosting administrator; no allocation request is created here.</p>}
    <Paging page={page} data={state.data} onPage={setPage} noun="hosting scopes" />
    <div className="flex gap-3"><button type="button" className={secondaryButtonClass}
      onClick={providerFirst ? () => setChoosingProvider(true) : onBack}>Back</button>
      <button type="button" className={buttonClass}
        disabled={state.loading || !!state.error || !current || !selectable(current)}
        onClick={() => { if (current) { onSelect(current); onContinue(); } }}>{hostingOnly ? 'Review hosting association' : 'Choose capabilities'}</button>
    </div>
  </section>;
}

function CapabilityChoices({ systemId, allocation, selected, completedIds, allowAssociationOnly, onSelect, onBack, onContinue }: {
  systemId: string; allocation: SystemHostingAllocation; selected: ApplicableProviderCapability[];
  completedIds: string[];
  allowAssociationOnly: boolean;
  onSelect: (value: ApplicableProviderCapability[]) => void; onBack?: () => void; onContinue: () => void;
}) {
  const [page, setPage] = useState(1);
  const state = useRemote(signal => api.listApplicableProviderCapabilities(systemId, {
    page, assignmentId: allocation.assignmentId,
  }, signal), [systemId, allocation.assignmentId, page]);
  const eligible = (item: ApplicableProviderCapability) => canPlanAdoption(item) && !!item.capabilityName?.trim()
    && !completedIds.includes(item.capabilityId)
    && item.assignmentId === allocation.assignmentId && item.assignmentRevision === allocation.revision;
  const invalidSelected = state.data?.items.some(item => {
    const previous = selected.find(value => identity(value) === identity(item));
    return previous && (!eligible(item) || previous.applicability.snapshotHash !== item.applicability.snapshotHash
      || previous.applicabilityPreviewHash !== item.applicabilityPreviewHash
      || previous.releaseSnapshotHash !== item.releaseSnapshotHash);
  });
  return <section className="space-y-4" aria-labelledby="capability-step">
    <h2 id="capability-step" className="text-xl font-semibold">Select security capabilities</h2>
    <p>Choose applicable published releases for this exact hosting allocation. You can review each source and its proposed duties before confirming.</p>
    <ReadStatus state={state} name="capabilities" />
    <fieldset className="space-y-3"><legend className="sr-only">Published capability choices</legend>
      {state.data?.items.map(item => <div key={identity(item)} className="space-y-2"><label className={choiceClass}>
        <input type="checkbox" disabled={!eligible(item) && !selected.some(value => identity(value) === identity(item))}
          checked={selected.some(value => identity(value) === identity(item))}
          onChange={event => onSelect(event.target.checked
            ? [...selected.filter(value => value.capabilityId !== item.capabilityId), item]
            : selected.filter(value => identity(value) !== identity(item)))} />
        <span><span className="block font-semibold">{capabilityName(item)}</span>
          <span className="block text-sm">{item.offeringName || allocation.offeringName} · Published version {item.releaseRevision} · {item.applicabilityState}</span>
          {completedIds.includes(item.capabilityId) && <span className="block text-sm">Already recorded in this task; not repeated.</span>}
          {eligible(item) && !item.canProposeAdoption && <span className="block text-sm">Planned selection. Adoption is checked again after the confirmed hosting association.</span>}
          {!eligible(item) && <span className="block text-sm">Not available for adoption. {item.reasonCodes.join(', ')}</span>}
        </span>
      </label><details><summary className="cursor-pointer text-sm underline">View {capabilityName(item)} responsibilities</summary>
        <CapabilityResponsibilities item={item} />
      </details></div>)}
    </fieldset>
    {state.data?.items.length === 0 && <p>No published capabilities are currently available for this allocation. Previously saved operations, if any, are unchanged.</p>}
    {!!state.data?.items.length && !state.data.items.some(eligible) && <div className="rounded border border-amber-300 p-4">
      <p>No capability on this page is currently selectable. Mission Owner alone does not grant capability subscription permission.
        Adoption requires the canonical ISSM/ISSO authorization and an applicable published release.</p>
      <p>You can inspect source responsibilities above. Ask an authorized system security officer to complete adoption; this task does not create an approval request.</p>
      <Link to={`/systems/${encodeURIComponent(systemId)}/capability-coverage`} className="underline">View system capability subscriptions</Link>
    </div>}
    {invalidSelected && <p role="alert" className={errorClass}>A selected capability changed. Deselect it and review the current release before continuing.</p>}
    <p>{selected.length} selected{selected.length ? `: ${selected.map(capabilityName).join(', ')}` : ''}.</p>
    <Paging page={page} data={state.data} onPage={setPage} noun="capabilities" />
    <div className="flex flex-wrap gap-3">{onBack && <button type="button" className={secondaryButtonClass} onClick={onBack}>Back</button>}
      <button type="button" className={buttonClass}
        disabled={state.loading || !!state.error || selected.length === 0 || !!invalidSelected}
        onClick={onContinue}>Review responsibilities</button>
      {allowAssociationOnly && selected.length === 0 && <button type="button" className={secondaryButtonClass}
        disabled={state.loading || !!state.error || !state.data} onClick={onContinue}>
        Continue with hosting association only
      </button>}
    </div>
  </section>;
}

interface ConfirmationPlan {
  relationshipKey: string;
  relationshipDone: boolean;
  reviewPending: boolean;
  subscriptionReviewRequired: boolean;
  awaitingSubscriptionConfirmation: boolean;
  capabilities: {
    key: string; name: string; reviewed: ApplicableProviderCapability;
    body: CapabilityAdoptionInput; prepared: boolean; done: boolean;
  }[];
}

function ReadOnlyCapabilities({ systemId, assignmentId }: { systemId: string; assignmentId: string }) {
  const [page, setPage] = useState(1);
  const state = useRemote(signal => api.listApplicableProviderCapabilities(systemId, {
    page, assignmentId,
  }, signal), [systemId, assignmentId, page]);
  return <section aria-labelledby="read-only-capabilities" className="space-y-4">
    <h3 id="read-only-capabilities" className="text-lg font-semibold">Published capabilities and duties — read-only</h3>
    <p>The hosting association remains saved. This view does not subscribe to capabilities or accept duties.</p>
    <ReadStatus state={state} name="published capability review" />
    {state.data?.items.map(item => <CapabilityResponsibilities key={identity(item)} item={item} />)}
    {state.data?.items.length === 0 && <p>No published capabilities are currently returned for this allocation.</p>}
    <Paging page={page} data={state.data} onPage={setPage} noun="published capabilities" />
  </section>;
}

function ConfirmAssociations({ systemId, systemName, allocation, selected, onBack, onAttempt, plan, setPlan, onReviewRemaining, hostingOnly, environmentEntry }: {
  systemId: string; systemName: string; allocation: SystemHostingAllocation; selected: ApplicableProviderCapability[];
  onBack: () => void; onAttempt: () => void;
  plan: ConfirmationPlan | null; setPlan: (plan: ConfirmationPlan) => void; onReviewRemaining: () => void; hostingOnly: boolean; environmentEntry: boolean;
}) {
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [needsReview, setNeedsReview] = useState(false);
  const lock = useRef(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const snapshot = (value: ConfirmationPlan) => {
    if (mounted.current) setPlan({ ...value, capabilities: value.capabilities.map(item => ({ ...item })) });
  };
  const submit = async () => {
    if (lock.current || !confirmed) return;
    lock.current = true; setBusy(true); setError(null); setNeedsReview(false); onAttempt();
    const current = plan && !plan.reviewPending ? plan : {
      relationshipKey: plan?.relationshipKey ?? crypto.randomUUID(), relationshipDone: plan?.relationshipDone ?? !!allocation.relationshipId,
      reviewPending: false,
      subscriptionReviewRequired: !plan?.relationshipDone && !allocation.relationshipId && selected.length > 0,
      awaitingSubscriptionConfirmation: false,
      capabilities: [...(plan?.capabilities ?? []), ...selected.map(item => ({
        key: crypto.randomUUID(), name: capabilityName(item), reviewed: item, prepared: false, done: false,
        body: {
          assignmentId: allocation.assignmentId, expectedAssignmentRevision: allocation.revision,
          capabilityId: item.capabilityId, releaseId: item.releaseId,
          contextSnapshotHash: item.applicability.snapshotHash, applicabilityPreviewHash: item.applicabilityPreviewHash,
        },
      }))],
    };
    snapshot(current);
    try {
      if (!current.relationshipDone) {
        await api.associateProviderRelationship(systemId, {
          assignmentId: allocation.assignmentId, expectedAssignmentRevision: allocation.revision,
        }, current.relationshipKey);
        current.relationshipDone = true;
        snapshot(current);
      }
      if (current.awaitingSubscriptionConfirmation) {
        current.subscriptionReviewRequired = false;
        current.awaitingSubscriptionConfirmation = false;
        snapshot(current);
      }
      if (current.subscriptionReviewRequired) {
        for (const item of current.capabilities) {
          if (!mounted.current) return;
          if (!item.prepared) {
            const prepared = await prepareAdoption(systemId, item.reviewed);
            item.body = prepared.body;
            item.reviewed = prepared.capability;
            item.prepared = true;
            snapshot(current);
          }
        }
        if (!mounted.current) return;
        current.awaitingSubscriptionConfirmation = true;
        setConfirmed(false);
        snapshot(current);
        return;
      }
      for (const item of current.capabilities) {
        if (!mounted.current) return;
        if (item.done) continue;
        if (!item.prepared) {
          const prepared = await prepareAdoption(systemId, item.reviewed);
          item.body = prepared.body;
          item.reviewed = prepared.capability;
          item.prepared = true;
          snapshot(current);
        }
        if (!mounted.current) return;
        await api.proposeProviderCapabilityAdoption(systemId, item.body, item.key);
        item.done = true;
        snapshot(current);
      }
    } catch (reason) {
      if (mounted.current) {
        setError(message(reason));
        const rejectedAsStale = reason instanceof api.ProviderRelationshipError
          && (reason.status === undefined || reason.status === 409)
          && ['AUTHORIZATION_CONTEXT_STALE', 'RESPONSIBILITY_CONTEXT_STALE'].includes(reason.code ?? '');
        setNeedsReview(reason instanceof MissionReviewRequiredError || rejectedAsStale);
      }
    } finally {
      lock.current = false;
      if (mounted.current) setBusy(false);
    }
  };
  const complete = plan && !plan.reviewPending && plan.relationshipDone && plan.capabilities.every(item => item.done);
  const base = `/systems/${encodeURIComponent(systemId)}`;
  return <section className="space-y-4" aria-labelledby="confirmation-step">
    <h2 id="confirmation-step" className="text-xl font-semibold">{plan?.awaitingSubscriptionConfirmation
      ? 'Review refreshed capability subscriptions' : complete
      ? plan.capabilities.length > 0 ? 'Associations recorded' : 'Hosting association recorded'
      : hostingOnly ? 'Review hosting association' : 'Confirm associations'}</h2>
    <p><strong>{systemName}</strong> → {allocation.offeringName}</p>
    <ScopeDetails scopes={allocation.assignedScopes} />
    <ul className="list-inside list-disc">{selected.map(item => <li key={identity(item)}>{capabilityName(item)} · Published version {item.releaseRevision}</li>)}</ul>
    {selected.length === 0 && <p>No capability subscriptions are included. This confirmation is for the hosting relationship only.</p>}
    {!hostingOnly && <p>Confirmation records the hosting relationship and each selected capability subscription with its release and applicability context. These are separate operations, not one atomic transaction.</p>}
    {!hostingOnly && !plan?.relationshipDone && !allocation.relationshipId && <p>This confirmation saves only the hosting association.
      Selected capability subscriptions require a separate confirmation after their exact published duties are refreshed.</p>}
    {!hostingOnly && <p>After the hosting association is recorded, each exact release is read again. Adoption proceeds only if its reviewed source, scope and duties are unchanged and the server permits it.</p>}
    <p>No authorization coverage is asserted. No control duties are accepted. AO review and ISSM/ISSO responsibility confirmation remain separate.</p>
    {plan?.awaitingSubscriptionConfirmation && <section aria-label="Refreshed subscription review" className="space-y-4">
      <p>The hosting association is saved. Review these refreshed published sources and duties, then explicitly confirm the selected subscriptions. None has been subscribed by this task yet.</p>
      {plan.capabilities.map(item => <CapabilityResponsibilities key={item.key} item={item.reviewed} />)}
      <button type="button" className={secondaryButtonClass} disabled={busy} onClick={onReviewRemaining}>Change capability selection</button>
    </section>}
    {plan && <div role="status" className="space-y-2">
      <p>{plan.relationshipDone
        ? allocation.relationshipId ? 'Existing hosting relationship retained.' : 'Hosting relationship recorded.'
        : 'Hosting relationship not yet confirmed by the server.'}</p>
      <ul>{plan.capabilities.map(item => <li key={item.key}>{item.name}: {item.done ? 'subscription and adoption recorded' : 'not yet confirmed by the server'}</li>)}</ul>
      {!hostingOnly && <p>{plan.capabilities.filter(item => item.done).length} of {plan.capabilities.length} capability operations confirmed.</p>}
    </div>}
    {error && <div role="alert" className={errorClass}><p>{error}</p>
      <p>{needsReview
        ? 'Confirmed operations are retained. Review refreshed choices and explicitly confirm a new intent before continuing.'
        : 'Completed operations are retained. A failed response may have an unknown outcome; retry uses the original request and operation key rather than creating duplicates. Keep this page open.'}</p>
      {needsReview && <button type="button" className={`${secondaryButtonClass} mt-3`} onClick={onReviewRemaining}>Review current choices</button>}
    </div>}
    {!complete && <>
      <label className="flex items-start gap-3"><input type="checkbox" checked={confirmed}
        disabled={busy || (!!plan && !plan.reviewPending && !plan.awaitingSubscriptionConfirmation)}
        onChange={event => setConfirmed(event.target.checked)} />
        <span>{hostingOnly ? 'I confirm this hosting association' : 'I confirm these associations'} for {systemName}; I understand this does not accept responsibilities or assert authorization coverage.</span>
      </label>
      <div className="flex flex-wrap gap-3">
        {(!plan || plan.reviewPending) && <button type="button" className={secondaryButtonClass} onClick={onBack}>Back</button>}
        <button type="button" className={buttonClass} disabled={!confirmed || busy} onClick={() => void submit()}>
          {busy ? 'Recording associations…' : plan?.awaitingSubscriptionConfirmation ? 'Confirm subscriptions'
            : plan && !plan.reviewPending ? 'Retry incomplete operations'
            : !plan?.relationshipDone && !allocation.relationshipId ? 'Associate this allocation'
            : hostingOnly ? 'Keep this hosting association' : 'Confirm subscriptions'}
        </button>
      </div>
    </>}
    {complete && environmentEntry && plan.capabilities.length > 0 && <section aria-label="Confirm responsibilities" className="space-y-3">
      <h3 className="text-lg font-semibold">Next: confirm responsibilities</h3>
      <p>Associations are saved, but responsibilities are not confirmed. Open each capability's Coverage &amp; duties tab.
        An authorized reviewer must review the current source and confirm each control's allocation, checks and notes.</p>
      <ul className="space-y-3">{plan.capabilities.map(item => <li key={item.key}>
        <Link className="underline" to={`${base}/security-capabilities/provider/${encodeURIComponent(item.body.capabilityId)}?tab=coverage`}>
          Confirm responsibilities: {item.name}
        </Link>
      </li>)}</ul>
    </section>}
    {complete && !hostingOnly && !environmentEntry && <nav aria-label="Next actions" className="flex flex-wrap gap-4">
      <Link to={`${base}/inheritance/subscriptions`} className="underline">Review subscription responsibilities</Link>
      <Link to={`${base}/authorize`} className="underline">Open authorization review</Link>
      <Link to={`${base}/capability-coverage`} className="underline">View system capabilities</Link>
    </nav>}
    {complete && plan.capabilities.length === 0 && <p>No capability subscription or pending request was created.
      An authorized ISSM/ISSO can select this existing relationship and subscribe to applicable published capabilities separately.</p>}
    {complete && !hostingOnly && plan.capabilities.length === 0 && <ReadOnlyCapabilities systemId={systemId} assignmentId={allocation.assignmentId} />}
  </section>;
}
