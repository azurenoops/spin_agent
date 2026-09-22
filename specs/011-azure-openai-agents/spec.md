# Feature Specification: Add Azure OpenAI to Security Posture Intelligence Navigator Agents

**Feature Branch**: `011-azure-openai-agents`  
**Created**: 2026-02-25  
**Status**: Draft  
**Input**: User description: "Add Azure OpenAI to Security Posture Intelligence Navigator Agents — wire up Azure OpenAI client, make agents AI-powered conversational entities with LLM-backed tool selection and natural language responses, integrate with ChatHub, enhance system prompts, add feature flags and configuration."

## User Scenarios & Testing *(mandatory)*

<!--
  User stories prioritized as independent slices. Each delivers standalone value.
  P1 = foundational wiring, P2 = core AI loop, P3 = streaming/UX, P4 = polish.
-->

### User Story 1 — Azure OpenAI Client Wiring (Priority: P1)

As a platform operator, I want the Security Posture Intelligence Navigator to construct and register a real Azure OpenAI client from configuration so that downstream AI features have a functioning LLM connection available through dependency injection.

**Why this priority**: This is the foundational wiring that all other AI features depend on. Without a registered `IChatClient`, no agent-level AI, no LLM routing, and no conversational responses are possible. It also immediately activates the existing `AiRemediationPlanGenerator` which already accepts `IChatClient?` but currently always receives null.

**Independent Test**: Can be tested by verifying that an `IChatClient` instance resolves from DI when valid Azure OpenAI configuration is present, and returns null gracefully when configuration is missing. No downstream AI behavior is required to validate this story.

**Acceptance Scenarios**:

1. **Given** valid `AzureOpenAI` configuration (endpoint, API key, deployment name) in appsettings, **When** the application starts, **Then** an `IChatClient` instance is registered in DI and resolvable by any service that accepts it.
2. **Given** empty or missing `AzureOpenAI` configuration, **When** the application starts, **Then** `IChatClient` resolves to null and all agents fall back to current direct-tool-execution behavior with no errors.
3. **Given** an Azure Government endpoint (`.us` suffix), **When** the client factory constructs the client, **Then** it correctly targets the Azure Government OpenAI endpoint.
4. **Given** a functioning `IChatClient` in DI, **When** the `AiRemediationPlanGenerator` is resolved, **Then** it receives the non-null client and its `IsAvailable` property returns true.

---

### User Story 2 — Agent-Level AI Processing (Priority: P2)

As a user interacting with the Security Posture Intelligence Navigator, I want agents to use an LLM to understand my intent, select the right tool(s), and generate natural language responses that interpret tool results — instead of returning raw JSON tool output.

**Why this priority**: This is the core value proposition. Without LLM-powered processing, agents are deterministic keyword matchers that dump raw data. This story transforms agents from dispatch layers into intelligent conversational assistants.

**Independent Test**: Can be tested by providing a mock `IChatClient` to BaseAgent, calling `TryProcessWithAiAsync` with a user message, and verifying: (a) the system prompt is sent to the LLM, (b) the LLM's tool-call requests trigger actual tool execution, (c) tool results are fed back to the LLM, and (d) the LLM's final text response is returned rather than raw tool JSON.

**Acceptance Scenarios**:

1. **Given** an agent with `IChatClient` configured and `AgentAIEnabled=true`, **When** the user sends a message, **Then** the agent builds a chat context with its system prompt, conversation history, and the user message, and sends it to the LLM along with tool function definitions.
2. **Given** the LLM responds with a tool-call request, **When** the agent processes the response, **Then** it executes the named tool with the LLM-provided parameters and sends the tool result back to the LLM for interpretation.
3. **Given** the LLM chains multiple tool calls (up to the configured maximum), **When** each tool call completes, **Then** the agent executes them in sequence and feeds each result back before requesting the final response.
4. **Given** `IChatClient` is null or `AgentAIEnabled=false`, **When** the user sends a message, **Then** the agent falls back to the current direct-tool-execution behavior, returning raw tool output.
5. **Given** the LLM exceeds the maximum tool-call rounds (default 5), **When** the limit is reached, **Then** the agent terminates the loop gracefully and returns an explanation to the user.

---

### User Story 3 — ChatHub AI Integration (Priority: P3)

As a user chatting through the web UI, I want my messages to flow through the AI-powered agent pipeline so that I receive conversational, context-aware responses instead of raw tool output — with the LLM having full visibility into conversation history.

**Why this priority**: Connects the AI-powered agents (US2) to the actual user-facing chat interface. Without this, AI features would only work through the MCP layer, not through the primary web chat UX.

**Independent Test**: Can be tested by sending a message through the ChatHub/MCP server path and verifying that the response is generated by the LLM (natural language, context-aware) rather than raw tool JSON. Conversation history should be correctly passed to the agent.

**Acceptance Scenarios**:

1. **Given** the user sends a message via the MCP chat endpoint, **When** the orchestrator routes to an agent with `IChatClient` available and `AgentAIEnabled=true`, **Then** the agent's AI-powered `TryProcessWithAiAsync` is invoked within `ProcessAsync` to handle the message.
2. **Given** an ongoing conversation with prior messages, **When** the user sends a follow-up message, **Then** the conversation history (both user messages and AI-generated assistant responses) is passed to the agent so the LLM has full context.
3. **Given** the LLM generates a response, **When** the response is returned, **Then** the AI-generated text is stored in the conversation as the assistant response — not the raw tool output.

---

### User Story 4 — System Prompt Enhancement (Priority: P3)

As an agent powered by an LLM, I need enhanced system prompts that guide my behavior — how to select tools, chain multi-step operations, format responses in Markdown, ask clarifying questions, and handle tool errors — so that my responses are consistently high-quality and compliance-appropriate.

**Why this priority**: System prompts are what give each agent its personality, expertise, and behavioral guidelines. Poor prompts produce poor AI responses regardless of model quality. Ranked P3 because AI processing (US2) works with existing prompts; enhancements improve quality.

**Independent Test**: Can be tested by verifying each prompt file contains the required new sections (Response Guidelines, Tool Selection) and that the content is well-formed. Can also be validated by sending test messages through an agent and checking that responses follow the formatting and behavioral guidelines specified in the prompt.

**Acceptance Scenarios**:

1. **Given** each agent's `.prompt.txt` file, **When** the prompt is loaded, **Then** it contains the original content plus new "Response Guidelines" and "Tool Selection" sections.
2. **Given** the Response Guidelines section, **When** the agent generates a response, **Then** the LLM follows Markdown formatting conventions (tables, code blocks, severity badges like `[HIGH]`, `[CRITICAL]`).
3. **Given** the Tool Selection section, **When** the user makes a multi-step request (e.g., "assess compliance then generate a plan"), **Then** the LLM chains the appropriate tools in sequence.
4. **Given** a tool returns an error, **When** the agent generates a response, **Then** the LLM explains the error in user-friendly language rather than returning the raw error string.

---

### User Story 5 — Configuration & Feature Flags (Priority: P1)

As a platform operator, I want feature flags and configuration knobs to control AI behavior — enabling/disabling AI processing, setting temperature, and limiting tool-call rounds — so that I can roll out AI features incrementally and tune behavior for a compliance-sensitive environment.

**Why this priority**: Co-ranked P1 with US1 because feature flags gate all AI behavior. Without the `AgentAIEnabled` flag, there is no safe way to deploy the Azure OpenAI wiring without immediately activating AI for all agents. Operators need a kill switch.

**Independent Test**: Can be tested by toggling `AgentAIEnabled` in configuration and verifying that agents switch between AI-powered and direct-tool-execution modes without restart (or at startup). Temperature and MaxToolCallRounds can be validated through unit tests on the configuration options class.

**Acceptance Scenarios**:

1. **Given** `Gateway:AzureOpenAI:AgentAIEnabled` is `false` (default), **When** an agent processes a message, **Then** the agent uses current direct-tool-execution behavior regardless of whether `IChatClient` is available.
2. **Given** `Gateway:AzureOpenAI:AgentAIEnabled` is `true` and `IChatClient` is available, **When** an agent processes a message, **Then** the agent uses LLM-powered processing via `TryProcessWithAiAsync`.
3. **Given** `Gateway:AzureOpenAI:MaxToolCallRounds` is set to 3, **When** the LLM attempts a 4th tool call, **Then** the agent stops the tool-call loop and returns a summarizing response.
4. **Given** `Gateway:AzureOpenAI:Temperature` is set to 0.3, **When** the LLM is called, **Then** the configured temperature value is used in the chat completion request.

---

### User Story 6 — Degraded Mode & Graceful Fallback (Priority: P2)

As a platform operator, I want the system to continue functioning fully when Azure OpenAI is unavailable or unconfigured — agents fall back to deterministic tool dispatch, the orchestrator falls back to keyword routing, and no user-facing errors occur.

**Why this priority**: Co-ranked P2 with US2 because resilience is essential for an IL5/IL6 compliance system. Air-gapped or disconnected environments must still work. This is implicit in the design but must be explicitly validated.

**Independent Test**: Can be tested by running the full application with no Azure OpenAI configuration and verifying all agents, tools, and the orchestrator function identically to the current behavior.

**Acceptance Scenarios**:

1. **Given** no `AzureOpenAI` configuration section, **When** agents process messages, **Then** all agents use direct-tool-execution and return raw tool output with no error logs.
2. **Given** `IChatClient` is available but the LLM call fails (timeout, rate limit, 500 error), **When** the agent catches the exception, **Then** it falls back to direct-tool-execution for that request and logs the error.
3. **Given** the system is running in degraded mode (no AI), **When** the user interacts through chat, **Then** all existing functionality works identically to pre-AI behavior — no regressions.

---

### Edge Cases

- What happens when the LLM returns an empty response (no text, no tool calls)? The agent should return a user-friendly message indicating the request could not be processed and suggest rephrasing.
- What happens when a tool name returned by the LLM does not match any registered tool? The agent should send a tool error result back to the LLM and let it retry or explain the issue.
- What happens when the LLM hallucinates parameters for a tool call (e.g., passes a nonexistent control ID)? The tool executes with the provided parameters and returns its normal error/not-found response; the LLM then interprets that result.
- What happens when `IChatClient` is available at startup but Azure OpenAI becomes unreachable mid-session? Per-request fallback to direct-tool-execution with error logging.
- What happens when multiple agents have AI processing active but only some have AI features enabled? Each agent independently checks `AgentAIEnabled` and their own `IChatClient` availability.
- What happens when conversation history exceeds the model's context window? The Azure OpenAI API returns a token-limit error, which is caught by the try-catch in `TryProcessWithAiAsync` and triggers fallback to deterministic processing. Context window truncation is deferred to a future enhancement.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST construct an Azure OpenAI client from `Gateway:AzureOpenAI` configuration section (endpoint, API key or managed identity, deployment name) and register the resulting `IChatClient` in DI.
- **FR-002**: System MUST support Azure Government endpoints (`.us` suffix) for IL5/IL6 compliance.
- **FR-003**: System MUST return null `IChatClient` gracefully when Azure OpenAI configuration is missing or empty — no startup failures, no error logs at startup.
- **FR-004**: `BaseAgent` MUST accept an optional `IChatClient?` parameter — it MUST NOT be required.
- **FR-005**: `BaseAgent` MUST provide a `TryProcessWithAiAsync` protected method that builds chat context (system prompt + conversation history + user message), describes available tools as function definitions, calls the LLM, handles tool-call responses in a loop, and returns the LLM's final text response — or returns null when AI is unavailable/disabled to trigger deterministic fallback.
- **FR-006**: Tool-call loop in `TryProcessWithAiAsync` MUST be limited to a configurable maximum number of rounds (default 5) to prevent infinite loops.
- **FR-007**: When `IChatClient` is null or `AgentAIEnabled` is false, agents MUST fall back to current direct-tool-execution behavior with no behavioral changes.
- **FR-008**: All 3 concrete agents (ComplianceAgent, ConfigurationAgent, KnowledgeBaseAgent) MUST have their constructors updated to accept `IChatClient?` and pass it to `BaseAgent`.
- **FR-009**: MCP server's chat processing MUST use the agent's AI-powered processing path when `IChatClient` is registered and `AgentAIEnabled` is `true`, passing conversation context to the agent.
- **FR-010**: Conversation history (both user and assistant messages) MUST be passed to `TryProcessWithAiAsync` for LLM context continuity.
- **FR-011**: Each agent's `.prompt.txt` file MUST be extended (not rewritten) with "Response Guidelines" and "Tool Selection" sections.
- **FR-012**: Configuration MUST include `Gateway:AzureOpenAI:AgentAIEnabled` (boolean, default false), `Gateway:AzureOpenAI:MaxToolCallRounds` (integer, default 5), and `Gateway:AzureOpenAI:Temperature` (double, default 0.3).
- **FR-013**: When the LLM exceeds the maximum tool-call rounds, the system MUST terminate the loop and return a user-friendly explanation.
- **FR-014**: When the LLM returns a tool name not matching any registered tool, the system MUST send a tool-error result back to the LLM.
- **FR-015**: The `IChatClient` MUST be registered in MCP `Program.cs` (via `AddAtoCopilotCore`). Chat project requires no changes as it connects to MCP over HTTP and does not instantiate agents directly.
- **FR-016**: All existing tests (2271 unit + 33 integration) MUST continue passing with no modifications (see SC-001).
- **FR-017**: When the LLM call fails (exception, timeout), the agent MUST fall back to direct-tool-execution for that request and log the error.
- **FR-018**: The `AzureOpenAI` NuGet package MUST be added to the Core project only — agents depend on the `IChatClient` abstraction, not the concrete Azure OpenAI implementation.

### Key Entities

- **AzureOpenAIGatewayOptions** (existing, extended): Configuration model binding to the `Gateway:AzureOpenAI` section. Already contains Endpoint, ApiKey, DeploymentName, UseManagedIdentity, ChatDeploymentName, EmbeddingDeploymentName. Extended with AgentAIEnabled, MaxToolCallRounds, Temperature.
- **IChatClient registration** (inline in `CoreServiceExtensions.AddAtoCopilotCore`): Conditional singleton registration that constructs `AzureOpenAIClient` from `AzureOpenAIGatewayOptions`, calls `.AsChatClient(ChatDeploymentName)`, and registers the resulting `IChatClient`. Skips registration when Endpoint is empty.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 2271 existing unit tests and 33 existing integration tests pass without modification after the feature is implemented.
- **SC-002**: When `AgentAIEnabled=true` and a valid `IChatClient` is available, agent responses are natural language text (not raw JSON) that interpret tool results for the user.
- **SC-003**: When `AgentAIEnabled=false` or `IChatClient` is null, agent behavior is identical to pre-feature behavior — zero regressions in output format or functionality.
- **SC-004**: The LLM-powered agent correctly selects and executes the right tool(s) for at least 10 representative test scenarios (e.g., compliance assessment lookup, remediation plan request, control search, configuration get/set, knowledge base query) — validated by unit tests that mock `IChatClient` to return expected `FunctionCallContent` and verify the correct tool is dispatched.
- **SC-005**: Tool-call chains terminate within the configured maximum rounds (default 5) with no infinite loops.
- **SC-006**: System starts and runs without errors when no Azure OpenAI configuration is present (degraded mode).
- **SC-007**: At least 8 new unit tests cover the AI processing flow (TryProcessWithAiAsync, IChatClient registration, fallback, feature flags, tool-call chaining, max rounds, ChatHub integration, config validation).
- **SC-008**: The Azure OpenAI client correctly targets Azure Government endpoints when configured with `.us` suffix URLs.

## Assumptions

- The `Microsoft.Extensions.AI` package (v9.4.0-preview.1.25207.5) already referenced in the Agents project provides the `IChatClient` abstraction and function-calling support needed for tool dispatch.
- The `Azure.AI.OpenAI` NuGet package provides the `AzureOpenAIClient` class that implements (or adapts to) `IChatClient` from `Microsoft.Extensions.AI`.
- Azure Government OpenAI endpoints follow the same API surface as commercial Azure OpenAI, differing only in the endpoint URL domain.
- The existing `AgentConversationContext.MessageHistory` provides sufficient conversation history for LLM context — no new conversation storage mechanism is needed.
- The `BaseTool.Parameters` dictionary (with `ToolParameter` entries containing name, description, type, required) provides enough metadata to generate function-call JSON Schema definitions for the LLM.
- The MCP `appsettings.json` `Agents:KnowledgeBaseAgent:ModelName` and `Temperature` fields are currently unused placeholders that will be superseded by the new `AzureOpenAI` configuration section.
- The ChatHub currently routes through the MCP server via HTTP; the AI integration happens at the agent level inside the MCP server, so no changes to the ChatHub-to-MCP HTTP call pattern are required.
- Streaming responses (token-by-token via `CompleteStreamingAsync`) is a desirable enhancement but not a P1 requirement — the initial implementation can return complete responses.
