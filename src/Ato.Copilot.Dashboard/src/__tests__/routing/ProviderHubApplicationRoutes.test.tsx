import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import ApplicationRoutes from '../../ApplicationRoutes';

const access = vi.hoisted(() => ({ authenticated: true, providerScope: true, canAccessCsp: true }));
vi.mock('../../features/auth/RequireAuth', () => ({
  default: ({ children }: { children: ReactNode }) => access.authenticated ? children : <h1>Authentication required</h1>,
}));
vi.mock('../../features/provider-workspace/ProviderWorkspacePage', () => ({
  default: ({ view }: { view: string }) => <h1>Provider {view}</h1>,
}));
vi.mock('../../features/csp-dashboard/CspSystemsPage', () => ({
  default: () => <h1>Cross-organization system oversight</h1>,
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => ({
    target: { kind: access.providerScope ? 'csp' : 'organization' },
    workspace: { permissions: { canAccessCsp: access.canAccessCsp } },
  }),
}));

describe('protected provider hub routes', () => {
  beforeEach(() => { access.authenticated = true; access.providerScope = true; access.canAccessCsp = true; });
  it.each(['changes', 'administration'])('routes provider-%s to the selected hub view', async view => {
    // Arrange / Act
    render(<MemoryRouter initialEntries={[`/provider-${view}`]}><ApplicationRoutes /></MemoryRouter>);
    // Assert
    expect(await screen.findByRole('heading', { name: `Provider ${view}` })).toBeVisible();
  });
  it.each(['changes', 'administration'])('protects provider-%s with the existing authentication boundary', view => {
    // Arrange
    access.authenticated = false;
    // Act
    render(<MemoryRouter initialEntries={[`/provider-${view}`]}><ApplicationRoutes /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('heading', { name: 'Authentication required' })).toBeVisible();
    expect(screen.queryByRole('heading', { name: `Provider ${view}` })).not.toBeInTheDocument();
  });
  it.each([true, false])('preserves protected legacy oversight with authenticated=%s', authenticated => {
    // Arrange
    access.authenticated = authenticated;
    // Act
    render(<MemoryRouter initialEntries={['/provider-oversight/systems']}><ApplicationRoutes /></MemoryRouter>);
    // Assert
    expect(screen.getByRole('heading', { name: authenticated ? 'Cross-organization system oversight' : 'Authentication required' })).toBeVisible();
  });
  it.each([{ providerScope: false, canAccessCsp: true }, { providerScope: true, canAccessCsp: false }])(
    'denies oversight without the selected provider workspace and grant: %o', permission => {
      // Arrange
      Object.assign(access, permission);
      // Act
      render(<MemoryRouter initialEntries={['/provider-oversight/systems']}><ApplicationRoutes /></MemoryRouter>);
      // Assert
      expect(screen.getByRole('alert')).toHaveTextContent('Provider workspace access is required.');
      expect(screen.queryByRole('heading', { name: 'Cross-organization system oversight' })).not.toBeInTheDocument();
    },
  );
});
