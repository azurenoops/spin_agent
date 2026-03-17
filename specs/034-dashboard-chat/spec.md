# Feature Specification: Dashboard Chat Side Panel

**Feature Branch**: `034-dashboard-chat`  
**Created**: 2026-03-16  
**Status**: Draft  
**Input**: User description: "Add a chat side panel to the dashboard with full MCP chat functionality including streaming, tool execution, conversation management, and rich response rendering"

## Clarifications

### Session 2026-03-16

- Q: Should the chat panel persist its state (open/closed, active conversation) across page navigations within the dashboard? → A: Yes. The chat panel is a global side panel that persists across all dashboard pages. Its open/closed state and active conversation survive navigation; scroll position resets to the bottom of the message list on each page change for simplicity. Conversation history persists in local storage for offline recall; messages are not persisted server-side by the dashboard (the MCP Server is the system of record).
- Q: Should the chat panel be aware of the current dashboard context (e.g., the system being viewed, the page the user is on)? → A: Yes. The chat panel automatically includes contextual metadata in each request — the current page, selected system ID, and any active entity (component, capability, boundary). This enables context-aware AI responses without the user needing to re-state what they're looking at.
- Q: How should streaming responses be delivered — SSE via the existing `/mcp/chat/stream` endpoint or a new WebSocket/SignalR connection? → A: Use the existing SSE streaming endpoint (`POST /mcp/chat/stream`) for response streaming. This matches the VS Code extension's approach and requires no new backend infrastructure. The dashboard's API client already communicates with the MCP server; SSE is the simplest addition.
- Q: How should the chat panel identify the user and their active RMF role to the MCP Server? → A: Full Azure AD / Entra ID authentication — the chat panel obtains a token and sends it in the Authorization header. In development environments, authentication is bypassed (requests sent anonymously), matching the existing MCP Server dev configuration.
- Q: What maximum conversation history depth should the dashboard chat send per request? → A: 20 messages (matching the VS Code extension default).
- Q: Should the dashboard chat panel support file attachments for import workflows (CKL, XCCDF, Prisma CSV, Nessus)? → A: Yes — support drag-and-drop and file picker for CKL, XCCDF, Prisma CSV, and Nessus file imports directly from the chat panel.
- Q: Should the chat panel push/resize the dashboard content or overlay on top of it? → A: Overlay mode — the chat panel floats on top of dashboard content with a subtle backdrop, keeping the dashboard layout stable.
- Q: Should the chat panel be accessible on narrow/mobile viewports or hidden (desktop-only)? → A: Full-width overlay below 768px — the panel remains accessible on tablets but switches to full-width mode.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Send a Message and Receive a Streaming AI Response (Priority: P1)

A user clicks the chat icon in the dashboard header to open the chat side panel. The panel slides in from the right edge of the screen. The user types a natural-language question (e.g., "What is the compliance status of my system?") and presses Enter or clicks Send. The message appears immediately in the chat panel. A streaming response begins — text tokens appear progressively as the AI generates them, along with progress steps indicating which agent and tools are being used. When the response completes, it renders with full Markdown formatting (headings, lists, code blocks, tables). The user can continue the conversation with follow-up messages that maintain context.

**Why this priority**: This is the fundamental capability — without sending and receiving messages, the chat panel has no purpose. This story validates the full pipeline: user input → MCP Server → streaming AI response → rendered output, all within the dashboard context.

**Independent Test**: Can be fully tested by opening the dashboard, clicking the chat icon, typing a compliance question, and verifying a streaming AI response appears with Markdown formatting.

**Acceptance Scenarios**:

1. **Given** the dashboard is open on any page, **When** the user clicks the chat icon in the header, **Then** a side panel slides in from the right occupying approximately 420px width without displacing the main content (overlay mode).
2. **Given** the chat panel is open, **When** the user types a message and presses Enter, **Then** the message appears immediately in the chat as a user bubble and the input is cleared.
3. **Given** a message has been sent, **When** the MCP Server begins streaming a response, **Then** text tokens appear progressively in real-time in the assistant bubble, and progress steps (e.g., "Routing to compliance agent…", "Executing tool: compliance_status…") appear as status indicators.
4. **Given** the response stream completes, **When** the final content is assembled, **Then** the response renders with full Markdown formatting including headings, bullet lists, code blocks, and tables.
5. **Given** the user sends a follow-up message, **When** the request is sent to the MCP Server, **Then** the conversation history (up to the configured limit) is included for context-aware responses.
6. **Given** the MCP Server is unreachable or returns an error, **When** the user sends a message, **Then** a user-friendly error message appears in the chat with a retry button.

---

### User Story 2 — Context-Aware Chat from Dashboard Pages (Priority: P2)

While viewing a specific system, component, capability, or boundary in the dashboard, the user opens the chat panel and asks a question. The chat automatically includes the current dashboard context (page, system ID, entity type, entity ID) in the request. The AI response is tailored to that context — for example, asking "What controls am I missing?" while viewing System X returns the gap analysis for System X specifically, without the user needing to name the system.

**Why this priority**: Context-awareness is what differentiates an embedded chat panel from a standalone chat app. Users expect the assistant to "see" what they're looking at, reducing friction and making the chat panel a natural extension of the dashboard UI.

**Independent Test**: Can be tested by navigating to a system detail page, opening the chat, asking a context-dependent question, and verifying the response references the correct system.

**Acceptance Scenarios**:

1. **Given** the user is on the System Detail page for "ACME System," **When** they open the chat and type "Show compliance status," **Then** the chat request includes `context.systemId` and the response shows compliance data specific to ACME System.
2. **Given** the user is on the Component Inventory page, **When** they ask "List all network components," **Then** the chat request includes `context.page: "components"` and the response is scoped to the component inventory.
3. **Given** the user navigates from System A to System B while the chat is open, **When** they send a new message, **Then** the context updates to reflect System B without losing the conversation history.
4. **Given** the user is on the Portfolio Dashboard (no specific system selected), **When** they ask a question, **Then** the request includes `context.page: "portfolio"` with no system scope, and the response covers the full portfolio.

---

### User Story 3 — Conversation Management (Priority: P3)

A user can start new conversations, switch between previous conversations, and clear conversation history. Conversations are stored locally and listed in a dropdown or collapsible section at the top of the chat panel. Each conversation has an auto-generated title based on the first message. The user can start a fresh conversation at any time without losing previous ones.

**Why this priority**: Conversation management enables users to organize and revisit past interactions. Without it, every chat session is a single continuous thread that becomes unwieldy over time.

**Independent Test**: Can be tested by starting a conversation, sending messages, starting a new conversation, switching back to the first, and verifying message history is preserved.

**Acceptance Scenarios**:

1. **Given** the chat panel is open, **When** the user clicks "New Conversation," **Then** the chat clears and a new conversation begins with a fresh context.
2. **Given** the user has multiple conversations, **When** they click the conversation selector, **Then** a list shows previous conversations with auto-generated titles and relative timestamps (e.g., "Today", "Yesterday").
3. **Given** the user selects a previous conversation, **When** it loads, **Then** the full message history for that conversation is restored in the chat panel.
4. **Given** the user clicks the delete/clear button on a conversation, **When** they confirm, **Then** the conversation and its messages are removed from local storage.
5. **Given** the user has not interacted with the chat, **When** they open the panel for the first time, **Then** a welcome message explains the capabilities and suggests example questions relevant to the current page.

---

### User Story 4 — Rich Response Rendering with Tool Evidence (Priority: P4)

When the AI executes tools during response generation, the chat panel displays rich metadata: which tools were executed, their results (expandable/collapsible), the agent used, and the intent classification. Proactive suggestion cards appear after responses, allowing one-click follow-up actions. Responses may include interactive elements like links to dashboard pages (e.g., "View System Detail" navigates within the dashboard).

**Why this priority**: Rich tool evidence and actionable suggestions elevate the chat from a text interface to an intelligent workflow assistant. Users gain transparency into AI reasoning and can act on recommendations immediately.

**Independent Test**: Can be tested by asking a question that triggers tool execution (e.g., "Check compliance status"), verifying tool evidence panels appear, and clicking a suggestion card to confirm it auto-fills the input.

**Acceptance Scenarios**:

1. **Given** the AI response includes tool execution data, **When** the response renders, **Then** a collapsible "Tools Executed" section lists each tool name with expandable result details.
2. **Given** the AI response includes proactive suggestions, **When** the response renders, **Then** clickable suggestion cards appear below the response. Clicking a card auto-fills the chat input with the suggested prompt.
3. **Given** the AI response references a dashboard entity (system, component, control), **When** the response renders, **Then** entity names are rendered as clickable links that navigate to the appropriate dashboard page.
4. **Given** the AI response includes an intent classification, **When** the response renders, **Then** a subtle badge shows the intent type and agent used (e.g., "compliance-agent").

---

### User Story 5 — Panel Toggle, Resize, and Keyboard Shortcuts (Priority: P5)

The chat panel can be toggled open/closed via a persistent icon in the dashboard header and via a keyboard shortcut. The panel supports resizing by dragging its left edge. The panel's open/closed state and width persist across page navigations and browser sessions. Focus management follows accessibility best practices — opening the panel focuses the input field; closing returns focus to the previously active element.

**Why this priority**: Panel ergonomics and keyboard accessibility make the chat a natural part of the dashboard workflow rather than a disruptive overlay. However, the core chat functionality must work before optimizing the container.

**Independent Test**: Can be tested by toggling the panel with the keyboard shortcut, resizing it, navigating to another page, and verifying the panel state persists.

**Acceptance Scenarios**:

1. **Given** the dashboard is open, **When** the user presses `Ctrl+Shift+C` (or `Cmd+Shift+C` on macOS), **Then** the chat panel toggles open or closed.
2. **Given** the chat panel is open, **When** the user drags the left edge of the panel, **Then** the panel width resizes between 320px minimum and 600px maximum.
3. **Given** the user has set a custom panel width and closes the panel, **When** they reopen it later (even after page navigation), **Then** the panel opens at the previously set width.
4. **Given** the chat panel is opened, **When** it finishes its slide-in animation, **Then** focus moves to the message input field.
5. **Given** the user presses `Escape` while the chat panel is focused, **When** the panel closes, **Then** focus returns to the element that was focused before the panel opened.

---

### Edge Cases

- What happens when the user sends a message while a previous response is still streaming? → The current stream is cancelled (AbortController) and the new message is sent. The partial response remains visible with a "Cancelled" indicator.
- What happens when the network drops mid-stream? → The partial response is preserved with an error indicator ("Connection lost") and a retry button that resends the last user message.
- What happens when local storage is full or unavailable? → Conversations gracefully degrade to session-only storage with a warning toast. The chat remains functional but history won't persist after the tab closes.
- What happens when the chat panel is open and the browser window is resized to a narrow width? → Below 768px viewport width, the chat panel switches to full-width overlay mode with a close button.
- What happens when the MCP Server returns a response with no content? → Display a "No response generated" message with a suggestion to rephrase the question.
- What happens when the user attaches an unsupported file type? → Display an inline error listing the accepted formats (.ckl, .xccdf, .csv, .nessus) and reject the attachment without sending a request.
- What happens when a file attachment exceeds the maximum upload size? → Display an error with the size limit and reject the attachment. Maximum file size is 10 MB.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The dashboard MUST include a persistent chat icon in the header that toggles a side panel open/closed.
- **FR-002**: The chat panel MUST send messages to the MCP Server via `POST /mcp/chat/stream` and render the streaming response in real-time.
- **FR-003**: The chat panel MUST include the current dashboard context (page, systemId, entityType, entityId) in every MCP chat request.
- **FR-004**: The chat panel MUST support multi-turn conversations by including conversation history in requests.
- **FR-005**: The chat panel MUST render AI responses as formatted Markdown including headings, lists, code blocks, and tables.
- **FR-006**: The chat panel MUST display streaming progress steps (agent routing, tool execution) as status indicators during response generation.
- **FR-007**: The chat panel MUST support creating new conversations, switching between them, and deleting them.
- **FR-008**: Conversations MUST be persisted in browser local storage with auto-generated titles based on the first user message.
- **FR-009**: The chat panel MUST display tool execution evidence (tool name, expandable results) when included in the response.
- **FR-010**: The chat panel MUST render proactive suggestion cards that auto-fill the input when clicked.
- **FR-011**: The chat panel MUST render entity references (systems, controls, components) as clickable links that navigate within the dashboard.
- **FR-012**: The chat panel MUST be toggleable via keyboard shortcut (`Ctrl/Cmd+Shift+C`).
- **FR-013**: The chat panel MUST be resizable by dragging its left edge, with width persisted in local storage.
- **FR-014**: The chat panel MUST handle errors gracefully — network failures, server errors, and empty responses — with user-friendly messages and retry options.
- **FR-015**: The chat panel MUST authenticate the user via Bearer token in the Authorization header of all MCP requests. In development environments (no auth configured), authentication MUST be bypassed and requests sent anonymously. Azure AD / Entra ID integration SHOULD be implemented when MSAL is adopted as a cross-cutting dashboard concern.
- **FR-016**: The chat panel MUST support file attachments via drag-and-drop or file picker, accepting CKL, XCCDF, Prisma CSV, and Nessus (.nessus) files. Attached files are sent as part of the MCP chat request to trigger the appropriate import workflow.
- **FR-017**: Below 768px viewport width, the chat panel MUST switch to full-width overlay mode with a close button. The panel remains fully functional on tablet-sized viewports.
- **FR-018**: The chat panel MUST cancel in-flight streaming requests when the user sends a new message or closes the panel.
- **FR-019**: The chat panel MUST display a welcome message with contextual example questions on first open.
- **FR-020**: The chat panel MUST maintain focus management (focus input on open, restore focus on close, trap focus within panel when open).

### Key Entities

- **Conversation**: A sequence of messages between user and assistant. Has an ID, title (auto-generated), creation timestamp, and an ordered list of messages. Persisted in local storage only.
- **Message**: A single chat message. Has a role (user/assistant/system), content (text/Markdown), timestamp, and optional metadata (tools executed, intent, suggestions, errors).
- **ChatContext**: Metadata about the user's current dashboard state — page name, system ID, entity type, entity ID. Sent with every request to enable context-aware responses.

## Assumptions

- The MCP Server's `POST /mcp/chat/stream` SSE endpoint is available and returns the same event format used by the VS Code extension (progress, result, error event types).
- The dashboard's existing `apiClient` (Axios) base URL points to the MCP Server. In production, authentication headers are populated from the Azure AD / Entra ID token; in dev, no auth headers are sent.
- Conversation history is not persisted server-side — the dashboard is a thin client and the MCP Server does not store dashboard chat sessions.
- The maximum conversation history depth sent per request is 20 messages. Only the most recent 20 messages are included; older messages are retained in local storage but omitted from the request payload.
- Markdown rendering will use the same library pattern as the Chat App (`react-markdown` or equivalent).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can send a message and receive a streaming AI response within the dashboard in under 5 seconds for the first token.
- **SC-002**: The chat panel opens and is ready for input within 300ms of the toggle action.
- **SC-003**: Context-aware responses correctly reference the active system/entity at least 95% of the time when the user is on a detail page.
- **SC-004**: Users can manage at least 50 conversations in local storage without noticeable performance degradation.
- **SC-005**: The chat panel functions identically across Chrome, Edge, and Firefox (latest 2 versions).
- **SC-006**: All chat features from the VS Code extension (streaming, tool evidence, progress steps, suggestions, error handling) are available in the dashboard chat panel.
