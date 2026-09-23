import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SettingsPanel from '../../components/settings/SettingsPanel';
import { DEFAULT_SETTINGS, SettingsContext } from '../../hooks/useSettings';

const context = vi.hoisted(() => ({
  current: {
    identity: { displayName: 'Verified Mission Owner', isCspAdmin: false },
    roles: ['MissionOwner', 'Sca'],
    workspace: {
      kind: 'organization', displayName: 'Organization Alpha',
      permissions: { canManageOrganization: false, canManageMemberships: false, canAccessCsp: false },
    },
  },
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => context.current }));
vi.mock('../../components/layout/useCspDashboardAvailable', () => ({ useCspDashboardAvailable: () => true }));
vi.mock('../../hooks/useImpersonationActive', () => ({ useImpersonationActive: () => false }));

function mount(updateSettings = vi.fn()) {
  return render(
    <MemoryRouter>
      <SettingsContext.Provider value={{
        settings: { ...DEFAULT_SETTINGS, displayName: 'Browser alias', role: 'AO', organization: 'Wrong organization' },
        updateSettings, resetSettings: vi.fn(),
      }}>
        <SettingsPanel onClose={vi.fn()} />
      </SettingsContext.Provider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  context.current.workspace.permissions.canManageOrganization = false;
  context.current.workspace.kind = 'organization';
  context.current.identity.isCspAdmin = false;
});

describe('workspace settings authority', () => {
  it('offers light, dark and system themes without requiring organization administration', () => {
    // Arrange
    const update = vi.fn();
    mount(update);
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Dashboard' }));
    const theme = screen.getByRole('combobox', { name: 'Theme' });
    fireEvent.change(theme, { target: { value: 'dark' } });
    // Assert
    expect(screen.getByRole('option', { name: 'Light' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Dark' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'System' })).toBeInTheDocument();
    expect(update).toHaveBeenCalledWith({ theme: 'dark' });
  });

  it('shows verified identity and all roles without an editable browser persona', () => {
    // Arrange
    const name = 'Verified Mission Owner';

    // Act
    mount();

    // Assert
    expect(screen.getByText(name)).toBeInTheDocument();
    expect(screen.getByText('Organization Alpha')).toBeInTheDocument();
    expect(screen.getByText(/MissionOwner.*Sca/)).toBeInTheDocument();
    expect(screen.queryByRole('combobox', { name: 'Role' })).not.toBeInTheDocument();
    expect(screen.queryByDisplayValue('Browser alias')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Administration' })).not.toBeInTheDocument();
  });

  it('uses organization permissions and scope rather than a cached CSP probe for administration', () => {
    // Arrange
    context.current.workspace.permissions.canManageOrganization = true;
    mount();

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Administration' }));

    // Assert
    expect(screen.getByRole('button', { name: 'Open onboarding wizard' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Open CSP onboarding wizard' })).not.toBeInTheDocument();
  });
});
