import { useRef, useState } from 'react';
import { useNavigate } from '../workspaces/workspaceNavigation';
import SetupDialog from './SetupDialog';
import { ProviderComponentPicker } from './ProviderPresentation';
import { buttonClass, errorClass, inputClass, message, secondaryButtonClass } from './workspaceUi';
import * as api from './api';

export default function ProviderAddCapabilityDialog({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const [componentId, setComponentId] = useState('');
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [uncertain, setUncertain] = useState(false);
  const saving = useRef(false);
  const save = async () => {
    if (saving.current || uncertain || !componentId || !name.trim() || !description.trim()) return;
    saving.current = true; setBusy(true); setError(null);
    try {
      const created = await api.createProviderCapability(componentId, {
        name: name.trim(), description: description.trim(), mappedNistControlIds: [], markMappedImmediately: false,
      });
      navigate(`/security-capabilities/${encodeURIComponent(created.id)}`);
    } catch (reason) {
      const status = typeof reason === 'object' && reason !== null && 'status' in reason ? reason.status : null;
      const ambiguous = typeof status !== 'number' || status >= 500;
      setUncertain(ambiguous);
      setError(`${message(reason)}${ambiguous ? ' The creation outcome is uncertain. Close and check the catalog before creating again.' : ''}`);
    } finally {
      saving.current = false; setBusy(false);
    }
  };
  return <SetupDialog busy={busy} onClose={onClose} title="Add provider capability"
    description="Create provider-owned content on an existing component. Review coverage before publication.">
    <form className="space-y-4" onSubmit={event => { event.preventDefault(); void save(); }}>
      {error && <p role="alert" className={errorClass}>{error}</p>}
      <fieldset disabled={busy || uncertain} className="space-y-4">
        <legend className="mb-3 text-sm font-semibold">Primary provider component</legend>
        <ProviderComponentPicker selected={[componentId]} multiple={false} onSelect={item => setComponentId(item.componentId)} />
        <label className="grid gap-1 text-sm">Name<input className={inputClass} required maxLength={200} value={name} onChange={event => setName(event.target.value)} /></label>
        <label className="grid gap-1 text-sm">Description<textarea className={inputClass} required rows={3} maxLength={2000} value={description} onChange={event => setDescription(event.target.value)} /></label>
      </fieldset>
      <p className="rounded border border-blue-100 bg-blue-50 p-3 text-xs text-blue-900 dark:border-blue-900 dark:bg-blue-950 dark:text-blue-100">The capability starts in Needs review. Creation does not approve mappings, publish a working revision, or subscribe customer systems.</p>
      <div className="flex justify-end gap-2 border-t border-slate-200 pt-4 dark:border-gray-700">
        <button type="button" disabled={busy} className={secondaryButtonClass} onClick={onClose}>Cancel</button>
        <button type="submit" disabled={busy || uncertain || !componentId || !name.trim() || !description.trim()} className={buttonClass}>{busy ? 'Creating…' : 'Create capability'}</button>
      </div>
    </form>
  </SetupDialog>;
}
