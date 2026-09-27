import { useEffect, useRef, useState } from 'react';
import { validatePackage, type ReadinessResult, type PackagePurpose, type RetainedPackageSelection } from '../../api/package';
import PackagePurposeSelect from '../../components/PackagePurposeSelect';
import RetainedPackageContext from '../../components/RetainedPackageContext';

export default function SystemPackageValidation({ systemId, initialPurpose = 'Legacy' }: {
  systemId: string;
  initialPurpose?: PackagePurpose;
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
    try {
      const next = purpose === 'Legacy'
        ? await validatePackage(systemId, controller.signal)
        : retainedContext ? await validatePackage(systemId, controller.signal, purpose, retainedContext)
          : await validatePackage(systemId, controller.signal, purpose);
      if (!controller.signal.aborted) setResult(next);
    } catch (reason) {
      if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Package validation is unavailable. Retry when access and connectivity are restored.');
    } finally {
      if (!controller.signal.aborted) {
        request.current = null;
        setPending(false);
      }
    }
  };

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
