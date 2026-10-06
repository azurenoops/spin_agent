import type { ReactNode } from 'react';
import { getSystemEnvironments, type SystemProviderScope } from '../../api/systemEnvironments';
import { Link, useLocation } from '../workspaces/workspaceNavigation';
import { Status, useRemote } from '../workspace-operations/workspaceUi';

export default function ProviderOfferingWorkflowContext({ systemId, children }: {
  systemId: string; children: (item?: SystemProviderScope) => ReactNode;
}) {
  const location = useLocation();
  const query = new URLSearchParams(location.search);
  const offering = query.get('offeringId');
  const assignment = query.get('assignmentId');
  const release = query.get('hostingScopeRevisionId');
  if (!offering && !assignment && !release) return children();
  if (!offering || !assignment || !release) return <p role="alert">The captured offering context is incomplete. Return to Environment and reopen its review.</p>;
  return <CapturedContext key={`${systemId}:${offering}:${assignment}:${release}`} systemId={systemId}
    offering={offering} assignment={assignment} release={release}>{children}</CapturedContext>;
}

function CapturedContext({ systemId, offering, assignment, release, children }: {
  systemId: string; offering: string; assignment: string; release: string;
  children: (item: SystemProviderScope) => ReactNode;
}) {
  const data = useRemote(signal => getSystemEnvironments(systemId, signal), [systemId, offering, assignment, release]);
  const item = data.data?.systemId === systemId ? data.data.providerScopes?.find(row =>
    row.state === 'Active' && row.assignmentId === assignment && row.offeringId === offering && row.hostingScopeRevisionId === release) : undefined;
  return <>
    <Status loading={data.loading} error={data.error} retry={data.retry} />
    {!data.loading && !data.error && !item && <p role="alert">The captured offering or scope release changed or is unavailable. Return to Environment and review the current record; no source has been replaced.</p>}
    <Link className="block text-sm underline" to={`/systems/${encodeURIComponent(systemId)}/profile/EnvironmentAndDeployment`}>Return to offering review</Link>
    {item && <><section aria-label="Offering review context" className="my-3 space-y-1 rounded border p-3 text-sm">
      <p className="font-semibold">{item.offeringName} · {item.providerName || 'Provider name unavailable'}</p>
      <p>Captured scope release {item.hostingScopeRevision ?? 'revision unavailable'}. This context does not select a capability release, accept duties or filter the system baseline.</p>
    </section>{children(item)}</>}
  </>;
}
