import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import LegalRegulatory from '../../pages/LegalRegulatory';
import * as api from '../../api/components';

const permission = vi.hoisted(() => ({ canManage: true }));
vi.mock('../../components/permissions/useSystemMutationPermission', () => ({ useSystemMutationPermission: () => permission.canManage }));
vi.mock('../../api/components', () => ({
  getComponents: vi.fn(), listComponents: vi.fn(), createOrgComponent: vi.fn(), assignToSystem: vi.fn(), removeAssignment: vi.fn(),
}));
const policy = { id: 'policy-a', name: 'Access management policy', componentType: 'Policy', status: 'Active', createdAt: '2026-09-01', capabilityLinks: [], systemAssignments: [] };
function mount() { render(<MemoryRouter initialEntries={['/systems/a/legal']}><Routes><Route path="/systems/:id/legal" element={<LegalRegulatory />} /></Routes></MemoryRouter>); }

describe('System policy task', () => {
  beforeEach(() => {
    vi.clearAllMocks(); permission.canManage = true;
    vi.mocked(api.getComponents).mockResolvedValue({ systemId: 'a', items: [], totalCount: 0, nextCursor: null,
      summary: { totalCount: 0, thingCount: 0, personCount: 0, placeCount: 0, policyCount: 0 } });
    vi.mocked(api.listComponents).mockResolvedValue({ items: [policy], totalCount: 1, page: 1, pageSize: 200 } as Awaited<ReturnType<typeof api.listComponents>>);
  });
  it('adds an existing policy reference to the exact system using the existing assignment API', async () => {
    // Arrange
    vi.mocked(api.assignToSystem).mockResolvedValue({} as Awaited<ReturnType<typeof api.assignToSystem>>);
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Add policy reference' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Assign' }));
    // Assert
    await waitFor(() => expect(api.assignToSystem).toHaveBeenCalledWith('policy-a', { registeredSystemId: 'a' }));
    expect(screen.getByRole('heading', { name: 'Applicable policies & references' })).toBeVisible();
    expect(api.createOrgComponent).not.toHaveBeenCalled();
  });
  it('surfaces a rejected assignment without claiming success', async () => {
    // Arrange
    vi.mocked(api.assignToSystem).mockRejectedValue(new Error('Policy assignment denied'));
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: 'Add policy reference' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Assign' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Policy assignment denied');
    expect(screen.queryByText('Assigned ✓')).not.toBeInTheDocument();
  });
  it('does not expose write actions to a read-only system user', async () => {
    // Arrange
    permission.canManage = false;
    mount();
    // Act / Assert
    expect(await screen.findByRole('button', { name: 'Add policy reference' })).toBeDisabled();
    expect(api.assignToSystem).not.toHaveBeenCalled();
  });
});
