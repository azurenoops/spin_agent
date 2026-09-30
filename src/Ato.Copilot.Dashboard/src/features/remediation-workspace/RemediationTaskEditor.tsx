import { useEffect, useRef, useState } from 'react';
import SetupDialog from '../workspace-operations/SetupDialog';
import { systemPrimaryAction, systemSecondaryAction } from '../systems/SystemTaskPresentation';
import './RemediationWorkspace.css';

export interface TaskDraft {
  title: string; description: string; controlId: string; severity: string; assigneeId: string;
  dueDate: string; affectedResources: string[]; validationCriteria: string;
}

export default function RemediationTaskEditor({ initial, owners, canSave, onSave, onClose, creating = false }: {
  initial: TaskDraft; owners: { id: string; name: string }[]; canSave: boolean;
  onSave: (draft: TaskDraft) => Promise<void>; onClose: () => void; creating?: boolean;
}) {
  const [draft, setDraft] = useState(initial);
  const resources = initial.affectedResources.join('\n');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const dirty = JSON.stringify(draft) !== JSON.stringify(initial) || resources !== initial.affectedResources.join('\n');
  const close = () => { if (!busy && (!dirty || window.confirm('Discard unsaved task changes?'))) onClose(); };
  const change = (key: keyof Omit<TaskDraft, 'affectedResources'>, value: string) =>
    setDraft(previous => ({ ...previous, [key]: value }));
  const save = async () => {
    if (!canSave) { setError('Permission denied: task editing is no longer authorized.'); return; }
    if (busy) return;
    if (!draft.title.trim()) { setError('Enter a task title.'); return; }
    setBusy(true); setError(null);
    try {
      await onSave({ ...draft, title: draft.title.trim(),
        affectedResources: Array.from(new Set(resources.split('\n').map(value => value.trim()).filter(Boolean))) });
    } catch (reason) {
      if (mounted.current) setError(reason instanceof Error ? reason.message : 'The task could not be saved. Your changes are retained.');
    } finally { if (mounted.current) setBusy(false); }
  };
  return <SetupDialog placement="right" expanded title={creating ? 'Create remediation task' : 'Edit remediation task'}
    description="Record corrective work without changing the original finding or closing its POA&M."
    busy={busy} onClose={close}><form className="rw-detail" onSubmit={event => { event.preventDefault(); void save(); }}>
    {(error || !canSave) && <p role="alert" className="rw-error">{error ?? 'Permission denied: task editing is no longer authorized.'}</p>}
    <fieldset disabled={busy || !canSave}>
      <label>Task title<input maxLength={500} required value={draft.title} onChange={event => change('title', event.target.value)} /></label>
      <label>Corrective action<textarea maxLength={4000} required={creating} rows={4} value={draft.description} onChange={event => change('description', event.target.value)} /></label>
      <small>{draft.description.length}/4000 characters</small>
      {!creating && <label>Owner<select value={draft.assigneeId} onChange={event => change('assigneeId', event.target.value)}>
        <option value="">Unassigned</option>
        {draft.assigneeId && !owners.some(owner => owner.id === draft.assigneeId)
          && <option value={draft.assigneeId} disabled>Retained owner (not currently assignable)</option>}
        {owners.map(owner => <option key={owner.id} value={owner.id}>{owner.name}</option>)}
      </select></label>}
      <p className="rw-muted">{creating ? 'Assign an owner after creating the retained task.' : 'Selecting an owner does not grant a role or change their system access.'}</p>
      <label>Due date<input type="date" required={!creating} value={draft.dueDate.slice(0, 10)} onChange={event => change('dueDate', event.target.value)} /></label>
      <label>Affected control<input required={creating} readOnly={!creating} value={draft.controlId} onChange={event => change('controlId', event.target.value)} /></label>
      <label>Severity<select disabled={!creating} value={draft.severity} onChange={event => change('severity', event.target.value)}>
        {Array.from(new Set([draft.severity, 'Critical', 'High', 'Medium', 'Low', 'Informational'])).filter(Boolean).map(value => <option key={value}>{value}</option>)}
      </select></label>
      {!creating && <details><summary>Retained scope & verification guidance</summary>
        <label>Affected resources (read-only)<textarea readOnly rows={3} value={resources} /></label>
        <label>Verification criteria (read-only)<textarea readOnly rows={3} value={draft.validationCriteria} /></label>
        <p className="rw-muted">These retained fields are not changed by this task editor. Saving corrective work is not verification.</p>
      </details>}
    </fieldset>
    <footer className="rw-actions"><button type="button" className={systemSecondaryAction} disabled={busy} onClick={close}>Cancel</button>
      <button type="submit" className={systemPrimaryAction} disabled={busy || !canSave || !draft.title.trim()}>
        {busy ? 'Saving task…' : creating ? 'Create task' : 'Save task'}</button></footer>
  </form></SetupDialog>;
}
