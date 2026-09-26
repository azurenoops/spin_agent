import { act, fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { EntraUserPicker } from '../../features/workspace-operations/EntraUserPicker';
import * as api from '../../features/workspace-operations/api';
vi.mock('../../features/workspace-operations/api', async original => ({ ...await original<typeof api>(), getDirectoryConnections: vi.fn(), searchDirectoryUsers: vi.fn() }));
const user = { directoryTenantId: 'tenant', objectId: 'object', displayName: 'Jordan Lee', email: 'jordan@example.mil', userPrincipalName: 'jordan@example.mil' };
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getDirectoryConnections).mockResolvedValue([{ id: 'gov', name: 'Provider directory', cloud: 'Government', directoryTenantId: 'tenant', configured: true }]);
  vi.mocked(api.searchDirectoryUsers).mockResolvedValue({ users: [user], hasMore: false });
});
it('requires explicit selection and fills identity from the directory result', async () => {
  // Arrange
  const selected = vi.fn(); render(<EntraUserPicker onSelect={selected} />);
  await screen.findByRole('option', { name: /Provider directory/ });
  // Act
  fireEvent.change(screen.getByLabelText('Find a person'), { target: { value: 'Jordan' } });
  fireEvent.click(screen.getByRole('button', { name: 'Search Entra' }));
  const choose = await screen.findByRole('button', { name: /Select Jordan Lee/ });
  // Assert
  expect(selected).not.toHaveBeenCalled();
  fireEvent.click(choose); expect(selected).toHaveBeenCalledWith(user);
});
it('shows permission failures without claiming no matches', async () => {
  // Arrange
  vi.mocked(api.searchDirectoryUsers).mockRejectedValue(new Error('Directory permission is required.'));
  render(<EntraUserPicker onSelect={vi.fn()} />); await screen.findByRole('option', { name: /Provider directory/ });
  // Act
  fireEvent.change(screen.getByLabelText('Find a person'), { target: { value: 'Jordan' } });
  fireEvent.click(screen.getByRole('button', { name: 'Search Entra' }));
  // Assert
  expect(await screen.findByRole('alert')).toHaveTextContent('Directory permission is required.');
  expect(screen.queryByText(/No people found/)).not.toBeInTheDocument();
});
it('does not search when no directory is configured', async () => {
  // Arrange
  vi.mocked(api.getDirectoryConnections).mockResolvedValue([]);
  render(<EntraUserPicker onSelect={vi.fn()} />);
  // Act / Assert
  expect(await screen.findByText(/No Entra directory is connected/)).toBeInTheDocument();
  expect(api.searchDirectoryUsers).not.toHaveBeenCalled();
});
it('discards a late response after the search text changes', async () => {
  // Arrange
  let complete!: (value: { users: typeof user[]; hasMore: boolean }) => void;
  vi.mocked(api.searchDirectoryUsers).mockImplementation(() => new Promise(resolve => { complete = resolve; }));
  render(<EntraUserPicker onSelect={vi.fn()} />); await screen.findByRole('option', { name: /Provider directory/ });
  // Act
  fireEvent.change(screen.getByLabelText('Find a person'), { target: { value: 'Jordan' } });
  fireEvent.click(screen.getByRole('button', { name: 'Search Entra' }));
  fireEvent.change(screen.getByLabelText('Find a person'), { target: { value: 'Different' } });
  await act(async () => complete({ users: [user], hasMore: false }));
  // Assert
  expect(screen.queryByRole('button', { name: /Select Jordan Lee/ })).not.toBeInTheDocument();
});
