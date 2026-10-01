import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import PageLayout from '../../components/layout/PageLayout';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';

const state = vi.hoisted(() => ({ support: false, provider: false, scoped: true }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => state.scoped ? ({
    target: state.provider ? { kind: 'csp' } : { kind: 'organization', tenantId: 'org-a', mode: state.support ? 'support' : undefined },
    workspace: { displayName: 'Organization A', mode: state.support ? 'support' : 'ordinary',
      permissions: { canManageMemberships: false, canManageOrganization: false, canAccessCsp: false } },
    systemAccess: null,
    roles: ['Reader'],
  }) : null,
}));
vi.mock('@azure/msal-react', () => ({ useMsal: () => ({ accounts: [] }) }));
vi.mock('../../components/layout/useCspBranding', () => ({ useCspBranding: () => ({ displayName: null, logoUrl: null }) }));
vi.mock('../../hooks/useNotifications', () => ({ useNotifications: () => ({ unreadCount: 0 }) }));
vi.mock('../../components/chat/ChatPanelContext', () => ({ useChatPanel: () => ({ panelState: { isOpen: false }, togglePanel: vi.fn() }) }));
vi.mock('../../components/chat/ChatToggle', () => ({ default: () => null }));
vi.mock('../../components/help/HelpPanel', () => ({ default: () => null }));
vi.mock('../../components/settings/SettingsPanel', () => ({ default: () => null }));
vi.mock('../../components/notifications/NotificationPanel', () => ({ default: () => null }));
vi.mock('../../features/tenancy/TenantPicker', () => ({ default: () => null }));
vi.mock('../../features/auth/AccountMenu', () => ({ default: () => null }));

beforeEach(() => { state.support = false; state.provider = false; state.scoped = true; });

describe('explicit setup entry without rebootstrap', () => {
  it.each(['/', '/systems/system-a/documents'])('offers guided setup in the current shell at %s', path => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={[`/workspaces/organizations/org-a${path}`]}><WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
      <PageLayout title="Workspace"><p>Existing content</p></PageLayout>
    </WorkspaceNavigationProvider></MemoryRouter>);
    // Assert
    expect(screen.getByRole('link', { name: 'Guided setup' })).toHaveAttribute('href', '/workspaces/organizations/org-a/setup');
    expect(screen.getByRole('navigation', { name: 'Organization navigation' })).toBeInTheDocument();
    expect(screen.getByText('Existing content')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'What are you setting up?' })).not.toBeInTheDocument();
  });

  it('offers provider-scoped setup without replacing provider navigation', () => {
    // Arrange
    state.provider = true;
    // Act
    render(<MemoryRouter initialEntries={['/workspaces/csp/authorizations']}>
      <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}>
        <PageLayout title="Provider"><p>Provider content</p></PageLayout>
      </WorkspaceNavigationProvider>
    </MemoryRouter>);
    // Assert
    expect(screen.getByRole('link', { name: 'Guided setup' })).toHaveAttribute('href', '/workspaces/csp/setup');
    expect(screen.getByRole('navigation', { name: 'Provider workspace' })).toBeInTheDocument();
  });

  it('does not offer ordinary enrollment through a support session', () => {
    // Arrange
    state.support = true;
    // Act
    render(<MemoryRouter initialEntries={['/workspaces/support/organizations/org-a']}><WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a', mode: 'support' }}>
      <PageLayout title="Support"><p>Support content</p></PageLayout>
    </WorkspaceNavigationProvider></MemoryRouter>);
    // Assert
    expect(screen.queryByRole('link', { name: 'Guided setup' })).not.toBeInTheDocument();
  });

  it('does not infer a guided setup workspace for an unscoped session', () => {
    // Arrange
    state.scoped = false;
    // Act
    render(<MemoryRouter><PageLayout title="Legacy"><p>Legacy content</p></PageLayout></MemoryRouter>);
    // Assert
    expect(screen.queryByRole('link', { name: 'Guided setup' })).not.toBeInTheDocument();
  });
});
