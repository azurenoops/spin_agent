/**
 * Wave 6 — GAP-016: Audit Log page.
 * Wave 8 — Feature 051 / US10: Login event audit trail (FR-032–FR-038).
 *
 * Surfaces GET /api/audit with filterable, sortable, paginated event table.
 * Login-specific event types (LoginSuccess, LoginFailure, IdleSignOut,
 * SimulationBlocked, ImpersonationStart, ImpersonationEnd, TenantSwitch)
 * include surface + correlationId columns per FR-032.
 *
 * Accessible at /audit (RequireAuth + CSP.Admin / Auditor).
 */
import { useState, useCallback } from 'react';
import PageLayout from '../components/layout/PageLayout';
import PageHero from '../components/layout/PageHero';
import apiClient from '../api/client';
import { usePolling } from '../hooks/usePolling';

interface AuditLogEntry {
  id: string;
  tenantId: string | null;
  actorUserId: string | null;
  actorDisplayName: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  detail: string | null;
  impersonatedTenantName: string | null;
  timestamp: string;
  ipAddress: string | null;
  // Feature 051 / US10 (FR-032) — login-event-specific fields.
  surface: string | null;
  correlationId: string | null;
}

interface AuditLogPage {
  items: AuditLogEntry[];
  page: number;
  pageSize: number;
  totalCount: number;
}

const PAGE_SIZE = 50;

// Feature 051 / US10 — expanded to cover all login event types (FR-032, FR-035).
const ACTION_COLORS: Record<string, string> = {
  Impersonation: 'bg-violet-100 text-violet-800',
  ImpersonationEnd: 'bg-violet-100 text-violet-600',
  ImpersonationStart: 'bg-violet-100 text-violet-800',
  TenantStatusChange: 'bg-amber-100 text-amber-800',
  TenantSwitch: 'bg-amber-100 text-amber-700',
  Login: 'bg-green-100 text-green-700',
  LoginSuccess: 'bg-green-100 text-green-700',
  LoginFailure: 'bg-red-100 text-red-700',
  IdleSignOut: 'bg-orange-100 text-orange-700',
  Logout: 'bg-gray-100 text-gray-600',
  Export: 'bg-indigo-100 text-indigo-700',
  Migration: 'bg-red-100 text-red-700',
  SimulationBlocked: 'bg-rose-100 text-rose-800',
  SimulatedLogin: 'bg-yellow-100 text-yellow-800',
};

function ActionBadge({ action }: { action: string }) {
  const cls = ACTION_COLORS[action] ?? 'bg-gray-100 text-gray-700';
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${cls}`}>
      {action}
    </span>
  );
}

function formatTimestamp(ts: string): string {
  return new Date(ts).toLocaleString('en-US', {
    year: 'numeric', month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  });
}

export default function AuditLogPage() {
  const [page, setPage] = useState(1);
  const [actionFilter, setActionFilter] = useState('');
  const [tenantFilter, setTenantFilter] = useState('');
  const [data, setData] = useState<AuditLogPage | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const query = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
      if (actionFilter) query.set('action', actionFilter);
      if (tenantFilter) query.set('tenantId', tenantFilter);
      const { data: result } = await apiClient.get<AuditLogPage>(`/audit?${query}`);
      setData(result);
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Failed to load audit log.');
    } finally {
      setLoading(false);
    }
  }, [page, actionFilter, tenantFilter]);

  const { refresh } = usePolling(
    useCallback(async () => { await load(); return null; }, [load]),
    30000,
  );

  const totalPages = Math.max(1, Math.ceil((data?.totalCount ?? 0) / PAGE_SIZE));

  return (
    <PageLayout title="Audit Log">
      <PageHero
        eyebrow="Administration"
        title="Platform Audit Log"
        description="All platform events: impersonation, tenant status changes, logins, exports, and migrations."
      />

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <input
          type="text"
          placeholder="Filter by action"
          value={actionFilter}
          onChange={(e) => { setActionFilter(e.target.value); setPage(1); }}
          className="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 w-48"
        />
        <input
          type="text"
          placeholder="Filter by tenant ID"
          value={tenantFilter}
          onChange={(e) => { setTenantFilter(e.target.value); setPage(1); }}
          className="rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 w-48"
        />
        <button
          onClick={() => void load()}
          className="rounded-md bg-indigo-600 px-3 py-2 text-sm font-medium text-white hover:bg-indigo-700"
        >
          Search
        </button>
        <button
          onClick={() => void refresh()}
          className="rounded-md border border-gray-300 px-3 py-2 text-sm text-gray-700 hover:bg-gray-50"
        >
          ↻ Refresh
        </button>
        {data && (
          <span className="text-xs text-gray-400 ml-auto">
            {data.totalCount.toLocaleString()} events · page {page}/{totalPages}
          </span>
        )}
      </div>

      {error && (
        <div className="mb-4 rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{error}</div>
      )}

      <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
        <table className="min-w-full text-sm">
          <thead className="bg-gray-50 text-xs font-medium uppercase tracking-wider text-gray-500">
            <tr>
              <th className="px-4 py-3 text-left">Timestamp</th>
              <th className="px-4 py-3 text-left">Action</th>
              <th className="px-4 py-3 text-left">Actor</th>
              <th className="px-4 py-3 text-left">Surface</th>
              <th className="px-4 py-3 text-left">Entity</th>
              <th className="px-4 py-3 text-left">Detail</th>
              <th className="px-4 py-3 text-left">Correlation ID</th>
              <th className="px-4 py-3 text-left">IP</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {loading && (
              <tr>
                <td colSpan={8} className="px-4 py-8 text-center text-gray-400">
                  <span className="inline-block h-5 w-5 animate-spin rounded-full border-2 border-indigo-500 border-t-transparent mr-2" />
                  Loading audit log…
                </td>
              </tr>
            )}
            {!loading && data?.items.length === 0 && (
              <tr>
                <td colSpan={8} className="px-4 py-8 text-center text-gray-400">No events match your filters.</td>
              </tr>
            )}
            {!loading && data?.items.map((entry) => (
              <tr key={entry.id} className="hover:bg-gray-50">
                <td className="whitespace-nowrap px-4 py-2 font-mono text-xs text-gray-500">
                  {formatTimestamp(entry.timestamp)}
                </td>
                <td className="whitespace-nowrap px-4 py-2">
                  <ActionBadge action={entry.action} />
                </td>
                <td className="px-4 py-2 max-w-[200px] truncate text-gray-700">
                  {entry.actorDisplayName ?? entry.actorUserId ?? '—'}
                  {entry.impersonatedTenantName && (
                    <span className="ml-1 text-xs text-violet-600">(impersonating {entry.impersonatedTenantName})</span>
                  )}
                </td>
                <td className="whitespace-nowrap px-4 py-2 text-xs text-gray-500">
                  {entry.surface ?? '—'}
                </td>
                <td className="px-4 py-2 text-xs text-gray-500">
                  {entry.entityType ? `${entry.entityType} ${entry.entityId ?? ''}` : '—'}
                </td>
                <td className="px-4 py-2 max-w-[280px] truncate text-gray-600 text-xs">
                  {entry.detail ?? '—'}
                </td>
                <td className="px-4 py-2 font-mono text-xs text-gray-400 max-w-[140px] truncate" title={entry.correlationId ?? ''}>
                  {entry.correlationId ? entry.correlationId.substring(0, 8) + '…' : '—'}
                </td>
                <td className="whitespace-nowrap px-4 py-2 font-mono text-xs text-gray-400">
                  {entry.ipAddress ?? '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {totalPages > 1 && (
        <div className="mt-4 flex items-center justify-between text-sm text-gray-500">
          <span>Page {page} of {totalPages}</span>
          <div className="flex gap-2">
            <button disabled={page <= 1} onClick={() => setPage((p) => p - 1)}
              className="rounded border border-gray-300 px-3 py-1 disabled:opacity-50 hover:bg-gray-50">Previous</button>
            <button disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}
              className="rounded border border-gray-300 px-3 py-1 disabled:opacity-50 hover:bg-gray-50">Next</button>
          </div>
        </div>
      )}
    </PageLayout>
  );
}
