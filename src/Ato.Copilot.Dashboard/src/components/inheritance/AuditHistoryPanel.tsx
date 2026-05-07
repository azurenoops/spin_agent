import type { AuditEntry } from '../../types/inheritance';

function ChangeSourceBadge({ source }: { source: string }) {
  const map: Record<string, string> = {
    Manual: 'bg-gray-100 text-gray-700',
    BulkUpdate: 'bg-indigo-100 text-indigo-700',
    ProfileApply: 'bg-purple-100 text-purple-700',
    CrmImport: 'bg-green-100 text-green-700',
  };
  return <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${map[source] ?? 'bg-gray-100 text-gray-700'}`}>{source}</span>;
}

function ChangeValue({ label, prev, next }: { label: string; prev?: string | null; next?: string | null }) {
  if (!prev && !next) return null;
  if (prev === next) return null;
  return (
    <div className="text-xs text-gray-600">
      <span className="font-medium">{label}:</span>{' '}
      {prev ? <span className="line-through text-red-500">{prev}</span> : <span className="text-gray-400">—</span>}
      {' → '}
      {next ? <span className="text-green-600">{next}</span> : <span className="text-gray-400">—</span>}
    </div>
  );
}

interface AuditHistoryPanelProps {
  controlId: string | null;
  entries: AuditEntry[];
  loading: boolean;
  onClose: () => void;
}

export default function AuditHistoryPanel({ controlId, entries, loading, onClose }: AuditHistoryPanelProps) {
  if (!controlId) return null;

  return (
    <div className="rounded-xl border border-gray-200 bg-white shadow-sm">
      <div className="flex items-center justify-between border-b border-gray-200 px-4 py-3">
        <h3 className="text-sm font-semibold text-gray-900">Audit History: {controlId}</h3>
        <button
          onClick={onClose}
          className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
        >
          <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
          </svg>
        </button>
      </div>

      <div className="max-h-80 overflow-y-auto p-4">
        {loading ? (
          <div className="flex items-center justify-center py-8 text-gray-400 text-sm">Loading audit history...</div>
        ) : entries.length === 0 ? (
          <div className="flex items-center justify-center py-8 text-gray-400 text-sm">No audit history for this control</div>
        ) : (
          <div className="space-y-3">
            {entries.map(entry => (
              <div key={entry.id} className="rounded-lg border border-gray-100 bg-gray-50 p-3">
                <div className="flex items-center justify-between mb-1">
                  <div className="flex items-center gap-2">
                    <span className="text-xs font-medium text-gray-700">{entry.actor}</span>
                    <ChangeSourceBadge source={entry.changeSource} />
                  </div>
                  <span className="text-xs text-gray-400">{new Date(entry.timestamp).toLocaleString()}</span>
                </div>
                <ChangeValue label="Type" prev={entry.previousInheritanceType} next={entry.newInheritanceType} />
                <ChangeValue label="Provider" prev={entry.previousProvider} next={entry.newProvider} />
                <ChangeValue label="Customer Resp." prev={entry.previousCustomerResponsibility} next={entry.newCustomerResponsibility} />
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
