import { useEffect, useRef, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { editPolicyReference, getPolicyReference, policyError, unlinkPolicyReference, type PolicyReferenceDetail } from '../../api/policyWorkspace';
import { PolicyDrawer, PolicySourceCard } from './PolicyDrawer';

export default function PolicyReferenceDrawer({ systemId, id, onClose, onChanged }: {
  systemId: string; id: string; onClose: () => void; onChanged: () => void;
}) {
  const [detail, setDetail] = useState<PolicyReferenceDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [revision, setRevision] = useState(0);
  const [editing, setEditing] = useState(false);
  const [removing, setRemoving] = useState(false);
  const [rationale, setRationale] = useState('');
  const [busy, setBusy] = useState(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useEffect(() => {
    const controller = new AbortController();
    setDetail(null); setError(null); setLoading(true); setEditing(false); setRemoving(false);
    getPolicyReference(systemId, id, controller.signal)
      .then(value => { if (!controller.signal.aborted) { setDetail(value); setRationale(value.reference.rationale ?? ''); } })
      .catch(reason => { if (!controller.signal.aborted) setError(policyError(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, id, revision]);
  const mutate = async (remove: boolean) => {
    if (!detail || busy) return;
    if (remove ? !detail.reference.canRemove : !detail.reference.canEdit) {
      setError(detail.reference.actionReason ?? 'This system action is not authorized.'); return;
    }
    if (!remove && (!rationale.trim() || rationale.trim().length > 500)) { setError('Enter a rationale of 1 to 500 characters.'); return; }
    setBusy(true); setError(null);
    try {
      if (remove) await unlinkPolicyReference(systemId, id, detail.reference.revision);
      else await editPolicyReference(systemId, id, { expectedRevision: detail.reference.revision, rationale: rationale.trim() });
      if (!mounted.current) return;
      onChanged();
      if (remove) onClose();
      else setRevision(value => value + 1);
    } catch (reason) { if (mounted.current) setError(policyError(reason)); }
    finally { if (mounted.current) setBusy(false); }
  };
  return <PolicyDrawer title={detail?.reference.name ?? 'Policy reference'} label="Policy reference details"
    systemName={detail?.systemName ?? 'Loading system…'} busy={busy} onClose={onClose}
    footer={<><small>Source status and applicability review are separate.</small><button type="button" disabled={busy} onClick={onClose}>Close</button></>}>
    {loading && <p role="status">Loading retained policy reference…</p>}
    {error && <div className="pw-error" role="alert"><p>{error}</p>
      <button type="button" disabled={busy} onClick={() => setRevision(value => value + 1)}>Refresh reference</button></div>}
    {!loading && detail && <>
      {detail.reference.sourceChanged && <p className="pw-notice">The library source has changed. Your retained reference is unchanged.</p>}
      {detail.reference.retention === 'Indirect' && <p className="pw-notice">This policy is present through a linked capability, not a direct system reference.
        Removing a direct reference will not remove capability relationships.</p>}
      <section className="pw-detail-section"><h3>Why this applies</h3>
        {editing ? <form onSubmit={event => { event.preventDefault(); void mutate(false); }}>
          <label className="pw-rationale">Why does this apply to this system?
            <textarea autoFocus maxLength={500} rows={5} value={rationale} onChange={event => setRationale(event.target.value)} disabled={busy} />
          </label><div className="pw-rationale-meta"><span>The retained source will not change.</span><span>{rationale.length}/500</span></div>
          <div className="pw-actions"><button type="submit" className="pw-primary" disabled={busy || !rationale.trim()}>Save rationale</button>
            <button type="button" disabled={busy} onClick={() => { setEditing(false); setRationale(detail.reference.rationale ?? ''); }}>Cancel edit</button></div>
        </form> : <><p className="pw-prose">{detail.reference.rationale ?? 'System-specific rationale not recorded.'}</p>
          {detail.reference.canEdit && <button type="button" onClick={() => setEditing(true)}>Edit rationale</button>}</>}
      </section>
      {detail.retainedSource ? <PolicySourceCard source={detail.retainedSource} heading="Retained source" />
        : <section className="pw-detail-section"><h3>Source retention</h3><p>No retained source snapshot exists for this legacy or indirect reference.
          Current library content is not a historical version.</p></section>}
      <section className="pw-detail-section"><h3>Applicability review</h3><p>{detail.reviewMessage}</p>
        <p>An Active source is not approval that this policy applies to the system.</p></section>
      <details className="pw-disclosure"><summary>Current library source</summary>
        {detail.currentSource ? <PolicySourceCard source={detail.currentSource} heading="Current source record" />
          : <p>The current source is unavailable. Any retained snapshot above remains unchanged.</p>}
      </details>
      <details className="pw-disclosure"><summary>Related controls and supporting references</summary>
        {detail.relatedControls.length ? <ul>{detail.relatedControls.map(control =>
          <li key={control}><Link to={`/systems/${encodeURIComponent(systemId)}/narratives?control=${encodeURIComponent(control)}`}>{control}</Link></li>)}</ul>
          : <p>No related capability controls recorded.</p>}
        <p>Control relationships do not prove implementation or assessment acceptance.</p>
      </details>
      <details className="pw-disclosure"><summary>Reference history</summary>
        {!detail.history.length && <p>No recorded reference history is available.</p>}
        {detail.history.map(event => <article className="pw-history" key={event.id}><strong>{event.action}</strong>
          <small>{new Date(event.at).toLocaleString()}{event.actor ? ` · ${event.actor}` : ''}</small>
          <p>{event.description}</p></article>)}
      </details>
      {detail.reference.actionReason && <p className="pw-hint">{detail.reference.actionReason}</p>}
      {detail.reference.canRemove && !removing && <button type="button" className="pw-unlink" disabled={busy} onClick={() => setRemoving(true)}>Unlink from system</button>}
      {removing && <section className="pw-removal" aria-label="Unlink impact"><h3>Unlink only from this system</h3>
        <ul>{detail.removalImpact.map((impact, index) => <li key={index}>{impact}</li>)}</ul>
        <p>This does not delete the organization-library policy, remove other systems' assignments, or change an assessment decision.</p>
        <div className="pw-actions"><button type="button" disabled={busy} onClick={() => setRemoving(false)}>Keep reference</button>
          <button type="button" className="pw-primary" disabled={busy} onClick={() => void mutate(true)}>Confirm unlink</button></div>
      </section>}
    </>}
  </PolicyDrawer>;
}
