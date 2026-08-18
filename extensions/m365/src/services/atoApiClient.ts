/**
 * ATO API Client Service (FR-049)
 *
 * HTTP client for communicating with the ATO Copilot MCP Server.
 * - 300s timeout
 * - User-Agent: ATO-Copilot-M365-Extension/1.0.0
 * - Generates m365-{timestamp}-{random9} conversationIds
 *
 * ToolExecution, ErrorDetail, and McpResponse are now sourced from
 * @ato-copilot/shared (#2683) to eliminate the three-way duplication.
 */

import axios, { AxiosInstance, AxiosError } from "axios";

// Shared canonical types (#2683)
export type { ToolExecution, ErrorDetail, McpResponse } from "@ato-copilot/shared";
import type { McpResponse } from "@ato-copilot/shared";

export interface McpRequest {
  conversationId: string;
  message: string;
  conversationHistory: Array<{ role: string; content: string }>;
  context: {
    source: string;
    platform: string;
    userId?: string;
    userName?: string;
  };
}

export class ATOApiClient {
  private client: AxiosInstance;

  constructor(baseUrl: string, apiKey?: string) {
    const headers: Record<string, string> = {
      "Content-Type": "application/json",
      "User-Agent": "ATO-Copilot-M365-Extension/1.0.0",
    };

    if (apiKey) {
      headers["Authorization"] = `Bearer ${apiKey}`;
    }

    this.client = axios.create({
      baseURL: baseUrl,
      timeout: 300_000, // 300 seconds
      headers,
    });
  }

  /**
   * Generate a unique conversation ID in m365-{timestamp}-{random9} format.
   */
  static generateConversationId(): string {
    const timestamp = Date.now();
    const random = Math.random().toString(36).substring(2, 11).padEnd(9, "0");
    return `m365-${timestamp}-${random}`;
  }

  /**
   * Send a message to the MCP server and get a response.
   * Supports optional action/actionContext for Action.Submit payloads (FR-014b).
   */
  async sendMessage(
    text: string,
    conversationId: string,
    userId: string,
    userName?: string,
    action?: string,
    actionContext?: Record<string, unknown>
  ): Promise<McpResponse> {
    const request: McpRequest & { action?: string; actionContext?: Record<string, unknown> } = {
      conversationId,
      message: text,
      conversationHistory: this.getConversationHistory(conversationId),
      context: {
        source: "m365-copilot",
        platform: "M365",
        userId,
        userName,
      },
    };

    if (action) request.action = action;
    if (actionContext) request.actionContext = actionContext;

    const startTime = Date.now();
    const response = await this.client.post<McpResponse>(
      "/mcp/chat",
      request
    );

    const elapsed = Date.now() - startTime;
    console.info(
      `[ATOApiClient] POST /mcp/chat | ${conversationId} | ${text.length} chars | ${elapsed}ms | intent=${response.data.intentType ?? "unknown"}`
    );

    if (response.data.errors && response.data.errors.length > 0) {
      for (const err of response.data.errors) {
        console.error(
          `[ATOApiClient] Error: ${err.errorCode} | ${conversationId} | ${err.message}${err.suggestion ? ` | suggestion=${err.suggestion}` : ""}`
        );
      }
    }

    // Track conversation history (FR-014d)
    this.addToConversationHistory(conversationId, "user", text);
    this.addToConversationHistory(conversationId, "assistant", response.data.response);

    return response.data;
  }

  // --- Conversation history (FR-014d, R-011) ---

  private conversationHistories = new Map<string, Array<{ role: string; content: string }>>();
  private static readonly MAX_HISTORY_EXCHANGES = 20;

  /**
   * Get conversation history for a given conversationId.
   */
  private getConversationHistory(conversationId: string): Array<{ role: string; content: string }> {
    return this.conversationHistories.get(conversationId) ?? [];
  }

  /**
   * Add a message to conversation history, capped at MAX_HISTORY_EXCHANGES pairs.
   */
  private addToConversationHistory(conversationId: string, role: string, content: string): void {
    if (!this.conversationHistories.has(conversationId)) {
      this.conversationHistories.set(conversationId, []);
    }
    const history = this.conversationHistories.get(conversationId)!;
    history.push({ role, content });

    // Cap at 20 exchanges (40 messages: 20 user + 20 assistant)
    const maxMessages = ATOApiClient.MAX_HISTORY_EXCHANGES * 2;
    if (history.length > maxMessages) {
      history.splice(0, history.length - maxMessages);
    }
  }

  /**
   * Check MCP server health.
   */
  async checkHealth(): Promise<boolean> {
    try {
      const response = await this.client.get("/health");
      return response.status === 200;
    } catch {
      return false;
    }
  }
}
