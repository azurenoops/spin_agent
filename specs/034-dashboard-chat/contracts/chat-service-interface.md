# Contract: Chat Service Interface

**Feature**: 034-dashboard-chat | **Date**: 2025-03-16
**Module**: `src/Ato.Copilot.Dashboard/src/services/chatService.ts`

## Service Interface

```typescript
/**
 * Chat service for communicating with the MCP Server's SSE endpoint.
 * Handles streaming, cancellation, and error recovery.
 */
interface ChatService {
  /**
   * Send a message and stream the response via SSE.
   *
   * @param request - Chat request parameters
   * @param onProgress - Callback for each progress event
   * @param onResult - Callback when the final result arrives
   * @param onError - Callback on error (network or server)
   * @param abortSignal - AbortSignal for cancellation
   */
  sendMessage(
    request: ChatRequest,
    onProgress: (event: SseProgressEvent) => void,
    onResult: (event: SseResultEvent) => void,
    onError: (error: SseErrorEvent | Error) => void,
    abortSignal?: AbortSignal,
  ): Promise<void>;
}
```

## React Hook Interface

```typescript
/**
 * Hook return type for useSseStream.
 */
interface UseSseStreamReturn {
  /** Whether a stream is currently active */
  isStreaming: boolean;
  /** Current progress steps received */
  progressSteps: SseProgressEvent[];
  /** Cancel the active stream */
  cancel: () => void;
  /** Start a new stream */
  stream: (
    request: ChatRequest,
    onResult: (event: SseResultEvent) => void,
    onError: (error: SseErrorEvent | Error) => void,
  ) => void;
}

/**
 * Hook return type for useChat.
 */
interface UseChatReturn {
  /** All conversations */
  conversations: Conversation[];
  /** Currently active conversation */
  activeConversation: Conversation | null;
  /** Whether a message is being sent/streamed */
  isProcessing: boolean;
  /** Current progress steps for active stream */
  progressSteps: SseProgressEvent[];
  /** Send a message in the active conversation */
  sendMessage: (content: string, attachments?: File[]) => Promise<void>;
  /** Create a new conversation */
  newConversation: () => void;
  /** Switch to a different conversation */
  selectConversation: (id: string) => void;
  /** Delete a conversation */
  deleteConversation: (id: string) => void;
  /** Cancel the active stream */
  cancelStream: () => void;
}

/**
 * Hook return type for useChatContext.
 */
interface UseChatContextReturn {
  /** Current dashboard context based on route and page state */
  context: ChatContext;
}
```

## Component Props Contracts

```typescript
/** Root chat panel */
interface ChatPanelProps {
  isOpen: boolean;
  onClose: () => void;
  width: number;
  onWidthChange: (width: number) => void;
}

/** Chat header bar */
interface ChatHeaderProps {
  title: string;
  onClose: () => void;
  onNewConversation: () => void;
  conversationCount: number;
}

/** Message list */
interface ChatMessagesProps {
  messages: Message[];
  progressSteps: SseProgressEvent[];
  isProcessing: boolean;
}

/** Text input area */
interface ChatInputProps {
  onSend: (content: string, attachments?: File[]) => void;
  onCancel: () => void;
  isProcessing: boolean;
  disabled: boolean;
}

/** Single message bubble */
interface ChatBubbleProps {
  message: Message;
}

/** Markdown content renderer */
interface MarkdownRendererProps {
  content: string;
}

/** Tool execution evidence */
interface ToolEvidenceProps {
  tools: ToolExecution[];
}

/** Suggestion chips */
interface SuggestionCardsProps {
  suggestions: SuggestedAction[];
  onSelect: (prompt: string) => void;
  disabled: boolean;
}

/** Conversation selector */
interface ConversationListProps {
  conversations: Conversation[];
  activeId: string | null;
  onSelect: (id: string) => void;
  onDelete: (id: string) => void;
  onNew: () => void;
}

/** Toggle button in header */
interface ChatToggleProps {
  isOpen: boolean;
  onClick: () => void;
}
```
