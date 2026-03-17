import type { ChatContext } from '../../types/chat';

export interface ChatHeaderProps {
  title: string;
  onClose: () => void;
  onNewConversation: () => void;
  conversationCount: number;
  context?: ChatContext | null;
}

function contextLabel(context?: ChatContext | null): string | null {
  if (!context) return null;
  if (context.systemId) return `Viewing: System ${context.systemId}`;
  if (context.page) return `Viewing: ${context.page}`;
  return null;
}

export default function ChatHeader({ title, onClose, onNewConversation, conversationCount, context }: ChatHeaderProps) {
  const label = contextLabel(context);

  return (
    <div className="border-b border-gray-200 bg-white px-4 py-3">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h2 className="text-sm font-semibold text-gray-800">{title}</h2>
          {conversationCount > 0 && (
            <span className="rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-500">
              {conversationCount}
            </span>
          )}
        </div>
        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={onNewConversation}
            className="rounded-md p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
            aria-label="New conversation"
            title="New conversation"
          >
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" />
            </svg>
          </button>
          <button
            type="button"
            onClick={onClose}
            className="rounded-md p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-600 transition-colors"
            aria-label="Close chat panel"
            title="Close"
          >
            <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
      </div>
      {label && (
        <p className="mt-1 text-xs text-gray-400">{label}</p>
      )}
    </div>
  );
}
