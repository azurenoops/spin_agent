import { useEffect, useMemo, useState, type ReactElement } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  createCspDashboardTenant,
  getCspDashboardTenants,
  isUnavailable,
  type ListTenantsParams,
  type TenantsPage,
  type TenantsSortField,
  type TenantStatus,
  type TenantSummary,
} from './api';
import { startImpersonation } from '../tenancy/api';
import { isVestigeTenant } from '../tenancy/vestigeTenants';

/**
 * Feature 048 / US8 (Phase 3 re-scope) — Org-portfolio table for the CSP
 * Portfolio page.
 *
 * In this codebase a `Tenant` IS the unit of "org / mission owner": every
 * compliance entity (`RegisteredSystem`, `Finding`, `PoamItem`,
 * `Deviation`, `AuthorizationDecision`) carries `TenantId` only — there is
 * no `OrganizationId` on those rows. The legacy `Organization` entity is a
 * sub-grouping stub that no compliance row references. The CSP-Admin's
 * portfolio view therefore rolls up *tenants* but presents them as "orgs"
 * — that is the contract the user navigates by.
 *
 * Wire source: `GET /api/csp/dashboard/tenants` (unchanged — its row
 * contract was already org-shaped: `displayName`, `systemCount`,
 * `atoStatusCounts`, `openFindingCount`, `openPoamCount`,
 * `openDeviationCount`, `lastActivityTimestamp`).
 *
 * Differences vs the retired `TenantsTable`:
 *   - Status filter dropdown removed (orgs aren't filtered by lifecycle at
 *     the portfolio level; the status badge stays for the impersonation
 *     guard).
 *   - "Orgs" count column dropped (legacy `Organization` row count is
 *     incoherent in an org-portfolio view).
 *   - Header / pagination labels relabeled "Org" / "orgs".
 *   - Status is no longer a sort field (only displayName /
 *     openFindingCount / lastActivityTimestamp).
 *
 * Row click → impersonate that org (== that tenant) and navigate to `/`,
 * which then resolves to the per-tenant `PortfolioRiskProfile`. Disabled
 * orgs are visible (with a slate badge) but cannot be impersonated.
 */
const SORT_FIELDS: { value: TenantsSortField; label: string }[] = [
  { value: 'displayName', label: 'Name' },
  { value: 'openFindingCount', label: 'Open findings' },
  { value: 'lastActivityTimestamp', label: 'Last activity' },
];

const STATUS_BADGE_CLASSES: Record<TenantStatus, string> = {
  Active: 'bg-emerald-100 text-emerald-800',
  Suspended: 'bg-amber-100 text-amber-800',
  Disabled: 'bg-slate-200 text-slate-700',
};

interface OrgsTableProps {
  /** Optional initial page size (defaults to 25, capped server-side at 200). */
  initialPageSize?: number;
}

export default function OrgsTable({
  initialPageSize = 25,
}: OrgsTableProps): ReactElement {
  const navigate = useNavigate();

  const [page, setPage] = useState(1);
  const [pageSize] = useState(initialPageSize);
  const [sort, setSort] = useState<TenantsSortField>('displayName');
  const [order, setOrder] = useState<'asc' | 'desc'>('asc');

  const [data, setData] = useState<TenantsPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [busyTenantId, setBusyTenantId] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [refreshNonce, setRefreshNonce] = useState(0);

  const params = useMemo<ListTenantsParams>(
    () => ({
      page,
      pageSize,
      sort,
      order,
    }),
    [page, pageSize, sort, order],
  );

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    getCspDashboardTenants(params)
      .then((result) => {
        if (cancelled) return;
        if (isUnavailable(result)) {
          setData(null);
          return;
        }
        // Hide the FR-070 system + default bootstrap rows so the org
        // portfolio only shows real onboarded mission-owner tenants
        // — see `vestigeTenants.ts` for the rationale. The pagination
        // counts are recomputed locally so the footer stays in sync
        // with the visible row count.
        const visibleItems = result.items.filter(
          (o) => !isVestigeTenant(o.tenantId),
        );
        const filteredCount =
          result.totalCount - (result.items.length - visibleItems.length);
        setData({
          ...result,
          items: visibleItems,
          totalCount: Math.max(filteredCount, visibleItems.length),
        });
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
  }, [params, refreshNonce]);

  const handleSortToggle = (field: TenantsSortField) => {
    if (sort === field) {
      setOrder((o) => (o === 'asc' ? 'desc' : 'asc'));
    } else {
      setSort(field);
      setOrder('asc');
    }
    setPage(1);
  };

  const handleRowClick = async (org: TenantSummary) => {
    if (org.status === 'Disabled') {
      return;
    }
    setBusyTenantId(org.tenantId);
    try {
      await startImpersonation(org.tenantId, org.displayName);
      navigate('/');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Impersonation failed.';
      setError(msg);
    } finally {
      setBusyTenantId(null);
    }
  };

  const totalPages = data
    ? Math.max(1, Math.ceil(data.totalCount / data.pageSize))
    : 1;

  return (
    <div
      className="rounded-lg border border-gray-200 bg-white shadow-sm"
      data-testid="csp-orgs-table"
    >
      <div className="flex flex-wrap items-end justify-between gap-3 border-b border-gray-100 p-4">
        <div>
          <h3 className="text-sm font-semibold text-gray-700">Orgs</h3>
          <p className="text-xs text-gray-500">
            Click an org to drop into its workspace and inspect the per-org
            dashboard. Disabled orgs are listed but cannot be entered.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <button
            type="button"
            onClick={() => setCreateOpen(true)}
            className="rounded-md bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700"
            data-testid="orgs-create-button"
          >
            + Create org
          </button>
          <label className="flex items-center gap-1 text-xs text-gray-600">
            Sort by
            <select
              value={sort}
              onChange={(e) => {
                setSort(e.target.value as TenantsSortField);
                setPage(1);
              }}
              className="rounded border border-gray-300 px-2 py-1 text-sm"
              data-testid="orgs-sort-field"
            >
              {SORT_FIELDS.map((s) => (
                <option key={s.value} value={s.value}>
                  {s.label}
                </option>
              ))}
            </select>
          </label>
          <button
            type="button"
            onClick={() => setOrder((o) => (o === 'asc' ? 'desc' : 'asc'))}
            className="rounded border border-gray-300 px-2 py-1 text-xs text-gray-700 hover:bg-gray-50"
            data-testid="orgs-sort-order"
          >
            {order === 'asc' ? '↑ Asc' : '↓ Desc'}
          </button>
        </div>
      </div>

      {error && (
        <div
          className="border-b border-red-100 bg-red-50 px-4 py-2 text-sm text-red-700"
          role="alert"
          data-testid="orgs-error"
        >
          {error}
        </div>
      )}

      <div className="overflow-x-auto">
        <table className="min-w-full divide-y divide-gray-200 text-sm">
          <thead className="bg-gray-50 text-xs font-medium uppercase tracking-wide text-gray-600">
            <tr>
              <SortableHeader
                label="Org"
                field="displayName"
                activeSort={sort}
                order={order}
                onClick={() => handleSortToggle('displayName')}
              />
              <th className="px-3 py-2 text-left">Status</th>
              <th className="px-3 py-2 text-left">Onboarding</th>
              <th className="px-3 py-2 text-right">Systems</th>
              <th className="px-3 py-2 text-right">ATOs (A/IP/D)</th>
              <SortableHeader
                label="Open findings"
                field="openFindingCount"
                activeSort={sort}
                order={order}
                onClick={() => handleSortToggle('openFindingCount')}
                align="right"
              />
              <th className="px-3 py-2 text-right">Open POA&amp;Ms</th>
              <th className="px-3 py-2 text-right">Open deviations</th>
              <SortableHeader
                label="Last activity"
                field="lastActivityTimestamp"
                activeSort={sort}
                order={order}
                onClick={() => handleSortToggle('lastActivityTimestamp')}
              />
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {loading && (
              <tr>
                <td
                  colSpan={9}
                  className="px-3 py-6 text-center text-sm text-gray-500"
                  data-testid="orgs-loading"
                >
                  Loading orgs…
                </td>
              </tr>
            )}
            {!loading && data && data.items.length === 0 && (
              <tr>
                <td
                  colSpan={9}
                  className="px-3 py-6 text-center text-sm text-gray-500"
                  data-testid="orgs-empty"
                >
                  No orgs to display.
                </td>
              </tr>
            )}
            {!loading &&
              data?.items.map((o) => {
                const disabled = o.status === 'Disabled';
                const busy = busyTenantId === o.tenantId;
                return (
                  <tr
                    key={o.tenantId}
                    onClick={() => !disabled && !busy && handleRowClick(o)}
                    className={
                      disabled
                        ? 'bg-slate-50/40 text-slate-500'
                        : 'cursor-pointer hover:bg-indigo-50'
                    }
                    aria-disabled={disabled}
                    data-testid={`org-row-${o.tenantId}`}
                  >
                    <td className="whitespace-nowrap px-3 py-2 font-medium text-gray-900">
                      {o.displayName}
                      {busy && (
                        <span className="ml-2 text-xs text-indigo-600">
                          entering…
                        </span>
                      )}
                    </td>
                    <td className="whitespace-nowrap px-3 py-2">
                      <span
                        className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${STATUS_BADGE_CLASSES[o.status]}`}
                      >
                        {o.status}
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-3 py-2 text-xs text-gray-600">
                      {o.onboardingState}
                    </td>
                    <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">
                      {o.systemCount.toLocaleString()}
                    </td>
                    <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums text-xs">
                      {o.atoStatusCounts.authorized}/{o.atoStatusCounts.inProcess}/
                      {o.atoStatusCounts.denied}
                    </td>
                    <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">
                      {o.openFindingCount.toLocaleString()}
                    </td>
                    <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">
                      {o.openPoamCount.toLocaleString()}
                    </td>
                    <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums">
                      {o.openDeviationCount.toLocaleString()}
                    </td>
                    <td className="whitespace-nowrap px-3 py-2 text-xs text-gray-500">
                      {o.lastActivityTimestamp
                        ? new Date(o.lastActivityTimestamp).toLocaleString()
                        : '—'}
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
          data-testid="orgs-pagination"
        >
          <div>
            Page {data.page} of {totalPages} · {data.totalCount.toLocaleString()} orgs
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={data.page <= 1 || loading}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              className="rounded border border-gray-300 px-2 py-1 disabled:opacity-50"
              data-testid="orgs-prev-page"
            >
              Previous
            </button>
            <button
              type="button"
              disabled={data.page >= totalPages || loading}
              onClick={() => setPage((p) => p + 1)}
              className="rounded border border-gray-300 px-2 py-1 disabled:opacity-50"
              data-testid="orgs-next-page"
            >
              Next
            </button>
          </div>
        </div>
      )}

      {createOpen && (
        <CreateOrgModal
          onClose={() => setCreateOpen(false)}
          onCreated={() => {
            setCreateOpen(false);
            setPage(1);
            setRefreshNonce((n) => n + 1);
          }}
        />
      )}
    </div>
  );
}

interface SortableHeaderProps {
  label: string;
  field: TenantsSortField;
  activeSort: TenantsSortField;
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

// ---------------------------------------------------------------------------
// CreateOrgModal — CSP-Admin-only surface that provisions a new mission-owner
// organization (== `Tenant` row) via `POST /api/csp/dashboard/tenants`.
// Created in `OnboardingState.Pending` so the CSP-Admin can immediately
// impersonate the new row and walk the per-tenant onboarding wizard.
// ---------------------------------------------------------------------------

function CreateOrgModal({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: () => void;
}): ReactElement {
  const [displayName, setDisplayName] = useState('');
  const [legalEntityName, setLegalEntityName] = useState('');
  const [pocName, setPocName] = useState('');
  const [pocEmail, setPocEmail] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setFormError(null);
    if (!displayName.trim()) {
      setFormError('Display name is required.');
      return;
    }
    setSubmitting(true);
    try {
      await createCspDashboardTenant({
        displayName: displayName.trim(),
        legalEntityName: legalEntityName.trim() || undefined,
        primaryPocName: pocName.trim() || undefined,
        primaryPocEmail: pocEmail.trim() || undefined,
      });
      onCreated();
    } catch (err) {
      const ex = err as { errorCode?: string; message?: string };
      setFormError(ex?.message ?? 'Failed to create organization.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="create-org-title"
      onClick={onClose}
      data-testid="create-org-modal"
    >
      <form
        onSubmit={handleSubmit}
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-lg rounded-lg bg-white p-5 shadow-xl"
      >
        <h2 id="create-org-title" className="text-lg font-semibold text-gray-900">
          Create organization
        </h2>
        <p className="mt-1 text-xs text-gray-500">
          New orgs are created in <span className="font-medium">Pending</span>{' '}
          onboarding state. After create you can impersonate the org to walk
          the onboarding wizard.
        </p>

        {formError && (
          <div role="alert" className="mt-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {formError}
          </div>
        )}

        <div className="mt-4 space-y-3">
          <div>
            <label className="block text-sm font-medium text-gray-700">Display name *</label>
            <input
              type="text"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              maxLength={256}
              required
              autoFocus
              placeholder="e.g., PEO Soldier"
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              data-testid="create-org-display-name"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">Legal entity name</label>
            <input
              type="text"
              value={legalEntityName}
              onChange={(e) => setLegalEntityName(e.target.value)}
              maxLength={256}
              placeholder="Optional"
              className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              data-testid="create-org-legal-name"
            />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-700">Primary POC name</label>
              <input
                type="text"
                value={pocName}
                onChange={(e) => setPocName(e.target.value)}
                maxLength={256}
                placeholder="Optional"
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                data-testid="create-org-poc-name"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Primary POC email</label>
              <input
                type="email"
                value={pocEmail}
                onChange={(e) => setPocEmail(e.target.value)}
                maxLength={256}
                placeholder="Optional"
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                data-testid="create-org-poc-email"
              />
            </div>
          </div>
        </div>

        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            disabled={submitting}
            className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={submitting || !displayName.trim()}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-indigo-300"
            data-testid="create-org-submit"
          >
            {submitting ? 'Creating…' : 'Create organization'}
          </button>
        </div>
      </form>
    </div>
  );
}
