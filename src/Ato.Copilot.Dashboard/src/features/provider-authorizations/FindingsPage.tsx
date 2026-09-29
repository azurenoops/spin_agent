import { useEffect, useState } from 'react';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import { Link, useLocation, useNavigate } from '../workspaces/workspaceNavigation';
import { errorClass, inputClass, Pager, secondaryButtonClass, Status, useRemote } from '../workspace-operations/workspaceUi';
import SetupDialog from '../workspace-operations/SetupDialog';
import { CitationFields, compact, Field, Lines, MutationForm } from './forms';
import { ProviderBadge, ProviderPanel } from './ProviderPresentation';
import { EvidenceSharingControls } from './EvidenceSharingControls';
import * as api from './api';
import type { Finding, FindingInput, FindingReviewInput, Offering } from './types';
import { readAllPages } from './providerReadModels';
import { stateLabel } from '../package-imports/PackageReceipts';

export function FindingsPage({ offering, findingId, evidenceId, onChanged }: {
  offering: Offering; findingId?: string; evidenceId?: string; onChanged: () => void;
}) {
  return <div className="provider-grid"><div className="space-y-[22px]">
    {findingId ? <FindingDetail key={findingId} offering={offering} findingId={findingId} evidenceId={evidenceId} />
      : <FindingList offering={offering} onChanged={onChanged} />}
  </div><aside className="provider-support">
    <ProviderPanel title="Evidence handoff"><p>Customers need permitted, reviewable evidence—not a broken link to a private attachment.</p>
      <p className="mt-3">Share an approved summary or a controlled reference with a clear access process. A retained file or reviewed finding does not grant customer access.</p>
      <p className="mt-3">Source-package documents remain separate provenance. <Link to={api.authorizationHref(offering.offeringId, 'packages')} className="font-semibold text-indigo-700">Source documents</Link></p>
    </ProviderPanel>
    <ProviderPanel title="Contributes to the system package">
      <ol className="list-decimal pl-[18px] leading-[1.8]"><li>System evidence index</li><li>Assessment scope and results references</li><li>Relevant risk and remediation records</li></ol>
    </ProviderPanel>
    <ProviderPanel title="Keep separate records"><p>Provider remediation and mission risk acceptance have different owners. Link them when a finding affects a customer.</p></ProviderPanel>
  </aside></div>;
}

function FindingList({ offering, onChanged }: { offering: Offering; onChanged: () => void }) {
  const location = useLocation();
  const navigate = useNavigate();
  const uploadRequested = new URLSearchParams(location.search).get('action') === 'upload';
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [pending, setPending] = useState(false);
  const [evidenceRevision, setEvidenceRevision] = useState(0);
  const [uploadResult, setUploadResult] = useState('');
  const remote = useRemote(signal => api.listFindings(offering.offeringId, page, signal), [offering.offeringId, offering.revision, page]);
  const closeUpload = () => {
    const query = new URLSearchParams(location.search);
    query.delete('action');
    navigate({ pathname: location.pathname, search: query.toString(), hash: location.hash }, { replace: true });
  };
  return <>
    {uploadResult && <p role="status">{uploadResult}</p>}
    <OfferingEvidenceLibrary key={evidenceRevision} offering={offering} />
    <ProviderPanel title="Service findings" action={<button type="button" className="provider-secondary" disabled={pending} onClick={() => setCreating(true)}>Add finding</button>}>
      <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
      {remote.data && <>
        {!remote.data.items.length ? <p>No provider findings recorded. This is not a claim that the service has no weaknesses.</p>
          : <div className="provider-table-wrap relative"><table className="provider-table" aria-label="Service findings">
            <thead><tr><th>Finding</th><th>Severity as stated</th><th>Workflow</th><th><span className="sr-only">Actions</span></th></tr></thead>
            <tbody>{remote.data.items.map(item => <tr key={item.findingId}><td><strong>{item.title}</strong><small>{item.controlIds.join(', ') || 'No controls recorded'}</small></td>
              <td>{item.severityAsStated || 'Not stated'}</td><td><ProviderBadge>{item.workflowState}</ProviderBadge></td>
              <td><Link className="provider-secondary" to={api.authorizationHref(offering.offeringId, `findings/${encodeURIComponent(item.findingId)}`)}>Manage finding</Link></td></tr>)}</tbody>
          </table></div>}
        {remote.data.total > remote.data.pageSize && <Pager {...remote.data} onPage={setPage} />}
      </>}
    </ProviderPanel>
    {creating && <SetupDialog title="Record a provider finding" description={`Offering: ${offering.name}`} busy={pending} onClose={() => setCreating(false)}>
      <FindingCreate offering={offering} onPendingChange={setPending} onSaved={() => { setCreating(false); setPending(false); remote.retry(); onChanged(); }} />
      <button type="button" className={`${secondaryButtonClass} mt-4`} disabled={pending} onClick={() => setCreating(false)}>Cancel</button>
    </SetupDialog>}
    {uploadRequested && <OfferingEvidenceUpload offering={offering} onClose={closeUpload} onSaved={() => {
      closeUpload(); remote.retry(); setEvidenceRevision(value => value + 1); onChanged();
      setUploadResult('Evidence submitted for review. The finding remains subject to explicit review; customer access is unchanged.');
    }} />}
  </>;
}

function OfferingEvidenceUpload({ offering, onClose, onSaved }: { offering: Offering; onClose: () => void; onSaved: () => void }) {
  const [selectedId, setSelectedId] = useState('');
  const [pending, setPending] = useState(false);
  const findings = useRemote(signal => readAllPages(next => api.listFindings(offering.offeringId, next, signal), signal),
    [offering.offeringId, offering.revision]);
  const finding = useRemote(async signal => {
    if (!selectedId) return null;
    const record = await api.getFinding(offering.offeringId, selectedId, signal);
    if (!record || record.offeringId !== offering.offeringId || record.findingId !== selectedId)
      throw new Error('The selected finding could not be verified in this offering. Reload before uploading evidence.');
    return record;
  }, [offering.offeringId, selectedId]);
  return <SetupDialog title="Submit remediation evidence" description={`Offering: ${offering.name}`} busy={pending} onClose={onClose}>
    <p className="mb-4 text-sm">Select the finding this evidence supports. Uploading does not close a finding or approve customer access.</p>
    <Status loading={findings.loading} error={findings.error} retry={findings.retry} />
    {findings.data && <>
      {!findings.data.length ? <p>No provider findings are available. Close this dialog and use Add finding before uploading remediation evidence.</p>
        : <label className="grid gap-1 text-sm">Target finding<select className={inputClass} value={selectedId} disabled={pending}
          onChange={event => setSelectedId(event.target.value)}>
          <option value="">Select a finding</option>
          {findings.data.map(item => <option key={item.findingId} value={item.findingId}>{item.title}</option>)}
        </select></label>}
      {selectedId && <>
        <Status loading={finding.loading} error={finding.error} retry={finding.retry} />
        {finding.data && <div className="mt-4">
          <p className="mb-4 text-sm">Finding: {finding.data.title} · Revision {finding.data.revision}</p>
          <EvidenceUpload key={finding.data.findingId} finding={finding.data} disabled={findings.loading || !!findings.error}
            onPendingChange={setPending} onSaved={onSaved} />
        </div>}
      </>}
    </>}
    <button type="button" className={`${secondaryButtonClass} mt-4`} disabled={pending} onClick={onClose}>Cancel</button>
  </SetupDialog>;
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
  return <ProviderPanel title="Evidence library">
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && <>{!remote.data.length ? <p>No finding evidence retained. Source-package documents remain in Authorizations &amp; sources.</p>
      : <div className="provider-table-wrap relative"><table className="provider-table table-fixed min-w-[700px]" aria-label="Evidence library">
        <colgroup><col style={{ width: '42%' }} /><col style={{ width: '24%' }} /><col style={{ width: '22%' }} /><col style={{ width: '12%' }} /></colgroup>
        <thead><tr><th>Artifact</th><th>Customer access</th><th>Freshness</th><th><span className="sr-only">Actions</span></th></tr></thead>
        <tbody>{remote.data.slice((currentPage - 1) * pageSize, currentPage * pageSize).map(item => <tr key={item.evidenceId}>
          <td><strong>{item.fileName}</strong><small><span className="line-clamp-2">{item.description || 'No description recorded'}</span></small></td>
          <td><ProviderBadge tone="neutral">Provider private</ProviderBadge><small>Summary approvals are separate</small></td>
          <td>{item.latestReview ? `Reviewed ${new Date(item.latestReview.reviewedAt).toLocaleDateString()}` : `Retained ${new Date(item.createdAt).toLocaleDateString()}`}
            <small>{item.latestReview ? stateLabel(item.state) : 'No retained review'}</small></td>
          <td><Link className="provider-secondary" aria-label="Review evidence access" to={api.authorizationHref(offering.offeringId, `evidence/${encodeURIComponent(item.evidenceId)}?findingId=${encodeURIComponent(item.findingId)}`)}>Review</Link></td>
        </tr>)}</tbody>
      </table></div>}
      {remote.data.length > pageSize && <Pager page={currentPage} pageSize={pageSize} total={remote.data.length} onPage={setPage} />}
    </>}
  </ProviderPanel>;
}

function FindingCreate({ offering, onSaved, onPendingChange }: { offering: Offering; onSaved: () => void; onPendingChange: (pending: boolean) => void }) {
  const [value, setValue] = useState<FindingInput>({ title: '', observation: '', severityAsStated: '', controlIds: [], citations: [] });
  const update = <K extends keyof FindingInput>(key: K, next: FindingInput[K]) => setValue(previous => ({ ...previous, [key]: next }));
  return <MutationForm label="Record finding" onSaved={onSaved} onPendingChange={onPendingChange} submitDisabled={!value.title.trim() || !value.observation.trim()}
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
  const [uploading, setUploading] = useState(false);
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
    <ProviderPanel title="Submit remediation evidence" action={<button type="button" className="provider-secondary"
      disabled={pending || evidence.loading || !!evidence.error} onClick={() => setUploading(true)}>Submit remediation evidence</button>}>
      <p>Attach evidence to {finding.title}. Uploading does not close the finding or approve customer access.</p>
    </ProviderPanel>
    {uploading && <SetupDialog title="Submit remediation evidence" description={`Finding: ${finding.title} · Revision ${finding.revision}`}
      busy={uploadPending} onClose={() => setUploading(false)}>
      <EvidenceUpload finding={finding} disabled={reviewPending || planPending || evidence.loading || !!evidence.error}
        onPendingChange={setUploadPending} onSaved={() => { setUploading(false); setUploadPending(false); reload(); }} />
      <button type="button" className={`${secondaryButtonClass} mt-4`} disabled={uploadPending} onClick={() => setUploading(false)}>Cancel</button>
    </SetupDialog>}
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
  return <ProviderPanel title="Provider remediation" action={<button type="button" className="provider-secondary" disabled={pending || disabled} onClick={() => {
    setEditing(true); setSaved(false); setTitle(''); setAction(''); setOwner('');
  }}>Add remediation plan</button>}>
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
    {editing && <SetupDialog title="Add remediation plan" description={`Finding: ${finding.title} · Revision ${finding.revision}`} busy={pending} onClose={() => setEditing(false)}>
      <Status loading={offering.loading} error={offering.error} retry={offering.retry} />
      <MutationForm label="Save remediation plan" disabled={disabled || offering.loading || !!offering.error || !offering.data}
      submitDisabled={!title.trim() || !action.trim()} onPendingChange={value => { setPending(value); onPendingChange(value); }}
      submit={key => api.createPoamItem(finding.offeringId, { expectedOfferingRevision: offering.data!.revision,
        title: title.trim(), findingIds: [finding.findingId], correctiveAction: action.trim(), ownerAsStated: owner.trim(), milestones: [], citations: [] }, key)}
      onSaved={() => { setSaved(true); setEditing(false); plans.retry(); offering.retry(); onPendingChange(false); setPending(false); }}>
      <Field label="Plan title" value={title} onChange={setTitle} required />
      <Field label="Corrective action" value={action} onChange={setAction} required multiline maxLength={8000} />
      <Field label="Owner as stated" value={owner} onChange={setOwner} />
    </MutationForm>
      <button type="button" className={`${secondaryButtonClass} mt-4`} disabled={pending} onClick={() => setEditing(false)}>Cancel</button>
    </SetupDialog>}
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
