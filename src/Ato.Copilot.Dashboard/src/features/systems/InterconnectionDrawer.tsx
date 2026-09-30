import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import {
  createSystemInterconnection, getSystemInterconnection, updateSystemInterconnection,
  type InterconnectionEditableFields, type SystemInterconnectionDetail,
} from '../../api/interconnections';
import SetupDialog from '../workspace-operations/SetupDialog';
import { systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';

const emptyFields: InterconnectionEditableFields = {
  targetSystemName: '', targetSystemOwner: '', targetSystemAcronym: '', dataClassification: '',
  dataDescription: '', authenticationMethod: '', protocolsUsed: [], portsUsed: [], securityMeasures: [],
  interconnectionType: 'Direct', dataFlowDirection: 'Inbound',
};
type EditorFields = Omit<InterconnectionEditableFields, 'protocolsUsed' | 'portsUsed' | 'securityMeasures'> & {
  protocols: string; ports: string; measures: string;
};
const editorFields = (item: InterconnectionEditableFields): EditorFields => ({
  ...item, protocols: item.protocolsUsed.join('\n'), ports: item.portsUsed.join('\n'), measures: item.securityMeasures.join('\n'),
});

export default function InterconnectionDrawer({ systemId, item, canCreate, onSaved, onClose }: {
  systemId: string;
  item?: SystemInterconnectionDetail;
  canCreate: boolean;
  onSaved: (record: SystemInterconnectionDetail) => void;
  onClose: () => void;
}) {
  const [record, setRecord] = useState<SystemInterconnectionDetail | null>(null);
  const [fields, setFields] = useState<EditorFields>(() => editorFields(emptyFields));
  const [loading, setLoading] = useState(!!item);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const writing = useRef(false);
  const canEdit = item ? record?.canManageInterconnections === true : canCreate;
  useEffect(() => {
    if (!item) return;
    const controller = new AbortController();
    setLoading(true); setError(null); setRecord(null);
    void getSystemInterconnection(systemId, item.id, controller.signal).then(result => {
      if (controller.signal.aborted) return;
      setRecord(result); setFields(editorFields(result));
    }).catch(reason => {
      if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Unable to load the selected interconnection.');
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, item?.id, attempt]);
  const close = () => { if (!writing.current) onClose(); };
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (writing.current) return;
    if (!canEdit || loading) { setError('You do not have permission to edit this interconnection.'); return; }
    if (!fields.targetSystemName.trim() || !fields.dataClassification.trim()) {
      setError('External system and data classification are required.'); return;
    }
    const lines = (value: string) => value.split('\n').map(line => line.trim()).filter(Boolean);
    const input: InterconnectionEditableFields = {
      targetSystemName: fields.targetSystemName.trim(), targetSystemOwner: fields.targetSystemOwner?.trim() || null,
      targetSystemAcronym: fields.targetSystemAcronym?.trim() || null,
      dataClassification: fields.dataClassification.trim(), dataDescription: fields.dataDescription?.trim() || null,
      authenticationMethod: fields.authenticationMethod?.trim() || null,
      interconnectionType: fields.interconnectionType, dataFlowDirection: fields.dataFlowDirection,
      protocolsUsed: lines(fields.protocols), portsUsed: lines(fields.ports), securityMeasures: lines(fields.measures),
    };
    writing.current = true; setBusy(true); setError(null);
    try {
      const saved = item ? await updateSystemInterconnection(systemId, item.id, input)
        : await createSystemInterconnection(systemId, input);
      onSaved(saved); onClose();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'The interconnection could not be saved.');
    } finally {
      writing.current = false; setBusy(false);
    }
  };
  const inputClass = 'mt-1.5 block w-full rounded-[7px] border border-slate-300 bg-white px-3 py-2 text-sm disabled:bg-slate-50 dark:border-slate-600 dark:bg-slate-900';
  return <SetupDialog placement="right" busy={busy} onClose={close}
    title={item ? `Interconnection · ${record?.targetSystemName ?? item.targetSystemName}` : 'Add interconnection'}
    description="Edit the recorded external system and communication details. Saving does not approve an agreement, grant authorization to connect, or alter network configuration.">
    {loading && <p role="status">Loading interconnection details…</p>}
    {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    {!loading && item && !record ? <button className={systemSecondaryAction} type="button" onClick={() => setAttempt(value => value + 1)}>Retry interconnection</button>
      : !loading && <form onSubmit={event => void submit(event)} className="space-y-4">
        {!canEdit && <p className="text-sm text-slate-500">Read-only. Interconnection management requires the applicable server-granted authority.</p>}
        {record && <p className="rounded-lg bg-slate-50 p-3 text-sm">Status: {record.status} · {record.hasAgreement ? 'Agreement recorded' : 'No agreement recorded'}</p>}
        <fieldset disabled={busy || !canEdit} className="min-w-0 space-y-4">
          <label className="block text-sm">External system<input autoFocus required className={inputClass} value={fields.targetSystemName}
            onChange={event => setFields({ ...fields, targetSystemName: event.target.value })} /></label>
          <label className="block text-sm">System acronym<input className={inputClass} value={fields.targetSystemAcronym ?? ''}
            onChange={event => setFields({ ...fields, targetSystemAcronym: event.target.value })} /></label>
          <label className="block text-sm">System owner<input className={inputClass} value={fields.targetSystemOwner ?? ''}
            onChange={event => setFields({ ...fields, targetSystemOwner: event.target.value })} /></label>
          <label className="block text-sm">Connection type<select aria-label="Connection type" className={inputClass} value={fields.interconnectionType}
            onChange={event => {
              const value = event.target.value;
              if (value === 'Direct' || value === 'Vpn' || value === 'Api' || value === 'Federated' || value === 'Wireless' || value === 'RemoteAccess')
                setFields({ ...fields, interconnectionType: value });
            }}>
            <option value="Direct">Direct</option><option value="Vpn">VPN</option><option value="Api">API</option>
            <option value="Federated">Federated</option><option value="Wireless">Wireless</option><option value="RemoteAccess">Remote access</option>
          </select></label>
          <label className="block text-sm">Data flow direction<select aria-label="Data flow direction" className={inputClass} value={fields.dataFlowDirection}
            onChange={event => {
              const value = event.target.value;
              if (value === 'Inbound' || value === 'Outbound' || value === 'Bidirectional') setFields({ ...fields, dataFlowDirection: value });
            }}>
            <option>Inbound</option><option>Outbound</option><option>Bidirectional</option>
          </select></label>
          <label className="block text-sm">Protocols (one per line)<textarea aria-label="Protocols (one per line)" rows={2} className={inputClass} value={fields.protocols}
            onChange={event => setFields({ ...fields, protocols: event.target.value })} /></label>
          <label className="block text-sm">Ports (one per line)<textarea aria-label="Ports (one per line)" rows={2} className={inputClass} value={fields.ports}
            onChange={event => setFields({ ...fields, ports: event.target.value })} /></label>
          <label className="block text-sm">Data classification<input required className={inputClass} value={fields.dataClassification}
            onChange={event => setFields({ ...fields, dataClassification: event.target.value })} /></label>
          <label className="block text-sm">Data exchanged<textarea aria-label="Data exchanged" rows={3} className={inputClass} value={fields.dataDescription ?? ''}
            onChange={event => setFields({ ...fields, dataDescription: event.target.value })} /></label>
          <label className="block text-sm">Authentication method<input className={inputClass} value={fields.authenticationMethod ?? ''}
            onChange={event => setFields({ ...fields, authenticationMethod: event.target.value })} /></label>
          <label className="block text-sm">Security measures (one per line)<textarea aria-label="Security measures (one per line)" rows={3} className={inputClass} value={fields.measures}
            onChange={event => setFields({ ...fields, measures: event.target.value })} /></label>
        </fieldset>
        {record && <details className="rounded-lg border border-slate-200 p-3 text-xs">
          <summary className="cursor-pointer font-semibold">Agreements &amp; record details</summary>
          <dl className="mt-3 space-y-2">
            <div><dt>Record ID</dt><dd className="break-all">{record.id}</dd></div>
            <div><dt>Authorization to connect</dt><dd>{record.authorizationToConnect ? 'Recorded' : 'Not recorded'}</dd></div>
            <div><dt>Status reason</dt><dd>{record.statusReason || 'Not recorded'}</dd></div>
            <div><dt>Last modified</dt><dd>{record.modifiedAt ?? record.createdAt}</dd></div>
          </dl>
          <pre className="mt-3 whitespace-pre-wrap break-all">{JSON.stringify(record.agreements, null, 2)}</pre>
          <Link onClick={event => { if (busy) event.preventDefault(); }} aria-disabled={busy} tabIndex={busy ? -1 : undefined}
            className="mt-3 inline-block text-indigo-700 underline" to={`/systems/${systemId}/documents`}>Review interconnection documents</Link>
        </details>}
        <div className="flex flex-wrap gap-3">
          {canEdit && <button type="submit" disabled={busy} className={systemPrimaryAction}>{busy ? 'Saving…' : 'Save interconnection'}</button>}
          <button type="button" disabled={busy} onClick={close} className={systemSecondaryAction}>{canEdit ? 'Cancel' : 'Close'}</button>
        </div>
      </form>}
  </SetupDialog>;
}
