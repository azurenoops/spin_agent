import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import WorkspacePicker from '../../features/workspaces/WorkspacePicker';
import { getWorkspaceOptions } from '../../features/workspaces/api';

vi.mock('../../features/workspaces/api', () => ({
  getWorkspaceOptions: vi.fn(), workspaceErrorMessage: (error: Error) => error.message,
}));
const option = { kind: 'organization' as const, tenantId: 'org-a', displayName: 'Organization A', status: 'Active', onboardingState: 'Active' };
function Destination() {
  const location = useLocation();
  return <output>{location.pathname}{location.search}{location.hash}</output>;
}
function picker(deepLink = '/') {
  render(<MemoryRouter initialEntries={[{ pathname: '/login/select-tenant', state: { deepLink } }]}>
    <Routes><Route path="/login/select-tenant" element={<WorkspacePicker />} /><Route path="*" element={<Destination />} /></Routes>
  </MemoryRouter>);
}
beforeEach(() => { vi.mocked(getWorkspaceOptions).mockReset(); });
describe('authorized workspace picker', () => {
  it('chooses ordinary membership without reusing support mode and preserves the deep link', async () => {
    // Arrange
    vi.mocked(getWorkspaceOptions).mockResolvedValue({ items: [option], total: 1 });
    picker('/workspaces/support/organizations/other/systems/system-a?review=1#mission');
    // Act
    fireEvent.click(await screen.findByRole('button', { name: /Organization A/ }));
    // Assert
    expect(screen.getByRole('status')).toHaveTextContent('/workspaces/organizations/org-a/systems/system-a?review=1#mission');
    expect(getWorkspaceOptions).toHaveBeenCalledTimes(1);
  });
  it('reads the next page rather than assuming the first page contains all contexts', async () => {
    // Arrange
    vi.mocked(getWorkspaceOptions).mockResolvedValueOnce({ items: [option], total: 51 })
      .mockResolvedValueOnce({ items: [{ ...option, tenantId: 'org-b', displayName: 'Organization B' }], total: 51 });
    picker();
    await screen.findByRole('button', { name: /Organization A/ });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    // Assert
    expect(await screen.findByRole('button', { name: /Organization B/ })).toBeInTheDocument();
    expect(getWorkspaceOptions).toHaveBeenLastCalledWith(2);
  });
  it('fails closed for malformed choice pages', async () => {
    // Arrange
    vi.mocked(getWorkspaceOptions).mockResolvedValue({ items: [option] } as never);
    // Act
    picker();
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('incomplete');
    expect(screen.queryByRole('button', { name: /Organization A/ })).not.toBeInTheDocument();
  });
});
