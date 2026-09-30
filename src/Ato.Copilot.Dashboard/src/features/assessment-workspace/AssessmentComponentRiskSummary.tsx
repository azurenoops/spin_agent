import { useEffect, useState } from 'react';
import { getAssessmentComponentRisks } from '../../api/components';
import { assessmentWorkspaceError } from '../../api/assessmentWorkspace';
import type { AssessmentComponentRisks } from '../../types/dashboard';

export default function AssessmentComponentRiskSummary({ systemId, assessmentId }: { systemId: string; assessmentId: string }) {
  const [open, setOpen] = useState(false);
  const [data, setData] = useState<AssessmentComponentRisks | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    if (!open) return;
    const controller = new AbortController(); setData(null); setError(null);
    getAssessmentComponentRisks(systemId, assessmentId, controller.signal)
      .then(value => {
        if (controller.signal.aborted) return;
        if (!Array.isArray(value.componentRisks) || typeof value.totalFindingCount !== 'number'
          || typeof value.unlinkedFindingCount !== 'number') throw new Error('The component-risk response is unavailable.');
        setData(value);
      })
      .catch(reason => { if (!controller.signal.aborted) setError(assessmentWorkspaceError(reason)); });
    return () => controller.abort();
  }, [systemId, assessmentId, open, attempt]);
  return <details className="aw-disclosure" open={open} onToggle={event => setOpen(event.currentTarget.open)}>
    <summary>Current component risk summary</summary>
    {open && <>
      <p>This is the current component/remediation view for this assessment, separate from retained collection scope and human review.</p>
      {error && <div className="aw-error" role="alert"><p>{error}</p><button type="button" onClick={() => setAttempt(v => v + 1)}>Retry component risks</button></div>}
      {!error && !data && <p role="status">Loading current component risks…</p>}
      {data && <>
        <p>{data.totalFindingCount} recorded findings · {data.unlinkedFindingCount} not linked to a component.</p>
        {data.componentRisks.length ? <div className="aw-table-scroll"><table aria-label="Assessment component risks"><thead><tr>
          <th>Component</th><th>Open findings</th><th>Highest severity</th><th>Overdue remediation</th>
        </tr></thead><tbody>{data.componentRisks.map(component => <tr key={component.componentId}>
          <td>{component.componentName}</td><td>{component.openFindingCount}</td><td>{component.highestSeverity}</td><td>{component.overdueRemediationCount}</td>
        </tr>)}</tbody></table></div> : <p>No component-linked findings were returned.</p>}
      </>}
    </>}
  </details>;
}
