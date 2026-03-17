import { useState, useCallback, useMemo, useRef } from 'react';
import { Link } from 'react-router-dom';
import PageLayout from '../components/layout/PageLayout';
import { usePolling } from '../hooks/usePolling';
import { useSettings } from '../hooks/useSettings';
import { getRemediationSummary, getRemediationTasks, updatePoamStatus, bulkUpdatePoamStatus, moveTask } from '../api/remediation';
import { getPortfolio } from '../api/portfolio';
import type { RemediationSummary, PoamItem, RemediationTask } from '../api/remediation';
import type { PortfolioSystemSummary } from '../types/dashboard';

// ─── Helpers ────────────────────────────────────────────────────────────────

type PoamTab = 'all' | 'Ongoing' | 'Delayed' | 'overdue' | 'Completed' | 'RiskAccepted';
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

function MilestoneBar({ total, completed }: { total: number; completed: number }) {
  if (total === 0) return <span className="text-xs text-gray-400">—</span>;
  const pct = Math.round((completed / total) * 100);
  return (
    <div className="flex items-center gap-2">
      <div className="h-1.5 w-16 rounded-full bg-gray-200">
        <div
          className={`h-1.5 rounded-full ${pct === 100 ? 'bg-green-500' : pct > 50 ? 'bg-blue-500' : 'bg-amber-500'}`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="text-xs text-gray-500">{completed}/{total}</span>
    </div>
  );
}

// ─── Component ──────────────────────────────────────────────────────────────

export default function Remediation() {
  const { settings } = useSettings();
  const [systemFilter, setSystemFilter] = useState('');
  const [activeTab, setActiveTab] = useState<PoamTab>('all');
  const [searchText, setSearchText] = useState('');
  const [viewMode, setViewMode] = useState<ViewMode>(settings.defaultRemediationView);
  const [selectedPoam, setSelectedPoam] = useState<PoamItem | null>(null);
  const [selectedPoamIds, setSelectedPoamIds] = useState<Set<string>>(new Set());
  const [bulkStatus, setBulkStatus] = useState('');
  const [bulkLoading, setBulkLoading] = useState(false);
  const [bulkError, setBulkError] = useState<string | null>(null);
  const [statusUpdateLoading, setStatusUpdateLoading] = useState<string | null>(null);
  const [systems, setSystems] = useState<PortfolioSystemSummary[]>([]);
  const [systemsLoaded, setSystemsLoaded] = useState(false);

  // Load systems for filter dropdown (once)
  const fetchSystems = useCallback(async () => {
    if (systemsLoaded) return systems;
    const res = await getPortfolio({ pageSize: 200 });
    const items = res.items ?? [];
    setSystems(items);
    setSystemsLoaded(true);
    return items;
  }, [systemsLoaded, systems]);
  usePolling(fetchSystems, 0, !systemsLoaded);

  // Main data
  const pollInterval = settings.autoRefreshInterval || undefined;
  const fetchSummary = useCallback(() => getRemediationSummary(systemFilter || undefined), [systemFilter]);
  const { data: summary, loading, error, refresh } = usePolling<RemediationSummary>(fetchSummary, pollInterval);

  const fetchTasks = useCallback(() => getRemediationTasks(systemFilter ? { systemId: systemFilter } : undefined), [systemFilter]);
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

  // Filtered POA&Ms
  const filteredPoams = useMemo(() => {
    if (!summary) return [];
    let items = summary.poams;

    // Tab filter
    if (activeTab === 'overdue') items = items.filter(p => p.isOverdue);
    else if (activeTab !== 'all') items = items.filter(p => p.status === activeTab);

    // Search filter
    if (searchText) {
      const q = searchText.toLowerCase();
      items = items.filter(p =>
        p.controlId.toLowerCase().includes(q) ||
        p.weakness.toLowerCase().includes(q) ||
        p.pointOfContact.toLowerCase().includes(q) ||
        (p.systemName?.toLowerCase().includes(q)) ||
        p.catSeverity.toLowerCase().includes(q)
      );
    }

    return items;
  }, [summary, activeTab, searchText]);

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

  // Bulk selection
  const toggleSelection = (id: string) => {
    setSelectedPoamIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };

  const toggleSelectAll = () => {
    if (selectedPoamIds.size === filteredPoams.length) {
      setSelectedPoamIds(new Set());
    } else {
      setSelectedPoamIds(new Set(filteredPoams.map(p => p.id)));
    }
  };

  // Bulk status update
  const handleBulkUpdate = async () => {
    if (!bulkStatus || selectedPoamIds.size === 0) return;
    setBulkLoading(true);
    setBulkError(null);
    try {
      await bulkUpdatePoamStatus([...selectedPoamIds], bulkStatus);
      setSelectedPoamIds(new Set());
      setBulkStatus('');
      refresh();
    } catch (err) {
      setBulkError(err instanceof Error ? err.message : 'Bulk update failed');
    } finally {
      setBulkLoading(false);
    }
  };

  // Single status update
  const handleStatusUpdate = async (poam: PoamItem, newStatus: string) => {
    setStatusUpdateLoading(poam.id);
    try {
      await updatePoamStatus(poam.registeredSystemId, poam.id, newStatus);
      refresh();
    } catch {
      // silent — will show stale status until next refresh
    } finally {
      setStatusUpdateLoading(null);
    }
  };

  const tabs: { key: PoamTab; label: string; count: number }[] = summary ? [
    { key: 'all', label: 'All', count: summary.totalPoams },
    { key: 'Ongoing', label: 'Ongoing', count: summary.openCount - summary.delayedCount },
    { key: 'Delayed', label: 'Delayed', count: summary.delayedCount },
    { key: 'overdue', label: 'Overdue', count: summary.overdueCount },
    { key: 'Completed', label: 'Completed', count: summary.completedCount },
    { key: 'RiskAccepted', label: 'Risk Accepted', count: summary.riskAcceptedCount },
  ] : [];

  return (
    <PageLayout title="Remediation">
      <div className="space-y-6 p-6">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-xl font-bold text-gray-900">Remediation & POA&M Management</h2>
            <p className="mt-1 text-sm text-gray-500">Track POA&M items, remediation tasks, and risk posture across systems.</p>
          </div>
          <div className="flex items-center gap-3">
            {/* System filter */}
            <select
              value={systemFilter}
              onChange={(e) => { setSystemFilter(e.target.value); setSelectedPoamIds(new Set()); }}
              className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            >
              <option value="">All Systems</option>
              {systems.map(s => (
                <option key={s.systemId} value={s.systemId}>{s.name}</option>
              ))}
            </select>
            {/* Search */}
            <input
              type="text"
              placeholder="Search POA&Ms..."
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
            {/* ──── Summary Cards ──── */}
            <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-4">
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Open POA&Ms</p>
                <p className="mt-1 text-2xl font-bold text-gray-900">{summary.openCount}</p>
              </div>
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Overdue</p>
                <p className={`mt-1 text-2xl font-bold ${summary.overdueCount > 0 ? 'text-red-600' : 'text-gray-900'}`}>{summary.overdueCount}</p>
              </div>
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">CAT I Open</p>
                <p className={`mt-1 text-2xl font-bold ${summary.severityBreakdown.catI > 0 ? 'text-purple-700' : 'text-gray-900'}`}>{summary.severityBreakdown.catI}</p>
              </div>
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Remediation Tasks</p>
                <p className="mt-1 text-2xl font-bold text-blue-600">{summary.totalTasks}</p>
              </div>
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <p className="text-xs font-medium text-gray-500 uppercase tracking-wider">Avg Days to Close</p>
                <p className="mt-1 text-2xl font-bold text-gray-900">{summary.avgDaysToClose}</p>
              </div>
            </div>

            {/* ──── Severity Heatbar + Aging + By-System ──── */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
              {/* Severity Heatbar */}
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <h3 className="text-sm font-semibold text-gray-700 mb-3">Severity Breakdown</h3>
                {summary.openCount > 0 ? (
                  <>
                    <div className="flex h-4 w-full overflow-hidden rounded-full">
                      {summary.severityBreakdown.catIPercent > 0 && (
                        <div className="bg-purple-500" style={{ width: `${summary.severityBreakdown.catIPercent}%` }} title={`CAT I: ${summary.severityBreakdown.catI}`} />
                      )}
                      {summary.severityBreakdown.catIIPercent > 0 && (
                        <div className="bg-red-500" style={{ width: `${summary.severityBreakdown.catIIPercent}%` }} title={`CAT II: ${summary.severityBreakdown.catII}`} />
                      )}
                      {summary.severityBreakdown.catIIIPercent > 0 && (
                        <div className="bg-amber-400" style={{ width: `${summary.severityBreakdown.catIIIPercent}%` }} title={`CAT III: ${summary.severityBreakdown.catIII}`} />
                      )}
                    </div>
                    <div className="mt-2 flex justify-between text-xs text-gray-500">
                      <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-purple-500" /> CAT I: {summary.severityBreakdown.catI}</span>
                      <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-red-500" /> CAT II: {summary.severityBreakdown.catII}</span>
                      <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-amber-400" /> CAT III: {summary.severityBreakdown.catIII}</span>
                    </div>
                  </>
                ) : (
                  <p className="text-sm text-gray-400 text-center py-2">No open POA&Ms</p>
                )}
              </div>

              {/* Aging Chart */}
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <h3 className="text-sm font-semibold text-gray-700 mb-3">POA&M Aging</h3>
                {summary.openCount > 0 ? (
                  <div className="space-y-2">
                    {[
                      { label: '0–30 days', value: summary.aging.days0To30, color: 'bg-green-500' },
                      { label: '31–60 days', value: summary.aging.days31To60, color: 'bg-blue-500' },
                      { label: '61–90 days', value: summary.aging.days61To90, color: 'bg-amber-500' },
                      { label: '90+ days', value: summary.aging.days90Plus, color: 'bg-red-500' },
                    ].map(b => {
                      const max = Math.max(summary.aging.days0To30, summary.aging.days31To60, summary.aging.days61To90, summary.aging.days90Plus, 1);
                      return (
                        <div key={b.label} className="flex items-center gap-2">
                          <span className="w-20 text-xs text-gray-500 text-right">{b.label}</span>
                          <div className="flex-1 h-3 bg-gray-100 rounded-full overflow-hidden">
                            <div className={`h-3 rounded-full ${b.color}`} style={{ width: `${(b.value / max) * 100}%` }} />
                          </div>
                          <span className="w-6 text-xs font-medium text-gray-700 text-right">{b.value}</span>
                        </div>
                      );
                    })}
                  </div>
                ) : (
                  <p className="text-sm text-gray-400 text-center py-2">No open POA&Ms</p>
                )}
              </div>

              {/* By-System Breakdown */}
              <div className="rounded-lg border border-gray-200 bg-white p-4">
                <h3 className="text-sm font-semibold text-gray-700 mb-3">By System</h3>
                {summary.bySystem.length > 0 ? (
                  <div className="space-y-2 max-h-40 overflow-y-auto">
                    {summary.bySystem.map(s => (
                      <div key={s.systemId} className="flex items-center justify-between text-xs">
                        <Link to={`/systems/${s.systemId}`} className="text-blue-600 hover:underline truncate max-w-[120px]">{s.systemName}</Link>
                        <div className="flex items-center gap-3">
                          <span className="text-gray-600">{s.open} open</span>
                          {s.overdue > 0 && <span className="text-red-600 font-medium">{s.overdue} overdue</span>}
                          {s.catI > 0 && <span className="text-purple-700 font-medium">{s.catI} CAT I</span>}
                        </div>
                      </div>
                    ))}
                  </div>
                ) : (
                  <p className="text-sm text-gray-400 text-center py-2">No systems with open POA&Ms</p>
                )}
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
                {/* Tabs */}
                <div className="flex items-center gap-1 border-b border-gray-200">
                  {tabs.map(t => (
                    <button
                      key={t.key}
                      onClick={() => { setActiveTab(t.key); setSelectedPoamIds(new Set()); }}
                      className={`px-3 py-2 text-sm font-medium border-b-2 transition-colors ${
                        activeTab === t.key
                          ? 'border-blue-500 text-blue-700'
                          : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                      }`}
                    >
                      {t.label} <span className="ml-1 text-xs text-gray-400">({t.count})</span>
                    </button>
                  ))}
                </div>

                {/* Bulk Actions */}
                {selectedPoamIds.size > 0 && (
                  <div className="flex items-center gap-3 rounded-lg border border-blue-200 bg-blue-50 px-4 py-2">
                    <span className="text-sm font-medium text-blue-700">{selectedPoamIds.size} selected</span>
                    <select
                      value={bulkStatus}
                      onChange={(e) => setBulkStatus(e.target.value)}
                      className="rounded-md border border-gray-300 bg-white px-2 py-1 text-sm"
                    >
                      <option value="">Change status...</option>
                      <option value="Ongoing">Ongoing</option>
                      <option value="Completed">Completed</option>
                      <option value="Delayed">Delayed</option>
                      <option value="RiskAccepted">Risk Accepted</option>
                    </select>
                    <button
                      onClick={() => void handleBulkUpdate()}
                      disabled={!bulkStatus || bulkLoading}
                      className="rounded-md bg-blue-600 px-3 py-1 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
                    >
                      {bulkLoading ? 'Updating…' : 'Apply'}
                    </button>
                    <button
                      onClick={() => setSelectedPoamIds(new Set())}
                      className="text-sm text-gray-500 hover:text-gray-700"
                    >
                      Clear
                    </button>
                    {bulkError && <span className="text-sm text-red-600">{bulkError}</span>}
                  </div>
                )}

                {/* POA&M Table */}
                <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
                  <table className="min-w-full divide-y divide-gray-200 text-sm">
                    <thead className="bg-gray-50">
                      <tr>
                        <th className="px-3 py-3 text-center w-8">
                          <input
                            type="checkbox"
                            checked={selectedPoamIds.size === filteredPoams.length && filteredPoams.length > 0}
                            onChange={toggleSelectAll}
                            className="rounded border-gray-300"
                          />
                        </th>
                        <th className="px-3 py-3 text-left font-medium text-gray-500">Control</th>
                        {!systemFilter && <th className="px-3 py-3 text-left font-medium text-gray-500">System</th>}
                        <th className="px-3 py-3 text-left font-medium text-gray-500">Weakness</th>
                        <th className="px-3 py-3 text-center font-medium text-gray-500">Severity</th>
                        <th className="px-3 py-3 text-center font-medium text-gray-500">Status</th>
                        <th className="px-3 py-3 text-left font-medium text-gray-500">POC</th>
                        <th className="px-3 py-3 text-left font-medium text-gray-500">Due Date</th>
                        <th className="px-3 py-3 text-center font-medium text-gray-500">Days Left</th>
                        <th className="px-3 py-3 text-center font-medium text-gray-500">Milestones</th>
                        <th className="px-3 py-3 text-center font-medium text-gray-500">Task</th>
                        <th className="px-3 py-3 text-center font-medium text-gray-500">Actions</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {filteredPoams.length === 0 ? (
                        <tr>
                          <td colSpan={systemFilter ? 11 : 12} className="px-4 py-12 text-center text-gray-400">
                            {searchText ? 'No POA&M items match your search.' : 'No POA&M items found.'}
                          </td>
                        </tr>
                      ) : filteredPoams.map(p => (
                        <tr
                          key={p.id}
                          className={`hover:bg-gray-50 cursor-pointer ${selectedPoam?.id === p.id ? 'bg-blue-50' : ''}`}
                          onClick={() => setSelectedPoam(selectedPoam?.id === p.id ? null : p)}
                        >
                          <td className="px-3 py-2.5 text-center" onClick={(e) => e.stopPropagation()}>
                            <input
                              type="checkbox"
                              checked={selectedPoamIds.has(p.id)}
                              onChange={() => toggleSelection(p.id)}
                              className="rounded border-gray-300"
                            />
                          </td>
                          <td className="px-3 py-2.5 font-mono text-xs text-gray-700">{p.controlId}</td>
                          {!systemFilter && (
                            <td className="px-3 py-2.5">
                              {p.registeredSystemId ? (
                                <Link to={`/systems/${p.registeredSystemId}`} className="text-blue-600 hover:underline text-xs" onClick={(e) => e.stopPropagation()}>
                                  {p.systemName ?? 'Unknown'}
                                </Link>
                              ) : <span className="text-xs text-gray-400">—</span>}
                            </td>
                          )}
                          <td className="px-3 py-2.5 text-gray-600 max-w-[200px] truncate" title={p.weakness}>{p.weakness}</td>
                          <td className="px-3 py-2.5 text-center"><CatBadge cat={p.catSeverity} /></td>
                          <td className="px-3 py-2.5 text-center"><StatusBadge status={p.status} /></td>
                          <td className="px-3 py-2.5 text-xs text-gray-600">{p.pointOfContact}</td>
                          <td className="px-3 py-2.5 text-xs text-gray-500 whitespace-nowrap">{formatDate(p.scheduledCompletionDate)}</td>
                          <td className="px-3 py-2.5 text-center">
                            {p.daysRemaining != null ? (
                              <span className={`text-xs font-medium ${p.daysRemaining < 0 ? 'text-red-600' : p.daysRemaining <= 14 ? 'text-amber-600' : 'text-gray-600'}`}>
                                {p.daysRemaining < 0 ? `${Math.abs(p.daysRemaining)}d over` : `${p.daysRemaining}d`}
                              </span>
                            ) : <span className="text-xs text-green-600">Done</span>}
                          </td>
                          <td className="px-3 py-2.5 text-center">
                            <MilestoneBar total={p.milestoneProgress.total} completed={p.milestoneProgress.completed} />
                          </td>
                          <td className="px-3 py-2.5 text-center">
                            {p.remediationTaskId ? (
                              <span className="inline-flex items-center gap-1 text-xs text-green-600" title="Linked to remediation task">
                                <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                  <path strokeLinecap="round" strokeLinejoin="round" d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" />
                                </svg>
                                Linked
                              </span>
                            ) : <span className="text-xs text-gray-400">—</span>}
                          </td>
                          <td className="px-3 py-2.5 text-center" onClick={(e) => e.stopPropagation()}>
                            <button
                              onClick={() => setSelectedPoam(p)}
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
                            className={`rounded-md border border-gray-200 bg-white p-3 shadow-sm hover:shadow transition-all cursor-grab active:cursor-grabbing ${movingTaskId === t.id ? 'opacity-50 animate-pulse' : ''}`}
                          >
                            <div className="flex items-center gap-1.5 mb-1">
                              <span className="text-[10px] font-mono text-gray-400">{t.taskNumber}</span>
                              {t.catSeverity ? <CatBadge cat={t.catSeverity} /> : <SeverityBadge severity={t.severity} />}
                            </div>
                            <p className="text-xs font-medium text-gray-800 line-clamp-2">{t.title}</p>
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

        {/* ──── POA&M Detail Drawer ──── */}
        {selectedPoam && (
          <div
            className="fixed inset-0 z-50 flex justify-end bg-black/30 backdrop-blur-sm"
            onClick={(e) => { if (e.target === e.currentTarget) setSelectedPoam(null); }}
          >
            <div className="w-full max-w-lg bg-white shadow-2xl border-l border-gray-200 flex flex-col overflow-hidden animate-in slide-in-from-right">
              {/* Header */}
              <div className="flex items-center justify-between px-6 pt-5 pb-3 flex-shrink-0 border-b border-gray-100">
                <div>
                  <h3 className="text-lg font-semibold text-gray-900">POA&M Detail</h3>
                  <div className="flex items-center gap-2 mt-1">
                    <span className="font-mono text-sm text-gray-600">{selectedPoam.controlId}</span>
                    <CatBadge cat={selectedPoam.catSeverity} />
                    <StatusBadge status={selectedPoam.status} />
                  </div>
                </div>
                <button
                  onClick={() => setSelectedPoam(null)}
                  className="rounded-lg p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
                >
                  <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>

              {/* Body */}
              <div className="flex-1 overflow-y-auto px-6 py-5 space-y-5">
                {/* Weakness */}
                <div>
                  <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-1">Weakness</h4>
                  <p className="text-sm text-gray-700">{selectedPoam.weakness}</p>
                  <p className="text-xs text-gray-400 mt-1">Source: {selectedPoam.weaknessSource}</p>
                </div>

                {/* Key Info Grid */}
                <div className="grid grid-cols-2 gap-3">
                  <div className="rounded-md border border-gray-200 p-3">
                    <p className="text-xs text-gray-500">Point of Contact</p>
                    <p className="text-sm font-medium text-gray-800">{selectedPoam.pointOfContact}</p>
                    {selectedPoam.pocEmail && <p className="text-xs text-gray-400">{selectedPoam.pocEmail}</p>}
                  </div>
                  <div className="rounded-md border border-gray-200 p-3">
                    <p className="text-xs text-gray-500">Scheduled Completion</p>
                    <p className="text-sm font-medium text-gray-800">{formatDate(selectedPoam.scheduledCompletionDate)}</p>
                    {selectedPoam.daysRemaining != null && (
                      <p className={`text-xs ${selectedPoam.daysRemaining < 0 ? 'text-red-600' : 'text-gray-400'}`}>
                        {selectedPoam.daysRemaining < 0 ? `${Math.abs(selectedPoam.daysRemaining)} days overdue` : `${selectedPoam.daysRemaining} days remaining`}
                      </p>
                    )}
                  </div>
                  {selectedPoam.resourcesRequired && (
                    <div className="rounded-md border border-gray-200 p-3">
                      <p className="text-xs text-gray-500">Resources Required</p>
                      <p className="text-sm text-gray-700">{selectedPoam.resourcesRequired}</p>
                    </div>
                  )}
                  {selectedPoam.costEstimate != null && (
                    <div className="rounded-md border border-gray-200 p-3">
                      <p className="text-xs text-gray-500">Cost Estimate</p>
                      <p className="text-sm font-medium text-gray-800">${selectedPoam.costEstimate.toLocaleString()}</p>
                    </div>
                  )}
                  {selectedPoam.systemName && (
                    <div className="rounded-md border border-gray-200 p-3">
                      <p className="text-xs text-gray-500">System</p>
                      <Link to={`/systems/${selectedPoam.registeredSystemId}`} className="text-sm text-blue-600 hover:underline">
                        {selectedPoam.systemName}
                      </Link>
                    </div>
                  )}
                </div>

                {/* Status Quick-Change */}
                <div>
                  <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2">Change Status</h4>
                  <div className="flex gap-2">
                    {['Ongoing', 'Delayed', 'Completed', 'RiskAccepted'].map(s => (
                      <button
                        key={s}
                        onClick={() => void handleStatusUpdate(selectedPoam, s)}
                        disabled={selectedPoam.status === s || statusUpdateLoading === selectedPoam.id}
                        className={`rounded-md px-2.5 py-1 text-xs font-medium transition-all ${
                          selectedPoam.status === s
                            ? 'bg-blue-100 text-blue-700 ring-1 ring-blue-300'
                            : 'bg-gray-100 text-gray-600 hover:bg-gray-200'
                        } disabled:opacity-50`}
                      >
                        {s === 'RiskAccepted' ? 'Risk Accepted' : s}
                      </button>
                    ))}
                  </div>
                </div>

                {/* Milestones */}
                {selectedPoam.milestones.length > 0 && (
                  <div>
                    <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2">
                      Milestones ({selectedPoam.milestoneProgress.completed}/{selectedPoam.milestoneProgress.total})
                    </h4>
                    <div className="space-y-2">
                      {selectedPoam.milestones.map(m => (
                        <div key={m.id} className={`flex items-start gap-2 rounded-md border p-2.5 ${m.completedDate ? 'border-green-200 bg-green-50' : m.isOverdue ? 'border-red-200 bg-red-50' : 'border-gray-200'}`}>
                          <div className={`mt-0.5 flex-shrink-0 h-4 w-4 rounded-full border-2 flex items-center justify-center ${m.completedDate ? 'border-green-500 bg-green-500' : m.isOverdue ? 'border-red-400' : 'border-gray-300'}`}>
                            {m.completedDate && (
                              <svg className="h-2.5 w-2.5 text-white" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}>
                                <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                              </svg>
                            )}
                          </div>
                          <div className="flex-1 min-w-0">
                            <p className={`text-xs ${m.completedDate ? 'text-green-700 line-through' : 'text-gray-700'}`}>{m.description}</p>
                            <div className="flex gap-2 mt-0.5 text-[10px]">
                              <span className="text-gray-400">Target: {formatDate(m.targetDate)}</span>
                              {m.completedDate && <span className="text-green-600">Done: {formatDate(m.completedDate)}</span>}
                              {m.isOverdue && <span className="text-red-600 font-medium">Overdue</span>}
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Linked Task */}
                {selectedPoam.remediationTaskId && (
                  <div className="rounded-md border border-blue-200 bg-blue-50 p-3">
                    <h4 className="text-xs font-semibold text-blue-700 uppercase tracking-wider mb-1">Linked Remediation Task</h4>
                    <p className="text-xs text-blue-600">Task ID: {selectedPoam.remediationTaskId}</p>
                  </div>
                )}

                {/* Finding Link */}
                {selectedPoam.findingId && (
                  <div className="rounded-md border border-gray-200 bg-gray-50 p-3">
                    <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-1">Originating Finding</h4>
                    <p className="text-xs text-gray-600">Finding ID: {selectedPoam.findingId}</p>
                  </div>
                )}

                {/* Comments */}
                {selectedPoam.comments && (
                  <div>
                    <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-1">Comments</h4>
                    <p className="text-sm text-gray-600 whitespace-pre-wrap">{selectedPoam.comments}</p>
                  </div>
                )}
              </div>
            </div>
          </div>
        )}
      </div>
    </PageLayout>
  );
}
