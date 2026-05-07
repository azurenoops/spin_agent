import type { SseProgressEvent } from '../../types/chat';

export interface ProgressStepsProps {
  steps: SseProgressEvent[];
}

export default function ProgressSteps({ steps }: ProgressStepsProps) {
  if (steps.length === 0) return null;

  return (
    <div className="space-y-1.5 py-2">
      {steps.map((step, index) => {
        const isLatest = index === steps.length - 1;
        return (
          <div key={index} className="flex items-start gap-2 text-sm">
            <div className="mt-0.5 flex-shrink-0">
              {isLatest ? (
                <div className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-indigo-500 border-t-transparent" />
              ) : (
                <svg className="h-3.5 w-3.5 text-green-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
                </svg>
              )}
            </div>
            <div className={isLatest ? 'text-gray-700' : 'text-gray-400'}>
              <span className="font-medium">{step.step}</span>
              {step.detail && <span className="ml-1 text-gray-400">— {step.detail}</span>}
            </div>
          </div>
        );
      })}
    </div>
  );
}
