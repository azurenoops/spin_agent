import { useEffect, useRef, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { useQueryState, useRemote, moveTabFocus, Status } from '../workspace-operations/workspaceUi';
import { getOverviewWork, type OverviewWork, type OverviewWorkGroup } from '../../api/systemOverview';
import OverviewJourney from './OverviewJourney';
import OverviewWorkGroups from './OverviewWorkGroups';
import OverviewDocuments from './OverviewDocuments';
import OverviewMonitoring from './OverviewMonitoring';
import OverviewAiHelp, { type OverviewAiEdit } from './OverviewAiHelp';
import { useSystemOverview } from './useSystemOverview';
import { overviewDate, overviewPhase } from './overviewPresentation';
import { systemPanel, systemSecondaryAction, SystemTaskHeading } from './SystemTaskPresentation';

export default function SystemReadinessOverview({ systemId, systemName }: {
  systemId: string; systemName: string; currentPhase?: string;
}) {
  const { params, set } = useQueryState();
  const state = useSystemOverview(systemId);
  const mode = params.get('overview') === 'monitoring' ? 'monitoring' : 'readiness';
  const mine = params.get('owner') === 'mine';
  const parsedOffset = Number(params.get('workOffset') ?? '0');
  const offset = Number.isSafeInteger(parsedOffset) && parsedOffset >= 0 ? parsedOffset : 0;
  const viewing = overviewPhase(params.get('phase')) ?? (state.workspace?.rmf.confirmed ? overviewPhase(state.workspace.rmf.phase) : null) ?? 'Prepare';
  const expanded = params.getAll('expanded').flatMap(value => value.split(',')).filter(Boolean);
  const allFindings = params.get('findings') === 'all';
  const runId = state.successful?.id ?? null;
  const work = useRemote<OverviewWork | null>(signal => runId
    ? getOverviewWork(systemId, runId, { mine, limit: 10, offset }, signal) : Promise.resolve(null),
  [systemId, runId, mine, offset, state.revision]);
  const [retainedTotals, setTotals] = useState<{ runId: string; counts: OverviewWork['counts'] } | null>(null);
  const totals = retainedTotals?.runId === runId ? retainedTotals.counts : null;
  const [aiGroup, setAiGroup] = useState<OverviewWorkGroup | null>(null);
  const aiEdits = useRef(new Map<string, OverviewAiEdit>());
  useEffect(() => {
    if (work.data?.findingsAvailable) setTotals({ runId: work.data.runId, counts: work.data.counts });
  }, [work.data]);
  const lastFailure = state.error || state.workspace?.latestRun?.failure?.message
    || (state.workspace?.latestRun?.outcome === 'SourceChanged' ? 'Sources changed during the last evaluation. Recheck against current records.'
      : state.workspace?.latestRun?.outcome === 'Failed' ? 'The last readiness evaluation failed. Current readiness is not established.' : '');
  const freshness = state.successful?.freshness.state;
  const status = state.checking ? 'Checking requirements…' : lastFailure ? 'Readiness refresh failed'
    : state.loading ? 'Loading saved readiness…' : !runId ? 'Not checked'
      : freshness === 'Stale' ? 'Stale — check again' : freshness === 'Unavailable' ? 'Freshness unavailable'
        : 'Current saved result';
  return <div className="min-w-0 space-y-5 pb-6">
    <SystemTaskHeading eyebrow={systemName} title="A clear path to your ATO package"
      description="Follow the RMF journey and finish the work that builds a reviewable, evidence-backed package." />
    <OverviewJourney systemId={systemId} rmf={state.workspace?.rmf} viewing={viewing}
      onView={phase => set({ phase })} onConfirmed={() => { void state.load(); }} />
    <div role="tablist" aria-label="Overview sections" className="flex flex-wrap gap-5 border-b border-slate-200 dark:border-slate-700" onKeyDown={moveTabFocus}>
      {([{ value: 'readiness', label: 'Package preparation' }, { value: 'monitoring', label: 'Monitoring & follow-up' }] as const).map(tab =>
        <button type="button" role="tab" key={tab.value} id={`overview-tab-${tab.value}`} aria-selected={mode === tab.value}
          aria-controls={`overview-panel-${tab.value}`} tabIndex={mode === tab.value ? 0 : -1}
          className={`min-h-11 border-b-2 px-2 text-sm ${mode === tab.value ? 'border-indigo-600 font-semibold text-indigo-700 dark:text-indigo-300' : 'border-transparent'}`}
          onClick={() => set({ overview: tab.value })}>{tab.label}</button>)}
    </div>
    <div role="tabpanel" id={`overview-panel-${mode}`} aria-labelledby={`overview-tab-${mode}`} tabIndex={0}>
      {mode === 'monitoring' ? <OverviewMonitoring systemId={systemId} /> : <div className="space-y-5">
        <section aria-label="Readiness summary" className={`${systemPanel} space-y-2`}>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div><h2 className="font-semibold">Package preparation</h2><p className="text-sm">Initial submission</p></div>
            <button type="button" className={systemSecondaryAction} disabled={state.loading || state.checking || !state.workspace?.permissions.canValidate}
              onClick={() => { void state.check(); }}>Check again</button>
          </div>
          <p role="status" className="text-sm font-medium">{status}</p>
          {totals && runId && <div className="flex flex-wrap gap-x-4 gap-y-1 text-sm">
            <span>{totals.blocking} blocking requirements</span><span>{totals.warnings} {totals.warnings === 1 ? 'warning' : 'warnings'}</span>
            <span>{totals.total} returned findings</span>
          </div>}
          {state.successful && <p className="text-xs text-slate-600 dark:text-slate-300">Last successful check: {overviewDate(state.successful.evaluatedAt)}</p>}
          {lastFailure && <><p role="alert" className="text-sm text-red-800 dark:text-red-200">{lastFailure}</p>
            {state.successful && <p className="text-xs">Previous successful check retained from {overviewDate(state.successful.evaluatedAt)}. It is not a successful refresh; current validity must be rechecked.</p>}
            <button type="button" className={systemSecondaryAction} disabled={state.loading || state.checking} onClick={() => { void state.load(); }}>Reload saved readiness</button></>}
          {freshness !== 'Current' && state.successful?.freshness.reason && <p className="text-xs">{state.successful.freshness.reason}</p>}
          {runId && !totals && !work.loading && <p className="text-xs">Individual finding totals are unavailable for this saved check. Aggregate check counts are not finding totals.</p>}
          {state.workspace?.permissions.validateReason && <p className="text-xs">{state.workspace.permissions.validateReason}</p>}
          <p className="text-xs text-slate-600 dark:text-slate-300">Finding counts do not indicate RMF phase completion. A check does not approve documents, accept inheritance, submit eMASS or authorize the system.</p>
        </section>
        <div className="grid min-w-0 items-start gap-5 min-[1051px]:grid-cols-[minmax(0,1.65fr)_minmax(260px,1fr)]">
          <div className="min-w-0 space-y-5">
            <Status loading={work.loading && !!runId} error={work.error} retry={work.retry} />
            {!runId && !state.loading && <p className="text-sm">No successful readiness evaluation is recorded. Check requirements to identify actual grouped package work.</p>}
            <OverviewWorkGroups systemId={systemId} work={work.data} mine={mine} offset={offset} expanded={expanded}
              allFindings={allFindings} search={`?${params.toString()}`} onOwner={personal => set({ owner: personal ? 'mine' : 'all', workOffset: 0 })}
              onOffset={next => set({ workOffset: next })}
              onExpanded={(id, open) => set({ expanded: [...new Set(open ? [...expanded, id] : expanded.filter(value => value !== id))].join(',') || null })}
              onAllFindings={() => set({ findings: allFindings ? null : 'all' })} onAi={setAiGroup} />
            {aiGroup && runId && <OverviewAiHelp key={`${runId}:${aiGroup.id}`} systemId={systemId} runId={runId}
              group={aiGroup} edits={aiEdits.current} onClose={() => setAiGroup(null)} />}
            <details className={`${systemPanel} text-sm`}><summary className="cursor-pointer font-semibold">How readiness is determined</summary>
              <p className="mt-3">Checks evaluate the selected purpose and saved source versions. Individual findings are grouped without discarding requirements. Source changes require revalidation; document and package review remain separate.</p>
              <Link className={`${systemSecondaryAction} mt-3`} to={`/systems/${encodeURIComponent(systemId)}/documents?purpose=InitialSubmission`}>Inspect canonical checks and history</Link>
            </details>
          </div>
          <OverviewDocuments systemId={systemId} workspace={state.workspace} search={`?${params.toString()}`} />
        </div>
      </div>}
    </div>
  </div>;
}
