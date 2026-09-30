import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../helpers/dialog';
import WorkspaceSwitchDialog from '../../features/workspaces/WorkspaceSwitchDialog';
import { getWorkspaceOptions } from '../../features/workspaces/api';

vi.mock('../../features/workspaces/api', () => ({
  getWorkspaceOptions: vi.fn(), workspaceErrorMessage: (error: Error) => error.message,
}));
const close = vi.fn();
const option = { kind: 'organization' as const, tenantId: 'org-b', displayName: 'Organization B', status: 'Active', onboardingState: 'Active' };
function Location() { const route = useLocation(); return <output aria-label="Current URL">{route.pathname}{route.search}{route.hash}</output>; }
function mount() {
  return render(<MemoryRouter initialEntries={['/workspaces/organizations/org-a/systems/system-a/profile/MissionAndPurpose?review=1#mission']}>
    <input aria-label="Unsaved mission" defaultValue="Unsaved work stays here" />
    <Location />
    <WorkspaceSwitchDialog currentWorkspace={{ kind: 'organization', tenantId: 'org-a' }} currentName="Organization A" onClose={close} />
  </MemoryRouter>);
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(getWorkspaceOptions).mockResolvedValue({ items: [option], total: 1 }); });
describe('Workspace switch without leaving the current form', () => {
  it('keeps the URL and draft on cancel without selecting a workspace', async () => {
    // Arrange
    mount(); await screen.findByRole('button', { name: /Organization B/ });
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // Assert
    expect(close).toHaveBeenCalledOnce();
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('/workspaces/organizations/org-a/systems/system-a/profile/MissionAndPurpose?review=1#mission');
    expect(screen.getByLabelText('Unsaved mission')).toHaveValue('Unsaved work stays here');
  });
  it('requires confirmation and switches to the target root without carrying another system ID', async () => {
    // Arrange
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: /Organization B/ }));
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('/systems/system-a/');
    fireEvent.click(screen.getByRole('button', { name: 'Switch to Organization B' }));
    // Assert
    expect(screen.getByLabelText('Current URL')).toHaveTextContent(/^\/workspaces\/organizations\/org-b$/);
  });
  it('stays on the current deep link when selecting the current ordinary workspace', async () => {
    // Arrange
    vi.mocked(getWorkspaceOptions).mockResolvedValue({ items: [{ ...option, tenantId: 'org-a', displayName: 'Organization A' }], total: 1 });
    mount();
    // Act
    fireEvent.click(await screen.findByRole('button', { name: /Organization A/ }));
    // Assert
    expect(close).toHaveBeenCalledOnce();
    expect(screen.getByLabelText('Current URL')).toHaveTextContent('/systems/system-a/');
  });
  it('allows cancellation after an unavailable choices request', async () => {
    // Arrange
    vi.mocked(getWorkspaceOptions).mockRejectedValue(new Error('Workspace choices unavailable'));
    mount();
    // Act
    await screen.findByRole('alert');
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    // Assert
    expect(close).toHaveBeenCalledOnce();
    expect(screen.queryByRole('button', { name: /Switch to/ })).not.toBeInTheDocument();
  });
  it('aborts an in-flight choice request when the dialog closes', () => {
    // Arrange
    vi.mocked(getWorkspaceOptions).mockReturnValue(new Promise(() => {}));
    const { unmount } = mount();
    const signal = vi.mocked(getWorkspaceOptions).mock.calls[0]?.[1];
    // Act
    unmount();
    // Assert
    expect(signal?.aborted).toBe(true);
  });
});
