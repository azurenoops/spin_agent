import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from '../features/workspaces/workspaceNavigation';
import { useSystemContext } from '../components/layout/SystemLayout';
import WorkspacePageHeader from '../components/layout/WorkspacePageHeader';
import { getMonitoringWorkspace, saveMonitoringRule, testMonitoringRule, dispositionMonitoringImpact,
  type MonitoringWorkspace, type MonitoringRule, type RuleInput, type MonitoringEvaluation } from '../api/scopedMonitoring';

type Section = 'coverage' | 'rules' | 'changes' | 'impacts';
const headings: Record<Section, [string, string]> = {
  coverage: ['Monitoring scope & health', 'Know which system resources are monitored and when collection last succeeded.'],
  rules: ['Continuous monitoring rules', 'Define which changes need attention and who reviews their impact.'],
  changes: ['Detected changes', 'Inspect attributed observations before deciding their documentation consequences.'],
  impacts: ['Review authorization impact', 'Understand the change, affected controls and proposed document updates before deciding next steps.'],
};
const emptyRule: RuleInput = { name: '', boundaryDefinitionId: '', baselineReference: '', ownerId: '',
  signal: 'Alert', condition: { field: 'Type', operator: 'Equals', value: 'Drift' },
  cadenceMinutes: 60, severity: 'Medium', isEnabled: true };
const date = (value: string | null) => value ? new Date(value).toLocaleString() : 'No successful collection';
const button = 'rounded-md border border-indigo-200 px-3 py-2 text-sm font-medium text-indigo-700 disabled:opacity-50';
const inputClass = 'mt-1 w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm';
function Badge({ value }: { value: string }) {
  return <span className={`inline-flex rounded px-2 py-1 text-xs ${['Healthy', 'NoImpact'].includes(value)
    ? 'bg-emerald-50 text-emerald-800' : ['Failed', 'Stale', 'Missing', 'Pending', 'CollectionUnavailable', 'ScopeReviewRequired'].includes(value)
      ? 'bg-amber-50 text-amber-900' : 'bg-slate-100 text-slate-600'}`}>{value}</span>;
}
function Details({ value }: { value: string }) {
  let display = value;
  try { display = JSON.stringify(JSON.parse(value), null, 2); } catch { /* Retained non-JSON source text remains readable. */ }
  return <pre className="max-h-80 overflow-auto whitespace-pre-wrap break-words rounded bg-slate-50 p-3 text-xs">{display}</pre>;
}

function ImpactRecords({ value, owner, systemId }: { value: string; owner: string; systemId: string }) {
  let records: {
    implementations?: { id: string; controlId: string; currentVersion: number }[];
    duties?: { id: string; controlId: string; customerResponsibility: string | null; confirmedBy: string }[];
    evidence?: { id: string; fileName: string }[]; documents?: string[];
  };
  try { records = JSON.parse(value); }
  catch { return <div role="alert">Retained targets could not be decoded. <Details value={value} /></div>; }
  return <div className="overflow-x-auto"><table className="w-full text-left text-sm">
    <thead><tr className="border-b text-xs uppercase text-slate-500"><th className="p-3">Affected record</th><th>Impact</th><th>Owner</th><th>State</th><th /></tr></thead>
    <tbody>
      {(records.implementations ?? []).map(record => <tr key={record.id} className="border-b"><td className="p-3 font-semibold">{record.controlId}</td>
        <td>Implementation / narrative v{record.currentVersion}</td><td>{owner}</td><td><Badge value="Review required" /></td>
        <td><Link className={button} to={`/systems/${systemId}/narratives`}>Open →</Link></td></tr>)}
      {(records.duties ?? []).map(record => <tr key={record.id} className="border-b"><td className="p-3 font-semibold">{record.controlId} duty</td>
        <td>{record.customerResponsibility ?? 'Responsibility review'}</td><td>{record.confirmedBy}</td><td><Badge value="Review required" /></td>
        <td><Link className={button} to={`/systems/${systemId}/inheritance/subscriptions`}>Open →</Link></td></tr>)}
      {(records.evidence ?? []).map(record => <tr key={record.id} className="border-b"><td className="p-3 font-semibold">{record.fileName}</td>
        <td>Relevance / freshness review</td><td>{owner}</td><td><Badge value="Review required" /></td>
        <td><Link className={button} to={`/systems/${systemId}/evidence`}>Open →</Link></td></tr>)}
      {(records.documents ?? []).map(document => <tr key={document} className="border-b"><td className="p-3 font-semibold">{document}</td>
        <td>Proposed document scope</td><td>{owner}</td><td><Badge value="Review candidate" /></td><td /></tr>)}
    </tbody>
  </table></div>;
}

export default function ScopedMonitoring({ section = 'coverage' }: { section?: Section }) {
  const { detail } = useSystemContext();
  return <ScopedMonitoringContent key={detail.systemId} system={{ id: detail.systemId, name: detail.name }} section={section} />;
}

function ScopedMonitoringContent({ system, section }: { system: { id: string; name: string }; section: Section }) {
  const currentSystem = useRef(system.id);
  currentSystem.current = system.id;
  const [data, setData] = useState<MonitoringWorkspace | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const operationPending = useRef(false);
  const [editing, setEditing] = useState<MonitoringRule | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [form, setForm] = useState<RuleInput>(emptyRule);
  const [tests, setTests] = useState<MonitoringEvaluation[] | null>(null);
  const [selected, setSelected] = useState<{ id: string; version: number } | null>(null);
  const [disposition, setDisposition] = useState('');
  const [rationale, setRationale] = useState('');
  const load = useCallback(async () => {
    const result = await getMonitoringWorkspace(system.id);
    if (currentSystem.current === system.id) {
      setData(result);
      setSelected(current => current ?? (result.impacts[0] ? { id: result.impacts[0].id, version: result.impacts[0].version } : null));
      setError('');
    }
  }, [system.id]);
  useEffect(() => {
    let cancelled = false;
    setData(null); setError(''); setSelected(null); setShowForm(false); setEditing(null); setTests(null);
    setDisposition(''); setRationale('');
    const refresh = () => load().catch(e => { if (!cancelled) setError(e instanceof Error ? e.message : 'Monitoring records could not be loaded.'); });
    void refresh();
    const interval = setInterval(() => void refresh(), 30_000);
    return () => { cancelled = true; clearInterval(interval); };
  }, [load]);
  const act = async (operation: () => Promise<unknown>) => {
    if (operationPending.current) return;
    operationPending.current = true;
    setBusy(true); setError('');
    try { await operation(); await load(); }
    catch (e) { setError(e instanceof Error ? e.message : 'The operation failed. Reload and retry.'); }
    finally { operationPending.current = false; setBusy(false); }
  };
  const edit = (rule: MonitoringRule) => {
    setEditing(rule); setShowForm(true); setTests(null);
    setForm({ name: rule.name, boundaryDefinitionId: rule.boundaryDefinitionId, baselineReference: rule.baselineReference,
      ownerId: rule.ownerId, signal: rule.signal, condition: JSON.parse(rule.triggerCondition),
      cadenceMinutes: rule.cadenceMinutes, severity: rule.severityOverride, isEnabled: rule.isEnabled, expectedVersion: rule.version });
  };
  const selectedImpact = data?.impacts.find(x => x.id === selected?.id);
  const staleSelection = selectedImpact !== undefined && selected?.version !== selectedImpact.version;
  const evaluation = data?.evaluations.find(x => x.id === selectedImpact?.evaluationId);
  const healthy = data?.coverage.filter(x => x.health === 'Healthy').length ?? 0;
  return <div className="space-y-6 text-slate-800">
    <WorkspacePageHeader eyebrow={system.name} title={headings[section][0]} description={headings[section][1]} actions={<>
      {section === 'rules' && data?.canManageRules && <button className={`${button} bg-indigo-600 !text-white`} onClick={() => {
        setEditing(null); setForm({ ...emptyRule, boundaryDefinitionId: data?.boundaries[0]?.id ?? '' }); setShowForm(true);
      }}>Create rule →</button>}
      {section === 'coverage' && <Link className={button} to={`/systems/${system.id}/boundaries`}>Review monitored scope →</Link>}
      <button className={button} disabled={busy} onClick={() => void act(load)}>Refresh</button>
    </>} />
    {error && <div role="alert" className="rounded border border-red-200 bg-red-50 p-4 text-sm text-red-800">{error}
      <button className={`${button} ml-3`} onClick={() => void act(load)}>Reload</button></div>}
    {!data && !error && <p role="status">Loading monitoring records…</p>}
    {data && <>
      <div className="rounded-lg border border-indigo-200 bg-indigo-50 px-4 py-3 text-sm">
        Review required · Approved baseline preserved. Observations and recommendations never grant or revoke authorization.
      </div>
      <div className="grid gap-6 xl:grid-cols-[minmax(0,2fr)_minmax(240px,1fr)]">
        <section aria-label="Monitoring records" className="min-w-0 space-y-6 rounded-xl border border-slate-200 bg-white p-5">
          {section === 'coverage' && <>
            <div className="grid gap-3 sm:grid-cols-3">{[
              ['Selected scope', data.coverage.length], ['Current collection', `${healthy} / ${data.coverage.length}`],
              ['Needs attention', data.coverage.length - healthy],
            ].map(([label, value]) => <div key={label} className="rounded-lg border p-4"><p className="text-xs text-slate-500">{label}</p><p className="mt-2 text-2xl font-bold">{value}</p></div>)}</div>
            <div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead><tr className="border-b text-xs uppercase text-slate-500">
              <th className="p-3">Resource</th><th>Scope</th><th>Last success</th><th>Health</th></tr></thead><tbody>
              {data.coverage.map(row => <tr key={row.assignmentId} className="border-b"><td className="max-w-xs break-all p-3">{row.resourceId ?? row.providerComponentId ?? 'Unmapped component'}</td>
                <td>{row.providerComponentId ? 'Provider dependency' : 'Included'}</td><td>{date(row.lastSuccessAt)}</td><td><Badge value={row.health} />{row.error && <p>{row.error}</p>}</td></tr>)}
            </tbody></table></div>
            {!data.coverage.length && <p>No in-scope component assignments. Collection coverage is unknown, not healthy.</p>}
            <p className="text-sm text-slate-500">Provider dependencies are not live connector health. Missing or unmapped resources require scope review.</p>
          </>}
          {section === 'rules' && <>
            <div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead><tr className="border-b text-xs uppercase text-slate-500"><th className="p-3">Rule</th><th>Target scope</th><th>Evaluation</th><th>State</th><th /></tr></thead><tbody>
              {data.rules.map(rule => <tr key={rule.id} className="border-b"><td className="p-3 font-semibold">{rule.name}<p className="text-xs font-normal text-slate-500">Version {rule.version} · {rule.ownerId}</p></td>
                <td>{data.boundaries.find(b => b.id === rule.boundaryDefinitionId)?.name ?? rule.boundaryDefinitionId}</td>
                <td>Every {rule.cadenceMinutes} minutes<br /><span className="text-xs">{rule.lastEvaluatedAt ? date(rule.lastEvaluatedAt) : 'Not evaluated'}</span></td>
                <td><Badge value={rule.isEnabled ? 'Enabled' : 'Disabled'} /></td><td><button className={button} onClick={() => edit(rule)}>Open →</button></td></tr>)}
            </tbody></table></div>
            {!data.rules.length && <p>No scoped rules. Create a rule after reviewing its boundary and baseline.</p>}
            {showForm && <form className="space-y-4 border-t pt-5" onSubmit={event => {
              event.preventDefault(); void act(async () => { const saved = await saveMonitoringRule(system.id, form, editing?.id); setEditing(saved); setForm({ ...form, expectedVersion: saved.version }); });
            }}>
              <h2 className="text-lg font-semibold">{editing ? 'Rule preview' : 'Create monitoring rule'}</h2>
              <div className="grid gap-4 sm:grid-cols-2">
                <label>Name<input required maxLength={200} className={inputClass} value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} /></label>
                <label>Reviewed boundary<select required className={inputClass} value={form.boundaryDefinitionId} onChange={e => setForm({ ...form, boundaryDefinitionId: e.target.value })}><option value="">Select boundary</option>{data.boundaries.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</select></label>
                <label>Reviewed baseline reference<input required maxLength={500} className={inputClass} value={form.baselineReference} onChange={e => setForm({ ...form, baselineReference: e.target.value })} /></label>
                <label>Owner<input required maxLength={200} className={inputClass} value={form.ownerId} onChange={e => setForm({ ...form, ownerId: e.target.value })} /></label>
                <label>Signal<select className={inputClass} value={form.signal} onChange={e => setForm({ ...form, signal: e.target.value })}><option>Alert</option><option>ProviderRelease</option></select></label>
                <label>Cadence (minutes)<input type="number" min={1} max={10080} required className={inputClass} value={form.cadenceMinutes} onChange={e => setForm({ ...form, cadenceMinutes: Number(e.target.value) })} /></label>
                <label>Condition field<input required list="condition-fields" className={inputClass} value={form.condition.field} onChange={e => setForm({ ...form, condition: { ...form.condition, field: e.target.value } })} /><datalist id="condition-fields">{['Type', 'Severity', 'ControlId', 'ControlFamily', 'Change.publicAccess'].map(v => <option key={v}>{v}</option>)}</datalist></label>
                <label>Operator<select className={inputClass} value={form.condition.operator} onChange={e => setForm({ ...form, condition: { ...form.condition, operator: e.target.value } })}>{['Equals', 'NotEquals', 'Becomes'].map(v => <option key={v}>{v}</option>)}</select></label>
                <label>Condition value<input required className={inputClass} value={form.condition.value} onChange={e => setForm({ ...form, condition: { ...form.condition, value: e.target.value } })} /></label>
                <label>Severity<select className={inputClass} value={form.severity} onChange={e => setForm({ ...form, severity: e.target.value })}>{['Low', 'Medium', 'High', 'Critical'].map(v => <option key={v}>{v}</option>)}</select></label>
              </div>
              <label className="flex gap-2 text-sm"><input type="checkbox" checked={form.isEnabled} onChange={e => setForm({ ...form, isEnabled: e.target.checked })} />Enabled</label>
              <p className="text-sm text-slate-500">Saving records your reviewed boundary snapshot. A later scope change requires re-review. Response: create impact review; never automatic authorization.</p>
              <div className="flex gap-3"><button disabled={busy || !data.canManageRules} className={button}>Save reviewed rule</button>
                {editing && <button type="button" className={button} disabled={busy || !data.canManageRules} onClick={() => void act(async () => setTests(await testMonitoringRule(system.id, editing.id)))}>Test saved rule (no writes)</button>}
                <button type="button" className={button} onClick={() => setShowForm(false)}>Close</button></div>
            </form>}
            {tests && <div role="status"><h3 className="font-semibold">Test results</h3>{tests.length ? tests.map(e => <p key={e.id}>{e.outcome}</p>) : <p>Disabled rule: no evaluation or review work.</p>}</div>}
            <details><summary className="cursor-pointer font-semibold">Version and evaluation history ({data.evaluations.length})</summary>
              {data.evaluations.map(e => <details key={e.id} className="mt-3 border-t pt-3"><summary>Version {e.ruleVersion} · {e.outcome} · {date(e.evaluatedAt)}</summary><Details value={e.ruleSnapshotJson} /><Details value={e.inputSnapshotJson} /></details>)}</details>
          </>}
          {section === 'changes' && <>
            {!data.changes.length && <p>No attributed observations. This does not establish healthy collection or absence of out-of-scope changes.</p>}
            {data.changes.map(change => <details key={change.sourceId} className="border-b pb-4"><summary className="cursor-pointer py-3 font-semibold">{change.title} <Badge value={change.attribution} /></summary>
              <p className="mb-3 text-sm">{change.kind} · {change.controlId ?? 'Control unknown'} · {date(change.observedAt)}</p><Details value={change.changeDetails ?? 'No property-level change details supplied.'} /></details>)}
          </>}
          {section === 'impacts' && <>
            {!data.impacts.length && <p>No rule-generated impact reviews. Check rule evaluation and collection health before interpreting this result.</p>}
            {selected && !selectedImpact && <p role="alert">The selected impact is no longer available. Choose a record explicitly before reviewing.</p>}
            {data.impacts.length > 0 && <label>Impact review<select className={inputClass} disabled={busy} value={selected?.id ?? ''} onChange={e => {
              const target = data.impacts.find(item => item.id === e.target.value);
              setSelected(target ? { id: target.id, version: target.version } : null); setDisposition(''); setRationale('');
            }}>
              {!selectedImpact && <option value={selected?.id ?? ''}>Select an available impact</option>}
              {data.impacts.map(i => <option key={i.id} value={i.id}>{i.controlId ?? 'Control unknown'} · {i.ownerId} · {i.disposition}</option>)}</select></label>}
            {selectedImpact && <>
              {staleSelection && <div role="alert" className="rounded-lg bg-amber-50 p-3 text-sm text-amber-900">
                This impact changed while you were reviewing it. Inspect the latest record before submitting.
                <button type="button" className={`${button} mt-2 block`} onClick={() => {
                  setSelected({ id: selectedImpact.id, version: selectedImpact.version }); setDisposition(''); setRationale('');
                }}>Review latest revision</button>
              </div>}
              <h2 className="text-lg font-semibold">{selectedImpact.controlId ?? 'Unknown control'} · Mission impact review</h2>
              <div className="grid gap-4 sm:grid-cols-2"><div className="rounded-lg bg-slate-50 p-4"><h3 className="mb-2 text-xs font-semibold uppercase text-slate-500">Reviewed rule & baseline</h3>
                <p className="font-semibold">Retained rule version {evaluation?.ruleVersion ?? 'unknown'}</p><p className="mt-2 text-sm text-slate-500">Approved narrative content remains unchanged.</p>
                <details className="mt-3"><summary className="cursor-pointer text-sm">Inspect baseline snapshot</summary><Details value={evaluation?.ruleSnapshotJson ?? '{}'} /></details></div>
                <div className="rounded-lg bg-amber-50 p-4"><h3 className="mb-2 text-xs font-semibold uppercase text-slate-500">Observed change</h3>
                  <p className="font-semibold">{evaluation?.outcome ?? 'Evaluation unavailable'}</p><p className="mt-2 text-sm text-slate-500">{evaluation ? date(evaluation.evaluatedAt) : 'Inspect collection evidence'}</p>
                  <details className="mt-3"><summary className="cursor-pointer text-sm">Inspect observed delta</summary><Details value={evaluation?.inputSnapshotJson ?? '{}'} /></details></div></div>
              <ImpactRecords value={selectedImpact.affectedRecordsJson} owner={selectedImpact.ownerId} systemId={system.id} />
              <p className="text-sm">Owner: {selectedImpact.ownerId} · <Badge value={selectedImpact.disposition} /></p>
              {selectedImpact.disposition === 'Pending' && data.canReviewImpacts ? <form className="space-y-3" onSubmit={event => {
                event.preventDefault();
                if (!selected || staleSelection) return;
                void act(() => dispositionMonitoringImpact(system.id, selected.id, selected.version, disposition, rationale));
              }}><label>Review outcome<select required className={inputClass} value={disposition} onChange={e => setDisposition(e.target.value)}>
                <option value="">Select after reviewing evidence</option><option value="NoImpact">No mission documentation impact</option>
                <option value="StageNarrativeReview">Stage narrative review (approved baseline unchanged)</option><option value="RecommendReassessment">Recommend reassessment (not an AO decision)</option></select></label>
                <label className="block">Review rationale<textarea required maxLength={4000} className={inputClass} value={rationale} onChange={e => setRationale(e.target.value)} /></label>
                <button disabled={busy || staleSelection} className={button}>Record review outcome →</button></form>
                : <div><p>{selectedImpact.rationale}</p><p className="text-xs text-slate-500">{selectedImpact.reviewedBy} · {date(selectedImpact.reviewedAt)}</p><Details value={selectedImpact.narrativeProposalIdsJson} /></div>}
            </>}
          </>}
        </section>
        <aside className="space-y-6 text-sm"><section className="border-l-2 border-indigo-200 pl-4"><p className="text-xs font-semibold uppercase tracking-widest text-slate-500">Used in your package</p>
          <h2 className="mt-2 font-semibold">{section === 'impacts' ? 'Change assessment / Draft package updates' : 'ConMon plan / Monitoring evidence'}</h2>
          <p className="mt-2 text-slate-500">Reviewed records contribute to the package. Draft changes must not replace the approved baseline.</p></section>
          <section className="border-l-2 border-indigo-200 pl-4"><p className="text-xs font-semibold uppercase tracking-widest text-slate-500">Review & ownership</p>
            <h2 className="mt-2 font-semibold">Keep the next action clear</h2><p className="mt-2 text-slate-500">Provider source review and mission impact disposition are separate. Rules create recommendations, not approvals.</p></section>
          <section className="border-l-2 border-indigo-200 pl-4"><p className="mb-3 text-xs font-semibold uppercase tracking-widest text-slate-500">Related work</p><Link className={button} to={`/systems/${system.id}/readiness`}>View package readiness →</Link></section>
        </aside>
      </div>
    </>}
  </div>;
}
