import { getMonitoringWorkspace } from '../../api/scopedMonitoring';
import { getConMonOverview } from '../../api/conmon';
import { Link } from '../workspaces/workspaceNavigation';
import { Status, useRemote } from '../workspace-operations/workspaceUi';
import { overviewDate } from './overviewPresentation';
import { systemPanel, systemSecondaryAction } from './SystemTaskPresentation';

export default function OverviewMonitoring({ systemId }: { systemId: string }) {
  const scoped = useRemote(signal => getMonitoringWorkspace(systemId, signal), [systemId]);
  const overview = useRemote(async signal => {
    const result = await getConMonOverview(systemId, signal);
    if (result.systemId !== systemId) throw new Error('Monitoring records do not match the selected system.');
    return result;
  }, [systemId]);
  const base = `/systems/${encodeURIComponent(systemId)}`;
  const data = scoped.data;
  return <section aria-label="Monitoring and follow-up" className="space-y-5">
    <div className={`${systemPanel} space-y-3`}>
      <h2 className="text-lg font-semibold">Keep your documented system current</h2>
      <p className="text-sm">Review actual coverage, evaluations and changes against the versioned reviewed baseline.</p>
      <p className="text-xs">Monitoring enabled, monitoring healthy, cATO readiness and authorization are separate states.</p>
      <Status loading={scoped.loading} error={scoped.error} retry={scoped.retry} />
      <Status loading={overview.loading} error={overview.error} retry={overview.retry} />
      {overview.data && <>
        <p className="text-sm">Monitoring configuration: {overview.data.status.monitoringEnabled ? 'Enabled' : 'Not enabled'}</p>
        <p className="text-xs">Last monitoring check: {overviewDate(overview.data.status.lastMonitoringCheck)}</p>
        <p className="text-sm">{overview.data.status.openFindings} recorded open findings · {overview.data.status.overduePoamItems} overdue POA&M items</p>
        <p className="text-sm">{overview.data.expiration.alertMessage}</p>
        {overview.data.plan && <p className="text-xs">Plan cadence: {overview.data.plan.assessmentFrequency} · annual review: {overviewDate(overview.data.plan.annualReviewDate)}</p>}
      </>}
      <div className="flex flex-wrap gap-2">
        <Link className={systemSecondaryAction} to={`${base}/conmon`}>Review monitoring coverage</Link>
        <Link className={systemSecondaryAction} to={`${base}/conmon/plan`}>Review monitoring documentation</Link>
      </div>
    </div>
    {data && <>
      <section className={`${systemPanel} space-y-3`} aria-label="Coverage and connectivity">
        <h3 className="font-semibold">Coverage and connectivity gaps</h3>
        <p className="text-xs">Configuration does not prove collector permissions or complete resource visibility. The source&apos;s reported coverage state is shown below.</p>
        {!data.coverage.length && <p className="text-sm">No scoped coverage records returned. Complete visibility is not established.</p>}
        <ul className="space-y-3">{data.coverage.slice(0, 10).map((coverage, index) => <li key={`${coverage.assignmentId}:${coverage.resourceId}:${index}`}
          className="space-y-1 border-b border-slate-200 pb-3 text-sm dark:border-slate-700">
          <p className="font-medium">{data.boundaries.find(boundary => boundary.id === coverage.boundaryId)?.name ?? 'System area not provided'}</p>
          <p>{coverage.health}</p>{coverage.error && <p>{coverage.error}</p>}
          <p className="text-xs">Last successful evaluation: {overviewDate(coverage.lastSuccessAt)}</p>
          <details className="text-xs"><summary className="cursor-pointer">Scope identifiers</summary>
            <p className="break-all">{coverage.resourceId ?? 'Resource identifier not provided'} · {coverage.assignmentId}</p></details>
        </li>)}</ul>
        <Link className={systemSecondaryAction} to={`${base}/conmon`}>View all {data.coverage.length} coverage records</Link>
      </section>
      <section className={`${systemPanel} space-y-3`} aria-label="Data sources and evaluation freshness">
        <h3 className="font-semibold">Data sources, owners and freshness</h3>
        <p className="text-xs">{data.canManageRules ? 'Rule management permitted' : 'Read-only rule configuration'} · {data.canReviewImpacts ? 'Impact review permitted' : 'Read-only impact review'}</p>
        {!data.rules.length && <p className="text-sm">No monitoring rules recorded.</p>}
        <ul className="space-y-4">{data.rules.slice(0, 10).map(rule => <li key={rule.id} className="space-y-1 text-sm">
          <h4 className="font-medium">{rule.name}</h4>
          <p>Source: {rule.signal} · {rule.isEnabled ? 'Enabled' : 'Disabled'} · Every {rule.cadenceMinutes} minutes</p>
          <p>Owner: {rule.ownerId || 'Owner not provided'}</p>
          <p className="text-xs">Recorded baseline reference:</p><p className="break-all text-xs">{rule.baselineReference || 'Not provided'}</p>
          <p className="text-xs">A configured reference alone does not verify baseline approval. Review its authoritative source in the monitoring workflow.</p>
          <p className="text-xs">Last evaluated: {overviewDate(rule.lastEvaluatedAt)} · rule version {rule.version}</p>
          <details className="text-xs"><summary className="cursor-pointer">Condition and recorded evaluations</summary>
            <pre className="whitespace-pre-wrap break-words">{rule.triggerCondition}</pre>
            {data.evaluations.filter(evaluation => evaluation.ruleId === rule.id).slice(0, 5).map(evaluation =>
              <p key={evaluation.id}>{evaluation.outcome} · {overviewDate(evaluation.evaluatedAt)} · evaluated rule version {evaluation.ruleVersion}</p>)}
          </details>
        </li>)}</ul>
        <Link className={systemSecondaryAction} to={`${base}/conmon/rules`}>Review all monitoring rules</Link>
      </section>
      <section className={`${systemPanel} space-y-3`} aria-label="Baseline changes and follow-up">
        <h3 className="font-semibold">Changes and documented follow-up</h3>
        <p className="text-sm">Proposed follow-up does not replace the reviewed baseline. Investigations, reassessment, remediation and documentation changes require their existing authorized review.</p>
        <ul className="space-y-3">{data.changes.slice(0, 10).map(change => <li key={change.sourceId} className="text-sm">
          <h4 className="font-medium">{change.title}</h4><p>{change.kind} · {change.attribution} · {overviewDate(change.observedAt)}</p>
          {change.controlId && <p className="text-xs">Control: {change.controlId}</p>}
          {change.changeDetails && <details><summary className="cursor-pointer">Recorded change details</summary><pre className="whitespace-pre-wrap break-words text-xs">{change.changeDetails}</pre></details>}
        </li>)}</ul>
        {!data.changes.length && <p className="text-sm">No attributed changes returned. This does not establish a healthy collector or unchanged system.</p>}
        <ul className="space-y-3">{data.impacts.slice(0, 10).map(impact => <li key={impact.id} className="space-y-1 text-sm">
          <h4 className="font-medium">{impact.controlId ?? 'System impact'} · {impact.disposition}</h4>
          <p>Owner: {impact.ownerId || 'Owner not provided'}</p>
          <p className="text-xs">Review: {impact.reviewedBy ? `${impact.reviewedBy} · ${overviewDate(impact.reviewedAt)}` : 'Review not recorded'}</p>
          {impact.rationale && <p>{impact.rationale}</p>}
          <details className="text-xs"><summary className="cursor-pointer">Affected evidence and documentation records</summary>
            <pre className="whitespace-pre-wrap break-words">{impact.affectedRecordsJson}</pre></details>
        </li>)}</ul>
        {!data.impacts.length && <p className="text-sm">No impact-review records returned.</p>}
        <div className="flex flex-wrap gap-2">
          <Link className={systemSecondaryAction} to={`${base}/conmon/changes`}>Review all observed changes</Link>
          <Link className={systemSecondaryAction} to={`${base}/conmon/impacts`}>Review investigation and documentation impacts</Link>
          <Link className={systemSecondaryAction} to={`${base}/evidence`}>Review evidence freshness</Link>
          <Link className={systemSecondaryAction} to={`${base}/poam`}>Review remediation plans</Link>
        </div>
      </section>
    </>}
  </section>;
}
