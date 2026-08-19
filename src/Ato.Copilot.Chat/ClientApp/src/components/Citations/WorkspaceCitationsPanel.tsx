// =============================================================================
// WorkspaceCitationsPanel.tsx — #1703
//
// Thin strip that sits below the Header in standard / research modes.
// Renders the active citation style chip + CitationStylePicker trigger.
// Hidden in focused mode (Header takes over with icon-only chip).
// =============================================================================

import React from 'react';
import CitationStylePicker from './CitationStylePicker';
import { useCitation } from '../../contexts/CitationContext';
import type { LayoutMode } from '../../contexts/EditorLayoutContext';

interface WorkspaceCitationsPanelProps {
  mode?: LayoutMode;
}

export default function WorkspaceCitationsPanel({ mode }: WorkspaceCitationsPanelProps) {
  const { selectedStyle } = useCitation();

  // Hidden in focused mode
  if (mode === 'focused') return null;

  return (
    <div
      className="flex items-center gap-2 px-4 py-1.5 bg-gray-50 border-b border-gray-100 text-xs text-gray-500"
      role="region"
      aria-label="Citation style panel"
    >
      <span className="hidden sm:inline">Citation style:</span>
      <CitationStylePicker
        className="text-xs"
      />
      <span className="hidden md:inline text-gray-400">·</span>
      <span className="hidden md:inline text-gray-400 truncate max-w-xs" aria-label="Selected discipline">
        {selectedStyle.discipline}
      </span>
    </div>
  );
}
