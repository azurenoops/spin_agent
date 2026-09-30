import { useEffect, useRef, useState } from 'react';
import * as api from '../../api/systemEnvironments';
import SetupDialog from '../workspace-operations/SetupDialog';
import { Link } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';

function message(error: unknown) { return error instanceof Error ? error.message : 'The allocation operation could not be confirmed.'; }
export default function ProviderSubscriptionAllocations({ offeringId }: { offeringId: string }) {
  const session = useWorkspaceSession();
  return <AllocationContent key={JSON.stringify([offeringId, session?.identity.oid, session?.roles])} offeringId={offeringId} />;
}
function AllocationContent({ offeringId }: { offeringId: string }) {
  const [data, setData] = useState<api.ProviderAllocationsResponse | null>(null);
  const [choices, setChoices] = useState<api.ProviderAllocationChoicesResponse | null>(null);
  const [revision, setRevision] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [registrationId, setRegistration] = useState('');
  const [consumerId, setConsumer] = useState('');
  const [scopeId, setScope] = useState('');
  const [selectedScopes, setScopes] = useState<string[]>([]);
  const [start, setStart] = useState('');
  const [end, setEnd] = useState('');
  const [externalSource, setExternalSource] = useState(false);
  const [source, setSource] = useState('FAST');
  const [externalId, setExternalId] = useState('');
  const [sourceRevision, setSourceRevision] = useState('');
  const [evidence, setEvidence] = useState('');
  const [reviewed, setReviewed] = useState(false);
  const [selected, setSelected] = useState<api.ProviderEnvironmentAllocation | null>(null);
  const [usage, setUsage] = useState<api.ProviderAllocationUsageResponse | null>(null);
  const [action, setAction] = useState<'Withdraw' | 'Replace'>('Withdraw');
  const [replacement, setReplacement] = useState('');
  const [rationale, setRationale] = useState('');
  const [preview, setPreview] = useState<api.EnvironmentImpactPreview | null>(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const replay = useRef<{ intent: string; key: string } | null>(null);
  const writing = useRef(false);
  const recordedAt = useRef(new Date().toISOString());
  const replayKey = (body: unknown) => {
    const intent = JSON.stringify(body);
    if (replay.current?.intent !== intent) replay.current = { intent, key: crypto.randomUUID() };
    return replay.current.key;
  };
  useEffect(() => {
    const controller = new AbortController(); setError(null); setData(null); setChoices(null);
    void Promise.all([api.listProviderEnvironmentAllocations(offeringId, controller.signal), api.getProviderAllocationChoices(offeringId, controller.signal)])
      .then(([list, options]) => { if (!controller.signal.aborted) { setData(list); setChoices(options); } })
      .catch(reason => { if (!controller.signal.aborted) setError(message(reason)); });
    return () => controller.abort();
  }, [offeringId, revision]);
  useEffect(() => {
    setUsage(null); setPreview(null); setAcknowledged(false); setRationale(''); setReplacement('');
    if (!selected) return;
    const controller = new AbortController();
    void api.getProviderAllocationUsage(offeringId, selected.allocationId, controller.signal)
      .then(result => { if (!controller.signal.aborted) setUsage(result); })
      .catch(reason => { if (!controller.signal.aborted) setError(message(reason)); });
    return () => controller.abort();
  }, [offeringId, selected?.allocationId]);
  const registration = choices?.registrations.find(item => item.registrationId === registrationId);
  const scope = choices?.releasedScopes.find(item => item.revisionId === scopeId);
  const allowedScopes = scope?.permittedResourceScopes.filter(path =>
    path.toLowerCase().startsWith(`/subscriptions/${registration?.subscriptionId.toLowerCase()}/`)
      || path.toLowerCase() === `/subscriptions/${registration?.subscriptionId.toLowerCase()}`) ?? [];
  const save = async () => {
    if (!choices?.canManage || writing.current) return;
    if (!registration || !consumerId || !scope || !selectedScopes.length || !start || !reviewed) {
      setError('Select the actual registration, consuming organization, released scope, effective date and reviewed provenance.'); return;
    }
    if (!Number.isFinite(Date.parse(start)) || end && (!Number.isFinite(Date.parse(end)) || Date.parse(end) <= Date.parse(start))) {
      setError('Enter valid effective dates with expiration after the start.'); return;
    }
    const body: api.RecordProviderAllocationRequest = { expectedOfferingVersion: choices.offeringVersion,
      registrationId, consumerTenantId: consumerId, hostingScopeRevisionId: scopeId, permittedResourceScopes: selectedScopes,
      startsAt: new Date(start).toISOString(), expiresAt: end ? new Date(end).toISOString() : null,
      provenance: { source: externalSource ? source.trim() : 'ProviderRecorded',
        externalId: externalSource ? externalId.trim() || null : null, sourceRevision: externalSource ? sourceRevision.trim() || null : null,
        reconciliationState: 'Verified', evidenceReference: evidence.trim() || null, recordedAt: recordedAt.current },
    };
    writing.current = true; setBusy(true); setError(null);
    try { await api.recordProviderEnvironmentAllocation(offeringId, body, replayKey(body)); setOpen(false); setRevision(value => value + 1); }
    catch (reason) { setError(message(reason)); }
    finally { writing.current = false; setBusy(false); }
  };
  const prepareChange = async () => {
    if (!selected || !rationale.trim() || writing.current) return;
    writing.current = true; setBusy(true); setError(null);
    try { setPreview(await api.previewProviderAllocationChange(offeringId, selected.allocationId,
      { expectedVersion: selected.version, action, replacementAllocationId: action === 'Replace' ? replacement || null : null, rationale })); }
    catch (reason) { setError(message(reason)); }
    finally { writing.current = false; setBusy(false); }
  };
  const commitChange = async () => {
    if (!selected || !preview || !acknowledged || writing.current) return;
    const body = { expectedVersion: selected.version, previewId: preview.previewId, rationale, acknowledgeImpact: true };
    writing.current = true; setBusy(true); setError(null);
    try { await api.commitProviderAllocationChange(offeringId, selected.allocationId, body, replayKey(body)); setSelected(null); setRevision(value => value + 1); }
    catch (reason) { setError(message(reason)); }
    finally { writing.current = false; setBusy(false); }
  };
  return <section className="provider-panel min-w-0 space-y-4" aria-label="Azure subscription allocations">
    <div className="flex flex-wrap items-start justify-between gap-3"><div><h2 className="text-lg font-semibold">Azure subscription allocations</h2>
      <p className="mt-1 text-sm text-slate-500">Allocate an existing registered subscription to an authorized consuming organization. This does not provision Azure or grant Azure access.</p></div>
      <button type="button" disabled={!choices?.canManage || busy} className="provider-primary" onClick={() => {
        setError(null); setReviewed(false); recordedAt.current = new Date().toISOString(); replay.current = null; setOpen(true);
      }}>Record subscription allocation</button></div>
    {!data && !error && <p role="status">Loading eligible allocation records…</p>}
    {error && !open && !selected && <p role="alert" className="text-sm text-red-700">{error}<button type="button" className="ml-3 underline" onClick={() => setRevision(value => value + 1)}>Retry allocations</button></p>}
    {data && <div className="provider-table-wrap"><table className="provider-table" aria-label="Provider subscription allocations">
      <thead><tr><th>Subscription</th><th>Consuming organization</th><th>Status</th><th>Systems</th><th>Open</th></tr></thead>
      <tbody>{data.allocations.map(item => <tr key={item.allocationId}><td>{item.registration.displayName}</td>
        <td>{item.consumerName}</td><td>{item.state}</td><td>{item.systemCount}</td><td><button type="button" className="provider-secondary" onClick={() => { setError(null); setSelected(item); }}>Review allocation</button></td></tr>)}
        {!data.allocations.length && <tr><td colSpan={5}>No subscription allocations recorded for this offering.</td></tr>}</tbody></table></div>}
    {open && choices && <SetupDialog title="Record an Azure subscription allocation" expanded busy={busy} onClose={() => setOpen(false)}
      description="Use explicit registered identity and a released hosting scope. External source details record reviewed provenance; this form does not claim a live FAST connector.">
      <form className="space-y-4" onSubmit={event => { event.preventDefault(); void save(); }}>
        {error && <p role="alert" className="text-sm text-red-700">{error}</p>}
        <fieldset disabled={busy} className="space-y-4">
          <label className="block text-sm">Actual Azure subscription<select required aria-label="Actual Azure subscription" className="mt-1 w-full rounded border p-2 dark:bg-slate-900" value={registrationId}
            onChange={event => { setRegistration(event.target.value); setScopes([]); }}><option value="">Choose a registered subscription</option>
            {choices.registrations.map(item => <option key={item.registrationId} value={item.registrationId}>{item.displayName} · {item.cloud}</option>)}</select></label>
          {!choices.registrations.length && <p className="text-sm">No eligible provider registrations. Register an actual subscription before allocating it.</p>}
          {choices.registrationHref && <Link to={choices.registrationHref} className="text-indigo-700 underline">Manage registered subscriptions</Link>}
          <label className="block text-sm">Consuming organization<select required aria-label="Consuming organization" className="mt-1 w-full rounded border p-2 dark:bg-slate-900" value={consumerId} onChange={event => setConsumer(event.target.value)}>
            <option value="">Choose an authorized organization</option>{choices.consumers.map(item => <option key={item.tenantId} value={item.tenantId}>{item.name}</option>)}</select></label>
          <label className="block text-sm">Released hosting scope<select required aria-label="Released hosting scope" className="mt-1 w-full rounded border p-2 dark:bg-slate-900" value={scopeId}
            onChange={event => { setScope(event.target.value); setScopes([]); }}><option value="">Choose the applicable release</option>
            {choices.releasedScopes.map(item => <option key={item.revisionId} value={item.revisionId}>{item.name}</option>)}</select></label>
          {allowedScopes.map(path => <label key={path} className="block break-all text-xs"><input type="checkbox" checked={selectedScopes.includes(path)}
            onChange={event => setScopes(values => event.target.checked ? [...values, path] : values.filter(value => value !== path))} /> {path}</label>)}
          {scope && registration && !allowedScopes.length && <p className="text-sm text-amber-800">This released scope does not include the selected subscription identity.</p>}
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="text-sm">Effective from<input type="datetime-local" required value={start} onChange={event => setStart(event.target.value)} className="mt-1 w-full rounded border p-2 dark:bg-slate-900" /></label>
            <label className="text-sm">Expires at (optional)<input type="datetime-local" value={end} onChange={event => setEnd(event.target.value)} className="mt-1 w-full rounded border p-2 dark:bg-slate-900" /></label>
          </div>
          <label className="block text-sm">Allocation provenance<select value={externalSource ? 'External' : 'ProviderRecorded'} onChange={event => { setExternalSource(event.target.value === 'External'); setEvidence(''); }}
            className="mt-1 w-full rounded border p-2 dark:bg-slate-900"><option value="ProviderRecorded">Verified directly by provider</option><option value="External">Verified external allocation record</option></select></label>
          {externalSource && <>
            <label className="block text-sm">Source system<input required value={source} onChange={event => setSource(event.target.value)} className="mt-1 w-full rounded border p-2 dark:bg-slate-900" placeholder="FAST or another verified source" /></label>
            <label className="block text-sm">External allocation reference<input required value={externalId} onChange={event => setExternalId(event.target.value)} className="mt-1 w-full rounded border p-2 dark:bg-slate-900" /></label>
            <label className="block text-sm">External source revision<input required value={sourceRevision} onChange={event => setSourceRevision(event.target.value)} className="mt-1 w-full rounded border p-2 dark:bg-slate-900" /></label>
            <p className="text-xs text-slate-500">Use the retained provider package-entry ID for the reviewed external record. The server verifies its evidence and offering ownership; recording it does not run a FAST connector.</p>
          </>}
          <label className="block text-sm">Reviewed source evidence reference<input required={externalSource} value={evidence} onChange={event => setEvidence(event.target.value)} className="mt-1 w-full rounded border p-2 dark:bg-slate-900" /></label>
          <label className="block text-sm"><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} /> I verified the exact subscription, consumer and external source provenance.</label>
        </fieldset>
        <div className="flex gap-3"><button type="submit" disabled={busy || !reviewed} className="provider-primary">{busy ? 'Recording…' : 'Record allocation'}</button>
          <button type="button" disabled={busy} className="provider-secondary" onClick={() => setOpen(false)}>Cancel</button></div>
      </form>
    </SetupDialog>}
    {selected && <SetupDialog title={`Allocation · ${selected.registration.displayName}`} busy={busy} onClose={() => setSelected(null)}
      description="Review usage before withdrawing or replacing eligibility. Historical evidence remains retained; replacement is not an automatic system migration.">
      {error && <p role="alert" className="mb-3 text-sm text-red-700">{error}</p>}
      <p className="text-sm">{selected.consumerName} · {selected.offeringName} · {selected.state}</p>
      <p className="mt-2 text-xs">Effective {selected.startsAt} to {selected.expiresAt ?? 'no recorded expiry'}. Source: {selected.provenance.source} · {selected.provenance.externalId ?? 'no external identifier'}</p>
      <details className="mt-3 text-xs"><summary>Subscription and scope identity</summary><p className="break-all">{selected.registration.subscriptionId} · {selected.registration.directoryTenantId}</p>
        <p className="break-all">{selected.hostingScopeRevisionId}</p>{selected.permittedResourceScopes.map(path => <p key={path} className="break-all">{path}</p>)}</details>
      <h3 className="mt-4 font-semibold">Referencing systems</h3>
      {!usage && !error && <p role="status">Loading system usage…</p>}
      {usage?.systems.map(item => <p key={item.attachmentId} className="mt-2 text-sm">{item.systemName} · {item.selectedResourceCount} resources</p>)}
      {usage?.systems.length === 0 && <p className="mt-2 text-sm">No systems currently reference this allocation.</p>}
      {data?.canManage && <fieldset disabled={busy} className="mt-5 space-y-3">
        <label className="block text-sm">Allocation change<select aria-label="Allocation change" className="mt-1 w-full rounded border p-2 dark:bg-slate-900" value={action} onChange={event => { setAction(event.target.value === 'Replace' ? 'Replace' : 'Withdraw'); setPreview(null); }}>
          <option>Withdraw</option><option>Replace</option></select></label>
        {action === 'Replace' && <label className="block text-sm">Replacement allocation<select className="mt-1 w-full rounded border p-2 dark:bg-slate-900" value={replacement} onChange={event => { setReplacement(event.target.value); setPreview(null); }}>
          <option value="">Choose an existing allocation</option>{data.allocations.filter(item => item.allocationId !== selected.allocationId && item.consumerTenantId === selected.consumerTenantId && item.state === 'Active')
            .map(item => <option key={item.allocationId} value={item.allocationId}>{item.registration.displayName}</option>)}</select></label>}
        <label className="block text-sm">Change rationale<textarea value={rationale} onChange={event => { setRationale(event.target.value); setPreview(null); }} className="mt-1 w-full rounded border p-2 dark:bg-slate-900" maxLength={2000} /></label>
        {!preview && <button type="button" disabled={!rationale.trim() || !usage} className="provider-secondary" onClick={() => void prepareChange()}>Preview allocation impact</button>}
        {preview && <div className="space-y-3 rounded border border-amber-200 p-3 text-sm">
          {preview.warnings.map(value => <p key={value}>{value}</p>)}
          <p>{preview.systems.length} affected systems. Eligibility will be reevaluated before future collection.</p>
          <label><input type="checkbox" checked={acknowledged} onChange={event => setAcknowledged(event.target.checked)} /> I reviewed affected systems and the access impact.</label>
          <button type="button" disabled={!acknowledged} className="provider-primary" onClick={() => void commitChange()}>Confirm allocation change</button>
        </div>}
      </fieldset>}
    </SetupDialog>}
  </section>;
}
