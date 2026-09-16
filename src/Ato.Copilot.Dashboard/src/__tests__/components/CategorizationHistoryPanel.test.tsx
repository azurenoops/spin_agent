import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('../../api/systemDetail', () => ({
  getCategorizationHistory: vi.fn(),
}));

import * as systemDetailApi from '../../api/systemDetail';
import CategorizationHistoryPanel from '../../components/cards/CategorizationHistoryPanel';

const mockGetHistory = systemDetailApi.getCategorizationHistory as ReturnType<typeof vi.fn>;

describe('CategorizationHistoryPanel', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders immutable versions with only the latest marked current', async () => {
    // Arrange
    mockGetHistory.mockResolvedValue([
      {
        id: 'version-2', version: 2, isCurrent: true, changedBy: 'issm@example.mil',
        changedAt: '2026-03-20T12:00:00Z', justification: 'Mission expanded',
        previousOverallImpact: 'Low', newOverallImpact: 'Moderate',
        newConfidentialityImpact: 'Moderate', newIntegrityImpact: 'Moderate',
        newAvailabilityImpact: 'Low', newInformationTypes: [],
      },
      {
        id: 'version-1', version: 1, isCurrent: false, changedBy: 'isso@example.mil',
        changedAt: '2026-03-19T12:00:00Z', justification: 'Initial decision',
        previousOverallImpact: null, newOverallImpact: 'Low',
        newConfidentialityImpact: 'Low', newIntegrityImpact: 'Low',
        newAvailabilityImpact: 'Low', newInformationTypes: [],
      },
    ]);

    // Act
    render(<CategorizationHistoryPanel systemId="system-1" />);

    // Assert
    await waitFor(() => expect(screen.getByText('Version 2')).toBeInTheDocument());
    expect(screen.getByText('Version 1')).toBeInTheDocument();
    expect(screen.getByText('Mission expanded')).toBeInTheDocument();
    expect(screen.getByText('Initial decision')).toBeInTheDocument();
    expect(screen.getAllByText('Current')).toHaveLength(1);
    expect(mockGetHistory).toHaveBeenCalledWith('system-1');
  });

  it('renders an unavailable state when history cannot be loaded', async () => {
    // Arrange
    mockGetHistory.mockRejectedValue(new Error('network unavailable'));

    // Act
    render(<CategorizationHistoryPanel systemId="system-1" />);

    // Assert
    await waitFor(() => {
      expect(screen.getByText('Categorization history is unavailable.')).toBeInTheDocument();
    });
    expect(screen.queryByText('No categorization decisions recorded.')).not.toBeInTheDocument();
  });
});