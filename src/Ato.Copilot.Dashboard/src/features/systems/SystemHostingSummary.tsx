import { useCallback, useEffect, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { listProviderRelationships } from '../provider-relationships/api';
import type { PagedResult, ProviderRelationship } from '../provider-relationships/types';
import { scopeLabel } from '../provider-authorizations/scopes';

const relationshipLabels = {
  Undetermined: 'Relationship review required',
  SeparateBoundaryConsumer: 'Separate boundary consumer',
  ExplicitlyCoveredByRecordedScope: 'Covered by recorded provider scope',
};

export default function SystemHostingSummary({ systemId }: { systemId: string }) {
  return <HostingContext key={systemId} systemId={systemId} />;
}

function HostingContext({ systemId }: { systemId: string }) {
  const [page, setPage] = useState(1);
  const [attempt, setAttempt] = useState(0);
  const [data, setData] = useState<PagedResult<ProviderRelationship> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const retry = useCallback(() => setAttempt(value => value + 1), []);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    setData(null);
    void listProviderRelationships(systemId, page, controller.signal).then(result => {
      if (controller.signal.aborted) return;
      if (result.items.some(item => item.systemId.toLowerCase() !== systemId.toLowerCase())) {
        throw new Error('Hosting context does not match the selected system.');
      }
      setData(result);
    }).catch(reason => {
      if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Provider hosting context is unavailable.');
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, page, attempt]);

  const base = `/systems/${encodeURIComponent(systemId)}`;
  return <section aria-labelledby="provider-hosting-heading" className="min-w-0 rounded-xl border border-slate-200 bg-white p-5 dark:border-slate-700 dark:bg-slate-900">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 id="provider-hosting-heading" className="text-lg font-semibold text-slate-900 dark:text-slate-100">Provider hosting</h2>
        <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">Confirm the service scope this system actually uses.</p>
      </div>
      <Link to={`${base}/profile/EnvironmentAndDeployment/hosting`}
        className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700">
        Review hosting scope
      </Link>
    </div>
    {loading && <p role="status" className="mt-4 text-sm">Loading provider hosting context…</p>}
    {error && <div className="mt-4 space-y-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">
      <p role="alert">{error}</p>
      <button type="button" className="rounded border border-amber-400 px-3 py-2 font-medium" onClick={retry}>Retry hosting context</button>
    </div>}
    {!loading && !error && data && <>
      {data.items.length === 0 ? <p className="mt-4 text-sm text-slate-600 dark:text-slate-300">No provider hosting scope is available for this system.</p>
        : <div className="mt-4 space-y-4">
          {data.items.map(item => <article key={item.assignmentId} className="rounded-lg border border-slate-200 p-4 dark:border-slate-700">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div><h3 className="font-semibold">{item.offeringName ?? 'Offering name unavailable'}</h3>
                <p className="text-sm text-slate-600 dark:text-slate-300">{item.providerName ?? 'Provider name unavailable'}</p></div>
              <span className={`rounded-full px-3 py-1 text-xs font-medium ${item.relationshipId && !item.reviewRequired
                ? 'bg-slate-100 text-slate-700' : 'bg-amber-50 text-amber-900'}`}>
                {!item.relationshipId ? 'Available for association'
                  : item.reviewRequired ? 'Review required' : relationshipLabels[item.state]}
              </span>
            </div>
            <dl className="mt-4 grid grid-cols-1 gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
              <div><dt className="text-slate-500">Allocated scope</dt><dd className="break-words font-medium">{item.hostingScopeName ?? 'Scope name unavailable'}</dd></div>
              <div><dt className="text-slate-500">Allocation revision</dt><dd className="font-medium">{item.assignmentRevision}</dd></div>
              <div><dt className="text-slate-500">Recorded authorization source</dt><dd className="break-all">{item.authorizationRevisionId ?? 'Not reviewed'}</dd></div>
              <div><dt className="text-slate-500">Recorded boundary source</dt><dd className="break-all">{item.boundaryRevisionId ?? 'Not reviewed'}</dd></div>
            </dl>
            <details className="mt-3 text-sm">
              <summary className="cursor-pointer font-medium text-indigo-700 dark:text-indigo-300">Allocated resources and retained context</summary>
              <ul className="mt-2 space-y-2">
                {item.assignedScopes.map((scope, index) => <li key={`${scopeLabel(scope)}:${index}`} className="break-all rounded bg-slate-50 p-2 dark:bg-slate-800">
                  {scope.kind === 'Service' ? scopeLabel(scope) : `${scope.cloud} · ${scope.resourceId}`}
                </li>)}
              </ul>
              {item.assignedScopes.length === 0 && <p className="mt-2">No allocated resource identifiers were returned.</p>}
              <p className="mt-2 break-all">Assignment: {item.assignmentId}</p>
              <p className="break-all">Relationship: {item.relationshipId ?? 'Not associated'} · Revision {item.revision}</p>
            </details>
          </article>)}
        </div>}
      {data.total > data.pageSize && <nav aria-label="Hosting context pages" className="mt-4 flex flex-wrap items-center gap-3 text-sm">
        <button type="button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}
          className="rounded border px-3 py-2 disabled:opacity-50">Previous</button>
        <span>Page {page} · {data.total} allocations</span>
        <button type="button" disabled={page * data.pageSize >= data.total} onClick={() => setPage(value => value + 1)}
          className="rounded border px-3 py-2 disabled:opacity-50">Next</button>
      </nav>}
    </>}
    <div className="mt-4 border-t border-slate-200 pt-4 text-sm text-slate-600 dark:border-slate-700 dark:text-slate-300">
      <p>A provider association does not authorize this mission system or establish live collection. Capabilities and customer responsibilities require separate review.</p>
      <div className="mt-3 flex flex-wrap gap-4">
        <Link className="text-indigo-700 underline dark:text-indigo-300" to={`${base}/security-capabilities`}>Review applied capabilities</Link>
        <Link className="text-indigo-700 underline dark:text-indigo-300" to={`${base}/assessments/environment`}>Configure assessment collection</Link>
        <Link className="text-indigo-700 underline dark:text-indigo-300" to={`${base}/conmon`}>Coverage & health</Link>
      </div>
    </div>
  </section>;
}
