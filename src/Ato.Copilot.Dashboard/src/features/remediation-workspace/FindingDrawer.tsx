import { useEffect, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import SetupDialog from '../workspace-operations/SetupDialog';
import { createRemediationTask, getRemediationFinding, linkRemediationFindingTask, remediationWorkspaceError, type RemediationFindingDetail, type LinkedRemediationTask } from '../../api/remediationWorkspace';
import { systemPanel, systemSecondaryAction } from '../systems/SystemTaskPresentation';
import { FindingSource, RemediationEvidenceSection, RemediationExceptionsSection, RemediationHistorySection, remediationDate, remediationStatus } from './RemediationRecordSections';
import RemediationTaskEditor from './RemediationTaskEditor';

const tabs = ['Overview', 'Linked work', 'Evidence & verification', 'History'] as const;
export default function FindingDrawer({ systemId, id, onClose, onTask, owners, availableTasks, onChanged }: {
  systemId: string; id: string; onClose: () => void; onTask: (id: string) => void;
  owners: { id: string; name: string }[]; availableTasks: LinkedRemediationTask[]; onChanged: () => void;
}) {
  const [data, setData] = useState<RemediationFindingDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [tab, setTab] = useState<typeof tabs[number]>('Overview');
  const [creating, setCreating] = useState<string | null>(null);
  const [linking, setLinking] = useState(false);
  const [taskId, setTaskId] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    const controller = new AbortController(); setData(null); setLoading(true); setError(null);
    getRemediationFinding(systemId, id, controller.signal)
      .then(value => { if (!controller.signal.aborted) setData(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(remediationWorkspaceError(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, id, attempt]);
  if (creating && data) return <RemediationTaskEditor creating canSave={data.permissions.canCreateTask} owners={owners}
    initial={{ title: data.finding.title, description: '', controlId: data.finding.controlId, severity: data.finding.severity,
      assigneeId: '', dueDate: '', affectedResources: [], validationCriteria: '' }}
    onClose={() => setCreating(null)} onSave={async draft => {
      if (!data.permissions.canCreateTask) throw new Error('Permission denied: task creation is not authorized.');
      try {
        await createRemediationTask(systemId, { ...draft, assigneeId: draft.assigneeId || null, dueDate: draft.dueDate || null,
          findingId: id, operationId: creating });
        setCreating(null); setAttempt(value => value + 1); onChanged();
      } catch (reason) { throw new Error(remediationWorkspaceError(reason)); }
    }} />;
  const linkTask = async () => {
    if (!data || busy) return;
    const task = availableTasks.find(task => task.id === taskId && !task.findingId);
    if (!data.permissions.canManageRemediation || !task) { setError('Choose an authorized task without an originating finding.'); return; }
    setBusy(true); setError(null);
    try {
      await linkRemediationFindingTask(systemId, id, task.id, task.rowVersion);
      setLinking(false); setTaskId(''); setAttempt(value => value + 1); onChanged();
    } catch (reason) { setError(remediationWorkspaceError(reason)); }
    finally { setBusy(false); }
  };
  return <SetupDialog placement="right" expanded busy={busy} title={data?.finding.title ?? 'Finding details'}
    description={data ? `${data.finding.controlId} · ${data.finding.severity}` : 'Loading the retained finding'}
    onClose={onClose}><div className="rw-detail">
    {loading && <p role="status">Loading finding details…</p>}
    {error && <div role="alert" className="rw-error"><p>{error}</p><button type="button" className={systemSecondaryAction}
      onClick={() => setAttempt(value => value + 1)}>Retry finding</button></div>}
    {data && <>
      <div className="rw-tabs" role="group" aria-label="Finding detail views">{tabs.map(name =>
        <button type="button" key={name} aria-pressed={tab === name} onClick={() => setTab(name)}>{name}</button>)}</div>
      {tab === 'Overview' && <>
        <section className={systemPanel}><h3>Observed weakness</h3><p>{data.finding.description}</p>
          <dl><dt>Finding disposition</dt><dd>{data.finding.status}</dd><dt>Work status</dt><dd>{data.finding.workStatus}</dd>
            <dt>Owner</dt><dd>{data.finding.ownerName ?? 'Unassigned'}</dd><dt>Affected control</dt><dd>{data.finding.controlId || 'Not recorded'}</dd></dl>
        </section>
        <FindingSource systemId={systemId} source={data.finding.source} />
        <button type="button" className={systemSecondaryAction} onClick={() => setTab(data.finding.readyToVerify ? 'Evidence & verification' : 'Linked work')}>
          {data.finding.readyToVerify ? 'Review verification evidence' : 'Review linked corrective work'}</button>
      </>}
      {tab === 'Linked work' && <>
        <FindingSource systemId={systemId} source={data.finding.source} />
        <section className={systemPanel}><h3>Remediation tasks ({data.tasks.length})</h3>
          {!data.tasks.length && <p>No corrective work linked.</p>}
          {data.tasks.map(task => <article key={task.id}><button type="button" className="rw-title" onClick={() => onTask(task.id)}>{task.title}</button>
            <p>{task.taskNumber} · {remediationStatus(task.status)} · {task.assigneeName ?? 'Unassigned'}</p>
            <p>Due {remediationDate(task.dueDate)}</p><button type="button" className="rw-card-link" onClick={() => onTask(task.id)}>View task & external tickets</button></article>)}
          {data.permissions.canCreateTask && <button type="button" className={systemSecondaryAction}
            disabled={busy} onClick={() => setCreating(crypto.randomUUID())}>Create remediation task</button>}
          {data.permissions.canManageRemediation && <button type="button" className={systemSecondaryAction} disabled={busy}
            onClick={() => setLinking(true)}>Link existing task</button>}
          {linking && <form className="rw-detail" onSubmit={event => { event.preventDefault(); void linkTask(); }}>
            <label>Existing corrective task<select required disabled={busy} value={taskId} onChange={event => setTaskId(event.target.value)}>
              <option value="">Choose unassociated task</option>{availableTasks.filter(task => !task.findingId).map(task =>
                <option key={task.id} value={task.id}>{task.taskNumber} · {task.title}</option>)}</select></label>
            <p className="rw-muted">Only tasks without an originating finding can be linked. Existing finding provenance cannot be replaced.</p>
            <div className="rw-actions"><button type="button" className={systemSecondaryAction} disabled={busy} onClick={() => setLinking(false)}>Cancel task link</button>
              <button type="submit" className={systemSecondaryAction} disabled={busy || !taskId}>Link selected corrective task</button></div>
          </form>}
        </section>
        <section className={systemPanel}><h3>Linked POA&M items ({data.poams.length})</h3>
          {!data.poams.length && <p>No formal commitment is linked.</p>}
          {data.poams.map(poam => <article key={poam.id}><Link className="rw-title" to={`/systems/${systemId}/poam?detail=${encodeURIComponent(poam.id)}`}>{poam.weakness}</Link>
            <p>{poam.status} · {poam.owner} · Due {remediationDate(poam.scheduledCompletionDate)}</p></article>)}
          <Link className="rw-card-link" to={`/systems/${systemId}/poam?finding=${encodeURIComponent(id)}&action=add`}>Review or create a POA&M commitment</Link>
          <p className="rw-muted">One POA&M can track multiple tasks. Linked work does not duplicate or close the commitment.</p>
        </section>
        <RemediationExceptionsSection systemId={systemId} items={data.exceptions} />
      </>}
      {tab === 'Evidence & verification' && <>
        <RemediationEvidenceSection items={data.evidence} onError={reason => { setData(null); setError(remediationWorkspaceError(reason)); }} />
        <section className={systemPanel}><h3>Verify the correction</h3>
          <p>Review retained corrective evidence, retest observations and affected scope before recording a disposition.</p>
          {data.verificationBlockers.length > 0 && <ul>{data.verificationBlockers.map(message => <li key={message}>{message}</li>)}</ul>}
          <p className="rw-muted">A completed ticket, task or milestone does not close this finding or grant authorization.</p>
          {data.tasks.map(task => <button type="button" className={systemSecondaryAction} key={task.id}
            onClick={() => onTask(task.id)}>Review verification: {task.title}</button>)}
          {!data.permissions.canVerify && <p className="rw-muted">{data.permissions.reason ?? 'Your current access does not authorize verification.'}</p>}
        </section>
      </>}
      {tab === 'History' && <><RemediationHistorySection items={data.history} />
        <details><summary>Technical record identity</summary><p>{data.finding.id}</p></details></>}
    </>}
  </div></SetupDialog>;
}
