# Tasks: KnowledgeBase Agent — "Compliance Library"

**Input**: Design documents from `/specs/010-knowledgebase-agent/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/mcp-tools.md, quickstart.md

**Tests**: Included per FR-028 — unit tests for all 7 tools, 7 services (including StigValidationService), orchestrator, query classification; integration tests through MCP layer.

**Organization**: Tasks grouped by user story for independent implementation and testing. 11 user stories (4×P1, 5×P2, 2×P3) across 12 phases.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US8)
- Exact file paths included in all descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create KnowledgeBase directory structure, configuration, and system prompt

- [x] T001 Create KnowledgeBase directory structure (Agents/, Configuration/, Data/, Prompts/, Services/, Tools/) and add `<Content Include="KnowledgeBase\Data\*.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>` glob in src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj
- [x] T002 [P] Create KnowledgeBaseAgentOptions configuration class with Enabled, MaxTokens, Temperature, ModelName, CacheDurationMinutes, KnowledgeBasePath, DefaultSubscriptionId, MinimumConfidenceThreshold properties in src/Ato.Copilot.Agents/KnowledgeBase/Configuration/KnowledgeBaseAgentOptions.cs
- [x] T003 [P] Create system prompt enforcing informational-only boundary (no scans, no assessments, educational content only, disclaimer requirement) in src/Ato.Copilot.Agents/KnowledgeBase/Prompts/KnowledgeBaseAgent.prompt.txt
- [x] T004 [P] Add AgentConfiguration:KnowledgeBaseAgent configuration section with default values to src/Ato.Copilot.Mcp/appsettings.json

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entity models, interface expansions, and BaseAgent CanHandle — MUST complete before any user story

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T005 [P] Add StigControl record, StigSeverity enum, and StigCrossReference record per data-model.md in src/Ato.Copilot.Core/Models/Compliance/StigModels.cs
- [x] T006 [P] Add RmfStep, RmfProcessData, ServiceGuidance, and DeliverableInfo records per data-model.md in src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs
- [x] T007 [P] Add DoDInstruction, ControlMapping, DoDWorkflow, and WorkflowStep records per data-model.md in src/Ato.Copilot.Core/Models/Compliance/DoDModels.cs
- [x] T008 [P] Add ImpactLevel, SecurityRequirements, and AzureImpactGuidance records per data-model.md in src/Ato.Copilot.Core/Models/Compliance/ImpactLevelModels.cs
- [x] T009 [P] Add FedRampTemplate, TemplateSection, FieldDefinition, and ChecklistItem records per data-model.md in src/Ato.Copilot.Core/Models/Compliance/FedRampModels.cs
- [x] T010 [P] Add KnowledgeQueryType enum and supporting types per data-model.md in src/Ato.Copilot.Core/Models/Compliance/KnowledgeQueryType.cs
- [x] T011 Expand IStigKnowledgeService, IRmfKnowledgeService, IDoDInstructionService, IDoDWorkflowService with new methods and add IImpactLevelService, IFedRampTemplateService interfaces per data-model.md in src/Ato.Copilot.Core/Interfaces/Compliance/IComplianceInterfaces.cs
- [x] T012 Add abstract double CanHandle(string message) method returning 0.0–1.0 confidence score to BaseAgent in src/Ato.Copilot.Agents/Common/BaseAgent.cs
- [x] T013 [P] Add CanHandle implementation with compliance/action keyword scoring (scan, assess, check, validate, run ≥0.8; compliance terms alone 0.4–0.6; default 0.2) to ComplianceAgent in src/Ato.Copilot.Agents/Compliance/Agents/ComplianceAgent.cs
- [x] T014 [P] Add CanHandle implementation with configuration keyword scoring (configure, set, subscription, framework, settings ≥0.8; default 0.0) to ConfigurationAgent in src/Ato.Copilot.Agents/Configuration/Agents/ConfigurationAgent.cs

**Checkpoint**: Foundation ready — all models, interfaces, and CanHandle contract in place. User story implementation can begin.

---

## Phase 3: User Story 8 — Orchestrator Routes Knowledge Queries (Priority: P1)

**Goal**: Replace hard-coded ClassifyAndRouteAgent with confidence-scored multi-agent orchestrator. Create KnowledgeBaseAgent skeleton with CanHandle and ProcessAsync.

**Independent Test**: Send "What is AC-2?" and verify KnowledgeBaseAgent is selected (via AgentId). Send "Scan my subscription" and verify ComplianceAgent is selected. Send unrelated query and verify graceful fallback.

### Tests for User Story 8

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T015 [P] [US8] Write unit tests for AgentOrchestrator (confidence scoring, threshold 0.3 fallback, tiebreaking by highest score, no-agent-above-threshold returns null, multi-agent selection with 3+ agents) in tests/Ato.Copilot.Tests.Unit/Orchestrator/AgentOrchestratorTests.cs
- [x] T016 [P] [US8] Write unit tests for KnowledgeBaseAgent (CanHandle scoring for knowledge keywords "explain"/"what is" ≥0.8, domain terms "nist"/"stig"/"rmf" boost, ProcessAsync routing, AnalyzeQueryType classification for all 8 KnowledgeQueryType values) in tests/Ato.Copilot.Tests.Unit/Agents/KnowledgeBaseAgentTests.cs

### Implementation for User Story 8

- [x] T017 [US8] Implement KnowledgeBaseAgent extending BaseAgent with CanHandle (knowledge-intent keyword scoring per research.md table), ProcessAsync skeleton (query classification → tool dispatch → response), GetSystemPrompt (load from prompt.txt), and AnalyzeQueryType method in src/Ato.Copilot.Agents/KnowledgeBase/Agents/KnowledgeBaseAgent.cs
- [x] T018 [US8] Implement AgentOrchestrator with IEnumerable\<BaseAgent\> DI discovery, SelectAgent evaluating all agents' CanHandle scores, configurable minimum threshold (default 0.3), and ILogger for routing decisions in src/Ato.Copilot.Mcp/Server/AgentOrchestrator.cs
- [x] T019 [US8] Add AddKnowledgeBaseAgent(IConfiguration) extension method registering KnowledgeBaseAgent as Singleton + BaseAgent alias, KnowledgeBaseAgentOptions binding, and all KB service implementations in src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs
- [x] T020 [US8] Replace ClassifyAndRouteAgent with AgentOrchestrator injection and SelectAgent call in ProcessChatRequestAsync, add graceful fallback for null selection in src/Ato.Copilot.Mcp/Server/McpServer.cs
- [x] T021 [US8] Register AgentOrchestrator as Singleton in DI container in src/Ato.Copilot.Mcp/Extensions/McpServiceExtensions.cs

**Checkpoint**: Multi-agent orchestrator operational. Knowledge queries route to KnowledgeBaseAgent, compliance queries to ComplianceAgent, unrecognized queries get graceful fallback.

---

## Phase 4: User Story 1 — Explain a NIST 800-53 Control (Priority: P1) 🎯 MVP

**Goal**: Users ask "What is AC-2?" and get a complete explanation with control statement, supplemental guidance, Azure implementation advice, related controls, and informational disclaimer.

**Independent Test**: Send "What is AC-2?" and verify response includes title "Account Management", control statement, Azure RBAC/Entra ID guidance, related controls, and disclaimer suffix.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T022 [P] [US1] Write unit tests for ExplainNistControlTool (control found with full response, enhancement fallback to base control, control not found with family suggestion, Azure family guidance mapping for AC/AU/IA/SC/CM/SI, cached result return, input normalization to uppercase, error handling) in tests/Ato.Copilot.Tests.Unit/Tools/ExplainNistControlToolTests.cs

### Implementation for User Story 1

- [x] T023 [P] [US1] Create nist-800-53-controls.json supplementary data file with curated control metadata (family descriptions, Azure service mappings) in src/Ato.Copilot.Agents/KnowledgeBase/Data/nist-800-53-controls.json
- [x] T024 [US1] Implement ExplainNistControlTool extending BaseTool with control_id parameter, INistControlsService lookup, enhancement fallback, Azure family guidance map per FR-023, related controls, informational disclaimer, and tool-level IMemoryCache caching with CacheDurationMinutes TTL (FR-026) in src/Ato.Copilot.Agents/KnowledgeBase/Tools/ExplainNistControlTool.cs
- [x] T025 [US1] Create KnowledgeBaseMcpTools class with kb_explain_nist_control handler method, add tool definition to ListToolsAsync and switch case to HandleToolCallAsync in McpServer in src/Ato.Copilot.Mcp/Tools/KnowledgeBaseMcpTools.cs and src/Ato.Copilot.Mcp/Server/McpServer.cs

**Checkpoint**: Users can explain any NIST control with Azure guidance. MVP complete — full request flow validated end-to-end (orchestrator → agent → tool → response).

---

## Phase 5: User Story 2 — Search NIST Controls by Keyword (Priority: P1)

**Goal**: Users search "Find controls related to encryption" and get relevant results with IDs, titles, and descriptions, optionally filtered by family.

**Independent Test**: Search "encryption" and verify SC-8, SC-12, SC-13 appear in results with titles and family names.

### Tests for User Story 2

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T026 [P] [US2] Write unit tests for SearchNistControlsTool (keyword match returns results, family filter limits to specified family, max_results default 10, max_results override, empty results for nonexistent term, error handling) in tests/Ato.Copilot.Tests.Unit/Tools/SearchNistControlsToolTests.cs

### Implementation for User Story 2

- [x] T027 [US2] Implement SearchNistControlsTool extending BaseTool with search_term (required), family (optional), max_results (optional, default 10) parameters delegating to INistControlsService.SearchControlsAsync in src/Ato.Copilot.Agents/KnowledgeBase/Tools/SearchNistControlsTool.cs
- [x] T028 [US2] Add kb_search_nist_controls handler to KnowledgeBaseMcpTools and switch case in McpServer.HandleToolCallAsync in src/Ato.Copilot.Mcp/Tools/KnowledgeBaseMcpTools.cs and src/Ato.Copilot.Mcp/Server/McpServer.cs

**Checkpoint**: NIST domain fully functional — explain and search capabilities operational.

---

## Phase 6: User Story 3 — Explain a STIG Control (Priority: P1)

**Goal**: Users ask "What is STIG V-12345?" and get severity, check/fix text, NIST control mappings, CCI references, and Azure implementation guidance.

**Independent Test**: Query STIG V-12345 and verify severity (High/CAT I), check text, fix text, NIST controls, CCI refs, and Azure guidance are returned.

### Tests for User Story 3

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T029 [P] [US3] Write unit tests for StigKnowledgeService (JSON loading from disk, IMemoryCache caching with 24h TTL, GetStigControlAsync found/not-found, SearchStigsAsync with severity filter, GetStigCrossReferenceAsync with DoD instruction enrichment, missing file returns null, malformed JSON returns null with logged error) in tests/Ato.Copilot.Tests.Unit/Services/StigKnowledgeServiceTests.cs
- [x] T030 [P] [US3] Write unit tests for ExplainStigTool (STIG found with severity/category formatting, NIST/CCI mapping display, Azure implementation details, STIG not found with suggestion, cross-reference enrichment, error handling) in tests/Ato.Copilot.Tests.Unit/Tools/ExplainStigToolTests.cs

### Implementation for User Story 3

- [x] T031 [P] [US3] Create stig-controls.json (STIG findings with severity, check/fix text, NIST mappings) and windows-server-stig-azure.json (Windows Server STIGs with Azure-specific implementation guidance) in src/Ato.Copilot.Agents/KnowledgeBase/Data/
- [x] T032 [US3] Replace StigKnowledgeService stub with full JSON-backed implementation using LoadDataFileAsync pattern, IMemoryCache 24h TTL, GetStigControlAsync, SearchStigsAsync, GetStigCrossReferenceAsync, preserving existing GetStigMappingAsync signature in src/Ato.Copilot.Agents/Compliance/Services/KnowledgeBase/StigKnowledgeService.cs
- [x] T033 [US3] Replace StigValidationService stub with enhanced implementation using real STIG-to-NIST control mappings from JSON data, preserving existing ValidateAsync signature for AtoComplianceEngine in src/Ato.Copilot.Agents/Compliance/Services/KnowledgeBase/StigValidationService.cs
- [x] T033b [P] [US3] Write unit tests for StigValidationService (ValidateAsync returns correct pass/fail with real mappings, preserves backward-compatible signature, handles missing STIG data gracefully) in tests/Ato.Copilot.Tests.Unit/Services/StigValidationServiceTests.cs
- [x] T034 [US3] Implement ExplainStigTool extending BaseTool with stig_id parameter, severity/category formatting, NIST/CCI display, Azure implementation guidance, and cross-reference enrichment via IStigKnowledgeService in src/Ato.Copilot.Agents/KnowledgeBase/Tools/ExplainStigTool.cs
- [x] T035 [US3] Add kb_explain_stig handler to KnowledgeBaseMcpTools and switch case in McpServer.HandleToolCallAsync in src/Ato.Copilot.Mcp/Tools/KnowledgeBaseMcpTools.cs and src/Ato.Copilot.Mcp/Server/McpServer.cs

**Checkpoint**: STIG domain operational with real data. Backward-compatible — IStigValidationService.ValidateAsync still works for AtoComplianceEngine.

---

## Phase 7: User Story 4 — Search STIGs by Severity or Keyword (Priority: P2)

**Goal**: Users search STIGs by keyword and/or severity with automatic normalization of severity inputs (high/cat1/cati → High).

**Independent Test**: Search "security" with severity "high" and verify only CAT I findings appear.

### Tests for User Story 4

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T036 [P] [US4] Write unit tests for SearchStigsTool (keyword search, severity normalization from "high"/"cat1"/"cati" → High / "medium"/"cat2"/"catii" → Medium / "low"/"cat3"/"catiii" → Low, combined keyword+severity filter, default max_results, empty results, error handling) in tests/Ato.Copilot.Tests.Unit/Tools/SearchStigsToolTests.cs

### Implementation for User Story 4

- [x] T037 [US4] Implement SearchStigsTool extending BaseTool with search_term (required), severity (optional with normalization), max_results (optional) parameters delegating to IStigKnowledgeService.SearchStigsAsync in src/Ato.Copilot.Agents/KnowledgeBase/Tools/SearchStigsTool.cs
- [x] T038 [US4] Add kb_search_stigs handler to KnowledgeBaseMcpTools and switch case in McpServer.HandleToolCallAsync in src/Ato.Copilot.Mcp/Tools/KnowledgeBaseMcpTools.cs and src/Ato.Copilot.Mcp/Server/McpServer.cs

**Checkpoint**: STIG domain fully functional — explain and search capabilities with severity filtering.

---

## Phase 8: User Story 5 — Explain the RMF Process (Priority: P2)

**Goal**: Users ask about the RMF process and get structured responses: full 6-step overview, specific step detail, service-specific guidance (Navy), or deliverables summary.

**Independent Test**: Ask "Explain the RMF process" and verify all 6 steps appear with titles, activities, outputs, and roles.

### Tests for User Story 5

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T039 [P] [US5] Write unit tests for RmfKnowledgeService (JSON loading, caching, GetRmfProcessAsync returns all 6 steps, GetRmfStepAsync valid step 1-6, GetRmfStepAsync invalid step returns null, GetServiceGuidanceAsync "navy" found, missing file handling) in tests/Ato.Copilot.Tests.Unit/Services/RmfKnowledgeServiceTests.cs
- [x] T040 [P] [US5] Write unit tests for DoDInstructionService (JSON loading, ExplainInstructionAsync found/not-found, GetInstructionsByControlAsync returns matching instructions, preserving GetInstructionAsync behavior, missing file handling) in tests/Ato.Copilot.Tests.Unit/Services/DoDInstructionServiceTests.cs
- [x] T041 [P] [US5] Write unit tests for DoDWorkflowService (JSON loading, GetWorkflowDetailAsync found/not-found, GetWorkflowsByOrganizationAsync filters by org, preserving GetWorkflowAsync behavior, missing file handling) in tests/Ato.Copilot.Tests.Unit/Services/DoDWorkflowServiceTests.cs
- [x] T042 [P] [US5] Write unit tests for ExplainRmfTool (full overview with no params, specific step number, service guidance topic "navy", deliverables aggregation, invalid step error, error handling) in tests/Ato.Copilot.Tests.Unit/Tools/ExplainRmfToolTests.cs

### Implementation for User Story 5

- [x] T043 [P] [US5] Create rmf-process.json (6 RMF steps with activities/outputs/roles), rmf-process-enhanced.json (service-specific guidance), dod-instructions.json (DoD Instructions with control mappings), and navy-workflows.json (Navy authorization workflows) in src/Ato.Copilot.Agents/KnowledgeBase/Data/
- [x] T044 [P] [US5] Replace RmfKnowledgeService stub with full JSON-backed implementation (GetRmfProcessAsync, GetRmfStepAsync, GetServiceGuidanceAsync), preserving existing GetGuidanceAsync signature, in src/Ato.Copilot.Agents/Compliance/Services/KnowledgeBase/RmfKnowledgeService.cs
- [x] T045 [P] [US5] Replace DoDInstructionService stub with full JSON-backed implementation (ExplainInstructionAsync, GetInstructionsByControlAsync), preserving existing GetInstructionAsync signature, in src/Ato.Copilot.Agents/Compliance/Services/KnowledgeBase/DoDInstructionService.cs
- [x] T046 [P] [US5] Replace DoDWorkflowService stub with full JSON-backed implementation (GetWorkflowDetailAsync, GetWorkflowsByOrganizationAsync), preserving existing GetWorkflowAsync signature, in src/Ato.Copilot.Agents/Compliance/Services/KnowledgeBase/DoDWorkflowService.cs
- [x] T047 [US5] Implement ExplainRmfTool extending BaseTool with optional topic and step parameters, routing to step-specific/service-specific/deliverables/full-overview handlers via IRmfKnowledgeService and IDoDWorkflowService in src/Ato.Copilot.Agents/KnowledgeBase/Tools/ExplainRmfTool.cs
- [x] T048 [US5] Add kb_explain_rmf handler to KnowledgeBaseMcpTools and switch case in McpServer.HandleToolCallAsync in src/Ato.Copilot.Mcp/Tools/KnowledgeBaseMcpTools.cs and src/Ato.Copilot.Mcp/Server/McpServer.cs

**Checkpoint**: RMF domain fully functional. All 3 DoD stub services replaced with real JSON-backed implementations. Backward-compatible with existing callers.

---

## Phase 9: User Story 6 — Explain DoD Impact Levels (Priority: P2)

**Goal**: Users ask "What is IL5?" and get data classification, security requirements, Azure guidance. "Compare impact levels" returns side-by-side comparison table.

**Independent Test**: Ask "What is IL5?" and verify encryption (FIPS 140-2 Level 1 minimum), network (dedicated connections, CAP required), and Azure region (Gov Secret regions) details.

### Tests for User Story 6

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T049 [P] [US6] Write unit tests for ImpactLevelService (JSON loading, GetImpactLevelAsync found/not-found, GetAllImpactLevelsAsync returns IL2-IL6, GetFedRampBaselineAsync, input normalization IL-5→IL5 / 5→IL5 / FEDRAMP-HIGH→FedRAMP-High, missing file handling) in tests/Ato.Copilot.Tests.Unit/Services/ImpactLevelServiceTests.cs
- [X] T050 [P] [US6] Write unit tests for ExplainImpactLevelTool (single level response, compare/all returns comparison table, FedRAMP baseline response, input normalization, level not found with suggestion, error handling) in tests/Ato.Copilot.Tests.Unit/Tools/ExplainImpactLevelToolTests.cs

### Implementation for User Story 6

- [X] T051 [P] [US6] Create impact-levels.json data file with IL2, IL4, IL5, IL6 and FedRAMP Low/Moderate/High baselines including data classification, security requirements, and Azure implementation guidance in src/Ato.Copilot.Agents/KnowledgeBase/Data/impact-levels.json
- [X] T052 [US6] Implement ImpactLevelService with JSON loading via LoadDataFileAsync pattern, level normalization, IMemoryCache 24h TTL, GetImpactLevelAsync, GetAllImpactLevelsAsync, GetFedRampBaselineAsync in src/Ato.Copilot.Agents/KnowledgeBase/Services/ImpactLevelService.cs
- [X] T053 [US6] Implement ExplainImpactLevelTool extending BaseTool with optional level parameter, routing to single-level/comparison/FedRAMP handlers, comparison table generation via IImpactLevelService in src/Ato.Copilot.Agents/KnowledgeBase/Tools/ExplainImpactLevelTool.cs
- [X] T054 [US6] Add kb_explain_impact_level handler to KnowledgeBaseMcpTools and switch case in McpServer.HandleToolCallAsync in src/Ato.Copilot.Mcp/Tools/KnowledgeBaseMcpTools.cs and src/Ato.Copilot.Mcp/Server/McpServer.cs

**Checkpoint**: Impact level domain operational with full IL2-IL6 and FedRAMP baseline support.

---

## Phase 10: User Story 7 — FedRAMP Template Guidance (Priority: P2)

**Goal**: Users ask "Show me POA&M template guidance" and get required fields, Azure discovery sources, and tracking options. SSP, POA&M, CRM, and package overview all handled.

**Independent Test**: Ask "Show POA&M template guidance" and verify required fields, Azure integration sources, and tracking options are returned.

### Tests for User Story 7

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T055 [P] [US7] Write unit tests for FedRampTemplateService (JSON loading, GetTemplateGuidanceAsync SSP/POAM/CRM found, template type normalization POA&M→POAM / CONMON→CRM, GetAllTemplatesAsync, baseline filter, missing file handling) in tests/Ato.Copilot.Tests.Unit/Services/FedRampTemplateServiceTests.cs
- [X] T056 [P] [US7] Write unit tests for GetFedRampTemplateGuidanceTool (SSP sections with Azure mappings, POAM fields with examples, CRM/CONMON requirements, package overview when no type, baseline filter, template not found, error handling) in tests/Ato.Copilot.Tests.Unit/Tools/GetFedRampTemplateGuidanceToolTests.cs

### Implementation for User Story 7

- [X] T057 [P] [US7] Create fedramp-templates.json data file with SSP sections/elements/Azure mappings, POAM field definitions/examples, CRM/continuous monitoring requirements, and authorization checklist in src/Ato.Copilot.Agents/KnowledgeBase/Data/fedramp-templates.json
- [X] T058 [US7] Implement FedRampTemplateService with JSON loading via LoadDataFileAsync pattern, template type normalization (POA&M→POAM, CONMON→CRM), IMemoryCache 24h TTL, GetTemplateGuidanceAsync, GetAllTemplatesAsync in src/Ato.Copilot.Agents/KnowledgeBase/Services/FedRampTemplateService.cs
- [X] T059 [US7] Implement GetFedRampTemplateGuidanceTool extending BaseTool with optional template_type and baseline parameters, routing to SSP/POAM/CRM/overview handlers via IFedRampTemplateService in src/Ato.Copilot.Agents/KnowledgeBase/Tools/GetFedRampTemplateGuidanceTool.cs
- [X] T060 [US7] Add kb_get_fedramp_template_guidance handler to KnowledgeBaseMcpTools and switch case in McpServer.HandleToolCallAsync in src/Ato.Copilot.Mcp/Tools/KnowledgeBaseMcpTools.cs and src/Ato.Copilot.Mcp/Server/McpServer.cs

**Checkpoint**: All 7 knowledge tools operational. FedRAMP domain complete. All P2 stories finished.

---

## Phase 11: User Story 9 + 11 — Cross-Agent Knowledge Sharing + Operation Metrics (Priority: P3)

**Goal**: (US9) Successful NIST/STIG queries store results in IAgentStateManager for cross-agent access. (US11) Every query tracks metadata: type, duration, success, operation counter.

**Independent Test**: Query "What is AC-2?", then verify IAgentStateManager contains `kb_last_nist_control` with query+result. Verify `last_operation`, `operation_count`, `last_query_duration_ms` state keys are set.

### Tests for User Story 9 + 11

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T061 [US9] [US11] Write unit tests for state sharing (kb_last_nist_control written on NIST success, kb_last_stig written on STIG success, no state written on failure) and query tracking (last_operation set to query type, operation_count increments by 1, last_query_success true/false, last_query_duration_ms reflects execution time, last_operation_at set to current timestamp), expanding existing test class in tests/Ato.Copilot.Tests.Unit/Agents/KnowledgeBaseAgentTests.cs

### Implementation for User Story 9 + 11

- [X] T062 [US9] Add cross-agent state sharing to ProcessAsync — store {query, result} under kb_last_nist_control and kb_last_stig keys via IAgentStateManager on successful queries only in src/Ato.Copilot.Agents/KnowledgeBase/Agents/KnowledgeBaseAgent.cs
- [X] T063 [US11] Add query tracking to ProcessAsync — record last_operation, last_operation_at, operation_count (increment), last_query, last_query_success, last_query_duration_ms via IAgentStateManager using Stopwatch timing in src/Ato.Copilot.Agents/KnowledgeBase/Agents/KnowledgeBaseAgent.cs

**Checkpoint**: All 11 user stories implemented. Cross-agent state and operational metrics functional.

---

## Phase 12: Polish & Cross-Cutting Concerns

**Purpose**: Integration tests, offline verification (US10), documentation, regression validation

- [X] T064 [P] Write integration tests for KnowledgeBase MCP tool endpoints (end-to-end request→orchestrator→agent→tool→response for all 7 kb_* tools) via WebApplicationFactory in tests/Ato.Copilot.Tests.Integration/KnowledgeBaseMcpToolEndpointTests.cs
- [X] T065 [P] Write integration tests for orchestrator routing through MCP layer (knowledge intent→KB agent, compliance intent→Compliance agent, unrecognized→graceful fallback, action keyword override) in tests/Ato.Copilot.Tests.Integration/OrchestratorRoutingIntegrationTests.cs
- [X] T066 [P] Write integration tests for offline operation per US10 (verify zero HTTP calls for data retrieval, all 9 JSON files loaded from disk, services function with no network, embedded NIST fallback works) in tests/Ato.Copilot.Tests.Integration/OfflineOperationTests.cs

> **US10 Note**: Offline operation (US10) is satisfied by construction — all 5 replacement services and 2 new services use `LoadDataFileAsync` from local disk (Phases 4–10) with no external HTTP calls. T066 validates this property via integration tests. No separate implementation tasks are required because every service task (T023, T031, T032, T033, T043–T046, T051, T052, T057, T058) inherently implements the offline-first design.
- [X] T067 [P] Create knowledgebase agent documentation covering capabilities, 7 tools, data file format, configuration options, and orchestrator routing in docs/knowledgebase.md
- [X] T070 [P] Add regression coverage that executes the crash-inducing RMF query through `KnowledgeBaseAgent` with a size-limited `IMemoryCache` and verifies a structured successful response (issues #632 and #696)
- [X] T068 Run full test suite (`dotnet test Ato.Copilot.sln`) and verify zero regressions against existing 2,000+ tests (SC-011)
- [X] T069 Run quickstart.md validation — build solution, execute KB-related unit tests, start MCP server, verify kb_explain_nist_control tool dispatch end-to-end

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **User Stories (Phases 3–11)**: All depend on Foundational phase completion
  - P1 stories must execute in order: **US8 → US1 → US2 → US3**
  - P2 stories: **US4** depends on US3 (StigKnowledgeService); **US5, US6, US7** depend on US8 only
  - P3 stories: **US9+US11** depend on all tool stories being complete
- **Polish (Phase 12)**: Depends on all user stories being complete

### User Story Dependencies

- **US8 (Orchestrator)**: Depends on Phase 2 only — MUST complete before any other story
- **US1 (Explain NIST)**: Depends on US8 (agent + MCP class creation)
- **US2 (Search NIST)**: Depends on US1 (KnowledgeBaseMcpTools class must exist)
- **US3 (Explain STIG)**: Depends on US8 — can parallel test writing with US1/US2 but McpServer changes are sequential
- **US4 (Search STIGs)**: Depends on US3 (StigKnowledgeService must exist)
- **US5 (Explain RMF)**: Depends on US8 only — independent of STIG/NIST stories
- **US6 (Impact Levels)**: Depends on US8 only — independent of other domain stories
- **US7 (FedRAMP)**: Depends on US8 only — independent of other domain stories
- **US9 + US11 (State/Metrics)**: Depend on all tool stories (need tools to track)

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- JSON data files before services (services load from files)
- Services before tools (tools delegate to services)
- Tools before MCP dispatch (dispatch calls tool handlers)
- Story complete before moving to next priority

### File Conflict Notes

- **KnowledgeBaseAgent.cs**: Created by US8, modified by US1–US7 (add tool registration), US9/US11 (add state logic) — sequential only
- **KnowledgeBaseMcpTools.cs**: Created by US1, modified by US2–US7 — sequential only
- **McpServer.cs**: Modified by US8 (orchestrator), US1–US7 (add switch cases) — sequential only
- **ServiceCollectionExtensions.cs**: Modified by US8 (AddKnowledgeBaseAgent), may need updates per story as new services are registered

### Parallel Opportunities

- **Phase 2**: All entity model files (T005–T010) can run in parallel — different files
- **Phase 2**: T013 + T014 (CanHandle for Compliance + Configuration) can run in parallel
- **Phase 3**: T015 + T016 (orchestrator + agent tests) can run in parallel — different test files
- **Phase 6**: T029 + T030 + T031 (STIG tests + data) can run in parallel — different files
- **Phase 8**: T039–T043 (RMF tests + data files) can all run in parallel — different files
- **Phase 8**: T044 + T045 + T046 (3 service replacements) can run in parallel — different files
- **Phase 9**: T049 + T050 + T051 (impact level tests + data) can run in parallel
- **Phase 10**: T055 + T056 + T057 (FedRAMP tests + data) can run in parallel
- **Phase 12**: T064–T067 (integration tests + docs) can all run in parallel

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Launch all entity models in parallel (6 tasks):
T005: "StigModels.cs"
T006: "RmfModels.cs"
T007: "DoDModels.cs"
T008: "ImpactLevelModels.cs"
T009: "FedRampModels.cs"
T010: "KnowledgeQueryType.cs"

# Then interface expansion (single file, sequential):
T011: "IComplianceInterfaces.cs"

# Then BaseAgent CanHandle (sequential):
T012: "BaseAgent.cs"

# Then existing agent CanHandle in parallel:
T013: "ComplianceAgent.cs"
T014: "ConfigurationAgent.cs"
```

## Parallel Example: User Story 5 (RMF)

```bash
# Launch all tests + data in parallel (5 tasks):
T039: "RmfKnowledgeServiceTests.cs"
T040: "DoDInstructionServiceTests.cs"
T041: "DoDWorkflowServiceTests.cs"
T042: "ExplainRmfToolTests.cs"
T043: "rmf-process.json, rmf-process-enhanced.json, dod-instructions.json, navy-workflows.json"

# Then launch all service replacements in parallel (3 tasks):
T044: "RmfKnowledgeService.cs"
T045: "DoDInstructionService.cs"
T046: "DoDWorkflowService.cs"

# Then sequentially:
T047: "ExplainRmfTool.cs" (depends on T044–T046)
T048: "Add MCP dispatch" (depends on T047)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (4 tasks)
2. Complete Phase 2: Foundational (10 tasks)
3. Complete Phase 3: US8 — Orchestrator (7 tasks)
4. Complete Phase 4: US1 — Explain NIST Control (4 tasks)
5. **STOP AND VALIDATE**: Send "What is AC-2?" → verify complete educational response
6. Total: **25 tasks to MVP**

### Incremental Delivery (P1 → P2 → P3)

1. **MVP** (Phases 1–4): Orchestrator + Explain NIST → Deploy/Demo
2. \+ Phase 5: Search NIST → NIST domain complete
3. \+ Phase 6: Explain STIG → Cross-domain (STIG→NIST mappings)
4. \+ Phase 7: Search STIGs → STIG domain complete
5. \+ Phases 8–10: RMF + Impact Levels + FedRAMP → All 7 tools complete
6. \+ Phase 11: State sharing + query metrics → Full feature
7. Phase 12: Polish + integration tests → Production-ready

### Parallel Team Strategy

With multiple developers after Phase 2 completion:

1. **Developer A**: US8 → US1 → US2 (Orchestrator + NIST domain)
2. **Developer B**: US3 → US4 (STIG domain, once US8 merge is available)
3. **Developer C**: US5 (RMF domain, once US8 merge is available)
4. **Developer D**: US6 + US7 (Impact + FedRAMP, once US8 merge is available)
5. All converge for US9+US11 and Polish

⚠️ **Note**: US1–US7 all modify KnowledgeBaseAgent.cs, KnowledgeBaseMcpTools.cs, and McpServer.cs. True parallelism requires merge coordination for these 3 shared files. Tests, data files, services, and tool implementations are fully parallelizable.

---

## Notes

- Total tasks: **70** (20 test tasks + 50 implementation tasks)
- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps each task to its user story for traceability
- Each user story is independently completable and testable at its checkpoint
- Verify tests FAIL before implementing
- Commit after each task or logical group
- Tool-level caching (FR-026): Each tool SHOULD cache results in `IMemoryCache` using `CacheDurationMinutes` (default 60 min) from `KnowledgeBaseAgentOptions`. Service-level data uses 24h TTL.
- Offline operation (US10) is by construction — all services load from local disk, validated by T066 integration tests
- Stop at any checkpoint to validate story independently
- All JSON data files are manually curated from authoritative DoD/NIST publications
