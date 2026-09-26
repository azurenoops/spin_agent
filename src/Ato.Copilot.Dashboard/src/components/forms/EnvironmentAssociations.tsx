import { useState } from 'react';
import { Link } from '../../features/workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import { listAllSystemHostingAllocations } from '../../features/provider-relationships/api';
import type { SystemHostingAllocation } from '../../features/provider-relationships/types';
import { ScopeDetails } from '../../features/provider-relationships/MissionTaskPresentation';
import { listSystemCapabilities } from '../../features/workspace-operations/system-capabilities/systemCapabilityApi';
import { buttonClass, secondaryButtonClass, Status, useRemote } from '../../features/workspace-operations/workspaceUi';
import SetupDialog from '../../features/workspace-operations/SetupDialog';

interface Props {
  systemId: string;
  hostingModel: string;
  description: string;
  readOnly: boolean;
  onPrefill: (values: Record<string, string>) => void;
}

export default function EnvironmentAssociations(props: Props) {
  const session = useWorkspaceSession();
  if (session?.workspace.kind !== 'organization' || !session.workspace.tenantId || session.workspace.mode !== 'ordinary'
    || session.systemAccess?.systemId.toLowerCase() !== props.systemId.toLowerCase()
    || session.systemAccess.permissions.canRead !== true) {
    return <section aria-label="Associated hosting and security capabilities" className="rounded-lg border p-4">
      <h2 className="font-semibold">Associated hosting &amp; security capabilities</h2>
      <p className="mt-2 text-sm">Open this system in its authorized organization workspace to view hosting and security capabilities.</p>
    </section>;
  }
  return <Associations key={`${session.workspace.tenantId}:${props.systemId}`} {...props} tenantId={session.workspace.tenantId} />;
}

function Associations({ systemId, tenantId, hostingModel, description, readOnly, onPrefill }: Props & { tenantId: string }) {
  const hosting = useRemote(signal => listAllSystemHostingAllocations(systemId, signal), [systemId]);
  const capabilities = useRemote(signal => listSystemCapabilities(tenantId, systemId,
    { scope: 'applied', grouping: 'capability', page: 1, pageSize: 10 }, signal), [tenantId, systemId]);
  const [prefill, setPrefill] = useState<SystemHostingAllocation | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const base = `/systems/${encodeURIComponent(systemId)}`;
  const associated = hosting.data?.filter(item => !!item.relationshipId);
  const proposedDescription = prefill
    ? `${prefill.offeringName} - ${prefill.hostingScopeName ?? 'Allocated hosting scope'}${prefill.providerName ? `, provided by ${prefill.providerName}` : ''}.`
    : '';
  return <section aria-labelledby="environment-associations" className="space-y-4 rounded-xl border border-slate-200 bg-slate-50 p-4 dark:border-gray-700 dark:bg-gray-900">
    <h2 id="environment-associations" className="text-lg font-semibold">Associated hosting &amp; security capabilities</h2>
    <p className="text-sm">These are saved associations for this system, not everything available in the library.
      Changing the environment description does not add or remove an association.</p>
    {['CSP-hosted', 'Hybrid'].includes(hostingModel) && <div className="space-y-2">
      <Link className={`inline-block ${buttonClass}`} to={`${base}/profile/EnvironmentAndDeployment/hosting`}>
        Associate hosting &amp; capabilities
      </Link>
      <p className="text-sm">Choose provider, select hosting scope and applicable capabilities, then confirm responsibilities with an authorized reviewer.</p>
      {!readOnly && <p className="text-sm">Save your environment draft before leaving this page to start the association task.</p>}
    </div>}
    <div className="grid gap-5 lg:grid-cols-2">
      <section aria-label="Associated hosting" className="min-w-0 space-y-3">
        <h3 className="font-semibold">Hosting</h3>
        <Status loading={hosting.loading} error={hosting.error} retry={hosting.retry} />
        {associated?.length === 0 && <p className="text-sm">No hosting association is recorded for this system.</p>}
        {associated?.map(item => <article key={item.assignmentId} className="space-y-2 rounded-lg border border-slate-200 bg-white p-3 text-sm dark:border-gray-700 dark:bg-gray-950">
          <h4 className="font-semibold">{item.offeringName}</h4>
          <p>Provider: {item.providerName ?? 'Not recorded'}</p>
          <p>Hosting scope: {item.hostingScopeName ?? 'Name not recorded'}</p>
          <ScopeDetails scopes={item.assignedScopes} />
          {!readOnly && <button type="button" className={secondaryButtonClass} onClick={() => { setPrefill(item); setConfirmed(false); }}>
            Use these hosting details
          </button>}
        </article>)}
      </section>
      <section aria-label="Associated security capabilities" className="min-w-0 space-y-3">
        <h3 className="font-semibold">Security capabilities{capabilities.data ? ` (${capabilities.data.total})` : ''}</h3>
        <Status loading={capabilities.loading} error={capabilities.error} retry={capabilities.retry} />
        {capabilities.data?.total === 0 && <p className="text-sm">No security capabilities are applied to this system yet. Organization capabilities do not require a provider.</p>}
        {capabilities.data && <ul className="space-y-2 text-sm">{capabilities.data.items.map(item =>
          <li key={`${item.source}:${item.recordId}`}><Link className="underline" to={`${base}/security-capabilities/${item.source}/${encodeURIComponent(item.recordId)}`}>
            {item.name}</Link><p className="text-xs">{item.sourceName} &middot; {item.reviewRequiredCount} controls need responsibility review</p></li>)}</ul>}
        <Link className="inline-block text-sm underline" to={`${base}/security-capabilities`}>View all applied capabilities</Link>
        {capabilities.data?.permissions.canManage && <Link className="block text-sm underline" to={`${base}/security-capabilities/add`}>Add from library</Link>}
      </section>
    </div>
    {prefill && <SetupDialog title="Review hosting details" description="Review this saved hosting scope before updating your environment draft. Nothing is saved automatically."
      busy={false} onClose={() => setPrefill(null)}>
      <div className="space-y-4 text-sm">
        <div><h3 className="font-semibold">Current description</h3><p className="whitespace-pre-wrap">{description || 'Not recorded'}</p></div>
        <div><h3 className="font-semibold">Proposed description</h3><p>{proposedDescription}</p></div>
        <p>Hosting model: {hostingModel === 'Hybrid' ? 'Hybrid (retained)' : 'CSP-hosted'}. Provider: {prefill.providerName ?? 'Not recorded'}.</p>
        <p>Network, recovery, availability and operating details remain unchanged. No cloud access, capability subscription or duty acceptance is created.</p>
        <label className="flex items-start gap-2"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />
          I reviewed the selected scope and want to replace the description in my draft.</label>
        <button type="button" className={buttonClass} disabled={!confirmed || readOnly} onClick={() => {
          onPrefill({ hostingModel: hostingModel === 'Hybrid' ? 'Hybrid' : 'CSP-hosted', additionalDetails: proposedDescription });
          setPrefill(null);
        }}>Use in draft</button>
        <p>Use Save Draft on the environment form to persist these changes.</p>
      </div>
    </SetupDialog>}
  </section>;
}
