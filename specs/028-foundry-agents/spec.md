# Feature Specification: Azure AI Foundry Agent Integration

**Feature Branch**: `028-foundry-agents`  
**Created**: 2026-03-13  
**Status**: Draft  
**Input**: User description: "Add Azure AI Foundry Agents as a parallel AI processing path — replace direct Azure OpenAI chat completions with Foundry agent orchestration using server-side threads, runs, and tool dispatch while preserving the existing IChatClient fallback path and all deterministic tool routing."

## User Scenarios & Testing *(mandatory)*

<!--
  User stories prioritized as independent slices. Each delivers standalone value.
  P1 = foundational wiring & backend selection, P2 = core Foundry loop & resilience.
-->

### User Story 1 — Foundry Agent Client Wiring (Priority: P1)

As a platform operator, I want the Security Posture Intelligence Navigator to construct and register an Azure AI Foundry `PersistentAgentsClient` from configuration so that downstream agent features can use server-side Foundry agents for orchestrated AI processing.

**Why this priority**: This is the foundational wiring that all Foundry agent features depend on. Without a registered `PersistentAgentsClient`, no Foundry-based AI processing is possible. The existing `IChatClient` path (Feature 011) continues to work independently — this adds a parallel capability.

**Independent Test**: Can be tested by verifying that an `PersistentAgentsClient` instance resolves from DI when valid Foundry configuration is present (project endpoint), and resolves to null gracefully when configuration is missing. No downstream AI behavior required.

**Acceptance Scenarios**:

1. **Given** valid `AzureAi` configuration with `Provider=Foundry` and a `FoundryProjectEndpoint` in appsettings, **When** the application starts, **Then** a `PersistentAgentsClient` instance is registered in DI and resolvable by any service.
2. **Given** empty or missing `AzureAi` configuration (or `Provider` is not `Foundry`), **When** the application starts, **Then** `PersistentAgentsClient` resolves to null and all agents continue using the existing `IChatClient` path or deterministic tool routing — no startup errors.
3. **Given** an Azure Government project endpoint, **When** the client factory constructs the `PersistentAgentsClient`, **Then** it correctly targets the Azure Government AI Foundry endpoint.
4. **Given** both `AzureAi:FoundryProjectEndpoint` and `AzureAi:Endpoint` are configured, **When** the application starts, **Then** both `PersistentAgentsClient` and `IChatClient` are registered independently — the `AzureAi:Provider` value (`OpenAi` or `Foundry`) determines which path agents use at runtime.

---

### User Story 2 — Foundry Agent Provisioning (Priority: P1)

As the system starting up, I want each Security Posture Intelligence Navigator agent (Compliance, Configuration, KnowledgeBase) to have a corresponding Foundry agent created (or reused) in Azure AI Foundry with the correct system prompt, model deployment, and tool definitions — so that subsequent user messages can be routed to server-side agent instances.

**Why this priority**: Co-ranked P1 because Foundry agents must exist before any user message can be processed. Without provisioned agents, the thread/run API has nothing to target. This is the equivalent of Feature 011's `TryProcessWithAiAsync` tool-definition setup, but done once at startup rather than per-request.

**Independent Test**: Can be tested by starting the application with valid Foundry configuration and verifying that three Foundry agents are created (or existing ones reused by name) with the correct instructions and tool schemas. Can be validated through the Azure AI Foundry portal or API.

**Acceptance Scenarios**:

1. **Given** a valid `PersistentAgentsClient` and `AzureAi:Enabled=true` with `Provider=Foundry`, **When** the application starts, **Then** each concrete agent (Compliance, Configuration, KnowledgeBase) creates or retrieves a Foundry agent using its `AgentName` as a lookup key.
2. **Given** a Foundry agent with the same name already exists, **When** the system provisions agents, **Then** it reuses the existing agent (by listing agents and matching on name) rather than creating a duplicate.
3. **Given** an agent's `.prompt.txt` system prompt, **When** the Foundry agent is created, **Then** the prompt content is passed as the agent's `instructions` parameter.
4. **Given** an agent's registered `BaseTool` list, **When** the Foundry agent is created, **Then** each tool is converted to a `FunctionToolDefinition` with the correct name, description, and JSON Schema parameter definitions.
5. **Given** the system prompt or tool list changes (code update), **When** the application restarts, **Then** the existing Foundry agent is updated with the new instructions and tools.

---

### User Story 3 — Foundry Thread & Run Processing (Priority: P2)

As a user interacting with the Security Posture Intelligence Navigator, I want my messages to be processed through Azure AI Foundry's thread and run API — so that the Foundry agent selects tools, chains multi-step operations, and returns natural language responses, with conversation state managed server-side.

**Why this priority**: This is the core value of Foundry over direct OpenAI — server-side conversation threads and run orchestration. It replaces the manual tool-calling loop in `TryProcessWithAiAsync` with Foundry's managed run lifecycle.

**Independent Test**: Can be tested by providing a valid `PersistentAgentsClient` and Foundry agent ID to an agent, sending a user message, and verifying: (a) a thread is created or reused for the conversation, (b) a run is created, (c) when the run reaches `RequiresAction`, the agent executes the requested tool locally and submits results, (d) the final completed run's response message is returned as natural language.

**Acceptance Scenarios**:

1. **Given** an agent with `PersistentAgentsClient` configured and `AzureAi:Enabled=true` with `Provider=Foundry`, **When** the user sends a message, **Then** the agent creates a new thread (or reuses one mapped to the conversation ID), adds the user message, and creates a run against the provisioned Foundry agent.
2. **Given** a run reaches `RequiresAction` status with tool-call requests, **When** the agent processes the required action, **Then** it executes the named `BaseTool` with the provided parameters, submits the tool output back to the run, and resumes polling.
3. **Given** a run completes successfully, **When** the agent reads the response, **Then** it extracts the assistant's text message from the thread and returns it as the `AgentResponse.Response`.
4. **Given** a run fails or is cancelled, **When** the agent detects the terminal status, **Then** it falls back to the existing `IChatClient` path or deterministic tool routing and logs the error.
5. **Given** the maximum tool iterations (`MaxToolIterations`, configurable, default 5) are exceeded during run polling, **When** the limit is reached, **Then** the agent cancels the run and returns a summary in the `AgentResponse.Response` field: "Completed {N} of {M} tool calls before reaching the maximum iteration limit. Last tool executed: {toolName}." — followed by the last successful tool output if available.
6. **Given** ongoing conversation history, **When** the user sends a follow-up message, **Then** the message is added to the same Foundry thread so the server-side agent maintains full context continuity.

---

### User Story 4 — AI Provider Selection (Priority: P1)

As a platform operator, I want a configuration flag to choose between AI providers (direct Azure OpenAI via `IChatClient`, Azure AI Foundry Agents, or disabled) — so that I can switch providers without code changes and safely roll out Foundry agents incrementally.

**Why this priority**: Co-ranked P1 because the provider selection is the mechanism that controls which AI path agents use. Without it, both paths would compete or require code-level switching.

**Independent Test**: Can be tested by setting `AzureAi:Provider` to each value (`OpenAi`, `Foundry`) and `AzureAi:Enabled` to `true`/`false`, then verifying that agents use the correct processing path or fall back to deterministic routing.

**Acceptance Scenarios**:

1. **Given** `AzureAi:Enabled` is `false` (or `AzureAi` section is absent), **When** an agent processes a message, **Then** the agent uses deterministic tool routing regardless of whether `IChatClient` or `PersistentAgentsClient` are available.
2. **Given** `AzureAi:Enabled=true` and `Provider=OpenAi`, **When** an agent processes a message with `IChatClient` available, **Then** the agent uses the existing `TryProcessWithAiAsync` path from Feature 011.
3. **Given** `AzureAi:Enabled=true` and `Provider=Foundry`, **When** an agent processes a message with `PersistentAgentsClient` available, **Then** the agent uses the new `TryProcessWithFoundryAsync` path.
4. **Given** `AzureAi:Provider=Foundry` but `PersistentAgentsClient` is null, **When** an agent processes a message, **Then** the agent falls back to deterministic tool routing with a warning log.
5. **Given** the `Provider` value is changed in configuration, **When** the application restarts, **Then** agents switch to the new backend seamlessly.

---

### User Story 5 — Thread-to-Conversation Mapping (Priority: P2)

As a user with an ongoing chat conversation, I want my conversation to be mapped to a persistent Foundry thread — so that follow-up messages maintain context and the Foundry agent can reference prior exchanges.

**Why this priority**: Foundry's primary advantage over direct OpenAI is server-side thread management. Without proper mapping, every message creates a new thread and loses context continuity.

**Independent Test**: Can be tested by sending multiple messages in the same conversation and verifying that all messages are added to the same Foundry thread. Verify that a new conversation creates a new thread.

**Acceptance Scenarios**:

1. **Given** a new conversation (no prior Foundry thread), **When** the first message is processed via Foundry, **Then** a new thread is created and its ID is stored in the `BaseAgent._threadMap` (`ConcurrentDictionary<string, string>`, keyed by `ConversationId`).
2. **Given** an existing conversation with a mapped Foundry thread, **When** a follow-up message arrives, **Then** the message is added to the existing thread rather than creating a new one.
3. **Given** a conversation that was originally processed via the `OpenAi` provider, **When** the operator switches to `Foundry`, **Then** a new Foundry thread is created for subsequent messages in that conversation — no attempt to migrate history.
4. **Given** a thread ID stored in the `_threadMap`, **When** the MCP server restarts or the conversation session expires, **Then** the in-memory thread mapping is lost and orphaned Foundry threads expire naturally server-side.

---

### User Story 6 — Graceful Fallback & Degradation (Priority: P2)

As a platform operator, I want the system to continue functioning fully when Azure AI Foundry is unavailable, misconfigured, or experiencing errors — with automatic fallback to either the existing `IChatClient` path or deterministic tool routing.

**Why this priority**: Essential for an IL5/IL6 compliance system. Air-gapped environments, Azure outages, or Foundry service issues must not break the application. The fallback chain is: Foundry → IChatClient → deterministic routing.

**Independent Test**: Can be tested by running the application with no Foundry configuration and verifying all agents and tools function identically to pre-feature behavior.

**Acceptance Scenarios**:

1. **Given** no `AzureAi` configuration section (or `Enabled=false`), **When** agents process messages, **Then** all agents use the existing AI backend (IChatClient or deterministic) with no error logs at startup.
2. **Given** `PersistentAgentsClient` is available but a Foundry run fails (timeout, service error, quota exceeded), **When** the agent catches the exception, **Then** it falls back to `TryProcessWithAiAsync` (IChatClient) and logs the error. If `IChatClient` also fails, it falls back to deterministic tool routing.
3. **Given** the Foundry agent provisioning fails at startup (e.g., invalid project endpoint), **When** the application starts, **Then** it logs a warning and disables Foundry processing — `PersistentAgentsClient` resolves to null and agents use the fallback chain.
4. **Given** the system is running in degraded mode (no Foundry, no OpenAI), **When** the user interacts through chat, **Then** all existing functionality works identically to pre-Feature-011 behavior — zero regressions.
---

### Edge Cases

- What happens when the Foundry run enters an unexpected status (e.g., `expired`)? The agent treats any non-`completed`/`requires_action` terminal status as a failure and falls back.
- What happens when the Foundry agent's tool list changes but the server-side agent hasn't been updated? On startup, the system updates the existing Foundry agent with the current tool definitions — the agent is always synced.
- What happens when a tool name in a `RequiresAction` response doesn't match any registered `BaseTool`? The agent submits a tool error output to the run and lets the Foundry agent retry or respond with an explanation.
- What happens when the Foundry thread exceeds the model's context window? The Foundry service returns a run failure, which is caught and triggers fallback to the IChatClient path.
- What happens when multiple concurrent users share the same agent but need separate threads? Each conversation gets its own thread via the `ConversationId` → `ThreadId` mapping; threads are never shared across conversations.
- What happens when the project endpoint rotates or becomes invalid mid-session? Per-request errors are caught, logged, and trigger fallback. The `PersistentAgentsClient` registration at startup uses the project endpoint available at that time.

## Clarifications

### Session 2026-03-13

- Q: What data residency posture should Foundry threads adopt for IL5/IL6? → A: Commercial or Gov — Foundry threads permitted only when the Foundry project is in an authorized Azure Commercial or Government regions.
- Q: What authentication method should the Foundry PersistentAgentsClient use? → A: `DefaultAzureCredential` unconditionally — research (R-004) confirmed the Foundry SDK only supports `TokenCredential`, not API keys. The authority host is selected based on `AzureAi:CloudEnvironment` (AzureGovernment or AzureCloud).
- Q: What timeout behavior should apply to Foundry run polling? → A: 60-second overall timeout per run with 1-second polling interval; configurable via AzureAi:RunTimeoutSeconds.
- Q: Should the system clean up Foundry threads on restart or session expiration? → A: Let threads expire naturally on the Foundry side — no active cleanup. New threads are created for new conversations after restart.
- Q: Spec references deleted types (AzureFoundryOptions, AiBackend, AzureOpenAIGatewayOptions) and config paths (Gateway:AzureFoundry, Gateway:AiBackend). Should the spec be updated to reflect the unified AzureAiOptions / AiProvider model? → A: Yes — update all spec references to the unified model so the spec remains useful for onboarding and compliance documentation.
- Q: What observability strategy should the Foundry integration use for production monitoring? → A: Structured logging only — key lifecycle events (run created, tool dispatched, run completed/failed, fallback triggered) at appropriate log levels; full metrics/tracing deferred to a future observability feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST construct an Azure AI Foundry `PersistentAgentsClient` from `AzureAi:FoundryProjectEndpoint` configuration and register it as a singleton in DI. The client MUST use `DefaultAzureCredential` with the appropriate Azure authority host (Government or Commercial based on `AzureAi:CloudEnvironment`).
- **FR-002**: System MUST support Azure Commercial or Government AI Foundry projects for IL5/IL6 compliance. Foundry thread processing MUST only be permitted when the Foundry project endpoint targets an authorized Azure region.
- **FR-003**: System MUST skip `PersistentAgentsClient` registration gracefully when `AzureAi:FoundryProjectEndpoint` is missing or empty — no startup failures, no error logs.
- **FR-004**: `BaseAgent` MUST accept an optional `PersistentAgentsClient?` parameter alongside the existing optional `IChatClient?` parameter — neither MUST be required.
- **FR-005**: `BaseAgent` MUST provide a `TryProcessWithFoundryAsync` protected method that creates or reuses Foundry threads, adds user messages, creates runs, handles `RequiresAction` tool calls via local `BaseTool` execution, and returns the final assistant response — or returns null to trigger fallback.
- **FR-006**: The `RequiresAction` tool-call handling loop MUST be limited to the existing configurable `MaxToolIterations` (default 5) to prevent infinite polling.
- **FR-007**: The `AzureAi:Provider` configuration value (bound to the `AiProvider` enum) MUST control which AI processing path agents use: `OpenAi` (existing IChatClient path, default) or `Foundry` (new Foundry path). When `AzureAi:Enabled` is `false` (or absent), agents use deterministic tool routing only.
- **FR-008**: All 3 concrete agents (ComplianceAgent, ConfigurationAgent, KnowledgeBaseAgent) MUST have their constructors updated to accept optional `PersistentAgentsClient?` and pass it to `BaseAgent`.
- **FR-009**: Each agent MUST provision (or reuse) a Foundry agent at startup when `AzureAi:Enabled=true` and `Provider=Foundry` by calling `CreateAgentAsync` with the agent's system prompt as `instructions` and its tools converted to `FunctionToolDefinition` objects.
- **FR-010**: Tool schemas MUST be generated from the existing `BaseTool.Parameters` metadata — the same source used by Feature 011's `ToolAIFunction` — converted to Foundry's `FunctionToolDefinition` format.
- **FR-011**: Conversation-to-thread mapping MUST use a `ConcurrentDictionary<string, string>` on `BaseAgent` keyed by `AgentConversationContext.ConversationId`, storing the Foundry `threadId` for reuse across messages. The mapping is ephemeral (in-memory, process-lifetime).
- **FR-012**: The fallback chain when `Provider=Foundry` MUST be: Foundry run → IChatClient (`TryProcessWithAiAsync`) → deterministic tool routing.
- **FR-013**: When a Foundry run reaches `RequiresAction` with tool calls, the system MUST execute the named `BaseTool` locally and submit the result via `SubmitToolOutputsToRunAsync`.
- **FR-014**: When a Foundry run reaches a terminal failure status (`failed`, `cancelled`, `expired`), the system MUST log the error and return null to trigger the fallback chain.
- **FR-015**: The `Azure.AI.Agents.Persistent` NuGet package MUST be added to the Core project only — agents depend on the `PersistentAgentsClient` abstraction passed from DI.
- **FR-016**: All existing tests MUST continue passing with no modifications.
- **FR-017**: The existing Feature 011 `IChatClient` path (`TryProcessWithAiAsync`) MUST remain fully functional and unchanged — Foundry is an additive capability, not a replacement.
- **FR-018**: Foundry agent provisioning at startup MUST be idempotent — if an agent with the same name already exists, it MUST be reused and updated rather than duplicated.
- **FR-019**: The `ProcessAsync` method in each concrete agent MUST check `AzureAi:Provider` to determine the processing order: if `Foundry`, call `TryProcessWithFoundryAsync` first; if `OpenAi`, call `TryProcessWithAiAsync` first; if `Enabled=false`, skip both.
- **FR-020**: Foundry run polling MUST enforce a configurable overall timeout (default 60 seconds, via `AzureAi:RunTimeoutSeconds`) with a 1-second polling interval. When the timeout is exceeded, the run MUST be cancelled and the agent MUST fall back per FR-012.
- **FR-021**: The Foundry integration MUST emit structured log events at appropriate levels for key lifecycle events: run created (Information), tool dispatched (Debug), run completed (Information), run failed/timed out (Warning), and fallback triggered (Warning). Full OpenTelemetry metrics and distributed tracing are deferred to a future observability feature.

### Key Entities

- **AzureAiOptions** (unified configuration model): Bound from the top-level `AzureAi` configuration section. Contains Enabled (boolean, default false), Provider (`AiProvider` enum: `OpenAi` or `Foundry`, default `OpenAi`), Endpoint (Azure OpenAI endpoint URI), DeploymentName (default "gpt-4o"), ApiKey, UseManagedIdentity, CloudEnvironment, MaxCompletionTokens, MaxToolIterations, ConversationWindowSize, Temperature, FoundryProjectEndpoint (Foundry project URI), RunTimeoutSeconds (integer, default 60), and SystemPromptTemplate. Computed properties: `IsFoundry` (true when `Provider=Foundry` and `FoundryProjectEndpoint` is set), `IsConfigured` (true when `Endpoint` is set). Authentication for the Foundry client uses `DefaultAzureCredential` unconditionally (the Foundry SDK does not support API key auth). Authentication for the OpenAI client uses either `DefaultAzureCredential` (when `UseManagedIdentity=true`) or `AzureKeyCredential`.
- **AiProvider enum**: Enum with values `OpenAi` (0, default) and `Foundry` (1) — bound from `AzureAi:Provider` configuration. Controls which AI processing path agents use.
- **PersistentAgentsClient registration** (inline in `CoreServiceExtensions.AddAtoCopilotCore`): Conditional singleton registration that constructs `PersistentAgentsClient` from `AzureAi:FoundryProjectEndpoint` with `DefaultAzureCredential`. Skips when `FoundryProjectEndpoint` is empty.
- **Thread-to-Conversation mapping** (`ConcurrentDictionary<string, string>` on `BaseAgent`): Maps `ConversationId` → Foundry `threadId` for thread reuse. Mapping is ephemeral — lost on MCP restart. Orphaned Foundry threads expire naturally server-side; no active cleanup is performed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All existing unit and integration tests pass without modification after the feature is implemented.
- **SC-002**: When `Provider=Foundry` and a valid `PersistentAgentsClient` is available, agent responses are natural language text generated by the Foundry agent — not raw JSON tool output.
- **SC-003**: When `Enabled=false` or both AI clients are null, agent behavior is identical to pre-feature behavior — zero regressions.
- **SC-004**: Foundry agents are correctly provisioned at startup with the right system prompt and tool definitions — validated by verifying the agent exists in Foundry with matching `instructions` and `tools`.
- **SC-005**: Tool-call handling during `RequiresAction` completes within the configured maximum rounds (default 5) with no infinite polling loops.
- **SC-006**: System starts and runs without errors when no Foundry configuration is present.
- **SC-007**: At least 6 new unit tests cover the Foundry processing flow (PersistentAgentsClient registration, TryProcessWithFoundryAsync, thread mapping, tool dispatch during RequiresAction, fallback chain, AiProvider selection).
- **SC-008**: Conversations maintain context across multiple messages via Foundry thread reuse — the second message in a conversation references the same thread as the first.
- **SC-009**: The fallback chain (Foundry → IChatClient → deterministic) activates correctly when each layer fails — validated by integration tests that simulate failures.

## Assumptions

- The `Azure.AI.Agents.Persistent` NuGet package (v1.1.0 stable) provides the `PersistentAgentsClient` class with sub-clients `.Administration` (CreateAgentAsync), `.Threads` (CreateThreadAsync), `.Messages` (CreateMessageAsync, GetMessagesAsync), and `.Runs` (CreateRunAsync, GetRunAsync, SubmitToolOutputsToRunAsync). Note: project endpoint support was discontinued in v1.1.0 — the SDK requires a project endpoint URI + `TokenCredential`.
- Azure AI Foundry Agent Service is available in Azure Government regions for IL5/IL6 workloads. If it is not, this feature cannot be deployed in Gov environments and the `AzureOpenAI` backend remains the only AI option.
- Foundry threads store conversation data (including user messages, tool results, and compliance content) server-side. Because this data may contain CUI, Foundry is only permitted in authorized Azure regions per FR-002. Orphaned threads (from server restarts or session expiration) are left to expire naturally on the Foundry side — no active cleanup is performed.
- Foundry's `FunctionToolDefinition` accepts JSON Schema parameter definitions compatible with the schemas currently generated by `ToolAIFunction.BuildSchema()` from `BaseTool.Parameters`.
- Foundry threads persist server-side and are automatically managed for context — no client-side conversation history assembly is needed (unlike the `BuildChatContext` approach in Feature 011).
- The run polling model (`CreateRunAsync` → poll `GetRunAsync` until terminal) is acceptable for latency. Foundry also supports streaming via `CreateRunStreamingAsync` but that is a future enhancement.
- The existing `BaseTool.ExecuteAsync` method returns results that can be serialized as strings for `SubmitToolOutputsToRunAsync` — no changes to the tool execution interface are needed.
- The MCP server is the only process that provisions Foundry agents — the Chat project connects to MCP over HTTP and does not need its own `PersistentAgentsClient`.
- Foundry agent names are unique within a project — listing agents by name is sufficient for idempotent provisioning.
