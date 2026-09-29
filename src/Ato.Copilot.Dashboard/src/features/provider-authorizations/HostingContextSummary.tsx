import type { HostingScopeRevision } from './hostingTypes';
import type { ProviderScope } from './types';
import { ProviderBadge } from './ProviderPresentation';
import { offeringEnvironments, scopeLabel } from './scopes';

export function HostingContextSummary({ offeringName, scope }: { offeringName: string; scope: HostingScopeRevision }) {
  return <div className="mb-4 space-y-2">
    <h3 className="font-semibold">{offeringName} · Scope revision {scope.snapshot.revision}</h3>
    {/\bsynthetic\b/i.test(scope.name) && !/\bsynthetic\b/i.test(offeringName) && <ProviderBadge tone="neutral">Synthetic source</ProviderBadge>}
    {scope.purpose?.trim() && <p className="text-sm">{scope.purpose}</p>}
    <details className="provider-record-details"><summary>Hosting provenance</summary>
      <dl className="space-y-2 break-all text-xs">
        <dt>Exact recorded scope name</dt><dd>{scope.name}</dd>
        <dt>Scope revision ID</dt><dd>{scope.snapshot.revisionId}</dd>
        <dt>Scope snapshot hash</dt><dd>{scope.snapshot.snapshotHash}</dd>
      </dl>
    </details>
  </div>;
}

export function HostingScopeIdentity({ scope, compact = false }: { scope: ProviderScope; compact?: boolean }) {
  const segments = scope.kind === 'Service' ? [] : scope.resourceId.split('/').filter(Boolean);
  const lastType = segments.at(-2)?.toLowerCase();
  const label = scope.kind === 'Service' ? scope.serviceName
    : lastType === 'resourcegroups' ? `Resource group · ${segments.at(-1)}`
      : lastType === 'subscriptions' ? 'Subscription scope'
        : `Resource · ${segments.at(-1) || 'Name not recorded'}`;
  return <div>
    <span>{label}</span>
    {compact ? lastType === 'subscriptions' && <small>Subscription identity available in details</small>
      : <small className="block">{offeringEnvironments[scope.kind === 'Service' ? scope.environment : scope.cloud]}</small>}
    {!compact && <>
    <details className="provider-record-details"><summary>Technical scope details</summary>
      <div className="break-all text-xs">{scopeLabel(scope)}</div>
      {scope.kind !== 'Service' && <dl className="space-y-1 break-all text-xs">
        <dt>Directory tenant</dt><dd>{scope.directoryTenantId}</dd>
        <dt>Subscription</dt><dd>{scope.subscriptionId}</dd>
      </dl>}
    </details>
    </>}
  </div>;
}
