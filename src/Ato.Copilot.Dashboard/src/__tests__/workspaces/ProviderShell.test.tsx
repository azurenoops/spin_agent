import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import PageLayout from '../../components/layout/PageLayout';
import PageHero from '../../components/layout/PageHero';

const state = vi.hoisted(() => ({
  session: {
    target: { kind: 'csp' },
    workspace: { displayName: 'Verified Provider', permissions: { canAccessCsp: true } },
    roles: ['CSP.Admin'], systemAccess: null,
  },
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => state.session }));
vi.mock('@azure/msal-react', () => ({ useMsal: () => ({ accounts: [] }) }));
vi.mock('../../components/layout/useCspBranding', () => ({ useCspBranding: () => ({ displayName: null, logoUrl: null }) }));
vi.mock('../../hooks/useOrganizationContext', () => ({ useOrganizationContext: () => ({ displayName: 'Not the active provider' }) }));
vi.mock('../../hooks/useNotifications', () => ({ useNotifications: () => ({ unreadCount: 0 }) }));
vi.mock('../../components/chat/ChatPanelContext', () => ({ useChatPanel: () => ({ panelState: { isOpen: false }, togglePanel: vi.fn() }) }));
vi.mock('../../components/chat/ChatToggle', () => ({ default: () => null }));
vi.mock('../../components/help/HelpPanel', () => ({ default: () => null }));
vi.mock('../../components/settings/SettingsPanel', () => ({ default: () => null }));
vi.mock('../../components/notifications/NotificationPanel', () => ({ default: () => null }));
vi.mock('../../features/tenancy/TenantPicker', () => ({ default: () => null }));
vi.mock('../../features/auth/AccountMenu', () => ({ default: () => null }));

beforeEach(() => { state.session.target.kind = 'csp'; });

describe('mock-defined workspace shell', () => {
  it('renders the provider sidebar and verified context rather than the old top-level entity navigation', () => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={['/authorizations']}><PageLayout title="Offering"><p>Retained offering content</p></PageLayout></MemoryRouter>);
    // Assert
    expect(screen.getByRole('navigation', { name: 'Provider workspace' })).toBeInTheDocument();
    expect(screen.getByText('Verified Provider')).toBeInTheDocument();
    expect(screen.getByText('Retained offering content')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Authorizations' })).not.toBeInTheDocument();
  });

  it('does not insert the provider sidebar into a system workspace', () => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={['/systems/system-a/documents']}><PageLayout title="Documents"><p>System content</p></PageLayout></MemoryRouter>);
    // Assert
    expect(screen.queryByRole('navigation', { name: 'Provider workspace' })).not.toBeInTheDocument();
    expect(screen.getByText('System content')).toBeInTheDocument();
  });

  it('uses the plain mock header for provider pages without displaying an organization fallback', () => {
    // Arrange / Act
    const { container } = render(<PageHero title="Service offerings" eyebrow="Provider operations" description="Source-backed services" />);
    // Assert
    expect(screen.getByRole('heading', { name: 'Service offerings' })).toBeInTheDocument();
    expect(screen.queryByText('Not the active provider')).not.toBeInTheDocument();
    expect(container.firstElementChild).not.toHaveClass('bg-gradient-to-r');
  });
});
