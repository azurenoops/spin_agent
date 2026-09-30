import { useState } from 'react';
import { ProviderBadge, ProviderFact } from './ProviderPresentation';
import { allocationScopeName } from './ProviderAllocationForm';
import type { OfferingBoundaryMission } from './types';

export type HandoffStep = 'hosting' | 'capabilities' | 'documents';

export function MissionReleaseSummary({ relationship }: { relationship: OfferingBoundaryMission }) {
  if (relationship.adoptedCapabilityCount === 0) return <span>Not applied</span>;
  if (!relationship.adoptedReleases?.length)
    return <span>{relationship.adoptedCapabilityCount} adopted capabilities · Exact releases unavailable</span>;
  return <ul className="space-y-2">{relationship.adoptedReleases.map(item => <li key={`${item.capabilityId}:${item.releaseId}`}>
    <span>{item.capabilityName} · Revision {item.revision}</span>
    {item.updateAvailable && <div><ProviderBadge tone="attention">Update available</ProviderBadge></div>}
  </li>)}</ul>;
}

export function ProviderMissionHandoff({ offeringName, relationship, initialStep = 'hosting' }: {
  offeringName: string; relationship: OfferingBoundaryMission; initialStep?: HandoffStep;
}) {
  const [step, setStep] = useState(initialStep);
  return <div className="provider-workspace space-y-4">
    <p className="text-sm">Provider-side, read-only preview for <strong>{relationship.systemName ?? relationship.systemId}</strong>.
      This does not open or impersonate the customer workspace.</p>
    <nav aria-label="Handoff preview steps" className="flex flex-wrap gap-2">
      {([['hosting', 'Hosting association'], ['capabilities', 'Applied capabilities'], ['documents', 'Package contribution']] as const)
        .map(([value, label]) => <button key={value} type="button" aria-pressed={step === value}
          className={step === value ? 'provider-primary' : 'provider-secondary'} onClick={() => setStep(value)}>{label}</button>)}
    </nav>
    <dl>
      <ProviderFact label="Organization">{relationship.targetTenantName ?? 'Name unavailable'}</ProviderFact>
      <ProviderFact label="Offering">{offeringName}</ProviderFact>
      <ProviderFact label="Mission system">{relationship.systemName ?? relationship.systemId}</ProviderFact>
    </dl>
    {step === 'hosting' && <section aria-label="Hosting association preview">
      <h3 className="font-semibold">Confirm the hosting association</h3>
      <p className="mt-2 text-sm">{relationship.associated ? `Recorded relationship: ${relationship.relationshipState}` : 'Awaiting Mission Owner association'}</p>
      <ul className="mt-3 list-disc pl-5 text-sm">{relationship.assignedScopes.map((scope, index) => <li key={index}>{allocationScopeName(scope)}</li>)}</ul>
      <p className="mt-3 text-sm">The Mission Owner reviews this exact allocation in Environment &amp; hosting. Allocation does not grant cloud permissions or establish authorization coverage.</p>
    </section>}
    {step === 'capabilities' && <section aria-label="Capability adoption preview">
      <h3 className="font-semibold">Apply published capabilities</h3>
      <div className="mt-3 text-sm"><MissionReleaseSummary relationship={relationship} /></div>
      <p className="mt-3 text-sm">The Mission Owner selects an applicable published release in Applied capabilities. Customer responsibility confirmation is a separate authorized mission action.</p>
    </section>}
    {step === 'documents' && <section aria-label="Package contribution preview">
      <h3 className="font-semibold">Complete the system package</h3>
      <p className="mt-2 text-sm">The allocated scope, selected releases and permitted evidence contribute to system documentation after mission review.</p>
      <div className="mt-3 text-sm"><MissionReleaseSummary relationship={relationship} /></div>
      <p className="mt-3 text-sm">Private mission narratives, evidence and readiness results are not exposed here. An actual document preview requires authorized mission access; this preview generates no package.</p>
    </section>}
  </div>;
}
