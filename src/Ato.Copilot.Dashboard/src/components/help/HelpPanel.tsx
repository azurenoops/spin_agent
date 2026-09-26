import { useState } from 'react';
import { helpSections, type HelpSection } from './helpContent';

interface HelpPanelProps {
  onClose: () => void;
}

function SectionItem({ section }: { section: HelpSection }) {
  const [expanded, setExpanded] = useState(false);

  return (
    <div className="border-b border-gray-100 last:border-b-0 dark:border-gray-700">
      <button
        type="button"
        onClick={() => setExpanded(!expanded)}
        className="flex w-full items-center justify-between px-4 py-3 text-left hover:bg-gray-50 transition-colors dark:hover:bg-gray-800"
        aria-expanded={expanded}
      >
        <span className="text-sm font-medium text-gray-900 dark:text-gray-100">{section.title}</span>
        <svg
          className={`h-4 w-4 flex-shrink-0 text-gray-400 transition-transform ${expanded ? 'rotate-180' : ''}`}
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          strokeWidth={2}
        >
          <path strokeLinecap="round" strokeLinejoin="round" d="m19.5 8.25-7.5 7.5-7.5-7.5" />
        </svg>
      </button>
      {expanded && (
        <div className="px-4 pb-3">
          <p className="text-sm text-gray-600 leading-relaxed dark:text-gray-300">{section.content}</p>
          {section.subsections?.map((sub) => (
            <div key={sub.title} className="mt-3">
              <p className="text-xs font-semibold text-gray-700 dark:text-gray-200">{sub.title}</p>
              <p className="mt-0.5 text-xs text-gray-500 leading-relaxed dark:text-gray-400">{sub.content}</p>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export default function HelpPanel({ onClose }: HelpPanelProps) {
  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-gray-200 px-4 py-3 dark:border-gray-700">
        <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">Help</h2>
        <button
          type="button"
          onClick={onClose}
          className="rounded-md p-1 text-gray-500 hover:text-gray-600 hover:bg-gray-100 transition-colors dark:text-gray-300 dark:hover:text-gray-100 dark:hover:bg-gray-800"
          aria-label="Close help panel"
        >
          <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
          </svg>
        </button>
      </div>

      {/* Sections */}
      <div className="flex-1 overflow-y-auto">
        {helpSections.length === 0 ? (
          <div className="px-4 py-8 text-center">
            <p className="text-sm text-gray-500 dark:text-gray-400">No help content available.</p>
          </div>
        ) : (
          helpSections.map((section) => (
            <SectionItem key={section.id} section={section} />
          ))
        )}
      </div>
    </div>
  );
}
