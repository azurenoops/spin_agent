# Quickstart: Dashboard Chat Side Panel

**Feature**: 034-dashboard-chat | **Branch**: `034-dashboard-chat`

## Prerequisites

- Node.js 18+ / npm 9+
- Docker Desktop (for MCP Server + SQL Server)
- VS Code (recommended)

## Setup

### 1. Start Backend Services

```bash
docker compose -f docker-compose.mcp.yml up -d
```

This starts:
- `ato-copilot-sql` (SQL Server 2022) — port 1433
- `ato-copilot-mcp` (MCP Server) — port 3001

### 2. Install Dashboard Dependencies

```bash
cd src/Ato.Copilot.Dashboard
npm install
```

New dependencies added by this feature:
- `react-markdown@^10.1.0` — Markdown rendering
- `remark-gfm@^4.0.0` — GitHub Flavored Markdown tables, strikethrough, etc.
- `react-syntax-highlighter@^16.1.0` — Code block syntax highlighting
- `@types/react-syntax-highlighter@^15.5.0` — TypeScript types

### 3. Start Dashboard Dev Server

```bash
npm run dev
```

Dashboard runs at `http://localhost:5173`. The Vite dev server proxies `/api` requests to the MCP Server at `http://localhost:3001`.

### 4. Verify Chat Panel

1. Open `http://localhost:5173` in your browser
2. Look for the chat icon button in the top-right header bar (next to Help)
3. Click to open the chat panel — it should slide in from the right
4. Type "Hello" and press Enter — you should see a streaming response from the MCP Server

## Development Workflow

### Run Tests

```bash
cd src/Ato.Copilot.Dashboard
npm test                    # Run all tests
npm test -- --watch         # Watch mode
npm run test:coverage       # Coverage report
```

### Key Directories

| Path | Purpose |
|------|---------|
| `src/components/chat/` | All chat panel React components |
| `src/hooks/useChat.ts` | Chat state management hook |
| `src/hooks/useSseStream.ts` | SSE streaming hook |
| `src/hooks/useChatContext.ts` | Dashboard context extraction |
| `src/services/chatService.ts` | SSE client service |
| `src/types/chat.ts` | TypeScript interfaces |

### Testing Chat Without Backend

The `useSseStream` hook can be tested with a mock SSE endpoint. For component testing, mock the `useChat` hook to return static conversations.

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `VITE_API_BASE_URL` | `/api/dashboard` | Dashboard API base URL |
| `VITE_MCP_BASE_URL` | `/api` | MCP Server base URL (for SSE endpoint) |

### Auth in Development

No auth token is required in development. The MCP Server accepts unauthenticated requests when no auth middleware is configured. The chat service reads `auth_token` from localStorage; if absent, requests are sent without `Authorization` header.
