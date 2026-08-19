// =============================================================================
// MobileCollaborationBanner.tsx — #1357
//
// Shown on viewports < 768 px to inform the user that editing is disabled
// on mobile. View and comment threads remain readable.
// =============================================================================

import React from 'react';

interface MobileCollaborationBannerProps {
  viewportWidth: number;
}

export default function MobileCollaborationBanner({
  viewportWidth,
}: MobileCollaborationBannerProps) {
  if (viewportWidth >= 768) return null;

  return (
    <div
      role="status"
      aria-live="polite"
      className="px-4 py-2 bg-amber-50 border-b border-amber-200
                 text-amber-800 text-xs text-center"
    >
      Editing is disabled on small screens. Switch to a larger device to make changes.
    </div>
  );
}
