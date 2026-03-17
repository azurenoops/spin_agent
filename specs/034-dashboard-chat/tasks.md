# Tasks: Dashboard Chat Side Panel

**Input**: Design documents from `/specs/034-dashboard-chat/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Unit and component tests included per Constitution Principle III (Testing Standards — NON-NEGOTIABLE).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Dashboard frontend**: `src/Ato.Copilot.Dashboard/src/`
- **Tests**: `src/Ato.Copilot.Dashboard/src/__tests__/` (co-located with Vitest)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Install new dependencies and create project scaffolding for the chat feature

- [x] T001 Install react-markdown, remark-gfm, react-syntax-highlighter, and @types/react-syntax-highlighter in `src/Ato.Copilot.Dashboard/package.json`
- [x] T002 Create TypeScript type definitions for all chat entities in `src/Ato.Copilot.Dashboard/src/types/chat.ts` — Conversation, Message, MessageStatus, ToolExecution, ErrorDetail, SuggestedAction, ChatContext, FileAttachment, FileAttachmentType, ChatPanelState, SseProgressEvent, SseResultEvent, SseErrorEvent, ChatRequest, ConversationHistoryEntry per data-model.md and contracts/
- [x] T003 [P] Create chat component directory structure: `src/Ato.Copilot.Dashboard/src/components/chat/`, `src/Ato.Copilot.Dashboard/src/hooks/`, `src/Ato.Copilot.Dashboard/src/services/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core services and hooks that ALL user stories depend on — MUST complete before any story work

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T004 Implement SSE parser function `parseSseChunk()` and `SseEvent` interface in `src/Ato.Copilot.Dashboard/src/services/chatService.ts` — port from `extensions/vscode/src/services/sseClient.ts` with native fetch + ReadableStream, line-based SSE parsing for progress/result/error events
- [x] T005 Implement `sendMessage()` function in `src/Ato.Copilot.Dashboard/src/services/chatService.ts` — POST to `/api/mcp/chat/stream` with ChatRequest body, Accept: text/event-stream header, Bearer token from localStorage, AbortSignal support, stream reading via ReadableStream, dispatching parsed events to onProgress/onResult/onError callbacks, 120s timeout
- [x] T006 [P] Implement `useLocalStorage` hook in `src/Ato.Copilot.Dashboard/src/hooks/useLocalStorage.ts` — typed get/set for localStorage keys (`ato-chat-conversations`, `ato-chat-panel-state`), JSON serialize/deserialize with error recovery, debounced writes
- [x] T007 Implement `useSseStream` hook in `src/Ato.Copilot.Dashboard/src/hooks/useSseStream.ts` — wraps chatService.sendMessage with React state (isStreaming, progressSteps), AbortController lifecycle, cancel() method, stream() method per contracts/chat-service-interface.md
- [x] T008 Implement `useChat` hook in `src/Ato.Copilot.Dashboard/src/hooks/useChat.ts` — full conversation state management: conversations array from useLocalStorage, activeConversation, sendMessage (creates user Message, calls useSseStream, builds assistant Message from result), newConversation, selectConversation, deleteConversation, cancelStream, LRU eviction at 50 conversations, 20-message history trimming for ConversationHistory per data-model.md
- [x] T009 [P] Implement `useChatContext` hook in `src/Ato.Copilot.Dashboard/src/hooks/useChatContext.ts` — extracts ChatContext from current react-router-dom location and route params (page name from pathname, systemId/boundaryId/entityType/entityId from URL params)

### Tests for Foundational Phase

- [x] T040 [P] Unit tests for `parseSseChunk()` and SSE stream reading in `src/Ato.Copilot.Dashboard/src/__tests__/services/chatService.test.ts` — valid progress/result/error events, multi-event chunks, partial chunks, malformed data, empty data, keepalive comments
- [x] T041 [P] Unit tests for `useLocalStorage` hook in `src/Ato.Copilot.Dashboard/src/__tests__/hooks/useLocalStorage.test.ts` — get/set/remove, JSON parse errors, missing keys, debounced writes
- [x] T042 [P] Unit tests for `useSseStream` hook in `src/Ato.Copilot.Dashboard/src/__tests__/hooks/useSseStream.test.ts` — stream start/cancel, AbortController lifecycle, progress accumulation, error callback, isStreaming transitions
- [x] T043 [P] Unit tests for `useChat` hook in `src/Ato.Copilot.Dashboard/src/__tests__/hooks/useChat.test.ts` — sendMessage flow, newConversation, selectConversation, deleteConversation, LRU eviction at 50, 20-message history trim, title auto-generation
- [x] T044 [P] Unit tests for `useChatContext` hook in `src/Ato.Copilot.Dashboard/src/__tests__/hooks/useChatContext.test.ts` — context extraction from portfolio, system detail, components, capabilities routes; null fields when no entity selected

**Checkpoint**: Foundation ready — all services, hooks, and their tests are in place. User story implementation can now begin.

---

## Phase 3: User Story 1 — Send a Message and Receive a Streaming AI Response (Priority: P1) 🎯 MVP

**Goal**: User can open a chat panel, type a message, see streaming progress, and read a Markdown-formatted AI response

**Independent Test**: Open dashboard → click chat icon → type a question → verify streaming response with Markdown formatting appears

### Implementation for User Story 1

- [x] T010 [P] [US1] Create `MarkdownRenderer` component in `src/Ato.Copilot.Dashboard/src/components/chat/MarkdownRenderer.tsx` — ReactMarkdown with remarkGfm plugin, Prism SyntaxHighlighter with oneDark theme for code blocks, table styling with Tailwind
- [x] T011 [US1] Create `ChatBubble` component in `src/Ato.Copilot.Dashboard/src/components/chat/ChatBubble.tsx` — renders user bubble (right-aligned, blue) and assistant bubble (left-aligned, white) with timestamp, uses MarkdownRenderer for assistant content, shows MessageStatus indicator (sending spinner, error icon)
- [x] T012 [P] [US1] Create `ProgressSteps` component in `src/Ato.Copilot.Dashboard/src/components/chat/ProgressSteps.tsx` — renders SseProgressEvent[] as animated step indicators (agent routing, tool execution) during streaming
- [x] T013 [P] [US1] Create `ErrorMessage` component in `src/Ato.Copilot.Dashboard/src/components/chat/ErrorMessage.tsx` — displays ErrorDetail with retry button, handles network errors and empty responses
- [x] T014 [US1] Create `ChatMessages` component in `src/Ato.Copilot.Dashboard/src/components/chat/ChatMessages.tsx` — scrollable message list using ChatBubble, auto-scroll to bottom on new messages via useRef + scrollIntoView, shows ProgressSteps during active streaming, shows "No response generated" for empty results
- [x] T015 [P] [US1] Create `ChatInput` component in `src/Ato.Copilot.Dashboard/src/components/chat/ChatInput.tsx` — textarea with Enter to send (Shift+Enter for newline), Send button, disabled state during processing, cancel button during streaming, auto-resize textarea
- [x] T016 [P] [US1] Create `ChatHeader` component in `src/Ato.Copilot.Dashboard/src/components/chat/ChatHeader.tsx` — panel title ("ATO Copilot"), close button (X), new conversation button (+), conversation count badge
- [x] T017 [US1] Create `ChatPanel` component in `src/Ato.Copilot.Dashboard/src/components/chat/ChatPanel.tsx` — root panel container: fixed position (right:0, top:56px, bottom:0, z-index:40), slide-in/out animation, renders ChatHeader + ChatMessages + ChatInput, wires useChat hook, width from ChatPanelState (default 420px), passes isOpen/onClose props
- [x] T018 [US1] Create `ChatToggle` component in `src/Ato.Copilot.Dashboard/src/components/chat/ChatToggle.tsx` — chat icon button for the dashboard header, shows active indicator when panel is open, tooltip "Chat (Ctrl+Shift+C)"
- [x] T019 [US1] Integrate ChatPanel and ChatToggle into `src/Ato.Copilot.Dashboard/src/App.tsx` — render ChatPanel as fixed overlay sibling to Routes, render ChatToggle in a shared state context, manage panel open/closed state with useLocalStorage, pass ChatPanelState to ChatPanel
- [x] T020 [US1] Add ChatToggle button to PageLayout header in `src/Ato.Copilot.Dashboard/src/components/layout/PageLayout.tsx` — insert chat icon button between Help and Settings buttons in the header bar, wire to shared panel toggle state

### Tests for User Story 1

- [x] T045 [P] [US1] Component tests for leaf components in `src/Ato.Copilot.Dashboard/src/__tests__/components/chat/` — MarkdownRenderer (headings, code blocks, tables, GFM), ChatBubble (user vs assistant styling, status indicators), ChatInput (Enter sends, Shift+Enter newline, disabled state), ChatHeader (close/new conversation buttons), ProgressSteps (step list rendering), ErrorMessage (error display + retry button)
- [x] T046 [US1] Component tests for container components in `src/Ato.Copilot.Dashboard/src/__tests__/components/chat/` — ChatMessages (auto-scroll, empty state, progress during streaming), ChatPanel (open/close animation, width prop, child rendering), ChatToggle (active indicator, click handler)

**Checkpoint**: User Story 1 complete — users can open the chat panel, send messages, see streaming progress, and read Markdown-formatted responses. Core component tests pass.

---

## Phase 4: User Story 2 — Context-Aware Chat from Dashboard Pages (Priority: P2)

**Goal**: Chat requests automatically include current dashboard context (page, systemId, entityType) so AI responses are scoped to what the user is viewing

**Independent Test**: Navigate to System Detail page → open chat → ask "Show compliance status" → verify response references the specific system

### Implementation for User Story 2

- [x] T021 [US2] Wire useChatContext into useChat hook in `src/Ato.Copilot.Dashboard/src/hooks/useChat.ts` — call useChatContext() to get current ChatContext, serialize as JSON string in ChatRequest.Context field on every sendMessage call, update context when route changes mid-conversation
- [x] T022 [US2] Update ChatHeader to display current context in `src/Ato.Copilot.Dashboard/src/components/chat/ChatHeader.tsx` — show subtle context badge (e.g., "Viewing: ACME System" or "Portfolio") below the title so user knows what context the chat sees

**Checkpoint**: User Story 2 complete — chat requests include dashboard context and AI responses are scoped to the current page/system.

---

## Phase 5: User Story 3 — Conversation Management (Priority: P3)

**Goal**: Users can create, switch between, and delete multiple conversations with persistent history

**Independent Test**: Start conversation → send messages → start new conversation → switch back → verify history preserved

### Implementation for User Story 3

- [x] T023 [P] [US3] Create `ConversationList` component in `src/Ato.Copilot.Dashboard/src/components/chat/ConversationList.tsx` — dropdown/collapsible list of conversations showing title (auto-generated from first message, max 50 chars), relative timestamp (Today/Yesterday/date), active conversation highlighted, delete button per conversation with confirmation
- [x] T024 [P] [US3] Create `WelcomeMessage` component in `src/Ato.Copilot.Dashboard/src/components/chat/WelcomeMessage.tsx` — displayed when panel opens with no active conversation or empty conversation, shows ATO Copilot branding, capability summary, 4 example question cards relevant to current page context (use ChatContext), clicking a card sends it as a message
- [x] T025 [US3] Integrate ConversationList into ChatPanel in `src/Ato.Copilot.Dashboard/src/components/chat/ChatPanel.tsx` — add conversation selector below ChatHeader, wire onSelect/onDelete/onNew to useChat methods, show WelcomeMessage when no messages in active conversation

### Tests for User Story 3

- [x] T047 [P] [US3] Component tests in `src/Ato.Copilot.Dashboard/src/__tests__/components/chat/` — ConversationList (renders titles, relative timestamps, delete confirmation, active highlight), WelcomeMessage (example cards render, click sends message)

**Checkpoint**: User Story 3 complete — users can manage multiple conversations with full CRUD and persistent local storage. Component tests pass.

---

## Phase 6: User Story 4 — Rich Response Rendering with Tool Evidence (Priority: P4)

**Goal**: Responses show tool execution details, suggestion cards for follow-up actions, and clickable entity links

**Independent Test**: Ask a question triggering tool execution → verify tool evidence panel appears → click suggestion card → verify input auto-fills

### Implementation for User Story 4

- [x] T026 [P] [US4] Create `ToolEvidence` component in `src/Ato.Copilot.Dashboard/src/components/chat/ToolEvidence.tsx` — collapsible section showing each ToolExecution: tool name, success/failure icon, execution time in ms, expandable result text; collapsed by default, toggle on click
- [x] T027 [P] [US4] Create `SuggestionCards` component in `src/Ato.Copilot.Dashboard/src/components/chat/SuggestionCards.tsx` — horizontal scrollable row of clickable chips rendered from SuggestedAction[], each showing icon + label, onClick calls onSelect(prompt) to auto-fill chat input, disabled during processing
- [x] T028 [US4] Integrate ToolEvidence and SuggestionCards into ChatBubble in `src/Ato.Copilot.Dashboard/src/components/chat/ChatBubble.tsx` — for assistant messages: render ToolEvidence below content when toolsExecuted is non-empty, render SuggestionCards below ToolEvidence when suggestedActions is non-empty, show agent name and intent type as subtle badge (e.g., "ComplianceAgent · query")
- [x] T029 [US4] Add entity link rendering to MarkdownRenderer in `src/Ato.Copilot.Dashboard/src/components/chat/MarkdownRenderer.tsx` — custom link renderer for react-markdown that intercepts relative URLs matching dashboard routes (e.g., `/systems/:id`, `/capabilities/:id`); regex-based detection for NIST control IDs (pattern: `[A-Z]{2}-\d+`) rendered as `<Link>` components navigating within the dashboard; no heuristic entity-name matching (server must format entity refs as Markdown links)

### Tests for User Story 4

- [x] T048 [P] [US4] Component tests in `src/Ato.Copilot.Dashboard/src/__tests__/components/chat/` — ToolEvidence (collapse/expand toggle, success/failure icons, execution time), SuggestionCards (chip rendering, onClick fires with prompt, disabled state)

**Checkpoint**: User Story 4 complete — responses include tool evidence, suggestion cards, and navigable entity links. Component tests pass.

---

## Phase 7: User Story 5 — Panel Toggle, Resize, and Keyboard Shortcuts (Priority: P5)

**Goal**: Panel supports keyboard shortcuts, drag-to-resize, focus management, and responsive layout

**Independent Test**: Press Ctrl+Shift+C → panel opens with focus in input → resize by dragging → navigate to another page → panel state persists

### Implementation for User Story 5

- [x] T030 [US5] Add keyboard shortcut handler to ChatPanel in `src/Ato.Copilot.Dashboard/src/components/chat/ChatPanel.tsx` — global keydown listener for Ctrl+Shift+C (Cmd+Shift+C on macOS) to toggle panel, Escape to close panel when focused
- [x] T031 [US5] Add drag-to-resize to ChatPanel in `src/Ato.Copilot.Dashboard/src/components/chat/ChatPanel.tsx` — left-edge drag handle (4px wide), mouse/touch events for resize, clamp width between 320px and 600px, persist width to localStorage via useLocalStorage on drag end
- [x] T032 [US5] Add focus management to ChatPanel in `src/Ato.Copilot.Dashboard/src/components/chat/ChatPanel.tsx` — on open: store previously focused element ref, focus ChatInput textarea after slide-in animation (requestAnimationFrame); on close: restore focus to stored element ref; trap focus within panel while open using focusin event listener
- [x] T033 [US5] Add responsive full-width overlay mode in `src/Ato.Copilot.Dashboard/src/components/chat/ChatPanel.tsx` — media query or ResizeObserver: below 768px viewport width, set panel to width: 100vw with close button, above 768px revert to configured width per FR-020

**Checkpoint**: User Story 5 complete — panel supports keyboard toggle, resize, focus trapping, and responsive layout.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Cross-cutting improvements affecting multiple user stories

- [x] T034 [P] Add MCP base URL environment variable support in `src/Ato.Copilot.Dashboard/src/services/chatService.ts` — read `VITE_MCP_BASE_URL` from import.meta.env (default `/api`), use for SSE endpoint URL construction
- [x] T035 [P] Handle in-flight stream cancellation edge cases in `src/Ato.Copilot.Dashboard/src/hooks/useChat.ts` — when user sends new message while streaming: abort current stream, mark partial response with "Cancelled" status, then send new message per edge case spec
- [x] T036 [P] Implement localStorage capacity error handling in `src/Ato.Copilot.Dashboard/src/hooks/useLocalStorage.ts` — catch QuotaExceededError on write, show warning toast, fall back to session-only storage per edge case spec
- [x] T037 Handle file attachment for import workflows in `src/Ato.Copilot.Dashboard/src/components/chat/FileAttachment.tsx` — drag-and-drop zone + file picker button, validate extension (.ckl/.xccdf/.xml/.csv/.nessus) and size (10MB max), read file content via FileReader, show attachment chips with remove button, pass file data as ActionContext in ChatRequest per FR-019 and research.md R-007
- [x] T038 Integrate FileAttachment into ChatInput in `src/Ato.Copilot.Dashboard/src/components/chat/ChatInput.tsx` — add paperclip/attach button, render FileAttachment component, pass attachments to onSend callback, show inline error for unsupported types or oversized files
- [x] T039 Run quickstart.md validation — verify end-to-end setup: `npm install`, `npm run dev`, open dashboard, toggle chat panel, send a message, confirm streaming response renders with Markdown (requires running backend)
- [x] T049 [P] Performance validation for SC-001 and SC-002 — measure first-token latency with `performance.now()` timestamps in chatService, measure panel open time with requestAnimationFrame timing, verify <5s first token and <300ms panel open thresholds (requires running backend)
- [x] T050 [P] Cross-browser manual verification for SC-005 — test full chat workflow in Chrome, Edge, and Firefox (latest 2 versions): panel toggle, message send/receive, Markdown rendering, resize, keyboard shortcuts; document results (manual testing task)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (types must exist) — **BLOCKS all user stories**; includes tests T040–T044
- **User Stories (Phases 3–7)**: All depend on Phase 2 completion (including Phase 2 tests)
  - US1 (Phase 3): No dependencies on other stories — **this is the MVP**; includes tests T045–T046
  - US2 (Phase 4): Depends on US1 (needs ChatPanel and useChat to exist)
  - US3 (Phase 5): Depends on US1 (needs ChatPanel to integrate ConversationList into); includes tests T047
  - US4 (Phase 6): Depends on US1 (needs ChatBubble and MarkdownRenderer to extend); includes tests T048
  - US5 (Phase 7): Depends on US1 (needs ChatPanel to add resize/shortcuts to)
- **Polish (Phase 8)**: Can start after US1; some tasks (T037–T038) can start after Phase 2; T049–T050 are validation tasks

### Within Each User Story

- Components listed as [P] can be built in parallel
- Components with layout integration depend on their child components being complete
- Integration tasks (wiring into App.tsx/PageLayout) come last within the story

### Parallel Opportunities

- T002 and T003 can run in parallel (Setup phase)
- T006 and T009 can run in parallel with T004–T005 (Foundational phase — different files)
- T040, T041, T042, T043, T044 can all run in parallel (Foundational tests — different test files)
- T010, T012, T013, T015, T016 can all run in parallel (US1 leaf components — T011 depends on T010)
- T045 and T046 can run after US1 implementation tasks complete (US1 tests)
- T023 and T024 can run in parallel (US3 — independent components)
- T026 and T027 can run in parallel (US4 — independent components)
- T034, T035, T036, T049, T050 can run in parallel (Polish — different files/activities)

---

## Parallel Example: User Story 1

```text
# Phase 3 parallelization:

# Batch 1 — All leaf components (no internal dependencies):
T010: MarkdownRenderer.tsx
T012: ProgressSteps.tsx
T013: ErrorMessage.tsx
T015: ChatInput.tsx
T016: ChatHeader.tsx

# Batch 2 — Components with dependencies (depend on Batch 1):
T011: ChatBubble.tsx        (depends on T010 for MarkdownRenderer)
T014: ChatMessages.tsx      (uses ChatBubble, ProgressSteps, ErrorMessage)
T018: ChatToggle.tsx

# Batch 3 — Root panel (depends on Batch 2):
T017: ChatPanel.tsx         (uses ChatHeader, ChatMessages, ChatInput)

# Batch 4 — App integration (depends on Batch 3):
T019: App.tsx integration
T020: PageLayout.tsx integration
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T003)
2. Complete Phase 2: Foundational (T004–T009) — **CRITICAL, blocks all stories**
3. Complete Phase 3: User Story 1 (T010–T020)
4. **STOP and VALIDATE**: Open dashboard, click chat icon, send message, verify streaming Markdown response
5. Deploy/demo if ready — core chat is functional

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. User Story 1 → Streaming chat works → **MVP!**
3. User Story 2 → Context-aware responses
4. User Story 3 → Conversation management
5. User Story 4 → Rich tool evidence + suggestions
6. User Story 5 → Keyboard shortcuts + resize + accessibility
7. Polish → File attachments, edge case handling, quickstart validation

### Single Developer Sequence

T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009 → T040–T044 →
T010 → T011 → T012 → T013 → T014 → T015 → T016 → T017 → T018 → T019 → T020 → T045–T046 →
T021 → T022 → T023 → T024 → T025 → T047 → T026 → T027 → T028 → T029 → T048 →
T030 → T031 → T032 → T033 → T034 → T035 → T036 → T037 → T038 → T039 → T049 → T050

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- No backend changes required — all work is in `src/Ato.Copilot.Dashboard/`
- Test tasks T040–T050 cover unit tests, component tests, performance, and cross-browser validation per Constitution Principle III
- SSE client ported from VS Code extension pattern, not a new library
- Auth uses existing localStorage Bearer token pattern; MSAL deferred to separate feature
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
