import { useState, useEffect, useRef } from 'react';
import { tooltipContent } from './helpContent';

interface HelpTooltipProps {
  helpKey: string;
}

export default function HelpTooltip({ helpKey }: HelpTooltipProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const content = tooltipContent[helpKey];

  useEffect(() => {
    if (!open) return;
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    const handleClick = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('keydown', handleKey);
    document.addEventListener('mousedown', handleClick);
    return () => {
      document.removeEventListener('keydown', handleKey);
      document.removeEventListener('mousedown', handleClick);
    };
  }, [open]);

  if (!content) return null;

  return (
    <div ref={containerRef} className="relative inline-flex">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className="ml-1.5 rounded-full p-0.5 text-gray-400 hover:text-blue-600 hover:bg-blue-50 transition-colors"
        aria-label={`Help: ${content.title}`}
        aria-expanded={open}
      >
        <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M9.879 7.519c1.171-1.025 3.071-1.025 4.242 0 1.172 1.025 1.172 2.687 0 3.712-.203.179-.43.326-.67.442-.745.361-1.45.999-1.45 1.827v.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 5.25h.008v.008H12v-.008Z" />
        </svg>
      </button>
      {open && (
        <div
          role="tooltip"
          className="absolute left-1/2 top-full z-50 mt-2 w-72 -translate-x-1/2 rounded-lg border border-gray-200 bg-white p-4 shadow-lg"
        >
          <div className="absolute -top-1.5 left-1/2 -translate-x-1/2 h-3 w-3 rotate-45 border-l border-t border-gray-200 bg-white" />
          <p className="text-sm font-semibold text-gray-900">{content.title}</p>
          <p className="mt-1 text-sm text-gray-600 leading-relaxed">{content.description}</p>
          {content.emptyStateHint && (
            <p className="mt-2 text-xs text-amber-700 bg-amber-50 rounded px-2 py-1.5 leading-relaxed">
              💡 {content.emptyStateHint}
            </p>
          )}
        </div>
      )}
    </div>
  );
}
