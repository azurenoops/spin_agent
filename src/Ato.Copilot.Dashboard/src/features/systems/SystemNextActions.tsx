import { useCallback, useEffect, useRef, useState } from 'react';
import { getSystemNextActions } from '../../api/systemNextActions';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import { displayWorkspaceRoles } from '../workspaces/workspaceRoles';
import { Link } from '../workspaces/workspaceNavigation';
import { systemPanel, systemSecondaryAction } from './SystemTaskPresentation';
import { Check } from 'lucide-react';

export default function SystemNextActions({ systemId, onNextPathChange, overview = false }: {
  systemId: string;
  onNextPathChange?: (path: string | null) => void;
  overview?: boolean;
}) {
  const workspace = useWorkspaceSession();
  const scope = JSON.stringify([workspace?.identity.oid, workspace?.target, workspace?.roles]);
  const [data, setData] = useState<Awaited<ReturnType<typeof getSystemNextActions>> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null);
  const refresh = useCallback(async () => {
    if (request.current) return;
    const controller = new AbortController();
    request.current = controller;
    setLoading(true); setError(null); setData(null); onNextPathChange?.(null);
    try {
      const next = await getSystemNextActions(systemId, controller.signal);
      if (!controller.signal.aborted) {
        setData(next); onNextPathChange?.(next.items[0]?.path ?? null);
      }
    } catch (reason) {
      if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message
        : reason && typeof reason === 'object' && 'error' in reason && typeof reason.error === 'string'
          ? reason.error : 'Unable to load next actions for your system roles.');
    } finally {
      if (!controller.signal.aborted) { request.current = null; setLoading(false); }
    }
  }, [systemId, onNextPathChange]);

  useEffect(() => {
    void refresh();
    const onFocus = () => { if (document.visibilityState === 'visible') void refresh(); };
    window.addEventListener('focus', onFocus);
    return () => {
      window.removeEventListener('focus', onFocus);
      request.current?.abort(); request.current = null;
    };
  }, [refresh, scope]);

  const tasks = data?.items.slice(0, 3) ?? [];
  return <section className={overview ? 'system-overview-work' : systemPanel} aria-label="Next actions for your system roles">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <h2 className="text-lg font-semibold">{overview ? 'Your work' : 'Next actions for this system'}</h2>
      {overview && data && <p className="text-xs text-slate-500 dark:text-slate-400">System role: {displayWorkspaceRoles(data.effectiveRoles).join(', ') || 'No actionable role assigned'}</p>}
      <button type="button" disabled={loading} onClick={() => void refresh()}
        className="text-xs text-indigo-700 underline disabled:opacity-50 dark:text-indigo-300">Refresh my tasks</button>
    </div>
    {loading && <p role="status" className="py-4 text-sm text-slate-500">Loading your next actions…</p>}
    {error && <p role="alert" className="mt-4 rounded-lg bg-amber-50 p-3 text-sm text-amber-900">{error}</p>}
    {data && <>
      {!overview && <p className="mt-2 text-xs text-slate-500">Your system roles: {displayWorkspaceRoles(data.effectiveRoles).join(', ') || 'No actionable role assigned'}</p>}
      <div className={overview ? 'mt-4 space-y-3' : 'mt-4 divide-y divide-slate-100 dark:divide-slate-700'}>
        {tasks.map(task => <article key={task.id} className={overview ? 'system-overview-task flex flex-wrap items-center justify-between gap-3' : 'flex flex-wrap items-center justify-between gap-3 py-4'}>
          <div className="min-w-0 flex-1">
            <h3 className="text-sm font-semibold">{task.title}</h3>
            <p className="mt-2 text-sm text-slate-500 dark:text-slate-400">{task.description}</p>
            {overview && <p className="mt-3 text-xs text-slate-500 dark:text-slate-400">Responsible role: {displayWorkspaceRoles([task.responsibleRole]).join(', ')}</p>}
          </div>
          <Link aria-label={`${task.actionLabel}: ${task.title}`} className={systemSecondaryAction}
            to={`/systems/${encodeURIComponent(systemId)}/${task.path}`}>{task.actionLabel} →</Link>
        </article>)}
        {!tasks.length && <div className={overview ? 'system-overview-task flex items-start gap-4 text-sm' : 'py-4 text-sm'}>
          {overview && <span className="overview-empty-icon"><Check size={18} aria-hidden="true" /></span>}
          <div>
          {overview ? <h3 className="font-semibold">No actions assigned to you</h3> : <p>No actions currently require your system roles.</p>}
          <p className="mt-2 text-slate-500 dark:text-slate-400">{overview ? 'You have no pending actions for your current system roles.' : 'Package readiness may still depend on other roles. This is not an authorization or completion of the overall package.'}</p>
          {overview && <p className="mt-3 text-xs text-slate-500 dark:text-slate-400">Other team members may still have work to complete. Check readiness to see the system-wide gaps.</p>}
          </div>
        </div>}
      </div>
      {data.items.length > 3 && <p className="mt-2 text-xs text-slate-500">Showing the next 3 of {data.items.length} actions for your roles.</p>}
      {data.waitingOnOtherRoles.length > 0 && <section aria-label="Waiting on other roles" className="mt-3 rounded-lg bg-slate-50 p-3 text-xs text-slate-600 dark:bg-slate-800 dark:text-slate-300">
        <h3 className="font-semibold">Waiting on other roles — not your actions</h3>
        <ul className="mt-1 space-y-1">{data.waitingOnOtherRoles.map(item => <li key={item.role}>
          {displayWorkspaceRoles([item.role]).join(', ')}: {item.count} pending
        </li>)}</ul>
      </section>}
    </>}
    {!overview && <p className="mt-4 rounded-lg bg-[#f4f5f9] p-4 text-xs leading-relaxed text-slate-700 dark:bg-slate-800 dark:text-slate-200">
      Initial package preparation does not require an already-issued ATO. Your actions refresh from saved records and effective system roles.
      {' '}Saved drafts may still require another role’s review.
    </p>}
  </section>;
}
