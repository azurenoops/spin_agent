import { useEffect, useRef, useState } from 'react';
import { Link, Navigate, useLocation, useNavigate } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import PageLayout from '../../components/layout/PageLayout';
import PageHero from '../../components/layout/PageHero';
import { Organizations, OrganizationDetailView } from './OrganizationPages';
import AddOrganization from './AddOrganizationPage';
import Provisioning from './OrganizationProvisioningPage';
import {
  inputClass, buttonClass, secondaryButtonClass, surfaceClass, warningClass, errorClass,
  message, useQueryState, useRemote, Status, Pager, moveTabFocus, ItemList,
} from './workspaceUi';
import { ArrowLeft, ChevronRight, FileCheck2, Plus, Search } from 'lucide-react';
import { heroAction, reviewStateLabel, StateBadge, SupportingComponents, workspaceCard, WorkspaceFootnote } from './CapabilityPresentation';
import { CapabilityChoices, SetupComponentChoices, SetupSystemPicker } from './SetupChoices';
import SetupDialog from './SetupDialog';
import OrganizationCatalogDialog from './OrganizationCatalogDialog';
import ProviderAddCapabilityDialog from './ProviderAddCapabilityDialog';
import { ImpactReviewSelection, impactReviewIds, impactSelectionError } from '../provider-authorizations/ImpactReviewSelection';
import { AuthorizationContextSummary } from '../provider-authorizations/AuthorizationContextSummary';
import { ProviderComponentPicker, ProviderArtifacts, ProviderOfferingSummary, ProviderVersions, PublishedVersion, WorkingVersion } from './ProviderPresentation';
import * as api from './api';
import type {
  CatalogQuery, OrganizationCapability, OrganizationCapabilityDetail,
  ProviderCatalogItem, ProviderSubscriber, PublicationPreview,
  SetupResult, WorkingRevision, SupportingComponent,
} from './types';

function CatalogFilters({ provider }: { provider: boolean }) {
  const { params, set } = useQueryState();
  const [search, setSearch] = useState(params.get('search') ?? '');
  useEffect(() => setSearch(params.get('search') ?? ''), [params]);
  return <div className="space-y-3">
    <form className="flex flex-wrap items-center justify-between gap-3" onSubmit={event => {
    event.preventDefault();
    set({ search: search.trim(), page: 1 });
  }}>
    <div className="inline-flex rounded-md border border-slate-200 bg-slate-50 p-1 dark:border-gray-700 dark:bg-gray-900">
      {(['capability', 'component'] as const).map(grouping => <button key={grouping} type="button"
        aria-pressed={(params.get('grouping') ?? 'capability') === grouping}
        className={`rounded px-3 py-2 text-xs font-medium ${(params.get('grouping') ?? 'capability') === grouping ? 'bg-indigo-100 text-indigo-700 dark:bg-indigo-950 dark:text-indigo-200' : 'text-slate-500'}`}
        onClick={() => set({ grouping, componentId: null, page: 1 })}>{grouping === 'capability' ? 'By capability' : 'By component'}</button>)}
    </div>
    <div className="flex items-end gap-2"><label className="grid gap-1 text-sm"><span className="sr-only">Search</span>
      <input className={inputClass} value={search} onChange={event => setSearch(event.target.value)}
        placeholder={provider ? 'Search provider catalog' : 'Search capability library'} />
    </label><button type="submit" className={secondaryButtonClass}>Search</button></div>
    </form>
    <details><summary className="cursor-pointer text-xs text-slate-500">Filter catalog</summary>
    <div className="mt-3 flex flex-wrap items-end gap-3">
    {!provider && <label className="grid gap-1 text-sm">Source
      <select className={inputClass} value={params.get('source') ?? ''}
        onChange={event => set({ source: event.target.value, page: 1 })}>
        <option value="">All sources</option><option value="local">Local</option><option value="provider">Provider</option>
      </select>
    </label>}
    <label className="grid gap-1 text-sm">{provider ? 'Lifecycle' : 'System'}
      {provider
        ? <select className={inputClass} value={params.get('lifecycle') ?? ''}
            onChange={event => set({ lifecycle: event.target.value, page: 1 })}>
            <option value="">All</option><option value="Published">Published</option><option value="Draft">Draft</option>
          </select>
        : <input className={inputClass} value={params.get('system') ?? ''}
            onChange={event => set({ system: event.target.value, page: 1 })} placeholder="System ID" />}
    </label>
    {provider && <label className="grid gap-1 text-sm">Review state
      <select className={inputClass} value={params.get('review') ?? ''}
        onChange={event => set({ review: event.target.value, page: 1 })}>
        <option value="">All review states</option><option value="NeedsReview">Needs review</option>
        <option value="Mapped">Mapped</option>
      </select>
    </label>}
    <label className="grid gap-1 text-sm">Sort
      <select className={inputClass} value={params.get('sort') ?? 'name'}
        onChange={event => set({ sort: event.target.value, page: 1 })}>
        <option value="name">Name</option><option value="updatedAt">Updated</option><option value="status">Status</option>
      </select>
    </label>
    <label className="grid gap-1 text-sm">Direction
      <select className={inputClass} value={params.get('direction') ?? 'asc'}
        onChange={event => set({ direction: event.target.value, page: 1 })}>
        <option value="asc">Ascending</option><option value="desc">Descending</option>
      </select>
    </label>
    </div></details>
  </div>;
}

function ProviderCatalog() {
  const { params, set } = useQueryState();
  const query: CatalogQuery = {
    page: Number(params.get('page') ?? 1), pageSize: 25,
    search: params.get('search') || undefined,
    grouping: (params.get('grouping') as CatalogQuery['grouping']) || 'capability',
    componentId: params.get('componentId') || undefined,
    lifecycle: params.get('lifecycle') || undefined,
    review: params.get('review') || undefined,
    sort: params.get('sort') || 'name',
    direction: (params.get('direction') as CatalogQuery['direction']) || 'asc',
  };
  const state = useRemote(signal => api.listProviderCatalog(query, signal), [
    query.page, query.search, query.grouping, query.lifecycle, query.review, query.sort, query.direction, query.componentId,
  ]);
  const [source, setSource] = useState<ProviderCatalogItem | null>(null);
  const [adding, setAdding] = useState(false);
  const canWrite = useWorkspaceSession()?.workspace.permissions.canAccessCsp === true;
  const sourceInvoker = useRef<HTMLButtonElement | null>(null);
  useEffect(() => {
    if (!source) sourceInvoker.current?.focus();
  }, [source]);
  return <PageLayout title="Security Capabilities"><PageHero eyebrow="Provider catalog · Provider offering" title="Capabilities you provide"
    description="Define once. Publish reviewed coverage. Keep mission owners informed."
    actions={canWrite && <div className="flex flex-wrap gap-2">
      <Link className={heroAction} to="/authorizations/import"><FileCheck2 size={16} aria-hidden="true" />Import authorization package</Link>
      <button type="button" className={heroAction} onClick={() => setAdding(true)}><Plus size={16} aria-hidden="true" />Add capability</button>
    </div>} />
    <div className="space-y-5">
      <ProviderOfferingSummary />
      <CatalogFilters provider />
      {query.componentId && <button type="button" className="text-xs text-indigo-700 underline" onClick={() => set({ componentId: null, page: 1 })}>Clear component filter</button>}
      <Status loading={state.loading} error={state.error} retry={state.retry} />
      {state.data?.aggregateState && state.data.aggregateState !== 'Available' &&
        <p role="alert" className={warningClass}>Partial results: {state.data.aggregateState}</p>}
      {!state.loading && !state.error && state.data?.items.length === 0 &&
        <p className={`${surfaceClass} p-6`}>{query.search ? 'No records match the current filters.' : 'No provider catalog records are available.'}</p>}
      {!!state.data?.items.length && <div className={`${surfaceClass} overflow-x-auto`}>
        <table className="min-w-full text-left text-sm">
          <thead className="bg-slate-50 text-xs font-medium text-slate-500 dark:bg-gray-800"><tr>
            <th className="px-5 py-3">{query.grouping === 'component' ? 'Component / delivered capabilities' : 'Capability / supporting components'}</th>
            <th className="px-5 py-3">Published version</th><th className="px-5 py-3">Working revision</th><th className="px-5 py-3">Subscriptions</th></tr></thead>
          <tbody>{state.data.items.map(item => <tr key={`${item.componentId}:${item.capabilityId ?? ''}`} className="border-t dark:border-gray-700">
            <td className="px-5 py-5"><Link className="font-semibold text-indigo-700 hover:underline dark:text-indigo-300"
              to={item.capabilityId ? `/security-capabilities/${item.capabilityId}` : `/security-capabilities?grouping=capability&componentId=${encodeURIComponent(item.componentId)}`}>{item.name}</Link>
              <p className="mt-1 text-xs text-slate-500">{item.supportingComponents?.length ? item.supportingComponents.map(component => component.name).join(' · ') : `${item.componentName} · ${item.componentType}`}</p>
              <button type="button" className="mt-2 text-xs text-slate-500 underline"
              onClick={event => {
                sourceInvoker.current = event.currentTarget;
                setSource(item);
              }} aria-label={`${item.name} source details`}>Source details</button>
            </td>
            <td className="px-5 py-5">{item.capabilityId ? <PublishedVersion item={item} /> : <StateBadge>{item.lifecycle} component</StateBadge>}</td>
            <td className="px-5 py-5">{item.capabilityId ? <WorkingVersion item={item} /> : <StateBadge>{reviewStateLabel(item.reviewState)}</StateBadge>}</td>
            <td className="px-5 py-5 text-xs text-slate-500">{item.distinctOrganizationCount != null ? `${item.distinctOrganizationCount} organizations / ` : ''}{item.distinctAdoptionCount == null ? 'Unavailable' : `${item.distinctAdoptionCount} systems`}</td>
          </tr>)}</tbody>
        </table>
      </div>}
      {state.data && <Pager {...state.data} onPage={page => set({ page })} />}
      <p className="flex flex-wrap justify-between gap-2 text-xs text-slate-500"><span>Availability is separate from subscription and accepted system coverage.</span><span>Provider-owned source records</span></p>
    </div>
    <WorkspaceFootnote />
    {adding && <ProviderAddCapabilityDialog onClose={() => setAdding(false)} />}
    {source && <aside role="complementary" aria-label="Source details"
      onKeyDown={event => {
        if (event.key === 'Escape') {
          event.preventDefault();
          setSource(null);
        }
      }}
      className="fixed inset-y-0 right-0 z-50 w-full max-w-md overflow-y-auto border-l bg-white p-6 text-gray-900 shadow-xl dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100">
      <button type="button" autoFocus className="float-right rounded px-2 py-1 text-gray-700 hover:bg-gray-100 dark:text-gray-200 dark:hover:bg-gray-800"
        onClick={() => setSource(null)} aria-label="Close source details">×</button>
      <h2 className="text-xl font-semibold">{source.name}</h2>
      <dl className="mt-5 grid grid-cols-[auto_1fr] gap-3 text-sm">
        <dt>Format</dt><dd>{source.sourceFormat}</dd><dt>Reference</dt><dd>{source.sourceReference ?? 'Not supplied'}</dd>
        <dt>Working revision</dt><dd>{source.workingRevision ?? 'None'}</dd>
        <dt>Released revision</dt><dd>{source.releasedRevision ?? 'None'}</dd>
      </dl>
      {source.capabilityId && <Link className="mt-6 inline-block text-indigo-700 underline dark:text-indigo-300"
        to={`/security-capabilities/${source.capabilityId}`}>Open capability</Link>}
    </aside>}
  </PageLayout>;
}

function parseList(value: string) {
  return value.split(/[\n,]/).map(item => item.trim()).filter(Boolean);
}

function ProviderCapability({ capabilityId }: { capabilityId: string }) {
  const { params, set } = useQueryState();
  const tab = params.get('tab') ?? 'implementation';
  const catalog = useRemote(signal => api.getProviderCapability(capabilityId, signal), [capabilityId]);
  const working = useRemote(async signal => {
    try {
      return await api.getWorkingRevision(capabilityId, signal);
    } catch (reason) {
      if (reason instanceof api.WorkspaceOperationError
        && reason.status === 404 && reason.code === 'WORKING_REVISION_NOT_FOUND') return null;
      throw reason;
    }
  }, [capabilityId]);
  const item = catalog.data?.capability ?? null;
  const subscriberPage = Number(params.get('subscriberPage') ?? 1);
  const subscribers = useRemote(signal => api.listProviderSubscribers(capabilityId, subscriberPage, signal), [capabilityId, subscriberPage]);
  const [linking, setLinking] = useState(false);
  const [linkedComponents, setLinkedComponents] = useState<ProviderCatalogItem[]>([]);
  const [evidenceReviewed, setEvidenceReviewed] = useState(false);
  const [dutiesReviewed, setDutiesReviewed] = useState(false);
  const [classification, setClassification] = useState('');
  const [serviceCategory, setServiceCategory] = useState('');
  const [contributors, setContributors] = useState('');
  const [duties, setDuties] = useState('');
  const [authorizationImpactIds, setAuthorizationImpactIds] = useState('');
  const [hydratedCapabilityId, setHydratedCapabilityId] = useState<string | null>(null);
  const [saved, setSaved] = useState<WorkingRevision | null>(null);
  const [approved, setApproved] = useState<WorkingRevision | null>(null);
  const [preview, setPreview] = useState<PublicationPreview | null>(null);
  const [result, setResult] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<{
    editing: WorkingRevision | null;
    latest: WorkingRevision | null;
  } | null>(null);
  const [busy, setBusy] = useState(false);
  const publicationKey = useRef(crypto.randomUUID());
  const mutationGeneration = useRef(0);
  const mutationController = useRef<AbortController | null>(null);
  const canWrite = useWorkspaceSession()?.workspace.permissions.canAccessCsp === true;
  const revisionReady = !!item && !catalog.loading && !catalog.error && !working.loading && !working.error;
  const firstRevision = revisionReady && !working.data && !saved && !conflict;

  useEffect(() => {
    mutationGeneration.current += 1;
    mutationController.current?.abort();
    setClassification('');
    setServiceCategory('');
    setContributors('');
    setDuties('');
    setAuthorizationImpactIds('');
    setSaved(null);
    setApproved(null);
    setPreview(null);
    setResult(null);
    setError(null);
    setConflict(null);
    setBusy(false);
    setHydratedCapabilityId(null);
    setLinking(false); setLinkedComponents([]); setEvidenceReviewed(false); setDutiesReviewed(false);
    return () => mutationController.current?.abort();
  }, [capabilityId]);

  useEffect(() => {
    if (conflict || !working.data || working.data.capabilityId.toLowerCase() !== capabilityId.toLowerCase()
      || hydratedCapabilityId === capabilityId) return;
    setClassification(working.data.classification);
    setServiceCategory(working.data.serviceCategory);
    setContributors(working.data.contributors.join('\n'));
    setDuties(Object.entries(working.data.controlDuties).map(([control, duty]) => `${control}: ${duty}`).join('\n'));
    setSaved(working.data);
    setApproved(working.data.approvalState === 'Approved' ? working.data : null);
    setHydratedCapabilityId(capabilityId);
  }, [working.data, capabilityId, hydratedCapabilityId, conflict]);

  useEffect(() => { setEvidenceReviewed(false); setDutiesReviewed(false); }, [preview?.previewId]);
  const hasUnsavedChanges = saved ? (classification !== saved.classification || serviceCategory !== saved.serviceCategory
    || contributors !== saved.contributors.join('\n')
    || duties !== Object.entries(saved.controlDuties).map(([control, duty]) => `${control}: ${duty}`).join('\n'))
    : !!(classification || serviceCategory || contributors || duties);

  useEffect(() => {
    if (!conflict || !working.data || working.data.capabilityId !== capabilityId
      || working.data.revision === conflict.editing?.revision
      || working.data.revision === conflict.latest?.revision) return;
    setConflict(current => current ? { ...current, latest: working.data } : null);
  }, [working.data, capabilityId, conflict]);

  const beginMutation = () => {
    mutationController.current?.abort();
    const controller = new AbortController();
    mutationController.current = controller;
    return {
      capabilityId,
      generation: mutationGeneration.current,
      controller,
    };
  };
  const mutationIsCurrent = (request: ReturnType<typeof beginMutation>) =>
    !request.controller.signal.aborted
    && request.capabilityId === capabilityId
    && request.generation === mutationGeneration.current
    && mutationController.current === request.controller;
  const loadRevision = (revision: WorkingRevision) => {
    setClassification(revision.classification);
    setServiceCategory(revision.serviceCategory);
    setContributors(revision.contributors.join('\n'));
    setDuties(Object.entries(revision.controlDuties)
      .map(([control, duty]) => `${control}: ${duty}`).join('\n'));
    setSaved(revision);
    setHydratedCapabilityId(capabilityId);
    setApproved(revision.approvalState === 'Approved' ? revision : null);
    setPreview(null);
    setConflict(null);
    setError(null);
  };

  const save = async () => {
    if (conflict || !revisionReady || (!saved && !firstRevision)) return;
    const dutyMap = Object.fromEntries(parseList(duties).map(line => {
      const [control, ...rest] = line.split(':');
      return [(control ?? '').trim(), rest.join(':').trim()];
    }).filter(([control, duty]) => control && duty));
    if (!classification.trim() || !serviceCategory.trim()) {
      setError('Classification and service category are required.');
      return;
    }
    const request = beginMutation();
    setBusy(true); setError(null);
    try {
      const next = await api.saveWorkingRevision(capabilityId, {
        expectedRevision: saved?.revision ?? 1,
        classification: classification.trim(), serviceCategory: serviceCategory.trim(),
        contributors: parseList(contributors), controlDuties: dutyMap,
      }, request.controller.signal);
      if (!mutationIsCurrent(request)) return;
      setSaved(next);
      setApproved(null);
      setPreview(null);
      publicationKey.current = crypto.randomUUID();
    } catch (reason) {
      if (!mutationIsCurrent(request)) return;
      const isConflict = (reason as { status?: number }).status === 409;
      setError(`${message(reason)}${isConflict ? ' Your edits were preserved and remain bound to their original revision until you explicitly reload and reconcile.' : ''}`);
      if (isConflict) {
        setConflict({ editing: saved, latest: null });
        working.retry();
      }
    } finally {
      if (mutationIsCurrent(request)) setBusy(false);
    }
  };

  const generatePreview = async () => {
    if (!saved || hasUnsavedChanges) return;
    const invalidSelection = impactSelectionError(authorizationImpactIds);
    if (invalidSelection) { setError(invalidSelection); return; }
    const request = beginMutation();
    setBusy(true); setError(null); setResult(null);
    try {
      const ids = impactReviewIds(authorizationImpactIds);
      const next = ids.length
        ? await api.generatePublicationPreview(capabilityId, saved.revision, request.controller.signal, ids)
        : await api.generatePublicationPreview(capabilityId, saved.revision, request.controller.signal);
      if (!mutationIsCurrent(request)) return;
      setPreview(next);
      if (next.isStale || new Date(next.expiresAt).getTime() <= Date.now()) {
        setError('The publication preview is stale or expired. Regenerate it before approval.');
      }
    } catch (reason) {
      if (!mutationIsCurrent(request)) return;
      setPreview(null);
      setError(message(reason));
    } finally {
      if (mutationIsCurrent(request)) setBusy(false);
    }
  };

  const previewCurrent = !!preview && !preview.isStale
    && new Date(preview.expiresAt).getTime() > Date.now()
    && preview.revision === saved?.revision
    && preview.workingSnapshotHash === saved.snapshotHash;

  const approve = async () => {
    if (!saved || !preview || !previewCurrent || !evidenceReviewed || !dutiesReviewed || hasUnsavedChanges) return;
    const request = beginMutation();
    setBusy(true); setError(null);
    try {
      const next = await api.approveWorkingRevision(
        capabilityId, saved.revision, preview.previewId, preview.previewHash,
        request.controller.signal,
      );
      if (!mutationIsCurrent(request)) return;
      setApproved(next);
    } catch (reason) {
      if (!mutationIsCurrent(request)) return;
      if ([409, 410].includes((reason as { status?: number }).status ?? 0)) setPreview(null);
      setError(`${message(reason)} Regenerate the publication preview and review the current impact.`);
    }
    finally {
      if (mutationIsCurrent(request)) setBusy(false);
    }
  };

  const publish = async () => {
    if (!saved || !preview || !previewCurrent || hasUnsavedChanges || !evidenceReviewed || !dutiesReviewed
      || approved?.approvedRevision !== saved.revision
      || approved.approvedPreviewId !== preview.previewId
      || approved.approvedPreviewHash !== preview.previewHash) return;
    const request = beginMutation();
    setBusy(true); setError(null);
    try {
      const release = await api.publishWorkingRevision(capabilityId, {
        revision: saved.revision, approvedRevision: approved.approvedRevision,
        previewId: preview.previewId, previewHash: preview.previewHash,
        idempotencyKey: publicationKey.current,
      }, request.controller.signal);
      if (!mutationIsCurrent(request)) return;
      setResult(release.existing
        ? `Release ${release.revision} was already published.`
        : `Release ${release.revision} published with ${release.impactCount} durable customer impacts.`);
      catalog.retry();
      subscribers.retry();
    } catch (reason) {
      if (!mutationIsCurrent(request)) return;
      if ([409, 410].includes((reason as { status?: number }).status ?? 0)) {
        setPreview(null);
        setApproved(null);
      }
      setError(`${message(reason)}${[409, 410].includes((reason as { status?: number }).status ?? 0)
        ? ' The approved preview is no longer current; regenerate it.' : ''}`);
    }
    finally {
      if (mutationIsCurrent(request)) setBusy(false);
    }
  };
  const primaryId = item?.componentId.toLowerCase();
  const contributorIds = parseList(contributors).map(id => id.toLowerCase());
  const unresolvedContributors = catalog.data?.unresolvedContributorIds.filter(id => contributorIds.includes(id.toLowerCase())) ?? [];
  const deliveryComponents = [
    ...(catalog.data?.supportingComponents ?? []).filter(component => component.id.toLowerCase() === primaryId || contributorIds.includes(component.id.toLowerCase())),
    ...linkedComponents.filter(component => contributorIds.includes(component.componentId.toLowerCase())
      && !catalog.data?.supportingComponents.some(existing => existing.id.toLowerCase() === component.componentId.toLowerCase()))
      .map(component => ({ id: component.componentId, name: component.name, componentType: component.componentType, source: 'provider', description: component.description })),
  ];
  const editor = <section className="mt-4 grid gap-4 text-sm">
    <label className="grid gap-1">Classification<input className={inputClass} value={classification}
      onChange={event => setClassification(event.target.value)} /></label>
    <label className="grid gap-1">Service category<input className={inputClass} value={serviceCategory}
      onChange={event => setServiceCategory(event.target.value)} /></label>
    <label className="grid gap-1">Contributors (one identifier per line)<textarea className={inputClass}
      value={contributors} onChange={event => setContributors(event.target.value)} /></label>
    <label className="grid gap-1">Control duties (CONTROL: DUTY)<textarea className={inputClass}
      value={duties} onChange={event => setDuties(event.target.value)} /></label>
    <p className="text-xs text-slate-500">Duty values: Provider, Shared, or Customer. Changes remain in the working revision until reviewed and published.</p>
    <button type="button" className={buttonClass}
      disabled={!canWrite || busy || !!conflict || !revisionReady || (!saved && !firstRevision) || !classification.trim() || !serviceCategory.trim()}
      onClick={() => void save()}>
      Save working revision</button>
  </section>;
  return <PageLayout title="Capability authoring"><PageHero eyebrow="Provider catalog · Provider offering"
    title={tab === 'review' ? 'Review & publish revision' : item?.name ?? 'Capability authoring'}
    description={tab === 'review' ? 'See what changed and who needs to review it before making a revision available.' : 'Provider components, source evidence and customer obligations in one place.'}
    actions={tab !== 'review' && <button className={heroAction} type="button" onClick={() => set({ tab: 'review' })}>Review publication</button>} />
    <div className="space-y-5">
      <Link className="inline-flex items-center gap-2 text-sm text-indigo-700 dark:text-indigo-300"
        to={tab === 'review' ? `/security-capabilities/${capabilityId}` : '/security-capabilities'}><ArrowLeft size={15} aria-hidden="true" />{tab === 'review' ? 'Capability details' : 'Provider catalog'}</Link>
      <div role="tablist" aria-label="Capability authoring sections" className="flex flex-wrap gap-5 border-b border-slate-200 dark:border-gray-700"
        onKeyDown={moveTabFocus}>
        {[['implementation', 'Implementation'], ['responsibilities', 'Coverage & duties'], ['subscribers', 'Subscribers'], ['review', 'Review and publish']].map(([value, label]) =>
          <button key={value} role="tab" aria-selected={tab === value} className={`border-b-2 pb-3 text-sm ${tab === value ? 'border-indigo-500 font-semibold text-indigo-700 dark:text-indigo-300' : 'border-transparent text-slate-500'}`}
            onClick={() => set({ tab: value })}>{label}</button>)}
      </div>
      <Status loading={catalog.loading || working.loading} error={catalog.error ?? working.error}
        retry={() => { catalog.retry(); working.retry(); }} />
      {firstRevision && <p role="status" className={warningClass}>No working revision yet. Enter classification and service category to save the first revision.</p>}
      {error && <p role="alert" className={errorClass}>{error}</p>}
      {conflict && <section role="region" aria-label="Revision conflict comparison"
        className={`${warningClass} space-y-3 p-4`}>
        <h2 className="font-semibold">Reconciliation required</h2>
        <p>Editing revision {conflict.editing?.revision ?? 'unsaved'} · Latest revision {conflict.latest?.revision ?? 'loading'}</p>
        <p>Your form has not been rebound to the latest concurrency token.</p>
        <button type="button" className={secondaryButtonClass} disabled={!conflict.latest}
          onClick={() => conflict.latest && loadRevision(conflict.latest)}>Reload latest revision</button>
      </section>}
      {result && <p role="status" className="rounded border border-green-300 bg-green-50 p-3 text-green-800 dark:border-green-700 dark:bg-green-950 dark:text-green-100">{result}</p>}
      {hasUnsavedChanges && <p role="status" className={warningClass}>Unsaved working changes. Save the working revision before generating or approving publication.</p>}
      {(tab === 'implementation' || tab === 'responsibilities') && <div className="grid gap-5 lg:grid-cols-[minmax(0,1.65fr)_minmax(0,1fr)]">
        <div className="min-w-0 space-y-5">
          <section className={workspaceCard}>
            <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200 pb-4 dark:border-gray-700">
              <h2 className="text-lg font-semibold">Components that deliver this capability</h2>
              {saved && <StateBadge tone="indigo">Working revision v{saved.revision}</StateBadge>}
            </div>
            <SupportingComponents items={deliveryComponents} />
            {!deliveryComponents.length && <p className="py-4 text-sm text-slate-500">No delivery components recorded.</p>}
            {!!unresolvedContributors.length && <p className="py-3 text-xs text-amber-800 dark:text-amber-200">Unresolved contributor references: {unresolvedContributors.join(', ')}. Review their source identities before publication.</p>}
            <button type="button" className="mt-3 text-sm text-indigo-700 dark:text-indigo-300" disabled={!canWrite || busy}
              aria-expanded={linking} onClick={() => setLinking(value => !value)}>Link another existing component</button>
            {linking && <div className="mt-4"><ProviderComponentPicker selected={parseList(contributors)} onSelect={component => {
              setContributors(current => {
                const ids = parseList(current);
                return (ids.some(id => id.toLowerCase() === component.componentId.toLowerCase())
                  ? ids.filter(id => id.toLowerCase() !== component.componentId.toLowerCase()) : [...ids, component.componentId]).join('\n');
              });
              setLinkedComponents(current => current.some(item => item.componentId === component.componentId) ? current : [...current, component]);
            }} /><p className="mt-3 text-xs text-slate-500">Selections are staged. Save the working revision to persist them.</p></div>}
          </section>
          <section className={workspaceCard}><h2 className="mb-3 font-semibold">Provider implementation narrative</h2>
            <p className="whitespace-pre-wrap text-sm leading-6 text-slate-600 dark:text-gray-300">{catalog.data?.implementationNarrative ?? 'No provider implementation narrative recorded.'}</p>
            <Link to="/narrative-library" className="mt-3 inline-block text-xs text-indigo-700 underline dark:text-indigo-300">Open provider narrative library</Link>
          </section>
          <details className={workspaceCard} open={tab === 'responsibilities' || linking || firstRevision}><summary className="cursor-pointer font-semibold">Edit working revision</summary>{editor}</details>
        </div>
        <aside className="min-w-0 space-y-5">
          <section className={workspaceCard}><h2 className="mb-5 text-lg font-semibold">Publication readiness</h2>
            <ProviderVersions item={item} working={saved} />
            <button type="button" className={`${buttonClass} mt-5 w-full`} disabled={!saved || busy || hasUnsavedChanges} onClick={() => set({ tab: 'review' })}>Review publication impact</button>
          </section>
          <section className={workspaceCard}><h2 className="mb-2 font-semibold">Source evidence</h2>
            <p className="mb-4 text-sm text-slate-500">No separately identified source evidence recorded.</p>
            <h3 className="border-t border-slate-200 pt-4 text-sm font-semibold dark:border-gray-700">Source package provenance</h3>
            <ProviderArtifacts items={catalog.data?.sourceArtifacts ?? []} />
            <p className="mt-4 text-xs text-slate-500">Package references remain traceable for consumers; they do not by themselves establish verified evidence.</p>
          </section>
        </aside>
      </div>}
      {tab === 'subscribers' && <section className={workspaceCard}><h2 className="text-lg font-semibold">Authorized subscriber systems</h2>
        <Status loading={subscribers.loading} error={subscribers.error} retry={subscribers.retry} />
        {!subscribers.loading && subscribers.data?.items.length === 0 && <p>No authorized subscribers.</p>}
        <ul className="divide-y">{subscribers.data?.items.map((subscriber: ProviderSubscriber) =>
          <li key={subscriber.subscriptionId} className="py-3">{subscriber.organizationName} · {subscriber.systemName}
            <span className="ml-2 text-gray-600 dark:text-gray-300">Revision {subscriber.sourceRevision ?? 'unavailable'} · {subscriber.reviewState}</span></li>)}</ul>
        {subscribers.data && <Pager {...subscribers.data} onPage={page => set({ subscriberPage: page })} />}
      </section>}
      {tab === 'review' && <section className="grid gap-5 lg:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]">
        <div className="min-w-0 space-y-5">
        {!preview && <section className={workspaceCard}>
        <div className="mb-4 flex flex-wrap items-center justify-between gap-2"><h2 className="text-lg font-semibold">{item?.name ?? 'Capability'} · {item?.releasedRevision ? `v${item.releasedRevision}` : 'Unpublished'} → v{saved?.revision ?? '—'}</h2>
          <StateBadge tone="amber">{approved?.approvedRevision === saved?.revision && approved ? 'Approved' : 'Awaiting approval'}</StateBadge></div>
        <p className="mb-4 text-sm text-slate-500">Review and publish exact revision. Working changes remain separate from the current subscriber release.</p>
        <button type="button" className={secondaryButtonClass} disabled={!saved || busy || !canWrite || hasUnsavedChanges}
          onClick={() => void generatePreview()}>
          {preview ? 'Regenerate publication preview' : 'Generate publication preview'}
        </button>
        </section>}
        {preview && <PublicationImpact preview={preview} current={previewCurrent} subscribers={subscribers.data?.items ?? []}
          components={deliveryComponents} title={`${item?.name ?? 'Capability'} · ${item?.releasedRevision ? `v${item.releasedRevision}` : 'Unpublished'} → v${preview.revision}`}
          approved={approved?.approvedPreviewId === preview.previewId && approved?.approvedPreviewHash === preview.previewHash}
          regenerate={() => void generatePreview()} disabled={busy || !canWrite || hasUnsavedChanges} />}
        </div>
        <aside className="min-w-0 space-y-5">
        <section className={workspaceCard}><h2 className="mb-5 text-lg font-semibold">Publication gate</h2>
        <div className="mb-5"><ImpactReviewSelection capabilityId={capabilityId} value={authorizationImpactIds} disabled={busy || !canWrite || hasUnsavedChanges}
          onChange={value => { setAuthorizationImpactIds(value); setPreview(null); setApproved(null); setEvidenceReviewed(false); setDutiesReviewed(false); }} /></div>
        <div className="mb-4 space-y-3 text-sm">
          <label className="flex items-start gap-2"><input type="checkbox" className="mt-1 accent-indigo-600" checked={evidenceReviewed} disabled={!previewCurrent || busy || hasUnsavedChanges}
            onChange={event => setEvidenceReviewed(event.target.checked)} />Source evidence and coverage reviewed</label>
          <label className="flex items-start gap-2"><input type="checkbox" className="mt-1 accent-indigo-600" checked={dutiesReviewed} disabled={!previewCurrent || busy || hasUnsavedChanges}
            onChange={event => setDutiesReviewed(event.target.checked)} />Provider and customer duties reviewed</label>
        </div>
        <button type="button" className={`${secondaryButtonClass} w-full`}
          disabled={!previewCurrent || !evidenceReviewed || !dutiesReviewed || hasUnsavedChanges || busy || !canWrite}
          onClick={() => void approve()}>Approve exact preview</button>
        <p className="mt-2 text-xs text-slate-500">Review both checks before approval. Approval is bound to this exact preview.</p>
        <section className="my-5 rounded-md border border-slate-200 bg-slate-50 p-4 dark:border-gray-700 dark:bg-gray-950">
          <h3 className="mb-2 text-sm font-semibold">Customer notification preview</h3>
          <p className="text-sm leading-6">{preview ? `A revised ${item?.name ?? 'provider'} capability is available for review. ${preview.dutyChanges.length ? `Review changed duties for ${preview.dutyChanges.map(change => change.key).join(', ')}.` : 'Review the source and contributor changes.'}` : 'Generate the publication preview to see the projected customer review work.'}</p>
          {preview && <p className="mt-3 text-xs text-slate-500">{preview.notifications.recipientCount} recipients · {preview.notifications.distinctOrganizations} organizations</p>}
          <p className="mt-2 text-xs text-slate-500">Projected review work, not a sent message.</p>
          <div className="mt-3"><StateBadge tone="indigo">Needs review</StateBadge></div>
        </section>
          <button type="button" className={`${buttonClass} w-full`}
            disabled={!previewCurrent || approved?.approvedRevision !== saved?.revision
              || approved.approvedPreviewId !== preview?.previewId
              || approved.approvedPreviewHash !== preview?.previewHash || !evidenceReviewed || !dutiesReviewed || hasUnsavedChanges || busy || !canWrite}
            onClick={() => void publish()}>Publish release</button>
        </section>
        <p className="rounded-lg border border-indigo-100 bg-indigo-50 p-4 text-sm leading-6 text-indigo-900 dark:border-indigo-900 dark:bg-indigo-950 dark:text-indigo-200">Publication creates review work. It does not overwrite approved customer narratives, accept customer responsibilities, or change system ATO decisions.</p>
        </aside>
      </section>}
    </div>
    <WorkspaceFootnote />
  </PageLayout>;
}

function PublicationImpact({ preview, current, subscribers, components, title, approved, regenerate, disabled }: {
  preview: PublicationPreview; current: boolean; subscribers: ProviderSubscriber[]; components: SupportingComponent[];
  title: string; approved: boolean; regenerate: () => void; disabled: boolean;
}) {
  const releaseSummary = [
    ...preview.dutyChanges.map(change => `${change.key}: ${change.before ?? 'None'} → ${change.after ?? 'None'}`),
    ...preview.referenceChanges.map(change => `${change.changeKind}: ${change.value}`),
    ...preview.contributorChanges.map(change => `${change.changeKind}: ${components.find(component => component.id === change.value)?.name ?? change.value}`),
  ].join('\n') || 'No source, contributor or control duty changes.';
  return <><article className={`${workspaceCard} space-y-5 text-sm`} aria-label="Publication impact preview">
    <header className="flex flex-wrap items-center justify-between gap-2"><h2 className="text-lg font-semibold">{title}</h2>
      <StateBadge tone={approved ? 'green' : 'amber'}>{approved ? 'Approved' : 'Awaiting approval'}</StateBadge></header>
    {!current && <p role="alert" className="text-red-700 dark:text-red-300">This preview is stale or expired and cannot be approved.</p>}
    <AuthorizationContextSummary contextSnapshotHash={preview.contextSnapshotHash} impactReviewIds={preview.impactReviewIds} />
    <section className="border-l-4 border-indigo-300 pl-3"><h3 className="font-semibold">Updated customer duties</h3>
      {preview.dutyChanges.length
        ? <ul className="mt-1 text-slate-600 dark:text-gray-300">{preview.dutyChanges.map(change => <li key={change.key}>{change.key}: {change.before ?? 'None'} → {change.after ?? 'None'}</li>)}</ul>
        : <p className="mt-1 text-slate-500">No control duty changes.</p>}
    </section>
    <section className="border-l-4 border-indigo-300 pl-3"><h3 className="font-semibold">Updated source references</h3>
      {preview.referenceChanges.length
        ? <ul className="mt-1 break-words text-slate-600 dark:text-gray-300">{preview.referenceChanges.map(change =>
          <li key={`${change.changeKind}:${change.value}`}>{change.changeKind}: {change.value}</li>)}</ul>
        : <p className="mt-1 text-slate-500">No source reference changes.</p>}
    </section>
    {!!preview.contributorChanges.length && <section className="border-l-4 border-indigo-300 pl-3"><h3 className="font-semibold">Contributor changes</h3>
      <ul className="mt-1 break-words text-slate-600 dark:text-gray-300">{preview.contributorChanges.map(change =>
        <li key={`${change.changeKind}:${change.value}`}>{change.changeKind}: {components.find(component => component.id === change.value)?.name ?? change.value}</li>)}</ul>
    </section>}
    <section><h3 className="mb-2 text-xs font-semibold">Release summary</h3><p className="whitespace-pre-wrap break-words rounded-md border border-slate-200 bg-slate-50 p-3 text-sm leading-6 dark:border-gray-700 dark:bg-gray-950">{releaseSummary}</p>
      <p className="mt-2 text-xs text-slate-500">Generated from the exact preview. Approved customer content is retained until its separate review.</p></section>
    <button type="button" className={secondaryButtonClass} disabled={disabled} onClick={regenerate}>Regenerate publication preview</button>
    <details className="text-xs text-slate-500"><summary className="cursor-pointer">Delivery and notification projection</summary><div className="mt-2 space-y-2">
    <p>{preview.affectedOrganizations.length} affected {preview.affectedOrganizations.length === 1 ? 'organization' : 'organizations'}
      {' · '}{preview.affectedSystems.length} affected {preview.affectedSystems.length === 1 ? 'system' : 'systems'}</p>
    <p>{preview.delivery.impactWrites} impact {preview.delivery.impactWrites === 1 ? 'write' : 'writes'}
      {' · '}{preview.notifications.recipientCount} notification recipients</p>
    <p>Delivery projection: {preview.delivery.distinctOrganizations} organizations ·
      {' '}{preview.delivery.distinctSystems} systems</p>
    <p>Notification projection: {preview.notifications.distinctOrganizations} organizations</p>
    </div></details>
  </article><section className={workspaceCard}><h3 className="mb-3 text-lg font-semibold">Organizations affected</h3>
      {preview.affectedSystems.length
        ? <div className="overflow-x-auto"><table className="min-w-full text-left text-xs"><thead><tr className="border-b border-slate-200 text-slate-500 dark:border-gray-700"><th className="pb-3 pr-3">Organization</th><th className="pb-3 pr-3">System</th><th className="pb-3">Next action</th></tr></thead>
          <tbody>{preview.affectedSystems.map(value => {
            const subscriber = subscribers.find(row => row.organizationId === value.organizationId && row.systemId === value.systemId);
            return <tr key={`${value.organizationId}:${value.systemId}`} className="border-b border-slate-100 dark:border-gray-800">
              <td className="break-all py-3 pr-3">{subscriber?.organizationName ?? value.organizationId}</td><td className="break-all py-3 pr-3">{subscriber?.systemName ?? value.systemId}</td><td className="py-3">{preview.dutyChanges.length ? 'Review updated duty' : 'Review source revision'}</td></tr>;
          })}</tbody></table></div>
        : <p>No subscribed systems are affected.</p>}
      <p className="mt-3 text-xs text-slate-500">Only systems subscribed to this capability receive review work. Unresolved names retain their source IDs.</p>
    </section></>;
}

function OrganizationRecordList({ items, routeSystemId, systemId }: {
  items: OrganizationCapability[]; routeSystemId?: string; systemId?: string;
}) {
  const session = useWorkspaceSession();
  return <div className="relative overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm dark:border-gray-700 dark:bg-gray-900">
    <table className="w-full min-w-[620px] text-left text-sm">
      <thead className="border-b border-slate-200 bg-slate-50 text-[11px] uppercase tracking-wider text-slate-500 dark:border-gray-700 dark:bg-gray-950 dark:text-gray-400">
        <tr><th className="w-[40%] px-5 py-3">Capability / supporting components</th>
          <th className="w-[32%] px-5 py-3">Source &amp; responsibility</th>
          <th className="px-5 py-3">Readiness</th><th className="w-10 px-3"><span className="sr-only">View</span></th></tr>
      </thead><tbody className="divide-y divide-slate-200 dark:divide-gray-700">{items.map(item => {
    const query = new URLSearchParams({ recordType: item.recordType });
    if (systemId && !routeSystemId) query.set('system', systemId);
    const prefix = routeSystemId ? `/systems/${encodeURIComponent(routeSystemId)}` : '';
    const path = `${prefix}/security-capabilities/${item.source}/${encodeURIComponent(item.recordId)}?${query}`;
    const reviewed = item.reviewState === 'Reviewed' || item.reviewState === 'Approved';
    return <tr key={`${item.source}:${item.recordType}:${item.recordId}`} className="group hover:bg-slate-50 dark:hover:bg-gray-800">
      <td className="px-5 py-5 align-top"><Link className="font-semibold text-indigo-700 hover:underline dark:text-indigo-300" to={path}>{item.name}</Link>
        <p className="mt-1 max-w-md text-xs leading-5 text-slate-500 dark:text-gray-400">
          {item.supportingComponents?.length ? item.supportingComponents.map(component => component.name).join(' · ') : item.description}
        </p></td>
      <td className="px-5 py-5 align-top"><p className="text-slate-700 dark:text-gray-200">
        {item.sourceName || (item.source === 'provider' ? 'Provider offering' : session?.workspace.displayName || 'Organization')}
      </p><div className="mt-2 flex flex-wrap gap-1.5">
          <StateBadge tone={item.responsibility === 'Shared' ? 'indigo' : 'neutral'}>{item.responsibility || 'Undesignated'}</StateBadge>
          <StateBadge>{item.mutationAuthority === 'organization' ? 'Organization managed' : 'Provider managed'}</StateBadge>
        </div></td>
      <td className="px-5 py-5 align-top"><StateBadge tone={reviewed ? 'green' : 'amber'}>
        {reviewStateLabel(item.reviewState || (item.recordType === 'component' ? item.availability : undefined))}
      </StateBadge><p className="mt-2 text-xs text-slate-500 dark:text-gray-400">
        {item.controlCount == null ? item.recordType === 'component' ? item.category : 'Control mappings unavailable'
          : `${item.controlCount} control mapping${item.controlCount === 1 ? '' : 's'}`}
      </p></td>
      <td className="pr-4"><Link to={path} aria-label={`View ${item.name}`} className="text-slate-500 hover:text-indigo-700"><ChevronRight size={18} aria-hidden="true" /></Link></td>
    </tr>;
  })}</tbody></table></div>;
}

function OrganizationCatalogFilters() {
  const { params, set } = useQueryState();
  const [search, setSearch] = useState(params.get('search') ?? '');
  useEffect(() => setSearch(params.get('search') ?? ''), [params]);
  const grouping = params.get('grouping') ?? 'capability';
  return <div className="space-y-3">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div aria-label="Library grouping" className="inline-flex rounded-lg border border-slate-200 bg-white p-1 dark:border-gray-700 dark:bg-gray-900">
        {(['capability', 'component'] as const).map(value => <button key={value} type="button"
          aria-pressed={grouping === value} onClick={() => set({ grouping: value, page: 1 })}
          className={`rounded-md px-4 py-2 text-sm ${grouping === value ? 'bg-indigo-50 font-semibold text-indigo-700 dark:bg-indigo-950 dark:text-indigo-200' : 'text-slate-500 hover:bg-slate-50 dark:text-gray-400'}`}>
          By {value}
        </button>)}
      </div>
      <form className="flex items-center gap-2" onSubmit={event => { event.preventDefault(); set({ search: search.trim(), page: 1 }); }}>
        <label className="relative"><span className="sr-only">Search</span><Search size={16} aria-hidden="true" className="absolute left-3 top-3 text-slate-400" />
          <input className={`${inputClass} w-64 max-w-full pl-9`} value={search} onChange={event => setSearch(event.target.value)}
            placeholder="Search capability library" /></label>
        <button className={secondaryButtonClass}>Search</button>
      </form>
    </div>
    <details className="text-xs text-slate-500 dark:text-gray-400"><summary className="w-fit cursor-pointer">Filter library</summary>
      <div className="mt-3 flex flex-wrap gap-3">
        <label className="grid gap-1">Source<select className={inputClass} value={params.get('source') ?? ''} onChange={event => set({ source: event.target.value, page: 1 })}>
          <option value="">All sources</option><option value="local">Local</option><option value="provider">Provider</option>
        </select></label>
      </div>
    </details>
  </div>;
}

function OrganizationLibrary({ tenantId, routeSystemId }: { tenantId: string; routeSystemId?: string }) {
  const session = useWorkspaceSession();
  const { params, set } = useQueryState();
  const query: CatalogQuery = {
    page: Number(params.get('page') ?? 1), pageSize: 25, search: params.get('search') || undefined,
    grouping: (params.get('grouping') as CatalogQuery['grouping']) || 'capability',
    source: params.get('source') || undefined, systemId: routeSystemId,
  };
  const state = useRemote(signal => api.listOrganizationCapabilities(tenantId, query, signal),
    [tenantId, ...Object.values(query), params.get('dialog')]);
  return <PageLayout title="Security Capabilities"><PageHero eyebrow="Implement · Organization workspace" title="Security Capabilities"
    description={routeSystemId ? 'Manage what protects your system, what delivers it, and who is responsible.' : 'Manage reusable organization capabilities, components, and CSP offerings.'}
    actions={(!routeSystemId || session?.systemAccess?.permissions.canManageSystem) &&
      <button type="button" className={heroAction} onClick={() => set({ dialog: 'capability', step: 1 })}><Plus size={16} aria-hidden="true" />Add capability</button>} />
    <div className="space-y-5"><OrganizationCatalogFilters />
      <Status loading={state.loading} error={state.error} retry={state.retry} />
      {state.data?.aggregateState && state.data.aggregateState !== 'Available' &&
        <p role="alert" className={warningClass}>Partial results: {state.data.aggregateState}</p>}
      {!state.loading && !state.error && state.data?.items.length === 0 &&
        <p className={`${surfaceClass} p-6`}>{query.search ? 'No capabilities match the current filters.' : 'No capabilities are available.'}</p>}
      <OrganizationRecordList items={state.data?.items ?? []} routeSystemId={routeSystemId} systemId={query.systemId} />
      {state.data && <Pager {...state.data} onPage={page => set({ page })} />}
    </div>
    <WorkspaceFootnote />
  </PageLayout>;
}

function CapabilityDetail({ tenantId, source, recordId, routeSystemId }: {
  tenantId: string; source: string; recordId: string; routeSystemId?: string;
}) {
  const session = useWorkspaceSession();
  const { params, set } = useQueryState();
  const systemId = routeSystemId;
  const recordType = params.get('recordType') || undefined;
  const state = useRemote(signal => api.getOrganizationCapability(tenantId, source, recordId, systemId, signal, recordType),
    [tenantId, source, recordId, systemId, recordType, params.get('dialog')]);
  const isComponent = (state.data?.capability.recordType ?? recordType) === 'component';
  const [error, setError] = useState<string | null>(null);
  const review = async (proposal: OrganizationCapabilityDetail['narrativeReviews'][number], decision: string) => {
    try {
      await api.reviewNarrativeProposal(tenantId, source, recordId, proposal.id, {
        systemId: proposal.systemId, expectedRevision: proposal.revision, decision,
      });
      state.retry();
    } catch (reason) {
      setError(`${message(reason)}${(reason as { status?: number }).status === 409 ? ' The proposal changed; reload and review the current revision.' : ''}`);
    }
  };
  const title = isComponent ? 'Component detail' : 'Capability detail';
  const libraryPath = routeSystemId ? `/systems/${encodeURIComponent(routeSystemId)}/security-capabilities` : '/security-capabilities';
  const openSetup = () => set({ dialog: 'capability', step: 1, setupSource: source, setupRecord: recordId, setupRecordType: isComponent ? 'component' : 'capability' });
  const coverage = state.data?.controlCoverage ?? state.data?.responsibilities.map(item => ({
    controlId: item.controlId, designation: item.designation, systemId: item.systemId, remainingDuty: null,
  })) ?? [];
  return <PageLayout title={title}><PageHero eyebrow={`Implement · ${source} ${isComponent ? 'component' : 'capability'}`}
    title={state.data?.capability.name ?? title}
    description={state.data?.capability.description || 'Persisted responsibility and narrative review state.'}
    actions={(!routeSystemId || (!isComponent && session?.systemAccess?.permissions.canManageSystem)) &&
      <button type="button" className={heroAction} onClick={openSetup}><Plus size={16} aria-hidden="true" />{isComponent ? 'Add component' : 'Add capability'}</button>} />
    <Link to={libraryPath} className="mb-5 inline-flex items-center gap-2 text-sm text-indigo-700 dark:text-indigo-300"><ArrowLeft size={15} aria-hidden="true" />All capabilities</Link>
    <Status loading={state.loading} error={state.error} retry={state.retry} />{error && <p role="alert" className={errorClass}>{error}</p>}
    {state.data && isComponent && <>
      <p className="mb-4">{state.data.capability.availability} ·
        {state.data.capability.mutationAuthority === 'provider' ? ' Provider managed' : ' Organization managed'}</p>
      <section className={`${workspaceCard} mb-5`}><h2 className="text-lg font-semibold">Organization contribution</h2>
        <dl className="mt-4 space-y-4 text-sm"><OrganizationContribution capability={state.data.capability} /></dl>
      </section>
      <ComponentCapabilities key={`${tenantId}:${source}:${recordId}:${systemId}`}
        tenantId={tenantId} component={state.data.capability} routeSystemId={routeSystemId} systemId={systemId} />
    </>}
    {state.data && !isComponent && <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1.75fr)_minmax(260px,1fr)]">
      <div className="space-y-5">
        <section className={workspaceCard}><div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-lg font-semibold">What delivers this capability</h2>
          {routeSystemId && session?.systemAccess?.permissions.canManageSystem &&
            <button type="button" className={secondaryButtonClass} onClick={openSetup}>Add components</button>}
        </div>
          {state.data.supportingComponents?.length
            ? <SupportingComponents items={state.data.supportingComponents} />
            : <p className="border-t border-slate-200 py-5 text-sm text-slate-500 dark:border-gray-700">No supporting components are linked in this scope.</p>}
          <p className="mt-3 border-l-4 border-indigo-300 pl-3 text-xs leading-5 text-slate-500 dark:text-gray-400">Reuse these components in other capabilities. Each retains its own owner and source.</p>
        </section>
        <section className={workspaceCard}><div className="mb-4 flex items-center justify-between gap-3">
          <h2 className="text-lg font-semibold">Mapped control coverage</h2>
          <StateBadge tone={state.data.capability.reviewState === 'Reviewed' ? 'green' : 'amber'}>{reviewStateLabel(state.data.capability.reviewState)}</StateBadge>
        </div>
          <div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead className="text-[11px] uppercase tracking-wide text-slate-500">
            <tr><th className="pb-3 pr-3">Control</th><th className="pb-3 pr-3">Responsibility</th><th className="pb-3">Remaining duty</th></tr></thead>
            <tbody className="divide-y divide-slate-200 border-t border-slate-200 dark:divide-gray-700 dark:border-gray-700">
              {coverage.map((item, index) => <tr key={`${item.systemId}:${item.controlId}:${index}`}>
                <td className="py-3 pr-3 font-medium">{item.controlId}</td>
                <td className="py-3 pr-3"><StateBadge tone={item.designation === 'Shared' ? 'indigo' : 'neutral'}>{item.designation}</StateBadge></td>
                <td className="py-3 text-xs text-slate-500 dark:text-gray-400">{item.remainingDuty || 'Not recorded; confirm system duties'}</td>
              </tr>)}
            </tbody></table></div>
          {!coverage.length && <p className="py-4 text-sm text-slate-500">No persisted control mappings are available.</p>}
          {!!state.data.responsibilities.length && <details className="mt-3 text-xs text-slate-500"><summary className="cursor-pointer">Confirmation history</summary>
            <ItemList items={state.data.responsibilities} render={item => `${item.controlId} · ${item.designation} · system ${item.systemId} · revision ${item.sourceRevision ?? 'unavailable'}`} />
          </details>}
        </section>
      </div>
      <div className="space-y-5">
        <section className={workspaceCard}><h2 className="text-lg font-semibold">Responsibility</h2>
          <dl className="mt-4 space-y-4 text-sm">
            <div><dt className="text-xs text-slate-500">Provider contribution</dt><dd className="mt-1">{source === 'provider' ? `${state.data.providerName || 'Provider'} manages the offered source capability.` : 'Local organization capability; no provider contribution recorded.'}</dd></div>
            <OrganizationContribution capability={state.data.capability} />
            {systemId ? <>
              <div><dt className="text-xs text-slate-500">System scope</dt><dd className="mt-1">Selected system workspace</dd></div>
              <div><dt className="text-xs text-slate-500">Authorization</dt><dd className="mt-1">Separate system ATO decision required</dd></div>
            </> : <div><dt className="text-xs text-slate-500">Organization availability</dt><dd className="mt-1">{source === 'local' || state.data.capability.isOrganizationAdopted ? 'In organization library' : 'CSP offering available to adopt'}</dd></div>}
          </dl>
        </section>
        <section className={workspaceCard}><h2 className="border-b border-slate-200 pb-3 text-base font-semibold dark:border-gray-700">Evidence &amp; narratives</h2>
          <div className="flex gap-3 border-b border-slate-200 py-4 dark:border-gray-700"><FileCheck2 size={18} className="mt-1 shrink-0 text-slate-500" aria-hidden="true" />
            <div><h3 className="text-sm font-medium">Source evidence</h3><p className="mt-1 break-words text-xs text-slate-500">{state.data.sourceReference || 'No source reference recorded'}</p></div></div>
          {systemId && <div className="py-4"><h3 className="text-sm font-medium">System narrative</h3><p className="mt-1 text-xs text-slate-500">{state.data.narrativeReviews.length ? `${state.data.narrativeReviews.length} persisted proposal(s)` : 'No narrative proposals recorded'}</p></div>}
          {systemId && source === 'provider'
            ? <Link className={`${buttonClass} flex justify-center`} to={`/systems/${encodeURIComponent(systemId)}/inheritance/subscriptions`}>Review responsibilities</Link>
            : <p className="rounded bg-indigo-50 p-3 text-xs text-indigo-800 dark:bg-indigo-950 dark:text-indigo-200">Organization contribution describes the reusable offering. Application and responsibility confirmation happen separately inside each system.</p>}
        </section>
      </div>
      {!!state.data.narrativeReviews.length && <section className={`${workspaceCard} lg:col-span-2`}><h2 className="text-lg font-semibold">Narrative proposals</h2>
        {state.data.narrativeReviews.map(proposal => <article key={proposal.id} className={`${surfaceClass} my-3 p-4`}>
          <h3 className="font-semibold">{proposal.controlId} · {proposal.narrativeType} · {proposal.status}</h3>
          <details className="mt-2 text-xs"><summary className="cursor-pointer text-slate-500">Source provenance</summary><pre className="mt-2 overflow-auto">{JSON.stringify(proposal.provenance)}</pre></details>
          {session?.systemAccess?.permissions.canReviewNarratives && proposal.status === 'Pending' &&
            <div className="mt-3 flex gap-2"><button type="button" className={buttonClass} onClick={() => void review(proposal, 'Accept')}>Accept</button>
              <button type="button" className={secondaryButtonClass} onClick={() => void review(proposal, 'Reject')}>Reject</button></div>}
        </article>)}
      </section>}
    </div>}
    <WorkspaceFootnote />
  </PageLayout>;
}

function OrganizationContribution({ capability }: { capability: OrganizationCapability }) {
  return <>
    <div><dt className="text-xs text-slate-500">Organization contribution</dt><dd className="mt-1 whitespace-pre-wrap">{capability.organizationContribution || 'No organization contribution recorded.'}</dd></div>
    <div><dt className="text-xs text-slate-500">Organization owner</dt><dd className="mt-1">{capability.organizationOwner || 'Not recorded'}</dd></div>
  </>;
}

function ComponentCapabilities({ tenantId, component, routeSystemId, systemId }: {
  tenantId: string; component: OrganizationCapability; routeSystemId?: string; systemId?: string;
}) {
  const { params, set } = useQueryState();
  const page = Number(params.get('page') ?? 1);
  const state = useRemote(signal => api.listOrganizationCapabilities(tenantId, {
    page, pageSize: 25, grouping: 'capability', componentId: component.recordId, source: component.source, systemId,
  }, signal), [tenantId, component.recordId, component.source, systemId, page]);
  return <section className="space-y-4">
    <h2 className="text-lg font-semibold">Capabilities</h2>
    <p>Select a capability to view its persisted responsibilities and narrative proposals.</p>
    <Status loading={state.loading} error={state.error} retry={state.retry} />
    {state.data?.aggregateState && state.data.aggregateState !== 'Available' &&
      <p role="alert" className={warningClass}>Partial results: {state.data.aggregateState}</p>}
    {!state.loading && !state.error && state.data?.items.length === 0 &&
      <p className={`${surfaceClass} p-4`}>No eligible capabilities are available for this component in the current scope.</p>}
    <OrganizationRecordList items={state.data?.items ?? []} routeSystemId={routeSystemId} systemId={systemId} />
    {state.data && <Pager {...state.data} onPage={value => set({ page: value })} />}
  </section>;
}

export function SetupWizard({ tenantId, routeSystemId }: { tenantId: string; routeSystemId?: string }) {
  const { params } = useQueryState();
  const operationId = params.get('operation') ?? 'fresh';
  const identity = `${tenantId}:${routeSystemId ?? 'organization'}:${operationId}`;
  return <SetupWizardState key={identity} tenantId={tenantId} routeSystemId={routeSystemId} />;
}

function SetupWizardState({ tenantId, routeSystemId }: { tenantId: string; routeSystemId?: string }) {
  const session = useWorkspaceSession();
  const navigate = useNavigate();
  const location = useLocation();
  const { params, set } = useQueryState();
  const step = ['2', '3'].includes(params.get('step') ?? '') ? Number(params.get('step')) : 1;
  const operationId = params.get('operation');
  const initialSource = params.get('setupSource') ?? params.get('source') ?? 'local';
  const [form, setForm] = useState({
    source: initialSource, recordId: params.get('setupRecord') ?? params.get('record') ?? '',
    systemId: routeSystemId ?? params.get('setupSystem') ?? '', componentIds: '', subscribe: initialSource === 'provider',
  });
  const [inlineCreate, setInlineCreate] = useState(false);
  const [inlineCapability, setInlineCapability] = useState({
    name: '', provider: '', category: '', description: '', implementationStatus: 'Planned', owner: '',
  });
  const [result, setResult] = useState<SetupResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loadingIntent, setLoadingIntent] = useState(!!operationId);
  const [intentRevision, setIntentRevision] = useState(0);
  const [acknowledged, setAcknowledged] = useState(false);
  const [capabilitySearch, setCapabilitySearch] = useState('');
  const [creatingComponent, setCreatingComponent] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const submittingRef = useRef(false);
  const key = useRef(params.get('key') ?? crypto.randomUUID());
  const resolvedOperation = useRef<string | null>(null);
  const setupController = useRef<AbortController | null>(null);
  useEffect(() => () => setupController.current?.abort(), [tenantId, routeSystemId]);
  const access = useRemote(signal => !routeSystemId && form.systemId
    ? api.getSetupSystemAccess(form.systemId, signal) : Promise.resolve(null), [tenantId, routeSystemId, form.systemId]);
  const systemAccess = routeSystemId ? session?.systemAccess : access.data;
  const canManage = !!form.systemId && systemAccess?.systemId.toLowerCase() === form.systemId.toLowerCase()
    && systemAccess.permissions.canManageSystem && (!!routeSystemId || systemAccess.permissions.canRead);
  const system = useRemote(signal => form.systemId ? api.getSetupSystem(form.systemId, signal) : Promise.resolve(null), [form.systemId]);
  const selectedCapability = useRemote(signal => form.recordId && !inlineCreate
    ? api.getOrganizationCapability(tenantId, form.source, form.recordId, undefined, signal, 'capability')
    : Promise.resolve(null), [tenantId, form.source, form.recordId, inlineCreate]);
  const componentsStage = step === 1 && params.get('stage') === 'components';
  const visualStep = step >= 2 ? 3 : componentsStage ? 2 : 1;
  const libraryPath = `/systems/${encodeURIComponent(routeSystemId ?? form.systemId)}/security-capabilities`;
  const busy = loadingIntent || submitting || creatingComponent;
  const close = () => {
    if (busy) return;
    if (location.pathname.endsWith('/setup')) {
      navigate(routeSystemId ? libraryPath : '/security-capabilities');
      return;
    }
    const next = new URLSearchParams(params);
    for (const key of ['dialog', 'step', 'stage', 'operation', 'setupSource', 'setupRecord', 'setupSystem', 'key']) next.delete(key);
    navigate({ pathname: location.pathname, search: next.size ? `?${next}` : '' }, { replace: true });
  };
  useEffect(() => {
    if (!operationId || resolvedOperation.current === operationId) return;
    const controller = new AbortController();
    setupController.current?.abort();
    setupController.current = controller;
    setLoadingIntent(true);
    api.getCapabilitySetup(tenantId, operationId, controller.signal)
      .then(operation => {
        if (controller.signal.aborted) return;
        if (routeSystemId && operation.systemId !== routeSystemId) {
          setError('This setup operation belongs to a different system route.');
          return;
        }
        resolvedOperation.current = operation.operationId;
        key.current = operation.idempotencyKey;
        setAcknowledged(false);
        set({ key: operation.idempotencyKey });
        setForm({
          source: operation.source,
          recordId: operation.recordId,
          systemId: operation.systemId ?? '',
          componentIds: operation.componentIds.join('\n'),
          subscribe: operation.subscribeRequested,
        });
        setInlineCreate(!!operation.inlineLocalCapability);
        if (operation.inlineLocalCapability) setInlineCapability(operation.inlineLocalCapability);
        setResult(operation);
      }).catch(reason => {
        if (!controller.signal.aborted) setError(message(reason));
      }).finally(() => {
        if (!controller.signal.aborted) setLoadingIntent(false);
      });
    return () => controller.abort();
  }, [operationId, tenantId, routeSystemId, intentRevision]);
  const setupRequest = () => ({
    idempotencyKey: key.current,
    source: form.source,
    recordId: form.recordId.trim(),
    systemId: form.systemId.trim() || null,
    componentIds: form.source === 'provider' ? [] : parseList(form.componentIds),
    subscribe: form.subscribe,
    inlineLocalCapability: inlineCreate ? {
      name: inlineCapability.name.trim(),
      provider: inlineCapability.provider.trim(),
      category: inlineCapability.category.trim(),
      description: inlineCapability.description.trim(),
      implementationStatus: inlineCapability.implementationStatus.trim(),
      owner: inlineCapability.owner.trim(),
    } : undefined,
  });
  const validate = () => {
    if ((!inlineCreate && !form.recordId.trim()) || !form.systemId.trim()) {
      setError('A capability and system are required.');
      return false;
    }
    if (inlineCreate && Object.values(inlineCapability).some(value => !value.trim())) {
      setError('All inline local capability fields are required.');
      return false;
    }
    return true;
  };
  const prepare = async () => {
    if (!canManage || busy || submittingRef.current || !validate()) return;
    submittingRef.current = true;
    setError(null);
    setupController.current?.abort();
    const controller = new AbortController();
    setupController.current = controller;
    setLoadingIntent(true);
    try {
      const operation = await api.prepareCapabilitySetup(tenantId, setupRequest(), controller.signal);
      if (controller.signal.aborted) return;
      resolvedOperation.current = operation.operationId;
      key.current = operation.idempotencyKey;
      setForm({
        source: operation.source,
        recordId: operation.recordId,
        systemId: operation.systemId ?? '',
        componentIds: operation.componentIds.join('\n'),
        subscribe: operation.subscribeRequested,
      });
      setInlineCreate(!!operation.inlineLocalCapability);
      if (operation.inlineLocalCapability) setInlineCapability(operation.inlineLocalCapability);
      setResult(operation);
      set({
        step: 2, operation: operation.operationId, setupSource: null, setupRecord: null,
        setupSystem: null, components: null, subscribe: null, key: null, stage: null,
      });
    } catch (reason) {
      if (!controller.signal.aborted) setError(message(reason));
    } finally {
      submittingRef.current = false;
      if (!controller.signal.aborted) setLoadingIntent(false);
    }
  };
  const submit = async () => {
    if (!canManage || loadingIntent || submittingRef.current || resolvedOperation.current !== operationId) return;
    if (!validate() || !operationId) {
      if (!operationId) setError('Prepare this setup before applying writes.');
      return;
    }
    submittingRef.current = true;
    setSubmitting(true);
    setError(null);
    setupController.current?.abort();
    const controller = new AbortController();
    setupController.current = controller;
    try {
      const next = await api.completeCapabilitySetup(tenantId, {
        ...setupRequest(),
        preparedOperationId: operationId,
      }, controller.signal);
      if (controller.signal.aborted) return;
      resolvedOperation.current = next.operationId;
      setResult(next);
      set({
        step: 3, operation: next.operationId, setupSource: null, setupRecord: null,
        setupSystem: null, components: null, subscribe: null, key: null,
      });
    } catch (reason) {
      if (!controller.signal.aborted) {
        setError(message(reason));
        try {
          const persisted = await api.getCapabilitySetup(tenantId, operationId, controller.signal);
          if (!controller.signal.aborted) { setResult(persisted); set({ step: 3 }); }
        } catch (reloadError) {
          if (!controller.signal.aborted) setError(`${message(reason)} Unable to reload saved outcomes: ${message(reloadError)}`);
        }
      }
    } finally {
      submittingRef.current = false;
      if (!controller.signal.aborted) setSubmitting(false);
    }
  };
  const needsProviderSubscription = form.source === 'provider' && !form.subscribe;
  const complete = result?.recordState === 'Completed' && result.componentLinksState === 'Completed'
    && (form.source === 'provider' ? result.subscriptionState === 'Completed'
      : result.outcomes.some(outcome => outcome.writeKind === 'system-link'
        && outcome.writeId === form.systemId && outcome.state === 'Completed'))
    && !result.lastError && result.outcomes.every(outcome => outcome.state === 'Completed');
  if (!form.systemId && !operationId) return <SetupDialog busy={false} onClose={close}>
    <SetupSystemPicker onCancel={close} onSelect={systemId => {
      setForm(current => ({ ...current, systemId }));
      set({ setupSystem: systemId });
    }} />
  </SetupDialog>;
  return <SetupDialog busy={busy} onClose={close}><div className="mx-auto max-w-2xl">
    <ol className="mb-6 flex flex-wrap items-center gap-3 border-b border-slate-200 pb-5 text-sm dark:border-gray-700" aria-label="Setup progress">{['Capability', 'Components', 'Review'].map((label, index) =>
      <li key={label} aria-current={visualStep === index + 1 ? 'step' : undefined}
        className={`flex items-center gap-2 ${visualStep === index + 1 ? 'font-semibold text-indigo-700 dark:text-indigo-300' : 'text-slate-500'}`}>
        {index > 0 && <span className="mr-1 text-slate-300" aria-hidden="true">—</span>}
        <span className={`flex h-6 w-6 items-center justify-center rounded-full border text-xs ${visualStep === index + 1 ? 'border-indigo-600 bg-indigo-600 text-white' : 'border-slate-300'}`}>{visualStep > index + 1 ? '✓' : index + 1}</span>{label}
      </li>)}</ol>
    {loadingIntent && <p role="status">Loading persisted setup intent…</p>}
    {error && <p role="alert" className={errorClass}>{error}</p>}
    {needsProviderSubscription && operationId && result && <div role="alert" className={`${warningClass} mb-4 space-y-3`}>
      <p>This saved operation did not request a subscription, so it does not add the provider capability to this system. Prepare a new subscription without changing the original operation.</p>
      <button type="button" className={secondaryButtonClass} disabled={!canManage || busy} onClick={() => {
        key.current = crypto.randomUUID();
        setForm(current => ({ ...current, subscribe: true, componentIds: '' }));
        setAcknowledged(false);
        set({ step: 1, stage: 'components' });
      }}>Prepare provider subscription</button>
    </div>}
    {operationId && error && !result && !loadingIntent &&
      <button type="button" className={secondaryButtonClass} onClick={() => {
        setError(null); setLoadingIntent(true); setIntentRevision(value => value + 1);
      }}>Reload setup</button>}
    <Status error={selectedCapability.error} retry={selectedCapability.retry} />
    <Status loading={!routeSystemId && access.loading} error={access.error} retry={access.retry} />
    {!canManage && !access.loading && <p role="alert" className={errorClass}>System setup permission is required. Select a system you can manage or request access.</p>}
    {step === 1 && !componentsStage && <div className="grid gap-5">
      <h2 className="text-lg font-semibold">What does your system need to do?</h2>
      {!inlineCreate && <label className="grid gap-2 text-sm font-medium">Capability name
        <input type="search" className={inputClass} placeholder="Search capabilities by name" value={capabilitySearch}
          onChange={event => setCapabilitySearch(event.target.value)} />
      </label>}
      <label className="grid gap-2 text-sm font-medium">Source
        <select className={inputClass} value={form.source} onChange={event => {
          setInlineCreate(false);
          setForm(current => ({ ...current, source: event.target.value, recordId: '', componentIds: '', subscribe: event.target.value === 'provider' }));
        }}><option value="provider">Use a provider capability</option><option value="local">Use an organization capability</option></select>
      </label>
      {form.source === 'local' && <label className="text-sm"><input type="checkbox" className="mr-2 accent-indigo-700 dark:accent-indigo-400" checked={inlineCreate}
        onChange={event => {
          setInlineCreate(event.target.checked);
          if (event.target.checked) setForm(current => ({ ...current, source: 'local', recordId: '', subscribe: false }));
        }} /> Create a new local capability</label>}
      {!inlineCreate && <CapabilityChoices key={`${tenantId}:${form.source}:${capabilitySearch}`} tenantId={tenantId} source={form.source} search={capabilitySearch}
        selected={form.recordId} onSelect={recordId => setForm(current => ({ ...current, recordId }))} />}
      {inlineCreate && <fieldset className={`${surfaceClass} grid gap-3 p-4`}>
        <legend className="px-1 font-semibold">New local capability</legend>
        {([
          ['name', 'Capability name'], ['provider', 'Provider'], ['category', 'Category'],
          ['description', 'Description'], ['implementationStatus', 'Implementation status'], ['owner', 'Owner'],
        ] as const).map(([name, label]) => <label key={name} className="grid gap-1">{label}
          {name === 'implementationStatus' ? <select className={inputClass} value={inlineCapability[name]}
            onChange={event => setInlineCapability(current => ({ ...current, implementationStatus: event.target.value }))}>
            {['Planned', 'InProgress', 'Implemented', 'Deprecated'].map(status => <option key={status}>{status}</option>)}
          </select> : <input className={inputClass} value={inlineCapability[name]}
            onChange={event => setInlineCapability(current => ({ ...current, [name]: event.target.value }))} />}
        </label>)}
      </fieldset>}
      <label className="grid gap-2 text-sm font-medium">Apply to<select className={inputClass} value={form.systemId} disabled>
        <option value={form.systemId}>{system.data?.name ?? 'Selected system workspace'}</option>
      </select></label>
      <Status error={system.error} retry={system.retry} />
      {!routeSystemId && !operationId && <button type="button" className="text-sm text-indigo-700 underline" onClick={() => {
        setForm(current => ({ ...current, systemId: '', componentIds: '' }));
        set({ setupSystem: null });
      }}>Choose a different system</button>}
      <div className="flex justify-between border-t border-slate-200 pt-5">
        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={close}>Cancel</button>
        <button type="button" className={buttonClass} disabled={!canManage || loadingIntent}
          onClick={() => { if (validate()) { setError(null); set({ stage: 'components' }); } }}>Continue →</button>
      </div>
    </div>}
    {componentsStage && <section className="space-y-5">
      <div><h2 className="text-lg font-semibold">Connect the components that deliver it</h2>
        <p className="mt-1 text-sm text-slate-500">Existing records are linked and reused.</p></div>
      {form.source === 'local' ? <fieldset disabled={!canManage || loadingIntent}><legend className="sr-only">Select supporting components</legend>
      <SetupComponentChoices systemId={form.systemId} selected={parseList(form.componentIds)}
        onChange={componentIds => setForm(current => ({ ...current, componentIds: componentIds.join('\n') }))}
        capability={selectedCapability.data} onBusyChange={setCreatingComponent} /></fieldset>
        : <div className={workspaceCard}>
          <SupportingComponents items={selectedCapability.data?.supportingComponents?.filter(item => item.source === 'provider') ?? []} />
          <p className="mt-3 text-sm text-slate-500">Provider components remain read-only. Local component links belong to organization capabilities, not provider offerings.</p>
        </div>}
      {form.source === 'provider' && <label className="flex gap-2 text-sm"><input type="checkbox" className="accent-indigo-600" checked={form.subscribe} disabled />Subscribe the selected system to provider updates</label>}
      <div className="flex justify-between border-t border-slate-200 pt-5">
        <button type="button" className={secondaryButtonClass} disabled={creatingComponent || loadingIntent} onClick={() => set({ stage: null })}>Back</button>
        <button type="button" className={buttonClass} disabled={!canManage || loadingIntent || creatingComponent} onClick={() => void prepare()}>
          {loadingIntent ? 'Preparing review…' : 'Continue →'}
        </button>
      </div>
    </section>}
    {step === 2 && <section className="space-y-5">
      <div><h2 className="text-lg font-semibold">Review responsibilities before applying</h2>
        <p className="mt-1 text-sm text-slate-500">{inlineCreate ? inlineCapability.name : selectedCapability.data?.capability.name || 'Selected capability'} · {system.data?.name ?? 'Selected system'}</p></div>
      <div className={workspaceCard}>
        <h3 className="font-semibold">{form.source === 'provider' ? 'Provider + organization implementation' : 'Organization implementation'}</h3>
        <p className="my-4 border-l-4 border-indigo-300 pl-3 text-sm text-slate-500">Source coverage is a contribution. Confirm remaining system duties in the responsibility review; this setup does not approve controls or grant an ATO.</p>
        <dl className="space-y-4 text-sm">
          <div><dt className="text-xs text-slate-500">Responsibility</dt><dd className="mt-1">Review and confirm within the selected system.</dd></div>
          <div><dt className="text-xs text-slate-500">Organization duties</dt><dd className="mt-1">Configure system use, assign operators and review applicable controls through the authorized system workflow.</dd></div>
          <div><dt className="text-xs text-slate-500">Local components to link</dt><dd className="mt-1">{parseList(form.componentIds).length} selected</dd></div>
          <div><dt className="text-xs text-slate-500">Provider subscription</dt><dd className="mt-1">{form.subscribe ? 'Requested' : 'Not requested'}</dd></div>
          <div><dt className="text-xs text-slate-500">New local capability</dt><dd className="mt-1">{inlineCreate ? inlineCapability.name : 'Reuse existing capability'}</dd></div>
        </dl>
        {!!selectedCapability.data?.controlCoverage?.length && <div className="mt-4 flex flex-wrap gap-1">{selectedCapability.data.controlCoverage.map(item => <StateBadge key={`${item.systemId}:${item.controlId}`}>{item.controlId}</StateBadge>)}</div>}
      </div>
      <label className="flex items-center gap-3 rounded-lg border border-slate-200 bg-white p-4 text-sm dark:border-gray-700 dark:bg-gray-900">
        <input type="checkbox" className="accent-indigo-600" checked={acknowledged} onChange={event => setAcknowledged(event.target.checked)} />
        I reviewed the source, system scope and proposed setup writes.
      </label>
      <p className="text-xs text-slate-500">Applying saves the selected links and subscription. Responsibility confirmation and narrative approval remain separate.</p>
      <details className="text-xs text-slate-500"><summary className="cursor-pointer">Operation identifiers</summary>
        <dl className="mt-2 grid gap-2 break-words"><dt>Capability identifier</dt><dd>{form.recordId || 'Assigned during creation'}</dd>
          <dt>Component identifiers</dt><dd>{parseList(form.componentIds).join(', ') || 'None'}</dd>
          <dt>Idempotency key</dt><dd>{key.current}</dd></dl>
      </details>
      <div className="flex justify-between border-t border-slate-200 pt-5">
        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => {
          key.current = crypto.randomUUID();
          setAcknowledged(false);
          set({ step: 1, stage: 'components' });
        }}>Back</button>
        <button type="button" className={buttonClass} disabled={!canManage || busy || !acknowledged || needsProviderSubscription || resolvedOperation.current !== operationId}
          onClick={() => void submit()}>{submitting ? 'Applying setup…' : 'Apply setup'}</button>
      </div>
    </section>}
    {step === 3 && <section className="space-y-3"><h2 className="text-lg font-semibold">{complete ? 'Capability added' : 'Setup outcomes'}</h2>
      {complete && <p role="status"><strong>{inlineCreate ? inlineCapability.name : selectedCapability.data?.capability.name || 'The capability'}</strong> is now available in <strong>{system.data?.name || 'the selected system'}</strong>. Responsibility review and authorization remain separate.</p>}
      {!result && <p role="status">Loading durable setup outcome…</p>}
      {result && <><p>Record: {result.recordState} · Component links: {result.componentLinksState} · Subscription: {result.subscriptionState}</p>
        {result.lastError && <p role="alert" className={errorClass}>{result.lastError}</p>}
        <ul className="break-words">{result.outcomes.map(outcome => <li key={`${outcome.writeKind}:${outcome.writeId}`}>
          {outcome.writeId}: {outcome.state}{outcome.error ? ` — ${outcome.error}` : ''}</li>)}</ul>
        {!complete && !needsProviderSubscription &&
          <button type="button" className={buttonClass} disabled={!canManage || busy}
            onClick={() => void submit()}>Retry incomplete writes</button>}</>}
      <div className="flex flex-wrap justify-between gap-3 border-t border-slate-200 pt-5">
        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={close}>{complete ? 'Done' : 'Close'}</button>
        {complete && <Link className={buttonClass} to={`${libraryPath}/${form.source}/${encodeURIComponent(form.recordId)}?recordType=capability`}>View capability</Link>}
      </div>
    </section>}
    </div></SetupDialog>;
}

export default function WorkspaceOperationsPage() {
  const session = useWorkspaceSession();
  const location = useLocation();
  if (!session) return <main className="bg-white p-6 text-gray-900 dark:bg-gray-950 dark:text-gray-100"><p role="alert" className={errorClass}>An authorized workspace is required.</p></main>;
  const segments = location.pathname.split('/').filter(Boolean);
  if (session.target.kind === 'csp') {
    if (segments[0] === 'security-capabilities' && segments[1] === 'imports') {
      if (!session.workspace.permissions.canAccessCsp) return <main className="bg-white p-6 text-gray-900 dark:bg-gray-950 dark:text-gray-100"><p role="alert" className={errorClass}>Provider access is required.</p></main>;
      const query = new URLSearchParams(location.search);
      if (segments[2]) query.set('packageId', segments[2]);
      return <Navigate replace to={`/authorizations${segments[2] ? '/import' : ''}${query.size ? `?${query}` : ''}${location.hash}`} />;
    }
    if (segments[0] === 'organizations') {
      if (!session.workspace.permissions.canAccessCsp) return <main className="bg-white p-6 text-gray-900 dark:bg-gray-950 dark:text-gray-100"><p role="alert" className={errorClass}>Provider access is required.</p></main>;
      const tenantId = segments[1];
      if (!tenantId) return <Organizations />;
      if (tenantId === 'new') return <AddOrganization />;
      if (segments[2] === 'provisioning') {
        return <Provisioning key={tenantId} tenantId={tenantId} />;
      }
      return <OrganizationDetailView tenantId={tenantId} />;
    }
    const capabilityId = segments[1];
    return capabilityId ? <ProviderCapability capabilityId={capabilityId} /> : <ProviderCatalog />;
  }
  const tenantId = session.target.tenantId;
  const setupOpen = new URLSearchParams(location.search).get('dialog') === 'capability';
  if (segments[0] === 'systems' && segments[1] && segments[2] === 'security-capabilities') {
    return <>{segments[3] && segments[4]
      ? <CapabilityDetail tenantId={tenantId} source={segments[3]} recordId={segments[4]} routeSystemId={segments[1]} />
      : <OrganizationLibrary tenantId={tenantId} routeSystemId={segments[1]} />}
      {(setupOpen || segments[3] === 'setup') && <SetupWizard tenantId={tenantId} routeSystemId={segments[1]} />}</>;
  }
  return <>{segments.length >= 3
    ? <CapabilityDetail tenantId={tenantId} source={segments[1]!} recordId={segments[2]!} />
    : <OrganizationLibrary tenantId={tenantId} />}
    {(setupOpen || segments[1] === 'setup') && <OrganizationCatalogDialog key={tenantId} tenantId={tenantId} />}</>;
}
