import { useEffect, useMemo, useState, type ReactElement } from 'react';
import { useNavigate } from 'react-router-dom';
import PageLayout from '../../components/layout/PageLayout';
import PageHero from '../../components/layout/PageHero';
import {
  getCspDashboardSystems,
  isUnavailable,
  type CspSystemAtoSeverity,
  type ListSystemsParams,
  type SystemRow,
  type SystemsPage,
  type SystemsSortField,
} from './api';
import { startImpersonation } from '../tenancy/api';

/**
 * Feature 048 follow-up — Cross-tenant systems table for the CSP-level
 * `/systems` page.
 *
 * Rendered by `SystemsRoute` when the caller is a CSP-Admin in MultiTenant
 * mode and is NOT currently impersonating a tenant. Mirrors the per-tenant
 * `PortfolioDashboard` columns + a leading `Organization` column. Row click
 * impersonates the system's owning organization and navigates to the system
 * detail page (`/systems/{systemId}`), so the CSP-Admin can drill into a
 * specific system without first browsing the portfolio.
 *
 * The data feed is `GET /api/csp/dashboard/systems`. Disabled-organization
 * systems and the FR-070 system organization are excluded server-side.
 */
const SORT_FIELDS: { value: SystemsSortField; label: string }[] = [
  { value: 'name', label: 'System name' },
  { value: 'orgDisplayName', label: 'Organization' },
  { value: 'rmfPhase', label: 'RMF phase' },
];

const IMPACT_FILTERS: { value: '' | 'Low' | 'Moderate' | 'High'; label: string }[] = [
  { value: '', label: 'All impact levels' },
  { value: 'Low', label: 'Low' },
  { value: 'Moderate', label: 'Moderate' },
  { value: 'High', label: 'High' },
];

const RMF_PHASE_FILTERS: {
  value:
    | ''
    | 'Prepare'
    | 'Categorize'
    | 'Select'
    | 'Implement'
    | 'Assess'
    | 'Authorize'
    | 'Monitor';
  label: string;
}[] = [
  { value: '', label: 'All RMF phases' },
  { value: 'Prepare', label: 'Prepare' },
  { value: 'Categorize', label: 'Categorize' },
  { value: 'Select', label: 'Select' },
  { value: 'Implement', label: 'Implement' },
  { value: 'Assess', label: 'Assess' },
  { value: 'Authorize', label: 'Authorize' },
  { value: 'Monitor', label: 'Monitor' },
];

const ATO_SEVERITY_BADGE: Record<CspSystemAtoSeverity, string> = {
  none: 'bg-slate-100 text-slate-600',
  green: 'bg-emerald-100 text-emerald-800',
  yellow: 'bg-amber-100 text-amber-800',
  red: 'bg-rose-100 text-rose-800',
  expired: 'bg-rose-200 text-rose-900',
};

interface CspSystemsPageProps {
  /** Optional initial page size (defaults to 50, capped server-side at 200). */
  initialPageSize?: number;
}

export default function CspSystemsPage({
  initialPageSize = 50,
}: CspSystemsPageProps): ReactElement {
  const navigate = useNavigate();

  const [page, setPage] = useState(1);
  const [pageSize] = useState(initialPageSize);
  const [impactFilter, setImpactFilter] = useState<'' | 'Low' | 'Moderate' | 'High'>('');
  const [rmfFilter, setRmfFilter] = useState<
    '' | 'Prepare' | 'Categorize' | 'Select' | 'Implement' | 'Assess' | 'Authorize' | 'Monitor'
  >('');
  const [sort, setSort] = useState<SystemsSortField>('name');
  const [order, setOrder] = useState<'asc' | 'desc'>('asc');

  const [data, setData] = useState<SystemsPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [busySystemId, setBusySystemId] = useState<string | null>(null);

  const params = useMemo<ListSystemsParams>(
    () => ({
      page,
      pageSize,
      impactLevel: impactFilter === '' ? undefined : impactFilter,
      rmfPhase: rmfFilter === '' ? undefined : rmfFilter,
      sort,
      order,
    }),
    [page, pageSize, impactFilter, rmfFilter, sort, order],
  );

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    getCspDashboardSystems(params)
      .then((result) => {
        if (cancelled) return;
        if (isUnavailable(result)) {
          // SystemsRoute already guards entry; just clear state.
          setData(null);
          return;
        }
        setData(result);
      })
      .catch((err: Error) => {
        if (cancelled) return;
        setError(err.message);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [params]);

  const handleSortToggle = (field: SystemsSortField) => {
    if (sort === field) {
      setOrder((o) => (o === 'asc' ? 'desc' : 'asc'));
    } else {
      setSort(field);
      setOrder('asc');
    }
    setPage(1);
  };

  const handleRowClick = async (system: SystemRow) => {
    setBusySystemId(system.systemId);
    try {
      await startImpersonation(system.tenantId, system.orgDisplayName);
      navigate(`/systems/${encodeURIComponent(system.systemId)}`);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Impersonation failed.';
      setError(msg);
    } finally {
      setBusySystemId(null);
    }
  };

  const totalPages = data
    ? Math.max(1, Math.ceil(data.totalCount / data.pageSize))
    : 1;

  return (
    <PageLayout title="Systems · CSP">
      <div data-testid="csp-systems-page">
        <PageHero
          eyebrow="Portfolio"
          title="Systems across all organizations"
          description="Cross-organizational view of every registered system. Click a row to impersonate the system's owning organization and open its detail page. Disabled-organization systems and the CSP's system-reference organization are excluded."
          actions={
            data ? (
              <span className="inline-flex items-center rounded-full bg-white/15 px-3 py-1 text-xs font-medium text-white ring-1 ring-white/30 backdrop-blur">
                {data.totalCount.toLocaleString()} system{data.totalCount === 1 ? '' : 's'}
              </span>
            ) : undefined
          }
        />

        {/* Toolbar — matches the org `PortfolioDashboard` toolbar pattern
            (flex-wrap row above the content card, plain bordered selects,
            sort controls on the right). */}
        <div className="mb-4 flex flex-wrap items-center gap-3">
          <select
            value={impactFilter}
            onChange={(e) => {
              setImpactFilter(e.target.value as typeof impactFilter);
              setPage(1);
            }}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            data-testid="csp-systems-impact-filter"
            aria-label="Impact level"
          >
            {IMPACT_FILTERS.map((f) => (
              <option key={f.value} value={f.value}>
                {f.label}
              </option>
            ))}
          </select>
          <select
            value={rmfFilter}
            onChange={(e) => {
              setRmfFilter(e.target.value as typeof rmfFilter);
              setPage(1);
            }}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            data-testid="csp-systems-rmf-filter"
            aria-label="RMF phase"
          >
            {RMF_PHASE_FILTERS.map((f) => (
              <option key={f.value} value={f.value}>
                {f.label}
              </option>
            ))}
          </select>
          <div className="ml-auto flex flex-wrap items-center gap-2">
            <select
              value={sort}
              onChange={(e) => {
                setSort(e.target.value as SystemsSortField);
                setPage(1);
              }}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              data-testid="csp-systems-sort-field"
              aria-label="Sort by"
            >
              {SORT_FIELDS.map((s) => (
                <option key={s.value} value={s.value}>
                  Sort: {s.label}
                </option>
              ))}
            </select>
            <button
              type="button"
              onClick={() => setOrder((o) => (o === 'asc' ? 'desc' : 'asc'))}
              className="rounded-md border border-gray-300 px-3 py-1.5 text-sm text-gray-700 hover:bg-gray-50"
              data-testid="csp-systems-sort-order"
            >
              {order === 'asc' ? '↑ Asc' : '↓ Desc'}
            </button>
          </div>
        </div>

        <div
          className="rounded-lg border border-gray-200 bg-white shadow-sm"
          data-testid="csp-systems-table"
        >
          {error && (
            <div
              className="border-b border-red-100 bg-red-50 px-4 py-2 text-sm text-red-700"
              role="alert"
              data-testid="csp-systems-error"
            >
              {error}
            </div>
          )}

          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
              <thead className="bg-gray-50 text-xs font-medium uppercase tracking-wide text-gray-600">
                <tr>
                  <SortableHeader
                    label="Organization"
                    field="orgDisplayName"
                    activeSort={sort}
                    order={order}
                    onClick={() => handleSortToggle('orgDisplayName')}
                  />
                  <SortableHeader
                    label="System"
                    field="name"
                    activeSort={sort}
                    order={order}
                    onClick={() => handleSortToggle('name')}
                  />
                  <th className="px-3 py-2 text-left">Impact</th>
                  <SortableHeader
                    label="RMF phase"
                    field="rmfPhase"
                    activeSort={sort}
                    order={order}
                    onClick={() => handleSortToggle('rmfPhase')}
                  />
                  <th className="px-3 py-2 text-right">Compliance</th>
                  <th className="px-3 py-2 text-left">ATO</th>
                  <th className="px-3 py-2 text-right">Open POA&amp;Ms</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {loading && (
                  <tr>
                    <td
                      colSpan={7}
                      className="px-3 py-6 text-center text-sm text-gray-500"
                      data-testid="csp-systems-loading"
                    >
                      Loading systems…
                    </td>
                  </tr>
                )}
                {!loading && data && data.items.length === 0 && (
                  <tr>
                    <td
                      colSpan={7}
                      className="px-3 py-6 text-center text-sm text-gray-500"
                      data-testid="csp-systems-empty"
                    >
                      No systems match the current filter.
                    </td>
                  </tr>
                )}
                {!loading &&
                  data?.items.map((s) => {
                    const busy = busySystemId === s.systemId;
                    const atoLabel =
                      s.atoStatus === 'Active' && s.atoDaysRemaining !== null
                        ? `${s.atoStatus} · ${s.atoDaysRemaining}d`
                        : s.atoStatus === 'Expired' && s.atoDaysRemaining !== null
                          ? `Expired · ${Math.abs(s.atoDaysRemaining)}d ago`
                          : s.atoStatus;
                    return (
                      <tr
                        key={s.systemId}
                        onClick={() => !busy && handleRowClick(s)}
                        className="cursor-pointer hover:bg-indigo-50"
                        data-testid={`csp-system-row-${s.systemId}`}
                      >
                        <td className="whitespace-nowrap px-3 py-2 text-gray-900">
                          {s.orgDisplayName}
                        </td>
                        <td className="whitespace-nowrap px-3 py-2 font-medium text-gray-900">
                          {s.name}
                          {s.acronym ? (
                            <span className="ml-1 text-xs text-gray-500">
                              ({s.acronym})
                            </span>
                          ) : null}
                          {busy && (
                            <span className="ml-2 text-xs text-indigo-600">
                              opening…
                            </span>
                          )}
                        </td>
                        <td className="whitespace-nowrap px-3 py-2 text-xs text-gray-700">
                          {s.impactLevel}
                        </td>
                        <td className="whitespace-nowrap px-3 py-2 text-xs text-gray-700">
                          {s.currentRmfPhase}
                        </td>
                        <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">
                          {s.complianceScore.toFixed(1)}%
                        </td>
                        <td className="whitespace-nowrap px-3 py-2">
                          <span
                            className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${ATO_SEVERITY_BADGE[s.atoSeverity]}`}
                            data-testid={`csp-system-ato-${s.systemId}`}
                          >
                            {atoLabel}
                          </span>
                        </td>
                        <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">
                          {s.openPoamCount.toLocaleString()}
                          {s.overduePoamCount > 0 && (
                            <span className="ml-1 text-xs text-rose-600">
                              ({s.overduePoamCount} overdue)
                            </span>
                          )}
                        </td>
                      </tr>
                    );
                  })}
              </tbody>
            </table>
          </div>

          {data && (
            <div
              className="flex items-center justify-between border-t border-gray-100 px-4 py-2 text-xs text-gray-600"
              data-testid="csp-systems-pagination"
            >
              <div>
                Page {data.page} of {totalPages} ·{' '}
                {data.totalCount.toLocaleString()} systems
              </div>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  disabled={data.page <= 1 || loading}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                  className="rounded border border-gray-300 px-2 py-1 disabled:opacity-50"
                  data-testid="csp-systems-prev-page"
                >
                  Previous
                </button>
                <button
                  type="button"
                  disabled={data.page >= totalPages || loading}
                  onClick={() => setPage((p) => p + 1)}
                  className="rounded border border-gray-300 px-2 py-1 disabled:opacity-50"
                  data-testid="csp-systems-next-page"
                >
                  Next
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </PageLayout>
  );
}

interface SortableHeaderProps {
  label: string;
  field: SystemsSortField;
  activeSort: SystemsSortField;
  order: 'asc' | 'desc';
  onClick: () => void;
  align?: 'left' | 'right';
}

function SortableHeader({
  label,
  field,
  activeSort,
  order,
  onClick,
  align = 'left',
}: SortableHeaderProps): ReactElement {
  const isActive = activeSort === field;
  const arrow = !isActive ? '' : order === 'asc' ? ' ↑' : ' ↓';
  return (
    <th
      onClick={onClick}
      className={`cursor-pointer select-none px-3 py-2 ${align === 'right' ? 'text-right' : 'text-left'} hover:text-gray-900`}
    >
      {label}
      {arrow}
    </th>
  );
}
