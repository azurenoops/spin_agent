import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { PackageReceiptCard } from '../../features/package-imports/PackageReceipts';
import { packageStatus } from './fixtures';

describe('durable package analysis progress', () => {
  it('shows saved segment progress and bounded automatic continuation without claiming completion', () => {
    // Arrange
    const receipt = {
      ...packageStatus({ processingState: 'Received' }),
      analysisProgress: { completedSegments: 12, totalSegments: 50, modelCalls: 8, modelCallLimit: 64, continuingAutomatically: true },
    };
    // Act
    render(<MemoryRouter><PackageReceiptCard item={receipt} /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('status', { name: 'Analysis progress' })).toHaveTextContent('12 of 50 source segments analyzed');
    expect(screen.getByRole('status', { name: 'Analysis progress' })).toHaveTextContent('8 of 64 model calls');
    expect(screen.getByText(/Continuing automatically in bounded passes/)).toBeInTheDocument();
    expect(screen.queryByText(/analysis complete/i)).not.toBeInTheDocument();
  });

  it('keeps older receipts readable when no saved analysis progress exists', () => {
    // Arrange
    const receipt = packageStatus();
    // Act
    render(<MemoryRouter><PackageReceiptCard item={receipt} /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('article', { name: receipt.name })).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Analysis progress' })).not.toBeInTheDocument();
  });

  it('stops the continuation message and retains explicit errors and exclusion warnings', () => {
    // Arrange
    const receipt = {
      ...packageStatus({ processingState: 'NeedsAttention', lastError: 'Source validation still needs review.',
        coverage: { total: 3, pending: 0, processed: 1, unsupported: 1, unreadable: 0, failed: 0, excluded: 1 } }),
      analysisProgress: { completedSegments: 1, totalSegments: 2, modelCalls: 64, modelCallLimit: 64, continuingAutomatically: false },
    };
    // Act
    render(<MemoryRouter><PackageReceiptCard item={receipt} /></MemoryRouter>);
    // Assert
    expect(screen.queryByText(/Continuing automatically/)).not.toBeInTheDocument();
    expect(screen.getByRole('status', { name: 'Analysis progress' })).toHaveTextContent('1 of 2 source segments analyzed');
    expect(screen.getByRole('alert')).toHaveTextContent('Source validation still needs review.');
    expect(screen.getByText(/Excluded content has not been analyzed/)).toBeInTheDocument();
  });
});
