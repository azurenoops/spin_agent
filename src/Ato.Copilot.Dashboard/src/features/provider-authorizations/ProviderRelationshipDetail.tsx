import { useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { Pager, Status, useQueryState, useRemote } from '../workspace-operations/workspaceUi';
import { ProviderBadge, ProviderFact, ProviderPanel } from './ProviderPresentation';
import { HostingContextSummary, HostingScopeIdentity } from './HostingContextSummary';
import { authorizationHref, changeImpactHref, getBoundaryOverview } from './api';
import { getHostingAssignment, getHostingScope } from './hostingApi';
import type { Offering, OfferingBoundaryOverview } from './types';
import type { HostingAssignment } from './hostingTypes';
import WorkspacePageHeader from '../../components/layout/WorkspacePageHeader';
import SetupDialog from '../workspace-operations/SetupDialog';
import { allocationScopeName, ProviderAllocationForm } from './ProviderAllocationForm';
import { MissionReleaseSummary, ProviderMissionHandoff, type HandoffStep } from './ProviderMissionHandoff';

async function relationshipProjection(offeringId: string, assignment: HostingAssignment, capabilityPage: number,
  missionPage: number, signal: AbortSignal) {
  const read = async (page: number) => {
    const value = await getBoundaryOverview(offeringId, capabilityPage, page, signal);
    const validPage = (data: OfferingBoundaryOverview['missionSystems'] | OfferingBoundaryOverview['capabilities']) =>
      data && Array.isArray(data.items) && Number.isSafeInteger(data.total) && data.total >= data.items.length
      && Number.isSafeInteger(data.pageSize) && data.pageSize > 0 && data.items.length <= data.pageSize;
    if (!value || value.offeringId !== offeringId || !Number.isSafeInteger(value.offeringRevision) || value.offeringRevision < 1
      || !validPage(value.missionSystems) || !validPage(value.capabilities)
      || value.missionSystems.page !== page || value.capabilities.page !== capabilityPage)
      throw new Error('The provider relationship projection is unavailable or does not match this offering.');
    return value;
  };
  const find = (value: OfferingBoundaryOverview) => {
    const item = value.missionSystems.items.find(row => row.assignmentId === assignment.assignmentId);
    if (!item) return null;
    if (item.systemId !== assignment.systemId || typeof item.associated !== 'boolean'
      || !Number.isSafeInteger(item.adoptedCapabilityCount) || item.adoptedCapabilityCount < 0
      || item.relationshipState !== assignment.relationshipState)
      throw new Error('The relationship projection changed or does not match the selected allocation. Reload current records.');
    return { relationship: item, capabilities: value.capabilities };
  };
  const first = await read(missionPage);
  const selected = find(first);
  if (selected) return selected;
  // Direct bookmarks may outlive a listing page; locate only the requested allocation.
  for (let page = 1; page <= Math.max(1, Math.ceil(first.missionSystems.total / first.missionSystems.pageSize)); page++) {
    if (page === missionPage) continue;
    const found = find(await read(page));
    if (found) return found;
  }
  throw new Error('The selected allocation is absent from the offering relationship projection. No association or adoption count is inferred.');
}

export function ProviderRelationshipDetail({ offering, assignmentId }: { offering: Offering; assignmentId: string }) {
  const [handoff, setHandoff] = useState<HandoffStep | null>(null);
  const [assignOpen, setAssignOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const { params: query, set } = useQueryState();
  const page = (key: string) => {
    const value = Number(query.get(key));
    return Number.isSafeInteger(value) && value > 0 ? value : 1;
  };
  const missionPage = page('missionPage');
  const offeringPage = page('offeringPage');
  const capabilityPage = page('capabilityPage');
  const allocation = useRemote(signal => getHostingAssignment(offering.offeringId, assignmentId, signal), [offering.offeringId, assignmentId]);
  const snapshot = useRemote(async signal => {
    if (!allocation.data) return null;
    const value = await getHostingScope(offering.offeringId, allocation.data.hostingScope.revisionId, signal);
    if (value.snapshot.revision !== allocation.data.hostingScope.revision || value.snapshot.snapshotHash !== allocation.data.hostingScope.snapshotHash)
      throw new Error('The retained scope no longer matches the allocation snapshot. Reload before following this relationship.');
    return value;
  }, [offering.offeringId, allocation.data?.hostingScope.revisionId, allocation.data?.hostingScope.revision, allocation.data?.hostingScope.snapshotHash]);
  const projection = useRemote(signal => allocation.data
    ? relationshipProjection(offering.offeringId, allocation.data, capabilityPage, missionPage, signal) : Promise.resolve(null),
  [offering.offeringId, allocation.data?.revision, allocation.data?.relationshipState, capabilityPage, missionPage]);
  const refresh = () => { allocation.retry(); snapshot.retry(); projection.retry(); };
  const back = `/systems?${new URLSearchParams({ offeringId: offering.offeringId, offeringPage: String(offeringPage), missionPage: String(missionPage) })}`;
  return <>
    <div className="provider-page-head"><WorkspacePageHeader eyebrow="Provider operations" title={allocation.data?.systemName || 'Service relationship'}
      description="Track the service relationship while preserving the Mission Owner's control over their system."
      actions={<button type="button" className="provider-primary" disabled={!projection.data} onClick={() => setHandoff('hosting')}>Preview Mission Owner handoff</button>} /></div>
    <div className="provider-grid"><div className="space-y-5">
    <div className="flex flex-wrap gap-3"><Link className="provider-secondary" to={back}>Back to mission systems</Link>
      <button className="provider-secondary" onClick={refresh}>Reload relationship</button></div>
    <Status loading={allocation.loading} error={allocation.error} retry={refresh} />
    {allocation.data && <>
      <ProviderPanel title="Service relationship">
        <dl>
          <ProviderFact label="Organization">{allocation.data.targetTenantName || 'Name unavailable'}</ProviderFact>
          <ProviderFact label="Offering">{offering.name}</ProviderFact>
          <ProviderFact label="Allocated scope">{allocation.data.assignedScopes.map(allocationScopeName).join(', ') || 'No scope recorded'}</ProviderFact>
          <ProviderFact label="Provider assignment"><ProviderBadge tone="success">Recorded · Revision {allocation.data.revision}</ProviderBadge></ProviderFact>
          <ProviderFact label="Mission association">{projection.data
            ? <ProviderBadge tone={projection.data.relationship.associated ? 'success' : 'attention'}>{projection.data.relationship.associated ? allocation.data.relationshipState : 'Awaiting confirmation'}</ProviderBadge>
            : projection.error ? 'Unavailable' : 'Loading association'}</ProviderFact>
          <ProviderFact label="Capability adoption">{projection.data ? <MissionReleaseSummary relationship={projection.data.relationship} /> : 'Not loaded'}</ProviderFact>
          <ProviderFact label="Customer duties">Require separate authorized mission review</ProviderFact>
        </dl>
        <details className="mt-4 text-xs"><summary>Retained identities</summary>
          <p className="break-all">Allocation: {allocation.data.assignmentId}<br />System: {allocation.data.systemId}<br />
            Scope: {allocation.data.hostingScope.revisionId} · Revision {allocation.data.hostingScope.revision}<br />
            Scope hash: {allocation.data.hostingScope.snapshotHash}</p>
        </details>
      </ProviderPanel>
      <details className="provider-record-details"><summary>Allocation source details</summary><ProviderPanel title="Allocated service scope">
        <Status loading={snapshot.loading} error={snapshot.error} retry={snapshot.retry} />
        {snapshot.data && <><HostingContextSummary offeringName={offering.name} scope={snapshot.data} />
          <p className="mt-1 text-xs">This is the allocation’s retained scope, not an automatic replacement with the offering’s newest scope.</p>
          <ul className="mt-4 space-y-2 text-sm">{allocation.data.assignedScopes.map((item, index) => <li className="break-words" key={index}><HostingScopeIdentity scope={item} /></li>)}</ul></>}
      </ProviderPanel></details>
      <Status loading={projection.loading} error={projection.error} retry={refresh} />
      {projection.data && <>
        <ProviderPanel title="Customer handoff">
          {([
            ['hosting', '1. Confirm the hosting association', 'Mission Owner checks the selected system and scope.'],
            ['capabilities', '2. Apply published capabilities', 'Choose the release and inspect customer duties.'],
            ['documents', '3. Complete the system package', 'Evidence and implementation records feed document previews.'],
          ] as const).map(([step, title, description]) => <div key={step} className="provider-checklist-row">
            <div><h3>{title}</h3><p>{description}</p></div>
            <button type="button" className="provider-secondary" aria-label={`Preview ${title.slice(3)}`} onClick={() => setHandoff(step)}>Preview</button>
          </div>)}
        </ProviderPanel>
        {!!projection.data.relationship.adoptedReleases?.some(item => item.updateAvailable) && <ProviderPanel title="Provider release updates">
          <p className="mb-3 text-xs">These actions review the provider's capability change. They do not record the customer's disposition or replace its adopted release.</p>
          {projection.data.relationship.adoptedReleases.filter(item => item.updateAvailable).map(item => <div key={item.capabilityId} className="provider-checklist-row">
            <div><h3>{item.capabilityName}</h3><p>Customer revision {item.revision} · Current offering revision {item.currentReleaseRevision}</p></div>
            <Link className="provider-secondary" to={changeImpactHref(offering.offeringId, { capabilityId: item.capabilityId })}
              aria-label={`Review provider impact for ${item.capabilityName}`}>Review provider impact</Link>
          </div>)}
        </ProviderPanel>}
        <details className="provider-record-details"><summary>Current offering catalog (not customer selections)</summary>
        <ProviderPanel title="Offering capability catalog">
          <p className="mb-3 text-xs">These are offering catalog records, not a per-system adopted-release ledger.</p>
          {!projection.data.capabilities.items.length && <p>No offering capabilities on this page.</p>}
          {projection.data.capabilities.items.map(item => <article key={item.capabilityId ?? item.candidateId} className="space-y-2 border-t py-4">
            <h3 className="font-semibold">{item.name}</h3><p className="text-sm">{item.publicationState} · {item.reviewState}</p>
            {item.capabilityId ? <div className="flex flex-wrap gap-3">
              <Link className="provider-secondary" to={`/security-capabilities/${encodeURIComponent(item.capabilityId)}`}>Open {item.name}</Link>
              <Link className="provider-secondary" to={`/security-capabilities/${encodeURIComponent(item.capabilityId)}?tab=review`}>Review current publication for {item.name}</Link>
            </div> : item.packageId && <Link className="provider-secondary"
              to={authorizationHref(offering.offeringId, `packages/${encodeURIComponent(item.packageId)}${item.candidateId ? `/candidates/${encodeURIComponent(item.candidateId)}` : ''}`)}>Review source proposal</Link>}
          </article>)}
          <Pager {...projection.data.capabilities} onPage={value => set({ capabilityPage: value })} />
        </ProviderPanel></details>
      </>}
    </>}
  </div><aside className="provider-support">
    <ProviderPanel title="Provider visibility"><p>Show this allocated relationship and its current customer release selections. This does not expose unrelated mission content or grant customer-workspace access.</p></ProviderPanel>
    <ProviderPanel title="Provider actions">
      <div className="grid gap-3">
        <button type="button" className="provider-secondary" onClick={() => setAssignOpen(true)}>Assign another service scope</button>
        <Link className="provider-text" to={`/provider-changes?offeringId=${encodeURIComponent(offering.offeringId)}&offeringPage=${offeringPage}`}>View offering impact reviews →</Link>
      </div>
    </ProviderPanel>
    <ProviderPanel title="Separate authority"><p>This provider-only view does not enter or impersonate the mission workspace. Customer responsibilities and mission authorization require their own authorized reviewers.</p></ProviderPanel>
  </aside></div>
  {handoff && projection.data && <SetupDialog title="Preview Mission Owner handoff" busy={false} onClose={() => setHandoff(null)}
    description="Read-only preview of this exact customer service relationship, not the private mission workspace.">
    <ProviderMissionHandoff initialStep={handoff} offeringName={offering.name} relationship={projection.data.relationship} />
  </SetupDialog>}
  {assignOpen && <SetupDialog title="Assign service scope" busy={pending} onClose={() => setAssignOpen(false)}
    description="Select the customer and permitted scope. Existing relationships are not replaced.">
    <ProviderAllocationForm offering={offering} onPendingChange={setPending} onSaved={() => { setAssignOpen(false); refresh(); }} />
  </SetupDialog>}
  </>;
}
