import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { generateSap } from '../../api/sap';
import { getSapDraft, updateSapDraft, documentActionError, type SapDraft } from './systemDecisionDraftApi';
import { systemPanel, systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';

export default function SapDraftEditor({ systemId, onSaved }: { systemId: string; onSaved: () => void }) {
  const [data, setData] = useState<SapDraft | null>(null);
  const [title, setTitle] = useState('');
  const [lead, setLead] = useState('');
  const [scope, setScope] = useState('');
  const [approach, setApproach] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const request = useRef<AbortController | null>(null);
  const writing = useRef(false);
  const accept = (value: SapDraft) => {
    setData(value); setTitle(value.title ?? ''); setLead(value.assessmentLead ?? '');
    setScope(value.scopeNotes ?? ''); setApproach(value.assessmentApproach ?? '');
  };
  const load = useCallback(async () => {
    request.current?.abort(); const controller = new AbortController(); request.current = controller;
    setLoading(true); setData(null);
    try { const value = await getSapDraft(systemId, controller.signal); if (!controller.signal.aborted) accept(value); }
    catch (reason) { if (!controller.signal.aborted) setError(documentActionError(reason)); }
    finally { if (!controller.signal.aborted) setLoading(false); }
  }, [systemId]);
  useEffect(() => { void load(); return () => request.current?.abort(); }, [load]);
  const save = async (event: FormEvent) => {
    event.preventDefault();
    if (!data?.canEdit || !data.sapId || !data.draftHash || writing.current) return;
    writing.current = true; setBusy(true); setError(null); setNotice(null);
    try {
      accept(await updateSapDraft(systemId, data.sapId, { title, assessmentLead: lead, scopeNotes: scope,
        assessmentApproach: approach, expectedContentHash: data.draftHash }));
      setNotice('Assessment draft saved. Review and finalization remain separate.'); onSaved();
    } catch (reason) {
      setError(`${documentActionError(reason)} The current draft has been reloaded; review again before saving.`);
      await load();
    } finally { writing.current = false; setBusy(false); }
  };
  const create = async () => {
    if (!data?.canCreate || writing.current) return;
    writing.current = true; setBusy(true); setError(null);
    try { await generateSap(systemId); await load(); onSaved(); }
    catch (reason) { setError(documentActionError(reason)); }
    finally { writing.current = false; setBusy(false); }
  };
  const locked = loading || busy || data?.canEdit !== true;
  const input = 'mt-2 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm disabled:bg-slate-50 dark:border-slate-600 dark:bg-slate-900';
  return <section className={systemPanel} aria-labelledby="manual-assessment-draft">
    <h2 id="manual-assessment-draft" className="text-lg font-semibold">Assessment draft record</h2>
    <p className="mt-2 text-sm text-slate-500">Describe the lead, scope and approach in the same retained SAP. Saving rerenders its content; it does not finalize the plan.</p>
    {loading && <p role="status" className="mt-4">Loading assessment draft…</p>}
    {error && <div className="mt-4 text-sm text-amber-900"><p role="alert">{error}</p>
      <button type="button" className={`mt-2 ${systemSecondaryAction}`} onClick={() => { setError(null); void load(); }}>Retry assessment draft</button></div>}
    {notice && <p role="status" className="mt-4 text-sm text-emerald-800">{notice}</p>}
    {data?.sapId ? <form onSubmit={event => void save(event)} className="mt-5 space-y-4">
      <p className="text-xs text-slate-500">Plan {data.sapId} · {data.status}</p>
      <div className="grid gap-4 sm:grid-cols-2">
        <label className="text-sm">Assessment title<input required maxLength={500} disabled={locked} className={input} value={title} onChange={event => setTitle(event.target.value)} /></label>
        <label className="text-sm">Assessment lead<input maxLength={200} disabled={locked} className={input} value={lead} onChange={event => setLead(event.target.value)} /></label>
      </div>
      <div><label htmlFor="sap-manual-scope" className="block text-sm">Assessment scope</label>
        <textarea id="sap-manual-scope" maxLength={4000} rows={4} disabled={locked} className={input} value={scope} onChange={event => setScope(event.target.value)} /></div>
      <div><label htmlFor="sap-manual-approach" className="block text-sm">Assessment approach</label>
        <textarea id="sap-manual-approach" maxLength={4000} rows={4} disabled={locked} className={input} value={approach} onChange={event => setApproach(event.target.value)} /></div>
      {data.canEdit && <button type="submit" disabled={busy || !title.trim()} className={systemPrimaryAction}>{busy ? 'Saving…' : 'Save assessment draft'}</button>}
      {data.status === 'Finalized' && <p className="text-sm text-slate-500">This finalized plan is immutable. Generate a new draft to propose changes.</p>}
    </form> : !loading && data && <p className="mt-4 text-sm">No assessment plan has been generated for this system.</p>}
    {data?.canCreate && data.status !== 'Draft' && <button type="button" disabled={busy} className={`mt-4 ${systemPrimaryAction}`} onClick={() => void create()}>Generate plan draft</button>}
  </section>;
}
