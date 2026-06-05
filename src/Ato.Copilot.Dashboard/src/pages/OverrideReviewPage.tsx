/**
 * OverrideReviewPage — SCA review queue for org-level control overrides
 * Route: /controls/overrides
 * Access: SecurityControlAssessor role can approve/reject; all roles can view
 *
 * Issues: #244 — Epic #219 Task
 */
import { useState, useEffect, useCallback } from 'react';
import PageLayout from '../components/layout/PageLayout';
import PageHero from '../components/layout/PageHero';
import { useSettings } from '../hooks/useSettings';
import {
  type OrgControlOverrideDto,
  type OrgControlOverrideApprovalStatus,
  listOrgControlOverrides,
  approveOrgControlOverride,
  rejectOrgControlOverride,
} from '../api/orgControlOverrides';

// ─── Types ──────────────────────────────────────────────────────────────────

type StatusFilter = 'All' | OrgControlOverrideApprovalStatus;

const STATUS_FILTERS: StatusFilter[] = ['All', 'Pending', 'Approved', 'Rejected'];

const STATUS_BADGE: Record<string, string> = {
  Pending: 'bg-amber-50 text-amber-700 border-amber-200',
  Approved: 'bg-green-50 text-green-700 border-green-200',
  Rejected: 'bg-red-50 text-red-700 border-red-200',
};

/** Infer approval status from DTO — backend may not yet have an approvalStatus field */
function getApprovalStatus(row: OrgControlOverrideDto): OrgControlOverrideApprovalStatus {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return (row as any).approvalStatus ?? 'Pending';
}

// ─── Component ──────────────────────────────────────────────────────────────

export default function OverrideReviewPage() {
  const { settings } = useSettings();
  const isScaRole = settings.role === 'SecurityControlAssessor';

  const [allOverrides, setAllOverrides] = useState<OrgControlOverrideDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('All');
  const [actionError, setActionError] = useState<string | null>(null);
  const [actingOn, setActingOn] = useState<string | null>(null);

  // ─── Reject modal state ────────────────────────────────────────────────────
  const [rejectTarget, setRejectTarget] = useState<OrgControlOverrideDto | null>(null);
  const [rejectComment, setRejectComment] = useState('');
  const [rejecting, setRejecting] = useState(false);

  // ─── Load overrides ────────────────────────────────────────────────────────
  const loadOverrides = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const rows = await listOrgControlOverrides();
      setAllOverrides(rows);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load overrides');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadOverrides();
  }, [loadOverrides]);

  // ─── Filtered list ─────────────────────────────────────────────────────────
  const filtered = allOverrides.filter((row) => {
    if (statusFilter === 'All') return true;
    return getApprovalStatus(row) === statusFilter;
  });

  // ─── Approve handler ───────────────────────────────────────────────────────
  const handleApprove = async (row: OrgControlOverrideDto) => {
    setActingOn(row.controlId);
    setActionError(null);
    try {
      await approveOrgControlOverride(row.controlId);
      await loadOverrides();
    } catch (err: unknown) {
      setActionError(err instanceof Error ? err.message : 'Approval failed');
    } finally {
      setActingOn(null);
    }
  };

  // ─── Reject handler ────────────────────────────────────────────────────────
  const openReject = (row: OrgControlOverrideDto) => {
    setRejectTarget(row);
    setRejectComment('');
  };

  const handleRejectConfirm = async () => {
    if (!rejectTarget) return;
    setRejecting(true);
    setActionError(null);
    try {
      await rejectOrgControlOverride(rejectTarget.controlId, rejectComment);
      setRejectTarget(null);
      setRejectComment('');
      await loadOverrides();
    } catch (err: unknown) {
      setActionError(err instanceof Error ? err.message : 'Rejection failed');
    } finally {
      setRejecting(false);
    }
  };

  return (
    <PageLayout title="Override Review">
      <PageHero
        eyebrow="Controls · Override Review"
        title="Org Control Overrides"
        description="Review, approve, or reject organization-level control overrides. Only SecurityControlAssessors can take action."
        showOrgName
      />

      <div className="space-y-4">
        {/* Status filter tabs */}
        <div className="flex gap-1 border-b border-gray-200">
          {STATUS_FILTERS.map((f) => {
            const count = f === 'All'
              ? allOverrides.length
              : allOverrides.filter((r) => getApprovalStatus(r) === f).length;
            return (
              <button
                key={f}
                onClick={() => setStatusFilter(f)}
                className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
                  statusFilter === f
                    ? 'border-indigo-600 text-indigo-700'
                    : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                }`}
              >
                {f}
                {count > 0 && (
                  <span className={`ml-1.5 rounded-full px-2 py-0.5 text-xs font-medium ${
                    f === 'Pending'
                      ? 'bg-amber-100 text-amber-700'
                      : f === 'Approved'
                      ? 'bg-green-100 text-green-700'
                      : f === 'Rejected'
                      ? 'bg-red-100 text-red-700'
                      : 'bg-gray-100 text-gray-600'
                  }`}>
                    {count}
                  </span>
                )}
              </button>
            );
          })}
        </div>

        {/* Action error */}
        {actionError && (
          <div className="rounded-md border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {actionError}
          </div>
        )}

        {/* Role gate notice */}
        {!isScaRole && (
          <div className="rounded-md border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
            You are viewing in read-only mode. Switch to <strong>SecurityControlAssessor</strong> role to approve or reject overrides.
          </div>
        )}

        {/* Table */}
        <div className="overflow-x-auto rounded-lg border border-gray-200 bg-white">
          <table className="min-w-full divide-y divide-gray-200 text-sm">
            <thead>
              <tr className="bg-gray-50">
                <th className="px-4 py-3 text-left font-semibold text-gray-700 w-28">Control ID</th>
                <th className="px-4 py-3 text-left font-semibold text-gray-700 w-24">Status</th>
                <th className="px-4 py-3 text-left font-semibold text-gray-700 w-28">Implementation</th>
                <th className="px-4 py-3 text-left font-semibold text-gray-700">Justification</th>
                <th className="px-4 py-3 text-left font-semibold text-gray-700 w-32">Submitted</th>
                <th className="px-4 py-3 text-left font-semibold text-gray-700 w-24">By</th>
                {isScaRole && (
                  <th className="px-4 py-3 text-center font-semibold text-gray-700 w-36">Actions</th>
                )}
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {loading ? (
                <tr>
                  <td colSpan={isScaRole ? 7 : 6} className="px-4 py-12 text-center text-gray-400">
                    Loading overrides…
                  </td>
                </tr>
              ) : error ? (
                <tr>
                  <td colSpan={isScaRole ? 7 : 6} className="px-4 py-8 text-center text-red-500">
                    {error}
                  </td>
                </tr>
              ) : filtered.length === 0 ? (
                <tr>
                  <td colSpan={isScaRole ? 7 : 6} className="px-4 py-12 text-center text-gray-400">
                    {statusFilter === 'All'
                      ? 'No org control overrides exist yet.'
                      : `No ${statusFilter.toLowerCase()} overrides.`}
                  </td>
                </tr>
              ) : (
                filtered.map((row) => {
                  const approvalStatus = getApprovalStatus(row);
                  const acting = actingOn === row.controlId;
                  return (
                    <tr key={row.id} className="hover:bg-gray-50 transition-colors">
                      <td className="px-4 py-3 font-mono text-xs text-gray-900">{row.controlId}</td>
                      <td className="px-4 py-3">
                        <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium ${STATUS_BADGE[approvalStatus] ?? 'bg-gray-50 text-gray-500 border-gray-200'}`}>
                          {approvalStatus}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-gray-600 text-xs">
                        {row.implementationStatus ?? '—'}
                      </td>
                      <td className="px-4 py-3 text-gray-700 max-w-xs">
                        <span title={row.justification ?? ''}>
                          {row.justification
                            ? row.justification.length > 100
                              ? `${row.justification.slice(0, 100)}…`
                              : row.justification
                            : <em className="text-gray-400">No justification</em>}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-gray-500 text-xs whitespace-nowrap">
                        {new Date(row.createdAt).toLocaleDateString()}
                      </td>
                      <td className="px-4 py-3 text-gray-500 text-xs truncate max-w-[6rem]" title={row.createdBy}>
                        {row.createdBy}
                      </td>
                      {isScaRole && (
                        <td className="px-4 py-3 text-center">
                          {approvalStatus === 'Pending' ? (
                            <div className="flex items-center justify-center gap-2">
                              <button
                                onClick={() => handleApprove(row)}
                                disabled={acting}
                                className="rounded-md bg-green-600 px-2.5 py-1 text-xs font-medium text-white hover:bg-green-700 disabled:opacity-50"
                              >
                                {acting ? '…' : 'Approve'}
                              </button>
                              <button
                                onClick={() => openReject(row)}
                                disabled={acting}
                                className="rounded-md border border-red-300 px-2.5 py-1 text-xs font-medium text-red-700 hover:bg-red-50 disabled:opacity-50"
                              >
                                Reject
                              </button>
                            </div>
                          ) : (
                            <span className="text-xs text-gray-400">—</span>
                          )}
                        </td>
                      )}
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Reject confirmation modal */}
      {rejectTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-full max-w-md rounded-lg bg-white p-6 shadow-xl">
            <h3 className="text-lg font-semibold text-gray-900 mb-1">Reject Override</h3>
            <p className="text-sm text-gray-600 mb-4">
              Rejecting override for <strong className="font-mono">{rejectTarget.controlId}</strong>.
              A comment is required.
            </p>
            <label className="block mb-4">
              <span className="block text-sm font-medium text-gray-700 mb-1">
                Comment <span className="text-red-600">*</span>
              </span>
              <textarea
                rows={3}
                value={rejectComment}
                onChange={(e) => setRejectComment(e.target.value)}
                placeholder="Explain why this override is being rejected…"
                className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-red-500 focus:outline-none focus:ring-1 focus:ring-red-500"
                maxLength={2000}
              />
              <span className="mt-0.5 block text-right text-xs text-gray-400">
                {rejectComment.length} / 2000
              </span>
            </label>
            {actionError && (
              <p className="mb-3 text-sm text-red-600">{actionError}</p>
            )}
            <div className="flex justify-end gap-2">
              <button
                type="button"
                onClick={() => { setRejectTarget(null); setRejectComment(''); }}
                disabled={rejecting}
                className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={() => void handleRejectConfirm()}
                disabled={rejecting || !rejectComment.trim()}
                className="rounded-md bg-red-600 px-4 py-2 text-sm text-white hover:bg-red-700 disabled:opacity-50"
              >
                {rejecting ? 'Rejecting…' : 'Confirm Reject'}
              </button>
            </div>
          </div>
        </div>
      )}
    </PageLayout>
  );
}
