import { beforeEach, describe, expect, it, vi } from 'vitest';
import apiClient from '../../api/client';
import { getNotificationCapabilities, parseNotificationCapabilities } from '../../features/notifications/capabilities';

vi.mock('../../api/client', () => ({ default: { get: vi.fn() } }));

function response() {
  return {
    recipientId: 'synthetic-actor',
    rest: { available: true, reasonCode: null },
    realtime: {
      available: false, authentication: 'bearer', cookieSessionSupported: false,
      reasonCode: 'REALTIME_BEARER_REQUIRED', hubPaths: ['/hubs/notifications', '/hubs/package', '/hubs/import-progress'],
    },
    fallback: { transport: 'rest-polling', pollIntervalSeconds: 30 },
  };
}

beforeEach(() => vi.resetAllMocks());

describe('notification capability contract', () => {
  it('preserves the server-bound cookie-session capability without synthesizing a bearer grant', () => {
    // Arrange
    const body = response();
    // Act
    const capability = parseNotificationCapabilities(body);
    // Assert
    expect(capability).toEqual(body);
    expect(capability.realtime.available).toBe(false);
    expect(capability.fallback.pollIntervalSeconds).toBe(30);
  });

  it('keeps notification REST availability separate from realtime resource authorization', () => {
    // Arrange
    const body = { ...response(),
      rest: { available: false, reasonCode: 'ORGANIZATION_WORKSPACE_REQUIRED' },
      realtime: { ...response().realtime, available: true, reasonCode: null },
      fallback: { transport: 'none', pollIntervalSeconds: null },
    };
    // Act
    const capability = parseNotificationCapabilities(body);
    // Assert
    expect(capability.rest.available).toBe(false);
    expect(capability.realtime.available).toBe(true);
    expect(capability.fallback.transport).toBe('none');
  });

  it.each([
    null,
    {},
    { ...response(), recipientId: '' },
    { ...response(), rest: { available: 'true', reasonCode: null } },
    { ...response(), realtime: { ...response().realtime, available: 'true' } },
    { ...response(), realtime: { ...response().realtime, authentication: 'cookie' } },
    { ...response(), realtime: { ...response().realtime, cookieSessionSupported: undefined } },
    { ...response(), realtime: { ...response().realtime, hubPaths: [123] } },
    { ...response(), realtime: { ...response().realtime, reasonCode: {} } },
    { ...response(), fallback: { transport: 'rest-polling', pollIntervalSeconds: 0 } },
    { ...response(), fallback: { transport: 'rest-polling', pollIntervalSeconds: Infinity } },
    { ...response(), fallback: { transport: 'rest-polling', pollIntervalSeconds: -1 } },
    { ...response(), fallback: { transport: 'none', pollIntervalSeconds: 30 } },
    { ...response(), fallback: { transport: 'unknown', pollIntervalSeconds: 30 } },
  ])('rejects malformed capability responses instead of using success defaults', body => {
    // Arrange
    const malformed = body;
    // Act
    const parse = () => parseNotificationCapabilities(malformed);
    // Assert
    expect(parse).toThrow('Unexpected notification capabilities response.');
  });

  it('uses the normal API client and forwards request cancellation', async () => {
    // Arrange
    const controller = new AbortController();
    vi.mocked(apiClient.get).mockResolvedValue({ data: response() });
    // Act
    const capability = await getNotificationCapabilities(controller.signal);
    // Assert
    expect(apiClient.get).toHaveBeenCalledExactlyOnceWith('/notifications/capabilities', { signal: controller.signal });
    expect(capability.recipientId).toBe('synthetic-actor');
  });

  it('propagates server errors rather than returning an unavailable-looking default', async () => {
    // Arrange
    const error = new Error('Workspace access denied.');
    vi.mocked(apiClient.get).mockRejectedValue(error);
    // Act
    const request = getNotificationCapabilities();
    // Assert
    await expect(request).rejects.toBe(error);
  });
});
