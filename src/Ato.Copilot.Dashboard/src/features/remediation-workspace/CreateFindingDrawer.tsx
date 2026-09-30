import { useState } from 'react';
import SetupDialog from '../workspace-operations/SetupDialog';
import { createRemediationFinding, remediationWorkspaceError } from '../../api/remediationWorkspace';
import { systemPrimaryAction, systemSecondaryAction } from '../systems/SystemTaskPresentation';

export default function CreateFindingDrawer({ systemId, canCreate, onClose, onCreated }: {
  systemId: string; canCreate: boolean; onClose: () => void; onCreated: () => void;
}) {
  const [operationId] = useState(() => crypto.randomUUID());
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [controlId, setControlId] = useState('');
  const [severity, setSeverity] = useState('Medium');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const close = () => { if (!busy && (!title && !description && !controlId || window.confirm('Discard this unsaved finding?'))) onClose(); };
  const save = async () => {
    if (!canCreate) { setError('Permission denied: manual findings are not authorized for this system.'); return; }
    if (busy) return;
    if (!title.trim() || !description.trim() || !controlId.trim()) { setError('Enter the finding title, observed weakness and affected control.'); return; }
    setBusy(true); setError(null);
    try {
      await createRemediationFinding(systemId, { operationId, title: title.trim(), description: description.trim(), controlId: controlId.trim(), severity });
      onCreated(); onClose();
    } catch (reason) { setError(remediationWorkspaceError(reason)); }
    finally { setBusy(false); }
  };
  return <SetupDialog placement="right" busy={busy} title="Add finding" description="Document a manually identified weakness. No assessment relationship will be inferred."
    onClose={close}><form className="rw-detail" onSubmit={event => { event.preventDefault(); void save(); }}>
    {(error || !canCreate) && <p role="alert" className="rw-error">{error ?? 'Permission denied: manual finding creation is not authorized.'}</p>}
    <fieldset disabled={busy || !canCreate}>
      <label>Finding title<input maxLength={500} required value={title} onChange={event => setTitle(event.target.value)} /></label>
      <label>Observed weakness<textarea maxLength={4000} required rows={4} value={description} onChange={event => setDescription(event.target.value)} /></label>
      <small>{description.length}/4000 characters</small>
      <label>Affected control<input required value={controlId} onChange={event => setControlId(event.target.value)} /></label>
      <label>Severity<select value={severity} onChange={event => setSeverity(event.target.value)}>
        {['Critical', 'High', 'Medium', 'Low', 'Informational'].map(value => <option key={value}>{value}</option>)}</select></label>
      <p className="rw-muted">Saving does not create corrective tasks, POA&M items, external tickets or an exception decision.</p>
    </fieldset>
    <div className="rw-actions"><button type="button" className={systemSecondaryAction} disabled={busy} onClick={close}>Cancel</button>
      <button type="submit" className={systemPrimaryAction} disabled={busy || !canCreate || !title.trim() || !description.trim() || !controlId.trim()}>
        {busy ? 'Saving finding…' : 'Save finding'}</button></div>
  </form></SetupDialog>;
}
