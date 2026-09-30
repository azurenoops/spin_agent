import { useState, useCallback } from 'react';
import { useSystemContext } from '../components/layout/SystemLayout';
import { useCreatePoam } from '../hooks/usePoam';
import { usePoamQueue } from '../hooks/usePoamQueue';
import PoamTable from '../components/poam/PoamTable';
import PoamDetailDrawer from '../components/poam/PoamDetailDrawer';
import PoamCreateForm from '../components/poam/PoamCreateForm';
import PoamTrendCharts from '../components/poam/PoamTrendCharts';
import TicketingConfig from '../components/poam/TicketingConfig';
import PoamExportDialog from '../components/poam/PoamExportDialog';
import type { PoamListQuery, CreatePoamRequest } from '../types/poam';
import AsyncErrorState from '../components/AsyncErrorState';
import { useSystemMutationPermission } from '../components/permissions/useSystemMutationPermission';
import { Link, useSearchParams } from '../features/workspaces/workspaceNavigation';
import { SystemTaskHeading, systemPanel, systemPrimaryAction, systemSecondaryAction } from '../features/systems/SystemTaskPresentation';

export default function PoamManagement() {
  const { detail } = useSystemContext();
  return <PoamWorkspace key={detail.systemId} systemId={detail.systemId} />;
}

function PoamWorkspace({ systemId }: { systemId: string }) {
  const canManageRemediation = useSystemMutationPermission(systemId, 'canManageRemediation');
  const [params, setParams] = useSearchParams();
  const [query, setQuery] = useState<PoamListQuery>({ page: 1, pageSize: 25, sortBy: 'scheduledCompletionDate', sortDirection: 'asc', view: 'all' });
  const [showCreate, setShowCreate] = useState(false);
  const [showExport, setShowExport] = useState(false);
  const [tools, setTools] = useState<'trends' | 'ticketing' | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(() => params.get('poam'));
  const { data, loading, error, refresh } = usePoamQueue(systemId, query);
  const { create, loading: creating } = useCreatePoam();
  const counts = data?.counts;
  const closeDetail = () => {
    setSelectedId(null);
    if (params.has('poam')) setParams(previous => { const next = new URLSearchParams(previous); next.delete('poam'); return next; }, { replace: true });
    refresh();
  };
  const handleCreate = useCallback(async (request: CreatePoamRequest) => {
    if (!canManageRemediation) throw new Error('Permission denied: you cannot manage remediation for this system.');
    const result = await create(systemId, request);
    setShowCreate(false);
    setSelectedId(result.id);
    refresh();
  }, [systemId, create, canManageRemediation, refresh]);
  const empty = !error && !loading && data !== null && counts?.all === 0;
  return <div className="space-y-5">
    <SystemTaskHeading title="Track remediation commitments" description="Keep weaknesses, milestones and verified outcomes connected."
      action={<div className="flex flex-wrap gap-2">
        <button className={systemSecondaryAction} onClick={() => setShowExport(true)}>Export</button>
        <button className={systemPrimaryAction} disabled={!canManageRemediation} onClick={() => setShowCreate(true)}>Add POA&amp;M</button>
      </div>} />
    {counts && counts.overdue > 0 && <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-red-200 bg-red-50 px-4 py-3 dark:border-red-900 dark:bg-red-950/30">
      <div><p className="text-sm font-semibold text-red-700 dark:text-red-300">{counts.overdue} {counts.overdue === 1 ? 'commitment has' : 'commitments have'} an overdue deadline or milestone</p><p className="mt-1 text-xs text-slate-600 dark:text-slate-300">Review overdue work to keep remediation on track.</p></div>
      <button className={systemSecondaryAction} onClick={() => setQuery(q => ({ ...q, view: 'overdue', page: 1 }))}>Review overdue work</button>
    </div>}
    <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 dark:border-slate-700">
      <nav aria-label="POA&M views" className="flex flex-wrap gap-4">
        {([
          ['all', 'All items', counts?.all], ['overdue', 'Overdue', counts?.overdue],
          ['ready', 'Ready to verify', counts?.readyToVerify], ['closed', 'Closed', counts?.closed],
        ] as const).map(([view, label, count]) => <button key={view} aria-pressed={query.view === view} disabled={view !== 'all' && !counts}
          className={`border-b-2 pb-3 text-xs font-medium disabled:cursor-not-allowed disabled:opacity-50 ${query.view === view ? 'border-indigo-600 text-indigo-700 dark:text-indigo-300' : 'border-transparent text-slate-500 dark:text-slate-300'}`}
          onClick={() => setQuery(q => ({ ...q, view, page: 1 }))}>{label} ({count ?? '—'})</button>)}
      </nav>
      <details className="relative mb-2 text-xs"><summary className="cursor-pointer rounded px-2 py-2 text-slate-600 dark:text-slate-300">Trends and ticketing</summary>
        <div className="absolute right-0 z-10 mt-1 w-48 space-y-1 rounded-lg border border-slate-200 bg-white p-2 shadow-lg dark:border-slate-700 dark:bg-slate-900">
          <button className="block w-full rounded p-2 text-left hover:bg-slate-100 dark:hover:bg-slate-800" onClick={() => setTools('trends')}>Trends &amp; analytics</button>
          <button className="block w-full rounded p-2 text-left hover:bg-slate-100 dark:hover:bg-slate-800" onClick={() => setTools('ticketing')}>Ticketing configuration</button>
        </div>
      </details>
    </div>
    {tools && <section className={systemPanel}><div className="mb-4 flex justify-between gap-2"><h2 className="font-semibold">{tools === 'trends' ? 'Trends & analytics' : 'Ticketing configuration'}</h2><button className={systemSecondaryAction} onClick={() => setTools(null)}>Hide</button></div>
      {tools === 'trends' ? <PoamTrendCharts systemId={systemId} /> : <TicketingConfig systemId={systemId} />}
    </section>}
    {error && <AsyncErrorState title={data ? 'Unable to refresh POA&M items.' : 'Unable to load POA&M items.'} onRetry={refresh} />}
    {query.view === 'ready' && <p className="text-xs text-slate-500 dark:text-slate-300">Review candidates have completed milestones and, where linked, completed tasks with passed verification. This view is not a closure gate or an approval.</p>}
    {empty ? <section className={`${systemPanel} text-center`}>
      <h2 className="font-semibold">No POA&amp;M items recorded</h2>
      <p className="mx-auto mt-2 max-w-lg text-sm text-slate-500 dark:text-slate-300">Create a commitment from an assessment finding or use Add POA&amp;M to record a manual weakness.</p>
      <Link to={`/systems/${systemId}/remediation`} className={`${systemSecondaryAction} mt-4`}>Review assessment findings</Link>
    </section> : (!error || data) && <PoamTable items={data?.items ?? []} totalItems={data?.totalCount ?? 0} query={{ ...query, page: data?.page ?? query.page }} loading={loading} onQueryChange={setQuery} onRowClick={item => setSelectedId(item.id)} />}
    <section className={systemPanel}><h2 className="text-sm font-semibold">How POA&amp;M items connect</h2>
      <div className="mt-3 grid gap-3 text-xs sm:grid-cols-3">
        <Link to={`/systems/${systemId}/remediation`} className="rounded-lg border border-indigo-100 bg-indigo-50/50 p-3 text-indigo-700 dark:border-indigo-900 dark:bg-indigo-950/40 dark:text-indigo-200"><strong>Assessment &amp; finding</strong><p className="mt-1">Identifies the weakness</p></Link>
        <div className="rounded-lg border border-indigo-100 bg-indigo-50/50 p-3 dark:border-indigo-900 dark:bg-indigo-950/40"><strong>POA&amp;M item</strong><p className="mt-1">Tracks the remediation commitment</p></div>
        <Link to={`/systems/${systemId}/remediation?view=tasks`} className="rounded-lg border border-blue-100 bg-blue-50/50 p-3 text-blue-700 dark:border-blue-900 dark:bg-blue-950/40 dark:text-blue-200"><strong>Remediation tasks</strong><p className="mt-1">Implement fixes and retain verification</p></Link>
      </div>
      <p className="mt-3 text-xs text-slate-500 dark:text-slate-300"><Link className="text-indigo-700 underline dark:text-indigo-300" to={`/systems/${systemId}/deviations`}>Exception records</Link> require a separate authorized decision. Linking does not accept risk or change deadlines.</p>
    </section>
    <details className={systemPanel}><summary className="cursor-pointer text-xs font-semibold">Package contribution &amp; history</summary><p className="mt-3 text-sm text-slate-500 dark:text-slate-300">Retained commitment records support POA&amp;M updates and assessment reporting. Review milestone completion and verification evidence before closure.</p></details>
    {selectedId && <PoamDetailDrawer key={selectedId} poamId={selectedId} systemId={systemId} onClose={closeDetail} />}
    {showCreate && <PoamCreateForm systemId={systemId} onClose={() => setShowCreate(false)} onSubmit={handleCreate} loading={creating} onOpenExisting={id => { setShowCreate(false); setSelectedId(id); }} />}
    {showExport && <PoamExportDialog systemId={systemId} currentStatus={query.status} currentSeverity={query.catSeverity} onClose={() => setShowExport(false)} />}
  </div>;
}
