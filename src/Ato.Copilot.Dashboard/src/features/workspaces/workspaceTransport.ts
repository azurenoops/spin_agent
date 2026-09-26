import { CanceledError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios';
import { buildWorkspaceUrl, parseWorkspaceUrl, type WorkspaceTarget } from './workspaceRoutes';

type WorkspaceRequestConfig = InternalAxiosRequestConfig & {
  _workspaceRequestKey?: string;
};

const WORKSPACE_HEADERS = ['X-Workspace-Kind', 'X-Workspace-Tenant-Id', 'X-Workspace-Mode'];

function activeWorkspace(): WorkspaceTarget | null {
  const path = window.location.pathname;
  if (path === '/onboarding/csp' || path === '/onboarding/csp/') return { kind: 'csp' };
  return parseWorkspaceUrl(path)?.workspace ?? null;
}

export function captureWorkspaceSnapshot(): { key: string; headers: Record<string, string> } {
  const workspace = activeWorkspace();
  if (!workspace) return { key: '', headers: {} };
  const headers: Record<string, string> = {
    'X-Workspace-Kind': workspace.kind,
    'X-Workspace-Mode': workspace.kind === 'organization' && workspace.mode === 'support' ? 'support' : 'ordinary',
  };
  if (workspace.kind === 'organization') headers['X-Workspace-Tenant-Id'] = workspace.tenantId;
  return { key: buildWorkspaceUrl(workspace), headers };
}

export function isWorkspaceSnapshotCurrent(snapshot: { key: string }): boolean {
  return snapshot.key === captureWorkspaceSnapshot().key;
}

export function assertWorkspaceRequestCurrent(config?: WorkspaceRequestConfig): void {
  if (config?._workspaceRequestKey === undefined) return;
  const workspace = activeWorkspace();
  const key = workspace ? buildWorkspaceUrl(workspace) : '';
  if (config._workspaceRequestKey !== key) {
    const error = new CanceledError('Workspace changed while the request was in progress.');
    error.config = config;
    throw error;
  }
}

export function captureWorkspaceRequest(client: AxiosInstance, config: WorkspaceRequestConfig): void {
  const snapshot = captureWorkspaceSnapshot();
  if (config._workspaceRequestKey === undefined) {
    config._workspaceRequestKey = snapshot.key;
  } else {
    assertWorkspaceRequestCurrent(config);
  }

  config.headers.delete(WORKSPACE_HEADERS);
  const destination = new URL(client.getUri(config), window.location.origin);
  const apiOrigin = new URL(config.baseURL ?? '/', window.location.origin).origin;
  if (destination.origin !== apiOrigin || !destination.pathname.startsWith('/api/')) return;
  // Cleanup must remain reachable after the support session itself expires.
  if (config.method?.toLowerCase() === 'delete' && destination.pathname === '/api/tenants/impersonation') {
    config.headers.set({ 'X-Workspace-Kind': 'csp', 'X-Workspace-Mode': 'ordinary' });
    return;
  }
  config.headers.set(snapshot.headers);
}
