import type { DeviationListItem } from '../types/dashboard';

const SEVERITY_LABELS: Record<number, string> = { 0: 'CAT I', 1: 'CAT II', 2: 'CAT III' };
const SEVERITY_COLORS: Record<number, string> = {
  0: 'bg-red-100 text-red-800',
  1: 'bg-yellow-100 text-yellow-800',
  2: 'bg-indigo-100 text-indigo-800',
};
const STATUS_COLORS: Record<string, string> = {
  Pending: 'bg-yellow-100 text-yellow-800',
  Approved: 'bg-green-100 text-green-800',
  Denied: 'bg-red-100 text-red-800',
  Expired: 'bg-gray-100 text-gray-800',
  Revoked: 'bg-orange-100 text-orange-800',
};

const TABS = [
  { key: '', label: 'All' },
  { key: 'FalsePositive', label: 'False Positives' },
  { key: 'RiskAcceptance', label: 'Risk Acceptances' },
  { key: 'Waiver', label: 'Waivers' },
] as const;

interface DeviationTableProps {
  items: DeviationListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  typeFilter: string;
  statusFilter: string;
  severityFilter: string;
  search: string;
  onTypeChange: (type: string) => void;
  onStatusChange: (status: string) => void;
  onSeverityChange: (severity: string) => void;
  onSearchChange: (search: string) => void;
  onPageChange: (page: number) => void;
  onRowClick: (id: string) => void;
}

export default function DeviationTable({
  items,
  totalCount,
  page,
  pageSize,
  typeFilter,
  statusFilter,
  severityFilter,
  search,
  onTypeChange,
  onStatusChange,
  onSeverityChange,
  onSearchChange,
  onPageChange,
  onRowClick,
}: DeviationTableProps) {
  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div className="rounded-lg border border-gray-200 bg-white shadow-sm overflow-hidden">
      {/* Tabs */}
      <div className="border-b border-gray-200">
        <nav className="flex -mb-px">
          {TABS.map((tab) => (
            <button
              key={tab.key}
              onClick={() => onTypeChange(tab.key)}
              className={`px-4 py-2 text-sm font-medium border-b-2 ${
                typeFilter === tab.key
                  ? 'border-indigo-500 text-indigo-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-center gap-3 p-4 border-b border-gray-100">
        <input
          type="text"
          placeholder="Search control ID or justification..."
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          className="rounded border border-gray-300 px-3 py-1.5 text-sm w-64"
        />
        <select
          value={statusFilter}
          onChange={(e) => onStatusChange(e.target.value)}
          className="rounded border border-gray-300 px-2 py-1.5 text-sm"
        >
          <option value="">All Statuses</option>
          <option value="Pending">Pending</option>
          <option value="Approved">Approved</option>
          <option value="Denied">Denied</option>
          <option value="Expired">Expired</option>
          <option value="Revoked">Revoked</option>
        </select>
        <select
          value={severityFilter}
          onChange={(e) => onSeverityChange(e.target.value)}
          className="rounded border border-gray-300 px-2 py-1.5 text-sm"
        >
          <option value="">All Severities</option>
          <option value="CatI">CAT I</option>
          <option value="CatII">CAT II</option>
          <option value="CatIII">CAT III</option>
        </select>
      </div>

      {/* Table */}
      <table className="w-full text-sm">
        <thead className="bg-gray-50 border-b border-gray-200">
          <tr>
            <th className="text-left px-4 py-2 font-medium text-gray-700">Control</th>
            <th className="text-left px-4 py-2 font-medium text-gray-700">Type</th>
            <th className="text-left px-4 py-2 font-medium text-gray-700">Severity</th>
            <th className="text-left px-4 py-2 font-medium text-gray-700">Status</th>
            <th className="text-left px-4 py-2 font-medium text-gray-700">Expires</th>
            <th className="text-left px-4 py-2 font-medium text-gray-700">Requested</th>
            <th className="text-left px-4 py-2 font-medium text-gray-700">Evidence</th>
          </tr>
        </thead>
        <tbody>
          {items.length === 0 ? (
            <tr>
              <td colSpan={7} className="text-center py-8 text-gray-400">
                No deviations found.
              </td>
            </tr>
          ) : (
            items.map((d) => (
              <tr
                key={d.id}
                onClick={() => onRowClick(d.id)}
                className="border-b border-gray-100 last:border-0 hover:bg-gray-50 cursor-pointer"
              >
                <td className="px-4 py-2 font-mono text-xs">{d.controlId}</td>
                <td className="px-4 py-2">{d.deviationType}</td>
                <td className="px-4 py-2">
                  <span className={`inline-block rounded px-2 py-0.5 text-xs font-medium ${SEVERITY_COLORS[d.catSeverity] ?? ''}`}>
                    {SEVERITY_LABELS[d.catSeverity] ?? `CAT ${d.catSeverity}`}
                  </span>
                </td>
                <td className="px-4 py-2">
                  <span className={`inline-block rounded px-2 py-0.5 text-xs font-medium ${STATUS_COLORS[d.status] ?? ''}`}>
                    {d.status}
                  </span>
                </td>
                <td className="px-4 py-2">
                  <span className={d.daysUntilExpiration <= 30 ? 'text-red-600 font-medium' : ''}>
                    {d.daysUntilExpiration}d
                  </span>
                </td>
                <td className="px-4 py-2 text-gray-500 text-xs">
                  {d.requestedBy}
                  <br />
                  {new Date(d.requestedAt).toLocaleDateString()}
                </td>
                <td className="px-4 py-2 text-center">{d.evidenceCount}</td>
              </tr>
            ))
          )}
        </tbody>
      </table>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex items-center justify-between border-t border-gray-200 px-4 py-2">
          <span className="text-xs text-gray-500">
            {totalCount} total · Page {page} of {totalPages}
          </span>
          <div className="flex gap-1">
            <button
              disabled={page <= 1}
              onClick={() => onPageChange(page - 1)}
              className="rounded border px-2 py-1 text-xs disabled:opacity-50"
            >
              Prev
            </button>
            <button
              disabled={page >= totalPages}
              onClick={() => onPageChange(page + 1)}
              className="rounded border px-2 py-1 text-xs disabled:opacity-50"
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
