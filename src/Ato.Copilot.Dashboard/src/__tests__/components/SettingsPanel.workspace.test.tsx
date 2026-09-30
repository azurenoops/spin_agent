import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import SettingsPanel from '../../components/settings/SettingsPanel';
import { DEFAULT_SETTINGS, SettingsContext } from '../../hooks/useSettings';
import '../helpers/dialog';

const context = vi.hoisted(() => ({
  present: true,
  current: {
    identity: { displayName: 'Verified Mission Owner', isCspAdmin: false },
    roles: ['MissionOwner', 'Sca'],
    workspace: {
      kind: 'organization', mode: 'ordinary', displayName: 'Organization Alpha',
      permissions: { canManageOrganization: false, canManageMemberships: false, canAccessCsp: false },
    },
  },
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => context.present ? context.current : null }));
vi.mock('../../components/layout/useCspDashboardAvailable', () => ({ useCspDashboardAvailable: () => true }));
vi.mock('../../hooks/useImpersonationActive', () => ({ useImpersonationActive: () => false }));
vi.mock('../../features/notifications/NotificationSettingsPanel', () => ({ default: () => <p>Account notification form</p> }));

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
  context.present = true;
  context.current.workspace.permissions.canManageOrganization = false;
  context.current.workspace.kind = 'organization';
  context.current.identity.isCspAdmin = false;
  context.current.workspace.mode = 'ordinary';
  context.current.workspace.permissions.canAccessCsp = false;
});

describe('workspace settings authority', () => {
  it('offers light, dark and system themes without requiring organization administration', () => {
    // Arrange
    const update = vi.fn();
    mount(update);
    // Act
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
    // Assert
    expect(screen.getByRole('link', { name: 'Open organization administration' })).toHaveAttribute('href', '/settings/org');
    expect(screen.queryByRole('button', { name: 'Open onboarding wizard' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Open CSP onboarding wizard' })).not.toBeInTheDocument();
  });
  it('uses three expandable sections without policy, integration or export controls', () => {
    // Arrange / Act
    mount();
    // Assert
    expect(screen.getByRole('button', { name: 'Preferences' })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('button', { name: 'Notifications' })).toHaveAttribute('aria-expanded', 'false');
    expect(screen.getByRole('button', { name: 'Assistant' })).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
    expect(screen.queryByText('Profile & Identity')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Organization Framework')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Session Timeout')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Default Export Format')).not.toBeInTheDocument();
    expect(screen.queryByText('Integrations')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reset personal preferences' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Notifications' }));
    expect(screen.getByText('Account notification form')).toBeVisible();
  });
  it('does not expose organization administration in a support workspace', () => {
    // Arrange
    context.current.workspace.permissions.canManageOrganization = true;
    context.current.workspace.mode = 'support';
    // Act
    mount();
    // Assert
    expect(screen.queryByRole('link', { name: 'Open organization administration' })).not.toBeInTheDocument();
  });
  it('links provider administrators to their own operational workspace', () => {
    // Arrange
    context.current.workspace.kind = 'csp';
    context.current.workspace.permissions.canAccessCsp = true;
    context.current.identity.isCspAdmin = true;
    // Act
    mount();
    // Assert
    expect(screen.getByRole('link', { name: 'Open provider administration' })).toHaveAttribute('href', '/provider-administration');
    expect(screen.queryByRole('link', { name: 'Open organization administration' })).not.toBeInTheDocument();
  });
  it('does not infer identity or administration from browser settings when workspace resolution is absent', () => {
    // Arrange
    context.present = false;
    // Act
    mount();
    // Assert
    expect(screen.getByText('Signed-in identity unavailable')).toBeVisible();
    expect(screen.queryByText('Browser alias')).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /administration/ })).not.toBeInTheDocument();
  });
});
