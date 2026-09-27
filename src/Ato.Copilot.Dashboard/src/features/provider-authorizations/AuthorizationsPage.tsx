import { useEffect, useState } from 'react';
import PageLayout from '../../components/layout/PageLayout';
import WorkspacePageHeader from '../../components/layout/WorkspacePageHeader';
import { Link, Navigate, useLocation, useNavigate } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import { errorClass, Pager, Status, surfaceClass, useRemote, warningClass } from '../workspace-operations/workspaceUi';
import { PackageDetail } from '../package-imports/PackageImportsPage';
import * as api from './api';
import { OfferingCreate, OfferingIntake } from './OfferingIntake';
import { BoundaryPage } from './BoundaryPage';
import { OfferingList } from './OfferingList';
import { FileFirstImport, PackagePreparation } from './FileFirstImport';
import { packageIsProcessing, stateLabel } from '../package-imports/PackageReceipts';
import { OfferingOverview } from './OfferingOverview';
import { ImpactPanel } from './ImpactPanel';
import { HostingSetupPage } from './HostingSetupPage';
import { FindingsPage } from './FindingsPage';
import { ProviderPanel, ProviderSupport } from './ProviderPresentation';
import { SourceReviewRedirect } from './SourceReviewRedirect';
import { DecisionDetailPage } from './DecisionDetailPage';
import { ScopeProposalPage } from './ScopeProposalPage';
import { ProviderRelationshipDetail } from './ProviderRelationshipDetail';
import type { Offering, PackageVersion } from './types';
import { ProviderMonitoringPanel } from './ProviderMonitoringPage';

const offeringSections: [string, string][] = [
  ['', 'Overview'], ['packages', 'Authorizations & sources'], ['inherited-coverage', 'Services & scope'],
  ['inherited-coverage?task=capabilities', 'Capabilities & responsibilities'],
  ['findings', 'Evidence & findings'], ['impact', 'Change impact'],
];

export function PackagesSection({ offering }: { offering: Offering }) {
  const [page, setPage] = useState(1);
  const [sourcePage, setSourcePage] = useState(1);
  const [authorizationPage, setAuthorizationPage] = useState(1);
  const [predecessor, setPredecessor] = useState<PackageVersion | undefined>();
  const [importOpen, setImportOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const versions = useRemote(signal => api.listPackageVersions(offering.offeringId, page, signal), [offering.offeringId, page, offering.revision]);
  const summary = useRemote(signal => api.getOfferingOverview(offering.offeringId, authorizationPage, sourcePage, signal),
    [offering.offeringId, offering.revision, authorizationPage, sourcePage]);
  return <div className="provider-grid"><section className="space-y-4">
    <Status loading={summary.loading} error={summary.error} retry={summary.retry} />
    {summary.data && <>
      <ProviderPanel title="Recorded authorization">
        {!summary.data.authorizations.total && <p>No external decision recorded here. This does not mean no authorization exists.</p>}
        {summary.data.authorizations.items.map(record => <article key={record.recordId} className="mb-4 space-y-2">
          <h3 className="font-semibold">{record.reference}</h3><p className="text-sm">{record.issuingAuthority || 'Issuer not stated'} · {stateLabel(record.metadataReviewState)}</p>
          <p className="text-xs">{record.scopeStatement}</p>
          <Link className="provider-secondary" to={api.authorizationHref(offering.offeringId, `decisions/${encodeURIComponent(record.recordId)}`)}>Review recorded authorization</Link>
        </article>)}
        <p className="mt-3 text-xs">Recorded source metadata is independent of capability publication and mission authorization.</p>
        {summary.data.authorizations.total > summary.data.authorizations.pageSize && <Pager {...summary.data.authorizations} onPage={setAuthorizationPage} />}
      </ProviderPanel>
      <ProviderPanel title="Source packages & documents">
        {!summary.data.packages.total ? <p>No source packages associated with this offering.</p> : <div className="provider-table-wrap">
          <table className="provider-table" aria-label="Source packages and documents"><thead><tr><th>Source</th><th>Analysis</th><th>Review</th><th><span className="sr-only">Actions</span></th></tr></thead>
            <tbody>{summary.data.packages.items.map(({ package: item, version, awaitingReview }) => <tr key={item.packageId}>
              <td>{item.name}<small>{version == null ? 'Version not recorded' : `Version ${version}`}</small></td>
              <td>{item.coverage.processed} of {item.coverage.total} files processed<small>{stateLabel(item.processingState)} · {item.coverage.excluded} excluded</small></td>
              <td>{awaitingReview} records awaiting review<small>Publication: {stateLabel(item.publicationState)}</small></td>
              <td><Link to={api.authorizationHref(offering.offeringId, `packages/${encodeURIComponent(item.packageId)}`)}>Review package</Link></td>
            </tr>)}</tbody>
          </table></div>}
        {summary.data.packages.total > summary.data.packages.pageSize && <Pager {...summary.data.packages} onPage={setSourcePage} />}
      </ProviderPanel>
    </>}
    <details className="provider-record-details"><summary>Retained version history</summary><ProviderPanel title="Retained version history">
    <Status loading={versions.loading} error={versions.error} retry={versions.retry} />
    {versions.data && <>{!versions.data.items.length && <p>No source packages retained. Add source material to begin the review.</p>}
      <ul className="space-y-3">{versions.data.items.map(item => <li key={item.packageVersionId} className="border-t border-slate-200 py-4">
      <Link className="font-semibold text-indigo-700 underline" to={api.authorizationHref(offering.offeringId, `packages/${item.packageId}`)}>Review package version {item.version}</Link>
      <details className="mt-2 text-xs text-slate-500"><summary className="cursor-pointer">Details</summary>
        <p className="break-all">Series {item.seriesId} · Boundary {item.boundaryRevisionId} · Manifest {item.manifestHash}</p>
      </details>
      <p className="text-xs text-slate-500">Retained {new Date(item.createdAt).toLocaleString()}</p>
      {!pending && <Link className="mr-4 mt-3 inline-block text-sm text-indigo-700 underline"
        to={api.changeImpactHref(offering.offeringId, { packageVersionId: item.packageVersionId, packageId: item.packageId,
          boundaryRevisionId: item.boundaryRevisionId })}>Review changes</Link>}
      <button type="button" disabled={pending} className="mt-3 text-sm text-indigo-700 underline" onClick={() => setPredecessor(item)}>Prepare successor to version {item.version}</button>
    </li>)}</ul><Pager {...versions.data} onPage={setPage} /></>}</ProviderPanel></details>
    <details className={`${surfaceClass} p-4`} open={importOpen || !!predecessor}
      onToggle={event => setImportOpen(event.currentTarget.open)}><summary className="cursor-pointer font-semibold" aria-disabled={pending}
        onClick={event => { if (pending) event.preventDefault(); }}>Import another source package</summary>
      {(importOpen || predecessor || pending) && <div className="mt-4"><OfferingIntake key={predecessor?.packageVersionId ?? 'new-series'} initialOfferingId={offering.offeringId} previousVersion={predecessor}
        onPendingChange={setPending} onReceived={() => { versions.retry(); summary.retry(); }} /></div>}
    </details>
  </section><ProviderSupport><ProviderPanel title="Keep the distinctions">
    <p><strong>Authorization:</strong> a recorded external decision.</p>
    <p className="mt-3"><strong>Source package:</strong> retained supporting documents.</p>
    <p className="mt-3"><strong>Service release:</strong> reviewed implementations customers can use.</p>
  </ProviderPanel><ProviderPanel title="Source privacy"><p>Source packages retain their protected content and exact version. Recording a reference or reviewing a claim does not publish a release or grant customer download access.</p></ProviderPanel></ProviderSupport></div>;
}

export function PackageAssociationRedirect({ packageId, initialOfferingId }: { packageId: string; initialOfferingId?: string }) {
  const location = useLocation();
  const remote = useRemote(signal => api.getAssociatedPackage(packageId, signal), [packageId]);
  useEffect(() => {
    if (!remote.loading && !remote.error && remote.data && !remote.data.association && packageIsProcessing(remote.data)) {
      const timer = window.setTimeout(remote.retry, 5000);
      return () => window.clearTimeout(timer);
    }
  }, [remote.loading, remote.error, remote.data, remote.retry]);
  if (remote.data?.association) {
    if (initialOfferingId && remote.data.association.offeringId !== initialOfferingId)
      return <p role="alert" className={errorClass}>This package is associated with a different offering. <Link className="underline" to={`${api.importHref}?packageId=${encodeURIComponent(packageId)}`}>Open its retained receipt</Link> to review the recorded association.</p>;
    return <Navigate replace to={`${api.authorizationHref(remote.data.association.offeringId, `packages/${packageId}`)}${location.search}${location.hash}`} />;
  }
  return <><Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && <PackagePreparation item={remote.data} initialOfferingId={initialOfferingId} />}
  </>;
}

function LinkedPackage({ offeringId, packageId, candidateId }: { offeringId: string; packageId: string; candidateId?: string }) {
  const remote = useRemote(signal => api.getAssociatedPackage(packageId, signal), [packageId, offeringId]);
  if (remote.loading || remote.error) return <Status loading={remote.loading} error={remote.error} retry={remote.retry} />;
  if (remote.data?.association?.offeringId !== offeringId) return <p role="alert" className={errorClass}>This package is not associated with the selected offering. Open its retained receipt from Authorizations to resolve the association.</p>;
  if (candidateId) return <SourceReviewRedirect offeringId={offeringId} packageId={packageId} candidateId={candidateId} />;
  return <PackageDetail key={packageId} packageId={packageId} offeringId={offeringId}
    packageVersionId={remote.data.association.packageVersionId} boundaryRevisionId={remote.data.association.boundaryRevisionId} />;
}

export function AuthorizationsPage() {
  const session = useWorkspaceSession();
  if (session?.target.kind !== 'csp' || !session.workspace.permissions.canAccessCsp) return <p role="alert" className={errorClass}>Provider authorization workspace access is required.</p>;
  return <AuthorizationRoutes />;
}

function AuthorizationRoutes() {
  const navigate = useNavigate();
  const location = useLocation();
  const segments = location.pathname.split('/').filter(Boolean);
  const offeringId = segments[1] === 'offerings' ? segments[2] : undefined;
  const offering = useRemote(signal => offeringId ? api.getOffering(offeringId, signal) : Promise.resolve(null), [offeringId, location.pathname]);
  const section = segments[3] ?? '';
  const packageId = new URLSearchParams(location.search).get('packageId');
  const impactQuery = new URLSearchParams(location.search);
  const capabilityId = impactQuery.get('capabilityId') ?? undefined;
  const packageVersionId = impactQuery.get('packageVersionId') ?? undefined;
  const boundaryRevisionId = impactQuery.get('boundaryRevisionId') ?? undefined;
  const reviewId = impactQuery.get('reviewId') ?? undefined;
  const ambiguousImpact = !!(capabilityId && packageVersionId) || !!(reviewId && (capabilityId || packageVersionId || boundaryRevisionId));
  const isCreating = !offeringId && segments[1] === 'create';
  const pageTitle = section === 'import' || segments[1] === 'import' ? 'Add source material'
    : section === 'packages' ? segments[4] ? 'Review package analysis' : 'Authorizations & sources'
      : section === 'findings' || section === 'evidence' ? 'Evidence & findings'
        : section === 'missions' ? 'Service relationship'
        : section === 'decisions' ? 'Recorded authorization'
        : section === 'inherited-coverage' ? segments[4] === 'propose' ? 'Propose a scope update' : impactQuery.get('task') === 'capabilities' ? 'Capabilities & responsibilities' : 'Services & scope'
          : section === 'boundary' ? 'Service boundary' : section === 'impact' ? 'Review change impact'
            : offeringId ? offering.data?.name ?? 'Service offering' : isCreating ? 'Create a service offering' : 'Service offerings';
  const nav = offeringId && <nav aria-label="Offering sections" className="provider-tabs">
    {offeringSections.map(([path, title]) => {
      const active = path.includes('?') ? section === 'inherited-coverage' && impactQuery.get('task') === 'capabilities'
        : path === 'inherited-coverage' ? (section === 'inherited-coverage' && impactQuery.get('task') !== 'capabilities') || section === 'boundary' || section === 'missions'
          : path === 'packages' ? section === 'packages' || section === 'import' || section === 'decisions'
            : path === 'findings' ? section === 'findings' || section === 'evidence' : section === path;
      return <Link key={path} aria-current={active ? 'page' : undefined} to={api.authorizationHref(offeringId, path)}>{title}</Link>;
    })}
  </nav>;
  return <PageLayout title="Service offerings">
    <div className="provider-workspace">
    {offeringId && <div className="mb-4 text-xs text-slate-500"><Link to={api.authorizationHref()}>Offerings</Link> / {offering.data?.name ?? 'Loading offering'} / {pageTitle}</div>}
    {section !== 'monitoring' && <WorkspacePageHeader eyebrow="Provider workspace" title={pageTitle}
      description={isCreating ? 'Start with the service identity. Add authorization sources and publish reviewed capabilities afterward.'
          : section === 'import' || segments[1] === 'import' ? 'Attach documents to this offering. Proposed records require review before anything reaches customers.'
            : section === 'packages' ? 'Review what the documents actually say before they support a published service release.'
              : offeringId ? 'Manage the source-backed service baseline that customers can apply to their systems.'
                : 'Keep each service, environment, and responsibility model clearly defined.'}
      actions={<>
        {!offeringId && !isCreating && <Link to="/authorizations/create" className="provider-primary">Create offering</Link>}
        {!isCreating && section !== 'import' && segments[1] !== 'import' && <Link className="provider-secondary"
          to={offeringId ? api.authorizationHref(offeringId, 'import') : api.importHref}>Add source material</Link>}
      </>} />}
    {nav}
    {offeringId ? <>
      <Status loading={offering.loading} error={offering.error} retry={offering.retry} />
      {offering.data && <div className="space-y-5">
        {section === 'import' && <Link className="inline-block text-sm text-indigo-700 underline dark:text-indigo-300" to={api.authorizationHref(offeringId)}>Back to offering</Link>}
        {section !== '' && section !== 'boundary' && section !== 'inherited-coverage' && section !== 'impact' && <p className="text-sm text-slate-500">{offering.data.lifecycle} offering · Revision {offering.data.revision} · {offering.data.environments.join(', ')}</p>}
        {section === 'monitoring' ? <ProviderMonitoringPanel offeringId={offeringId} />
          : section === '' ? <OfferingOverview key={offeringId} offering={offering.data} />
          : section === 'import' ? packageId
            ? <PackageAssociationRedirect packageId={packageId} initialOfferingId={offeringId} />
            : <FileFirstImport key={offeringId} offering={offering.data} />
          : section === 'inherited-coverage' ? segments[4] === 'propose'
            ? <ScopeProposalPage key={offeringId} offering={offering.data} /> : <HostingSetupPage key={offeringId} offering={offering.data} onChanged={offering.retry} />
          : section === 'decisions' && segments[4] ? <DecisionDetailPage key={`${offeringId}:${segments[4]}`} offering={offering.data} recordId={segments[4]} />
          : section === 'missions' && segments[4] ? <ProviderRelationshipDetail key={`${offeringId}:${segments[4]}`} offering={offering.data} assignmentId={segments[4]} />
          : section === 'boundary' ? <BoundaryPage key={offeringId} offering={offering.data} refresh={offering.retry} />
          : section === 'findings' || section === 'evidence' ? section === 'evidence' && !impactQuery.get('findingId')
            ? <p role="alert" className={errorClass}>Open evidence through its provider finding to retain offering ownership and access checks.</p>
            : <FindingsPage offering={offering.data} findingId={section === 'findings' ? segments[4] : impactQuery.get('findingId') ?? undefined}
              evidenceId={section === 'evidence' ? segments[4] : undefined} onChanged={offering.retry} />
          : section === 'impact' ? ambiguousImpact
            ? <p role="alert" className={errorClass}>This link combines different impact-review tasks. Return to the package, capability or saved review and use its Review changes action.</p>
            : <ImpactPanel key={`${offeringId}:${location.search}`} offering={offering.data} onChanged={offering.retry} initialReviewId={reviewId}
              source={packageVersionId ? { kind: 'Package', id: packageVersionId, boundaryRevisionId }
                : capabilityId ? { kind: 'Capability', id: capabilityId }
                  : boundaryRevisionId ? { kind: 'Boundary', id: boundaryRevisionId } : undefined}
              publicationHref={capabilityId ? `/workspaces/csp/security-capabilities/${encodeURIComponent(capabilityId)}?tab=review`
                : packageId ? api.authorizationHref(offeringId, `packages/${encodeURIComponent(packageId)}`) : undefined} />
          : section === 'packages' ? segments[4] ? <LinkedPackage offeringId={offeringId} packageId={segments[4]} candidateId={segments[5] === 'candidates' ? segments[6] : undefined} /> : <PackagesSection offering={offering.data} />
            : <p className={warningClass}>External decisions, hosting assignments and authorization impacts must be explicitly reviewed before linked publication. No authority is inferred from this offering.</p>}
      </div>}
    </> : segments[1] === 'import' ? packageId ? <PackageAssociationRedirect packageId={packageId} /> : <FileFirstImport />
      : segments[1] === 'create' ? <div className="provider-grid"><ProviderPanel title="Service identity"><OfferingCreate expanded onCreated={created => navigate(api.authorizationHref(created.offeringId))} /></ProviderPanel>
        <ProviderSupport><ProviderPanel title="Next steps"><ol className="list-decimal space-y-2 pl-4"><li>Add source material.</li><li>Review scope and responsibilities.</li><li>Publish a reviewed release.</li></ol></ProviderPanel>
          <ProviderPanel title="Existing record?"><p>Importing more sources updates an existing offering’s review workflow. It does not require creating another offering.</p><Link className="provider-secondary mt-3" to={api.authorizationHref()}>Back to offerings</Link></ProviderPanel></ProviderSupport></div> : <OfferingList />}
    </div>
  </PageLayout>;
}
