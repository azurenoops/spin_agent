import { useEffect, useId, useRef, useState } from 'react';
import * as api from '../../api/systemEnvironments';
import { listAllProviderRelationships } from '../provider-relationships/api';
import { ScopeDetails } from '../provider-relationships/MissionTaskPresentation';
import ProviderScopeReview from '../provider-relationships/ProviderScopeReview';
import { Link } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import SetupDialog from '../workspace-operations/SetupDialog';
import { buttonClass, inputClass, message, secondaryButtonClass, Status, useRemote } from '../workspace-operations/workspaceUi';

interface Props {
  systemId: string;
  systemName?: string;
  busy?: boolean;
  refreshVersion?: number;
  onChanged?: () => void;
}
const description = 'Select the provider services and scopes this system uses.';
const responsibilitiesPath = (systemId: string) => `/systems/${encodeURIComponent(systemId)}/inheritance/subscriptions`;

export default function ProviderServicesScopes(props: Props) {
  const session = useWorkspaceSession();
  const permitted = session?.workspace.kind === 'organization' && !!session.workspace.tenantId
    && session.workspace.mode === 'ordinary'
    && session.systemAccess?.systemId.toLowerCase() === props.systemId.toLowerCase()
    && session.systemAccess.permissions.canRead === true;
  if (!permitted) return <section aria-label="Provider services & scopes">
    <h2 className="text-lg font-semibold">Provider services &amp; scopes</h2>
    <p>{description}</p>
    <p>Open this system in its authorized organization workspace to view provider services and scopes.</p>
  </section>;
  const identity = JSON.stringify([session.workspace.tenantId, props.systemId, session.identity?.oid, session.roles]);
  return <ProviderContent key={identity} {...props} />;
}

function ProviderContent({ systemId, systemName, busy = false, refreshVersion = 0, onChanged }: Props) {
  const titleId = useId();
  const data = useRemote(signal => api.getSystemEnvironments(systemId, signal), [systemId, refreshVersion]);
  const [dialog, setDialog] = useState<'add' | 'view' | 'manage' | 'warning' | null>(null);
  const [selected, setSelected] = useState<api.SystemProviderScope | null>(null);
  const [selectedWarning, setSelectedWarning] = useState<api.LegacyEnvironmentReference | null>(null);
  const [working, setWorking] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const disabled = busy || working;
  const close = () => { if (!disabled) { setDialog(null); setSelected(null); setSelectedWarning(null); } };
  const saved = (noticeText: string) => {
    setDialog(null); setSelected(null); setSelectedWarning(null); setWorking(false); setNotice(noticeText); data.retry(); onChanged?.();
  };
  const scopes = data.data?.providerScopes?.filter(item => item.state === 'Active') ?? [];
  const warnings = [...new Map(data.data?.legacyReferences.filter(reference => reference.kind === 'ProviderScopeLink' || reference.kind === 'HostingAssignment')
    .map(reference => [reference.referenceId, reference])).values()];
  const warningAttachment = selectedWarning?.kind === 'ProviderScopeLink'
    ? data.data?.attachments.find(attachment => attachment.attachmentId === selectedWarning.referenceId) : undefined;
  const canManage = data.data?.permissions.canManageEnvironments === true;
  return <section id="provider-services-scopes" tabIndex={-1} aria-labelledby={titleId}
    className="min-w-0 space-y-4 rounded-[10px] border border-slate-200 bg-white p-[22px] dark:border-gray-700 dark:bg-gray-900">
    <header className="flex flex-wrap items-start justify-between gap-3">
      <div><h2 id={titleId} className="text-lg font-semibold">Provider services &amp; scopes</h2>
        <p className="mt-1 text-sm text-slate-500 dark:text-gray-400">{description}</p></div>
      <button type="button" className={buttonClass} disabled={disabled || data.loading || !canManage}
        onClick={() => { setDialog('add'); setNotice(null); }}>Add provider scope</button>
    </header>
    <Status loading={data.loading} error={data.error} retry={data.retry} />
    {notice && <p role="status" className="text-sm">{notice}</p>}
    {warnings.map(reference => <div key={reference.referenceId} role="alert" className="space-y-2 rounded border border-amber-300 p-3 text-sm">
      <h3 className="font-semibold">An existing hosting relationship needs review.</h3>
      <p><strong>{reference.displayName}</strong>: {reference.reason}</p>
      <button type="button" className={secondaryButtonClass} disabled={disabled} onClick={() => {
        const attachment = data.data?.attachments.find(row => row.attachmentId === reference.referenceId);
        const matches = data.data?.providerScopes?.filter(scope => reference.kind === 'ProviderScopeLink'
          ? scope.assignmentId === attachment?.hostingAssignmentId
          : scope.assignmentId === reference.referenceId || scope.relationshipId === reference.referenceId);
        const match = matches?.length === 1 ? matches[0] : undefined;
        setSelectedWarning(reference); setSelected(match ?? null); setDialog(match ? 'manage' : 'warning');
      }}>Review relationship</button>
    </div>)}
    {data.data && !canManage && <p className="text-sm">You can view provider scopes. Environment-management permission is required to add or remove relationships.</p>}
    {data.data && !scopes.length && <p className="text-sm">No provider scopes selected.</p>}
    <div className="grid min-w-0 gap-3">
      {scopes.map(item => {
        const linkedIds = new Set(data.data?.hostingLinks?.filter(link => link.assignmentId === item.assignmentId && link.state === 'Linked')
          .map(link => link.attachmentId));
        const linked = data.data?.attachments.filter(attachment => linkedIds.has(attachment.attachmentId) && attachment.attachmentState === 'Attached') ?? [];
        return <article key={item.assignmentId} aria-label={`${item.offeringName} — ${item.hostingScopeName}`}
          className="min-w-0 space-y-3 rounded border border-slate-200 p-4 text-sm dark:border-gray-700">
          <p className="text-slate-500 dark:text-gray-400">{item.providerName || 'Provider name unavailable'}</p>
          <h3 className="font-semibold">{item.offeringName}</h3>
          <p>{item.hostingScopeName}</p>
          <p>Scope release revision: {item.hostingScopeRevision ?? 'Unavailable'} · Relationship revision: {item.assignmentVersion}</p>
          <p>Relationship: {stateLabel(item.relationshipState)}{item.reviewRequired ? ' · Review required' : ''}</p>
          <ResponsibilityStatus review={item.responsibilityReview} />
          {linked.length ? <div><p>Linked subscriptions (optional)</p><ul className="list-inside list-disc">
            {linked.map(attachment => <li key={attachment.attachmentId}>{attachment.registration.displayName}</li>)}
          </ul></div> : <p>No subscriptions linked (optional).</p>}
          <div className="flex flex-wrap items-center gap-3">
            <button type="button" className={secondaryButtonClass} disabled={disabled}
              onClick={() => { setSelected(item); setDialog('view'); }}>View scope</button>
            <ResponsibilityLink systemId={systemId} review={item.responsibilityReview} />
            <button type="button" className={secondaryButtonClass} disabled={disabled}
              onClick={() => { setSelected(item); setDialog('manage'); }}>Manage relationship</button>
          </div>
        </article>;
      })}
    </div>
    <p className="text-xs text-slate-500 dark:text-gray-400">Provider relationships do not require subscriptions. They do not accept inheritance, approve responsibilities, satisfy controls or authorize this system. Environment-description drafts are saved separately.</p>
    {dialog && <SetupDialog title={dialog === 'add' ? 'Add provider scope' : dialog === 'manage' ? 'Manage provider relationship'
      : dialog === 'warning' ? 'Review relationship' : 'Provider scope details'}
      description={`${systemName || 'Selected system'} · ${description}`} placement="right" busy={disabled} onClose={close}>
      {dialog === 'add' && <AddProviderScope systemId={systemId} busy={busy} onBusyChange={setWorking}
        onSaved={() => saved('Provider scope added. Responsibilities and authorization remain separately reviewed.')} />}
      {selectedWarning && <div className="mb-4 space-y-2 rounded border border-amber-300 p-3 text-sm">
        <h3 className="font-semibold">{selectedWarning.displayName}</h3>
        <p>{selectedWarning.reason}</p>
        <p>Recorded state: {stateLabel(selectedWarning.reconciliationState)}</p>
        <p className="break-words">Recorded reference: {selectedWarning.referenceId}</p>
        {warningAttachment && <>
          <p>Recorded subscription: {warningAttachment.registration.displayName}</p>
          <p className="break-words">Retained provider assignment: {warningAttachment.hostingAssignmentId ?? 'Not recorded'}</p>
          <p>The subscription record is retained while its optional provider link is reconciled.</p>
        </>}
        {dialog === 'warning' && <p>This recorded reference cannot be matched to a current provider scope. Ask an authorized provider or system owner to reconcile this exact relationship. A subscription is not required and must not be added merely to resolve this reference.</p>}
      </div>}
      {selected && dialog === 'view' && <ScopeSummary item={selected} />}
      {selected && dialog === 'manage' && data.data && <ManageRelationship systemId={systemId} item={selected}
        workspace={data.data} canManage={canManage && selected.state === 'Active'} busy={disabled} onBusyChange={setWorking}
        onSaved={() => saved('Provider relationship updated. Subscriptions and retained history are unchanged.')} />}
    </SetupDialog>}
  </section>;
}

function stateLabel(state: string) {
  return state.replace(/([a-z])([A-Z])/g, '$1 $2');
}
function ScopeSummary({ item }: { item: api.SystemProviderScope }) {
  return <div className="space-y-3 text-sm">
    <h3 className="font-semibold">{item.providerName || 'Provider name unavailable'} · {item.offeringName}</h3>
    <p>{item.hostingScopeName}</p>
    <p className="break-words">Released scope: {item.hostingScopeRevisionId}</p>
    <p>Released scope revision: {item.hostingScopeRevision ?? 'Unavailable'}</p>
    <p>Relationship revision: {item.assignmentVersion} · Selection revision: {item.selectionVersion}</p>
    <ScopeDetails scopes={item.assignedScopes} />
    {item.publishedDuties && <PublishedDuties duties={item.publishedDuties} />}
    <p>Scope selection is not acceptance of provider coverage or customer responsibilities.</p>
  </div>;
}

function ResponsibilityStatus({ review }: { review?: api.ProviderScopeResponsibilityReview }) {
  if (!review) return <p>Responsibility review: unavailable here. Inspect the authoritative responsibility matrix for current decisions.</p>;
  return <div className="space-y-1">
    <p>Responsibility review: {stateLabel(review.state)}</p>
    {review.reason && <p>{review.reason}</p>}
    <p className="text-xs">{review.canReview
      ? review.canConfirm ? 'Review and confirmation are available in the responsibility matrix.' : 'Review is available; confirmation is not currently permitted.'
      : 'View only. Responsibility review and confirmation are not permitted here.'}</p>
  </div>;
}

function ResponsibilityLink({ systemId, review }: { systemId: string; review?: api.ProviderScopeResponsibilityReview }) {
  return <Link className="underline" to={responsibilitiesPath(systemId)}>
    {review?.canReview === true ? 'Review responsibilities' : 'View responsibilities'}
  </Link>;
}

function PublishedDuties({ duties }: { duties?: api.ProviderScopePublishedDuties }) {
  return <section aria-label="Published provider duties" className="space-y-3">
    <h4 className="font-semibold">Published provider duties</h4>
    {duties?.state === 'Available' ? <>
      {duties.reason && <p>{duties.reason}</p>}
      {!duties.capabilities.length && <p>No capability duty entries were recorded in this published scope.</p>}
      {duties.capabilities.map(capability => <article key={`${capability.capabilityId}:${capability.releaseId}`} className="space-y-2 rounded border p-3">
        <h5 className="font-semibold">{capability.capabilityName}</h5>
        {capability.description && <p className="whitespace-pre-wrap">{capability.description}</p>}
        <p>Capability release revision: {capability.releaseRevision}</p>
        {([
          ['Provider control responsibilities', capability.providerControlIds],
          ['Shared control responsibilities', capability.sharedControlIds],
          ['Customer control responsibilities', capability.customerControlIds],
        ] as const).map(([label, controls]) => <div key={label}>
          <h6 className="font-semibold">{label}</h6>
          <p>{controls.length ? controls.join(', ') : 'None recorded in this published release.'}</p>
        </div>)}
        <details className="break-words">
          <summary className="cursor-pointer">Pinned published source</summary>
          <div className="space-y-1 pt-2">
            <p>Capability: {capability.capabilityId}</p>
            <p>Release: {capability.releaseId}</p>
            <p>Release snapshot hash: {capability.releaseSnapshotHash}</p>
            <p>Content hash: {capability.contentHash}</p>
            <p>Applicability context: {capability.applicabilityContextId}</p>
          </div>
        </details>
      </article>)}
      <p>These are published source responsibilities, not accepted system responsibilities. Control applicability and customer-duty acceptance remain separately reviewed.</p>
    </> : <p>{duties?.reason ?? 'Customer-duty content and responsibility-review status are unavailable in this picker.'}</p>}
  </section>;
}

function ManageRelationship({ systemId, item, workspace, canManage, busy, onBusyChange, onSaved }: {
  systemId: string; item: api.SystemProviderScope; workspace: api.SystemEnvironmentsResponse; canManage: boolean; busy: boolean;
  onBusyChange: (busy: boolean) => void; onSaved: () => void;
}) {
  const [review, setReview] = useState(false);
  if (review) return <fieldset disabled={busy}><CanonicalRelationshipReview systemId={systemId} item={item}
    onBusyChange={onBusyChange} onSaved={onSaved} onCancel={() => setReview(false)} /></fieldset>;
  return <div className="space-y-4 text-sm">
    <ScopeSummary item={item} />
    <button type="button" className={secondaryButtonClass} disabled={busy || !item.relationshipId}
      onClick={() => setReview(true)}>Review provider relationship</button>
    <ResponsibilityStatus review={item.responsibilityReview} />
    <ResponsibilityLink systemId={systemId} review={item.responsibilityReview} />
    <ManageSubscriptionLinks systemId={systemId} item={item} workspace={workspace} busy={busy}
      canManage={canManage} onBusyChange={onBusyChange} onSaved={onSaved} />
    <p>Removing this relationship leaves system subscriptions, evidence and review history intact. Optional links are ended, not the subscription records.</p>
    {canManage ? <RemoveRelationship systemId={systemId} item={item} version={workspace.version} busy={busy}
      onBusyChange={onBusyChange} onSaved={onSaved} />
      : <p>Environment-management permission is required to remove this relationship.</p>}
  </div>;
}

function ManageSubscriptionLinks({ systemId, item, workspace, canManage, busy, onBusyChange, onSaved }: {
  systemId: string; item: api.SystemProviderScope; workspace: api.SystemEnvironmentsResponse; canManage: boolean; busy: boolean;
  onBusyChange: (busy: boolean) => void; onSaved: () => void;
}) {
  const linkedIds = new Set(workspace.hostingLinks?.filter(link => link.assignmentId === item.assignmentId && link.state === 'Linked')
    .map(link => link.attachmentId));
  const attachments = workspace.attachments.filter(attachment => attachment.attachmentState === 'Attached');
  const [intent, setIntent] = useState<{ attachmentId: string; action: 'Link' | 'Unlink' } | null>(null);
  const [rationale, setRationale] = useState('');
  const [preview, setPreview] = useState<api.EnvironmentImpactPreview | null>(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const writing = useRef(false);
  const mounted = useRef(true);
  const key = useRef(crypto.randomUUID());
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const selected = attachments.find(attachment => attachment.attachmentId === intent?.attachmentId);
  const resetPreview = () => { setPreview(null); setAcknowledged(false); setError(null); };
  const choose = (next: typeof intent) => { setIntent(next); setRationale(''); resetPreview(); };
  const prepare = async () => {
    if (!canManage || busy || writing.current || !selected || !intent || !rationale.trim()) return;
    writing.current = true; setWorking(true); onBusyChange(true); resetPreview();
    try {
      const result = await api.previewEnvironmentHostingLink(systemId, {
        expectedVersion: workspace.version, attachmentId: selected.attachmentId, expectedAttachmentVersion: selected.version,
        assignmentId: item.assignmentId, expectedAssignmentVersion: item.assignmentVersion,
        action: intent.action, rationale: rationale.trim(),
      });
      if (mounted.current) { setPreview(result); key.current = crypto.randomUUID(); }
    } catch (reason) { if (mounted.current) setError(message(reason)); }
    finally { writing.current = false; if (mounted.current) { setWorking(false); onBusyChange(false); } }
  };
  const commit = async () => {
    if (!canManage || busy || writing.current || !selected || !intent || !preview || !acknowledged || hasImpactBlockers(preview)) return;
    if (!Number.isFinite(Date.parse(preview.expiresAt)) || Date.parse(preview.expiresAt) <= Date.now()) {
      resetPreview(); setError('The link impact preview expired. Prepare a fresh preview.'); return;
    }
    writing.current = true; setWorking(true); onBusyChange(true); setError(null);
    try {
      const result = await api.commitEnvironmentHostingLink(systemId, {
        expectedVersion: preview.expectedVersion, previewId: preview.previewId, rationale: rationale.trim(), acknowledgeImpact: true,
      }, key.current);
      const linked = result.hostingLinks?.some(link => link.assignmentId === item.assignmentId
        && link.attachmentId === selected.attachmentId && link.state === 'Linked');
      if (!result.hostingLinks || linked !== (intent.action === 'Link')
        || !result.attachments.some(attachment => attachment.attachmentId === selected.attachmentId && attachment.attachmentState === 'Attached')
        || !result.providerScopes?.some(scope => scope.assignmentId === item.assignmentId && scope.state === 'Active'))
        throw new Error('The server did not confirm the link change with both records retained. Retry or refresh current records.');
      if (mounted.current) onSaved();
    } catch (reason) { if (mounted.current) setError(message(reason)); }
    finally { writing.current = false; if (mounted.current) { setWorking(false); onBusyChange(false); } }
  };
  const disabled = busy || working || !canManage;
  return <section aria-label="Optional subscription links" className="space-y-3 rounded border p-3">
    <h4 className="font-semibold">Optional subscription links</h4>
    <p>Link existing system subscriptions to this provider scope. A link does not change resource selection, accept inheritance or establish monitoring.</p>
    {attachments.filter(attachment => linkedIds.has(attachment.attachmentId)).map(attachment =>
      <div key={attachment.attachmentId} className="flex flex-wrap items-center justify-between gap-2">
        <p>{attachment.registration.displayName}</p>
        <button type="button" className={secondaryButtonClass} disabled={disabled}
          onClick={() => choose({ attachmentId: attachment.attachmentId, action: 'Unlink' })}>
          Remove link to {attachment.registration.displayName}
        </button>
      </div>)}
    {!attachments.some(attachment => linkedIds.has(attachment.attachmentId)) && <p>No subscriptions linked (optional).</p>}
    {!attachments.length && <p>There are no attached system subscriptions to link. This provider relationship remains valid without one.</p>}
    {attachments.some(attachment => !linkedIds.has(attachment.attachmentId)) && <label className="block">Subscription to link
      <select className={`${inputClass} mt-1 block w-full`} disabled={disabled} value={intent?.action === 'Link' ? intent.attachmentId : ''}
        onChange={event => choose(event.target.value ? { attachmentId: event.target.value, action: 'Link' } : null)}>
        <option value="">Choose an attached subscription (optional)</option>
        {attachments.filter(attachment => !linkedIds.has(attachment.attachmentId)).map(attachment =>
          <option key={attachment.attachmentId} value={attachment.attachmentId}>{attachment.registration.displayName}</option>)}
      </select>
    </label>}
    {error && <p role="alert">{error}</p>}
    {intent && selected && <div className="space-y-3">
      <p>{intent.action === 'Link' ? 'Link' : 'Remove link to'} {selected.registration.displayName}. Both the subscription and provider relationship will be retained.</p>
      <label className="block">Link change rationale<textarea className={`${inputClass} mt-1 block w-full`} maxLength={4000} rows={3}
        disabled={disabled} value={rationale} onChange={event => { setRationale(event.target.value); resetPreview(); }} /></label>
      <button type="button" className={secondaryButtonClass} disabled={disabled || !rationale.trim()} onClick={() => void prepare()}>
        Preview link change
      </button>
      {preview && <div className="space-y-3">
        <h4 className="font-semibold">Link change impact</h4>
        <ImpactDetails preview={preview} />
        <label className="flex items-start gap-2"><input type="checkbox" disabled={disabled || hasImpactBlockers(preview)} checked={acknowledged}
          onChange={event => setAcknowledged(event.target.checked)} />I acknowledge the link impact. Both records and their history are retained.</label>
        <button type="button" className={buttonClass} disabled={disabled || !acknowledged || hasImpactBlockers(preview)} onClick={() => void commit()}>Confirm link change</button>
      </div>}
    </div>}
  </section>;
}

function ImpactDetails({ preview }: { preview: api.EnvironmentImpactPreview }) {
  return <>
    <p>Workspace version: {preview.expectedVersion}</p>
    {hasImpactBlockers(preview) && <div role="alert">
      {preview.blockers?.length ? preview.blockers.map((blocker, index) => <p key={index}>{blocker}</p>)
        : <p>This change cannot be committed. Resolve the preview restrictions and prepare a fresh preview.</p>}
    </div>}
    {preview.warnings.map((warning, index) => <p key={index}>{warning}</p>)}
    {preview.systems.map(system => <p key={`${system.systemId}:${system.attachmentId}`}>
      {system.systemName}: {system.selectedResourceCount} selected resources · Assessment {system.assessmentAffected ? 'affected' : 'unchanged'} · Monitoring {system.monitoringAffected ? 'affected' : 'unchanged'}
    </p>)}
    {preview.requiresScopeReview && <p>Scope review is required after this change.</p>}
  </>;
}

function hasImpactBlockers(preview: api.EnvironmentImpactPreview) {
  return preview.canCommit === false || !!preview.blockers?.length;
}

function CanonicalRelationshipReview({ systemId, item, onBusyChange, onSaved, onCancel }: {
  systemId: string; item: api.SystemProviderScope; onBusyChange: (busy: boolean) => void;
  onSaved: () => void; onCancel: () => void;
}) {
  const review = useRemote(signal => listAllProviderRelationships(systemId, signal), [systemId, item.relationshipId]);
  const current = review.data?.find(row => row.relationshipId === item.relationshipId && row.assignmentId === item.assignmentId);
  return <div className="space-y-3">
    <Status loading={review.loading} error={review.error} retry={review.retry} />
    {current?.canReviewRelationship && current.state !== 'ExplicitlyCoveredByRecordedScope'
      ? <ProviderScopeReview systemId={systemId} item={current} onBusyChange={onBusyChange} onRecorded={onSaved} onCancel={onCancel} />
      : !review.loading && !review.error && <>
        <p>Relationship review is not permitted here. Covered-scope decisions require the assigned Authorizing Official and exact authorization evidence.</p>
        <button type="button" className={secondaryButtonClass} onClick={onCancel}>Back to relationship</button>
      </>}
  </div>;
}

function RemoveRelationship({ systemId, item, version, busy, onBusyChange, onSaved }: {
  systemId: string; item: api.SystemProviderScope; version: number; busy: boolean;
  onBusyChange: (busy: boolean) => void; onSaved: () => void;
}) {
  const [rationale, setRationale] = useState('');
  const [preview, setPreview] = useState<api.EnvironmentImpactPreview | null>(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [working, setWorking] = useState(false);
  const writing = useRef(false);
  const mounted = useRef(true);
  const key = useRef(crypto.randomUUID());
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const prepare = async () => {
    if (busy || writing.current || !rationale.trim()) return;
    writing.current = true; setWorking(true); onBusyChange(true); setError(null); setPreview(null); setAcknowledged(false);
    try {
      const result = await api.previewSystemProviderScopeRemoval(systemId, item.assignmentId, {
        expectedVersion: version, expectedAssignmentVersion: item.assignmentVersion,
        expectedSelectionVersion: item.selectionVersion, rationale: rationale.trim(),
      });
      if (mounted.current) { setPreview(result); key.current = crypto.randomUUID(); }
    } catch (reason) { if (mounted.current) setError(message(reason)); }
    finally { writing.current = false; if (mounted.current) { setWorking(false); onBusyChange(false); } }
  };
  const remove = async () => {
    if (busy || writing.current || !preview || !acknowledged || hasImpactBlockers(preview)) return;
    if (Date.parse(preview.expiresAt) <= Date.now() || !Number.isFinite(Date.parse(preview.expiresAt))) {
      setPreview(null); setAcknowledged(false); setError('The impact preview expired. Prepare a fresh preview.'); return;
    }
    writing.current = true; setWorking(true); onBusyChange(true); setError(null);
    try {
      const result = await api.removeSystemProviderScope(systemId, item.assignmentId, {
        expectedVersion: preview.expectedVersion, previewId: preview.previewId, rationale: rationale.trim(), acknowledgeImpact: true,
      }, key.current);
      if (!result.providerScopes || result.providerScopes.some(scope => scope.assignmentId === item.assignmentId && scope.state === 'Active'))
        throw new Error('The server did not confirm removal of this provider relationship. Retry or refresh the current scopes.');
      if (mounted.current) onSaved();
    } catch (reason) { if (mounted.current) setError(message(reason)); }
    finally { writing.current = false; if (mounted.current) { setWorking(false); onBusyChange(false); } }
  };
  return <div className="space-y-3">
    {error && <p role="alert">{error}</p>}
    <label className="block">Removal rationale<textarea className={`${inputClass} mt-1 block w-full`} rows={3} maxLength={4000}
      value={rationale} disabled={busy || working} onChange={event => {
        setRationale(event.target.value); setPreview(null); setAcknowledged(false);
      }} /></label>
    <button type="button" className={secondaryButtonClass} disabled={busy || working || !rationale.trim()} onClick={() => void prepare()}>
      {working ? 'Working…' : 'Preview removal'}
    </button>
    {preview && <div className="space-y-3">
      <h4 className="font-semibold">Removal impact</h4>
      <ImpactDetails preview={preview} />
      {hasImpactBlockers(preview) && <Link className="block underline" target="_blank" rel="noopener noreferrer"
        to={`/systems/${encodeURIComponent(systemId)}/security-capabilities`}>
        Review capability dependencies (opens in a new tab)
      </Link>}
      <label className="flex items-start gap-2"><input type="checkbox" checked={acknowledged} disabled={busy || working || hasImpactBlockers(preview)}
        onChange={event => setAcknowledged(event.target.checked)} />I acknowledge this impact. Remove only the provider relationship; retain subscriptions and history.</label>
      <button type="button" className={buttonClass} disabled={busy || working || !acknowledged || hasImpactBlockers(preview)} onClick={() => void remove()}>
        Remove provider relationship
      </button>
    </div>}
  </div>;
}

function AddProviderScope({ systemId, busy, onBusyChange, onSaved }: {
  systemId: string; busy: boolean; onBusyChange: (busy: boolean) => void; onSaved: () => void;
}) {
  const choices = useRemote(signal => api.getSystemProviderScopeChoices(systemId, signal), [systemId]);
  const [provider, setProvider] = useState<string | null>(null);
  const [selected, setSelected] = useState<api.SystemProviderScopeChoice | null>(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const writing = useRef(false);
  const mounted = useRef(true);
  const key = useRef(crypto.randomUUID());
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useEffect(() => { heading.current?.focus(); }, [provider, selected]);
  const save = async () => {
    if (!selected || !choices.data?.canManage || !acknowledged || busy || writing.current) return;
    writing.current = true; setWorking(true); onBusyChange(true); setError(null);
    try {
      const result = await api.addSystemProviderScope(systemId, {
        expectedVersion: choices.data.version, offeringId: selected.offeringId,
        expectedOfferingVersion: selected.offeringVersion, hostingScopeRevisionId: selected.hostingScopeRevisionId,
      }, key.current);
      if (!result.providerScopes?.some(scope => scope.state === 'Active' && scope.offeringId === selected.offeringId
        && scope.hostingScopeRevisionId === selected.hostingScopeRevisionId))
        throw new Error('The server did not confirm the selected provider scope. Retry or refresh the current scopes.');
      if (mounted.current) onSaved();
    } catch (reason) { if (mounted.current) setError(message(reason)); }
    finally { writing.current = false; if (mounted.current) { setWorking(false); onBusyChange(false); } }
  };
  const providers = [...new Map(choices.data?.choices.map(item => [item.providerId, {
    id: item.providerId, name: item.providerName || 'Provider name unavailable',
  }])).values()];
  const canonicalChoices = choices.data?.choices.every(item => typeof item.providerId === 'string' && !!item.providerId.trim()
    && typeof item.hostingScopeRevision === 'number' && Number.isSafeInteger(item.hostingScopeRevision) && item.hostingScopeRevision > 0);
  const allowed = choices.data?.canManage && !!choices.data.choices.length && canonicalChoices;
  const disabled = busy || working;
  return <div className="space-y-3 text-sm">
    <Status loading={choices.loading} error={choices.error} retry={choices.retry} />
    {choices.data && !choices.data.canManage && <p role="alert">You do not have permission to add provider scopes to this system.</p>}
    {choices.data?.canManage && !choices.data.choices.length && <p>No eligible released provider scopes are available.</p>}
    {choices.data?.canManage && !canonicalChoices && <div role="alert" className="space-y-2">
      <p>Provider identities or released scope revisions are unavailable. Refresh the provider choices before continuing.</p>
      <button type="button" className={secondaryButtonClass} disabled={disabled}
        onClick={() => { setProvider(null); setSelected(null); setAcknowledged(false); choices.retry(); }}>Refresh provider choices</button>
    </div>}
    {error && <p role="alert">{error}</p>}
    {allowed && <>
      <ol aria-label="Provider scope progress" className="flex flex-wrap gap-2 text-xs">
        {['Provider', 'Offering & released scope', 'Applicability & customer duties'].map((step, index) =>
          <li key={step} aria-current={index === (selected ? 2 : provider ? 1 : 0) ? 'step' : undefined}>{index + 1}. {step}</li>)}
      </ol>
      <h3 ref={heading} tabIndex={-1} className="font-semibold">{selected ? 'Applicability & customer duties' : provider ? 'Choose an offering & released scope' : 'Choose a provider'}</h3>
      {!provider && providers.map(value => <button key={value.id} type="button" className={`${secondaryButtonClass} block w-full text-left`}
        disabled={disabled} onClick={() => { if (value.id) setProvider(value.id); }}>
        {value.name}{providers.filter(other => other.name === value.name).length > 1 ? ` (${value.id})` : ''}
      </button>)}
      {provider && !selected && <>
        <p>{providers.find(item => item.id === provider)?.name}</p>
        {choices.data?.choices.filter(item => item.providerId === provider).map(item =>
          <button key={`${item.offeringId}:${item.hostingScopeRevisionId}`} type="button" disabled={disabled}
            className={`${secondaryButtonClass} block w-full text-left`}
            onClick={() => { setSelected(item); setAcknowledged(false); setError(null); key.current = crypto.randomUUID(); }}>
            {item.offeringName} — {item.hostingScopeName}
          </button>)}
        <button type="button" className={secondaryButtonClass} disabled={disabled} onClick={() => setProvider(null)}>Back to providers</button>
      </>}
      {selected && <>
        <p className="font-semibold">{selected.providerName || 'Provider name unavailable'} · {selected.offeringName}</p>
        <p>{selected.hostingScopeName}</p>
        <p className="break-words">Released scope: {selected.hostingScopeRevisionId} · Offering revision: {selected.offeringVersion}</p>
        <p>Released scope revision: {selected.hostingScopeRevision}</p>
        <p>Eligibility: {selected.eligibilitySource}</p>
        <ScopeDetails scopes={selected.permittedScopes} />
        {!!selected.exclusions.length && <section><h4 className="font-semibold">Scope exclusions</h4>
          {selected.exclusions.map((exclusion, index) => <div key={index}><p>{exclusion.rationale}</p>
            <ScopeDetails scopes={[exclusion.scope]} /></div>)}</section>}
        <section className="space-y-2 rounded border border-amber-300 p-3" aria-label="Customer duties">
          <h4 className="font-semibold">Customer duties</h4>
          <PublishedDuties duties={selected.publishedDuties} />
          <Link className="block underline" to={responsibilitiesPath(systemId)} target="_blank" rel="noopener noreferrer">
            Inspect recorded customer duties (opens in a new tab)
          </Link>
          <p>The authoritative responsibility matrix shows existing adopted capabilities and their recorded duties. It is not preselected to this provider release. Opening it in a new tab preserves this selection and your environment draft.</p>
          {selected.publishedDuties?.state !== 'Available' && <p>Missing duty information is not evidence of provider coverage.</p>}
          <p>Adding this scope does not accept customer duties or change their review status.</p>
        </section>
        <p>No Azure connection, subscription or allocation is required. This saves only the relationship, not the environment-description draft or authorization.</p>
        <label className="flex items-start gap-2"><input type="checkbox" checked={acknowledged} disabled={disabled}
          onChange={event => setAcknowledged(event.target.checked)} />I reviewed the applicability of this released scope. Customer responsibilities remain separately reviewed.</label>
        <div className="flex flex-wrap gap-3">
          <button type="button" className={secondaryButtonClass} disabled={disabled}
            onClick={() => { setSelected(null); setAcknowledged(false); setError(null); }}>Back to scopes</button>
          <button type="button" className={buttonClass} disabled={disabled || !acknowledged} onClick={() => void save()}>
            {working ? 'Saving…' : 'Save relationship'}
          </button>
        </div>
      </>}
    </>}
  </div>;
}
