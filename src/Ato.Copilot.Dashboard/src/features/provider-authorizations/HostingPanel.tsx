import { useEffect, useRef, useState } from 'react';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import { packageArtifactUrl } from '../package-imports/api';
import { PackageImportError } from '../package-imports/request';
import {
  errorClass, message, Pager, secondaryButtonClass, Status, surfaceClass, useRemote, warningClass,
} from '../workspace-operations/workspaceUi';
import { getOffering, listBoundaries, listDecisionHistory, listDecisions } from './api';
import { CitationFields, Field, MutationForm, ScopeFields } from './forms';
import { createHostingAssignment, createHostingScope, listHostingAssignments, listHostingScopes } from './hostingApi';
import type { HostingAssignmentInput, HostingExclusion, HostingScopeInput, HostingScopeRevision } from './hostingTypes';
import type { AzureScope, Citation, ExternalDecision, Offering } from './types';

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const blankScope = (): AzureScope => ({ cloud: 'AzureUSGovernment', directoryTenantId: '', subscriptionId: '', resourceId: '' });
function scopeProblem(scope: AzureScope) {
  if (!guid.test(scope.directoryTenantId.trim()) || !guid.test(scope.subscriptionId.trim())) {
    return 'Each scope requires a valid Azure directory tenant GUID and subscription GUID.';
  }
  const path = scope.resourceId.trim().replace(/\/$/, '').split('/');
  if (path[0] !== '' || path[1]?.toLowerCase() !== 'subscriptions'
    || path[2]?.toLowerCase() !== scope.subscriptionId.trim().toLowerCase()
    || path.slice(1).some(segment => !segment || segment === '.' || segment === '..' || /[\s*?#%\\]/.test(segment))) {
    return 'Resource scope must use the exact subscription ID and valid path segments, not a wildcard or a loose prefix.';
  }
  return null;
}
const scopesProblem = (scopes: AzureScope[]) => scopes.length > 100
  ? 'At most 100 explicit scopes can be saved.' : scopes.map(scopeProblem).find(Boolean) ?? null;
const validCitations = (citations: Citation[]) => citations.length <= 100 && citations.every(citation =>
  guid.test(citation.packageId.trim()) && guid.test(citation.artifactId.trim())
  && !!citation.archivePath.trim() && !!citation.locator.trim() && !!citation.quote.trim());
const scopeConfirmation = 'I confirm this exact technical scope revision, not authorization coverage.';
const assignmentConfirmation = 'I confirm this allocation grants no permissions or authorization coverage.';

function ScopeDetails({ scopes }: { scopes: AzureScope[] }) {
  return scopes.length ? <ul className="space-y-2">{scopes.map((scope, index) =>
    <li key={index}><dl className="grid min-w-0 gap-1 break-words text-sm sm:grid-cols-2">
      <dt>Cloud</dt><dd>{scope.cloud}</dd><dt>Directory tenant ID</dt><dd>{scope.directoryTenantId}</dd>
      <dt>Subscription ID</dt><dd>{scope.subscriptionId}</dd><dt>Resource scope</dt><dd>{scope.resourceId}</dd>
    </dl></li>)}</ul> : <p className="text-sm">No explicit resources recorded; not universal scope.</p>;
}
function Sources({ citations }: { citations: Citation[] }) {
  return <div className="space-y-2 text-sm">
    <p>Source references do not grant access. Downloads independently check your current permissions.</p>
    {citations.length ? citations.map((source, index) => <div key={index} className="space-y-1 break-words rounded border p-3">
      <p>Package: {source.packageId}; artifact: {source.artifactId}</p>
      <p>{source.archivePath} - {source.locator}</p><blockquote className="whitespace-pre-wrap">{source.quote}</blockquote>
      <AuthenticatedDownload className={secondaryButtonClass} url={packageArtifactUrl(source.packageId, source.artifactId)}
        fileName={source.archivePath.split('/').pop()}>Download source {source.archivePath}</AuthenticatedDownload>
    </div>) : <p>No citations recorded.</p>}
  </div>;
}
function ExclusionFields({ value, onChange }: { value: HostingExclusion[]; onChange: (value: HostingExclusion[]) => void }) {
  return <fieldset className="space-y-3 rounded border p-3"><legend className="text-sm font-semibold">Hosting exclusions</legend>
    {value.map((item, index) => <div key={index} className="space-y-3">
      <ScopeFields label={`Excluded scope ${index + 1}`} value={[item.scope]} maxItems={1} onChange={scopes => {
        const next = scopes[0];
        onChange(next ? value.map((entry, row) => row === index ? { ...entry, scope: next } : entry)
          : value.filter((_, row) => row !== index));
      }} />
      <Field label={`Exclusion rationale ${index + 1}`} value={item.rationale} required multiline
        onChange={rationale => onChange(value.map((entry, row) => row === index ? { ...entry, rationale } : entry))} />
    </div>)}
    <button type="button" className={secondaryButtonClass} disabled={value.length >= 100}
      onClick={() => onChange([...value, { scope: blankScope(), rationale: '' }])}>Add exclusion</button>
  </fieldset>;
}
function ReferenceSnapshot({ record }: { record: ExternalDecision }) {
  return <article className="space-y-3 rounded border p-3 text-sm">
    <h4 className="font-semibold">{record.reference}</h4>
    <dl className="grid min-w-0 gap-2 break-words sm:grid-cols-2">
      <dt>Record ID</dt><dd>{record.recordId}</dd><dt>Revision</dt><dd>{record.revision}</dd>
      <dt>Immutable revision ID</dt><dd>{record.revisionId}</dd><dt>Snapshot hash</dt><dd>{record.snapshotHash}</dd>
      <dt>Boundary revision ID</dt><dd>{record.boundaryRevisionId}</dd>
      <dt>Issuing authority as stated</dt><dd>{record.issuingAuthority ?? 'Not recorded'}</dd>
      <dt>Decision as stated</dt><dd>{record.decisionAsStated ?? 'Not recorded'}</dd>
      <dt>Metadata review</dt><dd>{record.metadataReviewState}</dd>
      <dt>Standing</dt><dd>{record.currentStanding === 'CurrentAsRecorded'
        ? 'Current as recorded; not independently verified' : record.currentStanding}</dd>
      <dt>Issued / effective / expires</dt><dd>{record.issuedOn ?? 'Not recorded'} / {record.effectiveOn ?? 'Not recorded'} / {record.expiresOn ?? 'Not recorded'}</dd>
      <dt>Expiry basis</dt><dd>{record.expiryBasis}</dd>
      <dt>Source-stated scope</dt><dd className="whitespace-pre-wrap">{record.scopeStatement}</dd>
      <dt>Conditions</dt><dd>{record.conditions.length ? record.conditions.join('; ') : 'None recorded'}</dd>
      <dt>Impact review required</dt><dd>{record.impactReviewRequired ? 'Yes' : 'No'}</dd>
    </dl>
    <Sources citations={record.citations} />
  </article>;
}
function ReferenceHistory({ offeringId, record }: { offeringId: string; record: ExternalDecision }) {
  const [page, setPage] = useState(1);
  const remote = useRemote(signal => listDecisionHistory(offeringId, record.recordId, page, signal),
    [offeringId, record.recordId, record.revision, page]);
  return <section aria-label="Inherited reference history" className="space-y-3">
    <h3 className="font-semibold">Immutable inherited reference history</h3>
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && <>{remote.data.items.map(item => <ReferenceSnapshot key={item.revisionId} record={item} />)}
      {!remote.data.items.length && <p>No reference revisions returned.</p>}
      <Pager {...remote.data} onPage={setPage} /></>}
  </section>;
}
function InheritedReferences({ offering }: { offering: Offering }) {
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<ExternalDecision | null>(null);
  const remote = useRemote(signal => listDecisions(offering.offeringId, page, signal), [offering.offeringId, offering.revision, page]);
  const inherited = remote.data?.items.filter(item => item.recordKind === 'InheritedMicrosoftReference');
  return <section aria-label="Inherited Microsoft references" className={`${surfaceClass} space-y-4 p-4`}>
    <h2 className="text-lg font-semibold">Inherited Microsoft references</h2>
    <p className={warningClass}>These retained source statements do not verify Microsoft authority or extend hosting or authorization scope.
      Review and revision actions remain in the external decision workflow.</p>
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && <>
      {!inherited?.length && <p>No inherited Microsoft references on this page. Paging includes all offering decision records.</p>}
      {inherited?.map(item => <div key={item.recordId} className="space-y-2">
        <ReferenceSnapshot record={item} />
        <button type="button" className={secondaryButtonClass} onClick={() => setSelected(item)}>History of {item.reference}</button>
      </div>)}
      <p className="text-xs">Totals below include provider decisions and inherited references; an empty filtered page is not proof of absence.</p>
      <Pager {...remote.data} onPage={setPage} />
      {selected && <ReferenceHistory key={selected.recordId} offeringId={offering.offeringId} record={selected} />}
    </>}
  </section>;
}
function Responsibilities({ offering }: { offering: Offering }) {
  const [page, setPage] = useState(1);
  const remote = useRemote(signal => listBoundaries(offering.offeringId, page, signal), [offering.offeringId, offering.revision, page]);
  return <section aria-label="Boundary responsibilities" className={`${surfaceClass} space-y-4 p-4`}>
    <h2 className="text-lg font-semibold">Owner, provider and customer responsibilities</h2>
    <p className="break-words text-sm">Provider owner: {offering.providerId}</p>
    <p className="text-sm">Duties below belong to exact retained boundary versions. Hosting allocation is not acceptance of customer responsibilities
      or a mission authorization decision. The mission system owner and customer review their own duties separately.</p>
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && <>
      {!remote.data.items.length && <p>No boundary responsibilities returned. No duties are inferred.</p>}
      {remote.data.items.map(item => <article key={item.boundaryRevisionId} className="space-y-2 break-words rounded border p-3 text-sm">
        <h3 className="font-semibold">{item.name} - boundary revision {item.version}</h3>
        <p>Boundary ID: {item.boundaryRevisionId}; snapshot: {item.snapshotHash}</p>
        <p>{item.boundaryRevisionId === offering.currentBoundaryRevisionId ? 'Current boundary pointer' : 'Historical boundary version'}</p>
        <p className="whitespace-pre-wrap">{item.scopeStatement}</p>
        <h4 className="font-semibold">Provider responsibilities</h4>
        {item.providerResponsibilities.length ? <ul>{item.providerResponsibilities.map((duty, index) => <li key={index}>{duty}</li>)}</ul> : <p>None recorded.</p>}
        <h4 className="font-semibold">Customer responsibilities</h4>
        {item.customerResponsibilities.length ? <ul>{item.customerResponsibilities.map((duty, index) => <li key={index}>{duty}</li>)}</ul> : <p>None recorded; not a waiver of customer duties.</p>}
      </article>)}
      <Pager {...remote.data} onPage={setPage} />
    </>}
  </section>;
}

interface HostingPanelProps {
  offering: Offering; onChanged: () => void;
  task?: 'scope' | 'allocations'; initialScope?: HostingScopeRevision;
  onPendingChange?: (pending: boolean) => void;
}
export function HostingPanel(props: HostingPanelProps) {
  return <HostingWorkspace key={props.offering.offeringId} {...props} />;
}

function HostingWorkspace({ offering, onChanged, task, initialScope, onPendingChange }: HostingPanelProps) {
  const [scopePage, setScopePage] = useState(1);
  const [assignmentPage, setAssignmentPage] = useState(1);
  const [refresh, setRefresh] = useState(0);
  const [reloaded, setReloaded] = useState<Offering | null>(null);
  const current = reloaded && reloaded.revision > offering.revision ? reloaded : offering;
  const scopes = useRemote(signal => listHostingScopes(offering.offeringId, scopePage, signal), [offering.offeringId, current.revision, scopePage, refresh]);
  const assignments = useRemote(signal => task === 'scope' ? Promise.resolve(null) : listHostingAssignments(offering.offeringId, assignmentPage, signal),
    [offering.offeringId, current.revision, assignmentPage, refresh, task]);
  const [draft, setDraft] = useState<HostingScopeInput>({
    expectedOfferingRevision: offering.revision, predecessorRevisionId: offering.currentHostingScopeRevisionId,
    name: initialScope?.name ?? '', permittedScopes: initialScope?.permittedScopes ?? [],
    exclusions: initialScope?.exclusions ?? [], citations: initialScope?.citations ?? [],
  });
  const [allocation, setAllocation] = useState<HostingAssignmentInput>({
    targetTenantId: '', systemId: '', hostingScopeRevisionId: initialScope?.snapshot.revisionId ?? '', assignedScopes: [], references: [],
  });
  const [selectedScope, setSelectedScope] = useState<HostingScopeRevision | null>(initialScope ?? null);
  const [scopeConfirmed, setScopeConfirmed] = useState(false);
  const [assignmentConfirmed, setAssignmentConfirmed] = useState(false);
  const [scopePending, setScopePending] = useState(false);
  const [assignmentPending, setAssignmentPending] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [reloadBusy, setReloadBusy] = useState(false);
  const reloadLock = useRef(false);
  const [reloadError, setReloadError] = useState<string | null>(null);
  const [scopeNotice, setScopeNotice] = useState<string | null>(null);
  const [assignmentNotice, setAssignmentNotice] = useState<string | null>(null);
  const pending = scopePending || assignmentPending;
  useEffect(() => { onPendingChange?.(pending); }, [pending, onPendingChange]);
  const stale = conflict || current.revision !== draft.expectedOfferingRevision
    || current.currentHostingScopeRevisionId !== draft.predecessorRevisionId;
  const draftProblem = scopesProblem(draft.permittedScopes) ?? scopesProblem(draft.exclusions.map(item => item.scope));
  const allocationProblem = scopesProblem(allocation.assignedScopes);
  const validDraft = !!draft.name.trim() && !draftProblem && draft.exclusions.length <= 100
    && draft.exclusions.every(item => !!item.rationale.trim()) && validCitations(draft.citations);
  const validAllocation = guid.test(allocation.targetTenantId.trim()) && !!allocation.systemId.trim()
    && !!selectedScope && selectedScope.snapshot.revisionId === allocation.hostingScopeRevisionId
    && allocation.assignedScopes.length > 0 && !allocationProblem && validCitations(allocation.references);
  const editScope = (change: Partial<HostingScopeInput>) => { setDraft(value => ({ ...value, ...change })); setScopeConfirmed(false); setScopeNotice(null); };
  const editAssignment = (change: Partial<HostingAssignmentInput>) => { setAllocation(value => ({ ...value, ...change })); setAssignmentConfirmed(false); setAssignmentNotice(null); };
  const changed = () => { setRefresh(value => value + 1); onChanged(); };
  const reload = async () => {
    if (reloadLock.current || pending) return;
    reloadLock.current = true; setReloadBusy(true); setReloadError(null);
    try {
      const latest = await getOffering(offering.offeringId);
      if (latest.offeringId !== offering.offeringId || !Number.isSafeInteger(latest.revision) || latest.revision < 1) {
        throw new Error('The offering response did not identify the requested current revision.');
      }
      setReloaded(latest);
      setDraft(value => ({ ...value, expectedOfferingRevision: latest.revision, predecessorRevisionId: latest.currentHostingScopeRevisionId }));
      setScopeConfirmed(false); setConflict(false); setRefresh(value => value + 1);
    } catch (reason) { setReloadError(message(reason)); }
    finally { reloadLock.current = false; setReloadBusy(false); }
  };
  const saveScope = async (key: string) => {
    if (!validDraft || !scopeConfirmed || stale) throw new PackageImportError('Review the current offering revision and confirm valid explicit hosting scope.', 422);
    try {
      const saved = await createHostingScope(offering.offeringId, draft, key);
      setScopeNotice(`Saved hosting scope revision ${saved.snapshot.revision}: ${saved.snapshot.revisionId}. Snapshot ${saved.snapshot.snapshotHash}. Impact review: ${saved.impactReviewId ?? 'None returned'}.`);
      setReloaded({ ...current, revision: saved.offeringRevision, currentHostingScopeRevisionId: saved.snapshot.revisionId });
      setScopeConfirmed(false);
    } catch (reason) {
      if (reason instanceof PackageImportError && reason.status === 409) setConflict(true);
      throw reason;
    }
  };
  const saveAssignment = async (key: string) => {
    if (!validAllocation || !assignmentConfirmed) throw new PackageImportError('Select an exact hosting revision, enter target IDs and explicit scopes, and confirm the allocation.', 422);
    const saved = await createHostingAssignment(offering.offeringId, allocation, key);
    setAssignmentNotice(`Saved hosting assignment ${saved.assignmentId}, revision ${saved.revision}. Relationship: ${saved.relationshipState}. Hosting revision: ${saved.hostingScope.revisionId}.`);
    setAssignmentConfirmed(false);
  };

  return <div className="min-w-0 space-y-6">
    {!task && <p className={warningClass}>Hosting scope is technical allocation, independent of the recorded authorization boundary.
      Saving does not create Azure resources, grant permissions, verify authority, accept inheritance, or issue a mission ATO.
      Empty scope never means universal coverage. Covered-workload status requires separate evidenced review by the mission system&apos;s Authorizing Official.</p>}
    <details open={task ? undefined : true}><summary className="cursor-pointer text-sm font-medium">{task ? 'Hosting version details and history' : 'Hosting scope records'}</summary>
    <section aria-label="Hosting scope revisions" className={`${surfaceClass} space-y-4 p-4`}>
      <h2 className="text-lg font-semibold">Immutable hosting scope revisions</h2>
      <Status loading={scopes.loading} error={scopes.error} retry={scopes.retry} />
      {scopes.data && <>
        {!scopes.data.items.length && <p>No hosting scope revisions returned.</p>}
        {scopes.data.items.map(item => <article key={item.snapshot.revisionId} className="min-w-0 space-y-3 rounded border p-3">
          <h3 className="font-semibold">{item.name}</h3>
          <dl className="grid gap-2 break-words text-sm sm:grid-cols-2">
            <dt>Revision ID</dt><dd>{item.snapshot.revisionId}</dd><dt>Revision</dt><dd>{item.snapshot.revision}</dd>
            <dt>Snapshot hash</dt><dd>{item.snapshot.snapshotHash}</dd><dt>Predecessor</dt><dd>{item.predecessorRevisionId ?? 'None'}</dd>
            <dt>Impact review ID</dt><dd>{item.impactReviewId ?? 'None returned'}</dd>
          </dl>
          <p className="text-sm">{item.snapshot.revisionId === current.currentHostingScopeRevisionId ? 'Current hosting pointer' : 'Retained historical revision'}</p>
          <h4 className="font-semibold">Permitted technical scopes</h4><ScopeDetails scopes={item.permittedScopes} />
          <h4 className="font-semibold">Explicit exclusions</h4>
          {item.exclusions.length ? item.exclusions.map((entry, index) => <div key={index} className="space-y-2">
            <ScopeDetails scopes={[entry.scope]} /><p className="whitespace-pre-wrap text-sm">{entry.rationale}</p>
          </div>) : <p className="text-sm">No exclusions recorded; not proof of coverage.</p>}
          <Sources citations={item.citations} />
          <div className="flex flex-wrap gap-2">
            {task !== 'allocations' && <button type="button" className={secondaryButtonClass}
              disabled={pending || reloadBusy || item.snapshot.revisionId !== current.currentHostingScopeRevisionId}
              onClick={() => {
                setDraft({ expectedOfferingRevision: current.revision, predecessorRevisionId: item.snapshot.revisionId,
                  name: item.name, permittedScopes: item.permittedScopes, exclusions: item.exclusions, citations: item.citations });
                setConflict(false); setScopeConfirmed(false); setScopeNotice(null);
              }}>Prepare successor of hosting revision {item.snapshot.revision}</button>}
            {task !== 'scope' && <button type="button" className={secondaryButtonClass} disabled={pending || reloadBusy}
              onClick={() => { setSelectedScope(item); editAssignment({ hostingScopeRevisionId: item.snapshot.revisionId }); }}>
              Assign using hosting revision {item.snapshot.revision}</button>}
          </div>
        </article>)}
        <Pager {...scopes.data} onPage={setScopePage} />
      </>}
    </section></details>
    {task !== 'allocations' && <section aria-label="Prepare hosting scope revision" className={`${surfaceClass} space-y-4 p-4`}>
      <h2 className="text-lg font-semibold">Prepare hosting scope revision</h2>
      <details open={task ? undefined : true}><summary className="cursor-pointer text-sm">Details</summary>
        <p className="break-words text-sm">Expected offering revision: {draft.expectedOfferingRevision}. Predecessor revision: {draft.predecessorRevisionId ?? 'None (initial revision)'}</p>
      </details>
      <p className="text-sm">A successor preserves the previous immutable snapshot. Reloading concurrency metadata retains all entered scope material; review and confirm again.</p>
      {stale && <p className={warningClass}>The offering context is stale. Reload current records and reconfirm before creating another revision.</p>}
      <button type="button" className={secondaryButtonClass} disabled={pending || reloadBusy} onClick={() => void reload()}>
        {reloadBusy ? 'Reloading offering…' : 'Reload current offering revision'}</button>
      {reloadError && <p role="alert" className={errorClass}>{reloadError}</p>}
      {task && scopes.error && <div role="alert" className={errorClass}>
        <p>Hosting configuration unavailable. Your inputs are retained, but saving requires a successful hosting read.</p>
        <button type="button" className="underline" onClick={scopes.retry}>Retry hosting data</button>
      </div>}
      <MutationForm label="Save hosting scope revision" disabled={assignmentPending || reloadBusy || scopes.loading || !!scopes.error}
        submitDisabled={!validDraft || !scopeConfirmed || stale || scopes.loading || !!scopes.error}
        onPendingChange={setScopePending} submit={saveScope} onSaved={changed}>
        <Field label="Hosting scope name" value={draft.name} required onChange={name => editScope({ name })} />
        <ScopeFields label="Permitted hosting scopes" value={draft.permittedScopes} onChange={permittedScopes => editScope({ permittedScopes })} />
        <ExclusionFields value={draft.exclusions} onChange={exclusions => editScope({ exclusions })} />
        {draftProblem && <p className={warningClass}>{draftProblem}</p>}
        <CitationFields value={draft.citations} onChange={citations => editScope({ citations })} />
        {!validCitations(draft.citations) && <p className={warningClass}>Citations require package and artifact GUIDs, archive path, locator and quote; maximum 100 citations.</p>}
        <label className="flex items-start gap-2 text-sm"><input type="checkbox" required checked={scopeConfirmed}
          onChange={event => setScopeConfirmed(event.target.checked)} />{scopeConfirmation}</label>
      </MutationForm>
      {scopeNotice && <p role="status" className={`${surfaceClass} break-words p-3`}>{scopeNotice}</p>}
    </section>}
    {task !== 'scope' && <>
    <section aria-label="Hosting assignments" className={`${surfaceClass} space-y-4 p-4`}>
      <h2 className="text-lg font-semibold">Hosting assignments</h2>
      <Status loading={assignments.loading} error={assignments.error} retry={assignments.retry} />
      {assignments.data && <>
        {!assignments.data.items.length && <p>No hosting assignments returned.</p>}
        {assignments.data.items.map(item => <article key={item.assignmentId} className="space-y-3 rounded border p-3">
          <dl className="grid gap-2 break-words text-sm sm:grid-cols-2">
            <dt>Assignment ID</dt><dd>{item.assignmentId}</dd><dt>Assignment revision</dt><dd>{item.revision}</dd>
            <dt>Customer system ID</dt><dd>{item.systemId}</dd>
            <dt>Exact hosting revision</dt><dd>{item.hostingScope.revisionId}</dd>
            <dt>Hosting snapshot hash</dt><dd>{item.hostingScope.snapshotHash}</dd>
            <dt>Relationship state</dt><dd>{item.relationshipState}</dd>
          </dl><ScopeDetails scopes={item.assignedScopes} />
        </article>)}
        <Pager {...assignments.data} onPage={setAssignmentPage} />
      </>}
    </section>
    <section aria-label="Create hosting assignment" className={`${surfaceClass} space-y-4 p-4`}>
      <h2 className="text-lg font-semibold">Create hosting assignment</h2>
      <p className="break-words text-sm">Selected hosting revision: {selectedScope?.snapshot.revisionId ?? 'None; select an immutable revision above'}.
        {selectedScope && ` Snapshot ${selectedScope.snapshot.snapshotHash}.`}</p>
      <p className="text-sm">Enter the actual customer tenant and system identifiers. Customer tenant ID is not the Azure directory tenant ID.
        The server verifies membership, system tenancy and scope; knowing an ID does not grant system access. Assigned scopes are never copied implicitly.</p>
      <MutationForm label="Save hosting assignment" disabled={scopePending || reloadBusy || assignments.loading || !!assignments.error || scopes.loading || !!scopes.error}
        submitDisabled={!validAllocation || !assignmentConfirmed || assignments.loading || !!assignments.error || scopes.loading || !!scopes.error}
        onPendingChange={setAssignmentPending} submit={saveAssignment} onSaved={changed}>
        <Field label="Customer tenant ID" value={allocation.targetTenantId} required onChange={targetTenantId => editAssignment({ targetTenantId })} />
        <Field label="Customer system ID" value={allocation.systemId} required onChange={systemId => editAssignment({ systemId })} />
        <ScopeFields label="Explicit assigned scopes" value={allocation.assignedScopes} onChange={assignedScopes => editAssignment({ assignedScopes })} />
        {allocationProblem && <p className={warningClass}>{allocationProblem}</p>}
        {!!allocation.targetTenantId && !guid.test(allocation.targetTenantId.trim())
          && <p className={warningClass}>Customer tenant ID must be a valid GUID for the actual customer tenant.</p>}
        <CitationFields value={allocation.references} onChange={references => editAssignment({ references })} />
        {!validCitations(allocation.references) && <p className={warningClass}>References require package and artifact GUIDs, archive path, locator and quote; maximum 100 references.</p>}
        <label className="flex items-start gap-2 text-sm"><input type="checkbox" required checked={assignmentConfirmed}
          onChange={event => setAssignmentConfirmed(event.target.checked)} />{assignmentConfirmation}</label>
      </MutationForm>
      {assignmentNotice && <p role="status" className={`${surfaceClass} break-words p-3`}>{assignmentNotice}</p>}
    </section>
    </>}
    {!task && <><Responsibilities offering={current} />
    <InheritedReferences offering={current} /></>}
  </div>;
}
