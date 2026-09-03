import { useState, useEffect, useCallback, useRef } from 'react';
import * as signalR from '@microsoft/signalr';
import apiClient from '../api/client';
import { getMsalInstance, DEFAULT_API_SCOPES } from '../features/auth/msalInstance';

// ─── Types ──────────────────────────────────────────────────────────────────

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

// ─── Hook ───────────────────────────────────────────────────────────────────

export function useNotifications(userId?: string) {
  // DEF-001 R2: Resolve the caller's OID from MSAL. If no authenticated account
  // exists, bail out entirely — do not fall back to a phantom 'dashboard-user'
  // identity. localAccountId is MSAL's projection of the Entra `oid` claim,
  // consistent with the identity used elsewhere in the app.
  const msalAccount = getMsalInstance().getAllAccounts()[0];
  const resolvedUserId = userId ?? msalAccount?.localAccountId ?? null;
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [loading, setLoading] = useState(resolvedUserId !== null);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  // Fetch notifications from API — skip when unauthenticated
  const fetchNotifications = useCallback(async () => {
    if (!resolvedUserId) {
      setLoading(false);
      return;
    }
    try {
      const [listRes, summaryRes] = await Promise.all([
        apiClient.get<{ items: Notification[] }>('/notifications', { params: { userId: resolvedUserId, limit: 50 } }),
        apiClient.get<NotificationSummary>('/notifications/summary', { params: { userId: resolvedUserId } }),
      ]);
      setNotifications(listRes.data.items);
      setUnreadCount(summaryRes.data.unreadCount);
    } catch {
      // Silently fail — notifications are best-effort
    } finally {
      setLoading(false);
    }
  }, [resolvedUserId]);

  // Mark specific notifications as read
  const markAsRead = useCallback(async (notificationIds: string[]) => {
    try {
      await apiClient.post('/notifications/mark-read', { notificationIds });
      setNotifications((prev) =>
        prev.map((n) =>
          notificationIds.includes(n.id)
            ? { ...n, isRead: true, readAt: new Date().toISOString() }
            : n,
        ),
      );
      setUnreadCount((prev) => Math.max(0, prev - notificationIds.length));
    } catch {
      // best-effort
    }
  }, []);

  // Mark all as read
  const markAllAsRead = useCallback(async () => {
    if (!resolvedUserId) return;
    try {
      await apiClient.post('/notifications/mark-all-read', null, { params: { userId: resolvedUserId } });
      setNotifications((prev) =>
        prev.map((n) => ({ ...n, isRead: true, readAt: new Date().toISOString() })),
      );
      setUnreadCount(0);
    } catch {
      // best-effort
    }
  }, [resolvedUserId]);

  // Initial fetch
  useEffect(() => {
    fetchNotifications();
  }, [fetchNotifications]);

  // SignalR real-time connection — skip entirely when unauthenticated.
  // DEF-001 R2: never connect or register without a verified MSAL account.
  useEffect(() => {
    if (!resolvedUserId) return;

    const baseUrl = import.meta.env.VITE_API_BASE_URL?.replace('/api/dashboard', '') || '';
    const hubUrl = `${baseUrl}/hubs/notifications`;

    // Issue #368 — accessTokenFactory wires MSAL bearer into the SignalR
    // WebSocket upgrade handshake (and reconnects). Without it the hub's
    // [Authorize] attribute returns 401 on the negotiate request, silently
    // falling back to the no-op error path and never receiving real-time
    // push events (Feature 051 § 3.3, FR-005).
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: async () => {
          try {
            const msal = getMsalInstance();
            const accounts = msal.getAllAccounts();
            if (!accounts.length) return '';
            const result = await msal.acquireTokenSilent({
              scopes: DEFAULT_API_SCOPES,
              account: accounts[0]!,
            });
            return result.accessToken;
          } catch {
            return '';
          }
        },
      })
      .withAutomaticReconnect()
      .build();

    connection.on('NewNotification', (notification: Notification) => {
      setNotifications((prev) => [notification, ...prev]);
      setUnreadCount((prev) => prev + 1);
    });

    connection.on('UnreadCountUpdated', (data: { unreadCount: number }) => {
      setUnreadCount(data.unreadCount);
    });

    connection.on('NotificationRead', (notificationId: string) => {
      setNotifications((prev) =>
        prev.map((n) =>
          n.id === notificationId
            ? { ...n, isRead: true, readAt: new Date().toISOString() }
            : n,
        ),
      );
    });

    // Issue #544 — guard against the start/stop race: if the component unmounts
    // before start() settles, the cleanup must not call stop() synchronously
    // (that throws "Failed to start the HttpConnection before stop() was called"
    // and triggers withAutomaticReconnect thrash). Instead we:
    //   1. Track cancellation with a flag so RegisterUser is skipped post-unmount.
    //   2. Capture the start() promise and chain stop() off .finally() so stop()
    //      only runs after start() has fully resolved or rejected.
    //   3. Swallow only the benign "already stopped" rejection; surface everything else.
    let cancelled = false;

    const startPromise = connection
      .start()
      .then(() => {
        if (cancelled) return;
        return connection.invoke('RegisterUser', resolvedUserId);
      })
      .catch(() => {
        // SignalR not available — fall back to polling
      });

    connectionRef.current = connection;

    return () => {
      cancelled = true;
      startPromise.finally(() => {
        connection.stop().catch((err: unknown) => {
          if (
            err instanceof Error &&
            err.message.toLowerCase().includes('already stopped')
          ) {
            // Expected when the connection never fully started — safe to ignore.
            return;
          }
          console.error('[useNotifications] SignalR stop error:', err);
        });
      });
    };
  }, [resolvedUserId]);

  return {
    notifications,
    unreadCount,
    loading,
    markAsRead,
    markAllAsRead,
    refresh: fetchNotifications,
  };
}
