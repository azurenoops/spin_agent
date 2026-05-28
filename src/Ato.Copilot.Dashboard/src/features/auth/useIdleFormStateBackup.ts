import { useEffect, useRef } from 'react';
import type {
  FormSnapshotSerializer,
  UseIdleFormStateBackupResult,
} from './types';

/**
 * Feature 051 T062b [US2] — implements FR-008. When the
 * {@link useIdleTimer} fires `'ato:idle-warning'` 60 seconds before
 * idle sign-out, this hook walks every registered form serializer
 * synchronously and writes the JSON snapshot to `localStorage` under
 * key `ato.unsavedChanges.{oid}.{formId}`. The snapshot survives the
 * subsequent sign-out so the next sign-in can offer a "Restore
 * unsaved changes" prompt via {@link RestoreUnsavedChangesPrompt}.
 *
 * - Scope: keys are namespaced by `oid` so a shared device never
 *   cross-pollinates between users.
 * - Idle sign-out preserves snapshots (the whole point of FR-008).
 * - Explicit sign-out clears them via {@link purgeUnsavedChanges}.
 *
 * The hook does NOT run on mount and does NOT poll. The only trigger
 * is the `'ato:idle-warning'` event. Cleanup on unmount removes the
 * window event listener.
 */
export function useIdleFormStateBackup(oid: string): UseIdleFormStateBackupResult {
  // Use a Map kept in a ref so register/unregister mutations do NOT
  // trigger a re-render — the serializer list is purely an internal
  // bookkeeping concern.
  const serializersRef = useRef<Map<string, () => unknown>>(new Map());

  useEffect(() => {
    const handler = () => {
      const ts = new Date().toISOString();
      for (const [formId, serialize] of serializersRef.current) {
        try {
          const data = serialize();
          const key = `ato.unsavedChanges.${oid}.${formId}`;
          localStorage.setItem(key, JSON.stringify({ savedAt: ts, data }));
        } catch {
          // A failing serializer must not block the rest of the
          // snapshot pass — best-effort persistence is the contract.
        }
      }
    };

    window.addEventListener('ato:idle-warning', handler as EventListener);
    return () => {
      window.removeEventListener('ato:idle-warning', handler as EventListener);
    };
  }, [oid]);

  return {
    register: <T,>(s: FormSnapshotSerializer<T>) => {
      serializersRef.current.set(s.formId, s.serialize);
    },
    unregister: (formId: string) => {
      serializersRef.current.delete(formId);
    },
  };
}

/**
 * Idempotent — removes every `ato.unsavedChanges.{oid}.*` localStorage
 * entry. Call on explicit (non-idle) sign-out so the next session does
 * NOT see a "Restore" prompt for snapshots that were committed via
 * idle. Other-`oid` keys are left untouched (multi-user device safe).
 */
export function purgeUnsavedChanges(oid: string): void {
  const prefix = `ato.unsavedChanges.${oid}.`;
  const toRemove: string[] = [];
  for (let i = 0; i < localStorage.length; i += 1) {
    const k = localStorage.key(i);
    if (k && k.startsWith(prefix)) {
      toRemove.push(k);
    }
  }
  for (const k of toRemove) {
    localStorage.removeItem(k);
  }
}
