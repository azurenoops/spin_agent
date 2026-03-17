import { useRef, useEffect } from 'react';
import type { Message, SseProgressEvent } from '../../types/chat';
import ChatBubble from './ChatBubble';
import ProgressSteps from './ProgressSteps';

export interface ChatMessagesProps {
  messages: Message[];
  progressSteps: SseProgressEvent[];
  isProcessing: boolean;
}

export default function ChatMessages({ messages, progressSteps, isProcessing }: ChatMessagesProps) {
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, progressSteps]);

  if (messages.length === 0 && !isProcessing) {
    return (
      <div className="flex flex-1 items-center justify-center p-6">
        <div className="text-center">
          <svg className="mx-auto h-10 w-10 text-gray-300" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M7.5 8.25h9m-9 3H12m-9.75 1.51c0 1.6 1.123 2.994 2.707 3.227 1.087.16 2.185.283 3.293.369V21l4.076-4.076a1.526 1.526 0 011.037-.443 48.282 48.282 0 005.68-.494c1.584-.233 2.707-1.626 2.707-3.228V6.741c0-1.602-1.123-2.995-2.707-3.228A48.394 48.394 0 0012 3c-2.392 0-4.744.175-7.043.513C3.373 3.746 2.25 5.14 2.25 6.741v6.018z" />
          </svg>
          <p className="mt-2 text-sm text-gray-400">No messages yet</p>
          <p className="mt-1 text-xs text-gray-300">Send a message to start a conversation</p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 overflow-y-auto p-4 space-y-3">
      {messages.map((message) => (
        <ChatBubble key={message.id} message={message} />
      ))}
      {isProcessing && progressSteps.length > 0 && (
        <div className="pl-2">
          <ProgressSteps steps={progressSteps} />
        </div>
      )}
      <div ref={bottomRef} />
    </div>
  );
}
