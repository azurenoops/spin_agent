import { useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { Pager, Status, inputClass, useRemote } from '../workspace-operations/workspaceUi';
import { ProviderBadge, ProviderPanel } from './ProviderPresentation';
import * as api from './api';
import { managementArrangements, serviceModels } from './OfferingIdentity';
import { offeringEnvironments as environments } from './scopes';
import type { Offering } from './types';
import { publishedReleaseLabel } from './providerReadModels';

export function OfferingList() {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const remote = useRemote(signal => api.listOfferings(page, search, signal), [page, search]);
  return <div className="space-y-5">
    <div className="provider-banner"><strong>One provider. Distinct services.</strong>
      <p>Keep each offering’s scope and source evidence separate. SPIN does not issue or independently verify external authorization decisions.</p>
    </div>
    <ProviderPanel title="Your offerings">
      <div className="mb-5 flex flex-wrap items-center justify-between gap-4">
        <label className="min-w-0 flex-1"><span className="sr-only">Search offerings</span>
          <input type="search" className={inputClass} placeholder="Search offerings" value={search}
            onChange={event => { setSearch(event.target.value); setPage(1); }} /></label>
        {remote.data && <ProviderBadge>{remote.data.total} {remote.data.total === 1 ? 'offering' : 'offerings'}{search ? ' found' : ''}</ProviderBadge>}
      </div>
      <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
      {remote.data && <>
        {!remote.data.items.length ? <div className="py-6">
          <h3 className="font-semibold">{search ? 'No matching offerings' : 'Create your first offering'}</h3>
          <p>{search ? 'Try another name or clear your search.' : 'Start with service identity, then add source material and review scope.'}</p>
          {search ? <button className="provider-secondary mt-3" onClick={() => { setSearch(''); setPage(1); }}>Clear search</button>
            : <Link className="provider-primary mt-3" to="/authorizations/create">Start service identity</Link>}
        </div> : <div className="provider-table-wrap"><table className="provider-table" aria-label="Your offerings">
          <thead><tr><th>Offering</th><th>Service model</th><th>Customer use</th><th>Status</th><th><span className="sr-only">Actions</span></th></tr></thead>
          <tbody>{remote.data.items.map(item => <tr key={item.offeringId}>
            <td><Link to={api.authorizationHref(item.offeringId)}>{item.name}</Link>
              <small>{item.environments.map(cloud => environments[cloud]).join(' · ') || 'Environment not recorded'}</small>
              {item.description && <small>{item.description}</small>}</td>
            <td>{item.serviceModel ? serviceModels[item.serviceModel] : 'Not recorded'}
              <small>{item.managementArrangement ? managementArrangements[item.managementArrangement] : 'Management not recorded'}</small></td>
            <OfferingUseCells offering={item} />
            <td><Link aria-label={`Manage ${item.name}`} className="provider-secondary" to={api.authorizationHref(item.offeringId)}>Manage</Link>
              <small><Link aria-label={`Add package to this offering: ${item.name}`} to={api.authorizationHref(item.offeringId, 'import')}>Add source material</Link></small></td>
          </tr>)}</tbody>
        </table></div>}
        {remote.data.total > remote.data.pageSize && <Pager {...remote.data} onPage={setPage} />}
      </>}
    </ProviderPanel>
  </div>;
}

function OfferingUseCells({ offering }: { offering: Offering }) {
  const remote = useRemote(signal => api.getOfferingOverview(offering.offeringId, 1, 1, signal), [offering.offeringId, offering.revision]);
  const releaseLabel = remote.data ? publishedReleaseLabel(remote.data) : null;
  return <>
    <td>{remote.data ? `${remote.data.hosting.associatedSystemCount} mission systems` : remote.loading ? 'Checking customer use…' : 'Customer use unavailable'}
      <small>{offering.currentBoundaryRevisionId ? 'Boundary recorded' : 'Boundary not recorded'}</small>
      <small>{offering.currentHostingScopeRevisionId ? 'Hosting scope recorded' : 'Hosting scope not recorded'}</small>
      {(!offering.currentBoundaryRevisionId || !offering.currentHostingScopeRevisionId) && <Link
        aria-label={`${offering.currentBoundaryRevisionId ? 'Define hosting scope' : 'Define boundary'} for ${offering.name}`}
        to={api.authorizationHref(offering.offeringId, offering.currentBoundaryRevisionId ? 'inherited-coverage' : 'boundary')}>
        {offering.currentBoundaryRevisionId ? 'Define hosting scope' : 'Define boundary'}</Link>}
    </td>
    <td>{remote.data ? <><ProviderBadge tone={remote.data.capabilities.published ? 'success' : 'neutral'}>
      {remote.data.capabilities.published ? releaseLabel ? `${releaseLabel} published` : `${remote.data.capabilities.published} capabilities published` : 'No published capabilities'}
    </ProviderBadge>{remote.data.capabilities.published > 0 && <small>{releaseLabel ? `${remote.data.capabilities.published} capabilities published` : 'Release version not reported'}</small>}</>
      : <span>{remote.loading ? 'Checking publication…' : 'Publication unavailable'}</span>}
      <small>Offering lifecycle: {offering.lifecycle}</small>
      {remote.error && <button className="text-indigo-700 underline" onClick={remote.retry}>Retry offering summary</button>}
    </td>
  </>;
}
