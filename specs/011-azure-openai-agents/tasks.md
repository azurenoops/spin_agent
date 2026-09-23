# Tasks: Add Azure OpenAI to Security Posture Intelligence Navigator Agents

**Input**: Design documents from `/specs/011-azure-openai-agents/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/agent-ai-contract.md, quickstart.md

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US5)
- Exact file paths included in all task descriptions

---

## Phase 1: Setup

**Purpose**: Add NuGet packages and configuration section needed for Azure OpenAI integration

- [X] T001 Add `Azure.AI.OpenAI` and `Microsoft.Extensions.AI.OpenAI` NuGet packages to `src/Ato.Copilot.Core/Ato.Copilot.Core.csproj`
- [X] T002 Add `Gateway:AzureOpenAI` configuration section to `src/Ato.Copilot.Mcp/appsettings.json` with Endpoint, ApiKey, DeploymentName, ChatDeploymentName, EmbeddingDeploymentName, UseManagedIdentity, AgentAIEnabled (false), MaxToolCallRounds (5), Temperature (0.3)
- [X] T003 Verify solution builds with `dotnet build Ato.Copilot.sln` — no warnings in modified files

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Configuration model, IChatClient factory, and BaseAgent AI processing — MUST be complete before any user story phase

**CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 [US5] Add `AgentAIEnabled` (bool, default false), `MaxToolCallRounds` (int, default 5), and `Temperature` (double, default 0.3) properties to `AzureOpenAIGatewayOptions` in `src/Ato.Copilot.Core/Configuration/GatewayOptions.cs` with XML documentation
- [X] T005 [US5] Add `services.Configure<AzureOpenAIGatewayOptions>` binding for `Gateway:AzureOpenAI` section in `src/Ato.Copilot.Core/Extensions/CoreServiceExtensions.cs` (inside `AddAtoCopilotCore`)
- [X] T006 [US1] Add conditional `IChatClient` singleton registration in `src/Ato.Copilot.Core/Extensions/CoreServiceExtensions.cs` — when `Endpoint` is non-empty, construct `AzureOpenAIClient` with `ApiKeyCredential` or `DefaultAzureCredential` (when `UseManagedIdentity` is true), call `.AsChatClient(ChatDeploymentName)`, register as singleton; when `Endpoint` is empty, skip registration (no errors, no warning logs)
- [X] T007 [US2] Add new constructor overload to `BaseAgent` in `src/Ato.Copilot.Agents/Common/BaseAgent.cs` accepting `IChatClient?` and `AzureOpenAIGatewayOptions?` — store as private fields `_chatClient` and `_aiOptions`; preserve existing `BaseAgent(ILogger)` constructor
- [X] T008 [US2] Add `BuildChatContext` private method to `BaseAgent` in `src/Ato.Copilot.Agents/Common/BaseAgent.cs` — constructs `List<ChatMessage>` with system prompt (from `GetSystemPrompt()`), conversation history (from `AgentConversationContext.MessageHistory`), and user message. Note: context window truncation is deferred — if history exceeds token limits the Azure OpenAI API error is caught by `TryProcessWithAiAsync`'s try-catch and triggers deterministic fallback
- [X] T009 [US2] Add `BuildToolDefinitions` private method to `BaseAgent` in `src/Ato.Copilot.Agents/Common/BaseAgent.cs` — converts registered `Tools` list into `List<AITool>` using `AIFunctionFactory.Create` with each tool's Name, Description, and Parameters metadata
- [X] T010 [US2] Add `TryProcessWithAiAsync` protected method to `BaseAgent` in `src/Ato.Copilot.Agents/Common/BaseAgent.cs` — returns `null` if `_chatClient` is null or `AgentAIEnabled` is false; otherwise builds chat context, builds tool definitions, calls `_chatClient.GetResponseAsync` with `ChatOptions { Tools, Temperature }`, handles `FunctionCallContent` responses by executing named `BaseTool.ExecuteAsync` and sending `FunctionResultContent` back, loops up to `MaxToolCallRounds`, returns final `AgentResponse` with LLM text; on unknown tool name sends error result to LLM; on max rounds exceeded returns summary response; on any exception logs error and returns `null`; wraps entire body in try-catch per research R9 pattern. **Structured logging (Constitution V)**: log LLM call start/end with duration, each tool call in chain (tool name, arguments hash, success/failure, duration), fallback-to-direct events, max-rounds-hit termination reason
- [X] T011 [US2] Update DI registration in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs` — in `AddComplianceAgent`, `AddConfigurationAgent`, and `AddKnowledgeBaseAgent`, resolve `IChatClient?` via `sp.GetService<IChatClient>()` and `IOptions<AzureOpenAIGatewayOptions>` and pass to agent constructors

**Checkpoint**: Foundation ready — IChatClient registers from config, BaseAgent has TryProcessWithAiAsync, DI passes dependencies to agents. All existing tests still pass.

---

## Phase 3: User Story 1 + User Story 5 — Azure OpenAI Client Wiring + Configuration & Feature Flags (Priority: P1) MVP

**Goal**: IChatClient resolves from DI when configured, null when unconfigured. Feature flag controls AI behavior. AiRemediationPlanGenerator immediately benefits from non-null IChatClient.

**Independent Test**: Verify IChatClient resolves/null based on config. Verify AzureOpenAIGatewayOptions defaults and new fields bind correctly.

### Tests for US1 + US5

- [X] T012 [P] [US5] Write unit tests for `AzureOpenAIGatewayOptions` in `tests/Ato.Copilot.Tests.Unit/Configuration/AzureOpenAIGatewayOptionsTests.cs` — test default values (AgentAIEnabled=false, MaxToolCallRounds=5, Temperature=0.3), binding from config section, validation of bounds
- [X] T013 [P] [US1] Write unit tests for IChatClient factory registration in `tests/Ato.Copilot.Tests.Unit/Extensions/CoreServiceExtensionsAiTests.cs` — test: registers IChatClient when Endpoint is non-empty with ApiKey; registers with DefaultAzureCredential when UseManagedIdentity=true; does NOT register when Endpoint is empty; does NOT register when Gateway:AzureOpenAI section is missing; no startup errors when unconfigured; ChatDeploymentName is used as model ID; correctly constructs client with Azure Government `.us` endpoint URL (FR-002/SC-008 validation)

**Checkpoint**: P1 infrastructure validated — config binds correctly, IChatClient factory produces/skips client based on config.

---

## Phase 4: User Story 2 + User Story 6 — Agent-Level AI Processing + Degraded Mode (Priority: P2)

**Goal**: BaseAgent's `TryProcessWithAiAsync` performs LLM-backed tool-calling loop. Agents fall back gracefully when AI unavailable.

**Independent Test**: Mock IChatClient, call TryProcessWithAiAsync, verify tool-calling loop and fallback behavior.

### Tests for US2 + US6

- [X] T014 [P] [US2] Write unit tests for `BaseAgent.TryProcessWithAiAsync` in `tests/Ato.Copilot.Tests.Unit/Common/BaseAgentAiProcessingTests.cs` — create concrete test subclass of BaseAgent with mock tools; test: returns null when IChatClient is null; returns null when AgentAIEnabled=false; sends system prompt + history + user message to LLM; handles single tool call (FunctionCallContent → tool execution → FunctionResultContent → final text); handles multi-tool chaining (2-3 rounds); terminates at MaxToolCallRounds with summary response; handles unknown tool name (sends error result back to LLM); handles tool execution exception (sends error to LLM); handles LLM exception (catches, logs, returns null for fallback); handles empty LLM response (returns user-friendly message); populates AgentResponse.ToolsExecuted with each tool invoked; includes ProcessingTimeMs
- [X] T015 [P] [US2] Write unit tests for `BuildChatContext` in `tests/Ato.Copilot.Tests.Unit/Common/BaseAgentAiProcessingTests.cs` — test: system prompt is first message with ChatRole.System; conversation history mapped correctly (user/assistant roles); user message is last with ChatRole.User; empty history produces only system + user messages
- [X] T016 [P] [US2] Write unit tests for `BuildToolDefinitions` in `tests/Ato.Copilot.Tests.Unit/Common/BaseAgentAiProcessingTests.cs` — test: each registered BaseTool produces an AITool with correct Name and Description; empty Tools list produces empty AITool list; tool Parameters metadata maps to function schema
- [X] T017 [P] [US6] Write degraded mode unit tests in `tests/Ato.Copilot.Tests.Unit/Common/BaseAgentAiProcessingTests.cs` — test: LLM timeout exception triggers fallback (returns null); LLM rate limit exception triggers fallback; agent with IChatClient=null processes identically to pre-AI behavior; agent with AgentAIEnabled=false processes identically to pre-AI behavior; mid-request Azure OpenAI failure falls back for that request only

### Implementation for US2 + US6

- [X] T018 [US2] Update `ComplianceAgent` constructor in `src/Ato.Copilot.Agents/Compliance/Agents/ComplianceAgent.cs` to accept `IChatClient? chatClient = null` and `IOptions<AzureOpenAIGatewayOptions>? aiOptions = null`, pass to `BaseAgent` constructor overload; in `ProcessAsync`, after auth gate, call `TryProcessWithAiAsync(message, context, ct)` — if non-null return it, otherwise continue to existing `RouteToToolAsync` behavior
- [X] T019 [P] [US2] Update `ConfigurationAgent` constructor in `src/Ato.Copilot.Agents/Configuration/Agents/ConfigurationAgent.cs` to accept `IChatClient? chatClient = null` and `IOptions<AzureOpenAIGatewayOptions>? aiOptions = null`, pass to `BaseAgent`; in `ProcessAsync`, call `TryProcessWithAiAsync` first — if non-null return it, otherwise continue to existing `ClassifyIntent → ExtractArguments → tool.ExecuteAsync` behavior
- [X] T020 [P] [US2] Update `KnowledgeBaseAgent` constructor in `src/Ato.Copilot.Agents/KnowledgeBase/Agents/KnowledgeBaseAgent.cs` to accept `IChatClient? chatClient = null` and `IOptions<AzureOpenAIGatewayOptions>? aiOptions = null`, pass to `BaseAgent`; in `ProcessAsync`, call `TryProcessWithAiAsync` first — if non-null return it, otherwise continue to existing `AnalyzeQueryType → FindToolForQueryType → tool.ExecuteAsync` behavior

### Per-Agent AI Tests

- [X] T021 [P] [US2] Write unit tests for ComplianceAgent AI behavior in `tests/Ato.Copilot.Tests.Unit/Agents/ComplianceAgentAiTests.cs` — test: AI-enabled processes through TryProcessWithAiAsync; AI-disabled falls back to deterministic RouteToToolAsync; auth gate still runs before AI path; AI failure falls back to deterministic
- [X] T022 [P] [US2] Write unit tests for ConfigurationAgent AI behavior in `tests/Ato.Copilot.Tests.Unit/Agents/ConfigurationAgentAiTests.cs` — test: AI-enabled processes through TryProcessWithAiAsync; AI-disabled falls back to ClassifyIntent path
- [X] T023 [P] [US2] Write unit tests for KnowledgeBaseAgent AI behavior in `tests/Ato.Copilot.Tests.Unit/Agents/KnowledgeBaseAgentAiTests.cs` — test: AI-enabled processes through TryProcessWithAiAsync; AI-disabled falls back to AnalyzeQueryType path

**Checkpoint**: All agents have AI-powered processing path and graceful fallback. All existing tests pass unchanged.

---

## Phase 5: User Story 3 — ChatHub AI Integration (Priority: P3)

**Goal**: Verify end-to-end flow from MCP chat endpoint through AI-enabled agents — conversation history correctly passed, AI responses stored as assistant messages.

**Independent Test**: Send message through `/mcp/chat` → verify agent receives message with history → AI response returned (or deterministic fallback).

No code changes needed for this story (per research R7) — AI integration happens inside `agent.ProcessAsync`. The verification is that the existing MCP→Orchestrator→Agent flow correctly triggers AI processing when configured.

### Tests for US3

- [X] T024 [US3] Write integration-style unit tests in `tests/Ato.Copilot.Tests.Unit/Server/McpServerAiIntegrationTests.cs` — test: `McpServer.ProcessChatRequestAsync` passes conversation history to agent's `AgentConversationContext.MessageHistory`; agent with AI enabled returns natural language response via ProcessAsync; conversation history includes both user and assistant roles; agent fallback produces raw tool output when AI disabled

**Checkpoint**: End-to-end ChatHub→MCP→Agent→AI flow verified. No code changes needed — tests confirm existing wiring works with AI-enabled agents.

---

## Phase 6: User Story 4 — System Prompt Enhancement (Priority: P3)

**Goal**: All 5 agent prompt files enhanced with "Response Guidelines" and "Tool Selection" sections for LLM behavioral guidance.

**Independent Test**: Load each prompt file and verify new sections are present. Verify original content is unchanged.

### Implementation for US4

- [X] T025 [P] [US4] Append "Response Guidelines" and "Tool Selection" sections to `src/Ato.Copilot.Agents/Compliance/Prompts/ComplianceAgent.prompt.txt` — Response Guidelines: Markdown formatting, severity badges ([CRITICAL], [HIGH], [MODERATE], [LOW]), actionable recommendations, user-friendly error explanations, no raw JSON. Tool Selection: guidance for 65+ compliance tools — assessment triggers, remediation workflow chaining, control lookup patterns, plan generation sequencing, Kanban/PIM integration
- [X] T026 [P] [US4] Append "Response Guidelines" and "Tool Selection" sections to `src/Ato.Copilot.Agents/Configuration/Prompts/ConfigurationAgent.prompt.txt` — Response Guidelines: Markdown formatting, clear setting names and values, before/after comparisons. Tool Selection: single `configuration_manage` tool — action detection (get/set/list/delete), parameter extraction guidance
- [X] T027 [P] [US4] Append "Response Guidelines" and "Tool Selection" sections to `src/Ato.Copilot.Agents/KnowledgeBase/Prompts/KnowledgeBaseAgent.prompt.txt` — Response Guidelines: educational tone, cite NIST/STIG/RMF/FedRAMP sources, structured explanations with examples. Tool Selection: 7 KB tools — query type to tool mapping, search vs. browse vs. explain patterns
- [X] T028 [P] [US4] Append "Response Guidelines" and "Tool Selection" sections to `src/Ato.Copilot.Agents/Compliance/Prompts/KanbanAgent.prompt.txt` — Response Guidelines: Kanban board formatting, status indicators, due date highlighting. Tool Selection: remediation tracking tools — board/task operations
- [X] T029 [P] [US4] Append "Response Guidelines" and "Tool Selection" sections to `src/Ato.Copilot.Agents/Compliance/Prompts/PimAgent.prompt.txt` — Response Guidelines: clear role activation status, time-bound information, security warnings. Tool Selection: PIM activation tools — role request patterns

### Tests for US4

- [X] T030 [P] [US4] Write unit tests for system prompt content in `tests/Ato.Copilot.Tests.Unit/Prompts/SystemPromptEnhancementTests.cs` — test each of the 5 agents: `GetSystemPrompt()` returns non-empty string; output contains "Response Guidelines" section; output contains "Tool Selection" section; original prompt content still present (check for known phrases from each agent's existing prompt)

**Checkpoint**: All 5 prompt files enhanced. LLM behavioral guidance in place for all agents.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Validation, regression testing, and cleanup

- [X] T031 Run `dotnet build Ato.Copilot.sln` and verify zero warnings in modified files
- [X] T032 Run `dotnet test Ato.Copilot.sln` and verify all existing tests (2271 unit + 33 integration) plus all new tests pass with zero failures
- [X] T033 Run quickstart.md validation — verify degraded mode works (no `Gateway:AzureOpenAI` config → all agents produce raw tool output as before)
- [X] T034 Verify `AiRemediationPlanGenerator` continues to work with both null and non-null `IChatClient` (existing tests in `tests/Ato.Copilot.Tests.Unit/Services/Engines/Remediation/AiRemediationPlanGeneratorTests.cs` pass)
- [X] T035 [US4] Update relevant `/docs/*.md` documentation with AI processing path description, `Gateway:AzureOpenAI` configuration reference, feature flag behavior, and fallback/degraded mode explanation (Constitution Quality Gate: Documentation)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user story phases
- **US1+US5 (Phase 3)**: Depends on Phase 2 (T004–T006 for config + factory)
- **US2+US6 (Phase 4)**: Depends on Phase 2 (T007–T011 for BaseAgent AI methods + DI)
- **US3 (Phase 5)**: Depends on Phase 4 (agents must have AI processing for integration tests)
- **US4 (Phase 6)**: Can start after Phase 2 — independent of US2/US3
- **Polish (Phase 7)**: Depends on all prior phases

### User Story Dependencies

- **US1 + US5 (P1)**: Independent — config + factory have no downstream story dependencies for testing
- **US2 + US6 (P2)**: Depends on Phase 2 foundation (BaseAgent AI methods). Independent of US1 tests.
- **US3 (P3)**: Depends on US2 (agents must have AI processing). Tests verify wiring, no new code.
- **US4 (P3)**: Independent — prompt file edits have no code dependencies. Can parallel with US2/US3.

### Within Each User Story

- Tests written alongside implementation (not strict TDD — tests validate the implementation)
- Configuration before factory before BaseAgent before concrete agents
- Core implementation before integration tests

### Parallel Opportunities

- T012, T013 can run in parallel (different test files, config vs factory)
- T014, T015, T016, T017 can run in parallel (all in same test file but independent test classes)
- T018, T019, T020 — ComplianceAgent is sequential; ConfigurationAgent and KnowledgeBaseAgent can parallel with each other
- T021, T022, T023 can run in parallel (different test files)
- T025, T026, T027, T028, T029 can ALL run in parallel (different .prompt.txt files)
- T030 can run in parallel with prompt file edits (different file)
- Phase 6 (US4 — prompts) can run in parallel with Phase 4 (US2 — agent AI) and Phase 5 (US3 — integration)

---

## Parallel Example: Phase 4 (US2 + US6)

```
# All base agent test classes can be written in parallel:
T014: BaseAgentAiProcessingTests — TryProcessWithAiAsync tests
T015: BaseAgentAiProcessingTests — BuildChatContext tests
T016: BaseAgentAiProcessingTests — BuildToolDefinitions tests
T017: BaseAgentAiProcessingTests — Degraded mode tests

# ConfigurationAgent and KnowledgeBaseAgent updates can parallel:
T019: ConfigurationAgent constructor + ProcessAsync AI path
T020: KnowledgeBaseAgent constructor + ProcessAsync AI path

# Per-agent AI tests can all parallel:
T021: ComplianceAgentAiTests
T022: ConfigurationAgentAiTests
T023: KnowledgeBaseAgentAiTests
```

---

## Parallel Example: Phase 6 (US4)

```
# All 5 prompt files can be edited in parallel:
T025: ComplianceAgent.prompt.txt
T026: ConfigurationAgent.prompt.txt
T027: KnowledgeBaseAgent.prompt.txt
T028: KanbanAgent.prompt.txt
T029: PimAgent.prompt.txt

# Prompt validation test:
T030: SystemPromptEnhancementTests (can parallel with prompt edits)
```

---

## Implementation Strategy

### MVP First (Phase 1 + 2 + 3)

1. Complete Phase 1: Setup (T001–T003) — packages + config
2. Complete Phase 2: Foundational (T004–T011) — options, factory, BaseAgent AI, DI
3. Complete Phase 3: US1+US5 tests (T012–T013) — validate config + factory
4. **STOP and VALIDATE**: `dotnet build && dotnet test` — all existing tests pass, new config/factory tests pass
5. At this point: IChatClient available from DI, feature flag controls AI, AiRemediationPlanGenerator auto-activates

### Incremental Delivery

1. Setup + Foundational + US1/US5 → Config & factory ready (MVP!)
2. Add US2/US6 → Agents have AI processing + graceful fallback → Test independently
3. Add US3 → Integration verified end-to-end → Test independently
4. Add US4 → Prompts enhanced for production quality → Test independently
5. Polish → Full regression pass → Deploy

### Parallel Team Strategy

With 2 developers after Phase 2:

- **Developer A**: Phase 4 (US2+US6 — agent AI processing + tests)
- **Developer B**: Phase 6 (US4 — prompt enhancements + tests)
- Then: Developer A does Phase 5 (US3 integration tests) while Developer B starts Phase 7 (polish)

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- No changes to McpServer.cs, McpHttpBridge.cs, ChatHub.cs, ChatService.cs (research R7)
- No changes to BaseTool.cs — tool metadata already structured for LLM function-calling
- No changes to AgentOrchestrator — keyword routing preserved per spec
- No new database entities or migrations — purely in-memory + config changes
- Existing `AiRemediationPlanGenerator` automatically benefits from IChatClient registration (no code change needed)
- Total: 35 tasks (3 setup + 8 foundational + 2 US1/US5 tests + 10 US2/US6 impl+tests + 1 US3 test + 6 US4 impl+tests + 5 polish)
