import { act, fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PackageReceipts } from '../../features/package-imports/PackageReceipts';
import * as packageApi from '../../features/package-imports/api';
import { page, packageStatus } from './fixtures';

vi.mock('../../features/package-imports/api', () => ({ listPackages: vi.fn() }));
beforeEach(() => vi.clearAllMocks());

describe('canonical retained receipt reads', () => {
  it('retries unavailable lookup without presenting false empty receipt state', async () => {
    // Arrange
    vi.mocked(packageApi.listPackages).mockRejectedValueOnce(new Error('Access denied.')).mockResolvedValue(page([packageStatus()]));
    render(<PackageReceipts />);
    // Act
    await screen.findByText('Access denied.');
    expect(screen.queryByText(/No source packages/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    // Assert
    expect(await screen.findByText('Synthetic package')).toBeInTheDocument();
    expect(packageApi.listPackages).toHaveBeenCalledTimes(2);
  });

  it('polls persisted processing status and stops on an explicit lookup failure', async () => {
    // Arrange
    vi.useFakeTimers();
    vi.mocked(packageApi.listPackages).mockResolvedValueOnce(page([packageStatus({ processingState: 'Processing' })]))
      .mockRejectedValueOnce(new Error('Status temporarily unavailable.'));
    const view = render(<PackageReceipts />);
    // Act
    await act(async () => { await Promise.resolve(); });
    await act(async () => { await vi.advanceTimersByTimeAsync(5000); });
    // Assert
    expect(screen.getByText('Status temporarily unavailable.')).toBeInTheDocument();
    expect(packageApi.listPackages).toHaveBeenCalledTimes(2);
    view.unmount();
    vi.useRealTimers();
  });
});
