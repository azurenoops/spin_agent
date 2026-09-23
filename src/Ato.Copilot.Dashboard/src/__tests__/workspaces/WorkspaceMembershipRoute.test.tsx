import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import axios from 'axios';
import WorkspaceMembershipRoute from '../../features/workspaces/WorkspaceMembershipRoute';
import type { WorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import type { WorkspaceMembership } from '../../features/workspaces/types';

const state = vi.hoisted(() => ({
  session: null as WorkspaceSession | null,
  revoked: null as WorkspaceMembership | null,
}));
vi.mock('axios', () => ({ default: { get: vi.fn() } }));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({
  useWorkspaceSession: () => state.session,
  WorkspaceStatus: ({ message }: { message: string }) => <p role="alert">{message}</p>,
}));
vi.mock('../../features/workspaces/MembershipAdministrationPage', () => ({
  default: ({ tenantId, tenantName, onRevoked }: {
    tenantId: string; tenantName: string; onRevoked?: (membership: WorkspaceMembership) => void;
  }) => <>
    <output>{tenantId} — {tenantName}</output>
    <button onClick={() => { if (state.revoked) onRevoked?.(state.revoked); }}>Confirm successful revocation</button>
  </>,
}));
function session(kind: 'organization' | 'csp', canManageMemberships: boolean): WorkspaceSession {
  return {
    target: kind === 'csp' ? { kind } : { kind, tenantId: 'org-a' },
    workspace: {
      kind, tenantId: kind === 'csp' ? null : 'org-a', displayName: 'Server Organization A',
      mode: 'ordinary', personId: 'person-a', roles: ['MissionOwner'],
      permissions: { canManageMemberships, canManageOrganization: true, canAccessCsp: kind === 'csp' },
    },
    identity: {
      oid: 'actor-a', directoryTenantId: 'directory-a', displayName: 'Actor', persona: 'MissionOwner',
      homeTenant: null, effectiveTenant: null, isImpersonating: false, impersonation: null,
      pimRoles: [], isCspAdmin: kind === 'csp', isSocAnalyst: false, tenantMemberships: [],
    },
    roles: ['MissionOwner'], systemAccess: null, refresh: vi.fn(),
  };
}
function route(path: string) {
  render(<MemoryRouter initialEntries={[path]}><Routes>
    <Route path="/settings/memberships" element={<WorkspaceMembershipRoute />} />
    <Route path="/organizations/:organizationId/memberships" element={<WorkspaceMembershipRoute />} />
  </Routes></MemoryRouter>);
}
beforeEach(() => { vi.mocked(axios.get).mockReset(); state.revoked = null; });
describe('membership route authorization', () => {
  it('does not infer membership administration from MissionOwner or other organization permissions', () => {
    // Arrange
    state.session = session('organization', false);
    // Act
    route('/settings/memberships');
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('not authorized');
    expect(axios.get).not.toHaveBeenCalled();
  });
  it('uses the authorized active organization ID and server label', () => {
    // Arrange
    state.session = session('organization', true);
    // Act
    route('/settings/memberships');
    // Assert
    expect(screen.getByRole('status')).toHaveTextContent('org-a — Server Organization A');
    expect(axios.get).not.toHaveBeenCalled();
  });
  it('denies cross-organization administration from an organization workspace', () => {
    // Arrange
    state.session = session('organization', true);
    // Act
    route('/organizations/org-b/memberships');
    // Assert
    expect(screen.getByRole('alert')).toHaveTextContent('active organization');
    expect(axios.get).not.toHaveBeenCalled();
  });
  it('verifies the provider organization and passes the returned name, not route text', async () => {
    // Arrange
    state.session = session('csp', true);
    vi.mocked(axios.get).mockResolvedValue({ data: { status: 'success', data: { id: 'org-b', displayName: 'Verified Organization B' } } });
    // Act
    route('/organizations/org-b/memberships');
    // Assert
    expect(await screen.findByRole('status')).toHaveTextContent('org-b — Verified Organization B');
    expect(axios.get).toHaveBeenCalledWith('/api/tenants/org-b', expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });
  it('does not mount the membership page for a mismatched provider organization response', async () => {
    // Arrange
    state.session = session('csp', true);
    vi.mocked(axios.get).mockResolvedValue({ data: { status: 'success', data: { id: 'org-a', displayName: 'Wrong Organization' } } });
    // Act
    route('/organizations/org-b/memberships');
    // Assert
    expect(await screen.findByText('The requested organization could not be verified.')).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it.each([
    ['DIRECTORY-A', 'ACTOR-A', 'directory-a', 'person-a', 'ordinary', 'org-a', true],
    ['directory-b', 'actor-a', 'directory-a', 'person-a', 'ordinary', 'org-a', false],
    ['directory-a', 'actor-b', 'directory-a', 'person-a', 'ordinary', 'org-a', false],
    ['directory-a', 'actor-a', undefined, 'PERSON-A', 'ordinary', 'org-a', true],
    ['directory-a', 'actor-a', undefined, 'person-b', 'ordinary', 'org-a', false],
    ['directory-a', 'actor-a', 'directory-a', 'person-a', 'support', 'org-a', false],
    ['directory-a', 'actor-a', 'directory-a', 'person-a', 'ordinary', 'org-b', false],
  ] as const)('refreshes only ordinary self-revocation: %s/%s/%s/%s/%s/%s', (directoryTenantId, objectId, identityDirectory, personId, mode, tenantId, refreshes) => {
    // Arrange
    state.session = session('organization', true);
    state.session.identity.directoryTenantId = identityDirectory;
    state.session.workspace.mode = mode;
    if (mode === 'support') state.session.target = { kind: 'organization', tenantId: 'org-a', mode };
    state.revoked = {
      id: 'membership-a', tenantId, personId, directoryTenantId, objectId,
      grantedAt: '2026-09-21T00:00:00Z', grantedBy: 'grantor', revokedAt: null, revokedBy: null,
    };
    route('/settings/memberships');
    // Act
    fireEvent.click(screen.getByRole('button', { name: 'Confirm successful revocation' }));
    // Assert
    expect(state.session.refresh).toHaveBeenCalledTimes(refreshes ? 1 : 0);
  });
});
