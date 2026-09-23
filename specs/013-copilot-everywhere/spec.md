# Feature Specification: Copilot Everywhere — Multi-Channel Extensions & Channels Library

**Feature Branch**: `013-copilot-everywhere`  
**Created**: 2026-02-26  
**Status**: Draft  
**Input**: User description: "Develop Multi-Channel Copilot Extensions (Copilot Everywhere) — VS Code GitHub Copilot Chat participant, M365 Copilot declarative agent, and .NET Channels class library for message routing, connection tracking, and streaming responses"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Channel Abstraction Layer (Priority: P1)

As a backend developer, I need a shared .NET Channels library that provides message routing, connection tracking, and streaming response abstractions so that the MCP Server and Chat app can communicate with any client through a unified interface, regardless of transport mechanism.

**Why this priority**: This is the foundational layer that all other components depend on. Without the channel abstraction, neither the VS Code extension nor the M365 extension can receive routed messages. It also decouples the existing Chat app from transport-specific details.

**Independent Test**: Can be fully tested by instantiating InMemoryChannel, registering connections, joining conversations, sending messages, and verifying delivery — all without any external dependencies. Streaming can be validated by starting a stream, writing chunks, and verifying sequence numbers and completion signals.

**Acceptance Scenarios**:

1. **Given** a new connection arrives, **When** it registers via `IChannelManager.RegisterConnectionAsync`, **Then** a `ConnectionInfo` is returned with a unique connection ID, timestamps, and empty conversation list.
2. **Given** a registered connection, **When** it joins a conversation via `JoinConversationAsync`, **Then** subsequent messages sent to that conversation via `SendToConversationAsync` are delivered to that connection.
3. **Given** two connections in the same conversation, **When** one sends a message, **Then** both connections receive it via the channel.
4. **Given** a connection is unregistered, **When** `UnregisterConnectionAsync` is called, **Then** the connection is removed from all conversation groups and `IsConnectedAsync` returns false.
5. **Given** a streaming handler, **When** `BeginStreamAsync` is called and chunks are written, **Then** each chunk is delivered to the conversation with incrementing sequence numbers, and `CompleteAsync` delivers the full aggregated content.
6. **Given** a stream in progress, **When** `AbortAsync` is called, **Then** an error message is sent to the conversation and the stream is marked as not completable.
7. **Given** a `DefaultMessageHandler` with an `AgentInvoker` configured, **When** `HandleMessageAsync` is called, **Then** the user message is stored, an `AgentThinking` notification is sent, the invoker is called, and the response is stored as an assistant message.
8. **Given** a `DefaultMessageHandler` without an `AgentInvoker` and `DefaultHandlerBehavior` set to `Echo`, **When** `HandleMessageAsync` is called, **Then** the user message is returned as an `AgentResponse`. **Given** `DefaultHandlerBehavior` set to `Error`, **Then** an `Error` type message is returned instead.

---

### User Story 2 — GitHub Copilot Chat Extension (Priority: P2)

As a DevSecOps engineer working in VS Code, I want to type `@ato` in GitHub Copilot Chat and ask compliance questions, run assessments, and generate remediation scripts so that I can stay in my editor and get Security Posture Intelligence Navigator assistance without switching to the web Chat app.

**Why this priority**: VS Code is the primary IDE for engineers writing Infrastructure-as-Code. This integration meets users where they already work and provides the highest-value developer experience.

**Independent Test**: Can be tested by installing the extension in VS Code, typing `@ato /compliance Run a FedRAMP assessment`, and verifying that a streamed Markdown response appears in the Copilot Chat panel with the agent attribution footer.

**Acceptance Scenarios**:

1. **Given** the extension is installed and activated, **When** a user types `@ato How do I comply with AC-2?`, **Then** the query is forwarded to the MCP Server's `/mcp/chat` endpoint and the response is rendered as Markdown in the chat panel.
2. **Given** the extension is activated, **When** a user types `@ato /compliance Run FedRAMP assessment on subscription 123`, **Then** the request includes `targetAgent: "ComplianceAgent"` and `metadata.routingHint: "ComplianceAgent"` in the MCP payload.
3. **Given** the MCP Server is unreachable, **When** a user sends a message, **Then** an error message is displayed with a "Configure Connection" button that opens the settings editor.
4. **Given** a multi-turn conversation, **When** the user sends follow-up messages, **Then** the conversation history from previous turns is included in the request to maintain context.
5. **Given** the MCP Server returns templates (Bicep, Terraform, etc.), **When** the response is rendered, **Then** each template appears as a syntax-highlighted code block with a "Save" button and an appropriate type icon.
6. **Given** the user runs the "Analyze Current File" command on a `.bicep` file, **When** the analysis completes, **Then** a webview panel opens side-by-side showing compliance findings grouped by severity with control IDs and recommendations.
7. **Given** the user runs "Check ATO API Health" from the command palette, **When** the MCP Server is running, **Then** an information message appears. **When** the server is down, **Then** a warning message appears.

---

### User Story 3 — M365 Copilot Extension for Teams (Priority: P3)

As a compliance officer using Microsoft Teams, I want to interact with the Security Posture Intelligence Navigator through Teams chat and receive rich Adaptive Card responses so that I can request compliance assessments, view results, and trigger remediation without leaving my collaboration tool.

**Why this priority**: Teams/M365 is the communication hub for non-developer stakeholders (compliance officers, auditors, program managers). This integration broadens the copilot's reach beyond the engineering team.

**Independent Test**: Can be tested by sending a POST to `/api/messages` with `{ "text": "Run compliance scan for subscription 123", "conversation": { "id": "test-1" }, "from": { "id": "user-1" } }` and verifying the response contains an Adaptive Card attachment with compliance assessment results.

**Acceptance Scenarios**:

1. **Given** the M365 extension server is running, **When** a user sends "Run compliance assessment for my subscription" in Teams, **Then** the message is forwarded to the MCP Server and the response is returned as a styled Adaptive Card with assessment results.
2. **Given** the MCP Server returns a compliance assessment, **When** the response is rendered, **Then** the Adaptive Card shows an overall score percentage (color-coded), passed/warning/failed counts, and action buttons for "View Full Report" and "Generate Remediation Plan".
3. **Given** the AI needs more information, **When** `requiresFollowUp` is true in the response, **Then** a follow-up Adaptive Card is returned with the prompt, numbered missing fields, and quick reply buttons.
4. **Given** the MCP Server is unreachable, **When** a user sends a message, **Then** an error Adaptive Card is returned with the error details and help text.
5. **Given** a health check request, **When** `GET /health` is called, **Then** the server returns its name, version, and current timestamp.
6. **Given** infrastructure provisioning results, **When** the response includes a resource ID, **Then** the Adaptive Card includes a "View in Azure Portal" button linking to `https://portal.azure.us/#resource/${resourceId}`.

---

### User Story 4 — Compliance Analysis Commands (Priority: P2)

As an engineer writing Infrastructure-as-Code, I want to analyze the current file or entire workspace for compliance issues directly from the VS Code command palette so that I can identify NIST 800-53 violations before committing code.

**Why this priority**: File-level and workspace-level compliance scanning is a critical developer workflow that provides immediate, actionable feedback during the coding process.

**Independent Test**: Can be tested by opening a Bicep file and running "Security Posture Intelligence Navigator: Analyze Current File for Compliance" from the command palette, verifying that findings appear in a side-by-side webview panel with severity-colored badges.

**Acceptance Scenarios**:

1. **Given** a `.bicep` file is open, **When** the user runs "Analyze Current File for Compliance", **Then** the file content, name, and language are sent to `/mcp/chat` as a structured compliance analysis prompt and results appear in a webview panel.
2. **Given** a workspace with IaC files, **When** the user runs "Analyze Workspace for Compliance", **Then** all matching files (`*.bicep`, `*.tf`, `*.yaml`, `*.yml`, `*.json`) are analyzed and results appear in a workspace-level webview panel.
3. **Given** analysis results are displayed, **When** the user views the webview panel, **Then** findings are styled by severity (high=red, medium=orange, low=green) with control IDs and recommendations.

---

### User Story 5 — Export and Template Management (Priority: P3)

As a DevSecOps engineer, I want to export compliance analysis results in multiple formats and save generated IaC templates to my workspace so that I can share findings with stakeholders and immediately use generated code.

**Why this priority**: Export and template management complete the developer workflow by enabling output from the copilot to be persisted, shared, and used.

**Independent Test**: Can be tested by running a compliance analysis, then using export to generate Markdown/HTML/JSON reports and verifying file creation, or by saving a template and verifying it lands in the correct type-based folder.

**Acceptance Scenarios**:

1. **Given** compliance analysis results, **When** the user exports to Markdown, **Then** a report is generated with title, timestamp, summary, findings table, and detailed findings.
2. **Given** compliance results, **When** the user exports to HTML, **Then** a styled HTML page is generated with severity-colored badges, control IDs in monospace, and recommendations in green cards.
3. **Given** generated templates are returned by the MCP Server, **When** the user clicks "Save" on a template, **Then** the file is created in the correct type-based folder (e.g., `bicep/`, `terraform/`, `kubernetes/`).
4. **Given** multiple templates are available, **When** the user clicks "Save All Templates to Workspace", **Then** a project folder is created with all templates organized by type.
5. **Given** a file already exists at the target path, **When** the user saves a template, **Then** they are prompted with Overwrite, Cancel, or Save As New options.

---

### Edge Cases

- What happens when the MCP Server is not running when the VS Code extension activates? → Health check logs a warning; commands show user-friendly error messages with configuration button.
- What happens when a connection drops mid-stream? → Stream disposal auto-completes as a safety net. The channel silently skips sends to inactive connections.
- What happens when a conversation group has no connections? → Messages are silently dropped (no error).
- What happens when the M365 webhook receives a request with no message text? → Returns HTTP 400 with an error response.
- What happens when the user sends a message without a slash command in GitHub Copilot Chat? → The MCP Server routes based on intent classification (no `targetAgent` is specified).
- What happens when `MaxConnectionsPerUser` is exceeded? → Configuration option is documented but enforcement is deferred to the host application.
- What happens when a connection exceeds `IdleConnectionTimeout`? → The ChannelManager's periodic cleanup removes the connection from all conversation groups and marks it inactive.
- What happens when a stream exceeds `StreamTimeout`? → Configuration option is documented but enforcement is deferred to the host application.

## Requirements *(mandatory)*

### Functional Requirements

#### Channels Library (.NET)

- **FR-001**: System MUST provide an `IChannel` interface with `SendAsync`, `SendToConversationAsync`, `BroadcastAsync`, and `IsConnectedAsync` methods.
- **FR-002**: System MUST provide a `ChannelType` enum with values: `SignalR`, `WebSocket`, `LongPolling`, `ServerSentEvents`.
- **FR-003**: System MUST provide a `ChannelMessage` model with `MessageId`, `ConversationId`, `Type`, `Content`, `AgentType`, `Timestamp`, `Metadata`, `IsStreaming`, and `IsComplete` properties.
- **FR-004**: System MUST provide a `MessageType` enum with values: `UserMessage`, `AgentResponse`, `AgentThinking`, `ToolExecution`, `ToolResult`, `Error`, `SystemNotification`, `ProgressUpdate`, `ConfirmationRequest`, `StreamChunk`.
- **FR-005**: System MUST provide an `IChannelManager` interface for connection registration, conversation group management, and message routing.
- **FR-006**: System MUST provide a `ConnectionInfo` model tracking connection ID, user ID, timestamps, conversation memberships, and metadata.
- **FR-007**: `ChannelManager` MUST automatically remove a connection from all conversation groups when `UnregisterConnectionAsync` is called.
- **FR-008**: `ChannelManager` MUST remove empty conversation group entries when the last connection leaves.
- **FR-009**: `ChannelManager` MUST update `LastActivityAt` on connection info whenever a message is sent to that connection.
- **FR-010**: System MUST provide an `IMessageHandler` interface with `HandleMessageAsync` that accepts `IncomingMessage` and returns `ChannelMessage`.
- **FR-011**: `DefaultMessageHandler` MUST store user messages via `IConversationStateManager`, send `AgentThinking` notifications, invoke the configured `AgentInvoker`, and store the response.
- **FR-012**: `DefaultMessageHandler` MUST return a response based on `ChannelOptions.DefaultHandlerBehavior` when no `AgentInvoker` is configured: `Echo` (default) returns the user message as an `AgentResponse`, `Error` returns an `Error` type message. When an exception occurs, it MUST always return an `Error` type message regardless of the setting.
- **FR-013**: System MUST provide an `IStreamingHandler` with `BeginStreamAsync` that returns an `IStreamContext`.
- **FR-014**: `IStreamContext` MUST support `WriteAsync` (text and typed chunks), `CompleteAsync`, `AbortAsync`, and `IAsyncDisposable`.
- **FR-015**: `StreamContext` MUST auto-increment sequence numbers per chunk and buffer all content for aggregation on completion.
- **FR-016**: `StreamContext.DisposeAsync` MUST call `CompleteAsync` if the stream has not been completed or aborted — streams must never be left open.
- **FR-017**: System MUST provide an `InMemoryChannel` implementation using in-memory collections for single-instance deployments and testing.
- **FR-017a**: `ChannelManager` MUST periodically clean up connections that have been idle longer than the configured `IdleConnectionTimeout` (default 30 minutes), removing them from all conversation groups.
- **FR-018**: `InMemoryChannel` MUST silently skip sends to inactive connections (log warning, no exception).
- **FR-019**: System MUST provide `ChannelOptions` with configuration for idle timeout, max connections per user (enforcement deferred to host application), `DefaultHandlerBehavior` (Echo or Error, default Echo), and nested `StreamingOptions`. SignalR-specific settings are deferred until a SignalR-backed `IChannel` implementation is added in a future feature.
- **FR-020**: System MUST provide DI extension methods: `AddChannels(configuration)` for full registration and `AddInMemoryChannels()` for simplified testing registration.
- **FR-021**: `IncomingMessage` MUST support `TargetAgentType` for explicit agent routing and `Attachments` for file attachments.

#### GitHub Copilot Extension (VS Code)

- **FR-022**: Extension MUST register `@ato` as a GitHub Copilot Chat participant with `id: "ato"`, name `"ato"`, and sticky behavior.
- **FR-023**: Extension MUST provide three slash commands: `/compliance`, `/knowledge`, `/config`, each mapping to the corresponding MCP Server agent (ComplianceAgent, KnowledgeBaseAgent, ConfigurationAgent).
- **FR-024**: Extension MUST forward chat messages to the MCP Server's `/mcp/chat` endpoint with message text, conversation history, and optional `targetAgent`.
- **FR-025**: Extension MUST rebuild conversation history from `ChatContext.history` as `{ role, content }` pairs for each turn.
- **FR-026**: Extension MUST render responses as streamed Markdown in the Copilot Chat panel.
- **FR-027**: Extension MUST render templates with type-specific icons, syntax-highlighted code blocks, and "Save" buttons.
- **FR-028**: Extension MUST show agent attribution (`*Processed by: {agentUsed}*`) when the response includes an `agentUsed` field.
- **FR-029**: Extension MUST register four commands: `ato.checkHealth`, `ato.configure`, `ato.analyzeCurrentFile`, `ato.analyzeWorkspace`.
- **FR-030**: `ato.analyzeCurrentFile` MUST send file content, name, and language to `/mcp/chat` as a structured compliance analysis prompt and display results in a side-by-side webview panel.
- **FR-031**: `ato.analyzeWorkspace` MUST send workspace file contents to `/mcp/chat` as structured compliance analysis prompts and display aggregated results in a webview panel.
- **FR-032**: Extension MUST provide configurable settings: `ato-copilot.apiUrl`, `ato-copilot.apiKey`, `ato-copilot.timeout`, `ato-copilot.enableLogging`.
- **FR-033**: Extension MUST display user-friendly error messages for connection failures (ECONNREFUSED, ETIMEDOUT, HTTP errors) with a "Configure Connection" action.
- **FR-034**: Extension MUST perform a silent background health check on activation, warning only on failure.
- **FR-035**: Extension MUST provide an `ExportService` supporting Markdown, JSON, and HTML export formats, clipboard copy, editor preview, email sharing (via `mailto:` link with report summary), and file save.
- **FR-036**: Extension MUST provide a `WorkspaceService` for creating files, project folders, and infrastructure projects organized by template type.

#### M365 Copilot Extension (Teams)

- **FR-037**: Server MUST provide a `POST /api/messages` endpoint that accepts Teams webhook messages and returns Adaptive Card responses.
- **FR-038**: Server MUST forward messages to the MCP Server's `/mcp/chat` endpoint with conversation ID and source context (`m365-copilot`, platform `M365`).
- **FR-039**: Server MUST generate unique conversation IDs in the format `m365-{timestamp}-{random9}`.
- **FR-040**: Server MUST route responses to intent-specific Adaptive Card builders based on `intentType`: infrastructure, compliance, cost, deployment, resource_discovery, or generic.
- **FR-041**: Server MUST provide a follow-up Adaptive Card when `requiresFollowUp` is true, showing prompt, missing fields, and quick reply buttons.
- **FR-042**: Server MUST provide Adaptive Card builders for: infrastructure result, compliance assessment, cost estimate, deployment result, resource list, generic response, and error.
- **FR-043**: Compliance Adaptive Card MUST display overall score percentage (color-coded by threshold), passed/warning/failed counts, and action buttons.
- **FR-044**: Infrastructure result card MUST link to Azure Government portal (`portal.azure.us`) for resource viewing.
- **FR-045**: Server MUST provide `GET /health` endpoint returning service name, version, and timestamp.
- **FR-046**: Server MUST provide `GET /openapi.json` and `GET /ai-plugin.json` endpoints for M365 Copilot plugin discovery.
- **FR-047**: Server MUST provide a Teams app manifest (`manifest.json`) with declarative copilot configuration, conversation starters, and app identity.
- **FR-048**: Server MUST validate configuration on startup and log warnings for missing required variables.
- **FR-049**: ATOApiClient MUST use a 300-second timeout for long-running operations and include `User-Agent: ATO-Copilot-M365-Extension/1.0.0` header.
- **FR-050**: Server MUST handle graceful shutdown on `SIGINT` and `SIGTERM` signals.

### Key Entities

- **ChannelMessage**: The universal message payload sent through channels — has message ID, conversation ID, type, content, agent type, timestamp, streaming state, and extensible metadata.
- **ConnectionInfo**: Tracks a client connection — connection ID, user ID, connection/activity timestamps, conversation memberships, and metadata.
- **IncomingMessage**: An inbound message from any client — includes connection context, optional agent routing hint, and file attachments.
- **StreamChunk**: A typed chunk in a streaming response — sequence-numbered with content type classification (Text, Code, Markdown, Json, Tool, Progress).
- **MessageAttachment**: File attachment on an incoming message — name, content type, optional URL or binary data, and size.
- **ChannelOptions**: Configuration for channel behavior — SignalR settings, connection limits, timeouts, and streaming parameters.

## Clarifications

### Session 2026-02-26

- Q: Should building `/mcp/analyze-code` and `/mcp/analyze-repository` endpoints be part of this feature, or should analysis commands use the existing `/mcp/chat` endpoint? → A: Route analysis through existing `/mcp/chat` with structured prompts — no new backend endpoints needed.
- Q: FR-023 states "six slash commands" but only 3 agents exist (ComplianceAgent, KnowledgeBaseAgent, ConfigurationAgent). How many slash commands? → A: Three commands (`/compliance`, `/knowledge`, `/config`) matching the 3 existing agents.
- Q: Should the InMemoryChannel implement any eviction/cleanup strategy for idle connections? → A: Yes — periodic idle connection cleanup using the existing `IdleConnectionTimeout` from `ChannelOptions` (default 30 minutes).
- Q: The spec uses both "Security Posture Intelligence Navigator" and "Platform Copilot" in different places. Which canonical name? → A: "Security Posture Intelligence Navigator" everywhere — rename M365 references (manifest, app ID, domain, card headers) to match the codebase's `Ato.Copilot.*` naming.
- Q: FR-012 says DefaultMessageHandler returns an Error when no AgentInvoker is configured, but the user description says "echo". Which behavior? → A: Both — configurable via `ChannelOptions.DefaultHandlerBehavior` (Echo or Error), defaulting to Echo for testing convenience.

## Assumptions

- The MCP Server already exposes `/mcp/chat`, `/mcp/chat/stream`, `/health`, and `/mcp/tools` endpoints. File and workspace analysis commands route through `/mcp/chat` with structured prompts rather than dedicated analysis endpoints.
- The existing `Ato.Copilot.State` project's `IConversationStateManager` and `ConversationMessage` interfaces are sufficient for the Channels library's message storage needs — no new state abstractions are required.
- The existing placeholder `.cs` files in `src/Ato.Copilot.Channels/GitHub/` and `src/Ato.Copilot.Channels/M365/` will be removed or replaced, as the actual GitHub and M365 extensions are TypeScript projects living in `extensions/`, not in the .NET Channels library.
- The `InMemoryChannel` is sufficient for v1 deployment (single-instance). SignalR-backed channel implementations are deferred to a future feature.
- Azure AD / Bot Framework authentication for the M365 extension is configured externally — the extension reads credentials from environment variables but does not implement the OAuth flow itself.
- App icon files (`color.png` 192x192, `outline.png` 32x32) for the Teams manifest will use placeholder images initially.
- The VS Code extension targets VS Code 1.90+ which includes stable GitHub Copilot Chat API support.
- All components MUST use "Security Posture Intelligence Navigator" as the canonical product name. The M365 extension's Teams manifest, app ID, deploy domain, and Adaptive Card headers must use "Security Posture Intelligence Navigator" (not "Platform Copilot") for brand consistency with the `Ato.Copilot.*` codebase.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Engineers can type `@ato` in VS Code Copilot Chat and receive a compliance-related response within 30 seconds of the MCP Server processing time.
- **SC-002**: The Channels library handles 100 concurrent connections with message delivery to all conversation members without errors.
- **SC-003**: Streaming responses deliver chunks to clients with sequence-numbered ordering, and auto-completion on disposal ensures no orphaned streams.
- **SC-004**: The M365 extension returns well-formed Adaptive Card v1.5 JSON for all six response intent types plus error and follow-up cards.
- **SC-005**: All three components (Channels library, VS Code extension, M365 extension) connect to the same MCP Server endpoint, demonstrating multi-channel architecture.
- **SC-006**: File and workspace compliance analysis produce webview panels with severity-colored findings within 10 seconds for typical files.
- **SC-007**: Export service generates valid Markdown, JSON, and HTML formats that preserve all compliance finding data.
- **SC-008**: Template save operations correctly organize files into type-based folders (bicep/, terraform/, kubernetes/, etc.).
- **SC-009**: The M365 extension processes messages end-to-end (webhook → MCP Server → Adaptive Card) within 60 seconds including MCP Server processing time.
- **SC-010**: Unit test coverage for the channel abstraction layer exceeds 90% of public interface surface.
