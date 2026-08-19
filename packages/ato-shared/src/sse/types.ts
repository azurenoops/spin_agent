/**
 * Canonical SSE event types for @ato-copilot/shared (#2683).
 *
 * Field name 'event' matches the raw SSE wire protocol and the existing
 * VS Code + Dashboard implementations. The M365 extension previously used
 * 'type' — that divergence is resolved here (#2683, step 1).
 */

/** Parsed SSE event. Field names match the SSE wire spec (RFC 8895). */
export interface SseEvent {
  /** Value of the SSE 'event:' field. Defaults to 'message' when absent. */
  event: string;
  /** Raw string value of the SSE 'data:' field. */
  data: string;
  /** Optional SSE 'id:' field. */
  id?: string;
  /** ISO 8601 timestamp extracted from JSON data, or set at parse time. */
  timestamp?: string;
}

/** Callback invoked for each parsed SSE event. */
export type SseEventHandler = (event: SseEvent) => void;

/**
 * Parse a raw SSE chunk into typed events.
 *
 * SSE format per RFC 8895:
 *   Lines separated by \n; events separated by \n\n.
 *   Supported fields: event, data, id.
 *   Lines starting with ':' are comments (keepalive) and are ignored.
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
      // Lines starting with ':' are comments — ignore
    }

    if (data) {
      const evt: SseEvent = { event: eventType, data };
      if (id) {
        evt.id = id;
      }
      try {
        const parsed: unknown = JSON.parse(data);
        if (
          parsed !== null &&
          typeof parsed === 'object' &&
          'timestamp' in parsed &&
          typeof (parsed as Record<string, unknown>).timestamp === 'string'
        ) {
          evt.timestamp = (parsed as Record<string, unknown>).timestamp as string;
        }
      } catch {
        // Not JSON — leave timestamp undefined
      }
      events.push(evt);
    }
  }

  return events;
}
