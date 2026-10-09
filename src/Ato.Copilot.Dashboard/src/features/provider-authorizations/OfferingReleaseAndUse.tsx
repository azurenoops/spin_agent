import { useEffect, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { Pager, Status, useRemote } from '../workspace-operations/workspaceUi';
import { ProviderBadge, ProviderFact, ProviderPanel } from './ProviderPresentation';
import { publishedReleaseLabel } from './providerReadModels';
import { missionRelationshipLabels, scopeLabel } from './scopes';
import SetupDialog from '../workspace-operations/SetupDialog';
import * as api from './api';
import type { Offering, OfferingBoundaryMission } from './types';

export function OfferingReleaseContext({ offering }: { offering: Offering }) {
  const remote = useRemote(signal => api.getOfferingOverview(offering.offeringId, 1, 1, signal), [offering.offeringId, offering.revision]);
  const pending = remote.data ? remote.data.capabilities.awaitingReview + remote.data.capabilities.awaitingApproval : null;
  return <div className="offering-release-workspace space-y-5">
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && !remote.loading && !remote.error && <>
      {remote.data.offeringRevision !== offering.revision && <p role="alert">Offering context changed since the identity loaded.
        {' '}<Link className="provider-text" to={api.authorizationHref(offering.offeringId)}>Reopen offering overview</Link> before editing. Published references remain separate.</p>}
      <ProviderPanel title="Published release & working changes"
        description={<p>Keep what customers can use separate from what you are editing.</p>}
        action={<ProviderBadge tone={remote.data.capabilities.published ? 'success' : 'neutral'}>
          {remote.data.capabilities.published ? `Published ${publishedReleaseLabel(remote.data)?.toLowerCase() ?? 'version not reported'}` : 'No published capabilities'}
        </ProviderBadge>}>
        <nav className="offering-release-stages" aria-label="Provider release workflow orientation">
          <Link to={api.authorizationHref(offering.offeringId, 'packages')}>Review source</Link>
          <span aria-hidden="true">→</span>
          <Link to={api.authorizationHref(offering.offeringId, 'boundary')}>Check scope & duties</Link>
          <span aria-hidden="true">→</span>
          <Link to={api.authorizationHref(offering.offeringId, 'packages')}>Review working changes</Link>
          <span aria-hidden="true">→</span>
          <Link to={api.authorizationHref(offering.offeringId, 'inherited-coverage?task=capabilities')}>
            {remote.data.capabilities.published ? 'Published snapshots available' : 'Published snapshots not recorded'}
          </Link>
        </nav>
        <p className="offering-release-caption">Workflow orientation only—not a completion checklist or publication gate result.</p>
        <div className="offering-release-columns">
          <section className="offering-release-card" aria-labelledby="release-snapshots-title">
            <h3 id="release-snapshots-title">Customer-visible capability snapshots</h3>
            <dl>
              <ProviderFact label="Published revision">{publishedReleaseLabel(remote.data) || 'Not reported'}</ProviderFact>
              <ProviderFact label="Capabilities available">{remote.data.capabilities.published}</ProviderFact>
            </dl>
            <p>Published snapshots stay unchanged when an identity draft is edited.</p>
            <Link className="provider-text" to={api.authorizationHref(offering.offeringId, 'inherited-coverage?task=capabilities')}>Review exact capability releases</Link>
          </section>
          <section className="offering-release-card" aria-labelledby="release-identity-title">
            <h3 id="release-identity-title">Working offering identity</h3>
            <dl>
              <ProviderFact label="Recorded lifecycle">{offering.lifecycle}</ProviderFact>
              <ProviderFact label="Offering revision">{offering.revision}</ProviderFact>
            </dl>
            <p>{pending} capability records awaiting review or approval. No replacement release is assumed.</p>
            <Link className="provider-text" to={`${api.authorizationHref(offering.offeringId)}?action=identity`}>Edit service identity</Link>
          </section>
        </div>
      </ProviderPanel>
      <ProviderPanel title="Working changes" description={<p>Inspect recorded proposals or compare current identity edits—not a publication gate result.</p>}>
        <p>{remote.data.capabilities.awaitingReview} awaiting review · {remote.data.capabilities.awaitingApproval} awaiting approval · {remote.data.capabilities.proposed} source proposals</p>
        {!remote.data.capabilities.proposed && !pending &&
          <p className="mt-3">No pending capability proposals. The offering identity can still be Draft independently of published capability revisions.</p>}
        <div className="offering-release-change-row">
          <div>
            <h3>Compare service identity edits</h3>
            <p>Compare unsaved fields with the opening persisted identity in the existing editor.</p>
            <p className="offering-release-caption">A published offering-identity snapshot is not supplied by this API; no historical identity diff is fabricated.</p>
          </div>
          <Link className="provider-secondary" to={`${api.authorizationHref(offering.offeringId)}?action=identity`}>Compare identity edits</Link>
        </div>
        <div className="offering-release-change-row">
          <div>
            <h3>Review & publish in the recorded workflow</h3>
            <p>Publication requires the exact source context, authorized review and server checks. Provider publication does not establish mission adoption or an AO decision.</p>
          </div>
          <Link className="provider-primary" to={api.authorizationHref(offering.offeringId, 'packages')}>Review source proposals & version history</Link>
        </div>
        <details className="provider-record-details offering-release-details">
          <summary>Retained source & scope comparisons</summary>
          <p>Select a capability, retained package version or boundary before generating an impact review. Existing workflows compare exact revisions and preserve reviewed baselines; opening this screen creates no review or publication.</p>
        <div className="mt-4 flex flex-wrap gap-3">
          <Link className="provider-secondary" to={api.authorizationHref(offering.offeringId, 'packages')}>Select retained source version</Link>
          {offering.currentBoundaryRevisionId ? <Link className="provider-secondary" to={api.changeImpactHref(offering.offeringId, { boundaryRevisionId: offering.currentBoundaryRevisionId })}>Review boundary change impact</Link>
            : <p>Boundary comparison unavailable: no boundary revision recorded.</p>}
          {offering.currentHostingScopeRevisionId ? <Link className="provider-secondary" to={api.authorizationHref(offering.offeringId, 'inherited-coverage/propose')}>Stage a scope update</Link>
            : <Link className="provider-secondary" to={api.authorizationHref(offering.offeringId, 'inherited-coverage?task=hosting')}>Record hosting prerequisites</Link>}
        </div>
          <p className="offering-release-caption">Mission systems retain their selected capability releases until an authorized reviewer explicitly accepts a replacement.</p>
        </details>
      </ProviderPanel>
    </>}
  </div>;
}

export function OfferingMissionUse({ offering }: { offering: Offering }) {
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<OfferingBoundaryMission | null>(null);
  useEffect(() => { setSelected(null); setPage(1); }, [offering.offeringId]);
  const remote = useRemote(signal => api.getBoundaryOverview(offering.offeringId, 1, page, signal), [offering.offeringId, offering.revision, page]);
  const name = (item: OfferingBoundaryMission) => item.systemName?.trim() || 'Mission name unavailable';
  const relationship = (item: OfferingBoundaryMission) => missionRelationshipLabels[item.relationshipState]
    || (item.relationshipState ? 'Unrecognized recorded relationship' : 'Relationship not recorded');
  return <div className="offering-mission-workspace space-y-5">
    <ProviderPanel title="Mission use"
      description={<p>See associations separately from adopted capability releases.</p>}
      action={remote.data && !remote.loading && !remote.error && <ProviderBadge tone="neutral">
        {remote.data.missionSystems.total} recorded hosting {remote.data.missionSystems.total === 1 ? 'allocation' : 'allocations'}
      </ProviderBadge>}>
      <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
      {remote.data && !remote.loading && !remote.error && <>
        {remote.data.offeringRevision !== offering.revision && <p role="alert">Offering context changed since the identity loaded.
          {' '}<Link className="provider-text" to={api.authorizationHref(offering.offeringId)}>Reopen offering overview</Link> before allocation changes. No mission baseline has been replaced.</p>}
        {!remote.data.missionSystems.total && <p className="mt-4">No mission hosting allocations recorded. This does not grant eligibility or automatically attach a system.</p>}
        {remote.data.missionSystems.total > 0 && !remote.data.missionSystems.items.length && <p role="alert">Mission allocation records are missing from this page.
          {' '}<button type="button" className="provider-text" onClick={remote.retry}>Retry mission records</button> before relying on the total.</p>}
        <ul className="offering-mission-list">{remote.data.missionSystems.items.map(item => <li key={item.assignmentId}>
          <div className="offering-mission-row">
            <div>
              <h3>{name(item)}</h3>
              <p>{item.targetTenantName?.trim() || 'Customer organization not reported'} · {relationship(item)}</p>
            </div>
            <button type="button" className="provider-secondary" aria-label={`View handoff for ${name(item)}`}
              onClick={() => setSelected(item)}>View handoff</button>
          </div>
          <dl className="offering-mission-facts">
            <ProviderFact label="Hosting relationship"><ProviderBadge tone="neutral">{item.associated ? 'Associated' : 'Association pending'}</ProviderBadge></ProviderFact>
            <ProviderFact label="Adopted capabilities">{item.adoptedCapabilityCount}
              {!item.adoptedCapabilityCount && ' · no adopted release recorded'}</ProviderFact>
          </dl>
          {!item.adoptedCapabilityCount ? <p className="offering-mission-note">No pinned mission release is available. Provider publication does not imply mission adoption, accepted inheritance or an AO authorization decision.</p>
            : <p className="offering-mission-note">{item.adoptedReleases?.length
              ? 'Selected releases are recorded in the handoff. Adoption does not establish completed customer duties or an AO decision.'
              : 'Exact adopted release versions are not reported here.'}</p>}
        </li>)}</ul>
        {remote.data.missionSystems.total > remote.data.missionSystems.pageSize && <Pager {...remote.data.missionSystems} onPage={setPage} />}
      </>}
    </ProviderPanel>
    <ProviderPanel title="What the Mission Owner still needs to review">
      <ol className="offering-mission-guidance">
        <li>Confirm the assigned service scope matches the mission&apos;s documented boundary.</li>
        <li>Select specific published capability releases and review customer duties.</li>
        <li>Record system responsibilities and supporting evidence in the mission workflow.</li>
      </ol>
      <p>Association is not adoption. This is handoff guidance, not a claim that these tasks are complete.</p>
      <Link className="provider-text mt-4" to={api.authorizationHref(offering.offeringId, 'boundary')}>Review the provider&apos;s recorded scope</Link>
      <details className="provider-record-details"><summary>Allocation management</summary>
        <p>Mission reviewers select exact releases and document their customer duties. Provider publication does not complete SSP narratives, accept inheritance, establish cATO readiness or submit an eMASS package.</p>
        <Link className="provider-secondary mt-3" to={api.authorizationHref(offering.offeringId, 'inherited-coverage?task=missions')}>Manage existing allocation workflow</Link>
      </details>
    </ProviderPanel>
    {selected && <SetupDialog title={`Mission handoff · ${name(selected)}`} busy={false}
      className="provider-workspace offering-mission-handoff" onClose={() => setSelected(null)}
      description="Recorded service relationship and selected capability releases. Inspection does not change mission records or authorize the system."
      footer={<button type="button" className="provider-secondary" onClick={() => setSelected(null)}>Close handoff</button>}>
      {remote.data && remote.data.offeringRevision !== offering.revision && <p role="alert">Offering context changed since the identity loaded.
        {' '}<Link className="provider-text" to={api.authorizationHref(offering.offeringId)}>Reopen offering overview</Link> before allocation changes.</p>}
      <dl className="offering-mission-handoff-facts">
        <ProviderFact label="Customer organization">{selected.targetTenantName?.trim() || 'Not reported'}</ProviderFact>
        <ProviderFact label="Recorded relationship">{relationship(selected)}</ProviderFact>
        <ProviderFact label="Adopted capabilities">{selected.adoptedCapabilityCount}</ProviderFact>
      </dl>
      <h3 className="mt-5 font-semibold">Selected capability releases</h3>
      {selected.adoptedReleases?.length ? <ul className="mt-3 space-y-3 text-sm">{selected.adoptedReleases.map(release => <li key={`${release.capabilityId}:${release.releaseId}`}>
        {release.capabilityName} · Selected revision {release.revision}{release.updateAvailable ? ` · Provider revision ${release.currentReleaseRevision ?? 'not reported'} available for explicit review` : ''}
      </li>)}</ul> : <p className="mt-3 text-sm">{selected.adoptedCapabilityCount
        ? 'Exact adopted release versions are not reported here.' : 'No provider capabilities adopted. Customer applicability and duties still need explicit review.'}</p>}
      <details className="provider-record-details"><summary>Allocated scope & provenance</summary>
        {selected.assignedScopes.length ? <ul className="mt-3 space-y-2 text-sm">{selected.assignedScopes.map((scope, index) =>
          <li key={index}>{scopeLabel(scope)}</li>)}</ul> : <p>No assigned resource or service scopes reported.</p>}
        <p className="mt-3 text-xs">System: {selected.systemId}<br />Allocation: {selected.assignmentId}<br />Recorded relationship code: {selected.relationshipState || 'Not recorded'}</p>
        <p className="mt-2 text-xs">Offering: {offering.offeringId} · identity revision {offering.revision}</p>
        {selected.adoptedReleases?.map(release => <p key={release.releaseId} className="mt-2 text-xs">{release.capabilityName} · Release: {release.releaseId}</p>)}
      </details>
      <p className="offering-mission-note">Recorded covered workload relationships do not independently verify authorization coverage. Source and mission reviews remain authoritative.</p>
      <Link className="provider-primary mt-4" to={api.authorizationHref(offering.offeringId, `missions/${encodeURIComponent(selected.assignmentId)}`)}>Inspect service relationship</Link>
    </SetupDialog>}
  </div>;
}
