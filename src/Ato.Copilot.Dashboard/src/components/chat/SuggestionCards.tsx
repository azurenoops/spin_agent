import type { SuggestedAction } from '../../types/chat';

export interface SuggestionCardsProps {
  suggestions: SuggestedAction[];
  onSelect: (prompt: string) => void;
  disabled: boolean;
}

export default function SuggestionCards({ suggestions, onSelect, disabled }: SuggestionCardsProps) {
  if (suggestions.length === 0) return null;

  return (
    <div className="mt-2 flex gap-2 overflow-x-auto pb-1">
      {suggestions.map((suggestion) => (
        <button
          key={suggestion.prompt}
          type="button"
          onClick={() => onSelect(suggestion.prompt)}
          disabled={disabled}
          className="flex-shrink-0 rounded-full border border-indigo-200 bg-indigo-50 px-3 py-1 text-xs text-indigo-700 hover:bg-indigo-100 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          {suggestion.icon && <span className="mr-1">{suggestion.icon}</span>}
          {suggestion.label}
        </button>
      ))}
    </div>
  );
}
