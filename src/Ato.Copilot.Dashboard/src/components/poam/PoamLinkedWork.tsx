import { useCallback, useRef, useState } from 'react';
import { getPoamWorkspace, createPoamWorkspaceTask, linkPoamWorkspaceTask, unlinkPoamWorkspaceTask } from '../../api/poamWorkspace';
import type { PoamWorkspaceTask } from '../../api/poamWorkspace';
import { usePoamRead } from '../../hooks/usePoamRead';
import { Link } from '../../features/workspaces/workspaceNavigation';
import { systemPanel, systemPrimaryAction, systemSecondaryAction } from '../../features/systems/SystemTaskPresentation';
import TaskTicketPanel from '../remediation/TaskTicketPanel';
import { poamErrorMessage } from '../../utils/poamErrors';
import AddDeviationDialog from '../AddDeviationDialog';

export default function PoamLinkedWork({ systemId, poamId, controlId, weakness, canManage, onChanged, onBusyChange }: {
  systemId: string; poamId: string; controlId: string; weakness: string; canManage: boolean;
  onChanged: () => void; onBusyChange?: (busy: boolean) => void;
}) {
  const fetcher = useCallback((signal: AbortSignal) => getPoamWorkspace(systemId, signal), [systemId]);
  const { data, loading, error, refresh } = usePoamRead(fetcher);
  const [mode, setMode] = useState<'link' | 'create' | null>(null);
  const [selectedTask, setSelectedTask] = useState('');
  const [unlinking, setUnlinking] = useState<PoamWorkspaceTask | null>(null);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [createdTask, setCreatedTask] = useState<string | null>(null);
  const [requestException, setRequestException] = useState(false);
  const intent = useRef<{ body: string; id: string } | null>(null);
  const poam = data?.poams.find(item => item.id === poamId);
  const finding = data?.findings.find(item => item.id === poam?.findingId);
  const tasks = data?.tasks.filter(item => poam?.taskIds.includes(item.id)) ?? [];
  const available = data?.tasks.filter(item => !poam?.taskIds.includes(item.id)) ?? [];
  const exceptions = data?.exceptions.filter(item => item.id === poam?.deviationId || item.poamEntryId === poamId) ?? [];
  const allowed = canManage && data?.permissions.canManageRemediation === true && !error;
  const mayCreate = allowed && data?.permissions.canCreateTasks === true;
  const revisions = (taskId: string) => ({
    expectedPoamRevision: poam?.rowVersion, expectedTaskRevision: data?.tasks.find(task => task.id === taskId)?.rowVersion,
  });
  const act = async (operation: () => Promise<unknown>, creating = false) => {
    if (!allowed || busy || creating && !mayCreate) return;
    setBusy(true); onBusyChange?.(true); setFailure(null);
    try { await operation(); refresh(); onChanged(); setMode(null); setUnlinking(null); setSelectedTask(''); }
    catch (reason) { setFailure(poamErrorMessage(reason)); }
    finally { setBusy(false); onBusyChange?.(false); }
  };
  const create = async () => {
    const request = { title: title.trim(), description: description.trim(), controlId, severity: finding?.severity ?? 'Medium', findingId: finding?.id, dueDate: dueDate || undefined };
    const serialized = JSON.stringify(request);
    if (intent.current?.body !== serialized) intent.current = { body: serialized, id: crypto.randomUUID() };
    const taskId = createdTask ?? (await createPoamWorkspaceTask(systemId, { ...request, requestId: intent.current.id })).id;
    setCreatedTask(taskId);
    const saved = await getPoamWorkspace(systemId);
    const task = saved.tasks.find(item => item.id === taskId);
    if (!task || !poam?.rowVersion || !saved.permissions.canManageRemediation)
      throw new Error('The saved task or current linking permission could not be confirmed. Refresh connected work before retrying.');
    await linkPoamWorkspaceTask(systemId, poamId, taskId, {
      expectedPoamRevision: poam.rowVersion, expectedTaskRevision: task.rowVersion,
    });
    setCreatedTask(null); setTitle(''); setDescription(''); setDueDate(''); intent.current = null;
  };
  if (!data && loading) return <p role="status" className="text-sm">Loading connected work…</p>;
  if (!data && error) return <div className={systemPanel}><p role="alert" className="text-sm text-red-700 dark:text-red-300">{error.message}</p><button className={`${systemSecondaryAction} mt-3`} onClick={refresh}>Retry connected work</button></div>;
  if (!poam) return <div className={systemPanel}><p role="alert" className="text-sm">This commitment is not available in the current system workspace.</p><button className={`${systemSecondaryAction} mt-3`} onClick={refresh}>Refresh connected work</button></div>;
  const input = 'mt-1 w-full rounded-md border border-slate-300 bg-white p-2 text-sm dark:border-slate-600 dark:bg-slate-800';
  return <div className="space-y-4">
    {(failure || error) && <div className={systemPanel}><p role="alert" className="text-sm text-red-700 dark:text-red-300">{failure ?? error?.message}</p><button className={`${systemSecondaryAction} mt-3`} disabled={busy} onClick={refresh}>Refresh connected work</button></div>}
    <section className={systemPanel}><h3 className="text-sm font-semibold">Assessment &amp; finding</h3>
      {finding ? <>
        <p className="mt-2 text-sm font-medium">{finding.title}</p>
        <p className="mt-1 text-xs text-slate-500 dark:text-slate-300">{finding.provenance?.sourceName ?? finding.source} · {finding.controlId}</p>
        {finding.provenance?.plan ? <p className="mt-1 text-xs">Plan: {finding.provenance.plan.title} · Revision {finding.provenance.plan.revision}</p> : <p className="mt-1 text-xs text-slate-500 dark:text-slate-300">No original plan revision recorded.</p>}
        <div className="mt-3 flex flex-wrap gap-3 text-xs">
          <Link className="text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/remediation?finding=${encodeURIComponent(finding.id)}`}>View finding</Link>
          {finding.assessmentId && finding.provenance?.sourceType !== 'Manual' && <Link className="text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/assessments?result=${encodeURIComponent(finding.importRecordId ? `import:${finding.importRecordId}` : `assessment:${finding.assessmentId}`)}`}>View assessment</Link>}
        </div>
      </> : <p className="mt-2 text-sm text-slate-500 dark:text-slate-300">Manual commitment — no retained finding linked.</p>}
    </section>
    <section className={systemPanel}><div className="flex flex-wrap items-center justify-between gap-2"><h3 className="text-sm font-semibold">Linked tasks ({tasks.length})</h3>
      <button className={systemSecondaryAction} disabled={!allowed || busy} onClick={() => { setMode('link'); setFailure(null); }}>Link task</button></div>
      <div className="mt-3 space-y-3">
        {tasks.length === 0 && <p className="text-sm text-slate-500 dark:text-slate-300">No remediation tasks linked.</p>}
        {tasks.map(task => <article key={task.id} className="rounded-lg border border-slate-200 p-3 dark:border-slate-700">
          <div className="flex items-start justify-between gap-2"><div className="min-w-0"><p className="text-xs text-indigo-700 dark:text-indigo-300">{task.taskNumber}</p><h4 className="mt-1 break-words text-sm font-semibold">{task.title}</h4></div>
            <button className="shrink-0 text-xs text-slate-500 underline disabled:opacity-50 dark:text-slate-300" disabled={!allowed || busy} onClick={() => setUnlinking(task)}>Unlink {task.taskNumber}</button></div>
          <p className="mt-2 text-xs">{task.status} · {task.assigneeName ?? 'Unassigned'} · Verification: {task.verificationStatus}</p>
          {task.poamIds.length > 1 && <p className="mt-1 text-xs text-indigo-700 dark:text-indigo-300">Shared with {task.poamIds.length - 1} other {task.poamIds.length === 2 ? 'commitment' : 'commitments'}</p>}
          <Link className="mt-2 inline-block text-xs text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/remediation?task=${encodeURIComponent(task.id)}&view=tasks`}>Review task &amp; verification</Link>
          <details className="mt-3"><summary className="cursor-pointer text-xs font-medium">Task scope &amp; instructions</summary>
            <div className="mt-2 space-y-3 text-xs">
              <p className="whitespace-pre-wrap">{task.description || 'Corrective description not recorded.'}</p>
              <div><p className="font-medium">Affected resources</p>{task.affectedResources?.length
                ? <ul className="mt-1 list-inside list-disc break-all">{task.affectedResources.map(resource => <li key={resource}>{resource}</li>)}</ul>
                : <p className="mt-1 text-slate-500 dark:text-slate-300">No affected resources recorded.</p>}</div>
              <div><p className="font-medium">Validation criteria</p><p className="mt-1 whitespace-pre-wrap">{task.validationCriteria || 'No validation criteria recorded.'}</p></div>
              {task.remediationScript && <div><p className="font-medium">Retained script{task.remediationScriptType ? ` · ${task.remediationScriptType}` : ''}</p><pre className="mt-2 overflow-x-auto rounded bg-slate-100 p-2 text-xs dark:bg-slate-950">{task.remediationScript}</pre></div>}
            </div>
          </details>
          <details className="mt-3"><summary className="cursor-pointer text-xs font-medium">Task ticket</summary><div className="mt-2"><TaskTicketPanel systemId={systemId} taskId={task.id} /></div></details>
          <details className="mt-3"><summary className="cursor-pointer text-xs font-medium">Retained evidence ({task.evidence.length})</summary>
            {task.evidence.length ? <ul className="mt-2 space-y-2">{task.evidence.map(evidence => <li key={evidence.id} className="text-xs"><p>{evidence.name}</p><p className="break-all text-slate-500 dark:text-slate-300">Hash: {evidence.contentHash}</p></li>)}</ul> : <p className="mt-2 text-xs">No retained task evidence.</p>}
            {task.verificationNotes && <p className="mt-2 whitespace-pre-wrap text-xs">Review: {task.verificationNotes}</p>}
          </details>
          {unlinking?.id === task.id && <div className="mt-3 rounded border border-amber-200 p-3 text-xs dark:border-amber-800"><p>Remove only this relationship? The task and its other commitments are retained.</p>
            <div className="mt-2 flex flex-wrap gap-2"><button className={systemSecondaryAction} disabled={!allowed || busy} onClick={() => void act(() => unlinkPoamWorkspaceTask(systemId, poamId, task.id, revisions(task.id)))}>Confirm unlink {task.taskNumber}</button><button className={systemSecondaryAction} disabled={busy} onClick={() => setUnlinking(null)}>Cancel</button></div></div>}
        </article>)}
      </div>
      {mode === 'link' && <div className="mt-4 space-y-3 rounded-lg border border-slate-200 p-3 dark:border-slate-700">
        <label className="block text-sm">Existing task<select aria-label="Existing task" className={input} disabled={busy} value={selectedTask} onChange={event => setSelectedTask(event.target.value)}><option value="">Choose a task</option>{available.map(task => <option key={task.id} value={task.id}>{task.taskNumber} · {task.title}{task.poamIds.length ? ' · Shared work' : ''}</option>)}</select></label>
        {!available.length && <p className="text-xs">No unlinked tasks available. Create a task instead.</p>}
        <div className="flex flex-wrap gap-2"><button className={systemPrimaryAction} disabled={!allowed || busy || !selectedTask} onClick={() => void act(() => linkPoamWorkspaceTask(systemId, poamId, selectedTask, revisions(selectedTask)))}>Link selected task</button><button className={systemSecondaryAction} disabled={busy} onClick={() => setMode(null)}>Cancel</button></div>
      </div>}
      {mode === 'create' && <form className="mt-4 space-y-3 rounded-lg border border-slate-200 p-3 dark:border-slate-700" onSubmit={event => { event.preventDefault(); void act(create, true); }}>
        <h4 className="text-sm font-semibold">Create linked remediation task</h4>
        {createdTask && <p className="text-xs text-amber-700 dark:text-amber-300">Task {createdTask} was created. Retry linking below; another task will not be created.</p>}
        <label className="block text-sm">Task title<input className={input} required maxLength={500} disabled={busy || !!createdTask} value={title} onChange={event => setTitle(event.target.value)} /></label>
        <label className="block text-sm">Corrective work<textarea className={input} required maxLength={8000} rows={3} disabled={busy || !!createdTask} value={description} onChange={event => setDescription(event.target.value)} /></label>
        <label className="block text-sm">Task due date<input className={input} type="date" disabled={busy || !!createdTask} value={dueDate} onChange={event => setDueDate(event.target.value)} /></label>
        <p className="text-xs text-slate-500 dark:text-slate-300">Control {controlId}. Assign an owner and retain verification in the task workspace.</p>
        <div className="flex flex-wrap gap-2"><button className={systemPrimaryAction} disabled={!mayCreate || busy || !title.trim() || !description.trim()}>{createdTask ? 'Retry linking created task' : 'Create and link task'}</button><button type="button" className={systemSecondaryAction} disabled={busy} onClick={() => setMode(null)}>Cancel</button></div>
      </form>}
      {!mode && <button className={`${systemSecondaryAction} mt-3`} disabled={!mayCreate || busy} onClick={() => { setMode('create'); setTitle(`Remediate ${controlId}`); setDescription(weakness); setFailure(null); }}>Create task</button>}
      <p className="mt-3 text-xs text-slate-500 dark:text-slate-300">Link existing work without duplicating tasks or tickets. Completion never cascades implicitly.</p>
    </section>
    <section className={systemPanel}><h3 className="text-sm font-semibold">Related exceptions</h3>
      {exceptions.length ? <ul className="mt-3 space-y-2">{exceptions.map(exception => <li key={exception.id} className="rounded-lg border border-slate-200 p-3 text-sm dark:border-slate-700">
        <p className="font-medium">{exception.type} · {exception.status}</p>
        <p className="mt-1 text-xs">{exception.isEffective ? 'Effective approved exception' : 'Not effective — no current risk acceptance'}</p>
        <p className="mt-2 text-xs">{exception.justification}</p><p className="mt-1 text-xs">Expires: {new Date(exception.expirationDate).toLocaleDateString(undefined, { timeZone: 'UTC' })}</p>
        {exception.reviewedBy || exception.reviewerRole || exception.reviewedAt ? <div className="mt-2 space-y-1 text-xs">
          <p>Reviewed by {exception.reviewedBy ?? 'Identity not recorded'}{exception.reviewerRole ? ` · ${exception.reviewerRole}` : ''}</p>
          {exception.reviewedAt && <p>Reviewed: {new Date(exception.reviewedAt).toLocaleString()}</p>}
        </div> : <p className="mt-2 text-xs text-slate-500 dark:text-slate-300">Review details not recorded.</p>}
        {exception.compensatingControls && <div className="mt-2 text-xs"><p className="font-medium">Compensating controls</p><p className="mt-1 whitespace-pre-wrap">{exception.compensatingControls}</p></div>}
      </li>)}</ul> : <p className="mt-3 text-sm text-slate-500 dark:text-slate-300">No exception linked.</p>}
      <Link className="mt-3 inline-block text-sm text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/deviations`}>Review exception records</Link>
      <button className={`${systemSecondaryAction} mt-3 block`} disabled={!allowed || busy} onClick={() => setRequestException(true)}>Start exception request</button>
      <p className="mt-2 text-xs text-slate-500 dark:text-slate-300">Existing exception linking is not available here; review the retained exception records separately.</p>
      <p className="mt-3 text-xs text-slate-500 dark:text-slate-300">Exceptions require a separate authorized decision. Linking does not accept risk or change deadlines.</p>
    </section>
    {requestException && <AddDeviationDialog systemId={systemId} initialPoamEntryId={poamId} initialFindingId={finding?.id} initialControlId={controlId}
      onClose={() => setRequestException(false)} onCreated={() => { setRequestException(false); refresh(); onChanged(); }} />}
  </div>;
}
