import type { ReactNode } from 'react';
import { renderHook } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useSystemMutationPermission } from '../../components/permissions/useSystemMutationPermission';
import type { SystemWorkspacePermissions } from '../../features/workspaces/types';

const state = vi.hoisted(() => ({
  session: null as {
    roles?: string[];
    systemAccess?: { systemId: string; permissions?: Partial<SystemWorkspacePermissions> } | null;
  } | null,
}));
vi.mock('../../features/workspaces/WorkspaceBoundary', () => ({ useWorkspaceSession: () => state.session }));

function wrapper(path: string) {
  return ({ children }: { children: ReactNode }) => <MemoryRouter initialEntries={[path]}>{children}</MemoryRouter>;
}
const canonical = '/workspaces/organizations/org-a/systems/system-a/assessments';

beforeEach(() => {
  state.session = null;
});

describe('useSystemMutationPermission', () => {
  it.each([true, false])('retains deliberate legacy permission %s outside canonical routes', legacyAllowed => {
    // Arrange
    const wrap = wrapper('/systems/system-a/assessments');
    // Act
    const { result } = renderHook(() => useSystemMutationPermission('system-a', 'canRunAssessments', legacyAllowed), { wrapper: wrap });
    // Assert
    expect(result.current).toBe(legacyAllowed);
  });

  it.each([null, {}, { systemAccess: null }, { systemAccess: { systemId: 'system-a' } }])(
    'fails closed for missing canonical permission context',
    session => {
      // Arrange
      state.session = session;
      // Act
      const { result } = renderHook(() => useSystemMutationPermission('system-a', 'canRunAssessments'), { wrapper: wrapper(canonical) });
      // Assert
      expect(result.current).toBe(false);
    },
  );

  it('does not infer authority from RMF roles or a broader system-management flag', () => {
    // Arrange
    state.session = { roles: ['MissionOwner', 'ISSM', 'AO'], systemAccess: { systemId: 'system-a', permissions: { canManageSystem: true } } };
    // Act
    const { result } = renderHook(() => useSystemMutationPermission('system-a', 'canRunAssessments'), { wrapper: wrapper(canonical) });
    // Assert
    expect(result.current).toBe(false);
  });

  it.each(['system-b', undefined])('rejects permissions for a different or unknown system (%s)', systemId => {
    // Arrange
    state.session = { systemAccess: { systemId: 'system-a', permissions: { canManageEvidence: true } } };
    // Act
    const { result } = renderHook(() => useSystemMutationPermission(systemId, 'canManageEvidence'), { wrapper: wrapper(canonical) });
    // Assert
    expect(result.current).toBe(false);
  });

  it('honors the requested flag and reacts to revocation without using legacy fallback', () => {
    // Arrange
    state.session = { roles: ['MissionOwner', 'Isso'], systemAccess: { systemId: 'SYSTEM-A', permissions: { canManageEvidence: true } } };
    const { result, rerender } = renderHook(() => useSystemMutationPermission('system-a', 'canManageEvidence'), { wrapper: wrapper(canonical) });
    expect(result.current).toBe(true);
    // Act
    state.session.systemAccess = { systemId: 'system-a', permissions: { canManageEvidence: false } };
    rerender();
    // Assert
    expect(result.current).toBe(false);
  });

  it('does not use legacy authority when a workspace session exists on an alias', () => {
    // Arrange
    state.session = { systemAccess: { systemId: 'system-a', permissions: { canManageRemediation: false } } };
    // Act
    const { result } = renderHook(() => useSystemMutationPermission('system-a', 'canManageRemediation'), { wrapper: wrapper('/systems/system-a/remediation') });
    // Assert
    expect(result.current).toBe(false);
  });

  it('keeps operations without a server projection unavailable in canonical workspaces', () => {
    // Arrange
    state.session = { systemAccess: { systemId: 'system-a', permissions: { canManageSystem: true, canRunAssessments: true } } };
    // Act
    const { result } = renderHook(() => useSystemMutationPermission('system-a', null), { wrapper: wrapper(canonical) });
    // Assert
    expect(result.current).toBe(false);
  });
});
