import { useEffect, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import SystemPackageValidation from './SystemPackageValidation';
import { getConMonOverview, type ConMonOverviewResponse } from '../../api/conmon';
import { SystemTaskColumns, SystemTaskHeading, SystemTaskSupport, systemPanel, systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';

export default function SystemReadinessOverview({ systemId, systemName, currentPhase }: { systemId: string; systemName: string; currentPhase?: string }) {
  const [mode, setMode] = useState(currentPhase === 'Monitor' ? 'monitoring' : 'preparation');
  return <>
    <nav aria-label="System overview tasks" className="mb-5 flex flex-wrap gap-3">
      <button type="button" aria-pressed={mode === 'preparation'} className={mode === 'preparation' ? systemPrimaryAction : systemSecondaryAction}
        onClick={() => setMode('preparation')}>Preparing for ATO</button>
      <button type="button" aria-pressed={mode === 'monitoring'} className={mode === 'monitoring' ? systemPrimaryAction : systemSecondaryAction}
        onClick={() => setMode('monitoring')}>Monitoring &amp; follow-up</button>
    </nav>
    {mode === 'preparation' ? <SubmissionReadiness systemId={systemId} systemName={systemName} /> : <MonitoringNextActions systemId={systemId} />}
  </>;
}

function SubmissionReadiness({ systemId, systemName }: { systemId: string; systemName: string }) {
  const base = `/systems/${encodeURIComponent(systemId)}`;
  return <section aria-label="Initial submission readiness" className="mb-6">
    <SystemTaskHeading title="A clear path to your ATO package"
      description="Finish applicable documentation, review evidence, and prepare an initial submission."
      status={<span className="rounded-full bg-indigo-50 px-3 py-1 font-medium text-indigo-700 dark:bg-indigo-950 dark:text-indigo-200">Package preparation · {systemName}</span>}
      action={<Link to={`${base}/documents`} className={systemPrimaryAction}>Continue preparation</Link>} />
    <SystemTaskColumns support={<>
      <SystemTaskSupport title="Contributes to"><p>Complete reviewed submission package</p>
        <p>SSP, assessment plan, assessment report, POA&amp;M and supporting evidence as required by the server validation.</p>
      </SystemTaskSupport>
      <SystemTaskSupport title="Next in Systems">
        <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`${base}/documents`}>Readiness checklist</Link>
        <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`${base}/documents?tab=exports`}>Generate &amp; export a package</Link>
        <Link className="block text-indigo-700 underline dark:text-indigo-300" to={`${base}/conmon`}>Coverage &amp; health</Link>
      </SystemTaskSupport>
      <SystemTaskSupport title="Keep the baseline distinct">
        <p>A readiness check is not an authorization decision or eMASS acceptance. Monitoring an operational mission system remains a separate task.</p>
      </SystemTaskSupport>
    </>}>
      <section className={systemPanel}>
        <h2 className="text-lg font-semibold">Next actions for this system</h2>
        <p className="mt-3 text-sm text-slate-500">Select the package purpose and validate the current records to identify applicable blockers. These task links are navigation, not completion claims.</p>
        <div className="mt-4 divide-y divide-slate-100 dark:divide-slate-700">
          {[
            { title: 'Confirm system boundary', description: 'Define the SSP scope and included resources.', label: 'Review system boundary', path: 'boundaries' },
            { title: 'Review mission profile', description: 'Review the system description used in the SSP.', label: 'Review mission profile', path: 'profile/MissionAndPurpose' },
            { title: 'Prepare assessment plan', description: 'Define the procedures and assessors for the SAP.', label: 'Review assessment plan', path: 'assessments?tab=plan' },
          ].map(task => <article key={task.path} className="flex flex-wrap items-center justify-between gap-3 py-5">
            <div className="min-w-0 flex-1"><h3 className="text-sm font-semibold">{task.title}</h3><p className="mt-2 text-sm text-slate-500">{task.description}</p></div>
            <Link aria-label={task.label} className={systemSecondaryAction} to={`${base}/${task.path}`}>Review →</Link>
          </article>)}
        </div>
        <p className="mt-5 rounded-lg border border-indigo-100 bg-indigo-50 p-4 text-sm leading-relaxed text-indigo-900 dark:border-indigo-900 dark:bg-indigo-950 dark:text-indigo-200">
          Initial package preparation does not require an already-issued ATO. Applicability and blockers are determined by the shared readiness service.
        </p>
      </section>
      <SystemPackageValidation systemId={systemId} initialPurpose="InitialSubmission" />
    </SystemTaskColumns>
  </section>;
}

function MonitoringNextActions({ systemId }: { systemId: string }) {
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
    <SystemTaskHeading title="Maintain the reviewed baseline"
      description="Review recorded changes and follow-up work before deciding whether reassessment or document updates are needed."
      action={<Link className={systemPrimaryAction} to={`${base}/conmon`}>Review monitoring</Link>} />
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
  </section>;
}
