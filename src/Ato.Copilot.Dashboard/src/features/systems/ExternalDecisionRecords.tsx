import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import SetupDialog from '../workspace-operations/SetupDialog';
import { getExternalDecisionContext, recordExternalDecision, documentActionError, type ExternalDecisionContext } from './systemDecisionDraftApi';
import { systemPanel, systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';

export default function ExternalDecisionRecords({ systemId, onRecorded }: { systemId: string; onRecorded: () => void }) {
  const [data, setData] = useState<ExternalDecisionContext | null>(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [sourceId, setSourceId] = useState('');
  const [packageId, setPackageId] = useState('');
  const [authority, setAuthority] = useState('');
  const [decisionType, setDecisionType] = useState('ATO');
  const [decisionDate, setDecisionDate] = useState('');
  const [expirationDate, setExpirationDate] = useState('');
  const [risk, setRisk] = useState('Medium');
  const [terms, setTerms] = useState('');
  const [makeCurrent, setMakeCurrent] = useState(false);
  const [reviewed, setReviewed] = useState(false);
  const request = useRef<AbortController | null>(null);
  const writing = useRef(false);
  const load = useCallback(async () => {
    request.current?.abort(); const controller = new AbortController(); request.current = controller;
    setLoading(true); setData(null); setReviewed(false); setSourceId(''); setPackageId('');
    try { const value = await getExternalDecisionContext(systemId, page, controller.signal); if (!controller.signal.aborted) setData(value); }
    catch (reason) { if (!controller.signal.aborted) setError(documentActionError(reason)); }
    finally { if (!controller.signal.aborted) setLoading(false); }
  }, [systemId, page]);
  useEffect(() => { void load(); return () => request.current?.abort(); }, [load]);
  const source = data?.sourceEvidence.find(item => item.id === sourceId);
  const baseline = data?.completedPackages.find(item => item.id === packageId);
  const valid = data?.canRecord && source && baseline && authority.trim() && decisionDate && reviewed
    && (decisionType === 'DATO' || expirationDate);
  const save = async (event: FormEvent) => {
    event.preventDefault();
    if (!valid || !source || !baseline || !data || writing.current) return;
    writing.current = true; setBusy(true); setError(null); setNotice(null);
    try {
      const saved = await recordExternalDecision(systemId, {
        decisionType, decisionDate, expirationDate: expirationDate || null, residualRiskLevel: risk,
        sourceEvidenceId: source.id, expectedSourceHash: source.contentHash, issuingAuthority: authority.trim(),
        baselinePackageId: baseline.id, expectedPackageHash: baseline.contentHash, termsAndConditions: terms,
        makeCurrent, expectedActiveDecisionId: data.activeDecisionId,
      });
      setNotice(`External decision ${saved.id} recorded. No package was generated and no eMASS outcome was inferred.`);
      setOpen(false); onRecorded(); await load();
    } catch (reason) {
      setError(`${documentActionError(reason)} Source and package selections were cleared for a fresh review.`);
      await load();
    } finally { writing.current = false; setBusy(false); }
  };
  const input = 'mt-1 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm dark:border-slate-600 dark:bg-slate-900';
  const totalPages = data ? Math.max(1, Math.ceil(Math.max(data.sourceTotal, data.packageTotal, data.recordTotal) / data.pageSize)) : 1;
  return <section className={systemPanel} aria-label="Externally issued decision records">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h2 className="text-lg font-semibold">Externally issued decisions</h2>
        <p className="mt-2 text-sm text-slate-500">Record a received decision against its retained source and exact completed package. This action is distinct from issuing a new AO decision.</p></div>
      {data?.canRecord && <button type="button" className={systemPrimaryAction} onClick={() => setOpen(true)}>Record external decision</button>}
    </div>
    {loading && <p role="status" className="mt-4 text-sm">Loading source, package and decision records…</p>}
    {error && <div className="mt-4 text-sm text-amber-900"><p role="alert">{error}</p>
      <button type="button" className={`mt-2 ${systemSecondaryAction}`} onClick={() => { setError(null); void load(); }}>Refresh decision context</button></div>}
    {notice && <p role="status" className="mt-4 text-sm text-emerald-800">{notice}</p>}
    {data && <>
      {!data.canRecord && <p className="mt-4 text-sm text-slate-500">AO permission is required to record an external decision; retained history is read-only.</p>}
      <div className="mt-5 space-y-4">{data.records.map(record => <article key={record.id} className="rounded-lg border border-slate-200 p-4 text-sm dark:border-slate-700">
        <h3 className="font-semibold">{record.decisionType} · {record.externalIssuingAuthority ?? 'Internally issued decision'}</h3>
        <p className="mt-2">{record.decisionDate} · {record.isActive ? 'Current recorded decision' : 'Historical record'}</p>
        {record.termsAndConditions && <p className="mt-2 whitespace-pre-wrap">{record.termsAndConditions}</p>}
        <details className="mt-3"><summary className="cursor-pointer font-medium">Source and baseline references</summary>
          <dl className="mt-3 space-y-2">{[
            ['Decision record', record.id], ['Source evidence', record.sourceEvidenceId], ['Source hash', record.sourceEvidenceHash],
            ['Retained package', record.baselinePackageId], ['Package hash', record.baselinePackageHash],
            ['Recorded by', record.recordedBy], ['Recorded at', record.recordedAt],
          ].map(([label, value]) => <div key={label}><dt className="text-xs text-slate-500">{label}</dt><dd className="break-all">{value ?? 'Not recorded on this historical record'}</dd></div>)}</dl>
        </details>
      </article>)}</div>
      {data.records.length === 0 && <p className="mt-4 text-sm text-slate-500">No decisions are retained on this page.</p>}
      {totalPages > 1 && <nav aria-label="Decision context pages" className="mt-4 flex flex-wrap gap-3 text-sm">
        <button type="button" disabled={page <= 1 || busy} className={systemSecondaryAction} onClick={() => setPage(value => value - 1)}>Previous sources and records</button>
        <span>Page {page} of {totalPages}</span>
        <button type="button" disabled={page >= totalPages || busy} className={systemSecondaryAction} onClick={() => setPage(value => value + 1)}>Next sources and records</button>
      </nav>}
    </>}
    {open && <SetupDialog title="Record external authorization decision" busy={busy} onClose={() => setOpen(false)}
      description="The authenticated AO records the received authority; source selection and package references do not issue a decision automatically.">
      <form className="space-y-4" onSubmit={event => void save(event)}>
        {error && <p className="text-sm text-amber-900">{error}</p>}
        <label className="block text-sm">Decision source<select className={input} value={sourceId} onChange={event => { setSourceId(event.target.value); setReviewed(false); }}>
          <option value="">Select retained evidence</option>{data?.sourceEvidence.map(item => <option key={item.id} value={item.id}>{item.fileName ?? item.id}</option>)}
        </select></label>
        <Link className="block text-sm text-indigo-700 underline" to={`/systems/${systemId}/evidence`}>Upload or review decision source evidence</Link>
        <label className="block text-sm">Issuing authority as recorded<input required maxLength={500} className={input} value={authority} onChange={event => { setAuthority(event.target.value); setReviewed(false); }} /></label>
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="text-sm">Decision type<select className={input} value={decisionType} onChange={event => { setDecisionType(event.target.value); setReviewed(false); }}>
            {['ATO', 'ATOwC', 'IATT', 'DATO'].map(type => <option key={type}>{type}</option>)}</select></label>
          <label className="text-sm">Decision date<input required type="date" className={input} value={decisionDate} onChange={event => { setDecisionDate(event.target.value); setReviewed(false); }} /></label>
          <label className="text-sm">Expiration date<input required={decisionType !== 'DATO'} type="date" className={input} value={expirationDate} onChange={event => { setExpirationDate(event.target.value); setReviewed(false); }} /></label>
          <label className="text-sm">Recorded residual risk<select className={input} value={risk} onChange={event => { setRisk(event.target.value); setReviewed(false); }}>
            {['Low', 'Medium', 'High', 'Critical'].map(level => <option key={level}>{level}</option>)}</select></label>
        </div>
        <label className="block text-sm">Applicable retained package<select className={input} value={packageId} onChange={event => { setPackageId(event.target.value); setReviewed(false); }}>
          <option value="">Select completed package</option>{data?.completedPackages.map(item => <option key={item.id} value={item.id}>{item.id} · {item.generatedAt}</option>)}
        </select></label>
        {baseline && <p className="break-all text-xs text-slate-500">Package hash: {baseline.contentHash}</p>}
        {source && <p className="break-all text-xs text-slate-500">Decision source hash: {source.contentHash}</p>}
        <label className="block text-sm">Recorded conditions<textarea rows={3} maxLength={8000} className={input} value={terms} onChange={event => { setTerms(event.target.value); setReviewed(false); }} /></label>
        <label className="flex items-start gap-2 text-sm"><input type="checkbox" checked={makeCurrent} onChange={event => { setMakeCurrent(event.target.checked); setReviewed(false); }} />Make this the current recorded decision, superseding the existing one.</label>
        <label className="flex items-start gap-2 text-sm"><input type="checkbox" checked={reviewed} onChange={event => setReviewed(event.target.checked)} />I reviewed the source and exact retained package.</label>
        <button type="submit" className={systemPrimaryAction} disabled={!valid || busy}>{busy ? 'Recording…' : 'Save recorded decision'}</button>
        <p className="text-xs text-slate-500">Historical records and package bytes are retained unchanged. No eMASS receipt or RMF phase advancement is inferred.</p>
      </form>
    </SetupDialog>}
  </section>;
}
