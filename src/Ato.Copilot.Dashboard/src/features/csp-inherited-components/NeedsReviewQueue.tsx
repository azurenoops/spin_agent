import { useEffect, useState, type ReactElement } from 'react';
import {
  listCspInheritedCapabilities,
  reviewCspInheritedCapability,
  type CspInheritedCapability,
} from './api';

interface Props {
  componentId: string;
  /** Whether the caller is allowed to resolve `NeedsReview` items. */
  canReview: boolean;
}

/**
 * `NeedsReviewQueue` — Feature 048 / US9 / T214 sub-component.
 *
 * Lists all `NeedsReview` capabilities for the selected component and
 * provides an inline form for the operator to:
 *   1. Enter the operator-chosen NIST control IDs (comma-separated).
 *   2. Optionally add a reviewer note.
 *   3. Submit → `PATCH /capabilities/{id}/review` → status flips to `Mapped`.
 *
 * Read-only for non-CSP-Admin callers (the inputs are hidden when
 * `canReview === false`).
 */
export default function NeedsReviewQueue({ componentId, canReview }: Props): ReactElement {
  const [items, setItems] = useState<CspInheritedCapability[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setItems(null);
    setLoadError(null);
    void (async () => {
      try {
        const next = await listCspInheritedCapabilities(componentId, 'NeedsReview');
        if (!cancelled) setItems(next);
      } catch (err) {
        const e = err as { errorCode?: string; message?: string };
        if (!cancelled) {
          setLoadError(e?.message ?? 'Failed to load capabilities.');
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [componentId]);

  if (loadError) {
    return (
      <div role="alert" className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
        {loadError}
      </div>
    );
  }

  if (items === null) {
    return <p className="text-sm text-gray-500">Loading capabilities awaiting review…</p>;
  }

  if (items.length === 0) {
    return (
      <p className="text-sm text-gray-500">
        No capabilities awaiting review for this component.
      </p>
    );
  }

  return (
    <ul className="divide-y divide-gray-100 rounded-md border border-gray-200">
      {items.map((item) => (
        <NeedsReviewRow
          key={item.id}
          capability={item}
          componentId={componentId}
          canReview={canReview}
          onResolved={(updated) =>
            setItems((prev) =>
              prev ? prev.filter((c) => c.id !== updated.id) : prev,
            )
          }
        />
      ))}
    </ul>
  );
}

function NeedsReviewRow({
  capability,
  componentId,
  canReview,
  onResolved,
}: {
  capability: CspInheritedCapability;
  componentId: string;
  canReview: boolean;
  onResolved: (updated: CspInheritedCapability) => void;
}): ReactElement {
  const [controlIds, setControlIds] = useState<string>(
    capability.mappedNistControlIds.join(', '),
  );
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const parsed = controlIds
    .split(',')
    .map((s) => s.trim())
    .filter((s) => s.length > 0);

  const submit = async () => {
    if (parsed.length === 0) {
      setError('At least one NIST control ID is required.');
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const updated = await reviewCspInheritedCapability(componentId, capability.id, {
        mappedNistControlIds: parsed,
        reviewerNote: note.trim() ? note.trim() : undefined,
      });
      onResolved(updated);
    } catch (err) {
      const e = err as { errorCode?: string; message?: string };
      setError(e?.message ?? 'Failed to resolve capability.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <li className="px-3 py-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <p className="font-medium text-gray-900">{capability.name}</p>
          {capability.description && (
            <p className="mt-0.5 text-xs text-gray-500">{capability.description}</p>
          )}
          {capability.mappingFailureReason && (
            <p className="mt-1 text-xs text-amber-700">
              Reason: {capability.mappingFailureReason}
            </p>
          )}
          {capability.mappingConfidence !== null &&
            capability.mappingConfidence !== undefined && (
              <p className="mt-1 text-xs text-gray-500">
                Confidence: {(capability.mappingConfidence * 100).toFixed(0)}%
              </p>
            )}
        </div>
        <span className="inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">
          Needs review
        </span>
      </div>

      {canReview && (
        <div className="mt-2 space-y-2">
          <label className="block text-xs font-medium text-gray-700">
            NIST control IDs (comma-separated)
            <input
              type="text"
              value={controlIds}
              onChange={(e) => setControlIds(e.target.value)}
              placeholder="e.g. AC-2, AC-2(1), IA-5"
              disabled={saving}
              className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-100"
            />
          </label>
          <label className="block text-xs font-medium text-gray-700">
            Reviewer note (optional)
            <textarea
              value={note}
              onChange={(e) => setNote(e.target.value)}
              rows={2}
              maxLength={2048}
              disabled={saving}
              className="mt-1 block w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-100"
            />
          </label>
          {error && (
            <p role="alert" className="text-xs text-red-700">
              {error}
            </p>
          )}
          <button
            type="button"
            onClick={submit}
            disabled={saving || parsed.length === 0}
            className="rounded-md bg-indigo-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-indigo-700 disabled:cursor-not-allowed disabled:bg-indigo-300"
          >
            {saving ? 'Saving…' : 'Mark as mapped'}
          </button>
        </div>
      )}
    </li>
  );
}
