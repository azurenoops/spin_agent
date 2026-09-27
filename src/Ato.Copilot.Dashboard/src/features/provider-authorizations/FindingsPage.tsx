import { useEffect, useState } from 'react';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import { Link } from '../workspaces/workspaceNavigation';
import { errorClass, inputClass, Pager, Status, useRemote } from '../workspace-operations/workspaceUi';
import { CitationFields, compact, Field, Lines, MutationForm } from './forms';
import { ProviderBadge, ProviderPanel, ProviderSupport } from './ProviderPresentation';
import { EvidenceSharingControls } from './EvidenceSharingControls';
import * as api from './api';
import type { Finding, FindingInput, FindingReviewInput, Offering } from './types';
import { readAllPages } from './providerReadModels';
import { stateLabel } from '../package-imports/PackageReceipts';

export function FindingsPage({ offering, findingId, evidenceId, onChanged }: {
  offering: Offering; findingId?: string; evidenceId?: string; onChanged: () => void;
}) {
  return <div className="provider-grid"><div className="space-y-5">
    {findingId ? <FindingDetail key={findingId} offering={offering} findingId={findingId} evidenceId={evidenceId} />
      : <FindingList offering={offering} onChanged={onChanged} />}
  </div><ProviderSupport>
    <ProviderPanel title="Evidence handoff"><p>Provider evidence is access controlled. A retained file or reviewed finding does not grant customer access. Customers need an approved summary or a controlled reference.</p></ProviderPanel>
    <ProviderPanel title="Keep separate records"><p>Provider remediation and mission risk acceptance have different owners. Link them when a finding affects a customer.</p></ProviderPanel>
  </ProviderSupport></div>;
}

function FindingList({ offering, onChanged }: { offering: Offering; onChanged: () => void }) {
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const remote = useRemote(signal => api.listFindings(offering.offeringId, page, signal), [offering.offeringId, offering.revision, page]);
  return <>
    <OfferingEvidenceLibrary offering={offering} />
    <ProviderPanel title="Service findings" action={<button className="provider-secondary" onClick={() => setCreating(value => !value)} aria-expanded={creating}>{creating ? 'Hide finding form' : 'Add finding'}</button>}>
      <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
      {remote.data && <>
        {!remote.data.items.length ? <p>No provider findings recorded. This is not a claim that the service has no weaknesses.</p>
          : <div className="provider-table-wrap"><table className="provider-table" aria-label="Service findings">
            <thead><tr><th>Finding</th><th>Severity as stated</th><th>Workflow</th><th><span className="sr-only">Actions</span></th></tr></thead>
            <tbody>{remote.data.items.map(item => <tr key={item.findingId}><td>{item.title}<small>{item.controlIds.join(', ') || 'No controls recorded'}</small></td>
              <td>{item.severityAsStated || 'Not stated'}</td><td><ProviderBadge>{item.workflowState}</ProviderBadge></td>
              <td><Link to={api.authorizationHref(offering.offeringId, `findings/${encodeURIComponent(item.findingId)}`)}>Manage finding</Link></td></tr>)}</tbody>
          </table></div>}
        <Pager {...remote.data} onPage={setPage} />
      </>}
    </ProviderPanel>
    {creating && <ProviderPanel title="Record a provider finding"><FindingCreate offering={offering} onSaved={() => { setCreating(false); remote.retry(); onChanged(); }} /></ProviderPanel>}
  </>;
}

function OfferingEvidenceLibrary({ offering }: { offering: Offering }) {
  const [page, setPage] = useState(1);
  const remote = useRemote(async signal => {
    const findings = await readAllPages(next => api.listFindings(offering.offeringId, next, signal), signal);
    const records = await Promise.all(findings.map(finding =>
      readAllPages(next => api.listFindingEvidence(offering.offeringId, finding.findingId, next, signal), signal)));
    return records.flat();
  }, [offering.offeringId, offering.revision]);
  const pageSize = 10;
  const currentPage = Math.min(page, Math.max(1, Math.ceil((remote.data?.length ?? 0) / pageSize)));
  return <ProviderPanel title="Evidence library" action={<Link className="provider-secondary" to={api.authorizationHref(offering.offeringId, 'packages')}>Source documents</Link>}>
    <p className="mb-4 text-sm">Retained finding evidence is listed separately from remediation. Customer access requires an explicit approval; review the retained sharing controls for each artifact.</p>
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && <>{!remote.data.length ? <p>No finding evidence retained. Source-package documents remain in Authorizations &amp; sources.</p>
      : <div className="provider-table-wrap"><table className="provider-table" aria-label="Evidence library">
        <thead><tr><th>Evidence</th><th>Review state</th><th>Customer access</th><th><span className="sr-only">Actions</span></th></tr></thead>
        <tbody>{remote.data.slice((currentPage - 1) * pageSize, currentPage * pageSize).map(item => <tr key={item.evidenceId}>
          <td>{item.fileName}<small>{item.description || 'No description recorded'}</small></td>
          <td><ProviderBadge tone={item.latestReview ? 'neutral' : 'attention'}>{stateLabel(item.state)}</ProviderBadge>
            <small>{item.latestReview ? `Reviewed ${new Date(item.latestReview.reviewedAt).toLocaleDateString()}` : 'No retained review'}</small></td>
          <td>Controlled sharing<small>Inspect current approvals</small></td>
          <td><Link to={api.authorizationHref(offering.offeringId, `evidence/${encodeURIComponent(item.evidenceId)}?findingId=${encodeURIComponent(item.findingId)}`)}>Review evidence access</Link></td>
        </tr>)}</tbody>
      </table></div>}
      {remote.data.length > pageSize && <Pager page={currentPage} pageSize={pageSize} total={remote.data.length} onPage={setPage} />}
    </>}
  </ProviderPanel>;
}

function FindingCreate({ offering, onSaved }: { offering: Offering; onSaved: () => void }) {
  const [value, setValue] = useState<FindingInput>({ title: '', observation: '', severityAsStated: '', controlIds: [], citations: [] });
  const update = <K extends keyof FindingInput>(key: K, next: FindingInput[K]) => setValue(previous => ({ ...previous, [key]: next }));
  return <MutationForm label="Record finding" onSaved={onSaved} submitDisabled={!value.title.trim() || !value.observation.trim()}
    submit={key => api.createFinding(offering.offeringId, { ...value, controlIds: compact(value.controlIds), expectedOfferingRevision: offering.revision }, key)}>
    <Field label="Finding title" value={value.title} onChange={text => update('title', text)} required />
    <Field label="Observation" value={value.observation} onChange={text => update('observation', text)} multiline required maxLength={8000} />
    <Field label="Severity as stated" value={value.severityAsStated ?? ''} onChange={text => update('severityAsStated', text)} />
    <Lines label="Controls" values={value.controlIds} onChange={values => update('controlIds', values)} />
    <CitationFields value={value.citations} onChange={values => update('citations', values)} />
    <p>A new finding remains open. Source-stated closure does not close a provider finding.</p>
  </MutationForm>;
}

function FindingDetail({ offering, findingId, evidenceId }: { offering: Offering; findingId: string; evidenceId?: string }) {
  const remote = useRemote(signal => api.getFinding(offering.offeringId, findingId, signal), [offering.offeringId, findingId]);
  const [retainedFinding, setRetainedFinding] = useState<Finding | null>(null);
  const [reviewResult, setReviewResult] = useState('');
  useEffect(() => { if (remote.data) setRetainedFinding(remote.data); }, [remote.data]);
  const finding = remote.data ?? retainedFinding;
  return <>
    <Link className="provider-secondary" to={api.authorizationHref(offering.offeringId, 'findings')}>Back to evidence &amp; findings</Link>
    {reviewResult && <p role="status">{reviewResult}</p>}
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {!remote.loading && !remote.error && !remote.data && <p role="alert" className={errorClass}>This finding is unavailable in the selected offering. No evidence can be accessed from this link.</p>}
    {finding && <fieldset className="min-w-0 space-y-5" disabled={remote.loading || !!remote.error || !remote.data}
      hidden={!!remote.error || (!remote.loading && !remote.data)} aria-busy={remote.loading}>
      <FindingRecord finding={finding} evidenceId={evidenceId} refresh={remote.retry} onReviewed={setReviewResult} />
    </fieldset>}
  </>;
}

function FindingRecord({ finding, evidenceId, refresh, onReviewed }: { finding: Finding; evidenceId?: string; refresh: () => void; onReviewed: (result: string) => void }) {
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string[]>([]);
  const [disposition, setDisposition] = useState<FindingReviewInput['disposition']>('KeepOpen');
  const [rationale, setRationale] = useState('');
  const [uploadPending, setUploadPending] = useState(false);
  const [reviewPending, setReviewPending] = useState(false);
  const [planPending, setPlanPending] = useState(false);
  const pending = uploadPending || reviewPending || planPending;
  const evidence = useRemote(async signal => {
    if (!evidenceId) return api.listFindingEvidence(finding.offeringId, finding.findingId, page, signal);
    const item = await api.getFindingEvidence(finding.offeringId, finding.findingId, evidenceId, signal);
    return { items: item ? [item] : [], page: 1, pageSize: 25, total: item ? 1 : 0 };
  }, [finding.offeringId, finding.findingId, finding.revision, page, evidenceId]);
  const reload = () => { setSelected([]); evidence.retry(); refresh(); };
  return <>
    <ProviderPanel title={finding.title}>
      <div className="mb-4 flex flex-wrap gap-2"><ProviderBadge>{finding.workflowState}</ProviderBadge><ProviderBadge>Revision {finding.revision}</ProviderBadge></div>
      <p className="whitespace-pre-wrap">{finding.observation}</p>
      <p className="mt-3">Severity as stated: {finding.severityAsStated || 'Not stated'} · Controls: {finding.controlIds.join(', ') || 'Not recorded'}</p>
      <details className="mt-4"><summary>Source citations</summary>{finding.citations.length ? finding.citations.map((citation, index) =>
        <blockquote key={index} className="my-3 break-words border-l-2 pl-3">{citation.quote}<p>{citation.archivePath} · {citation.locator}</p></blockquote>) : <p>No source citations recorded.</p>}</details>
      <button className="provider-secondary mt-4" onClick={reload} disabled={pending}>Reload current finding</button>
    </ProviderPanel>
    <ProviderPanel title="Retained evidence">
      <p className="mb-4">Uploading evidence does not close a finding. Downloads are authenticated and provider-private; no customer sharing is implied.</p>
      <Status loading={evidence.loading} error={evidence.error} retry={evidence.retry} />
      {evidence.data && <>
        {evidenceId && !evidence.data.items.length && <p role="alert" className={errorClass}>The requested evidence is unavailable in this finding. No download or customer access is assumed.</p>}
        {!evidence.data.items.length && <p>No retained evidence on this page.</p>}
        {evidence.data.items.map(item => <article key={item.evidenceId} className="my-4 space-y-2 rounded border border-slate-200 p-4">
          <div className="flex flex-wrap justify-between gap-2"><h3 className="break-all font-semibold">{item.fileName}</h3><ProviderBadge>{item.state}</ProviderBadge></div>
          <p>{item.description}</p>
          <p className="text-xs">Retained {new Date(item.createdAt).toLocaleString()} · {item.byteLength} bytes · Finding revision {item.findingRevision}</p>
          <details><summary className="text-sm">File identity and review</summary>
            <p className="break-all text-xs">SHA-256: {item.sha256}</p>
            <p className="text-xs">{item.latestReview ? `${item.latestReview.disposition} · ${item.latestReview.reviewedBy} · ${item.latestReview.rationale}` : 'No explicit review recorded.'}</p>
          </details>
          <AuthenticatedDownload className="block text-sm font-semibold text-indigo-700 underline" url={api.findingEvidenceUrl(finding.offeringId, finding.findingId, item.evidenceId)} fileName={item.fileName}>Download protected evidence</AuthenticatedDownload>
          {evidenceId && <EvidenceSharingControls evidence={item} />}
          {!evidenceId && <Link className="block text-sm text-indigo-700 underline" to={`${api.authorizationHref(finding.offeringId, `evidence/${encodeURIComponent(item.evidenceId)}`)}?findingId=${encodeURIComponent(finding.findingId)}`}>View evidence record</Link>}
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" disabled={pending} checked={selected.includes(item.evidenceId)}
            onChange={event => setSelected(current => event.target.checked ? [...current, item.evidenceId] : current.filter(id => id !== item.evidenceId))} />Select {item.fileName} for review</label>
        </article>)}
        <fieldset disabled={pending}><Pager {...evidence.data} onPage={next => { setSelected([]); setPage(next); }} /></fieldset>
      </>}
    </ProviderPanel>
    <RemediationPlans finding={finding} disabled={uploadPending || reviewPending} onPendingChange={setPlanPending} />
    <ProviderPanel title="Submit remediation evidence"><EvidenceUpload finding={finding} disabled={reviewPending || planPending || evidence.loading || !!evidence.error} onPendingChange={setUploadPending} onSaved={reload} /></ProviderPanel>
    <ProviderPanel title="Explicit evidence review">
      <MutationForm label="Record evidence review" disabled={uploadPending || planPending || evidence.loading || !!evidence.error} onPendingChange={setReviewPending}
        submitDisabled={!rationale.trim() || (disposition === 'AcceptClosure' && !selected.length)}
        submit={async () => {
          const result = await api.reviewFinding(finding.offeringId, finding.findingId, {
            expectedRevision: finding.revision, evidenceIds: selected, disposition, rationale: rationale.trim(),
          });
          onReviewed(`Review recorded: ${result.workflowState}`);
        }} onSaved={reload}>
        <label className="grid gap-1 text-sm">Review disposition<select className={inputClass} value={disposition} onChange={event => setDisposition(event.target.value as FindingReviewInput['disposition'])}>
          <option value="KeepOpen">Keep open</option><option value="AcceptClosure">Accept closure using selected evidence</option>
        </select></label>
        <Field label="Review rationale" value={rationale} onChange={setRationale} required multiline maxLength={8000} />
        <p className="text-sm">{selected.length} evidence items selected. Closure requires an explicit reviewer decision, not an upload.</p>
      </MutationForm>
    </ProviderPanel>
  </>;
}

function RemediationPlans({ finding, disabled, onPendingChange }: { finding: Finding; disabled: boolean; onPendingChange: (pending: boolean) => void }) {
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState(false);
  const [pending, setPending] = useState(false);
  const [title, setTitle] = useState('');
  const [action, setAction] = useState('');
  const [owner, setOwner] = useState('');
  const [saved, setSaved] = useState(false);
  const plans = useRemote(signal => api.listPoamItems(finding.offeringId, page, signal), [finding.offeringId, finding.revision, page]);
  const offering = useRemote(signal => api.getOffering(finding.offeringId, signal), [finding.offeringId, finding.revision]);
  return <ProviderPanel title="Provider remediation" action={<button className="provider-secondary" disabled={pending || disabled} onClick={() => { setEditing(value => !value); setSaved(false); }} aria-expanded={editing}>
    {editing ? 'Hide remediation form' : 'Add remediation plan'}</button>}>
    <Status loading={plans.loading || offering.loading} error={plans.error ?? offering.error} retry={() => { plans.retry(); offering.retry(); }} />
    {plans.data && <><p className="mb-3 text-sm">Showing same-finding remediation from the provider’s paginated plan register. Plans do not close findings.</p>
      {plans.data.items.filter(item => item.findingIds.includes(finding.findingId)).map(item => <article key={item.poamId} className="space-y-2 border-t py-3">
        <h3 className="font-semibold">{item.title}</h3><ProviderBadge>{item.workflowState}</ProviderBadge>
        <p>{item.correctiveAction}</p><p className="text-xs">Owner as stated: {item.ownerAsStated || 'Not recorded'} · Revision {item.revision}</p>
        {item.milestones.map((milestone, index) => <p key={index} className="text-sm">{milestone.description} · {milestone.dueDate || 'No date stated'}</p>)}
      </article>)}
      {!plans.data.items.some(item => item.findingIds.includes(finding.findingId)) && <p>No remediation linked to this finding on this page.</p>}
      <fieldset disabled={pending || disabled}><Pager {...plans.data} onPage={setPage} /></fieldset>
    </>}
    {editing && <div className="mt-4"><MutationForm label="Save remediation plan" disabled={disabled || offering.loading || !!offering.error || !offering.data}
      submitDisabled={!title.trim() || !action.trim()} onPendingChange={value => { setPending(value); onPendingChange(value); }}
      submit={key => api.createPoamItem(finding.offeringId, { expectedOfferingRevision: offering.data!.revision,
        title: title.trim(), findingIds: [finding.findingId], correctiveAction: action.trim(), ownerAsStated: owner.trim(), milestones: [], citations: [] }, key)}
      onSaved={() => { setSaved(true); setEditing(false); plans.retry(); offering.retry(); onPendingChange(false); setPending(false); }}>
      <Field label="Plan title" value={title} onChange={setTitle} required />
      <Field label="Corrective action" value={action} onChange={setAction} required multiline maxLength={8000} />
      <Field label="Owner as stated" value={owner} onChange={setOwner} />
    </MutationForm></div>}
    {saved && <p role="status" className="mt-3">Remediation plan saved.</p>}
  </ProviderPanel>;
}

function EvidenceUpload({ finding, disabled, onSaved, onPendingChange }: { finding: Finding; disabled: boolean; onSaved: () => void; onPendingChange: (pending: boolean) => void }) {
  const [file, setFile] = useState<File | null>(null);
  const [description, setDescription] = useState('');
  const invalidFile = !!file && (file.size < 1 || file.size > 10 * 1024 * 1024);
  return <MutationForm label="Submit evidence for review" disabled={disabled} onPendingChange={onPendingChange}
    submitDisabled={!file || invalidFile || !description.trim()} onSaved={onSaved}
    submit={key => api.submitFindingEvidence(finding.offeringId, finding.findingId, finding.revision, description.trim(), file!, key)}>
    <label className="grid gap-2 text-sm">Evidence file (1 byte–10 MiB)<input type="file" onChange={event => setFile(event.target.files?.[0] ?? null)} /></label>
    {invalidFile && <p role="alert" className={errorClass}>Choose a file between 1 byte and 10 MiB.</p>}
    <Field label="Evidence description" value={description} onChange={setDescription} required multiline maxLength={8000} />
  </MutationForm>;
}
