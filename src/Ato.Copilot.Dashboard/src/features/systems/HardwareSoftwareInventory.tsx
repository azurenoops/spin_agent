import { useEffect, useRef, useState } from 'react';
import { Link, useLocation } from '../workspaces/workspaceNavigation';
import { inventoryWorkbookUrl, listInventoryRecords, saveInventoryRecord, retireInventoryRecord,
  inventoryHardwareFunctions as hardwareFunctions, inventorySoftwareFunctions as softwareFunctions,
  type InventoryInput, type InventoryPage, type InventoryRecord } from '../../api/inventoryRegister';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import SetupDialog from '../workspace-operations/SetupDialog';
import { systemPanel, systemPrimaryAction, systemSecondaryAction, SystemTaskHeading } from './SystemTaskPresentation';

const textFields = [
  ['manufacturer', 'Manufacturer', 300], ['model', 'Model', 300], ['serialNumber', 'Serial number', 200],
  ['ipAddress', 'IP address', 45], ['macAddress', 'MAC address', 17], ['location', 'Physical or logical location', 500],
  ['vendor', 'Vendor', 300], ['version', 'Version', 100], ['patchLevel', 'Patch level', 200], ['licenseType', 'License type', 200],
] as const;
function inputFor(item?: InventoryRecord): InventoryInput {
  if (!item) return { itemName: '', type: 1, softwareFunction: null };
  return { itemName: item.itemName, type: item.type === 'Hardware' ? 0 : 1,
    hardwareFunction: item.hardwareFunction ? hardwareFunctions.indexOf(item.hardwareFunction) : null,
    softwareFunction: item.softwareFunction ? softwareFunctions.indexOf(item.softwareFunction) : null,
    manufacturer: item.manufacturer ?? '', model: item.model ?? '', serialNumber: item.serialNumber ?? '',
    ipAddress: item.ipAddress ?? '', macAddress: item.macAddress ?? '', location: item.location ?? '',
    vendor: item.vendor ?? '', version: item.version ?? '', patchLevel: item.patchLevel ?? '',
    licenseType: item.licenseType ?? '', ...(item.parentHardwareId ? { parentHardwareId: item.parentHardwareId } : {}),
  };
}
function message(reason: unknown) {
  return reason instanceof Error ? reason.message : reason && typeof reason === 'object' && 'error' in reason && typeof reason.error === 'string'
    ? reason.error : 'The inventory operation could not be completed.';
}
export default function HardwareSoftwareInventory({ systemId }: { systemId: string }) {
  const location = useLocation();
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [data, setData] = useState<InventoryPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [editor, setEditor] = useState<{ item?: InventoryRecord; input: InventoryInput } | null>(null);
  const [busy, setBusy] = useState(false);
  const [retiring, setRetiring] = useState(false);
  const [rationale, setRationale] = useState('');
  const [formError, setFormError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const writing = useRef(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const restoreFocus = useRef(false);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError(null); setData(null);
    void listInventoryRecords(systemId, page, controller.signal).then(result => { if (!controller.signal.aborted) setData(result); })
      .catch(reason => { if (!controller.signal.aborted) setError(message(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, page, revision]);
  useEffect(() => {
    if (!editor && !loading && restoreFocus.current) { restoreFocus.current = false; trigger.current?.focus(); }
  }, [editor, loading]);
  const close = () => { if (!writing.current) { setEditor(null); setRetiring(false); setFormError(null); } };
  const open = (item?: InventoryRecord) => { setEditor({ item, input: inputFor(item) }); setRetiring(false); setRationale(''); setFormError(null); };
  const save = async () => {
    if (!editor || writing.current) return;
    if (!data?.canManage) { setFormError('System-management permission is required.'); return; }
    writing.current = true; setBusy(true); setFormError(null);
    try {
      if (retiring && editor.item) {
        if (!rationale.trim()) { setFormError('A retirement rationale is required.'); return; }
        await retireInventoryRecord(systemId, editor.item.id, rationale.trim());
        setNotice('Inventory item retired. Historical records are retained.');
      } else {
        await saveInventoryRecord(systemId, editor.item?.id, { ...editor.input, itemName: editor.input.itemName.trim() });
        setNotice('Inventory item saved. Recheck package readiness; this does not approve or submit a package.');
      }
      restoreFocus.current = true; setEditor(null); setRetiring(false); setRevision(value => value + 1);
    } catch (reason) { setFormError(message(reason)); }
    finally { writing.current = false; setBusy(false); }
  };
  const registryQuery = new URLSearchParams(location.search); registryQuery.delete('tab');
  const readonly = !data?.canManage;
  const inputClass = 'mt-1 block w-full rounded border border-slate-300 bg-white p-2 text-sm dark:border-slate-600 dark:bg-slate-900';
  return <div className="space-y-5">
    <SystemTaskHeading title="Hardware/software inventory" description="Document the hardware, software and managed services included in the system's submission."
      action={<button ref={trigger} type="button" disabled={loading || readonly} className={systemPrimaryAction} onClick={() => open()}>Add inventory item</button>} />
    <p className="text-sm text-slate-500">Managed/SaaS/PaaS software may be recorded without a physical parent. Use supported vendor and version information; do not invent serial numbers, IP addresses or provider hardware.</p>
    <div className="flex flex-wrap gap-4 text-sm">
      <Link className="text-indigo-700 underline" to={`/systems/${systemId}/security-capabilities/inventory${registryQuery.size ? `?${registryQuery}` : ''}`}>View component registry</Link>
      <AuthenticatedDownload url={inventoryWorkbookUrl(systemId)} disabled={loading || !data}
        fileName="hardware-software-inventory.xlsx" className="text-indigo-700 underline">Export HW/SW workbook</AuthenticatedDownload>
    </div>
    {notice && <p role="status" className="rounded bg-green-50 p-3 text-sm text-green-800">{notice}</p>}
    {loading && <p role="status">Loading inventory records…</p>}
    {error && <div role="alert" className="text-sm text-red-700">{error}<button className="ml-3 underline" onClick={() => setRevision(value => value + 1)}>Retry inventory</button></div>}
    {data && <section className={systemPanel}>
      <div className="relative overflow-x-auto"><table className="w-full text-left text-sm"><thead><tr>
        {['Item', 'Type / function', 'Vendor / manufacturer', 'Version', 'Open'].map(label => <th className="border-b p-3" key={label}>{label}</th>)}
      </tr></thead><tbody>
        {data.items.map(item => <tr key={item.id}>
          <td className="p-3 font-medium">{item.itemName}</td><td className="p-3">{item.type} · {item.hardwareFunction ?? item.softwareFunction ?? 'Not recorded'}</td>
          <td className="p-3">{item.type === 'Hardware' ? item.manufacturer : item.vendor}</td><td className="p-3">{item.version ?? 'Not recorded'}</td>
          <td className="p-3"><button type="button" className="text-indigo-700 underline" aria-label={`Open inventory item ${item.itemName}`} onClick={() => open(item)}>Open →</button></td>
        </tr>)}
        {!data.items.length && <tr><td colSpan={5} className="p-3">No active inventory records.</td></tr>}
      </tbody></table></div>
      {data.totalCount > data.pageSize && <nav aria-label="Inventory pages" className="mt-3 flex justify-between text-sm">
        <button disabled={page === 1} onClick={() => setPage(value => value - 1)}>Previous</button><span>{page} / {Math.ceil(data.totalCount / data.pageSize)}</span>
        <button disabled={page * data.pageSize >= data.totalCount} onClick={() => setPage(value => value + 1)}>Next</button>
      </nav>}
    </section>}
    {editor && <SetupDialog placement="right" busy={busy} onClose={close}
      title={editor.item ? `Inventory · ${editor.item.itemName}` : 'Add inventory item'}
      description="This updates the canonical hardware/software register. Component assignments, review records and authorization remain separate.">
      <form className="space-y-4" onSubmit={event => { event.preventDefault(); void save(); }}>
        {formError && <p role="alert" className="text-sm text-red-700">{formError}</p>}
        {retiring ? <>
          <p className="text-sm">Retire this item? Installed software is also retired when its parent hardware is decommissioned.</p>
          <label className="block text-sm">Retirement rationale<textarea required maxLength={2000} className={inputClass} disabled={busy}
            value={rationale} onChange={event => setRationale(event.target.value)} /></label>
        </> : <fieldset disabled={busy || readonly} className="space-y-4">
          <label className="block text-sm">Item name<input autoFocus required maxLength={300} className={inputClass} value={editor.input.itemName}
            onChange={event => setEditor({ ...editor, input: { ...editor.input, itemName: event.target.value } })} /></label>
          <label className="block text-sm">Type<select aria-label="Type" disabled={!!editor.item} className={inputClass} value={editor.input.type}
            onChange={event => setEditor({ ...editor, input: { itemName: editor.input.itemName, type: event.target.value === '0' ? 0 : 1 } })}>
            <option value="0">Hardware</option><option value="1">Software / managed service</option>
          </select></label>
          <label className="block text-sm">Function<select required aria-label="Function" className={inputClass}
            value={(editor.input.type === 0 ? editor.input.hardwareFunction : editor.input.softwareFunction) ?? ''}
            onChange={event => setEditor({ ...editor, input: { ...editor.input,
              ...(editor.input.type === 0 ? { hardwareFunction: Number(event.target.value) } : { softwareFunction: Number(event.target.value) }) } })}>
            <option value="">Choose function</option>{(editor.input.type === 0 ? hardwareFunctions : softwareFunctions).map((label, index) => <option key={label} value={index}>{label}</option>)}
          </select></label>
          {textFields.filter(([key]) => editor.input.type === 0 ? !['vendor', 'version', 'patchLevel', 'licenseType'].includes(key)
            : ['vendor', 'version', 'patchLevel', 'licenseType', 'location'].includes(key)).map(([key, label, max]) =>
            <label key={key} className="block text-sm">{label}<input className={inputClass} maxLength={max}
              required={key === 'manufacturer' || key === 'vendor' || key === 'version'
                || key === 'ipAddress' && [0, 2].includes(editor.input.hardwareFunction ?? -1)}
              value={editor.input[key] ?? ''} onChange={event => setEditor({ ...editor, input: { ...editor.input, [key]: event.target.value } })} /></label>)}
          {editor.item?.parentHardwareId && <p className="break-all text-xs">Retained parent hardware: {editor.item.parentHardwareId}</p>}
        </fieldset>}
        <div className="flex flex-wrap gap-3">
          {!readonly && <button type="submit" disabled={busy} className={systemPrimaryAction}>{busy ? 'Saving…' : retiring ? 'Confirm retirement' : 'Save inventory item'}</button>}
          <button type="button" disabled={busy} className={systemSecondaryAction} onClick={retiring ? () => setRetiring(false) : close}>{readonly ? 'Close' : 'Cancel'}</button>
          {!readonly && editor.item && !retiring && <button type="button" className="text-sm text-red-700 underline" disabled={busy} onClick={() => setRetiring(true)}>Retire item</button>}
        </div>
      </form>
    </SetupDialog>}
  </div>;
}
