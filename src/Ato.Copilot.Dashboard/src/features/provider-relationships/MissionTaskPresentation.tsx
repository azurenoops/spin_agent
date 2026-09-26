import { useEffect, useRef, type ReactNode } from 'react';
import { errorClass, secondaryButtonClass } from '../workspace-operations/workspaceUi';
import type { AzureScope, ApplicableProviderCapability } from './types';

export const taskSteps = ['Select system', 'Choose CSP hosting scope', 'Select security capabilities', 'Review responsibilities', 'Confirm associations'];
export const capabilityName = (item: ApplicableProviderCapability) => item.capabilityName?.trim() || 'Capability name unavailable';

export function TaskFrame({ step, children, withinSystem = false, hostingOnly = false, environmentEntry = false }: {
  step: number; children: ReactNode; withinSystem?: boolean; hostingOnly?: boolean; environmentEntry?: boolean;
}) {
  const heading = useRef<HTMLHeadingElement>(null);
  const Container = withinSystem ? 'section' : 'main';
  const steps = hostingOnly ? ['Confirm system', 'Choose CSP hosting scope', 'Review hosting association']
    : environmentEntry ? ['Choose provider & hosting scope', 'Select capabilities', 'Review proposed duties', 'Save associations', 'Confirm responsibilities'] : taskSteps;
  const currentStep = hostingOnly && step === 4 ? 2 : environmentEntry ? Math.max(0, step - 1) : step;
  useEffect(() => { heading.current?.focus(); }, [step]);
  return <Container aria-label="Mission association task" className="mx-auto max-w-4xl space-y-6 p-4 text-slate-900 sm:p-6 dark:text-slate-100">
    <header className="space-y-3">
      <p className="text-sm font-medium text-indigo-700 dark:text-indigo-300">Mission Owner task · Existing hosting only</p>
      <h1 className="text-2xl font-semibold" ref={heading} tabIndex={-1}>{hostingOnly ? 'Hosting' : 'Associate CSP hosting and capabilities'}</h1>
      <p>{hostingOnly
        ? 'Review existing provider hosting allocated to this system. Record only the hosting association; apply capabilities separately in Security Capabilities.'
        : 'Connect an existing system hosting allocation to published security capabilities. Your authorization boundary and control responsibilities remain separate decisions.'}</p>
      {hostingOnly && <p>Hosting is optional. You can use organization capabilities without a provider.</p>}
    </header>
    <ol aria-label="Association progress" className="flex flex-wrap gap-3 text-sm">
      {steps.map((label, index) => <li key={label} aria-current={index === currentStep ? 'step' : undefined}
        className={`rounded border px-3 py-2 ${index === currentStep ? 'border-indigo-600 font-semibold' : 'border-slate-300'}`}>
        {index + 1}. {label}
      </li>)}
    </ol>
    {children}
  </Container>;
}

export function ReadStatus({ state, name }: {
  state: { loading: boolean; error: string | null; retry: () => void }; name: string;
}) {
  if (state.loading) return <p role="status">Loading {name}…</p>;
  if (!state.error) return null;
  return <div role="alert" className={errorClass}><p>{state.error}</p>
    <p>This step is blocked until the required data can be read.</p>
    <button type="button" className={`${secondaryButtonClass} mt-3`} onClick={state.retry}>Retry {name}</button>
  </div>;
}

export function ScopeDetails({ scopes }: { scopes: AzureScope[] }) {
  return <details className="text-sm"><summary className="cursor-pointer">Hosting scope details</summary>
    <ul className="space-y-3 pt-2">{scopes.map(scope => <li className="break-all" key={`${scope.cloud}:${scope.resourceId}`}>
      <p>Cloud: {scope.cloud}</p><p>Directory: {scope.directoryTenantId}</p>
      <p>Subscription: {scope.subscriptionId}</p><p>Resource scope: {scope.resourceId}</p>
    </li>)}</ul>
  </details>;
}

export function CapabilityResponsibilities({ item }: { item: ApplicableProviderCapability }) {
  const groups = [
    ['Provider coverage', item.providerCoverage], ['Shared duties', item.sharedDuties],
    ['Customer duties', item.customerDuties], ['Outstanding decisions', item.outstandingDecisions],
  ] as const;
  return <article className="space-y-4 rounded border border-slate-300 p-4">
    <h3 className="text-lg font-semibold">{capabilityName(item)}</h3>
    <p>{item.offeringName || 'Offering name unavailable'} · Published version {item.releaseRevision}</p>
    <p>Applicability: {item.applicabilityState} · Authorization relationship: {item.authorizationRelationship}</p>
    {item.relationshipReviewRequired && <p>Authorization relationship review is still required.</p>}
    {groups.map(([title, values]) => <section key={title}>
      <h4 className="font-semibold">{title}</h4>
      {values.length ? <ul className="list-inside list-disc">{values.map((value, index) => <li key={index}>{value}</li>)}</ul>
        : <p>None recorded in this published applicability context.</p>}
    </section>)}
    <section><h4 className="font-semibold">Source references</h4>
      {item.sourceReferences.length ? <ul className="space-y-2">{item.sourceReferences.map(source => <li key={source.referenceId}>
        <p>{source.title} · {source.locator}</p>
        <p className="text-sm">{source.canReadContent ? 'Source content access is checked separately.' : 'Reference metadata only; source content access is not granted.'}</p>
      </li>)}</ul> : <p>No source references were recorded for this release.</p>}
    </section>
    <details><summary>Details</summary><div className="break-all text-sm">
      <p>Capability: {item.capabilityId}</p><p>Release: {item.releaseId}</p>
      <p>Release snapshot: {item.releaseSnapshotHash}</p><p>Context snapshot: {item.applicability.snapshotHash}</p>
    </div></details>
  </article>;
}
