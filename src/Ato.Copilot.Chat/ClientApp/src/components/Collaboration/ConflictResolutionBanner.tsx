// =============================================================================
// ConflictResolutionBanner.tsx — #1357 Sub-task 3: Section Locking
//
// Shown when the server detects two users claimed the same block simultaneously.
// The local user can choose to keep their version or accept the incumbent's.
//
// The banner is rendered globally (e.g. in ChatWindow) and reads activeConflict
// from CollaborationContext. It dismisses itself on resolution.
// =============================================================================

import React from 'react';
import { useCollaboration } from '../../contexts/CollaborationContext';

export default function ConflictResolutionBanner() {
  const { activeConflict, dismissConflict } = useCollaboration();
  if (!activeConflict) return null;

  const { incumbentUserId, incumbentContent, challengerUserId, challengerContent, blockId } =
    activeConflict;

  return (
    <div
      role="alert"
      aria-live="assertive"
      className="fixed bottom-16 left-1/2 -translate-x-1/2 z-50 w-full max-w-lg
                 bg-white border border-orange-400 rounded-xl shadow-xl p-4 text-sm"
    >
      <div className="flex items-start gap-3">
        <span className="text-orange-500 text-lg mt-0.5" aria-hidden="true">⚠</span>
        <div className="flex-1">
          <p className="font-semibold text-gray-800 mb-1">Edit conflict in block</p>
          <p className="text-gray-500 text-xs mb-3 font-mono truncate">{blockId}</p>

          <div className="grid grid-cols-2 gap-3 mb-3">
            <div className="border border-gray-200 rounded p-2">
              <p className="text-xs text-gray-400 mb-1">{incumbentUserId} (existing)</p>
              <p className="text-gray-700 text-xs line-clamp-3">{incumbentContent || '(empty)'}</p>
            </div>
            <div className="border border-orange-300 rounded p-2 bg-orange-50">
              <p className="text-xs text-gray-400 mb-1">{challengerUserId} (your edit)</p>
              <p className="text-gray-700 text-xs line-clamp-3">{challengerContent || '(empty)'}</p>
            </div>
          </div>

          <div className="flex gap-2 justify-end">
            <button
              className="px-3 py-1 rounded-md border border-gray-300 text-gray-700
                         text-xs hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-gray-400"
              onClick={dismissConflict}
            >
              Keep existing
            </button>
            <button
              className="px-3 py-1 rounded-md bg-blue-600 text-white
                         text-xs hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500"
              onClick={dismissConflict}
            >
              Use my version
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
