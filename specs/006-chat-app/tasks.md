# Tasks: Security Posture Intelligence Navigator Chat Application

**Input**: Design documents from `/specs/006-chat-app/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/rest-api.md, contracts/signalr.md, quickstart.md

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the .NET project, scaffold the React frontend, and install all dependencies

- [x] T001 Create Ato.Copilot.Chat .NET web project, add to Ato.Copilot.sln, add project references (Core, Agents, State) and NuGet packages (EF Core SQLite + SqlServer + Design, Serilog.AspNetCore, Serilog.Sinks.Console) per plan.md dependencies in src/Ato.Copilot.Chat/Ato.Copilot.Chat.csproj
- [x] T002 [P] Scaffold React TypeScript frontend via Create React App in src/Ato.Copilot.Chat/ClientApp/
- [x] T003 [P] Install frontend dependencies (@microsoft/signalr ^8.0.0, axios, react-markdown, react-syntax-highlighter, @types/react-syntax-highlighter, tailwindcss ^3.4.17, postcss, autoprefixer) and initialize Tailwind config in src/Ato.Copilot.Chat/ClientApp/

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Define data model, database context, application bootstrap, and shared TypeScript types that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T004 Define all entities (Conversation, ChatMessage, ConversationContext, MessageAttachment), value object (ToolExecutionResult), enums (MessageRole, MessageStatus, AttachmentType), and DTOs (SendMessageRequest, ChatResponse, CreateConversationRequest) per data-model.md in src/Ato.Copilot.Chat/Models/ChatModels.cs
- [x] T005 Implement ChatDbContext with Fluent API configuration (field constraints, indexes per data-model.md), JSON ValueConverters for Dictionary<string,object>, List<string>, and ToolExecutionResult fields, and dual-provider registration (UseSqlite/UseSqlServer auto-detected from connection string) per research.md Topic 3: EF Core Dual-Provider in src/Ato.Copilot.Chat/Data/ChatDbContext.cs
- [x] T006 Configure Program.cs with DI registration (ChatDbContext, IChatService, SignalR), Serilog structured logging (console + file sinks), middleware pipeline (UseSerilogRequestLogging → UseSwagger/UI (dev) → UseHttpsRedirection → UseStaticFiles → UseRouting → UseCors → MapControllers → MapHub → MapHealthChecks → MapFallbackToFile per research.md middleware order), health check endpoint, and SPA fallback in src/Ato.Copilot.Chat/Program.cs
- [x] T007 [P] Configure appsettings.json with ChatDb connection string, MCP Server base URL, Serilog configuration (add Serilog.Sinks.ApplicationInsights for production), and appsettings.Development.json with SQLite connection string and SPA proxy settings in src/Ato.Copilot.Chat/appsettings.json and src/Ato.Copilot.Chat/appsettings.Development.json
- [x] T008 [P] Define TypeScript interfaces and enums (Conversation, ChatMessage, ConversationContext, MessageAttachment, ToolExecutionResult, MessageRole, MessageStatus, AttachmentType, SendMessageRequest, ChatResponse, ChatState, ChatAction) matching backend models in src/Ato.Copilot.Chat/ClientApp/src/types/chat.ts

**Checkpoint**: Foundation ready — backend builds, frontend compiles, database creates on startup. User story implementation can now begin.

---

## Phase 3: User Story 1 — Send a Message and Receive an AI Response (Priority: P1) 🎯 MVP

**Goal**: User types a message → it appears in the chat → AI response arrives with Markdown rendering. Full end-to-end pipeline: input → backend → MCP Server → response → rendered output.

**Independent Test**: Launch the application, type a message, press Enter. Verify the message appears immediately, a processing indicator shows, and an AI response renders with proper Markdown formatting.

### Tests for User Story 1

- [x] T009 [P] [US1] Unit tests for ChatService.SendMessageAsync: positive (successful MCP response returns ChatResponse), negative (MCP timeout returns categorized error, fallback endpoint used on primary failure), boundary (empty context window, max 20-message history) in tests/Ato.Copilot.Tests.Unit/Chat/ChatServiceTests.cs
- [x] T010 [P] [US1] Unit tests for MessagesController: positive (valid message returns ChatResponse with 200), negative (empty message returns 400, missing conversationId returns 400), boundary (pagination skip=0 take=50) in tests/Ato.Copilot.Tests.Unit/Chat/MessagesControllerTests.cs
- [x] T011 [P] [US1] Integration tests for POST /api/messages and GET /api/messages via WebApplicationFactory: happy path (send message → 200 + ChatResponse), error path (invalid conversationId → 400), pagination ordering (messages returned ascending by timestamp) in tests/Ato.Copilot.Tests.Integration/Chat/MessagesControllerIntegrationTests.cs

### Implementation for User Story 1

- [x] T012 [US1] Define IChatService interface with SendMessageAsync, GetMessagesAsync, GetConversationHistoryAsync (last 20 messages), and EnsureDatabaseCreatedAsync methods in src/Ato.Copilot.Chat/Services/IChatService.cs
- [x] T013 [US1] Implement ChatService.SendMessageAsync with HTTP POST to MCP Server /mcp/chat (with 20-message context window from conversation history), fallback to /api/chat/query on failure per research.md, persist user message (status Sent→Processing→Completed/Error), persist AI response with metadata (intentType, confidence, toolResults, suggestions, processingTimeMs), and categorized error handling (Timeout, ServiceUnavailable, ProcessingError per contracts/signalr.md error categories) in src/Ato.Copilot.Chat/Services/ChatService.cs
- [x] T014 [US1] Implement MessagesController with GET /api/messages (pagination via skip/take, ordered by timestamp ascending, includes attachments) and POST /api/messages (validate conversationId + message not empty, delegate to ChatService, return ChatResponse) per contracts/rest-api.md in src/Ato.Copilot.Chat/Controllers/ChatControllers.cs
- [x] T015 [P] [US1] Implement Axios-based REST API client with sendMessage and getMessages methods, base URL configuration, and error response handling in src/Ato.Copilot.Chat/ClientApp/src/services/chatApi.ts
- [x] T016 [US1] Implement ChatContext with useReducer for message state (actions: ADD_MESSAGE, UPDATE_MESSAGE_STATUS, SET_MESSAGES, SET_LOADING, SET_ERROR), ChatProvider wrapper with auto-database-creation on mount, and useChatContext hook in src/Ato.Copilot.Chat/ClientApp/src/contexts/ChatContext.tsx
- [x] T017 [US1] Implement ChatWindow component with scrollable message list (user/assistant message styling), Markdown rendering via react-markdown with react-syntax-highlighter for code blocks, message input area with Enter-to-send and Shift+Enter for newline, Send button disabled while processing (client-side debounce per FR-043), processing indicator ("Processing your request..." within 1s per SC-003), and auto-scroll to newest message per FR-027 in src/Ato.Copilot.Chat/ClientApp/src/components/ChatWindow.tsx
- [x] T018 [US1] Wire up App.tsx root component with ChatProvider, basic two-panel layout (sidebar placeholder + ChatWindow), and create index.tsx entry point with React 18 createRoot in src/Ato.Copilot.Chat/ClientApp/src/App.tsx and src/Ato.Copilot.Chat/ClientApp/src/index.tsx

**Checkpoint**: User Story 1 complete — users can send messages and receive AI responses with Markdown rendering. This is the MVP.

---

## Phase 4: User Story 2 — Manage Conversations (Priority: P2)

**Goal**: Users can create, list, search, select, and delete conversations in a sidebar. Conversations auto-title from the first message and sort by recency.

**Independent Test**: Create 3+ conversations, verify they appear in the sidebar sorted by most recent. Search for a keyword — only matching conversations show. Delete a conversation — confirm it and all its messages are removed.

### Tests for User Story 2

- [x] T019 [P] [US2] Unit tests for ChatService conversation methods: positive (CreateConversationAsync returns conversation with auto-title, GetConversationsAsync sorted by UpdatedAt desc, SearchConversationsAsync matches title + content), negative (GetConversationAsync with non-existent ID returns null, DeleteConversationAsync with non-existent ID throws), boundary (empty title auto-generates, pagination skip=0 take=20, search max 20 results) in tests/Ato.Copilot.Tests.Unit/Chat/ChatServiceConversationTests.cs
- [x] T020 [P] [US2] Integration tests for conversation endpoints via WebApplicationFactory: happy path (POST creates → GET returns → GET by ID returns with messages → DELETE removes → GET returns 404), error path (DELETE non-existent → 404), search (GET /search?query=keyword returns matching results) in tests/Ato.Copilot.Tests.Integration/Chat/ConversationsControllerIntegrationTests.cs

### Implementation for User Story 2

- [x] T021 [US2] Add conversation management methods to ChatService: CreateConversationAsync (with auto-title generation using keyword patterns per FR-002), GetConversationsAsync (filtered by userId, sorted by UpdatedAt desc, paginated), GetConversationAsync (with messages and context), SearchConversationsAsync (title + content search, max 20 results), DeleteConversationAsync (cascade delete messages, attachments, and files on disk per FR-005) in src/Ato.Copilot.Chat/Services/ChatService.cs
- [x] T022 [US2] Implement ConversationsController with GET /api/conversations (userId, skip, take query params), GET /api/conversations/{conversationId}, POST /api/conversations (optional title + userId), DELETE /api/conversations/{conversationId}, and GET /api/conversations/search (query + userId) per contracts/rest-api.md in src/Ato.Copilot.Chat/Controllers/ChatControllers.cs
- [x] T023 [US2] Add conversation API methods (createConversation, getConversations, getConversation, searchConversations, deleteConversation) to chatApi client in src/Ato.Copilot.Chat/ClientApp/src/services/chatApi.ts
- [x] T024 [US2] Add conversation state management to ChatContext: actions (SET_CONVERSATIONS, ADD_CONVERSATION, DELETE_CONVERSATION, SET_ACTIVE_CONVERSATION, SET_SEARCH_RESULTS), load conversations on mount, select conversation loads messages, auto-create first conversation if none exist in src/Ato.Copilot.Chat/ClientApp/src/contexts/ChatContext.tsx
- [x] T025 [US2] Implement ConversationList sidebar component with search input (filters on type), conversation list sorted by recency with relative date labels (Today, Yesterday, N days ago), "New Conversation" button, delete button with confirmation dialog, active conversation highlight, and empty state ("No conversations found") in src/Ato.Copilot.Chat/ClientApp/src/components/ConversationList.tsx
- [x] T026 [US2] Implement ConversationContext upsert method in ChatService: CreateOrUpdateContextAsync that saves/updates ConversationContext with activeTools, lastIntent, confidenceScore, and metadata per FR-035 and data-model.md in src/Ato.Copilot.Chat/Services/ChatService.cs

**Checkpoint**: User Stories 1 and 2 complete — users can manage multiple conversations and send/receive messages in each.

---

## Phase 5: User Story 3 — Real-Time Messaging via SignalR (Priority: P3)

**Goal**: Messages arrive in real-time via WebSocket. Optimistic UI updates show messages immediately. Connection status is visible. Automatic reconnection handles network interruptions.

**Independent Test**: Send a message and verify the processing indicator appears without page refresh. Open a second tab to the same conversation — messages sent in one appear in the other. Disconnect the network briefly — verify the reconnection indicator appears and resolves.

### Tests for User Story 3

- [x] T027 [P] [US3] Unit tests for ChatHub methods: positive (JoinConversation adds to group, SendMessage broadcasts MessageReceived, NotifyTyping broadcasts UserTyping), negative (SendMessage with null conversationId throws HubException, content >10,000 chars throws HubException), boundary (empty content string, LeaveConversation from non-joined group) in tests/Ato.Copilot.Tests.Unit/Chat/ChatHubTests.cs

### Implementation for User Story 3

- [x] T028 [US3] Implement ChatHub with JoinConversation (Groups.AddToGroupAsync), LeaveConversation (Groups.RemoveFromGroupAsync), SendMessage (validate content ≤10,000 chars, persist via IServiceScopeFactory-scoped IChatService, broadcast MessageProcessing/MessageReceived/MessageError to group), NotifyTyping (broadcast UserTyping to others in group), and connection lifecycle logging per contracts/signalr.md in src/Ato.Copilot.Chat/Hubs/ChatHub.cs
- [x] T029 [US3] Add SignalR HubConnection to ChatContext: useRef for connection instance (avoids stale closures per research.md Topic 2: Stale Closure Avoidance), withAutomaticReconnect([0,2000,5000,10000,30000]), register server event handlers (MessageProcessing → SET_PROCESSING, MessageReceived → ADD_MESSAGE + UPDATE_STATUS, MessageError → SET_ERROR + UPDATE_STATUS, UserTyping → SET_TYPING_USER), onreconnecting/onreconnected/onclose handlers, auto-JoinConversation on connect and conversation change in src/Ato.Copilot.Chat/ClientApp/src/contexts/ChatContext.tsx
- [x] T030 [US3] Implement optimistic UI updates: on send → immediately dispatch ADD_MESSAGE with client-generated GUID and status "Sending", on MessageReceived → match by ID and update to "Completed", on MessageError → update to "Error" with categorized error message, disable input during processing per FR-043 in src/Ato.Copilot.Chat/ClientApp/src/contexts/ChatContext.tsx
- [x] T031 [US3] Add connection status indicator to ChatWindow: green dot + "Real-time features active" when connected, orange dot + "Reconnecting..." during reconnection, red dot + "Real-time features unavailable" when disconnected, all with ARIA labels per FR-044 in src/Ato.Copilot.Chat/ClientApp/src/components/ChatWindow.tsx

**Checkpoint**: User Stories 1–3 complete — full real-time messaging pipeline with optimistic updates and automatic reconnection.

---

## Phase 6: User Story 4 — View Rich AI Response Metadata (Priority: P4)

**Goal**: AI responses display intent classification badges, expandable tool execution panels, multi-step progress visualization, and clickable proactive suggestion cards.

**Independent Test**: Send a message that triggers a tool execution (e.g., "Show compliance status"). Verify the intent badge shows type + confidence %, the tool panel expands to show JSON output, and suggestion cards auto-fill the input when clicked.

### Implementation for User Story 4

- [x] T032 [US4] Implement intent classification badge that renders below AI responses showing intent type and confidence percentage (e.g., "compliance_check — 92%") when metadata.intentType is present per FR-023 in src/Ato.Copilot.Chat/ClientApp/src/components/ChatWindow.tsx
- [x] T033 [US4] Implement collapsible tool execution result panel that renders on AI responses containing toolResult metadata, with clickable header "Tool Executed: {toolName}" that toggles formatted JSON output display per FR-024 in src/Ato.Copilot.Chat/ClientApp/src/components/ChatWindow.tsx
- [x] T034 [US4] Implement multi-step workflow progress panel that renders when metadata contains tool chain data, showing step completion count (e.g., "3/5 steps"), a visual progress bar, status text, and success rate percentage per FR-025 in src/Ato.Copilot.Chat/ClientApp/src/components/ChatWindow.tsx
- [x] T035 [US4] Implement proactive suggestion cards that render below AI responses when metadata.suggestedActions is present, each showing title, description, priority badge, and a clickable prompt that auto-fills the chat input per FR-026 in src/Ato.Copilot.Chat/ClientApp/src/components/ChatWindow.tsx

**Checkpoint**: User Stories 1–4 complete — users see full AI transparency with intent, tools, progress, and actionable suggestions.

---

## Phase 7: User Story 5 — Attach Files to Messages (Priority: P5)

**Goal**: Users can attach files (up to 10 MB) to messages. Files without text auto-generate analysis prompts. Attachments are stored on disk and cleaned up on conversation deletion.

**Independent Test**: Click the attach button, select a file, verify the preview badge appears. Send with text — verify the attachment is stored. Attach a .yaml file without text — verify an auto-generated prompt appears. Try a >10 MB file — verify rejection. Delete the conversation — verify files are removed from disk.

### Tests for User Story 5

- [x] T036 [P] [US5] Unit tests for ChatService file methods: positive (SaveAttachmentAsync stores file and returns MessageAttachment, GetAttachmentTypeFromContentType correctly classifies MIME types, GenerateAnalysisPrompt produces extension-specific prompt), negative (SaveAttachmentAsync with null file throws, unknown MIME type defaults to Document), boundary (exactly 10 MB file, 0-byte file, filename with special characters) in tests/Ato.Copilot.Tests.Unit/Chat/ChatServiceAttachmentTests.cs
- [x] T037 [P] [US5] Integration tests for POST /api/messages/{messageId}/attachments via WebApplicationFactory: happy path (upload valid file → 200 + MessageAttachment), error path (upload >10 MB → 400, upload with no file → 400, upload to non-existent messageId → 404) in tests/Ato.Copilot.Tests.Integration/Chat/AttachmentControllerIntegrationTests.cs

### Implementation for User Story 5

- [x] T038 [US5] Add file storage methods to ChatService: SaveAttachmentAsync (GUID-based filename in uploads/ directory, preserve original name + MIME type in metadata), GetAttachmentTypeFromContentType (detect Document/Image/Code/Configuration/Log per data-model.md AttachmentType rules), GenerateAnalysisPrompt (auto-prompt by file extension per FR-020), and cascade file deletion in DeleteConversationAsync per FR-021 in src/Ato.Copilot.Chat/Services/ChatService.cs
- [x] T039 [US5] Implement file upload endpoint POST /api/messages/{messageId}/attachments with multipart/form-data, 10 MB size validation (10,485,760 bytes), file-null check, delegate to ChatService.SaveAttachmentAsync, return MessageAttachment object per contracts/rest-api.md in src/Ato.Copilot.Chat/Controllers/ChatControllers.cs
- [x] T040 [P] [US5] Add uploadAttachment method (multipart/form-data POST) to chatApi client in src/Ato.Copilot.Chat/ClientApp/src/services/chatApi.ts
- [x] T041 [US5] Implement file attachment UI: attach button (click to open file picker), file preview badges above input area (filename + remove button), 10 MB validation with error feedback, auto-analysis prompt generation for file-only sends, and attachment rendering in message history in src/Ato.Copilot.Chat/ClientApp/src/components/ChatWindow.tsx

**Checkpoint**: User Stories 1–5 complete — full messaging with file attachment support.

---

## Phase 8: User Story 6 — Application Settings and Navigation (Priority: P6)

**Goal**: Header shows conversation title, sidebar toggles open/closed, settings modal shows version and shortcuts.

**Independent Test**: Click the menu button — sidebar toggles. Click the settings gear — modal shows version 1.0.0 and keyboard shortcuts. Press Escape — modal closes. Press Ctrl+K — sidebar toggles.

### Implementation for User Story 6

- [x] T042 [P] [US6] Implement Header component with dynamic title (conversation title when active, "Security Posture Intelligence Navigator" when none), hamburger menu button for sidebar toggle, settings gear icon, and "New Conversation" shortcut button in src/Ato.Copilot.Chat/ClientApp/src/components/Header.tsx
- [x] T043 [US6] Implement settings modal (triggered from Header gear icon) with application name "Security Posture Intelligence Navigator", version "1.0.0", feature list, keyboard shortcuts reference table (Ctrl+K, Ctrl+N, Enter, Shift+Enter, Escape), close on Escape per FR-045, and close on backdrop click in src/Ato.Copilot.Chat/ClientApp/src/components/Header.tsx
- [x] T044 [US6] Implement global keyboard shortcut handler: Ctrl+K toggles sidebar, Ctrl+N creates new conversation, Escape dismisses active modal, integrate with sidebar state and conversation actions in src/Ato.Copilot.Chat/ClientApp/src/App.tsx

**Checkpoint**: All 6 user stories complete — full-featured chat application with navigation and settings.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, containerization, styling, observability, and final quality validation

- [x] T045 [P] Add custom CSS animations (typing indicator pulse, message fade-in, sidebar slide transition) and scrollbar styles (thin scrollbar, custom thumb) in src/Ato.Copilot.Chat/ClientApp/src/styles/App.css
- [x] T046 [P] Create multi-stage Dockerfile: Stage 1 Node.js 20.18.1 (npm ci + npm run build), Stage 2 .NET 9.0 SDK (dotnet publish), Stage 3 aspnet:9.0 runtime (copy builds, non-root user per FR-040, expose port, health check) in src/Ato.Copilot.Chat/Dockerfile
- [x] T047 [P] Create feature documentation covering architecture overview, setup instructions, API reference, SignalR events, configuration options, and troubleshooting per Constitution Principle I in docs/chat-app.md
- [x] T048 [P] Add Serilog.Sinks.ApplicationInsights NuGet package to Ato.Copilot.Chat.csproj, configure Application Insights instrumentation key in appsettings.json (empty for dev), and add conditional WriteTo.ApplicationInsights() sink in Program.cs Serilog configuration per Constitution Principle V in src/Ato.Copilot.Chat/Ato.Copilot.Chat.csproj and src/Ato.Copilot.Chat/Program.cs
- [x] T049 Run quickstart.md end-to-end validation: build backend (dotnet build), build frontend (npm run build), verify database auto-creation, send a test message, confirm AI response renders. Additionally, manually verify multi-tab scenario: open two browser tabs to the same conversation, send a message in tab 1, confirm it appears in tab 2 via SignalR per spec.md edge case
- [x] T050 Final code cleanup: add XML documentation comments on all public types and members per Constitution Principle VI, verify dotnet build produces zero warnings, validate Ato.Copilot.* naming conventions, run full test suite (dotnet test) and confirm all unit and integration tests pass with zero failures

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — **BLOCKS all user stories**
- **User Stories (Phase 3–8)**: All depend on Foundational phase completion
  - User Story 1 (P1): No dependencies on other stories — **start here for MVP**. Tests (T009–T011) can run in parallel with each other.
  - User Story 2 (P2): Builds on ChatService from US1 (T021 adds methods to existing service). Tests (T019–T020) can run in parallel with each other.
  - User Story 3 (P3): Builds on ChatContext from US1 (T029 adds SignalR to existing context). Test (T027) can run in parallel with US2 tests.
  - User Story 4 (P4): Depends on ChatWindow from US1 (T032–T035 add metadata rendering)
  - User Story 5 (P5): Depends on ChatService from US1 and Controllers from US1 (adds upload endpoint). Tests (T036–T037) can run in parallel with each other.
  - User Story 6 (P6): Can start after US1 + US2 (header needs conversation state)
- **Polish (Phase 9)**: Depends on all desired user stories being complete

### Within Each User Story

- Tests are written first (test-first per Constitution Principle III) → Backend service methods → Controller endpoints → API client → Context/state → UI components
- Each story builds incrementally on shared files (ChatService.cs, ChatControllers.cs, ChatContext.tsx, ChatWindow.tsx)

### Parallel Opportunities

- **Phase 1**: T002 and T003 can run in parallel (frontend scaffolding is independent)
- **Phase 2**: T007 and T008 can run in parallel (config files and TypeScript types are independent)
- **Phase 3**: T009–T011 (test tasks) can run in parallel with each other. T015 can run in parallel with T012–T014 (frontend API client vs backend implementation)
- **Phase 4**: T019–T020 (test tasks) can run in parallel with each other
- **Phase 5**: T027 (test task) can run in parallel with US2 tests
- **Phase 7**: T036–T037 (test tasks) can run in parallel. T040 can run in parallel with T038–T039 (frontend upload method vs backend upload endpoint)
- **Phase 8**: T042 can run in parallel with other stories (Header.tsx is a new file)
- **Phase 9**: T045, T046, T047, T048 can all run in parallel (CSS, Dockerfile, docs, App Insights are independent files)
- **Cross-story**: US6 (Header.tsx) can be built in parallel with US3–US5 since it's a separate file

---

## Parallel Example: Phase 2 (Foundational)

```
# Sequential (file dependencies):
T004 → T005 → T006  (Models → DbContext → Program.cs)

# Parallel with above:
T007  (appsettings.json — independent config file)
T008  (types/chat.ts — independent TypeScript file)
```

## Parallel Example: User Story 1

```
# Parallel test tasks (write first per Constitution Principle III):
T009 | T010 | T011  (ChatServiceTests | MessagesControllerTests | Integration tests)

# Sequential backend chain:
T012 → T013 → T014  (Interface → Service → Controller)

# Parallel frontend (can start once types exist from T008):
T015  (chatApi.ts — only needs types)

# Sequential frontend chain (after T015 ready):
T016 → T017 → T018  (Context → ChatWindow → App.tsx)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (3 tasks)
2. Complete Phase 2: Foundational (5 tasks)
3. Complete Phase 3: User Story 1 — Send & Receive Messages (10 tasks, including 3 test tasks)
4. **STOP and VALIDATE**: Launch app, type a message, verify AI response renders with Markdown. Run `dotnet test` — all US1 tests pass.
5. Deploy/demo if ready — this delivers the core conversational capability

### Incremental Delivery

1. Setup + Foundational → Foundation ready (8 tasks)
2. Add User Story 1 → MVP: Send & receive messages (18 tasks cumulative)
3. Add User Story 2 → Conversation management with sidebar (26 tasks cumulative)
4. Add User Story 3 → Real-time messaging with SignalR (31 tasks cumulative)
5. Add User Story 4 → Rich AI metadata display (35 tasks cumulative)
6. Add User Story 5 → File attachments (41 tasks cumulative)
7. Add User Story 6 → Settings and navigation polish (44 tasks cumulative)
8. Polish → Documentation, Docker, observability, cleanup (50 tasks total)
9. Each story adds value without breaking previous stories

---

## Notes

- Test tasks are included per Constitution Principle III (non-negotiable testing standards) — each user story includes unit tests (positive, negative, boundary) and integration tests (happy path, error path)
- [P] tasks = different files, no dependencies on incomplete tasks in the same phase
- [US*] label maps each task to a specific user story for traceability
- Stories are ordered P1→P6 but US6 (Header.tsx) can be built in parallel since it's a separate file
- ChatWindow.tsx accumulates changes across US1, US3, US4, US5 — tasks within this file are sequential
- ChatService.cs accumulates changes across US1, US2, US5 — tasks within this file are sequential
- ChatContext.tsx accumulates changes across US1, US2, US3 — tasks within this file are sequential
- Test files are independent per user story and can be written in parallel across stories
- Commit after each task or logical group for clean history
