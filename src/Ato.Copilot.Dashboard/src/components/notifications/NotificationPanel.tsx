import { useNotifications, type Notification } from '../../hooks/useNotifications';

interface NotificationPanelProps {
  onClose: () => void;
}

function severityColor(severity: string | null): string {
  switch (severity?.toLowerCase()) {
    case 'critical':
      return 'bg-red-100 text-red-800';
    case 'high':
      return 'bg-orange-100 text-orange-800';
    case 'medium':
      return 'bg-yellow-100 text-yellow-800';
    case 'low':
      return 'bg-blue-100 text-blue-800';
    default:
      return 'bg-gray-100 text-gray-700';
  }
}

function timeAgo(dateStr: string): string {
  const diff = Date.now() - new Date(dateStr).getTime();
  const minutes = Math.floor(diff / 60000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

function NotificationItem({
  notification,
  onMarkRead,
}: {
  notification: Notification;
  onMarkRead: (id: string) => void;
}) {
  return (
    <div
      className={`flex gap-3 px-4 py-3 transition-colors ${
        notification.isRead ? 'bg-white' : 'bg-blue-50/60'
      } hover:bg-gray-50`}
    >
      {/* Unread dot */}
      <div className="mt-1.5 flex-shrink-0">
        {!notification.isRead ? (
          <span className="block h-2 w-2 rounded-full bg-blue-500" />
        ) : (
          <span className="block h-2 w-2" />
        )}
      </div>

      {/* Content */}
      <div className="min-w-0 flex-1">
        <div className="flex items-start justify-between gap-2">
          <p className={`text-sm leading-snug ${notification.isRead ? 'text-gray-600' : 'font-medium text-gray-900'}`}>
            {notification.subject ?? notification.alertTitle ?? 'Notification'}
          </p>
          {notification.alertSeverity && (
            <span className={`flex-shrink-0 rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase ${severityColor(notification.alertSeverity)}`}>
              {notification.alertSeverity}
            </span>
          )}
        </div>
        <p className="mt-0.5 text-xs text-gray-500 line-clamp-2">
          {notification.alertTitle && notification.subject ? notification.alertTitle : ''}
        </p>
        <div className="mt-1 flex items-center gap-2">
          <span className="text-[11px] text-gray-400">{timeAgo(notification.sentAt)}</span>
          {!notification.isRead && (
            <button
              type="button"
              onClick={() => onMarkRead(notification.id)}
              className="text-[11px] font-medium text-blue-600 hover:text-blue-800"
            >
              Mark read
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

export default function NotificationPanel({ onClose }: NotificationPanelProps) {
  const { notifications, unreadCount, loading, markAsRead, markAllAsRead } = useNotifications();

  return (
    <div className="absolute right-0 top-full z-50 mt-1 w-96 overflow-hidden rounded-lg border border-gray-200 bg-white shadow-lg">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-gray-200 px-4 py-3">
        <div className="flex items-center gap-2">
          <h3 className="text-sm font-semibold text-gray-900">Notifications</h3>
          {unreadCount > 0 && (
            <span className="rounded-full bg-blue-100 px-2 py-0.5 text-xs font-medium text-blue-700">
              {unreadCount}
            </span>
          )}
        </div>
        <div className="flex items-center gap-1">
          {unreadCount > 0 && (
            <button
              type="button"
              onClick={markAllAsRead}
              className="rounded px-2 py-1 text-xs font-medium text-blue-600 hover:bg-blue-50"
            >
              Mark all read
            </button>
          )}
          <button
            type="button"
            onClick={onClose}
            className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600"
            aria-label="Close notifications"
          >
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
      </div>

      {/* Notification list */}
      <div className="max-h-96 overflow-y-auto divide-y divide-gray-100">
        {loading ? (
          <div className="px-4 py-8 text-center text-sm text-gray-400">Loading...</div>
        ) : notifications.length === 0 ? (
          <div className="px-4 py-8 text-center">
            <svg className="mx-auto h-8 w-8 text-gray-300" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M14.857 17.082a23.848 23.848 0 0 0 5.454-1.31A8.967 8.967 0 0 1 18 9.75V9A6 6 0 0 0 6 9v.75a8.967 8.967 0 0 1-2.312 6.022c1.733.64 3.56 1.085 5.455 1.31m5.714 0a24.255 24.255 0 0 1-5.714 0m5.714 0a3 3 0 1 1-5.714 0" />
            </svg>
            <p className="mt-2 text-sm text-gray-500">No notifications</p>
          </div>
        ) : (
          notifications.map((n) => (
            <NotificationItem
              key={n.id}
              notification={n}
              onMarkRead={(id) => markAsRead([id])}
            />
          ))
        )}
      </div>
    </div>
  );
}
