import { useEffect, useState, type ReactNode } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import SystemPackageValidation from './SystemPackageValidation';
import SystemNextActions from './SystemNextActions';
import { getConMonOverview, type ConMonOverviewResponse } from '../../api/conmon';
import { SystemTaskHeading, systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';
import { moveTabFocus, useQueryState } from '../workspace-operations/workspaceUi';
import type { ReadinessResult, ValidationFinding } from '../../api/package';
import { getCategoryRoute } from './packageReadiness';
import './systemReadinessOverview.css';

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
  return <div className="system-readiness-overview">{mode === 'readiness'
    ? <SubmissionReadiness key={systemId} systemId={systemId} systemName={systemName} navigation={navigation} />
    : <MonitoringNextActions key={systemId} systemId={systemId} systemName={systemName} navigation={navigation} />}</div>;
}

function SubmissionReadiness({ systemId, systemName, navigation }: { systemId: string; systemName: string; navigation: ReactNode }) {
  const [nextPath, setNextPath] = useState<string | null>(null);
  const [result, setResult] = useState<ReadinessResult | null>(null);
  const base = `/systems/${encodeURIComponent(systemId)}`;
  const checklist = `${base}/documents?purpose=InitialSubmission`;
  return <section aria-label="Initial submission readiness" className="mb-6">
    <SystemTaskHeading eyebrow={systemName} title="A clear path to your ATO package"
      description="See what needs attention, who owns it, and where it contributes."
      action={nextPath && <Link to={`${base}/${nextPath}`} className={systemPrimaryAction}>Continue preparation</Link>} />
    {navigation}
    <div role="tabpanel" id="overview-panel-readiness" aria-labelledby="overview-tab-readiness">
    <SystemPackageValidation systemId={systemId} initialPurpose="InitialSubmission" overviewHero onResult={setResult} />
    <div className="system-overview-columns">
      <div className="min-w-0 space-y-6">
        <SystemNextActions systemId={systemId} onNextPathChange={setNextPath} overview />
        {result && result.findings.length > 0 && <TeamFindings result={result} base={base} />}
        <details className="text-xs text-slate-500 dark:text-slate-400">
          <summary>How readiness is determined</summary>
          <p className="mt-3 leading-relaxed">A check reads saved system records for the initial submission. It does not require an already-issued ATO, assign every team finding to you, approve drafts or establish authorization.</p>
          <p className="mt-2 leading-relaxed">Your actions come from effective system roles and saved workflow state. Recheck after changing records; a document preview is not an approved export.</p>
        </details>
      </div>
      <aside className="system-overview-sidebar" aria-label="Package documentation and workflow">
        <section className="system-overview-sideblock" aria-label="Documentation at a glance">
          <p className="overview-eyebrow">Your package</p>
          <h2>Documentation at a glance</h2>
          <p>{result ? 'These summaries reflect the returned readiness findings, not document approval.' : 'Check readiness to see which documents need attention.'}</p>
          {result && <dl className="mt-3">{[
            { label: 'System Security Plan', findings: result.findings.filter(finding => finding.artifactType?.toLowerCase() === 'ssp'
              || ['ssp', 'boundary', 'profile-approval', 'provider-authorization'].includes(finding.category.toLowerCase())) },
            { label: 'Assessment plan', findings: result.findings.filter(finding => finding.artifactType?.toLowerCase() === 'assessment-plan' || finding.category.toLowerCase() === 'sap') },
            { label: 'Evidence index', findings: result.findings.filter(finding => finding.category.toLowerCase() === 'evidence') },
          ].map(({ label, findings }) => <div key={label} className="overview-document">
            <dt>{label}</dt><dd>{findings.length ? 'Gaps' : 'No gaps reported'}</dd>
          </div>)}</dl>}
          <Link className={`${systemSecondaryAction} mt-5`} to={`${base}/documents/preview`}>Preview documentation ↗</Link>
        </section>
        <section className="system-overview-sideblock">
          <h3>Need help with a task?</h3>
          <p>Task details explain what to provide, which source workflow owns it, and which document it supports.</p>
          <Link className="mt-3 inline-block text-xs text-indigo-700 underline dark:text-indigo-300" to={checklist}>Understand the workflow →</Link>
          <Link className="mt-3 block text-xs text-indigo-700 underline dark:text-indigo-300" to={checklist}>View package readiness</Link>
        </section>
        <p className="overview-note">Draft changes remain separate from the approved baseline. Preparation, export, submission and authorization are distinct.</p>
      </aside>
    </div>
    </div>
  </section>;
}

function TeamFindings({ result, base }: { result: ReadinessResult; base: string }) {
  return <section aria-label="Team readiness findings">
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <h2 className="text-lg font-semibold">What the team needs to finish</h2>
      <span className="system-overview-badge">{result.findings.length} returned findings</span>
    </div>
    <div className="space-y-3">{result.findings.slice(0, 5).map((finding, index) =>
      <ReadinessFinding key={`${finding.category}:${index}`} finding={finding} base={base} />)}</div>
    {result.findings.length > 5 && <p className="mt-3 text-xs text-slate-500">Showing 5 of {result.findings.length} findings. <Link className="underline" to={`${base}/documents?purpose=InitialSubmission`}>Review all package requirements</Link></p>}
  </section>;
}

function ReadinessFinding({ finding, base }: { finding: ValidationFinding; base: string }) {
  const route = getCategoryRoute(finding.category, finding.artifactType, finding.description);
  return <article className="system-overview-task">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <h3 className="min-w-0 flex-1">{finding.description.replace(/^[A-Z_]+:\s*/, '')}</h3>
      <span className="system-overview-badge needs-input">{finding.severity.toLowerCase() === 'error' ? 'Needs input' : 'Review warning'}</span>
    </div>
    <p className="mt-3 text-xs text-slate-500 dark:text-slate-400">Owner not provided by the readiness check</p>
    <details><summary>View task details</summary>
      <p className="mt-3">{finding.remediation || 'Inspect the source workflow and record what is missing.'}</p>
      <p className="mt-2 text-slate-500 dark:text-slate-400">Source category: {finding.category}{finding.artifactType ? ` · Artifact: ${finding.artifactType}` : ''}</p>
      <p className="mt-2 text-slate-500 dark:text-slate-400">Original finding: {finding.description}</p>
      <Link className={`${systemSecondaryAction} mt-3`} to={`${base}/${route?.path ?? 'documents?purpose=InitialSubmission'}`}>
        {route ? `View ${route.label.toLowerCase()}` : 'View package requirements'} →
      </Link>
    </details>
  </article>;
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
    <SystemTaskHeading eyebrow={systemName} title="A clear path to your ATO package"
      description="See what needs attention, who owns it, and where it contributes." />
    {navigation}
    <div role="tabpanel" id="overview-panel-monitoring" aria-labelledby="overview-tab-monitoring">
    <section className="system-overview-hero mb-6">
      <div><span className="system-overview-badge">Monitoring &amp; follow-up</span>
        <h2>Keep your documentation current</h2>
        <p>Review observed changes and their impact before updating the documented baseline.</p></div>
      <Link className={systemPrimaryAction} to={`${base}/conmon`}>Review monitoring</Link>
    </section>
    <div className="system-overview-columns">
      <section className="min-w-0">
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
          <div className="mt-5 space-y-3">{data.significantChanges.filter(change => !change.reviewedAt).slice(0, 5).map(change => <article key={change.id} className="system-overview-task">
            <h3 className="font-semibold">{change.changeType}</h3><p className="mt-2 text-sm text-slate-500">{change.description}</p>
            <Link className="mt-3 inline-block text-sm text-indigo-700 underline dark:text-indigo-300" to={`${base}/conmon`}>Review recorded change</Link>
          </article>)}</div>
          {data.significantChanges.every(change => !!change.reviewedAt) && <p className="system-overview-task mt-5 text-sm text-slate-500 dark:text-slate-400">No unreviewed significant-change records were returned. This does not establish complete collection coverage.</p>}
        </>}
      </section>
      <aside className="system-overview-sidebar" aria-label="Monitoring source records">
        <section className="system-overview-sideblock"><p className="overview-eyebrow">Reviewed baseline follow-up</p>
          <h2>Keep monitoring evidence traceable</h2><p>Review observations before staging documentation changes. Preserve the approved baseline while changes are reviewed.</p></section>
        <section className="system-overview-sideblock"><h3>Review scope and records</h3>
          <Link className="my-3 block text-xs text-indigo-700 underline dark:text-indigo-300" to={`${base}/conmon`}>Coverage &amp; health</Link>
          <Link className="my-3 block text-xs text-indigo-700 underline dark:text-indigo-300" to={`${base}/documents?tab=exports`}>Retained packages</Link>
          <Link className="my-3 block text-xs text-indigo-700 underline dark:text-indigo-300" to={`${base}/authorize`}>Recorded decisions</Link></section>
        <p className="overview-note">Monitoring enabled, monitoring health, documentation readiness and authorization decisions are tracked separately.</p>
      </aside>
    </div>
    </div>
  </section>;
}
