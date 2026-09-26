import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SystemComponentPlacements from '../../features/workspace-operations/system-capabilities/SystemComponentPlacements';
import * as api from '../../features/workspace-operations/system-capabilities/systemCapabilityApi';

vi.mock('../../features/workspace-operations/system-capabilities/systemCapabilityApi', () => ({
  getSystemComponentPlacements: vi.fn(), assignSystemComponentBoundary: vi.fn(), unassignSystemComponentBoundary: vi.fn(),
}));
const options = {
  source: 'provider' as const, recordId: 'component-a', sourceRevision: 'source-r1', relationshipRevision: 'relationship-r1',
  canAssignBoundary: true, assignBlockedReason: null,
  boundaries: [{ id: 'boundary-a', name: 'Operations' }, { id: 'boundary-b', name: 'Workloads' }],
  placements: [{ id: 'placement-a', boundaryId: 'boundary-a', boundaryName: 'Operations', state: 'InScope' as const,
    revision: 'placement-r1', canUnassign: true, unassignBlockedReason: null }],
};
const onBusy = vi.fn();
const onChanged = vi.fn();
function mount() {
  return render(<SystemComponentPlacements tenantId="org-a" systemId="system-a" source="provider" componentId="component-a"
    onBusyChange={onBusy} onChanged={onChanged} />);
}
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getSystemComponentPlacements).mockResolvedValue(options);
  vi.mocked(api.assignSystemComponentBoundary).mockResolvedValue({ source: 'provider', recordId: 'component-a',
    placementId: 'placement-b', boundaryId: 'boundary-b', action: 'Assigned', relationshipRevision: 'relationship-r2' });
  vi.mocked(api.unassignSystemComponentBoundary).mockResolvedValue({ source: 'provider', recordId: 'component-a',
    placementId: 'placement-a', boundaryId: 'boundary-a', action: 'Unassigned', relationshipRevision: 'relationship-r2' });
});

describe('selected-system component placement', () => {
  it('assigns an authorized boundary without editing provider source ownership', async () => {
    // Arrange
    mount();
    await screen.findByRole('combobox', { name: 'Boundary for this component' });
    // Act
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'boundary-b' } });
    fireEvent.click(screen.getByRole('button', { name: 'Assign to boundary' }));
    // Assert
    await waitFor(() => expect(onChanged).toHaveBeenCalledWith('Assigned to Workloads.'));
    expect(api.assignSystemComponentBoundary).toHaveBeenCalledWith('org-a', 'system-a',
      { source: 'provider', recordType: 'component', recordId: 'component-a' },
      { boundaryId: 'boundary-b', sourceRevision: 'source-r1', relationshipRevision: 'relationship-r1' }, expect.any(AbortSignal));
    expect(onBusy).toHaveBeenCalledWith(true);
    expect(onBusy).toHaveBeenLastCalledWith(false);
  });

  it('requires explicit acknowledgement before unassigning the exact placement', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Remove from Operations' }));
    // Assert
    expect(screen.getByRole('button', { name: 'Confirm placement removal' })).toBeDisabled();
    // Act
    fireEvent.click(screen.getByRole('checkbox', { name: /only this boundary placement/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm placement removal' }));
    // Assert
    await waitFor(() => expect(api.unassignSystemComponentBoundary).toHaveBeenCalledWith('org-a', 'system-a',
      { source: 'provider', recordType: 'component', recordId: 'component-a' }, 'placement-a',
      { sourceRevision: 'source-r1', relationshipRevision: 'relationship-r1', placementRevision: 'placement-r1' }, expect.any(AbortSignal)));
    expect(onChanged).toHaveBeenCalledWith('Removed from Operations. Other placements are unchanged.');
  });

  it('blocks stale or uncertain writes until current placements are explicitly refreshed', async () => {
    // Arrange
    vi.mocked(api.assignSystemComponentBoundary).mockRejectedValue(new Error('The reviewed relationships changed.'));
    mount();
    await screen.findByRole('combobox');
    // Act
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'boundary-b' } });
    fireEvent.click(screen.getByRole('button', { name: 'Assign to boundary' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('reviewed relationships changed');
    expect(screen.getByRole('button', { name: 'Assign to boundary' })).toBeDisabled();
    expect(api.assignSystemComponentBoundary).toHaveBeenCalledTimes(1);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Refresh placements' }));
    // Assert
    await waitFor(() => expect(api.getSystemComponentPlacements).toHaveBeenCalledTimes(2));
    expect(onChanged).not.toHaveBeenCalled();
  });

  it('shows explicit permission and legacy-assignment reasons without enabling writes', async () => {
    // Arrange
    vi.mocked(api.getSystemComponentPlacements).mockResolvedValue({ ...options, canAssignBoundary: false,
      assignBlockedReason: 'System-management permission is required.', placements: options.placements.map(placement => ({ ...placement,
        canUnassign: false, unassignBlockedReason: 'Manage this legacy placement in inventory.' })) });
    // Act
    mount();
    // Assert
    expect(await screen.findByText('System-management permission is required.')).toBeVisible();
    expect(screen.getByText('Manage this legacy placement in inventory.')).toBeVisible();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove from Operations' })).not.toBeInTheDocument();
    expect(api.assignSystemComponentBoundary).not.toHaveBeenCalled();
  });
});
