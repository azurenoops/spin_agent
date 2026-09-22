import { useState, useEffect, useCallback, useRef } from 'react';
import * as signalR from '@microsoft/signalr';
import apiClient from '../api/client';
import { getMsalInstance, DEFAULT_API_SCOPES } from '../features/auth/msalInstance';
import { useMe } from '../features/auth/useMe';
import { workspaceHubUrl } from '../features/workspaces/workspaceHubUrl';
import { msalAccountKey, selectMsalAccount } from '../features/auth/accountSelection';

export interface Notification {
  id: string;
  alertId: string;
  channel: string;
  subject: string | null;
  body: string | null;
  isRead: boolean;
  readAt: string | null;
  sentAt: string;
  alertTitle: string | null;
  alertSeverity: string | null;
}

export interface NotificationSummary {
  unreadCount: number;
  totalCount: number;
}

interface NotificationCapabilities {
  recipientId: string | null;
  rest: { available: boolean; reasonCode: string | null };
  realtime: { available: boolean; reasonCode: string | null };
  fallback: { transport: 'rest-polling' | 'none'; pollIntervalSeconds: number | null };
}

interface NotificationState {
  key: string;
  notifications: Notification[];
  unreadCount: number;
  loading: boolean;
  error: string | null;
  transportMessage: string | null;
}

function notificationError(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (error && typeof error === 'object' && 'error' in error) {
    const detail = error.error;
    if (detail && typeof detail === 'object' && 'message' in detail && typeof detail.message === 'string') {
      return detail.message;
    }
  }
  return 'Unable to load or update notifications. Please retry.';
}

export function useNotifications(userId?: string) {
  const { data: identity, isLoading, error: identityError } = useMe();
  const actor = !isLoading && !identityError ? identity?.oid : null;
  const accountKey = msalAccountKey(getMsalInstance());
  const key = JSON.stringify([
    actor, identity?.directoryTenantId, identity?.workspace?.kind, identity?.workspace?.tenantId,
    identity?.workspace?.mode, identity?.workspace?.personId, identity?.effectiveTenant?.id, accountKey, userId,
  ]);
  const currentKey = useRef(key);
  currentKey.current = key;
  const empty: NotificationState = {
    key, notifications: [], unreadCount: 0, loading: Boolean(actor),
    error: null, transportMessage: null,
  };
  const [state, setState] = useState<NotificationState>(empty);
  const actions = useRef<{
    key: string;
    refresh: () => Promise<void>;
    mark: (ids?: string[]) => Promise<void>;
  } | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    const current = () => !controller.signal.aborted && currentKey.current === key;
    let capabilities: NotificationCapabilities | null = null;
    let connection: signalR.HubConnection | null = null;
    let starting: Promise<void> = Promise.resolve();
    let timer: ReturnType<typeof setTimeout> | undefined;
    let inFlight: Promise<void> | null = null;
    let pollMs = 0;
    let realtimeConnected = false;
    setState({ key, notifications: [], unreadCount: 0, loading: Boolean(actor), error: null, transportMessage: null });

    const update = (change: Partial<NotificationState>) => {
      if (current()) setState(previous => ({ ...previous, ...change, key }));
    };
    const schedulePolling = () => {
      clearTimeout(timer);
      if (current() && pollMs && !realtimeConnected) {
        timer = setTimeout(() => { void refresh(); }, pollMs);
      }
    };
    const stopConnection = () => {
      const old = connection;
      connection = null;
      realtimeConnected = false;
      if (old) void starting.then(() => old.stop()).catch(error => {
        console.error('[useNotifications] Unable to stop notification connection:', error);
      });
    };
    const connect = () => {
      if (connection || !current()) return;
      if (!accountKey) throw new Error('A matching bearer identity is required for real-time notifications.');
      const baseUrl = import.meta.env.VITE_API_BASE_URL?.replace('/api/dashboard', '') || '';
      const hub = new signalR.HubConnectionBuilder()
        .withUrl(workspaceHubUrl(`${baseUrl}/hubs/notifications`), {
          accessTokenFactory: async () => {
            const msal = getMsalInstance();
            const account = selectMsalAccount(msal);
            if (!current() || !account || msalAccountKey(msal) !== accountKey) {
              throw new Error('Notification identity changed. Reopen the authorized workspace.');
            }
            const token = await msal.acquireTokenSilent({ scopes: DEFAULT_API_SCOPES, account });
            if (!current() || msalAccountKey(msal) !== accountKey) {
              throw new Error('Notification identity changed during token acquisition.');
            }
            return token.accessToken;
          },
        })
        .withAutomaticReconnect()
        .build();
      connection = hub;
      for (const event of ['NewNotification', 'UnreadCountUpdated', 'NotificationRead']) {
        hub.on(event, () => { if (current() && connection === hub) void refresh(); });
      }
      hub.onreconnecting(() => {
        if (connection === hub) {
          realtimeConnected = false;
          schedulePolling();
          update({ transportMessage: 'Real-time notifications reconnecting.' });
        }
      });
      hub.onreconnected(async () => {
        if (!current() || connection !== hub) return;
        try {
          await refresh();
          if (current() && capabilities?.realtime.available && connection === hub) {
            await hub.invoke('RegisterUser', capabilities.recipientId);
            realtimeConnected = true;
            schedulePolling();
            update({ transportMessage: null });
          }
        } catch (error) {
          update({
            error: notificationError(error),
            transportMessage: 'Real-time notifications unavailable.'
              + (pollMs ? ' REST polling remains available.' : ' Retry to reconnect.'),
          });
          stopConnection();
          schedulePolling();
        }
      });
      hub.onclose(() => {
        if (connection === hub) {
          connection = null;
          realtimeConnected = false;
          schedulePolling();
          update({ transportMessage: 'Real-time notifications disconnected.'
            + (pollMs ? ' REST polling remains available.' : ' Retry to reconnect.') });
        }
      });
      starting = hub.start().then(async () => {
        if (current() && connection === hub) {
          await hub.invoke('RegisterUser', actor);
          if (current() && connection === hub) {
            realtimeConnected = true;
            schedulePolling();
          }
        }
      }).catch(error => {
        update({
          error: notificationError(error),
          transportMessage: 'Real-time notifications unavailable.'
            + (pollMs ? ' REST polling remains available.' : ' Retry to reconnect.'),
        });
        stopConnection();
        schedulePolling();
      });
    };
    const refresh = (): Promise<void> => {
      if (!current() || !actor) return Promise.resolve();
      if (inFlight) return inFlight;
      clearTimeout(timer);
      inFlight = Promise.resolve().then(async () => {
        try {
          if (userId && userId !== actor) throw new Error('Notification identity does not match the authenticated session.');
          const response = await apiClient.get<NotificationCapabilities>('/notifications/capabilities', { signal: controller.signal });
          if (!current()) return;
          const next = response.data;
          if (typeof next?.rest?.available !== 'boolean' || typeof next?.realtime?.available !== 'boolean'
            || !next.fallback || !['rest-polling', 'none'].includes(next.fallback.transport)) {
            throw new Error('Unexpected notification capabilities response.');
          }
          capabilities = next;
          if (!next.rest.available) {
            pollMs = 0;
            stopConnection();
            update({
              notifications: [], unreadCount: 0, error: null,
              transportMessage: next.rest.reasonCode === 'ORGANIZATION_WORKSPACE_REQUIRED'
                ? 'Choose an organization workspace to view personal notifications.'
                : 'Notifications are unavailable for this workspace identity.',
            });
            return;
          }
          if (next.recipientId !== actor) throw new Error('Notification identity does not match the authenticated session.');
          const seconds = next.fallback.pollIntervalSeconds;
          if (next.fallback.transport === 'rest-polling') {
            if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds <= 0) {
              throw new Error('Unexpected notification polling interval.');
            }
            pollMs = seconds * 1000;
          } else pollMs = 0;
          const [list, summary] = await Promise.all([
            apiClient.get<{ items: Notification[] }>('/notifications', { params: { limit: 50 }, signal: controller.signal }),
            apiClient.get<NotificationSummary>('/notifications/summary', { signal: controller.signal }),
          ]);
          if (!current()) return;
          update({
            notifications: list.data.items, unreadCount: summary.data.unreadCount, error: null,
            transportMessage: next.realtime.available ? null
              : `Real-time notifications unavailable (${next.realtime.reasonCode ?? 'unsupported session'}).`
                + (pollMs ? ` Checking for updates every ${seconds} seconds.` : ''),
          });
          if (next.realtime.available) connect();
          else stopConnection();
        } catch (error) {
          if (!current()) return;
          capabilities = null;
          pollMs = 0;
          stopConnection();
          update({ notifications: [], unreadCount: 0, error: notificationError(error), transportMessage: null });
        } finally {
          update({ loading: false });
          inFlight = null;
          schedulePolling();
        }
      });
      return inFlight;
    };
    const mark = async (ids?: string[]) => {
      if (!current()) return;
      if (!capabilities?.rest.available) {
        update({ error: 'Notification access has not been authorized. Refresh and try again.' });
        return;
      }
      try {
        await apiClient.post(ids ? '/notifications/mark-read' : '/notifications/mark-all-read',
          ids ? { notificationIds: ids } : null, { signal: controller.signal });
        await inFlight;
        if (current()) await refresh();
      } catch (error) {
        if (current()) update({ error: notificationError(error) });
      }
    };
    actions.current = { key, refresh, mark };
    void refresh();
    return () => {
      controller.abort();
      clearTimeout(timer);
      stopConnection();
      if (actions.current?.key === key) actions.current = null;
    };
  }, [key, actor, accountKey, userId]);

  const refresh = useCallback(async () => {
    if (actions.current?.key === key) await actions.current.refresh();
  }, [key]);
  const markAsRead = useCallback(async (ids: string[]) => {
    if (actions.current?.key === key) await actions.current.mark(ids);
  }, [key]);
  const markAllAsRead = useCallback(async () => {
    if (actions.current?.key === key) await actions.current.mark();
  }, [key]);
  return { ...(state.key === key ? state : empty), refresh, markAsRead, markAllAsRead };
}
