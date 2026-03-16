import { useState } from 'react';
import { helpSections, type HelpSection } from './helpContent';

interface HelpPanelProps {
  onClose: () => void;
}

function SectionItem({ section }: { section: HelpSection }) {
  const [expanded, setExpanded] = useState(false);

  return (
    <div className="border-b border-gray-100 last:border-b-0">
      <button
        type="button"
        onClick={() => setExpanded(!expanded)}
        className="flex w-full items-center justify-between px-4 py-3 text-left hover:bg-gray-50 transition-colors"
        aria-expanded={expanded}
      >
        <span className="text-sm font-medium text-gray-900">{section.title}</span>
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
          <p className="text-sm text-gray-600 leading-relaxed">{section.content}</p>
          {section.subsections?.map((sub) => (
            <div key={sub.title} className="mt-3">
              <p className="text-xs font-semibold text-gray-700">{sub.title}</p>
              <p className="mt-0.5 text-xs text-gray-500 leading-relaxed">{sub.content}</p>
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
      <div className="flex items-center justify-between border-b border-gray-200 px-4 py-3">
        <h2 className="text-lg font-semibold text-gray-900">Help</h2>
        <button
          type="button"
          onClick={onClose}
          className="rounded-md p-1 text-gray-400 hover:text-gray-600 hover:bg-gray-100 transition-colors"
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
            <p className="text-sm text-gray-500">No help content available.</p>
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
