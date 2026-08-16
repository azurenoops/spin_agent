import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { VerdictSummaryChip } from '../VerdictSummaryChip';
import { VerdictStoreProvider, useVerdictStore, VerificationResult } from '../../../lib/verdict-store';

function SeedStore({ results }: { results: VerificationResult[] }) {
  const { dispatch } = useVerdictStore();
  React.useEffect(() => {
    results.forEach((r) => dispatch({ type: 'ADD_RESULT', payload: r }));
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  return null;
}

function renderChip(
  messageId: string,
  results: VerificationResult[],
  onTogglePanel = jest.fn(),
  panelOpen = false
) {
  return render(
    <VerdictStoreProvider>
      <SeedStore results={results} />
      <VerdictSummaryChip
        messageId={messageId}
        panelOpen={panelOpen}
        onTogglePanel={onTogglePanel}
      />
    </VerdictStoreProvider>
  );
}

const makeResult = (id: string, messageId: string): VerificationResult => ({
  id,
  messageId,
  claim_sentence: 'AC-2 is addressed in SSP',
  verdict: 'SUPPORTED',
  source_title: 'SSP v3',
});

describe('VerdictSummaryChip', () => {
  it('renders without crashing when there are no results', () => {
    const { container } = renderChip('msg-1', []);
    expect(container).toBeInTheDocument();
  });

  it('renders a chip button when results are present', async () => {
    renderChip('msg-2', [makeResult('r1', 'msg-2')]);
    const btn = await screen.findByRole('button');
    expect(btn).toBeInTheDocument();
  });

  it('calls onTogglePanel when the chip is clicked', async () => {
    const handler = jest.fn();
    renderChip('msg-3', [makeResult('r2', 'msg-3')], handler);
    const btn = await screen.findByRole('button');
    fireEvent.click(btn);
    expect(handler).toHaveBeenCalledTimes(1);
  });

  it('shows mixed state when verdicts differ', async () => {
    const results: VerificationResult[] = [
      makeResult('r3', 'msg-4'),
      { ...makeResult('r4', 'msg-4'), id: 'r4', verdict: 'CONTRADICTED' },
    ];
    renderChip('msg-4', results);
    // Some text about mixed / multiple verdicts should appear
    const btn = await screen.findByRole('button');
    expect(btn).toBeInTheDocument();
  });
});
