import { useCallback, useEffect, useRef, useState } from 'react';
import { Network } from 'lucide-react';
import { Link } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import SetupDialog from '../workspace-operations/SetupDialog';
import * as api from '../../api/systemEnvironments';
import { systemPanel, systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';
import SubscriptionAttachmentWizard from './SubscriptionAttachmentWizard';
import EnvironmentResourceSelection from './EnvironmentResourceSelection';
import ProviderServicesScopes from './ProviderServicesScopes';

function message(reason: unknown) {
  if (reason instanceof Error) return reason.message;
  if (reason && typeof reason === 'object' && 'error' in reason && typeof reason.error === 'string') return reason.error;
  return 'The environment operation could not be confirmed. Refresh the current source and retry.';
}
const stateLabel = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2');
function sourceSelection(choice: api.EnvironmentChoice): api.EnvironmentSourceSelection {
  return { source: choice.source, registrationId: choice.registration.registrationId,
    allocationId: choice.allocationId, expectedAllocationVersion: choice.allocationVersion };
}
function choiceFor(item: api.SystemEnvironmentAttachment): api.EnvironmentChoice {
  return { choiceId: item.attachmentId, source: item.source, registration: item.registration,
    allocationId: item.allocationId, allocationVersion: item.allocationVersion,
    offeringId: item.offeringId, offeringName: item.offeringName, hostingScopeRevisionId: item.hostingScopeRevisionId ?? null,
    allocationState: item.allocationState ?? null, startsAt: item.allocationStartsAt ?? null, expiresAt: item.allocationExpiresAt ?? null, provenance: item.provenance,
    providerName: item.providerName, consumerName: item.consumerName, hostingScopeName: item.hostingScopeName,
    eligible: item.attachmentState === 'Attached', ineligibleReason: item.readiness.reason };
}
export default function ConnectedSystemEnvironments({ systemId, systemName, busy = false, onStatusChange }: {
  systemId: string; systemName?: string; busy?: boolean; onStatusChange?: (status: string) => void;
}) {
  const session = useWorkspaceSession();
  const identity = JSON.stringify([systemId, session?.identity.oid, session?.roles]);
  return <ConnectedContent key={identity} systemId={systemId} systemName={systemName} organizationName={session?.workspace.displayName}
    busy={busy} onStatusChange={onStatusChange} />;
}
function ConnectedContent({ systemId, systemName, organizationName, busy, onStatusChange }: {
  systemId: string; systemName?: string; organizationName?: string; busy: boolean; onStatusChange?: (status: string) => void;
}) {
  const [data, setData] = useState<api.SystemEnvironmentsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const refreshSubscriptions = useCallback(() => setRevision(value => value + 1), []);
  const [checking, setChecking] = useState(false);
  const [attaching, setAttaching] = useState(false);
  const [wizard, setWizard] = useState<{ attachment: api.SystemEnvironmentAttachment; reviewPendingScope?: boolean } | null>(null);
  const [detail, setDetail] = useState<api.SystemEnvironmentAttachment | null>(null);
  const [detachRationale, setDetachRationale] = useState('');
  const [detachPreview, setDetachPreview] = useState<api.EnvironmentImpactPreview | null>(null);
  const [detachBusy, setDetachBusy] = useState(false);
  const [detachAck, setDetachAck] = useState(false);
  const detachKey = useRef(crypto.randomUUID());
  useEffect(() => { setDetachRationale(''); setDetachPreview(null); setDetachAck(false); detachKey.current = crypto.randomUUID(); }, [detail?.attachmentId]);
  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError(null);
    void api.getSystemEnvironments(systemId, controller.signal).then(value => { if (!controller.signal.aborted) setData(value); })
      .catch(reason => { if (!controller.signal.aborted) { setError(message(reason)); setData(null); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, revision]);
  useEffect(() => {
    onStatusChange?.(loading ? 'System subscriptions · Loading' : error ? 'System subscriptions · Unavailable'
      : `System subscriptions · ${data?.attachments.filter(x => x.attachmentState === 'Attached').length ?? 0} attached · Access and scope checked separately`);
  }, [data, loading, error, onStatusChange]);
  const disabled = busy || checking || loading;
  const checkAccess = async () => {
    if (!data || disabled || !data.permissions.canCheckAccess) return;
    setChecking(true); setError(null);
    try {
      const result = await api.checkEnvironmentAccess(systemId, { expectedVersion: data.version, purpose: 'Assessment' });
      setData({ ...data, version: result.version, attachments: result.attachments });
      setNotice('Access checked. Monitoring and assessment execution have independent prerequisites.');
    } catch (reason) { setError(message(reason)); }
    finally { setChecking(false); }
  };
  const base = `/systems/${encodeURIComponent(systemId)}`;
  const prepareDetach = async () => {
    if (!data || !detail || !detachRationale.trim() || detachBusy) return;
    setDetachBusy(true); setError(null);
    try { setDetachPreview(await api.previewEnvironmentDetach(systemId, detail.attachmentId,
      { expectedVersion: data.version, expectedAttachmentVersion: detail.version, rationale: detachRationale })); }
    catch (reason) { setError(message(reason)); }
    finally { setDetachBusy(false); }
  };
  const detach = async () => {
    if (!data || !detail || !detachPreview || !detachAck || detachBusy) return;
    setDetachBusy(true); setError(null);
    try { const result = await api.detachSystemEnvironment(systemId, detail.attachmentId,
      { expectedVersion: data.version, previewId: detachPreview.previewId, rationale: detachRationale, acknowledgeImpact: true }, detachKey.current);
      setData(result); setDetail(null); setNotice('Environment detached. Historical source and evidence records are retained.'); }
    catch (reason) { setError(message(reason)); }
    finally { setDetachBusy(false); }
  };
  return <div className="space-y-5">
    <ProviderServicesScopes systemId={systemId} systemName={systemName} busy={busy}
      refreshVersion={data?.version ?? 0} onChanged={refreshSubscriptions} />
    <section className={systemPanel} aria-label="System subscriptions">
      <div className="mb-4 flex items-start gap-3"><Network className="mt-1 text-indigo-600" size={22} aria-hidden="true" />
        <div><h2 className="text-lg font-semibold">System subscriptions</h2><p className="mt-1 text-xs text-slate-500">Attach the Azure subscriptions and resources this system uses.</p></div></div>
      {loading && <p role="status" className="text-sm">Loading system subscriptions…</p>}
      {error && <p role="alert" className="mb-3 text-sm text-red-700">{error}<button type="button" className="ml-3 underline" onClick={() => setRevision(value => value + 1)}>Retry environments</button></p>}
      {notice && <p role="status" className="mb-3 text-sm text-indigo-800 dark:text-indigo-200">{notice}</p>}
      {data && <div className="relative overflow-x-auto"><table className="w-full text-left text-xs">
        <thead><tr>{['Subscription', 'Source', 'System scope', 'Assessment access', 'Monitoring', 'Actions'].map(label => <th key={label} className="border-b px-2 py-2 text-[10px] uppercase text-slate-500">{label}</th>)}</tr></thead>
        <tbody className="divide-y divide-slate-100">{!data.attachments.length && <tr><td className="py-4" colSpan={6}>No subscriptions attached.</td></tr>}
          {data.attachments.map(item => <tr key={item.attachmentId}>
            <td className="px-2 py-3 font-semibold">{item.registration.displayName}<span className="mt-1 block font-normal text-slate-500">{stateLabel(item.attachmentState)}</span>
              {(data.hostingLinks ?? []).filter(link => link.attachmentId === item.attachmentId && link.state === 'Linked').map(link =>
                <span key={link.linkId} className="mt-1 block font-normal text-slate-500">Related scope: {data.providerScopes?.find(scope => scope.assignmentId === link.assignmentId)?.hostingScopeName ?? 'Relationship needs review'}</span>)}</td>
            <td className="px-2 py-3">{item.source === 'ProviderAllocation' ? 'Provider allocation' : 'Organization-owned'}</td>
            <td className="px-2 py-3">{item.scope.resourceIds.length} resources<span className="block text-slate-500">{stateLabel(item.scope.reviewState)}</span>
              {item.readiness.reason && <p className="mt-1 max-w-xs text-amber-800 dark:text-amber-300">{item.readiness.reason}</p>}</td>
            <td className="px-2 py-3">{stateLabel(item.assessmentAccess.state)}<span className="block text-slate-500">{item.assessmentAccess.checkedAt ? new Date(item.assessmentAccess.checkedAt).toLocaleString() : 'Not checked'}</span></td>
            <td className="px-2 py-3"><span className="rounded bg-slate-100 px-2 py-1 text-slate-700 dark:bg-slate-800 dark:text-slate-200">{!item.monitoring.enabled ? 'Not enabled' : item.monitoring.health}</span></td>
            <td className="px-2 py-3"><button type="button" disabled={disabled} className="text-indigo-700 underline" onClick={() => setDetail(item)}>View details</button>
              <button type="button" disabled={disabled || !data.permissions.canManageEnvironments || item.attachmentState !== 'Attached'}
                className="mt-2 block text-indigo-700 underline disabled:opacity-50" onClick={() => setWizard({ attachment: item })}>Manage system scope</button></td>
          </tr>)}</tbody></table></div>}
      <div className="mt-4 flex flex-wrap gap-3">
        <button type="button" disabled={disabled || !data?.permissions.canManageEnvironments} className={systemPrimaryAction}
          onClick={() => setAttaching(true)}>Attach subscription</button>
        <button type="button" disabled={disabled || !data?.permissions.canCheckAccess || !data.attachments.length}
          className={systemSecondaryAction} onClick={() => void checkAccess()}>{checking ? 'Checking access…' : 'Check access'}</button>
        <Link className={systemSecondaryAction} to={`${base}/conmon`}>{data?.permissions.canManageMonitoring ? 'Set up monitoring' : 'View monitoring'}</Link>
      </div>
      {data && !data.permissions.canManageEnvironments && <p className="mt-3 text-xs text-slate-500">Your system permissions do not allow subscription attachment. Ask an authorized system manager to attach subscriptions.</p>}
      {data?.legacyReferences.filter(item => item.kind === 'AzureProfile').map(item => <div key={item.referenceId} className="mt-3 rounded border border-amber-200 p-3 text-xs text-amber-900">
        <p>{item.displayName}: {item.reason}</p>
        <button type="button" disabled={!data.permissions.canManageEnvironments} className="mt-2 underline" onClick={() => setAttaching(true)}>Review subscription scope</button>
      </div>)}
      {data?.attachments.filter(item => item.scope.reviewState === 'PendingReview' && item.attachmentState === 'Attached').map(item =>
        <div key={item.attachmentId} className="mt-3 flex flex-wrap items-center justify-between gap-3 rounded border border-amber-200 p-3 text-xs">
          <p>{item.registration.displayName}: selected resource scope is pending review. Existing approved boundary records have not been changed.</p>
          <button type="button" disabled={disabled || !data.permissions.canManageEnvironments} className={systemSecondaryAction}
            onClick={() => setWizard({ attachment: item, reviewPendingScope: true })}>Review pending scope</button>
        </div>)}
    </section>
    {attaching && data && <SubscriptionAttachmentWizard systemId={systemId} systemName={systemName} organizationName={organizationName}
      version={data.version} onClose={() => setAttaching(false)}
      onSaved={result => { setData(result); setAttaching(false); setNotice('Subscriptions attached. Scope review, access checks and monitoring remain separate.'); }} />}
    {wizard && data && <EnvironmentWizard systemId={systemId} systemName={systemName} organizationName={organizationName} version={data.version} {...wizard}
      onClose={() => setWizard(null)} onSaved={value => { setData(value); setWizard(null); setNotice('Environment and selected scope saved. Documentation remains a draft; access, monitoring and provider coverage are separate.'); }} />}
    {detail && <SetupDialog placement="right" title={detail.registration.displayName} busy={detachBusy} onClose={() => setDetail(null)}
      description="One shared environment reference. Provider subscription and offering identity are not editable copies.">
      {error && <p role="alert" className="mb-3 text-sm text-red-700">{error}</p>}
      <dl className="space-y-3 text-sm">{[
        ['Source', detail.source], ['Offering', detail.offeringName ?? 'Not applicable'],
        ['Provider', detail.providerName ?? 'Not applicable / not recorded'], ['Consuming organization', detail.consumerName ?? organizationName ?? 'Active organization'],
        ['Released hosting scope', detail.hostingScopeName ?? 'Not applicable / not recorded'],
        ['Allocation status', detail.allocationState ?? 'Not applicable / unavailable'],
        ['Allocation effective from', detail.allocationStartsAt ?? 'Not applicable / not recorded'],
        ['Allocation expires', detail.allocationExpiresAt ?? 'No recorded expiry'],
        ['Azure subscription', detail.registration.subscriptionId], ['Azure directory', detail.registration.directoryTenantId],
        ['Cloud', detail.registration.cloud], ['Allocation', detail.allocationId ?? 'Not applicable'],
        ['Hosting assignment', detail.hostingAssignmentId ?? 'Not applicable'], ['Hosting review', detail.hostingReviewState],
        ['Scope review', detail.scope.reviewState], ['Assessment access', `${detail.assessmentAccess.state}: ${detail.assessmentAccess.reason ?? 'No check details recorded'}`],
        ['Scope reviewed by', detail.scope.reviewedBy ?? 'Not reviewed'], ['Scope reviewed at', detail.scope.reviewedAt ?? 'Not reviewed'],
        ['Monitoring', `${detail.monitoring.enabled ? detail.monitoring.health : 'Not enabled'}: ${detail.monitoring.reason ?? 'Not evaluated'}`],
        ['Current eligibility', `${detail.readiness.state}: ${detail.readiness.reason ?? 'No additional reason'}`],
      ].map(([label, value]) => <div key={label}><dt className="text-xs text-slate-500">{label}</dt><dd className="break-all">{value}</dd></div>)}</dl>
      <h3 className="mt-5 font-semibold">Selected resources</h3>
      <ul className="mt-2 space-y-2 text-xs">{detail.scope.resourceIds.map(id => <li key={id} className="break-all">{id}</li>)}</ul>
      <p className="mt-4 text-xs">Provenance: {detail.provenance.source} · {detail.provenance.externalId ?? 'No external ID'} · {detail.provenance.reconciliationState}</p>
      <p className="mt-4 text-xs">Provider relationships are optional and independent of this subscription's source.</p>
      {(data?.hostingLinks ?? []).some(link => link.attachmentId === detail.attachmentId && link.state === 'Linked') &&
        <button type="button" className="mt-2 text-sm underline" onClick={() => {
          setDetail(null);
          requestAnimationFrame(() => document.getElementById('provider-services-scopes')?.scrollIntoView({ block: 'start' }));
        }}>View related provider scopes</button>}
      <h3 className="mt-5 font-semibold">Independent source checks</h3>
      <div className="mt-2 space-y-3 text-xs">
        {([...(detail.assessmentAccess.sources ?? []), ...(detail.monitoringAccess.sources ?? []), ...(detail.monitoring.sources ?? [])])
          .map((source, index) => <div key={`${source.kind}:${source.sourceId}:${index}`} className="rounded border border-slate-200 p-3">
            <strong>{source.sourceId} · {source.kind} · {source.state}</strong>
            <p className="mt-1">{source.reason ?? 'No additional result recorded'}</p>
            <p className="mt-1">Last successful check: {source.lastSucceededAt ? new Date(source.lastSucceededAt).toLocaleString() : 'Not recorded'}</p>
          </div>)}
        {!detail.assessmentAccess.sources?.length && !detail.monitoringAccess.sources?.length && !detail.monitoring.sources?.length &&
          <p>Per-source observations have not been recorded. Attachment is not evidence of healthy collection.</p>}
      </div>
      {data?.permissions.canManageEnvironments && detail.attachmentState === 'Attached' && <fieldset disabled={detachBusy} className="mt-5 space-y-3 border-t pt-4">
        <label className="block text-sm">Detachment rationale<textarea maxLength={2000} value={detachRationale}
          onChange={event => { setDetachRationale(event.target.value); setDetachPreview(null); setDetachAck(false); }}
          className="mt-1 w-full rounded border p-2 dark:bg-slate-900" /></label>
        {!detachPreview && <button type="button" disabled={!detachRationale.trim()} className={systemSecondaryAction} onClick={() => void prepareDetach()}>Preview detachment impact</button>}
        {detachPreview && <div className="space-y-3 text-sm">{detachPreview.warnings.map(value => <p key={value}>{value}</p>)}
          <label><input type="checkbox" checked={detachAck} onChange={event => setDetachAck(event.target.checked)} /> I reviewed the affected assessment and monitoring work.</label>
          <button type="button" disabled={!detachAck} className={systemSecondaryAction} onClick={() => void detach()}>Confirm detachment</button>
        </div>}
      </fieldset>}
    </SetupDialog>}
  </div>;
}

function EnvironmentWizard({ systemId, systemName, organizationName, attachment, reviewPendingScope = false, version, onClose, onSaved }: {
  systemId: string; systemName?: string; organizationName?: string; attachment: api.SystemEnvironmentAttachment; reviewPendingScope?: boolean; version: number;
  onClose: () => void; onSaved: (response: api.SystemEnvironmentsResponse) => void;
}) {
  const [choices, setChoices] = useState<api.EnvironmentChoicesResponse | null>(null);
  const choice = choiceFor(attachment);
  const [discovery, setDiscovery] = useState<api.EnvironmentDiscoveryResponse | null>(null);
  const [selected, setSelected] = useState<string[]>(attachment?.scope.resourceIds ?? []);
  const [dependencies, setDependencies] = useState<string[]>(attachment?.scope.sharedDependencyResourceIds ?? []);
  const [exclusions, setExclusions] = useState<api.EnvironmentExcludedResource[]>(attachment?.scope.exclusions ?? []);
  const [step, setStep] = useState(1);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);
  const [rationale, setRationale] = useState('');
  const [preview, setPreview] = useState<api.EnvironmentImpactPreview | null>(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const key = useRef<{ intent: string; key: string } | null>(null);
  const writing = useRef(false);
  useEffect(() => {
    const controller = new AbortController(); setError(null);
    void api.getEnvironmentChoices(systemId, controller.signal)
      .then(available => { if (!controller.signal.aborted) setChoices(available); })
      .catch(reason => { if (!controller.signal.aborted) setError(message(reason)); });
    return () => controller.abort();
  }, [systemId, retry]);
  const discover = async () => {
    if (!choice || writing.current) return;
    writing.current = true; setPending(true); setError(null);
    try {
      const result = await api.discoverEnvironmentResources(systemId, { expectedVersion: choices?.version ?? version, selection: sourceSelection(choice) });
      setDiscovery(result); setStep(2);
    } catch (reason) { setError(message(reason)); }
    finally { writing.current = false; setPending(false); }
  };
  const review = async () => {
    if (!discovery || !selected.length || writing.current) return;
    setError(null);
      if (!rationale.trim()) { setError('Explain why the system scope must change.'); return; }
      writing.current = true; setPending(true);
      try { setPreview(await api.previewEnvironmentScope(systemId, attachment.attachmentId, {
        expectedVersion: choices?.version ?? version, expectedAttachmentVersion: attachment.version,
        discoveryToken: discovery.discoveryToken, resourceIds: selected, exclusions, sharedDependencyResourceIds: dependencies, rationale,
        reviewPendingScope,
      })); setStep(3); setAcknowledged(false); } catch (reason) { setError(message(reason)); }
      finally { writing.current = false; setPending(false); }
  };
  const apply = async () => {
    if (!discovery || !preview || !acknowledged || writing.current) return;
    const commit: api.CommitEnvironmentChangeRequest = { expectedVersion: choices?.version ?? version,
      previewId: preview.previewId, acknowledgeImpact: true, rationale };
    const intent = JSON.stringify(commit);
    if (key.current?.intent !== intent) key.current = { intent, key: crypto.randomUUID() };
    writing.current = true; setPending(true); setError(null);
    try {
      const result = await api.commitEnvironmentScope(systemId, attachment.attachmentId, commit, key.current.key);
      const saved = result.attachments.find(row => row.registration.registrationId === choice.registration.registrationId
        && row.allocationId === choice.allocationId && row.attachmentState === 'Attached');
      if (!saved || !selected.every(id => saved.scope.resourceIds.some(savedId => savedId.toLowerCase() === id.toLowerCase())))
        throw new Error('The server did not confirm the selected attachment and resource scope. Refresh before retrying.');
      onSaved(result);
    } catch (reason) { setError(message(reason)); }
    finally { writing.current = false; setPending(false); }
  };
  return <SetupDialog title={reviewPendingScope ? 'Review system resource scope' : 'Manage system resource scope'}
    expanded busy={pending} onClose={onClose} description={reviewPendingScope
      ? 'Review the unchanged pending resource selection against fresh discovery. Accepting this environment scope does not rewrite approved boundaries or grant Azure collection permissions.'
      : "Choose an eligible source, select only this system's resources, and review the exact links. This does not provision a subscription or grant Azure permissions."}>
    <ol aria-label="Environment attachment steps" className="mb-5 flex flex-wrap gap-5 text-sm">
      {['Choose source', 'System scope', 'Review'].map((label, i) => <li key={label} aria-current={step === i + 1 ? 'step' : undefined}
        className={step === i + 1 ? 'font-semibold text-indigo-700' : 'text-slate-500'}>{i + 1}. {label}</li>)}
    </ol>
    {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    {step === 1 && <>
      {!choices && !error && <p role="status">Loading authorized environment choices…</p>}
      {error && !choices && <button type="button" className={systemSecondaryAction} onClick={() => setRetry(value => value + 1)}>Retry source choices</button>}
      {choices && <div className="space-y-3">
        {[choice].map(item => <div key={item.choiceId} className="block rounded-lg border border-slate-200 p-3 text-sm">
          <strong className="ml-2">{item.registration.displayName}</strong>
          <span className="mt-1 block text-xs text-slate-500">{item.offeringName ?? 'Organization-owned'} · {item.registration.cloud} · {item.allocationState ?? item.registration.status}</span>
          {item.source === 'ProviderAllocation' && <span className="mt-1 block text-xs text-slate-500">
            {item.providerName ?? 'Provider name not recorded'} · {item.consumerName ?? organizationName ?? 'Active organization'} · {item.hostingScopeName ?? item.hostingScopeRevisionId}
          </span>}
          {!item.eligible && <span className="mt-1 block text-xs text-amber-800">{item.ineligibleReason}</span>}
          <details className="mt-2 text-xs"><summary>Identity &amp; source details</summary>
            <p className="break-all">Subscription: {item.registration.subscriptionId}<br />Azure directory: {item.registration.directoryTenantId}<br />
              Allocation: {item.allocationId ?? 'Not applicable'}<br />Released scope: {item.hostingScopeRevisionId ?? 'Not applicable'}<br />
              Provenance: {item.provenance.source} · {item.provenance.externalId ?? 'No external reference'} · {item.provenance.reconciliationState}</p>
          </details>
        </div>)}
        {choices.registrationHref && <Link className="block text-sm text-indigo-700 underline" to={choices.registrationHref}>Register or connect organization subscriptions</Link>}
        {!choices.registrationHref && <p className="text-xs text-slate-500">If the subscription is not registered, an authorized organization administrator must register it through the existing Azure connection workflow.</p>}
      </div>}
    </>}
    {step === 2 && discovery && <div className="space-y-4">
      <EnvironmentResourceSelection discovery={discovery} value={{ resourceIds: selected, exclusions, sharedDependencyResourceIds: dependencies }}
        disabled={pending || reviewPendingScope} onChange={value => {
          setSelected(value.resourceIds); setExclusions(value.exclusions); setDependencies(value.sharedDependencyResourceIds);
        }} />
      {attachment && <label className="block text-sm">Reason for scope change<textarea required value={rationale}
        onChange={event => setRationale(event.target.value)} className="mt-1 w-full rounded border p-3 dark:bg-slate-900" maxLength={2000} /></label>}
    </div>}
    {step === 3 && choice && <div className="space-y-4">
      <h3 className="font-semibold">Subscription and resource scope summary</h3>
      <dl className="grid gap-3 rounded-lg bg-slate-50 p-3 text-sm sm:grid-cols-2 dark:bg-slate-800">
        <div><dt>System</dt><dd className="break-all">{systemName ?? systemId}</dd></div>
        <div><dt>Organization</dt><dd>{organizationName ?? 'Active authorized organization'}</dd></div>
        <div><dt>Subscription</dt><dd>{choice.registration.displayName}</dd></div>
        <div><dt>Offering</dt><dd>{choice.offeringName ?? 'Not applicable — organization-owned'}</dd></div>
        {choice.source === 'ProviderAllocation' && <><div><dt>Provider</dt><dd>{choice.providerName ?? 'Not recorded'}</dd></div>
          <div><dt>Released hosting scope</dt><dd>{choice.hostingScopeName ?? choice.hostingScopeRevisionId ?? 'Not recorded'}</dd></div></>}
        <div><dt>Scope</dt><dd>{selected.length} explicit resources · {dependencies.length} shared dependencies · {exclusions.length} exclusions</dd></div>
      </dl>
      <details><summary className="cursor-pointer text-sm">Review selected resource IDs</summary><ul className="mt-2 space-y-1 text-xs">{selected.map(id => <li className="break-all" key={id}>{id}</li>)}</ul></details>
      {preview && <div className="rounded-lg border border-amber-200 p-3 text-sm">
        <p>{preview.requiresScopeReview ? 'Scope changes require review; approved boundary records remain intact.' : 'Review the proposed source changes.'}</p>
        {preview.warnings.map(value => <p key={value} className="mt-2">{value}</p>)}
        {preview.systems.map(value => <p key={value.attachmentId}>{value.systemName} · assessments {value.assessmentAffected ? 'affected' : 'not identified'} · monitoring {value.monitoringAffected ? 'affected' : 'not identified'}</p>)}
      </div>}
      <p className="rounded-lg bg-indigo-50 p-3 text-xs text-indigo-900">Reuses the canonical subscription registration. Optional provider relationships remain separate. Scope review, assessment access and monitoring require their own checks.</p>
      <label className="block text-sm"><input type="checkbox" checked={acknowledged} disabled={pending} onChange={event => setAcknowledged(event.target.checked)} /> I reviewed this exact system scope and the remaining prerequisites.</label>
    </div>}
    <footer className="mt-6 flex flex-wrap items-center justify-between gap-3 border-t pt-4">
      <p className="text-xs text-slate-500">Deployment documentation remains unchanged.</p>
      <div className="flex flex-wrap gap-3">
        {step > 1 && <button type="button" disabled={pending} className={systemSecondaryAction} onClick={() => setStep(value => value - 1)}>Back</button>}
        <button type="button" disabled={pending} className={systemSecondaryAction} onClick={onClose}>Cancel</button>
        {step === 1 && <button type="button" disabled={pending || !choice || !choices?.permissions.canManageEnvironments || !choice.eligible} className={systemPrimaryAction}
          onClick={() => void discover()}>{pending ? 'Discovering resources…' : 'Continue to system scope'}</button>}
        {step === 2 && <button type="button" disabled={pending || !selected.length} className={systemPrimaryAction} onClick={() => void review()}>Review attachment</button>}
        {step === 3 && <button type="button" disabled={pending || !acknowledged} className={systemPrimaryAction} onClick={() => void apply()}>
          {pending ? 'Saving…' : reviewPendingScope ? 'Accept reviewed environment scope' : 'Confirm scope changes'}</button>}
      </div>
    </footer>
  </SetupDialog>;
}
