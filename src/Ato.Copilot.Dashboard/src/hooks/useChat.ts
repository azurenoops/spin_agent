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

// #201: Canonical MIME allowlist — reconciled across FileAttachment.tsx / useChat.ts / McpHttpBridge.cs
// NOTE: text/plain covers .txt; text/csv covers .csv; text/xml is an alternate MIME for XML.
// Binary formats (PDF, DOCX, XLSX) are accepted; backend extracts text with StreamReader (may produce
// garbled output for binary, but is better than silent drop — see McpHttpBridge.cs).
const ALLOWED_ATTACHMENT_TYPES = new Set([
  'application/pdf',
  'text/plain',
  'text/csv',
  'application/json',
  'application/xml',
  'text/xml',
  // SCAP/STIG formats
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',  // .xlsx
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',  // .docx
]);
const MAX_ATTACHMENT_SIZE_BYTES = 10 * 1024 * 1024; // 10 MB (unified with FileAttachment.tsx)

export interface AttachmentValidationError {
  fileName: string;
  reason: string;
}

export interface UseChatReturn {
  conversations: Conversation[];
  activeConversation: Conversation | null;
  isProcessing: boolean;
  progressSteps: SseProgressEvent[];
  /** T270: Active MCP tool chips (toolName → start epoch ms) */
  activeToolChips: Map<string, number>;
  panelState: ChatPanelState;
  context: ReturnType<typeof useChatContext>;
  sendMessage: (content: string, attachments?: File[]) => Promise<void>;
  newConversation: () => void;
  selectConversation: (id: string) => void;
  deleteConversation: (id: string) => void;
  cancelStream: () => void;
  setPanelState: (state: ChatPanelState | ((prev: ChatPanelState) => ChatPanelState)) => void;
  validateAttachments: (files: File[]) => AttachmentValidationError[];
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
  const context = useChatContext();
  const { isStreaming, progressSteps, activeToolChips, cancel, stream } = useSseStream();

  const activeConversation = useMemo(
    () => conversations.find((c) => c.id === panelState.activeConversationId) ?? null,
    [conversations, panelState.activeConversationId],
  );

  const isProcessing = isStreaming;

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
      setPanelState((prev) => ({
        ...prev,
        activeConversationId: prev.activeConversationId === id ? null : prev.activeConversationId,
      }));
    },
    [setConversations, setPanelState],
  );

  const cancelStream = useCallback(() => {
    cancel();
  }, [cancel]);

  // T260/#201: Validate files before sending — returns list of errors (empty = valid)
  const validateAttachments = useCallback((files: File[]): AttachmentValidationError[] => {
    const errors: AttachmentValidationError[] = [];
    for (const file of files) {
      if (!ALLOWED_ATTACHMENT_TYPES.has(file.type)) {
        errors.push({
          fileName: file.name,
          reason: `Unsupported type (${file.type || 'unknown'}). Allowed: PDF, TXT, CSV, JSON, XML, DOCX, XLSX.`,
        });
      } else if (file.size > MAX_ATTACHMENT_SIZE_BYTES) {
        errors.push({
          fileName: file.name,
          reason: `File exceeds the 10 MB limit (${(file.size / 1024 / 1024).toFixed(1)} MB).`,
        });
      }
    }
    return errors;
  }, []);

  const sendMessage = useCallback(
    async (content: string, attachments?: File[]) => {
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

      // T260: Build attachment error chips for invalid files; only forward valid files
      const validFiles: File[] = [];
      const attachmentErrors: string[] = [];
      if (attachments && attachments.length > 0) {
        const validationErrors = validateAttachments(attachments);
        const errorNames = new Set(validationErrors.map((e) => e.fileName));
        for (const file of attachments) {
          if (errorNames.has(file.name)) {
            attachmentErrors.push(`${file.name}: ${validationErrors.find((e) => e.fileName === file.name)!.reason}`);
          } else {
            validFiles.push(file);
          }
        }
      }

      const userMessage: Message = {
        id: generateId(),
        role: 'user',
        content,
        status: 'sending',
        timestamp: new Date().toISOString(),
        // T260: Attach file metadata for display
        attachments: validFiles.length > 0
          ? validFiles.map((f) => ({ name: f.name, size: f.size, type: f.type }))
          : undefined,
      };

      const assistantMessage: Message = {
        id: generateId(),
        role: 'assistant',
        content: '',
        status: 'sending',
        timestamp: new Date().toISOString(),
      };

      // T260: If any attachments were invalid, prepend an error message
      const extraMessages: Message[] = attachmentErrors.length > 0
        ? [{
            id: generateId(),
            role: 'assistant' as const,
            content: `⚠️ **Attachment error(s):** The following files could not be uploaded:\n\n${attachmentErrors.map((e) => `- ${e}`).join('\n')}`,
            status: 'complete' as const,
            timestamp: new Date().toISOString(),
          }]
        : [];

      // Add user message (+ optional error chips) and placeholder assistant message
      const targetConvId = convId;
      setConversations((prev) =>
        prev.map((c) => {
          if (c.id !== targetConvId) return c;
          const updatedMessages = [...c.messages, ...extraMessages, userMessage, assistantMessage];
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
        // T260: forward valid file attachments via multipart
        attachments: validFiles.length > 0 ? validFiles : undefined,
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
            : error;
          setConversations((prev) =>
            prev.map((c) => {
              if (c.id !== targetConvId) return c;
              return {
                ...c,
                messages: c.messages.map((m) => {
                  if (m.id === assistantMessage.id) {
                    return {
                      ...m,
                      status: 'error' as const,
                      errors: [errorDetail],
                    };
                  }
                  return m;
                }),
              };
            }),
          );
        },
      );
    },
    [
      isStreaming,
      cancel,
      panelState.activeConversationId,
      conversations,
      context,
      setConversations,
      setPanelState,
      stream,
      validateAttachments,
    ],
  );

  return {
    conversations,
    activeConversation,
    isProcessing,
    progressSteps,
    activeToolChips,
    panelState,
    context,
    sendMessage,
    newConversation,
    selectConversation,
    deleteConversation,
    cancelStream,
    setPanelState,
    validateAttachments,
  };
}
