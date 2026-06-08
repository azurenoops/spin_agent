// Chat entity types — per data-model.md and contracts/

export type MessageStatus = 'sending' | 'streaming' | 'complete' | 'error';
export type MessageRole = 'user' | 'assistant';
export type FileAttachmentType = 'stig-ckl' | 'stig-xccdf' | 'prisma-csv' | 'nessus' | 'unknown';

export interface ToolExecution {
  toolName: string;
  success: boolean;
  executionTimeMs: number;
  result?: string | null;
}

export interface ErrorDetail {
  errorCode: string;
  message: string;
  suggestion?: string | null;
}

export interface SuggestedAction {
  label: string;
  prompt: string;
  icon?: string | null;
}

export interface ChatContext {
  page: string;
  systemId?: string | null;
  boundaryId?: string | null;
  entityType?: string | null;
  entityId?: string | null;
  rmfPhase?: string | null;
  systemName?: string | null;
  pageData?: PageData | null;
}

/** Live metrics from the current page for intelligent chat guidance. */
export interface PageData {
  complianceScore?: number;
  narrativeCoverage?: number;
  catIFindings?: number;
  catIIFindings?: number;
  catIIIFindings?: number;
  totalFindings?: number;
  openPoams?: number;
  overduePoams?: number;
  atoStatus?: string;
  atoDaysRemaining?: number | null;
  baselineLevel?: string;
  hasCategorization?: boolean;
  hasBaseline?: boolean;
  boundaryCount?: number;
  roleCount?: number;
  phaseCompletionPercent?: number;
  // Deviation & outstanding-info metrics (Feature 035)
  pendingDeviations?: number;
  expiringDeviations?: number;
  catIDeviations?: number;
  deviationsMissingEvidence?: number;
  // Outstanding-info urgency triggers (T272, Feature 035 — T023)
  catIWithoutDeviationOrRemediation?: number;
  draftSspSections?: number;
  missingDocDueDates?: number;
  poamMissingCompletionDates?: number;
  authDecisionMissingExpiry?: number;
}

/** T260: Lightweight file metadata for rendering in the chat thread. */
export interface AttachedFileInfo {
  name: string;
  size: number;
  type: string;
}

export interface FileAttachment {
  id: string;
  file?: File;
  fileName: string;
  fileSize: number;
  fileType: FileAttachmentType;
  uploadedAt: string;
  url?: string;
}

export interface Message {
  id: string;
  role: MessageRole;
  content: string;
  status: MessageStatus;
  timestamp: string;
  agentName?: string;
  intentType?: string;
  processingTimeMs?: number;
  toolsExecuted?: ToolExecution[];
  errors?: ErrorDetail[];
  suggestedActions?: SuggestedAction[];
  requiresFollowUp?: boolean;
  /** T260: Files attached to this user message for display purposes. */
  attachments?: AttachedFileInfo[];
}

export interface Conversation {
  id: string;
  title: string;
  messages: Message[];
  createdAt: string;
  updatedAt: string;
  context: ChatContext | null;
}

export interface ChatPanelState {
  isOpen: boolean;
  width: number;
  activeConversationId: string | null;
}

// SSE event types — per contracts/sse-chat-stream.md

export interface SseProgressEvent {
  step: string;
  detail: string;
  timestamp: string;
}

/** T270: MCP tool invocation event — emitted by server as tool_start / tool_end SSE events. */
export interface SseMcpToolEvent {
  phase: 'start' | 'end';
  toolName: string;
}

export interface SseResultEvent {
  success: boolean;
  response: string;
  conversationId: string;
  agentUsed: string;
  intentType: string;
  processingTimeMs: number;
  toolsExecuted: ToolExecution[];
  errors: ErrorDetail[];
  suggestedActions: SuggestedAction[];
  requiresFollowUp: boolean;
}

export interface SseErrorEvent {
  errorCode: string;
  message: string;
  suggestion?: string | null;
}

// Chat request types — per contracts/sse-chat-stream.md

export interface ConversationHistoryEntry {
  role: 'user' | 'assistant';
  content: string;
}

export interface ChatRequest {
  message: string;
  conversationId: string | null;
  context: Record<string, unknown> | null;
  conversationHistory: ConversationHistoryEntry[];
  action: string | null;
  actionContext: Record<string, unknown> | null;
  /** Files to attach. When present, chatService sends multipart/form-data instead of JSON. (T006, #141) */
  attachments?: File[];
}
