import { useEffect, useMemo, useState, type ReactElement, type ReactNode } from 'react';
import {
  archiveCspInheritedCapability,
  isUnavailable,
  listCapabilityHistory,
  listCspInheritedCapabilities,
  listCspInheritedComponents,
  patchCspInheritedCapability,
  remapCspInheritedComponent,
  reviewCspInheritedCapability,
  type CapabilityHistoryEvent,
  type CapabilityHistoryEventType,
  type CapabilityHistoryPage,
  type CspInheritedCapability,
  type CspInheritedComponent,
} from './api';
import MoveCapabilityDialog from './MoveCapabilityDialog';

interface Props {
  componentId: string;
  capabilityId: string;
  /** Display-only name + type of the parent component (passed in by the
   *  caller so the drawer doesn't have to re-fetch the component just to
   *  render the header). */
  componentName: string;
  componentType: CspInheritedComponent['componentType'];
  canManage: boolean;
  onClose: () => void;
  /** Called whenever the drawer mutates the capability so the parent list
   *  can refresh (or splice the row). */
  onMutated: () => void;
}

/**
 * `CapabilityDetailDrawer` — Feature 048 / US9 follow-on.
 *
 * Right-side drawer showing the full record for a single CSP-inherited
 * capability with full CRUD (Edit / Archive / Remap-parent-component) for
 * CSP-Admin users. Read-only for non-CSP-Admin callers. Mirrors
 * `ComponentDetailDrawer.tsx` chrome so the two surfaces behave the same.
 *
 * NOTE: A per-capability "Remap" backend operation does not exist — the AI
 * mapping pipeline operates on a whole component. The Remap button on this
 * drawer therefore re-maps the **parent component** (with
 * `preserveHumanMappings = true` by default), which is the closest
 * equivalent. The component-level remap is what the user means by
 * "Remap (for components)".
 */
export default function CapabilityDetailDrawer({
  componentId,
  capabilityId,
  componentName,
  componentType,
  canManage,
  onClose,
  onMutated,
}: Props): ReactElement {
  const [capability, setCapability] = useState<CspInheritedCapability | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Edit-form state
  const [editing, setEditing] = useState(false);
  const [editName, setEditName] = useState('');
  const [editDescription, setEditDescription] = useState('');
  const [editControls, setEditControls] = useState('');

  // Review-form state (only relevant when status === 'NeedsReview')
  const [reviewing, setReviewing] = useState(false);
  const [reviewControls, setReviewControls] = useState('');
  const [reviewerNote, setReviewerNote] = useState('');

  // Feature 050 / US2 — Move-to-another-component state.
  // `hasEligibleTarget` is computed via a tiny eager fetch (pageSize=2)
  // on mount; the dialog itself does the full pageSize=200 fetch when
  // opened. Tri-state: `null` = probing, `true` = at least one other
  // non-archived component exists, `false` = the user must create one
  // before this affordance is enabled.
  const [hasEligibleTarget, setHasEligibleTarget] = useState<boolean | null>(null);
  const [showMoveDialog, setShowMoveDialog] = useState(false);

  // Feature 050 / US3 — History tab. Lazy-fetches on tab activation per
  // contracts/frontend-types.md § 3.2.2. Pagination state is local to the
  // drawer instance; the server clamps pageSize to [1, 200] and a
  // pageSize change re-fires the fetch (covered by T034).
  const [activeTab, setActiveTab] = useState<'details' | 'history'>('details');
  const [historyPage, setHistoryPage] = useState(1);
  const [historyPageSize, setHistoryPageSize] = useState(50);
  const [history, setHistory] = useState<CapabilityHistoryPage | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);

  // Initial fetch — there is no dedicated GET-single-capability endpoint;
  // pull the parent component's capability list and pick this row.
  useEffect(() => {
    let cancelled = false;
    setCapability(null);
    setError(null);
    void (async () => {
      try {
        const caps = await listCspInheritedCapabilities(componentId);
        if (cancelled) return;
        const found = caps.find((c) => c.id === capabilityId);
        if (!found) {
          setError('Capability not found — it may have been archived or remapped away.');
          return;
        }
        setCapability(found);
        setEditName(found.name);
        setEditDescription(found.description ?? '');
        setEditControls(found.mappedNistControlIds.join(', '));
        setReviewControls(found.mappedNistControlIds.join(', '));
      } catch (err) {
        const e = err as { errorCode?: string; message?: string };
        if (!cancelled) setError(e?.message ?? 'Failed to load capability.');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [componentId, capabilityId]);

  // Eligible-target probe (Feature 050 / US2). One small fetch on mount —
  // `pageSize=2` because the only signal needed is "does at least one OTHER
  // non-archived component exist?". Cached per-drawer; the move dialog does
  // its own `pageSize=200` fetch when opened.
  useEffect(() => {
    let cancelled = false;
    setHasEligibleTarget(null);
    void (async () => {
      try {
        const result = await listCspInheritedComponents({
          page: 1,
          pageSize: 2,
          status: 'Published',
        });
        if (cancelled) return;
        if (isUnavailable(result)) {
          setHasEligibleTarget(false);
          return;
        }
        // "At least one OTHER" — total ≥ 2 covers the case where the source
        // component itself counts toward the page. We don't introspect ids
        // here; the dialog's filter excludes the source component on render.
        setHasEligibleTarget(result.total >= 2);
      } catch {
        if (!cancelled) setHasEligibleTarget(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [componentId]);

  // Feature 050 / US3 — History fetch on tab activation, page/pageSize change,
  // OR capability identity change (the latter ensures a successful
  // Move/Edit/Review refreshes the trail since rowVersion bumps each time).
  useEffect(() => {
    if (activeTab !== 'history') return;
    let cancelled = false;
    setHistoryLoading(true);
    setHistoryError(null);
    void (async () => {
      try {
        const page = await listCapabilityHistory(componentId, capabilityId, {
          page: historyPage,
          pageSize: historyPageSize,
        });
        if (cancelled) return;
        setHistory(page);
      } catch (err) {
        if (cancelled) return;
        const e = err as { message?: string };
        setHistoryError(e?.message ?? 'Failed to load history.');
      } finally {
        if (!cancelled) setHistoryLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [
    activeTab,
    componentId,
    capabilityId,
    historyPage,
    historyPageSize,
    // Re-fire when the capability's rowVersion changes so a Move/Edit
    // refreshes the trail. `null`-coalesce keeps the dep array stable.
    capability?.rowVersion ?? '',
  ]);

  const parsedEditControls = useMemo(() => parseControlIds(editControls), [editControls]);
  const parsedReviewControls = useMemo(() => parseControlIds(reviewControls), [reviewControls]);

  const handleSaveEdit = async () => {
    if (!capability) return;
    if (editName.trim().length === 0 || editDescription.trim().length === 0) {
      setError('Name and description are required.');
      return;
    }
    if (parsedEditControls.length === 0) {
      setError('Provide at least one NIST control ID (e.g. AC-2, AC-2(1)).');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const updated = await patchCspInheritedCapability(
        componentId,
        capability.id,
        {
          name: editName.trim(),
          description: editDescription.trim(),
          mappedNistControlIds: parsedEditControls,
        },
        capability.rowVersion ?? undefined,
      );
      setCapability(updated);
      setEditing(false);
      onMutated();
    } catch (err) {
      const e = err as { errorCode?: string; message?: string };
      setError(
        e?.errorCode === 'ROW_VERSION_MISMATCH'
          ? 'This capability was changed by another user. Reload to see the latest.'
          : (e?.message ?? 'Failed to save changes.'),
      );
    } finally {
      setBusy(false);
    }
  };

  const handleReview = async () => {
    if (!capability) return;
    if (parsedReviewControls.length === 0) {
      setError('Provide at least one NIST control ID to resolve the review.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const updated = await reviewCspInheritedCapability(componentId, capability.id, {
        mappedNistControlIds: parsedReviewControls,
        reviewerNote: reviewerNote.trim() || undefined,
      });
      setCapability(updated);
      setReviewing(false);
      onMutated();
    } catch (err) {
      const e = err as { errorCode?: string; message?: string };
      setError(e?.message ?? 'Failed to complete review.');
    } finally {
      setBusy(false);
    }
  };

  const handleArchive = async () => {
    if (!capability) return;
    if (!window.confirm(`Archive capability “${capability.name}”?`)) return;
    setBusy(true);
    setError(null);
    try {
      await archiveCspInheritedCapability(componentId, capability.id);
      onMutated();
      onClose();
    } catch (err) {
      const e = err as { errorCode?: string; message?: string };
      setError(e?.message ?? 'Failed to archive capability.');
    } finally {
      setBusy(false);
    }
  };

  const handleRemap = async () => {
    if (!capability) return;
    if (
      !window.confirm(
        `Remap the parent component “${componentName}”? AI mappings will be regenerated; existing human-mapped capabilities are preserved.`,
      )
    ) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      // Per-capability remap doesn't exist server-side — fall back to the
      // component-level remap which is what the user means by
      // "Remap (for components)".
      await remapCspInheritedComponent(componentId);
      onMutated();
      // The remap may have replaced this capability id; close so the parent
      // list reload surfaces the fresh rows.
      onClose();
    } catch (err) {
      const e = err as { errorCode?: string; message?: string };
      setError(
        e?.errorCode === 'AI_MAPPING_UNAVAILABLE'
          ? 'AI capability mapping service is unavailable. Try again later.'
          : (e?.message ?? 'Failed to remap.'),
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <aside
      className="fixed inset-y-0 right-0 z-40 flex w-full max-w-xl flex-col border-l border-gray-200 bg-white shadow-xl"
      aria-label="Capability detail"
      data-testid="csp-capability-drawer"
    >
      {/* Header */}
      <header className="flex items-start justify-between gap-2 border-b border-gray-200 px-4 py-3">
        <div className="min-w-0 flex-1">
          <h2 className="truncate text-base font-semibold text-gray-900">
            {capability?.name ?? 'Loading…'}
          </h2>
          <p className="text-xs text-gray-500">
            {componentName} · {componentType}
            {capability && (
              <>
                {' '}
                · <StatusBadge status={capability.status} />
              </>
            )}
          </p>
        </div>
        <button
          type="button"
          onClick={onClose}
          className="rounded-md p-1 text-gray-500 hover:bg-gray-100"
          aria-label="Close detail panel"
          data-testid="csp-capability-drawer-close"
        >
          ✕
        </button>
      </header>

      {/* Body */}
      <div className="flex-1 overflow-y-auto px-4 py-3">
        {error && (
          <div
            role="alert"
            className="mb-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700"
          >
            {error}
          </div>
        )}

        {!capability ? (
          <p className="text-sm text-gray-500">Loading capability…</p>
        ) : (
          <div className="space-y-3">
            {/* Tab nav (Feature 050 / US3 — Details vs History). */}
            <div
              role="tablist"
              aria-label="Capability detail tabs"
              className="flex gap-1 border-b border-gray-200"
            >
              <button
                role="tab"
                type="button"
                aria-selected={activeTab === 'details'}
                onClick={() => setActiveTab('details')}
                className={`px-3 py-1.5 text-xs font-medium border-b-2 -mb-px ${
                  activeTab === 'details'
                    ? 'border-emerald-600 text-emerald-700'
                    : 'border-transparent text-gray-500 hover:text-gray-700'
                }`}
                data-testid="csp-capability-details-tab"
              >
                Details
              </button>
              <button
                role="tab"
                type="button"
                aria-selected={activeTab === 'history'}
                onClick={() => setActiveTab('history')}
                className={`px-3 py-1.5 text-xs font-medium border-b-2 -mb-px ${
                  activeTab === 'history'
                    ? 'border-emerald-600 text-emerald-700'
                    : 'border-transparent text-gray-500 hover:text-gray-700'
                }`}
                data-testid="csp-capability-history-tab"
              >
                History
              </button>
            </div>

            {activeTab === 'history' ? (
              <HistoryPanel
                history={history}
                loading={historyLoading}
                error={historyError}
                page={historyPage}
                pageSize={historyPageSize}
                onPageChange={setHistoryPage}
                onPageSizeChange={(n) => {
                  setHistoryPage(1);
                  setHistoryPageSize(n);
                }}
              />
            ) : (
              <div className="space-y-5">
                {/* Edit form */}
                {editing ? (
              <div className="space-y-3 rounded-md border border-indigo-200 bg-indigo-50 p-3">
                <label className="block text-xs font-medium text-gray-700">
                  Name
                  <input
                    type="text"
                    value={editName}
                    onChange={(e) => setEditName(e.target.value)}
                    maxLength={256}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                    data-testid="csp-capability-edit-name"
                  />
                </label>
                <label className="block text-xs font-medium text-gray-700">
                  Description
                  <textarea
                    value={editDescription}
                    onChange={(e) => setEditDescription(e.target.value)}
                    rows={3}
                    maxLength={2000}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                    data-testid="csp-capability-edit-description"
                  />
                </label>
                <label className="block text-xs font-medium text-gray-700">
                  Mapped NIST control IDs
                  <input
                    type="text"
                    value={editControls}
                    onChange={(e) => setEditControls(e.target.value)}
                    placeholder="AC-2, AC-2(1), SC-7"
                    className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 font-mono text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                    data-testid="csp-capability-edit-controls"
                  />
                  <span className="mt-0.5 block text-[11px] text-gray-500">
                    Comma- or space-separated. Saving stamps this row as a
                    human mapping (mappedBy=User) and resolves any pending
                    NeedsReview.
                  </span>
                </label>
                <div className="flex justify-end gap-2">
                  <button
                    type="button"
                    onClick={() => setEditing(false)}
                    disabled={busy}
                    className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    onClick={handleSaveEdit}
                    disabled={
                      busy ||
                      editName.trim().length === 0 ||
                      editDescription.trim().length === 0 ||
                      parsedEditControls.length === 0
                    }
                    className="rounded-md bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-indigo-300"
                    data-testid="csp-capability-edit-save"
                  >
                    {busy ? 'Saving…' : 'Save changes'}
                  </button>
                </div>
              </div>
            ) : (
              <dl className="divide-y divide-gray-100 rounded-md border border-gray-200 text-sm">
                <Row label="Description">
                  {capability.description ? (
                    capability.description
                  ) : (
                    <em className="text-gray-400">(none)</em>
                  )}
                </Row>
                <Row label="NIST controls">
                  {capability.mappedNistControlIds.length === 0 ? (
                    <em className="text-gray-400">(none)</em>
                  ) : (
                    <div className="flex flex-wrap gap-1">
                      {capability.mappedNistControlIds.map((id) => (
                        <span
                          key={id}
                          className="inline-flex items-center rounded bg-indigo-50 px-1.5 py-0.5 text-[11px] font-medium text-indigo-700"
                        >
                          {id}
                        </span>
                      ))}
                    </div>
                  )}
                </Row>
                <Row label="Mapped by">
                  {capability.mappedBy}
                  {typeof capability.mappingConfidence === 'number' && (
                    <span className="ml-2 text-xs text-gray-500">
                      (confidence {(capability.mappingConfidence * 100).toFixed(0)}%)
                    </span>
                  )}
                </Row>
                {capability.status === 'NeedsReview' && capability.mappingFailureReason && (
                  <Row label="Review reason">
                    <span className="text-amber-700">{capability.mappingFailureReason}</span>
                  </Row>
                )}
                {capability.createdAt && (
                  <Row label="Created">
                    {new Date(capability.createdAt).toLocaleString()}
                    {capability.createdBy && <> by {capability.createdBy}</>}
                  </Row>
                )}
                {capability.reviewedAt && (
                  <Row label="Last reviewed">
                    {new Date(capability.reviewedAt).toLocaleString()}
                    {capability.reviewedBy && <> by {capability.reviewedBy}</>}
                  </Row>
                )}
                {capability.reviewerNote && (
                  <Row label="Reviewer note">{capability.reviewerNote}</Row>
                )}
              </dl>
            )}

            {/* Review form — only when this capability needs review */}
            {canManage && capability.status === 'NeedsReview' && reviewing && (
              <div className="space-y-3 rounded-md border border-amber-200 bg-amber-50 p-3">
                <h3 className="text-sm font-semibold text-gray-900">Resolve review</h3>
                <label className="block text-xs font-medium text-gray-700">
                  Mapped NIST control IDs
                  <input
                    type="text"
                    value={reviewControls}
                    onChange={(e) => setReviewControls(e.target.value)}
                    placeholder="AC-2, AC-2(1), SC-7"
                    className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 font-mono text-sm focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500"
                    data-testid="csp-capability-review-controls"
                  />
                </label>
                <label className="block text-xs font-medium text-gray-700">
                  Reviewer note (optional)
                  <textarea
                    value={reviewerNote}
                    onChange={(e) => setReviewerNote(e.target.value)}
                    rows={2}
                    maxLength={2000}
                    className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-amber-500 focus:outline-none focus:ring-1 focus:ring-amber-500"
                    data-testid="csp-capability-review-note"
                  />
                </label>
                <div className="flex justify-end gap-2">
                  <button
                    type="button"
                    onClick={() => setReviewing(false)}
                    disabled={busy}
                    className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    onClick={handleReview}
                    disabled={busy || parsedReviewControls.length === 0}
                    className="rounded-md bg-amber-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-amber-700 disabled:cursor-not-allowed disabled:bg-amber-300"
                    data-testid="csp-capability-review-submit"
                  >
                    {busy ? 'Resolving…' : 'Resolve review'}
                  </button>
                </div>
              </div>
            )}

            {/* Action toolbar — CSP-Admin only */}
            {canManage && !editing && !reviewing && (
              <div className="flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  onClick={() => {
                    setEditName(capability.name);
                    setEditDescription(capability.description ?? '');
                    setEditControls(capability.mappedNistControlIds.join(', '));
                    setEditing(true);
                  }}
                  disabled={busy || capability.status === 'Archived'}
                  className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
                  data-testid="csp-capability-edit-toggle"
                  title={capability.status === 'Archived' ? 'Cannot edit an archived capability' : undefined}
                >
                  Edit
                </button>
                {capability.status === 'NeedsReview' && (
                  <button
                    type="button"
                    onClick={() => {
                      setReviewControls(capability.mappedNistControlIds.join(', '));
                      setReviewerNote('');
                      setReviewing(true);
                    }}
                    disabled={busy}
                    className="rounded-md bg-amber-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-amber-700 disabled:cursor-not-allowed disabled:bg-amber-300"
                    data-testid="csp-capability-review-toggle"
                  >
                    Resolve review
                  </button>
                )}
                {capability.status !== 'Archived' && (
                  <button
                    type="button"
                    onClick={handleArchive}
                    disabled={busy}
                    className="rounded-md border border-red-300 bg-white px-3 py-1.5 text-xs font-medium text-red-700 hover:bg-red-50 disabled:opacity-50"
                    data-testid="csp-capability-archive"
                  >
                    Archive
                  </button>
                )}
                {capability.status !== 'Archived' && (
                  <button
                    type="button"
                    onClick={() => setShowMoveDialog(true)}
                    disabled={busy || hasEligibleTarget !== true}
                    className="rounded-md border border-emerald-300 bg-white px-3 py-1.5 text-xs font-medium text-emerald-700 hover:bg-emerald-50 disabled:cursor-not-allowed disabled:opacity-50"
                    data-testid="csp-capability-move-toggle"
                    title={
                      hasEligibleTarget === false
                        ? 'No other CSP-inherited component exists yet. Create one first.'
                        : hasEligibleTarget === null
                          ? 'Checking eligible components…'
                          : 'Move this capability to a different CSP-inherited component'
                    }
                  >
                    Move to another component…
                  </button>
                )}
                <button
                  type="button"
                  onClick={handleRemap}
                  disabled={busy}
                  className="rounded-md border border-indigo-300 bg-white px-3 py-1.5 text-xs font-medium text-indigo-700 hover:bg-indigo-50 disabled:opacity-50"
                  title="Re-run AI capability mapping on the parent component"
                  data-testid="csp-capability-remap"
                >
                  Remap parent component
                </button>
              </div>
            )}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Feature 050 / US2 — Move-capability dialog. Mounted conditionally
          to keep DOM clean when not in use. The dialog owns its own
          candidate fetch (pageSize=200) per contract § 3.1.2. */}
      {showMoveDialog && capability && (
        <MoveCapabilityDialog
          capability={capability}
          sourceComponentId={componentId}
          onCancel={() => setShowMoveDialog(false)}
          onMoved={(next) => {
            setShowMoveDialog(false);
            setCapability(next);
            onMutated();
          }}
        />
      )}
    </aside>
  );
}

function parseControlIds(raw: string): string[] {
  return raw
    .split(/[,\s]+/)
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}

function Row({ label, children }: { label: string; children: ReactNode }): ReactElement {
  return (
    <div className="grid grid-cols-3 gap-3 px-3 py-2">
      <dt className="font-medium text-gray-700">{label}</dt>
      <dd className="col-span-2 text-gray-900">{children}</dd>
    </div>
  );
}

function StatusBadge({ status }: { status: CspInheritedCapability['status'] }): ReactElement {
  const palette =
    status === 'Mapped'
      ? 'bg-emerald-100 text-emerald-800'
      : status === 'NeedsReview'
        ? 'bg-amber-100 text-amber-800'
        : 'bg-gray-200 text-gray-700'; // Archived
  const label = status === 'NeedsReview' ? 'Needs review' : status;
  return (
    <span className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${palette}`}>
      {label}
    </span>
  );
}

// ---------------------------------------------------------------------------
// Feature 050 / US3 — History tab panel
// ---------------------------------------------------------------------------

const PAGE_SIZE_OPTIONS = [25, 50, 100, 200] as const;

interface HistoryPanelProps {
  history: CapabilityHistoryPage | null;
  loading: boolean;
  error: string | null;
  page: number;
  pageSize: number;
  onPageChange: (next: number) => void;
  onPageSizeChange: (next: number) => void;
}

function HistoryPanel({
  history,
  loading,
  error,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
}: HistoryPanelProps): ReactElement {
  if (loading) {
    return <p className="text-sm text-gray-500">Loading history…</p>;
  }
  if (error) {
    return (
      <div
        role="alert"
        className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700"
      >
        {error}
      </div>
    );
  }
  if (!history || history.items.length === 0) {
    return (
      <p className="rounded-md border border-gray-200 bg-gray-50 px-3 py-3 text-sm text-gray-600">
        No history yet.
      </p>
    );
  }

  const totalPages = Math.max(1, Math.ceil(history.total / history.pageSize));

  return (
    <div className="space-y-2">
      <ol
        className="divide-y divide-gray-100 rounded-md border border-gray-200"
        data-testid="csp-capability-history-list"
      >
        {history.items.map((evt) => (
          <li
            key={evt.id}
            data-testid={`csp-capability-history-row-${evt.id}`}
            data-event-type={evt.eventType}
            className="flex items-start gap-2 px-3 py-2 text-xs"
          >
            <span className="mt-0.5 inline-block w-6 text-center text-base leading-none">
              {iconFor(evt.eventType)}
            </span>
            <div className="flex-1 space-y-1">
              <div className="flex flex-wrap items-baseline gap-2">
                <span className="font-medium text-gray-900">{evt.summary}</span>
                {pillsFor(evt)}
              </div>
              <div className="text-[11px] text-gray-500">
                {formatTime(evt.occurredAt)} · {evt.actorOid}
              </div>
              {previewFor(evt)}
            </div>
          </li>
        ))}
      </ol>

      <div className="flex flex-wrap items-center justify-between gap-2 text-[11px] text-gray-600">
        <span>
          Page {history.page} of {totalPages} · {history.total} event
          {history.total === 1 ? '' : 's'}
        </span>
        <div className="flex items-center gap-2">
          <label className="flex items-center gap-1">
            Rows
            <select
              value={pageSize}
              onChange={(e) => onPageSizeChange(Number(e.target.value))}
              className="rounded-md border border-gray-300 bg-white px-1.5 py-0.5 text-[11px]"
              data-testid="csp-capability-history-pagesize"
            >
              {PAGE_SIZE_OPTIONS.map((n) => (
                <option key={n} value={n}>
                  {n}
                </option>
              ))}
            </select>
          </label>
          <button
            type="button"
            onClick={() => onPageChange(Math.max(1, page - 1))}
            disabled={page <= 1}
            className="rounded-md border border-gray-300 bg-white px-2 py-0.5 text-[11px] disabled:opacity-50"
            data-testid="csp-capability-history-prev"
          >
            Prev
          </button>
          <button
            type="button"
            onClick={() => onPageChange(Math.min(totalPages, page + 1))}
            disabled={page >= totalPages}
            className="rounded-md border border-gray-300 bg-white px-2 py-0.5 text-[11px] disabled:opacity-50"
            data-testid="csp-capability-history-next"
          >
            Next
          </button>
        </div>
      </div>
    </div>
  );
}

function iconFor(t: CapabilityHistoryEventType): string {
  switch (t) {
    case 'Created':
      return '+';
    case 'Edited':
      return '✎';
    case 'Reviewed':
      return '✓';
    case 'Moved':
      return '→';
    case 'Archived':
      return '🗑';
    case 'Unarchived':
      return '↺';
  }
}

function pillsFor(evt: CapabilityHistoryEvent): ReactElement | null {
  const pills: ReactElement[] = [];
  const meta = evt.metadata;
  if (meta && typeof meta === 'object') {
    if (
      evt.eventType === 'Created' &&
      meta.markedMappedImmediately === true
    ) {
      pills.push(
        <Pill
          key="auto-mapped"
          className="bg-emerald-100 text-emerald-800"
        >
          Auto-mapped on create
        </Pill>,
      );
    }
    if (meta.source === 'Remap') {
      pills.push(
        <Pill key="remap" className="bg-indigo-100 text-indigo-800">
          Remap
        </Pill>,
      );
    }
  }
  return pills.length === 0 ? null : <>{pills}</>;
}

function previewFor(evt: CapabilityHistoryEvent): ReactElement | null {
  const meta = evt.metadata;
  if (!meta || typeof meta !== 'object') return null;
  switch (evt.eventType) {
    case 'Reviewed':
      return meta.reviewerNote ? (
        <blockquote className="border-l-2 border-amber-300 pl-2 italic text-gray-700">
          {meta.reviewerNote}
        </blockquote>
      ) : null;
    case 'Moved':
      return meta.fromComponentId || meta.toComponentId ? (
        <p className="text-[11px] text-gray-500">
          from <code>{meta.fromComponentId ?? '—'}</code> to{' '}
          <code>{meta.toComponentId ?? '—'}</code>
        </p>
      ) : null;
    case 'Edited':
      return meta.fields && meta.fields.length > 0 ? (
        <p className="text-[11px] text-gray-500">
          Fields: <span className="font-mono">{meta.fields.join(', ')}</span>
        </p>
      ) : null;
    default:
      return null;
  }
}

function Pill({
  children,
  className,
}: {
  children: ReactNode;
  className: string;
}): ReactElement {
  return (
    <span className={`inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-medium ${className}`}>
      {children}
    </span>
  );
}

function formatTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}
