import React, {
  createContext,
  useContext,
  useReducer,
  useEffect,
  useRef,
  useCallback,
  ReactNode,
} from 'react';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import {
  ChatState,
  ChatAction,
  ChatMessage,
  Conversation,
  ConnectionStatus,
  MessageRole,
  MessageStatus,
  SendMessageRequest,
} from '../types/chat';
import * as chatApi from '../services/chatApi';

// ────────────────────────────────────────────────────────────────
//  Initial State
// ────────────────────────────────────────────────────────────────

const initialState: ChatState = {
  conversations: [],
  activeConversationId: null,
  messages: [],
  isLoading: false,
  isProcessing: false,
  progressMessage: null,
  error: null,
  connectionStatus: ConnectionStatus.Disconnected,
  typingUsers: [],
  searchResults: [],
};

// ────────────────────────────────────────────────────────────────
//  Reducer
// ────────────────────────────────────────────────────────────────

function chatReducer(state: ChatState, action: ChatAction): ChatState {
  switch (action.type) {
    case 'SET_CONVERSATIONS':
      return { ...state, conversations: action.payload };

    case 'ADD_CONVERSATION':
      return {
        ...state,
        conversations: [action.payload, ...state.conversations],
      };

    case 'DELETE_CONVERSATION':
      return {
        ...state,
        conversations: state.conversations.filter((c) => c.id !== action.payload),
        activeConversationId:
          state.activeConversationId === action.payload ? null : state.activeConversationId,
        messages: state.activeConversationId === action.payload ? [] : state.messages,
      };

    case 'SET_ACTIVE_CONVERSATION':
      return { ...state, activeConversationId: action.payload, messages: [] };

    case 'SET_MESSAGES':
      return { ...state, messages: action.payload };

    case 'ADD_MESSAGE':
      return {
        ...state,
        messages: [...state.messages, action.payload],
      };

    case 'UPDATE_MESSAGE_STATUS': {
      const { id, status, error } = action.payload;
      return {
        ...state,
        messages: state.messages.map((m) =>
          m.id === id
            ? {
                ...m,
                status,
                metadata: error ? { ...m.metadata, error } : m.metadata,
              }
            : m
        ),
      };
    }

    case 'SET_LOADING':
      return { ...state, isLoading: action.payload };

    case 'SET_PROCESSING':
      return { ...state, isProcessing: action.payload, progressMessage: action.payload ? state.progressMessage : null };

    case 'SET_PROGRESS':
      return { ...state, progressMessage: action.payload };

    case 'SET_ERROR':
      return { ...state, error: action.payload };

    case 'SET_CONNECTION_STATUS':
      return { ...state, connectionStatus: action.payload };

    case 'SET_TYPING_USER': {
      const { userId, isTyping } = action.payload;
      if (isTyping) {
        return {
          ...state,
          typingUsers: state.typingUsers.includes(userId)
            ? state.typingUsers
            : [...state.typingUsers, userId],
        };
      }
      return {
        ...state,
        typingUsers: state.typingUsers.filter((u) => u !== userId),
      };
    }

    case 'SET_SEARCH_RESULTS':
      return { ...state, searchResults: action.payload };

    default:
      return state;
  }
}

// ────────────────────────────────────────────────────────────────
//  Context
// ────────────────────────────────────────────────────────────────

interface ChatContextValue {
  state: ChatState;
  dispatch: React.Dispatch<ChatAction>;
  sendMessage: (content: string) => Promise<void>;
  loadConversations: () => Promise<void>;
  selectConversation: (conversationId: string) => Promise<void>;
  createConversation: (title?: string) => Promise<Conversation>;
  deleteConversation: (conversationId: string) => Promise<void>;
  searchConversations: (query: string) => Promise<void>;
}

const ChatContext = createContext<ChatContextValue | null>(null);

// ────────────────────────────────────────────────────────────────
//  Provider
// ────────────────────────────────────────────────────────────────

interface ChatProviderProps {
  children: ReactNode;
  userId: string;
}

export function ChatProvider({ children, userId }: ChatProviderProps) {
  const [state, dispatch] = useReducer(chatReducer, initialState);
  const connectionRef = useRef<HubConnection | null>(null);
  const dispatchRef = useRef(dispatch);
  const activeConversationRef = useRef<string | null>(null);

  // Keep refs up to date (avoids stale closures per research.md Topic 2)
  dispatchRef.current = dispatch;
  activeConversationRef.current = state.activeConversationId;

  // ─── SignalR Connection Setup ────────────────────────────────

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/chat')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    // ─── Server Event Handlers ─────────────────────────────────

    connection.on('MessageProcessing', (_data: { conversationId: string; messageId: string }) => {
      dispatchRef.current({ type: 'SET_PROCESSING', payload: true });
      dispatchRef.current({ type: 'SET_PROGRESS', payload: 'Processing your request...' });
    });

    connection.on(
      'MessageProgress',
      (data: { conversationId: string; messageId: string; step: string }) => {
        dispatchRef.current({ type: 'SET_PROGRESS', payload: data.step });
      }
    );

    connection.on(
      'MessageReceived',
      (data: { conversationId: string; message: ChatMessage }) => {
        dispatchRef.current({ type: 'ADD_MESSAGE', payload: data.message });
        dispatchRef.current({ type: 'SET_PROCESSING', payload: false });
      }
    );

    connection.on(
      'MessageError',
      (data: { conversationId: string; messageId: string; error: string; category: string }) => {
        dispatchRef.current({ type: 'SET_ERROR', payload: data.error });
        dispatchRef.current({
          type: 'UPDATE_MESSAGE_STATUS',
          payload: { id: data.messageId, status: MessageStatus.Error, error: data.error },
        });
        dispatchRef.current({ type: 'SET_PROCESSING', payload: false });
      }
    );

    connection.on('UserTyping', (data: { conversationId: string; userId: string }) => {
      dispatchRef.current({
        type: 'SET_TYPING_USER',
        payload: { userId: data.userId, isTyping: true },
      });
      // Clear typing after 3 seconds
      setTimeout(() => {
        dispatchRef.current({
          type: 'SET_TYPING_USER',
          payload: { userId: data.userId, isTyping: false },
        });
      }, 3000);
    });

    // ─── Connection Lifecycle ──────────────────────────────────

    connection.onreconnecting(() => {
      dispatchRef.current({
        type: 'SET_CONNECTION_STATUS',
        payload: ConnectionStatus.Reconnecting,
      });
    });

    connection.onreconnected(async () => {
      dispatchRef.current({
        type: 'SET_CONNECTION_STATUS',
        payload: ConnectionStatus.Connected,
      });
      // The useEffect watching connectionStatus + activeConversationId
      // will automatically re-join the active conversation group.
    });

    connection.onclose(() => {
      dispatchRef.current({
        type: 'SET_CONNECTION_STATUS',
        payload: ConnectionStatus.Disconnected,
      });
    });

    // ─── Start Connection ──────────────────────────────────────

    connection
      .start()
      .then(() => {
        dispatchRef.current({
          type: 'SET_CONNECTION_STATUS',
          payload: ConnectionStatus.Connected,
        });
      })
      .catch((err) => {
        console.error('SignalR connection failed:', err);
        dispatchRef.current({
          type: 'SET_CONNECTION_STATUS',
          payload: ConnectionStatus.Disconnected,
        });
      });

    return () => {
      connection.stop();
    };
  }, []);

  // ─── Auto-join SignalR group when active conversation changes ─
  // Handles: auto-created conversations (loadConversations never calls
  // selectConversation), connection established after conversation was
  // already set, and normal conversation switches. Double-joins are
  // harmless — Hub just re-adds to the same group.

  useEffect(() => {
    const conversationId = state.activeConversationId;
    const connection = connectionRef.current;
    if (
      !conversationId ||
      !connection ||
      connection.state !== HubConnectionState.Connected
    )
      return;

    connection.invoke('JoinConversation', conversationId).catch((err) => {
      console.error('Failed to auto-join conversation group:', err);
    });

    return () => {
      if (connection.state === HubConnectionState.Connected) {
        connection.invoke('LeaveConversation', conversationId).catch(() => {});
      }
    };
  }, [state.activeConversationId, state.connectionStatus]);

  // ─── Load Conversations on Mount ─────────────────────────────

  const loadConversations = useCallback(async () => {
    dispatch({ type: 'SET_LOADING', payload: true });
    try {
      const conversations = await chatApi.getConversations(userId);
      dispatch({ type: 'SET_CONVERSATIONS', payload: conversations });

      // Auto-create first conversation if none exist
      if (conversations.length === 0) {
        const newConv = await chatApi.createConversation({});
        dispatch({ type: 'ADD_CONVERSATION', payload: newConv });
        dispatch({ type: 'SET_ACTIVE_CONVERSATION', payload: newConv.id });
      }
    } catch (err) {
      console.error('Failed to load conversations:', err);
      dispatch({ type: 'SET_ERROR', payload: 'Failed to load conversations' });
    } finally {
      dispatch({ type: 'SET_LOADING', payload: false });
    }
  }, []);

  useEffect(() => {
    loadConversations();
  }, [loadConversations]);

  // ─── Actions ─────────────────────────────────────────────────

  const selectConversation = useCallback(
    async (conversationId: string) => {
      // Setting active conversation triggers the useEffect that handles
      // SignalR group leave (cleanup) + join (effect) automatically.
      dispatch({ type: 'SET_ACTIVE_CONVERSATION', payload: conversationId });
      dispatch({ type: 'SET_LOADING', payload: true });

      try {
        const messages = await chatApi.getMessages(conversationId);
        dispatch({ type: 'SET_MESSAGES', payload: messages });
      } catch (err) {
        console.error('Failed to load messages:', err);
        dispatch({ type: 'SET_ERROR', payload: 'Failed to load messages' });
      } finally {
        dispatch({ type: 'SET_LOADING', payload: false });
      }
    },
    [] // conversationId comes from the parameter; no reactive closure deps needed
  );

  const sendMessage = useCallback(
    async (content: string) => {
      if (!state.activeConversationId || !content.trim()) return;

      const optimisticId = crypto.randomUUID();

      // Optimistic UI update
      const optimisticMessage: ChatMessage = {
        id: optimisticId,
        conversationId: state.activeConversationId,
        content: content.trim(),
        role: MessageRole.User,
        timestamp: new Date().toISOString(),
        status: MessageStatus.Sending,
        metadata: {},
        tools: [],
      };

      dispatch({ type: 'ADD_MESSAGE', payload: optimisticMessage });
      dispatch({ type: 'SET_PROCESSING', payload: true });
      dispatch({ type: 'SET_PROGRESS', payload: 'Sending message to ATO Copilot...' });
      dispatch({ type: 'SET_ERROR', payload: null });

      try {
        const request: SendMessageRequest = {
          conversationId: state.activeConversationId,
          message: content.trim(),
        };

        // Send via SignalR so progress events fire back in real-time
        if (connectionRef.current?.state === HubConnectionState.Connected) {
          await connectionRef.current.invoke('SendMessage', request);
          // SignalR handlers (MessageReceived / MessageError) handle the response
          dispatch({
            type: 'UPDATE_MESSAGE_STATUS',
            payload: { id: optimisticId, status: MessageStatus.Completed },
          });
        } else {
          // Fallback to REST if SignalR is disconnected
          const response = await chatApi.sendMessage(request);

          dispatch({
            type: 'UPDATE_MESSAGE_STATUS',
            payload: { id: optimisticId, status: MessageStatus.Completed },
          });

          if (response.success) {
            const meta = response.metadata || {};
            // Inject suggestedActions into metadata for REST fallback path
            if (response.suggestedActions?.length) {
              meta.suggestedActions = response.suggestedActions;
            }
            const aiMessage: ChatMessage = {
              id: response.messageId,
              conversationId: state.activeConversationId,
              content: response.content,
              role: MessageRole.Assistant,
              timestamp: new Date().toISOString(),
              status: MessageStatus.Completed,
              metadata: meta,
              tools: response.recommendedTools || [],
            };
            dispatch({ type: 'ADD_MESSAGE', payload: aiMessage });
          } else {
            dispatch({ type: 'SET_ERROR', payload: response.error || 'Failed to send message' });
          }
          dispatch({ type: 'SET_PROCESSING', payload: false });
        }
      } catch (err) {
        dispatch({
          type: 'UPDATE_MESSAGE_STATUS',
          payload: { id: optimisticId, status: MessageStatus.Error, error: 'Failed to send' },
        });
        dispatch({ type: 'SET_ERROR', payload: 'Failed to send message' });
        dispatch({ type: 'SET_PROCESSING', payload: false });
      }
    },
    [state.activeConversationId]
  );

  const createConversationHandler = useCallback(
    async (title?: string): Promise<Conversation> => {
      const conversation = await chatApi.createConversation({ title });
      dispatch({ type: 'ADD_CONVERSATION', payload: conversation });
      return conversation;
    },
    []
  );

  const deleteConversationHandler = useCallback(async (conversationId: string) => {
    await chatApi.deleteConversation(conversationId);
    dispatch({ type: 'DELETE_CONVERSATION', payload: conversationId });
  }, []);

  const searchConversationsHandler = useCallback(async (query: string) => {
    if (!query.trim()) {
      dispatch({ type: 'SET_SEARCH_RESULTS', payload: [] });
      return;
    }
    const results = await chatApi.searchConversations(query, userId);
    dispatch({ type: 'SET_SEARCH_RESULTS', payload: results });
  }, []);

  const value: ChatContextValue = {
    state,
    dispatch,
    sendMessage,
    loadConversations,
    selectConversation,
    createConversation: createConversationHandler,
    deleteConversation: deleteConversationHandler,
    searchConversations: searchConversationsHandler,
  };

  return <ChatContext.Provider value={value}>{children}</ChatContext.Provider>;
}

// ────────────────────────────────────────────────────────────────
//  Hook
// ────────────────────────────────────────────────────────────────

export function useChatContext(): ChatContextValue {
  const context = useContext(ChatContext);
  if (!context) {
    throw new Error('useChatContext must be used within a ChatProvider');
  }
  return context;
}
