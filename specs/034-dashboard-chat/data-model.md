# Data Model: Dashboard Chat Side Panel

**Feature**: 034-dashboard-chat | **Date**: 2025-03-16

## Entity Relationship Overview

```text
Conversation 1──* Message
Message 1──* ToolExecution
Message 0──* SuggestedAction
Message 0──* ErrorDetail
Message 0──1 ChatContext
```

## Entities

### Conversation

Represents a multi-turn chat session stored in localStorage.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| id | `string` | Yes | UUID v4 generated client-side |
| title | `string` | Yes | Auto-generated from first user message (first 50 chars) |
| messages | `Message[]` | Yes | Ordered list of messages |
| createdAt | `string` | Yes | ISO 8601 timestamp |
| updatedAt | `string` | Yes | ISO 8601 timestamp (updated on every new message) |
| context | `ChatContext \| null` | No | Dashboard context at conversation creation |

**Validation Rules**:
- `id` must be a valid UUID v4
- `title` max length: 100 characters
- `messages` max depth: 20 per request (older messages trimmed from `ConversationHistory`)
- Max 50 conversations in localStorage (LRU eviction by `updatedAt`)

### Message

A single user or assistant message within a conversation.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| id | `string` | Yes | UUID v4 generated client-side |
| role | `'user' \| 'assistant'` | Yes | Who sent the message |
| content | `string` | Yes | Message text (Markdown for assistant) |
| status | `MessageStatus` | Yes | Current state of the message |
| timestamp | `string` | Yes | ISO 8601 timestamp |
| agentName | `string \| null` | No | MCP agent that handled the request (assistant only) |
| intentType | `string \| null` | No | Detected intent type (assistant only) |
| processingTimeMs | `number \| null` | No | Server processing time in ms (assistant only) |
| toolsExecuted | `ToolExecution[]` | No | Tool execution results (assistant only) |
| errors | `ErrorDetail[]` | No | Errors returned from MCP (assistant only) |
| suggestedActions | `SuggestedAction[]` | No | Follow-up suggestions (assistant only) |
| requiresFollowUp | `boolean` | No | Whether the response needs user follow-up |
| attachments | `FileAttachment[]` | No | Files attached to the message (user only) |

**State Transitions**:
```text
[sending] ──► [streaming] ──► [complete]
    │              │
    └──► [error]   └──► [error]
```

### MessageStatus

Enum representing message lifecycle states.

| Value | Description |
|-------|-------------|
| `sending` | Request sent, waiting for first SSE event |
| `streaming` | Receiving SSE `progress` events |
| `complete` | SSE `result` event received, message finalized |
| `error` | SSE `error` event or network failure |

### ToolExecution

Details of a tool executed by the MCP agent during response generation.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| toolName | `string` | Yes | Name of the executed tool |
| success | `boolean` | Yes | Whether the tool execution succeeded |
| executionTimeMs | `number` | Yes | Time taken in milliseconds |
| result | `string \| null` | No | Tool output summary |

### ErrorDetail

Structured error information from the MCP Server.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| errorCode | `string` | Yes | Machine-readable error code |
| message | `string` | Yes | Human-readable error message |
| suggestion | `string \| null` | No | Actionable suggestion for the user |

### SuggestedAction

A follow-up action suggested by the MCP agent.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| label | `string` | Yes | Display text for the suggestion chip |
| prompt | `string` | Yes | Full prompt to send when clicked |
| icon | `string \| null` | No | Optional emoji or icon identifier |

### ChatContext

Dashboard context included with every chat request for page awareness.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| page | `string` | Yes | Current dashboard route name |
| systemId | `string \| null` | No | Selected system ID (if on system detail) |
| boundaryId | `string \| null` | No | Selected boundary ID (if applicable) |
| entityType | `string \| null` | No | Type of entity being viewed |
| entityId | `string \| null` | No | ID of the entity being viewed |

### FileAttachment

Metadata for a file attached to a user message.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| name | `string` | Yes | Original filename |
| size | `number` | Yes | File size in bytes |
| type | `FileAttachmentType` | Yes | Detected file type |
| content | `string` | No | File content (not persisted to localStorage) |

### FileAttachmentType

Enum representing supported import file types.

| Value | Extensions | Description |
|-------|------------|-------------|
| `stig-ckl` | `.ckl` | DISA STIG Checklist |
| `stig-xccdf` | `.xccdf`, `.xml` | STIG XCCDF benchmark |
| `prisma-csv` | `.csv` | Prisma Cloud export |
| `nessus` | `.nessus` | Nessus scan results |
| `unknown` | other | Unrecognized format |

### ChatPanelState

UI state for the chat panel (persisted to localStorage).

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| isOpen | `boolean` | Yes | Whether the panel is currently visible |
| width | `number` | Yes | Panel width in pixels (320–600, default 420) |
| activeConversationId | `string \| null` | No | Currently displayed conversation |

## localStorage Schema

| Key | Type | Max Size | Description |
|-----|------|----------|-------------|
| `ato-chat-conversations` | `Conversation[]` | ~750KB | All conversations with messages |
| `ato-chat-panel-state` | `ChatPanelState` | ~100B | Panel open/closed, width, active conversation |
