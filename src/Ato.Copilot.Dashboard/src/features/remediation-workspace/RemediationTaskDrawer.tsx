import { useEffect, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import SetupDialog from '../workspace-operations/SetupDialog';
import { getRemediationTask, moveRemediationTask, saveRemediationTask, linkRemediationPoamTask, unlinkRemediationPoamTask,
  verifyRemediationTask, linkRemediationEvidence, remediationWorkspaceError, type RemediationTaskDetail, type RemediationPoamReference } from '../../api/remediationWorkspace';
import { systemPanel, systemPrimaryAction, systemSecondaryAction } from '../systems/SystemTaskPresentation';
import { RemediationEvidenceSection, RemediationHistorySection, remediationDate, remediationStatus } from './RemediationRecordSections';
import RemediationTaskEditor from './RemediationTaskEditor';
import TaskTicketPanel from '../../components/remediation/TaskTicketPanel';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import TaskEvidencePicker from './TaskEvidencePicker';

export default function RemediationTaskDrawer({ systemId, id, onClose, owners, poams, onChanged }: {
  systemId: string; id: string; onClose: () => void; owners: { id: string; name: string }[];
  poams: RemediationPoamReference[]; onChanged: () => void;
}) {
  const [data, setData] = useState<RemediationTaskDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [tab, setTab] = useState('Overview');
  const [editing, setEditing] = useState(false);
  const [moving, setMoving] = useState(false);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState('');
  const [comment, setComment] = useState('');
  const [linking, setLinking] = useState(false);
  const [poamId, setPoamId] = useState('');
  const [unlinking, setUnlinking] = useState<string | null>(null);
  const [verification, setVerification] = useState<'Passed' | 'Failed' | ''>('');
  const [verificationNotes, setVerificationNotes] = useState('');
  const [showEvidencePicker, setShowEvidencePicker] = useState(false);
  const session = useWorkspaceSession();
  const canMove = !!data && (data.permissions.canMoveAnyTasks === true
    || data.permissions.canMoveTasks === true && data.task.assigneeId === session?.identity?.oid);
  useEffect(() => {
    const controller = new AbortController(); setData(null); setLoading(true); setError(null);
    getRemediationTask(systemId, id, controller.signal)
      .then(value => { if (!controller.signal.aborted) setData(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(remediationWorkspaceError(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, id, attempt]);
  if (editing && data) return <RemediationTaskEditor canSave={data.permissions.canManageRemediation} owners={owners}
    initial={{ title: data.task.title, description: data.task.description, controlId: data.task.controlId,
      severity: data.task.severity, assigneeId: data.task.assigneeId ?? '', dueDate: data.task.dueDate?.slice(0, 10) ?? '',
      affectedResources: data.task.affectedResources, validationCriteria: data.task.validationCriteria ?? '' }}
    onClose={() => setEditing(false)} onSave={async draft => {
      if (!data.permissions.canManageRemediation) throw new Error('Permission denied: task editing is not authorized.');
      try {
        await saveRemediationTask(systemId, id, { ...draft, assigneeId: draft.assigneeId || null,
          assigneeName: owners.find(owner => owner.id === draft.assigneeId)?.name ?? data.task.assigneeName,
          dueDate: draft.dueDate || null, expectedRowVersion: data.task.rowVersion });
        setEditing(false); setAttempt(value => value + 1); onChanged();
      } catch (reason) { throw new Error(remediationWorkspaceError(reason)); }
    }} />;
  const move = async () => {
    if (!data || busy) return;
    if (!canMove || !data.allowedTransitions.includes(status)) {
      setError('This task transition is not authorized. Refresh the task.'); return;
    }
    setBusy(true); setError(null);
    try {
      await moveRemediationTask(systemId, id, { expectedRowVersion: data.task.rowVersion, status, comment });
      setMoving(false); setAttempt(value => value + 1); onChanged();
    } catch (reason) { setError(remediationWorkspaceError(reason)); }
    finally { setBusy(false); }
  };
  const saveVerification = async () => {
    if (!data || busy) return;
    if (!data.permissions.canVerify || !verification || !verificationNotes.trim()) {
      setError('Select a verification result and record reviewer notes with authorized access.'); return;
    }
    setBusy(true); setError(null);
    try {
      await verifyRemediationTask(systemId, id, { rowVersion: data.task.rowVersion, status: verification, notes: verificationNotes.trim() });
      setVerification(''); setVerificationNotes(''); setAttempt(value => value + 1); onChanged();
    } catch (reason) { setError(remediationWorkspaceError(reason)); }
    finally { setBusy(false); }
  };
  const linkEvidence = async (evidenceId: string) => {
    if (!data || busy) return;
    if (!data.permissions.canManageRemediation || !evidenceId.trim()) { setError('Choose an evidence record to link.'); return; }
    setBusy(true); setError(null);
    try {
      await linkRemediationEvidence(systemId, id, { rowVersion: data.task.rowVersion, evidenceId: evidenceId.trim() });
      setShowEvidencePicker(false); setAttempt(value => value + 1); onChanged();
    } catch (reason) { setError(remediationWorkspaceError(reason)); }
    finally { setBusy(false); }
  };
  const link = async (remove: boolean) => {
    if (!data || busy) return;
    const targetId = remove ? unlinking : poamId;
    const poam = (remove ? data.poams : poams).find(item => item.id === targetId);
    if (!poam || !data.permissions.canManageRemediation) { setError('Select an authorized POA&M commitment.'); return; }
    setBusy(true); setError(null);
    try {
      await (remove ? unlinkRemediationPoamTask : linkRemediationPoamTask)(systemId, poam.id, id, {
        expectedPoamRevision: poam.rowVersion, expectedTaskRevision: data.task.rowVersion,
      });
      setLinking(false); setUnlinking(null); setAttempt(value => value + 1); onChanged();
    } catch (reason) { setError(remediationWorkspaceError(reason)); }
    finally { setBusy(false); }
  };
  return <SetupDialog placement="right" expanded busy={busy} onClose={onClose}
    title={data?.task.title ?? 'Remediation task'} description={data ? `${data.task.taskNumber} · ${data.task.controlId}` : 'Loading retained work'}>
    <div className="rw-detail">
      {loading && <p role="status">Loading remediation task…</p>}
      {error && <div role="alert" className="rw-error"><p>{error}</p><button type="button" className={systemSecondaryAction}
        onClick={() => setAttempt(value => value + 1)}>Retry task</button></div>}
      {data && <>
        <div className="rw-tabs" role="group" aria-label="Task detail views">{['Overview', 'Linked work', 'Evidence & verification', 'History'].map(name =>
          <button type="button" key={name} aria-pressed={tab === name} onClick={() => setTab(name)}>{name}</button>)}</div>
        {tab === 'Overview' && <section className={systemPanel}><h3>Corrective work</h3><p>{data.task.description}</p>
          <dl><dt>Local task status</dt><dd>{remediationStatus(data.task.status)}</dd><dt>Owner</dt><dd>{data.task.assigneeName ?? 'Unassigned'}</dd>
            <dt>Due date</dt><dd>{remediationDate(data.task.dueDate)}</dd><dt>Severity</dt><dd>{data.task.severity}</dd></dl>
          <details><summary>Affected scope & guidance</summary>
            {data.task.affectedResources.length ? <ul>{data.task.affectedResources.map(resource => <li key={resource}>{resource}</li>)}</ul> : <p>No affected resources recorded.</p>}
            <p>{data.task.validationCriteria ?? 'Verification criteria are not recorded.'}</p>
            {data.task.remediationScript && <pre className="overflow-x-auto whitespace-pre-wrap rounded border p-3 text-xs">{data.task.remediationScript}</pre>}
          </details>
          {data.permissions.canManageRemediation && <div className="rw-actions">
            <button type="button" className={systemSecondaryAction} disabled={data.task.status === 'Done'} onClick={() => setEditing(true)}>Edit task</button>
            </div>}
          {canMove && <button type="button" className={systemSecondaryAction} disabled={!data.allowedTransitions.length}
            onClick={() => { setMoving(true); setStatus(''); setComment(''); }}>Change task status</button>}
          {moving && <form className="rw-detail" onSubmit={event => { event.preventDefault(); void move(); }}>
            <label>New task status<select value={status} disabled={busy} onChange={event => setStatus(event.target.value)} required>
              <option value="">Choose transition</option>{data.allowedTransitions.map(value => <option key={value} value={value}>{remediationStatus(value)}</option>)}</select></label>
            <label>Transition notes<textarea maxLength={3800} value={comment} disabled={busy} onChange={event => setComment(event.target.value)} /></label>
            <small>{comment.length}/3800 characters</small>
            <p className="rw-muted">The server enforces transition, ownership and verification requirements. This changes only the task.</p>
            <div className="rw-actions"><button type="button" disabled={busy} className={systemSecondaryAction} onClick={() => setMoving(false)}>Cancel transition</button>
              <button type="submit" disabled={busy || !status} className={systemPrimaryAction}>Save task status</button></div>
          </form>}
        </section>}
        {tab === 'Linked work' && <>
          {data.task.findingId ? <Link className="rw-card-link" to={`/systems/${systemId}/remediation?finding=${encodeURIComponent(data.task.findingId)}`}>View originating finding</Link>
            : <p>No originating finding is recorded for this task.</p>}
          <section className={systemPanel}><h3>Linked POA&M items ({data.poams.length})</h3>
            {!data.poams.length && <p>No formal commitment linked.</p>}
            {data.poams.map(poam => <article key={poam.id}><Link className="rw-title" to={`/systems/${systemId}/poam?detail=${encodeURIComponent(poam.id)}`}>{poam.weakness}</Link>
              <p>{poam.status} · {poam.owner} · Due {remediationDate(poam.scheduledCompletionDate)}</p>
              {data.permissions.canManageRemediation && <button type="button" className="rw-card-link" disabled={busy}
                onClick={() => setUnlinking(poam.id)}>Unlink from this task</button>}</article>)}
            {data.permissions.canManageRemediation && <button type="button" className={systemSecondaryAction} disabled={busy}
              onClick={() => { setLinking(true); setPoamId(''); }}>Link existing POA&M</button>}
            {linking && <form className="rw-detail" onSubmit={event => { event.preventDefault(); void link(false); }}>
              <label>POA&M commitment<select required disabled={busy} value={poamId} onChange={event => setPoamId(event.target.value)}>
                <option value="">Choose existing commitment</option>{poams.map(poam => <option key={poam.id} value={poam.id}
                  disabled={data.poams.some(linked => linked.id === poam.id)}>{poam.controlId} · {poam.weakness}{data.poams.some(linked => linked.id === poam.id) ? ' (Already linked)' : ''}</option>)}</select></label>
              <p className="rw-muted">The same task can support multiple commitments. Linking creates no task, ticket or POA&M copy.</p>
              <div className="rw-actions"><button type="button" className={systemSecondaryAction} disabled={busy} onClick={() => setLinking(false)}>Cancel link</button>
                <button type="submit" className={systemPrimaryAction} disabled={busy || !poamId}>Confirm link</button></div>
            </form>}
            {unlinking && <div className="rw-detail"><p>Remove only this task-to-POA&M relationship? Both records, external tickets,
              other task links and historical evidence are preserved. The commitment will no longer list this task as linked work.</p>
              <div className="rw-actions"><button type="button" className={systemSecondaryAction} disabled={busy} onClick={() => setUnlinking(null)}>Keep link</button>
                <button type="button" className={systemSecondaryAction} disabled={busy} onClick={() => void link(true)}>Confirm unlink</button></div></div>}
          </section>
          <TaskTicketPanel key={id} systemId={systemId} taskId={id} />
        </>}
        {tab === 'Evidence & verification' && <>
          <RemediationEvidenceSection items={data.evidence} onError={reason => { setData(null); setError(remediationWorkspaceError(reason)); }} />
          {data.permissions.canManageRemediation && <button type="button" className={systemSecondaryAction} disabled={busy}
            onClick={() => setShowEvidencePicker(value => !value)}>{showEvidencePicker ? 'Cancel evidence selection' : 'Link corrective evidence'}</button>}
          {showEvidencePicker && data.permissions.canManageRemediation && <TaskEvidencePicker systemId={systemId} busy={busy} onLink={linkEvidence} />}
          <section className={systemPanel}><h3>Verification</h3><p>{data.task.validationCriteria ?? 'No verification criteria recorded.'}</p>
            <p>Recorded result: {data.task.verificationStatus ?? 'Not recorded'}</p>
            {data.task.verificationNotes && <p>{data.task.verificationNotes}</p>}
            {data.verificationBlockers.length > 0 && <ul>{data.verificationBlockers.map(message => <li key={message}>{message}</li>)}</ul>}
            <p className="rw-muted">Task completion, finding disposition and POA&M closure remain separate. External closure does not verify this task.</p>
            {data.permissions.canVerify && <form className="rw-detail" onSubmit={event => { event.preventDefault(); void saveVerification(); }}>
              <label>Verification result<select required disabled={busy} value={verification}
                onChange={event => setVerification(event.target.value === 'Passed' || event.target.value === 'Failed' ? event.target.value : '')}>
                <option value="">Choose a result</option><option value="Passed">Passed</option><option value="Failed">Failed</option></select></label>
              <label>Reviewer notes & retest outcome<textarea maxLength={3900} required disabled={busy} rows={4} value={verificationNotes}
                onChange={event => setVerificationNotes(event.target.value)} /></label>
              <small>{verificationNotes.length}/3900 characters</small>
              <p className="rw-muted">This records your human review, not a new automated retest. Review retained evidence and affected scope first. Saving does not change task status.</p>
              <button type="submit" className={systemPrimaryAction} disabled={busy || !verification || !verificationNotes.trim()}>Save verification</button>
            </form>}
          </section>
        </>}
        {tab === 'History' && <><RemediationHistorySection items={data.history} />
          <details><summary>Technical record identity</summary><p>{data.task.id}</p><p>Revision: {data.task.rowVersion}</p></details></>}
      </>}
    </div>
  </SetupDialog>;
}
