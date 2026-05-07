import type { PoamListItem, PoamListQuery } from '../../types/poam';

// ─── Shared Badges ──────────────────────────────────────────────────────────

export function SeverityBadge({ severity }: { severity: string }) {
  const map: Record<string, string> = {
    CatI: 'bg-red-100 text-red-700',
    CatII: 'bg-amber-100 text-amber-700',
    CatIII: 'bg-indigo-100 text-indigo-700',
  };
  return <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${map[severity] ?? 'bg-gray-100 text-gray-700'}`}>{severity}</span>;
}

export function StatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    Ongoing: 'bg-indigo-100 text-indigo-700',
    Completed: 'bg-green-100 text-green-700',
    Delayed: 'bg-red-100 text-red-700',
    RiskAccepted: 'bg-purple-100 text-purple-700',
    Cancelled: 'bg-gray-100 text-gray-600',
  };
  return <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${map[status] ?? 'bg-gray-100 text-gray-700'}`}>{status}</span>;
}

function DaysLeft({ date, status }: { date: string; status: string }) {
  if (status === 'Completed' || status === 'Cancelled' || status === 'RiskAccepted') {
    return <span className="text-gray-400">—</span>;
  }
  const days = Math.ceil((new Date(date).getTime() - Date.now()) / (1000 * 60 * 60 * 24));
  if (days < 0) return <span className="font-medium text-red-600">{Math.abs(days)}d overdue</span>;
  if (days <= 30) return <span className="font-medium text-amber-600">{days}d</span>;
  return <span className="text-gray-600">{days}d</span>;
}

// ─── Component ──────────────────────────────────────────────────────────────

interface PoamTableProps {
  items: PoamListItem[];
  totalItems: number;
  query: PoamListQuery;
  loading: boolean;
  onQueryChange: (updater: (prev: PoamListQuery) => PoamListQuery) => void;
  onRowClick: (item: PoamListItem) => void;
}

export default function PoamTable({ items, totalItems, query, loading, onQueryChange, onRowClick }: PoamTableProps) {
  return (
    <div className="overflow-hidden rounded-xl border border-gray-200 bg-white shadow-sm">
      {/* Filter bar */}
      <div className="flex flex-wrap items-center gap-3 border-b border-gray-200 px-4 py-3">
        <input
          type="text"
          placeholder="Search controls, weaknesses, POC..."
          className="w-64 rounded-lg border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          value={query.search ?? ''}
          onChange={e => onQueryChange(q => ({ ...q, search: e.target.value || undefined, page: 1 }))}
        />
        <select
          className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm"
          value={query.status ?? ''}
          onChange={e => onQueryChange(q => ({ ...q, status: e.target.value || undefined, page: 1 }))}
        >
          <option value="">All Statuses</option>
          <option value="Ongoing">Ongoing</option>
          <option value="Completed">Completed</option>
          <option value="Delayed">Delayed</option>
          <option value="RiskAccepted">Risk Accepted</option>
          <option value="Cancelled">Cancelled</option>
        </select>
        <select
          className="rounded-lg border border-gray-300 px-3 py-1.5 text-sm"
          value={query.catSeverity ?? ''}
          onChange={e => onQueryChange(q => ({ ...q, catSeverity: e.target.value || undefined, page: 1 }))}
        >
          <option value="">All Severities</option>
          <option value="CatI">CAT I</option>
          <option value="CatII">CAT II</option>
          <option value="CatIII">CAT III</option>
        </select>
        <label className="flex items-center gap-1.5 text-sm text-gray-600">
          <input
            type="checkbox"
            className="rounded border-gray-300"
            checked={query.overdue ?? false}
            onChange={e => onQueryChange(q => ({ ...q, overdue: e.target.checked || undefined, page: 1 }))}
          />
          Overdue only
        </label>
      </div>

      {/* Table content */}
      {loading && items.length === 0 ? (
        <div className="flex items-center justify-center py-20 text-gray-400">Loading POA&amp;M items...</div>
      ) : items.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-20 text-gray-400">
          <svg className="mb-3 h-12 w-12" fill="none" viewBox="0 0 24 24" strokeWidth={1} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75L11.25 15 15 9.75M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <p className="text-sm font-medium">No POA&amp;M items found</p>
          <p className="mt-1 text-xs">Create your first POA&amp;M item to get started</p>
        </div>
      ) : (
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                {['Control', 'Weakness', 'Severity', 'Status', 'POC', 'Due Date', 'Days Left'].map(h => (
                  <th key={h} className="px-4 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-500">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200 bg-white">
              {items.map(item => (
                <tr
                  key={item.id}
                  className="cursor-pointer hover:bg-gray-50"
                  onClick={() => onRowClick(item)}
                >
                  <td className="whitespace-nowrap px-4 py-3 text-sm font-medium text-gray-900">{item.controlId}</td>
                  <td className="max-w-xs truncate px-4 py-3 text-sm text-gray-700">{item.weakness}</td>
                  <td className="whitespace-nowrap px-4 py-3 text-sm"><SeverityBadge severity={item.catSeverity} /></td>
                  <td className="whitespace-nowrap px-4 py-3 text-sm"><StatusBadge status={item.status} /></td>
                  <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-600">{item.poc}</td>
                  <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-600">{new Date(item.dueDate).toLocaleDateString()}</td>
                  <td className="whitespace-nowrap px-4 py-3 text-sm"><DaysLeft date={item.dueDate} status={item.status} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Pagination */}
      {totalItems > 0 && (
        <div className="flex items-center justify-between border-t border-gray-200 px-4 py-3">
          <div className="text-sm text-gray-500">
            Showing {((query.page ?? 1) - 1) * (query.pageSize ?? 25) + 1}–{Math.min((query.page ?? 1) * (query.pageSize ?? 25), totalItems)} of {totalItems}
          </div>
          <div className="flex items-center gap-2">
            <select
              className="rounded border border-gray-300 px-2 py-1 text-sm"
              value={query.pageSize ?? 25}
              onChange={e => onQueryChange(q => ({ ...q, pageSize: Number(e.target.value), page: 1 }))}
            >
              <option value={25}>25</option>
              <option value={50}>50</option>
              <option value={100}>100</option>
            </select>
            <button
              disabled={(query.page ?? 1) === 1}
              onClick={() => onQueryChange(q => ({ ...q, page: (q.page ?? 1) - 1 }))}
              className="rounded border border-gray-300 px-3 py-1 text-sm disabled:opacity-50"
            >
              Prev
            </button>
            <button
              disabled={(query.page ?? 1) * (query.pageSize ?? 25) >= totalItems}
              onClick={() => onQueryChange(q => ({ ...q, page: (q.page ?? 1) + 1 }))}
              className="rounded border border-gray-300 px-3 py-1 text-sm disabled:opacity-50"
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
