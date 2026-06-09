import { useMemo, useState } from 'react';
import axios from 'axios';
import { useLocation, useNavigate } from 'react-router-dom';
import { useMe } from './useMe';
import type { TenantSummary } from './types';

/**
 * Feature 051 T072 [US3] — tenant / organization picker page.
 *
 * Renders one row per entry in `me.tenantMemberships`:
 *   • Active rows are clickable; click POSTs /api/auth/select-tenant
 *     with `{ tenantId, remember }` and navigates to the post-login
 *     deep link (carried via `location.state.deepLink`) or "/".
 *   • Suspended rows are clickable (per FR-010 — Suspended tenants are
 *     still pickable; mutating endpoints later 423 per Feature 048).
 *   • Disabled rows are hidden for non-CSP-Admin users and grayed-out
 *     (button disabled) for CSP-Admin per FR-010.
 *
 * CSP-Admin callers also see an extra "All Tenants (CSP view)" row
 * (FR-011) that navigates to `/csp/dashboard` (Feature 048 root)
 * WITHOUT calling `/select-tenant` — the CSP dashboard handles its
 * own cross-tenant scope.
 */
export default function TenantPickerPage() {
  const { data: me, isLoading, error } = useMe();
  const [remember, setRemember] = useState<boolean>(false);
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const navigate = useNavigate();
  const location = useLocation();

  // Deep-link target preserved across the picker. Falls back to "/".
  const postLoginTarget = useMemo<string>(() => {
    const state = location.state as { deepLink?: string } | null;
    return state?.deepLink && typeof state.deepLink === 'string' ? state.deepLink : '/';
  }, [location.state]);

  const visibleTenants = useMemo<TenantSummary[]>(() => {
    if (!me) return [];
    if (me.isCspAdmin) return me.tenantMemberships;
    // Non-CSP-Admin: hide Disabled rows (FR-010).
    return me.tenantMemberships.filter((t) => t.status !== 'Disabled');
  }, [me]);

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-50">
        <div className="text-center">
          <div className="inline-block animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600 mb-4" />
          <p className="text-sm text-gray-700">Loading your tenants…</p>
        </div>
      </div>
    );
  }
  if (error || !me) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-50">
        <div className="rounded-md border border-red-200 bg-red-50 p-4 text-sm text-red-800">
          Could not load your tenant list. Try refreshing the page.
        </div>
      </div>
    );
  }

  const handleSelect = async (t: TenantSummary) => {
    if (t.status === 'Disabled' && !me.isCspAdmin) {
      // Defense in depth — UI already hides this row.
      return;
    }
    setSubmitting(true);
    setSubmitError(null);
    try {
      await axios.post('/api/auth/select-tenant', {
        tenantId: t.id,
        remember,
      });
      navigate(postLoginTarget, { replace: true });
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      setSubmitError(msg);
    } finally {
      setSubmitting(false);
    }
  };

  const handleCspViewAll = () => {
    // FR-011 — bypass /select-tenant; the CSP dashboard binds its own scope.
    navigate("/", { replace: true }); // fix #361: /csp/dashboard retired
  };

  return (
    <div className="min-h-screen bg-gray-50 py-12 px-4 sm:px-6 lg:px-8">
      <div className="mx-auto max-w-md">
        <h1 className="text-2xl font-semibold text-gray-900">Choose a tenant</h1>
        <p className="mt-2 text-sm text-gray-600">
          Pick the organization you want to work in for this session.
        </p>

        {submitError && (
          <div
            role="alert"
            className="mt-4 rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800"
          >
            {submitError}
          </div>
        )}

        <ul className="mt-6 space-y-2" data-testid="tenant-picker-list">
          {visibleTenants.map((t) => {
            const isDisabledRow = t.status === 'Disabled';
            const isDisabledClick = isDisabledRow || submitting;
            return (
              <li key={t.id}>
                <button
                  type="button"
                  onClick={() => {
                    void handleSelect(t);
                  }}
                  disabled={isDisabledClick}
                  aria-label={t.displayName}
                  className={[
                    'w-full flex items-center justify-between rounded-md border px-4 py-3 text-left text-sm',
                    isDisabledRow
                      ? 'border-gray-200 bg-gray-50 text-gray-400 cursor-not-allowed'
                      : 'border-gray-300 bg-white text-gray-900 hover:border-indigo-500 hover:bg-indigo-50',
                  ].join(' ')}
                >
                  <span className="font-medium">{t.displayName}</span>
                  <StatusBadge status={t.status} />
                </button>
              </li>
            );
          })}
        </ul>

        <label className="mt-4 flex items-center gap-2 text-sm text-gray-700">
          <input
            type="checkbox"
            checked={remember}
            onChange={(e) => setRemember(e.target.checked)}
            className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
          />
          Remember on this device
        </label>

        {me.isCspAdmin && (
          <div className="mt-6 border-t border-gray-200 pt-4">
            <button
              type="button"
              onClick={handleCspViewAll}
              aria-label="All Tenants (CSP view)"
              className="w-full rounded-md border border-indigo-300 bg-indigo-50 px-4 py-3 text-left text-sm font-medium text-indigo-700 hover:bg-indigo-100"
            >
              All Tenants (CSP view)
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: TenantSummary['status'] }) {
  const styles =
    status === 'Active'
      ? 'bg-green-100 text-green-800'
      : status === 'Suspended'
      ? 'bg-yellow-100 text-yellow-800'
      : 'bg-gray-200 text-gray-600';
  return (
    <span
      className={`ml-3 inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${styles}`}
    >
      {status}
    </span>
  );
}
