import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ProviderAdministration } from '../../features/provider-authorizations/ProviderAdministration';
import * as directory from '../../features/workspace-operations/api';
import * as memberships from '../../features/workspaces/api';
import * as admin from '../../features/provider-authorizations/providerAdministrationApi';
import '../package-imports/crypto';

vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => ({
  target: { kind: 'csp' }, workspace: { roles: ['CSP.Admin'], permissions: { canAccessCsp: true } },
}) }));
vi.mock('../../features/workspace-operations/api', () => ({
  getDirectoryConnections: vi.fn(), searchDirectoryUsers: vi.fn(), listOrganizations: vi.fn(),
}));
vi.mock('../../features/workspaces/api', () => ({ getMembershipPersons: vi.fn(), getWorkspaceMemberships: vi.fn() }));
vi.mock('../../features/provider-authorizations/providerAdministrationApi', () => ({ enrollOrganizationAdministrator: vi.fn() }));
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(directory.getDirectoryConnections).mockResolvedValue([]);
  vi.mocked(directory.listOrganizations).mockResolvedValue({
    items: [{ id: 'tenant-1', displayName: 'Synthetic organization', lifecycle: 'Active', onboarding: 'Complete', reviewState: 'Current', systemCount: 0, distinctAdoptionCount: 0 }],
    page: 1, pageSize: 25, total: 1,
  });
  vi.mocked(memberships.getMembershipPersons).mockResolvedValue([{ id: 'person-1', displayName: 'Synthetic administrator', email: 'synthetic@example.test' }]);
  vi.mocked(memberships.getWorkspaceMemberships).mockResolvedValue({ items: [{
    id: 'member-1', tenantId: 'tenant-1', personId: 'person-1', objectId: 'object-1', directoryTenantId: 'directory-1',
    grantedAt: '2026-09-26T00:00:00Z', grantedBy: 'test', revokedAt: null, revokedBy: null,
  }], total: 1 });
});
it('enrolls only an explicitly selected active member using the existing administrator endpoint', async () => {
  // Arrange
  vi.mocked(admin.enrollOrganizationAdministrator).mockResolvedValue({ id: 'role-1', tenantId: 'tenant-1', personId: 'person-1', role: 'Administrator' });
  render(<ProviderAdministration />);
  await screen.findByRole('option', { name: 'Synthetic organization' });
  // Act
  fireEvent.change(screen.getByLabelText('Organization for access administration'), { target: { value: 'tenant-1' } });
  await screen.findByRole('option', { name: 'Synthetic administrator' });
  fireEvent.change(screen.getByLabelText('Existing active member'), { target: { value: 'person-1' } });
  fireEvent.click(screen.getByLabelText('I explicitly grant the organization Administrator role to this active member.'));
  fireEvent.click(screen.getByRole('button', { name: 'Enroll initial Administrator' }));
  // Assert
  await waitFor(() => expect(admin.enrollOrganizationAdministrator).toHaveBeenCalledWith('tenant-1', 'person-1'));
  expect(await screen.findByText('Administrator role recorded: role-1')).toBeInTheDocument();
  expect(screen.getByText(/No Entra directory is connected/)).toBeInTheDocument();
});
it('does not offer revoked memberships for a role grant', async () => {
  // Arrange
  vi.mocked(memberships.getWorkspaceMemberships).mockResolvedValue({ items: [], total: 0 });
  render(<ProviderAdministration />);
  await screen.findByRole('option', { name: 'Synthetic organization' });
  // Act
  fireEvent.change(screen.getByLabelText('Organization for access administration'), { target: { value: 'tenant-1' } });
  // Assert
  expect(await screen.findByText('No active members on this page. Grant explicit membership through organization administration first.')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Enroll initial Administrator' })).toBeDisabled();
  expect(admin.enrollOrganizationAdministrator).not.toHaveBeenCalled();
});
