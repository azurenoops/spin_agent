import { useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { errorClass, Status, useRemote } from '../workspace-operations/workspaceUi';
import { DecisionPanel } from './DecisionPanel';
import { ProviderPanel, ProviderSupport } from './ProviderPresentation';
import * as api from './api';
import type { Offering } from './types';

export function DecisionDetailPage({ offering: initialOffering, recordId }: { offering: Offering; recordId: string }) {
  const [offering, setOffering] = useState(initialOffering);
  const remote = useRemote(signal => api.getDecision(offering.offeringId, recordId, signal), [offering.offeringId, recordId]);
  return <div className="provider-grid"><div className="space-y-5">
    <Link className="provider-secondary" to={api.authorizationHref(offering.offeringId, 'packages')}>Back to authorizations &amp; sources</Link>
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {!remote.loading && !remote.error && !remote.data && <p role="alert" className={errorClass}>Decision unavailable in this offering. No substitute record or authorization is inferred.</p>}
    {remote.data && <ProviderPanel title="Source-backed authorization">
      <DecisionPanel offering={offering} initialDecision={remote.data} inheritedOnly={remote.data.recordKind !== 'ProviderDecision'}
        onChanged={() => undefined} onRefreshOffering={async () => setOffering(await api.getOffering(offering.offeringId))} />
    </ProviderPanel>}
  </div><ProviderSupport><ProviderPanel title="Independent decisions"><p>This external decision describes its recorded service boundary. It is not a published capability release or a mission authorization. Successor drafts preserve existing recorded snapshots.</p></ProviderPanel></ProviderSupport></div>;
}
