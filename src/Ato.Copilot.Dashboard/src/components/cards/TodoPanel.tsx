import { useCallback, useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { usePolling } from '../../hooks/usePolling';
import { useSettings } from '../../hooks/useSettings';
import type { TodoList, TodoItem, ProfileTodoResponse } from '../../types/dashboard';
import apiClient from '../../api/client';
import { getProfileTodos } from '../../api/systemProfile';
import TodoActionDialog from './TodoActionDialog';

interface ResolveBlockedInfo {
  gateName: string;
  message: string;
  actionLink: string;
  actionLabel: string;
}

interface TodoPanelProps {
  systemId: string;
}

export default function TodoPanel({ systemId }: TodoPanelProps) {
  const [selectedItem, setSelectedItem] = useState<TodoItem | null>(null);
  const [resolving, setResolving] = useState<string | null>(null);
  const [resolveBlocked, setResolveBlocked] = useState<ResolveBlockedInfo | null>(null);
  const [profileTodos, setProfileTodos] = useState<ProfileTodoResponse | null>(null);
  const { settings } = useSettings();
  const navigate = useNavigate();

  // Fetch profile todos for MissionOwner
  useEffect(() => {
    if (settings.role !== 'MissionOwner') {
      setProfileTodos(null);
      return;
    }
    getProfileTodos(systemId).then(setProfileTodos).catch(() => setProfileTodos(null));
  }, [systemId, settings.role]);

  const fetcher = useCallback(
    () => apiClient.get<TodoList>(`/systems/${systemId}/todos`).then((r) => r.data),
    [systemId],
  );
  const { data, loading, error, refresh } = usePolling(fetcher, 30000);

  const handleResolve = async (item: TodoItem, e: React.MouseEvent) => {
    e.stopPropagation();
    if (!item.deferredId) return;
    setResolving(item.deferredId);
    try {
      await apiClient.post(`/systems/${systemId}/deferred-prerequisites/${item.deferredId}/resolve`);
      refresh();
    } catch (err: unknown) {
      // 422 = gate still failing
      if (err && typeof err === 'object' && 'response' in err) {
        const axiosErr = err as { response?: { status?: number; data?: ResolveBlockedInfo } };
        if (axiosErr.response?.status === 422 && axiosErr.response.data) {
          setResolveBlocked(axiosErr.response.data);
          return;
        }
      }
    } finally {
      setResolving(null);
    }
  };

  if (loading) {
    return (
      <div className="rounded-xl border border-gray-200 bg-white px-5 py-5">
        <div className="h-4 w-16 animate-pulse rounded bg-gray-200" />
      </div>
    );
  }

  if (error || !data || data.items.length === 0) return null;

  const arrow = (
    <svg className="h-4 w-4 flex-shrink-0 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M8.25 4.5l7.5 7.5-7.5 7.5" />
    </svg>
  );

  return (
    <>
      {/* Profile Tasks (MissionOwner only) */}
      {profileTodos?.hasProfileTasks && (
        <div className="rounded-xl border border-indigo-200 bg-indigo-50 mb-3">
          <div className="px-5 pt-3 pb-1">
            <p className="text-xs font-semibold text-indigo-600 uppercase tracking-wider">Your Profile Tasks</p>
          </div>
          <div className="divide-y divide-indigo-100">
            {profileTodos.incompleteSections.map((s) => (
              <button
                key={s.sectionType}
                type="button"
                onClick={() => navigate(`/systems/${systemId}/profile/${s.sectionType}`)}
                className="flex w-full items-center justify-between px-5 py-3 hover:bg-indigo-100 transition-colors text-left"
              >
                <div>
                  <p className="text-sm font-medium text-indigo-900">{s.label}</p>
                  <p className="text-xs text-indigo-600">{s.status}</p>
                </div>
                {arrow}
              </button>
            ))}
            {profileTodos.revisionSections.map((s) => (
              <button
                key={`rev-${s.sectionType}`}
                type="button"
                onClick={() => navigate(`/systems/${systemId}/profile/${s.sectionType}`)}
                className="flex w-full items-center justify-between px-5 py-3 hover:bg-amber-100 bg-amber-50 transition-colors text-left"
              >
                <div>
                  <p className="text-sm font-medium text-amber-900">{s.label} — Needs Revision</p>
                  {s.reviewerComments && <p className="text-xs text-amber-700 mt-0.5">{s.reviewerComments}</p>}
                </div>
                {arrow}
              </button>
            ))}
            {profileTodos.flaggedControls.map((c) => (
              <div key={c.controlId} className="px-5 py-3 text-sm text-indigo-700">
                Business context needed: <span className="font-medium">{c.controlTitle}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="rounded-xl border border-gray-200 bg-white">
        <div className="px-5 pt-3 pb-1">
          <p className="text-xs text-gray-500">
            Phase: {data.currentPhase}{data.nextPhase ? ` → ${data.nextPhase}` : ''}
          </p>
        </div>

        <div className="divide-y divide-gray-100">
          {data.items.map((item) => (
            <button
              key={item.id}
              type="button"
              onClick={() => setSelectedItem(item)}
              className={`flex w-full items-center justify-between gap-4 px-5 py-4 hover:bg-gray-50 transition-colors cursor-pointer text-left ${
                item.category === 'deferred' ? 'bg-amber-50 border-l-4 border-amber-400' : ''
              }`}
            >
              <div className="min-w-0 flex-1">
                <p className={`text-sm font-medium ${item.category === 'deferred' ? 'text-amber-900' : 'text-gray-900'}`}>{item.label}</p>
                <p className={`text-sm mt-0.5 ${item.category === 'deferred' ? 'text-amber-700' : 'text-gray-500'}`}>{item.detail}</p>
              </div>
              {item.category === 'deferred' && item.deferredId ? (
                <span
                  role="button"
                  tabIndex={0}
                  onClick={(e) => void handleResolve(item, e)}
                  onKeyDown={(e) => { if (e.key === 'Enter') void handleResolve(item, e as unknown as React.MouseEvent); }}
                  className="flex-shrink-0 rounded-md border border-green-300 bg-green-50 px-2.5 py-1 text-xs font-medium text-green-700 hover:bg-green-100 transition-colors"
                >
                  {resolving === item.deferredId ? 'Resolving...' : 'Resolve'}
                </span>
              ) : arrow}
            </button>
          ))}
        </div>
      </div>

      {selectedItem && (
        <TodoActionDialog
          item={selectedItem}
          systemName={data.systemName}
          onClose={() => setSelectedItem(null)}
        />
      )}

      {resolveBlocked && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
          onClick={(e) => { if (e.target === e.currentTarget) setResolveBlocked(null); }}
        >
          <div className="w-full max-w-md rounded-xl bg-white shadow-2xl border border-gray-200 overflow-hidden">
            <div className="px-6 pt-5 pb-3">
              <div className="flex items-center gap-2 mb-2">
                <span className="flex h-8 w-8 items-center justify-center rounded-full bg-red-100 text-red-600">
                  <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z" />
                  </svg>
                </span>
                <h3 className="text-lg font-semibold text-gray-900">Cannot Resolve Yet</h3>
              </div>
              <p className="text-sm text-gray-600 mt-2">
                <span className="font-medium text-red-800">{resolveBlocked.gateName}</span> has not been completed yet.
              </p>
              <p className="text-sm text-gray-500 mt-1">{resolveBlocked.message}</p>
            </div>

            <div className="border-t border-gray-100 px-6 py-4">
              <p className="text-xs font-semibold text-gray-500 uppercase tracking-wide mb-3">To resolve this item:</p>
              <button
                type="button"
                onClick={() => {
                  navigate(resolveBlocked.actionLink);
                  setResolveBlocked(null);
                }}
                className="flex w-full items-center gap-3 rounded-lg border border-indigo-200 bg-indigo-50 px-4 py-3 text-left hover:bg-indigo-100 transition-colors"
              >
                <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-lg bg-indigo-100 text-indigo-600">
                  <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 6H5.25A2.25 2.25 0 0 0 3 8.25v10.5A2.25 2.25 0 0 0 5.25 21h10.5A2.25 2.25 0 0 0 18 18.75V10.5m-10.5 6L21 3m0 0h-5.25M21 3v5.25" />
                  </svg>
                </div>
                <div>
                  <p className="text-sm font-medium text-indigo-900">{resolveBlocked.actionLabel}</p>
                  <p className="text-xs text-indigo-700 mt-0.5">Complete this action, then try resolving again</p>
                </div>
              </button>
            </div>

            <div className="border-t border-gray-100 px-6 py-3 bg-gray-50 flex justify-end">
              <button
                type="button"
                onClick={() => setResolveBlocked(null)}
                className="rounded-lg px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 transition-colors"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
