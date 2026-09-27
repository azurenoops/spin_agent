import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import SystemEmassConflictReview from '../../features/systems/SystemEmassConflictReview';
import type { EmassConflict } from '../../api/emass-status';

const conflict: EmassConflict = {
  id: 'conflict-1', entityType: 'SystemInfo', entityId: null, fieldName: 'SystemInfo.SystemName',
  spinValue: 'Original', emassValue: 'Returned', conflictStatus: 'Unresolved',
  detectedAt: '2026-09-20T12:00:00Z', resolvedAt: null, resolvedBy: null,
};

describe('eMASS conflict resolution rationale', () => {
  afterEach(() => vi.restoreAllMocks());

  it.each(['AcceptEmass', 'KeepSpin', 'Deferred'] as const)('passes retained rationale for %s without modifying displayed differences', async resolution => {
    // Arrange
    const onResolve = vi.fn().mockResolvedValue(undefined);
    render(<SystemEmassConflictReview conflicts={[conflict]} onResolve={onResolve} />);
    // Act
    fireEvent.change(screen.getByLabelText('Resolution'), { target: { value: resolution } });
    if (resolution === 'AcceptEmass') expect(screen.getByRole('button', { name: 'Record resolution' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Resolution rationale'), { target: { value: 'Reviewed the source and confirmed this decision.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record resolution' }));
    // Assert
    await waitFor(() => expect(onResolve).toHaveBeenCalledWith('conflict-1', resolution, 'Reviewed the source and confirmed this decision.'));
    expect(screen.getByText('Original')).toBeVisible();
    expect(screen.getByText('Returned')).toBeVisible();
  });

  it('requires a separate bulk rationale and stops on the first failed resolution', async () => {
    // Arrange
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const onResolve = vi.fn().mockRejectedValue(new Error('Source changed; compare again.'));
    render(<SystemEmassConflictReview conflicts={[conflict, { ...conflict, id: 'conflict-2' }]} onResolve={onResolve} />);
    fireEvent.click(screen.getByText('Bulk resolution for this page'));
    expect(screen.getByRole('button', { name: 'Accept all eMASS' })).toBeDisabled();
    // Act
    fireEvent.change(screen.getByLabelText('Bulk acceptance rationale'), { target: { value: 'Reviewed every displayed difference.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Accept all eMASS' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Source changed; compare again.');
    expect(onResolve).toHaveBeenCalledExactlyOnceWith('conflict-1', 'AcceptEmass', 'Reviewed every displayed difference.');
    expect(screen.getByLabelText('Bulk acceptance rationale')).toHaveValue('Reviewed every displayed difference.');
  });

  it('retains rationale after failed single decision and resets it when a different conflict is selected', async () => {
    // Arrange
    const onResolve = vi.fn().mockRejectedValue(new Error('Conflict already resolved.'));
    render(<SystemEmassConflictReview conflicts={[conflict, { ...conflict, id: 'conflict-2' }]} onResolve={onResolve} />);
    // Act
    fireEvent.change(screen.getByLabelText('Resolution'), { target: { value: 'AcceptEmass' } });
    fireEvent.change(screen.getByLabelText('Resolution rationale'), { target: { value: 'Reviewed first source.' } });
    fireEvent.click(screen.getByRole('button', { name: 'Record resolution' }));
    // Assert
    await screen.findByText('Conflict already resolved.');
    expect(screen.getByLabelText('Resolution rationale')).toHaveValue('Reviewed first source.');
    // Act
    fireEvent.change(screen.getByLabelText('Conflict to review'), { target: { value: 'conflict-2' } });
    // Assert
    expect(screen.getByLabelText('Resolution rationale')).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Record resolution' })).toBeDisabled();
  });
});
