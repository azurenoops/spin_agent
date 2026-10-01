import { useEffect, useMemo, useRef, useState } from 'react';
import { Building2, FileText, Package, Plus, Search, Users } from 'lucide-react';
import { Link } from '../../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../../workspaces/WorkspaceBoundary';
import { SystemTaskHeading } from '../../systems/SystemTaskPresentation';
import SetupDialog from '../SetupDialog';
import SystemComponentPlacements from './SystemComponentPlacements';
import SystemCapabilitySetup from './SystemCapabilitySetup';
import AppliedCapabilityReview, { type AppliedReviewNavigation } from './AppliedCapabilityReview';
import type { ResponsibilityDraftEdits } from './ResponsibilityDraftEditor';
import { buttonClass, inputClass, moveTabFocus, Pager, secondaryButtonClass, Status, surfaceClass, useQueryState, useRemote } from '../workspaceUi';
import * as api from './systemCapabilityApi';
import type { SystemCapabilityItem, SystemCapabilityPlacement, SystemCapabilityQuery, SystemCapabilitySource } from './systemCapabilityTypes';

const linkClass = 'font-medium text-indigo-700 underline-offset-2 hover:underline dark:text-indigo-300';
const cellClass = 'px-4 py-4 align-top';
const types = ['Person', 'Place', 'Thing', 'Policy'] as const;
const sorts = ['name', 'source', 'status', 'componentType'] as const;

export function SystemCapabilityPlacements({ placements }: { placements: SystemCapabilityPlacement[] }) {
  if (!placements.length) return <span className="text-gray-500 dark:text-gray-400">Unassigned</span>;
  return <ul className="space-y-1">{placements.map(placement => <li key={placement.id || `${placement.state}:${placement.boundaryId}`}>
    {placement.state === 'SystemWide' ? 'System-wide' : placement.state === 'Unassigned' ? 'Unassigned'
      : <>{placement.boundaryName ?? 'Unnamed boundary'}{placement.state === 'Excluded' ? ' (Excluded)' : ''}</>}
  </li>)}</ul>;
}

export function SystemCapabilitySourceBadge({ source }: { source: SystemCapabilitySource }) {
  return <span className="inline-flex rounded-md border border-gray-200 bg-white px-2 py-0.5 text-xs font-medium text-gray-700 dark:border-gray-700 dark:bg-gray-900 dark:text-gray-200">
    {source === 'provider' ? 'Provider' : 'Organization'}
  </span>;
}

function ComponentIcon({ type }: { type: string | null }) {
  const Icon = type === 'Person' ? Users : type === 'Place' ? Building2 : type === 'Policy' ? FileText : Package;
  return <Icon size={18} aria-hidden className="mt-0.5 shrink-0 text-indigo-600 dark:text-indigo-300" />;
}

function AppliedCapabilityDrawer({ tenantId, systemId, source, recordId, onClose, onManageComponent, edits }: {
  tenantId: string;
  systemId: string;
  source: SystemCapabilitySource;
  recordId: string;
  onClose: () => void;
  onManageComponent: (componentId: string, componentSource: SystemCapabilitySource) => void;
  edits: ResponsibilityDraftEdits;
}) {
  const [navigation, setNavigation] = useState<AppliedReviewNavigation>({ section: 'overview', controlId: '' });
  const remote = useRemote(signal => api.getSystemCapability(
    tenantId,
    systemId,
    { source, recordType: 'capability', recordId },
    signal,
  ), [tenantId, systemId, source, recordId]);
  const data = remote.data;
  const item = data?.item;

  return <SetupDialog
    title="Review applied capability"
    description="Review the applied source, system placements, controls, and outstanding responsibilities."
    placement="right"
    busy={false}
    onClose={onClose}
  >
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {data && item && <AppliedCapabilityReview data={data} tenantId={tenantId} systemId={systemId}
      edits={edits} navigation={navigation} onNavigate={setNavigation} onChanged={remote.retry}
      onManageComponent={onManageComponent} />}
  </SetupDialog>;
}

export function SystemComponentDrawer({ tenantId, systemId, source, componentId, onClose, onChanged }: {
  tenantId: string; systemId: string; source: SystemCapabilitySource; componentId: string; onClose: () => void; onChanged?: () => void;
}) {
  const [editingPlacement, setEditingPlacement] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const remote = useRemote(signal => api.getSystemCapability(tenantId, systemId,
    { source, recordType: 'component', recordId: componentId }, signal), [tenantId, systemId, source, componentId]);
  const item = remote.data?.item;
  return <SetupDialog title="Component details" placement="right" busy={busy} onClose={onClose}
    description="Source ownership and placement in this system are separate.">
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {notice && <p role="status" className="mb-4 rounded bg-green-50 p-3 text-sm text-green-900 dark:bg-green-950 dark:text-green-200">{notice}</p>}
    {item && remote.data && <div className="space-y-6">
      <header className="space-y-3">
        <h2 className="text-xl font-semibold">{item.name}</h2>
        <SystemCapabilitySourceBadge source={item.source} />
        <p className="text-sm text-gray-600 dark:text-gray-300">{item.description || 'No source description recorded.'}</p>
      </header>
      <dl className="grid grid-cols-2 gap-3 text-sm">
        <dt className="text-gray-500 dark:text-gray-400">Component type</dt><dd>{item.componentType || 'Not recorded'}</dd>
        <dt className="text-gray-500 dark:text-gray-400">Subtype</dt><dd>{item.subType || 'Not recorded'}</dd>
        <dt className="text-gray-500 dark:text-gray-400">Source</dt><dd>{item.sourceName}</dd>
      </dl>
      {source === 'provider' && <p className={`${surfaceClass} p-3 text-sm`}>
        This source is managed by the provider and is read-only here. Changing a system placement does not change provider authorship.
      </p>}
      <section className="space-y-2">
        <h3 className="font-semibold">Delivered capabilities</h3>
        {item.capabilities.length ? <ul className="space-y-2">{item.capabilities.map(capability =>
          <li key={`${capability.source}:${capability.recordId}`}><Link className={linkClass}
            to={`/systems/${encodeURIComponent(systemId)}/security-capabilities/${capability.source}/${encodeURIComponent(capability.recordId)}`}>
            {capability.name}
          </Link></li>)}</ul> : <p className="text-sm">Direct system assignment. No applied capability links this component.</p>}
      </section>
      <section className="space-y-3">
        <h3 className="font-semibold">System placements</h3>
        {!editingPlacement && <SystemCapabilityPlacements placements={item.placements} />}
        {remote.data.permissions.canManage && item.isApplied
          ? editingPlacement ? <SystemComponentPlacements tenantId={tenantId} systemId={systemId} source={source} componentId={componentId}
            onBusyChange={setBusy} onChanged={text => { setNotice(text); remote.retry(); onChanged?.(); }} />
            : <button type="button" className={secondaryButtonClass} onClick={() => setEditingPlacement(true)}>Manage system placement</button>
          : <p className="text-sm text-gray-600 dark:text-gray-300">System-management permission and an applied component are required to change placement.</p>}
        <Link className={`${linkClass} inline-block text-sm`} to={`/systems/${encodeURIComponent(systemId)}/boundaries`}>Open system boundaries</Link>
        <p className="text-xs text-gray-500 dark:text-gray-400">Placements apply only to this system. Excluded assignments are not in-scope coverage.</p>
      </section>
      <Link className={`${linkClass} inline-block text-sm`}
        to={`/security-capabilities/${source}/${encodeURIComponent(componentId)}?recordType=component`}>Open source in library</Link>
      <details className="text-sm"><summary className="cursor-pointer">Technical details</summary>
        <dl className="mt-2 space-y-2 break-all"><dt>Source revision</dt><dd>{item.sourceRevision}</dd><dt>Component identifier</dt><dd>{item.recordId}</dd></dl>
      </details>
    </div>}
  </SetupDialog>;
}

export default function SystemCapabilityList(props: {
  tenantId: string; systemId: string; systemName: string;
}) {
  const session = useWorkspaceSession();
  return <CapabilityList key={`${session?.identity.directoryTenantId}:${session?.identity.oid}:${session?.workspace.mode}:${props.tenantId}:${props.systemId}`} {...props} />;
}

function CapabilityList({ tenantId, systemId, systemName }: {
  tenantId: string; systemId: string; systemName: string;
}) {
  const draftEdits = useRef<ResponsibilityDraftEdits>(new Map());
  const { params, set } = useQueryState();
  const [setupOpen, setSetupOpen] = useState(false);
  const grouping = params.get('view') === 'component' ? 'component' : 'capability';
  const source = params.get('source');
  const componentType = types.find(type => type === params.get('componentType'));
  const sort = sorts.find(value => value === params.get('sort')) ?? 'name';
  const direction = params.get('direction') === 'desc' ? 'desc' : 'asc';
  const requestedPage = Number(params.get('page') ?? 1);
  const page = Number.isInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1;
  const query: SystemCapabilityQuery = {
    scope: 'applied', grouping, search: params.get('search') || undefined,
    source: source === 'local' || source === 'provider' ? source : undefined,
    componentType, boundaryId: params.get('boundaryId') || undefined, sort, direction, page, pageSize: 25,
  };
  const remote = useRemote(signal => api.listSystemCapabilities(tenantId, systemId, query, signal),
    [tenantId, systemId, JSON.stringify(query)]);
  const metadataScope = `${tenantId}:${systemId}`;
  const [boundaryOptions, setBoundaryOptions] = useState<{ scope: string; values: { id: string; name: string }[] }>({ scope: metadataScope, values: [] });
  useEffect(() => {
    if (remote.data) setBoundaryOptions({ scope: metadataScope, values: remote.data.boundaries });
  }, [remote.data, metadataScope]);
  const boundaries = remote.data?.boundaries ?? (boundaryOptions.scope === metadataScope ? boundaryOptions.values : []);
  const base = `/systems/${encodeURIComponent(systemId)}/security-capabilities`;
  const systemBase = `/systems/${encodeURIComponent(systemId)}`;
  const items = remote.data?.items ?? [];
  const filtered = !!(query.search || query.source || query.componentType || query.boundaryId);
  const selectedCapability = params.get('capabilityId');
  const selectedCapabilitySource = params.get('capabilitySource') === 'provider' ? 'provider'
    : params.get('capabilitySource') === 'local' ? 'local' : null;
  const selectedComponent = params.get('componentId');
  const selectedComponentSource = params.get('componentSource') === 'provider' ? 'provider' : 'local';
  const openCapability = (recordId: string, capabilitySource: SystemCapabilitySource) =>
    set({ capabilityId: recordId, capabilitySource });
  const openComponent = (recordId: string, componentSource: SystemCapabilitySource) => set({ componentId: recordId, componentSource });
  const followUp = useMemo(() => {
    const reviewItems = remote.data?.items.reduce((total, item) => total + item.reviewRequiredCount, 0) ?? 0;
    return <aside className="space-y-5" aria-label="Applied capability supporting actions">
      <section className="border-l-2 border-indigo-200 pl-4">
        <p className="text-xs font-semibold uppercase tracking-wider text-gray-600 dark:text-gray-400">Used in your package</p>
        <h2 className="mt-2 text-sm font-semibold text-gray-900 dark:text-gray-100">SSP · Control implementation / CRM</h2>
        <p className="mt-1 text-sm text-gray-600 dark:text-gray-300">
          This page supplies reviewed records to the SSP. Draft edits do not replace approved content.
        </p>
        <Link className="mt-2 inline-flex rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          to={`${systemBase}/documents#ssp-sections`}>Preview contribution <span aria-hidden="true" className="ml-1">→</span></Link>
      </section>
      <section className="border-l-2 border-indigo-200 pl-4">
        <p className="text-xs font-semibold uppercase tracking-wider text-gray-600 dark:text-gray-400">Review & ownership</p>
        <h2 className="mt-2 text-sm font-semibold text-gray-900 dark:text-gray-100">Keep the next action clear</h2>
        {remote.loading ? <p className="mt-1 text-sm">Loading review status...</p>
          : remote.error ? <p className="mt-1 text-sm">Review status could not be loaded.</p>
            : <p className="mt-1 text-sm text-gray-600 dark:text-gray-300">{reviewItems
              ? `${reviewItems} responsibility ${reviewItems === 1 ? 'item requires' : 'items require'} review on this page.`
              : 'No pending responsibility reviews reported on this page.'}</p>}
        <Link className={`${linkClass} mt-2 inline-block text-sm`} to={`${systemBase}/inheritance/subscriptions`}>
          Review responsibilities
        </Link>
      </section>
      <section className="border-l-2 border-indigo-200 pl-4">
        <p className="text-xs font-semibold uppercase tracking-wider text-gray-600 dark:text-gray-400">Related work</p>
        <Link className="mt-2 inline-flex rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          to={systemBase}>View package readiness <span aria-hidden="true" className="ml-1">→</span></Link>
      </section>
      <p className="text-xs text-gray-600 dark:text-gray-300">Applying a capability does not confirm responsibilities, approve narratives, or grant an ATO.</p>
    </aside>;
  }, [remote.data, remote.loading, remote.error, systemId]);
  function capabilityRow(item: SystemCapabilityItem) {
    return <tr key={`${item.source}:${item.recordType}:${item.recordId}`} className="border-t border-gray-100 dark:border-gray-800">
      <td className={cellClass}><Link className={linkClass} to={`${base}/${item.source}/${encodeURIComponent(item.recordId)}`}>{item.name}</Link>
        <p className="mt-1 line-clamp-2 text-xs text-gray-500 dark:text-gray-400">{item.description}</p></td>
      <td className={cellClass}>{item.source === 'provider' ? `Provider · ${item.sourceName}` : 'Organization'}</td>
      <td className={cellClass}>{item.components.length ? <ul className="space-y-2">{item.components.map(component =>
        <li key={`${component.source}:${component.recordId}`}><button className={`${linkClass} text-left`}
          onClick={() => openComponent(component.recordId, component.source)}>{component.name}</button>
          <div className="mt-1 text-xs text-gray-600 dark:text-gray-300"><SystemCapabilityPlacements placements={component.placements} /></div>
        </li>)}</ul> : <span className="text-gray-500 dark:text-gray-400">{item.sourceName}</span>}</td>
      <td className={cellClass}>{item.reviewRequiredCount > 0
        ? <span className="rounded-md bg-amber-50 px-2 py-1 text-xs font-medium text-amber-900 dark:bg-amber-950 dark:text-amber-200">Responsibilities pending ({item.reviewRequiredCount})</span>
        : <span className="text-xs">{item.status}</span>}</td>
      <td className={cellClass}><button type="button"
        className="whitespace-nowrap rounded-md border border-gray-300 bg-white px-2.5 py-1.5 text-xs font-medium text-indigo-700 hover:bg-gray-50"
        onClick={() => openCapability(item.recordId, item.source)}>
        Open<span className="sr-only"> {item.name}</span><span aria-hidden="true"> →</span>
      </button></td>
    </tr>;
  }

  function componentRow(item: SystemCapabilityItem) {
    return <tr key={`${item.source}:${item.recordType}:${item.recordId}`} className="border-t border-gray-100 dark:border-gray-800">
      <td className={cellClass}><span className="flex gap-2"><ComponentIcon type={item.componentType} />
        <button className={`${linkClass} text-left`} onClick={() => openComponent(item.recordId, item.source)}>{item.name}</button></span></td>
      <td className={cellClass}>{item.componentType || 'Not recorded'}{item.subType && <span className="block text-xs text-gray-500 dark:text-gray-400">{item.subType}</span>}</td>
      <td className={cellClass}><SystemCapabilitySourceBadge source={item.source} /><p className="mt-1 text-xs">{item.sourceName}</p></td>
      <td className={cellClass}>{item.capabilities.length ? <ul className="space-y-2">{item.capabilities.map(capability =>
        <li key={`${capability.source}:${capability.recordId}`}><Link className={linkClass}
          to={`${base}/${capability.source}/${encodeURIComponent(capability.recordId)}`}>{capability.name}</Link></li>)}</ul>
        : <span className="text-xs text-gray-500 dark:text-gray-400">Direct system assignment</span>}</td>
      <td className={cellClass}><SystemCapabilityPlacements placements={item.placements} /></td>
      <td className={cellClass}><button className={`${linkClass} whitespace-nowrap`}
        onClick={() => openComponent(item.recordId, item.source)}>View details<span className="sr-only"> for {item.name}</span></button></td>
    </tr>;
  }

  const pendingReviews = items.reduce((total, item) => total + item.reviewRequiredCount, 0);

  return <div className="space-y-5">
    <SystemTaskHeading
      eyebrow={systemName}
      title="Applied security capabilities"
      description="See which security functions serve this system and which responsibilities still need review."
      action={<div className="flex flex-wrap gap-2">
        <Link className={secondaryButtonClass} to={`/systems/${encodeURIComponent(systemId)}/provider-relationships/setup`}>
          Add CSP hosting &amp; capabilities
        </Link>
        <button className={`${buttonClass} inline-flex items-center gap-2`} disabled={!remote.data?.permissions.canManage}
          onClick={() => setSetupOpen(true)}><Plus size={16} aria-hidden />Add organization capability</button>
      </div>}
    />
    {remote.data && !remote.data.permissions.canManage && <p className="text-sm">Adding capabilities requires system-management permission. Your current access is read-only for this action.</p>}
    <div data-testid="capability-review-summary"
      className="flex flex-col gap-2 rounded-lg border border-gray-200 bg-white px-4 py-3 text-sm text-gray-700 sm:flex-row sm:items-center sm:justify-between">
      <span className="font-medium">{pendingReviews ? 'Draft · Review required' : 'Applied · No pending reviews'}</span>
      <span className="text-xs text-gray-500">{remote.data ? `${remote.data.total} applied ${remote.data.total === 1 ? 'record' : 'records'}` : 'Loading applied records'}</span>
    </div>
    <div className="flex flex-wrap items-center justify-between gap-3 border-b border-gray-200 dark:border-gray-700">
      <div role="tablist" aria-label="Security capability views" className="flex gap-2" onKeyDown={moveTabFocus}>
        {(['capability', 'component'] as const).map(view => <button key={view} role="tab" id={`view-${view}`}
          aria-controls="system-capability-results" aria-selected={grouping === view} tabIndex={grouping === view ? 0 : -1}
          onClick={() => set({ view: view === 'capability' ? null : view, page: 1 })}
          className={`border-b-2 px-3 py-3 text-sm font-medium ${grouping === view ? 'border-indigo-600 text-indigo-700 dark:border-indigo-400 dark:text-indigo-300' : 'border-transparent text-gray-600 dark:text-gray-300'}`}>
          By {view}
        </button>)}
      </div>
      <Link className={`${linkClass} text-sm`} to={`${base}/inventory`}>Manage inventory</Link>
    </div>
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
      <label className="relative text-sm"><span className="mb-1 block">Search this system</span>
        <Search size={15} className="absolute bottom-3 left-3 text-gray-400" aria-hidden />
        <input type="search" className={`${inputClass} w-full pl-9`} value={query.search ?? ''} maxLength={200}
          placeholder={`Search ${grouping === 'capability' ? 'capabilities' : 'components'}...`}
          onChange={event => set({ search: event.target.value, page: 1 })} /></label>
      <label className="text-sm"><span className="mb-1 block">Component type</span><select className={`${inputClass} w-full`} value={componentType ?? ''}
        onChange={event => set({ componentType: event.target.value, page: 1 })}><option value="">All types</option>{types.map(type => <option key={type}>{type}</option>)}</select></label>
      <label className="text-sm"><span className="mb-1 block">Source</span><select className={`${inputClass} w-full`} value={query.source ?? ''}
        onChange={event => set({ source: event.target.value, page: 1 })}><option value="">All sources</option><option value="local">Organization</option><option value="provider">Provider</option></select></label>
      <label className="text-sm"><span className="mb-1 block">Boundary</span><select className={`${inputClass} w-full`} value={query.boundaryId ?? ''}
        onChange={event => set({ boundaryId: event.target.value, page: 1 })}><option value="">All placements</option><option value="system-wide">System-wide</option><option value="unassigned">Unassigned</option>
        {boundaries.map(boundary => <option key={boundary.id} value={boundary.id}>{boundary.name}</option>)}</select></label>
      <label className="text-sm"><span className="mb-1 block">Sort</span><select className={`${inputClass} w-full`} value={`${sort}:${direction}`}
        onChange={event => { const [nextSort, nextDirection] = event.target.value.split(':'); set({ sort: nextSort, direction: nextDirection, page: 1 }); }}>
        <option value="name:asc">Name A-Z</option><option value="name:desc">Name Z-A</option><option value="source:asc">Source</option>
        <option value="status:asc">Status</option><option value="componentType:asc">Component type</option>
      </select></label>
    </div>
    <div className="grid min-w-0 items-start gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(16rem,1fr)]">
      <div role="tabpanel" id="system-capability-results" aria-labelledby={`view-${grouping}`} aria-busy={remote.loading}>
        <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
        {remote.data && (items.length ? <div className={`${surfaceClass} relative overflow-x-auto rounded-lg`}>
          <table className="w-full text-left text-sm"><thead className="bg-slate-50 text-xs text-gray-700 dark:bg-gray-800 dark:text-gray-200"><tr>
            {(grouping === 'capability' ? ['Record', 'Context', 'Source / owner', 'Status', '']
              : ['Component', 'Type', 'Source', 'Delivers', 'Boundary', 'Action']).map(label => <th key={label} scope="col" className="px-4 py-3 font-semibold">{label}</th>)}
          </tr></thead><tbody>{items.map(grouping === 'capability' ? capabilityRow : componentRow)}</tbody></table>
        </div> : <section className={`${surfaceClass} space-y-3 p-6`}>
          <h2 className="font-semibold">{filtered ? 'No matching applied records' : grouping === 'capability' ? 'No security capabilities applied to this system yet.' : 'No components applied to this system yet.'}</h2>
          <p className="text-sm text-gray-600 dark:text-gray-300">{filtered ? 'Change the filters to find other records applied to this system.'
            : 'You can use organization capabilities without a provider. Available library records are separate until you add them to this system.'}</p>
          {filtered && <button className={secondaryButtonClass} onClick={() => set({ search: null, source: null, componentType: null, boundaryId: null, page: 1 })}>Clear filters</button>}
        </section>)}
        {remote.data && <Pager page={remote.data.page} pageSize={remote.data.pageSize} total={remote.data.total} onPage={next => set({ page: next })} />}
      </div>
      <div>{followUp}</div>
    </div>
    {selectedCapability && selectedCapabilitySource && <AppliedCapabilityDrawer
      edits={draftEdits.current}
      key={`${metadataScope}:${selectedCapabilitySource}:${selectedCapability}`}
      tenantId={tenantId}
      systemId={systemId}
      source={selectedCapabilitySource}
      recordId={selectedCapability}
      onClose={() => set({ capabilityId: null, capabilitySource: null })}
      onManageComponent={(recordId, componentSource) => set({
        capabilityId: null,
        capabilitySource: null,
        componentId: recordId,
        componentSource,
      })}
    />}
    {setupOpen && <SetupDialog
      title="Add organization capability"
      description={`Choose organization-managed capabilities and review applicability for ${systemName}.`}
      placement="right"
      expanded
      busy={false}
      onClose={() => { setSetupOpen(false); remote.retry(); }}
    >
      <SystemCapabilitySetup tenantId={tenantId} systemId={systemId} embedded catalogSource="local" onCompleted={remote.retry} onClose={() => {
        setSetupOpen(false);
        remote.retry();
      }} />
    </SetupDialog>}
    {selectedComponent && <SystemComponentDrawer key={`${metadataScope}:${selectedComponentSource}:${selectedComponent}`}
      tenantId={tenantId} systemId={systemId} source={selectedComponentSource} componentId={selectedComponent}
      onChanged={remote.retry}
      onClose={() => set({ componentId: null, componentSource: null })} />}
  </div>;
}
