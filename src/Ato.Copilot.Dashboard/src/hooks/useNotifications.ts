import { useState, useEffect, useCallback, useRef } from 'react';
import * as signalR from '@microsoft/signalr';
import apiClient from '../api/client';

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

export function useNotifications(userId: string = 'dashboard-user') {
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  // Fetch notifications from API
  const fetchNotifications = useCallback(async () => {
    try {
      const [listRes, summaryRes] = await Promise.all([
        apiClient.get<{ items: Notification[] }>('/notifications', { params: { userId, limit: 50 } }),
        apiClient.get<NotificationSummary>('/notifications/summary', { params: { userId } }),
      ]);
      setNotifications(listRes.data.items);
      setUnreadCount(summaryRes.data.unreadCount);
    } catch {
      // Silently fail — notifications are best-effort
    } finally {
      setLoading(false);
    }
  }, [userId]);

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
    try {
      await apiClient.post('/notifications/mark-all-read', null, { params: { userId } });
      setNotifications((prev) =>
        prev.map((n) => ({ ...n, isRead: true, readAt: new Date().toISOString() })),
      );
      setUnreadCount(0);
    } catch {
      // best-effort
    }
  }, [userId]);

  // Initial fetch
  useEffect(() => {
    fetchNotifications();
  }, [fetchNotifications]);

  // SignalR real-time connection
  useEffect(() => {
    const baseUrl = import.meta.env.VITE_API_BASE_URL?.replace('/api/dashboard', '') || '';
    const hubUrl = `${baseUrl}/hubs/notifications`;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
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

    connection
      .start()
      .then(() => connection.invoke('RegisterUser', userId))
      .catch(() => {
        // SignalR not available — fall back to polling
      });

    connectionRef.current = connection;

    return () => {
      connection.stop();
    };
  }, [userId]);

  return {
    notifications,
    unreadCount,
    loading,
    markAsRead,
    markAllAsRead,
    refresh: fetchNotifications,
  };
}
