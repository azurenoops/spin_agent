import { useCallback, useMemo } from 'react';
import { useLocalStorage } from './useLocalStorage';
import { useSseStream } from './useSseStream';
import { useChatContext } from './useChatContext';
import type {
  Conversation,
  Message,
  ChatPanelState,
  ChatRequest,
  SseResultEvent,
  SseErrorEvent,
  SseProgressEvent,
} from '../types/chat';

const MAX_CONVERSATIONS = 50;
const MAX_HISTORY_DEPTH = 20;
const CONVERSATIONS_KEY = 'ato-chat-conversations';
const PANEL_STATE_KEY = 'ato-chat-panel-state';

const DEFAULT_PANEL_STATE: ChatPanelState = {
  isOpen: false,
  width: 420,
  activeConversationId: null,
};

export interface UseChatReturn {
  conversations: Conversation[];
  activeConversation: Conversation | null;
  isProcessing: boolean;
  progressSteps: SseProgressEvent[];
  panelState: ChatPanelState;
  context: ReturnType<typeof useChatContext>;
  sendMessage: (content: string, attachments?: File[]) => Promise<void>;
  newConversation: () => void;
  selectConversation: (id: string) => void;
  deleteConversation: (id: string) => void;
  cancelStream: () => void;
  setPanelState: (state: ChatPanelState | ((prev: ChatPanelState) => ChatPanelState)) => void;
}

function generateId(): string {
  return crypto.randomUUID();
}

function generateTitle(content: string): string {
  return content.length > 50 ? content.substring(0, 50) + '…' : content;
}

export function useChat(): UseChatReturn {
  const [conversations, setConversations] = useLocalStorage<Conversation[]>(CONVERSATIONS_KEY, []);
  const [panelState, setPanelState] = useLocalStorage<ChatPanelState>(PANEL_STATE_KEY, DEFAULT_PANEL_STATE);
  const { isStreaming, progressSteps, cancel, stream } = useSseStream();
  const context = useChatContext();

  const activeConversation = useMemo(
    () => conversations.find((c) => c.id === panelState.activeConversationId) ?? null,
    [conversations, panelState.activeConversationId],
  );

  const newConversation = useCallback(() => {
    const conv: Conversation = {
      id: generateId(),
      title: 'New Conversation',
      messages: [],
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      context,
    };

    setConversations((prev) => {
      const updated = [conv, ...prev];
      // LRU eviction: keep only MAX_CONVERSATIONS, sorted by updatedAt
      if (updated.length > MAX_CONVERSATIONS) {
        updated.sort((a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime());
        return updated.slice(0, MAX_CONVERSATIONS);
      }
      return updated;
    });

    setPanelState((prev) => ({ ...prev, activeConversationId: conv.id }));
  }, [context, setConversations, setPanelState]);

  const selectConversation = useCallback(
    (id: string) => {
      setPanelState((prev) => ({ ...prev, activeConversationId: id }));
    },
    [setPanelState],
  );

  const deleteConversation = useCallback(
    (id: string) => {
      setConversations((prev) => prev.filter((c) => c.id !== id));
      setPanelState((prev) => {
        if (prev.activeConversationId === id) {
          return { ...prev, activeConversationId: null };
        }
        return prev;
      });
    },
    [setConversations, setPanelState],
  );

  const cancelStream = useCallback(() => {
    cancel();
  }, [cancel]);

  const sendMessage = useCallback(
    async (content: string) => {
      // T035: Cancel in-flight stream before sending new message
      if (isStreaming) {
        cancel();
        // Mark any streaming assistant message as cancelled
        setConversations((prev) =>
          prev.map((c) => ({
            ...c,
            messages: c.messages.map((m) =>
              m.status === 'streaming' || m.status === 'sending'
                ? { ...m, status: 'complete' as const, content: m.content || '*(Cancelled)*' }
                : m,
            ),
          })),
        );
      }

      let convId = panelState.activeConversationId;

      // Validate the target conversation still exists (it may have been evicted
      // by LRU, deleted, or left stale from a prior session in localStorage).
      if (convId && !conversations.some((c) => c.id === convId)) {
        convId = null;
      }

      // Auto-create conversation if none active
      if (!convId) {
        const conv: Conversation = {
          id: generateId(),
          title: generateTitle(content),
          messages: [],
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          context,
        };
        convId = conv.id;
        setConversations((prev) => {
          const updated = [conv, ...prev];
          if (updated.length > MAX_CONVERSATIONS) {
            updated.sort((a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime());
            return updated.slice(0, MAX_CONVERSATIONS);
          }
          return updated;
        });
        setPanelState((prev) => ({ ...prev, activeConversationId: conv.id }));
      }

      const userMessage: Message = {
        id: generateId(),
        role: 'user',
        content,
        status: 'sending',
        timestamp: new Date().toISOString(),
      };

      const assistantMessage: Message = {
        id: generateId(),
        role: 'assistant',
        content: '',
        status: 'sending',
        timestamp: new Date().toISOString(),
      };

      // Add user message and placeholder assistant message
      const targetConvId = convId;
      setConversations((prev) =>
        prev.map((c) => {
          if (c.id !== targetConvId) return c;
          const updatedMessages = [...c.messages, userMessage, assistantMessage];
          return {
            ...c,
            messages: updatedMessages,
            updatedAt: new Date().toISOString(),
            title: c.messages.length === 0 ? generateTitle(content) : c.title,
          };
        }),
      );

      // Build conversation history (last N messages, excluding the ones we just added)
      const conv = conversations.find((c) => c.id === targetConvId);
      const existingMessages = conv?.messages ?? [];
      const historyMessages = existingMessages.slice(-MAX_HISTORY_DEPTH);
      const ConversationHistory = historyMessages
        .filter((m) => m.status === 'complete')
        .map((m) => ({
          role: m.role as 'user' | 'assistant',
          content: m.content,
        }));

      const request: ChatRequest = {
        message: content,
        conversationId: targetConvId,
        context: context ? { ...context } : null,
        conversationHistory: ConversationHistory,
        action: null,
        actionContext: null,
      };

      stream(
        request,
        (result: SseResultEvent) => {
          setConversations((prev) =>
            prev.map((c) => {
              if (c.id !== targetConvId) return c;
              return {
                ...c,
                updatedAt: new Date().toISOString(),
                messages: c.messages.map((m) => {
                  if (m.id === userMessage.id) {
                    return { ...m, status: 'complete' as const };
                  }
                  if (m.id === assistantMessage.id) {
                    return {
                      ...m,
                      content: result.response,
                      status: 'complete' as const,
                      agentName: result.agentUsed,
                      intentType: result.intentType,
                      processingTimeMs: result.processingTimeMs,
                      toolsExecuted: result.toolsExecuted,
                      errors: result.errors.length > 0 ? result.errors : undefined,
                      suggestedActions: result.suggestedActions.length > 0 ? result.suggestedActions : undefined,
                      requiresFollowUp: result.requiresFollowUp,
                    };
                  }
                  return m;
                }),
              };
            }),
          );
        },
        (error: SseErrorEvent | Error) => {
          const errorDetail = error instanceof Error
            ? { errorCode: 'NETWORK_ERROR', message: error.message, suggestion: 'Check your connection and try again.' }
            : { errorCode: error.errorCode, message: error.message, suggestion: error.suggestion ?? null };

          setConversations((prev) =>
            prev.map((c) => {
              if (c.id !== targetConvId) return c;
              return {
                ...c,
                messages: c.messages.map((m) => {
                  if (m.id === assistantMessage.id) {
                    return {
                      ...m,
                      content: errorDetail.message,
                      status: 'error' as const,
                      errors: [errorDetail],
                    };
                  }
                  if (m.id === userMessage.id) {
                    return { ...m, status: 'complete' as const };
                  }
                  return m;
                }),
              };
            }),
          );
        },
      );
    },
    [panelState.activeConversationId, conversations, context, setConversations, setPanelState, stream, isStreaming, cancel],
  );

  return {
    conversations,
    activeConversation,
    isProcessing: isStreaming,
    progressSteps,
    panelState,
    context,
    sendMessage,
    newConversation,
    selectConversation,
    deleteConversation,
    cancelStream,
    setPanelState,
  };
}
