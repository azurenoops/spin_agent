import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MemoryRouter, Route, Routes, useLocation as useActualLocation } from 'react-router-dom';
import {
  Link,
  NavLink,
  Navigate,
  useLocation,
  useNavigate,
  useWorkspaceHref,
  WorkspaceNavigationProvider,
} from '../../features/workspaces/workspaceNavigation';

const organization = { kind: 'organization' as const, tenantId: 'org-alpha' };

function Probe() {
  const relative = useLocation();
  const actual = useActualLocation();
  const navigate = useNavigate();
  const href = useWorkspaceHref();
  return (
    <>
      <output data-testid="relative">{relative.pathname}{relative.search}{relative.hash}</output>
      <output data-testid="actual">{actual.pathname}{actual.search}{actual.hash}</output>
      <output data-testid="state">{JSON.stringify(actual.state)}</output>
      <Link to="/systems?view=mine#list">Systems</Link>
      <NavLink to="/systems" className={({ isActive }) => isActive ? 'selected' : 'unselected'}>Active systems</NavLink>
      <Link to="/login">Sign in</Link>
      <Link to="/workspaces/organizations/org-beta/systems">Other workspace</Link>
      <Link to="https://example.invalid/reference">External reference</Link>
      <Link to="//example.invalid/help">External help</Link>
      <Link to="?view=review">Filter</Link>
      <Link to={{ search: '?view=pending' }}>Pending filter</Link>
      <Link to={{ pathname: '/controls', search: '?family=AC', hash: '#AC-2' }}>Control catalog</Link>
      <a href={href('/capabilities')}>Native capability link</a>
      <button onClick={() => void navigate('/components', { state: { from: 'systems' } })}>Components</button>
      <button onClick={() => void navigate(-1)}>Back</button>
    </>
  );
}

describe('workspace navigation adapter', () => {
  it('prefixes application destinations without rewriting global, relative or external links', () => {
    // Arrange
    const url = '/workspaces/organizations/org-alpha/systems?view=all#list';

    // Act
    render(
      <MemoryRouter initialEntries={[url]}>
        <WorkspaceNavigationProvider workspace={organization}>
          <Probe />
        </WorkspaceNavigationProvider>
      </MemoryRouter>,
    );

    // Assert
    expect(screen.getByRole('link', { name: 'Systems' })).toHaveAttribute('href', '/workspaces/organizations/org-alpha/systems?view=mine#list');
    expect(screen.getByRole('link', { name: 'Active systems' })).toHaveClass('selected');
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/login');
    expect(screen.getByRole('link', { name: 'Other workspace' })).toHaveAttribute('href', '/workspaces/organizations/org-beta/systems');
    expect(screen.getByRole('link', { name: 'External reference' })).toHaveAttribute('href', 'https://example.invalid/reference');
    expect(screen.getByRole('link', { name: 'External help' })).toHaveAttribute('href', '//example.invalid/help');
    expect(screen.getByRole('link', { name: 'Filter' })).toHaveAttribute('href', '/workspaces/organizations/org-alpha/systems?view=review');
    expect(screen.getByRole('link', { name: 'Pending filter' })).toHaveAttribute('href', '/workspaces/organizations/org-alpha/systems?view=pending');
    expect(screen.getByRole('link', { name: 'Control catalog' })).toHaveAttribute('href', '/workspaces/organizations/org-alpha/controls?family=AC#AC-2');
    expect(screen.getByRole('link', { name: 'Native capability link' })).toHaveAttribute('href', '/workspaces/organizations/org-alpha/capabilities');
    expect(screen.getByTestId('relative')).toHaveTextContent('/systems?view=all#list');
  });

  it('preserves navigation state and browser history', () => {
    // Arrange
    render(
      <MemoryRouter initialEntries={['/workspaces/organizations/org-alpha/systems']}>
        <WorkspaceNavigationProvider workspace={organization}>
          <Probe />
        </WorkspaceNavigationProvider>
      </MemoryRouter>,
    );

    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Components' }));

    // Assert
    expect(screen.getByTestId('actual')).toHaveTextContent('/workspaces/organizations/org-alpha/components');
    expect(screen.getByTestId('relative')).toHaveTextContent('/components');
    expect(screen.getByTestId('state')).toHaveTextContent('{"from":"systems"}');
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByTestId('actual')).toHaveTextContent('/workspaces/organizations/org-alpha/systems');
  });

  it('keeps legacy behavior outside a workspace provider', () => {
    // Arrange
    const url = '/systems';

    // Act
    render(<MemoryRouter initialEntries={[url]}><Probe /></MemoryRouter>);

    // Assert
    expect(screen.getByRole('link', { name: 'Systems' })).toHaveAttribute('href', '/systems?view=mine#list');
    expect(screen.getByTestId('relative')).toHaveTextContent('/systems');
  });

  it('uses the CSP workspace prefix', () => {
    // Arrange
    const workspace = { kind: 'csp' as const };

    // Act
    render(
      <MemoryRouter initialEntries={['/workspaces/csp/systems']}>
        <WorkspaceNavigationProvider workspace={workspace}><Probe /></WorkspaceNavigationProvider>
      </MemoryRouter>,
    );

    // Assert
    expect(screen.getByRole('link', { name: 'Systems' })).toHaveAttribute('href', '/workspaces/csp/systems?view=mine#list');
    expect(screen.getByTestId('relative')).toHaveTextContent('/systems');
  });

  it('scopes Navigate without adding an extra history entry', async () => {
    // Arrange
    const start = '/workspaces/organizations/org-alpha/start';

    // Act
    render(
      <MemoryRouter initialEntries={['/workspaces/organizations/org-alpha/systems', start]}>
        <WorkspaceNavigationProvider workspace={organization}>
          <Routes>
            <Route path={start} element={<Navigate to="/components" replace />} />
            <Route path="*" element={<Probe />} />
          </Routes>
        </WorkspaceNavigationProvider>
      </MemoryRouter>,
    );

    // Assert
    expect(await screen.findByTestId('actual')).toHaveTextContent('/workspaces/organizations/org-alpha/components');
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByTestId('actual')).toHaveTextContent('/workspaces/organizations/org-alpha/systems');
  });

  it.each(['/workspaces/csp', '/systems'])('fails closed for a mismatched navigation context at %s', url => {
    // Arrange
    const view = (
      <MemoryRouter initialEntries={[url]}>
        <WorkspaceNavigationProvider workspace={organization}><Probe /></WorkspaceNavigationProvider>
      </MemoryRouter>
    );

    // Act
    const mount = () => render(view);

    // Assert
    expect(mount).toThrow('Workspace navigation context does not match the current URL.');
  });
});
