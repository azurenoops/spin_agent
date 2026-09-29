import { useRef, useState, type FormEvent } from 'react';
import type { PpsItem } from '../../types/dashboard';
import SetupDialog from '../workspace-operations/SetupDialog';
import { systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';

export type NetworkInterfaceDraft = Omit<PpsItem, 'id'> & { id?: string };

export default function NetworkInterfaceDrawer({ item, readOnly, error, onSave, onRemove, onClose }: {
  item?: PpsItem;
  readOnly: boolean;
  error: string | null;
  onSave: (item: NetworkInterfaceDraft) => Promise<boolean>;
  onRemove?: (id: string) => Promise<boolean>;
  onClose: () => void;
}) {
  const [draft, setDraft] = useState<NetworkInterfaceDraft>(() => item ? { ...item } : {
    serviceName: '', portOrRange: '', protocol: 'TCP', direction: 'Inbound', justification: null, sortOrder: 0,
  });
  const [busy, setBusy] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const writing = useRef(false);
  const close = () => { if (!writing.current) onClose(); };
  const persist = async (remove: boolean) => {
    if (writing.current) return;
    if (readOnly) { setLocalError('This network profile is read-only.'); return; }
    const normalized = { ...draft, serviceName: draft.serviceName.trim(), portOrRange: draft.portOrRange.trim(),
      protocol: draft.protocol.trim(), direction: draft.direction.trim(), justification: draft.justification?.trim() || null };
    if (!remove && (!normalized.serviceName || !normalized.portOrRange || !normalized.protocol || !normalized.direction)) {
      setLocalError('Service name, ports, protocol and direction are required.');
      return;
    }
    if (remove && (!item?.id || !onRemove)) { setLocalError('This network interface cannot be removed.'); return; }
    writing.current = true; setBusy(true); setLocalError(null);
    try {
      const saved = remove ? await onRemove!(item!.id) : await onSave(normalized);
      if (saved) onClose();
    } catch (reason) {
      setLocalError(reason instanceof Error ? reason.message : 'The network interface could not be saved.');
    } finally {
      writing.current = false; setBusy(false);
    }
  };
  const submit = (event: FormEvent) => { event.preventDefault(); void persist(removing); };
  const input = 'mt-1.5 block w-full rounded-[7px] border border-slate-300 bg-white px-3 py-2 text-sm disabled:bg-slate-50 dark:border-slate-600 dark:bg-slate-900';
  return <SetupDialog placement="right" busy={busy} onClose={close}
    title={item ? `Network interface · ${item.serviceName}` : 'Add network interface'}
    description="Record permitted communication in the network profile. Saving creates a draft; it does not approve connectivity or change firewall rules.">
    <form onSubmit={submit} className="space-y-4">
      {(localError || error) && <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-700">{localError || error}</p>}
      {removing ? <p>Remove <strong>{item?.serviceName}</strong> from the working network profile? Retained approved history is not erased.</p> : <>
        <fieldset disabled={busy || readOnly} className="min-w-0 space-y-4">
          <label className="block text-sm">Service / interface name
            <input required autoFocus maxLength={200} className={input} value={draft.serviceName}
              onChange={event => setDraft({ ...draft, serviceName: event.target.value })} />
          </label>
          <label className="block text-sm">Ports or range
            <input required maxLength={100} placeholder="443 or 8080-8090" className={input} value={draft.portOrRange}
              onChange={event => setDraft({ ...draft, portOrRange: event.target.value })} />
          </label>
          <label className="block text-sm">Protocol
            <input required list="network-interface-protocols" maxLength={50} className={input} value={draft.protocol}
              onChange={event => setDraft({ ...draft, protocol: event.target.value })} />
            <datalist id="network-interface-protocols">{['TCP', 'UDP', 'TCP and UDP', 'TLS', 'ICMP'].map(value => <option key={value} value={value} />)}</datalist>
          </label>
          <label className="block text-sm">Direction
            <select aria-label="Direction" className={input} value={draft.direction} onChange={event => setDraft({ ...draft, direction: event.target.value })}>
              {!['Inbound', 'Outbound', 'Both'].includes(draft.direction) && <option>{draft.direction}</option>}
              <option>Inbound</option><option>Outbound</option><option>Both</option>
            </select>
          </label>
          <label className="block text-sm">Justification
            <textarea aria-label="Justification" maxLength={2000} rows={4} className={input} value={draft.justification ?? ''}
              onChange={event => setDraft({ ...draft, justification: event.target.value })} />
          </label>
        </fieldset>
        <p className="text-xs text-slate-500">Network interfaces and communication context are reviewed together as a profile section. External interconnections and agreements are separate records.</p>
      </>}
      <div className="flex flex-wrap gap-3">
        {!readOnly && <button type="submit" disabled={busy} className={systemPrimaryAction}>
          {busy ? 'Saving…' : removing ? 'Confirm removal' : 'Save network interface'}
        </button>}
        <button type="button" disabled={busy} onClick={removing ? () => setRemoving(false) : close} className={systemSecondaryAction}>
          {readOnly ? 'Close' : 'Cancel'}
        </button>
        {!readOnly && item && onRemove && !removing && <button type="button" disabled={busy}
          className="text-sm text-red-700 underline disabled:opacity-50" onClick={() => setRemoving(true)}>Remove network interface</button>}
      </div>
    </form>
  </SetupDialog>;
}
