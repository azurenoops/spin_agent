import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { WorkspaceNavigationProvider, useLocation } from '../../features/workspaces/workspaceNavigation';
import OrganizationNavigation from '../../features/workspaces/OrganizationNavigation';
import { SystemPageSelector, SystemSidebar } from '../../features/systems/SystemNavigation';
import { SYSTEM_SCREEN_GROUPS } from '../../features/systems/systemScreenRoutes';

function Location() {
  const location = useLocation();
  return <output aria-label="Selected route">{location.pathname}{location.search}</output>;
}
describe('mock-aligned scoped system navigation', () => {
  it('shows the four top-level destinations with Systems active inside a system', () => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={['/workspaces/organizations/org-a/systems/system-a/documents']}>
      <WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}><OrganizationNavigation /></WorkspaceNavigationProvider>
    </MemoryRouter>);
    // Assert
    const nav = screen.getByRole('navigation', { name: 'Organization navigation' });
    expect(within(nav).getAllByRole('link').map(link => link.textContent)).toEqual(['Portfolio', 'Systems', 'Security Capabilities', 'Knowledge Base']);
    expect(within(nav).getByRole('link', { name: 'Systems' })).toHaveAttribute('aria-current', 'page');
    expect(within(nav).getByRole('link', { name: 'Security Capabilities' })).toHaveAttribute('href', '/workspaces/organizations/org-a/security-capabilities');
  });
  it('shows only eight section links under the selected system name', () => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={['/workspaces/organizations/org-a/systems/system-a/documents?tab=exports']}>
      <WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}><SystemSidebar systemId="system-a" systemName="SPIN Demo System" /></WorkspaceNavigationProvider>
    </MemoryRouter>);
    // Assert
    const nav = screen.getByRole('navigation', { name: 'System navigation' });
    expect(screen.getByText('SPIN Demo System')).toBeInTheDocument();
    expect(within(nav).getAllByRole('link').map(link => link.textContent)).toEqual(SYSTEM_SCREEN_GROUPS.map(group => group.label));
    expect(within(nav).getByRole('link', { name: 'ATO Readiness' })).toHaveAttribute('aria-current', 'page');
    expect(within(nav).getByRole('link', { name: 'ATO Readiness' })).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a/documents');
    expect(within(nav).queryByRole('link', { name: 'Export packages' })).not.toBeInTheDocument();
    expect(within(nav).getByRole('link', { name: 'Overview' })).toHaveAttribute('href', '/workspaces/organizations/org-a/systems/system-a');
    expect(nav.querySelectorAll('svg[aria-hidden="true"]')).toHaveLength(8);
  });
  it('navigates from the grouped mobile selector without dropping system context', () => {
    // Arrange
    render(<MemoryRouter initialEntries={['/workspaces/organizations/org-a/systems/system-a']}>
      <WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a' }}>
        <SystemPageSelector systemId="system-a" /><Location />
      </WorkspaceNavigationProvider>
    </MemoryRouter>);
    // Act
    fireEvent.change(screen.getByLabelText('Navigate system pages'), { target: { value: 'documents?tab=exports' } });
    // Assert
    expect(screen.getByLabelText('Selected route')).toHaveTextContent('/systems/system-a/documents?tab=exports');
    expect(screen.getByLabelText('Navigate system pages')).toHaveValue('documents?tab=exports');
    expect(screen.getByLabelText('Navigate system pages').querySelectorAll('optgroup')).toHaveLength(8);
  });
  it('keeps audited support URLs and role context in navigation targets', () => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={['/workspaces/support/organizations/org-a/systems/system-a/profile/MissionAndPurpose']}>
      <WorkspaceNavigationProvider workspace={{ kind: 'organization', tenantId: 'org-a', mode: 'support' }}>
        <OrganizationNavigation /><SystemSidebar systemId="system-a" systemName="Mission Alpha" />
      </WorkspaceNavigationProvider>
    </MemoryRouter>);
    // Assert
    expect(within(screen.getByRole('navigation', { name: 'Organization navigation' })).getByRole('link', { name: 'Systems' }))
      .toHaveAttribute('href', '/workspaces/support/organizations/org-a/systems');
    expect(within(screen.getByRole('navigation', { name: 'System navigation' })).getByRole('link', { name: 'Controls & evidence' }))
      .toHaveAttribute('href', '/workspaces/support/organizations/org-a/systems/system-a/baseline');
  });
});
