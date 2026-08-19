// =============================================================================
// PresenceAvatars.tsx — #1357 Sub-task 2: Real-time Presence
//
// Renders a compact avatar stack of currently active remote participants.
// Shown in the Header micro-bar (all modes) and suppressed on mobile.
//
// Props:
//   maxVisible — max avatars before "+N more" overflow pill (default 4)
//   size       — "sm" (24px) | "md" (32px)  (default "sm")
// =============================================================================

import React from 'react';
import { useCollaboration } from '../../contexts/CollaborationContext';

interface PresenceAvatarsProps {
  maxVisible?: number;
  size?: 'sm' | 'md';
  className?: string;
}

function initials(displayName: string): string {
  return displayName
    .split(' ')
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? '')
    .join('');
}

export default function PresenceAvatars({
  maxVisible = 4,
  size = 'sm',
  className = '',
}: PresenceAvatarsProps) {
  const { presence } = useCollaboration();

  const active = Array.from(presence.values()).filter((p) => p.status !== 'left');
  if (active.length === 0) return null;

  const visible = active.slice(0, maxVisible);
  const overflow = active.length - visible.length;

  const dim = size === 'sm' ? 24 : 32;
  const textClass = size === 'sm' ? 'text-[10px]' : 'text-xs';

  return (
    <div
      className={`flex items-center ${className}`}
      aria-label={`${active.length} active collaborator${active.length !== 1 ? 's' : ''}`}
      role="group"
    >
      {visible.map((entry, i) => (
        <div
          key={entry.userId}
          title={entry.displayName}
          aria-label={entry.displayName}
          style={{
            width: dim,
            height: dim,
            borderRadius: '50%',
            backgroundColor: entry.color,
            border: '2px solid white',
            marginLeft: i === 0 ? 0 : -(dim / 4),
            zIndex: maxVisible - i,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flexShrink: 0,
            cursor: 'default',
          }}
          className={`${textClass} font-semibold text-white select-none`}
        >
          {initials(entry.displayName)}
        </div>
      ))}

      {overflow > 0 && (
        <div
          style={{
            width: dim,
            height: dim,
            borderRadius: '50%',
            backgroundColor: '#6b7280',
            border: '2px solid white',
            marginLeft: -(dim / 4),
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flexShrink: 0,
          }}
          className={`${textClass} font-semibold text-white select-none`}
          aria-label={`${overflow} more collaborators`}
          title={`${overflow} more`}
        >
          +{overflow}
        </div>
      )}
    </div>
  );
}
