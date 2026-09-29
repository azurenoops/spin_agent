import { useState } from 'react';
import { getCspDashboardSystems, isUnavailable } from '../csp-dashboard/api';
import { Status, useRemote } from '../workspace-operations/workspaceUi';
import { MutationForm } from './forms';
import { createHostingAssignment, getHostingScope } from './hostingApi';
import { readAllPages } from './providerReadModels';
import { managementArrangements, serviceModels } from './OfferingIdentity';
import type { Offering, ProviderScope } from './types';

export function allocationScopeName(scope: ProviderScope): string {
  if (scope.kind === 'Service') return scope.serviceName;
  const parts = scope.resourceId.split('/').filter(Boolean);
  return parts.length > 2 ? parts[parts.length - 1] ?? scope.resourceId : `Subscription ${scope.subscriptionId}`;
}

export function ProviderAllocationForm({ offering, onSaved, onPendingChange }: {
  offering: Offering; onSaved: () => void; onPendingChange: (pending: boolean) => void;
}) {
  const [tenantId, setTenantId] = useState('');
  const [systemId, setSystemId] = useState('');
  const [selectedScopes, setSelectedScopes] = useState<number[]>([]);
  const [confirmed, setConfirmed] = useState(false);
  const systems = useRemote(signal => readAllPages(async page => {
    const result = await getCspDashboardSystems({ page, pageSize: 100, sort: 'name', order: 'asc' });
    if (isUnavailable(result)) throw new Error(`Customer system lookup unavailable: ${result.reason}.`);
    if (!Array.isArray(result.items) || result.items.some(item => !item.systemId || !item.tenantId || !item.name || !item.orgDisplayName))
      throw new Error('Customer system lookup did not return named organization/system identities.');
    return { ...result, total: result.totalCount };
  }, signal), [offering.offeringId]);
  const scope = useRemote(signal => offering.currentHostingScopeRevisionId
    ? getHostingScope(offering.offeringId, offering.currentHostingScopeRevisionId, signal) : Promise.resolve(null),
  [offering.offeringId, offering.currentHostingScopeRevisionId]);
  const organizations = [...new Map(systems.data?.map(item => [item.tenantId, item.orgDisplayName])).entries()];
  const customerSystems = systems.data?.filter(item => item.tenantId === tenantId) ?? [];
  const selectedSystem = customerSystems.find(item => item.systemId === systemId);
  const assignedScopes = scope.data?.permittedScopes.filter((_, index) => selectedScopes.includes(index)) ?? [];
  const unavailable = systems.loading || !!systems.error || scope.loading || !!scope.error || !scope.data;
  const input = 'mt-1 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm dark:border-slate-600 dark:bg-slate-900';
  const invalidate = () => setConfirmed(false);
  return <div className="space-y-4">
    <Status loading={systems.loading} error={systems.error} retry={systems.retry} />
    <Status loading={scope.loading} error={scope.error} retry={scope.retry} />
    {!scope.loading && !scope.error && !scope.data && <p role="alert">This offering has no current service scope. Configure a scope before assigning it.</p>}
    {systems.data?.length === 0 && <p>No named customer systems are available for assignment.</p>}
    <MutationForm label="Save service assignment" disabled={unavailable}
      submitDisabled={!selectedSystem || !assignedScopes.length || !confirmed} onPendingChange={onPendingChange}
      onSaved={onSaved} submit={async key => {
        if (!scope.data || !selectedSystem || !assignedScopes.length || !confirmed)
          throw new Error('Select the customer, mission system and exact service scope before confirming.');
        const result = await createHostingAssignment(offering.offeringId, {
          targetTenantId: selectedSystem.tenantId, systemId: selectedSystem.systemId,
          hostingScopeRevisionId: scope.data.snapshot.revisionId, assignedScopes, references: [],
        }, key);
        if (result.hostingScope.snapshotHash !== scope.data.snapshot.snapshotHash)
          throw new Error('The returned assignment does not match the reviewed scope snapshot. Retain this operation and reconcile it before continuing.');
      }}>
      <div className="grid gap-4 sm:grid-cols-2">
        <label className="text-sm">Customer organization<select aria-label="Customer organization" className={input} required value={tenantId} onChange={event => {
          setTenantId(event.target.value); setSystemId(''); invalidate();
        }}><option value="">Select customer organization</option>
          {organizations.map(([id, name]) => <option key={id} value={id}>{name}</option>)}
        </select></label>
        <label className="text-sm">Mission system<select aria-label="Mission system" className={input} required disabled={!tenantId} value={systemId}
          onChange={event => { setSystemId(event.target.value); invalidate(); }}>
          <option value="">Select mission system</option>
          {customerSystems.map(item => <option key={item.systemId} value={item.systemId}>{item.name}</option>)}
        </select></label>
        <div className="text-sm"><p className="text-slate-500">Offering</p><p className="mt-1 font-medium">{offering.name}</p></div>
        <div className="text-sm"><p className="text-slate-500">Scope version</p><p className="mt-1">{scope.data ? `Revision ${scope.data.snapshot.revision}` : 'Not available'}</p></div>
        <div className="text-sm sm:col-span-2"><p className="text-slate-500">Service arrangement</p><p className="mt-1">
          {offering.serviceModel ? serviceModels[offering.serviceModel] : 'Not recorded'} · {offering.managementArrangement ? managementArrangements[offering.managementArrangement] : 'Not recorded'}
        </p></div>
      </div>
      <fieldset className="space-y-2"><legend className="mb-2 text-sm font-semibold">Available service scopes</legend>
        {scope.data?.permittedScopes.map((item, index) => <label key={index} className="flex items-start gap-2 text-sm">
          <input type="checkbox" checked={selectedScopes.includes(index)} onChange={event => {
            setSelectedScopes(values => event.target.checked ? [...values, index] : values.filter(value => value !== index)); invalidate();
          }} /><span>{allocationScopeName(item)}</span>
        </label>)}
      </fieldset>
      <details className="text-xs"><summary className="cursor-pointer">Exact selected identities and scope</summary>
        <p className="mt-2 break-all">Organization: {tenantId || 'Not selected'}<br />System: {systemId || 'Not selected'}<br />
          Scope revision: {scope.data?.snapshot.revisionId ?? 'Not available'}</p>
        <pre className="mt-2 max-h-48 overflow-auto whitespace-pre-wrap break-all">{JSON.stringify(assignedScopes, null, 2)}</pre>
      </details>
      <p className="text-xs text-slate-500">Only recorded scopes are selectable. This allocates existing service metadata; it does not provision cloud access, apply capabilities or accept customer duties.</p>
      <label className="flex items-start gap-2 text-sm"><input type="checkbox" required checked={confirmed}
        onChange={event => setConfirmed(event.target.checked)} />I confirm this allocation grants no permissions or authorization coverage.</label>
    </MutationForm>
  </div>;
}
