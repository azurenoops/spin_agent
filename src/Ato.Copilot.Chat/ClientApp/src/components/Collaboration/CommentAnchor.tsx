// =============================================================================
// CommentAnchor.tsx — #1357 Sub-task 4: Comment/Annotation System
//
// A small gutter chip rendered next to a block that has open comment threads.
// Clicking it scrolls to / opens the CommentThread panel.
//
// Props:
//   blockId        — the block this anchor belongs to
//   onClick        — called with threadIds when the anchor is clicked
//   focusedMode    — when true the chip is hidden (focused layout AC)
// =============================================================================

import React from 'react';
import { useCollaboration } from '../../contexts/CollaborationContext';

interface CommentAnchorProps {
  blockId: string;
  onClick?: (threadIds: string[]) => void;
  focusedMode?: boolean;
}

export default function CommentAnchor({
  blockId,
  onClick,
  focusedMode = false,
}: CommentAnchorProps) {
  const { threads } = useCollaboration();

  const blockThreads = threads.filter(
    (t) => t.anchorBlockId === blockId && !t.resolved
  );

  if (blockThreads.length === 0 || focusedMode) return null;

  const handleClick = () => {
    onClick?.(blockThreads.map((t) => t.id));
  };

  return (
    <button
      aria-label={`${blockThreads.length} comment${blockThreads.length !== 1 ? 's' : ''}`}
      title={`${blockThreads.length} comment${blockThreads.length !== 1 ? 's' : ''}`}
      onClick={handleClick}
      className="
        inline-flex items-center justify-center
        w-5 h-5 rounded-full
        bg-yellow-400 hover:bg-yellow-500
        text-white font-bold text-[10px]
        focus:outline-none focus:ring-2 focus:ring-yellow-300
        transition-colors
      "
    >
      {blockThreads.length}
    </button>
  );
}
