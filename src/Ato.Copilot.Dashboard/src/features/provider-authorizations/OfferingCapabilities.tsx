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
  const [search, setSearch] = useState('');
  const remote = useRemote(async signal => {
    const first = await api.getBoundaryOverview(offering.offeringId, 1, 1, signal);
    const items = await readAllPages(async next => next === 1 ? first.capabilities
      : (await api.getBoundaryOverview(offering.offeringId, next, 1, signal)).capabilities, signal);
    const records = await Promise.all(items.map(async item => {
      try {
        return { item, detail: await readCapabilityDetails(item, signal), error: null };
      } catch (error) {
        signal.throwIfAborted();
        return { item, detail: null, error: error instanceof Error ? error.message : 'Implementation details unavailable.' };
      }
    }));
    return { ...first.capabilities, records };
  }, [offering.offeringId, offering.revision]);
  const query = search.trim().toLocaleLowerCase();
  const filtered = remote.data?.records.filter(({ item, detail }) =>
    !query || [item.name, detail?.description, ...(detail?.controls ?? [])].some(value => value?.toLocaleLowerCase().includes(query))) ?? [];
  const pageSize = 10;
  const currentPage = Math.min(page, Math.max(1, Math.ceil(filtered.length / pageSize)));
  const published = remote.data?.records.filter(record => record.item.publicationState === 'Published') ?? [];
  const revisions = [...new Set(published.flatMap(({ item }) => item.releaseRevision == null ? [] : [item.releaseRevision]))].sort((a, b) => a - b);
  const releaseLabel = !remote.data?.published ? 'No published release'
    : published.some(({ item }) => item.releaseRevision == null) || !revisions.length ? 'Release version not reported'
      : `Live: ${revisions.length === 1 ? 'revision' : 'revisions'} ${revisions.join(', ')}`;
  return <div className="space-y-[22px]">
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && !remote.loading && !remote.error && <>
      <div className="provider-banner provider-banner-release"><div>
        <strong>{remote.data.published} capabilities available · {remote.data.awaitingReview} awaiting review</strong>
        <p>Mission Owners use the published release until they review a replacement.</p>
      </div><ProviderBadge tone={remote.data.published ? 'success' : 'neutral'}>{releaseLabel}</ProviderBadge></div>
      <ProviderPanel title="Service implementations">
        <div className="provider-toolbar mb-4 flex flex-wrap items-center justify-between gap-3">
          <input type="text" aria-label="Search capabilities" placeholder="Search capabilities or controls"
            className="max-w-full rounded-md border border-slate-300 bg-white px-3 py-3 text-sm sm:w-[380px]"
            value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} />
          <ProviderBadge>{remote.data.awaitingReview} awaiting review</ProviderBadge>
        </div>
        {query && remote.data.records.some(record => record.error) && <div className="mb-3 text-sm">
          <p role="status">Search is incomplete while implementation details are unavailable. Retry the details before relying on control matches.</p>
          <button className="provider-secondary mt-2" onClick={remote.retry}>Retry capability search</button>
        </div>}
        {!remote.data.total ? <p>No capability records linked to this offering. Review retained source proposals before publication.</p>
          : <div className="provider-table-wrap relative"><table className="provider-table table-fixed min-w-[1040px]" aria-label="Service implementations">
            <colgroup><col style={{ width: '35%' }} /><col style={{ width: '17%' }} /><col style={{ width: '25%' }} /><col style={{ width: '15%' }} /><col style={{ width: '8%' }} /></colgroup>
            <thead><tr><th>Capability</th><th>Control references</th><th>Responsibilities</th><th>Release state</th><th><span className="sr-only">Actions</span></th></tr></thead>
            <tbody>{filtered.slice((currentPage - 1) * pageSize, currentPage * pageSize).map(record =>
              <CapabilityRow key={`${record.item.capabilityId}:${record.item.candidateId}`} offeringId={offering.offeringId} {...record} retry={remote.retry} />)}
              {!filtered.length && <tr><td colSpan={5}>{remote.data.records.some(record => record.error) ? 'No matches in the available metadata.' : 'No capabilities match this search.'}</td></tr>}
            </tbody>
          </table></div>}
        {filtered.length > pageSize && <Pager page={currentPage} pageSize={pageSize} total={filtered.length} onPage={setPage} />}
      </ProviderPanel>
    </>}
    <div className="grid gap-[22px] md:grid-cols-2">
      <ProviderPanel title="Components support capabilities"><p>A service or product is the implementation building block. Each capability explains the protection delivered and the evidence that supports it.</p>
        <Link className="provider-secondary mt-3" to={api.authorizationHref(offering.offeringId, 'inherited-coverage')}>View supporting components</Link>
      </ProviderPanel>
      <ProviderPanel title="Keep customer work visible"><p>Shared controls include specific Mission Owner tasks and evidence. Applying a capability does not mark those tasks complete.</p>
        <Link className="mt-4 inline-block text-sm font-semibold text-indigo-700" to={api.authorizationHref(offering.offeringId, 'inherited-coverage?task=responsibilities')}>Review mission responsibilities</Link>
      </ProviderPanel>
    </div>
  </div>;
}

interface CapabilityDetails { description: string; controls: string[]; duties: Record<string, string> | null }

async function readCapabilityDetails(item: OfferingBoundaryCapability, signal: AbortSignal): Promise<CapabilityDetails | null> {
  if (item.capabilityId) {
    const record = await getProviderCapability(item.capabilityId, signal);
    return { description: record.capability.description, controls: record.mappedControlIds, duties: null };
  }
  if (item.packageId && item.candidateId) {
    const records = await readAllPages(page => getPackageCandidates(item.packageId!, { page, pageSize: 25 }, signal), signal);
    const record = records.find(candidate => candidate.candidateId === item.candidateId);
    if (!record) throw new Error('The retained source proposal could not be found.');
    return { description: record.description, controls: Object.keys(record.controlDuties), duties: record.controlDuties };
  }
  return null;
}

function CapabilityRow({ offeringId, item, detail, error, retry }: {
  offeringId: string; item: OfferingBoundaryCapability; detail: CapabilityDetails | null; error: string | null; retry: () => void;
}) {
  const sourceHref = item.packageId && item.candidateId
    ? api.authorizationHref(offeringId, `packages/${encodeURIComponent(item.packageId)}/candidates/${encodeURIComponent(item.candidateId)}`) : null;
  const href = item.capabilityId ? `/security-capabilities/${encodeURIComponent(item.capabilityId)}` : sourceHref;
  return <tr>
    <td><strong>{item.name}</strong><small><span className="line-clamp-2">{detail?.description || 'Implementation summary unavailable'}</span></small></td>
    <td>{detail ? detail.controls.length ? detail.controls.map((control, index) =>
      <span key={control}>{index > 0 && ', '}<span className="inline-block whitespace-nowrap">{control}</span></span>) : 'No mappings recorded' : 'Not reported'}</td>
    <td>{detail?.duties && Object.keys(detail.duties).length
      ? <><small><span className="line-clamp-2">{Object.entries(detail.duties).map(([control, duty]) => <span className="block" key={control}>{control}: {duty}</span>)}</span></small><small>Source proposal · not a published duty</small></>
      : href ? <Link to={`${href}${item.capabilityId ? '?tab=responsibilities' : ''}`}>Inspect responsibility split</Link> : 'Not reported'}</td>
    <td><ProviderBadge tone={item.publicationState === 'Published' ? 'success' : 'attention'}>{stateLabel(item.publicationState)}</ProviderBadge>
      {(item.releaseId || item.releaseRevision != null) ? <details className="mt-1"><summary>Exact release</summary>
        {item.reviewState !== item.publicationState && <small>{stateLabel(item.reviewState)}</small>}
        {item.releaseRevision != null && <small>Revision {item.releaseRevision}</small>}
        {item.releaseId && <small>{item.releaseId}</small>}
      </details> : item.reviewState !== item.publicationState && <small>{stateLabel(item.reviewState)}</small>}
    </td>
    <td>{href && <Link className="provider-secondary" aria-label={item.capabilityId ? 'Review capability' : 'Review source proposal'} to={href}>Review</Link>}
      {error && <><p role="alert">{error}</p><button className="mt-2 text-xs text-indigo-700 underline" onClick={retry}>Retry implementation details</button></>}</td>
  </tr>;
}
