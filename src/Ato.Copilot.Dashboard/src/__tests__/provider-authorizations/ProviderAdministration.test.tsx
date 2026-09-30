import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ProviderAdministration } from '../../features/provider-authorizations/ProviderAdministration';
import * as directory from '../../features/workspace-operations/api';
import * as memberships from '../../features/workspaces/api';
import * as admin from '../../features/provider-authorizations/providerAdministrationApi';
import { PackageImportError } from '../../features/package-imports/request';
import '../package-imports/crypto';
import '../helpers/dialog';

vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => ({
  identity: { oid: 'signed-in-user', displayName: 'Actual Provider Admin', directoryTenantId: 'provider-directory' },
  target: { kind: 'csp' }, workspace: { roles: ['CSP.Admin'], permissions: { canAccessCsp: true } },
}) }));
vi.mock('../../features/workspace-operations/api', () => ({
  getDirectoryConnections: vi.fn(), searchDirectoryUsers: vi.fn(), listOrganizations: vi.fn(),
}));
vi.mock('../../features/workspaces/api', () => ({ getMembershipPersons: vi.fn(), getWorkspaceMemberships: vi.fn() }));
vi.mock('../../features/provider-authorizations/providerAdministrationApi', () => ({ enrollOrganizationAdministrator: vi.fn() }));
vi.mock('../../features/provider-authorizations/api', async original => ({
  ...await original<typeof import('../../features/provider-authorizations/api')>(),
  listOfferings: vi.fn(async () => ({ items: [], page: 1, pageSize: 25, total: 0 })),
}));
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
it('matches provider team/connections structure and opens real role and directory dialogs without granting access', async () => {
  // Arrange
  render(<MemoryRouter><ProviderAdministration /></MemoryRouter>);
  // Assert
  expect(screen.getByRole('table', { name: 'Provider team' })).toHaveTextContent('Actual Provider Admin');
  expect(screen.queryByRole('region', { name: 'Entra directory search' })).not.toBeInTheDocument();
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'View role' }));
  // Assert
  expect(screen.getByRole('dialog', { name: 'Provider role details' })).toHaveTextContent('CSP.Admin');
  expect(screen.getByRole('dialog')).toHaveTextContent('does not grant');
  fireEvent.click(screen.getByRole('button', { name: 'Close dialog' }));
  // Act
  fireEvent.click(screen.getByRole('button', { name: 'Find user in Entra' }));
  // Assert
  expect(await screen.findByRole('dialog', { name: 'Find user in Microsoft Entra' })).toHaveTextContent('No Entra directory');
  expect(admin.enrollOrganizationAdministrator).not.toHaveBeenCalled();
});

it('enrolls only an explicitly selected active member using the existing administrator endpoint', async () => {
  // Arrange
  vi.mocked(admin.enrollOrganizationAdministrator).mockResolvedValue({ id: 'role-1', tenantId: 'tenant-1', personId: 'person-1', role: 'Administrator' });
  render(<MemoryRouter><ProviderAdministration /></MemoryRouter>);
  fireEvent.click(screen.getByText('Organization role administration', { selector: 'summary' }));
  await screen.findByRole('option', { name: 'Synthetic organization' });
  // Act
  fireEvent.change(screen.getByLabelText('Organization for access administration'), { target: { value: 'tenant-1' } });
  await waitFor(() => expect(screen.getByRole('button', { name: 'Enroll initial Administrator' })).toBeEnabled());
  expect(screen.queryByLabelText('Existing active member')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Enroll initial Administrator' }));
  const dialog = screen.getByRole('dialog', { name: 'Enroll initial Administrator' });
  expect(dialog).toHaveTextContent('Synthetic organization');
  await screen.findByRole('option', { name: 'Synthetic administrator' });
  fireEvent.change(screen.getByLabelText('Existing active member'), { target: { value: 'person-1' } });
  fireEvent.click(screen.getByLabelText('I explicitly grant the organization Administrator role to this active member.'));
  fireEvent.click(within(dialog).getByRole('button', { name: 'Enroll initial Administrator' }));
  // Assert
  await waitFor(() => expect(admin.enrollOrganizationAdministrator).toHaveBeenCalledWith('tenant-1', 'person-1'));
  expect(await screen.findByText('Administrator role recorded: role-1')).toBeInTheDocument();
  expect(screen.getByText('Not configured', { exact: true })).toBeInTheDocument();
});
it('does not offer revoked memberships for a role grant', async () => {
  // Arrange
  vi.mocked(memberships.getWorkspaceMemberships).mockResolvedValue({ items: [], total: 0 });
  render(<MemoryRouter><ProviderAdministration /></MemoryRouter>);
  fireEvent.click(screen.getByText('Organization role administration', { selector: 'summary' }));
  await screen.findByRole('option', { name: 'Synthetic organization' });
  // Act
  fireEvent.change(screen.getByLabelText('Organization for access administration'), { target: { value: 'tenant-1' } });
  // Assert
  expect(await screen.findByText('No active members on this page. Grant explicit membership through organization administration first.')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Enroll initial Administrator' })).toBeDisabled();
  expect(admin.enrollOrganizationAdministrator).not.toHaveBeenCalled();
});
it('prevents dismissal during enrollment and retains a rejected member selection', async () => {
  // Arrange
  let reject!: (reason: Error) => void;
  vi.mocked(admin.enrollOrganizationAdministrator).mockImplementation(() => new Promise((_, fail) => { reject = fail; }));
  render(<MemoryRouter><ProviderAdministration /></MemoryRouter>);
  fireEvent.click(screen.getByText('Organization role administration', { selector: 'summary' }));
  await screen.findByRole('option', { name: 'Synthetic organization' });
  fireEvent.change(screen.getByLabelText('Organization for access administration'), { target: { value: 'tenant-1' } });
  const trigger = screen.getByRole('button', { name: 'Enroll initial Administrator' });
  await waitFor(() => expect(trigger).toBeEnabled());
  trigger.focus();
  fireEvent.click(trigger);
  const dialog = screen.getByRole('dialog');
  fireEvent.change(within(dialog).getByLabelText('Existing active member'), { target: { value: 'person-1' } });
  fireEvent.click(within(dialog).getByLabelText(/I explicitly grant/));
  // Act
  fireEvent.click(within(dialog).getByRole('button', { name: 'Enroll initial Administrator' }));
  fireEvent(dialog, new Event('cancel', { bubbles: true, cancelable: true }));
  // Assert
  expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeDisabled();
  expect(within(dialog).getByRole('button', { name: 'Close dialog' })).toBeDisabled();
  reject(new PackageImportError('Administrator already exists', 409));
  expect(await within(dialog).findByRole('alert')).toHaveTextContent('Administrator already exists');
  expect(within(dialog).getByLabelText('Existing active member')).toHaveValue('person-1');
  await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeEnabled());
  fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(trigger).toHaveFocus();
});
