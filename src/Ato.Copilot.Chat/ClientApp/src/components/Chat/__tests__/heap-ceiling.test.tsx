// =============================================================================
// heap-ceiling.test.tsx — Issue #2820 / Wave 14 CI gate
//
// Verifies that rendering TraceabilityPanel across 200 simulated message IDs
// does not leak heap memory beyond HEAP_CEILING_DELTA_MB (50 MB).
//
// Skip rule: if the V8 GC is not exposed (NODE_OPTIONS=--expose-gc not set),
// the test is skipped with an informational message rather than failing.
// In CI (ci.yml chat-client-tests job), NODE_OPTIONS is set to --expose-gc
// so the test always runs.
//
// Measurement approach:
//   1. Force GC before the test loop.
//   2. Record baseline heap (process.memoryUsage().heapUsed).
//   3. Mount + unmount TraceabilityPanel 200 times across unique messageIds.
//   4. Force GC again after the loop.
//   5. Assert (heapAfter - heapBefore) <= HEAP_CEILING_DELTA_MB * 1024 * 1024.
//
// This is a deterministic Jest test — no browser / Playwright required.
// It runs in the same CI job as freeze-on-stream.test.tsx.
// =============================================================================

import React from 'react';
import { render, act } from '@testing-library/react';
import { TraceabilityPanel } from '../TraceabilityPanel';
import { VerdictStoreProvider } from '../../../lib/verdict-store';
import { AnchorRegistry } from '../../../lib/anchorRegistry';

// Feature flag must be on to exercise the full render path.
process.env.REACT_APP_FEATURE_TRACEABILITY_PANEL = 'true';
process.env.REACT_APP_FEATURE_ANCHOR_REGISTRY = 'true';

// ─── Constants ────────────────────────────────────────────────────────────────

const HEAP_CEILING_DELTA_MB = 50;
const RENDER_CYCLES = 200;

// ─── Helpers ──────────────────────────────────────────────────────────────────

/** Returns true only if V8's GC is available (--expose-gc flag set). */
function gcAvailable(): boolean {
  return typeof (global as any).gc === 'function';
}

/** Force V8 GC if available. No-op otherwise. */
function forceGC(): void {
  if (gcAvailable()) {
    (global as any).gc();
  }
}

function heapUsedBytes(): number {
  return process.memoryUsage().heapUsed;
}

// ─── Tests ────────────────────────────────────────────────────────────────────

describe('#2820 heap-ceiling — TraceabilityPanel 200-render stress', () => {
  it(
    `heap delta after ${RENDER_CYCLES} mount/unmount cycles must be ≤ ${HEAP_CEILING_DELTA_MB} MB`,
    () => {
      if (!gcAvailable()) {
        // Skip rather than fail: GC not exposed in this environment.
        // In CI, NODE_OPTIONS=--expose-gc ensures this never skips.
        console.warn(
          '[heap-ceiling] Skipping: global.gc() not available. ' +
            'Run with NODE_OPTIONS=--expose-gc to enforce the ceiling.'
        );
        return;
      }

      // ── Baseline ──────────────────────────────────────────────────────────
      forceGC();
      const heapBefore = heapUsedBytes();

      // ── Stress loop ───────────────────────────────────────────────────────
      for (let i = 0; i < RENDER_CYCLES; i++) {
        const messageId = `heap-test-msg-${i}`;

        let unmount!: () => void;

        act(() => {
          const result = render(
            <VerdictStoreProvider>
              <TraceabilityPanel
                messageId={messageId}
                open={true}
                loading={false}
                onClose={jest.fn()}
                onViewSource={jest.fn()}
              />
            </VerdictStoreProvider>
          );
          unmount = result.unmount;
        });

        act(() => {
          unmount();
        });
      }

      // ── Post-loop GC + measurement ─────────────────────────────────────────
      forceGC();
      const heapAfter = heapUsedBytes();

      const deltaMB = (heapAfter - heapBefore) / (1024 * 1024);

      // Surface the number in every run (pass or fail) so trends are visible
      // in CI logs even when the test passes.
      console.info(
        `[heap-ceiling] heapBefore=${(heapBefore / 1024 / 1024).toFixed(1)} MB  ` +
          `heapAfter=${(heapAfter / 1024 / 1024).toFixed(1)} MB  ` +
          `delta=${deltaMB.toFixed(1)} MB  ` +
          `ceiling=${HEAP_CEILING_DELTA_MB} MB`
      );

      expect(deltaMB).toBeLessThanOrEqual(HEAP_CEILING_DELTA_MB);
    }
  );
});

// ── AnchorRegistry anchor-drift stress gate ───────────────────────────────────
//
// Verifies that 200 insert/delete remap cycles on an AnchorRegistry leave
// zero orphaned anchors (start >= end) and do not leak significant heap.
// This enforces the CI drift gate requirement from work item 9e3ff674970d4e4a.
//
// Skip rule: same GC availability check as the TraceabilityPanel test above.

describe('#2683/9e3ff67 anchor-drift stress — 200 remap cycles, 0 orphans', () => {
  it(
    'leaves 0 orphaned anchors and heap delta ≤ 20 MB after 200 insert/delete cycles',
    () => {
      if (!gcAvailable()) {
        console.warn(
          '[anchor-drift] Skipping: global.gc() not available. ' +
            'Run with NODE_OPTIONS=--expose-gc to enforce the ceiling.'
        );
        return;
      }

      forceGC();
      const heapBefore = heapUsedBytes();

      const registry = new AnchorRegistry();
      const ATTR = { source_id: 'stress-doc', user_modified: false };
      const ids: string[] = [];

      // Insert 200 non-overlapping 10-char spans at [i*15, i*15+10)
      for (let i = 0; i < 200; i++) {
        ids.push(registry.insert({ start: i * 15, end: i * 15 + 10 }, ATTR));
      }

      // Alternate: remap an insert-then-delete at the document head (200 cycles)
      for (let cycle = 0; cycle < 200; cycle++) {
        registry.remap({ kind: 'insert', at: 0, length: 5 });
        registry.remap({ kind: 'delete', at: 0, length: 5 });
      }

      // Assert zero orphaned anchors (start >= end would indicate a remap bug)
      const orphans = registry.all().filter((a) => a.position.start >= a.position.end);
      expect(orphans.length).toBe(0);

      // Assert all 200 original IDs are still resolvable with valid positions
      let missingOrInvalid = 0;
      for (const id of ids) {
        const pos = registry.resolve(id);
        if (!pos || pos.start >= pos.end) missingOrInvalid++;
      }
      expect(missingOrInvalid).toBe(0);

      registry.dispose();

      forceGC();
      const heapAfter = heapUsedBytes();
      const deltaMB = (heapAfter - heapBefore) / (1024 * 1024);

      console.info(
        `[anchor-drift] heapBefore=${(heapBefore / 1024 / 1024).toFixed(1)} MB  ` +
          `heapAfter=${(heapAfter / 1024 / 1024).toFixed(1)} MB  ` +
          `delta=${deltaMB.toFixed(1)} MB  ceiling=20 MB`
      );

      expect(deltaMB).toBeLessThanOrEqual(20);
    }
  );
});
