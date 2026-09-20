# Feature Specification: KnowledgeBase Agent — "Compliance Library"

**Feature Branch**: `010-knowledgebase-agent`  
**Created**: 2026-02-24  
**Status**: Draft  
**Input**: User description: "KnowledgeBase Agent - Compliance Library: always-available compliance education and reference service with 7 tools, 6 services, 9 JSON data files, offline IL6 support, multi-agent routing, and cross-agent knowledge sharing"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Explain a NIST 800-53 Control (Priority: P1)

A platform engineer building infrastructure templates asks "What is AC-2?" The system routes the query to the KnowledgeBase Agent (not the Compliance Agent), which looks up the control from the shared NIST controls catalog, formats a comprehensive explanation with the control statement, supplemental guidance, Azure-specific implementation recommendations, and related controls, then returns the educational response with a clear disclaimer that this is informational only.

**Why this priority**: NIST 800-53 control lookups are the most common compliance knowledge query. This validates the core request flow: orchestrator routing, tool selection, service call, response formatting, and the informational-only boundary.

**Independent Test**: Can be fully tested by sending "What is AC-2?" and verifying the response includes the control title, statement, guidance, Azure implementation advice, and the disclaimer suffix. Delivers immediate educational value without any other tool or domain.

**Acceptance Scenarios**:

1. **Given** the KnowledgeBase Agent is registered and the NIST catalog is loaded, **When** a user asks "What is AC-2?", **Then** the system routes to KnowledgeBase (not Compliance), and returns a markdown explanation containing the control title "Account Management," the control statement, supplemental guidance, Azure RBAC/Entra ID guidance, and the informational disclaimer.
2. **Given** a user asks about an enhancement control "IA-2(1)", **When** the enhancement exists in the catalog, **Then** the response includes enhancement-specific detail; if the enhancement is not found, the system falls back to the base control IA-2.
3. **Given** a user asks about a nonexistent control "ZZ-99", **When** no matching control is found, **Then** the response includes `success: false` and suggests valid control families.
4. **Given** a user previously asked "What is AC-2?", **When** the same query is repeated within the cache window, **Then** the cached result is returned without re-querying the service.

---

### User Story 2 — Search NIST Controls by Keyword (Priority: P1)

A compliance officer preparing for an assessment wants to find all controls related to "encryption." The agent performs a full-text search across the NIST 800-53 catalog, optionally filters by control family, limits results, and returns a summary list with control IDs, titles, and brief descriptions.

**Why this priority**: Search complements explain — together they cover the foundational NIST domain. Users frequently need to discover controls before they can ask for explanations.

**Independent Test**: Can be tested by sending "Find controls related to encryption" and verifying the response includes SC-8, SC-12, SC-13, SC-28 (or similar encryption-related controls) with IDs, titles, and descriptions.

**Acceptance Scenarios**:

1. **Given** the NIST catalog is loaded, **When** a user searches for "encryption", **Then** the response lists matching controls with ID, title, family, and brief description.
2. **Given** a search for "access" with family filter "AC", **When** the search executes, **Then** only controls in the AC family are returned.
3. **Given** a search returns more than 10 results, **When** no `max_results` parameter is provided, **Then** results are limited to the default of 10.

---

### User Story 3 — Explain a STIG Control (Priority: P1)

A security lead investigating a vulnerability scan result asks "What is STIG V-12345?" The agent looks up the STIG control from the embedded data, returns its severity (CAT I/II/III), description, check procedure, remediation steps, mapped NIST controls, CCI references, and Azure-specific implementation guidance including service, configuration, Azure Policy, and automation commands.

**Why this priority**: STIG lookups are critical for security teams interpreting scan results and planning remediation. The STIG domain includes cross-references to NIST controls and DoD Instructions, validating the cross-service architecture.

**Independent Test**: Can be tested by sending "Explain STIG V-12345" and verifying the response includes severity, check text, fix text, NIST mappings, and Azure implementation details.

**Acceptance Scenarios**:

1. **Given** the STIG data is loaded, **When** a user asks "What is STIG V-12345?", **Then** the response includes title, severity (e.g., "High — CAT I"), description, check procedure, remediation steps, NIST control mappings, and Azure implementation guidance.
2. **Given** a STIG has cross-references to DoD Instructions, **When** `GetStigCrossReferenceAsync` is called, **Then** the response includes the chain: STIG → NIST Controls → DoD Instructions.
3. **Given** a nonexistent STIG ID is queried, **When** no match is found, **Then** the response includes `success: false` with helpful suggestions.

---

### User Story 4 — Search STIGs by Severity or Keyword (Priority: P2)

A security lead asks "Find high-severity STIG controls" or "Search STIGs for Windows Server." The agent searches across STIG title, description, and category, optionally filtering by severity (CAT I/II/III) and limiting results.

**Why this priority**: Bulk STIG discovery supports security assessment workflows. Severity filtering is essential for prioritized remediation.

**Independent Test**: Can be tested by searching "security" with severity "high" and verifying only CAT I findings appear.

**Acceptance Scenarios**:

1. **Given** STIG data is loaded, **When** a user searches for "security" with severity "high", **Then** only CAT I (High) findings are returned.
2. **Given** severity input "cat2" or "catii", **When** the search normalizes severity, **Then** it maps to `StigSeverity.Medium` and returns CAT II results.
3. **Given** no severity filter, **When** searching "windows server", **Then** results include all matching STIGs regardless of severity.

---

### User Story 5 — Explain the RMF Process (Priority: P2)

A compliance officer new to the RMF asks "Explain the RMF process." The agent returns a structured overview of all 6 RMF steps with activities, deliverables, roles, and guidance. The officer can also ask about a specific step ("What happens in RMF Step 3?") or service-specific guidance ("Navy RMF guidance").

**Why this priority**: RMF process knowledge is foundational for ATO workflows. This domain supports the Compliance Agent by providing context for assessment steps.

**Independent Test**: Can be tested by asking "Explain the RMF process" and verifying all 6 steps are included with titles, activities, and deliverables.

**Acceptance Scenarios**:

1. **Given** RMF data files are loaded, **When** a user asks "Explain the RMF process" with no parameters, **Then** the response includes all 6 RMF steps with titles, activities, outputs, and roles.
2. **Given** a user asks "What happens in RMF Step 3?", **When** step "3" is provided, **Then** only Step 3 (Implement) detail is returned with specific activities and deliverables.
3. **Given** a user asks "Navy RMF guidance", **When** topic is "navy", **Then** Navy-specific contacts, requirements, timeline, and tools are returned.
4. **Given** topic is "deliverables", **When** the tool aggregates outputs, **Then** all deliverables organized by step are returned.

---

### User Story 6 — Explain DoD Impact Levels (Priority: P2)

A platform engineer asks "What is IL5?" to understand data classification, encryption requirements, network boundaries, personnel clearances, and Azure region restrictions for Impact Level 5. They can also ask "Compare impact levels" for a side-by-side comparison table.

**Why this priority**: Impact level decisions drive architecture choices and are prerequisite to environment setup. Comparison support reduces errors in classification.

> **Deferred**: Step-by-step migration guidance (e.g., "How to migrate from IL2 to IL5?") is deferred to a future feature once the ImpactLevelService has been validated with real users.

**Independent Test**: Can be tested by asking "What is IL5?" and verifying encryption (FIPS 140-2 Level 1 minimum), network (dedicated connections, CAP required), and Azure region (Gov Secret regions) details are returned.

**Acceptance Scenarios**:

1. **Given** impact level data is loaded, **When** a user asks "What is IL5?", **Then** the response includes data classification, security requirements (encryption, network, personnel), and Azure implementation guidance.
2. **Given** a user asks "Compare impact levels" or "all", **When** the comparison handler executes, **Then** a side-by-side table with IL2/IL4/IL5/IL6 across data classification, encryption, network, and personnel categories is returned.
3. **Given** level input "IL-5" or "5", **When** the input is normalized, **Then** it maps to "IL5" and returns the correct level.
4. **Given** a user asks about FedRAMP baselines ("What is FedRAMP High?"), **When** input "HIGH" or "FEDRAMP-HIGH" is provided, **Then** FedRAMP High baseline details are returned.

---

### User Story 7 — FedRAMP Template Guidance (Priority: P2)

A compliance officer preparing an authorization package asks "What's in a FedRAMP SSP?" The agent returns SSP section requirements, required elements, example content, Azure service mappings, and the authorization package checklist. Other queries cover POA&M field definitions, continuous monitoring requirements, and authorization pathways.

**Why this priority**: FedRAMP documentation is the primary deliverable for authorization. Template guidance reduces errors and accelerates package preparation.

**Independent Test**: Can be tested by asking "Show me POA&M template guidance" and verifying required fields, Azure discovery sources, and tracking options are returned.

**Acceptance Scenarios**:

1. **Given** FedRAMP data is loaded, **When** a user asks with template_type "SSP", **Then** the response includes SSP sections, required elements, Azure mappings, and the authorization checklist.
2. **Given** template_type "POAM" or "POA&M", **When** the tool routes to the POA&M handler, **Then** required fields with descriptions, examples, and Azure integration sources are returned.
3. **Given** template_type "CRM" or "CONMON", **When** the tool routes to continuous monitoring, **Then** requirements by frequency, Azure implementation tools, and deliverables are returned.
4. **Given** no template_type is specified, **When** the default handler executes, **Then** a package overview with document summaries is returned.

---

### User Story 8 — Orchestrator Routes Knowledge Queries Before Compliance (Priority: P1)

When a user asks "What is AC-2?" or "Explain the RMF process," the multi-agent orchestrator evaluates all registered agents via their `CanHandle(message)` confidence scores and routes to the highest-confidence match. The KnowledgeBase Agent returns high confidence for knowledge-intent keywords ("explain," "what is," "tell me about," "stig," "rmf," "nist," etc.), ensuring educational queries are not accidentally routed to the Compliance Agent for environment scanning.

**Why this priority**: Without correct routing, educational queries could trigger compliance scans, causing unnecessary Azure API calls, elevated permission requirements, and confusing responses. This is the architectural linchpin of multi-agent separation.

**Independent Test**: Can be tested by sending "What is AC-2?" and verifying the response comes from the KnowledgeBase Agent (identified by agent name in response), not the Compliance Agent.

**Acceptance Scenarios**:

1. **Given** both KnowledgeBase and Compliance agents are registered, **When** a user sends "What is AC-2?", **Then** the KnowledgeBase Agent returns a higher confidence score than Compliance, and the orchestrator routes to KnowledgeBase.
2. **Given** a user sends "Run a compliance assessment", **When** the Compliance Agent returns the highest confidence, **Then** the orchestrator routes to the Compliance Agent.
3. **Given** a user sends "Explain STIG V-12345", **When** the KnowledgeBase Agent scores highest for "explain" + "stig", **Then** the orchestrator routes to KnowledgeBase.
4. **Given** a user sends "Scan my subscription for AC-2 compliance", **When** the Compliance Agent scores higher due to action-intent keywords ("scan"), **Then** the orchestrator routes to Compliance despite "AC-2" being present.
5. **Given** a user sends an unrelated query like "What's the weather?", **When** no agent returns a confidence score above the minimum threshold (0.3), **Then** the orchestrator returns a graceful fallback response suggesting supported query types.

---

### User Story 9 — Cross-Agent Knowledge Sharing (Priority: P3)

After the KnowledgeBase Agent answers a NIST or STIG query, the result is stored in the agent state so that other agents can access the most recent knowledge lookup for a conversation. For example, the Compliance Agent can reference the last NIST control explanation when enriching assessment results.

**Why this priority**: Cross-agent sharing enables a coherent multi-agent experience but is not required for standalone knowledge queries.

**Independent Test**: Can be tested by querying "What is AC-2?" then verifying that `IAgentStateManager` contains the key `kb_last_nist_control` with the query and result for that conversation.

**Acceptance Scenarios**:

1. **Given** a NIST control query succeeds, **When** the response is returned, **Then** the agent stores `{ query, result }` under the key `kb_last_nist_control` in `IAgentStateManager`.
2. **Given** a STIG query succeeds, **When** the response is returned, **Then** the agent stores `{ query, result }` under the key `kb_last_stig`.
3. **Given** a query fails, **When** the error response is returned, **Then** no state key is written.

---

### User Story 10 — Offline Operation for IL6 Environments (Priority: P2)

In air-gapped IL6 environments with no internet connectivity, the KnowledgeBase Agent operates entirely from embedded JSON data files shipped with the build. No external HTTP calls are made for data retrieval. All 9 JSON data files and the NIST catalog are resolved from the application's base directory, ensuring the agent functions identically whether online or offline.

**Why this priority**: IL6 environments are fully disconnected. The agent must guarantee zero external network dependencies for data retrieval. This is a hard security requirement for classified environments.

**Independent Test**: Can be tested by disabling all network connectivity (or mocking HTTP to throw), sending "What is AC-2?", and verifying the response is returned successfully from embedded/local data.

**Acceptance Scenarios**:

1. **Given** the application is deployed in an IL6 environment with no internet, **When** any knowledge query is made, **Then** the agent resolves all data from local embedded JSON files without attempting external HTTP calls.
2. **Given** the NIST controls service has both online and offline loading paths, **When** the online path fails or is disabled, **Then** the embedded resource fallback loads the catalog successfully.
3. **Given** all 9 JSON data files are present in the build output, **When** the application starts, **Then** all knowledge services load their data from disk and cache it in memory.

---

### User Story 11 — Operation Metrics and Query Tracking (Priority: P3)

Every knowledge query is tracked with metadata: query type, raw query text, success/failure, execution duration, and a cumulative operation counter. This data supports operational monitoring and debugging without requiring external telemetry infrastructure.

**Why this priority**: Observability is important but not blocking for core functionality.

**Independent Test**: Can be tested by making a knowledge query and verifying the agent state contains `last_operation`, `last_operation_at`, `operation_count`, `last_query`, `last_query_success`, and `last_query_duration_ms`.

**Acceptance Scenarios**:

1. **Given** a knowledge query completes successfully, **When** the agent updates state, **Then** `last_query_success` is true, `last_query_duration_ms` reflects actual execution time, and `operation_count` increments by 1.
2. **Given** a knowledge query fails, **When** the agent updates state, **Then** `last_query_success` is false and the error is logged.

---

### Edge Cases

- What happens when a JSON data file is missing from the build output? The service logs a warning and returns null/empty results; the tool returns a JSON error response with `success: false`.
- What happens when JSON data is malformed? The service logs a deserialization error and returns null; the tool returns an error response.
- What happens when `IMemoryCache` evicts data during operation? The next query triggers a re-load from disk (not from network in offline mode).
- What happens when a user sends a query that partially matches both knowledge and compliance intent (e.g., "Scan my AC-2 controls")? Action keywords ("scan," "assess," "check my") take precedence over knowledge keywords, routing to Compliance.
- What happens when the KnowledgeBase Agent is disabled via configuration? The concrete agent is registered but not aliased as `BaseAgent`, so the orchestrator does not discover it and queries fall through to the Compliance Agent.
- What happens when a query matches no agent above the minimum confidence threshold (0.3)? The orchestrator returns a graceful "I'm not sure how to help with that" response with suggestions for supported query types, rather than routing to a low-confidence agent.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a `KnowledgeBaseAgent` class extending `BaseAgent` with agent ID `knowledgebase`, registering 7 tools, and implementing `ProcessAsync` with query classification, tool dispatch, state tracking, and error handling.
- **FR-002**: System MUST provide an `explain_nist_control` tool that accepts a `control_id` parameter, normalizes it to uppercase, resolves the control via `INistControlsService`, formats a markdown response with control statement, supplemental guidance, Azure implementation guidance by family, and related controls, and appends an informational-only disclaimer.
- **FR-003**: System MUST provide a `search_nist_controls` tool that accepts `search_term` (required), `family` (optional), and `max_results` (optional, default 10) parameters, performs full-text search via `INistControlsService`, filters by family if provided, limits results, and returns control summaries.
- **FR-004**: System MUST provide an `explain_stig` tool that accepts a `stig_id` parameter, resolves the STIG control via `IStigKnowledgeService`, and returns severity, description, check procedure, remediation steps, NIST mappings, CCI references, and Azure implementation guidance.
- **FR-005**: System MUST provide a `search_stigs` tool that accepts `search_term` (required), `severity` (optional, normalized from "high/cat1/cati" to High, "medium/cat2/catii" to Medium, "low/cat3/catiii" to Low), and `max_results` (optional), and returns matching STIG controls.
- **FR-006**: System MUST provide an `explain_rmf` tool that accepts optional `topic` and `step` parameters, routes to step-specific, service-specific, deliverables, or full-overview handlers as appropriate, and returns RMF step details with activities, outputs, and roles.
- **FR-007**: System MUST provide an `explain_impact_level` tool that accepts an optional `level` parameter, normalizes input (IL2/IL-2/2 to IL2, FEDRAMP-HIGH/HIGH to FedRAMP-High, COMPARE/ALL to comparison), and returns impact level details including data classification, security requirements, and Azure implementation guidance.
- **FR-008**: System MUST provide a `get_fedramp_template_guidance` tool that accepts optional `template_type` (SSP, POA&M/POAM, CRM/CONMON, overview) and `baseline` (default: "High") parameters, routing to the appropriate handler and returning structured template guidance.
- **FR-009**: System MUST replace the 5 existing stub services (`RmfKnowledgeService`, `StigKnowledgeService`, `StigValidationService`, `DoDInstructionService`, `DoDWorkflowService`) with full implementations backed by JSON data files, while maintaining the existing interface contracts consumed by the Compliance Agent.
- **FR-010**: System MUST expand the existing interfaces (`IRmfKnowledgeService`, `IStigKnowledgeService`, `IDoDInstructionService`, `IDoDWorkflowService`) to include the additional methods required by the KnowledgeBase tools (e.g., `ExplainRmfProcessAsync`, `GetStigControlAsync`, `SearchStigsAsync`, `GetStigCrossReferenceAsync`, `GetInstructionsByControlAsync`, `ExplainInstructionAsync`, etc.) while maintaining backward compatibility with existing method signatures.
- **FR-011**: System MUST add two new interfaces (`IImpactLevelService` and `IFedRampTemplateService`) with their full service implementations backed by JSON data files.
- **FR-012**: System MUST maintain the existing `IStigValidationService` interface and enhance its `StigValidationService` implementation with real STIG-to-NIST control mappings from the JSON data files, replacing the current empty-list stub.
- **FR-013**: System MUST load all JSON data files from `Path.Combine(AppContext.BaseDirectory, "KnowledgeBase/Data/{file}.json")`, cache them in `IMemoryCache` with a 24-hour TTL, and gracefully handle missing or malformed files by returning null/empty results.
- **FR-014**: System MUST operate fully offline by resolving all compliance knowledge from embedded or local JSON data files without making any external HTTP requests for data retrieval. When online resources are configured, the system MUST fall back to local data if the online source is unreachable.
- **FR-015**: System MUST introduce a multi-agent orchestrator where each agent extends `BaseAgent`, which defines `abstract double CanHandle(string message)` returning a confidence score (0.0–1.0). The orchestrator discovers all `BaseAgent` registrations via DI, evaluates each agent's `CanHandle` score, and routes to the highest-confidence match. If no agent returns a score above the configurable minimum threshold (default: 0.3), the orchestrator MUST return a graceful "I'm not sure how to help with that" response instead of routing to a low-confidence agent. Knowledge-intent patterns (e.g., "explain," "what is," "tell me about," "stig," "rmf," "nist," "cci," "impact level," "fedramp") MUST score higher than compliance patterns for educational queries, ensuring they are not accidentally routed to the Compliance Agent. _(See also FR-027 for the `CanHandle` addition to `BaseAgent`.)_
- **FR-016**: System MUST register the `KnowledgeBaseAgent` and all supporting services and tools in the DI container via a new `AddKnowledgeBaseAgent()` extension method, called from the existing composition root.
- **FR-017**: System MUST expose all 7 KnowledgeBase tools via MCP protocol through a new `KnowledgeBaseMcpTools` class with `kb_*` prefixed tool IDs, registered in `McpServiceExtensions` and dispatched from `McpServer`.
- **FR-018**: System MUST classify incoming queries using keyword and regex matching (`AnalyzeQueryType`) into categories: `nist_control`, `stig`, `rmf`, `impact_level`, `fedramp`, `nist_search`, `stig_search`, `general_knowledge`.
- **FR-019**: System MUST track query metadata in `IAgentStateManager`: `last_operation`, `last_operation_at`, `operation_count`, `last_query`, `last_query_success`, `last_query_duration_ms`.
- **FR-020**: System MUST share NIST and STIG query results via `IAgentStateManager` under keys `kb_last_nist_control` and `kb_last_stig` so other agents can access the most recent knowledge lookup for a conversation.
- **FR-021**: System MUST provide a `KnowledgeBaseAgentOptions` configuration class bound to `AgentConfiguration:KnowledgeBaseAgent` with properties for: `Enabled`, `MaxTokens`, `Temperature`, `ModelName`, `CacheDurationMinutes`, `KnowledgeBasePath`, `DefaultSubscriptionId`, and offline-mode settings.
- **FR-022**: System MUST load its system prompt from an embedded resource file (`KnowledgeBaseAgent.prompt.txt`) that enforces the informational-only boundary: the agent provides knowledge and explanations but does NOT scan environments, run assessments, or modify resources.
- **FR-023**: System MUST provide hardcoded Azure implementation guidance mapped by NIST control family (AC to RBAC/Entra ID, AU to Azure Monitor, IA to MFA/managed identities, SC to Firewall/NSGs/Key Vault, CM to Azure Policy, SI to Defender for Cloud, etc.) in the `explain_nist_control` tool.
- **FR-024**: System MUST include 9 manually curated JSON data files (nist-800-53-controls.json, stig-controls.json, windows-server-stig-azure.json, rmf-process.json, rmf-process-enhanced.json, impact-levels.json, fedramp-templates.json, dod-instructions.json, navy-workflows.json) authored from authoritative DoD/NIST publications and committed as static content files in the repository. Files are copied to the build output directory and loaded at runtime. No build-time code generation is involved.
- **FR-025**: System MUST handle tool-level errors by returning JSON with `success: false` and contextual error/suggestion information, and agent-level errors by logging the failure and returning `AgentResponse { Success = false }`.
- **FR-026**: System MUST cache tool-level results using `CacheDurationMinutes` from configuration (default: 60 minutes) and service-level data using a 24-hour TTL in `IMemoryCache`. Every cache entry MUST declare a size so KnowledgeBase queries remain compatible with the production size-limited cache.
- **FR-027**: System MUST add `abstract double CanHandle(string message)` to `BaseAgent`. Existing agents (`ComplianceAgent`, `ConfigurationAgent`) MUST each receive a `CanHandle` implementation with keyword-based scoring appropriate to their domain, preserving current routing behavior under the new orchestrator. _(See also FR-015 for the orchestrator that consumes `CanHandle`.)_
- **FR-028**: System MUST include comprehensive test coverage: unit tests for all 7 tools (input normalization, error paths, response formatting), all 7 services (data loading, caching, search, missing/malformed file handling — including `StigValidationService`), orchestrator routing (confidence scoring, threshold fallback, multi-agent tiebreaking), query classification (`AnalyzeQueryType` for all categories), and integration tests verifying end-to-end query routing through the MCP layer (request → orchestrator → agent → tool → response). Manual/exploratory test scenarios are covered by the integration test suite via `WebApplicationFactory`.

### Key Entities

- **StigControl**: Represents a STIG finding with `StigId`, `VulnId`, `RuleId`, `Title`, `Description`, `Severity` (High/Medium/Low), `Category`, `StigFamily`, `NistControls` (list), `CciRefs` (list), `CheckText`, `FixText`, `AzureImplementation` (dictionary), `ServiceType`.
- **RmfProcessData**: Contains a `List<RmfStep>` where each `RmfStep` has `Step` (1-6), `Title`, `Description`, `Activities` (list), `Outputs` (list), `Roles` (list), `DodInstruction`. Aligns with the entity defined in [data-model.md](data-model.md).
- **DoDInstruction**: Represents a DoD Instruction with `InstructionId`, `Title`, `Description`, `PublicationDate`, `Applicability`, `Url`, `RelatedNistControls` (list), `RelatedStigIds` (list), `ControlMappings` (list).
- **DoDWorkflow**: Represents a DoD authorization workflow with `WorkflowId`, `Name`, `Organization`, `ImpactLevel`, `Description`, `Steps` (list), `RequiredDocuments` (list), `ApprovalAuthorities` (list).
- **ImpactLevel**: Represents a DoD Impact Level (IL2-IL6) or FedRAMP baseline with data classification, security requirements (encryption, network, personnel), Azure implementation guidance, and additional required controls.
- **FedRampTemplate**: Represents FedRAMP authorization package content including SSP sections, control narrative templates, POA&M fields, continuous monitoring requirements, and authorization package checklists.
- **KnowledgeBaseAgentOptions**: Configuration entity with properties governing agent behavior, caching, model settings, and feature flags.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can ask "What is [any NIST control ID]?" and receive a complete, correctly formatted educational response within 2 seconds for cached data and within 5 seconds for first-time lookups. Cold lookups are within the MCP tool timeout threshold (Constitution VIII) and do not require a progress indicator since they complete without user-perceived delay beyond initial load.
- **SC-002**: Users can search NIST controls by keyword and receive results ranked by match quality, with the top result's title or control statement containing the search term in 90%+ of queries against the embedded NIST catalog.
- **SC-003**: Users can ask about any STIG control by ID and receive severity, check procedure, remediation steps, NIST mappings, and Azure implementation guidance in a single response.
- **SC-004**: Users can search STIGs by keyword and/or severity and receive filtered results, with severity normalization correctly mapping all input variations (high/cat1/cati, medium/cat2/catii, low/cat3/catiii).
- **SC-005**: Users can request RMF process explanations (full overview, specific step, service-specific, deliverables) and receive structured, accurate responses for all 6 RMF steps.
- **SC-006**: Users can query any DoD Impact Level (IL2-IL6) or FedRAMP baseline and receive data classification, encryption, network, personnel, and Azure requirements. Side-by-side comparison is available for all levels.
- **SC-007**: Users can request FedRAMP template guidance (SSP, POA&M, continuous monitoring, overview) and receive structured requirements, field definitions, and Azure service mappings.
- **SC-008**: The orchestrator correctly routes 95%+ of knowledge-intent queries to the KnowledgeBase Agent and 95%+ of action-intent queries to the Compliance Agent, validated against a test corpus of 20 knowledge-intent and 20 action-intent phrases defined in OrchestratorRoutingTests, with zero cases of educational queries triggering environment scans.
- **SC-009**: The agent operates fully offline in IL6 environments — all 9 JSON data files and the NIST catalog are resolvable from local storage with no external network calls for data retrieval.
- **SC-010**: All 5 existing stub services are replaced without breaking backward compatibility — the Compliance Agent and Remediation Engine continue to consume `IRmfKnowledgeService`, `IStigKnowledgeService`, `IDoDInstructionService`, `IDoDWorkflowService`, and `IStigValidationService` with identical method signatures and improved data quality.
- **SC-011**: All existing unit tests (2,000+) continue to pass after the KnowledgeBase Agent is integrated, confirming zero regressions in Compliance and Remediation functionality.
- **SC-012**: Query tracking records accurate metadata (type, duration, success) for every knowledge operation, with duration measured via `Stopwatch` wall-clock time from tool entry to exit.
- **SC-013**: New unit tests cover all 7 tools, all 7 services (5 replacements + 2 new), orchestrator routing, and query classification. Integration tests verify end-to-end MCP request routing. Combined new test count targets 150+ tests.

## Assumptions

- The existing `INistControlsService` implementation (603 lines with online/offline fallback and OSCAL parsing) is the authoritative NIST catalog source. The KnowledgeBase Agent consumes it rather than providing a competing implementation.
- The `ISharedMemory` interface referenced in the feature description does not exist in the current codebase. Cross-agent knowledge sharing will use the existing `IAgentStateManager` pattern with agent-scoped keys (e.g., `knowledgebase:kb_last_nist_control`).
- The `IChannelManager` and `IStreamingHandler` interfaces referenced in the feature description do not exist. Channel notifications will be omitted from the initial implementation and can be added when the Channels system is built.
- The `PlatformSelectionStrategy` referenced in the feature description does not exist. A new multi-agent orchestrator will be introduced that discovers all `BaseAgent` registrations via DI. `BaseAgent` gains an `abstract double CanHandle(string message)` method so every agent must provide confidence scoring. The orchestrator replaces the current `McpServer.ClassifyAndRouteAgent()` if/else routing.
- DI lifetime will follow the existing codebase pattern (`Singleton`) rather than the `Scoped` lifetime described in the feature description, since all existing agents and services use Singleton registration.
- The feature description references RAG/Semantic Search configuration properties that have no implementation. These config properties will be included in `KnowledgeBaseAgentOptions` for forward compatibility but will not be wired to any service in this feature.
- The `IComplianceKnowledgeBaseService` unified facade described in the feature description is deferred — it can be added in a future feature once the individual services are proven.
- JSON data files are manually curated from authoritative DoD/NIST publications, committed to the repository as static content files, and included in the build output (Copy to Output Directory) via `Path.Combine(AppContext.BaseDirectory, ...)`. No build-time code generation or OSCAL/XCCDF parsing pipeline is required.
- The NIST controls JSON data file (`nist-800-53-controls.json`) referenced in the feature description will provide supplementary data. The primary NIST catalog will continue to be served by the existing `NistControlsService` and its embedded OSCAL resource.

## Clarifications

### Session 2026-02-24

- Q: Multi-agent orchestration model — extend current keyword if/else, introduce IAgentRouter strategy, or full multi-agent orchestrator with DI discovery and confidence scoring? → A: Option C — Full multi-agent orchestrator with DI-discovered agents and confidence-scored `CanHandle` routing.
- Q: JSON data file sourcing strategy — generate programmatically from OSCAL/XCCDF at build time, manually curate from authoritative sources as static files, or minimal seed data now? → A: Option B — Manually curated JSON files authored from authoritative DoD/NIST sources, committed as static content files and copied to build output.
- Q: CanHandle confidence-score threshold and fallback when no agent scores high enough — always route to highest scorer, minimum threshold with graceful rejection, or default to a catch-all agent? → A: Option A — Minimum confidence threshold (e.g., 0.3); below it, return a graceful "I'm not sure how to help with that" response instead of forcing a low-confidence route.
- Q: CanHandle placement — abstract method on BaseAgent or separate IRoutableAgent interface? → A: Option A — Add `abstract double CanHandle(string message)` to `BaseAgent` so all agents must implement it, ensuring every registered agent participates in orchestrator routing.
- Q: Unit test coverage scope for new KnowledgeBase code — tools only, tools+services+orchestrator, or full including integration? → A: Option C — Unit tests for all 7 tools, all 6 services, orchestrator routing with confidence scoring, query classification, plus integration tests verifying end-to-end query routing through the MCP layer.

## Dependencies

- **INistControlsService** (existing, Compliance domain): Provides the authoritative NIST 800-53 control catalog for the `explain_nist_control` and `search_nist_controls` tools.
- **IAgentStateManager** (existing, State project): Provides per-agent state storage for query tracking and cross-agent knowledge sharing.
- **IMemoryCache** (Microsoft.Extensions.Caching.Memory): Provides caching for JSON data files and tool results.
- **BaseAgent / BaseTool** (existing, Common): Abstract base classes that the KnowledgeBase Agent and tools extend.
- **McpServer** (existing, MCP project): Orchestrator that must be updated to route knowledge-intent queries to the new agent.
- **ComplianceAgent** and **AtoRemediationEngine** (existing consumers): Currently consume the 5 stub KB services; must continue working with the full implementations.
