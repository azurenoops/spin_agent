import { useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { getProviderCapability } from '../workspace-operations/api';
import { Pager, Status, useRemote } from '../workspace-operations/workspaceUi';
import { getPackageCandidates } from '../package-imports/api';
import { stateLabel } from '../package-imports/PackageReceipts';
import * as api from './api';
import type { Offering, OfferingBoundaryCapability } from './types';
import { ProviderBadge, ProviderPanel } from './ProviderPresentation';
import { readAllPages } from './providerReadModels';

export function OfferingCapabilities({ offering }: { offering: Offering }) {
  const [page, setPage] = useState(1);
  const remote = useRemote(signal => api.getBoundaryOverview(offering.offeringId, page, 1, signal), [offering.offeringId, offering.revision, page]);
  return <div className="space-y-5">
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && <>
      <div className="provider-banner provider-banner-release"><div>
        <strong>{remote.data.capabilities.published} capabilities available · {remote.data.capabilities.awaitingReview} awaiting review</strong>
        <p>Mission Owners use their selected published release until they review a replacement. Offering revision is not a release version.</p>
      </div><Link className="provider-secondary" to={api.authorizationHref(offering.offeringId, 'packages')}>Review source proposals</Link></div>
      <ProviderPanel title="Service implementations">
        {!remote.data.capabilities.total ? <p>No capability records linked to this offering. Review retained source proposals before publication.</p>
          : <div className="provider-table-wrap"><table className="provider-table" aria-label="Service implementations">
            <thead><tr><th>Capability</th><th>Control references</th><th>Responsibilities</th><th>Release state</th><th><span className="sr-only">Actions</span></th></tr></thead>
            <tbody>{remote.data.capabilities.items.map(item => <CapabilityRow key={`${item.capabilityId}:${item.candidateId}`} offeringId={offering.offeringId} item={item} />)}</tbody>
          </table></div>}
        {remote.data.capabilities.total > remote.data.capabilities.pageSize && <Pager {...remote.data.capabilities} onPage={setPage} />}
      </ProviderPanel>
    </>}
    <div className="grid gap-5 md:grid-cols-2">
      <ProviderPanel title="Components support capabilities"><p>A service or product is the implementation building block. Review the exact capability to inspect supporting components, evidence and publication checks.</p></ProviderPanel>
      <ProviderPanel title="Keep customer work visible"><p>Provider publication does not accept a Mission Owner’s duties. Review the retained responsibility split for the exact release before adoption.</p></ProviderPanel>
    </div>
  </div>;
}

function CapabilityRow({ offeringId, item }: { offeringId: string; item: OfferingBoundaryCapability }) {
  const sourceHref = item.packageId && item.candidateId
    ? api.authorizationHref(offeringId, `packages/${encodeURIComponent(item.packageId)}/candidates/${encodeURIComponent(item.candidateId)}`) : null;
  const href = item.capabilityId ? `/security-capabilities/${encodeURIComponent(item.capabilityId)}` : sourceHref;
  const detail = useRemote(async signal => {
    if (item.capabilityId) {
      const record = await getProviderCapability(item.capabilityId, signal);
      return { description: record.capability.description, controls: record.mappedControlIds, duties: null, releasedRevision: record.capability.releasedRevision };
    }
    if (item.packageId && item.candidateId) {
      const records = await readAllPages(page => getPackageCandidates(item.packageId!, { page, pageSize: 25 }, signal), signal);
      const record = records.find(candidate => candidate.candidateId === item.candidateId);
      if (!record) throw new Error('The retained source proposal could not be found.');
      return { description: record.description, controls: Object.keys(record.controlDuties), duties: record.controlDuties, releasedRevision: null };
    }
    return null;
  }, [item.capabilityId, item.packageId, item.candidateId, item.releaseId, item.reviewState]);
  return <tr>
    <td>{item.name}<small>{detail.data?.description || (detail.loading ? 'Loading implementation…' : 'Implementation summary unavailable')}</small></td>
    <td>{detail.data ? detail.data.controls.join(', ') || 'No mappings recorded' : detail.loading ? 'Loading…' : 'Not reported'}</td>
    <td>{detail.data?.duties && Object.keys(detail.data.duties).length
      ? <>{Object.entries(detail.data.duties).map(([control, duty]) => <small key={control}>{control}: {duty}</small>)}<small>Source proposal · not a published duty</small></>
      : href ? <Link to={`${href}${item.capabilityId ? '?tab=responsibilities' : ''}`}>Inspect responsibility split</Link> : 'Not reported'}</td>
    <td><ProviderBadge tone={item.publicationState === 'Published' ? 'success' : 'attention'}>{stateLabel(item.publicationState)}</ProviderBadge>
      <small>{stateLabel(item.reviewState)}</small>
      {item.releaseRevision != null && <small>Revision {item.releaseRevision}</small>}
      {item.releaseId && <details><summary>Exact release</summary><small>{item.releaseId}</small></details>}
    </td>
    <td>{href && <Link to={href}>{item.capabilityId ? 'Review capability' : 'Review source proposal'}</Link>}
      {detail.error && <button className="mt-2 text-xs text-indigo-700 underline" onClick={detail.retry}>Retry implementation details</button>}</td>
  </tr>;
}
