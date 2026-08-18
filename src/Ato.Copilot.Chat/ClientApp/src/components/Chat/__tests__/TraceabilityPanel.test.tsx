import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { TraceabilityPanel } from '../TraceabilityPanel';
import { VerdictStoreProvider, useVerdictStore, VerificationResult } from '../../../lib/verdict-store';

function SeedStore({ results }: { results: VerificationResult[] }) {
  const { dispatch } = useVerdictStore();
  React.useEffect(() => {
    results.forEach((r) => dispatch({ type: 'ADD_RESULT', payload: r }));
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  return null;
}

const makeResult = (id: string, messageId: string): VerificationResult => ({
  id,
  messageId,
  claim_sentence: 'AC-2 is addressed',
  verdict: 'SUPPORTED',
  source_title: 'SSP v3',
});

function renderPanel(
  messageId: string,
  results: VerificationResult[],
  open: boolean,
  onClose = jest.fn(),
  onViewSource = jest.fn()
) {
  return render(
    <VerdictStoreProvider>
      <SeedStore results={results} />
      <TraceabilityPanel
        messageId={messageId}
        open={open}
        onClose={onClose}
        onViewSource={onViewSource}
      />
    </VerdictStoreProvider>
  );
}

describe('TraceabilityPanel', () => {
  it('renders nothing when open=false', () => {
    const { container } = renderPanel('msg-1', [], false);
    expect(container).toBeInTheDocument();
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument();
  });

  it('renders panel when open=true', () => {
    renderPanel('msg-1', [makeResult('r1', 'msg-1')], true);
    expect(screen.getByRole('complementary')).toBeInTheDocument();
  });

  it('calls onClose when Escape is pressed', () => {
    const onClose = jest.fn();
    renderPanel('msg-2', [makeResult('r2', 'msg-2')], true, onClose);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('shows the claim text in the panel', async () => {
    renderPanel('msg-3', [makeResult('r3', 'msg-3')], true);
    expect(await screen.findByText(/AC-2 is addressed/)).toBeInTheDocument();
  });

  it('shows source title', async () => {
    renderPanel('msg-4', [makeResult('r4', 'msg-4')], true);
    expect(await screen.findByText(/SSP v3/)).toBeInTheDocument();
  });

  it('shows guided empty state when open=true and no results', () => {
    // GATE-2437 F4: dead-end "No sources traced yet." replaced with guided copy
    // GATE-2437 AC#2: empty state copy updated to "Sources appear here after your first AI response."
    renderPanel('msg-empty', [], true);
    expect(screen.getByTestId('traceability-guided-empty')).toBeInTheDocument();
    expect(
      screen.getByText(/Sources appear here after your first AI response\./i)
    ).toBeInTheDocument();
  });
});
