import type { ChatContext } from '../../types/chat';
import { getIntelligentSuggestions, getPhaseGuidance, getPhaseColors, getNextPhase } from './phasePageSuggestions';

export interface WelcomeMessageProps {
  context?: ChatContext | null;
  onSendExample: (message: string) => void;
}

export default function WelcomeMessage({ context, onSendExample }: WelcomeMessageProps) {
  const defaultContext: ChatContext = { page: 'portfolio' };
  const effectiveContext = context ?? defaultContext;
  const suggestions = getIntelligentSuggestions(effectiveContext);
  const phase = effectiveContext.rmfPhase;
  const guidance = getPhaseGuidance(phase);
  const colors = getPhaseColors(phase);
  const nextPhase = getNextPhase(phase);

  return (
    <div className="flex flex-1 flex-col items-center justify-center p-6">
      <div className="mb-4 rounded-full bg-blue-50 p-3">
        <svg className="h-8 w-8 text-blue-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M7.5 8.25h9m-9 3H12m-9.75 1.51c0 1.6 1.123 2.994 2.707 3.227 1.087.16 2.185.283 3.293.369V21l4.076-4.076a1.526 1.526 0 011.037-.443 48.282 48.282 0 005.68-.494c1.584-.233 2.707-1.626 2.707-3.228V6.741c0-1.602-1.123-2.995-2.707-3.228A48.394 48.394 0 0012 3c-2.392 0-4.744.175-7.043.513C3.373 3.746 2.25 5.14 2.25 6.741v6.018z" />
        </svg>
      </div>
      <h3 className="text-sm font-semibold text-gray-800">ATO Copilot</h3>

      {/* Phase badge + guidance */}
      {phase ? (
        <div className="mt-3 w-full rounded-lg border border-gray-100 bg-white p-3 shadow-sm">
          <div className="flex items-center gap-2">
            <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold ring-1 ring-inset ${colors.bg} ${colors.text} ${colors.ring}`}>
              {phase}
            </span>
            {context?.systemName && (
              <span className="truncate text-xs text-gray-500">{context.systemName}</span>
            )}
          </div>
          <p className="mt-2 text-xs leading-relaxed text-gray-500">{guidance}</p>
          {nextPhase && (
            <p className="mt-1 text-[10px] text-gray-400">
              Next phase: <span className="font-medium text-gray-600">{nextPhase}</span>
            </p>
          )}
        </div>
      ) : (
        <p className="mt-1 text-center text-xs text-gray-400">
          Your AI assistant for compliance, authorization, and security management.
        </p>
      )}

      {/* Intelligent suggestions */}
      <div className="mt-4 w-full space-y-2">
        <p className="text-[10px] font-medium uppercase tracking-wide text-gray-400">
          {phase ? 'Recommended next steps' : 'Get started'}
        </p>
        {suggestions.map((s) => (
          <button
            key={s.prompt}
            type="button"
            onClick={() => onSendExample(s.prompt)}
            className="flex w-full items-center gap-2 rounded-lg border border-gray-200 px-3 py-2 text-left text-sm text-gray-600 hover:border-blue-300 hover:bg-blue-50 hover:text-blue-700 transition-colors"
          >
            {s.icon && <span className="flex-shrink-0 text-base">{s.icon}</span>}
            <span>{s.label}</span>
          </button>
        ))}
      </div>
    </div>
  );
}
