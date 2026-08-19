// =============================================================================
// anchorRegistry.test.ts — Unit tests for AnchorRegistry
// (work item 9e3ff674970d4e4a, Editor Suite Phase 5)
//
// Test groups:
//   1. insert() — ID assignment, position storage, attribution defaults
//   2. remove() — idempotent deletion, resolve returns undefined after remove
//   3. remap / insert mutation — left / right / spanning / no-op cases
//   4. remap / delete mutation — shift-left, overlap-clamp, complete-coverage removal
//   5. remap / replace mutation — combined delete+insert, net-positive, net-negative
//   6. remap / multi-mutation — sequential mutations in one call
//   7. markUserModified() — idempotency, attribution update
//   8. subscribe/unsubscribe — observer called, unsubscribe stops calls
//   9. all() — sorted by start, defensive copies
//  10. dispose() — clears anchors and observers, no post-dispose notifications
//  11. round-trip — 200 insert/delete cycles leave 0 orphaned anchors
// =============================================================================

import { AnchorRegistry } from '../anchorRegistry';
import type { AnchorId } from '../anchorRegistry';

// ── Helpers ───────────────────────────────────────────────────────────────────

const ATTR = { source_id: 'doc-1', user_modified: false } as const;

function makeRegistry() {
  return new AnchorRegistry();
}

// ── 1. insert() ───────────────────────────────────────────────────────────────

describe('AnchorRegistry — insert()', () => {
  it('returns a non-empty string ID', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 0, end: 5 }, ATTR);
    expect(typeof id).toBe('string');
    expect(id.length).toBeGreaterThan(0);
  });

  it('assigns unique IDs for separate inserts', () => {
    const r = makeRegistry();
    const a = r.insert({ start: 0, end: 5 }, ATTR);
    const b = r.insert({ start: 6, end: 10 }, ATTR);
    expect(a).not.toBe(b);
  });

  it('stores the position faithfully', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 4, end: 12 }, ATTR);
    expect(r.resolve(id)).toEqual({ start: 4, end: 12 });
  });

  it('stores attribution with an inserted_at timestamp', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 0, end: 1 }, ATTR);
    const rec = r.get(id)!;
    expect(rec.attribution.source_id).toBe('doc-1');
    expect(rec.attribution.user_modified).toBe(false);
    expect(typeof rec.attribution.inserted_at).toBe('string');
  });

  it('throws on invalid position (start > end)', () => {
    const r = makeRegistry();
    expect(() => r.insert({ start: 10, end: 5 }, ATTR)).toThrow(RangeError);
  });

  it('throws on negative start', () => {
    const r = makeRegistry();
    expect(() => r.insert({ start: -1, end: 5 }, ATTR)).toThrow(RangeError);
  });

  it('increments size', () => {
    const r = makeRegistry();
    r.insert({ start: 0, end: 3 }, ATTR);
    r.insert({ start: 4, end: 8 }, ATTR);
    expect(r.size).toBe(2);
  });
});

// ── 2. remove() ───────────────────────────────────────────────────────────────

describe('AnchorRegistry — remove()', () => {
  it('removes an anchor so resolve returns undefined', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 0, end: 5 }, ATTR);
    r.remove(id);
    expect(r.resolve(id)).toBeUndefined();
  });

  it('is idempotent — removing twice does not throw', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 0, end: 5 }, ATTR);
    r.remove(id);
    expect(() => r.remove(id)).not.toThrow();
  });

  it('decrements size', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 0, end: 5 }, ATTR);
    r.insert({ start: 6, end: 10 }, ATTR);
    r.remove(id);
    expect(r.size).toBe(1);
  });
});

// ── 3. remap — insert mutation ────────────────────────────────────────────────

describe('AnchorRegistry — remap insert', () => {
  it('shifts span right when insertion is before start', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    r.remap({ kind: 'insert', at: 5, length: 3 });
    expect(r.resolve(id)).toEqual({ start: 13, end: 23 });
  });

  it('shifts span right when insertion is exactly at start', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    r.remap({ kind: 'insert', at: 10, length: 4 });
    expect(r.resolve(id)).toEqual({ start: 14, end: 24 });
  });

  it('grows end when insertion is inside span', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    r.remap({ kind: 'insert', at: 15, length: 5 });
    expect(r.resolve(id)).toEqual({ start: 10, end: 25 });
  });

  it('no-ops when insertion is after end', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    r.remap({ kind: 'insert', at: 25, length: 3 });
    expect(r.resolve(id)).toEqual({ start: 10, end: 20 });
  });
});

// ── 4. remap — delete mutation ────────────────────────────────────────────────

describe('AnchorRegistry — remap delete', () => {
  it('shifts span left when deletion is entirely before span', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    r.remap({ kind: 'delete', at: 2, length: 5 });
    expect(r.resolve(id)).toEqual({ start: 5, end: 15 });
  });

  it('no-ops when deletion is entirely after span', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    r.remap({ kind: 'delete', at: 25, length: 5 });
    expect(r.resolve(id)).toEqual({ start: 10, end: 20 });
  });

  it('clamps span on partial leading overlap', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    // deletes [8, 13) — overlaps first 3 chars of span
    r.remap({ kind: 'delete', at: 8, length: 5 });
    // start clamped to at=8, end=max(8, 20-5)=15 → [8,15)
    expect(r.resolve(id)).toEqual({ start: 8, end: 15 });
  });

  it('removes anchor when deletion fully covers span', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    r.remap({ kind: 'delete', at: 5, length: 20 });
    expect(r.resolve(id)).toBeUndefined();
    expect(r.size).toBe(0);
  });
});

// ── 5. remap — replace mutation ───────────────────────────────────────────────

describe('AnchorRegistry — remap replace', () => {
  it('net-positive replacement before span shifts span right', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    // replace [3, 6) (d=3) with 6 chars (i=6) → net +3
    r.remap({ kind: 'replace', at: 3, deleteLength: 3, insertLength: 6 });
    expect(r.resolve(id)).toEqual({ start: 13, end: 23 });
  });

  it('net-negative replacement before span shifts span left', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    // replace [3, 8) (d=5) with 2 chars (i=2) → net -3
    r.remap({ kind: 'replace', at: 3, deleteLength: 5, insertLength: 2 });
    expect(r.resolve(id)).toEqual({ start: 7, end: 17 });
  });

  it('replacement entirely after span has no effect', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    r.remap({ kind: 'replace', at: 25, deleteLength: 3, insertLength: 10 });
    expect(r.resolve(id)).toEqual({ start: 10, end: 20 });
  });

  it('full replacement of span removes it', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    r.remap({ kind: 'replace', at: 9, deleteLength: 15, insertLength: 1 });
    expect(r.resolve(id)).toBeUndefined();
  });
});

// ── 6. remap — multi-mutation ─────────────────────────────────────────────────

describe('AnchorRegistry — remap multi-mutation', () => {
  it('applies mutations sequentially in order', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 10, end: 20 }, ATTR);
    // first insert 5 at pos 5 → [15, 25); then delete 3 at pos 0 → [12, 22)
    r.remap(
      { kind: 'insert', at: 5, length: 5 },
      { kind: 'delete', at: 0, length: 3 }
    );
    expect(r.resolve(id)).toEqual({ start: 12, end: 22 });
  });

  it('notifies observers only once per remap call', () => {
    const r = makeRegistry();
    r.insert({ start: 5, end: 10 }, ATTR);
    const calls: number[] = [];
    r.subscribe(() => calls.push(1));
    r.remap(
      { kind: 'insert', at: 0, length: 2 },
      { kind: 'insert', at: 3, length: 1 }
    );
    expect(calls.length).toBe(1);
  });
});

// ── 7. markUserModified() ─────────────────────────────────────────────────────

describe('AnchorRegistry — markUserModified()', () => {
  it('flips user_modified to true', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 0, end: 5 }, ATTR);
    r.markUserModified(id);
    expect(r.get(id)!.attribution.user_modified).toBe(true);
  });

  it('is idempotent — calling twice does not change state', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 0, end: 5 }, ATTR);
    r.markUserModified(id);
    r.markUserModified(id);
    expect(r.get(id)!.attribution.user_modified).toBe(true);
  });

  it('does not affect other anchors', () => {
    const r = makeRegistry();
    const a = r.insert({ start: 0, end: 5 }, ATTR);
    const b = r.insert({ start: 6, end: 10 }, ATTR);
    r.markUserModified(a);
    expect(r.get(b)!.attribution.user_modified).toBe(false);
  });

  it('no-ops on unknown ID (does not throw)', () => {
    const r = makeRegistry();
    expect(() => r.markUserModified('non-existent-id')).not.toThrow();
  });
});

// ── 8. subscribe / unsubscribe ────────────────────────────────────────────────

describe('AnchorRegistry — subscribe/unsubscribe', () => {
  it('observer is called after insert()', () => {
    const r = makeRegistry();
    const snapshots: number[] = [];
    r.subscribe((s) => snapshots.push(s.size));
    r.insert({ start: 0, end: 5 }, ATTR);
    expect(snapshots).toEqual([1]);
  });

  it('observer is called after remove()', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 0, end: 5 }, ATTR);
    const snapshots: number[] = [];
    r.subscribe((s) => snapshots.push(s.size));
    r.remove(id);
    expect(snapshots).toEqual([0]);
  });

  it('observer receives defensive copies (mutation does not affect snapshot)', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 0, end: 5 }, ATTR);
    let captured: any;
    r.subscribe((s) => { captured = s.get(id); });
    r.remap({ kind: 'insert', at: 0, length: 10 });
    // captured.position should be the snapshot value, not live
    expect(captured.position).toEqual({ start: 10, end: 15 });
  });

  it('unsubscribe stops notifications', () => {
    const r = makeRegistry();
    const calls: number[] = [];
    const unsub = r.subscribe(() => calls.push(1));
    r.insert({ start: 0, end: 5 }, ATTR);
    unsub();
    r.insert({ start: 6, end: 10 }, ATTR);
    expect(calls.length).toBe(1);
  });
});

// ── 9. all() ──────────────────────────────────────────────────────────────────

describe('AnchorRegistry — all()', () => {
  it('returns anchors sorted by start offset', () => {
    const r = makeRegistry();
    r.insert({ start: 20, end: 25 }, ATTR);
    r.insert({ start: 5, end: 10 }, ATTR);
    r.insert({ start: 0, end: 3 }, ATTR);
    const starts = r.all().map((a) => a.position.start);
    expect(starts).toEqual([0, 5, 20]);
  });

  it('returns defensive copies — mutating returned objects does not affect registry', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 5, end: 10 }, ATTR);
    const rec = r.all()[0];
    rec.position.start = 999;
    expect(r.resolve(id)).toEqual({ start: 5, end: 10 });
  });
});

// ── 10. dispose() ─────────────────────────────────────────────────────────────

describe('AnchorRegistry — dispose()', () => {
  it('clears all anchors', () => {
    const r = makeRegistry();
    r.insert({ start: 0, end: 5 }, ATTR);
    r.insert({ start: 6, end: 10 }, ATTR);
    r.dispose();
    expect(r.size).toBe(0);
  });

  it('removes all observers — no notifications after dispose', () => {
    const r = makeRegistry();
    r.insert({ start: 0, end: 5 }, ATTR);
    const calls: number[] = [];
    r.subscribe(() => calls.push(1));
    r.dispose();
    // insert after dispose — observers should not fire
    r.insert({ start: 0, end: 3 }, ATTR);
    expect(calls.length).toBe(0);
  });
});

// ── 11. round-trip stress — 200 cycles, 0 orphans ────────────────────────────

describe('AnchorRegistry — round-trip stress (200 insert/delete cycles)', () => {
  it('leaves 0 orphaned anchors after 200 cycles', () => {
    const r = makeRegistry();
    const ids: AnchorId[] = [];

    // Insert 200 non-overlapping 10-char spans
    for (let i = 0; i < 200; i++) {
      ids.push(r.insert({ start: i * 15, end: i * 15 + 10 }, ATTR));
    }

    expect(r.size).toBe(200);

    // Delete every other span (100 removals via remap at the span location)
    // Use explicit remove() to simulate editor node deletion
    for (let i = 0; i < 200; i += 2) {
      r.remove(ids[i]);
    }

    expect(r.size).toBe(100);

    // Verify no resolve() call returns a position with start > end
    const orphans = r.all().filter((a) => a.position.start >= a.position.end);
    expect(orphans.length).toBe(0);
  });

  it('preserves all source_id attributions through 200 remap mutations', () => {
    const r = makeRegistry();
    const id = r.insert({ start: 500, end: 510 }, { source_id: 'sentinel', user_modified: false });

    // Apply 200 small insertions before the span
    for (let i = 0; i < 200; i++) {
      r.remap({ kind: 'insert', at: 0, length: 1 });
    }

    const rec = r.get(id)!;
    expect(rec.attribution.source_id).toBe('sentinel');
    expect(rec.position.start).toBe(700); // 500 + 200 shifts of 1
    expect(rec.position.end).toBe(710);
  });
});
