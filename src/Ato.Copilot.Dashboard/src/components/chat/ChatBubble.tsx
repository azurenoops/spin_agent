import type { Message } from '../../types/chat';
import MarkdownRenderer from './MarkdownRenderer';
import ToolEvidence from './ToolEvidence';
import SuggestionCards from './SuggestionCards';

export interface ChatBubbleProps {
  message: Message;
  onSuggestionSelect?: (prompt: string) => void;
  isProcessing?: boolean;
}

function formatTime(timestamp: string): string {
  const date = new Date(timestamp);
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function StatusIndicator({ status }: { status: Message['status'] }) {
  switch (status) {
    case 'sending':
      return (
        <div className="h-3 w-3 animate-spin rounded-full border-2 border-gray-400 border-t-transparent" title="Sending" />
      );
    case 'streaming':
      return (
        <div className="flex items-center gap-0.5" title="Streaming">
          <div className="h-1.5 w-1.5 animate-pulse rounded-full bg-blue-500" />
          <div className="h-1.5 w-1.5 animate-pulse rounded-full bg-blue-500" style={{ animationDelay: '0.2s' }} />
          <div className="h-1.5 w-1.5 animate-pulse rounded-full bg-blue-500" style={{ animationDelay: '0.4s' }} />
        </div>
      );
    case 'error':
      return (
        <svg className="h-3.5 w-3.5 text-red-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2} aria-label="Error">
          <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
        </svg>
      );
    default:
      return null;
  }
}

export default function ChatBubble({ message, onSuggestionSelect, isProcessing }: ChatBubbleProps) {
  const isUser = message.role === 'user';

  return (
    <div className={`flex ${isUser ? 'justify-end' : 'justify-start'}`}>
      <div
        className={`max-w-[85%] rounded-lg px-3 py-2 ${
          isUser
            ? 'bg-blue-600 text-white'
            : 'bg-white border border-gray-200 text-gray-800'
        }`}
      >
        {isUser ? (
          <p className="text-sm whitespace-pre-wrap">{message.content}</p>
        ) : (
          <>
            {message.agentName && (
              <div className="mb-1 flex items-center gap-1.5 text-xs text-gray-400">
                <span className="font-medium">{message.agentName}</span>
                {message.intentType && (
                  <>
                    <span>·</span>
                    <span>{message.intentType}</span>
                  </>
                )}
              </div>
            )}
            {message.content ? (
              <MarkdownRenderer content={message.content} />
            ) : message.status === 'sending' || message.status === 'streaming' ? (
              <div className="flex items-center gap-2 py-1 text-sm text-gray-400">
                <StatusIndicator status={message.status} />
                <span>Thinking…</span>
              </div>
            ) : null}
            {message.toolsExecuted && message.toolsExecuted.length > 0 && (
              <ToolEvidence tools={message.toolsExecuted} />
            )}
            {message.suggestedActions && message.suggestedActions.length > 0 && onSuggestionSelect && (
              <SuggestionCards
                suggestions={message.suggestedActions}
                onSelect={onSuggestionSelect}
                disabled={isProcessing ?? false}
              />
            )}
          </>
        )}

        <div className={`mt-1 flex items-center gap-2 ${isUser ? 'justify-end' : 'justify-start'}`}>
          {message.status !== 'complete' && message.status !== 'error' && (
            <StatusIndicator status={message.status} />
          )}
          <span className={`text-xs ${isUser ? 'text-blue-200' : 'text-gray-400'}`}>
            {formatTime(message.timestamp)}
          </span>
        </div>
      </div>
    </div>
  );
}
