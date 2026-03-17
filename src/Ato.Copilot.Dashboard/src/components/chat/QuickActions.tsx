import { useMemo } from 'react';
import type { ChatContext } from '../../types/chat';
import { getIntelligentSuggestions, getPhaseColors } from './phasePageSuggestions';

export interface QuickActionsProps {
  context: ChatContext;
  onSend: (prompt: string) => void;
  disabled: boolean;
}

/**
 * Always-visible strip above chat input showing 2-3 phase+page-aware
 * quick-action pills. Updates dynamically on route and phase changes.
 */
export default function QuickActions({ context, onSend, disabled }: QuickActionsProps) {
  const suggestions = useMemo(() => getIntelligentSuggestions(context), [context]);
  const colors = getPhaseColors(context.rmfPhase);

  if (suggestions.length === 0) return null;

  // Show top 3 as compact pills
  const visible = suggestions.slice(0, 3);

  return (
    <div className="border-t border-gray-100 bg-gray-50/80 px-3 py-2">
      {context.rmfPhase && (
        <div className="mb-1.5 flex items-center gap-1.5">
          <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold ring-1 ring-inset ${colors.bg} ${colors.text} ${colors.ring}`}>
            {context.rmfPhase}
          </span>
          <span className="text-[10px] text-gray-400">phase</span>
        </div>
      )}
      <div className="flex gap-1.5 overflow-x-auto">
        {visible.map((s) => (
          <button
            key={s.prompt}
            type="button"
            onClick={() => onSend(s.prompt)}
            disabled={disabled}
            className="flex-shrink-0 rounded-lg border border-gray-200 bg-white px-2.5 py-1.5 text-[11px] leading-tight text-gray-700 shadow-sm hover:border-blue-300 hover:bg-blue-50 hover:text-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {s.icon && <span className="mr-1">{s.icon}</span>}
            {s.label}
          </button>
        ))}
      </div>
    </div>
  );
}
