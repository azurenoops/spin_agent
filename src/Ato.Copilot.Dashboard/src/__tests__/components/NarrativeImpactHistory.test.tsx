import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import NarrativeImpactHistory from '../../components/narratives/NarrativeImpactHistory';
import { getImpactReceipts } from '../../api/narrativeLibrary';
vi.mock('../../api/narrativeLibrary', async importOriginal => ({
  ...await importOriginal<typeof import('../../api/narrativeLibrary')>(), getImpactReceipts: vi.fn(),
}));
const receipt = { id: 'receipt-a', impactId: 'event-a', recordedAt: '2026-09-22T00:00:00Z',
  sourceKind: null, sourceId: null, sourceActor: null, sourceContext: {
    sourceRevision: 'delivered-revision', cause: 'ProviderChanged', baselineId: 'baseline-a', subscriptionId: 'subscription-a',
    cspProfileId: null, cspInheritedComponentId: null, cspCapabilityId: null, previousInheritanceType: null, currentInheritanceType: null,
  } };
beforeEach(() => { vi.clearAllMocks(); });
describe('immutable narrative delivery history', () => {
  it('pages real receipts separately from immutable creation context without inventing missing fields', async () => {
    // Arrange
    vi.mocked(getImpactReceipts).mockResolvedValueOnce({ items: [receipt], totalCount: 51, page: 1, pageSize: 50 })
      .mockResolvedValueOnce({ items: [{ ...receipt, id: 'receipt-b', impactId: 'event-b' }], totalCount: 51, page: 2, pageSize: 50 });
    render(<NarrativeImpactHistory systemId="system-a" proposalId="proposal-a" creationTrigger={{ SourceRevision: 'creation-revision' }} />);
    await screen.findByText('event-a');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Next receipt page' }));
    // Assert
    expect(await screen.findByText('event-b')).toBeInTheDocument();
    expect(screen.getByText(/creation-revision/)).toBeInTheDocument();
    expect(screen.getByText(/Delivery recorded at/)).toBeInTheDocument();
    expect(screen.getAllByText('Not recorded').length).toBeGreaterThan(0);
    expect(getImpactReceipts).toHaveBeenLastCalledWith('system-a', 'proposal-a', 2, expect.anything());
    expect(screen.queryByText(/latest provider change/i)).not.toBeInTheDocument();
  });
  it('shows errors rather than success-shaped empty history and allows a read retry', async () => {
    // Arrange
    vi.mocked(getImpactReceipts).mockRejectedValueOnce({ error: 'Receipt access denied.' })
      .mockResolvedValueOnce({ items: [], totalCount: 0, page: 1, pageSize: 50 });
    render(<NarrativeImpactHistory systemId="system-a" proposalId="proposal-a" creationTrigger={null} />);
    expect(await screen.findByRole('alert')).toHaveTextContent('Receipt access denied.');
    expect(screen.queryByText('No delivery receipts recorded.')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Retry delivery history' }));
    // Assert
    await waitFor(() => expect(screen.getByText('No delivery receipts recorded.')).toBeInTheDocument());
  });
});
