// =============================================================================
// anchorRegistry.ts — Editor Suite Phase 5 (work item 9e3ff674970d4e4a)
//
// AnchorRegistry: stable, transaction-driven anchor IDs for every editor span.
//
// Responsibilities:
//   - Assign a stable UUID anchor ID to each provenance-tracked span on insert.
//   - Remap anchor offsets atomically when text is inserted, deleted, or
//     replaced so IDs never drift silently (CI drift gate enforces zero orphans).
//   - Track provenance attribution per anchor: source_id + user_modified flag.
//   - Subscribe/unsubscribe pattern for consumers (TraceabilityPanel, citation
//     panel) to react to anchor changes without polling.
//   - Resolve an anchor ID → current position at any time.
//
// Framework-agnostic: no ProseMirror / TipTap / Slate dependency.
// Works with any document model that exposes character offsets.
//
// Invariants:
//   1. Anchor IDs are v4 UUIDs assigned once and never reused after removal.
//   2. Remap operations are atomic — either all anchors in a transaction update,
//      or none do (throws on invalid input).
//   3. Attribution (source_id, user_modified) survives remap; it is mutated only
//      by explicit markUserModified() calls.
//   4. Observers are notified synchronously after every mutation.
// =============================================================================

import { v4 as uuidv4 } from 'uuid';

// ── Public Types ──────────────────────────────────────────────────────────────

/** The stable identifier for one anchor. Never reused after remove(). */
export type AnchorId = string;

/**
 * Current position of an anchor within the document.
 * Both values are character offsets (inclusive start, exclusive end).
 */
export interface AnchorPosition {
  start: number;
  end: number;
}

/**
 * Provenance attribution stored alongside each anchor.
 * Mirrors the Phase-1 ProvenanceState model but is kept as a lightweight,
 * registry-owned copy — the canonical ProvenanceState lives on the editor node.
 */
export interface AnchorAttribution {
  /** Stable ID of the originating source document / passage. */
  source_id: string;
  /** True when the user has manually edited text inside this span. */
  user_modified: boolean;
  /** ISO 8601 timestamp of first insertion. */
  inserted_at: string;
}

/** Full snapshot of one registered anchor. */
export interface AnchorRecord {
  id: AnchorId;
  position: AnchorPosition;
  attribution: AnchorAttribution;
}

/**
 * Describes a single text mutation for a remap transaction.
 *
 * - insert: text was inserted before `at`; existing content at `at` and after
 *   shifts right by `length` characters.
 * - delete: `length` characters starting at `at` were removed; content after
 *   the deleted range shifts left.
 * - replace: `deleteLength` characters at `at` are replaced by text of
 *   `insertLength` characters. Net shift = insertLength - deleteLength.
 */
export type TextMutation =
  | { kind: 'insert'; at: number; length: number }
  | { kind: 'delete'; at: number; length: number }
  | { kind: 'replace'; at: number; deleteLength: number; insertLength: number };

/** Observer callback fired after every successful mutation batch. */
export type AnchorObserver = (snapshot: ReadonlyMap<AnchorId, AnchorRecord>) => void;

// ── Internal ──────────────────────────────────────────────────────────────────

interface MutableAnchorRecord {
  id: AnchorId;
  position: { start: number; end: number };
  attribution: AnchorAttribution;
}

// ── Registry Class ────────────────────────────────────────────────────────────

/**
 * Session-scoped AnchorRegistry.
 * Instantiate once per document; discard on navigation / new document load.
 *
 * @example
 * const registry = new AnchorRegistry();
 * const id = registry.insert({ start: 10, end: 25 }, { source_id: 'doc-1', user_modified: false });
 * registry.remap({ kind: 'insert', at: 5, length: 3 }); // shifts start/end by +3
 * const pos = registry.resolve(id); // { start: 13, end: 28 }
 */
export class AnchorRegistry {
  private readonly _anchors = new Map<AnchorId, MutableAnchorRecord>();
  private readonly _observers = new Set<AnchorObserver>();

  // ── Mutations ───────────────────────────────────────────────────────────────

  /**
   * Register a new anchor for a span at the given position.
   * Returns the stable AnchorId that identifies this span for its lifetime.
   *
   * @param position   - character offsets [start, end) within the document
   * @param attribution - provenance metadata for this span
   */
  insert(
    position: AnchorPosition,
    attribution: Omit<AnchorAttribution, 'inserted_at'>
  ): AnchorId {
    if (position.start < 0 || position.end < position.start) {
      throw new RangeError(
        `AnchorRegistry.insert: invalid position [${position.start}, ${position.end})`
      );
    }
    const id: AnchorId = uuidv4();
    this._anchors.set(id, {
      id,
      position: { ...position },
      attribution: { ...attribution, inserted_at: new Date().toISOString() },
    });
    this._notify();
    return id;
  }

  /**
   * Remove an anchor permanently.
   * After removal the ID is dead — resolve() returns undefined.
   */
  remove(id: AnchorId): void {
    if (!this._anchors.has(id)) return; // idempotent
    this._anchors.delete(id);
    this._notify();
  }

  /**
   * Mark an anchor's span as user-modified.
   * Idempotent: calling twice has no effect.
   */
  markUserModified(id: AnchorId): void {
    const record = this._anchors.get(id);
    if (!record) return;
    if (record.attribution.user_modified) return; // already set
    record.attribution = { ...record.attribution, user_modified: true };
    this._notify();
  }

  /**
   * Apply one or more text mutations atomically.
   * All anchors are updated together; observers are notified once.
   *
   * Remap rules per anchor [s, e):
   *   insert at `at`:
   *     - at <= s         → s += length, e += length  (span shifts right)
   *     - s < at < e      → e += length               (span grows to contain insertion)
   *     - at >= e         → no change
   *   delete at `at`, length `n` (removes [at, at+n)):
   *     - at+n <= s       → s -= n, e -= n            (span shifts left)
   *     - at >= e         → no change
   *     - overlap         → clamp: s = max(s, at), e = max(at, e - n); if s>=e remove anchor
   *   replace at `at`, deleteLength `d`, insertLength `i`:
   *     modelled as delete(at, d) followed by insert(at, i)
   *
   * @throws RangeError on negative offsets or lengths
   */
  remap(...mutations: TextMutation[]): void {
    // Validate before touching state (atomicity guarantee)
    for (const m of mutations) {
      if (m.at < 0) throw new RangeError(`AnchorRegistry.remap: at must be >= 0, got ${m.at}`);
      if (m.kind === 'insert' && m.length < 0)
        throw new RangeError(`AnchorRegistry.remap: insert length must be >= 0`);
      if (m.kind === 'delete' && m.length < 0)
        throw new RangeError(`AnchorRegistry.remap: delete length must be >= 0`);
      if (m.kind === 'replace' && (m.deleteLength < 0 || m.insertLength < 0))
        throw new RangeError(`AnchorRegistry.remap: replace lengths must be >= 0`);
    }

    const toRemove: AnchorId[] = [];

    for (const record of this._anchors.values()) {
      let { start, end } = record.position;

      for (const mutation of mutations) {
        switch (mutation.kind) {
          case 'insert': {
            const { at, length } = mutation;
            if (at <= start) {
              start += length;
              end += length;
            } else if (at < end) {
              end += length;
            }
            break;
          }
          case 'delete': {
            const { at, length: n } = mutation;
            const delEnd = at + n;
            if (delEnd <= start) {
              start -= n;
              end -= n;
            } else if (at >= end) {
              // no overlap
            } else {
              // overlap — clamp:
              //   if deletion starts before the span, the span's new start
              //   is `at` (the content at [at, min(at+n, start)] is gone).
              //   if deletion starts inside the span, start is unchanged.
              start = at < start ? at : start;
              end = Math.max(at, end - n);
              if (start >= end) {
                toRemove.push(record.id);
              }
            }
            break;
          }
          case 'replace': {
            const { at, deleteLength: d, insertLength: i } = mutation;
            // model as delete then insert
            const delEnd = at + d;
            if (delEnd <= start) {
              start -= d;
              end -= d;
            } else if (at >= end) {
              // no overlap
            } else {
              start = at < start ? at : start;
              end = Math.max(at, end - d);
              if (start >= end) {
                toRemove.push(record.id);
                break;
              }
            }
            // insert phase
            if (at <= start) {
              start += i;
              end += i;
            } else if (at < end) {
              end += i;
            }
            break;
          }
        }
      }

      if (!toRemove.includes(record.id)) {
        record.position.start = start;
        record.position.end = end;
      }
    }

    for (const id of toRemove) {
      this._anchors.delete(id);
    }

    this._notify();
  }

  // ── Queries ─────────────────────────────────────────────────────────────────

  /**
   * Resolve an AnchorId to its current position.
   * Returns undefined if the anchor was removed (e.g. its span was deleted).
   */
  resolve(id: AnchorId): AnchorPosition | undefined {
    const record = this._anchors.get(id);
    return record ? { ...record.position } : undefined;
  }

  /**
   * Return a copy of the full AnchorRecord for an ID.
   * Returns undefined for unknown / removed IDs.
   */
  get(id: AnchorId): AnchorRecord | undefined {
    const record = this._anchors.get(id);
    if (!record) return undefined;
    return {
      id: record.id,
      position: { ...record.position },
      attribution: { ...record.attribution },
    };
  }

  /**
   * Snapshot of all live anchors, sorted by start offset ascending.
   * Each entry is a defensive copy.
   */
  all(): AnchorRecord[] {
    return Array.from(this._anchors.values())
      .map((r) => ({
        id: r.id,
        position: { ...r.position },
        attribution: { ...r.attribution },
      }))
      .sort((a, b) => a.position.start - b.position.start);
  }

  /** Number of currently registered (live) anchors. */
  get size(): number {
    return this._anchors.size;
  }

  // ── Observers ───────────────────────────────────────────────────────────────

  /**
   * Subscribe to anchor changes.
   * `observer` is called synchronously after every mutation with a read-only
   * snapshot of the current registry state.
   * Returns an unsubscribe function.
   */
  subscribe(observer: AnchorObserver): () => void {
    this._observers.add(observer);
    return () => this._observers.delete(observer);
  }

  // ── Lifecycle ───────────────────────────────────────────────────────────────

  /**
   * Remove all anchors and all observers.
   * Call when the document is unloaded to prevent memory leaks.
   */
  dispose(): void {
    this._anchors.clear();
    this._observers.clear();
  }

  // ── Private ─────────────────────────────────────────────────────────────────

  private _notify(): void {
    if (this._observers.size === 0) return;
    const snapshot: ReadonlyMap<AnchorId, AnchorRecord> = new Map(
      Array.from(this._anchors.entries()).map(([k, v]) => [
        k,
        { id: v.id, position: { ...v.position }, attribution: { ...v.attribution } },
      ])
    );
    for (const observer of this._observers) {
      observer(snapshot);
    }
  }
}
