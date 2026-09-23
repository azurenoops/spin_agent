import axios, { AxiosError, type AxiosInstance, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { afterEach, describe, expect, it } from 'vitest';
import { attachAuthInterceptor } from '../../features/auth/interceptors';

function authenticatedClient(baseURL?: string): AxiosInstance {
  const client = axios.create({ baseURL });
  attachAuthInterceptor(client, () => { throw new Error('Cookie-authenticated fixture'); }, []);
  return client;
}

function response(config: InternalAxiosRequestConfig): AxiosResponse {
  return { status: 200, statusText: 'OK', data: { ok: true }, headers: {}, config };
}

function location(path: string) {
  window.history.replaceState({}, '', path);
}

afterEach(() => location('/'));

describe('workspace transport through the shared auth client', () => {
  it.each([
    ['/workspaces/organizations/org-alpha/systems/a', 'organization', 'org-alpha'],
    ['/workspaces/csp/capabilities', 'csp', undefined],
  ])('uses server-verifiable selectors from %s', async (path, kind, tenantId) => {
    // Arrange
    location(path);
    const client = authenticatedClient();
    let captured: InternalAxiosRequestConfig | undefined;
    client.defaults.adapter = async config => {
      captured = config;
      return response(config);
    };

    // Act
    await client.get('/api/auth/me');

    // Assert
    expect(captured?.headers.get('X-Workspace-Kind')).toBe(kind);
    expect(captured?.headers.get('X-Workspace-Tenant-Id')).toBe(tenantId);
    expect(captured?.headers.get('X-Workspace-Mode')).toBe('ordinary');
  });

  it('removes stale selectors from legacy requests instead of assuming membership', async () => {
    // Arrange
    location('/systems/a');
    const client = authenticatedClient();
    client.defaults.adapter = async config => response(config);

    // Act
    const result = await client.get('/api/auth/me', { headers: {
      'X-Workspace-Kind': 'organization',
      'X-Workspace-Tenant-Id': 'old-org',
      'X-Workspace-Mode': 'support',
    } });

    // Assert
    expect(result.config.headers.has('X-Workspace-Kind')).toBe(false);
    expect(result.config.headers.has('X-Workspace-Tenant-Id')).toBe(false);
    expect(result.config.headers.has('X-Workspace-Mode')).toBe(false);
  });

  it('does not let a stale support header turn an ordinary route into support access', async () => {
    // Arrange
    location('/workspaces/organizations/org-alpha');
    const client = authenticatedClient();
    client.defaults.adapter = async config => response(config);

    // Act
    const result = await client.get('/api/auth/me', { headers: { 'X-Workspace-Mode': 'support' } });

    // Assert
    expect(result.config.headers.get('X-Workspace-Mode')).toBe('ordinary');
  });

  it('sends explicit support mode only from a support namespace', async () => {
    // Arrange
    location('/workspaces/support/organizations/org-alpha/systems/a');
    const client = authenticatedClient();
    client.defaults.adapter = async config => response(config);

    // Act
    const result = await client.get('/api/auth/me');

    // Assert
    expect(result.config.headers.get('X-Workspace-Kind')).toBe('organization');
    expect(result.config.headers.get('X-Workspace-Tenant-Id')).toBe('org-alpha');
    expect(result.config.headers.get('X-Workspace-Mode')).toBe('support');
  });

  it('exits expired support through ordinary CSP scope without an organization header', async () => {
    // Arrange
    location('/workspaces/support/organizations/org-alpha');
    const client = authenticatedClient('/api');
    client.defaults.adapter = async config => response(config);

    // Act
    const result = await client.delete('/tenants/impersonation');

    // Assert
    expect(result.config.headers.get('X-Workspace-Kind')).toBe('csp');
    expect(result.config.headers.get('X-Workspace-Mode')).toBe('ordinary');
    expect(result.config.headers.has('X-Workspace-Tenant-Id')).toBe(false);
  });

  it('scopes a separately configured API origin', async () => {
    // Arrange
    location('/workspaces/organizations/org-alpha');
    const client = authenticatedClient('https://backend.example.invalid/api/dashboard');
    client.defaults.adapter = async config => response(config);

    // Act
    const result = await client.get('/systems');

    // Assert
    expect(result.config.headers.get('X-Workspace-Tenant-Id')).toBe('org-alpha');
  });

  it.each(['https://external.example.invalid/api/file', '/static/reference.json'])(
    'does not send workspace metadata to %s',
    async url => {
      // Arrange
      location('/workspaces/organizations/org-alpha');
      const client = authenticatedClient();
      client.defaults.adapter = async config => response(config);

      // Act
      const result = await client.get(url, { headers: { 'X-Workspace-Tenant-Id': 'stale-org' } });

      // Assert
      expect(result.config.headers.has('X-Workspace-Kind')).toBe(false);
      expect(result.config.headers.has('X-Workspace-Tenant-Id')).toBe(false);
      expect(result.config.headers.has('X-Workspace-Mode')).toBe(false);
    },
  );

  it('rejects malformed workspace context before sending an API request', async () => {
    // Arrange
    location('/workspaces/organizations/');
    const client = authenticatedClient();
    let sent = false;
    client.defaults.adapter = async config => { sent = true; return response(config); };

    // Act
    const request = client.get('/api/auth/me');

    // Assert
    await expect(request).rejects.toThrow('Invalid workspace URL');
    expect(sent).toBe(false);
  });

  it.each([false, true])('discards a previous-workspace response (failed=%s)', async failed => {
    // Arrange
    location('/workspaces/organizations/org-alpha');
    const client = authenticatedClient();
    let captured: InternalAxiosRequestConfig | undefined;
    client.defaults.adapter = async config => {
      captured = config;
      location('/workspaces/organizations/org-beta');
      if (failed) throw new AxiosError('Request failed', 'SERVER_ERROR', config, undefined, {
        ...response(config), status: 500,
      });
      return response(config);
    };

    // Act
    const request = client.get('/api/dashboard/systems');

    // Assert
    await expect(request).rejects.toMatchObject({ code: 'ERR_CANCELED' });
    expect(captured?.headers.get('X-Workspace-Tenant-Id')).toBe('org-alpha');
  });

  it('preserves responses while navigating within the same workspace', async () => {
    // Arrange
    location('/workspaces/organizations/org-alpha/systems/a');
    const client = authenticatedClient();
    client.defaults.adapter = async config => {
      location('/workspaces/organizations/org-alpha/systems/a/narratives');
      return response(config);
    };

    // Act
    const result = await client.get('/api/auth/me');

    // Assert
    expect(result.status).toBe(200);
    expect(result.config.headers.get('X-Workspace-Tenant-Id')).toBe('org-alpha');
  });

  it('never retries an old request under the newly selected workspace', async () => {
    // Arrange
    location('/workspaces/organizations/org-alpha');
    const client = authenticatedClient();
    let sent = 0;
    client.defaults.adapter = async config => {
      sent++;
      if (sent === 1) {
        location('/workspaces/organizations/org-beta');
        return client.request(config);
      }
      return response(config);
    };

    // Act
    const request = client.get('/api/auth/me');

    // Assert
    await expect(request).rejects.toMatchObject({ code: 'ERR_CANCELED' });
    expect(sent).toBe(1);
  });

  it('preserves the original network error when workspace did not change', async () => {
    // Arrange
    location('/workspaces/organizations/org-alpha');
    const client = authenticatedClient();
    const error = new AxiosError('Unavailable', 'ERR_NETWORK');
    client.defaults.adapter = async () => { throw error; };

    // Act
    const request = client.get('/api/auth/me');

    // Assert
    await expect(request).rejects.toBe(error);
  });
});
