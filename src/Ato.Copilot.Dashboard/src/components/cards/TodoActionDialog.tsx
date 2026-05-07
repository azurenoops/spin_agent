import { useState, useEffect, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import type { TodoItem } from '../../types/dashboard';

interface TodoActionDialogProps {
  item: TodoItem;
  systemName: string;
  onClose: () => void;
}

const categoryLabels: Record<string, string> = {
  'phase-action': 'RMF Phase Action',
  finding: 'Compliance Finding',
  poam: 'POA&M Item',
  narrative: 'SSP Narrative',
  authorization: 'Authorization',
};

export default function TodoActionDialog({ item, systemName, onClose }: TodoActionDialogProps) {
  const [copied, setCopied] = useState(false);
  const dialogRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();

  // Close on Escape
  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [onClose]);

  // Close on click outside
  const handleBackdrop = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) onClose();
  };

  const copyPrompt = () => {
    if (!item.prompt) return;
    navigator.clipboard.writeText(`${item.prompt}`);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleDashboardAction = () => {
    if (item.link) {
      navigate(item.link);
      onClose();
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm"
      onClick={handleBackdrop}
    >
      <div
        ref={dialogRef}
        className="w-full max-w-md rounded-xl bg-white shadow-2xl border border-gray-200 overflow-hidden"
        role="dialog"
        aria-modal="true"
        aria-label={item.label}
      >
        {/* Header */}
        <div className="flex items-start justify-between px-6 pt-5 pb-3">
          <div className="min-w-0 flex-1">
            <span className="inline-block rounded-full bg-indigo-50 px-2.5 py-0.5 text-xs font-medium text-indigo-700 mb-2">
              {categoryLabels[item.category] ?? item.category}
            </span>
            <h3 className="text-lg font-semibold text-gray-900">{item.label}</h3>
            <p className="text-sm text-gray-500 mt-1">{item.detail}</p>
            <p className="text-xs text-gray-400 mt-1">System: {systemName}</p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="ml-4 rounded-lg p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
            aria-label="Close"
          >
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <div className="border-t border-gray-100" />

        {/* Actions */}
        <div className="px-6 py-4 space-y-3">
          <p className="text-xs font-semibold text-gray-500 uppercase tracking-wide">How would you like to proceed?</p>

          {/* Option 1: Dashboard action */}
          {item.link && (
            <button
              type="button"
              onClick={handleDashboardAction}
              className="flex w-full items-center gap-3 rounded-lg border border-gray-200 px-4 py-3 text-left hover:border-indigo-300 hover:bg-indigo-50 transition-colors group"
            >
              <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-lg bg-indigo-100 text-indigo-600 group-hover:bg-indigo-200">
                <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 6A2.25 2.25 0 0 1 6 3.75h2.25A2.25 2.25 0 0 1 10.5 6v2.25a2.25 2.25 0 0 1-2.25 2.25H6a2.25 2.25 0 0 1-2.25-2.25V6Zm0 9.75A2.25 2.25 0 0 1 6 13.5h2.25a2.25 2.25 0 0 1 2.25 2.25V18a2.25 2.25 0 0 1-2.25 2.25H6A2.25 2.25 0 0 1 3.75 18v-2.25ZM13.5 6a2.25 2.25 0 0 1 2.25-2.25H18A2.25 2.25 0 0 1 20.25 6v2.25A2.25 2.25 0 0 1 18 10.5h-2.25a2.25 2.25 0 0 1-2.25-2.25V6Zm0 9.75a2.25 2.25 0 0 1 2.25-2.25H18a2.25 2.25 0 0 1 2.25 2.25V18A2.25 2.25 0 0 1 18 20.25h-2.25A2.25 2.25 0 0 1 13.5 18v-2.25Z" />
                </svg>
              </div>
              <div>
                <p className="text-sm font-medium text-gray-900">Open in Dashboard</p>
                <p className="text-xs text-gray-500">View and manage directly in the compliance dashboard</p>
              </div>
            </button>
          )}

          {/* Option 2: Teams / VS Code query */}
          {item.prompt && (
            <button
              type="button"
              onClick={copyPrompt}
              className="flex w-full items-center gap-3 rounded-lg border border-gray-200 px-4 py-3 text-left hover:border-purple-300 hover:bg-purple-50 transition-colors group"
            >
              <div className={`flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-lg transition-colors ${
                copied ? 'bg-green-100 text-green-600' : 'bg-purple-100 text-purple-600 group-hover:bg-purple-200'
              }`}>
                {copied ? (
                  <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                  </svg>
                ) : (
                  <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M7.5 8.25h9m-9 3H12m-9.75 1.51c0 1.6 1.123 2.994 2.707 3.227 1.129.166 2.27.293 3.423.379.35.026.67.21.865.501L12 21l2.755-4.133a1.14 1.14 0 0 1 .865-.501 48.172 48.172 0 0 0 3.423-.379c1.584-.233 2.707-1.626 2.707-3.228V6.741c0-1.602-1.123-2.995-2.707-3.228A48.394 48.394 0 0 0 12 3c-2.392 0-4.744.175-7.043.513C3.373 3.746 2.25 5.14 2.25 6.741v6.018Z" />
                  </svg>
                )}
              </div>
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-gray-900">
                  {copied ? 'Copied! Paste in Teams or VS Code' : 'Ask in Teams or VS Code'}
                </p>
                <p className="text-xs text-gray-500 truncate font-mono mt-0.5">
                  {item.prompt}
                </p>
              </div>
            </button>
          )}
        </div>

        {/* Footer */}
        <div className="border-t border-gray-100 px-6 py-3 bg-gray-50 flex justify-end">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-200 transition-colors"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
