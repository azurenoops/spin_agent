import { useCallback, useEffect, useMemo, useState, type KeyboardEvent } from 'react';
import { useLocation, useNavigate } from '../workspaces/workspaceNavigation';

export const inputClass = 'rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-100 dark:focus:ring-indigo-400';
export const buttonClass = 'rounded-md bg-indigo-700 px-3 py-2 text-sm font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50 dark:bg-indigo-500 dark:text-gray-950';
export const secondaryButtonClass = 'rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-semibold text-gray-800 disabled:opacity-50 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-100';
export const surfaceClass = 'rounded border border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-900 dark:text-gray-100';
export const warningClass = 'rounded border border-amber-300 bg-amber-50 p-3 text-amber-950 dark:border-amber-700 dark:bg-amber-950 dark:text-amber-100';
export const errorClass = 'rounded border border-red-300 bg-red-50 p-3 text-red-800 dark:border-red-700 dark:bg-red-950 dark:text-red-100';

export function message(error: unknown) {
  return error instanceof Error ? error.message : 'Unable to complete the request.';
}

export function useQueryState() {
  const location = useLocation();
  const navigate = useNavigate();
  const params = useMemo(() => new URLSearchParams(location.search), [location.search]);
  const set = useCallback((updates: Record<string, string | number | null | undefined>) => {
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries(updates)) {
      if (value === null || value === undefined || value === '') next.delete(key);
      else next.set(key, String(value));
    }
    navigate({ pathname: location.pathname, search: next.toString() ? `?${next}` : '' });
  }, [params, navigate, location.pathname]);
  return { params, set };
}

export function Status({ loading, error, retry }: { loading?: boolean; error?: string | null; retry?: () => void }) {
  if (loading) return <p role="status" className={`${surfaceClass} p-4`}>Loading workspace data…</p>;
  if (!error) return null;
  return <div role="alert" className={`${errorClass} p-4`}>
    <p>{error}</p>
    {retry && <button type="button" className="mt-2 underline" onClick={retry}>Retry</button>}
  </div>;
}

export function Pager({ page, pageSize, total, onPage }: {
  page: number; pageSize: number; total: number; onPage: (page: number) => void;
}) {
  const last = Math.max(1, Math.ceil(total / pageSize));
  return <nav aria-label="Pagination" className="flex items-center justify-between gap-4 py-4 text-sm">
    <span>{total} total {total === 1 ? 'record' : 'records'} · Page {page} of {last}</span>
    <span className="flex gap-2">
      <button type="button" className={secondaryButtonClass} disabled={page <= 1} onClick={() => onPage(page - 1)}>Previous</button>
      <button type="button" className={secondaryButtonClass} disabled={page >= last} onClick={() => onPage(page + 1)}>Next</button>
    </span>
  </nav>;
}

export function moveTabFocus(event: KeyboardEvent<HTMLDivElement>) {
  if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
  const tabs = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="tab"]:not(:disabled)'));
  if (!tabs.length) return;
  const current = Math.max(0, tabs.indexOf(document.activeElement as HTMLButtonElement));
  const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1
    : (current + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
  event.preventDefault();
  tabs[next]?.focus();
  tabs[next]?.click();
}

export function useRemote<T>(load: (signal: AbortSignal) => Promise<T>, keys: unknown[]) {
  const [revision, setRevision] = useState(0);
  const identity = keys.map(key => JSON.stringify(key)).join('|');
  const requestIdentity = `${identity}|${revision}`;
  const [state, setState] = useState<{
    identity: string; data: T | null; error: string | null; loading: boolean;
  }>({ identity: requestIdentity, data: null, error: null, loading: true });
  useEffect(() => {
    const controller = new AbortController();
    setState({ identity: requestIdentity, data: null, error: null, loading: true });
    load(controller.signal).then(value => {
      if (!controller.signal.aborted) {
        setState({ identity: requestIdentity, data: value, error: null, loading: false });
      }
    }).catch(reason => {
      if (!controller.signal.aborted) {
        setState({ identity: requestIdentity, data: null, error: message(reason), loading: false });
      }
    });
    return () => controller.abort();
    // The caller supplies stable scalar request keys.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...keys, revision]);
  const current = state.identity === requestIdentity;
  return {
    data: current ? state.data : null,
    error: current ? state.error : null,
    loading: current ? state.loading : true,
    retry: () => setRevision(value => value + 1),
  };
}

export function ItemList<T>({ items, render }: { items: T[]; render: (item: T) => string }) {
  return items.length ? <ul className={`${surfaceClass} divide-y dark:divide-gray-700`}>{items.map((item, index) =>
    <li key={index} className="p-3">{render(item)}</li>)}</ul> : <p className={`${surfaceClass} p-4`}>No records are available.</p>;
}
