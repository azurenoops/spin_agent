import { useEffect, useRef, useState } from 'react';
import { validatePackage, type ReadinessResult, type PackagePurpose, type RetainedPackageSelection } from '../../api/package';
import PackagePurposeSelect from '../../components/PackagePurposeSelect';
import RetainedPackageContext from '../../components/RetainedPackageContext';
import { systemPrimaryAction } from './SystemTaskPresentation';

export default function SystemPackageValidation({ systemId, initialPurpose = 'Legacy', summaryOnly = false, overviewHero = false, onResult, onPurposeChange }: {
  systemId: string;
  initialPurpose?: PackagePurpose;
  summaryOnly?: boolean;
  overviewHero?: boolean;
  onResult?: (result: ReadinessResult | null) => void;
  onPurposeChange?: (purpose: PackagePurpose) => void;
}) {
  const [result, setResult] = useState<ReadinessResult | null>(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [purpose, setPurpose] = useState<PackagePurpose>(initialPurpose);
  const [retainedContext, setRetainedContext] = useState<RetainedPackageSelection | null>(null);
  const requiresContext = purpose === 'AuthorizedBaselineArchive' || purpose === 'ChangeSubmission';
  const request = useRef<AbortController | null>(null);
  useEffect(() => {
    setResult(null);
    setError(null);
    setPending(false);
    setPurpose(initialPurpose);
    setRetainedContext(null);
    return () => { request.current?.abort(); request.current = null; };
  }, [systemId, initialPurpose]);

  const validate = async () => {
    if (request.current) return;
    if (requiresContext && !retainedContext) { setError('Select the retained baseline, decision and any required change preview first.'); return; }
    const controller = new AbortController();
    request.current = controller;
    setPending(true);
    setError(null);
    setResult(null);
    onResult?.(null);
    try {
      const next = purpose === 'Legacy'
        ? await validatePackage(systemId, controller.signal)
        : retainedContext ? await validatePackage(systemId, controller.signal, purpose, retainedContext)
          : await validatePackage(systemId, controller.signal, purpose);
      if (!controller.signal.aborted) { setResult(next); onResult?.(next); }
    } catch (reason) {
      if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Package validation is unavailable. Retry when access and connectivity are restored.');
    } finally {
      if (!controller.signal.aborted) {
        request.current = null;
        setPending(false);
      }
    }
  };

  if (overviewHero) return <section aria-label="Initial submission readiness status" className="system-overview-hero">
    <div className="min-w-0">
      <span role="status" className={`system-overview-badge ${result?.isValid ? 'is-clear' : 'needs-input'}`}>
        {pending ? 'Checking saved records…' : error ? 'Readiness unavailable'
          : result ? result.isValid ? 'No blocking requirements returned'
            : `${result.errorCount} blocking requirement${result.errorCount === 1 ? ' remains' : 's remain'}`
            : 'Readiness not checked'}
      </span>
      <h2>{pending ? 'Checking what your package needs' : error ? 'The readiness check could not finish'
        : result ? result.isValid ? 'Review the returned package findings'
          : 'Your team has requirements to address' : 'Find out what your package needs'}</h2>
      <p>{result ? 'Review the saved-record findings below. A check does not approve, submit or authorize this package.'
        : 'Run a check against saved system records to see missing information and reviews.'}</p>
      <p className="overview-meta">Package purpose: Initial submission</p>
      {result && <p className="overview-meta">{result.warningCount} warnings · Checked {result.validatedAt}. Recheck after changing source records.</p>}
      {error && <p role="alert" className="mt-3 text-sm text-amber-800 dark:text-amber-200">{error}</p>}
    </div>
    <button type="button" disabled={pending} className={systemPrimaryAction} onClick={() => void validate()}>
      {pending ? 'Checking readiness…' : result ? 'Check again' : error ? 'Retry readiness check' : 'Check readiness'}
    </button>
  </section>;
  if (summaryOnly) return <section aria-label="Initial submission readiness status" className="mb-[22px]">
    <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-[#dedaf5] bg-[#f1effc] px-[17px] py-3 text-xs dark:border-indigo-900 dark:bg-indigo-950">
      <span role="status">{pending ? 'Initial submission · Checking requirements…' : error ? 'Initial submission · Readiness unavailable'
        : result ? result.isValid ? 'Initial submission · No blocking requirements returned'
          : `Initial submission · ${result.errorCount} blocking requirement${result.errorCount === 1 ? ' remains' : 's remain'}` : 'Initial submission · Not checked'}</span>
      <button type="button" disabled={pending} className="font-semibold text-indigo-700 disabled:opacity-50 dark:text-indigo-200"
        onClick={() => void validate()}>Check readiness</button>
    </div>
    {error && <p role="alert" className="mt-3 rounded border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">{error}</p>}
    {result && <p className="mt-2 text-xs text-slate-500">{result.warningCount} warnings · Checked {result.validatedAt}. Recheck after changing source records.</p>}
  </section>;
  return <section aria-labelledby="package-validation-title" className="mb-6 rounded-xl border border-slate-200 bg-white p-5 dark:border-slate-700 dark:bg-slate-900">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 id="package-validation-title" className="text-lg font-semibold">Current package validation</h2>
        <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">Review the server&apos;s validation result. Validation does not grant authorization or record a submission.</p>
      </div>
      <PackagePurposeSelect value={purpose} disabled={pending} onChange={value => {
        setPurpose(value);
        setRetainedContext(null);
        setResult(null);
        setError(null);
        onResult?.(null);
        onPurposeChange?.(value);
      }} />
      {requiresContext && <RetainedPackageContext key={`${systemId}:${purpose}`} systemId={systemId} purpose={purpose}
        disabled={pending} onChange={selection => { setRetainedContext(selection); setResult(null); setError(null); }} />}
      <button type="button" onClick={() => void validate()} disabled={pending || requiresContext && !retainedContext}
        className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50">
        {pending ? 'Validating package…' : 'Validate current package'}
      </button>
    </div>
    {pending && <p role="status" className="mt-3 text-sm">Checking the current server records…</p>}
    {error && <p role="alert" className="mt-3 rounded border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">{error}</p>}
    {result && <div className="mt-4 space-y-3">
      <p role="status" className={`rounded-lg p-3 text-sm font-medium ${result.isValid ? 'bg-emerald-50 text-emerald-900' : 'bg-amber-50 text-amber-900'}`}>
        {result.isValid ? 'Current package validation passed' : 'Package validation found blockers'}
        <span className="mt-1 block font-normal">{result.errorCount} errors · {result.warningCount} warnings · Checked {result.validatedAt}</span>
      </p>
      <p className="text-xs text-slate-500">This is a point-in-time result. Revalidate after changing source records.</p>
      {result.findings.map((finding, index) => <article key={`${finding.category}:${index}`} className="rounded-lg border border-slate-200 p-3 text-sm dark:border-slate-700">
        <p className="text-xs font-medium uppercase tracking-wide text-slate-500">{finding.severity} · {finding.category}{finding.artifactType ? ` · ${finding.artifactType}` : ''}</p>
        <h3 className="mt-1 font-medium">{finding.description}</h3>
        {finding.remediation && <p className="mt-2 text-slate-600 dark:text-slate-300">{finding.remediation}</p>}
      </article>)}
    </div>}
  </section>;
}
