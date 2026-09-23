import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import SupportWorkspaceButton from '../../features/workspaces/SupportWorkspaceButton';
import { startImpersonation } from '../../features/tenancy/api';

vi.mock('../../features/tenancy/api', () => ({ startImpersonation: vi.fn() }));
function Location() { return <output>{useLocation().pathname}</output>; }
function renderButton() {
  render(<MemoryRouter initialEntries={['/workspaces/csp']}>
    <SupportWorkspaceButton tenantId="org-a" tenantName="Organization A" route="/systems/system-a" />
    <Location />
  </MemoryRouter>);
}
beforeEach(() => { vi.mocked(startImpersonation).mockReset(); });
describe('explicit audited support entry', () => {
  it('requires confirmation before starting the existing audited action', async () => {
    // Arrange
    vi.mocked(startImpersonation).mockResolvedValue({ impersonatedTenantId: 'org-a', expiresAt: '2099-01-01T00:00:00Z' });
    renderButton();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Audited support' }));
    expect(startImpersonation).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: 'Start audited support' }));
    // Assert
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('/workspaces/support/organizations/org-a/systems/system-a'));
    expect(startImpersonation).toHaveBeenCalledWith('org-a', 'Organization A');
  });
  it('does not navigate after a failed support request', async () => {
    // Arrange
    vi.mocked(startImpersonation).mockRejectedValue(new Error('Support denied'));
    renderButton();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Audited support' }));
    fireEvent.click(screen.getByRole('button', { name: 'Start audited support' }));
    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('Support denied');
    expect(screen.getByRole('status')).toHaveTextContent('/workspaces/csp');
  });
});
