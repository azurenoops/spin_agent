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

// Feature flag must be on to exercise the full render path.
process.env.REACT_APP_FEATURE_TRACEABILITY_PANEL = 'true';

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
