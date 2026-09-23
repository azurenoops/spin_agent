import { useEffect, useRef, useState, type ReactNode } from 'react';
import { ArrowLeft, ArrowUpFromLine, BookOpen, Check, FileText, Trash2 } from 'lucide-react';
import type { NarrativeReference, ReferenceDraftUpdate, ReferencePassage } from '../../api/narrativeLibrary';
import { narrativeErrorCode, narrativeErrorMessage } from './referenceLibraryUtils';

export interface ReferenceScopeChoice {
  scope: string;
  id?: string;
  available: boolean;
  capability?: boolean;
}
interface Props {
  view: 'library' | 'import';
  contextLabel: string;
  scopes: ReferenceScopeChoice[];
  capabilities: { id: string; name: string }[];
  references: NarrativeReference[];
  canAuthor: boolean;
  locked: boolean;
  loadError: boolean;
  onNavigate: (view: 'library' | 'import') => void;
  onChanged: () => void;
  onBusyChange?: (busy: boolean) => void;
  onBack?: () => void;
  footer?: ReactNode;
  api: {
    import: (form: FormData) => Promise<NarrativeReference>;
    update: (id: string, request: ReferenceDraftUpdate) => Promise<NarrativeReference>;
    publish: (id: string, revision: number, passages: ReferencePassage[]) => Promise<NarrativeReference>;
  };
}

export default function ReferenceLibraryManager(props: Props) {
  const [filter, setFilter] = useState('All');
  const [title, setTitle] = useState('');
  const [scope, setScope] = useState(props.scopes[0]?.scope ?? '');
  const [capabilityId, setCapabilityId] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [paste, setPaste] = useState('');
  const [draft, setDraft] = useState<NarrativeReference | null>(null);
  const [passages, setPassages] = useState<ReferencePassage[]>([]);
  const [reviewed, setReviewed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [denied, setDenied] = useState(false);
  const [error, setError] = useState('');
  const active = useRef(true);
  const changing = useRef(false);
  const authorized = useRef(props.canAuthor);
  authorized.current = props.canAuthor;
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  const locked = props.locked || busy || denied;
  const canAuthor = props.canAuthor === true && !locked;
  const choice = props.scopes.find(item => item.scope === scope);
  const scopeId = choice?.capability ? capabilityId : choice?.id ?? '';
  const permitted = (selectedScope: string, selectedId: string) => {
    const target = props.scopes.find(item => item.scope === selectedScope);
    return props.canAuthor === true && target?.available === true && (target.capability
      ? props.capabilities.some(item => item.id === selectedId) : target.id === selectedId);
  };
  const complete = passages.length > 0 && passages.every(passage =>
    /^[A-Z]{2,3}-\d+(?:\(\d+\))?$/i.test(passage.controlId?.trim() ?? '')
    && ['Policy', 'Technical'].includes(passage.narrativeType ?? '') && passage.content.trim().length > 0 && passage.content.length <= 20000);
  const sameScope = draft?.scope === scope && draft.scopeId === scopeId;
  const canPublish = canAuthor && !!draft && permitted(scope, scopeId) && sameScope && complete && reviewed;
  const latest = props.references.filter(item => !props.references.some(other =>
    other.referenceKey === item.referenceKey && other.isPublished && item.isPublished && other.version > item.version));

  function beginDraft(reference: NarrativeReference) {
    if (reference.isPublished || !permitted(reference.scope, reference.scopeId)) {
      setError('Reference authoring permission is required for this scope.');
      return;
    }
    setDraft(reference); setScope(reference.scope); setCapabilityId(reference.scopeId);
    setPassages(reference.passages.map(passage => ({ ...passage }))); setReviewed(false);
    props.onNavigate('import');
  }
  async function perform(operation: () => Promise<void>) {
    if (changing.current || locked) return;
    if (!permitted(scope, scopeId)) { setError('Reference authoring permission is required for this scope.'); return; }
    changing.current = true; setBusy(true); props.onBusyChange?.(true); setError('');
    try { await operation(); }
    catch (reason) {
      if (!active.current) return;
      setError(narrativeErrorMessage(reason));
      const code = narrativeErrorCode(reason);
      if (code === 'FORBIDDEN' || code === 'NOT_FOUND') { setDenied(true); props.onChanged(); }
      if (code === 'CONCURRENCY_CONFLICT') {
        setDraft(null); setPassages([]); setReviewed(false); props.onNavigate('library'); props.onChanged();
      }
    } finally {
      changing.current = false;
      if (active.current) setBusy(false);
      props.onBusyChange?.(false);
    }
  }
  const current = () => active.current && authorized.current;
  const extract = () => void perform(async () => {
    const content = file ?? new File([paste], 'pasted-reference.txt', { type: 'text/plain' });
    if (!content.size || content.size > 5 * 1024 * 1024) throw new Error('Reference uploads must be nonempty and must not exceed 5 MB.');
    const form = new FormData();
    form.append('title', title.trim()); form.append('scope', scope); form.append('scopeId', scopeId); form.append('file', content);
    const result = await props.api.import(form);
    if (current()) beginDraft(result);
    props.onChanged();
  });
  const save = () => {
    if (!draft) { setError('Open an unpublished reference before saving mappings.'); return; }
    void perform(async () => {
      const result = await props.api.update(draft.id, { expectedRevision: draft.revision, scope, scopeId, passages });
      if (current()) beginDraft(result);
      props.onChanged();
    });
  };
  const publish = () => {
    if (!canPublish || !draft) { setError('Complete and review the current mappings before publishing.'); return; }
    void perform(async () => {
      await props.api.publish(draft.id, draft.revision, passages);
      if (current()) {
        setDraft(null); setPassages([]); setReviewed(false); setFile(null); setPaste(''); setTitle('');
        props.onNavigate('library');
      }
      props.onChanged();
    });
  };
  const update = (index: number, change: Partial<ReferencePassage>) => {
    setPassages(items => items.map((item, position) => position === index ? { ...item, ...change } : item)); setReviewed(false);
  };
  const scopePicker = <div className="nw-two-columns">
    <label>Reference scope<select value={scope} disabled={locked} onChange={event => { setScope(event.target.value); setReviewed(false); }}>
      {props.scopes.map(item => <option key={item.scope} disabled={!item.available}>{item.scope}</option>)}
    </select></label>
    {choice?.capability ? <label>Capability<select value={capabilityId} disabled={locked} onChange={event => { setCapabilityId(event.target.value); setReviewed(false); }}>
      <option value="">Select capability</option>{props.capabilities.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
    </select></label> : <div className="nw-scope-target">{props.contextLabel}<span className="nw-muted">Pre-written reference</span></div>}
  </div>;

  return <>
    {error && <div role="alert" className="nw-alert">{error}</div>}
    {props.view === 'library' ? <>
      <header className="nw-header"><div><h2>Narrative Library</h2><p>Reference claims, separate from implementation evidence.</p></div>
        {props.onBack && <div className="nw-mobile-navigation"><button type="button" onClick={props.onBack}><ArrowLeft size={16} />Narratives</button></div>}
        <button className="nw-primary" disabled={!canAuthor} onClick={() => { setDraft(null); setReviewed(false); props.onNavigate('import'); }}><ArrowUpFromLine size={16} />Upload narratives</button>
      </header>
      <div className="nw-toolbar"><label>Reference scope<select value={filter} onChange={event => setFilter(event.target.value)}>
        <option value="All">All available</option>{props.scopes.map(item => <option key={item.scope}>{item.scope}</option>)}
      </select></label><span className="nw-muted">{props.contextLabel}</span></div>
      {!props.locked && !props.loadError && latest.length === 0 && <p className="nw-empty">No reference narratives published.</p>}
      <div className="nw-reference-list">{latest.filter(item => filter === 'All' || item.scope === filter).map(reference =>
        <article key={reference.id} className="nw-reference">
          <div className="nw-reference-heading"><BookOpen size={20} /><h3>{reference.title}</h3><span className="nw-badge">{reference.isPublished ? `Published v${reference.version}` : 'Import draft'}</span></div>
          <div className="nw-meta"><span>{reference.scope}</span><span>{reference.sourceName}</span><span>{reference.passages.length} passages</span></div>
          {reference.passages.map((passage, index) => <div key={index} className="nw-passage">
            <span className="nw-badge">{passage.controlId ?? 'Unmapped'}</span><span className="nw-muted">{passage.narrativeType ?? 'Unclassified'}</span><p>{passage.content}</p>
          </div>)}
          {(reference.scope === 'Capability' || reference.scope === 'ProviderCapability') && <p className="nw-warning">Inherited responsibilities require confirmation.</p>}
          {!reference.isPublished && <button disabled={!canAuthor || !permitted(reference.scope, reference.scopeId)} onClick={() => beginDraft(reference)}>Review import</button>}
          {reference.isPublished && <details><summary>Version provenance</summary>
            <p>Published by {reference.publishedBy} on {reference.publishedAt ? new Date(reference.publishedAt).toLocaleString() : 'Unknown'}</p>
            <p className="nw-hash">SHA-256 {reference.sourceSha256}</p>
            {props.references.filter(item => item.referenceKey === reference.referenceKey && item.version < reference.version).map(item =>
              <div key={item.id}>Version {item.version} / {item.isPublished ? 'Published' : 'Draft'}<pre>{item.passages.map(passage => `${passage.controlId}: ${passage.content}`).join('\n\n')}</pre></div>)}
          </details>}
        </article>)}</div>
      {props.footer}
    </> : <>
      <header className="nw-header"><div><h2>Import &amp; map</h2><p>{draft ? draft.sourceName : props.contextLabel}</p></div>
        <button onClick={() => props.onNavigate('library')}><ArrowLeft size={16} />Library</button></header>
      {!draft ? <section className="nw-import">
        <label>Reference title<input value={title} maxLength={200} disabled={locked} onChange={event => setTitle(event.target.value)} /></label>
        {scopePicker}
        <label>Upload file<input type="file" accept=".xlsx,.csv,.docx,.pdf,.txt,.md" disabled={locked} onChange={event => { setFile(event.target.files?.[0] ?? null); setPaste(''); }} /></label>
        <label>Paste narratives<textarea rows={8} value={paste} disabled={locked || file !== null} onChange={event => setPaste(event.target.value)} /></label>
        {file && <button disabled={locked} onClick={() => setFile(null)}><Trash2 size={16} />Remove selected file</button>}
        <p className="nw-muted">XLSX, CSV, DOCX, digital PDF, TXT, Markdown / Maximum 5 MB</p>
        <button className="nw-primary" disabled={!canAuthor || !title.trim() || (!file && !paste.trim()) || (choice?.capability && !capabilityId)} onClick={extract}>
          <FileText size={16} />Extract passages</button>
      </section> : <section>
        <div className="nw-section-heading"><h3>Review before publishing</h3><span className="nw-muted">{passages.length} extracted passages</span></div>
        {scopePicker}
        {passages.map((passage, index) => <article className="nw-mapping" key={index}>
          <div className="nw-section-heading"><h3>Passage {index + 1}</h3><button title={`Remove passage ${index + 1}`} disabled={!canAuthor}
            onClick={() => { setPassages(items => items.filter((_, position) => position !== index)); setReviewed(false); }}><Trash2 size={16} /></button></div>
          <textarea aria-label={`Passage ${index + 1}`} value={passage.content} rows={4} maxLength={20000} disabled={!canAuthor} onChange={event => update(index, { content: event.target.value })} />
          <div className="nw-two-columns"><label>Map to control<input aria-label={`Control ${index + 1}`} value={passage.controlId ?? ''} placeholder="Unmapped" maxLength={20} disabled={!canAuthor}
            onChange={event => update(index, { controlId: event.target.value.toUpperCase() })} /></label>
            <label>Narrative type<select aria-label={`Narrative type ${index + 1}`} value={passage.narrativeType ?? ''} disabled={!canAuthor}
              onChange={event => update(index, { narrativeType: event.target.value })}><option value="">Unclassified</option><option>Policy</option><option>Technical</option></select></label></div>
          <p className="nw-warning">Imported reference claim. Execution has not been verified.</p>
        </article>)}
        <label className="nw-checkbox"><input type="checkbox" checked={reviewed} disabled={!canAuthor} onChange={event => setReviewed(event.target.checked)} />I reviewed these reference claims and mappings</label>
        {!sameScope && <p className="nw-warning">Save the changed scope before publishing.</p>}
        <div className="flex flex-wrap gap-2">
          <button disabled={!canAuthor || !permitted(scope, scopeId)} onClick={save}>Save draft mappings</button>
          <button className="nw-primary" disabled={!canPublish} onClick={publish}><Check size={16} />Publish references</button>
        </div>
      </section>}
    </>}
  </>;
}
