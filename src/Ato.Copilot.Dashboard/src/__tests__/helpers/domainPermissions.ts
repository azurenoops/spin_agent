import { act } from '@testing-library/react';
import { vi } from 'vitest';
import type { WorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import type { SystemWorkspacePermissions, WorkspaceDescriptor } from '../../features/workspaces/types';

export function workspaceSession(
  systemId: string,
  permissions: Partial<SystemWorkspacePermissions> = {},
  roles = ['MissionOwner'],
): WorkspaceSession {
  const workspace: WorkspaceDescriptor = {
    kind: 'organization', tenantId: 'tenant-a', displayName: 'Synthetic organization',
    mode: 'ordinary', personId: 'person-a', roles,
    permissions: { canManageMemberships: false, canManageOrganization: false, canAccessCsp: false },
  };
  return {
    workspace, roles, target: { kind: 'organization', tenantId: 'tenant-a' }, refresh: vi.fn(),
    identity: {
      oid: 'actor-a', directoryTenantId: 'directory-a', displayName: 'Synthetic actor', persona: 'User',
      homeTenant: null, effectiveTenant: null, isImpersonating: false, impersonation: null,
      pimRoles: [], isCspAdmin: false, isSocAnalyst: false, tenantMemberships: [],
      workspace, permissions: workspace.permissions,
    },
    systemAccess: {
      systemId, roles,
      permissions: {
        canRead: true, canEditProfile: false, canManageSystem: false,
        canAuthorNarratives: false, canReviewNarratives: false, canManageEvidence: false,
        canRunAssessments: false, canManageRemediation: false, canDecideAuthorization: false,
        ...permissions,
      },
    },
  };
}

export function requireElement(element: HTMLElement | null | undefined): HTMLElement {
  if (!element) throw new Error('Expected a rendered element.');
  return element;
}

// Bypass the disabled DOM affordance to verify that the current handler independently denies writes.
export async function invokeClick(element: HTMLElement | null | undefined): Promise<void> {
  const button = requireElement(element);
  const key = Object.keys(button).find(name => name.startsWith('__reactProps$'));
  if (!key) throw new Error('Expected React props on the rendered element.');
  const props: unknown = Reflect.get(button, key);
  if (!props || typeof props !== 'object' || !('onClick' in props) || typeof props.onClick !== 'function') {
    throw new Error('Expected a React click handler.');
  }
  const onClick = props.onClick;
  await act(async () => { await onClick({ stopPropagation: vi.fn() }); });
}
