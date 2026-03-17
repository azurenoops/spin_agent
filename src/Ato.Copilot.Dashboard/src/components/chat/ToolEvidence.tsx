import { useState } from 'react';
import type { ToolExecution } from '../../types/chat';

export interface ToolEvidenceProps {
  tools: ToolExecution[];
}

export default function ToolEvidence({ tools }: ToolEvidenceProps) {
  const [expandedIndex, setExpandedIndex] = useState<number | null>(null);

  if (tools.length === 0) return null;

  return (
    <div className="mt-2 rounded-md border border-gray-100 bg-gray-50">
      <button
        type="button"
        onClick={() => setExpandedIndex(expandedIndex === -1 ? null : -1)}
        className="flex w-full items-center gap-2 px-3 py-1.5 text-xs text-gray-500 hover:text-gray-700 transition-colors"
      >
        <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M11.42 15.17l-5.384-3.19A.6.6 0 015.4 11.4V6.6a.6.6 0 01.636-.58l5.384 3.19a.6.6 0 01.6.58v4.8a.6.6 0 01-.6.58z" />
          <path strokeLinecap="round" strokeLinejoin="round" d="M20.58 15.17l-5.384-3.19a.6.6 0 01-.6-.58V6.6a.6.6 0 01.636-.58l5.384 3.19a.6.6 0 01.6.58v4.8a.6.6 0 01-.636.58z" />
        </svg>
        <span>{tools.length} tool{tools.length !== 1 ? 's' : ''} executed</span>
        <svg className={`ml-auto h-3 w-3 transition-transform ${expandedIndex === -1 ? 'rotate-180' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
        </svg>
      </button>
      {expandedIndex === -1 && (
        <div className="border-t border-gray-100 px-3 py-2 space-y-1.5">
          {tools.map((tool, index) => (
            <div key={index}>
              <button
                type="button"
                onClick={() => setExpandedIndex(expandedIndex === index ? -1 : index)}
                className="flex w-full items-center gap-2 text-xs"
              >
                {tool.success ? (
                  <svg className="h-3 w-3 text-green-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
                  </svg>
                ) : (
                  <svg className="h-3 w-3 text-red-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                )}
                <span className="font-medium text-gray-700">{tool.toolName}</span>
                <span className="ml-auto text-gray-400">{tool.executionTimeMs}ms</span>
              </button>
              {expandedIndex === index && tool.result && (
                <pre className="mt-1 overflow-x-auto rounded bg-gray-100 p-2 text-xs text-gray-600">
                  {tool.result}
                </pre>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
