// =============================================================================
// RemoteCursor.tsx — #1357 Sub-task 2: Real-time Presence
//
// Renders an overlay pill for a single remote user's cursor position.
// Mount one of these for each entry in the presence map that has a cursor.
//
// Usage:
//   <RemoteCursor userId="…" />
//
// The cursor's viewportY from the SignalR payload is used as the top offset
// inside the editor container. Parent must be position:relative.
// =============================================================================

import React from 'react';
import { useCollaboration } from '../../contexts/CollaborationContext';

interface RemoteCursorProps {
  userId: string;
}

export default function RemoteCursor({ userId }: RemoteCursorProps) {
  const { presence } = useCollaboration();
  const entry = presence.get(userId);

  if (!entry || !entry.cursor || entry.cursor.viewportY == null) return null;

  return (
    <div
      aria-hidden="true"
      style={{
        position: 'absolute',
        top: entry.cursor.viewportY,
        left: 0,
        pointerEvents: 'none',
        display: 'flex',
        alignItems: 'center',
        gap: 4,
        zIndex: 20,
        transform: 'translateY(-50%)',
      }}
    >
      {/* Caret line */}
      <div
        style={{
          width: 2,
          height: 18,
          borderRadius: 1,
          backgroundColor: entry.color,
        }}
      />
      {/* Name pill */}
      <span
        style={{
          backgroundColor: entry.color,
          color: 'white',
          fontSize: 11,
          fontWeight: 600,
          padding: '1px 6px',
          borderRadius: 4,
          whiteSpace: 'nowrap',
          lineHeight: '18px',
        }}
      >
        {entry.displayName}
      </span>
    </div>
  );
}
