import { useRef, useState } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import WorkspacePageHeader from '../../components/layout/WorkspacePageHeader';
import { Link } from '../workspaces/workspaceNavigation';
import { buttonClass, secondaryButtonClass, inputClass, message, Status, Pager, useRemote } from '../workspace-operations/workspaceUi';
import SetupDialog from '../workspace-operations/SetupDialog';
import { changeImpactHref, listOfferings } from './api';
import * as api from './providerMonitoringApi';
import type { ProviderMonitoringRule, ProviderMonitoringRuleInput, ProviderMonitoringSource, ProviderMonitoringWorkspace } from './providerMonitoringApi';
import './providerPresentation.css';

const labels = {
  AuthorizationExpiry: 'Reviewed authorization expiry', AuthorizationWithdrawal: 'Recorded authorization withdrawal',
  EvidenceFreshness: 'Reviewed evidence freshness', PublishedReleaseChange: 'Published release change',
};
const ruleInputClass = `${inputClass} w-full min-w-0`;
function inputFor(source: ProviderMonitoringSource): ProviderMonitoringRuleInput {
  return { expectedRevision: null, name: '', ownerId: '', signal: source.signal, sourceId: source.sourceId,
    condition: { field: source.field, operator: source.signal === 'AuthorizationExpiry' ? 'LessThanOrEqual'
      : source.signal === 'EvidenceFreshness' ? 'GreaterThanOrEqual' : 'Equals',
    value: source.signal === 'AuthorizationExpiry' || source.signal === 'EvidenceFreshness' ? '30' : 'true' },
    cadenceMinutes: 60, response: 'CreateProviderImpactReview', isEnabled: true, expectedSourceRevision: source.sourceRevision };
}
function JsonDetail({ value }: { value: string }) {
  let content = value;
  try { content = JSON.stringify(JSON.parse(value), null, 2); }
  catch { return <p role="alert">Stored detail cannot be decoded: {value}</p>; }
  return <pre className="max-h-72 overflow-auto whitespace-pre-wrap break-words rounded bg-slate-50 p-3 text-xs dark:bg-slate-900">{content}</pre>;
}

export function ProviderMonitoringPanel({ offeringId }: { offeringId: string }) {
  return <ProviderMonitoringContent key={offeringId} offeringId={offeringId} />;
}

function ProviderMonitoringContent({ offeringId }: { offeringId: string }) {
  const remote = useRemote(signal => api.getProviderMonitoring(offeringId, signal), [offeringId]);
  const [form, setForm] = useState<ProviderMonitoringRuleInput | null>(null);
  const [editing, setEditing] = useState<ProviderMonitoringRule | null>(null);
  const [result, setResult] = useState<api.ProviderMonitoringEvaluation | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editorContext, setEditorContext] = useState<ProviderMonitoringWorkspace | null>(null);
  const [saved, setSaved] = useState(false);
  const lock = useRef(false);
  const retry = useRef<{ intent: string; key: string } | null>(null);
  const keyFor = (intent: unknown) => {
    const serialized = JSON.stringify(intent);
    if (retry.current?.intent !== serialized) retry.current = { intent: serialized, key: crypto.randomUUID() };
    return retry.current.key;
  };
  const perform = async (action: () => Promise<void>) => {
    if (lock.current) return;
    lock.current = true;
    setBusy(true); setError(null);
    try { await action(); retry.current = null; remote.retry(); }
    catch (e) { setError(message(e)); }
    finally { lock.current = false; setBusy(false); }
  };
  const close = () => { if (!lock.current) { setForm(null); setResult(null); setError(null); setSaved(false); } };
  const edit = (rule: ProviderMonitoringRule) => {
    setEditing(rule); setResult(null); setError(null); setSaved(false); setEditorContext(remote.data);
    setForm({ expectedRevision: rule.revision, name: rule.name, ownerId: rule.ownerId, signal: rule.signal,
      sourceId: rule.sourceId, condition: JSON.parse(rule.conditionJson), cadenceMinutes: rule.cadenceMinutes,
      response: 'CreateProviderImpactReview', isEnabled: rule.isEnabled,
      expectedSourceRevision: remote.data?.sources.find(x => x.sourceId === rule.sourceId && x.signal === rule.signal)?.sourceRevision ?? null });
  };
  const data = remote.data ?? (form ? editorContext : null);
  const available = data?.sources.filter(x => x.collectionHealth === 'Available') ?? [];
  const unavailable = remote.loading || !!remote.error;
  return <div className="provider-workflow provider-page space-y-5">
    <WorkspacePageHeader eyebrow={data?.offeringName ?? 'Provider operations'} title="Service monitoring"
      description="Monitor reviewed source freshness and published service changes, then review consequences before handing changes to missions."
      actions={<><button className={buttonClass} disabled={busy || unavailable || available.length === 0} onClick={() => {
        const source = available[0];
        if (source) { setEditing(null); setResult(null); setError(null); setSaved(false); setEditorContext(data); setForm(inputFor(source)); }
      }}>Create monitoring rule</button><button className={secondaryButtonClass} disabled={busy} onClick={remote.retry}>Refresh</button></>} />
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {data && <div className="grid gap-6 xl:grid-cols-[minmax(0,3fr)_minmax(220px,1fr)]">
      <div className="min-w-0 space-y-5">
        {data.sources.some(x => x.collectionHealth !== 'Available') && <div role="status" className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm">
          Some source facts are missing or unreviewed. They do not evaluate as healthy or as no trigger.
        </div>}
        <section className="provider-panel space-y-4 p-5"><h2 className="text-lg font-semibold">Monitoring rules</h2>
          {!data.rules.length && <p className="text-sm">No provider-owned rules. Select a concrete reviewed source to create one.</p>}
          <div className="provider-table-wrap"><table className="provider-table"><thead><tr><th>Rule</th><th>Scope</th><th>Last evaluation</th><th>State</th><th /></tr></thead>
            <tbody>{data.rules.map(rule => {
              const last = data.evaluations.find(x => x.ruleId === rule.id && x.outcome !== 'Configured');
              return <tr key={rule.id}><td>{rule.name}<small>{labels[rule.signal]} · Revision {rule.revision}</small></td><td>{data.offeringName}</td>
                <td>{last ? `${last.collectionHealth} · ${last.outcome}` : 'Not evaluated'}
                  <small>{rule.lastEvaluatedAt ? new Date(rule.lastEvaluatedAt).toLocaleString() : 'No saved evaluation'} · Every {rule.cadenceMinutes} minutes</small></td>
                <td>{rule.isEnabled ? 'Enabled' : 'Disabled'}</td><td><button className={secondaryButtonClass} disabled={busy || unavailable} onClick={() => edit(rule)}>Inspect rule</button>
                  {last?.impactReviewId && <Link className="mt-2 block text-indigo-600" to={changeImpactHref(offeringId, { reviewId: last.impactReviewId })}>View impact</Link>}</td></tr>;
            })}</tbody></table></div>
          {form && <SetupDialog title={editing ? 'Edit monitoring rule' : 'Create monitoring rule'}
            description={`Offering: ${data.offeringName}${editing ? ` · Rule: ${editing.name} · Revision ${editing.revision}` : ''}`}
            busy={busy} onClose={close}>
            <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
            <form className="space-y-4" onSubmit={event => {
              event.preventDefault(); if (unavailable) return; void perform(async () => {
                const saved = await api.saveProviderMonitoringRule(offeringId, form, keyFor({ offeringId, ruleId: editing?.id, form }), editing?.id);
                setEditing(saved); setForm({ ...form, expectedRevision: saved.revision }); setSaved(true);
              });
            }}>
            <fieldset disabled={busy || unavailable} className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <label className="min-w-0 text-sm">Rule name<input className={ruleInputClass} required maxLength={200} value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} /></label>
              <label className="min-w-0 text-sm">Source<select className={ruleInputClass} value={`${form.signal}:${form.sourceId}`} onChange={e => {
                const source = data.sources.find(x => `${x.signal}:${x.sourceId}` === e.target.value);
                if (source) setForm({ ...inputFor(source), expectedRevision: form.expectedRevision, name: form.name, ownerId: form.ownerId, isEnabled: form.isEnabled });
              }}>{!data.sources.some(x => x.signal === form.signal && x.sourceId === form.sourceId) &&
                <option value={`${form.signal}:${form.sourceId}`}>Retained source unavailable</option>}
                {data.sources.map(source => <option key={`${source.signal}:${source.sourceId}`} value={`${source.signal}:${source.sourceId}`}>
                  {source.name} · {labels[source.signal]} · {source.collectionHealth}</option>)}</select></label>
              <label className="min-w-0 text-sm">Scope<input className={ruleInputClass} readOnly value={data.offeringName} /></label>
              <label className="min-w-0 text-sm">Owner<input className={ruleInputClass} required maxLength={254} value={form.ownerId} onChange={e => setForm({ ...form, ownerId: e.target.value })} /></label>
              <label className="min-w-0 text-sm">Condition field<input className={ruleInputClass} readOnly value={form.condition.field} /></label>
              <label className="min-w-0 text-sm">Condition operator<select className={ruleInputClass} value={form.condition.operator} onChange={e => setForm({ ...form, condition: { ...form.condition, operator: e.target.value } })}>
                {(form.signal === 'AuthorizationExpiry' || form.signal === 'EvidenceFreshness' ? ['LessThanOrEqual', 'GreaterThanOrEqual'] : ['Equals', 'NotEquals'])
                  .map(op => <option key={op}>{op}</option>)}</select></label>
              <label className="min-w-0 text-sm">{form.signal === 'AuthorizationExpiry' || form.signal === 'EvidenceFreshness' ? 'Threshold (days)' : 'Condition value (true/false)'}
                <input className={ruleInputClass} required value={form.condition.value} onChange={e => setForm({ ...form, condition: { ...form.condition, value: e.target.value } })} /></label>
              <label className="min-w-0 text-sm">Cadence (minutes)<input className={ruleInputClass} type="number" min={1} max={10080} required value={form.cadenceMinutes} onChange={e => setForm({ ...form, cadenceMinutes: Number(e.target.value) })} /></label>
              <label className="min-w-0 text-sm">Response<input className={ruleInputClass} readOnly value="Create provider impact review" /></label>
            </div>
            <label className="flex gap-2 text-sm"><input type="checkbox" checked={form.isEnabled} onChange={e => setForm({ ...form, isEnabled: e.target.checked })} />Enabled</label>
            <p className="text-sm text-slate-500">Saving retains the selected source baseline and rule version. A release-change rule compares future offering-linked releases with this baseline.</p>
            </fieldset>
            <Status error={error} />
            {saved && <p role="status">Rule saved. Tests and evaluations use the saved rule, not unsaved edits.</p>}
            <div className="flex flex-wrap gap-2"><button disabled={busy || unavailable} className={buttonClass}>Save rule</button>
              {editing && <><button type="button" disabled={busy || unavailable} className={secondaryButtonClass} onClick={() => void perform(async () =>
                setResult(await api.testProviderMonitoringRule(offeringId, editing.id)))}>Test saved rule</button>
                <button type="button" disabled={busy || unavailable || !editing.isEnabled} className={secondaryButtonClass} onClick={() => void perform(async () =>
                  setResult(await api.evaluateProviderMonitoringRule(offeringId, editing.id, editing.revision, keyFor({ offeringId, id: editing.id, revision: editing.revision }))))}>Evaluate now</button></>}
              <button type="button" disabled={busy} className={secondaryButtonClass} onClick={close}>Cancel</button></div>
          </form>
          {result && <div role="status" className="rounded bg-indigo-50 p-3 text-sm">{result.collectionHealth} · {result.outcome}
            {result.impactReviewId ? busy ? <span className="ml-3">Review provider impact after this operation finishes.</span>
              : <Link className="ml-3 text-indigo-700 underline" to={changeImpactHref(offeringId, { reviewId: result.impactReviewId })}>Review provider impact</Link>
              : <p>No provider review was created by this result.</p>}</div>}
          </SetupDialog>}
        </section>
        <section className="provider-panel space-y-4 p-5"><h2 className="text-lg font-semibold">Source facts & health</h2>
          {!data.sources.length && <p>No recorded authorization, reviewed evidence, or offering-linked published release is available.</p>}
          {data.sources.map(source => <div key={`${source.signal}:${source.sourceId}`} className="flex flex-wrap justify-between gap-3 border-b py-3 text-sm">
            <div><p className="font-semibold">{source.name}</p><p>{labels[source.signal]}</p><p className="text-slate-500">
              {source.sourceTimestamp ? `Retained source: ${new Date(source.sourceTimestamp).toLocaleString()}` : 'Source timestamp unavailable'}</p></div>
            <span>{source.collectionHealth}{source.value !== null ? ` · ${source.value}` : ''}</span></div>)}
          <p className="text-sm text-slate-500">Evidence age uses the retained upload timestamp after evidence review. Availability describes database facts, not a live connector.</p>
        </section>
        <details className="provider-panel p-5"><summary className="cursor-pointer font-semibold">Immutable rule & evaluation history ({data.evaluations.length})</summary>
          {data.evaluations.map(item => <details key={item.id} className="mt-3 border-t pt-3"><summary>Revision {item.ruleRevision} · {item.outcome} · {new Date(item.createdAt).toLocaleString()}</summary>
            <JsonDetail value={item.ruleSnapshotJson} /><JsonDetail value={item.sourceSnapshotJson} />
            {item.impactReviewId && <Link className="text-indigo-600 underline" to={changeImpactHref(offeringId, { reviewId: item.impactReviewId })}>Open retained impact review</Link>}</details>)}</details>
      </div>
      <aside className="space-y-6 text-sm"><section><h2 className="font-semibold">Provider vs mission monitoring</h2><p className="mt-2 text-slate-500">
        These rules belong only to this offering. Provider identity does not authorize customer-system changes or mission review.</p></section>
        <section><h2 className="font-semibold">Review before handoff</h2><p className="mt-2 text-slate-500">
          Matched facts open provider impact review. Select exact current context and explicitly review it; publication remains a separate action.
          Only that existing reviewed publication workflow delivers release changes through recorded mission dependencies.</p></section>
        <section><h2 className="font-semibold">No live connection claims</h2><p className="mt-2 text-slate-500">No Azure, multicloud, or eMASS connector is configured by these rules.</p></section></aside>
    </div>}
  </div>;
}

export default function ProviderMonitoringPage({ offeringId }: { offeringId?: string }) {
  const params = useParams<{ id?: string }>();
  const [query, setQuery] = useSearchParams();
  const selected = offeringId ?? params.id ?? query.get('offeringId');
  const [page, setPage] = useState(1);
  const offerings = useRemote(signal => selected ? Promise.resolve(null) : listOfferings(page, '', signal), [selected, page]);
  if (selected) return <ProviderMonitoringPanel key={selected} offeringId={selected} />;
  return <div className="space-y-5"><WorkspacePageHeader title="Service monitoring" eyebrow="Provider operations"
    description="Select an owning offering before configuring source monitoring. This does not select a mission system." />
    <Status loading={offerings.loading} error={offerings.error} retry={offerings.retry} />
    {offerings.data && <><ul className="space-y-3">{offerings.data.items.map(item => <li key={item.offeringId}>
      <button className={secondaryButtonClass} onClick={() => setQuery(previous => { previous.set('offeringId', item.offeringId); return previous; })}>{item.name} — Monitor offering</button>
    </li>)}</ul>{!offerings.data.items.length && <p>No provider offerings are available.</p>}<Pager {...offerings.data} onPage={setPage} /></>}
  </div>;
}
