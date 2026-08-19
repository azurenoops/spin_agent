/**
 * SSE Client (FR-029d, FR-029e, R-005, Constitution VIII)
 *
 * Server-Sent Events client for streaming MCP responses.
 * - Native fetch + ReadableStream
 * - Line-based SSE parser (from @ato-copilot/shared)
 * - Event type dispatch
 * - Retry with exponential backoff
 * - AbortController support for cancellation
 *
 * SseEvent, SseEventHandler, and parseSseChunk are now sourced from
 * @ato-copilot/shared (#2683) to eliminate the three-way duplication.
 * The SseEvent.event field (canonical name) matches the SSE wire spec.
 */

import type { McpResponse } from "./atoApiClient";

// Shared canonical types and parser (#2683)
export type { SseEvent, SseEventHandler } from "@ato-copilot/shared";
export { parseSseChunk } from "@ato-copilot/shared";

import type { SseEvent, SseEventHandler } from "@ato-copilot/shared";
import { parseSseChunk } from "@ato-copilot/shared";

export interface SseClientOptions {
  baseUrl: string;
  apiKey?: string;
  maxRetries?: number;
  initialRetryDelayMs?: number;
  maxRetryDelayMs?: number;
  timeoutMs?: number;
}

const DEFAULT_MAX_RETRIES = 3;
const DEFAULT_INITIAL_RETRY_DELAY_MS = 1000;
const DEFAULT_MAX_RETRY_DELAY_MS = 30000;
const DEFAULT_TIMEOUT_MS = 300_000;

/**
 * SSE Client for streaming MCP responses.
 */
export class SseClient {
  private options: Required<SseClientOptions>;

  constructor(options: SseClientOptions) {
    this.options = {
      baseUrl: options.baseUrl,
      apiKey: options.apiKey ?? "",
      maxRetries: options.maxRetries ?? DEFAULT_MAX_RETRIES,
      initialRetryDelayMs: options.initialRetryDelayMs ?? DEFAULT_INITIAL_RETRY_DELAY_MS,
      maxRetryDelayMs: options.maxRetryDelayMs ?? DEFAULT_MAX_RETRY_DELAY_MS,
      timeoutMs: options.timeoutMs ?? DEFAULT_TIMEOUT_MS,
    };
  }

  /**
   * Stream an SSE request to the MCP server.
   * Calls onEvent for each received event.
   * Returns the final McpResponse on "complete" event, or null on error/abort.
   */
  async stream(
    requestBody: Record<string, unknown>,
    onEvent: SseEventHandler,
    abortController?: AbortController
  ): Promise<McpResponse | null> {
    const controller = abortController ?? new AbortController();
    const headers: Record<string, string> = {
      "Content-Type": "application/json",
      Accept: "text/event-stream",
      "User-Agent": "ATO-Copilot-M365-Extension/1.0.0",
    };

    if (this.options.apiKey) {
      headers["Authorization"] = `Bearer ${this.options.apiKey}`;
    }

    let finalResponse: McpResponse | null = null;

    for (let attempt = 0; attempt <= this.options.maxRetries; attempt++) {
      try {
        // Timeout via AbortController
        const timeoutId = setTimeout(() => controller.abort(), this.options.timeoutMs);

        const response = await fetch(`${this.options.baseUrl}/mcp/chat/stream`, {
          method: "POST",
          headers,
          body: JSON.stringify(requestBody),
          signal: controller.signal,
        });

        clearTimeout(timeoutId);

        if (!response.ok) {
          throw new Error(`SSE request failed: ${response.status} ${response.statusText}`);
        }

        const reader = response.body?.getReader();
        if (!reader) {
          throw new Error("No readable stream in SSE response");
        }

        const decoder = new TextDecoder();
        let buffer = "";

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          buffer += decoder.decode(value, { stream: true });

          // Process complete SSE blocks (terminated by \n\n)
          while (buffer.includes("\n\n")) {
            const blockEnd = buffer.indexOf("\n\n") + 2;
            const block = buffer.slice(0, blockEnd);
            buffer = buffer.slice(blockEnd);

            const events = parseSseChunk(block);
            for (const event of events) {
              onEvent(event);

              if (event.event === "complete" || event.event === "error") {
                try {
                  finalResponse = JSON.parse(event.data) as McpResponse;
                } catch {
                  // data not JSON — leave finalResponse as-is
                }
              }
            }
          }
        }

        // Process any remaining buffer
        if (buffer.trim()) {
          const events = parseSseChunk(buffer);
          for (const event of events) {
            onEvent(event);
            if (event.event === "complete") {
              try {
                finalResponse = JSON.parse(event.data) as McpResponse;
              } catch {
                // not JSON — leave finalResponse as-is
              }
            }
          }
        }

        return finalResponse;
      } catch (error) {
        if (controller.signal.aborted) {
          console.info("[SseClient] Request aborted");
          return null;
        }

        if (attempt < this.options.maxRetries) {
          const delay = Math.min(
            this.options.initialRetryDelayMs * Math.pow(2, attempt),
            this.options.maxRetryDelayMs
          );
          console.warn(
            `[SseClient] Retry ${attempt + 1}/${this.options.maxRetries} in ${delay}ms`,
            error
          );
          await new Promise((resolve) => setTimeout(resolve, delay));
        } else {
          console.error("[SseClient] All retries exhausted, falling back to sync", error);
          return null;
        }
      }
    }

    return null;
  }
}
