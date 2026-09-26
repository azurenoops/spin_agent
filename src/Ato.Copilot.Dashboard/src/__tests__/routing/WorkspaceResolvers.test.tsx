import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { WorkspaceNavigationProvider } from '../../features/workspaces/workspaceNavigation';
import type { WorkspaceTarget } from '../../features/workspaces/workspaceRoutes';
import PortfolioRoute from '../../pages/PortfolioRoute';
import SystemsRoute from '../../pages/SystemsRoute';
import ComponentsRoute from '../../pages/ComponentsRoute';
import CapabilitiesRoute from '../../pages/CapabilitiesRoute';
import ControlsRoute from '../../pages/ControlsRoute';

const legacy = vi.hoisted(() => ({ csp: vi.fn(), impersonating: vi.fn() }));
vi.mock('../../components/layout/useCspDashboardAvailable', () => ({ useCspDashboardAvailable: legacy.csp }));
vi.mock('../../hooks/useImpersonationActive', () => ({ useImpersonationActive: legacy.impersonating }));
vi.mock('../../pages/PortfolioRiskProfile', () => ({ default: () => <div>Organization portfolio</div> }));
vi.mock('../../pages/PortfolioDashboard', () => ({ default: () => <div>Organization systems</div> }));
vi.mock('../../pages/ComponentLibrary', () => ({ default: () => <div>Organization components</div> }));
vi.mock('../../pages/CapabilityLibrary', () => ({ default: () => <div>Organization capabilities</div> }));
vi.mock('../../pages/ControlCatalog', () => ({ default: ({ scope = 'org' }: { scope?: string }) => <div>Controls {scope}</div> }));
vi.mock('../../features/csp-dashboard/CspDashboardPage', () => ({ default: () => <div>Provider portfolio</div> }));
vi.mock('../../features/csp-dashboard/CspSystemsPage', () => ({ default: () => <div>Provider systems</div> }));
vi.mock('../../features/csp-inherited-components/CspInheritedComponentsPage', () => ({ default: () => <div>Provider components</div> }));
vi.mock('../../features/csp-inherited-components/CspCapabilitiesPage', () => ({ default: () => <div>Provider capabilities</div> }));

const routes = [
  [PortfolioRoute, 'portfolio'],
  [SystemsRoute, 'systems'],
  [ComponentsRoute, 'components'],
  [CapabilitiesRoute, 'capabilities'],
] as const;

function CurrentLocation() {
  const location = useLocation();
  return <output aria-label="Current location">{location.pathname}{location.search}{location.hash}</output>;
}

beforeEach(() => vi.clearAllMocks());

describe('server-validated workspace route resolution', () => {
  for (const [Page, label] of routes) {
    it.each(['csp', 'organization'] as const)(`uses %s context for ${label} instead of stale probes`, kind => {
      // Arrange
      const workspace: WorkspaceTarget = kind === 'csp' ? { kind } : { kind, tenantId: 'org-alpha' };
      legacy.csp.mockReturnValue(kind !== 'csp');
      legacy.impersonating.mockReturnValue(kind === 'csp');

      // Act
      const prefix = kind === 'csp' ? '/workspaces/csp' : '/workspaces/organizations/org-alpha';
      render(<MemoryRouter initialEntries={[`${prefix}/${label}?search=threat#source`]}>
        <WorkspaceNavigationProvider workspace={workspace}><Page /><CurrentLocation /></WorkspaceNavigationProvider>
      </MemoryRouter>);

      // Assert
      if (label === 'components' || label === 'capabilities') {
        expect(screen.getByLabelText('Current location')).toHaveTextContent(
          `${prefix}/security-capabilities?search=threat&grouping=${label === 'components' ? 'component' : 'capability'}#source`,
        );
      } else {
        expect(screen.getByText(`${kind === 'csp' ? 'Provider' : 'Organization'} ${label}`)).toBeInTheDocument();
      }
      expect(legacy.csp).toHaveBeenCalledWith(false);
    });
  }

  it.each(['csp', 'organization'] as const)('labels controls for the explicit %s context', kind => {
    // Arrange
    const workspace: WorkspaceTarget = kind === 'csp' ? { kind } : { kind, tenantId: 'org-alpha' };
    legacy.csp.mockReturnValue(kind !== 'csp');
    legacy.impersonating.mockReturnValue(kind === 'csp');

    // Act
    const prefix = kind === 'csp' ? '/workspaces/csp' : '/workspaces/organizations/org-alpha';
    render(<MemoryRouter initialEntries={[`${prefix}/controls`]}>
      <WorkspaceNavigationProvider workspace={workspace}><ControlsRoute /></WorkspaceNavigationProvider>
    </MemoryRouter>);

    // Assert
    expect(screen.getByText(`Controls ${kind === 'csp' ? 'csp' : 'org'}`)).toBeInTheDocument();
    expect(legacy.csp).toHaveBeenCalledWith(false);
  });

  it('retains legacy probing outside a validated workspace', () => {
    // Arrange
    legacy.csp.mockReturnValue(true);
    legacy.impersonating.mockReturnValue(false);

    // Act
    render(<PortfolioRoute />);

    // Assert
    expect(screen.getByText('Provider portfolio')).toBeInTheDocument();
  });
});
