import { useEffect, useMemo, useState } from 'react';
import { Link } from '../../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../../workspaces/WorkspaceBoundary';
import { useSystemContext } from '../../../components/layout/SystemLayout';
import { getCapabilityResponsibilities } from '../../../api/capabilityResponsibilities';
import { ComponentIcon, StateBadge, workspaceCard } from '../CapabilityPresentation';
import { buttonClass, errorClass, moveTabFocus, secondaryButtonClass, Status, useQueryState, useRemote, warningClass } from '../workspaceUi';
import * as api from './systemCapabilityApi';
import type { SystemCapabilityComponent, SystemCapabilityDetail as Detail, SystemCapabilitySource } from './systemCapabilityTypes';
import { boundedRequest } from './systemCapabilityRequests';
import SystemCapabilityResponsibility from './SystemCapabilityResponsibility';
import SystemCapabilityNarratives from './SystemCapabilityNarratives';
import SystemCapabilityRemoval from './SystemCapabilityRemoval';

export default function SystemCapabilityDetail(props: {
  tenantId: string; systemId: string; source: SystemCapabilitySource; recordId: string;
}) {
  const session = useWorkspaceSession();
  return <CapabilityDetail key={`${session?.identity.directoryTenantId}:${session?.identity.oid}:${session?.workspace.mode}:${props.tenantId}:${props.systemId}:${props.source}:${props.recordId}`} {...props} />;
}

function CapabilityDetail({ tenantId, systemId, source, recordId }: {
  tenantId: string; systemId: string; source: SystemCapabilitySource; recordId: string;
}) {
  const session = useWorkspaceSession();
  const { detail: system, setPageContext } = useSystemContext();
  const systemName = system.systemId === systemId ? system.name : systemId;
  const { params, set } = useQueryState();
  const tab = ['coverage', 'evidence'].includes(params.get('tab') ?? '') ? params.get('tab')! : 'implementation';
  const [removalOpen, setRemovalOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const detail = useRemote(signal => boundedRequest(inner => api.getSystemCapability(tenantId, systemId,
    { source, recordType: 'capability', recordId }, inner), signal), [tenantId, systemId, source, recordId]);
  const responsibility = useRemote(signal => source === 'provider' && tab === 'coverage'
    ? boundedRequest(inner => getCapabilityResponsibilities(systemId, inner), signal) : Promise.resolve(null),
  [tenantId, systemId, source, recordId, tab]);
  const data = detail.data;
  const base = `/systems/${encodeURIComponent(systemId)}/security-capabilities`;
  const selectedControl = params.get('control') ?? data?.controls[0]?.controlId ?? '';
  const selected = data?.controls.find(item => item.controlId === selectedControl);
  const closeRemoval = () => { setRemovalOpen(false); set({ operationId: null, removalKey: null }); };
  const tabs = [{ id: 'implementation', label: 'Implementation' }, { id: 'coverage', label: 'Coverage & duties' }, { id: 'evidence', label: 'Evidence & narratives' }];
  const organizationName = session?.workspace.displayName ?? tenantId;
  const context = useMemo(() => data?.permissions.canRead ? <ApplicabilityContext data={data}
    systemName={systemName} organizationName={organizationName} onRemove={() => setRemovalOpen(true)} /> : null,
  [data, systemName, organizationName]);
  useEffect(() => { setPageContext?.(context); return () => setPageContext?.(null); }, [setPageContext, context]);
  return <div className="space-y-5">
    <Link className="text-sm text-indigo-700 underline dark:text-indigo-300" to={base}>All system security capabilities</Link>
    <Status loading={detail.loading} error={detail.error} retry={detail.retry} />
    {data && !data.permissions.canRead && <p role="alert" className={errorClass}>This capability is no longer readable in the selected system.</p>}
    {data?.permissions.canRead && <>
      <header className="rounded-xl border border-indigo-100 bg-gradient-to-r from-indigo-50 to-white p-5 dark:border-indigo-900 dark:from-indigo-950 dark:to-gray-900">
        <p className="text-xs font-semibold uppercase tracking-wide text-indigo-700 dark:text-indigo-300">Selected system · {systemName}</p>
        <div className="mt-2 flex flex-wrap items-center justify-between gap-4"><div>
          <h1 className="text-2xl font-semibold">{data.item.name}</h1>
          <p className="mt-2 text-sm">{data.item.isApplied ? 'Applied to' : 'Not applied to'} {systemName} · {data.item.sourceName}</p>
          <p className="mt-1 text-sm text-slate-600 dark:text-gray-300">{source === 'provider' ? 'Provider source is read-only.' : 'Organization source metadata is managed in the reusable library.'}</p>
        </div><button type="button" className={buttonClass} onClick={() => set({ tab: 'coverage' })}>Review responsibilities</button></div>
      </header>
      {notice && <p role="status" className="rounded border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-900 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-100">{notice}</p>}
      <div className={`grid items-start gap-5 ${setPageContext ? '' : 'xl:grid-cols-[minmax(0,1fr)_260px]'}`}>
        <div className="min-w-0 space-y-5">
          <div role="tablist" aria-label="Capability detail views" onKeyDown={moveTabFocus} className="flex flex-wrap border-b border-slate-200 dark:border-gray-700">
            {tabs.map(item => <button key={item.id} id={`system-capability-tab-${item.id}`} type="button" role="tab" aria-selected={tab === item.id}
              aria-controls={`system-capability-panel-${item.id}`} tabIndex={tab === item.id ? 0 : -1}
              className={`border-b-2 px-4 py-3 text-sm ${tab === item.id ? 'border-indigo-600 font-semibold text-indigo-700 dark:text-indigo-300' : 'border-transparent text-slate-600 dark:text-gray-400'}`}
              onClick={() => set({ tab: item.id })}>{item.label}</button>)}
          </div>
          <div role="tabpanel" id={`system-capability-panel-${tab}`} aria-labelledby={`system-capability-tab-${tab}`} tabIndex={0} className="space-y-5">
            {tab === 'implementation' && <>
              <section className={workspaceCard}><h2 className="text-lg font-semibold">Implementation</h2>
                <p className="mt-3 whitespace-pre-wrap text-sm text-slate-600 dark:text-gray-300">{data.item.description || 'No source implementation description is recorded.'}</p></section>
              <section className={`${workspaceCard} space-y-4`}>
                <div className="flex flex-wrap justify-between gap-3"><h2 className="text-lg font-semibold">Contributing components</h2>
                  <Link className="text-sm text-indigo-700 underline dark:text-indigo-300" to={`/systems/${encodeURIComponent(systemId)}/boundaries`}>View system boundaries</Link></div>
                <p className="text-sm text-slate-500 dark:text-gray-400">Source ownership and actual system placements remain separate.</p>
                <div className="grid gap-3 md:grid-cols-2">{data.item.components.map(component =>
                  <Contributor key={`${component.source}:${component.recordId}`} component={component} systemId={systemId} providerCapability={source === 'provider'} />)}</div>
                {!data.item.components.length && <p className="text-sm text-slate-500 dark:text-gray-400">No contributing components are recorded in this scope.</p>}
              </section>
              {!!data.item.capabilities.length && <section className={`${workspaceCard} space-y-3`}><h2 className="font-semibold">Related capability relationships</h2>
                <ul className="space-y-2">{data.item.capabilities.map(capability => <li key={`${capability.source}:${capability.recordId}`}>
                  <Link className="text-sm text-indigo-700 underline dark:text-indigo-300" to={`${base}/${capability.source}/${encodeURIComponent(capability.recordId)}`}>{capability.name}</Link> · {capability.source === 'local' ? 'Organization support' : 'Provider source'}
                </li>)}</ul></section>}
              <Coverage data={data} selectedControl={selectedControl} onSelect={control => set({ tab: 'coverage', control })} />
            </>}
            {tab === 'coverage' && <>
              {data.controls.some(control => control.confirmedSourceRevision !== null && control.confirmedSourceRevision !== control.availableSourceRevision)
                && <p className={warningClass}>The available source differs from a confirmed snapshot. Review affected controls before confirming.</p>}
              <Coverage data={data} selectedControl={selectedControl} onSelect={control => set({ control })} />
              {selected && <section className={`${workspaceCard} space-y-3`}><h2 className="text-lg font-semibold">{selected.controlId} source and responsibility</h2>
                <dl className="grid gap-3 text-sm md:grid-cols-2">
                  <div><dt className="font-medium">Provider coverage</dt><dd className="whitespace-pre-wrap">{selected.providerCoverage ?? 'Not recorded'}</dd></div>
                  <div><dt className="font-medium">Remaining organization duty</dt><dd className="whitespace-pre-wrap">{selected.organizationDuty ?? 'Not recorded; review required'}</dd></div>
                  <div><dt className="font-medium">Confirmed source revision</dt><dd className="break-all">{selected.confirmedSourceRevision ?? 'Not confirmed'}</dd></div>
                  <div><dt className="font-medium">Available source revision</dt><dd className="break-all">{selected.availableSourceRevision}</dd></div>
                </dl>
              </section>}
              {source === 'provider' ? <>
                <Status loading={responsibility.loading} error={responsibility.error} retry={responsibility.retry} />
                {selected && responsibility.data && <SystemCapabilityResponsibility tenantId={tenantId} systemId={systemId} capabilityId={recordId}
                  controlId={selected.controlId} sourceRevision={selected.availableSourceRevision} reviewRevision={selected.reviewRevision} preview={{ ...responsibility.data,
                    canConfirm: responsibility.data.canConfirm && data.permissions.canReviewResponsibilities }} onChanged={next => {
                    setNotice(next ? 'Responsibility confirmation saved. Narrative acceptance remains a separate action.' : null);
                    responsibility.retry(); detail.retry();
                  }} />}
              </> : <p className={warningClass}>Organization mappings do not establish an inherited allocation. Review current baseline designations through Control Inheritance.</p>}
              <div className="flex flex-wrap gap-3">
                <Link className={secondaryButtonClass} to={`/systems/${encodeURIComponent(systemId)}/inheritance/subscriptions`}>Open full system responsibility review</Link>
                <Link className={secondaryButtonClass} to={`/systems/${encodeURIComponent(systemId)}/inheritance`}>Review control inheritance</Link>
              </div>
            </>}
            {tab === 'evidence' && <SystemCapabilityNarratives tenantId={tenantId} systemId={systemId} detail={data} onChanged={detail.retry} />}
          </div>
        </div>
        <div className={setPageContext ? 'xl:hidden' : undefined}>{context}</div>
      </div>
      {(removalOpen || params.has('operationId') || params.has('removalKey')) && <SystemCapabilityRemoval tenantId={tenantId} systemId={systemId}
        systemName={systemName} detail={data} onClose={closeRemoval} onRefresh={() => { closeRemoval(); detail.retry(); }} />}
    </>}
  </div>;
}

function ApplicabilityContext({ data, systemName, organizationName, onRemove }: {
  data: Detail; systemName: string; organizationName: string; onRemove: () => void;
}) {
  return <aside className={`${workspaceCard} space-y-5`} aria-label="System capability applicability">
    <h2 className="font-semibold">System applicability</h2>
    <dl className="space-y-4 text-sm">
      <div><dt className="text-xs text-slate-500 dark:text-gray-400">System</dt><dd className="mt-1 font-medium">{systemName}</dd></div>
      <div><dt className="text-xs text-slate-500 dark:text-gray-400">Organization</dt><dd className="mt-1">{organizationName}</dd></div>
      <div><dt className="text-xs text-slate-500 dark:text-gray-400">Source owner</dt><dd className="mt-1">{data.item.sourceName}</dd></div>
      <div><dt className="text-xs text-slate-500 dark:text-gray-400">Source revision</dt><dd className="mt-1 break-all text-xs">{data.item.sourceRevision}</dd></div>
      <div><dt className="text-xs text-slate-500 dark:text-gray-400">Library authority</dt><dd className="mt-1">{data.item.mutationAuthority}</dd></div>
      <div><dt className="text-xs text-slate-500 dark:text-gray-400">Responsibility review</dt><dd className="mt-1">{data.item.reviewRequiredCount} affected controls require review</dd></div>
    </dl>
    <Link className="block text-sm text-indigo-700 underline dark:text-indigo-300" to={`/security-capabilities/${data.item.source}/${encodeURIComponent(data.item.recordId)}`}>Open source in organization library</Link>
    <p className="text-xs text-slate-500 dark:text-gray-400">Mappings and subscriptions do not prove complete implementation, inheritance or an ATO.</p>
    {data.item.isApplied && <button type="button" className="border-t border-slate-200 pt-4 text-left text-sm text-red-700 disabled:opacity-50 dark:border-gray-700 dark:text-red-300"
      disabled={!data.permissions.canManage} onClick={onRemove}>Remove from this system</button>}
    {!data.permissions.canManage && <p className="text-xs text-slate-500 dark:text-gray-400">Current system management permission is required to change applicability.</p>}
  </aside>;
}

function Contributor({ component, systemId, providerCapability }: { component: SystemCapabilityComponent; systemId: string; providerCapability: boolean }) {
  return <article className="space-y-3 rounded-lg border border-slate-200 p-4 dark:border-gray-700">
    <div className="flex gap-3"><ComponentIcon type={component.componentType} /><div><h3 className="font-semibold">{component.name}</h3>
      <p className="text-xs text-slate-500 dark:text-gray-400">{component.componentType}{component.subType ? ` · ${component.subType}` : ''}</p></div></div>
    <StateBadge tone={component.source === 'provider' ? 'indigo' : 'neutral'}>{component.source === 'provider' ? 'Provider contributor · Read-only' : providerCapability ? 'Organization supporting contributor' : 'Organization contributor'}</StateBadge>
    <p className="text-sm">{component.sourceName}</p>{component.description && <p className="text-sm text-slate-500 dark:text-gray-400">{component.description}</p>}
    <ul className="space-y-2 text-sm">{component.placements.length ? component.placements.map(placement => <li key={placement.id || `${placement.boundaryId}:${placement.state}`}>
      {placement.boundaryId ? <Link className="text-indigo-700 underline dark:text-indigo-300" to={`/systems/${encodeURIComponent(systemId)}/boundaries?boundaryId=${encodeURIComponent(placement.boundaryId)}`}>{placement.boundaryName ?? placement.boundaryId}</Link> : placement.state === 'SystemWide' ? 'System-wide' : 'Unassigned'}
      {placement.state === 'Excluded' && <span> · Excluded from scope</span>}
    </li>) : <li>Unassigned</li>}</ul>
    {providerCapability && component.source === 'local' && <p className="text-xs text-slate-500 dark:text-gray-400">Local support remains separate from provider authorship.</p>}
    {!!component.capabilities.length && <details className="text-xs"><summary className="cursor-pointer">Delivered capability relationships</summary>
      <ul className="mt-2 space-y-1">{component.capabilities.map(capability => <li key={`${capability.source}:${capability.recordId}`}>{capability.name} · {capability.source}</li>)}</ul></details>}
  </article>;
}

function Coverage({ data, selectedControl, onSelect }: { data: Detail; selectedControl: string; onSelect: (control: string) => void }) {
  return <section className={`${workspaceCard} space-y-4`}><h2 className="text-lg font-semibold">Control coverage &amp; responsibilities</h2>
    <div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead><tr>
      <th className="p-2">Control</th><th className="p-2">Provider coverage</th><th className="p-2">Organization duty</th><th className="p-2">Allocation</th><th className="p-2">Review state</th>
    </tr></thead><tbody className="divide-y divide-slate-200 dark:divide-gray-700">{data.controls.map(control => <tr key={control.controlId} className={selectedControl === control.controlId ? 'bg-indigo-50/60 dark:bg-indigo-950/40' : ''}>
      <td className="p-2"><button type="button" className="text-indigo-700 underline dark:text-indigo-300" onClick={() => onSelect(control.controlId)}>{control.controlId}</button></td>
      <td className="p-2">{control.providerCoverage ?? 'Not recorded'}</td><td className="p-2">{control.organizationDuty ?? 'Not recorded'}</td>
      <td className="p-2">{control.allocation ?? 'Unresolved'}</td><td className="p-2">{control.reviewState}</td>
    </tr>)}</tbody></table></div>
    {!data.controls.length && <p className="text-sm text-slate-500 dark:text-gray-400">No scoped mapped controls are available.</p>}
    {!data.baselineId && <p className={warningClass}>This system has no selected baseline. Mapping a capability does not create one.</p>}
  </section>;
}
