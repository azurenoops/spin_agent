# SignalR Contract: Security Posture Intelligence Navigator Chat Application

**Feature**: 006-chat-app | **Date**: 2026-02-23
**Hub Path**: `/hubs/chat`
**Transport**: WebSocket (primary), Server-Sent Events (fallback)
**Client Library**: `@microsoft/signalr ^8.0.0`

---

## Connection Lifecycle

### Connection Configuration

```typescript
const connection = new HubConnectionBuilder()
  .withUrl("/hubs/chat")
  .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
  .configureLogging(LogLevel.Warning)
  .build();
```

### Reconnection Backoff Schedule

| Attempt | Delay (ms) | Description |
|---------|-----------|-------------|
| 1 | 0 | Immediate retry |
| 2 | 2,000 | 2 seconds |
| 3 | 5,000 | 5 seconds |
| 4 | 10,000 | 10 seconds |
| 5+ | 30,000 | 30 seconds (max) |

After all retries exhausted, connection enters `Disconnected` state. Client must call `connection.start()` manually to reconnect.

---

## Client → Server Methods (Hub Methods)

### JoinConversation

Join a SignalR group for a conversation to receive real-time updates.

**Invocation**: `connection.invoke("JoinConversation", conversationId)`

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| conversationId | string | yes | Conversation GUID to join |

**Server Behavior**:
- Adds caller's connection to the SignalR group keyed by `conversationId`
- No acknowledgment event sent (invoke resolves on completion)

**Error**: HubException if conversationId is null/empty.

---

### LeaveConversation

Leave a SignalR group when navigating away from a conversation.

**Invocation**: `connection.invoke("LeaveConversation", conversationId)`

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| conversationId | string | yes | Conversation GUID to leave |

**Server Behavior**:
- Removes caller's connection from the SignalR group
- No error if caller was not in the group

---

### SendMessage

Send a chat message to a conversation. The hub orchestrates AI processing and streams progress events back.

**Invocation**: `connection.invoke("SendMessage", request)`

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| request | SendMessageRequest | yes | Message payload |

**SendMessageRequest Schema**:

```json
{
  "conversationId": "guid-string",
  "content": "What is our current ATO compliance status?",
  "metadata": {}
}
```

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| conversationId | string | yes | — | Target conversation |
| content | string | yes | — | Message text (max 10,000 chars) |
| metadata | object | no | {} | Additional message context |

**Server Behavior**:
1. Validates request (content not empty, ≤ 10,000 chars)
2. Persists user message with `MessageStatus.Sent`
3. Sends `MessageProcessing` event to the conversation group
4. Invokes AI agent pipeline via `IChatService`
5. Persists AI response with `MessageStatus.Completed`
6. Sends `MessageReceived` event to the conversation group
7. On error: sends `MessageError` event to the conversation group

**Error**: HubException if validation fails.

---

### NotifyTyping

Notify other participants that the user is typing.

**Invocation**: `connection.send("NotifyTyping", conversationId, userId)`

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| conversationId | string | yes | Conversation GUID |
| userId | string | yes | Typing user's ID |

**Server Behavior**:
- Broadcasts `UserTyping` event to all other connections in the conversation group (excludes caller)
- Fire-and-forget (use `send` not `invoke`)

---

## Server → Client Events

### MessageProcessing

Indicates the AI is processing a message. Client should show a loading/thinking indicator.

**Event**: `connection.on("MessageProcessing", callback)`

**Payload**:

```json
{
  "conversationId": "guid-string",
  "messageId": "guid-string",
  "status": "Processing"
}
```

| Field | Type | Description |
|-------|------|-------------|
| conversationId | string | Conversation this event belongs to |
| messageId | string | The user message being processed |
| status | string | Always "Processing" |

---

### MessageReceived

AI response is ready. Client should render the message and hide the loading indicator.

**Event**: `connection.on("MessageReceived", callback)`

**Payload**:

```json
{
  "conversationId": "guid-string",
  "message": {
    "id": "guid-string",
    "conversationId": "guid-string",
    "role": "Assistant",
    "content": "Your ATO compliance status is...",
    "status": "Completed",
    "timestamp": "2026-02-23T10:05:02Z",
    "metadata": {
      "intentType": "compliance_check",
      "confidence": 0.92,
      "toolExecuted": true,
      "toolName": "GetComplianceStatus",
      "processingTimeMs": 2500
    },
    "attachments": [],
    "toolResults": []
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| conversationId | string | Conversation this message belongs to |
| message | ChatMessage | Full message object (see data-model.md) |

---

### MessageError

An error occurred while processing the message. Client should display categorized error.

**Event**: `connection.on("MessageError", callback)`

**Payload**:

```json
{
  "conversationId": "guid-string",
  "messageId": "guid-string",
  "error": "The AI service is temporarily unavailable",
  "category": "ServiceUnavailable"
}
```

| Field | Type | Description |
|-------|------|-------------|
| conversationId | string | Conversation this error belongs to |
| messageId | string | The user message that triggered the error |
| error | string | Human-readable error message |
| category | string | Error category (see table below) |

**Error Categories** (per FR-043 — categorized user-friendly errors):

| Category | Description | User Action |
|----------|-------------|-------------|
| Timeout | AI processing exceeded time limit | Retry with shorter input |
| ServiceUnavailable | AI gateway unreachable | Retry after a moment |
| ProcessingError | Internal error during AI pipeline | Retry; contact support if persistent |
| ValidationError | Input rejected by server | Fix input and resend |

---

### UserTyping

Another user in the conversation is typing.

**Event**: `connection.on("UserTyping", callback)`

**Payload**:

```json
{
  "conversationId": "guid-string",
  "userId": "default-user"
}
```

| Field | Type | Description |
|-------|------|-------------|
| conversationId | string | Conversation where typing is occurring |
| userId | string | User who is typing |

**Client Behavior**: Show typing indicator for 3 seconds, reset on each new event.

---

## Connection State Management

### Client-Side State Machine

```
Disconnected → Connecting → Connected → Reconnecting → Connected
                                      → Disconnected (retries exhausted)
```

### Required Client Event Handlers

| Event | Handler | Purpose |
|-------|---------|---------|
| `onclose` | Attempt manual reconnect or show banner | Connection permanently lost |
| `onreconnecting` | Show "Reconnecting..." indicator | Transient disconnection |
| `onreconnected` | Re-join active conversation group, hide indicator | Connection restored |

### Reconnection Protocol

On `onreconnected`:
1. Re-invoke `JoinConversation` for the active conversation
2. Fetch messages from REST API since last known timestamp to fill any gaps
3. Clear reconnection indicator

---

## DI Scoping in Hub

The `ChatHub` uses `IServiceScopeFactory` to resolve scoped services (EF Core DbContext, etc.) within hub method invocations, per research decision R-003:

```csharp
public async Task SendMessage(SendMessageRequest request)
{
    using var scope = _scopeFactory.CreateScope();
    var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
    // ...
}
```

This avoids the "cannot resolve scoped service from singleton" error since Hubs are transient by default but outlive the DI scope.
