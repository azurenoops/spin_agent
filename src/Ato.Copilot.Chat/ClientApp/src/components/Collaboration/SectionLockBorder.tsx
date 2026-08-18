// =============================================================================
// SectionLockBorder.tsx — #1357 Sub-task 3: Section Locking
//
// Wraps a block/section with a coloured left-border + name chip when it is
// locked by a remote user, or with a neutral "editing" indicator when locked
// by the local user.
//
// Usage:
//   <SectionLockBorder blockId="block-42" localUserId="me">
//     {children}
//   </SectionLockBorder>
// =============================================================================

import React from 'react';
import { useCollaboration } from '../../contexts/CollaborationContext';

interface SectionLockBorderProps {
  blockId: string;
  localUserId: string;
  children: React.ReactNode;
}

export default function SectionLockBorder({
  blockId,
  localUserId,
  children,
}: SectionLockBorderProps) {
  const { sectionLocks } = useCollaboration();
  const lock = sectionLocks.get(blockId);

  if (!lock) {
    return <>{children}</>;
  }

  const isLocal = lock.userId === localUserId;

  return (
    <div
      style={{
        position: 'relative',
        borderLeft: `3px solid ${isLocal ? '#9ca3af' : lock.color}`,
        paddingLeft: 8,
      }}
      aria-label={
        isLocal
          ? 'You are editing this section'
          : `${lock.displayName} is editing this section`
      }
    >
      {/* Lock chip — only for remote locks */}
      {!isLocal && (
        <span
          aria-hidden="true"
          style={{
            position: 'absolute',
            top: 0,
            right: 0,
            backgroundColor: lock.color,
            color: 'white',
            fontSize: 10,
            fontWeight: 600,
            padding: '1px 6px',
            borderRadius: '0 0 0 4px',
            lineHeight: '16px',
            whiteSpace: 'nowrap',
            pointerEvents: 'none',
          }}
        >
          {lock.displayName}
        </span>
      )}
      {children}
    </div>
  );
}
