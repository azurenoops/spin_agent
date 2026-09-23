import { captureWorkspaceSnapshot } from './workspaceTransport';

export function workspaceHubUrl(url: string): string {
  const destination = new URL(url, window.location.origin);
  const { headers } = captureWorkspaceSnapshot();
  const selectors = [
    ['workspaceKind', 'X-Workspace-Kind'],
    ['workspaceTenantId', 'X-Workspace-Tenant-Id'],
    ['workspaceMode', 'X-Workspace-Mode'],
  ] as const;
  for (const [query, header] of selectors) {
    destination.searchParams.delete(query);
    const value = headers[header];
    if (value) destination.searchParams.set(query, value);
  }
  return destination.href;
}
