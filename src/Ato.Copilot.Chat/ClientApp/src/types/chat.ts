// ────────────────────────────────────────────────────────────────
//  Security Posture Intelligence Navigator Chat — TypeScript Type Definitions
//  Mirrors backend Models/ChatModels.cs exactly
// ────────────────────────────────────────────────────────────────

// ─── Enums ───────────────────────────────────────────────────────

export enum MessageRole {
  User = 'User',
  Assistant = 'Assistant',
  System = 'System',
  Tool = 'Tool',
}

export enum MessageStatus {
  Sending = 'Sending',
  Sent = 'Sent',
  Processing = 'Processing',
  Completed = 'Completed',
  Error = 'Error',
  Retry = 'Retry',
}

export enum AttachmentType {
  Document = 'Document',
  Image = 'Image',
  Code = 'Code',
  Configuration = 'Configuration',
  Log = 'Log',
}

// ─── Value Objects ───────────────────────────────────────────────

export interface ToolExecutionResult {
  toolName: string;
  success: boolean;
  result?: unknown;
  error?: string;
  parameters: Record<string, unknown>;
  executedAt: string; // ISO 8601
  duration: string;   // TimeSpan as string (e.g., "00:00:01.234")
}

// ─── Entities ────────────────────────────────────────────────────

export interface Conversation {
  id: string;
  title: string;
  userId: string;
  createdAt: string;
  updatedAt: string;
  isArchived: boolean;
  metadata: Record<string, unknown>;
  messages?: ChatMessage[];
  context?: ConversationContext;
}

export interface ChatMessage {
  id: string;
  conversationId: string;
  content: string;
  role: MessageRole;
  timestamp: string;
  status: MessageStatus;
  metadata: Record<string, unknown>;
  parentMessageId?: string;
  tools: string[];
  toolResult?: ToolExecutionResult;
  attachments?: MessageAttachment[];
}

export interface ConversationContext {
  id: string;
  conversationId: string;
  type: string;
  title: string;
  summary: string;
  data: Record<string, unknown>;
  createdAt: string;
  lastAccessedAt: string;
  tags: string[];
}

export interface MessageAttachment {
  id: string;
  messageId: string;
  fileName: string;
  contentType: string;
  size: number;
  storagePath: string;
  type: AttachmentType;
  uploadedAt: string;
  metadata: Record<string, unknown>;
}

// ─── Request DTOs ────────────────────────────────────────────────

export interface SendMessageRequest {
  conversationId: string;
  message: string;
  attachmentIds?: string[];
  context?: Record<string, unknown>;
}

export interface CreateConversationRequest {
  title?: string;
  userId?: string;
}

// ─── Response DTOs ───────────────────────────────────────────────

export interface ChatResponse {
  messageId: string;
  content: string;
  success: boolean;
  error?: string;
  suggestedActions?: SuggestedAction[];
  recommendedTools?: string[];
  metadata?: Record<string, unknown>;
}

export interface ErrorResponse {
  message: string;
  error?: string;
  suggestion?: string;
}

// ─── Suggested Actions ───────────────────────────────────────────

export interface SuggestedAction {
  title: string;
  description: string;
  prompt: string;
  priority?: string;
}

// ─── State Management ────────────────────────────────────────────

export interface ChatState {
  conversations: Conversation[];
  activeConversationId: string | null;
  messages: ChatMessage[];
  isLoading: boolean;
  isProcessing: boolean;
  progressMessage: string | null;
  error: string | null;
  connectionStatus: ConnectionStatus;
  typingUsers: string[];
  searchResults: Conversation[];
}

export enum ConnectionStatus {
  Connected = 'Connected',
  Reconnecting = 'Reconnecting',
  Disconnected = 'Disconnected',
}

export type ChatAction =
  | { type: 'SET_CONVERSATIONS'; payload: Conversation[] }
  | { type: 'ADD_CONVERSATION'; payload: Conversation }
  | { type: 'DELETE_CONVERSATION'; payload: string }
  | { type: 'SET_ACTIVE_CONVERSATION'; payload: string | null }
  | { type: 'SET_MESSAGES'; payload: ChatMessage[] }
  | { type: 'ADD_MESSAGE'; payload: ChatMessage }
  | { type: 'UPDATE_MESSAGE_STATUS'; payload: { id: string; status: MessageStatus; error?: string } }
  | { type: 'SET_LOADING'; payload: boolean }
  | { type: 'SET_PROCESSING'; payload: boolean }
  | { type: 'SET_PROGRESS'; payload: string | null }
  | { type: 'SET_ERROR'; payload: string | null }
  | { type: 'SET_CONNECTION_STATUS'; payload: ConnectionStatus }
  | { type: 'SET_TYPING_USER'; payload: { userId: string; isTyping: boolean } }
  | { type: 'SET_SEARCH_RESULTS'; payload: Conversation[] };
