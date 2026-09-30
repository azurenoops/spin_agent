import { useEffect, useState } from 'react';
import { Download, Plus, RefreshCw } from 'lucide-react';
import { Link, useSearchParams } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import { SystemTaskHeading, systemPanel, systemPrimaryAction, systemSecondaryAction } from '../systems/SystemTaskPresentation';
import { createRemediationTask, getRemediationWorkspace, remediationWorkspaceError, type RemediationWorkspace } from '../../api/remediationWorkspace';
import { exportTasks } from '../../api/remediation';
import FindingsQueue from './FindingsQueue';
import FindingDrawer from './FindingDrawer';
import RemediationTaskDrawer from './RemediationTaskDrawer';
import RemediationTaskEditor from './RemediationTaskEditor';
import CreateFindingDrawer from './CreateFindingDrawer';
import './RemediationWorkspace.css';

export default function FindingsWorkspace({ systemId }: { systemId: string }) {
  const session = useWorkspaceSession();
  const accessKey = JSON.stringify([session?.identity?.oid, session?.systemAccess]);
  return <ScopedFindingsWorkspace key={`${systemId}:${accessKey}`} systemId={systemId} />;
}
function ScopedFindingsWorkspace({ systemId }: { systemId: string }) {
  const [params, setParams] = useSearchParams();
  const [data, setData] = useState<RemediationWorkspace | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [operationError, setOperationError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [exporting, setExporting] = useState(false);
  const [creatingFinding, setCreatingFinding] = useState(false);
  const [creatingTask, setCreatingTask] = useState<string | null>(null);
  const findingId = params.get('finding');
  const taskId = params.get('task');
  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError(null);
    getRemediationWorkspace(systemId, controller.signal)
      .then(value => { if (!controller.signal.aborted) setData(value); })
      .catch(reason => { if (!controller.signal.aborted) { setData(null); setError(remediationWorkspaceError(reason)); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, attempt]);
  const select = (key: 'finding' | 'task', value: string | null) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value); else next.delete(key);
    setParams(next, { preventScrollReset: true });
  };
  const exportWork = async () => {
    if (exporting) return;
    setExporting(true); setOperationError(null);
    try { await exportTasks(systemId); }
    catch (reason) { setOperationError(remediationWorkspaceError(reason)); }
    finally { setExporting(false); }
  };
  return <div className="rw-workspace">
    <SystemTaskHeading title="Resolve assessment findings"
      description="Trace each issue to its evidence, assign work, and verify the outcome."
      action={<div className="rw-actions">{data?.permissions.canCreateFinding && <button type="button" className={systemPrimaryAction}
        disabled={loading} onClick={() => setCreatingFinding(true)}><Plus size={16} aria-hidden="true" />Add finding</button>}
        <button type="button" className={systemSecondaryAction} disabled={loading}
        onClick={() => setAttempt(value => value + 1)}><RefreshCw size={16} aria-hidden="true" />Refresh</button>
        <button type="button" className={systemSecondaryAction} disabled={!data?.tasks.length || exporting} onClick={() => void exportWork()}>
          <Download size={16} aria-hidden="true" />{exporting ? 'Exporting…' : 'Export tasks'}</button></div>} />
    {loading && <p role="status">Loading findings and linked work…</p>}
    {error && <div role="alert" className="rw-error"><p>{error}</p>
      <button type="button" className={systemSecondaryAction} onClick={() => setAttempt(value => value + 1)}>Retry workspace</button></div>}
    {operationError && <p role="alert" className="rw-error">{operationError}</p>}
    {data && <>
      {data.warnings.length > 0 && <section className={systemPanel} aria-label="Workspace notices"><ul>{data.warnings.map(warning => <li key={warning}>{warning}</li>)}</ul></section>}
      <FindingsQueue items={data.findings.map(item => ({ id: item.id, title: item.title, controlId: item.controlId,
        severity: item.severity, disposition: item.status, owner: item.ownerName, workStatus: item.workStatus,
        readyToVerify: item.readyToVerify, closed: item.isClosed, sourceName: item.source?.name ?? null, planRevision: item.source?.planRevision ?? null }))}
        onOpen={id => select('finding', id)}
        emptyAction={<Link className={systemSecondaryAction} to={`/systems/${systemId}/assessments`}>Review assessment results</Link>} />
      {(data.tasks.some(task => !task.findingId) || data.permissions.canCreateTask) && <details className={systemPanel}>
        <summary>Additional remediation work ({data.tasks.filter(task => !task.findingId).length})</summary>
        <div className="mt-3 space-y-3 text-sm"><p>Tasks without a recorded finding remain separate; they are not counted as findings.</p>
          {data.tasks.filter(task => !task.findingId).map(task => <article key={task.id}>
            <button type="button" className="rw-title" onClick={() => select('task', task.id)}>{task.title}</button>
            <p>{task.taskNumber} · {task.status} · {task.assigneeName ?? 'Unassigned'}</p></article>)}
          {data.permissions.canCreateTask && <button type="button" className={systemSecondaryAction}
            onClick={() => setCreatingTask(crypto.randomUUID())}>Create standalone task</button>}</div>
      </details>}
      <details className={systemPanel}><summary>How findings and corrective work connect</summary>
        <div className="mt-3 space-y-3 text-sm"><p>Assessment → Finding → Remediation tasks ↔ POA&M items</p>
          <p>External tickets link to tasks. Exceptions retain their own authorized decision lifecycle.</p>
          <div className="rw-actions"><Link className="rw-card-link" to={`/systems/${systemId}/assessments`}>Assessment results</Link>
            <Link className="rw-card-link" to={`/systems/${systemId}/poam`}>POA&M commitments</Link>
            <Link className="rw-card-link" to={`/systems/${systemId}/poam?view=ticketing`}>Manage ticketing connections</Link></div>
          <p className="rw-muted">Document preparation is separate from eMASS submission and the Authorizing Official's decision.</p></div>
      </details>
      {findingId && !taskId && <FindingDrawer key={findingId} systemId={systemId} id={findingId} owners={data.owners} availableTasks={data.tasks}
        onChanged={() => setAttempt(value => value + 1)} onClose={() => select('finding', null)} onTask={id => select('task', id)} />}
      {taskId && <RemediationTaskDrawer key={taskId} systemId={systemId} id={taskId} owners={data.owners} poams={data.poams}
        onChanged={() => setAttempt(value => value + 1)} onClose={() => select('task', null)} />}
      {creatingFinding && <CreateFindingDrawer systemId={systemId} canCreate={data.permissions.canCreateFinding}
        onClose={() => setCreatingFinding(false)} onCreated={() => setAttempt(value => value + 1)} />}
      {creatingTask && <RemediationTaskEditor creating canSave={data.permissions.canCreateTask} owners={data.owners}
        initial={{ title: '', description: '', controlId: '', severity: 'Medium', assigneeId: '', dueDate: '', affectedResources: [], validationCriteria: '' }}
        onClose={() => setCreatingTask(null)} onSave={async draft => {
          if (!data.permissions.canCreateTask) throw new Error('Permission denied: task creation is not authorized.');
          try {
            await createRemediationTask(systemId, { ...draft, assigneeId: draft.assigneeId || null, dueDate: draft.dueDate || null, operationId: creatingTask });
            setCreatingTask(null); setAttempt(value => value + 1);
          } catch (reason) { throw new Error(remediationWorkspaceError(reason)); }
        }} />}
    </>}
  </div>;
}
