import { useWorkspaceSession } from './WorkspaceBoundary';

interface StorageScope {
  directoryTenantId: string | null | undefined;
  objectId: string;
  kind: 'csp' | 'organization';
  tenantId: string | null;
  mode: 'ordinary' | 'support';
  systemId?: string | null;
}

export function workspaceStorageKey(base: string, scope: StorageScope | null): string | null {
  if (!scope) return base;
  if (!scope.directoryTenantId?.trim() || !scope.objectId.trim()) return null;
  const identity = [
    scope.directoryTenantId.toLowerCase(), scope.objectId.toLowerCase(),
    scope.kind, scope.tenantId?.toLowerCase() ?? null, scope.mode, scope.systemId?.toLowerCase() ?? null,
  ];
  return `${base}:workspace:${encodeURIComponent(JSON.stringify(identity))}`;
}

export function useWorkspaceStorageKey(base: string): string | null {
  const session = useWorkspaceSession();
  return workspaceStorageKey(base, session ? {
    directoryTenantId: session.identity.directoryTenantId,
    objectId: session.identity.oid,
    kind: session.workspace.kind,
    tenantId: session.workspace.tenantId,
    mode: session.workspace.mode,
    systemId: session.systemAccess?.systemId,
  } : null);
}
