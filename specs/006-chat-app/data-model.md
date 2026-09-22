# Data Model: Security Posture Intelligence Navigator Chat Application

**Feature**: 006-chat-app | **Date**: 2026-02-23

## Entity Relationship Diagram

```
┌──────────────────────┐       ┌──────────────────────────┐
│    Conversation       │       │   ConversationContext     │
├──────────────────────┤       ├──────────────────────────┤
│ Id          (PK, str)│1────1?│ Id          (PK, str)    │
│ Title       (str 200)│       │ ConversationId (FK, str) │
│ UserId      (str 100)│       │ Type        (str 50)     │
│ CreatedAt   (DateTime)│       │ Title       (str 200)    │
│ UpdatedAt   (DateTime)│       │ Summary     (str)        │
│ IsArchived  (bool)   │       │ Data        (JSON dict)  │
│ Metadata    (JSON)   │       │ CreatedAt   (DateTime)   │
└────────┬─────────────┘       │ LastAccessedAt (DateTime)│
         │ 1                   │ Tags        (JSON list)  │
         │                     └──────────────────────────┘
         │ *
┌────────┴─────────────┐
│    ChatMessage        │
├──────────────────────┤
│ Id          (PK, str)│
│ ConversationId(FK)   │
│ Content     (str, req)│
│ Role        (enum→str)│
│ Timestamp   (DateTime)│
│ Status      (enum→str)│
│ Metadata    (JSON)   │
│ ParentMessageId (str)│
│ Tools       (JSON list)│
│ ToolResult  (JSON)   │
└────────┬─────────────┘
         │ 1
         │
         │ *
┌────────┴─────────────┐
│  MessageAttachment    │
├──────────────────────┤
│ Id          (PK, str)│
│ MessageId   (FK, str)│
│ FileName    (str 255)│
│ ContentType (str 100)│
│ Size        (long)   │
│ StoragePath (str 500)│
│ Type        (enum→str)│
│ UploadedAt  (DateTime)│
│ Metadata    (JSON)   │
└──────────────────────┘
```

## Entities

### Conversation

The top-level container for a chat session.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | string | PK, max 450, GUID | Auto-generated on creation |
| Title | string | max 200 | Auto-generated from first message or user-provided |
| UserId | string | max 100, indexed | Owner identifier, defaults to "default-user" |
| CreatedAt | DateTime | indexed | UTC creation timestamp |
| UpdatedAt | DateTime | indexed | UTC last-modified timestamp |
| IsArchived | bool | default false | Soft-archive flag |
| Metadata | Dictionary<string, object> | JSON-serialized | Arbitrary key-value metadata |

**Relationships**: One-to-many → ChatMessage (cascade delete), One-to-one? → ConversationContext

### ChatMessage

An individual message within a conversation.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | string | PK, max 450, GUID | Auto-generated |
| ConversationId | string | FK, max 450, indexed | References Conversation.Id |
| Content | string | required | Message text content |
| Role | MessageRole | stored as string, indexed | User, Assistant, System, Tool |
| Timestamp | DateTime | indexed | Message creation time (UTC) |
| Status | MessageStatus | stored as string | Lifecycle state |
| Metadata | Dictionary<string, object> | JSON-serialized | Intent, tool results, suggestions, errors |
| ParentMessageId | string? | max 450 | For threaded replies (nullable) |
| Tools | List<string> | JSON-serialized | Tool names involved |
| ToolResult | ToolExecutionResult? | JSON-serialized, nullable | Tool execution output |

**Relationships**: Many-to-one → Conversation, One-to-many → MessageAttachment (cascade delete)

### ConversationContext

Contextual metadata for a conversation — tracks analysis scope, workflow type, and tags.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | string | PK, max 450, GUID | Auto-generated |
| ConversationId | string | FK, max 450, indexed | References Conversation.Id |
| Type | string | max 50, indexed | Context type: "ato_scan", "deployment", "cost_analysis" |
| Title | string | max 200 | Display title |
| Summary | string | — | Summary text |
| Data | Dictionary<string, object> | JSON-serialized | Arbitrary context data |
| CreatedAt | DateTime | indexed | Creation time (UTC) |
| LastAccessedAt | DateTime | indexed | Last access time (UTC) |
| Tags | List<string> | JSON-serialized | Categorization tags |

**Relationships**: One-to-one (optional) → Conversation

### MessageAttachment

A file attached to a message.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | string | PK, max 450, GUID | Auto-generated |
| MessageId | string | FK, max 450, indexed | References ChatMessage.Id |
| FileName | string | max 255 | Original filename |
| ContentType | string | max 100 | MIME type |
| Size | long | — | File size in bytes |
| StoragePath | string | max 500 | Filesystem path to stored file |
| Type | AttachmentType | stored as string, indexed | Document, Image, Code, Configuration, Log |
| UploadedAt | DateTime | indexed | Upload timestamp (UTC) |
| Metadata | Dictionary<string, object> | JSON-serialized | Arbitrary metadata |

**Relationships**: Many-to-one → ChatMessage

### ToolExecutionResult (Value Object)

Result of a tool invocation — embedded as serialized JSON within ChatMessage.ToolResult.

| Field | Type | Notes |
|-------|------|-------|
| ToolName | string | Which tool was executed |
| Success | bool | Whether it succeeded |
| Result | object? | The tool's output payload |
| Error | string? | Error message if failed |
| Parameters | Dictionary<string, object> | Input parameters |
| ExecutedAt | DateTime | Execution timestamp |
| Duration | TimeSpan | How long it took |

**Not a database entity** — serialized into ChatMessage.ToolResult as JSON.

## Enums

### MessageRole

| Value | Storage | Description |
|-------|---------|-------------|
| User | "User" | Message from the user |
| Assistant | "Assistant" | AI-generated response |
| System | "System" | System notification |
| Tool | "Tool" | Tool execution output |

### MessageStatus

| Value | Storage | Description |
|-------|---------|-------------|
| Sending | "Sending" | Client has submitted, not yet confirmed |
| Sent | "Sent" | Server has persisted the message |
| Processing | "Processing" | MCP Server is processing |
| Completed | "Completed" | Response received and saved |
| Error | "Error" | Processing failed |
| Retry | "Retry" | Scheduled for retry |

### AttachmentType

| Value | Storage | Detection Rule |
|-------|---------|----------------|
| Document | "Document" | Default / unrecognized MIME types |
| Image | "Image" | Content type starts with `image/` |
| Code | "Code" | Contains `code`, `javascript`, or `python` |
| Configuration | "Configuration" | Contains `json`, `yaml`, or `xml` |
| Log | "Log" | Contains `log` or `text/plain` |

## Indexes

| Entity | Column(s) | Type | Purpose |
|--------|-----------|------|---------|
| Conversation | UserId | Non-unique | Filter by user |
| Conversation | CreatedAt | Non-unique | Sort by creation |
| Conversation | UpdatedAt | Non-unique | Sort by recency |
| ChatMessage | ConversationId | Non-unique | Filter messages by conversation |
| ChatMessage | Timestamp | Non-unique | Sort messages chronologically |
| ChatMessage | Role | Non-unique | Filter by role |
| ConversationContext | ConversationId | Non-unique | Lookup context by conversation |
| ConversationContext | Type | Non-unique | Filter by context type |
| ConversationContext | CreatedAt | Non-unique | Sort by creation |
| ConversationContext | LastAccessedAt | Non-unique | Sort by access recency |
| MessageAttachment | MessageId | Non-unique | Filter attachments by message |
| MessageAttachment | UploadedAt | Non-unique | Sort by upload time |
| MessageAttachment | Type | Non-unique | Filter by attachment type |

## JSON-Serialized Fields

All JSON fields use `ValueConverter<T, string>` with `System.Text.Json.JsonSerializer`. This pattern matches the existing `AtoCopilotContext` approach for cross-provider compatibility (SQLite + SQL Server).

| Entity | Field | CLR Type | Null Handling |
|--------|-------|----------|---------------|
| Conversation | Metadata | Dictionary<string, object> | Default empty dict on null |
| ChatMessage | Metadata | Dictionary<string, object> | Default empty dict on null |
| ChatMessage | Tools | List<string> | Default empty list on null |
| ChatMessage | ToolResult | ToolExecutionResult? | Serialize returns null if value is null |
| ConversationContext | Data | Dictionary<string, object> | Default empty dict on null |
| ConversationContext | Tags | List<string> | Default empty list on null |
| MessageAttachment | Metadata | Dictionary<string, object> | Default empty dict on null |

## Validation Rules

| Rule | Entity | Field | Constraint |
|------|--------|-------|------------|
| Required content | ChatMessage | Content | Cannot be null or empty |
| Title length | Conversation | Title | Max 200 characters |
| User ID length | Conversation | UserId | Max 100 characters |
| File size | MessageAttachment | Size | Max 10 MB (10,485,760 bytes) — enforced at API layer |
| Filename length | MessageAttachment | FileName | Max 255 characters |
| Storage path | MessageAttachment | StoragePath | Max 500 characters |

## State Transitions

### Message Lifecycle

```
User sends message:    [Sending] → [Sent] → [Processing] → [Completed]
                                                          → [Error]
                                                          → [Retry] → [Processing] → ...
```

### Conversation Lifecycle

```
Created → Active (messages being exchanged)
       → Archived (IsArchived = true, excluded from default listings)
       → Deleted (removed from database, attachments cleaned from filesystem)
```
