import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import ProviderNavigation from '../../features/provider-workspace/ProviderNavigation';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';

describe('mock-defined provider navigation', () => {
  it('keeps the five task destinations in the current provider workspace', () => {
    // Arrange
    render(<MemoryRouter initialEntries={['/workspaces/csp/authorizations']}>
      <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}>
        <ProviderNavigation />
      </WorkspaceNavigationProvider>
    </MemoryRouter>);
    // Act
    const nav = screen.getByRole('navigation', { name: 'Provider workspace' });
    // Assert
    expect(within(nav).getAllByRole('link').map(link => link.textContent)).toEqual([
      'Overview', 'Offerings', 'Mission systems', 'Changes', 'Administration',
    ]);
    expect(within(nav).getByRole('link', { name: 'Offerings' })).toHaveAttribute('aria-current', 'page');
    expect(within(nav).getByRole('link', { name: 'Mission systems' })).toHaveAttribute('href', '/workspaces/csp/systems');
    expect(within(nav).getByRole('link', { name: 'Changes' })).toHaveAttribute('href', '/workspaces/csp/provider-changes');
  });

  it('provides mobile navigation without dropping workspace scope', () => {
    // Arrange
    render(<MemoryRouter initialEntries={['/workspaces/csp']}>
      <WorkspaceNavigationProvider workspace={{ kind: 'csp' }}>
        <ProviderNavigation mobile />
      </WorkspaceNavigationProvider>
    </MemoryRouter>);
    // Act
    fireEvent.click(screen.getByText('Provider navigation'));
    // Assert
    expect(screen.getByRole('link', { name: 'Administration' })).toHaveAttribute('href', '/workspaces/csp/provider-administration');
    expect(screen.queryByText('Flank Speed')).not.toBeInTheDocument();
  });
});
