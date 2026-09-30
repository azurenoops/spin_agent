import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { CheckCircle2, ExternalLink, FileText, Info, X } from 'lucide-react';
import { evidenceError, getEvidenceCatalogDetail, linkCatalogEvidence, type EvidenceCatalogDetail } from '../../api/evidenceCatalog';
import { collectEvidence, deleteEvidence, replaceEvidence } from '../../api/evidence';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import { useWorkspaceHref } from '../workspaces/workspaceNavigation';

interface Props {
  systemId: string;
  id: string;
  tab: string;
  onTab: (tab: string) => void;
  onClose: () => void;
  onChanged: () => void;
}
const tabs = [{ id: 'overview', name: 'Overview' }, { id: 'controls', name: 'Linked controls' }, { id: 'history', name: 'History' }];
const date = (value: string | null) => value ? new Date(value).toLocaleString() : 'Not recorded';

export default function EvidenceCatalogDrawer({ systemId, id, tab, onTab, onClose, onChanged }: Props) {
  const [detail, setDetail] = useState<EvidenceCatalogDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false);
  const [replace, setReplace] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [control, setControl] = useState('');
  const [success, setSuccess] = useState('');
  const closeRef = useRef<HTMLButtonElement>(null);
  const drawerRef = useRef<HTMLElement>(null);
  const closeCallback = useRef(onClose);
  closeCallback.current = onClose;
  const mounted = useRef(true);
  const href = useWorkspaceHref();
  const activeTab = tabs.some(t => t.id === tab) ? tab : 'overview';

  useEffect(() => {
    mounted.current = true;
    closeRef.current?.focus();
    const keydown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.preventDefault(); closeCallback.current(); }
      if (event.key !== 'Tab' || !drawerRef.current) return;
      const elements = Array.from(drawerRef.current.querySelectorAll<HTMLElement>(
        'button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), summary, [tabindex="0"]',
      )).filter(element => !element.closest('details:not([open])') || element.tagName === 'SUMMARY');
      const first = elements[0], last = elements[elements.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    };
    document.addEventListener('keydown', keydown);
    return () => { mounted.current = false; document.removeEventListener('keydown', keydown); };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError(null); setDetail(null);
    getEvidenceCatalogDetail(systemId, id, controller.signal)
      .then(value => { if (!controller.signal.aborted) setDetail(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(evidenceError(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, id, revision]);

  const refresh = () => { setDetail(null); setRevision(value => value + 1); };
  const act = async (action: 'replace' | 'delete' | 'collect' | 'link') => {
    if (!detail || busy) return;
    const permitted = action === 'replace' ? detail.permissions.canReplace : action === 'delete' ? detail.permissions.canDelete
      : action === 'collect' ? detail.permissions.canCollect : detail.permissions.canLink;
    if (!permitted) { setError('Your current access does not permit this evidence action.'); return; }
    if ((action === 'replace' || action === 'delete' || action === 'link') && !detail.contentHash) {
      setError('A retained evidence version is required. Refresh before changing this record.'); return;
    }
    if (action === 'delete' && !window.confirm(`Delete "${detail.item.name}"? The record will no longer appear in this catalog.`)) return;
    setBusy(true); setError(null); setSuccess('');
    try {
      if (action === 'replace') {
        if (!file) { setError('Select a replacement file.'); return; }
        await replaceEvidence({ systemId, evidenceId: detail.item.recordId, file, expectedHash: detail.contentHash ?? undefined });
      } else if (action === 'delete') {
        await deleteEvidence(systemId, detail.item.recordId, detail.contentHash ?? undefined);
      } else if (action === 'collect') {
        const controlId = detail.item.controls[0]?.controlId;
        if (!controlId) { setError('No scoped control is available for collection.'); return; }
        await collectEvidence(systemId, controlId);
      } else {
        if (!control.trim()) { setError('Enter a control ID from this system.'); return; }
        await linkCatalogEvidence(systemId, id, control.trim().toUpperCase(), detail.contentHash!);
      }
      if (!mounted.current) return;
      onChanged();
      if (action === 'delete') { onClose(); return; }
      setReplace(false); setFile(null); setControl('');
      setSuccess(action === 'collect' ? 'Evidence collected. The catalog has been refreshed.' : 'Evidence updated.');
      refresh();
    } catch (reason) {
      if (mounted.current) setError(evidenceError(reason));
    } finally {
      if (mounted.current) setBusy(false);
    }
  };
  const accessFailed = (reason: unknown) => {
    setDetail(null); setError(evidenceError(reason));
  };

  return <div className="ew-drawer-layer">
    <button type="button" className="ew-scrim" aria-label="Close evidence backdrop" tabIndex={-1} onClick={onClose} />
    <aside className="ew-drawer" ref={drawerRef} role="dialog" aria-modal="true" aria-label="Evidence details">
      <header className="ew-drawer-header"><div><span>Evidence details · On selection</span>
        <h2><FileText size={27} />{detail?.item.name ?? (loading ? 'Loading record…' : 'Evidence access unavailable')}</h2>
        {detail && <span className="ew-tag">{detail.item.source === 'Provider' ? 'Provider shared' : 'System evidence'}</span>}
      </div><button type="button" ref={closeRef} aria-label="Close evidence details" onClick={onClose}><X size={19} /></button></header>
      <div className="ew-tabs" role="tablist" aria-label="Evidence detail sections">
        {tabs.map(t => <button key={t.id} type="button" role="tab" aria-selected={activeTab === t.id} onClick={() => onTab(t.id)}>{t.name}</button>)}
      </div>
      <div className="ew-drawer-body">
        {loading && <p role="status">Loading evidence details…</p>}
        {error && <div className="ew-error" role="alert"><p>{error}</p>
          <button type="button" disabled={busy} onClick={refresh}>Refresh record access</button></div>}
        {success && <p role="status">{success}</p>}
        {!loading && detail && activeTab === 'overview' && <>
          <section className="ew-detail-card"><h3>Access</h3><div className="ew-access">
            <span>{detail.availability === 'FileAvailable' && <CheckCircle2 size={18} />}
              {detail.availability === 'FileAvailable' ? 'File available' : detail.availability === 'SummaryOnly' ? 'Summary only'
                : detail.availability === 'Unavailable' ? 'Access unavailable' : 'File availability unknown'}</span>
            {detail.permissions.canDownload && detail.permissions.downloadUrl && <AuthenticatedDownload
              className="ew-primary" url={detail.permissions.downloadUrl} fileName={detail.item.name} onDownloadError={accessFailed}>
              <ExternalLink size={14} />{detail.availability === 'SummaryOnly' ? 'Download approved summary' : 'Open file'}
            </AuthenticatedDownload>}
          </div>{detail.availabilityReason && <p>{detail.availabilityReason}</p>}
            {detail.item.source === 'Provider' && <p>Private attachments are not included in this sharing approval.</p>}
          </section>
          {detail.summary && <section className="ew-detail-card"><h3>Approved shared summary</h3><p className="ew-prose">{detail.summary}</p></section>}
          <section className="ew-detail-card"><h3>Metadata</h3><dl>
            <dt>Owner</dt><dd>{detail.owner ?? 'Owner not recorded'}</dd>
            <dt>Source</dt><dd>{detail.item.sourceLabel}</dd>
            <dt>{detail.item.source === 'Automated' ? 'Collected' : detail.item.source === 'Provider' ? 'Shared' : 'Uploaded'}</dt><dd>{date(detail.item.recordedAt)}</dd>
            <dt>{detail.item.source === 'Provider' ? 'Sharing approver' : detail.item.source === 'Automated' ? 'Collector' : 'Uploader'}</dt><dd>{detail.recordedBy ?? 'Not recorded'}</dd>
            <dt>Version</dt><dd>{detail.version ?? 'Not recorded'}</dd>
            {detail.fileSizeBytes !== null && <><dt>Size</dt><dd>{detail.fileSizeBytes.toLocaleString()} bytes</dd></>}
            {detail.contentType && <><dt>File type</dt><dd>{detail.contentType}</dd></>}
          </dl>{detail.description && <p className="ew-prose">{detail.description}</p>}</section>
          <section className="ew-detail-card"><h3>Review</h3><p>{detail.review ?? 'Review status not recorded'}</p>
            <p className="ew-muted">{detail.relevance ?? 'Relevance not reviewed'}</p>
            <p className="ew-muted">{detail.currency ?? 'Currency not evaluated'}</p>
            <p>Check that this record supports the linked control and review period.</p></section>
          <section className="ew-detail-card"><h3>Linked controls</h3><ControlLinks detail={detail} systemId={systemId} href={href} /></section>
          <details className="ew-detail-card"><summary>Source &amp; version details</summary>
            <dl>{detail.provenance.map((p, i) => <div key={`${p.label}:${i}`} className="ew-provenance"><dt>{p.label}</dt><dd>{p.value}</dd></div>)}
              {detail.contentHash && <><dt>Retained content hash</dt><dd className="ew-hash">{detail.contentHash}</dd></>}</dl>
          </details>
          <div className="ew-info"><Info size={17} /><p>Available evidence does not mean a control has passed assessment.
            Sharing approval applies only to its authorized organization and system.</p></div>
          {(detail.permissions.canReplace || detail.permissions.canDelete || detail.permissions.canCollect) && <details className="ew-detail-card">
            <summary>Manage evidence</summary><div className="ew-management">
              {detail.permissions.canReplace && <button type="button" disabled={busy} onClick={() => setReplace(value => !value)}>Replace file</button>}
              {detail.permissions.canDelete && <button type="button" disabled={busy} onClick={() => void act('delete')}>Delete evidence</button>}
              {detail.permissions.canCollect && <button type="button" disabled={busy} onClick={() => void act('collect')}>Collect automated evidence</button>}
            </div>
            {replace && <form onSubmit={e => { e.preventDefault(); void act('replace'); }}>
              <label>Replacement file<input aria-label="Replacement file" type="file" onChange={e => setFile(e.target.files?.[0] ?? null)} /></label>
              <button type="submit" className="ew-primary" disabled={!file || busy}>Upload replacement</button>
            </form>}
          </details>}
        </>}
        {!loading && detail && activeTab === 'controls' && <section className="ew-detail-card"><h3>Linked controls</h3>
          <ControlLinks detail={detail} systemId={systemId} href={href} />
          <p>A link documents supporting material. It does not change the control's assessment result.</p>
          {detail.permissions.canLink ? <form onSubmit={e => { e.preventDefault(); void act('link'); }}>
            <label>Control ID<input aria-label="Control ID" placeholder="For example, AC-2" value={control} onChange={e => setControl(e.target.value)} /></label>
            <button type="submit" className="ew-primary" disabled={busy || !control.trim()}>Link control</button>
          </form> : <p>{detail.permissions.linkReason ?? 'Linking is not available for this evidence source.'}</p>}
        </section>}
        {!loading && detail && activeTab === 'history' && <section className="ew-detail-card"><h3>Retained history</h3>
          {!detail.history.length && <p>No additional history is available for this record.</p>}
          {detail.history.map(event => <article key={event.id} className="ew-history">
            <h4>{event.label}</h4><p>{date(event.at)}{event.actor ? ` · ${event.actor}` : ''}</p>
            {event.downloadUrl && <AuthenticatedDownload url={event.downloadUrl} fileName={event.fileName ?? detail.item.name}
              onDownloadError={accessFailed}>Download retained version</AuthenticatedDownload>}
          </article>)}
        </section>}
      </div>
      <footer className="ew-drawer-footer"><small>{detail?.permissions.manageReason ?? (detail ? 'Actions follow your current system access.' : 'Access is checked for this record.')}</small>
        <button type="button" onClick={onClose}>Close</button></footer>
    </aside>
  </div>;
}

function ControlLinks({ detail, systemId, href }: { detail: EvidenceCatalogDetail; systemId: string; href: (path: string) => string }) {
  if (!detail.item.controls.length) return <p>{detail.item.linksKnown ? 'No controls linked to this record.' : 'Control links were not included in the shared record.'}</p>;
  return <ul className="ew-linked-controls">{detail.item.controls.map(control => <li key={`${control.kind}:${control.controlId}`}>
    <Link to={href(`/systems/${encodeURIComponent(systemId)}/narratives?control=${encodeURIComponent(control.controlId)}`)}>
      <span className="ew-tag">{control.controlId}</span> {control.title ?? control.controlId}
    </Link><small>{control.kind}</small>
  </li>)}</ul>;
}
