import { getPackageCandidates } from '../package-imports/api';
import { Link, Navigate } from '../workspaces/workspaceNavigation';
import { errorClass, Status, useRemote } from '../workspace-operations/workspaceUi';
import { authorizationHref } from './api';

export function SourceReviewRedirect({ offeringId, packageId, candidateId }: { offeringId: string; packageId: string; candidateId: string }) {
  const remote = useRemote(async signal => {
    // Resolve the candidate's current page before entering the canonical, revision-fenced reviewer.
    for (let page = 1; ; page++) {
      const result = await getPackageCandidates(packageId, { page, pageSize: 25 }, signal);
      if (result.items.some(item => item.candidateId === candidateId)) return page;
      if (!result.items.length || result.page * result.pageSize >= result.total) return null;
    }
  }, [offeringId, packageId, candidateId]);
  const href = authorizationHref(offeringId, `packages/${encodeURIComponent(packageId)}`);
  if (remote.data) return <Navigate replace to={`${href}?${new URLSearchParams({ page: String(remote.data), candidate: candidateId })}`} />;
  return <>
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {!remote.loading && !remote.error && <p role="alert" className={errorClass}>This source record is unavailable in the retained package. <Link className="underline" to={href}>Review the package</Link> without selecting a different record automatically.</p>}
  </>;
}
