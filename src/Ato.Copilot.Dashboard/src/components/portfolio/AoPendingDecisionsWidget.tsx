import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import apiClient from '../../api/client';

// ─── Types ────────────────────────────────────────────────────────────────────

interface PendingDecision {
  systemId: string;
  systemName: string;
  decisionType: string;
  expirationDate: string | null;
  daysUntilExpiration: number;
  isOverdue: boolean;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function urgencyClass(d: PendingDecision): string {
  if (d.isOverdue) return 'bg-red-50 border-red-200';
  if (d.daysUntilExpiration < 7) return 'bg-orange-50 border-orange-200';
  return 'bg-yellow-50 border-yellow-200';
}

function urgencyBadge(d: PendingDecision): { label: string; cls: string } {
  if (d.isOverdue)
    return { label: `${Math.abs(d.daysUntilExpiration)}d overdue`, cls: 'bg-red-100 text-red-800' };
  if (d.daysUntilExpiration < 7)
    return { label: `${d.daysUntilExpiration}d left`, cls: 'bg-orange-100 text-orange-800' };
  return { label: `${d.daysUntilExpiration}d left`, cls: 'bg-yellow-100 text-yellow-800' };
}

// ─── Component ────────────────────────────────────────────────────────────────

/**
 * Epic #121 / UF-009 — AO Pending Decisions Widget
 *
 * Surfaces authorizations that are expiring within 30 days or already overdue
 * on the Portfolio Dashboard. Polls GET /api/dashboard/ao/pending-decisions.
 * Hidden when the list is empty.
 */
export default function AoPendingDecisionsWidget() {
  const [decisions, setDecisions] = useState<PendingDecision[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    async function fetchDecisions() {
      try {
        const res = await apiClient.get<PendingDecision[]>('/ao/pending-decisions');
        if (!cancelled) setDecisions(res.data);
      } catch {
        // best-effort — widget is non-critical
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void fetchDecisions();
    const interval = setInterval(() => { void fetchDecisions(); }, 60_000);

    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, []);

  // Hidden while loading or when there are no pending decisions
  if (loading || decisions.length === 0) return null;

  return (
    <div className="mb-6 rounded-lg border border-amber-300 bg-amber-50 shadow-sm">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-amber-200 px-5 py-3">
        <div className="flex items-center gap-2">
          {/* Warning icon */}
          <svg
            className="h-5 w-5 flex-shrink-0 text-amber-600"
            fill="none"
            viewBox="0 0 24 24"
            strokeWidth={1.5}
            stroke="currentColor"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z"
            />
          </svg>
          <h2 className="text-sm font-semibold text-amber-900">
            Authorization Review Required ({decisions.length})
          </h2>
        </div>
        <span className="text-xs text-amber-700">
          Authorizations expiring within 30 days or already overdue
        </span>
      </div>

      {/* Table */}
      <div className="overflow-x-auto">
        <table className="min-w-full">
          <thead>
            <tr className="border-b border-amber-200 bg-amber-100/60">
              {['System', 'Decision Type', 'Expiration Date', 'Status', 'Action'].map((h) => (
                <th
                  key={h}
                  scope="col"
                  className="px-4 py-2 text-left text-xs font-semibold uppercase tracking-wide text-amber-800"
                >
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y divide-amber-100">
            {decisions.map((d) => {
              const { label, cls } = urgencyBadge(d);
              return (
                <tr key={d.systemId} className={`${urgencyClass(d)} border-b border-transparent`}>
                  <td className="px-4 py-3 text-sm font-medium text-gray-900">
                    {d.systemName}
                  </td>
                  <td className="px-4 py-3 text-sm text-gray-700">
                    {d.decisionType}
                  </td>
                  <td className="whitespace-nowrap px-4 py-3 text-sm text-gray-700">
                    {d.expirationDate
                      ? new Date(d.expirationDate).toLocaleDateString('en-US', {
                          year: 'numeric',
                          month: 'short',
                          day: 'numeric',
                        })
                      : '—'}
                  </td>
                  <td className="px-4 py-3">
                    <span
                      className={`inline-flex rounded-full px-2 py-0.5 text-xs font-semibold ${cls}`}
                    >
                      {label}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    <Link
                      to={`/systems/${d.systemId}/authorize`}
                      className="text-xs font-medium text-indigo-600 hover:text-indigo-800 hover:underline"
                    >
                      Review →
                    </Link>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
