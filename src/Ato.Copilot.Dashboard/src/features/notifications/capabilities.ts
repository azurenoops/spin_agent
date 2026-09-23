import apiClient from '../../api/client';

export interface NotificationCapabilities {
  recipientId: string;
  rest: { available: boolean; reasonCode: string | null };
  realtime: {
    available: boolean;
    authentication: 'bearer';
    cookieSessionSupported: boolean;
    reasonCode: string | null;
    hubPaths: string[];
  };
  fallback:
    | { transport: 'rest-polling'; pollIntervalSeconds: number }
    | { transport: 'none'; pollIntervalSeconds: null };
}

function record(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function invalid(): never {
  throw new Error('Unexpected notification capabilities response.');
}

function reasonCode(value: unknown): string | null {
  if (value !== null && typeof value !== 'string') invalid();
  return value;
}

export function parseNotificationCapabilities(value: unknown): NotificationCapabilities {
  if (!record(value) || typeof value.recipientId !== 'string' || !value.recipientId.trim()
    || !record(value.rest) || !record(value.realtime) || !record(value.fallback)) invalid();
  const { rest, realtime, fallback } = value;
  if (typeof rest.available !== 'boolean' || typeof realtime.available !== 'boolean'
    || realtime.authentication !== 'bearer' || typeof realtime.cookieSessionSupported !== 'boolean'
    || !Array.isArray(realtime.hubPaths)
    || !realtime.hubPaths.every((path): path is string => typeof path === 'string' && path.startsWith('/hubs/'))
    || (fallback.transport !== 'rest-polling' && fallback.transport !== 'none')) invalid();
  let parsedFallback: NotificationCapabilities['fallback'];
  if (fallback.transport === 'rest-polling') {
    const interval = fallback.pollIntervalSeconds;
    if (typeof interval !== 'number' || !Number.isSafeInteger(interval) || interval <= 0 || interval > 2_147_483) invalid();
    parsedFallback = { transport: 'rest-polling', pollIntervalSeconds: interval };
  } else {
    if (fallback.pollIntervalSeconds !== null) invalid();
    parsedFallback = { transport: 'none', pollIntervalSeconds: null };
  }
  return {
    recipientId: value.recipientId,
    rest: { available: rest.available, reasonCode: reasonCode(rest.reasonCode) },
    realtime: {
      available: realtime.available, authentication: realtime.authentication,
      cookieSessionSupported: realtime.cookieSessionSupported, reasonCode: reasonCode(realtime.reasonCode),
      hubPaths: [...realtime.hubPaths],
    },
    fallback: parsedFallback,
  };
}

export async function getNotificationCapabilities(signal?: AbortSignal): Promise<NotificationCapabilities> {
  const response = await apiClient.get<unknown>('/notifications/capabilities', { signal });
  return parseNotificationCapabilities(response.data);
}
