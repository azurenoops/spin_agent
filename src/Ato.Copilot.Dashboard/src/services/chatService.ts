import type {
  ChatRequest,
  SseProgressEvent,
  SseResultEvent,
  SseErrorEvent,
  SseMcpToolEvent,
} from '../types/chat';
import { acquireBearer } from '../features/auth/msalInstance';

// Shared canonical types — eliminates duplication with extensions (#2683).
export type { SseEvent } from '@ato-copilot/shared';
export { parseSseChunk } from '@ato-copilot/shared';

import { parseSseChunk } from '@ato-copilot/shared';

const SSE_TIMEOUT_MS = 120_000;

/**
 * Send a chat message via SSE streaming.
 *
 * POST to /mcp/chat/stream with ChatRequest body.
 * Dispatches parsed SSE events to the provided callbacks.
 *
 * T270: Added onToolEvent callback for MCP tool start/end chips.
 */
export async function sendMessage(
  request: ChatRequest,
  onProgress: (event: SseProgressEvent) => void,
  onToolEvent: (event: SseMcpToolEvent) => void,
  onResult: (event: SseResultEvent) => void,
  onError: (error: SseErrorEvent | Error) => void,
  abortSignal?: AbortSignal,
): Promise<void> {
  const baseUrl = import.meta.env.VITE_MCP_BASE_URL || '/api';
  const url = `${baseUrl}/mcp/chat/stream`;

  const controller = new AbortController();
  const signal = abortSignal
    ? abortSignal
    : controller.signal;

  // Link external abort signal to our controller
  if (abortSignal) {
    abortSignal.addEventListener('abort', () => controller.abort(), { once: true });
  }

  const timeoutId = setTimeout(() => controller.abort(), SSE_TIMEOUT_MS);

  try {
    // T006 (052-api-mismatch-fixes #141): use FormData multipart when attachments present
    const hasAttachments = request.attachments && request.attachments.length > 0;

    const headers: Record<string, string> = {
      // Do NOT set Content-Type for multipart — browser sets boundary automatically
      ...(hasAttachments ? {} : { 'Content-Type': 'application/json' }),
      Accept: 'text/event-stream',
    };

    // Feature 051 T053: MSAL-backed bearer; empty string when no account.
    const token = await acquireBearer();
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }

    let body: BodyInit;
    if (hasAttachments) {
      const form = new FormData();
      form.append('message', request.message);
      if (request.conversationId) form.append('conversationId', request.conversationId);
      request.attachments!.forEach((file) => form.append('attachment[]', file));
      body = form;
    } else {
      body = JSON.stringify(request);
    }

    const response = await fetch(url, {
      method: 'POST',
      headers,
      body,
      signal,
    });

    clearTimeout(timeoutId);

    if (!response.ok) {
      throw new Error(`Chat request failed: ${response.status} ${response.statusText}`);
    }

    if (!response.body) {
      throw new Error('Response has no body');
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
      const { done, value } = await reader.read();

      if (done) {
        // Process remaining buffer
        if (buffer.trim()) {
          processBuffer(buffer, onProgress, onToolEvent, onResult, onError);
        }
        return;
      }

      buffer += decoder.decode(value, { stream: true });

      // Process complete events (separated by \n\n)
      const parts = buffer.split('\n\n');
      buffer = parts.pop() ?? '';

      for (const part of parts) {
        if (part.trim()) {
          processBuffer(part + '\n\n', onProgress, onToolEvent, onResult, onError);
        }
      }
    }
  } catch (error) {
    clearTimeout(timeoutId);

    if (signal.aborted) {
      return; // User-initiated cancellation — silent
    }

    onError(
      error instanceof Error
        ? error
        : new Error(String(error)),
    );
  }
}

function processBuffer(
  chunk: string,
  onProgress: (event: SseProgressEvent) => void,
  onToolEvent: (event: SseMcpToolEvent) => void,
  onResult: (event: SseResultEvent) => void,
  onError: (error: SseErrorEvent | Error) => void,
): void {
  const events = parseSseChunk(chunk);
  for (const event of events) {
    try {
      const parsed = JSON.parse(event.data);
      // Server sends type in JSON data field (not SSE event: field)
      const eventType = event.event !== 'message' ? event.event : parsed.type;
      switch (eventType) {
        case 'progress':
          onProgress(parsed as SseProgressEvent);
          break;
        case 'tool_start':
          // T270: MCP tool invocation start chip
          onToolEvent({ phase: 'start', toolName: parsed.toolName ?? parsed.tool ?? 'tool' });
          break;
        case 'tool_end':
          // T270: MCP tool invocation end — remove chip
          onToolEvent({ phase: 'end', toolName: parsed.toolName ?? parsed.tool ?? 'tool' });
          break;
        case 'result':
          // Server wraps result as { type: "result", data: {...} }
          onResult((parsed.data ?? parsed) as SseResultEvent);
          break;
        case 'error':
          onError((parsed.data ?? parsed) as SseErrorEvent);
          break;
        // 'message' and unknown types are ignored
      }
    } catch {
      // Non-JSON data — ignore
    }
  }
}
