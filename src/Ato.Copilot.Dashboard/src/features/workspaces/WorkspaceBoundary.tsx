import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useMe } from '../auth/useMe';
import type { MeResponse } from '../auth/types';
import { getSystemWorkspaceAccess, workspaceErrorMessage } from './api';
import { WorkspaceNavigationProvider } from './workspaceNavigation';
import { buildWorkspaceUrl, parseWorkspaceUrl, systemIdFromRoute, type WorkspaceTarget } from './workspaceRoutes';
import type { SystemWorkspaceAccess, WorkspaceDescriptor } from './types';

export interface WorkspaceSession {
  identity: MeResponse;
  workspace: WorkspaceDescriptor;
  target: WorkspaceTarget;
  systemAccess: SystemWorkspaceAccess | null;
  roles: string[];
  refresh: () => void;
}

const WorkspaceSessionContext = createContext<WorkspaceSession | null>(null);

export function useWorkspaceSession(): WorkspaceSession | null {
  return useContext(WorkspaceSessionContext);
}

export function WorkspaceStatus({ message, loading = false, onRetry }: {
  message: string;
  loading?: boolean;
  onRetry?: () => void;
}) {
  return (
    <main className="mx-auto max-w-xl space-y-4 px-6 py-16">
      <h1 className="text-xl font-semibold text-gray-900">{loading ? 'Resolving workspace' : 'Workspace unavailable'}</h1>
      <p role={loading ? 'status' : 'alert'} className="text-sm text-gray-700">{message}</p>
      {!loading && (
        <div className="flex gap-4 text-sm">
          {onRetry && <button type="button" onClick={onRetry} className="text-indigo-700 underline">Retry</button>}
          <Link to="/login/select-tenant" className="text-indigo-700 underline">Choose a workspace</Link>
        </div>
      )}
    </main>
  );
}

function matchesWorkspace(target: WorkspaceTarget, workspace: WorkspaceDescriptor): boolean {
  if (workspace.kind !== target.kind) return false;
  if (target.kind === 'csp') return workspace.tenantId === null && workspace.mode === 'ordinary';
  return workspace.tenantId?.toLowerCase() === target.tenantId.toLowerCase()
    && workspace.mode === (target.mode ?? 'ordinary');
}

function SessionContent({ session, children }: { session: WorkspaceSession; children: ReactNode }) {
  return (
    <WorkspaceSessionContext.Provider value={session}>
      <WorkspaceNavigationProvider workspace={session.target}>{children}</WorkspaceNavigationProvider>
    </WorkspaceSessionContext.Provider>
  );
}

export default function WorkspaceBoundary({ target, children }: { target: WorkspaceTarget; children: ReactNode }) {
  const { data: identity, isLoading, error, refetch } = useMe();
  const location = useLocation();
  if (isLoading) return <WorkspaceStatus loading message="Resolving workspace access..." />;
  if (error) return <WorkspaceStatus message={workspaceErrorMessage(error)} onRetry={refetch} />;
  if (!identity || !Object.hasOwn(identity, 'workspace')) {
    return <WorkspaceStatus message="The workspace API is unavailable. Reload after the server has been updated." onRetry={refetch} />;
  }
  const workspace = identity.workspace;
  if (!workspace || !matchesWorkspace(target, workspace)) {
    return <WorkspaceStatus message="The server did not authorize the requested workspace." onRetry={refetch} />;
  }
  const permissions = workspace.permissions;
  if (typeof identity.oid !== 'string' || !identity.oid
    || typeof workspace.displayName !== 'string'
    || !Array.isArray(workspace.roles) || workspace.roles.some(role => typeof role !== 'string') || !permissions
    || typeof permissions.canManageMemberships !== 'boolean'
    || typeof permissions.canManageOrganization !== 'boolean'
    || typeof permissions.canAccessCsp !== 'boolean'
    || (target.kind === 'csp' && !permissions.canAccessCsp)
    || (workspace.kind === 'organization' && workspace.mode === 'ordinary' && !workspace.personId)) {
    return <WorkspaceStatus message="The workspace API returned incomplete authorization information." onRetry={refetch} />;
  }
  const parsed = parseWorkspaceUrl(location.pathname);
  if (!parsed || buildWorkspaceUrl(parsed.workspace) !== buildWorkspaceUrl(target)) {
    return <WorkspaceStatus message="The requested workspace does not match this navigation context." />;
  }
  const systemId = systemIdFromRoute(parsed.route);
  const session: WorkspaceSession = { identity, workspace, target, systemAccess: null, roles: workspace.roles, refresh: refetch };
  const key = `${identity.directoryTenantId ?? ''}:${identity.oid}:${buildWorkspaceUrl(target)}:${systemId ?? ''}`;
  return systemId ? (
    <SystemBoundary key={key} session={session} systemId={systemId}>{children}</SystemBoundary>
  ) : <SessionContent key={key} session={session}>{children}</SessionContent>;
}

function SystemBoundary({ session, systemId, children }: {
  session: WorkspaceSession;
  systemId: string;
  children: ReactNode;
}) {
  const [access, setAccess] = useState<SystemWorkspaceAccess | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setAccess(null);
    setError(null);
    getSystemWorkspaceAccess(systemId, controller.signal)
      .then(data => { if (!controller.signal.aborted) setAccess(data); })
      .catch(reason => { if (!controller.signal.aborted) setError(workspaceErrorMessage(reason)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [systemId, revision]);

  const retry = () => { setLoading(true); setRevision(value => value + 1); };
  if (loading) return <WorkspaceStatus loading message="Resolving workspace system access..." />;
  if (error) return <WorkspaceStatus message={error} onRetry={retry} />;
  if (!access || typeof access.systemId !== 'string' || access.systemId.toLowerCase() !== systemId.toLowerCase()
    || access.permissions?.canRead !== true || !Array.isArray(access.roles)
    || access.roles.some(role => typeof role !== 'string')) {
    return <WorkspaceStatus message="An applicable role assignment is required for system access." onRetry={retry} />;
  }
  return <SessionContent session={{ ...session, systemAccess: access, roles: access.roles }}>{children}</SessionContent>;
}
