# ATO Copilot Chat Application

## Overview

The ATO Copilot Chat Application provides a full-stack conversational interface for interacting with the ATO compliance AI system. Users can send messages, receive AI-powered responses with rich metadata, manage multiple conversations, and attach files for analysis.

### Architecture

```
┌──────────────────────────────────────────────────────┐
│  React Frontend (TypeScript + Tailwind CSS)          │
│  ┌──────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │ ChatWindow│  │ConversationList│ │  Header       │  │
│  │  (US1,4,5)│  │  (US2)        │ │  (US6)        │  │
│  └─────┬─────┘  └──────┬───────┘  └───────────────┘  │
│        │               │                              │
│  ┌─────┴───────────────┴──────────────┐              │
│  │  ChatContext (useReducer + SignalR) │              │
│  └────────────┬───────────────────────┘              │
│               │  REST API (axios)                    │
│               │  WebSocket (SignalR)                  │
└───────────────┼──────────────────────────────────────┘
                │
┌───────────────┼──────────────────────────────────────┐
│  ASP.NET Core │Backend (.NET 9.0)                    │
│  ┌────────────┴────────────┐  ┌──────────────┐       │
│  │  MessagesController     │  │  ChatHub      │       │
│  │  ConversationsController│  │  (SignalR)    │       │
│  └────────────┬────────────┘  └──────┬───────┘       │
│               │                      │               │
│  ┌────────────┴──────────────────────┴───────┐       │
│  │            ChatService (IChatService)      │       │
│  └────────────────────┬──────────────────────┘       │
│                       │                              │
│  ┌────────────────────┴──────────────────────┐       │
│  │  ChatDbContext (EF Core, SQLite/SQL Server)│       │
│  └───────────────────────────────────────────┘       │
└──────────────────────────────────────────────────────┘
                │
                ▼
┌──────────────────────────┐
│  MCP Server (AI Backend) │
│  POST /mcp/chat          │
│  POST /api/chat/query    │
└──────────────────────────┘
```

### Tech Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Frontend | React + TypeScript | 18.2 / 4.9 |
| Styling | Tailwind CSS | 3.4 |
| State | React Context + useReducer | - |
| Real-time | SignalR | 8.0 |
| Backend | ASP.NET Core | 9.0 |
| ORM | Entity Framework Core | 9.0 |
| Database | SQLite (dev) / SQL Server (prod) | - |
| Logging | Serilog | 9.0 |
| Testing | xUnit + FluentAssertions + Moq | 2.9 / 7.0 / 4.20 |

---

## Setup Instructions

### Prerequisites

- .NET 9.0 SDK
- Node.js 20.x
- npm 10.x

### Development Setup

```bash
# Clone and navigate to the chat project
cd src/Ato.Copilot.Chat

# Restore NuGet packages
dotnet restore

# Install frontend dependencies
cd ClientApp && npm install && cd ..

# Run the application (starts both backend and SPA proxy)
dotnet run
```

The application will be available at:
- **Backend API**: https://localhost:5001
- **Frontend (SPA proxy)**: https://localhost:44414
- **Health check**: https://localhost:5001/health
- **SignalR hub**: wss://localhost:5001/hubs/chat

### Database

The database is automatically created on first startup using `EnsureCreatedAsync()`.

- **Development**: SQLite database at `chat.db` in the project directory
- **Production**: SQL Server (configure `ConnectionStrings:ChatDb` in appsettings)

The provider is auto-detected from the connection string (`:memory:` or `.db` → SQLite, otherwise SQL Server).

---

## API Reference

### Messages

#### POST /api/messages
Send a message and receive an AI response.

**Request Body:**
```json
{
  "conversationId": "string (required)",
  "message": "string (required)",
  "context": { }
}
```

**Response (200):**
```json
{
  "messageId": "string",
  "content": "string",
  "success": true,
  "suggestedActions": [],
  "recommendedTools": [],
  "metadata": {
    "processingTimeMs": 150,
    "intentType": "compliance_check",
    "confidence": 0.92
  }
}
```

**Error (400):**
```json
{
  "message": "ConversationId is required",
  "error": "Validation",
  "suggestion": "Provide a valid conversationId"
}
```

#### GET /api/messages?conversationId={id}&skip=0&take=100
Get messages for a conversation with pagination.

#### POST /api/messages/{messageId}/attachments
Upload a file attachment (multipart/form-data, max 10 MB).

### Conversations

#### POST /api/conversations
Create a new conversation.

**Request Body:**
```json
{
  "title": "string (optional, defaults to 'New Conversation')",
  "userId": "string (optional, defaults to 'default-user')"
}
```

#### GET /api/conversations?userId=default-user&skip=0&take=50
List conversations sorted by most recently updated.

#### GET /api/conversations/{conversationId}
Get a conversation with messages and context.

#### DELETE /api/conversations/{conversationId}
Delete a conversation and all associated data (messages, attachments, files).

#### GET /api/conversations/search?query={keyword}&userId=default-user
Search conversations by title or message content (max 20 results).

---

## SignalR Events

### Hub Endpoint
`/hubs/chat`

### Client → Server Methods

| Method | Parameters | Description |
|--------|-----------|-------------|
| `JoinConversation` | `conversationId: string` | Join a conversation group for real-time updates |
| `LeaveConversation` | `conversationId: string` | Leave a conversation group |
| `SendMessage` | `SendMessageRequest` | Send a message via SignalR (broadcasts to group) |
| `NotifyTyping` | `conversationId: string, userId: string` | Notify others that user is typing |

### Server → Client Events

| Event | Payload | Description |
|-------|---------|-------------|
| `MessageProcessing` | `{ conversationId, messageId, status }` | AI is processing the message |
| `MessageReceived` | `{ conversationId, message }` | AI response received |
| `MessageError` | `{ conversationId, messageId, error, category }` | Processing failed |
| `UserTyping` | `{ conversationId, userId }` | Another user is typing |

### Error Categories
- `Timeout` — Request timed out
- `ServiceUnavailable` — MCP server unavailable
- `ProcessingError` — Unexpected processing error
- `ValidationError` — Invalid request

### Reconnection Strategy
Automatic reconnection with backoff: `[0, 2000, 5000, 10000, 30000]` ms

---

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "ChatDb": "Data Source=chat.db"
  },
  "McpServer": {
    "BaseUrl": "http://localhost:3001"
  },
  "Server": {
    "Port": 5001
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:3000"]
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information"
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/chat-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 14
        }
      }
    ]
  }
}
```

### Environment Variables

| Variable | Description | Default |
|----------|-----------|---------|
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | `Production` |
| `ASPNETCORE_URLS` | Listening URLs | `http://+:8080` (Docker) |
| `ConnectionStrings__ChatDb` | Database connection string | `Data Source=chat.db` |

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Enter` | Send message |
| `Shift+Enter` | New line in message |
| `Ctrl+K` | Toggle sidebar |
| `Ctrl+N` | New conversation |
| `Escape` | Close settings modal |

---

## Docker

### Build

```bash
docker build -f src/Ato.Copilot.Chat/Dockerfile -t ato-copilot-chat .
```

The frontend stage copies `package.json`, `package-lock.json`, and `.npmrc`, then runs `npm ci`. Do not switch that install back to a lockfile-less `npm install` — CD `build-image` failed on 2026-09-01 with `Cannot find module 'ajv/dist/compile/codegen'` when the image resolved a floating `ajv`/`ajv-keywords` tree.

### Run

```bash
docker run -p 8080:8080 \
  -e ConnectionStrings__ChatDb="Data Source=/app/data/chat.db" \
  -v chat-data:/app/data \
  -v chat-uploads:/app/uploads \
  ato-copilot-chat
```

### Health Check

The container includes a built-in health check at `/health` with a 30-second interval.

---

## Troubleshooting

### Common Issues

**Database not created:**
The database is created automatically on startup with `EnsureCreatedAsync()`. Check the connection string in `appsettings.json`. For SQLite, ensure the directory is writable.

**MCP Server connection failed:**
Verify the `McpServer:BaseUrl` configuration points to a running MCP server instance. The service attempts a primary endpoint (`/mcp/chat`) and falls back to `/api/chat/query`.

**SignalR connection drops:**
The client automatically reconnects with exponential backoff. Check the browser console for connection status updates. The connection status indicator in the UI shows the current state.

**File upload rejected:**
Files must be under 10 MB. Verify the file is not empty. Check the `uploads/` directory exists and is writable.

**CORS errors:**
Ensure the frontend origin is listed in `Cors:AllowedOrigins` in appsettings.json. In development, `AllowAll` policy is used.

### Logging

Logs are written to:
- **Console**: All environments
- **File**: `logs/chat-{date}.log` (14-day retention, daily rolling)
- **Application Insights**: Production (when instrumentation key is configured)

Log levels can be adjusted in `appsettings.json` under the `Serilog` section.
