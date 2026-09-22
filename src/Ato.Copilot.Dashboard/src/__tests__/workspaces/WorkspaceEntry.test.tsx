import { describe, it, expect, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import WorkspaceEntry from '../../features/workspaces/WorkspaceEntry';
import { MeContext } from '../../features/auth/useMe';
import type { MeResponse } from '../../features/auth/types';

const tenant = { id: 'org-a', displayName: 'Organization A', status: 'Active' as const };
const option = { kind: 'organization' as const, tenantId: tenant.id, displayName: tenant.displayName, status: 'Active', onboardingState: 'Active' };
const identity: MeResponse = {
  oid: 'owner', displayName: 'Mission Owner', persona: 'MissionOwner',
  homeTenant: tenant, effectiveTenant: tenant, isImpersonating: false,
  impersonation: null, pimRoles: [], isCspAdmin: false, isSocAnalyst: false, tenantMemberships: [tenant],
};

function Destination() {
  const location = useLocation();
  return <output>{location.pathname}{location.search}{location.hash}|{JSON.stringify(location.state)}</output>;
}
function renderEntry(me: MeResponse, route = '/') {
  return render(<MemoryRouter initialEntries={[route]}>
    <MeContext.Provider value={{ data: me, isLoading: false, error: null, refetch: vi.fn() }}>
      <Routes>
        <Route path="/workspaces/*" element={<Destination />} />
        <Route path="/login/select-tenant" element={<Destination />} />
        <Route path="*" element={<WorkspaceEntry><p>Legacy application</p></WorkspaceEntry>} />
      </Routes>
    </MeContext.Provider>
  </MemoryRouter>);
}

describe('workspace entry', () => {
  it('redirects ordinary MissionOwner deep links to the only authorized context with suffix intact', async () => {
    // Arrange
    const me = { ...identity, workspace: null, availableWorkspaces: [option], availableWorkspacesTotal: 1 };
    // Act
    renderEntry(me, '/systems/system-a/profile/MissionAndPurpose?review=1#mission');
    // Assert
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('/workspaces/organizations/org-a/systems/system-a/profile/MissionAndPurpose?review=1#mission'));
  });

  it('requires an explicit choice for multiple contexts despite a remembered home tenant', async () => {
    // Arrange
    const me = { ...identity, workspace: null, availableWorkspaces: [option], availableWorkspacesTotal: 2 };
    // Act
    renderEntry(me, '/systems/unknown?tab=review#section');
    // Assert
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('/login/select-tenant'));
    expect(screen.getByRole('status')).toHaveTextContent('/systems/unknown?tab=review#section');
    expect(screen.queryByText('Legacy application')).not.toBeInTheDocument();
  });

  it('preserves genuinely old unscoped responses', () => {
    // Arrange / Act
    renderEntry(identity);
    // Assert
    expect(screen.getByText('Legacy application')).toBeInTheDocument();
  });

  it('denies incomplete new responses rather than falling back to legacy probes', () => {
    // Arrange / Act
    renderEntry({ ...identity, workspace: null });
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('incomplete');
    expect(screen.queryByText('Legacy application')).not.toBeInTheDocument();
  });

  it('does not turn a denied context into a no-organizations claim', () => {
    // Arrange
    render(<MemoryRouter><MeContext.Provider value={{
      data: null, isLoading: false, error: new Error('Requested organization denied'), refetch: vi.fn(),
    }}><WorkspaceEntry><p>Private</p></WorkspaceEntry></MeContext.Provider></MemoryRouter>);
    // Act / Assert
    expect(screen.getByRole('alert')).toHaveTextContent('Requested organization denied');
    expect(screen.getByRole('link', { name: 'Choose a workspace' })).toBeInTheDocument();
    expect(screen.queryByText('Private')).not.toBeInTheDocument();
  });
});
