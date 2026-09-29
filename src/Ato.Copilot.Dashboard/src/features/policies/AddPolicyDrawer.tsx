import { useEffect, useRef, useState } from 'react';
import { FileText, Info, Plus, RefreshCw, Search } from 'lucide-react';
import { addPolicyReference, createPolicySource, getPolicyLibrary, getPolicySource, policyError,
  type PolicyLibrary, type PolicyReference, type PolicySource, type PolicyWorkspace } from '../../api/policyWorkspace';
import { PolicyDrawer, PolicySourceCard } from './PolicyDrawer';

const commonPolicies = [
  { name: 'FISMA 2014', description: 'Federal Information Security Modernization Act — requires federal agencies to implement information security programs.' },
  { name: 'Privacy Act of 1974', description: 'Governs the collection, maintenance, use, and dissemination of personally identifiable information by federal agencies.' },
  { name: 'E-Government Act of 2002', description: 'Requires federal agencies to conduct privacy impact assessments for electronic information systems.' },
  { name: 'OMB Circular A-130', description: 'Managing Information as a Strategic Resource — establishes policy for federal information resources.' },
  { name: 'HIPAA', description: 'Health Insurance Portability and Accountability Act — standards for protection of health information.' },
  { name: 'FIPS 199', description: 'Standards for Security Categorization of Federal Information and Information Systems.' },
  { name: 'FIPS 200', description: 'Minimum Security Requirements for Federal Information and Information Systems.' },
  { name: 'NIST SP 800-53 Rev 5', description: 'Security and Privacy Controls for Information Systems and Organizations.' },
  { name: 'NIST SP 800-37 Rev 2', description: 'Risk Management Framework for Information Systems and Organizations.' },
  { name: 'FedRAMP Authorization Act', description: 'Codifies the Federal Risk and Authorization Management Program for cloud security assessment.' },
];
interface Props {
  systemId: string; systemName: string; permissions: PolicyWorkspace['permissions'];
  initialSourceId?: string | null;
  onClose: () => void; onAdded: (reference: PolicyReference) => void;
}
export default function AddPolicyDrawer({ systemId, systemName, permissions, initialSourceId, onClose, onAdded }: Props) {
  const [step, setStep] = useState<'choose' | 'explain' | 'create'>('choose');
  const [view, setView] = useState<'all' | 'common'>('all');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [library, setLibrary] = useState<PolicyLibrary | null>(null);
  const [libraryError, setLibraryError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [selected, setSelected] = useState<PolicySource | null>(null);
  const [previewId, setPreviewId] = useState<string | null>(null);
  const [preview, setPreview] = useState<PolicySource | null>(null);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [rationale, setRationale] = useState('');
  const [name, setName] = useState('');
  const [topic, setTopic] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);
  const alreadyLinked = selected?.alreadyLinked || library?.items.some(source => source.id === selected?.id && source.alreadyLinked);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useEffect(() => {
    const controller = new AbortController();
    if (initialSourceId) getPolicySource(systemId, initialSourceId, controller.signal)
      .then(source => {
        if (!controller.signal.aborted) { setSelected(source); setSearch(source.name); setPage(1); }
      })
      .catch(reason => { if (!controller.signal.aborted) setError(policyError(reason)); });
    return () => controller.abort();
  }, [systemId, initialSourceId]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setLibrary(null); setLibraryError(null);
    getPolicyLibrary(systemId, { search, page, pageSize: 25 }, controller.signal)
      .then(value => { if (!controller.signal.aborted) setLibrary(value); })
      .catch(reason => { if (!controller.signal.aborted) setLibraryError(policyError(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, search, page, revision]);
  useEffect(() => {
    const controller = new AbortController();
    setPreview(null); setPreviewError(null);
    if (previewId) getPolicySource(systemId, previewId, controller.signal)
      .then(value => { if (!controller.signal.aborted) setPreview(value); })
      .catch(reason => { if (!controller.signal.aborted) setPreviewError(policyError(reason)); });
    return () => controller.abort();
  }, [systemId, previewId, revision]);

  const showCreate = () => {
    if (!permissions.canCreateLibrary) { setError(permissions.createReason ?? 'Library creation is not authorized.'); return; }
    const suggestion = commonPolicies.find(p => p.name === search);
    setName(suggestion?.name ?? ''); setDescription(suggestion?.description ?? ''); setTopic('');
    setError(null); setStep('create');
  };
  const create = async () => {
    if (!permissions.canCreateLibrary || busy) { setError(permissions.createReason ?? 'Library creation is not authorized.'); return; }
    if (!name.trim()) { setError('Policy name is required.'); return; }
    setBusy(true); setError(null);
    try {
      const source = await createPolicySource(systemId, { name: name.trim(), subType: topic.trim(), description: description.trim() });
      if (!mounted.current) return;
      setSelected(source); setSearch(source.name); setPage(1); setView('all'); setStep('choose'); setPreviewId(null);
      setRevision(value => value + 1); setNotice('Library policy created; it has not been added to this system. Cancel will keep the shared library record.');
    } catch (reason) { if (mounted.current) setError(policyError(reason)); }
    finally { if (mounted.current) setBusy(false); }
  };
  const add = async () => {
    if (!permissions.canAssign || busy) { setError(permissions.assignReason ?? 'Assignment is not authorized.'); return; }
    if (!selected || alreadyLinked || !rationale.trim() || rationale.trim().length > 500) {
      setError('Choose an unlinked policy and enter a rationale of 1 to 500 characters.'); return;
    }
    setBusy(true); setError(null);
    try {
      const reference = await addPolicyReference(systemId, {
        policyId: selected.id, expectedSourceRevision: selected.revision, rationale: rationale.trim(),
      });
      if (mounted.current) onAdded(reference);
    } catch (reason) { if (mounted.current) setError(policyError(reason)); }
    finally { if (mounted.current) setBusy(false); }
  };
  const next = () => {
    if (selected && !alreadyLinked && permissions.canAssign) { setStep('explain'); setPreviewId(null); setError(null); }
  };
  const refreshLibrary = () => { setSelected(null); setPreviewId(null); setError(null); setRevision(value => value + 1); };
  const back = () => { if (error) refreshLibrary(); setStep('choose'); setError(null); };

  return <PolicyDrawer title="Add policy to this system" label="Add policy" systemName={systemName} busy={busy} onClose={onClose}
    footer={<>
      <button type="button" disabled={busy} onClick={step === 'choose' ? onClose : back}>{step === 'choose' ? 'Cancel' : 'Back'}</button>
      {step === 'choose' && <button type="button" className="pw-primary" disabled={!selected || alreadyLinked || !permissions.canAssign || busy} onClick={next}>Continue</button>}
      {step === 'explain' && <button type="button" className="pw-primary" disabled={!permissions.canAssign || !rationale.trim() || busy} onClick={() => void add()}>{busy ? 'Adding…' : 'Add system reference'}</button>}
      {step === 'create' && <button type="button" className="pw-primary" disabled={!permissions.canCreateLibrary || !name.trim() || busy} onClick={() => void create()}>{busy ? 'Saving…' : 'Save library policy'}</button>}
    </>}>
    <ol className="pw-stepper" aria-label="Add policy steps"><li aria-current={step !== 'explain' ? 'step' : undefined}><span>1</span> Choose policy</li>
      <li aria-current={step === 'explain' ? 'step' : undefined}><span>2</span> Explain &amp; add</li></ol>
    {error && <p className="pw-error" role="alert">{error}</p>}
    {notice && <p className="pw-notice" role="status">{notice}</p>}
    {!permissions.canAssign && <p className="pw-notice">{permissions.assignReason}</p>}
    {step === 'choose' && <>
      <div className="pw-library-toolbar"><label className="pw-search"><Search size={16} /><input aria-label="Find a policy by name or topic"
        placeholder="Find a policy by name or topic…" value={search} onChange={e => { setSearch(e.target.value); setPage(1); setSelected(null); }} /></label>
        <button type="button" aria-label="Refresh library" title="Refresh library" disabled={loading} onClick={refreshLibrary}><RefreshCw size={15} /></button></div>
      <div className="pw-segmented" role="tablist" aria-label="Policy library views">
        <button type="button" role="tab" aria-selected={view === 'all'} onClick={() => setView('all')}>All policies</button>
        <button type="button" role="tab" aria-selected={view === 'common'} onClick={() => setView('common')}>Common references</button>
      </div>
      <p className="pw-hint"><Info size={14} />Common references are suggestions, not automatic requirements.</p>
      {view === 'common' ? <div className="pw-options">
        {commonPolicies.filter(p => `${p.name} ${p.description}`.toLowerCase().includes(search.toLowerCase())).map(policy =>
          <article className="pw-option" key={policy.name}><strong>{policy.name}</strong><p>{policy.description}</p>
            <small>Suggestion · choose an authorized library record before adding.</small>
            <button type="button" className="pw-link-button" onClick={() => { setSearch(policy.name); setPage(1); setView('all'); setSelected(null); }}>Find in library</button>
          </article>)}
        {!commonPolicies.some(p => `${p.name} ${p.description}`.toLowerCase().includes(search.toLowerCase())) && <p>No common references match this search.</p>}
      </div> : <>
        {loading && <p role="status">Loading organization policies…</p>}
        {libraryError && <div className="pw-error" role="alert"><p>{libraryError}</p><button type="button" onClick={() => setRevision(value => value + 1)}>Retry library</button></div>}
        {!loading && library && !library.items.length && <div className="pw-library-empty">
          <FileText size={24} /><h3>{search ? 'No matching library policies.' : 'The policy library is empty.'}</h3>
          <p>{search ? 'Try another name or topic, or explicitly create the missing source if authorized.' : 'An authorized library author can create the first source.'}</p>
        </div>}
        <div className="pw-options">{library?.items.map(source => <article key={source.id} className={`pw-option${selected?.id === source.id ? ' pw-option-selected' : ''}`}>
          <label><input type="radio" name="policy-source" aria-label={`${source.name} ${source.versionLabel}`} disabled={source.alreadyLinked || !permissions.canAssign}
            checked={selected?.id === source.id} onChange={() => { setSelected(source); setError(null); }} />
            <span><strong>{source.name}</strong><small>Organization library · {source.versionLabel}</small></span></label>
          <span className="pw-pill">{source.status} source</span>
          {source.alreadyLinked && <small>Already linked</small>}
          <button type="button" className="pw-link-button" onClick={() => setPreviewId(source.id)}>View source</button>
        </article>)}</div>
        {library && library.totalCount > library.pageSize && <nav className="pw-pager" aria-label="Library pages">
          <button type="button" disabled={page <= 1} onClick={() => { setPage(p => p - 1); setSelected(null); }}>Previous</button>
          <span>{page} of {Math.ceil(library.totalCount / library.pageSize)}</span>
          <button type="button" disabled={page * library.pageSize >= library.totalCount} onClick={() => { setPage(p => p + 1); setSelected(null); }}>Next</button>
        </nav>}
      </>}
      {previewId && <div className="pw-preview">
        {previewError ? <div role="alert" className="pw-error"><p>{previewError}</p>
          <button type="button" onClick={() => setRevision(value => value + 1)}>Retry source</button></div>
          : preview ? <PolicySourceCard source={preview} heading="Current library source" /> : <p role="status">Loading source…</p>}
        <button type="button" onClick={() => setPreviewId(null)}>Close source preview</button>
      </div>}
      <p className="pw-hint">Source status does not approve system applicability.</p>
      {permissions.canCreateLibrary && <button type="button" className="pw-create-link" aria-label="Create a library policy" onClick={showCreate}><Plus size={17} />
        <span>Create a library policy<small>Creates a shared organization record only</small></span></button>}
      {!permissions.canCreateLibrary && <p className="pw-hint">{permissions.createReason}</p>}
    </>}
    {step === 'create' && <form onSubmit={e => { e.preventDefault(); void create(); }} className="pw-form">
      <h3>Create a library policy</h3>
      <p>This saves a shared organization-library source. It does not assign the policy to this system.
        After saving, return to selection and explain its applicability.</p>
      <label>Policy name<input autoFocus maxLength={200} value={name} onChange={e => setName(e.target.value)} disabled={busy} /></label>
      <label>Topic<input maxLength={100} value={topic} onChange={e => setTopic(e.target.value)} disabled={busy} /></label>
      <label>Source description<textarea maxLength={2000} rows={5} value={description} onChange={e => setDescription(e.target.value)} disabled={busy} /></label>
      <small>{description.length}/2000 · The new source will be Active, not applicability-approved.</small>
    </form>}
    {step === 'explain' && selected && <>
      <PolicySourceCard source={selected} heading="Selected policy" />
      <label className="pw-rationale">Why does this apply to this system?
        <textarea autoFocus rows={5} maxLength={500} value={rationale} disabled={busy} onChange={e => setRationale(e.target.value)} />
      </label>
      <div className="pw-rationale-meta"><span>Applicability review is not implemented.</span><span>{rationale.length}/500</span></div>
      <details className="pw-disclosure"><summary>Related controls and supporting references (optional)</summary>
        <p>{selected.relatedControls.length ? `Related capability controls: ${selected.relatedControls.join(', ')}` : 'No related capability controls recorded.'}</p>
        <p>These are existing source relationships. Additional supporting references are not stored by the current assignment contract.</p>
      </details>
      <section className="pw-save-preview" aria-label="System reference preview"><h3>System reference preview</h3>
        <dl><dt>System</dt><dd>{systemName}</dd><dt>Policy</dt><dd>{selected.name}</dd><dt>Retained source</dt><dd>{selected.versionLabel}</dd>
          <dt>Rationale</dt><dd className="pw-prose">{rationale.trim() || 'Enter a rationale above.'}</dd></dl>
        <p>Saves this source snapshot and your rationale. The shared library policy stays unchanged.</p>
      </section>
    </>}
  </PolicyDrawer>;
}
