import { useCallback, useEffect, useRef, useState } from 'react';
import { ArrowRight, CalendarDays, Check, Circle, Cloud, FileText, Flag, ListChecks, PencilLine, RefreshCw, Upload, UsersRound } from 'lucide-react';
import { Link, useSearchParams } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import SetupDialog from '../workspace-operations/SetupDialog';
import { systemSecondaryAction } from '../systems/SystemTaskPresentation';
import ScanImportDialog from '../scan-import/ScanImportDialog';
import CreateRemediationTaskModal from '../../components/remediation/CreateRemediationTaskModal';
import AddDeviationDialog from '../../components/AddDeviationDialog';
import type { AssessmentFinding } from '../../api/assessments';
import { assessmentWorkspaceError, collectAssessmentResults, createAssessmentPlan, finalizeAssessmentPlan,
  getAssessmentPlan, getAssessmentResults, prepareAssessmentReport,
  type AssessmentPlanTask, type AssessmentPlanWorkspace, type AssessmentResultsWorkspace } from '../../api/assessmentWorkspace';
import AssessmentPlanEditor, { type PlanEditorDraft } from './AssessmentPlanEditor';
import AssessmentResultDrawer from './AssessmentResultDrawer';
import { RetainedSarPreview, SavedPlanPreview } from './AssessmentDocuments';
import './AssessmentWorkflow.css';

const taskKeys: AssessmentPlanTask[] = ['title', 'lead', 'scope', 'approach', 'team', 'schedule', 'procedures'];
const planActions = [
  { task: 'title', label: 'Edit title', icon: PencilLine },
  { task: 'team', label: 'Edit team', icon: UsersRound },
  { task: 'schedule', label: 'Edit schedule', icon: CalendarDays },
  { task: 'procedures', label: 'Review control procedures', icon: ListChecks },
] as const;
function planningTask(value: string | null): AssessmentPlanTask | null {
  return taskKeys.find(key => key === value) ?? (value === 'objectives' || value === 'methods' ? 'procedures' : null);
}
const date = (value: string | null | undefined) => value ? new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) : 'Not recorded';
const statusLabel = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2');

export default function AssessmentWorkflow({ systemId }: { systemId: string }) {
  const [params, setParams] = useSearchParams();
  const session = useWorkspaceSession();
  const accessKey = JSON.stringify([session?.identity.oid, session?.workspace.personId, session?.systemAccess?.permissions]);
  const planView = params.get('tab') === 'plan';
  const requestedPlan = params.get('plan');
  const task = planningTask(params.get('task'));
  const selectedResult = params.get('result');
  const reportId = params.get('sar');
  const action = params.get('action');
  const [planState, setPlanState] = useState<{ key: string; value: AssessmentPlanWorkspace } | null>(null);
  const [planLoading, setPlanLoading] = useState(true);
  const [planError, setPlanError] = useState<string | null>(null);
  const [planAttempt, setPlanAttempt] = useState(0);
  const planKey = JSON.stringify([systemId, requestedPlan, accessKey]);
  const workspace = planState?.key === planKey ? planState.value : null;
  const plan = workspace?.plan ?? null;
  const [resultsState, setResultsState] = useState<{ key: string; scopeKey: string; value: AssessmentResultsWorkspace } | null>(null);
  const [resultsLoading, setResultsLoading] = useState(false);
  const [resultsError, setResultsError] = useState<string | null>(null);
  const [resultsAttempt, setResultsAttempt] = useState(0);
  const [busy, setBusy] = useState(false);
  const [operationError, setOperationError] = useState<string | null>(null);
  const [notice, setNotice] = useState<{ warning: boolean; text: string } | null>(null);
  const [taskFinding, setTaskFinding] = useState<AssessmentFinding | null>(null);
  const [deviationFinding, setDeviationFinding] = useState<AssessmentFinding | null>(null);
  const drafts = useRef(new Map<string, PlanEditorDraft>());
  const [, setDraftChange] = useState(0);
  const mounted = useRef(true);
  const actionKey = useRef<string | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const search = params.get('search') ?? '';
  const page = Math.max(1, Math.floor(Number(params.get('page')) || 1));
  const included = Array.from(new Set(params.getAll('include'))).sort();
  const includeKey = included.join(',');
  const resultsScopeKey = JSON.stringify([systemId, plan?.id, plan?.contentHash, search, page, accessKey]);
  const resultsKey = JSON.stringify([resultsScopeKey, includeKey]);
  const results = resultsState?.scopeKey === resultsScopeKey ? resultsState.value : null;
  const currentResults = resultsState?.key === resultsKey && !resultsLoading && !resultsError;
  const azureReady = results?.collection.canRunAzure === true && results.collection.azure.state.toLowerCase() === 'ready';
  const activeStage = planView ? 'plan' : params.get('stage') ?? 'collect';
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useEffect(() => { setTaskFinding(null); setDeviationFinding(null); }, [accessKey]);
  useEffect(() => { setNotice(null); }, [planView]);
  useEffect(() => {
    const controller = new AbortController(); setPlanLoading(true); setPlanError(null);
    getAssessmentPlan(systemId, requestedPlan, controller.signal)
      .then(value => { if (!controller.signal.aborted) setPlanState({ key: planKey, value }); })
      .catch(reason => { if (!controller.signal.aborted) { setPlanState(null); setPlanError(assessmentWorkspaceError(reason)); } })
      .finally(() => { if (!controller.signal.aborted) setPlanLoading(false); });
    return () => controller.abort();
  }, [systemId, requestedPlan, planKey, planAttempt]);
  useEffect(() => {
    if (planView || planLoading || !workspace || planError) return;
    const controller = new AbortController(); setResultsLoading(true); setResultsError(null);
    getAssessmentResults(systemId, { planId: plan?.id ?? null, search, page, pageSize: 25,
      selectedResultIds: includeKey ? includeKey.split(',') : [] }, controller.signal)
      .then(value => { if (!controller.signal.aborted) setResultsState({ key: resultsKey, scopeKey: resultsScopeKey, value }); })
      .catch(reason => { if (!controller.signal.aborted) setResultsError(assessmentWorkspaceError(reason)); })
      .finally(() => { if (!controller.signal.aborted) setResultsLoading(false); });
    return () => controller.abort();
  }, [systemId, planView, planLoading, !!workspace, planError, plan?.id, resultsKey, search, page, includeKey, resultsAttempt]);
  useEffect(() => {
    if (planView || resultsLoading || !(busy && action === 'collect' || results?.items.some(item => /pending|running|inprogress/i.test(item.collectionStatus)))) return;
    const timer = window.setTimeout(() => setResultsAttempt(value => value + 1), 3000);
    return () => window.clearTimeout(timer);
  }, [planView, resultsLoading, results, busy, action]);
  const change = (key: string, value: string | null, replace = false) => {
    const next = new URLSearchParams(params);
    if (['task', 'preview', 'result', 'sar', 'action'].includes(key))
      ['task', 'preview', 'result', 'sar', 'action'].filter(value => value !== key).forEach(value => next.delete(value));
    if (value === null || value === '') next.delete(key); else next.set(key, value);
    if (key === 'search') next.delete('page');
    setParams(next, { replace });
  };
  const close = useCallback(() => {
    const next = new URLSearchParams(params);
    ['task', 'preview', 'result', 'sar', 'action'].forEach(key => next.delete(key));
    setParams(next, { replace: true }); setOperationError(null);
  }, [params, setParams]);
  const stageUrl = (stage: string) => {
    const next = new URLSearchParams(params);
    ['task', 'preview', 'result', 'sar', 'action'].forEach(key => next.delete(key));
    next.set('tab', stage === 'plan' ? 'plan' : 'results'); next.set('stage', stage);
    if (plan) next.set('plan', plan.id);
    return `/systems/${systemId}/assessments?${next}`;
  };
  const edit = (key: string) => {
    const resolved = planningTask(key);
    if (!resolved) { setOperationError('This planning task has no editable field in the retained-plan contract. Review its source details.'); return; }
    if (!workspace?.permissions.canEditPlan) { setOperationError(workspace?.permissions.editReason ?? 'Plan editing is not authorized.'); return; }
    change('task', resolved);
  };
  const cacheKey = plan && task ? `${plan.id}:${task}` : '';
  const cacheDraft = useCallback((value: PlanEditorDraft | null) => {
    if (!cacheKey) return;
    if (value) drafts.current.set(cacheKey, value); else drafts.current.delete(cacheKey);
  }, [cacheKey]);
  const acceptPlan = (value: AssessmentPlanWorkspace) => {
    if (!mounted.current) return;
    if (cacheKey) drafts.current.delete(cacheKey);
    const next = new URLSearchParams(params); ['task', 'action', 'preview', 'planRequest'].forEach(key => next.delete(key));
    if (value.plan?.id !== plan?.id && value.plan) next.set('plan', value.plan.id);
    const nextPlanKey = JSON.stringify([systemId, next.get('plan'), accessKey]);
    setPlanState({ key: nextPlanKey, value }); setParams(next, { replace: true });
    setNotice({ warning: false, text: 'Retained plan updated. Planning, collection and report review remain separate.' });
  };
  const openAction = (value: 'create' | 'finalize' | 'collect' | 'sar' | 'import', newRequest = false) => {
    setOperationError(null);
    const next = new URLSearchParams(params);
    ['task', 'preview', 'result', 'sar'].forEach(key => next.delete(key));
    next.set('action', value);
    if (value === 'collect' || value === 'sar' || value === 'create') {
      const requestKey = value === 'collect' ? 'runRequest' : value === 'create' ? 'planRequest' : 'sarRequest';
      if (newRequest || !next.has(requestKey)) next.set(requestKey, crypto.randomUUID());
      actionKey.current = next.get(requestKey);
    } else actionKey.current = crypto.randomUUID();
    setParams(next);
  };
  const execute = async (kind: 'create' | 'finalize' | 'collect' | 'sar') => {
    if (busy || !workspace) return;
    const allowed = kind === 'create' ? workspace.permissions.canCreatePlan
      : kind === 'finalize' ? workspace.permissions.canFinalizePlan && !workspace.finalizationBlockers.length
        : kind === 'collect' ? currentResults && azureReady : currentResults && results?.sarReadiness.canPrepareDraft;
    if (!allowed) { setOperationError('Your current permissions or recorded prerequisites do not allow this action. Refresh the workspace to review its requirements.'); return; }
    setBusy(true); setOperationError(null);
    try {
      if (kind === 'create') {
        const requestId = params.get('planRequest') ?? (actionKey.current ??= crypto.randomUUID());
        acceptPlan(await createAssessmentPlan(systemId, { requestId,
          previousPlanId: plan?.id ?? null, expectedContentHash: plan?.contentHash ?? null }));
      } else if (kind === 'finalize') {
        if (!plan) { setOperationError('No saved plan is selected.'); return; }
        acceptPlan(await finalizeAssessmentPlan(systemId, plan.id, { expectedContentHash: plan.contentHash, expectedRevision: plan.revision }));
      } else if (kind === 'collect') {
        const requestId = params.get('runRequest') ?? actionKey.current;
        if (!requestId) { setOperationError('A collection request identity is required. Close and open this action again.'); return; }
        const value = await collectAssessmentResults(systemId, { planId: plan?.id ?? null, expectedPlanHash: plan?.contentHash ?? null, requestId });
        if (!mounted.current) return;
        const warning = /failed|partial|cancelled/i.test(value.status);
        setNotice({ warning, text: value.message }); setResultsAttempt(v => v + 1);
        const next = new URLSearchParams(params); next.delete('action');
        if (!warning && !/running|pending|inprogress/i.test(value.status)) next.delete('runRequest');
        setParams(next, { replace: true });
      } else {
        const selected = results?.selectedResults ?? [];
        if (!included.length || included.some(id => !selected.some(source => source.id === id))) {
          setOperationError('One or more selected sources are unavailable. Refresh the results before preparing a report.'); return;
        }
        const requestId = params.get('sarRequest') ?? actionKey.current;
        if (!requestId) { setOperationError('A report request identity is required. Close and open this action again.'); return; }
        const value = await prepareAssessmentReport(systemId, { planId: plan?.id ?? null, expectedPlanHash: plan?.contentHash ?? null,
          resultIds: included, expectedResultRevisions: Object.fromEntries(selected.map(source => [source.id, source.revision])),
          requestId, title: `Security Assessment Report — ${workspace.systemName}` });
        if (!mounted.current) return;
        const next = new URLSearchParams(params); next.delete('action'); next.delete('sarRequest'); next.set('sar', value.id);
        setParams(next, { replace: true }); setResultsAttempt(v => v + 1);
      }
    } catch (reason) { if (mounted.current) setOperationError(assessmentWorkspaceError(reason)); }
    finally { if (mounted.current) setBusy(false); }
  };
  const selectForSar = (id: string, checked: boolean) => {
    const next = new URLSearchParams(params); next.delete('include'); next.delete('sarRequest');
    const values = checked ? [...included, id] : included.filter(value => value !== id);
    Array.from(new Set(values)).forEach(value => next.append('include', value));
    setParams(next, { replace: true });
  };
  const firstTask = workspace?.tasks.find(item => !item.complete)?.key ?? 'scope';
  const incomplete = workspace?.tasks.filter(item => !item.complete).length ?? 0;
  const showSearch = Boolean(search || resultsState?.value.totalCount);
  const unsaved = Array.from(drafts.current.keys()).filter(key => key.startsWith(`${plan?.id}:`));
  const awaitingReview = results?.items.filter(item => item.reviewStatus.toLowerCase() !== 'reviewed') ?? [];

  return <section className="aw-page" aria-labelledby="assessment-heading">
    <header className="aw-heading"><h1 ref={heading} tabIndex={-1} id="assessment-heading">{planView ? 'Prepare your assessment plan' : 'Assessments & results'}</h1>
      <p>{planView ? 'Define what will be assessed, how, and by whom.' : 'Collect evidence, review outcomes, and prepare your assessment report.'}</p></header>
    <nav className="aw-workflow" aria-label="Assessment workflow">
      {[['plan', 'Plan'], ['collect', 'Collect results'], ['review', 'Review'], ['sar', 'Prepare SAR']].map(([stage, label], index) =>
        <Link key={stage} to={stageUrl(stage!)} aria-current={activeStage === stage ? 'step' : undefined}><span>{index + 1}</span>{label}</Link>)}
    </nav>
    <p className="aw-stage-note">Stages describe the selected plan and records, not system-wide completion.</p>
    {notice && <p className={notice.warning ? 'aw-warning' : 'aw-info'} role={notice.warning ? 'alert' : 'status'}>{notice.text}</p>}
    {operationError && !action && <p className="aw-error" role="alert">{operationError}</p>}
    {planLoading && <p role="status">Loading retained assessment plan…</p>}
    {planError && <div className="aw-error" role="alert"><p>{planError}</p><button type="button" onClick={() => setPlanAttempt(v => v + 1)}>Retry plan</button>
      {requestedPlan && <button type="button" onClick={() => change('plan', null)}>Use working plan</button>}</div>}
    {workspace && !planLoading && !planError && <>
      {plan ? <section className={`aw-card aw-plan-summary${planView ? '' : ' aw-compact-plan'}`}>
        <div className="aw-card-heading"><div><h2>{planView ? plan.title : 'Linked assessment plan'}</h2>
          <p>{workspace.baselineLevel ?? 'Baseline not recorded'} baseline · <span>{workspace.baselineControlCount} baseline controls</span> · {date(plan.generatedAt)}</p>
          <strong className="aw-scope">{plan.scopeCount} {plan.scopeCount === 1 ? 'control' : 'controls'} in assessment scope</strong>
          <p>Saved revision {plan.revision} · Baseline size is not assessed coverage.</p></div><span className="aw-state-badge">{plan.status}</span></div>
        {!planView && <>
          {incomplete > 0 && <div className="aw-plan-attention"><div><strong>Planning details need attention</strong><p>Review the retained plan and its advisory completeness warnings.</p></div>
            <Link className="aw-primary-link" to={`${stageUrl('plan')}&task=${encodeURIComponent(firstTask)}`}>Continue planning <ArrowRight size={14} /></Link></div>}
          <div className="aw-actions"><button type="button" className="aw-link-button" onClick={() => change('preview', 'saved')}>Preview saved plan</button>
            <Link to={stageUrl('plan')}>View plan and scope →</Link></div>
        </>}
        {planView && <details className="aw-disclosure"><summary>Plan details &amp; history</summary>
          <dl><dt>System</dt><dd>{workspace.systemName}</dd><dt>Lead</dt><dd>{plan.assessmentLead ?? 'Not recorded'}</dd>
            <dt>Last saved</dt><dd>{date(plan.updatedAt ?? plan.generatedAt)}</dd><dt>Finalized</dt><dd>{date(plan.finalizedAt)}</dd>
            <dt>Schedule</dt><dd>{date(plan.scheduleStart)} – {date(plan.scheduleEnd)}</dd></dl>
          <label>Saved assessment plan<select value={plan.id} onChange={event => {
            const next = new URLSearchParams(params); next.set('plan', event.target.value);
            ['task', 'result', 'sar', 'action', 'preview', 'include', 'runRequest', 'sarRequest', 'planRequest'].forEach(key => next.delete(key));
            setParams(next);
          }}>{workspace.plans.map(value => <option key={value.id} value={value.id}>{value.title} · {value.status} · revision {value.revision} · {date(value.generatedAt)}</option>)}</select></label>
          <div className="aw-plan-tools" role="group" aria-label="Saved plan actions">
            {workspace.permissions.canEditPlan && <div className="aw-plan-edit-tools">
              {planActions.map(({ task, label, icon: Icon }) => <button type="button" key={task}
                className={`${systemSecondaryAction} aw-plan-tool`} onClick={() => edit(task)}>
                <Icon size={16} aria-hidden="true" className="shrink-0" /><span>{label}</span>
              </button>)}
            </div>}
            <button type="button" className={`${systemSecondaryAction} aw-plan-tool aw-plan-refresh`}
              disabled={planLoading} onClick={() => setPlanAttempt(v => v + 1)}>
              <RefreshCw size={16} aria-hidden="true" className="shrink-0" /><span>Refresh saved plan</span>
            </button>
          </div>
        </details>}
      </section> : <section className="aw-card"><h2>No saved assessment plan</h2><p>Select a baseline before creating the retained plan. Preliminary collection remains a separate supported workflow.</p>
        {planView ? <><button type="button" className="aw-primary" disabled={!workspace.permissions.canCreatePlan} onClick={() => openAction('create')}>Create assessment plan</button>
          {workspace.permissions.createReason && <p>{workspace.permissions.createReason}</p>}<Link to={`/systems/${systemId}/baseline`}>Review control baseline</Link></>
          : <Link className="aw-primary-link" to={stageUrl('plan')}>Continue planning →</Link>}</section>}

      {planView && plan && <>
        {unsaved.length > 0 && !task && <div className="aw-warning">Unsaved planning changes are retained in this page.
          {unsaved.map(key => <button key={key} type="button" onClick={() => change('task', key.split(':').at(-1) ?? 'approach')}>Resume {key.split(':').at(-1)}</button>)}
          <button type="button" onClick={() => { drafts.current.clear(); setDraftChange(value => value + 1); }}>Discard unsaved changes</button></div>}
        <section className="aw-card"><h2>Complete your plan</h2><p>{incomplete} planning details need attention.</p>
          <div className="aw-checklist">{workspace.tasks.map(item => <div className="aw-checklist-row" key={item.key}>
            {item.complete ? <Check size={18} /> : <Circle size={18} />}
            <strong>{item.title}</strong><span>{item.description}{item.required && <small>Required by the current validation contract.</small>}</span>
            <button type="button" disabled={!workspace.permissions.canEditPlan} onClick={() => edit(item.key)}>{item.actionLabel} <ArrowRight size={14} /></button>
          </div>)}</div>
          <p className="aw-muted">Completeness guidance and finalization blockers are tracked separately.</p>
          {incomplete > 0 && workspace.permissions.canEditPlan && <div className="aw-next-action"><Flag size={20} /><div>
            <strong>Continue with {workspace.tasks.find(item => !item.complete)?.title.toLowerCase()}</strong><small>Save each planning detail, then preview the retained draft.</small>
          </div><button type="button" className="aw-primary" onClick={() => edit(firstTask)}>Continue planning</button></div>}
          {!workspace.permissions.canEditPlan && <p>{workspace.permissions.editReason ?? 'This retained plan is read-only.'}</p>}
        </section>
        {workspace.warnings.length > 0 && <details className="aw-disclosure"><summary>Advisory completeness warnings ({workspace.warnings.length})</summary>
          <ul>{workspace.warnings.map(warning => <li key={warning}>{warning}</li>)}</ul><p>These warnings do not introduce new finalization requirements.</p></details>}
        <div className="aw-plan-footer"><div>{workspace.finalizationBlockers.length ? <ul>{workspace.finalizationBlockers.map(reason => <li key={reason}>{reason}</li>)}</ul>
          : <small>Preview the saved content and review any advisory gaps before finalizing.</small>}</div>
          <button type="button" onClick={() => change('preview', 'saved')}>{plan.status === 'Draft' ? 'Preview saved draft' : 'Preview saved plan'}</button>
          {plan.status === 'Draft' ? <button type="button" className="aw-primary" disabled={!workspace.permissions.canFinalizePlan || !!workspace.finalizationBlockers.length || !!unsaved.length}
            onClick={() => openAction('finalize')}>Finalize plan</button>
            : <button type="button" className="aw-primary" disabled={!workspace.permissions.canCreatePlan} onClick={() => openAction('create')}>Start a new draft revision</button>}
        </div>
      </>}
      {!planView && <>
        {resultsLoading && <p role="status">{results ? 'Refreshing results and readiness; previously loaded rows are shown until verified.' : 'Loading collection access and retained results…'}</p>}
        {resultsError && <div className="aw-error" role="alert"><p>{resultsError}</p><button type="button" onClick={() => setResultsAttempt(v => v + 1)}>Retry results</button>
          {included.length > 0 && <button type="button" onClick={() => {
            const next = new URLSearchParams(params); next.delete('include'); next.delete('sarRequest'); setParams(next, { replace: true });
          }}>Clear report selection and retry</button>}</div>}
        {results && !resultsError && awaitingReview.length > 0 && <div className="aw-next-action">
          <FileText size={20} /><div><strong>{awaitingReview.length} {awaitingReview.length === 1 ? 'result set' : 'result sets'} on this page need review</strong>
            <small>Inspect observations, findings and scope before preparing the report.</small></div>
          <button type="button" className="aw-primary" onClick={() => change('result', awaitingReview[0]!.id)}>Review results</button>
        </div>}
        {results && !resultsError && <section className="aw-card" id="assessment-collect"><h2>Collect assessment results</h2>
          <p>Run configured checks or import results, then review what the evidence establishes.</p>
          <div className="aw-collection-path"><Cloud size={22} /><div><h3>Azure checks</h3><p>{results.collection.azure.message}</p>
            <small>{results.collection.runReason}</small>
            <details><summary>Scope and access requirements</summary>
              <p>{results.collection.azure.scopeDescription.length ? results.collection.azure.scopeDescription.join('; ') : 'Assessment execution scope is not available.'}</p>
              <ul>{results.collection.azure.subscriptions.map(subscription => <li key={subscription.id}>{subscription.name}</li>)}</ul>
              <p>Readiness checked: {results.collection.azure.checkedAt ? new Date(results.collection.azure.checkedAt).toLocaleString() : 'Not verified'}</p>
              {results.collection.configurationReason && <p>{results.collection.configurationReason}</p>}
              <button type="button" disabled={resultsLoading} onClick={() => setResultsAttempt(v => v + 1)}>Refresh readiness</button>
            </details></div><button type="button" disabled={!azureReady || resultsLoading || busy} onClick={() => {
              if (!azureReady) { setOperationError(results.collection.runReason ?? results.collection.azure.message); return; } openAction('collect');
            }}>Run Azure checks</button></div>
          <div className="aw-collection-path"><Upload size={22} /><div><h3>Import results</h3><p>Supported formats: {results.collection.importFormats.join(', ') || 'None exposed by this service'}.</p>
            {results.collection.importReason && <small>{results.collection.importReason}</small>}</div>
            <button type="button" disabled={!results.collection.canImport || resultsLoading} onClick={() => {
              if (!results.collection.canImport) { setOperationError(results.collection.importReason); return; } openAction('import');
            }}>View import options</button></div>
          {results.collection.canConfigureAzure && <Link to={`/systems/${systemId}/assessments/environment`}>Configure Azure assessment →</Link>}
          {params.has('runRequest') && !busy && <div className="aw-info"><p>A previous collection request is retained. Retrying reuses that request; inspect its outcome before starting another run.</p>
            <button type="button" disabled={!azureReady || resultsLoading} onClick={() => openAction('collect', true)}>Start a new collection request</button></div>}
          <p className="aw-muted">Examine, Interview and Test remain available in planning and authorized control review. Preliminary collection is not formal assessment acceptance.</p>
        </section>}
        {showSearch && <label className="aw-result-search">Search result sets<input value={search} onChange={event => change('search', event.target.value, true)} /></label>}
        {results && !resultsError && <section className="aw-card" id="assessment-review" aria-busy={resultsLoading}>
          {!results.items.length ? <div className="aw-empty"><FileText size={30} /><h2>{search ? 'No matching result sets' : 'No results yet'}</h2>
            <p>{search ? 'Adjust your search to find retained results.' : 'Results will appear here after collection or import. Review is a separate step.'}</p></div>
            : <><h2>Assessment results</h2><p>Choose retained sources for reporting and open each result to review observations and findings.</p>
              <div className="aw-table-scroll"><table aria-label="Assessment result sets"><thead><tr><th>For SAR</th><th>Result set / source</th><th>Collection</th><th>Review</th><th>Date</th><th>Action</th></tr></thead>
                <tbody>{results.items.map(item => <tr key={item.id}><td><input type="checkbox" aria-label={`Include ${item.name} in SAR`}
                  checked={included.includes(item.id)} onChange={event => selectForSar(item.id, event.target.checked)} /></td>
                  <td><strong>{item.name}</strong><small>{item.source} · {item.method}</small>{item.requiresReconciliation && <small>Plan reconciliation needed</small>}</td>
                  <td>{statusLabel(item.collectionStatus)}</td><td>{statusLabel(item.reviewStatus)}</td><td>{date(item.recordedAt)}</td><td>
                    <button type="button" aria-label={`Open result ${item.name}`} onClick={() => change('result', item.id)}>Open</button></td></tr>)}</tbody></table></div>
              <div className="aw-record-footer"><span>{results.totalCount} matching {results.totalCount === 1 ? 'result set' : 'result sets'}</span>
                {results.totalCount > results.pageSize && <nav className="aw-pager" aria-label="Result pages">
                  <button type="button" disabled={page <= 1} onClick={() => change('page', String(page - 1))}>Previous</button><span>{page} of {Math.ceil(results.totalCount / results.pageSize)}</span>
                  <button type="button" disabled={page * results.pageSize >= results.totalCount} onClick={() => change('page', String(page + 1))}>Next</button></nav>}
              </div>
            </>}
        </section>}
        {results && !resultsError && <section className="aw-card" id="assessment-sar"><h2>Prepare assessment report</h2>
          <p>Review selected results, findings and coverage before preparing a draft SAR.</p>
          <p>{results.sarReadiness.selectedResultIds.length} {results.sarReadiness.selectedResultIds.length === 1 ? 'result set' : 'result sets'} selected · {results.sarReadiness.reviewedControlCount} {results.sarReadiness.reviewedControlCount === 1 ? 'control' : 'controls'} with review information.</p>
          <div className="aw-actions"><button type="button" onClick={() => openAction('sar')}>View report readiness</button>
            <button type="button" className="aw-primary" disabled={!results.sarReadiness.canPrepareDraft || resultsLoading || !included.length}
              onClick={() => openAction('sar')}>Prepare draft SAR</button></div>
          <small>Draft preparation is separate from report approval, package export, eMASS submission and authorization.</small>
          {results.reports.length > 0 && <details className="aw-disclosure"><summary>Retained reports</summary>
            {results.reports.map(value => <div className="aw-retained-report" key={value.id}><span>{value.title} · {value.status} · {date(value.createdAt)}</span>
              <button type="button" onClick={() => change('sar', value.id)}>Open retained SAR</button></div>)}</details>}
        </section>}
      </>}
    </>}
    <details className="aw-disclosure aw-package-help"><summary>{planView ? 'How this supports your assessment package' : 'How SAP and SAR work together'}</summary>
      <p>The SAP retains the assessment plan. Collected observations and human determinations remain separate and feed the selected-source SAR.</p>
      <p>Plan finalization does not mean evidence has been collected or controls have passed. A generated SAR is not a reviewed report, an exported package, an eMASS submission or an AO decision.</p>
      <p>Inspect retained document sources before package preparation. Separate downstream exporters may not yet preserve all guided plan/result pins.</p>
    </details>
    {workspace?.plan && task && planView && <AssessmentPlanEditor key={`${workspace.plan.id}:${task}`}
      systemId={systemId} workspace={{ ...workspace, plan: workspace.plan }} task={task} restored={drafts.current.get(cacheKey)}
      onDraft={cacheDraft} onClose={close} onSaved={acceptPlan} onReload={() => { close(); setPlanAttempt(v => v + 1); }} />}
    {plan && params.has('preview') && <SavedPlanPreview systemId={systemId} planId={plan.id} onClose={close} />}
    {workspace && !planView && selectedResult && !action && <AssessmentResultDrawer key={`${systemId}:${selectedResult}`} systemId={systemId} id={selectedResult}
      plan={plan} onClose={close} onChanged={() => setResultsAttempt(v => v + 1)}
      onTask={finding => { close(); setTaskFinding(finding); }} onDeviation={finding => { close(); setDeviationFinding(finding); }} />}
    {reportId && <RetainedSarPreview key={reportId} systemId={systemId} reportId={reportId} onClose={close} />}
    {action === 'import' && results?.collection.canImport && <ScanImportDialog systemId={systemId} planId={plan?.id}
      expectedPlanHash={plan?.contentHash} canImport={results.collection.canImport} onClose={close} onImportComplete={() => { close(); setResultsAttempt(v => v + 1); }} />}
    {action && action !== 'import' && workspace && <SetupDialog placement="right"
      title={action === 'create' ? 'Create assessment draft' : action === 'finalize' ? 'Finalize assessment plan' : action === 'collect' ? 'Collect Azure results' : 'SAR readiness'}
      description={workspace.systemName} busy={busy} onClose={close}><div className="aw-dialog">
        {operationError && <div className="aw-error" role="alert"><p>{operationError}</p>
          {(action === 'create' || action === 'finalize') && <button type="button" disabled={busy} onClick={() => { close(); setPlanAttempt(v => v + 1); }}>Reload saved plan</button>}</div>}
        {action === 'create' && <><p>Create the next working draft using the existing SAP service. An existing draft is reused; finalized records stay unchanged.</p>
          {workspace.permissions.createReason && <p>{workspace.permissions.createReason}</p>}</>}
        {action === 'finalize' && <><p>Finalize saved revision {plan?.revision}. This locks the retained plan, not the assessment results or an authorization decision.</p>
          <h3>Advisory completeness</h3><ul>{workspace.warnings.map(warning => <li key={warning}>{warning}</li>)}</ul>
          <p>Warnings remain advisory under the existing service rules.</p>
          {!!workspace.finalizationBlockers.length && <ul className="aw-warning">{workspace.finalizationBlockers.map(reason => <li key={reason}>{reason}</li>)}</ul>}</>}
        {action === 'collect' && <><p>Run checks only within the server-validated configured scope.</p>
          <p>{plan ? `Record collection against ${plan.title}, saved revision ${plan.revision} (${plan.status}).` : 'This is preliminary collection without a retained plan link.'}</p>
          <p>No controls are approved by starting or completing a run. Partial work and errors remain visible.</p>
          <p>{results?.collection.runReason}</p></>}
        {action === 'sar' && results && <>
          <h3>Selected retained sources</h3><ul>{results.selectedResults.map(source => <li key={source.id}>{source.name}</li>)}</ul>
          {!included.length && <p>Select result sets in the results list before preparing a report.</p>}
          <p>Selected plan scope: {results.sarReadiness.scopeCount ?? 'Not recorded'} controls. Observed: {results.sarReadiness.observedControlCount}.
            Review information: {results.sarReadiness.reviewedControlCount}.</p>
          {results.sarReadiness.blockers.length > 0 && <><h3>Current blockers</h3><ul className="aw-warning">{results.sarReadiness.blockers.map(reason => <li key={reason}>{reason}</li>)}</ul></>}
          {results.sarReadiness.warnings.length > 0 && <><h3>Advisory readiness gaps</h3><ul>{results.sarReadiness.warnings.map(reason => <li key={reason}>{reason}</li>)}</ul></>}
          {results.sarReadiness.missingControlIds.length > 0 && <details className="aw-disclosure"><summary>Controls without observations</summary>
            <p>{results.sarReadiness.missingControlIds.join(', ')}</p></details>}
          <p>A draft can record gaps where supported. It does not approve the report or authorize the system.</p>
        </>}
        <footer className="aw-dialog-footer"><button type="button" disabled={busy} onClick={close}>Cancel</button>
          <button type="button" className="aw-primary" disabled={busy || (action === 'create' ? !workspace.permissions.canCreatePlan
            : action === 'finalize' ? !workspace.permissions.canFinalizePlan || !!workspace.finalizationBlockers.length
              : action === 'collect' ? !azureReady : !results?.sarReadiness.canPrepareDraft || !included.length)}
            onClick={() => { if (action === 'create' || action === 'finalize' || action === 'collect' || action === 'sar') void execute(action); }}>
            {busy ? 'Working…' : action === 'create' ? 'Create draft' : action === 'finalize' ? 'Finalize saved plan' : action === 'collect' ? 'Start scoped checks' : 'Prepare selected-source SAR'}
          </button></footer>
      </div></SetupDialog>}
    {taskFinding && <CreateRemediationTaskModal systemId={systemId} canCreateFromFinding findingId={taskFinding.findingId}
      findingTitle={taskFinding.title} findingSeverity={taskFinding.severity} onClose={() => setTaskFinding(null)} onCreated={() => setResultsAttempt(v => v + 1)} />}
    {deviationFinding && <AddDeviationDialog systemId={systemId} initialFindingId={deviationFinding.findingId}
      initialControlId={deviationFinding.controlId ?? undefined} initialTitle={deviationFinding.title}
      onClose={() => setDeviationFinding(null)} onCreated={() => { setDeviationFinding(null); setResultsAttempt(v => v + 1); }} />}
  </section>;
}
