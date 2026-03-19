import { useState, useCallback, useMemo, useRef, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { usePolling } from '../hooks/usePolling';
import { useSettings } from '../hooks/useSettings';
import { useSystemContext } from '../components/layout/SystemLayout';
import { getRemediationSummary, getRemediationTasks, moveTask } from '../api/remediation';
import { linkTask as poamLinkTask } from '../api/poam';
import { listPoamItems } from '../api/poam';
import { getDeviations } from '../api/deviations';
import type { RemediationSummary, RemediationTask } from '../api/remediation';
import type { DeviationListItem } from '../types/dashboard';
import SyncIndicator from '../components/poam/SyncIndicator';

// ─── Helpers ────────────────────────────────────────────────────────────────

type ViewMode = 'table' | 'kanban';

function formatDate(dt: string | null | undefined): string {
  if (!dt) return '—';
  return new Date(dt).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
}

function CatBadge({ cat }: { cat: string }) {
  const colors: Record<string, string> = {
    CatI: 'bg-purple-100 text-purple-800 ring-purple-600/20',
    CatII: 'bg-red-100 text-red-700 ring-red-600/20',
    CatIII: 'bg-amber-100 text-amber-700 ring-amber-600/20',
  };
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold ring-1 ring-inset ${colors[cat] ?? 'bg-gray-100 text-gray-500 ring-gray-500/20'}`}>
      {cat.replace('Cat', 'CAT ')}
    </span>
  );
}

function StatusBadge({ status }: { status: string }) {
  const colors: Record<string, string> = {
    Ongoing: 'bg-blue-100 text-blue-700',
    Completed: 'bg-green-100 text-green-700',
    Delayed: 'bg-amber-100 text-amber-700',
    RiskAccepted: 'bg-gray-100 text-gray-600',
  };
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${colors[status] ?? 'bg-gray-100 text-gray-500'}`}>
      {status === 'RiskAccepted' ? 'Risk Accepted' : status}
    </span>
  );
}

function SeverityBadge({ severity }: { severity: string }) {
  const colors: Record<string, string> = {
    Critical: 'bg-purple-100 text-purple-800',
    High: 'bg-red-100 text-red-700',
    Medium: 'bg-amber-100 text-amber-700',
    Low: 'bg-blue-100 text-blue-700',
  };
  return (
    <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase ${colors[severity] ?? 'bg-gray-100 text-gray-500'}`}>
      {severity}
    </span>
  );
}

// ─── Component ──────────────────────────────────────────────────────────────

export default function Remediation() {
  const { detail } = useSystemContext();
  const systemId = detail.systemId;
  const { settings } = useSettings();
  const [searchText, setSearchText] = useState('');
  const [viewMode, setViewMode] = useState<ViewMode>(settings.defaultRemediationView);
  const [selectedTask, setSelectedTask] = useState<RemediationTask | null>(null);
  const [linkPickerTask, setLinkPickerTask] = useState<RemediationTask | null>(null);
  const [linkPoamSearch, setLinkPoamSearch] = useState('');
  const [linkPoamResults, setLinkPoamResults] = useState<{ id: string; controlId: string; weakness: string; status: string; hasTask: boolean }[]>([]);
  const [linkLoading, setLinkLoading] = useState(false);
  const [linkError, setLinkError] = useState<string | null>(null);

  // Deviation map: poamEntryId → deviation info for badge display
  const [deviationsByPoam, setDeviationsByPoam] = useState<Map<string, DeviationListItem>>(new Map());
  useEffect(() => {
    getDeviations(systemId, { status: 'Approved', pageSize: 500 })
      .then(resp => {
        const map = new Map<string, DeviationListItem>();
        for (const d of resp.items) {
          if (d.poamEntryId) map.set(d.poamEntryId, d);
        }
        setDeviationsByPoam(map);
      })
      .catch(() => {});
  }, [systemId]);

  // Main data — always scoped to current system
  const pollInterval = settings.autoRefreshInterval || undefined;
  const fetchSummary = useCallback(() => getRemediationSummary(systemId), [systemId]);
  const { data: summary, loading, error, refresh } = usePolling<RemediationSummary>(fetchSummary, pollInterval);

  const fetchTasks = useCallback(() => getRemediationTasks({ systemId }), [systemId]);
  const { data: tasksData, refresh: refreshTasks } = usePolling(fetchTasks, pollInterval);

  // DnD state
  const dragTaskRef = useRef<string | null>(null);
  const [dragOverCol, setDragOverCol] = useState<string | null>(null);
  const [movingTaskId, setMovingTaskId] = useState<string | null>(null);

  const handleDragStart = (e: React.DragEvent, taskId: string) => {
    dragTaskRef.current = taskId;
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', taskId);
    // Make the dragged ghost semi-transparent
    if (e.currentTarget instanceof HTMLElement) {
      e.currentTarget.style.opacity = '0.5';
    }
  };

  const handleDragEnd = (e: React.DragEvent) => {
    dragTaskRef.current = null;
    setDragOverCol(null);
    if (e.currentTarget instanceof HTMLElement) {
      e.currentTarget.style.opacity = '1';
    }
  };

  const handleDragOver = (e: React.DragEvent, colStatus: string) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
    setDragOverCol(colStatus);
  };

  const handleDragLeave = (e: React.DragEvent, colStatus: string) => {
    // Only clear if we're actually leaving the column (not entering a child)
    const related = e.relatedTarget as HTMLElement | null;
    if (!related || !e.currentTarget.contains(related)) {
      if (dragOverCol === colStatus) setDragOverCol(null);
    }
  };

  const handleDrop = async (e: React.DragEvent, targetStatus: string) => {
    e.preventDefault();
    setDragOverCol(null);
    const taskId = dragTaskRef.current;
    dragTaskRef.current = null;
    if (!taskId) return;

    // Find the task to check if it's already in this column
    const allTasks = tasksData?.items ?? [];
    const task = allTasks.find(t => t.id === taskId);
    if (!task || task.status === targetStatus) return;

    setMovingTaskId(taskId);
    try {
      await moveTask(taskId, targetStatus);
      refreshTasks();
      refresh();
    } catch {
      // Silent failure — will show stale state until next poll
    } finally {
      setMovingTaskId(null);
    }
  };

  // Kanban columns
  const kanbanColumns = useMemo(() => {
    const tasks = tasksData?.items ?? [];
    const cols: Record<string, RemediationTask[]> = {
      Backlog: [], ToDo: [], InProgress: [], InReview: [], Blocked: [], Done: [],
    };
    const catOrder: Record<string, number> = { CatI: 0, CatII: 1, CatIII: 2 };
    for (const t of tasks) {
      (cols[t.status] ??= []).push(t);
    }
    // Sort each column: CAT I first, then CAT II, then CAT III, then unlinked
    for (const col of Object.values(cols)) {
      col.sort((a, b) => {
        const aOrder = a.catSeverity ? (catOrder[a.catSeverity] ?? 99) : 99;
        const bOrder = b.catSeverity ? (catOrder[b.catSeverity] ?? 99) : 99;
        if (aOrder !== bOrder) return aOrder - bOrder;
        return new Date(a.dueDate).getTime() - new Date(b.dueDate).getTime();
      });
    }
    return cols;
  }, [tasksData]);

  // Filtered tasks for table view
  const filteredTasks = useMemo(() => {
    const tasks = tasksData?.items ?? [];
    if (!searchText) return tasks;
    const q = searchText.toLowerCase();
    return tasks.filter(t =>
      t.taskNumber.toLowerCase().includes(q) ||
      t.title.toLowerCase().includes(q) ||
      t.controlId.toLowerCase().includes(q) ||
      (t.assigneeName?.toLowerCase().includes(q)) ||
      t.severity.toLowerCase().includes(q)
    );
  }, [tasksData, searchText]);

  // POA&M search for Link to POA&M picker
  const handleLinkPoamSearch = async (query: string) => {
    setLinkPoamSearch(query);
    setLinkError(null);
    if (query.length < 2) { setLinkPoamResults([]); return; }
    try {
      const resp = await listPoamItems(systemId, { search: query, status: 'Ongoing', pageSize: 10 });
      setLinkPoamResults(resp.items.map(p => ({
        id: p.id, controlId: p.controlId, weakness: p.weakness, status: p.status,
        hasTask: !!p.remediationTaskId,
      })));
    } catch {
      setLinkPoamResults([]);
    }
  };

  const handleLinkPoamToTask = async (poamId: string, taskId: string) => {
    setLinkLoading(true);
    setLinkError(null);
    try {
      await poamLinkTask(poamId, { taskId });
      setLinkPickerTask(null);
      setSelectedTask(null);
      refreshTasks();
      refresh();
    } catch (err: unknown) {
      const resp = err && typeof err === 'object' && 'response' in err
        ? (err as { response?: { data?: { error?: string } } }).response?.data?.error
        : null;
      setLinkError(resp || (err instanceof Error ? err.message : 'Failed to link POA&M'));
    } finally {
      setLinkLoading(false);
    }
  };

  return (
    <div className="space-y-6">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-xl font-bold text-gray-900">Remediation Tasks</h2>
            <p className="mt-1 text-sm text-gray-500">Track remediation tasks and manage task lifecycle.</p>
          </div>
          <div className="flex items-center gap-3">
            {/* Search */}
            <input
              type="text"
              placeholder="Search tasks..."
              value={searchText}
              onChange={(e) => setSearchText(e.target.value)}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500 w-48"
            />
            {/* View toggle */}
            <div className="inline-flex rounded-md shadow-sm">
              <button
                onClick={() => setViewMode('table')}
                className={`px-3 py-1.5 text-sm font-medium rounded-l-md border ${viewMode === 'table' ? 'bg-blue-50 text-blue-700 border-blue-300' : 'bg-white text-gray-600 border-gray-300 hover:bg-gray-50'}`}
              >
                Table
              </button>
              <button
                onClick={() => setViewMode('kanban')}
                className={`px-3 py-1.5 text-sm font-medium rounded-r-md border-t border-b border-r ${viewMode === 'kanban' ? 'bg-blue-50 text-blue-700 border-blue-300' : 'bg-white text-gray-600 border-gray-300 hover:bg-gray-50'}`}
              >
                Kanban
              </button>
            </div>
          </div>
        </div>

        {/* Loading / Error */}
        {loading && !summary && (
          <div className="flex items-center justify-center py-16">
            <div className="h-8 w-8 animate-spin rounded-full border-4 border-blue-500 border-t-transparent" />
          </div>
        )}
        {error && (
          <div className="rounded-md bg-red-50 p-4 text-sm text-red-700">{String(error)}</div>
        )}

        {summary && (
          <>
            {/* ──── Task Summary Cards ──── */}
            <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-4">
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Total Tasks</p>
                <p className="mt-1 text-2xl font-bold text-blue-600">{summary.totalTasks}</p>
              </div>
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">In Progress</p>
                <p className="mt-1 text-2xl font-bold text-indigo-600">{summary.tasksByStatus.inProgress}</p>
              </div>
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Blocked</p>
                <p className={`mt-1 text-2xl font-bold ${summary.tasksByStatus.blocked > 0 ? 'text-red-600' : 'text-gray-900'}`}>{summary.tasksByStatus.blocked}</p>
              </div>
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Done</p>
                <p className="mt-1 text-2xl font-bold text-green-600">{summary.tasksByStatus.done}</p>
              </div>
            </div>

            {/* ──── Task Pipeline (mini kanban status bar) ──── */}
            {summary.totalTasks > 0 && (
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <h3 className="text-sm font-semibold text-gray-700 mb-3">Remediation Task Pipeline</h3>
                <div className="flex gap-1 h-6 rounded-full overflow-hidden">
                  {[
                    { key: 'backlog', label: 'Backlog', color: 'bg-gray-400', count: summary.tasksByStatus.backlog },
                    { key: 'todo', label: 'To Do', color: 'bg-blue-400', count: summary.tasksByStatus.todo },
                    { key: 'inProgress', label: 'In Progress', color: 'bg-indigo-500', count: summary.tasksByStatus.inProgress },
                    { key: 'inReview', label: 'In Review', color: 'bg-amber-400', count: summary.tasksByStatus.inReview },
                    { key: 'blocked', label: 'Blocked', color: 'bg-red-500', count: summary.tasksByStatus.blocked },
                    { key: 'done', label: 'Done', color: 'bg-green-500', count: summary.tasksByStatus.done },
                  ].filter(s => s.count > 0).map(s => (
                    <div
                      key={s.key}
                      className={`${s.color} flex items-center justify-center text-white text-[10px] font-medium`}
                      style={{ width: `${(s.count / summary.totalTasks) * 100}%`, minWidth: s.count > 0 ? '24px' : '0' }}
                      title={`${s.label}: ${s.count}`}
                    >
                      {s.count}
                    </div>
                  ))}
                </div>
                <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-gray-500">
                  {[
                    { label: 'Backlog', color: 'bg-gray-400', count: summary.tasksByStatus.backlog },
                    { label: 'To Do', color: 'bg-blue-400', count: summary.tasksByStatus.todo },
                    { label: 'In Progress', color: 'bg-indigo-500', count: summary.tasksByStatus.inProgress },
                    { label: 'In Review', color: 'bg-amber-400', count: summary.tasksByStatus.inReview },
                    { label: 'Blocked', color: 'bg-red-500', count: summary.tasksByStatus.blocked },
                    { label: 'Done', color: 'bg-green-500', count: summary.tasksByStatus.done },
                  ].map(s => (
                    <span key={s.label} className="flex items-center gap-1">
                      <span className={`h-2 w-2 rounded-full ${s.color}`} /> {s.label}: {s.count}
                    </span>
                  ))}
                </div>
              </div>
            )}

            {/* ──── View Switcher: Table vs Kanban ──── */}
            {viewMode === 'table' ? (
              <>
                {/* Task Table */}
                <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
                  <table className="min-w-full divide-y divide-gray-200 text-sm">
                    <thead className="bg-gray-50">
                      <tr>
                        <th className="px-3 py-3 text-left font-medium text-gray-500">Task #</th>
                        <th className="px-3 py-3 text-left font-medium text-gray-500">Title</th>
                        <th className="px-3 py-3 text-left font-medium text-gray-500">Control</th>
                        <th className="px-3 py-3 text-center font-medium text-gray-500">Severity</th>
                        <th className="px-3 py-3 text-center font-medium text-gray-500">Status</th>
                        <th className="px-3 py-3 text-left font-medium text-gray-500">Assignee</th>
                        <th className="px-3 py-3 text-left font-medium text-gray-500">Due Date</th>
                        <th className="px-3 py-3 text-center font-medium text-gray-500">Linked POA&M</th>
                        <th className="px-3 py-3 text-center font-medium text-gray-500">Actions</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {filteredTasks.length === 0 ? (
                        <tr>
                          <td colSpan={9} className="px-4 py-12 text-center text-gray-400">
                            {searchText ? 'No tasks match your search.' : 'No remediation tasks found.'}
                          </td>
                        </tr>
                      ) : filteredTasks.map(t => (
                        <tr
                          key={t.id}
                          className={`hover:bg-gray-50 cursor-pointer ${selectedTask?.id === t.id ? 'bg-blue-50' : ''}`}
                          onClick={() => setSelectedTask(selectedTask?.id === t.id ? null : t)}
                        >
                          <td className="px-3 py-2.5 font-mono text-xs text-gray-400">{t.taskNumber}</td>
                          <td className="px-3 py-2.5 text-xs text-gray-700 max-w-[200px] truncate" title={t.title}>{t.title}</td>
                          <td className="px-3 py-2.5 font-mono text-xs text-gray-700">{t.controlId}</td>
                          <td className="px-3 py-2.5 text-center">
                            {t.catSeverity ? <CatBadge cat={t.catSeverity} /> : <SeverityBadge severity={t.severity} />}
                          </td>
                          <td className="px-3 py-2.5 text-center"><StatusBadge status={t.status} /></td>
                          <td className="px-3 py-2.5 text-xs text-gray-600">{t.assigneeName ?? '—'}</td>
                          <td className="px-3 py-2.5 text-xs text-gray-500 whitespace-nowrap">
                            {t.isOverdue ? (
                              <span className="text-red-600 font-medium">Overdue · {formatDate(t.dueDate)}</span>
                            ) : formatDate(t.dueDate)}
                          </td>
                          <td className="px-3 py-2.5 text-center" onClick={(e) => e.stopPropagation()}>
                            <div className="flex items-center justify-center gap-1">
                            {t.poamItemId ? (
                              <Link
                                to={`/systems/${systemId}/poam?detail=${t.poamItemId}`}
                                className="inline-flex items-center gap-1 rounded-full bg-blue-100 px-2 py-0.5 text-[10px] font-medium text-blue-700 hover:bg-blue-200"
                              >
                                <svg className="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                  <path strokeLinecap="round" strokeLinejoin="round" d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" />
                                </svg>
                                POA&M
                              </Link>
                            ) : <span className="text-xs text-gray-400">—</span>}
                            {t.poamItemId && deviationsByPoam.has(t.poamItemId) && (() => {
                              const dev = deviationsByPoam.get(t.poamItemId!)!;
                              const label = dev.deviationType === 'RiskAcceptance' ? 'Risk Accepted' : dev.deviationType === 'Waiver' ? 'Waiver' : 'FP';
                              const colors = dev.deviationType === 'Waiver' ? 'bg-amber-100 text-amber-700' : dev.deviationType === 'RiskAcceptance' ? 'bg-orange-100 text-orange-700' : 'bg-purple-100 text-purple-700';
                              return (
                                <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-medium ${colors}`}>
                                  {label}
                                </span>
                              );
                            })()}
                            </div>
                          </td>
                          <td className="px-3 py-2.5 text-center" onClick={(e) => e.stopPropagation()}>
                            <button
                              onClick={() => setSelectedTask(t)}
                              className="text-xs text-blue-600 hover:text-blue-800"
                            >
                              Detail
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            ) : (
              /* ──── Kanban Board View ──── */
              <div className="flex gap-3 overflow-x-auto pb-4">
                {Object.entries(kanbanColumns).map(([colStatus, tasks]) => {
                  const colColors: Record<string, { bg: string; border: string; header: string }> = {
                    Backlog: { bg: 'bg-gray-50', border: 'border-gray-200', header: 'text-gray-600' },
                    ToDo: { bg: 'bg-blue-50', border: 'border-blue-200', header: 'text-blue-700' },
                    InProgress: { bg: 'bg-indigo-50', border: 'border-indigo-200', header: 'text-indigo-700' },
                    InReview: { bg: 'bg-amber-50', border: 'border-amber-200', header: 'text-amber-700' },
                    Blocked: { bg: 'bg-red-50', border: 'border-red-200', header: 'text-red-700' },
                    Done: { bg: 'bg-green-50', border: 'border-green-200', header: 'text-green-700' },
                  };
                  const labels: Record<string, string> = { ToDo: 'To Do', InProgress: 'In Progress', InReview: 'In Review' };
                  const c = colColors[colStatus] ?? colColors.Backlog!;
                  const isOver = dragOverCol === colStatus;
                  return (
                    <div
                      key={colStatus}
                      className={`flex-shrink-0 w-64 rounded-lg border-2 transition-colors ${isOver ? 'border-blue-400 bg-blue-50/50 ring-2 ring-blue-200' : `${c.border} ${c.bg}`}`}
                      onDragOver={(e) => handleDragOver(e, colStatus)}
                      onDragLeave={(e) => handleDragLeave(e, colStatus)}
                      onDrop={(e) => void handleDrop(e, colStatus)}
                    >
                      <div className="px-3 py-2 border-b border-gray-200">
                        <div className="flex items-center justify-between">
                          <span className={`text-sm font-semibold ${c.header}`}>{labels[colStatus] ?? colStatus}</span>
                          <span className="rounded-full bg-white px-2 py-0.5 text-xs font-medium text-gray-600 shadow-sm">{tasks.length}</span>
                        </div>
                      </div>
                      <div className={`p-2 space-y-2 max-h-[60vh] overflow-y-auto min-h-[80px] ${isOver ? 'bg-blue-50/30' : ''}`}>
                        {tasks.length === 0 ? (
                          <p className={`text-xs text-center py-4 ${isOver ? 'text-blue-500 font-medium' : 'text-gray-400'}`}>
                            {isOver ? 'Drop here' : 'No tasks'}
                          </p>
                        ) : tasks.map(t => (
                          <div
                            key={t.id}
                            draggable
                            onDragStart={(e) => handleDragStart(e, t.id)}
                            onDragEnd={handleDragEnd}
                            onClick={() => setSelectedTask(t)}
                            className={`rounded-md border border-gray-200 bg-white p-3 shadow-sm hover:shadow transition-all cursor-grab active:cursor-grabbing ${movingTaskId === t.id ? 'opacity-50 animate-pulse' : ''}`}
                          >
                            <div className="flex items-center gap-1.5 mb-1">
                              <span className="text-[10px] font-mono text-gray-400">{t.taskNumber}</span>
                              {t.catSeverity ? <CatBadge cat={t.catSeverity} /> : <SeverityBadge severity={t.severity} />}
                              {t.poamItemId && (
                                <span className="inline-flex items-center rounded-full bg-blue-100 px-1.5 py-0.5 text-[8px] font-bold text-blue-700" title="Linked to POA&M">
                                  POA&M
                                </span>
                              )}
                              {t.poamItemId && deviationsByPoam.has(t.poamItemId) && (() => {
                                const dev = deviationsByPoam.get(t.poamItemId!)!;
                                const label = dev.deviationType === 'RiskAcceptance' ? 'Risk Accepted' : dev.deviationType === 'Waiver' ? 'Waiver' : 'False Positive';
                                const colors = dev.deviationType === 'Waiver' ? 'bg-amber-100 text-amber-700' : dev.deviationType === 'RiskAcceptance' ? 'bg-orange-100 text-orange-700' : 'bg-purple-100 text-purple-700';
                                return (
                                  <span className={`inline-flex items-center rounded-full px-1.5 py-0.5 text-[8px] font-bold ${colors}`} title={`Deviation: ${label}`}>
                                    {label}
                                  </span>
                                );
                              })()}
                            </div>
                            <p className="text-xs font-medium text-gray-800 line-clamp-2">{t.title}</p>
                            {t.componentName && (
                              <p className="text-[10px] text-blue-600 mt-0.5 truncate" title={`Component: ${t.componentName}`}>⬡ {t.componentName}</p>
                            )}
                            <div className="mt-2 flex items-center justify-between text-[10px] text-gray-500">
                              <span className="font-mono">{t.controlId}</span>
                              {t.isOverdue && <span className="text-red-600 font-medium">Overdue</span>}
                              {!t.isOverdue && <span>{formatDate(t.dueDate)}</span>}
                            </div>
                            {t.assigneeName && (
                              <p className="mt-1 text-[10px] text-gray-400 truncate">{t.assigneeName}</p>
                            )}
                          </div>
                        ))}
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </>
        )}

        {/* ──── Task Detail Drawer ──── */}
        {selectedTask && (
          <div
            className="fixed inset-0 z-50 flex justify-end bg-black/30 backdrop-blur-sm"
            onClick={(e) => { if (e.target === e.currentTarget) setSelectedTask(null); }}
          >
            <div className="w-full max-w-lg bg-white shadow-2xl border-l border-gray-200 flex flex-col overflow-hidden animate-in slide-in-from-right">
              {/* Header */}
              <div className="flex items-center justify-between px-6 pt-5 pb-3 flex-shrink-0 border-b border-gray-100">
                <div>
                  <h3 className="text-lg font-semibold text-gray-900">Task Detail</h3>
                  <div className="flex items-center gap-2 mt-1">
                    <span className="font-mono text-sm text-gray-400">{selectedTask.taskNumber}</span>
                    {selectedTask.catSeverity ? <CatBadge cat={selectedTask.catSeverity} /> : <SeverityBadge severity={selectedTask.severity} />}
                    <StatusBadge status={selectedTask.status} />
                  </div>
                </div>
                <button
                  onClick={() => setSelectedTask(null)}
                  className="rounded-lg p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
                >
                  <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>

              {/* Body */}
              <div className="flex-1 overflow-y-auto px-6 py-5 space-y-5">
                {/* Title */}
                <div>
                  <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-1">Title</h4>
                  <p className="text-sm text-gray-700">{selectedTask.title}</p>
                </div>

                {/* Description */}
                {selectedTask.description && (
                  <div>
                    <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-1">Description</h4>
                    <p className="text-sm text-gray-600 whitespace-pre-wrap">{selectedTask.description}</p>
                  </div>
                )}

                {/* Key Info Grid */}
                <div className="grid grid-cols-2 gap-3">
                  <div className="rounded-md border border-gray-200 p-3">
                    <p className="text-xs text-gray-500">Control</p>
                    <p className="text-sm font-medium text-gray-800 font-mono">{selectedTask.controlId}</p>
                  </div>
                  <div className="rounded-md border border-gray-200 p-3">
                    <p className="text-xs text-gray-500">Assignee</p>
                    <p className="text-sm font-medium text-gray-800">{selectedTask.assigneeName ?? 'Unassigned'}</p>
                  </div>
                  <div className="rounded-md border border-gray-200 p-3">
                    <p className="text-xs text-gray-500">Due Date</p>
                    <p className={`text-sm font-medium ${selectedTask.isOverdue ? 'text-red-600' : 'text-gray-800'}`}>
                      {formatDate(selectedTask.dueDate)}
                      {selectedTask.isOverdue && ' (Overdue)'}
                    </p>
                  </div>
                  <div className="rounded-md border border-gray-200 p-3">
                    <p className="text-xs text-gray-500">Status</p>
                    <StatusBadge status={selectedTask.status} />
                  </div>
                  {selectedTask.componentName && (
                    <div className="rounded-md border border-blue-100 bg-blue-50 p-3 col-span-2">
                      <p className="text-xs text-blue-500">Component</p>
                      <p className="text-sm font-medium text-blue-800">⬡ {selectedTask.componentName}</p>
                    </div>
                  )}
                </div>

                {/* POA&M Sync Indicator (T065) */}
                <div>
                  <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2">Linked POA&M</h4>
                  {selectedTask.poamItemId ? (
                    <div className="space-y-2">
                      <SyncIndicator
                        linked={true}
                        linkedEntityName="POA&M Item"
                        linkedEntityId={selectedTask.poamItemId}
                      />
                      <Link
                        to={`/systems/${systemId}/poam?detail=${selectedTask.poamItemId}`}
                        className="inline-flex items-center gap-1 rounded-lg bg-blue-50 px-3 py-1.5 text-xs font-medium text-blue-700 hover:bg-blue-100"
                      >
                        View POA&M →
                      </Link>
                    </div>
                  ) : (
                    <div className="space-y-2">
                      <SyncIndicator linked={false} />
                      <button
                        onClick={() => { setLinkPickerTask(selectedTask); setLinkPoamSearch(''); setLinkPoamResults([]); setLinkError(null); }}
                        className="inline-flex items-center gap-1 rounded-lg bg-indigo-50 px-3 py-1.5 text-xs font-medium text-indigo-700 hover:bg-indigo-100"
                      >
                        <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" />
                        </svg>
                        Link to POA&M
                      </button>
                    </div>
                  )}
                </div>

                {/* Remediation Script */}
                {selectedTask.remediationScript && (
                  <div>
                    <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-1">
                      Remediation Script {selectedTask.remediationScriptType && `(${selectedTask.remediationScriptType})`}
                    </h4>
                    <pre className="rounded-md bg-gray-900 p-3 text-xs text-green-400 overflow-x-auto">{selectedTask.remediationScript}</pre>
                  </div>
                )}

                {/* Validation Criteria */}
                {selectedTask.validationCriteria && (
                  <div>
                    <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-1">Validation Criteria</h4>
                    <p className="text-sm text-gray-600 whitespace-pre-wrap">{selectedTask.validationCriteria}</p>
                  </div>
                )}
              </div>
            </div>
          </div>
        )}

        {/* ──── Link to POA&M Picker Dialog (T067) ──── */}
        {linkPickerTask && (
          <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/30" onClick={() => setLinkPickerTask(null)}>
            <div className="w-full max-w-md rounded-xl bg-white p-6 shadow-xl" onClick={e => e.stopPropagation()}>
              <h3 className="mb-4 text-lg font-bold text-gray-900">Link to POA&M</h3>
              <p className="mb-3 text-sm text-gray-500">Search for an open POA&M item to link to task {linkPickerTask.taskNumber}.</p>
              {linkError && (
                <div className="mb-3 rounded-md bg-red-50 border border-red-200 p-2.5">
                  <p className="text-xs text-red-700">{linkError}</p>
                </div>
              )}
              <input
                type="text"
                placeholder="Search by control ID or weakness..."
                value={linkPoamSearch}
                onChange={(e) => void handleLinkPoamSearch(e.target.value)}
                className="w-full rounded-lg border px-3 py-2 text-sm mb-3"
              />
              <div className="max-h-48 overflow-y-auto space-y-1">
                {linkPoamResults.length === 0 && linkPoamSearch.length >= 2 && (
                  <p className="text-sm text-gray-400 text-center py-4">No matching POA&M items found.</p>
                )}
                {linkPoamResults.filter(p => !p.hasTask).length === 0 && linkPoamResults.length > 0 && (
                  <p className="text-sm text-amber-600 text-center py-2">All matching POA&M items are already linked to tasks.</p>
                )}
                {linkPoamResults.map(p => (
                  <button
                    key={p.id}
                    onClick={() => !p.hasTask && void handleLinkPoamToTask(p.id, linkPickerTask.id)}
                    disabled={linkLoading || p.hasTask}
                    className={`w-full flex items-center justify-between rounded-lg border px-3 py-2 text-sm ${
                      p.hasTask
                        ? 'border-gray-100 bg-gray-50 opacity-50 cursor-not-allowed'
                        : 'border-gray-200 hover:bg-blue-50 disabled:opacity-50'
                    }`}
                  >
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-xs text-gray-600">{p.controlId}</span>
                      <span className="text-gray-700 truncate max-w-[200px]">{p.weakness}</span>
                    </div>
                    <div className="flex items-center gap-1.5">
                      <StatusBadge status={p.status} />
                      {p.hasTask && (
                        <span className="inline-flex items-center rounded-full bg-gray-200 px-1.5 py-0.5 text-[10px] font-medium text-gray-500">
                          Linked
                        </span>
                      )}
                    </div>
                  </button>
                ))}
              </div>
              <div className="flex justify-end mt-4">
                <button onClick={() => setLinkPickerTask(null)} className="rounded-lg bg-gray-100 px-4 py-2 text-sm hover:bg-gray-200">
                  Cancel
                </button>
              </div>
            </div>
          </div>
        )}
    </div>
  );
}
