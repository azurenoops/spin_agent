import { useLocation } from 'react-router-dom';
import { useWorkspaceSession } from '../../features/workspaces/WorkspaceBoundary';
import type { SystemWorkspacePermissions } from '../../features/workspaces/types';

export function useSystemMutationPermission(
  systemId: string | undefined,
  permission: keyof SystemWorkspacePermissions | null,
  legacyAllowed = true,
): boolean {
  const workspace = useWorkspaceSession();
  const { pathname } = useLocation();
  const canonical = pathname === '/workspaces' || pathname.startsWith('/workspaces/');
  if (!workspace && !canonical) return legacyAllowed;
  const access = workspace?.systemAccess;
  // A null permission means this operation has no server projection yet.
  return permission !== null && typeof systemId === 'string' && systemId.length > 0
    && access?.systemId?.toLowerCase() === systemId.toLowerCase()
    && access.permissions?.[permission] === true;
}
