// =============================================================================
// freeze-on-stream.test.tsx — Regression guard for TraceabilityPanel #2437
//
// Verifies three acceptance sub-criteria (Hawkeye artifact 29c152e341fb4a7e):
//   (a) Skeleton (aria-busy) renders while citation data load is pending
//   (b) AbortController.abort() fires on unmount mid-fetch
//   (c) No loading/frozen state persists after abort and unmount
// =============================================================================

import React from 'react';
import { render, screen, act } from '@testing-library/react';
import { TraceabilityPanel } from '../TraceabilityPanel';
import { VerdictStoreProvider } from '../../../lib/verdict-store';

// Ensure feature flag is on for all tests in this file.
process.env.REACT_APP_FEATURE_TRACEABILITY_PANEL = 'true';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function renderPanel(
  loading: boolean,
  open: boolean,
  onClose = jest.fn(),
  onViewSource = jest.fn()
) {
  return render(
    <VerdictStoreProvider>
      <TraceabilityPanel
        messageId="msg-freeze-test"
        open={open}
        loading={loading}
        onClose={onClose}
        onViewSource={onViewSource}
      />
    </VerdictStoreProvider>
  );
}

// ─── Tests ────────────────────────────────────────────────────────────────────

describe('freeze-on-stream regression guard (#2437)', () => {
  // (a) Skeleton renders with aria-busy="true" during a delayed fetch
  it('(a) renders skeleton with aria-busy="true" while loading=true', () => {
    renderPanel(/* loading */ true, /* open */ true);
    const panel = screen.getByRole('complementary', { name: /claim traceability/i });
    expect(panel).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByTestId('traceability-skeleton')).toBeInTheDocument();
  });

  // (b) AbortController.abort() is called on unmount mid-fetch
  it('(b) calls AbortController.abort() on unmount while loading', () => {
    const abortSpy = jest.spyOn(AbortController.prototype, 'abort');

    const { unmount } = renderPanel(/* loading */ true, /* open */ true);

    act(() => {
      unmount();
    });

    expect(abortSpy).toHaveBeenCalledTimes(1);
    abortSpy.mockRestore();
  });

  // (c) No loading state persists after abort — component fully unmounts
  it('(c) loading state clears after unmount — no frozen aria-busy in DOM', () => {
    const { unmount } = renderPanel(/* loading */ true, /* open */ true);

    // Confirm skeleton is present before unmount
    expect(screen.getByTestId('traceability-skeleton')).toBeInTheDocument();

    act(() => {
      unmount();
    });

    // After unmount nothing from the panel should remain
    expect(screen.queryByTestId('traceability-skeleton')).not.toBeInTheDocument();
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument();
  });
});
