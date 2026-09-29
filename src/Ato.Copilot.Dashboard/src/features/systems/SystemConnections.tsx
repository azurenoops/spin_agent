import { useEffect, useRef, useState } from 'react';
import type { ProfileSectionDetail } from '../../types/dashboard';
import { listSystemInterconnections, type SystemInterconnectionDetail } from '../../api/interconnections';
import NetworkInterfaceDrawer, { type NetworkInterfaceDraft } from './NetworkInterfaceDrawer';
import InterconnectionDrawer from './InterconnectionDrawer';
import SetupDialog from '../workspace-operations/SetupDialog';
import { systemPanel, systemSecondaryAction } from './SystemTaskPresentation';

type Editor = { kind: 'network'; id?: string } | { kind: 'external'; item?: SystemInterconnectionDetail } | null;

export default function SystemConnections({ systemId, profile, profileReadOnly, profileError, saving,
  addOpen, onAddClose, onCanAddChange, onSaveProfile }: {
  systemId: string; profile: Pick<ProfileSectionDetail, 'draftContent' | 'ppsEntries' | 'governanceStatus'>;
  profileReadOnly: boolean; profileError: string | null; saving: boolean;
  addOpen: boolean; onAddClose: () => void; onCanAddChange: (allowed: boolean) => void;
  onSaveProfile: (content: string, items: NetworkInterfaceDraft[]) => Promise<boolean>;
}) {
  const [external, setExternal] = useState<SystemInterconnectionDetail[]>([]);
  const [canCreate, setCanCreate] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);
  const [editor, setEditor] = useState<Editor>(null);
  const table = useRef<HTMLTableElement>(null);
  const restoreAfterRemoval = useRef(false);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError(null); setCanCreate(false);
    void (async () => {
      const first = await listSystemInterconnections(systemId, controller.signal);
      const rows = [...first.items];
      for (let page = first.page + 1; rows.length < first.total; page++) {
        const next = await listSystemInterconnections(systemId, controller.signal, page, first.pageSize);
        if (!next.items.length) throw new Error('The interconnection register is incomplete. Retry before editing.');
        rows.push(...next.items);
      }
      if (controller.signal.aborted) return;
      setExternal(rows); setCanCreate(first.canManageInterconnections);
    })().catch(reason => {
      if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Unable to load external interconnections.');
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, attempt]);
  useEffect(() => { onCanAddChange(!profileReadOnly || canCreate); }, [profileReadOnly, canCreate, onCanAddChange]);
  useEffect(() => {
    if (editor || !restoreAfterRemoval.current) return;
    restoreAfterRemoval.current = false;
    (table.current?.querySelector<HTMLButtonElement>('button') ?? table.current)?.focus();
  }, [editor]);

  const close = () => { setEditor(null); onAddClose(); };
  const saveNetwork = (item: NetworkInterfaceDraft) => onSaveProfile(profile.draftContent ?? '{}', item.id
    ? profile.ppsEntries.map(row => row.id === item.id ? item : row)
    : [...profile.ppsEntries, { ...item, sortOrder: profile.ppsEntries.length }]);
  const removeNetwork = async (id: string) => {
    const saved = await onSaveProfile(profile.draftContent ?? '{}', profile.ppsEntries.filter(row => row.id !== id));
    if (saved) restoreAfterRemoval.current = true;
    return saved;
  };
  const choose = (kind: 'network' | 'external') => {
    onAddClose(); setNotice(null); setEditor({ kind });
  };
  const rowButton = 'whitespace-nowrap rounded-[7px] border border-[#dce1ec] px-[9px] py-[5px] text-[11px] text-[#5143d7] disabled:opacity-50 dark:border-slate-600 dark:text-indigo-300';
  const cell = 'px-2.5 py-[15px]';
  return <>
    <section className={`${systemPanel} connection-register flex-1`} aria-label="Network interfaces and interconnections">
      {notice && <p role="status" className="mb-4 rounded-lg bg-green-50 p-3 text-sm text-green-800">{notice}</p>}
      {profileError && !editor && <p role="alert" className="mb-4 text-sm text-red-700">{profileError}</p>}
      {loading && <p role="status" className="mb-4 text-sm text-slate-500">Loading interconnections…</p>}
      {error && <div className="mb-4 space-y-2 rounded-lg bg-amber-50 p-3 text-sm text-amber-900">
        <p role="alert">{error} Network profile rows are shown separately below; the external register could not be confirmed.</p>
        <button type="button" className="underline" onClick={() => setAttempt(value => value + 1)}>Retry interconnections</button>
      </div>}
      <div className="relative overflow-x-auto">
        <table ref={table} tabIndex={-1} aria-label="Network interfaces and interconnections" className="w-full text-left text-xs">
          <thead><tr>{['Record', 'Context', 'Source / owner', 'Status', 'Open'].map(label =>
            <th key={label} scope="col" className="border-b border-[#dfe4ed] px-2.5 py-2.5 text-[10px] font-semibold uppercase tracking-[.6px] text-slate-500">
              {label === 'Open' ? <span className="sr-only">Open</span> : label}
            </th>)}</tr></thead>
          <tbody className="divide-y divide-slate-100 dark:divide-slate-700">
            {!loading && !error && profile.ppsEntries.length + external.length === 0 && <tr><td colSpan={5} className={`${cell} text-slate-500`}>
              No network interfaces or interconnections are recorded. Choose Add connection to record one. This is not a no-interconnections certification.
            </td></tr>}
            {profile.ppsEntries.map(item => <tr key={`network-${item.id}`}>
              <td className={`${cell} font-semibold`}>{item.serviceName}</td>
              <td className={cell}>{item.protocol} / {item.portOrRange}</td>
              <td className={cell}>Network profile</td>
              <td className={cell}><span className="rounded-[5px] bg-[#fff1d9] px-2 py-1 text-[11px] text-[#865d15]">Profile: {profile.governanceStatus}</span></td>
              <td className={cell}><button type="button" disabled={saving} className={rowButton} aria-label={`Open network interface ${item.serviceName}`}
                onClick={() => { setNotice(null); setEditor({ kind: 'network', id: item.id }); }}>Open →</button></td>
            </tr>)}
            {!error && external.map(item => <tr key={`external-${item.id}`}>
              <td className={`${cell} font-semibold`}>{item.targetSystemName}</td>
              <td className={cell}>{item.protocolsUsed.join(', ') || 'Protocol not recorded'} / {item.portsUsed.join(', ') || 'Ports not recorded'}</td>
              <td className={cell}>{item.targetSystemOwner || 'External interconnection'}</td>
              <td className={cell}><span className="rounded-[5px] bg-[#f0f2f6] px-2 py-1 text-[11px] text-[#657088]">{item.status}</span>
                <span className="mt-1 block text-[10px] text-slate-500">{item.hasAgreement ? 'Agreement recorded' : 'No agreement recorded'}</span></td>
              <td className={cell}><button type="button" disabled={saving || loading} className={rowButton} aria-label={`Open interconnection ${item.targetSystemName}`}
                onClick={() => { setNotice(null); setEditor({ kind: 'external', item }); }}>Open →</button></td>
            </tr>)}
          </tbody>
        </table>
      </div>
    </section>
    {addOpen && !editor && <SetupDialog placement="right" busy={saving} title="Add connection" onClose={onAddClose}
      description="Choose the kind of record. Network interfaces describe permitted ports and services; interconnections describe an external system and its agreements.">
      <div className="space-y-4">
        <button type="button" disabled={profileReadOnly || saving} className={`${systemSecondaryAction} w-full`} onClick={() => choose('network')}>Add network interface</button>
        <p className="text-xs text-slate-500">Network interfaces are saved and reviewed with the communication profile.</p>
        <button type="button" disabled={!canCreate || loading || saving} className={`${systemSecondaryAction} w-full`} onClick={() => choose('external')}>Add interconnection</button>
        <p className="text-xs text-slate-500">{canCreate ? 'External records are saved independently. Agreements and authorization remain separate.' : 'External interconnection management is not available with the current server permissions.'}</p>
      </div>
    </SetupDialog>}
    {editor?.kind === 'network' && <NetworkInterfaceDrawer key={editor.id ?? 'new-network'}
      item={profile.ppsEntries.find(item => item.id === editor.id)} readOnly={profileReadOnly} error={profileError}
      onSave={saveNetwork} onRemove={removeNetwork} onClose={close} />}
    {editor?.kind === 'external' && <InterconnectionDrawer key={editor.item?.id ?? 'new-external'} systemId={systemId}
      item={editor.item} canCreate={canCreate} onClose={close} onSaved={item => {
        setExternal(rows => rows.some(row => row.id === item.id) ? rows.map(row => row.id === item.id ? item : row) : [...rows, item]);
        setNotice('Interconnection saved. Agreement and authorization status were not changed.');
      }} />}
  </>;
}
