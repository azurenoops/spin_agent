import { Link } from '../workspaces/workspaceNavigation';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import type { RemediationEvidence, RemediationExceptionReference, RemediationHistory, RemediationSource } from '../../api/remediationWorkspace';
import { systemPanel, systemSecondaryAction } from '../systems/SystemTaskPresentation';

export const remediationDate = (value: string | null) => value
  ? new Date(/^\d{4}-\d{2}-\d{2}$/.test(value) ? `${value}T12:00:00` : value)
    .toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) : 'Not recorded';
export const remediationStatus = (value: string) => value === 'InReview' ? 'Ready to verify'
  : value === 'Done' ? 'Task completed' : value.replace(/([a-z])([A-Z])/g, '$1 $2');
export function FindingSource({ systemId, source }: { systemId: string; source: RemediationSource | null }) {
  if (!source) return <p className="rw-muted">No assessment relationship is recorded. The source has not been inferred from a control or subscription.</p>;
  const params = new URLSearchParams();
  if (source.resultId) params.set('result', source.resultId);
  if (source.planId) params.set('plan', source.planId);
  return <section className={systemPanel}><h3>{source.name}</h3>
    <p>{source.planRevision === null ? 'No retained plan version recorded' : `Plan revision ${source.planRevision}`}</p>
    {source.resultId && <Link className="rw-card-link" to={`/systems/${systemId}/assessments?${params}`}>View source assessment</Link>}
    <p className="rw-muted">This is the original source association, not the current plan or a new assessment decision.</p>
  </section>;
}
export function RemediationEvidenceSection({ items, onError }: { items: RemediationEvidence[]; onError?: (error: unknown) => void }) {
  return <section className={systemPanel}><h3>Retained evidence & provenance</h3>
    {!items.length ? <p>No retained evidence is linked to this record.</p> : <ul className="rw-history">
      {items.map(item => <li key={item.id}><strong>{item.name}</strong>
        <p>{item.linkedAt ? `Linked: ${remediationDate(item.linkedAt)}` : `Collected: ${remediationDate(item.collectedAt)}`}</p>
        {item.downloadUrl ? <AuthenticatedDownload url={item.downloadUrl} className={systemSecondaryAction} onDownloadError={onError}>
          Download evidence</AuthenticatedDownload> : <p className="rw-muted">A downloadable source is not available here.</p>}
        <details><summary>Integrity & identity</summary><p>Hash: {item.hash ?? 'Not retained'}</p><p>Record: {item.id}</p></details>
      </li>)}</ul>}
  </section>;
}
export function RemediationHistorySection({ items }: { items: RemediationHistory[] }) {
  return <section className={systemPanel}><h3>Audit history</h3>
    {!items.length ? <p>No additional history is recorded.</p> : <ol className="rw-history">{items.map((item, index) =>
      <li key={`${item.at}-${index}`}><strong>{item.action}</strong><p>{remediationDate(item.at)} · {item.actor ?? 'Actor not recorded'}</p>
        <p>{item.description}</p></li>)}</ol>}
    <p className="rw-muted">Corrective work does not rewrite historical assessment observations.</p>
  </section>;
}
export function RemediationExceptionsSection({ systemId, items }: { systemId: string; items: RemediationExceptionReference[] }) {
  return <section className={systemPanel}><h3>Related exceptions</h3>
    {!items.length ? <p>No exception linked.</p> : items.map(item => <article key={item.id}>
      <h4>{item.type} · {item.controlId}</h4><span className="rw-status">{item.status}</span>
      {item.expiresAt && new Date(item.expiresAt).getTime() <= Date.now() && <p>Validity date has passed.</p>}
      <p>{item.justification}</p><dl><dt>Decision authority</dt><dd>{item.decisionAuthority ?? 'No decision recorded'}</dd>
        <dt>Compensating controls</dt><dd>{item.conditions ?? 'Not recorded'}</dd><dt>Validity ends</dt><dd>{remediationDate(item.expiresAt)}</dd>
        <dt>Reviewed</dt><dd>{remediationDate(item.reviewedAt)}</dd></dl>
      {item.reviewerId && <details><summary>Reviewer identity</summary><p>{item.reviewerId}</p></details>}
    </article>)}
    <Link className="rw-card-link" to={`/systems/${systemId}/deviations`}>Open exception requests</Link>
    <p className="rw-muted">Requests have a separate authorized decision lifecycle. Linking does not accept risk, extend dates or close work.</p>
  </section>;
}
