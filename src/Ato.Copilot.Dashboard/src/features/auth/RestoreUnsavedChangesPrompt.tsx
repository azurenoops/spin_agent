import { useEffect, useState } from 'react';
import type { UnsavedSnapshot } from './types';

/**
 * Feature 051 T062d [US2] — surfaces a dismissible banner on the next
 * sign-in when {@link useIdleFormStateBackup} has persisted
 * snapshots to `localStorage`. Implements the second half of FR-008
 * per `contracts/frontend-types.md § 4.5`.
 *
 * - Scans `localStorage` for keys matching `ato.unsavedChanges.{oid}.*`
 *   ONLY (other-oid keys ignored — multi-user device safe).
 * - Renders nothing when no snapshots exist.
 * - Per-form "Restore" button dispatches a `'ato:restore-unsaved'`
 *   CustomEvent with the parsed snapshot AND removes the localStorage
 *   key.
 * - Per-form "Discard" button removes the localStorage key without
 *   dispatching.
 *
 * Page-level forms opt-in by listening for `'ato:restore-unsaved'`,
 * filtering on `detail.formId`, and re-hydrating their state from
 * `detail.data`.
 */
export interface RestoreUnsavedChangesPromptProps {
  /** Authenticated user's oid — keys outside this scope are ignored. */
  oid: string;
}

interface SnapshotEntry extends UnsavedSnapshot {
  /** The localStorage key that produced this entry. */
  storageKey: string;
}

function readSnapshots(oid: string): SnapshotEntry[] {
  const prefix = `ato.unsavedChanges.${oid}.`;
  const out: SnapshotEntry[] = [];
  for (let i = 0; i < localStorage.length; i += 1) {
    const k = localStorage.key(i);
    if (!k || !k.startsWith(prefix)) continue;
    const raw = localStorage.getItem(k);
    if (raw === null) continue;
    try {
      const parsed = JSON.parse(raw) as { savedAt?: string; data?: unknown };
      const formId = k.substring(prefix.length);
      out.push({
        storageKey: k,
        formId,
        savedAt: parsed.savedAt ?? '',
        data: parsed.data,
      });
    } catch {
      // Corrupted entry — skip it. Leave the key in place so a future
      // version can attempt salvage if needed.
    }
  }
  // Stable ordering — most recent first by savedAt.
  return out.sort((a, b) => b.savedAt.localeCompare(a.savedAt));
}

function formatSavedAt(iso: string): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  try {
    return d.toLocaleString();
  } catch {
    return iso;
  }
}

export default function RestoreUnsavedChangesPrompt({
  oid,
}: RestoreUnsavedChangesPromptProps) {
  const [entries, setEntries] = useState<SnapshotEntry[]>(() => readSnapshots(oid));

  // Re-scan if `oid` changes (sign-out / sign-in as another user in
  // the same window — rare but possible in dev with SimulationPanel).
  useEffect(() => {
    setEntries(readSnapshots(oid));
  }, [oid]);

  if (entries.length === 0) {
    return null;
  }

  const handleRestore = (entry: SnapshotEntry) => {
    window.dispatchEvent(
      new CustomEvent('ato:restore-unsaved', {
        detail: { formId: entry.formId, savedAt: entry.savedAt, data: entry.data },
      }),
    );
    localStorage.removeItem(entry.storageKey);
    setEntries((prev) => prev.filter((e) => e.storageKey !== entry.storageKey));
  };

  const handleDiscard = (entry: SnapshotEntry) => {
    localStorage.removeItem(entry.storageKey);
    setEntries((prev) => prev.filter((e) => e.storageKey !== entry.storageKey));
  };

  return (
    <div
      role="region"
      aria-label="Unsaved changes"
      className="fixed bottom-4 right-4 z-40 w-96 max-w-[calc(100vw-2rem)] rounded-lg border border-amber-200 bg-amber-50 p-4 shadow-lg"
    >
      <h2 className="text-sm font-semibold text-amber-900">
        Unsaved changes from your last session
      </h2>
      <p className="mt-1 text-xs text-amber-800">
        These drafts were saved when you were signed out due to inactivity.
      </p>
      <ul className="mt-3 space-y-2">
        {entries.map((entry) => (
          <li
            key={entry.storageKey}
            className="rounded border border-amber-200 bg-white p-2"
          >
            <div className="flex items-start justify-between gap-2">
              <div className="min-w-0">
                <div className="truncate text-sm font-medium text-gray-900">
                  {entry.formId}
                </div>
                <div className="truncate text-xs text-gray-500">
                  Saved {formatSavedAt(entry.savedAt)}
                </div>
              </div>
              <div className="flex flex-shrink-0 gap-2">
                <button
                  type="button"
                  onClick={() => handleRestore(entry)}
                  className="rounded bg-indigo-600 px-3 py-1 text-xs font-medium text-white hover:bg-indigo-700"
                >
                  Restore
                </button>
                <button
                  type="button"
                  onClick={() => handleDiscard(entry)}
                  className="rounded border border-gray-300 bg-white px-3 py-1 text-xs font-medium text-gray-700 hover:bg-gray-50"
                >
                  Discard
                </button>
              </div>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}
