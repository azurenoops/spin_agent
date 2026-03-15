import type { RecentActivity } from '../../types/dashboard';

interface ActivityFeedProps {
  activities: RecentActivity[];
}

const eventIcons: Record<string, string> = {
  AssessmentCompleted: '📋',
  NarrativeUpdated: '📝',
  ScanImported: '📥',
  AuthorizationDecision: '✅',
  CapabilityChanged: '🛡️',
  CapabilityDeleted: '🗑️',
  ComponentRemoved: '⚠️',
};

function formatTimestamp(ts: string): string {
  const date = new Date(ts);
  const now = new Date();
  const diff = now.getTime() - date.getTime();
  const minutes = Math.floor(diff / 60000);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

export default function ActivityFeed({ activities }: ActivityFeedProps) {
  if (activities.length === 0) {
    return <p className="py-4 text-center text-sm text-gray-400">No recent activity</p>;
  }

  return (
    <ul className="divide-y divide-gray-100">
      {activities.map((activity) => (
        <li key={activity.id} className="flex items-start gap-3 py-3">
          <span className="text-lg">{eventIcons[activity.eventType] ?? '📌'}</span>
          <div className="flex-1">
            <p className="text-sm text-gray-900">{activity.summary}</p>
            <p className="text-xs text-gray-400">
              {activity.actor} · {formatTimestamp(activity.timestamp)}
            </p>
          </div>
        </li>
      ))}
    </ul>
  );
}
