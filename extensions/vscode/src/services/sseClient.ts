/**
 * SSE (Server-Sent Events) client for VS Code extension (FR-029a, FR-029e).
 *
 * SseEvent, SseEventHandler, and parseSseChunk are now sourced from
 * @ato-copilot/shared (#2683) to eliminate the three-way duplication.
 * The SseClient class implementation remains extension-specific (different
 * constructor signature — no baseUrl, URL passed per-call).
 */

// Re-export canonical types from shared package — callers get the same type
// regardless of which import path they use.
export type { SseEvent, SseEventHandler } from "@ato-copilot/shared";
export { parseSseChunk } from "@ato-copilot/shared";

import type { SseEvent, SseEventHandler } from "@ato-copilot/shared";
import { parseSseChunk } from "@ato-copilot/shared";

/**
 * SSE client configuration.
 */
export interface SseClientOptions {
  /** Maximum number of retry attempts (default: 3) */
  maxRetries?: number;
  /** Initial retry delay in milliseconds (default: 1000) */
  initialRetryDelayMs?: number;
  /** Maximum retry delay in milliseconds (default: 30000) */
  maxRetryDelayMs?: number;
  /** Request timeout in milliseconds (default: 120000) */
  timeoutMs?: number;
}

/**
 * SSE client for streaming responses from MCP Server.
 */
export class SseClient {
  private readonly maxRetries: number;
  private readonly initialRetryDelayMs: number;
  private readonly maxRetryDelayMs: number;
  private readonly timeoutMs: number;

  constructor(options?: SseClientOptions) {
    this.maxRetries = options?.maxRetries ?? 3;
    this.initialRetryDelayMs = options?.initialRetryDelayMs ?? 1000;
    this.maxRetryDelayMs = options?.maxRetryDelayMs ?? 30000;
    this.timeoutMs = options?.timeoutMs ?? 120000;
  }

  /**
   * Open an SSE stream and dispatch events to the handler.
   * Returns when the stream completes or is aborted.
   *
   * @param url - Full URL to the SSE endpoint (e.g. http://localhost:3001/mcp/chat/stream)
   * @param body - Request body for POST
   * @param headers - HTTP headers (must include Content-Type and Authorization)
   * @param onEvent - Callback for each parsed SSE event
   * @param abortController - AbortController for cancellation (Constitution VIII)
   * @returns true if stream completed, false if fallback is needed
   */
  public async stream(
    url: string,
    body: unknown,
    headers: Record<string, string>,
    onEvent: SseEventHandler,
    abortController?: AbortController
  ): Promise<boolean> {
    let retries = 0;
    let delay = this.initialRetryDelayMs;

    while (retries <= this.maxRetries) {
      try {
        const controller = abortController ?? new AbortController();

        // Set timeout
        const timeoutId = setTimeout(() => controller.abort(), this.timeoutMs);

        const response = await fetch(url, {
          method: "POST",
          headers: {
            ...headers,
            Accept: "text/event-stream",
          },
          body: JSON.stringify(body),
          signal: controller.signal,
        });

        clearTimeout(timeoutId);

        if (!response.ok) {
          throw new Error(`SSE request failed: ${response.status}`);
        }

        if (!response.body) {
          throw new Error("SSE response has no body");
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = "";

        while (true) {
          const { done, value } = await reader.read();

          if (done) {
            // Process remaining buffer
            if (buffer.trim()) {
              const events = parseSseChunk(buffer);
              for (const event of events) {
                onEvent(event);
              }
            }
            return true;
          }

          buffer += decoder.decode(value, { stream: true });

          // Process complete events (separated by \n\n)
          const parts = buffer.split("\n\n");
          // Keep the last potentially incomplete part in the buffer
          buffer = parts.pop() ?? "";

          for (const part of parts) {
            if (part.trim()) {
              const events = parseSseChunk(part + "\n\n");
              for (const event of events) {
                onEvent(event);
              }
            }
          }
        }
      } catch (error) {
        if (abortController?.signal.aborted) {
          // User-initiated cancellation — don't retry
          return false;
        }

        retries++;
        if (retries > this.maxRetries) {
          // Exhausted retries — caller should fall back to sync
          return false;
        }

        // Exponential backoff
        await new Promise((resolve) => setTimeout(resolve, delay));
        delay = Math.min(delay * 2, this.maxRetryDelayMs);
      }
    }

    return false;
  }
}
