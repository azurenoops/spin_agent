import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import PageLayout from '../../components/layout/PageLayout';
import { SYSTEM_NAV_GROUPS } from '../../components/layout/SystemLayout';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import { buildWorkspaceUrl, type WorkspaceTarget } from '../../features/workspaces/workspaceRoutes';
import type { WorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';

const context = vi.hoisted(() => ({
  session: null as Pick<WorkspaceSession, 'target' | 'workspace'> | null,
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => context.session }));
vi.mock('@azure/msal-react', () => ({ useMsal: () => ({ accounts: [] }) }));
vi.mock('../../components/layout/useCspBranding', () => ({ useCspBranding: () => ({ displayName: 'Synthetic provider', logoUrl: null }) }));
vi.mock('../../hooks/useNotifications', () => ({ useNotifications: () => ({ unreadCount: 0 }) }));
vi.mock('../../components/chat/ChatPanelContext', () => ({ useChatPanel: () => ({ panelState: { isOpen: false }, togglePanel: vi.fn() }) }));
vi.mock('../../components/chat/ChatToggle', () => ({ default: () => null }));
vi.mock('../../components/help/HelpPanel', () => ({ default: () => null }));
vi.mock('../../components/settings/SettingsPanel', () => ({ default: () => null }));
vi.mock('../../components/notifications/NotificationPanel', () => ({ default: () => null }));
vi.mock('../../features/auth/AccountMenu', () => ({ default: () => null }));
vi.mock('../../features/tenancy/TenantPicker', () => ({ default: () => null }));

function RouteProbe() {
  const location = useLocation();
  return <output aria-label="Current route">{location.pathname}</output>;
}

function renderNavigation(target: WorkspaceTarget, systemPanel = false) {
  context.session = {
    target,
    workspace: {
      kind: target.kind, tenantId: target.kind === 'csp' ? null : target.tenantId,
      displayName: 'Synthetic workspace', mode: target.kind === 'organization' ? target.mode ?? 'ordinary' : 'ordinary',
      personId: target.kind === 'csp' ? null : 'synthetic-person', roles: ['CSP.Admin'],
      permissions: { canAccessCsp: true, canManageOrganization: true, canManageMemberships: true },
    },
  };
  return render(<MemoryRouter initialEntries={[buildWorkspaceUrl(target, '/security-capabilities')]}>
    <WorkspaceNavigationProvider workspace={target}>
      <RouteProbe /><PageLayout title="Catalog" sidePanel={systemPanel ? <p>System follow-up tasks</p> : undefined}
        defaultSidePanelOpen={!systemPanel}><p>Synthetic catalog content</p></PageLayout>
    </WorkspaceNavigationProvider>
  </MemoryRouter>);
}

beforeEach(() => { context.session = null; });

describe('authorization-led provider navigation', () => {
  it('keeps the system follow-up panel collapsed until explicitly expanded', () => {
    // Arrange
    renderNavigation({ kind: 'organization', tenantId: 'org-a' }, true);
    // Assert
    expect(screen.queryByText('System follow-up tasks')).not.toBeInTheDocument();
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Expand panel' }));
    // Assert
    expect(screen.getByText('System follow-up tasks')).toBeVisible();
  });
  it('uses Environment for hosting instead of a separate provider relationships destination', () => {
    // Arrange
    const items = SYSTEM_NAV_GROUPS.flatMap(group => group.items);
    // Act
    const relationship = items.find(item => item.path === 'provider-relationships');
    // Assert
    expect(relationship).toBeUndefined();
    expect(items.find(item => item.path === 'profile/EnvironmentAndDeployment')).toMatchObject({ label: 'Environment' });
    expect(items.find(item => item.path === 'security-capabilities')).toMatchObject({ label: 'Security Capabilities' });
    expect(items.some(item => item.path === 'authorize')).toBe(true);
  });
  it('provides a CSP Authorizations destination beside the reusable catalog', () => {
    // Arrange
    renderNavigation({ kind: 'csp' });
    const desktop = screen.getAllByRole('navigation').find(nav => nav.getAttribute('aria-label') !== 'Mobile navigation')!;
    // Act
    fireEvent.click(within(desktop).getByRole('link', { name: 'Authorizations' }));
    // Assert
    expect(screen.getByLabelText('Current route')).toHaveTextContent('/workspaces/csp/authorizations');
    expect(within(desktop).getByRole('link', { name: 'Authorizations' })).toHaveAttribute('aria-current', 'page');
    expect(within(desktop).getByRole('link', { name: 'Security Capabilities' })).toHaveAttribute('href', '/workspaces/csp/security-capabilities');
  });

  it('includes the same Authorizations destination in compact navigation', () => {
    // Arrange
    renderNavigation({ kind: 'csp' });
    // Act
    fireEvent.click(screen.getByText('Navigation', { selector: 'summary' }));
    const mobile = screen.getByRole('navigation', { name: 'Mobile navigation' });
    // Assert
    expect(within(mobile).getByRole('link', { name: 'Authorizations' })).toHaveAttribute('href', '/workspaces/csp/authorizations');
    expect(within(mobile).getByRole('link', { name: 'Security Capabilities' })).toBeInTheDocument();
  });

  it.each(['ordinary', 'support'] as const)('does not expose provider Authorizations in an %s organization workspace', mode => {
    // Arrange
    const target: WorkspaceTarget = mode === 'support'
      ? { kind: 'organization', tenantId: 'synthetic-org', mode }
      : { kind: 'organization', tenantId: 'synthetic-org' };
    // Act
    renderNavigation(target);
    fireEvent.click(screen.getByText('Navigation', { selector: 'summary' }));
    // Assert
    expect(screen.queryByRole('link', { name: 'Authorizations' })).not.toBeInTheDocument();
    for (const link of screen.getAllByRole('link', { name: 'Security Capabilities' })) {
      expect(link).toHaveAttribute('href', buildWorkspaceUrl(target, '/security-capabilities'));
    }
  });

  it('does not infer provider navigation without an authorized workspace', () => {
    // Arrange
    context.session = null;
    // Act
    render(<MemoryRouter><PageLayout title="Legacy"><p>Legacy content</p></PageLayout></MemoryRouter>);
    // Assert
    expect(screen.queryByRole('link', { name: 'Authorizations' })).not.toBeInTheDocument();
  });
});
