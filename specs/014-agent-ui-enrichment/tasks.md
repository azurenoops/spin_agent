# Tasks: Agent-to-UI Response Enrichment

**Input**: Design documents from `/specs/014-agent-ui-enrichment/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md  
**Branch**: `014-agent-ui-enrichment`  
**Baseline**: 2462 .NET tests + 93 TypeScript tests (61 VS Code + 32 M365)

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Exact file paths included in every task description

---

## Phase 1: Setup

**Purpose**: Project initialization — no story labels, no parallelism dependencies

- [X] T001 Verify baseline: run `dotnet build Ato.Copilot.sln` and `dotnet test` confirming 2462 tests pass, then `cd extensions/vscode && npm test` (61 tests) and `cd extensions/m365 && npm test` (32 tests)
- [X] T002 Verify branch `014-agent-ui-enrichment` is checked out and up-to-date with main

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core model and protocol changes that ALL user stories depend on. No user story work can begin until this phase is complete.

**⚠️ CRITICAL**: These tasks modify shared models used by every story phase

- [X] T003 Extend `AgentResponse` with optional properties (`Suggestions`, `RequiresFollowUp`, `FollowUpPrompt`, `MissingFields`, `ResponseData`) in `src/Ato.Copilot.Agents/Common/BaseAgent.cs` — all with defaults so existing agents compile unchanged (FR-005a, R-001)
- [X] T004 Add `ErrorDetail` class (`ErrorCode`, `Message`, `Suggestion`) with XML documentation comments (`<summary>`, `<param>`) to `src/Ato.Copilot.Mcp/Models/McpProtocol.cs` (FR-007, R-004, Constitution VI)
- [X] T005 Add `[JsonPropertyName("agentUsed")]` to `McpChatResponse.AgentName` in `src/Ato.Copilot.Mcp/Models/McpProtocol.cs` (FR-003, R-003)
- [X] T006 Add `IntentType`, `FollowUpPrompt`, `MissingFields`, `Data` properties to `McpChatResponse` in `src/Ato.Copilot.Mcp/Models/McpProtocol.cs` (FR-001, FR-005, FR-006)
- [X] T007 Change `McpChatResponse.Errors` from `List<string>` to `List<ErrorDetail>` in `src/Ato.Copilot.Mcp/Models/McpProtocol.cs` and update all code that populates `Errors` (FR-007)
- [X] T008 Add `Action` and `ActionContext` fields to the private `ChatRequest` class in `src/Ato.Copilot.Mcp/Server/McpHttpBridge.cs` (FR-014a, R-006)
- [X] T009 Add SSE event model classes (`SseAgentRoutedEvent`, `SseThinkingEvent`, `SseToolStartEvent`, `SseToolProgressEvent`, `SseToolCompleteEvent`, `SsePartialEvent`, `SseValidatingEvent`, `SseCompleteEvent`) with XML documentation on all public types to `src/Ato.Copilot.Mcp/Models/McpProtocol.cs` (FR-029a, data-model entity 5, Constitution VI)
- [X] T010 Update all existing .NET tests that reference `McpChatResponse.Errors` as `List<string>` in `tests/Ato.Copilot.Tests.Unit/` to use `List<ErrorDetail>` — ensure zero regressions
- [X] T011 Run `dotnet build Ato.Copilot.sln && dotnet test` — confirm all tests pass with foundational model changes

**Checkpoint**: Foundation ready — all model types updated, solution builds clean

---

## Phase 3: User Story 1 — Enriched MCP Server Responses (Priority: P1) 🎯 MVP

**Goal**: MCP server returns intent classification, tool execution details, structured data, suggestions, follow-up prompts, and structured errors for every chat response. Action routing enables drill-down and tool invocation via `/mcp/chat`.

**Independent Test**: POST to `/mcp/chat` with `"Run a FedRAMP assessment"` → response includes `intentType: "compliance"`, `agentUsed` (not `agentName`), populated `toolsExecuted`, and structured `data`.

### Implementation for User Story 1

- [X] T012 [US1] Add intent type mapping dictionary (`"compliance-agent"` → `"compliance"`, `"knowledgebase-agent"` → `"knowledgebase"`, `"configuration-agent"` → `"configuration"`, default → `"general"`) in `ProcessChatRequestAsync` in `src/Ato.Copilot.Mcp/Server/McpServer.cs` (FR-001, R-002)
- [X] T013 [US1] Map `ToolsExecuted` from `AgentResponse` to `McpChatResponse` (copying `ToolName`, `Success`, `ExecutionTimeMs`) in `ProcessChatRequestAsync` in `src/Ato.Copilot.Mcp/Server/McpServer.cs` (FR-002)
- [X] T014 [US1] Map `Suggestions`, `RequiresFollowUp`, `FollowUpPrompt`, `MissingFields`, and `ResponseData` from `AgentResponse` to `McpChatResponse` in `ProcessChatRequestAsync` in `src/Ato.Copilot.Mcp/Server/McpServer.cs` (FR-004, FR-005, FR-006)
- [X] T015 [US1] Update error handling in `ProcessChatRequestAsync` to use `ErrorDetail` with `errorCode`, `message`, and `suggestion` in `src/Ato.Copilot.Mcp/Server/McpServer.cs` (FR-007)
- [X] T016 [US1] Implement action routing: when `ChatRequest.Action` is present, route to appropriate MCP tool (`"remediate"` → `RemediationExecuteTool`, `"drillDown"` → `NistControlSearchTool`, `"collectEvidence"` → `EvidenceCollectionTool`, `"acknowledgeAlert"` → `WatchAcknowledgeAlertTool`, `"dismissAlert"` → `WatchDismissAlertTool`, `"escalateAlert"` → `WatchEscalateAlertTool`, `"updateFindingStatus"` → `ComplianceStatusTool`, `"showKanban"` → `KanbanBoardShowTool`, `"moveKanbanTask"` → `KanbanMoveTaskTool`, `"checkPimStatus"` → `PimListActiveTool`, `"activatePim"` → `PimActivateRoleTool`, `"listEligiblePimRoles"` → `PimListEligibleTool`) in `src/Ato.Copilot.Mcp/Server/McpServer.cs` (FR-014a, FR-014b, R-006)
- [X] T017 [US1] Pass `Action` and `ActionContext` from `HandleChatRequestAsync` and `HandleChatStreamRequestAsync` to `ProcessChatRequestAsync` in `src/Ato.Copilot.Mcp/Server/McpHttpBridge.cs` (FR-014a)
- [X] T018 [US1] Extend SSE streaming in `HandleChatStreamRequestAsync` to emit typed events (`agentRouted`, `thinking`, `toolStart`, `toolProgress`, `toolComplete`, `partial`, `validating`, `complete`) instead of plain progress strings in `src/Ato.Copilot.Mcp/Server/McpHttpBridge.cs` (FR-029a, R-005)
- [X] T019 [US1] Update `IProgress<string>` reports in `ProcessChatRequestAsync` to emit typed JSON event strings for agent routing and thinking phases in `src/Ato.Copilot.Mcp/Server/McpServer.cs` (FR-029a)
- [X] T020 [P] [US1] Create `IacComplianceScanTool` extending `BaseTool` with XML documentation on all public members in `src/Ato.Copilot.Agents/Compliance/Tools/IacComplianceScanTool.cs` — accepts `filePath`/`fileContent`/`fileType`/`framework` parameters, returns `List<ComplianceFinding>` as structured JSON. `ExecuteAsync` MUST accept and honor `CancellationToken` (FR-029f, R-009, Constitution VI, Constitution VIII)
- [X] T021 [US1] Register `IacComplianceScanTool` in `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs` and DI in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs` (FR-029f)
- [X] T022 [US1] Update audit logging to include PIM role and action context for infrastructure-modifying actions routed through action routing in `src/Ato.Copilot.Mcp/Middleware/AuditLoggingMiddleware.cs` (FR-018d)
- [X] T022a [P] [US1] Update `ComplianceAgent` to populate `AgentResponse.ResponseData` with structured dictionaries after assessments (`{ "type": "assessment", "complianceScore", "passedControls", "warningControls", "failedControls", "findings", "framework", "assessmentScope" }`), finding details (`{ "type": "finding", ... }`), remediation plans (`{ "type": "remediationPlan", ... }`), and kanban board views (`{ "type": "kanban", "board": ... }`) in `src/Ato.Copilot.Agents/Compliance/Agents/ComplianceAgent.cs` (FR-007a)
- [X] T022b [P] [US1] Update `ComplianceAgent` to populate `AgentResponse.Suggestions` with contextually relevant follow-up actions (e.g., failing scan → `["Generate remediation plan", "View detailed findings", "Show kanban board"]`; passing scan → `["Export compliance report", "View trend"]`) in `src/Ato.Copilot.Agents/Compliance/Agents/ComplianceAgent.cs` (FR-007d)
- [X] T022c [P] [US1] Update `KnowledgeBaseAgent` to populate `AgentResponse.ResponseData` with `{ "type": "answer", "answer", "sources", "controlId", "controlFamily" }` and `AgentResponse.Suggestions` with relevant follow-ups in the KnowledgeBase agent source file (FR-007b, FR-007d)
- [X] T022d [P] [US1] Update `ConfigurationAgent` to populate `AgentResponse.ResponseData` with `{ "type": "configuration", "subscriptionId", "framework", "baseline", "cloudEnvironment" }` and `AgentResponse.Suggestions` in the Configuration agent source file (FR-007c, FR-007d)
- [X] T022e [P] [US1] Add unit tests for `ComplianceAgent` ResponseData population (assessment, finding, remediationPlan, kanban data types) and Suggestions population in `tests/Ato.Copilot.Tests.Unit/Agents/ComplianceAgentResponseDataTests.cs` (FR-007a, FR-007d)
- [X] T022f [P] [US1] Add unit tests for `KnowledgeBaseAgent` and `ConfigurationAgent` ResponseData and Suggestions population in `tests/Ato.Copilot.Tests.Unit/Agents/AgentResponseDataTests.cs` (FR-007b, FR-007c, FR-007d)
- [X] T023 [P] [US1] Add unit tests for intent type mapping (all 3 agents + unknown default) in `tests/Ato.Copilot.Tests.Unit/Mcp/McpServerIntentMappingTests.cs`
- [X] T024 [P] [US1] Add unit tests for `ToolsExecuted` mapping, `Suggestions` pass-through, `RequiresFollowUp` / `FollowUpPrompt` / `MissingFields` mapping in `tests/Ato.Copilot.Tests.Unit/Mcp/McpServerResponseEnrichmentTests.cs`
- [X] T025 [P] [US1] Add unit tests for `ErrorDetail` model and error response construction in `tests/Ato.Copilot.Tests.Unit/Mcp/ErrorDetailTests.cs`
- [X] T026 [P] [US1] Add unit tests for action routing (each action type, missing action context, unknown action) in `tests/Ato.Copilot.Tests.Unit/Mcp/ActionRoutingTests.cs`
- [X] T027 [P] [US1] Add unit tests for `IacComplianceScanTool` (Bicep file, Terraform file, non-IaC file, empty content) in `tests/Ato.Copilot.Tests.Unit/Agents/IacComplianceScanToolTests.cs`
- [X] T028 [P] [US1] Add unit tests for SSE typed event serialization (all 8 event types) in `tests/Ato.Copilot.Tests.Unit/Mcp/SseEventTests.cs`
- [X] T029 [US1] Run `dotnet build Ato.Copilot.sln && dotnet test` — confirm all tests pass including new US1 tests

**Checkpoint**: MCP server returns fully enriched responses. POST `/mcp/chat` returns `intentType`, `agentUsed`, `toolsExecuted`, structured `data`, dynamic `suggestions`, and `ErrorDetail` errors. Action routing works. SSE streaming emits 8 typed events. IaC tool registered. All .NET tests pass.

---

## Phase 4: User Story 2 — Rich M365 Adaptive Cards (Priority: P2)

**Goal**: M365 Teams extension renders intent-specific Adaptive Cards with action buttons, drill-down navigation, conversation history, SSE streaming progress, and PIM-secured infrastructure actions. Dead card builders removed.

**Independent Test**: POST `/api/messages` with compliance question → response contains Adaptive Card with color-coded score, control counts, action buttons, and agent attribution footer instead of generic text card.

### Implementation for User Story 2

- [x] T030 [US2] Update `McpResponse` TypeScript interface in `extensions/m365/src/services/atoApiClient.ts` (inline interface — M365 has no `models/` directory) to align with enriched server response: `agentUsed`, `intentType`, `data`, `toolsExecuted`, `suggestions`, `requiresFollowUp`, `followUpPrompt`, `missingFields`, structured `errors` (FR-012, FR-026)
- [x] T031 [P] [US2] Delete `extensions/m365/src/cards/costCard.ts` and remove all imports/exports (FR-008)
- [x] T032 [P] [US2] Delete `extensions/m365/src/cards/deploymentCard.ts` and remove all imports/exports (FR-008)
- [x] T033 [P] [US2] Delete `extensions/m365/src/cards/infrastructureCard.ts` and remove all imports/exports (FR-008)
- [x] T034 [P] [US2] Delete `extensions/m365/src/cards/resourceCard.ts` and remove all imports/exports (FR-008)
- [x] T035 [US2] Delete tests for removed card builders (cost, deployment, infrastructure, resource) in `extensions/m365/test/` or `extensions/m365/src/__tests__/`
- [x] T036 [P] [US2] Create knowledge base Adaptive Card builder in `extensions/m365/src/cards/knowledgeBaseCard.ts` — answer text, source references, "Learn More" action, agent attribution footer (FR-009, contracts/adaptive-cards.json)
- [x] T037 [P] [US2] Create configuration Adaptive Card builder in `extensions/m365/src/cards/configurationCard.ts` — settings table (framework, baseline, subscription, environment), "Update Setting" actions, attribution footer (FR-010, contracts/adaptive-cards.json)
- [x] T038 [P] [US2] Create finding detail Adaptive Card builder in `extensions/m365/src/cards/findingDetailCard.ts` — 5-level severity badge, resource context, remediation guidance, "Apply Fix" action, Azure portal link (FR-010a, contracts/adaptive-cards.json)
- [x] T039 [P] [US2] Create remediation plan Adaptive Card builder in `extensions/m365/src/cards/remediationPlanCard.ts` — prioritized list, 5-phase timeline, risk reduction, "Start Remediation" action (FR-010a, contracts/adaptive-cards.json)
- [x] T040 [P] [US2] Create alert lifecycle Adaptive Card builder in `extensions/m365/src/cards/alertLifecycleCard.ts` — severity, affected resources, SLA countdown, "Acknowledge"/"Dismiss"/"Escalate" actions (FR-010a, contracts/adaptive-cards.json)
- [x] T041 [P] [US2] Create compliance trend Adaptive Card builder in `extensions/m365/src/cards/complianceTrendCard.ts` — sparkline approximation, trend direction indicator, significant events (FR-010a, contracts/adaptive-cards.json)
- [x] T042 [P] [US2] Create evidence collection Adaptive Card builder in `extensions/m365/src/cards/evidenceCollectionCard.ts` — completeness meter, evidence items with hashes, "Collect More" action (FR-010a, contracts/adaptive-cards.json)
- [x] T043 [P] [US2] Create NIST control Adaptive Card builder in `extensions/m365/src/cards/nistControlCard.ts` — control statement, implementation guidance, STIGs, FedRAMP baseline (FR-010a, contracts/adaptive-cards.json)
- [x] T044 [P] [US2] Create multi-turn clarification Adaptive Card builder in `extensions/m365/src/cards/clarificationCard.ts` — missing fields with input dropdowns, submit action (FR-010a, contracts/adaptive-cards.json)
- [x] T045 [P] [US2] Create remediation confirmation Adaptive Card builder in `extensions/m365/src/cards/confirmationCard.ts` — script preview, resource info, risk level, "Confirm"/"Cancel" actions (FR-018e, contracts/adaptive-cards.json)
- [x] T045a [P] [US2] Create remediation kanban board Adaptive Card builder in `extensions/m365/src/cards/kanbanBoardCard.ts` — columns (To Do, In Progress, Done/Verified) with finding title, severity badge, assigned resource, "Move to In Progress"/"Mark Complete" action buttons. Data from `KanbanBoardShowTool` via `data.type === "kanban"` (FR-010a, contracts/adaptive-cards.json)
- [x] T046 [US2] Update card index and card routing in `extensions/m365/src/cards/index.ts` — intent + `data.type` sub-routing per contracts/adaptive-cards.json `CardRouting` rules, including `"kanban"` → kanban board card (FR-011)
- [x] T047 [US2] Add agent attribution footer helper to all cards (`"Processed by: {agentUsed}"`) in `extensions/m365/src/cards/` shared utility (FR-013)
- [x] T048 [US2] Add suggestion buttons rendering (Action.Submit for each suggestion) to all card builders in `extensions/m365/src/cards/` (FR-023a)
- [x] T049 [US2] Update `extensions/m365/src/cards/genericCard.ts` to show agent attribution footer and suggestion buttons (FR-013, FR-023a)
- [x] T050 [US2] Update `extensions/m365/src/cards/errorCard.ts` to display `errorCode` badge, `message`, `suggestion`, and retry button (FR-007)
- [x] T051 [US2] Update `extensions/m365/src/cards/followUpCard.ts` to display follow-up prompt with missing fields and quick-reply buttons (FR-005)
- [x] T052 [US2] Update existing compliance card `extensions/m365/src/cards/complianceCard.ts` to use enriched `data` fields (score color coding, control counts, action buttons) (FR-011)
- [x] T053 [US2] Implement M365 Adaptive Card action button payloads — all action buttons submit structured `{ action, actionContext }` to `/mcp/chat` in `extensions/m365/src/` route handler (FR-014b)
- [x] T054 [US2] Implement conversation history accumulation in `extensions/m365/src/services/atoApiClient.ts` — `Map<string, ChatMessage[]>` keyed by conversationId, capped at 20 exchanges, pass to `/mcp/chat` requests (FR-014d, R-011)
- [x] T055 [US2] Add typing indicator / "Security Posture Intelligence Navigator is processing..." message while waiting for MCP server response in `extensions/m365/src/` bot handler (FR-014)
- [x] T056 [US2] Implement SSE client in `extensions/m365/src/services/sseClient.ts` — native `fetch` + `ReadableStream`, line-based SSE parser, event type dispatch, retry with exponential backoff, fallback to sync `/mcp/chat`. MUST support `AbortController` for cancellation (FR-029d, FR-029e, R-005, Constitution VIII)
- [x] T057 [US2] Integrate SSE client in M365 bot handler — show typing indicator on `agentRouted`/`thinking`, update intermediate status on `toolStart`/`toolComplete`, render final card on `complete` (FR-029d)
- [x] T058 [US2] Implement PIM pre-flight check flow in M365 — `action: "checkPimStatus"` before infrastructure-modifying actions, display PIM activation card with eligible roles on failure, `action: "activatePim"` button (FR-018c-ii)
- [x] T059 [US2] Add info-level logging for all MCP API calls (URL, conversationId, message length, response time, intentType) in `extensions/m365/src/services/atoApiClient.ts` (FR-027)
- [x] T060 [US2] Add error-level logging for errors (errorCode, conversationId, suggestion) in `extensions/m365/src/services/atoApiClient.ts` (FR-028)
- [x] T061 [US2] Add info-level logging for compliance actions (agentUsed, toolsExecuted, intentType, timestamp) in `extensions/m365/src/services/atoApiClient.ts` (FR-029)
- [x] T062 [P] [US2] Add unit tests for knowledge base card builder in `extensions/m365/test/knowledgeBaseCard.test.ts`
- [x] T063 [P] [US2] Add unit tests for configuration card builder in `extensions/m365/test/configurationCard.test.ts`
- [x] T064 [P] [US2] Add unit tests for finding detail card builder in `extensions/m365/test/findingDetailCard.test.ts`
- [x] T065 [P] [US2] Add unit tests for remediation plan card builder in `extensions/m365/test/remediationPlanCard.test.ts`
- [x] T066 [P] [US2] Add unit tests for alert lifecycle card builder in `extensions/m365/test/alertLifecycleCard.test.ts`
- [x] T067 [P] [US2] Add unit tests for compliance trend card builder in `extensions/m365/test/complianceTrendCard.test.ts`
- [x] T067a [P] [US2] Add unit tests for evidence collection card builder in `extensions/m365/test/evidenceCollectionCard.test.ts`
- [x] T067b [P] [US2] Add unit tests for NIST control card builder in `extensions/m365/test/nistControlCard.test.ts`
- [x] T067c [P] [US2] Add unit tests for clarification card builder in `extensions/m365/test/clarificationCard.test.ts`
- [x] T067d [P] [US2] Add unit tests for confirmation card builder in `extensions/m365/test/confirmationCard.test.ts`
- [x] T067e [P] [US2] Add unit tests for kanban board card builder in `extensions/m365/test/kanbanBoardCard.test.ts`
- [x] T068 [P] [US2] Add unit tests for updated card routing (intent + data.type sub-routing, removed intent types return generic, kanban routing) in `extensions/m365/test/cardRouting.test.ts`
- [x] T069 [P] [US2] Add unit tests for conversation history accumulation (append, truncation at 20, per-conversationId isolation) in `extensions/m365/test/conversationHistory.test.ts`
- [x] T070 [P] [US2] Add unit tests for SSE client (event parsing, retry logic, sync fallback, 100%-progress-without-toolComplete 30s timeout) in `extensions/m365/test/sseClient.test.ts`
- [x] T071 [P] [US2] Add unit tests for PIM pre-flight check flow and PIM activation card in `extensions/m365/test/pimFlow.test.ts`
- [x] T072 [US2] Run `cd extensions/m365 && npm test` — confirm all tests pass including new US2 tests, zero references to removed card builders

**Checkpoint**: M365 extension renders 9+ card types with action buttons, agent attribution, suggestions, conversation history, SSE streaming, and PIM-secured actions. Dead cards removed. All M365 tests pass (SC-004, SC-005, SC-010).

---

## Phase 5: User Story 3 — Enriched VS Code Compliance Analysis Panel (Priority: P2)

**Goal**: VS Code analysis panel displays 5-level severity, control family grouping, framework reference, resource context, auto-remediation with diff preview, finding status lifecycle, CAC+PIM security, and SSE streaming progress.

**Independent Test**: Run "Security Posture Intelligence Navigator: Analyze Current File" on a `.bicep` file → panel shows findings with 5 severity levels, control family groupings, "NIST 800-53 Rev 5" badge, resource identifiers, and "Auto-Remediate" badges.

### Implementation for User Story 3

- [x] T073 [US3] Expand `ComplianceFinding` TypeScript interface in `extensions/vscode/src/commands/analyzeFile.ts` (or new shared types file) — add `controlFamily`, `resourceId`, `resourceType`, `autoRemediable`, `remediationScript`, `riskLevel`, `frameworkReference`, `findingStatus`, `findingId`; expand severity to 5 levels (FR-015, FR-018, data-model entity 6)
- [x] T074 [US3] Update `McpChatResponse` TypeScript interface in `extensions/vscode/src/services/mcpClient.ts` — add `intentType`, `toolsExecuted`, `suggestions`, `requiresFollowUp`, `followUpPrompt`, `missingFields`, `data`; rename `agentName` to `agentUsed`; change `errors` to `ErrorDetail[]` (FR-026 — satisfies both US3 and US4)
- [x] T075 [US3] Enable scripts in the webview panel (`enableScripts: true`) in `extensions/vscode/src/webview/analysisPanel.ts` for interactive actions (drill-down, remediation confirmation, PIM prompts)
- [x] T076 [US3] Implement 5-level severity badges with color coding (Critical=purple, High=red, Medium=orange, Low=yellow, Informational=blue) in `extensions/vscode/src/webview/analysisPanel.ts` (FR-015, R-008)
- [x] T077 [US3] Implement control family grouping with collapsible sections and finding counts per family in `extensions/vscode/src/webview/analysisPanel.ts` (FR-016)
- [x] T078 [US3] Add framework reference badge ("NIST 800-53 Rev 5") and assessment scope (file/workspace name) to panel header in `extensions/vscode/src/webview/analysisPanel.ts` (FR-017)
- [x] T079 [US3] Display `resourceId`, `resourceType`, `controlFamily`, `riskLevel` for each finding in `extensions/vscode/src/webview/analysisPanel.ts` (FR-018)
- [x] T080 [US3] Add "Auto-Remediate" badge on findings where `autoRemediable === true` in `extensions/vscode/src/webview/analysisPanel.ts` (FR-019)
- [x] T081 [US3] Implement "Apply Fix" action button — opens read-only diff preview of `remediationScript` with resource info, risk level, control ID, severity in `extensions/vscode/src/webview/analysisPanel.ts` and extension host handler (FR-018a)
- [x] T082 [US3] Implement "Confirm & Apply" button — executes remediation after explicit confirmation, sends `action: "remediate"` to `/mcp/chat` via `extensions/vscode/src/services/mcpClient.ts` (FR-018a, FR-018e)
- [x] T083 [US3] Implement finding status lifecycle display and status change actions (Open → Acknowledged → Remediated → Verified) via `action: "updateFindingStatus"` routed to `ComplianceStatusTool` in `extensions/vscode/src/webview/analysisPanel.ts` (FR-018b)
- [x] T084 [US3] Make control IDs and finding IDs clickable — click triggers `action: "drillDown"` with `actionContext: { "controlId": "<id>" }`, renders detail response in inline expansion in `extensions/vscode/src/webview/analysisPanel.ts` (FR-014c)
- [x] T085 [US3] Add tool execution summary section (tool names, execution times, success/failure status) to analysis panel when `toolsExecuted` present in `extensions/vscode/src/webview/analysisPanel.ts` (FR-020)
- [x] T086 [US3] Implement PIM pre-flight check before infrastructure-modifying actions — send `action: "checkPimStatus"`, show notification with "Activate PIM" button on failure, send `action: "activatePim"` on click in `extensions/vscode/src/services/mcpClient.ts` or new PIM helper (FR-018c-i)
- [x] T087 [US3] Implement SSE client in `extensions/vscode/src/services/sseClient.ts` — native `fetch` + `ReadableStream`, line-based parser, event dispatch, retry + sync fallback. MUST support `AbortController` for cancellation (FR-029a, FR-029e, R-005, Constitution VIII)
- [x] T088 [US3] Integrate SSE streaming in analysis panel — progress bar during scans, tool-by-tool progress updates from `toolStart`/`toolProgress`/`toolComplete` events in `extensions/vscode/src/webview/analysisPanel.ts` (FR-029c)
- [x] T089 [US3] Add info-level logging for MCP API calls (URL, conversationId, message length, responseTime, intentType) in `extensions/vscode/src/services/mcpClient.ts` (FR-027)
- [x] T090 [US3] Add error-level logging for errors (errorCode, conversationId, suggestion) in `extensions/vscode/src/services/mcpClient.ts` (FR-028)
- [x] T091 [US3] Add info-level logging for compliance actions (agentUsed, toolsExecuted, intentType, timestamp) in `extensions/vscode/src/services/mcpClient.ts` (FR-029)
- [x] T092 [P] [US3] Add unit tests for 5-level severity badge rendering in `extensions/vscode/test/suite/analysisPanel.test.ts`
- [x] T093 [P] [US3] Add unit tests for control family grouping logic in `extensions/vscode/test/suite/analysisPanelGrouping.test.ts`
- [x] T094 [P] [US3] Add unit tests for enriched ComplianceFinding interface (all new fields, graceful absence) in `extensions/vscode/test/suite/complianceFinding.test.ts`
- [x] T095 [P] [US3] Add unit tests for drill-down action, remediation confirmation flow, finding status transitions in `extensions/vscode/test/suite/analysisPanelActions.test.ts`
- [x] T096 [P] [US3] Add unit tests for SSE client (event parsing, retry, fallback, 100%-progress-without-toolComplete 30s timeout) in `extensions/vscode/test/suite/sseClient.test.ts`
- [x] T097 [P] [US3] Add unit tests for PIM pre-flight check flow in `extensions/vscode/test/suite/pimFlow.test.ts`
- [x] T098 [US3] Run `cd extensions/vscode && npm test` — confirm all tests pass including new US3 tests

**Checkpoint**: VS Code analysis panel shows 5 severity levels, control family grouping, framework badge, resource context, auto-remediation with PIM/CAC security, finding status lifecycle, SSE progress, and drill-down. All VS Code tests pass (SC-006, SC-007).

---

## Phase 6: User Story 4 — Enriched VS Code Chat Participant (Priority: P3)

**Goal**: VS Code `@ato` chat participant displays agent attribution, tool execution summaries, clickable suggestions, follow-up prompts, compliance score summaries, and SSE streaming progress.

**Independent Test**: Type `@ato /compliance How do I comply with AC-2?` in Copilot Chat → response shows "Processed by: Compliance Agent", tool execution summary, and follow-up suggestion buttons.

### Implementation for User Story 4

- [x] T099 [US4] Add agent attribution footer ("Processed by: {agentUsed}") rendering when `agentUsed` is non-empty in `extensions/vscode/src/participant.ts` (FR-021)
- [x] T100 [US4] Add collapsible "Tools Used" summary showing tool names and execution times when `toolsExecuted` is present in `extensions/vscode/src/participant.ts` (FR-022)
- [x] T101 [US4] Render `suggestions` as clickable follow-up buttons that re-submit as new `@ato` messages in `extensions/vscode/src/participant.ts` (FR-023)
- [x] T102 [US4] Render `followUpPrompt` with "Reply" action button when `requiresFollowUp` is true in `extensions/vscode/src/participant.ts` (FR-024)
- [x] T103 [US4] Render compliance summary (score, pass/warn/fail counts) as Markdown table when `intentType === "compliance"` and `data` present in `extensions/vscode/src/participant.ts` (FR-025)
- [x] T104 [US4] Integrate SSE streaming in chat participant — show "Routing to {agentName}..." on `agentRouted`, progress indicator on `toolStart`/`toolProgress`, append text on `partial`, render final enriched response on `complete` in `extensions/vscode/src/participant.ts` (FR-029b)
- [x] T105 [P] [US4] Add unit tests for agent attribution rendering in `extensions/vscode/test/suite/participantAttribution.test.ts`
- [x] T106 [P] [US4] Add unit tests for tools summary, suggestions rendering, follow-up prompt rendering in `extensions/vscode/test/suite/participantEnrichment.test.ts`
- [x] T107 [P] [US4] Add unit tests for compliance summary table rendering in `extensions/vscode/test/suite/participantComplianceSummary.test.ts`
- [x] T108 [P] [US4] Add unit tests for SSE streaming integration in chat participant in `extensions/vscode/test/suite/participantStreaming.test.ts`
- [x] T109 [US4] Run `cd extensions/vscode && npm test` — confirm all tests pass including new US4 tests

**Checkpoint**: VS Code chat participant shows agent attribution, tool summaries, suggestion buttons, follow-up prompts, compliance score tables, and streaming progress. All VS Code tests pass (SC-002).

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that span multiple user stories, final validation

- [x] T110 Verify `agentUsed` (not `agentName`) in all JSON responses across .NET and both TS extensions — grep for `agentName` in client code to ensure no references remain (SC-002)
- [x] T111 Verify M365 codebase contains zero references to cost, deployment, infrastructure, or resource card builders — `grep -r "costCard\|deploymentCard\|infrastructureCard\|resourceCard" extensions/m365/` returns nothing (SC-005)
- [x] T112 Run full .NET test suite: `dotnet build Ato.Copilot.sln && dotnet test --collect:"XPlat Code Coverage"` — confirm ≥2462 tests pass, zero warnings, ≥80% coverage on modified files (SC-009)
- [x] T113 Run full VS Code test suite: `cd extensions/vscode && npx nyc npm test` — confirm ≥61 tests pass, ≥80% coverage on modified files (SC-009)
- [x] T114 Run full M365 test suite: `cd extensions/m365 && npx nyc npm test` — confirm ≥32 tests pass, ≥80% coverage on modified files (SC-009)
- [x] T115 Run quickstart.md validation checklist — all items pass
- [x] T116 Verify SSE `/mcp/chat/stream` emits all 8 event types in correct order via manual test or integration test
- [x] T117 Verify error responses include structured `errorCode` and `suggestion` per Constitution VII (SC-008)
- [x] T118 [P] Code cleanup — remove any TODO/FIXME markers, verify XML documentation completeness on all new public types, naming conventions compliance (Constitution VI)

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1: Setup ──────────────────────┐
                                     ▼
Phase 2: Foundational ───────────────┐ (BLOCKS all user stories)
                                     ▼
Phase 3: US1 (P1 - Server) ─────────┐ (BLOCKS US2, US3, US4 — they depend on enriched responses)
                                     ▼
              ┌──────────────────────┼──────────────────────┐
              ▼                      ▼                      ▼
Phase 4: US2 (P2 - M365)   Phase 5: US3 (P2 - VS Code)   Phase 6: US4 (P3 - Chat)
              │                      │                      │
              └──────────────────────┼──────────────────────┘
                                     ▼
                         Phase 7: Polish & Validation
```

### User Story Dependencies

- **US1 (P1 — Server Enrichment)**: Depends on Foundational (Phase 2). No dependency on other stories. **BLOCKS US2, US3, US4** — all client stories consume the enriched server response.
- **US2 (P2 — M365 Cards)**: Depends on US1 being complete. Independent of US3 and US4.
- **US3 (P2 — VS Code Panel)**: Depends on US1 being complete. Independent of US2. Shares SSE client pattern with US4 but different files.
- **US4 (P3 — VS Code Chat)**: Depends on US1 being complete. Reuses SSE client from US3 (`sseClient.ts`), so US3 should complete first or T087 should be done before T104.

### Within Each User Story

1. Models/interfaces updated first
2. Core logic implementation
3. Integration with other components
4. Tests in parallel where files are independent
5. Final validation run

### Parallel Opportunities

**Phase 2**: T004 + T005 can be done together (same file but different sections); T009 is independent
**Phase 3 (US1)**: T020 (IaC tool) is independent of T012-T019; T022a-T022d (agent population) are parallel with each other (different agent files); T022e-T022f + T023-T028 (tests) are all parallel
**Phase 4 (US2)**: T031-T034 (delete cards) are parallel; T036-T045a (new card builders incl. kanban) are all parallel; T062-T071 (tests) are all parallel
**Phase 5 (US3)**: T092-T097 (tests) are all parallel
**Phase 6 (US4)**: T105-T108 (tests) are all parallel
**Cross-story**: US2 and US3 can proceed in parallel after US1 completes (different codebases)

---

## Parallel Example: User Story 2 (M365 Cards)

```bash
# After US1 is complete, launch in parallel batches:

# Batch 1 — Interface update + delete dead cards (4 parallel):
T030: Update McpResponse interface
T031-T034: Delete cost/deployment/infrastructure/resource cards

# Batch 2 — Create new card builders (11 parallel):
T036-T045a: All new card builders (incl. kanban board) can be created in parallel (separate files)

# Batch 3 — Routing and integration (sequential):
T046: Update card routing
T047-T052: Attribution, suggestions, update existing cards

# Batch 4 — Tests (11 parallel):
T062-T071: All test files are independent (incl. kanban board card tests)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup → verify baseline
2. Complete Phase 2: Foundational → models compile
3. Complete Phase 3: US1 → server returns enriched responses
4. **STOP and VALIDATE**: POST `/mcp/chat` returns `intentType`, `agentUsed`, `toolsExecuted`, `data`
5. This alone unblocks all client stories and proves the enrichment pipeline

### Incremental Delivery

1. **Setup + Foundational** → Foundation ready
2. **US1 (Server)** → Enriched API contract validated → **Deploy/Demo (MVP!)**
3. **US2 (M365) + US3 (VS Code Panel)** → Rich cards + enriched panel → **Deploy/Demo**
4. **US4 (VS Code Chat)** → Chat enrichment → **Deploy/Demo**
5. **Polish** → Final validation, cleanup → **Feature complete**
6. Each increment adds user value without breaking previous stories

### Task Count Summary

| Phase | Tasks | Parallel Tasks |
|-------|-------|----------------|
| Phase 1: Setup | 2 | 0 |
| Phase 2: Foundational | 9 | 0 |
| Phase 3: US1 (Server) | 24 | 13 |
| Phase 4: US2 (M365) | 49 | 30 |
| Phase 5: US3 (VS Code Panel) | 26 | 6 |
| Phase 6: US4 (VS Code Chat) | 11 | 4 |
| Phase 7: Polish | 9 | 1 |
| **Total** | **130** | **54** |

---

## Notes

- All [P] tasks target different files with no in-progress dependencies
- [US*] labels trace every task to its user story for FR traceability
- Tests use existing frameworks: xUnit + FluentAssertions + Moq (.NET), mocha + chai + sinon (TS)
- SSE client is shared between US3 and US4 via `extensions/vscode/src/services/sseClient.ts`
- PIM tools are already registered — no new MCP tool registration needed for PIM operations
- Commit after each task or logical batch; run tests at each checkpoint
