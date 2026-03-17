# Contract: SSE Chat Streaming

**Feature**: 034-dashboard-chat | **Date**: 2025-03-16
**Endpoint**: `POST /mcp/chat/stream` (existing — no backend changes)

## Request

```typescript
/**
 * Chat request sent to the MCP Server SSE endpoint.
 * Maps to the existing C# ChatRequest model.
 */
interface ChatRequest {
  /** User's message text */
  Message: string;
  /** Conversation ID for multi-turn context (null for new conversations) */
  ConversationId: string | null;
  /** JSON-encoded dashboard context (page, systemId, boundaryId, etc.) */
  Context: string | null;
  /** Previous messages for multi-turn context (max 20) */
  ConversationHistory: ConversationHistoryEntry[];
  /** Optional action to trigger (e.g., "import-stig") */
  Action: string | null;
  /** Optional action parameters (e.g., file content, import options) */
  ActionContext: Record<string, unknown> | null;
}

interface ConversationHistoryEntry {
  Role: 'user' | 'assistant';
  Content: string;
}
```

## SSE Event Types

### `progress`

Emitted during agent processing to show real-time status.

```typescript
interface SseProgressEvent {
  step: string;       // e.g., "Analyzing request", "Executing tool: ListSystems"
  detail: string;     // Human-readable progress description
  timestamp: string;  // ISO 8601
}
```

### `result`

Emitted once when the agent completes processing.

```typescript
interface SseResultEvent {
  Success: boolean;
  Response: string;              // Markdown-formatted response text
  ConversationId: string;        // Server-assigned or echoed conversation ID
  AgentName: string;             // e.g., "ComplianceAgent", "RemediationAgent"
  IntentType: string;            // e.g., "query", "action", "import"
  ProcessingTimeMs: number;
  ToolsExecuted: ToolExecution[];
  Errors: ErrorDetail[];
  SuggestedActions: SuggestedAction[];
  RequiresFollowUp: boolean;
}

interface ToolExecution {
  ToolName: string;
  Success: boolean;
  ExecutionTimeMs: number;
}

interface ErrorDetail {
  ErrorCode: string;
  Message: string;
  Suggestion: string | null;
}

interface SuggestedAction {
  Label: string;
  Prompt: string;
  Icon: string | null;
}
```

### `error`

Emitted on unrecoverable server errors.

```typescript
interface SseErrorEvent {
  ErrorCode: string;   // e.g., "AGENT_TIMEOUT", "AUTH_FAILED"
  Message: string;
  Suggestion: string | null;
}
```

## SSE Wire Format

```text
event: progress
data: {"step":"Analyzing request","detail":"Routing to ComplianceAgent","timestamp":"2025-03-16T10:00:00Z"}

event: progress
data: {"step":"Executing tool","detail":"Running ListSystems","timestamp":"2025-03-16T10:00:01Z"}

event: result
data: {"Success":true,"Response":"# Systems Overview\n\nFound 3 registered systems...","ConversationId":"abc-123","AgentName":"ComplianceAgent","IntentType":"query","ProcessingTimeMs":2341,"ToolsExecuted":[{"ToolName":"ListSystems","Success":true,"ExecutionTimeMs":450}],"Errors":[],"SuggestedActions":[{"Label":"View system details","Prompt":"Show details for ACME Portal","Icon":"🔍"}],"RequiresFollowUp":false}

```

## HTTP Headers

### Request Headers

| Header | Value | Required |
|--------|-------|----------|
| `Content-Type` | `application/json` | Yes |
| `Accept` | `text/event-stream` | Yes |
| `Authorization` | `Bearer {token}` | Yes (prod) / No (dev bypass) |
| `Last-Event-ID` | `{event-id}` | No (reconnection only) |

### Response Headers

| Header | Value |
|--------|-------|
| `Content-Type` | `text/event-stream` |
| `Cache-Control` | `no-cache` |
| `Connection` | `keep-alive` |

## Error Codes

| Code | HTTP Status | Description | Client Action |
|------|-------------|-------------|---------------|
| `AUTH_FAILED` | 401 | Invalid or expired token | Refresh token / re-authenticate |
| `AGENT_TIMEOUT` | 504 | Agent processing exceeded timeout | Retry with same message |
| `RATE_LIMITED` | 429 | Too many requests | Wait and retry (backoff) |
| `INVALID_REQUEST` | 400 | Malformed request body | Fix request and resend |
| `INTERNAL_ERROR` | 500 | Unhandled server error | Retry; report if persistent |

## Keepalive

Server sends a comment line every 30 seconds to keep the connection alive:

```text
: keepalive
```

Clients must not treat comment lines as events.
