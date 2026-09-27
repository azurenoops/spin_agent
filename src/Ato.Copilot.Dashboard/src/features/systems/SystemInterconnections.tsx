import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { getSystemDocuments, type InterconnectionDocInfo } from '../../api/documents';
import { addInterconnection } from '../../api/systemDetail';
import { useSystemMutationPermission } from '../../components/permissions/useSystemMutationPermission';
import SetupDialog from '../workspace-operations/SetupDialog';
import { systemPanel, systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';

export default function SystemInterconnections({ systemId }: { systemId: string }) {
  const canManage = useSystemMutationPermission(systemId, 'canManageSystem');
  const [items, setItems] = useState<InterconnectionDocInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [target, setTarget] = useState('');
  const [direction, setDirection] = useState('Inbound');
  const [protocol, setProtocol] = useState('');
  const [ports, setPorts] = useState('');
  const [classification, setClassification] = useState('CUI');
  const writing = useRef(false);
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);
  useEffect(() => {
    let current = true;
    setLoading(true); setError(null); setItems([]);
    void getSystemDocuments(systemId).then(data => {
      if (!current) return;
      if (data.systemId !== systemId || !Array.isArray(data.interconnections)) throw new Error('Interconnection records do not match this system.');
      setItems(data.interconnections);
    }).catch(reason => { if (current) setError(reason instanceof Error ? reason.message : 'Interconnection records are unavailable.'); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [systemId, attempt]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!canManage) { setError('System-management permission is required to record an interconnection.'); return; }
    if (!target.trim() || writing.current) return;
    writing.current = true; setBusy(true); setError(null); setNotice(null);
    try {
      const result = await addInterconnection(systemId, {
        remoteSystem: target.trim(), direction, protocol: protocol.trim() || undefined,
        port: ports.trim() || undefined, dataClassification: classification.trim() || undefined,
      });
      if (!mounted.current) return;
      if (!result.interconnectionId || !result.status) throw new Error('The interconnection write was not confirmed. Refresh records before retrying.');
      setNotice(`Interconnection recorded with state ${result.status}. Review agreements and supporting evidence separately.`);
      setOpen(false); setTarget(''); setProtocol(''); setPorts(''); setAttempt(value => value + 1);
    } catch (reason) {
      if (mounted.current) setError(reason instanceof Error ? reason.message : 'The interconnection could not be recorded.');
    } finally {
      writing.current = false;
      if (mounted.current) setBusy(false);
    }
  };
  const input = 'mt-1 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm dark:border-slate-600 dark:bg-slate-900';
  return <section className={systemPanel} aria-labelledby="system-interconnection-register">
    <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
      <div><h2 id="system-interconnection-register" className="text-lg font-semibold">Interconnection register</h2>
        <p className="mt-2 text-sm text-slate-500">External systems and agreement records, distinct from the permitted port list above.</p></div>
      <button type="button" disabled={!canManage || loading} onClick={() => setOpen(true)} className={systemSecondaryAction}>Add interconnection</button>
    </div>
    {notice && <p role="status" className="mb-4 rounded-lg bg-indigo-50 p-3 text-sm text-indigo-900">{notice}</p>}
    {loading && <p role="status" className="text-sm text-slate-500">Loading interconnection records…</p>}
    {error && <div className="mb-4 space-y-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">
      <p role="alert">{error}</p><button className="underline" type="button" onClick={() => setAttempt(value => value + 1)}>Retry interconnection records</button>
    </div>}
    {!loading && !error && (items.length ? <div className="overflow-x-auto">
      <table className="w-full text-left text-sm"><thead className="bg-slate-50 dark:bg-slate-800"><tr>
        {['External system', 'Direction', 'Record status', 'Agreement'].map(label => <th key={label} scope="col" className="px-3 py-3 font-medium">{label}</th>)}
      </tr></thead><tbody className="divide-y divide-slate-100 dark:divide-slate-700">{items.map(item => <tr key={item.interconnectionId}>
        <td className="px-3 py-3 font-medium">{item.targetSystem}</td><td className="px-3 py-3">{item.direction}</td>
        <td className="px-3 py-3">{item.status}</td><td className="px-3 py-3">{item.hasAgreement
          ? `${item.agreementType ?? 'Recorded agreement'} · ${item.agreementStatus ?? 'Status not returned'}` : 'No agreement recorded'}</td>
      </tr>)}</tbody></table>
    </div> : <p className="text-sm text-slate-500">No interconnections are recorded.</p>)}
    <p className="mt-4 text-xs text-slate-500">An empty register is not a certification that the system has no interconnections.</p>
    <Link className="mt-3 inline-block text-sm text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/documents`}>Review interconnection documents</Link>
    {open && <SetupDialog title="Record an interconnection" busy={busy} onClose={() => setOpen(false)}>
      <form onSubmit={event => void submit(event)} className="space-y-4">
        {error && <p className="text-sm text-amber-900">{error}</p>}
        <label className="block text-sm">External system<input autoFocus required maxLength={200} className={input} value={target} onChange={event => setTarget(event.target.value)} /></label>
        <label className="block text-sm">Data flow direction<select className={input} value={direction} onChange={event => setDirection(event.target.value)}>
          <option>Inbound</option><option>Outbound</option><option>Bidirectional</option>
        </select></label>
        <label className="block text-sm">Protocol<input className={input} value={protocol} onChange={event => setProtocol(event.target.value)} maxLength={100} /></label>
        <label className="block text-sm">Ports<input className={input} value={ports} onChange={event => setPorts(event.target.value)} maxLength={100} /></label>
        <label className="block text-sm">Data classification<input className={input} value={classification} onChange={event => setClassification(event.target.value)} maxLength={100} /></label>
        <p className="text-xs text-slate-500">This records the relationship. It does not approve an agreement or an authorization decision.</p>
        <button type="submit" disabled={!canManage || busy || !target.trim()} className={systemPrimaryAction}>{busy ? 'Recording…' : 'Record interconnection'}</button>
      </form>
    </SetupDialog>}
  </section>;
}
