# Tasks: Copilot Everywhere — Multi-Channel Extensions & Channels Library

**Input**: Design documents from `/specs/013-copilot-everywhere/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are included — plan.md and spec.md define 4 .NET test files and 3 TypeScript test suites with 90%+ coverage required for channel abstractions (SC-010).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. Three-component architecture: .NET Channels library, VS Code extension, M365 extension.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Clean scaffold stubs, configure NuGet dependencies, initialize TypeScript extension projects

- [x] T001 Delete all placeholder scaffold stubs: `src/Ato.Copilot.Channels/GitHub/` (PlatformParticipant.cs, InlineComplianceChecker.cs, README.md, manifest.json), `src/Ato.Copilot.Channels/M365/` (PlatformBot.cs, AdaptiveCards/, README.md, manifest.json), and `src/Ato.Copilot.Channels/placeholder.md`
- [x] T001a Add `Ato.Copilot.Channels` project to `Ato.Copilot.sln` — the project exists on disk but is not in the solution file
- [x] T002 Update `src/Ato.Copilot.Channels/Ato.Copilot.Channels.csproj` with NuGet dependencies per R9: `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Hosting.Abstractions`, `System.Text.Json`, `System.Collections.Immutable`
- [x] T003 [P] Initialize VS Code extension project with `package.json` (engine `^1.90.0`, activation `onChatParticipant:ato`, settings schema per FR-032) and `tsconfig.json` in `extensions/vscode/`
- [x] T004 [P] Initialize M365 extension project with `package.json` (express, adaptivecards, axios dependencies) and `tsconfig.json` in `extensions/m365/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Models, enums, configuration, and interfaces shared across ALL implementations

**⚠️ CRITICAL**: No user story implementation can begin until this phase is complete

- [x] T005 [P] Create ChannelType, MessageType, StreamContentType, DefaultHandlerBehavior enums per data-model.md in `src/Ato.Copilot.Channels/Models/ChannelEnums.cs`
- [x] T006 [P] Create ChannelMessage model with MessageId, ConversationId, Type, Content, AgentType, Timestamp, Metadata, IsStreaming, IsComplete, SequenceNumber per data-model.md in `src/Ato.Copilot.Channels/Models/ChannelMessage.cs`
- [x] T007 [P] Create ConnectionInfo model with ConnectionId, UserId, ConnectedAt, LastActivityAt, Conversations, Metadata, IsActive per data-model.md in `src/Ato.Copilot.Channels/Models/ConnectionInfo.cs`
- [x] T008 [P] Create IncomingMessage model with ConnectionId, ConversationId, Content, TargetAgentType, Attachments, Metadata per data-model.md in `src/Ato.Copilot.Channels/Models/IncomingMessage.cs`
- [x] T009 [P] Create StreamChunk model with SequenceNumber, Content, ContentType per data-model.md in `src/Ato.Copilot.Channels/Models/StreamChunk.cs`
- [x] T010 [P] Create MessageAttachment model with Name, ContentType, Url, Data, Size per data-model.md in `src/Ato.Copilot.Channels/Models/MessageAttachment.cs`
- [x] T011 [P] Create ChannelOptions with StreamingOptions nested class and DefaultHandlerBehavior config (SectionName="Channels") per data-model.md in `src/Ato.Copilot.Channels/Configuration/ChannelOptions.cs`
- [x] T012 [P] Create IChannel interface with SendAsync, SendToConversationAsync, BroadcastAsync, IsConnectedAsync per contracts/channel-interfaces.md in `src/Ato.Copilot.Channels/Abstractions/IChannel.cs`
- [x] T013 [P] Create IChannelManager interface with RegisterConnectionAsync, UnregisterConnectionAsync, JoinConversationAsync, LeaveConversationAsync, SendToConversationAsync, IsConnectedAsync, GetConnectionInfoAsync, GetAllConnections per contracts/channel-interfaces.md in `src/Ato.Copilot.Channels/Abstractions/IChannelManager.cs`
- [x] T014 [P] Create IMessageHandler interface with HandleMessageAsync per contracts/channel-interfaces.md in `src/Ato.Copilot.Channels/Abstractions/IMessageHandler.cs`
- [x] T015 [P] Create IStreamingHandler and IStreamContext interfaces with BeginStreamAsync, WriteAsync, CompleteAsync, AbortAsync, IAsyncDisposable per contracts/channel-interfaces.md in `src/Ato.Copilot.Channels/Abstractions/IStreamingHandler.cs`

**Checkpoint**: All models, enums, and interfaces compile. `dotnet build src/Ato.Copilot.Channels/` succeeds.

---

## Phase 3: User Story 1 — Channel Abstraction Layer (Priority: P1) 🎯 MVP

**Goal**: Provide InMemoryChannel, ChannelManager, StreamContext, DefaultMessageHandler implementations with full connection lifecycle, conversation routing, streaming, and idle cleanup.

**Independent Test**: Instantiate InMemoryChannel, register connections, join conversations, send messages, verify delivery. Start a stream, write chunks, verify sequence numbers and completion. All without external dependencies.

### Implementation for User Story 1

- [x] T016 [P] [US1] Implement InMemoryChannel with ConcurrentDictionary connections + ImmutableHashSet conversation groups, pre-filter + try-catch inactive sends, warning log for dead connections (R1, R5, FR-017, FR-018) in `src/Ato.Copilot.Channels/Implementations/InMemoryChannel.cs`
- [x] T017 [P] [US1] Implement ChannelManager with connection registration (Guid ID), conversation group management, UnregisterConnectionAsync removing from all groups, empty group cleanup, LastActivityAt tracking (FR-005–FR-009) in `src/Ato.Copilot.Channels/Implementations/ChannelManager.cs`
- [x] T018 [P] [US1] Implement StreamContext (IStreamContext) with Interlocked.Increment sequence numbers, StringBuilder buffer, CompareExchange completion guard, IAsyncDisposable auto-complete safety net in `src/Ato.Copilot.Channels/Implementations/StreamContext.cs`; implement StreamingHandler (IStreamingHandler) factory with BeginStreamAsync in `src/Ato.Copilot.Channels/Implementations/StreamingHandler.cs` (R3, R4, FR-013–FR-016)
- [x] T019 [P] [US1] Implement DefaultMessageHandler with optional AgentInvoker delegate, Echo/Error behavior per ChannelOptions.DefaultHandlerBehavior, IConversationStateManager integration for message persistence, AgentThinking notification (FR-010–FR-012) in `src/Ato.Copilot.Channels/Implementations/DefaultMessageHandler.cs`
- [x] T020 [P] [US1] Implement IdleConnectionCleanupService as BackgroundService with PeriodicTimer at IdleCleanupInterval, scanning GetAllConnections for idle > IdleConnectionTimeout (R2, FR-017a) in `src/Ato.Copilot.Channels/Implementations/IdleConnectionCleanupService.cs`
- [x] T021 [US1] Create AddChannels(IConfiguration) and AddInMemoryChannels() DI extension methods registering IChannel, IChannelManager, IStreamingHandler, IMessageHandler (scoped), IdleConnectionCleanupService (hosted) (FR-020) in `src/Ato.Copilot.Channels/Extensions/ChannelServiceExtensions.cs`

### Tests for User Story 1

- [x] T022 [P] [US1] Create InMemoryChannelTests — send to connection updates LastActivityAt, send to conversation delivers to all members, broadcast reaches all connections, skip inactive connection logs warning without exception, IsConnectedAsync returns false for unknown ID in `tests/Ato.Copilot.Tests.Unit/Channels/InMemoryChannelTests.cs`
- [x] T023 [P] [US1] Create ChannelManagerTests — RegisterConnectionAsync returns ConnectionInfo with generated ID, UnregisterConnectionAsync removes from all groups and sets IsActive=false, JoinConversationAsync adds to group, LeaveConversationAsync removes and cleans empty group, GetAllConnections enumerates all in `tests/Ato.Copilot.Tests.Unit/Channels/ChannelManagerTests.cs`
- [x] T024 [P] [US1] Create StreamContextTests — WriteAsync increments sequence numbers, CompleteAsync sends aggregated content with IsComplete=true, AbortAsync sends Error type message, DisposeAsync auto-completes if not finalized (FR-016), double CompleteAsync is idempotent in `tests/Ato.Copilot.Tests.Unit/Channels/StreamContextTests.cs`
- [x] T025 [P] [US1] Create DefaultMessageHandlerTests — with AgentInvoker stores message and returns response, Echo mode returns user content as AgentResponse, Error mode returns Error type message, exception always returns Error regardless of setting, validates IConversationStateManager.SaveConversationAsync called in `tests/Ato.Copilot.Tests.Unit/Channels/DefaultMessageHandlerTests.cs`
- [x] T025a [P] [US1] Create IdleConnectionCleanupServiceTests — cleanup removes connections idle longer than IdleConnectionTimeout, active connections are not removed, cleanup runs on configured PeriodicTimer interval, service stops cleanly on cancellation in `tests/Ato.Copilot.Tests.Unit/Channels/IdleConnectionCleanupServiceTests.cs`

**Checkpoint**: Channel Abstraction Layer fully functional. Run `dotnet test tests/Ato.Copilot.Tests.Unit --filter "FullyQualifiedName~Channels"` — all tests pass. This is the MVP.

---

## Phase 4: User Story 2 — GitHub Copilot Chat Extension (Priority: P2)

**Goal**: Register `@ato` as a GitHub Copilot Chat participant in VS Code with `/compliance`, `/knowledge`, `/config` slash commands, forwarding requests to the MCP Server and rendering streamed Markdown responses.

**Independent Test**: Install extension in VS Code, type `@ato How do I comply with AC-2?` in Copilot Chat, verify streamed Markdown response with agent attribution.

### Implementation for User Story 2

- [x] T026 [P] [US2] Create MCP HTTP client service with configurable apiUrl/apiKey/timeout, POST /mcp/chat request, GET /health check, error code mapping (ECONNREFUSED, ETIMEDOUT, HTTP errors) per contracts/vscode-extension.md in `extensions/vscode/src/services/mcpClient.ts`
- [x] T027 [P] [US2] Create health check command — GET /health with info/warning message and "Configure Connection" action button (FR-029, FR-034) in `extensions/vscode/src/commands/health.ts`
- [x] T028 [P] [US2] Create configure command — opens VS Code settings filtered to `@ext:ato-copilot.ato-copilot-vscode` (FR-029, FR-032) in `extensions/vscode/src/commands/configure.ts`
- [x] T029 [US2] Create @ato chat participant handler with slash command routing (/compliance→ComplianceAgent, /knowledge→KnowledgeBaseAgent, /config→ConfigurationAgent), conversation history rebuild from ChatContext.history, streamed Markdown rendering, agent attribution footer, template rendering with Save buttons (FR-022–FR-028) in `extensions/vscode/src/participant.ts`
- [x] T030 [US2] Create extension activation with participant registration (isSticky=true, iconPath), command registration (ato.checkHealth, ato.configure, ato.analyzeCurrentFile, ato.analyzeWorkspace), silent background health check on activation (FR-034) in `extensions/vscode/src/extension.ts`

### Tests for User Story 2

- [x] T031 [P] [US2] Create participant unit tests — slash command maps to correct targetAgent, history rebuild produces role/content pairs, error displays "Configure Connection" button, no command sends without targetAgent in `extensions/vscode/test/suite/participant.test.ts`
- [x] T032 [P] [US2] Create mcpClient unit tests — request payload matches McpChatRequest schema, ECONNREFUSED/ETIMEDOUT/HTTP error mapping, timeout configuration, health check success/failure in `extensions/vscode/test/suite/mcpClient.test.ts`

**Checkpoint**: VS Code extension builds and tests pass. Launch in Extension Development Host, type `@ato` in Copilot Chat — participant appears with icon and slash commands.

---

## Phase 5: User Story 4 — Compliance Analysis Commands (Priority: P2)

**Goal**: Analyze the current editor file or entire workspace for NIST 800-53 compliance issues from the VS Code command palette, displaying findings in a severity-colored webview panel.

**Independent Test**: Open a `.bicep` file, run "Security Posture Intelligence Navigator: Analyze Current File for Compliance" from command palette, verify findings appear in side-by-side webview panel with severity-colored badges.

### Implementation for User Story 4

- [x] T033 [P] [US4] Create analyzeFile command — send file content/name/language to /mcp/chat as structured compliance prompt, parse findings JSON, open webview panel (FR-030) in `extensions/vscode/src/commands/analyzeFile.ts`
- [x] T034 [P] [US4] Create analyzeWorkspace command — glob scan `**/*.bicep`, `**/*.tf`, `**/*.yaml`, `**/*.yml`, `**/*.json` (exclude node_modules/.git/bin/obj), send each file to /mcp/chat, aggregate findings (FR-031) in `extensions/vscode/src/commands/analyzeWorkspace.ts`
- [x] T035 [US4] Create analysis panel webview — HTML content with severity-colored badges (high=red, medium=orange, low=green), control IDs in monospace, recommendations in green cards, grouping by severity in `extensions/vscode/src/webview/analysisPanel.ts`

### Tests for User Story 4

- [x] T035a [P] [US4] Create analyzeFile unit tests — structured prompt includes file content/name/language, findings parsed from JSON response, webview panel opens side-by-side, error displays "Configure Connection" button in `extensions/vscode/test/suite/analyzeFile.test.ts`
- [x] T035b [P] [US4] Create analyzeWorkspace unit tests — glob scan matches correct file patterns, excludes node_modules/.git/bin/obj, aggregates findings from multiple files, workspace-level webview panel displays combined results in `extensions/vscode/test/suite/analyzeWorkspace.test.ts`

**Checkpoint**: "Analyze Current File" and "Analyze Workspace" commands work from command palette. Webview panel displays findings with correct severity coloring.

---

## Phase 6: User Story 3 — M365 Copilot Extension for Teams (Priority: P3)

**Goal**: Express.js webhook server receiving Teams messages, forwarding to MCP Server, and returning intent-routed Adaptive Card responses with compliance scores, Azure Gov portal links, and follow-up prompts.

**Independent Test**: POST to `/api/messages` with `{ "text": "Run compliance scan for subscription 123", "conversation": { "id": "test-1" }, "from": { "id": "user-1" } }`, verify Adaptive Card attachment returns with compliance results.

### Implementation for User Story 3

- [x] T036 [P] [US3] Create ATOApiClient service with 300s timeout, `User-Agent: ATO-Copilot-M365-Extension/1.0.0` header, sendMessage (generates `m365-{timestamp}-{random9}` conversationId), checkHealth (FR-049) in `extensions/m365/src/services/atoApiClient.ts`
- [x] T037 [P] [US3] Create compliance assessment Adaptive Card builder with overall score percentage (≥80% green, ≥60% orange, <60% red), passed/warning/failed column counts, "View Full Report" and "Generate Remediation Plan" action buttons (FR-043) in `extensions/m365/src/cards/complianceCard.ts`
- [x] T038 [P] [US3] Create infrastructure result Adaptive Card builder with resource details and "View in Azure Portal" button linking to `https://portal.azure.us/#resource/${resourceId}` (FR-044) in `extensions/m365/src/cards/infrastructureCard.ts`
- [x] T039 [P] [US3] Create cost estimate Adaptive Card builder with cost breakdown display in `extensions/m365/src/cards/costCard.ts`
- [x] T040 [P] [US3] Create deployment result Adaptive Card builder with deployment status and logs in `extensions/m365/src/cards/deploymentCard.ts`
- [x] T041 [P] [US3] Create resource list Adaptive Card builder with resource name, type, status table in `extensions/m365/src/cards/resourceCard.ts`
- [x] T042 [P] [US3] Create generic response Adaptive Card builder for unclassified intents in `extensions/m365/src/cards/genericCard.ts`
- [x] T043 [P] [US3] Create error Adaptive Card builder with error message and help text (FR-042) in `extensions/m365/src/cards/errorCard.ts`
- [x] T044 [P] [US3] Create follow-up Adaptive Card builder with prompt, numbered missing fields, quick reply action buttons (FR-041) in `extensions/m365/src/cards/followUpCard.ts`
- [x] T045 [US3] Create Express server with POST /api/messages (webhook handler with intent-based card routing), GET /health, GET /openapi.json, GET /ai-plugin.json endpoints, config validation on startup, graceful shutdown on SIGINT/SIGTERM (FR-037, FR-045, FR-046, FR-048, FR-050) in `extensions/m365/src/index.ts`
- [x] T046 [US3] Create Teams app manifest with bot configuration and conversation starters, ai-plugin.json, and openapi.json per contracts/m365-extension.md in `extensions/m365/src/manifest/`

### Tests for User Story 3

- [x] T047 [P] [US3] Create card builder unit tests — all 8 card types produce valid Adaptive Card v1.5 JSON, compliance score color thresholds (≥80%/≥60%/<60%), infrastructure card includes portal.azure.us link, follow-up card renders missing fields in `extensions/m365/test/cards.test.ts`
- [x] T048 [P] [US3] Create ATOApiClient unit tests — request payload includes conversation context and User-Agent header, 300s timeout configured, health check returns boolean, sendMessage generates m365-format conversationId in `extensions/m365/test/atoApiClient.test.ts`

**Checkpoint**: M365 extension starts on port 3978. `curl http://localhost:3978/health` returns JSON. POST to `/api/messages` returns Adaptive Card attachment.

---

## Phase 7: User Story 5 — Export and Template Management (Priority: P3)

**Goal**: Export compliance analysis results in Markdown, JSON, and HTML formats, and save generated IaC templates to the workspace organized by type.

**Independent Test**: Run a compliance analysis, export to Markdown/HTML/JSON, verify file creation. Save a template, verify it lands in the correct type-based folder (e.g., `bicep/`).

### Implementation for User Story 5

- [x] T049 [P] [US5] Create export service supporting Markdown (title, timestamp, summary table, findings), JSON (machine-readable array), HTML (styled page with severity badges) formats, clipboard copy via vscode.env.clipboard, editor preview, file save dialog (FR-035) in `extensions/vscode/src/services/exportService.ts`
- [x] T050 [P] [US5] Create workspace service for type-based template saving (bicep→`bicep/`, terraform→`terraform/`, kubernetes→`kubernetes/`, powershell→`scripts/`, arm→`arm/`, other→`templates/`), project folder creation, conflict resolution with Overwrite/Cancel/Save As New prompt (FR-036) in `extensions/vscode/src/services/workspaceService.ts`

### Tests for User Story 5

- [x] T051 [P] [US5] Create export service unit tests — Markdown report includes title and findings table, JSON output is valid array, HTML includes severity-colored badges, clipboard and file save operations in `extensions/vscode/test/suite/exportService.test.ts`
- [x] T051a [P] [US5] Create workspace service unit tests — type-based folder mapping (bicep→bicep/, terraform→terraform/, etc.), project folder creation, conflict resolution prompts (Overwrite/Cancel/Save As New), file write verification in `extensions/vscode/test/suite/workspaceService.test.ts`

**Checkpoint**: Export commands produce correct format output. Template saves land in type-based folders with conflict resolution.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Icons, documentation, build validation, and quickstart verification

- [x] T052 [P] Add extension icon file (`media/icon.png`) for VS Code extension in `extensions/vscode/media/`
- [x] T053 [P] Add icon files (`color.png`, `outline.png`) for M365 Teams manifest in `extensions/m365/src/manifest/`
- [x] T054 [P] Add README documentation for VS Code extension covering installation, commands, settings in `extensions/vscode/README.md`
- [x] T055 [P] Add README documentation for M365 extension covering setup, configuration, deployment in `extensions/m365/README.md`
- [x] T056 Verify full solution build: `dotnet build Ato.Copilot.sln` succeeds and `npm test` passes in `extensions/vscode/` and `extensions/m365/`
- [x] T057 Run quickstart.md verification scenarios per `specs/013-copilot-everywhere/quickstart.md` — Channels unit tests, in-process smoke test, VS Code extension, M365 extension

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on T002 (NuGet deps) — BLOCKS all .NET user stories
- **US1 (Phase 3)**: Depends on Phase 2 completion — implements all interfaces
- **US2 (Phase 4)**: Depends on T003 (VS Code project init) — independent of US1 (.NET)
- **US4 (Phase 5)**: Depends on T026 (mcpClient.ts from US2) — same VS Code extension
- **US3 (Phase 6)**: Depends on T004 (M365 project init) — independent of US1/US2
- **US5 (Phase 7)**: Depends on T030 (extension.ts from US2) — same VS Code extension
- **Polish (Phase 8)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: Pure .NET — no dependency on TypeScript extensions. Can start after Phase 2.
- **US2 (P2)**: Pure TypeScript — no dependency on US1. Can start after T003 (Phase 1).
- **US4 (P2)**: Depends on US2's `mcpClient.ts` (T026). Must follow US2 or at least T026.
- **US3 (P3)**: Pure TypeScript — no dependency on US1/US2. Can start after T004 (Phase 1).
- **US5 (P3)**: Depends on US2's extension activation (T030). Must follow US2.

### Within Each User Story

- Models and interfaces before implementations (Phase 2 prerequisite)
- Independent implementations marked [P] can run in parallel
- DI registration after all implementations complete
- Tests after implementation (tests reference concrete classes)
- Story complete before moving to next priority

### Parallel Opportunities

- All Phase 2 tasks (T005–T015) can run in parallel (separate files, no dependencies)
- Phase 3 implementations T016–T020 can run in parallel (separate implementation files)
- Phase 3 tests T022–T025 can run in parallel (separate test files)
- **US1 (Phase 3) and US2 (Phase 4) can run in parallel** — different languages, different projects
- **US1 (Phase 3) and US3 (Phase 6) can run in parallel** — different languages, different projects
- Phase 6 card builders T037–T044 can ALL run in parallel (9 independent files)
- Phase 7 tasks T049–T051 can run in parallel (separate files)

---

## Parallel Example: Post-Setup Execution

```bash
# After Phase 1 (Setup) completes, launch Phase 2 + TypeScript stories in parallel:

# Developer A: .NET path (Phase 2 → Phase 3)
Task T005-T015: All foundational models/interfaces in parallel
# → then Phase 3:
Task T016-T020: All US1 implementations in parallel
Task T021: DI extensions (after T016-T020)
Task T022-T025: All US1 tests in parallel

# Developer B: VS Code extension (Phase 4 → Phase 5 → Phase 7)
Task T026-T028: mcpClient + health + configure in parallel
Task T029: participant.ts (after T026)
Task T030: extension.ts (after T029)
Task T031-T032: Tests in parallel
# → then Phase 5:
Task T033-T034: analyzeFile + analyzeWorkspace in parallel
Task T035: analysisPanel.ts
# → then Phase 7:
Task T049-T051: export + workspace services + tests in parallel

# Developer C: M365 extension (Phase 6)
Task T036-T044: atoApiClient + all 8 card builders in parallel
Task T045: index.ts Express server (after T036-T044)
Task T046: manifest files
Task T047-T048: Tests in parallel
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (4 tasks)
2. Complete Phase 2: Foundational (11 tasks)
3. Complete Phase 3: US1 — Channel Abstraction Layer (10 tasks)
4. **STOP and VALIDATE**: `dotnet test --filter "FullyQualifiedName~Channels"` — all pass
5. Deploy as MVP — Channels library ready for consumption

### Incremental Delivery

1. Setup + Foundational → Models, interfaces, project structure ready
2. Add US1 → Channel Abstraction Layer → **MVP!** (core library functional)
3. Add US2 → VS Code `@ato` participant → Deploy/Demo (users can chat in VS Code)
4. Add US4 → Compliance commands → Deploy/Demo (file/workspace analysis)
5. Add US3 → M365 Adaptive Cards → Deploy/Demo (Teams integration)
6. Add US5 → Export + templates → Deploy/Demo (full feature)
7. Polish → Icons, docs, validation

### Task Count Summary

| Phase | Impl | Tests | Total |
|-------|------|-------|-------|
| Phase 1: Setup | 5 | 0 | 5 |
| Phase 2: Foundational | 11 | 0 | 11 |
| Phase 3: US1 (P1) — MVP | 6 | 5 | 11 |
| Phase 4: US2 (P2) | 5 | 2 | 7 |
| Phase 5: US4 (P2) | 3 | 2 | 5 |
| Phase 6: US3 (P3) | 11 | 2 | 13 |
| Phase 7: US5 (P3) | 2 | 2 | 4 |
| Phase 8: Polish | 6 | 0 | 6 |
| **Total** | **49** | **13** | **62** |

---

## Notes

- [P] tasks = different files, no dependencies — safe to parallelize
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable after its prerequisites
- US1 (.NET) and US2/US3 (TypeScript) have zero cross-dependencies — ideal for parallel teams
- US4 and US5 share the VS Code extension with US2 — sequence after US2's core files
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Baseline test count: ~2364 — verify no regressions after each phase
