import type {
  ChatRequest,
  SseProgressEvent,
  SseResultEvent,
  SseErrorEvent,
} from '../types/chat';

/** Parsed SSE event (ported from extensions/vscode/src/services/sseClient.ts). */
export interface SseEvent {
  event: string;
  data: string;
  id?: string;
  timestamp?: string;
}

/**
 * Parse a raw SSE chunk into events.
 * SSE format: lines separated by \n, events separated by \n\n.
 * Fields: event:, data:, id:
 */
export function parseSseChunk(chunk: string): SseEvent[] {
  const events: SseEvent[] = [];
  const blocks = chunk.split('\n\n').filter((b) => b.trim().length > 0);

  for (const block of blocks) {
    const lines = block.split('\n');
    let eventType = 'message';
    let data = '';
    let id: string | undefined;

    for (const line of lines) {
      if (line.startsWith('event:')) {
        eventType = line.substring(6).trim();
      } else if (line.startsWith('data:')) {
        data = line.substring(5).trim();
      } else if (line.startsWith('id:')) {
        id = line.substring(3).trim();
      }
      // Comment lines (starting with ':') are ignored (keepalive)
    }

    if (data) {
      const event: SseEvent = { event: eventType, data };
      if (id) {
        event.id = id;
      }
      try {
        const parsed = JSON.parse(data);
        if (parsed.timestamp) {
          event.timestamp = parsed.timestamp;
        }
      } catch {
        // Not JSON — leave timestamp undefined
      }
      events.push(event);
    }
  }

  return events;
}

const SSE_TIMEOUT_MS = 120_000;

/**
 * Send a chat message via SSE streaming.
 *
 * POST to /mcp/chat/stream with ChatRequest body.
 * Dispatches parsed SSE events to the provided callbacks.
 */
export async function sendMessage(
  request: ChatRequest,
  onProgress: (event: SseProgressEvent) => void,
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
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      Accept: 'text/event-stream',
    };

    const token = localStorage.getItem('auth_token');
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }

    const response = await fetch(url, {
      method: 'POST',
      headers,
      body: JSON.stringify(request),
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
          processBuffer(buffer, onProgress, onResult, onError);
        }
        return;
      }

      buffer += decoder.decode(value, { stream: true });

      // Process complete events (separated by \n\n)
      const parts = buffer.split('\n\n');
      buffer = parts.pop() ?? '';

      for (const part of parts) {
        if (part.trim()) {
          processBuffer(part + '\n\n', onProgress, onResult, onError);
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
