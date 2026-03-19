interface SyncIndicatorProps {
  linked: boolean;
  linkedEntityName?: string;
  linkedEntityId?: string;
  lastSyncTimestamp?: string;
  onNavigate?: () => void;
}

export default function SyncIndicator({
  linked,
  linkedEntityName,
  linkedEntityId,
  lastSyncTimestamp,
  onNavigate,
}: SyncIndicatorProps) {
  if (!linked) {
    return (
      <div className="flex items-center gap-1.5 text-xs text-gray-400">
        <span className="inline-block h-2 w-2 rounded-full bg-gray-300" />
        Not linked
      </div>
    );
  }

  return (
    <div className="flex items-center gap-2 text-xs">
      <span className="inline-block h-2 w-2 rounded-full bg-green-500" />
      <span className="text-gray-600">Synced with</span>
      {onNavigate ? (
        <button
          onClick={onNavigate}
          className="font-medium text-indigo-600 hover:underline"
        >
          {linkedEntityName ?? linkedEntityId ?? 'linked entity'}
        </button>
      ) : (
        <span className="font-medium text-gray-900">{linkedEntityName ?? linkedEntityId ?? 'linked entity'}</span>
      )}
      {lastSyncTimestamp && (
        <span className="text-gray-400">· {new Date(lastSyncTimestamp).toLocaleDateString()}</span>
      )}
    </div>
  );
}
