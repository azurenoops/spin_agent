import { Link } from '../workspaces/workspaceNavigation';
import { errorClass, Status, useRemote } from '../workspace-operations/workspaceUi';
import { HostingPanel } from './HostingPanel';
import { getHostingScope } from './hostingApi';
import { authorizationHref } from './api';
import { ProviderPanel, ProviderSupport } from './ProviderPresentation';
import type { Offering } from './types';

export function ScopeProposalPage({ offering }: { offering: Offering }) {
  const scope = useRemote(signal => offering.currentHostingScopeRevisionId
    ? getHostingScope(offering.offeringId, offering.currentHostingScopeRevisionId, signal) : Promise.resolve(null),
  [offering.offeringId, offering.currentHostingScopeRevisionId, offering.revision]);
  const mismatch = scope.data && (scope.data.offeringId !== offering.offeringId
    || scope.data.snapshot.revisionId !== offering.currentHostingScopeRevisionId);
  return <div className="provider-grid"><div className="space-y-5">
    <Link className="provider-secondary" to={authorizationHref(offering.offeringId, 'inherited-coverage')}>Back to services &amp; scope</Link>
    <Status loading={scope.loading} error={scope.error} retry={scope.retry} />
    {mismatch && <p role="alert" className={errorClass}>The returned scope does not match this offering’s exact predecessor. Reload before preparing a proposal.</p>}
    {!scope.loading && !scope.error && !mismatch && <HostingPanel offering={offering} task="scope" proposalMode initialScope={scope.data ?? undefined} onChanged={() => undefined} />}
  </div><ProviderSupport><ProviderPanel title="Versioned change"><p>Saving creates an immutable technical scope revision and an impact review. Existing allocations and adopted releases retain their prior scope until explicitly changed.</p>
    <Link className="provider-secondary mt-3" to={authorizationHref(offering.offeringId, 'impact')}>Review affected systems</Link></ProviderPanel>
    <ProviderPanel title="Explicit technical scope"><p>Choose Azure resources or a manually documented service relationship. Use exact recorded service and tenant references for SaaS; never invent Azure identifiers. Neither scope kind creates a live connector.</p></ProviderPanel>
  </ProviderSupport></div>;
}
