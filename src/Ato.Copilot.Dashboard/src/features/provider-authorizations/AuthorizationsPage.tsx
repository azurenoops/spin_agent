import { useEffect, useState } from 'react';
import PageLayout from '../../components/layout/PageLayout';
import PageHero from '../../components/layout/PageHero';
import { Link, NavLink, Navigate, useLocation, useNavigate } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import { errorClass, Pager, Status, surfaceClass, useRemote, warningClass } from '../workspace-operations/workspaceUi';
import { PackageDetail } from '../package-imports/PackageImportsPage';
import * as api from './api';
import { OfferingCreate, OfferingIntake } from './OfferingIntake';
import { BoundaryPage } from './BoundaryPage';
import { OfferingList } from './OfferingList';
import { FileFirstImport, PackagePreparation } from './FileFirstImport';
import { packageIsProcessing } from '../package-imports/PackageReceipts';
import { OfferingOverview } from './OfferingOverview';
import { ImpactPanel } from './ImpactPanel';
import { HostingSetupPage } from './HostingSetupPage';
import type { Offering, PackageVersion } from './types';

const offeringSections = [
  ['', 'Offering overview'], ['boundary', 'Boundary'], ['inherited-coverage', 'Hosting and responsibilities'],
  ['packages', 'Packages and claims'], ['findings', 'Findings and evidence'], ['impact', 'Change impact'],
];

function PackagesSection({ offering }: { offering: Offering }) {
  const [page, setPage] = useState(1);
  const [predecessor, setPredecessor] = useState<PackageVersion | undefined>();
  const [pending, setPending] = useState(false);
  const versions = useRemote(signal => api.listPackageVersions(offering.offeringId, page, signal), [offering.offeringId, page, offering.revision]);
  return <section className="space-y-4"><h2 className="text-xl font-semibold">Source packages and versions</h2>
    <Status loading={versions.loading} error={versions.error} retry={versions.retry} />
    {versions.data && <><ul className="space-y-3">{versions.data.items.map(item => <li key={item.packageVersionId} className={`${surfaceClass} p-4`}>
      <Link className="font-semibold text-indigo-700 underline" to={api.authorizationHref(offering.offeringId, `packages/${item.packageId}`)}>Review package version {item.version}</Link>
      <details className="mt-2 text-xs text-slate-500"><summary className="cursor-pointer">Details</summary>
        <p className="break-all">Series {item.seriesId} · Boundary {item.boundaryRevisionId} · Manifest {item.manifestHash}</p>
      </details>
      <p className="text-xs text-slate-500">Retained {new Date(item.createdAt).toLocaleString()}</p>
      {!pending && <Link className="mr-4 mt-3 inline-block text-sm text-indigo-700 underline"
        to={api.changeImpactHref(offering.offeringId, { packageVersionId: item.packageVersionId, packageId: item.packageId,
          boundaryRevisionId: item.boundaryRevisionId })}>Review changes</Link>}
      <button type="button" disabled={pending} className="mt-3 text-sm text-indigo-700 underline" onClick={() => setPredecessor(item)}>Prepare successor to version {item.version}</button>
    </li>)}</ul><Pager {...versions.data} onPage={setPage} /></>}
    <details className={`${surfaceClass} p-4`} open={!!predecessor}><summary className="cursor-pointer font-semibold">Import another source package</summary>
      <div className="mt-4"><OfferingIntake key={predecessor?.packageVersionId ?? 'new-series'} initialOfferingId={offering.offeringId} previousVersion={predecessor}
        onPendingChange={setPending} onReceived={versions.retry} /></div>
    </details>
  </section>;
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

function LinkedPackage({ offeringId, packageId }: { offeringId: string; packageId: string }) {
  const remote = useRemote(signal => api.getAssociatedPackage(packageId, signal), [packageId, offeringId]);
  if (remote.loading || remote.error) return <Status loading={remote.loading} error={remote.error} retry={remote.retry} />;
  if (remote.data?.association?.offeringId !== offeringId) return <p role="alert" className={errorClass}>This package is not associated with the selected offering. Open its retained receipt from Authorizations to resolve the association.</p>;
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
  const isLanding = !offeringId && segments[1] !== 'import' && segments[1] !== 'create';
  const nav = offeringId && section !== 'import' && <nav aria-label="Offering sections" className="hidden space-y-2 p-4 lg:block">
    {offeringSections.map(([path, title]) =>
      <NavLink key={path} end={!path} className={({ isActive }) => `block rounded-lg px-3 py-2 text-sm ${isActive ? 'bg-indigo-100 font-semibold text-indigo-800' : 'text-slate-700 hover:bg-slate-100'}`}
        to={api.authorizationHref(offeringId, path)}>{title}</NavLink>)}
    <Link className="block px-3 py-2 text-sm text-indigo-700 underline" to={api.authorizationHref()}>All offerings</Link>
  </nav>;
  return <PageLayout title="Authorizations" leftPanel={nav || undefined}>
    <div className={isLanding ? '-m-6 min-h-[calc(100%+3rem)] bg-slate-50 p-6 dark:bg-gray-950' : undefined}>
    <PageHero eyebrow={offeringId && section === '' || section === 'boundary' || section === 'inherited-coverage' || section === 'impact' ? 'Provider workspace · Provider offering' : 'Provider workspace · External authorization records'} title={offering.data?.name ?? (segments[1] === 'create' ? 'Create offering' : segments[1] === 'import' ? 'Import authorization package' : 'Authorizations')}
      description={section === 'boundary' ? 'Review the package, confirm the boundary, then publish capabilities for Mission Owner use.'
        : section === 'inherited-coverage' ? 'Define what your offering provides, complete setup, and review mission-system associations.'
          : section === 'impact' ? 'Understand the consequences of a proposed change before publishing it.'
          : offeringId && section === '' ? 'Understand the recorded decision, source analysis, security capabilities and mission-system use of this offering.'
          : 'Manage your offerings, authorization boundaries, and source packages.'}
      showOrgName={false} actions={<>{!offeringId && segments[1] !== 'create' && <Link to="/authorizations/create" className="rounded-lg border border-white/50 px-4 py-2.5 text-sm font-semibold text-white hover:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white">Create offering</Link>}{!(offeringId && section === 'import') && <Link className={section === 'boundary' || section === 'inherited-coverage' || offeringId && section === '' ? 'rounded-lg border border-white/50 px-4 py-2.5 text-sm font-medium text-white hover:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white' : 'rounded-lg bg-white px-4 py-2.5 text-sm font-semibold text-indigo-700 shadow-sm hover:bg-indigo-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white focus-visible:ring-offset-2 focus-visible:ring-offset-indigo-700'}
        to={offeringId ? api.authorizationHref(offeringId, 'import') : api.importHref}>
        {offeringId ? 'Add package to this offering' : 'Import authorization package'}
      </Link>}</>} />
    {nav && offeringId && <label className="mb-5 block space-y-2 text-sm font-medium lg:hidden">
      <span>Offering section</span>
      <select aria-label="Offering section" className="w-full rounded-md border border-slate-300 bg-white px-3 py-2 dark:border-gray-700 dark:bg-gray-900"
        value={api.authorizationHref(offeringId, section)} onChange={event => navigate(event.target.value)}>
        {offeringSections.map(([path, title]) => <option key={path} value={api.authorizationHref(offeringId, path)}>{title}</option>)}
        <option value={api.authorizationHref()}>All offerings</option>
      </select>
    </label>}
    {offeringId ? <>
      <Status loading={offering.loading} error={offering.error} retry={offering.retry} />
      {offering.data && <div className="space-y-5">
        {section === 'import' && <Link className="inline-block text-sm text-indigo-700 underline dark:text-indigo-300" to={api.authorizationHref(offeringId)}>Back to offering</Link>}
        {section !== '' && section !== 'boundary' && section !== 'inherited-coverage' && section !== 'impact' && <p className="text-sm text-slate-500">{offering.data.lifecycle} offering · Revision {offering.data.revision} · {offering.data.environments.join(', ')}</p>}
        {section === '' ? <OfferingOverview key={offeringId} offering={offering.data} />
          : section === 'import' ? packageId
            ? <PackageAssociationRedirect packageId={packageId} initialOfferingId={offeringId} />
            : <FileFirstImport key={offeringId} offering={offering.data} />
          : section === 'inherited-coverage' ? <HostingSetupPage key={offeringId} offering={offering.data} onChanged={offering.retry} />
          : section === 'boundary' ? <BoundaryPage key={offeringId} offering={offering.data} refresh={offering.retry} />
          : section === 'impact' ? ambiguousImpact
            ? <p role="alert" className={errorClass}>This link combines different impact-review tasks. Return to the package, capability or saved review and use its Review changes action.</p>
            : <ImpactPanel key={`${offeringId}:${location.search}`} offering={offering.data} onChanged={offering.retry} initialReviewId={reviewId}
              source={packageVersionId ? { kind: 'Package', id: packageVersionId, boundaryRevisionId }
                : capabilityId ? { kind: 'Capability', id: capabilityId }
                  : boundaryRevisionId ? { kind: 'Boundary', id: boundaryRevisionId } : undefined}
              publicationHref={capabilityId ? `/workspaces/csp/security-capabilities/${encodeURIComponent(capabilityId)}?tab=review`
                : packageId ? api.authorizationHref(offeringId, `packages/${encodeURIComponent(packageId)}`) : undefined} />
          : section === 'packages' ? segments[4] ? <LinkedPackage offeringId={offeringId} packageId={segments[4]} /> : <PackagesSection offering={offering.data} />
            : <p className={warningClass}>External decisions, hosting assignments and authorization impacts must be explicitly reviewed before linked publication. No authority is inferred from this offering.</p>}
      </div>}
    </> : segments[1] === 'import' ? packageId ? <PackageAssociationRedirect packageId={packageId} /> : <FileFirstImport />
      : segments[1] === 'create' ? <section className="mx-auto w-full max-w-2xl space-y-4"><Link className="text-sm text-indigo-700 underline dark:text-indigo-300" to={api.authorizationHref()}>Back to offerings</Link><OfferingCreate expanded onCreated={created => navigate(api.authorizationHref(created.offeringId))} /></section> : <OfferingList />}
    </div>
  </PageLayout>;
}
