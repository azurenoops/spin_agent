# REST API Contract: Security Posture Intelligence Navigator Chat Application

**Feature**: 006-chat-app | **Date**: 2026-02-23
**Base URL**: `http://localhost:5001/api`

## Conversations

### GET /api/conversations

List conversations for a user, sorted by most recently updated.

**Query Parameters**:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| userId | string | "default-user" | Owner identifier |
| skip | int | 0 | Pagination offset |
| take | int | 50 | Page size (max items) |

**Response**: `200 OK`

```json
[
  {
    "id": "guid-string",
    "title": "ATO Compliance Discussion",
    "userId": "default-user",
    "createdAt": "2026-02-23T10:00:00Z",
    "updatedAt": "2026-02-23T10:05:00Z",
    "isArchived": false,
    "metadata": {},
    "messages": [],
    "context": null
  }
]
```

---

### GET /api/conversations/{conversationId}

Get a single conversation with its messages and context.

**Path Parameters**: `conversationId` (string, required)

**Response**: `200 OK` — Conversation object with included Messages (with Attachments) and Context.

**Error**: `404 Not Found` — Conversation not found.

---

### POST /api/conversations

Create a new conversation.

**Request Body**:

```json
{
  "title": "New Conversation",
  "userId": "default-user"
}
```

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| title | string | no | "New Conversation" | Conversation title |
| userId | string | no | "default-user" | Owner identifier |

**Response**: `200 OK` — Created Conversation object.

---

### DELETE /api/conversations/{conversationId}

Delete a conversation and all associated messages, attachments, and files.

**Path Parameters**: `conversationId` (string, required)

**Response**: `200 OK`

```json
{
  "message": "Conversation deleted successfully"
}
```

**Error**: `404 Not Found` — Conversation not found.

---

### GET /api/conversations/search

Search conversations by title or message content.

**Query Parameters**:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| query | string | (required) | Search term |
| userId | string | "default-user" | Owner identifier |

**Response**: `200 OK` — Array of matching Conversation objects (max 20 results).

**Error**: `400 Bad Request` — Query is empty.

---

## Messages

### GET /api/messages

List messages for a conversation, ordered by timestamp ascending.

**Query Parameters**:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| conversationId | string | (required) | Conversation ID |
| skip | int | 0 | Pagination offset |
| take | int | 100 | Page size |

**Response**: `200 OK` — Array of ChatMessage objects with Attachments included.

---

### POST /api/messages

Send a message and receive an AI response.

**Request Body**:

```json
{
  "conversationId": "guid-string",
  "message": "Show me the current compliance status",
  "attachmentIds": [],
  "context": {}
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| conversationId | string | yes | Target conversation |
| message | string | yes | User's message text |
| attachmentIds | string[] | no | Pre-uploaded attachment IDs |
| context | object | no | Additional request context |

**Response**: `200 OK` — ChatResponse object:

```json
{
  "messageId": "guid-string",
  "content": "Here is your compliance status...",
  "success": true,
  "error": null,
  "suggestedActions": [],
  "recommendedTools": [],
  "metadata": {
    "intentType": "compliance_check",
    "confidence": 0.92,
    "toolExecuted": true,
    "toolName": "GetComplianceStatus",
    "processingTimeMs": 2500
  }
}
```

**Error**: `400 Bad Request` — ConversationId or Message is empty.

---

### POST /api/messages/{messageId}/attachments

Upload a file attachment to a message.

**Path Parameters**: `messageId` (string, required)

**Request**: `multipart/form-data` with `file` field.

**Constraints**: Max file size 10 MB (10,485,760 bytes).

**Response**: `200 OK` — MessageAttachment object:

```json
{
  "id": "guid-string",
  "messageId": "guid-string",
  "fileName": "compliance-report.pdf",
  "contentType": "application/pdf",
  "size": 245760,
  "storagePath": "uploads/guid.pdf",
  "type": "Document",
  "uploadedAt": "2026-02-23T10:05:00Z",
  "metadata": {}
}
```

**Error**: `400 Bad Request` — File is null/empty or exceeds 10 MB.

---

## Health

### GET /health

Health check endpoint for container orchestration.

**Response**: `200 OK` — `"Healthy"` (plain text or JSON depending on health check configuration).

**Performance**: Must respond within 200ms (p95).

---

## Error Response Format

All error responses follow this structure:

```json
{
  "message": "Human-readable error description",
  "error": "Error category code",
  "suggestion": "Corrective guidance for the user"
}
```

Error categories (per clarification — categorized user-friendly errors):

| Category | HTTP Status | User Message |
|----------|-------------|-------------|
| Category | HTTP Status | User Message | Suggestion |
|----------|-------------|-------------|------------|
| Validation | 400 | "The request is invalid: {field} is required" | "Check the required fields and try again" |
| Not Found | 404 | "Conversation not found" | "Verify the conversation ID or create a new conversation" |
| File Too Large | 400 | "File size exceeds the 10 MB limit" | "Reduce the file size or split into smaller files" |
| Service Unavailable | 502/503 | "The AI service is temporarily unavailable" | "Wait a moment and try again" |
| Timeout | 504 | "The request timed out — try a shorter question" | "Simplify your request or try again shortly" |
| Processing Error | 500 | "The request could not be processed" | "Try again; contact support if the issue persists" |
