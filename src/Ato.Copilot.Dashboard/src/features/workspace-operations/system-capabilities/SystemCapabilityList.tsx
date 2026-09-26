import { useEffect, useMemo, useState } from 'react';
import { Building2, FileText, Package, Plus, Search, ShieldCheck, Users } from 'lucide-react';
import { Link, useNavigate } from '../../workspaces/workspaceNavigation';
import { useSystemContext } from '../../../components/layout/SystemLayout';
import SetupDialog from '../SetupDialog';
import SystemComponentPlacements from './SystemComponentPlacements';
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
  return <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
    source === 'provider' ? 'bg-blue-50 text-blue-800 dark:bg-blue-950 dark:text-blue-200'
      : 'bg-indigo-50 text-indigo-800 dark:bg-indigo-950 dark:text-indigo-200'}`}>
    {source === 'provider' ? 'Provider' : 'Organization'}
  </span>;
}

function ComponentIcon({ type }: { type: string | null }) {
  const Icon = type === 'Person' ? Users : type === 'Place' ? Building2 : type === 'Policy' ? FileText : Package;
  return <Icon size={18} aria-hidden className="mt-0.5 shrink-0 text-indigo-600 dark:text-indigo-300" />;
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

export default function SystemCapabilityList({ tenantId, systemId, systemName }: {
  tenantId: string; systemId: string; systemName: string;
}) {
  const { params, set } = useQueryState();
  const navigate = useNavigate();
  const { setPageContext } = useSystemContext();
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
  const items = remote.data?.items ?? [];
  const filtered = !!(query.search || query.source || query.componentType || query.boundaryId);
  const selectedComponent = params.get('componentId');
  const selectedComponentSource = params.get('componentSource') === 'provider' ? 'provider' : 'local';
  const openComponent = (recordId: string, componentSource: SystemCapabilitySource) => set({ componentId: recordId, componentSource });
  const followUp = useMemo(() => {
    const reviewItems = remote.data?.items.reduce((total, item) => total + item.reviewRequiredCount, 0) ?? 0;
    return <section className={`${surfaceClass} space-y-4 p-4`}>
      <h2 className="font-semibold">System follow-up</h2>
      {remote.loading ? <p className="text-sm">Loading review status...</p>
        : remote.error ? <p className="text-sm">Review status could not be loaded.</p>
          : <p className="text-sm">{reviewItems ? `${reviewItems} review ${reviewItems === 1 ? 'item' : 'items'} on this page require attention.`
            : 'No pending responsibility reviews reported on this page.'}</p>}
      <Link className={`${linkClass} inline-block text-sm`} to={`/systems/${encodeURIComponent(systemId)}/inheritance/subscriptions`}>
        Open responsibility review
      </Link>
      <p className="text-xs text-gray-600 dark:text-gray-300">Applying a capability does not confirm responsibilities, approve narratives, or grant an ATO.</p>
    </section>;
  }, [remote.data, remote.loading, remote.error, systemId]);
  useEffect(() => {
    setPageContext?.(followUp);
    return () => setPageContext?.(null);
  }, [setPageContext, followUp]);

  function capabilityRow(item: SystemCapabilityItem) {
    return <tr key={`${item.source}:${item.recordType}:${item.recordId}`} className="border-t border-gray-100 dark:border-gray-800">
      <td className={cellClass}><Link className={linkClass} to={`${base}/${item.source}/${encodeURIComponent(item.recordId)}`}>{item.name}</Link>
        <p className="mt-1 line-clamp-2 text-xs text-gray-500 dark:text-gray-400">{item.description}</p></td>
      <td className={cellClass}><SystemCapabilitySourceBadge source={item.source} /><p className="mt-1 text-xs">{item.sourceName}</p></td>
      <td className={cellClass}>{item.components.length ? <ul className="space-y-2">{item.components.map(component =>
        <li key={`${component.source}:${component.recordId}`}><button className={`${linkClass} text-left`}
          onClick={() => openComponent(component.recordId, component.source)}>{component.name}</button>
          <div className="mt-1 text-xs text-gray-600 dark:text-gray-300"><SystemCapabilityPlacements placements={component.placements} /></div>
        </li>)}</ul> : <span className="text-gray-500 dark:text-gray-400">No contributors recorded</span>}</td>
      <td className={cellClass}>{item.controlIds.length} mapped</td>
      <td className={cellClass}>{item.reviewRequiredCount > 0
        ? <span className="rounded bg-amber-50 px-2 py-1 text-xs font-medium text-amber-900 dark:bg-amber-950 dark:text-amber-200">Review required ({item.reviewRequiredCount})</span>
        : <span className="text-xs">{item.status}</span>}</td>
      <td className={cellClass}><Link className={`${linkClass} whitespace-nowrap`} to={`${base}/${item.source}/${encodeURIComponent(item.recordId)}`}>View details<span className="sr-only"> for {item.name}</span></Link></td>
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

  return <div className="space-y-5">
    <header className="rounded-xl bg-gradient-to-r from-indigo-50 to-sky-50 p-5 dark:from-indigo-950 dark:to-slate-900">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div><div className="flex flex-wrap items-center gap-3"><h1 className="text-2xl font-semibold tracking-tight">Security Capabilities</h1>
          <span className="rounded-full border border-indigo-200 bg-white/60 px-2 py-1 text-xs text-indigo-800 dark:border-indigo-700 dark:bg-indigo-950 dark:text-indigo-200">Applied to this system</span></div>
          <p className="mt-2 text-sm text-gray-600 dark:text-gray-300">Connect security functions, contributing components and control responsibilities.</p>
          <p className="mt-4 flex items-center gap-2 text-sm font-medium"><ShieldCheck size={17} aria-hidden />{systemName}</p>
        </div>
        <button className={`${buttonClass} inline-flex items-center gap-2`} disabled={!remote.data?.permissions.canManage}
          onClick={() => navigate(`${base}/add`)}><Plus size={16} aria-hidden />Add from library</button>
      </div>
      {remote.data && !remote.data.permissions.canManage && <p className="mt-3 text-sm">Adding capabilities requires system-management permission. Your current access is read-only for this action.</p>}
    </header>
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
    <div className="grid gap-3 sm:grid-cols-2 2xl:grid-cols-5">
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
    <div role="tabpanel" id="system-capability-results" aria-labelledby={`view-${grouping}`} aria-busy={remote.loading}>
      <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
      {remote.data && (items.length ? <div className={`${surfaceClass} relative overflow-x-auto rounded-lg`}>
        <table className="w-full text-left text-sm"><thead className="bg-slate-50 text-xs text-gray-700 dark:bg-gray-800 dark:text-gray-200"><tr>
          {(grouping === 'capability' ? ['Capability', 'Source', 'Contributors & placements', 'Controls', 'Responsibility', 'Action']
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
    <div className={setPageContext ? 'xl:hidden' : undefined}>{followUp}</div>
    {selectedComponent && <SystemComponentDrawer key={`${metadataScope}:${selectedComponentSource}:${selectedComponent}`}
      tenantId={tenantId} systemId={systemId} source={selectedComponentSource} componentId={selectedComponent}
      onChanged={remote.retry}
      onClose={() => set({ componentId: null, componentSource: null })} />}
  </div>;
}
