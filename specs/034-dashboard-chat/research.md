# Research: Dashboard Chat Side Panel

**Feature**: 034-dashboard-chat | **Date**: 2025-03-16

## R-001: SSE Client Implementation for Dashboard

**Task**: How to implement SSE streaming in the React dashboard?

**Decision**: Port the SSE client pattern from the VS Code extension (`extensions/vscode/src/services/sseClient.ts`) into a React-friendly service using native `fetch` + `ReadableStream`.

**Rationale**: The VS Code extension already has a production-tested SSE client with line-based parsing (`parseSseChunk`), retry with exponential backoff, AbortController cancellation, and timeout handling. The core parsing logic is framework-agnostic and can be reused directly. The dashboard needs a React hook wrapper (`useSseStream`) around the same streaming mechanism.

**Alternatives Considered**:
- **EventSource API**: Rejected — only supports GET requests; the MCP endpoint is POST-based (`POST /mcp/chat/stream`).
- **SignalR**: Rejected — the Chat App uses SignalR but the MCP Server already exposes SSE; adding a second real-time transport adds unnecessary complexity.
- **Third-party SSE library (e.g., eventsource-polyfill)**: Rejected — the existing `parseSseChunk` function and `SseEvent` interface from the VS Code extension are sufficient and avoid an extra dependency.

**Key Implementation Details**:
- Endpoint: `POST /mcp/chat/stream` (relative path via Vite proxy: `/api/mcp/chat/stream` or direct MCP base URL)
- SSE event types: `progress`, `result`, `error`
- Keepalive: 30-second interval from server
- Reconnection: `Last-Event-ID` header supported via `SseEventBuffer`
- Cancellation: `AbortController.abort()` on panel close or new message
- Timeout: 120 seconds (matches VS Code extension default)

---

## R-002: Markdown Rendering Libraries

**Task**: Which Markdown rendering libraries to use in the dashboard?

**Decision**: Add `react-markdown@^10.1.0`, `remark-gfm@^4.0.0`, and `react-syntax-highlighter@^16.1.0` to the dashboard's `package.json`.

**Rationale**: The Chat App (`src/Ato.Copilot.Chat/ClientApp/`) already uses these exact versions for rendering MCP responses. Using the same libraries ensures consistent Markdown rendering across both UIs and avoids divergent rendering behavior. The Chat App's `ChatWindow.tsx` demonstrates the exact integration pattern: `ReactMarkdown` with `remarkGfm` plugin and `Prism` syntax highlighter using the `oneDark` theme.

**Alternatives Considered**:
- **marked + DOMPurify**: Rejected — requires manual HTML sanitization; `react-markdown` renders to React elements directly (no `dangerouslySetInnerHTML`).
- **mdx-js**: Rejected — over-engineered for rendering server responses; MDX is for authoring, not display.
- **Sharing a package/library between Chat App and Dashboard**: Rejected — the two apps have different build systems (CRA vs Vite) and React versions (18 vs 19). Direct code sharing would require a monorepo setup that's out of scope.

---

## R-003: Authentication Strategy

**Task**: How to implement Azure AD / Entra ID authentication (FR-018)?

**Decision**: Use the existing `localStorage` Bearer token pattern for the initial implementation. Add `@azure/msal-react` as a future enhancement tracked separately.

**Rationale**: No frontend in the project currently uses MSAL. The dashboard API client (`src/Ato.Copilot.Dashboard/src/api/client.ts`) already reads `Bearer` tokens from `localStorage` and attaches them to every request. The MCP Server accepts Bearer tokens. Implementing full MSAL login flow is a cross-cutting concern that affects all dashboard pages, not just the chat panel. Adding MSAL to the chat panel alone would create an inconsistent auth experience.

The spec's FR-018 requires "Azure AD / Entra ID authentication for MCP Server calls" with "development bypass when no auth is configured." The current `localStorage` Bearer token pattern already satisfies the dev bypass requirement. The chat service will use the same `apiClient` interceptor pattern, and MSAL integration can be added as a separate feature that upgrades all dashboard auth at once.

**Alternatives Considered**:
- **Add `@azure/msal-react` now for chat only**: Rejected — creates inconsistent auth across the dashboard; other pages still use raw localStorage tokens.
- **Proxy auth through a BFF (Backend-For-Frontend)**: Rejected — adds backend complexity; the MCP Server already accepts Bearer tokens.

---

## R-004: Dashboard Layout Integration (Overlay Mode)

**Task**: How to integrate the chat panel into the existing dashboard layout?

**Decision**: Render the `ChatPanel` as a fixed-position overlay (`position: fixed`) in the `App.tsx` root, outside of `PageLayout`. The panel slides in from the right edge over existing content without causing layout reflow.

**Rationale**: The spec explicitly requires "overlay mode" (Clarification Q4) — the panel must not push or reflow existing content. The existing `PageLayout` component has a `sidePanel` prop, but this is a layout-integrated side panel (w-80, with toggle) that shares container space with the main content. Using the `sidePanel` prop would cause content reflow, violating the overlay requirement.

A fixed-position overlay is the correct approach: it sits above the z-index stack, can be any width (320–600px per spec), works identically on every route, and requires no changes to `PageLayout` or individual page components.

**Key Implementation Details**:
- `ChatPanel` rendered as sibling to `<Routes>` in `App.tsx`
- `position: fixed; right: 0; top: 56px; bottom: 0; z-index: 40;` (below modals, above content)
- Width: 420px default, resizable 320–600px
- Below 768px: full-width overlay per FR-020
- `ChatToggle` button added to `PageLayout` header bar (alongside Search, Notifications, Help, Settings)
- Panel state (open/closed) stored in `localStorage` to persist across page navigations

**Alternatives Considered**:
- **Use `PageLayout.sidePanel` prop**: Rejected — causes layout reflow (content area shrinks), violates overlay requirement, only visible on xl+ breakpoint.
- **React Portal**: Considered but unnecessary — a fixed-position div at the App root achieves the same result without portal complexity.

---

## R-005: Conversation Persistence

**Task**: How to persist conversations in localStorage without degradation at 50 conversations (SC-004)?

**Decision**: Store conversations as a JSON array in `localStorage` under key `ato-chat-conversations`. Each conversation object contains metadata and messages. Bound to 50 conversations max; oldest auto-pruned when limit reached.

**Rationale**: The spec requires client-side persistence with no server-side DB changes. localStorage has a ~5MB limit per origin. A typical conversation with 20 messages (avg 500 chars each) is ~15KB including metadata. 50 conversations = ~750KB, well within the 5MB limit.

**Key Implementation Details**:
- Storage key: `ato-chat-conversations`
- Active conversation ID: `ato-chat-active-conversation`
- Panel state: `ato-chat-panel-open` (boolean)
- Panel width: `ato-chat-panel-width` (number)
- Serialization: `JSON.stringify` / `JSON.parse` with error recovery
- LRU eviction: when adding conversation #51, remove the oldest by `updatedAt` timestamp
- Debounce writes: batch localStorage updates to avoid write storms during streaming
- Error handling: if localStorage is full or corrupted, gracefully create a fresh store

**Alternatives Considered**:
- **IndexedDB**: More capacity but over-engineered for <1MB of chat data; adds async complexity.
- **Server-side persistence**: Out of scope per spec; would require new API endpoints and DB schema.
- **sessionStorage**: Rejected — data lost on tab close, violating conversation persistence requirement.

---

## R-006: MCP Server API Contract

**Task**: What is the exact request/response contract for the chat endpoint?

**Decision**: Use existing MCP contracts without modification.

**Request** (`POST /mcp/chat/stream`):
```json
{
  "Message": "string",
  "ConversationId": "string | null",
  "Context": "string | null",
  "ConversationHistory": [
    { "Role": "user | assistant", "Content": "string" }
  ],
  "Action": "string | null",
  "ActionContext": "object | null"
}
```

**SSE Events**:
- `event: progress` — `data: { "step": "string", "detail": "string", "timestamp": "ISO8601" }`
- `event: result` — `data: { "Success": bool, "Response": "string", "ConversationId": "string", "AgentName": "string", "IntentType": "string", "ProcessingTimeMs": number, "ToolsExecuted": [...], "Errors": [...], "SuggestedActions": [...], "RequiresFollowUp": bool }`
- `event: error` — `data: { "ErrorCode": "string", "Message": "string", "Suggestion": "string" }`

**Context Field**: JSON string containing dashboard context:
```json
{
  "page": "portfolio | system-detail | capabilities | ...",
  "systemId": "string | null",
  "boundaryId": "string | null",
  "entityType": "string | null",
  "entityId": "string | null"
}
```

---

## R-007: File Attachment Handling

**Task**: How to handle file attachments for CKL/XCCDF/Prisma CSV/Nessus imports (FR-019)?

**Decision**: Read file content client-side and include as base64 in the `ActionContext` field of the `ChatRequest`. The MCP Server already handles file-based import actions (STIG, Prisma, Nessus importers) — the chat message will trigger the appropriate agent action.

**Rationale**: The MCP Server's existing import agents already process these file formats. The `Action` and `ActionContext` fields in `ChatRequest` allow specifying a tool invocation with parameters. The chat panel simply needs to read the file, detect its type by extension, and populate the request accordingly.

**Key Implementation Details**:
- Accepted extensions: `.ckl`, `.xccdf`, `.xml` (STIG), `.csv` (Prisma), `.nessus`
- Max file size: 10MB (FR-019)
- File reading: `FileReader.readAsText()` for XML/CSV, `FileReader.readAsDataURL()` for binary
- Multiple files: supported (attach multiple, each processed in sequence)
- UI: drag-and-drop zone + file picker button, attachment chips with remove button

**Alternatives Considered**:
- **Multipart form upload to separate endpoint**: Rejected — would require a new backend endpoint; using `ActionContext` leverages existing infrastructure.
- **Streaming file upload**: Over-engineered for 10MB max files; base64 encoding in ActionContext is sufficient.
