import { useEffect, useRef, useState } from 'react';
import * as api from '../../api/systemEnvironments';
import { Link } from '../workspaces/workspaceNavigation';
import SetupDialog from '../workspace-operations/SetupDialog';
import EnvironmentResourceSelection, { type ResourceSelection } from './EnvironmentResourceSelection';
import { systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';

interface Selection extends ResourceSelection {
  choice: api.EnvironmentChoice;
  discovery: api.EnvironmentDiscoveryResponse | null;
  relatedScopeId: string;
}
const sourceLabel = (choice: api.EnvironmentChoice) => choice.source === 'ProviderAllocation' ? 'Provider allocation' : 'Organization registration';
const errorMessage = (error: unknown) => error instanceof Error ? error.message : 'Subscription attachment could not be confirmed. Retry or refresh the current records.';

export default function SubscriptionAttachmentWizard({ systemId, systemName, organizationName, version, onClose, onSaved }: {
  systemId: string; systemName?: string; organizationName?: string; version: number;
  onClose: () => void; onSaved: (response: api.SystemEnvironmentsResponse) => void;
}) {
  const [choices, setChoices] = useState<api.EnvironmentChoicesResponse | null>(null);
  const [workspace, setWorkspace] = useState<api.SystemEnvironmentsResponse | null>(null);
  const [selections, setSelections] = useState<Selection[]>([]);
  const [step, setStep] = useState(1);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);
  const [acknowledged, setAcknowledged] = useState(false);
  const [attempted, setAttempted] = useState(false);
  const writing = useRef(false);
  const mounted = useRef(true);
  const intent = useRef<{ body: api.ApplySystemEnvironmentsRequest; key: string } | null>(null);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useEffect(() => {
    const controller = new AbortController();
    setChoices(null); setWorkspace(null); setError(null);
    void Promise.all([api.getEnvironmentChoices(systemId, controller.signal), api.getSystemEnvironments(systemId, controller.signal)])
      .then(([available, current]) => { if (!controller.signal.aborted) { setChoices(available); setWorkspace(current); } })
      .catch(reason => { if (!controller.signal.aborted) setError(errorMessage(reason)); });
    return () => controller.abort();
  }, [systemId, retry]);
  const update = (id: string, patch: Partial<Selection>) => setSelections(values => values.map(value =>
    value.choice.choiceId === id ? { ...value, ...patch } : value));
  const discover = async () => {
    if (!choices || !selections.length || writing.current) return;
    writing.current = true; setPending(true); setError(null);
    try {
      const discovered: Selection[] = [];
      for (const value of selections) {
        const result = await api.discoverEnvironmentResources(systemId, { expectedVersion: choices.version, selection: {
          source: value.choice.source, registrationId: value.choice.registration.registrationId,
          allocationId: value.choice.allocationId, expectedAllocationVersion: value.choice.allocationVersion,
        } });
        if (!mounted.current) return;
        discovered.push({ ...value, discovery: result });
      }
      setSelections(discovered); setStep(2);
    } catch (reason) { if (mounted.current) setError(errorMessage(reason)); }
    finally { writing.current = false; if (mounted.current) setPending(false); }
  };
  const attach = async () => {
    if (!acknowledged || writing.current || !selections.length) return;
    if (!intent.current) {
      const items: api.ApplySystemEnvironmentRequest[] = [];
      for (const selection of selections) {
        if (!selection.discovery || !selection.resourceIds.length) return;
        items.push({ expectedVersion: choices?.version ?? version, selection: selection.discovery.selection,
          discoveryToken: selection.discovery.discoveryToken, resourceIds: selection.resourceIds,
          exclusions: selection.exclusions, sharedDependencyResourceIds: selection.sharedDependencyResourceIds,
          reuseHostingAssignmentId: selection.relatedScopeId || null });
      }
      intent.current = { body: { expectedVersion: choices?.version ?? version, items }, key: crypto.randomUUID() };
    }
    writing.current = true; setPending(true); setError(null); setAttempted(true);
    try {
      const result = await api.applySystemEnvironments(systemId, intent.current.body, intent.current.key);
      if (!mounted.current) return;
      for (const selection of selections) {
        const saved = result.attachments.find(item => item.registration.registrationId === selection.choice.registration.registrationId &&
          item.allocationId === selection.choice.allocationId && item.attachmentState === 'Attached');
        if (!saved || saved.scope.resourceIds.length !== selection.resourceIds.length ||
          !selection.resourceIds.every(id => saved.scope.resourceIds.some(savedId => savedId.toLowerCase() === id.toLowerCase()))) {
          throw new Error('The server did not confirm every selected subscription and resource. Retry the same request or close and refresh the saved records.');
        }
        const links = result.hostingLinks?.filter(link => link.attachmentId === saved.attachmentId && link.state === 'Linked') ?? [];
        if (selection.relatedScopeId && !links.some(link => link.assignmentId === selection.relatedScopeId)) {
          throw new Error('The server did not confirm the requested provider relationship. Retry the same request or close and inspect the saved records.');
        }
        if (!selection.relatedScopeId && links.length) {
          throw new Error('The server returned an unrequested provider relationship. Close and inspect the saved records before continuing.');
        }
      }
      onSaved(result);
    } catch (reason) { if (mounted.current) setError(errorMessage(reason)); }
    finally { writing.current = false; if (mounted.current) setPending(false); }
  };
  const scopes = workspace?.providerScopes?.filter(scope => scope.state === 'Active' && scope.relationshipId) ?? [];
  return <SetupDialog title="Attach subscriptions" expanded busy={pending} onClose={onClose}
    description="Attach eligible subscriptions independently of provider services. This does not grant Azure access, enable monitoring, run assessments or save deployment documentation.">
    <ol aria-label="Subscription attachment steps" className="mb-5 flex flex-wrap gap-4 text-sm">
      {['Select subscriptions', 'Select system resource scope', 'Review and attach'].map((label, index) =>
        <li key={label} aria-current={step === index + 1 ? 'step' : undefined} className={step === index + 1 ? 'font-semibold text-indigo-700' : 'text-slate-500'}>{index + 1}. {label}</li>)}
    </ol>
    {error && <p role="alert" className="mb-4 rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{error}</p>}
    {step === 1 && <div className="space-y-3">
      {!choices && !error && <p role="status">Loading eligible subscriptions…</p>}
      {!choices && error && <button type="button" className={systemSecondaryAction} onClick={() => setRetry(value => value + 1)}>Retry subscriptions</button>}
      {choices && !choices.choices.length && <p>No eligible subscriptions are available. Register a subscription or reconcile its source with an authorized administrator.</p>}
      {choices?.choices.map(choice => {
        const selected = selections.some(value => value.choice.choiceId === choice.choiceId);
        const duplicate = selections.some(value => value.choice.choiceId !== choice.choiceId && value.choice.registration.registrationId === choice.registration.registrationId);
        return <label key={choice.choiceId} className="block rounded border border-slate-200 p-3 text-sm">
          <input type="checkbox" disabled={pending || !choice.eligible || duplicate || !choices.permissions.canManageEnvironments}
            checked={selected} onChange={event => setSelections(values => event.target.checked
              ? [...values, { choice, discovery: null, resourceIds: [], exclusions: [], sharedDependencyResourceIds: [], relatedScopeId: '' }]
              : values.filter(value => value.choice.choiceId !== choice.choiceId))} />
          <strong className="ml-2">{choice.registration.displayName}</strong>
          <span className="mt-1 block text-xs text-slate-500">{sourceLabel(choice)} · {choice.registration.cloud} · {choice.allocationState ?? choice.registration.status}</span>
          {choice.providerName && <span className="block text-xs">Allocation source: {choice.providerName} · {choice.offeringName}. Linking a provider scope is optional.</span>}
          {!choice.eligible && <span className="block text-xs text-amber-800">{choice.ineligibleReason ?? 'This subscription is not currently eligible.'}</span>}
          {duplicate && <span className="block text-xs text-amber-800">This registration is already selected through another source.</span>}
          <details className="mt-2 text-xs"><summary>Identity and provenance</summary><p className="break-all">
            Subscription: {choice.registration.subscriptionId}<br />Azure directory: {choice.registration.directoryTenantId}<br />
            Source: {choice.provenance.source} · {choice.provenance.externalId ?? 'No external reference'}</p></details>
        </label>;
      })}
      {choices?.registrationHref ? <Link className="block text-sm underline" to={choices.registrationHref}>Register or reconcile subscriptions</Link>
        : <p className="text-xs text-slate-500">An authorized organization administrator can register missing subscriptions. Entering an ID does not establish ownership or access.</p>}
    </div>}
    {step === 2 && <div className="space-y-5">
      {selections.map(selection => <section key={selection.choice.choiceId} aria-label={`Resource scope for ${selection.choice.registration.displayName}`} className="space-y-3 rounded border p-3">
        <h3 className="font-semibold">{selection.choice.registration.displayName}</h3>
        {selection.discovery && <EnvironmentResourceSelection discovery={selection.discovery} value={selection} disabled={pending}
          onChange={value => update(selection.choice.choiceId, value)} />}
        <details><summary className="cursor-pointer text-sm">Optional provider relationship</summary>
          <label className="mt-3 block text-sm">Related provider scope — optional
            <select className="mt-1 w-full rounded border p-2 dark:bg-slate-900" value={selection.relatedScopeId} disabled={pending}
              onChange={event => update(selection.choice.choiceId, { relatedScopeId: event.target.value })}>
              <option value="">No provider scope</option>
              {scopes.map(scope => <option key={scope.assignmentId} value={scope.assignmentId}>{scope.providerName ?? 'Provider'} · {scope.offeringName} · {scope.hostingScopeName}</option>)}
            </select>
          </label>
          <p className="mt-2 text-xs text-slate-500">A link records use of an existing provider scope. It does not confirm inheritance or change subscription ownership.</p>
        </details>
      </section>)}
    </div>}
    {step === 3 && <div className="space-y-4 text-sm">
      <p><strong>{systemName ?? 'Selected system'}</strong> · {organizationName ?? 'Active organization'}</p>
      {selections.map(selection => <section key={selection.choice.choiceId} className="rounded border p-3">
        <h3 className="font-semibold">{selection.choice.registration.displayName}</h3>
        <p>{sourceLabel(selection.choice)} · {selection.resourceIds.length} explicitly selected resources</p>
        <p>Related provider scope: {scopes.find(scope => scope.assignmentId === selection.relatedScopeId)?.hostingScopeName ?? 'No provider scope'}</p>
        <p>Assessment access: Not checked · Monitoring: Not enabled</p>
        <details className="mt-2"><summary>Selected resources and source</summary>
          <p className="break-all">{selection.choice.registration.subscriptionId} · {selection.choice.provenance.source}</p>
          <ul className="mt-2 text-xs">{selection.resourceIds.map(id => <li key={id} className="break-all">{id}</li>)}</ul>
        </details>
      </section>)}
      <p>Existing approved boundaries and deployment drafts remain unchanged. Scope review and access checks are still required.</p>
      <label className="flex gap-2"><input type="checkbox" disabled={pending || attempted} checked={acknowledged} onChange={event => setAcknowledged(event.target.checked)} />
        I reviewed this exact system scope and the remaining prerequisites.</label>
      {attempted && error && <p>No successful result has been confirmed. Retry sends the same atomic batch and replay key; it does not create another set of attachments. For a stale-version or expired-discovery error, close this dialog, refresh subscriptions and review a new selection.</p>}
    </div>}
    <div className="mt-6 flex flex-wrap gap-3">
      <button type="button" disabled={pending} className={systemSecondaryAction} onClick={onClose}>Cancel</button>
      {step > 1 && !attempted && <button type="button" disabled={pending} className={systemSecondaryAction} onClick={() => { setStep(value => value - 1); setError(null); }}>Back</button>}
      {step === 1 && <button type="button" disabled={pending || !selections.length || !choices?.permissions.canManageEnvironments} className={systemPrimaryAction} onClick={() => void discover()}>
        {pending ? 'Discovering resources…' : 'Select system resource scope'}</button>}
      {step === 2 && <button type="button" disabled={pending || selections.some(value => !value.resourceIds.length)} className={systemPrimaryAction}
        onClick={() => { setStep(3); setAcknowledged(false); }}>Review and attach</button>}
      {step === 3 && <button type="button" disabled={pending || !acknowledged} className={systemPrimaryAction} onClick={() => void attach()}>
        {pending ? 'Attaching…' : attempted ? 'Retry remaining attachments' : 'Attach selected subscriptions'}</button>}
    </div>
  </SetupDialog>;
}
