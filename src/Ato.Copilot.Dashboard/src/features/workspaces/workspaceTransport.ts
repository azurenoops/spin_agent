import { CanceledError, type AxiosInstance, type InternalAxiosRequestConfig } from 'axios';
import { buildWorkspaceUrl, parseWorkspaceUrl } from './workspaceRoutes';

type WorkspaceRequestConfig = InternalAxiosRequestConfig & {
  _workspaceRequestKey?: string;
};

const WORKSPACE_HEADERS = ['X-Workspace-Kind', 'X-Workspace-Tenant-Id', 'X-Workspace-Mode'];

function activeWorkspace() {
  return parseWorkspaceUrl(window.location.pathname)?.workspace ?? null;
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
  const workspace = activeWorkspace();
  if (config._workspaceRequestKey === undefined) {
    config._workspaceRequestKey = workspace ? buildWorkspaceUrl(workspace) : '';
  } else {
    assertWorkspaceRequestCurrent(config);
  }

  config.headers.delete(WORKSPACE_HEADERS);
  const destination = new URL(client.getUri(config), window.location.origin);
  const apiOrigin = new URL(config.baseURL ?? '/', window.location.origin).origin;
  if (!workspace || destination.origin !== apiOrigin || !destination.pathname.startsWith('/api/')) return;

  config.headers.set('X-Workspace-Kind', workspace.kind);
  config.headers.set('X-Workspace-Mode', 'ordinary');
  if (workspace.kind === 'organization') {
    config.headers.set('X-Workspace-Tenant-Id', workspace.tenantId);
  }
}
