import { useCallback, useEffect, useRef, useState } from 'react';
import { AlertTriangle, CheckCircle2, CircleHelp, FileText, RefreshCw } from 'lucide-react';
import { Link, useLocation, useNavigate } from '../workspaces/workspaceNavigation';
import {
  getPackageReadinessWorkspace, getPackageReadinessRun, getPackageReadinessCheck,
  listPackageReadinessRuns, validatePackageReadiness, generatePackageFromReadiness,
  type PackageReadinessWorkspace, type PackageReadinessRun, type PackageReadinessCheck,
  type PackageReadinessSelection, type PackageReadinessHistoryResponse, type PackageReadinessAction,
  type PackageReadinessProgress,
} from '../../api/packageReadiness';
import type { PackagePurpose, RetainedPackageSelection } from '../../api/package';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import RetainedPackageContext from '../../components/RetainedPackageContext';
import SetupDialog from '../workspace-operations/SetupDialog';
import ReadinessPurposeControl from './ReadinessPurposeControl';
import { packagePurposeFromSearch, packagePurposeLabels, packageSourceHref } from './packageReadinessNavigation';
import { systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';
import './packageReadiness.css';

const outcomeLabels = { Passed: 'Passed', Blocking: 'Blocking check', FollowUp: 'Follow-up',
  NotApplicable: 'Not applicable', Unavailable: 'Unable to verify' };
const milestoneLabels = { prepare: 'Prepare package', validate: 'Validate package', export: 'Export',
  emass: 'eMASS submission', decision: 'AO decision' };
const stateLabels = { NotChecked: 'Not checked', Ready: 'Ready', Blocked: 'Needs work', Stale: 'Out of date',
  Failed: 'Failed', Recorded: 'Recorded', NotRecorded: 'Not recorded', Unavailable: 'Unavailable' };
function errorMessage(reason: unknown) {
  if (reason instanceof Error) return reason.message;
  if (reason && typeof reason === 'object' && 'error' in reason && typeof reason.error === 'string') return reason.error;
  return 'Package readiness is unavailable. Retry after access and connectivity are restored.';
}
function date(value: string) { return new Date(value).toLocaleString(); }
function isBlocking(check: PackageReadinessCheck) { return check.outcome === 'Blocking' || check.outcome === 'Unavailable' && check.required; }

export default function PackageReadinessExperience({ systemId }: { systemId: string }) {
  const location = useLocation();
  const purpose = packagePurposeFromSearch(location.search);
  const workspace = useWorkspaceSession();
  const scopeKey = JSON.stringify([systemId, workspace?.identity.oid, workspace?.roles, purpose]);
  if (!purpose) return <p role="alert">Unsupported package purpose. <Link className="underline"
    to={`/systems/${encodeURIComponent(systemId)}/documents`}>Choose a supported package purpose</Link>.</p>;
  const rawContext = new URLSearchParams(location.search).get('context');
  if (rawContext) {
    let parsed: unknown;
    try { parsed = JSON.parse(rawContext); } catch { parsed = null; }
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed))
      return <p role="alert">Invalid retained source selection. <Link className="underline"
        to={`/systems/${encodeURIComponent(systemId)}/documents?purpose=${purpose}`}>Select retained sources again</Link>.</p>;
  }
  return <ReadinessWorkspace key={scopeKey} systemId={systemId} purpose={purpose} />;
}

function ReadinessWorkspace({ systemId, purpose }: { systemId: string; purpose: PackagePurpose }) {
  const location = useLocation();
  const navigate = useNavigate();
  const params = new URLSearchParams(location.search);
  const selectedRunId = params.get('run');
  const selectedCheckId = params.get('check');
  const activeTab = params.get('checks') === 'follow-up' ? 'follow-up' : params.get('checks') === 'all' ? 'all' : 'blocking';
  const [context, setContext] = useState<RetainedPackageSelection | null>(() => {
    const raw = params.get('context');
    if (!raw) return null;
    try { return JSON.parse(raw); } catch { return null; }
  });
  const contextKey = JSON.stringify(context);
  const selection: PackageReadinessSelection = { purpose, retainedContext: context };
  const needsContext = (purpose === 'AuthorizedBaselineArchive' || purpose === 'ChangeSubmission') && !context;
  const [workspace, setWorkspace] = useState<PackageReadinessWorkspace | null>(null);
  const [run, setRun] = useState<PackageReadinessRun | null>(null);
  const [checks, setChecks] = useState<PackageReadinessCheck[]>([]);
  const [detail, setDetail] = useState<PackageReadinessCheck | null>(null);
  const [loading, setLoading] = useState(!needsContext);
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [commandError, setCommandError] = useState<string | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [page, setPage] = useState(0);
  const [history, setHistory] = useState<PackageReadinessHistoryResponse | null>(null);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [historyOffset, setHistoryOffset] = useState(0);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [generationOpen, setGenerationOpen] = useState(false);
  const [generating, setGenerating] = useState(false);
  const request = useRef<AbortController | null>(null);
  const command = useRef<AbortController | null>(null);
  const checkTrigger = useRef<HTMLElement | null>(null);
  const recheckButton = useRef<HTMLButtonElement>(null);
  const restoreCheckFocus = useRef(false);
  const base = `/systems/${encodeURIComponent(systemId)}`;
  const updateQuery = (changes: Record<string, string | null>, replace = false) => {
    const query = new URLSearchParams(location.search);
    for (const [key, value] of Object.entries(changes)) value === null ? query.delete(key) : query.set(key, value);
    navigate(`${base}/documents${query.size ? `?${query}` : ''}`, { replace });
  };
  const readChecks = useCallback(async (id: string, signal: AbortSignal) => {
    const selected = { purpose, retainedContext: context };
    const first = await getPackageReadinessRun(systemId, id, selected, {}, signal);
    const items = [...first.checks.items];
    for (let offset = items.length; offset < first.checks.totalCount; offset = items.length) {
      const next = await getPackageReadinessRun(systemId, id, selected, { offset }, signal);
      if (!next.checks.items.length || next.run.sourceHash !== first.run.sourceHash) throw new Error('The retained check list could not be read consistently.');
      items.push(...next.checks.items);
    }
    return { run: first.run, items };
  }, [systemId, purpose, contextKey]);
  const load = useCallback(async () => {
    if (needsContext || command.current) return;
    request.current?.abort();
    const controller = new AbortController(); request.current = controller;
    setLoading(true); setError(null); setWorkspace(null); setRun(null); setChecks([]);
    try {
      const next = await getPackageReadinessWorkspace(systemId, { purpose, retainedContext: context }, controller.signal);
      const id = selectedRunId ?? next.latestRun?.id;
      const result = id ? await readChecks(id, controller.signal) : null;
      if (controller.signal.aborted) return;
      setWorkspace(next); setRun(result?.run ?? null); setChecks(result?.items ?? []);
    } catch (reason) { if (!controller.signal.aborted) setError(errorMessage(reason)); }
    finally { if (!controller.signal.aborted) { setLoading(false); request.current = null; } }
  }, [systemId, purpose, contextKey, needsContext, selectedRunId, readChecks, attempt]);
  useEffect(() => {
    void load();
    const refresh = () => { if (document.visibilityState === 'visible' && !command.current) void load(); };
    window.addEventListener('focus', refresh);
    return () => { request.current?.abort(); window.removeEventListener('focus', refresh); };
  }, [load]);
  useEffect(() => () => { command.current?.abort(); }, []);
  useEffect(() => { setPage(0); }, [run?.id]);
  useEffect(() => {
    if (selectedCheckId || !restoreCheckFocus.current || loading) return;
    restoreCheckFocus.current = false;
    if (checkTrigger.current?.isConnected) checkTrigger.current.focus();
    else recheckButton.current?.focus();
  }, [selectedCheckId, loading]);
  useEffect(() => {
    setDetail(null); setDetailError(null);
    if (!selectedCheckId || !run) return;
    const controller = new AbortController();
    void getPackageReadinessCheck(systemId, run.id, selectedCheckId, selection, controller.signal)
      .then(result => { if (!controller.signal.aborted) setDetail(result.check); })
      .catch(reason => { if (!controller.signal.aborted) setDetailError(errorMessage(reason)); });
    return () => controller.abort();
  }, [systemId, purpose, contextKey, selectedCheckId, run?.id, attempt]);
  useEffect(() => {
    if (!historyOpen) return;
    const controller = new AbortController(); setHistory(null); setHistoryError(null);
    void listPackageReadinessRuns(systemId, selection, { offset: historyOffset }, controller.signal)
      .then(value => { if (!controller.signal.aborted) setHistory(value); })
      .catch(reason => { if (!controller.signal.aborted) setHistoryError(errorMessage(reason)); });
    return () => controller.abort();
  }, [systemId, purpose, contextKey, historyOpen, historyOffset, attempt]);

  const validate = async () => {
    if (command.current || !workspace?.permissions.canValidate || needsContext) return;
    request.current?.abort(); const controller = new AbortController(); command.current = controller;
    setChecking(true); setError(null); setCommandError(null);
    try {
      const result = await validatePackageReadiness(systemId, selection, controller.signal);
      if (controller.signal.aborted) return;
      setRun(result.run);
      updateQuery({ run: result.run.id, check: null }, true);
    } catch (reason) { if (!controller.signal.aborted) { setCommandError(errorMessage(reason)); setRun(null); setChecks([]); } }
    finally { if (!controller.signal.aborted) { command.current = null; setChecking(false); setAttempt(value => value + 1); } }
  };
  const stale = run?.freshness.state === 'Stale' || run?.outcome === 'SourceChanged';
  const currentReady = workspace?.source.state === 'Available' && !stale && run?.freshness.state === 'Current' && run.outcome === 'Ready';
  const canGenerate = currentReady && !commandError && workspace?.permissions.canGenerate && !!run?.sourceHash;
  const status = checking ? 'Checking package readiness…' : loading ? 'Loading package readiness…'
    : commandError ? 'Validation request failed'
      : error || workspace?.source.state === 'Unavailable' || run?.freshness.state === 'Unavailable' ? 'Readiness unavailable'
      : !run ? 'Readiness not checked' : stale ? 'Readiness result is out of date'
        : run.outcome === 'Failed' ? 'Validation failed' : currentReady ? 'Package ready for export' : 'Package needs work';
  const recommended = checks.find(check => check.id === run?.recommendedCheckId);
  const filtered = checks.filter(check => activeTab === 'all' || (activeTab === 'blocking' ? isBlocking(check)
    : check.outcome === 'FollowUp' || check.outcome === 'Unavailable' && !check.required));
  const visible = filtered.slice(page * 20, page * 20 + 20);
  const openCheck = (id: string) => {
    if (!run) return;
    checkTrigger.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    updateQuery({ run: run.id, check: id });
  };
  const closeCheck = () => { restoreCheckFocus.current = true; updateQuery({ check: null }, true); };
  const sourceLink = (action: PackageReadinessAction) => {
    const query = new URLSearchParams(location.search);
    query.set('purpose', purpose);
    if (run) query.set('run', run.id);
    return action.canView && action.path ? packageSourceHref(systemId, action.path, `?${query}`) : null;
  };
  const packageLink = (path: string, tab?: string) => {
    const query = new URLSearchParams({ purpose });
    if (context) query.set('context', JSON.stringify(context));
    if (run) query.set('run', run.id);
    if (tab) query.set('tab', tab);
    return `${base}/${path}?${query}`;
  };
  const exportPackage = async () => {
    if (!canGenerate || !run?.sourceHash || command.current) return;
    const controller = new AbortController(); command.current = controller; setGenerating(true); setCommandError(null);
    try {
      const receipt = await generatePackageFromReadiness(systemId, { ...selection, readinessRunId: run.id,
        expectedSourceHash: run.sourceHash, evidenceMode: 'Embedded', includeEvidence: true }, controller.signal);
      if (!controller.signal.aborted) navigate(`${base}/documents?${new URLSearchParams({
        purpose, tab: 'exports', packageId: receipt.packageId, ...(context ? { context: JSON.stringify(context) } : {}),
      })}`);
    } catch (reason) { if (!controller.signal.aborted) setCommandError(errorMessage(reason)); }
    finally { if (!controller.signal.aborted) { command.current = null; setGenerating(false); setGenerationOpen(false); setAttempt(value => value + 1); } }
  };
  return <div className="package-readiness space-y-4">
    <header className="flex flex-wrap items-start justify-between gap-4">
      <div><h1 className="text-[27px] font-bold tracking-tight">Your path to ATO submission</h1>
        <p className="mt-1 text-sm text-slate-500">See what is documented, what needs review, and what to do next.</p></div>
      <ReadinessPurposeControl purpose={purpose} disabled={checking || generating} onChange={value => {
        updateQuery({ purpose: value, run: null, check: null, context: null, checks: null });
      }} />
    </header>
    {(purpose === 'AuthorizedBaselineArchive' || purpose === 'ChangeSubmission') && <details open={!context}
      className="rounded-lg border border-slate-200 bg-white p-3 dark:bg-slate-900">
      <summary className="cursor-pointer text-sm font-semibold">Retained source selection</summary>
      <RetainedPackageContext systemId={systemId} purpose={purpose} disabled={checking || generating} onChange={value => {
        setContext(value); updateQuery({ context: value ? JSON.stringify(value) : null, run: null, check: null });
      }} />
      {context && <p className="text-xs text-slate-500">Selected baseline: {context.baselinePackageId}. Retained source hashes are verified by the server.</p>}
    </details>}
    {needsContext && <p role="status" className="rounded-lg bg-amber-50 p-4 text-sm text-amber-900">Select retained source versions before checking this purpose. No readiness result is assumed.</p>}
    <div className="grid gap-4 lg:grid-cols-[1.4fr_1fr]">
      <section aria-label="Package validation outcome" className={`readiness-card ${currentReady && !error && !commandError ? 'border-emerald-200 bg-emerald-50 dark:bg-emerald-950' : 'border-amber-200 bg-amber-50 dark:bg-amber-950'}`}>
        <div className="flex gap-3"><span aria-hidden="true" className="mt-1">{currentReady && !error && !commandError ? <CheckCircle2 /> : <AlertTriangle />}</span>
          <div className="min-w-0"><h2 role="status" className="text-xl font-bold">{status}</h2>
            {run && !checking && !error && <p className="mt-1 text-sm">
              {run.counts.blocking + run.counts.requiredUnavailable} blocking checks · {run.counts.followUp + run.counts.unavailable - run.counts.requiredUnavailable} follow-up items
            </p>}
            {run && <p className="mt-2 text-xs text-slate-600">Checked {date(run.evaluatedAt)} · Snapshot {run.sourceHash?.slice(0, 12) ?? 'Unavailable'}</p>}
            {run && workspace?.latestRun && workspace.latestRun.id !== run.id && <p className="mt-2 text-xs font-semibold">
              Viewing a historical validation run. Package progress reflects the currently retained records.
              {' '}<button type="button" className="underline" onClick={() => updateQuery({ run: null, check: null })}>View latest result</button>
            </p>}
            <div className="mt-3 flex flex-wrap gap-2">
              <button ref={recheckButton} type="button" disabled={loading || checking || needsContext || !workspace?.permissions.canValidate}
                onClick={() => void validate()} className={systemPrimaryAction}><RefreshCw size={14} aria-hidden="true" className="mr-2" />{checking ? 'Checking…' : run ? 'Recheck readiness' : 'Check readiness'}</button>
              {recommended && <button type="button" disabled={checking} className={systemSecondaryAction} onClick={() => openCheck(recommended.id)}>View check details</button>}
              <button type="button" disabled={!workspace || checking || needsContext} className="text-xs text-indigo-700 underline dark:text-indigo-300" onClick={() => setHistoryOpen(true)}>Validation history</button>
            </div>
            {workspace?.permissions.validateReason && <p className="mt-2 text-xs">{workspace.permissions.validateReason}</p>}
            {workspace?.source.state === 'Unavailable' && <p className="mt-2 text-xs text-amber-900 dark:text-amber-200">
              {workspace.source.reason ?? 'The evaluated source identity is unavailable. Restore access and recheck readiness.'}
            </p>}
          </div></div>
      </section>
      <section className="readiness-card bg-white dark:bg-slate-900"><h2 className="text-sm font-semibold">Recorded RMF phase</h2>
        <p className="mt-2 text-sm font-semibold text-indigo-700 dark:text-indigo-300">{workspace?.rmf.phase ?? 'Unavailable'}</p>
        <p className="mt-2 text-xs text-slate-500">Package progress and recorded RMF phases are tracked separately.</p>
        <details className="mt-2 text-xs"><summary className="cursor-pointer text-indigo-700 underline">View phase history</summary>
          {workspace?.rmf.transitions.map(item => <p key={item.id} className="mt-2">{item.fromPhase} → {item.toPhase} · {date(item.occurredAt)} · {item.actor}</p>)}
          {workspace && workspace.rmf.totalCount === 0 && <p className="mt-2">No phase transitions recorded.</p>}
          <Link className="mt-2 inline-block underline" to={`${base}/history`}>View retained activity history</Link>
        </details>
      </section>
    </div>
    {(error || commandError) && <div role="alert" className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-800">{commandError ?? error}
      <button type="button" className="ml-3 underline" onClick={() => setAttempt(value => value + 1)}>Retry readiness data</button></div>}
    {run?.failure && <p role="alert" className="text-sm text-red-700">{run.failure.message}</p>}
    {stale && <section aria-label="Stale validation" className="readiness-card border-indigo-200 bg-indigo-50 text-sm dark:bg-indigo-950">
      <h2 className="font-semibold">Records changed after validation</h2>
      <p className="mt-1">{run?.freshness.reason ?? 'This retained result no longer evaluates the current records. Recheck before relying on readiness.'}</p>
    </section>}
    {workspace && <section className="readiness-card bg-white dark:bg-slate-900">
      <h2 className="text-sm font-semibold">Package progress <span className="font-normal text-slate-500">(not RMF phases)</span></h2>
      <ol className="readiness-milestones mt-4 grid gap-4 sm:grid-cols-5">{workspace.progress.map((milestone, index) =>
        <li key={milestone.id} className="min-w-0 text-xs">
          <span className={`relative z-10 mb-2 inline-flex h-6 w-6 items-center justify-center rounded-full border ${
            milestone.state === 'Ready' || milestone.state === 'Recorded' ? 'border-indigo-600 bg-indigo-600 text-white'
              : milestone.state === 'Blocked' || milestone.state === 'Failed' ? 'border-amber-500 bg-amber-50 text-amber-900'
                : 'border-slate-300 bg-slate-50 text-slate-600'}`}>{index + 1}</span>
          <h3 className="font-semibold">{milestoneLabels[milestone.id]}</h3>
          <p className="mt-1">{stateLabels[milestone.state]}</p><p className="mt-1 text-slate-500">{milestone.description}</p>
          <ProgressRecords milestone={milestone} sourceLink={sourceLink} />
        </li>)}</ol>
    </section>}
    {recommended && !error && !commandError && <section className="readiness-card flex flex-wrap items-center justify-between gap-3 border-indigo-200 bg-indigo-50 dark:bg-indigo-950">
      <div className="min-w-0"><p className="text-xs font-medium text-indigo-700">Recommended next step</p>
        <h2 className="mt-1 font-semibold">{recommended.title}</h2>
        <p className="mt-1 text-xs text-slate-500">{recommended.missingSource ?? recommended.why}</p></div>
      {sourceLink(recommended.action) ? <Link className={systemPrimaryAction} to={sourceLink(recommended.action)!}>
        {recommended.action.canEdit ? 'Open source workflow' : 'View source record'} →</Link>
        : <button type="button" disabled={checking} className={systemPrimaryAction} onClick={() => openCheck(recommended.id)}>Inspect next task →</button>}
    </section>}
    {run && !error && <section className="overflow-hidden rounded-lg border border-slate-200 bg-white dark:border-slate-700 dark:bg-slate-900">
      <nav aria-label="Package readiness checks" role="tablist" className="flex gap-5 overflow-x-auto border-b border-slate-200 px-4"
        onKeyDown={event => {
          if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
          const tabs = [...event.currentTarget.querySelectorAll<HTMLButtonElement>('button')];
          const current = tabs.indexOf(document.activeElement as HTMLButtonElement);
          const target = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : (current + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
          event.preventDefault(); tabs[target]?.click(); tabs[target]?.focus();
        }}>
        {([['blocking', `Blocking checks (${run.counts.blocking + run.counts.requiredUnavailable})`],
          ['follow-up', `Follow-up (${run.counts.followUp + run.counts.unavailable - run.counts.requiredUnavailable})`],
          ['all', `All checks (${run.counts.total})`]] as const).map(([key, label]) =>
          <button type="button" key={key} role="tab" id={`readiness-${key}`} aria-selected={activeTab === key}
            aria-controls="readiness-check-panel" tabIndex={activeTab === key ? 0 : -1}
            className={`whitespace-nowrap border-b-2 py-3 text-xs ${activeTab === key ? 'border-indigo-600 font-semibold text-indigo-700' : 'border-transparent text-slate-500'}`}
            onClick={() => { setPage(0); updateQuery({ checks: key }); }}>{label}</button>)}
      </nav>
      <div role="tabpanel" id="readiness-check-panel" aria-labelledby={`readiness-${activeTab}`} className="relative overflow-x-auto">
        <table className="w-full text-left text-xs"><thead><tr><th className="p-3">Check</th><th className="p-3">Responsible person / role</th><th className="p-3">Action</th></tr></thead>
          <tbody className="divide-y divide-slate-100 dark:divide-slate-700">{visible.map(check =>
            <tr key={check.id} className={selectedCheckId === check.id ? 'bg-indigo-50 dark:bg-indigo-950' : ''}>
              <td className="p-3"><button type="button" className="text-left font-semibold" onClick={() => openCheck(check.id)}>{check.title}</button>
                <p className="mt-1 text-slate-500">{outcomeLabels[check.outcome]} · {check.missingSource ?? check.why}</p></td>
              <td className="p-3">{check.recordedOwner?.displayName ?? 'Not recorded'}
                {check.expectedRole && <p className="mt-1 text-slate-500">Workflow role: {check.expectedRole}</p>}</td>
              <td className="p-3"><button type="button" className="whitespace-nowrap rounded border border-indigo-200 px-2 py-1 text-indigo-700 dark:text-indigo-300"
                onClick={() => openCheck(check.id)}>View details →</button>
                {sourceLink(check.action) && <Link className="mt-2 block text-indigo-700 underline dark:text-indigo-300"
                  to={sourceLink(check.action)!}>{check.action.canEdit ? 'Open source' : 'View source'}</Link>}</td>
            </tr>)}
            {!visible.length && <tr><td colSpan={3} className="p-4 text-slate-500">No checks in this group. Other results and overall freshness remain separate.</td></tr>}
          </tbody></table>
      </div>
      {filtered.length > 20 && <nav aria-label="Check pages" className="flex justify-between p-3 text-xs">
        <button disabled={page === 0} onClick={() => setPage(value => value - 1)}>Previous checks</button>
        <span>{page + 1} / {Math.ceil(filtered.length / 20)}</span>
        <button disabled={(page + 1) * 20 >= filtered.length} onClick={() => setPage(value => value + 1)}>Next checks</button>
      </nav>}
    </section>}
    {workspace && <section className="readiness-card bg-white dark:bg-slate-900">
      <details><summary className="flex cursor-pointer items-center gap-2 text-sm font-semibold"><FileText size={16} aria-hidden="true" />Package documents &amp; supporting records</summary>
        <p className="mt-2 text-xs text-slate-500">Presence, review, freshness and validation are separate. Narrative counts do not prove implemented controls; a responsibility matrix does not confirm inheritance.</p>
        {workspace.documents.map(document => <details key={document.kind} className="mt-3 border-t border-slate-100 pt-3 text-xs dark:border-slate-700">
          <summary className="flex cursor-pointer flex-wrap justify-between gap-2"><strong>{document.title}</strong><span>{document.presence} · {document.status ?? 'Status not recorded'}</span></summary>
          <dl className="mt-2 grid gap-2 sm:grid-cols-2">
            <div><dt>Review</dt><dd>{document.reviewState ?? 'Not recorded'}</dd></div>
            <div><dt>Source freshness</dt><dd>{document.sourceState ?? 'Not evaluated'}</dd></div>
            <div><dt>Validation</dt><dd>{document.validationOutcome ? outcomeLabels[document.validationOutcome] : 'Not evaluated'}</dd></div>
            <div><dt>Recorded items</dt><dd>{document.recordCount ?? 'Unavailable'}</dd></div>
          </dl>
          {document.records.map(record => <p key={`${record.kind}:${record.id}`} className="mt-2 break-all">{record.status ?? record.kind} · {record.id} · {record.sourceRelationship}</p>)}
          {sourceLink(document.action) && <Link className="mt-2 inline-block text-indigo-700 underline dark:text-indigo-300"
            to={sourceLink(document.action)!}>View {document.title} source</Link>}
          {document.action.reason && <p className="mt-2 text-slate-500">{document.action.reason}</p>}
        </details>)}
      </details>
      <div className="mt-3 flex flex-wrap gap-4 text-xs">
        <Link className="text-indigo-700 underline dark:text-indigo-300" to={packageLink('documents/preview')}>Preview documents</Link>
        <Link className="text-indigo-700 underline dark:text-indigo-300" to={packageLink('documents', 'exports')}>View export packages</Link>
        <Link className="text-indigo-700 underline dark:text-indigo-300" to={packageLink('documents', 'records')}>View all document states</Link>
        <button type="button" disabled={!canGenerate || checking || generating} className="text-indigo-700 underline disabled:opacity-50 dark:text-indigo-300"
          onClick={() => setGenerationOpen(true)}>Prepare validated export</button>
      </div>
      {!canGenerate && workspace.permissions.generateReason && <p className="mt-2 text-xs text-slate-500">{workspace.permissions.generateReason}</p>}
    </section>}
    <p className="flex gap-2 rounded-lg border border-indigo-100 bg-indigo-50 p-3 text-xs text-slate-600 dark:border-indigo-900 dark:bg-indigo-950 dark:text-slate-300">
      <CircleHelp size={16} aria-hidden="true" className="shrink-0" /><span>Readiness checks support preparation. They do not submit a package or grant authorization.
        {purpose === 'InitialSubmission' && ' A missing AO decision is not a blocker for initial ATO submission.'}</span>
    </p>
    {selectedCheckId && <SetupDialog placement="right" busy={checking || generating} title={detail?.title ?? 'Readiness check details'}
      description="Inspect the retained result, its source and the workflow that resolves the gap."
      onClose={closeCheck}>
      {detailError ? <p role="alert">{detailError}</p> : !detail ? <p role="status">Loading check details…</p> : <>
        <span className="rounded bg-amber-50 px-2 py-1 text-xs text-amber-900">{outcomeLabels[detail.outcome]}</span>
        <h3 className="mt-5 text-sm font-semibold">Why this matters</h3><p className="mt-2 text-sm text-slate-500">{detail.why}</p>
        <h3 className="mt-5 text-sm font-semibold">Current record at validation</h3>
        <div className="mt-2 rounded-lg border border-slate-200 p-3 text-sm">
          {detail.missingSource && <p>{detail.missingSource}</p>}
          {detail.sources.map(source => <dl key={`${source.kind}:${source.recordId}`} className="mb-2 text-xs">
            <dt className="font-semibold">{source.label}</dt><dd className="break-all">{source.kind} · {source.recordId}</dd>
            <dd className="break-all">Revision: {source.revision ?? 'Not recorded'} · Hash: {source.contentHash ?? 'Not recorded'}</dd>
          </dl>)}
          {sourceLink(detail.action) && <Link className="mt-2 inline-block text-indigo-700 underline dark:text-indigo-300" to={sourceLink(detail.action)!}>
            {detail.action.canEdit ? 'Open source workflow' : 'View source record'}</Link>}
          {detail.action.reason && <p className="mt-2 text-xs text-slate-500">{detail.action.reason}</p>}
        </div>
        <h3 className="mt-5 text-sm font-semibold">Next steps</h3>
        <ol className="mt-2 list-decimal space-y-3 pl-5 text-sm">{detail.nextSteps.map((step, index) => <li key={index}>{step}</li>)}</ol>
        <h3 className="mt-5 text-sm font-semibold">Recorded ownership</h3>
        <p className="mt-2 text-sm">Responsible person: {detail.recordedOwner?.displayName ?? 'Not recorded'}</p>
        <p className="mt-1 text-xs text-slate-500">{detail.recordedOwner ? `${detail.recordedOwner.role} · ${detail.recordedOwner.scope}`
          : detail.expectedRole ? `Workflow role: ${detail.expectedRole}; no named assignment was returned.` : 'Responsible role not recorded.'}</p>
        <details open className="mt-5 rounded-lg border border-slate-200 p-3 text-xs"><summary className="cursor-pointer font-semibold">Check details</summary>
          <dl className="mt-3 space-y-2"><div><dt>Package purpose</dt><dd>{packagePurposeLabels[purpose]}</dd></div>
            <div><dt>Evaluated snapshot</dt><dd>{run ? date(run.evaluatedAt) : 'Unavailable'}</dd><dd className="break-all">{run?.sourceHash ?? 'Unavailable'}</dd></div>
            <div><dt>Rule</dt><dd>{detail.ruleId} · {run?.ruleVersion}</dd></div>
            <div><dt>Applicability</dt><dd>{detail.applicability} · {detail.required ? 'Required' : 'Optional/advisory'}</dd></div>
          </dl><p className="mt-3 text-slate-500">Changing a source does not resolve this retained finding. Return and recheck readiness.</p>
        </details>
        {stale && <p className="mt-4 rounded-lg bg-indigo-50 p-3 text-sm text-indigo-900">This result is out of date. Recheck readiness after updating records.</p>}
        <div className="mt-4 flex flex-wrap gap-3">
          {sourceLink(detail.action) && <Link className={systemPrimaryAction} to={sourceLink(detail.action)!}>
            {detail.action.canEdit ? 'Open source workflow' : 'View source record'}</Link>}
          <button type="button" className={systemSecondaryAction} onClick={closeCheck}>Close</button>
        </div>
      </>}
    </SetupDialog>}
    {historyOpen && <SetupDialog title="Validation history" busy={false} onClose={() => setHistoryOpen(false)}
      description="Retained evaluations for the selected package purpose and source selection. Historical results do not automatically describe current records.">
      {historyError && <p role="alert">{historyError}</p>}
      {!history && !historyError && <p role="status">Loading validation history…</p>}
      {history?.items.map(item => <button type="button" key={item.id} className="mb-3 block w-full rounded border p-3 text-left text-sm"
        onClick={() => { setHistoryOpen(false); updateQuery({ run: item.id, check: null }); }}>
        {date(item.evaluatedAt)} · {item.outcome} · {item.freshness.state}<span className="block break-all text-xs text-slate-500">{item.id}</span>
      </button>)}
      {history?.totalCount === 0 && <p>No retained validation runs for this selection.</p>}
      {history && history.totalCount > history.limit && <nav className="flex justify-between text-sm" aria-label="Validation history pages">
        <button disabled={historyOffset === 0} onClick={() => setHistoryOffset(value => Math.max(0, value - history.limit))}>Previous runs</button>
        <button disabled={historyOffset + history.limit >= history.totalCount} onClick={() => setHistoryOffset(value => value + history.limit)}>Next runs</button>
      </nav>}
    </SetupDialog>}
    {generationOpen && <SetupDialog title="Prepare validated export" busy={generating} onClose={() => setGenerationOpen(false)}
      description="Generate a new retained package against this exact purpose and evaluated snapshot. The server rechecks freshness; this does not submit to eMASS or authorize the system.">
      <p className="text-sm">{packagePurposeLabels[purpose]}</p>
      <p className="mt-2 break-all text-xs">Validation: {run?.id} · Source: {run?.sourceHash}</p>
      <div className="mt-4 flex gap-3"><button type="button" disabled={!canGenerate || generating} className={systemPrimaryAction}
        onClick={() => void exportPackage()}>{generating ? 'Starting export…' : 'Generate package'}</button>
        <button type="button" disabled={generating} className={systemSecondaryAction} onClick={() => setGenerationOpen(false)}>Cancel</button></div>
    </SetupDialog>}
  </div>;
}

function ProgressRecords({ milestone, sourceLink }: { milestone: PackageReadinessProgress; sourceLink: (action: PackageReadinessAction) => string | null }) {
  return <>
    {milestone.records.length > 0 && <details className="mt-2"><summary className="cursor-pointer text-indigo-700 underline dark:text-indigo-300">Recorded history ({milestone.totalCount})</summary>
      {milestone.records.map(record => <p key={`${record.kind}:${record.id}`} className="mt-2 break-all">
        {record.status ?? record.kind} · {record.recordedAt ? date(record.recordedAt) : 'Time not recorded'} · {record.sourceRelationship}
      </p>)}
    </details>}
    {sourceLink(milestone.action) && <Link className="mt-2 inline-block text-indigo-700 underline dark:text-indigo-300" to={sourceLink(milestone.action)!}>View records</Link>}
  </>;
}
