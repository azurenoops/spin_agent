import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import RolesManagementPage from '../../pages/RolesManagementPage';
import { rolesApi } from '../../api/roles';
vi.mock('../../api/roles', () => ({ rolesApi: { getEffectiveRole: vi.fn() } }));
vi.mock('../../components/layout/SystemLayout', () => ({ useSystemContext: () => ({ detail: { name: 'Mission Alpha' } }) }));
vi.mock('../../components/cards/RoleAssignmentPanel', () => ({
  default: ({ registeredSystemId, callerEffectiveRole }: { registeredSystemId: string; callerEffectiveRole: string | null }) =>
    <section aria-label="Effective role assignments">{registeredSystemId}: {callerEffectiveRole ?? 'Read-only'}</section>,
}));
function mount() { render(<MemoryRouter initialEntries={['/systems/a/roles']}><Routes><Route path="/systems/:id/roles" element={<RolesManagementPage />} /></Routes></MemoryRouter>); }
describe('System team task', () => {
  it('composes effective role assignments with their document contribution', async () => {
    // Arrange
    vi.mocked(rolesApi.getEffectiveRole).mockResolvedValue({ effectiveRole: 'Issm', isTenantAdministrator: false });
    // Act
    mount();
    // Assert
    expect(await screen.findByText('a: Issm')).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Team & permissions' })).toBeVisible();
    expect(screen.getByText('SSP · Responsible personnel / Review routing')).toBeVisible();
  });
  it('keeps assignment context read-only and retryable when the effective role cannot load', async () => {
    // Arrange
    vi.mocked(rolesApi.getEffectiveRole).mockRejectedValueOnce(new Error('Identity service unavailable'));
    vi.mocked(rolesApi.getEffectiveRole).mockResolvedValue({ effectiveRole: 'Issm', isTenantAdministrator: false });
    mount();
    // Act
    expect(await screen.findByRole('alert')).toHaveTextContent('Identity service unavailable');
    expect(screen.getByText('a: Read-only')).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Retry assignment access' }));
    // Assert
    expect(await screen.findByText('a: Issm')).toBeVisible();
  });
});
