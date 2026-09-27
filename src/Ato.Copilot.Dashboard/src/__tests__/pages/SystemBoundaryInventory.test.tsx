import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SystemBoundaryInventory from '../../features/systems/SystemBoundaryInventory';
import { listBoundaryComponents } from '../../api/boundaries';
vi.mock('../../api/boundaries', () => ({ listBoundaryComponents: vi.fn() }));
const boundaries = [{ id: 'boundary-a', name: 'Production', isPrimary: true, boundaryType: 'Logical' },
  { id: 'boundary-b', name: 'Recovery', isPrimary: false, boundaryType: 'Physical' }];
describe('Recorded boundary inventory', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listBoundaryComponents).mockResolvedValue({
      items: [{ assignmentId: 'assignment-a', componentId: 'component-a', componentName: 'Mission storage', componentType: 'Thing',
        source: 'System', isInScope: false, exclusionRationale: 'Outside production', inheritanceProvider: null, subType: null,
        azureResourceId: null, azureResourceType: null, azureResourceGroup: null, azureLocation: null,
        createdAt: '2026-09-26', createdBy: 'Reviewer A' }],
      totalCount: 1, page: 1, pageSize: 25,
    });
  });
  it('shows actual included/excluded assignments without acquiring an edit lock', async () => {
    // Arrange / Act
    render(<SystemBoundaryInventory systemId="a" boundaries={boundaries} onReview={vi.fn()} />);
    // Assert
    expect(await screen.findByRole('cell', { name: 'Mission storage' })).toBeVisible();
    expect(screen.getByText('Excluded')).toBeVisible();
    expect(screen.getByText('Outside production')).toBeVisible();
    expect(listBoundaryComponents).toHaveBeenCalledWith('a', 'boundary-a', { page: 1, pageSize: 25 });
  });
  it('reviews and switches only explicit boundaries within this system', async () => {
    // Arrange
    const review = vi.fn();
    render(<SystemBoundaryInventory systemId="a" boundaries={boundaries} onReview={review} />);
    await screen.findByText('Mission storage');
    // Act
    fireEvent.change(screen.getByLabelText('Recorded boundary'), { target: { value: 'boundary-b' } });
    // Assert
    await waitFor(() => expect(listBoundaryComponents).toHaveBeenCalledWith('a', 'boundary-b', { page: 1, pageSize: 25 }));
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Review selected boundary' }));
    // Assert
    expect(review).toHaveBeenCalledWith('boundary-b');
  });
});
