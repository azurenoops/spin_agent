import { useEffect, useState } from 'react';
import { useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { ArrowLeft, ArrowUpFromLine, BookOpen, Check, FileText, GitCompareArrows, RefreshCw, ShieldAlert, Trash2 } from 'lucide-react';
import { diffWordsWithSpace } from 'diff';
import Narratives from './Narratives';
import { generateProposal, getNarrativeAccess, getProposals, getReferences, importReference, publishReference, reviewProposal,
  type NarrativeAccess, type NarrativeProposal, type NarrativeReference, type ReferencePassage } from '../api/narrativeLibrary';
import { useSettings } from '../hooks/useSettings';
import './NarrativeWorkspace.css';

function errorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'error' in error && typeof error.error === 'string') return error.error;
  return error instanceof Error ? error.message : 'The operation failed. Your active narratives are unchanged.';
}

export default function NarrativeWorkspace() {
  const { id } = useParams<{ id: string }>();
  return id ? <Workspace key={id} systemId={id} /> : null;
}

function Workspace({ systemId }: { systemId: string }) {
  const location = useLocation();
  const navigate = useNavigate();
  const [query] = useSearchParams();
  const { settings, updateSettings } = useSettings();
  const base = `/systems/${encodeURIComponent(systemId)}/narratives`;
  const view = location.pathname.slice(base.length).split('/')[1] || 'narratives';
  const [references, setReferences] = useState<NarrativeReference[]>([]);
  const [proposals, setProposals] = useState<NarrativeProposal[]>([]);
  const [access, setAccess] = useState<NarrativeAccess | null>(null);
  const [revision, setRevision] = useState(0);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [loadError, setLoadError] = useState('');
  const [scopeFilter, setScopeFilter] = useState('All');
  const [title, setTitle] = useState('');
  const [scope, setScope] = useState('System');
  const [capabilityId, setCapabilityId] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [paste, setPaste] = useState('');
  const [draft, setDraft] = useState<NarrativeReference | null>(null);
  const [passages, setPassages] = useState<ReferencePassage[]>([]);
  const [reviewed, setReviewed] = useState(false);
  const [note, setNote] = useState('');
  const [clearLegacy, setClearLegacy] = useState(false);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setLoadError('');
    Promise.all([getReferences(systemId), getProposals(systemId), getNarrativeAccess(systemId)])
      .then(([nextReferences, nextProposals, nextAccess]) => {
        if (!active) return;
        setReferences(nextReferences); setProposals(nextProposals); setAccess(nextAccess);
      }).catch(reason => {
        if (!active) return;
        setReferences([]); setProposals([]); setAccess(null); setLoadError(errorMessage(reason));
      })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [systemId, revision]);
  useEffect(() => { setNote(''); }, [query.get('proposal')]);

  const go = (next: string) => navigate(next === 'narratives' ? base : `${base}/${next}`);
  const locked = busy || loading || Boolean(loadError);
  const canAuthor = !locked && access?.canAuthor === true;
  const proposal = proposals.find(item => item.id === query.get('proposal')) ?? proposals.find(item => item.status === 'Draft') ?? proposals[0];
  const latestReferences = references.filter(item => !references.some(other => other.referenceKey === item.referenceKey &&
    other.isPublished && item.isPublished && other.version > item.version));
  const hasLegacy = Boolean(settings.sharePointSiteUrl || settings.sourceDocuments);
  const readyToPublish = canAuthor && Boolean(draft) && reviewed && passages.length > 0 && passages.every(passage =>
    /^[A-Z]{2,3}-\d+(?:\(\d+\))?$/i.test(passage.controlId?.trim() ?? '') &&
    ['Policy', 'Technical'].includes(passage.narrativeType ?? '') && passage.content.trim().length > 0 && passage.content.length <= 20000);

  async function perform(action: () => Promise<void>) {
    if (busy) return;
    setBusy(true); setError('');
    try { await action(); } catch (reason) { setError(errorMessage(reason)); } finally { setBusy(false); }
  }
  function openDraft(reference: NarrativeReference) {
    setDraft(reference); setPassages(reference.passages.map(passage => ({ ...passage })));
    setReviewed(false); go('import');
  }
  function updatePassage(index: number, update: Partial<ReferencePassage>) {
    setPassages(current => current.map((passage, position) => position === index ? { ...passage, ...update } : passage));
    setReviewed(false);
  }
  async function generate(control: string, version: number, type: 'Policy' | 'Technical') {
    await perform(async () => {
      const created = await generateProposal(systemId, control, type, version);
      setProposals(current => [created, ...current.filter(item => item.id !== created.id)]);
      navigate(`${base}/review?proposal=${encodeURIComponent(created.id)}`);
      setRevision(current => current + 1);
    });
  }
  function extract() {
    void perform(async () => {
      const content = file ?? new File([paste], 'pasted-reference.txt', { type: 'text/plain' });
      if (content.size > 5 * 1024 * 1024) throw new Error('Reference uploads must not exceed 5 MB.');
      const form = new FormData();
      form.append('title', title); form.append('scope', scope);
      form.append('scopeId', scope === 'System' ? systemId : scope === 'Organization' ? access!.tenantId : capabilityId);
      form.append('file', content);
      openDraft(await importReference(systemId, form));
      setRevision(current => current + 1);
    });
  }
  function publish() {
    if (!draft || !readyToPublish) return;
    void perform(async () => {
      await publishReference(systemId, draft.id, draft.revision, passages);
      setDraft(null); setPassages([]); setReviewed(false); setFile(null); setPaste(''); setTitle('');
      go('library'); setRevision(current => current + 1);
    });
  }
  function decide(decision: string) {
    if (!proposal || !proposal.canReview || locked || proposal.isStale && decision === 'Approve') return;
    void perform(async () => {
      const result = await reviewProposal(systemId, proposal.id, proposal.revision, decision, note);
      setProposals(current => current.map(item => item.id === result.id ? result : item));
      setRevision(current => current + 1);
    });
  }
  const pending = proposals.filter(item => item.status === 'Draft');

  return <div className="narrative-workspace">
    {(loadError || error) && <div role="alert" className="nw-alert"><ShieldAlert size={18} /><span>{loadError || error}</span>
      <button type="button" onClick={() => { setError(''); setRevision(current => current + 1); }} title="Retry loading"><RefreshCw size={16} /></button></div>}
    {loading && <p role="status" className="nw-muted">Loading narrative context...</p>}

    {view === 'narratives' && <>
      {pending.length > 0 && <div className="nw-notice"><GitCompareArrows size={18} /><span>{pending.length} proposed update{pending.length === 1 ? '' : 's'} awaiting review</span>
        <button onClick={() => go('review')}>Review changes</button></div>}
      <Narratives key={revision} onGenerateDraft={generate} proposals={proposals} canGenerate={canAuthor}
        onOpenLibrary={() => go('library')}
        onReviewProposal={item => navigate(`${base}/review?proposal=${encodeURIComponent(item.id)}`)} />
    </>}

    {view === 'library' && <>
      <header className="nw-header"><div><h2>Narrative Library</h2><p>Reference claims, separate from implementation evidence.</p></div>
        <div className="nw-mobile-navigation"><button type="button" onClick={() => go('narratives')}><ArrowLeft size={16} />Narratives</button></div>
        <button className="nw-primary" onClick={() => { setDraft(null); go('import'); }} disabled={!canAuthor}><ArrowUpFromLine size={16} />Upload narratives</button></header>
      <div className="nw-toolbar"><label>Reference scope<select value={scopeFilter} onChange={event => setScopeFilter(event.target.value)}>
        <option value="All">All available</option><option>Organization</option><option>System</option><option>Capability</option></select></label>
        <span className="nw-muted">{access?.systemName}</span></div>
      {!loading && !loadError && latestReferences.length === 0 && <p className="nw-empty">No reference narratives published.</p>}
      <div className="nw-reference-list">{latestReferences.filter(item => scopeFilter === 'All' || item.scope === scopeFilter).map(reference =>
        <article key={reference.id} className="nw-reference"><div className="nw-reference-heading"><BookOpen size={20} />
          <h3>{reference.title}</h3><span className="nw-badge">{reference.isPublished ? `Published v${reference.version}` : 'Import draft'}</span></div>
          <div className="nw-meta"><span>{reference.scope}</span><span>{reference.sourceName}</span><span>{reference.passages.length} passages</span></div>
          {reference.passages.map((passage, index) => <div key={index} className="nw-passage"><span className="nw-badge">{passage.controlId ?? 'Unmapped'}</span>
            <span className="nw-muted">{passage.narrativeType ?? 'Unclassified'}</span><p>{passage.content}</p></div>)}
          {reference.scope === 'Capability' && <p className="nw-warning">Inherited responsibilities require confirmation.</p>}
          {!reference.isPublished && <button disabled={!canAuthor} onClick={() => openDraft(reference)}>Review import</button>}
          {reference.isPublished && <details><summary>Version provenance</summary><p>Published by {reference.publishedBy} on {reference.publishedAt ? new Date(reference.publishedAt).toLocaleString() : 'Unknown'}</p>
            <p className="nw-hash">SHA-256 {reference.sourceSha256}</p>
            {references.filter(item => item.referenceKey === reference.referenceKey && item.version < reference.version).map(item =>
              <div key={item.id}>Version {item.version} / {item.isPublished ? 'Published' : 'Draft'}<pre>{item.passages.map(passage => `${passage.controlId}: ${passage.content}`).join('\n\n')}</pre></div>)}</details>}
        </article>)}</div>
      {hasLegacy && <section className="nw-legacy"><h3>Legacy document sources</h3><p className="nw-warning">These browser-local sources have not been imported or published.</p>
        <pre>{[settings.sharePointSiteUrl, settings.sourceDocuments].filter(Boolean).join('\n')}</pre>
        <button onClick={() => go('import')}>Upload source documents</button>
        <label className="nw-checkbox"><input type="checkbox" checked={clearLegacy} onChange={event => setClearLegacy(event.target.checked)} />I have retained or migrated these source references</label>
        <button disabled={!clearLegacy} onClick={() => updateSettings({ sharePointSiteUrl: '', sourceDocuments: '' })}>Clear legacy settings</button></section>}
    </>}

    {view === 'import' && <>
      <header className="nw-header"><div><h2>Import &amp; map</h2><p>{draft ? draft.sourceName : access?.systemName}</p></div>
        <button onClick={() => go('library')}><ArrowLeft size={16} />Library</button></header>
      {!draft ? <section className="nw-import">
        <label>Reference title<input value={title} maxLength={200} onChange={event => setTitle(event.target.value)} /></label>
        <div className="nw-two-columns"><label>Reference scope<select value={scope} onChange={event => setScope(event.target.value)}>
          <option>System</option><option disabled={!access?.canPublishShared}>Organization</option><option disabled={!access?.canPublishShared}>Capability</option></select></label>
          {scope === 'Capability' ? <label>Capability<select value={capabilityId} onChange={event => setCapabilityId(event.target.value)}>
            <option value="">Select capability</option>{access?.capabilities.map(capability => <option key={capability.id} value={capability.id}>{capability.name}</option>)}</select></label>
            : <div className="nw-scope-target">{scope === 'System' ? access?.systemName : 'Organization'}<span className="nw-muted">Pre-written reference</span></div>}</div>
        <label>Upload file<input type="file" accept=".xlsx,.csv,.docx,.pdf,.txt,.md" onChange={event => { setFile(event.target.files?.[0] ?? null); setPaste(''); }} /></label>
        <label>Paste narratives<textarea rows={8} value={paste} disabled={file !== null} onChange={event => setPaste(event.target.value)} /></label>
        {file && <button onClick={() => setFile(null)}><Trash2 size={16} />Remove selected file</button>}
        <p className="nw-muted">XLSX, CSV, DOCX, digital PDF, TXT, Markdown / Maximum 5 MB</p>
        <button className="nw-primary" disabled={!canAuthor || !title.trim() || (!file && !paste.trim()) || (scope === 'Capability' && !capabilityId)} onClick={extract}>
          <FileText size={16} />Extract passages</button>
      </section> : <section>
        <div className="nw-section-heading"><h3>Review before publishing</h3><span className="nw-muted">{passages.length} extracted passages</span></div>
        {passages.map((passage, index) => <article className="nw-mapping" key={index}>
          <div className="nw-section-heading"><h3>Passage {index + 1}</h3><button title={`Remove passage ${index + 1}`} disabled={locked}
            onClick={() => { setPassages(current => current.filter((_, position) => position !== index)); setReviewed(false); }}><Trash2 size={16} /></button></div>
          <textarea aria-label={`Passage ${index + 1}`} value={passage.content} rows={4} maxLength={20000} disabled={locked}
            onChange={event => updatePassage(index, { content: event.target.value })} />
          <div className="nw-two-columns"><label>Map to control<input aria-label={`Control ${index + 1}`} value={passage.controlId ?? ''} placeholder="Unmapped" maxLength={20} disabled={locked}
            onChange={event => updatePassage(index, { controlId: event.target.value.toUpperCase() })} /></label>
            <label>Narrative type<select aria-label={`Narrative type ${index + 1}`} value={passage.narrativeType ?? ''} disabled={locked}
              onChange={event => updatePassage(index, { narrativeType: event.target.value })}><option value="">Unclassified</option><option>Policy</option><option>Technical</option></select></label></div>
          <p className="nw-warning">Imported reference claim. Execution has not been verified.</p>
        </article>)}
        <label className="nw-checkbox"><input type="checkbox" checked={reviewed} disabled={locked} onChange={event => setReviewed(event.target.checked)} />I reviewed these reference claims and mappings</label>
        <button className="nw-primary" disabled={!readyToPublish} onClick={publish}><Check size={16} />Publish references</button>
      </section>}
    </>}

    {view === 'review' && <>
      <header className="nw-header"><div><h2>Review change</h2><p>Active content remains unchanged until approval.</p></div>
        <button onClick={() => go('narratives')}><ArrowLeft size={16} />Narratives</button></header>
      {!proposal ? !loading && !loadError && <p className="nw-empty">No proposed narrative changes.</p> : <>
        <label className="nw-review-select">Proposed change<select value={proposal.id} onChange={event => navigate(`${base}/review?proposal=${encodeURIComponent(event.target.value)}`)}>
          {proposals.map(item => <option key={item.id} value={item.id}>{item.controlId} / {item.narrativeType} / v{item.baseVersion + 1} / {item.status}</option>)}</select></label>
        <div className="nw-section-heading"><h3>{proposal.controlId} / {proposal.narrativeType} / Proposed v{proposal.baseVersion + 1}</h3>
          <span className="nw-badge">{proposal.status === 'Draft' ? 'Draft / Not active' : proposal.status}</span></div>
        {proposal.isStale && <p className="nw-warning" role="status">Source state changed. Generate a new proposal before approval.</p>}
        <div className="nw-diff-grid">{['before', 'after'].map(side => <section key={side} className="nw-diff">
          <h3>{side === 'before' ? `Previous v${proposal.baseVersion}` : `Proposed v${proposal.baseVersion + 1}`}</h3>
          <p className="nw-muted">{side === 'before' ? 'Preserved version' : `Generated ${new Date(proposal.createdAt).toLocaleString()}`}</p>
          <div className="nw-diff-text">{diffWordsWithSpace(proposal.beforeContent, proposal.proposedContent).map((part, index) =>
            side === 'before' ? !part.added && (part.removed ? <del key={index}>{part.value}</del> : <span key={index}>{part.value}</span>)
              : !part.removed && (part.added ? <ins key={index}>{part.value}</ins> : <span key={index}>{part.value}</span>))}</div>
        </section>)}</div>
        <section className="nw-sources"><h3>Sources and review findings</h3>
          <p className="nw-hash">Source state {proposal.stateHash}</p>
          {proposal.conflicts.map((conflict, index) => <p className="nw-warning" key={`conflict-${index}`}>{conflict}</p>)}
          {proposal.missingEvidence.map((gap, index) => <p className="nw-warning" key={`gap-${index}`}>{gap}</p>)}
          <details><summary>Source snapshot</summary><pre>{JSON.stringify(proposal.provenance, null, 2)}</pre></details>
        </section>
        {proposal.status === 'Draft' ? <>
          <label>Review note<textarea rows={3} maxLength={2000} value={note} disabled={locked || !proposal.canReview} onChange={event => setNote(event.target.value)} /></label>
          <div className="nw-review-actions"><button disabled={locked || !proposal.canReview || !note.trim()} onClick={() => decide('RequestRevision')}>Return for revision</button>
            <button className="nw-primary" disabled={locked || !proposal.canReview || proposal.isStale} onClick={() => decide('Approve')}><Check size={16} />Approve v{proposal.baseVersion + 1}</button></div>
          <p className="nw-muted">Approval preserves version history. Implementation and authorization decisions are unchanged.</p>
        </> : <p className="nw-muted">{proposal.status} by {proposal.reviewedBy}. {proposal.reviewNote}</p>}
      </>}
    </>}

  </div>;
}