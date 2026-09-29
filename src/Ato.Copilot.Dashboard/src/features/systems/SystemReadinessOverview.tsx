import { useEffect, useState, type ReactNode } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import SystemPackageValidation from './SystemPackageValidation';
import SystemNextActions from './SystemNextActions';
import { getConMonOverview, type ConMonOverviewResponse } from '../../api/conmon';
import { SystemTaskColumns, SystemTaskHeading, SystemTaskSupport, systemPanel, systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';
import { moveTabFocus, useQueryState } from '../workspace-operations/workspaceUi';

export default function SystemReadinessOverview({ systemId, systemName, currentPhase }: { systemId: string; systemName: string; currentPhase?: string }) {
  const { params, set } = useQueryState();
  const requested = params.get('overview');
  const mode = requested === 'readiness' ? 'readiness' : requested === 'monitoring' || currentPhase === 'Monitor' ? 'monitoring' : 'readiness';
  const navigation = <nav role="tablist" aria-label="System overview tasks" className="system-section-tabs my-6" onKeyDown={moveTabFocus}>
    {([['readiness', 'Readiness'], ['monitoring', 'Monitoring & follow-up']] as const).map(([value, label]) =>
      <button key={value} type="button" id={`overview-tab-${value}`} role="tab" aria-selected={mode === value}
        aria-controls={`overview-panel-${value}`} tabIndex={mode === value ? 0 : -1}
        className={`shrink-0 whitespace-nowrap border-b-2 px-0 py-[11px] text-xs ${mode === value ? 'border-[#5143d7] text-[#5143d7]' : 'border-transparent text-slate-500'}`}
        onClick={() => set({ overview: value })}>{label}</button>)}
  </nav>;
  return mode === 'readiness'
    ? <SubmissionReadiness key={systemId} systemId={systemId} systemName={systemName} navigation={navigation} />
    : <MonitoringNextActions key={systemId} systemId={systemId} systemName={systemName} navigation={navigation} />;
}

function SubmissionReadiness({ systemId, systemName, navigation }: { systemId: string; systemName: string; navigation: ReactNode }) {
  const [nextPath, setNextPath] = useState<string | null>(null);
  const base = `/systems/${encodeURIComponent(systemId)}`;
  const checklist = `${base}/documents?purpose=InitialSubmission`;
  const continueTo = nextPath ? `${base}/${nextPath}` : checklist;
  return <section aria-label="Initial submission readiness" className="mb-6">
    <SystemTaskHeading eyebrow={systemName} title="A clear path to your ATO package"
      description="Finish applicable documentation, review evidence, and prepare an initial submission."
      action={<Link to={continueTo} className={systemPrimaryAction}>Continue preparation</Link>} />
    {navigation}
    <div role="tabpanel" id="overview-panel-readiness" aria-labelledby="overview-tab-readiness">
    <SystemPackageValidation systemId={systemId} initialPurpose="InitialSubmission" summaryOnly />
    <SystemTaskColumns support={<>
      <section className="border-l-2 border-[#d9d3f9] pl-[18px] text-xs text-slate-500">
        <p className="mb-2 text-[10px] font-semibold uppercase tracking-[1.2px]">Used in your package</p>
        <h2 className="mb-2 text-sm font-semibold text-slate-700 dark:text-slate-200">Complete reviewed submission package</h2>
        <p className="mb-3 leading-relaxed">Reviewed records supply the submission package. Draft edits must not replace the approved baseline.</p>
        <Link className={systemSecondaryAction} to={`${base}/documents/preview`}>Preview contribution</Link>
        <p className="mt-2 leading-relaxed">The preview uses current records; it is not an approved export.</p>
      </section>
      <section className="border-l-2 border-[#d9d3f9] pl-[18px] text-xs text-slate-500">
        <p className="mb-2 text-[10px] font-semibold uppercase tracking-[1.2px]">Review &amp; ownership</p>
        <h2 className="mb-2 text-sm font-semibold text-slate-700 dark:text-slate-200">Keep the next action clear</h2>
        <p className="leading-relaxed">Review responsible roles, source versions, review status and evidence in each task. A readiness check is not an authorization decision or eMASS acceptance.</p>
      </section>
      <section className="border-l-2 border-[#d9d3f9] pl-[18px] text-xs text-slate-500">
        <p className="mb-2 text-[10px] font-semibold uppercase tracking-[1.2px]">Related work</p>
        <Link className={systemSecondaryAction} to={checklist}>View package readiness</Link>
      </section>
    </>}>
      <SystemNextActions systemId={systemId} onNextPathChange={setNextPath} />
    </SystemTaskColumns>
    </div>
    <p className="mt-6 text-xs text-slate-500">Inputs → reviewed records → document output → ongoing change review</p>
  </section>;
}

function MonitoringNextActions({ systemId, systemName, navigation }: { systemId: string; systemName: string; navigation: ReactNode }) {
  const [data, setData] = useState<ConMonOverviewResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    let current = true;
    setLoading(true); setError(null); setData(null);
    void getConMonOverview(systemId).then(value => {
      if (!current) return;
      if (value.systemId !== systemId) throw new Error('Monitoring context does not match the selected system.');
      setData(value);
    }).catch(reason => { if (current) setError(reason instanceof Error ? reason.message : 'Monitoring context is unavailable.'); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [systemId, attempt]);
  const base = `/systems/${encodeURIComponent(systemId)}`;
  return <section className="mb-6" aria-label="Monitoring follow-up">
    <SystemTaskHeading eyebrow={systemName} title="Maintain the reviewed baseline"
      description="Review recorded changes and follow-up work before deciding whether reassessment or document updates are needed."
      action={<Link className={systemPrimaryAction} to={`${base}/conmon`}>Review monitoring</Link>} />
    {navigation}
    <div role="tabpanel" id="overview-panel-monitoring" aria-labelledby="overview-tab-monitoring">
    <SystemTaskColumns support={<>
      <SystemTaskSupport title="Contributes to"><p>Monitoring evidence / Reviewed baseline follow-up</p></SystemTaskSupport>
      <SystemTaskSupport title="Review scope and records">
        <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`${base}/conmon`}>Coverage &amp; health</Link>
        <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`${base}/documents?tab=exports`}>Retained packages</Link>
        <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`${base}/authorize`}>Recorded decisions</Link>
        <p>A monitoring check is not proof of complete resource coverage, an active authorization, or an approved package revision.</p>
      </SystemTaskSupport>
    </>}>
      <section className={systemPanel}>
        <h2 className="text-lg font-semibold">Changes &amp; follow-up</h2>
        {loading && <p role="status" className="mt-4 text-sm">Loading recorded monitoring context…</p>}
        {error && <div className="mt-4 space-y-3 text-sm text-amber-900"><p role="alert">{error}</p>
          <button type="button" className={systemSecondaryAction} onClick={() => setAttempt(value => value + 1)}>Retry monitoring context</button></div>}
        {data && <>
          <div className="my-5 grid gap-3 sm:grid-cols-3">
            {[['Monitoring', data.status.monitoringEnabled ? 'Enabled' : 'Not enabled'],
              ['Recorded open findings', data.status.openFindings], ['Overdue POA&M items', data.status.overduePoamItems]].map(([label, value]) =>
              <div key={label} className="rounded-lg bg-slate-50 p-4 dark:bg-slate-800"><p className="text-xs text-slate-500">{label}</p><p className="mt-2 text-xl font-semibold">{value}</p></div>)}
          </div>
          <p className="text-sm text-slate-600 dark:text-slate-300">{data.expiration.alertMessage}</p>
          <p className="mt-2 text-xs text-slate-500">Last monitoring check: {data.status.lastMonitoringCheck ? new Date(data.status.lastMonitoringCheck).toLocaleString() : 'Not recorded'}</p>
          {!data.plan && <div className="mt-5 flex flex-wrap items-center justify-between gap-3 border-t pt-4">
            <p className="text-sm">No continuous monitoring plan is recorded.</p><Link className={systemSecondaryAction} to={`${base}/conmon`}>Review monitoring plan</Link>
          </div>}
          {data.reauthorization.isTriggered && <div className="mt-5 rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900">
            <p className="font-semibold">Review recorded reassessment triggers</p><ul className="mt-2 list-inside list-disc">{data.reauthorization.triggers.map(trigger => <li key={trigger}>{trigger}</li>)}</ul>
          </div>}
          <div className="mt-5 divide-y divide-slate-100 dark:divide-slate-700">{data.significantChanges.filter(change => !change.reviewedAt).slice(0, 5).map(change => <article key={change.id} className="py-4">
            <h3 className="font-semibold">{change.changeType}</h3><p className="mt-2 text-sm text-slate-500">{change.description}</p>
            <Link className="mt-3 inline-block text-sm text-indigo-700 underline dark:text-indigo-300" to={`${base}/conmon`}>Review recorded change</Link>
          </article>)}</div>
          {data.significantChanges.length === 0 && <p className="mt-5 text-sm text-slate-500">No significant-change records were returned. This does not establish complete collection coverage.</p>}
        </>}
      </section>
    </SystemTaskColumns>
    </div>
  </section>;
}
