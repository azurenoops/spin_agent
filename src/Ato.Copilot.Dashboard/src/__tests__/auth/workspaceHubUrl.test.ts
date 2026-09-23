import { afterEach, describe, expect, it } from 'vitest';
import { workspaceHubUrl } from '../../features/workspaces/workspaceHubUrl';

afterEach(() => { window.history.replaceState({}, '', '/'); });

describe('workspace hub selectors', () => {
  it.each([
    ['/workspaces/csp', 'csp', null, 'ordinary'],
    ['/workspaces/organizations/org-alpha', 'organization', 'org-alpha', 'ordinary'],
    ['/workspaces/support/organizations/org-alpha', 'organization', 'org-alpha', 'support'],
  ])('binds %s without encoding credentials', (page, kind, tenantId, mode) => {
    // Arrange
    window.history.replaceState({}, '', page);

    // Act
    const url = new URL(workspaceHubUrl('/hubs/notifications?version=1'));

    // Assert
    expect(url.searchParams.get('workspaceKind')).toBe(kind);
    expect(url.searchParams.get('workspaceTenantId')).toBe(tenantId);
    expect(url.searchParams.get('workspaceMode')).toBe(mode);
    expect(url.searchParams.get('version')).toBe('1');
    expect(url.searchParams.has('access_token')).toBe(false);
  });

  it('does not carry stale workspace selectors from a legacy route', () => {
    // Arrange
    window.history.replaceState({}, '', '/systems/a');

    // Act
    const url = new URL(workspaceHubUrl('https://api.example.invalid/hubs/package?workspaceKind=csp&workspaceTenantId=old&workspaceMode=support'));

    // Assert
    expect(url.origin).toBe('https://api.example.invalid');
    expect(url.searchParams.has('workspaceKind')).toBe(false);
    expect(url.searchParams.has('workspaceTenantId')).toBe(false);
    expect(url.searchParams.has('workspaceMode')).toBe(false);
  });
});
