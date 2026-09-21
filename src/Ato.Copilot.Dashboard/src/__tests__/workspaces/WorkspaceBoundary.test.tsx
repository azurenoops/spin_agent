import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import WorkspaceBoundary, { useWorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import type { MeResponse } from '../../features/auth/types';
import type { WorkspaceDescriptor } from '../../features/workspaces/types';

const mocks = vi.hoisted(() => ({
  me: { data: null as MeResponse | null, isLoading: false, error: null as Error | null, refetch: vi.fn() },
  access: vi.fn(),
}));
vi.mock('../../features/auth/useMe', () => ({ useMe: () => mocks.me }));
vi.mock('../../features/workspaces/api', () => ({
  getSystemWorkspaceAccess: mocks.access,
  workspaceErrorMessage: (error: unknown) => error instanceof Error ? error.message : 'Request failed',
}));

const target = { kind: 'organization' as const, tenantId: 'org-alpha' };
const workspace: WorkspaceDescriptor = {
  kind: 'organization', tenantId: 'org-alpha', displayName: 'Organization Alpha', mode: 'ordinary',
  personId: 'person-a', roles: ['MissionOwner'],
  permissions: { canManageMemberships: false, canManageOrganization: false, canAccessCsp: false },
};

function identity(selected: WorkspaceDescriptor | null = workspace): MeResponse {
  return {
    oid: 'user-a', displayName: 'Synthetic member', persona: 'User',
    homeTenant: null, effectiveTenant: null, isImpersonating: false, impersonation: null,
    pimRoles: [], isCspAdmin: false, isSocAnalyst: false, tenantMemberships: [],
    workspace: selected, availableWorkspaces: [], availableWorkspacesTotal: 1,
    permissions: workspace.permissions,
  };
}

function Content() {
  const session = useWorkspaceSession();
  return <div>Protected content: {session?.workspace.displayName}; roles: {session?.roles.join(', ')}</div>;
}

function mount(path = '/workspaces/organizations/org-alpha') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <WorkspaceBoundary target={target}><Content /></WorkspaceBoundary>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  Object.assign(mocks.me, { data: identity(), isLoading: false, error: null });
});

describe('WorkspaceBoundary', () => {
  it('permits a validated organization without inventing a home tenant', () => {
    // Arrange
    mocks.me.data = identity();

    // Act
    mount();

    // Assert
    expect(screen.getByText(/Protected content: Organization Alpha; roles: MissionOwner/)).toBeInTheDocument();
    expect(mocks.access).not.toHaveBeenCalled();
  });

  it('keeps protected content unmounted while identity is loading', () => {
    // Arrange
    mocks.me.isLoading = true;

    // Act
    mount();

    // Assert
    expect(screen.getByRole('status')).toHaveTextContent('Resolving workspace');
    expect(screen.queryByText(/Protected content/)).not.toBeInTheDocument();
  });

  it.each([
    { ...workspace, tenantId: 'org-beta' },
    { ...workspace, mode: 'support' as const },
    null,
  ])('rejects a server context that does not match the ordinary URL', selected => {
    // Arrange
    mocks.me.data = identity(selected);

    // Act
    mount();

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('requested workspace');
    expect(screen.queryByText(/Protected content/)).not.toBeInTheDocument();
  });

  it('does not treat a legacy response as workspace authorization', () => {
    // Arrange
    mocks.me.data = identity();
    delete mocks.me.data.workspace;

    // Act
    mount();

    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('workspace API');
    expect(screen.queryByText(/Protected content/)).not.toBeInTheDocument();
  });

  it('requires a server-authorized system role before mounting a system page', async () => {
    // Arrange
    mocks.access.mockResolvedValue({
      systemId: 'system-a', roles: [], permissions: { canRead: false },
    });

    // Act
    mount('/workspaces/organizations/org-alpha/systems/system-a');

    // Assert
    expect(await screen.findByRole('alert')).toHaveTextContent('system access');
    expect(screen.queryByText(/Protected content/)).not.toBeInTheDocument();
  });

  it('shows all effective system roles rather than a single highest persona', async () => {
    // Arrange
    mocks.access.mockResolvedValue({
      systemId: 'system-a', roles: ['MissionOwner', 'Sca'], permissions: { canRead: true },
    });

    // Act
    mount('/workspaces/organizations/org-alpha/systems/system-a/narratives');

    // Assert
    expect(await screen.findByText(/roles: MissionOwner, Sca/)).toBeInTheDocument();
    expect(mocks.access).toHaveBeenCalledWith('system-a', expect.any(AbortSignal));
  });

  it('surfaces a failed permission request without exposing system content', async () => {
    // Arrange
    mocks.access.mockRejectedValue(new Error('System is not available to this identity.'));

    // Act
    mount('/workspaces/organizations/org-alpha/systems/system-a');

    // Assert
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('System is not available'));
    expect(screen.queryByText(/Protected content/)).not.toBeInTheDocument();
  });
});
