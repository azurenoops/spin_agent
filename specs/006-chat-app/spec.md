# Feature Specification: Security Posture Intelligence Navigator Chat Application

**Feature Branch**: `006-chat-app`  
**Created**: 2026-02-23  
**Status**: Draft  
**Input**: User description: "Full-stack conversational chat application — ASP.NET Core 9.0 backend with REST APIs and SignalR hub for real-time messaging, React 18 + TypeScript frontend styled with Tailwind CSS, serving as the primary user interface for the Security Posture Intelligence Navigator."

## Clarifications

### Session 2026-02-23

- Q: Should the chat application sanitize rendered Markdown content to prevent XSS, and if so, where (server-side, client-side, or both)? → A: Sanitize at render time only — rely on React/react-markdown's built-in AST-based escaping; no explicit server-side sanitization required for V1.
- Q: What is the data retention policy for conversations and messages? → A: No automatic retention or purging; users manually delete conversations when no longer needed. The chat app is a thin UI client, not the system of record — compliance artifacts are retained by the MCP Server under its own retention policies.
- Q: Should the chat application enforce rate limiting on message submissions and file uploads? → A: Client-side debounce only — disable the Send button while processing to prevent duplicate submissions. No server-side rate limiting in the chat app; the MCP Server enforces its own rate limits.
- Q: What level of accessibility (a11y) support is required for V1? → A: Basic keyboard navigation and semantic HTML — proper heading hierarchy, form labels, ARIA labels on status indicators, Escape to dismiss modals. Full WCAG 2.1 AA compliance deferred to a future release.
- Q: When AI processing fails, what level of error detail should be shown to the user? → A: Show a categorized user-friendly error reason (e.g., "service unavailable", "request timed out", "processing error") with full technical details stored in message metadata for developers/admins.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Send a Message and Receive an AI Response (Priority: P1)

A user opens the chat application, starts or selects a conversation, types a natural-language question or command (e.g., "Show me the current compliance status"), and submits it. The message appears immediately in the chat window. A processing indicator displays while the backend forwards the message to the MCP Server for AI processing. Within seconds, the AI-generated response appears in the conversation with rich formatting (Markdown, code blocks, tables). The user can continue the conversation with follow-up messages that maintain context.

**Why this priority**: This is the core value proposition — without the ability to send and receive messages through the copilot, no other feature has meaning. This story validates the full end-to-end pipeline: user input → backend → MCP Server → AI response → rendered output.

**Independent Test**: Can be fully tested by launching the application, typing a message, and verifying an AI response appears. Delivers the fundamental conversational capability.

**Acceptance Scenarios**:

1. **Given** a user has the chat application open and a conversation is active, **When** the user types a message and presses Enter (or clicks Send), **Then** the message appears immediately in the chat window as a user message with a "Sending" status indicator.
2. **Given** a user message has been submitted, **When** the backend begins processing, **Then** a "Processing your request..." indicator appears in the chat window within 1 second.
3. **Given** the MCP Server has generated a response, **When** the response is delivered back, **Then** the AI response appears in the chat window with proper Markdown formatting (headings, lists, code blocks, tables) and a "Completed" status.
4. **Given** the user sends a follow-up message in the same conversation, **When** the backend processes it, **Then** the conversation history (up to the last 20 messages) is included for context-aware responses.
5. **Given** the MCP Server's primary endpoint is unavailable, **When** the user sends a message, **Then** the system automatically falls back to the legacy endpoint, and the user still receives a response (with no visible error).

---

### User Story 2 — Manage Conversations (Priority: P2)

A user can create new conversations, browse a list of previous conversations in a sidebar, select a conversation to resume it, search across conversations by title or message content, and delete conversations they no longer need. Each conversation has an auto-generated title based on the first message, and conversations are sorted by most recently updated.

**Why this priority**: Conversation management enables users to organize and revisit their work. Without it, every session starts from scratch and users lose valuable context from past interactions.

**Independent Test**: Can be tested by creating multiple conversations, verifying they appear in the sidebar sorted by recency, searching for a specific term, selecting a past conversation to load its messages, and deleting one to confirm removal.

**Acceptance Scenarios**:

1. **Given** a user clicks "New Conversation," **When** the conversation is created, **Then** it appears at the top of the sidebar with the title "New Conversation" and becomes the active conversation.
2. **Given** a user sends the first message in a new conversation, **When** the message is saved, **Then** the conversation title is auto-generated from the message content (e.g., a message mentioning "compliance" produces "ATO Compliance Discussion").
3. **Given** a user has multiple conversations, **When** they view the sidebar, **Then** conversations are listed in most-recently-updated order with relative date labels (Today, Yesterday, N days ago).
4. **Given** a user types a search term in the sidebar search box, **When** results are filtered, **Then** only conversations whose title or message content contains the search term are shown.
5. **Given** a user clicks the delete button on a conversation, **When** they confirm the deletion, **Then** the conversation and all its messages and attachments are permanently removed.

---

### User Story 3 — Real-Time Messaging via SignalR (Priority: P3)

Messages are delivered in real-time through a persistent connection. When a user sends a message, they immediately see it in the chat (optimistic update). Processing events and AI responses arrive as they happen — no polling or page refresh required. A connection status indicator shows whether real-time features are active, and the system automatically reconnects if the connection drops.

**Why this priority**: Real-time communication provides a responsive, modern chat experience. While the application could function with polling-based REST calls alone, SignalR enables instant feedback (processing indicators, typing notifications) that significantly improves perceived performance and user satisfaction.

**Independent Test**: Can be tested by sending a message and verifying the processing indicator appears without page refresh, then observing the AI response arriving in real-time. Connection status changes can be tested by temporarily disrupting the network.

**Acceptance Scenarios**:

1. **Given** a user opens the application, **When** the page loads, **Then** a SignalR connection is established and a green "Real-time features active" indicator is displayed.
2. **Given** a SignalR connection is active and a user sends a message, **When** the message is sent, **Then** a "MessageProcessing" event is broadcast and a processing indicator appears in the chat without any page refresh.
3. **Given** a SignalR connection drops, **When** the disconnection is detected, **Then** an orange "Real-time features unavailable" warning is displayed and the system automatically attempts to reconnect.
4. **Given** the connection has been lost and then restored, **When** reconnection succeeds, **Then** the indicator returns to green and real-time messaging resumes without user intervention.

---

### User Story 4 — View Rich AI Response Metadata (Priority: P4)

When the AI responds, the user sees not just the text response but also rich metadata: how the AI classified their intent (with confidence score), which tools were executed (with expandable result details), multi-step workflow progress (with a visual progress bar and step counts), and proactive suggestions for next steps (clickable cards that auto-fill the input). This metadata empowers users to understand what the copilot did, trust the results, and efficiently continue their work.

**Why this priority**: Rich metadata transforms the chat from a simple Q&A interface into an intelligent assistant dashboard. Users gain transparency into AI decision-making and actionable next-step guidance, which increases trust, reduces follow-up questions, and accelerates task completion.

**Independent Test**: Can be tested by sending a message that triggers a tool execution (e.g., asking about compliance status), verifying the intent classification badge appears, expanding the tool result panel to see JSON details, and clicking a suggestion card to see the input auto-fill.

**Acceptance Scenarios**:

1. **Given** an AI response includes intent classification metadata, **When** the response is rendered, **Then** a badge displaying the intent type and confidence percentage (e.g., "Intent: compliance_check — 92%") appears below the response.
2. **Given** an AI response includes a tool execution result, **When** the response is rendered, **Then** a collapsible panel labeled "Tool Executed: {toolName}" appears. Clicking the panel header expands it to show the tool result as formatted JSON.
3. **Given** an AI response includes a multi-step tool chain, **When** the response is rendered, **Then** a progress panel shows step completion (e.g., "3/5 steps"), a visual progress bar, status text, and success rate.
4. **Given** an AI response includes proactive suggestions, **When** the response is rendered, **Then** suggestion cards appear with title, description, priority badge, and a clickable prompt. Clicking a suggestion auto-fills the chat input with the suggested prompt.

---

### User Story 5 — Attach Files to Messages (Priority: P5)

A user can attach files (documents, images, configuration files, logs, code files) to a message before sending. Attached files are uploaded, stored, and associated with the message. If a user attaches a file without typing a message, the system auto-generates an analysis prompt (e.g., "Analyze this .yaml document for compliance and security issues"). File size is limited to 10 MB.

**Why this priority**: File attachments enable users to share artifacts for analysis — compliance documents, configuration files, deployment logs — which are essential for many ATO workflows. However, the core conversational capability is more fundamental.

**Independent Test**: Can be tested by attaching a file to a message, verifying the file preview appears, sending the message, and confirming the attachment is stored and visible in the conversation history.

**Acceptance Scenarios**:

1. **Given** a user clicks the attachment button, **When** they select one or more files, **Then** file preview badges appear above the input area showing each filename with a remove button.
2. **Given** a user has attached files and typed a message, **When** they send, **Then** the message is sent with the attached file IDs and the attachments appear in the rendered message.
3. **Given** a user attaches a file without typing any text, **When** they send, **Then** an analysis prompt is auto-generated based on the file extension (e.g., "Analyze this .json document for compliance and security issues").
4. **Given** a user attempts to upload a file larger than 10 MB, **When** the upload is submitted, **Then** an error message is displayed and the upload is rejected.
5. **Given** a conversation with attachments is deleted, **When** the deletion is confirmed, **Then** the associated files are removed from the filesystem as well as the database.

---

### User Story 6 — Application Settings and Navigation (Priority: P6)

A user can toggle the conversation sidebar open and closed, view application information and version number in a settings modal, and reference keyboard shortcuts (Ctrl+K for sidebar toggle, Ctrl+N for new conversation, Enter to send, Shift+Enter for new line). The header displays the current conversation title or the application name when no conversation is selected.

**Why this priority**: Settings and navigation polish the user experience but are not critical to core functionality. The application is fully usable without these refinements.

**Independent Test**: Can be tested by clicking the menu button to toggle the sidebar, opening settings to view version info, and using keyboard shortcuts.

**Acceptance Scenarios**:

1. **Given** the sidebar is open, **When** the user clicks the menu toggle button, **Then** the sidebar slides out of view and the chat area expands to fill the space.
2. **Given** the user clicks the settings gear icon, **When** the settings modal opens, **Then** it displays the application name, version (1.0.0), feature list, and keyboard shortcuts.
3. **Given** a conversation is selected, **When** the user views the header, **Then** the header displays the conversation's title. When no conversation is selected, the header displays "Security Posture Intelligence Navigator."

---

### Edge Cases

- What happens when the user sends an empty message (whitespace only)? The send button should be disabled; the message should not be submitted.
- What happens when the MCP Server is completely unreachable (both primary and fallback endpoints)? The user receives a categorized error message (e.g., "The AI service is temporarily unavailable — please try again shortly") and the message is marked with an "Error" status. Full technical details are stored in message metadata. The application does not crash.
- What happens when the SignalR connection fails on initial page load? The application still renders with an orange disconnection warning; REST-based operations (loading conversations, viewing history) still work; the system automatically attempts reconnection using a graduated backoff schedule (immediate, 2s, 5s, 10s, 30s).
- What happens when a user rapidly sends multiple messages? The Send button and input are disabled while a message is processing, preventing duplicate submissions. Optimistic UI updates ensure each submitted message appears immediately.
- What happens when two browser tabs have the same conversation open? Both tabs are in the same SignalR group; messages sent from one tab appear in the other via real-time broadcasting.
- What happens when the database does not exist on first startup? The application automatically creates the database via `EnsureCreatedAsync` and logs whether it was newly created or already existed.
- What happens when a file upload fails (e.g., network interruption)? The user receives an error message and the failed attachment is not associated with the message.
- What happens when conversation search returns no results? An empty state with "No conversations found" is displayed.
- What happens when the conversation history exceeds 20 messages? Only the most recent 20 messages are sent to the MCP Server for context; all messages remain visible in the UI.
- What happens when a keyboard-only user navigates the interface? All core workflows (send message, create/select/delete conversation, dismiss modals, toggle sidebar) are accessible via keyboard. Interactive elements are reachable via Tab, modals close on Escape, and status indicators have ARIA labels for screen readers.
- What happens when an AI response contains potentially malicious HTML or script content? The Markdown renderer uses AST-based parsing (not raw HTML injection), which inherently escapes unsafe content. No explicit server-side sanitization is applied — client-side rendering protection is sufficient for V1.

## Requirements *(mandatory)*

### Functional Requirements

#### Conversation Management

- **FR-001**: System MUST allow users to create new conversations with an optional title and user ID.
- **FR-002**: System MUST auto-generate a conversation title from the first message content using keyword-based patterns (compliance, cost, deploy) with fallback to truncation at 50 characters.
- **FR-003**: System MUST list conversations for a user, sorted by most-recently-updated, excluding archived conversations, with pagination support (skip/take).
- **FR-004**: System MUST allow users to search conversations by title or message content, limited to 20 results.
- **FR-005**: System MUST allow users to permanently delete a conversation, which cascades to remove all associated messages, attachments, and files on disk.

#### Messaging

- **FR-006**: System MUST accept user messages containing text content along with optional file attachment IDs and contextual metadata.
- **FR-007**: System MUST persist each user message with a unique ID, conversation association, role (User/Assistant/System/Tool), timestamp, and lifecycle status (Sending/Sent/Processing/Completed/Error/Retry).
- **FR-008**: System MUST forward user messages to the MCP Server at `/mcp/chat` along with the last 20 messages of conversation history for context-aware AI processing.
- **FR-009**: System MUST persist the AI-generated response as an Assistant message with metadata including intent type, confidence score, tool execution details, processing time, and proactive suggestions.
- **FR-010**: System MUST fall back to the legacy `/api/chat/query` endpoint when the primary MCP Server endpoint fails, transparently returning a response to the user.
- **FR-011**: System MUST gracefully handle AI processing errors by displaying a categorized, user-friendly error reason (e.g., "The AI service is temporarily unavailable", "The request timed out — try a shorter question", "The request could not be processed") and marking the assistant message with "Error" status. Full technical error details (error type, HTTP status, stack context) MUST be stored in message metadata for developer/admin diagnostics but MUST NOT be shown to end users.

#### Real-Time Communication

- **FR-012**: System MUST establish a persistent bidirectional connection for real-time message delivery when the application loads.
- **FR-013**: System MUST broadcast a "MessageProcessing" event to the conversation group when message processing begins.
- **FR-014**: System MUST broadcast a "MessageReceived" event with the complete AI response when processing completes, or a "MessageError" event on failure.
- **FR-015**: System MUST support conversation-scoped groups so that events are only broadcast to participants of the relevant conversation.
- **FR-016**: System MUST support automatic reconnection when the real-time connection drops, with visual indication of connection status.
- **FR-017**: System MUST support a typing notification event that broadcasts to other participants in the conversation group.

#### File Attachments

- **FR-018**: System MUST allow users to upload file attachments up to 10 MB per file, supporting Document, Image, Code, Configuration, and Log types.
- **FR-019**: System MUST store uploaded files with unique GUID-based filenames while preserving the original filename and MIME type in metadata.
- **FR-020**: System MUST auto-generate an analysis prompt when a user sends an attachment without text content, tailored to the file extension.
- **FR-021**: System MUST delete attachment files from storage when the parent conversation is deleted.

#### User Interface

- **FR-022**: System MUST render AI responses with full Markdown support including headings, lists, code blocks with syntax highlighting, tables, blockquotes, and inline formatting.
- **FR-023**: System MUST display intent classification badges showing intent type and confidence percentage on AI responses that include this metadata.
- **FR-024**: System MUST display expandable tool execution result panels on AI responses that include tool results, showing formatted JSON output.
- **FR-025**: System MUST display multi-step workflow progress panels showing step counts, visual progress bars, status, and success rate on AI responses that include tool chain data.
- **FR-026**: System MUST display clickable proactive suggestion cards on AI responses that include suggestions, auto-filling the chat input when clicked.
- **FR-027**: System MUST auto-scroll the message list to the newest message when new messages arrive.
- **FR-028**: System MUST provide a collapsible conversation sidebar with search, conversation list, and create/delete actions.
- **FR-029**: System MUST display a connection status indicator showing whether real-time features are active or unavailable.
- **FR-030**: System MUST provide a settings modal showing application version, feature list, and keyboard shortcuts.

#### Data & Persistence

- **FR-031**: System MUST persist conversations, messages, conversation context, and message attachments in a relational database.
- **FR-032**: System MUST support both file-based databases for local development and server-based databases for production, auto-detected from the connection string format.
- **FR-033**: System MUST auto-create the database schema on first startup if it does not already exist.
- **FR-034**: System MUST store arbitrary metadata on conversations, messages, contexts, and attachments as serialized JSON.
- **FR-035**: System MUST support conversation context objects that track contextual metadata (type, title, summary, tags, custom data) with last-accessed timestamps, using an upsert pattern.

#### Deployment & Operations

- **FR-036**: System MUST serve the frontend as static files from the backend in production, with a catch-all route that serves the SPA for any unmatched path (excluding API and hub routes).
- **FR-037**: System MUST provide a health check endpoint for container orchestration and monitoring.
- **FR-038**: System MUST support secrets management via Azure Key Vault with managed identity, gracefully degrading if Key Vault is unavailable.
- **FR-039**: System MUST log structured events including request processing, connection lifecycle, message operations, and errors.
- **FR-040**: System MUST run as a non-root user in containerized deployments.
- **FR-041**: System MUST render Markdown content using AST-based parsing that inherently escapes unsafe HTML and script content, preventing cross-site scripting (XSS) without requiring explicit server-side sanitization.
- **FR-042**: System MUST NOT automatically delete, archive, or purge conversations based on age or inactivity. Conversation lifecycle is entirely user-controlled via explicit delete actions.
- **FR-043**: System MUST disable the message input and Send button while a message is being processed, preventing duplicate submissions. No server-side rate limiting is required; the MCP Server enforces its own rate limits.
- **FR-044**: System MUST use semantic HTML elements (proper heading hierarchy, labeled form controls, button elements for interactive actions) and ARIA attributes on status indicators (connection status, processing state) to support screen readers.
- **FR-045**: System MUST support keyboard navigation for core workflows: Enter to send messages, Escape to dismiss modals, Tab to navigate between interactive elements, and documented keyboard shortcuts (Ctrl+K, Ctrl+N).

### Key Entities

- **Conversation**: A top-level container for a chat session. Identified by a unique ID, owned by a user, with a title (auto-generated or user-provided), timestamps for creation and last update, an archive flag, and arbitrary key-value metadata. Has many Messages and an optional Context.
- **ChatMessage**: An individual message within a conversation. Carries text content, a role indicating sender type (User, Assistant, System, Tool), a lifecycle status, a timestamp, optional metadata (intent, tool results, suggestions, errors), optional threaded reply reference, associated tool names, tool execution result, and file attachments.
- **ConversationContext**: Contextual metadata for a conversation — categorized by type (e.g., "ato_scan", "deployment"), with a title, summary, arbitrary data, creation and last-access timestamps, and categorization tags. One-to-one with Conversation.
- **MessageAttachment**: A file attached to a message. Stores the original filename, MIME type, file size, storage path, attachment type category (Document, Image, Code, Configuration, Log), upload timestamp, and arbitrary metadata. Many-to-one with ChatMessage.
- **ToolExecutionResult**: The result of a tool invocation — captures the tool name, success/failure, result payload, error message, input parameters, execution time, and duration. Embedded within a ChatMessage as serialized metadata.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can send a message and receive an AI-generated response within 30 seconds for typical queries (excluding complex multi-step tool chains).
- **SC-002**: The real-time connection is established within 3 seconds of page load under normal network conditions, with automatic reconnection completing within 10 seconds of a connection drop.
- **SC-003**: Processing indicators appear within 1 second of message submission, giving users immediate feedback that their request is being handled.
- **SC-004**: AI responses render with proper Markdown formatting (headings, lists, code blocks, tables) 100% of the time — no raw Markdown syntax is ever visible to the user.
- **SC-005**: All User Story 1 acceptance scenarios (send message, see processing indicator, receive formatted response, maintain context) pass end-to-end without external documentation — the interface is self-explanatory for the core workflow.
- **SC-006**: Conversation search returns relevant results within 2 seconds for repositories of up to 1,000 conversations.
- **SC-007**: File attachments up to 10 MB upload successfully within 5 seconds on standard network connections.
- **SC-008**: The application remains functional (conversations load, messages display, history is accessible) even when the real-time connection is unavailable, degrading gracefully to REST-only operation.
- **SC-009**: The application starts and is ready to accept user input within 10 seconds, including database initialization on first run.
- **SC-010**: All proactive suggestion cards are clickable and auto-fill the input with the suggested prompt, allowing users to continue their workflow with a single click.

## Assumptions

- The MCP Server is a pre-existing service deployed separately; this chat application is a consumer of its `/mcp/chat` endpoint. The MCP Server handles all AI logic, intent classification, tool orchestration, and response generation.
- Users are identified by a simple string user ID (defaulting to "default-user") with no authentication enforcement within the chat application itself. Authentication is assumed to be handled at the network/infrastructure layer or by the MCP Server.
- The application targets a single-user-per-browser-session model. Multi-user real-time collaboration (multiple users in the same conversation) is architecturally supported via SignalR groups but is not a primary use case for this release.
- File attachments are stored on the local filesystem in an `uploads/` directory. Cloud-based blob storage (e.g., Azure Blob Storage) is a future enhancement.
- The frontend build toolchain uses Create React App with `react-scripts`. Migration to a modern bundler (Vite, Next.js) is out of scope.
- Conversation history sent to the MCP Server is limited to the most recent 20 messages. This is sufficient for context-aware responses while keeping request payloads manageable.
- The AI processing timeout is set to 180 seconds to accommodate complex multi-step tool chain operations. This is a known trade-off between responsiveness and completeness.
- Conversation data has no automatic retention or purging policy. Users manually delete conversations when no longer needed. Compliance data retention is the responsibility of the MCP Server, not the chat UI.
