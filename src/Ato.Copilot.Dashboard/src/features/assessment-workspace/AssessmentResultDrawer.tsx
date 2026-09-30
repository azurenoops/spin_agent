import { useEffect, useRef, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import SetupDialog from '../workspace-operations/SetupDialog';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import AssessmentComponentRiskSummary from './AssessmentComponentRiskSummary';
import type { AssessmentFinding } from '../../api/assessments';
import { assessmentWorkspaceError, getAssessmentResult, reconcileAssessmentResult, reviewAssessmentControl,
  type AssessmentControlReview, type AssessmentResultDetail, type RetainedAssessmentPlan } from '../../api/assessmentWorkspace';

export default function AssessmentResultDrawer({ systemId, id, plan, onClose, onChanged, onTask, onDeviation }: {
  systemId: string; id: string; plan: RetainedAssessmentPlan | null; onClose: () => void; onChanged: () => void;
  onTask: (finding: AssessmentFinding) => void; onDeviation: (finding: AssessmentFinding) => void;
}) {
  const [data, setData] = useState<AssessmentResultDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [attempt, setAttempt] = useState(0);
  const [busy, setBusy] = useState(false);
  const [reviewing, setReviewing] = useState(false);
  const [reconciling, setReconciling] = useState(false);
  const [controlId, setControlId] = useState('');
  const [determination, setDetermination] = useState<AssessmentControlReview['determination'] | ''>('');
  const [method, setMethod] = useState<AssessmentControlReview['method']>('Examine');
  const [severity, setSeverity] = useState('');
  const [notes, setNotes] = useState('');
  const [evidence, setEvidence] = useState<string[]>([]);
  const [findingSearch, setFindingSearch] = useState('');
  const [findingStatus, setFindingStatus] = useState('');
  const [findingPage, setFindingPage] = useState(1);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setData(null); setError(null);
    getAssessmentResult(systemId, id, plan?.id, controller.signal)
      .then(value => { if (!controller.signal.aborted) setData(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(assessmentWorkspaceError(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, id, plan?.id, plan?.contentHash, attempt]);
  const review = async () => {
    if (!data?.permissions.canReview || busy) { setError(data?.permissions.reviewReason ?? 'Assessor permission is required.'); return; }
    if (!controlId || !determination || determination === 'OtherThanSatisfied' && !severity) {
      setError('Choose a control and determination, including CAT severity for Other Than Satisfied.'); return;
    }
    setBusy(true); setError(null);
    try {
      await reviewAssessmentControl(systemId, id, { expectedResultRevision: data.item.revision, controlId,
        determination, method, notes, evidenceIds: evidence, catSeverity: severity || null });
      if (!mounted.current) return;
      setReviewing(false); setDetermination(''); setNotes(''); setEvidence([]); setAttempt(v => v + 1); onChanged();
    } catch (reason) { if (mounted.current) setError(assessmentWorkspaceError(reason)); }
    finally { if (mounted.current) setBusy(false); }
  };
  const reconcile = async () => {
    if (!data?.permissions.canReconcile || !plan || busy) { setError(data?.permissions.reconcileReason ?? 'Select a plan and confirm reconciliation permission.'); return; }
    setBusy(true); setError(null);
    try {
      await reconcileAssessmentResult(systemId, id, { planId: plan.id, expectedPlanHash: plan.contentHash, expectedResultRevision: data.item.revision });
      if (!mounted.current) return;
      setReconciling(false); setAttempt(v => v + 1); onChanged();
    } catch (reason) { if (mounted.current) setError(assessmentWorkspaceError(reason)); }
    finally { if (mounted.current) setBusy(false); }
  };
  const selected = new Set(data?.selectedScope.map(id => id.toUpperCase()) ?? []);
  const observed = new Set(data?.observedControls.map(id => id.toUpperCase()).filter(id => selected.has(id)) ?? []);
  const findings = data?.findings.filter(f => (!findingStatus || f.status === findingStatus)
    && `${f.title} ${f.controlId} ${f.description}`.toLowerCase().includes(findingSearch.toLowerCase())) ?? [];
  const reviewControls = Array.from(new Set([...(data?.selectedScope ?? []), ...(data?.observedControls ?? [])])).sort();
  return <SetupDialog placement="right" expanded title="Result details" description={data?.item.name ?? 'Loading retained result'}
    busy={busy} onClose={onClose}><div className="aw-dialog">
    {loading && <p role="status">Loading result details…</p>}
    {error && <div className="aw-error" role="alert"><p>{error}</p><button type="button" disabled={busy} onClick={() => setAttempt(v => v + 1)}>Refresh result</button></div>}
    {data && !loading && <>
      <section className="aw-card"><h3>{data.item.name}</h3>
        <div className="aw-pills"><span>{data.item.collectionStatus} collection</span><span>{data.item.reviewStatus}</span></div>
        <p>{data.item.planRevision === null ? 'Collected without a retained plan link (preliminary).' : `Collected against revision ${data.item.planRevision}`}</p>
        {data.item.planTitle && <p>{data.item.planTitle} · {data.item.planStatusAtCollection ?? 'Status not recorded'} at collection</p>}
        <dl><dt>Source / method</dt><dd>{data.item.source} · {data.item.method}</dd>
          <dt>Collected</dt><dd>{new Date(data.item.recordedAt).toLocaleString()}</dd>
          <dt>Collector / assessor</dt><dd>{data.item.actor ?? 'Not recorded'}</dd></dl>
        <p className="aw-info">Collection completion is not a control assessment decision.</p>
      </section>
      {data.item.warnings.length > 0 && <ul className="aw-warning">{data.item.warnings.map(warning => <li key={warning}>{warning}</li>)}</ul>}
      <section className="aw-card"><h3>Coverage against the selected plan</h3>
        {plan ? <><p>Comparing with saved revision {plan.revision}.</p>
          <strong>{observed.size} of {data.selectedScope.length} planned controls have observations.</strong></>
          : <p>No plan selected. Assessed coverage is unknown; baseline size is not coverage.</p>}
        <p>{data.item.reviewedControlCount} controls have recorded review information. Observation coverage is not a passing result.</p>
        {data.missingControls.length > 0 && <div><h4>Coverage gaps</h4><div className="aw-tags">{data.missingControls.map(control =>
          <span className="aw-gap-control" key={control}>{control}</span>)}</div></div>}
        {data.outOfScopeControls.length > 0 && <p>Out-of-scope observations: {data.outOfScopeControls.join(', ')}</p>}
        {data.excludedControls.length > 0 && <p>Excluded controls: {data.excludedControls.join(', ')}</p>}
        {data.duplicateControls.length > 0 && <p>Duplicate control observations: {data.duplicateControls.join(', ')}</p>}
        <details className="aw-disclosure"><summary>Original collection scope</summary>
          <p>{data.originalScope.length ? data.originalScope.join(', ') : 'No original plan scope was retained.'}</p>
          <p>Changing the selected plan does not change this historical association.</p></details>
      </section>
      {data.errors.length > 0 && <section className="aw-card aw-warning"><h3>Collection errors and unavailable checks</h3>
        <ul>{data.errors.map((error, index) => <li key={index}>{error}</li>)}</ul><p>Retained completed work is preserved. Missing checks are not counted as passing.</p></section>}
      {data.item.requiresReconciliation && <section className="aw-card"><h3>Plan reconciliation</h3>
        <p>The collection source and selected plan need reconciliation before treating these results as aligned.</p>
        {data.permissions.canReconcile && plan
          ? <button type="button" onClick={() => setReconciling(true)}>Reconcile with selected plan</button>
          : <p>{data.permissions.reconcileReason ?? 'Select a plan and use an authorized assessor identity.'}</p>}
        {reconciling && <div className="aw-warning"><p>Record a comparison to {plan?.title}, revision {plan?.revision}. Original collection scope stays unchanged.
          This does not rerun checks or approve controls.</p><button type="button" disabled={busy} onClick={() => void reconcile()}>Confirm reconciliation</button></div>}
      </section>}
      <section className="aw-card"><h3>Retained evidence</h3>
        {!data.evidence.length && <p>No linked evidence records were returned. Evidence completeness is not established.</p>}
        {data.evidence.map(item => <article className="aw-evidence" key={item.id}><strong>{item.name}</strong>
          {item.downloadUrl ? <AuthenticatedDownload url={item.downloadUrl} fileName={item.name}
            onDownloadError={reason => { setData(null); setError(assessmentWorkspaceError(reason)); }}>Open retained evidence</AuthenticatedDownload>
            : <p className="aw-muted">Evidence metadata retained; no protected file is available through this result.</p>}
          {item.contentHash && <details><summary>Integrity reference</summary><code>{item.contentHash}</code></details>}
        </article>)}
        <Link to={`/systems/${systemId}/evidence`}>Open system evidence catalog</Link>
      </section>
      <section className="aw-card"><h3>Control review</h3>
        <p>Record an explicit assessor determination using Examine, Interview or Test. It does not close findings or grant authorization.</p>
        {data.permissions.canReview ? <button type="button" onClick={() => { setReviewing(true); setError(null); }}>Review a control</button>
          : <p>{data.permissions.reviewReason ?? 'Assessor permission is required.'}</p>}
        {reviewing && <form className="aw-fields" onSubmit={event => { event.preventDefault(); void review(); }}>
          <label>Control to review<select value={controlId} onChange={e => setControlId(e.target.value)} disabled={busy}>
            <option value="">Choose a control</option>{reviewControls.map(c => <option key={c}>{c}</option>)}</select></label>
          <label>Determination<select value={determination} onChange={e => {
            const value = e.target.value;
            if (value === '' || value === 'Satisfied' || value === 'OtherThanSatisfied') setDetermination(value);
          }} disabled={busy}><option value="">Choose a determination</option><option value="Satisfied">Satisfied</option>
            <option value="OtherThanSatisfied">Other Than Satisfied</option></select></label>
          <label>Assessment method<select value={method} onChange={e => {
            const value = e.target.value; if (value === 'Examine' || value === 'Interview' || value === 'Test') setMethod(value);
          }} disabled={busy}>{['Examine', 'Interview', 'Test'].map(m => <option key={m}>{m}</option>)}</select></label>
          {determination === 'OtherThanSatisfied' && <label>CAT severity<select value={severity} onChange={e => setSeverity(e.target.value)} disabled={busy}>
            <option value="">Choose severity</option><option value="CatI">CAT I</option><option value="CatII">CAT II</option><option value="CatIII">CAT III</option></select></label>}
          <label>Assessor notes<textarea rows={4} value={notes} onChange={e => setNotes(e.target.value)} disabled={busy} /></label>
          {data.evidence.length > 0 && <fieldset><legend>Evidence used</legend>{data.evidence.map(item => <label className="aw-check" key={item.id}>
            <input type="checkbox" checked={evidence.includes(item.id)} disabled={busy} onChange={e => setEvidence(previous =>
              e.target.checked ? [...previous, item.id] : previous.filter(id => id !== item.id))} />{item.name}</label>)}</fieldset>}
          <div className="aw-actions"><button type="button" disabled={busy} onClick={() => setReviewing(false)}>Cancel review</button>
            <button type="submit" className="aw-primary" disabled={busy || !controlId || !determination}>Save control review</button></div>
        </form>}
      </section>
      <section className="aw-card"><h3>Findings and disposition</h3>
        {!data.findings.length ? <p>No findings are recorded in this result. That alone does not establish that controls passed.</p> : <>
          <div className="aw-filter-row"><input aria-label="Search findings" placeholder="Find by control or title" value={findingSearch}
            onChange={e => { setFindingSearch(e.target.value); setFindingPage(1); }} />
            <select aria-label="Finding disposition" value={findingStatus} onChange={e => { setFindingStatus(e.target.value); setFindingPage(1); }}>
              <option value="">All dispositions</option>{Array.from(new Set(data.findings.map(f => f.status))).map(status => <option key={status}>{status}</option>)}</select>
          </div>
          {findings.slice((findingPage - 1) * 20, findingPage * 20).map(finding => <article className="aw-finding" key={finding.findingId}>
            <h4>{finding.controlId ?? 'Unmapped control'} · {finding.title}</h4><small>{finding.severity} · {finding.status}</small>
            <p>{finding.description}</p><details><summary>Resource and remediation details</summary>
              <p>{finding.resourceType ?? 'Resource type not recorded'}</p><code>{finding.resourceId}</code>
              <p>{finding.remediationGuidance ?? 'No remediation guidance recorded.'}</p>
              {finding.deviationType && <p>Existing deviation: {finding.deviationType}</p>}</details>
            <div className="aw-actions">
              <Link to={`/systems/${systemId}/remediation?finding=${encodeURIComponent(finding.findingId)}`}>Review finding &amp; linked work</Link>
              {data.permissions.canRemediate && <button type="button" onClick={() => onTask(finding)}>Create remediation task</button>}
              {data.permissions.canRequestDeviation && <button type="button" onClick={() => onDeviation(finding)}>Request deviation</button>}
            </div>
          </article>)}
          <p>{findings.length} matching findings</p>
          {findings.length > 20 && <nav className="aw-pager" aria-label="Finding pages">
            <button type="button" disabled={findingPage === 1} onClick={() => setFindingPage(p => p - 1)}>Previous</button>
            <span>{findingPage} of {Math.ceil(findings.length / 20)}</span>
            <button type="button" disabled={findingPage * 20 >= findings.length} onClick={() => setFindingPage(p => p + 1)}>Next</button></nav>}
        </>}
        <div className="aw-actions"><Link to={`/systems/${systemId}/remediation`}>Findings &amp; remediation</Link><Link to={`/systems/${systemId}/poam`}>POA&amp;M</Link></div>
      </section>
      <details className="aw-disclosure"><summary>Source and review history</summary>
        {data.history.length ? data.history.map((event, index) => <article key={index}><strong>{event.action}</strong>
          <p>{new Date(event.at).toLocaleString()}{event.actor ? ` · ${event.actor}` : ''}</p><p>{event.description}</p></article>)
          : <p>No additional history is available for this record.</p>}</details>
      {data.item.id.startsWith('assessment:') && <AssessmentComponentRiskSummary key={data.item.recordId}
        systemId={systemId} assessmentId={data.item.recordId} />}
    </>}
    <footer className="aw-dialog-footer"><button type="button" disabled={busy} onClick={onClose}>Close result</button></footer>
  </div></SetupDialog>;
}
