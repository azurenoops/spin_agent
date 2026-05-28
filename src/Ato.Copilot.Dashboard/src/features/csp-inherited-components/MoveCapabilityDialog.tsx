import { useEffect, useMemo, useState, type ReactElement } from 'react';
import {
  isUnavailable,
  listCspInheritedComponents,
  reparentCspInheritedCapability,
  type CspInheritedCapability,
  type CspInheritedComponent,
} from './api';

interface Props {
  /** Capability being moved; supplies `id` and `rowVersion`. */
  capability: CspInheritedCapability;
  /**
   * Current parent component id. Passed in by the drawer (the wire DTO key
   * is `cspInheritedComponentId` but the dashboard type uses `componentId`,
   * and the parent prop is the source of truth either way).
   */
  sourceComponentId: string;
  /** Fires after a successful move; parent re-fetches and closes the dialog. */
  onMoved: (next: CspInheritedCapability) => void;
  /** Fires on cancel / dismiss / "Reload capability" click. */
  onCancel: () => void;
}

/** Hard cap on initial component fetch (per http-api.md § 2 client contract). */
const PAGE_SIZE = 200;

/**
 * `MoveCapabilityDialog` — Feature 050 / US2 (FR-002 / FR-012).
 *
 * Reparent a CSP-inherited capability to a different non-archived
 * component in the caller's tenant. Single eager fetch of candidates
 * (pageSize=200, status=Published), client-side filter-as-you-type,
 * If-Match required on confirm (412 surfaces inline + "Reload capability"
 * link). Contract pinned in
 * `specs/050-csp-capability-lifecycle/contracts/frontend-types.md § 3.1`.
 */
export default function MoveCapabilityDialog({
  capability,
  sourceComponentId,
  onMoved,
  onCancel,
}: Props): ReactElement {
  const [candidates, setCandidates] = useState<ReadonlyArray<CspInheritedComponent> | null>(null);
  const [candidatesTotal, setCandidatesTotal] = useState(0);
  const [candidatesError, setCandidatesError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [filter, setFilter] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<{ code: string; message: string } | null>(null);

  // Eager fetch — exactly once per mount. The component picker is intentionally
  // un-paginated; pageSize=200 is the server cap.
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const result = await listCspInheritedComponents({
          page: 1,
          pageSize: PAGE_SIZE,
          status: 'Published',
        });
        if (cancelled) return;
        if (isUnavailable(result)) {
          setCandidatesError('CSP inherited components are not available in this deployment.');
          setIsLoading(false);
          return;
        }
        setCandidates(result.items);
        setCandidatesTotal(result.total ?? result.items.length);
        setIsLoading(false);
      } catch (err) {
        if (cancelled) return;
        const e = err as { message?: string };
        setCandidatesError(e?.message ?? 'Failed to load components.');
        setIsLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // Exclude the current parent; apply client-side filter (case-insensitive
  // substring match against `name`). The picker NEVER shows the source
  // component — "same component" is unselectable per contract § 3.1.2.
  const visibleCandidates = useMemo<ReadonlyArray<CspInheritedComponent>>(() => {
    if (!candidates) return [];
    const q = filter.trim().toLowerCase();
    return candidates.filter((c) => {
      if (c.id === sourceComponentId) return false;
      if (q.length === 0) return true;
      return c.name.toLowerCase().includes(q);
    });
  }, [candidates, sourceComponentId, filter]);

  const handleConfirm = async () => {
    if (selectedId === null) return;
    if (!capability.rowVersion) {
      setSubmitError({
        code: 'MISSING_ROW_VERSION',
        message: 'Capability is missing its concurrency stamp — reload and retry.',
      });
      return;
    }
    setSubmitError(null);
    setIsSubmitting(true);
    try {
      const next = await reparentCspInheritedCapability(
        sourceComponentId,
        capability.id,
        { targetComponentId: selectedId },
        capability.rowVersion,
      );
      onMoved(next);
    } catch (err) {
      const e = err as { errorCode?: string; message?: string };
      setSubmitError({
        code: e?.errorCode ?? 'MOVE_FAILED',
        message: e?.message ?? 'Failed to move capability.',
      });
    } finally {
      setIsSubmitting(false);
    }
  };

  const confirmDisabled = isSubmitting || selectedId === null;
  const showOver200Notice = candidatesTotal > PAGE_SIZE;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="csp-move-capability-title"
      data-testid="csp-move-capability-dialog"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
    >
      <div className="w-full max-w-lg rounded-lg bg-white shadow-xl">
        <header className="border-b border-gray-200 px-4 py-3">
          <h2
            id="csp-move-capability-title"
            className="text-base font-semibold text-gray-900"
          >
            Move “{capability.name}” to another component
          </h2>
          <p className="mt-0.5 text-xs text-gray-500">
            The capability will reset to <strong>Needs review</strong> at its
            new parent. Existing field values and mapped controls are
            preserved.
          </p>
        </header>

        <div className="px-4 py-3 space-y-3">
          {submitError && (
            <div
              role="alert"
              className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700"
            >
              <p>{submitError.message}</p>
              {submitError.code === 'ROW_VERSION_MISMATCH' && (
                <button
                  type="button"
                  onClick={onCancel}
                  className="mt-1 underline text-red-800 hover:text-red-900"
                  data-testid="csp-move-capability-reload"
                >
                  Reload capability
                </button>
              )}
            </div>
          )}

          {isLoading && (
            <p className="text-sm text-gray-500">Loading components…</p>
          )}

          {candidatesError && !isLoading && (
            <div
              role="alert"
              className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700"
            >
              {candidatesError}
            </div>
          )}

          {!isLoading && !candidatesError && candidates && (
            <>
              <label className="block">
                <span className="block text-xs font-medium text-gray-700">
                  Filter components
                </span>
                <input
                  type="text"
                  value={filter}
                  onChange={(e) => setFilter(e.target.value)}
                  placeholder="Type to narrow…"
                  className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-emerald-500 focus:outline-none focus:ring-1 focus:ring-emerald-500"
                  data-testid="csp-move-capability-filter"
                />
              </label>

              {showOver200Notice && (
                <p className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800">
                  Showing first 200 of {candidatesTotal} components. Refine
                  your filter if your target is not listed.
                </p>
              )}

              <ul
                className="max-h-72 overflow-y-auto rounded-md border border-gray-200 divide-y divide-gray-100"
                data-testid="csp-move-capability-list"
              >
                {visibleCandidates.length === 0 ? (
                  <li className="px-3 py-2 text-xs text-gray-500">
                    No matching components.
                  </li>
                ) : (
                  visibleCandidates.map((c) => {
                    const selected = c.id === selectedId;
                    return (
                      <li key={c.id}>
                        <button
                          type="button"
                          onClick={() => setSelectedId(c.id)}
                          data-testid={`csp-move-capability-option-${c.id}`}
                          className={`flex w-full items-start gap-2 px-3 py-2 text-left text-sm hover:bg-emerald-50 ${
                            selected ? 'bg-emerald-100' : ''
                          }`}
                        >
                          <span className="mt-0.5 inline-block h-3 w-3 flex-shrink-0 rounded-full border border-gray-300">
                            {selected && (
                              <span className="block h-full w-full rounded-full bg-emerald-600" />
                            )}
                          </span>
                          <span className="flex-1">
                            <span className="block font-medium text-gray-900">
                              {c.name}
                            </span>
                            <span className="block text-[11px] text-gray-500">
                              {c.componentType}
                            </span>
                          </span>
                        </button>
                      </li>
                    );
                  })
                )}
              </ul>
            </>
          )}
        </div>

        <footer className="flex justify-end gap-2 border-t border-gray-200 px-4 py-3">
          <button
            type="button"
            onClick={onCancel}
            disabled={isSubmitting}
            className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
            data-testid="csp-move-capability-cancel"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={handleConfirm}
            disabled={confirmDisabled}
            className="rounded-md bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-700 disabled:cursor-not-allowed disabled:bg-emerald-300"
            data-testid="csp-move-capability-confirm"
          >
            {isSubmitting ? 'Moving…' : 'Move capability'}
          </button>
        </footer>
      </div>
    </div>
  );
}
