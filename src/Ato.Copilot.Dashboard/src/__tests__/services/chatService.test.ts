import { describe, it, expect } from 'vitest';
import { parseSseChunk } from '../../services/chatService';

describe('parseSseChunk', () => {
  it('parses a single progress event', () => {
    const chunk = 'event: progress\ndata: {"step":"Analyzing","detail":"Routing","timestamp":"2025-01-01T00:00:00Z"}\n\n';
    const events = parseSseChunk(chunk);
    expect(events).toHaveLength(1);
    expect(events[0]!.event).toBe('progress');
    expect(events[0]!.timestamp).toBe('2025-01-01T00:00:00Z');
  });

  it('parses a result event with all fields', () => {
    const data = JSON.stringify({
      Success: true,
      Response: '# Hello',
      ConversationId: 'abc',
      AgentName: 'ComplianceAgent',
      IntentType: 'query',
      ProcessingTimeMs: 1234,
      ToolsExecuted: [],
      Errors: [],
      SuggestedActions: [],
      RequiresFollowUp: false,
    });
    const chunk = `event: result\ndata: ${data}\n\n`;
    const events = parseSseChunk(chunk);
    expect(events).toHaveLength(1);
    expect(events[0]!.event).toBe('result');
    const parsed = JSON.parse(events[0]!.data);
    expect(parsed.Success).toBe(true);
    expect(parsed.Response).toBe('# Hello');
  });

  it('parses an error event', () => {
    const chunk = 'event: error\ndata: {"ErrorCode":"AGENT_TIMEOUT","Message":"Timed out","Suggestion":"Retry"}\n\n';
    const events = parseSseChunk(chunk);
    expect(events).toHaveLength(1);
    expect(events[0]!.event).toBe('error');
  });

  it('parses multiple events in one chunk', () => {
    const chunk = [
      'event: progress\ndata: {"step":"Step 1","detail":"d","timestamp":"t1"}',
      'event: progress\ndata: {"step":"Step 2","detail":"d","timestamp":"t2"}',
      'event: result\ndata: {"Success":true,"Response":"done"}',
    ].join('\n\n') + '\n\n';
    const events = parseSseChunk(chunk);
    expect(events).toHaveLength(3);
    expect(events[0]!.event).toBe('progress');
    expect(events[1]!.event).toBe('progress');
    expect(events[2]!.event).toBe('result');
  });

  it('ignores keepalive comments', () => {
    const chunk = ': keepalive\n\nevent: progress\ndata: {"step":"s","detail":"d","timestamp":"t"}\n\n';
    const events = parseSseChunk(chunk);
    expect(events).toHaveLength(1);
    expect(events[0]!.event).toBe('progress');
  });

  it('returns empty array for empty chunk', () => {
    expect(parseSseChunk('')).toHaveLength(0);
    expect(parseSseChunk('\n\n')).toHaveLength(0);
  });

  it('ignores blocks with no data field', () => {
    const chunk = 'event: progress\n\n';
    const events = parseSseChunk(chunk);
    expect(events).toHaveLength(0);
  });

  it('handles non-JSON data gracefully', () => {
    const chunk = 'event: message\ndata: plain text\n\n';
    const events = parseSseChunk(chunk);
    expect(events).toHaveLength(1);
    expect(events[0]!.data).toBe('plain text');
    expect(events[0]!.timestamp).toBeUndefined();
  });

  it('extracts id field', () => {
    const chunk = 'event: progress\nid: evt-42\ndata: {"step":"s","detail":"d","timestamp":"t"}\n\n';
    const events = parseSseChunk(chunk);
    expect(events).toHaveLength(1);
    expect(events[0]!.id).toBe('evt-42');
  });

  it('defaults event type to message when not specified', () => {
    const chunk = 'data: {"value":1}\n\n';
    const events = parseSseChunk(chunk);
    expect(events).toHaveLength(1);
    expect(events[0]!.event).toBe('message');
  });
});
