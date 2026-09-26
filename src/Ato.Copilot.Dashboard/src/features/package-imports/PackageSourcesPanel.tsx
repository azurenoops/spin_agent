import { useState } from 'react';
import AuthenticatedDownload from '../../components/AuthenticatedDownload';
import { Link } from '../workspaces/workspaceNavigation';
import { errorClass, Pager, secondaryButtonClass, Status, surfaceClass, useRemote } from '../workspace-operations/workspaceUi';
import { getPackageCandidates, packageArtifactUrl, packageImportHref } from './api';
import { PackageReceipts } from './PackageReceipts';
import type { PackageStatus } from './types';

export function PackageSourcesPanel() {
  return <div className="space-y-4">
    <p className="rounded border border-indigo-200 bg-indigo-50 p-3 text-sm text-indigo-900">
      Source packages are provider-owned evidence, not verified authorization decisions.
      Recording references or publishing capabilities does not grant a mission system an ATO.
    </p>
    <PackageReceipts showLinks renderDetails={item => <PackageReferenceSection item={item} />} />
    <Link className="inline-block text-sm font-semibold text-indigo-700 underline" to="/workspaces/csp/authorizations">View offering authorization records</Link>
  </div>;
}

function PackageReferenceSection({ item }: { item: PackageStatus }) {
  const [expanded, setExpanded] = useState(false);
  return <div className={`${surfaceClass} p-3`}>
    <button type="button" className={secondaryButtonClass} aria-expanded={expanded} onClick={() => setExpanded(value => !value)}>
      {expanded ? 'Hide' : 'Show'} reviewed references for {item.name}
    </button>
    {expanded && <ReviewedReferences packageId={item.packageId} revision={item.revision} />}
  </div>;
}

function ReviewedReferences({ packageId, revision }: { packageId: string; revision: number }) {
  const [page, setPage] = useState(1);
  const remote = useRemote(signal => getPackageCandidates(packageId, {
    page, pageSize: 25, type: 'AuthorizationReference', reviewState: 'Reviewed',
  }, signal), [packageId, revision, page]);
  return <section aria-label="Reviewed authorization references" className="mt-3 space-y-3">
    <h4 className="font-semibold">Reviewed source references</h4>
    <p className="text-sm text-gray-600">Human-reviewed source metadata, not verification of an authorization decision. Proposed and rejected references are not listed here.</p>
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && <>
      {!remote.data.items.length && <p className="text-sm text-gray-600">No reviewed authorization references on this page. Proposed references may still need review in the package.</p>}
      {remote.data.items.map(candidate => {
        const reference = candidate.authorizationReference;
        const query = new URLSearchParams({ type: 'AuthorizationReference', reviewState: 'Reviewed', page: String(page), candidate: candidate.candidateId });
        return <article key={candidate.candidateId} className="space-y-2 border-t border-gray-200 pt-3">
          {reference ? <>
            <p className="break-words font-medium">{reference.reference}</p>
            <p className="break-words text-sm">Issuing authority: {reference.issuer ?? 'Not stated in the reference'}</p>
            <p className="break-words text-xs text-gray-600">Issued: {reference.issuedAt ?? 'Not stated'} · Expires: {reference.expiresAt ?? 'Not stated'}</p>
          </> : <p role="alert" className={errorClass}>Reviewed reference metadata is unavailable. Open the source record to review its current state.</p>}
          {candidate.citations.map((citation, index) => <blockquote key={`${citation.artifactId}-${index}`} className="border-l-2 border-indigo-300 pl-3 text-sm">
            <p className="break-words whitespace-pre-wrap">{citation.quote}</p>
            <p className="break-all text-xs text-gray-500">{citation.archivePath} · {citation.locator}</p>
            <AuthenticatedDownload className="font-medium text-indigo-700 underline" url={packageArtifactUrl(packageId, citation.artifactId)} fileName={citation.archivePath}>Download cited source</AuthenticatedDownload>
          </blockquote>)}
          <Link className="inline-block text-sm font-semibold text-indigo-700 underline" to={`${packageImportHref(packageId)}?${query}`}>Review source authorization reference</Link>
        </article>;
      })}
      {remote.data.total > 25 && <Pager {...remote.data} onPage={setPage} />}
    </>}
  </section>;
}
