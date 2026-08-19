/**
 * Canonical MCP API response types for @ato-copilot/shared (#2683).
 *
 * These types are the authoritative source of truth, replacing the three
 * independent copies that existed in:
 *   - extensions/vscode/src/services/mcpClient.ts   (McpChatResponse)
 *   - extensions/m365/src/services/atoApiClient.ts  (McpResponse)
 *   - src/Ato.Copilot.Dashboard/src/types/chat.ts   (ToolExecution subset)
 *
 * Consumers that need richer shapes (e.g. VS Code's McpChatRequest with full
 * context metadata) extend these base types locally.
 */

/** Tool execution record from MCP response (FR-002). */
export interface ToolExecution {
  toolName: string;
  success: boolean;
  executionTimeMs: number;
  /** Optional summary — present in VS Code extension responses. */
  resultSummary?: string;
}

/** Structured error detail (FR-007, Constitution VII). */
export interface ErrorDetail {
  errorCode: string;
  message: string;
  suggestion?: string;
}

/** Enriched response from MCP Server. */
export interface McpResponse {
  response: string;
  success?: boolean;
  agentUsed?: string;
  intentType?: string;
  conversationId?: string;
  processingTimeMs?: number;
  data?: Record<string, unknown>;
  toolsExecuted?: ToolExecution[];
  suggestions?: string[];
  requiresFollowUp?: boolean;
  followUpPrompt?: string;
  missingFields?: string[];
  errors?: ErrorDetail[];
}

/**
 * Generate a conversation ID in the format used by the M365 extension:
 *   m365-{timestamp}-{random9}
 *
 * Extracted from ATOApiClient.generateConversationId() to allow deduplication
 * across extensions.
 */
export function generateConversationId(prefix: string = 'ato'): string {
  const timestamp = Date.now();
  const random = Math.random().toString(36).substring(2, 11);
  return `${prefix}-${timestamp}-${random}`;
}
