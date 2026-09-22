# Research: Add Azure OpenAI to Security Posture Intelligence Navigator Agents

**Feature**: 011-azure-openai-agents | **Date**: 2026-02-25

## Research Tasks

### R1: Configuration Model — Extend Existing or Create New?

**Context**: Spec says create `AzureOpenAIOptions` with `AzureOpenAI:*` config path. Codebase already has `AzureOpenAIGatewayOptions` in `GatewayOptions.cs` under `Gateway:AzureOpenAI:*` with Endpoint, ApiKey, DeploymentName, UseManagedIdentity, ChatDeploymentName, EmbeddingDeploymentName.

**Decision**: Extend existing `AzureOpenAIGatewayOptions` with 3 new fields.

**Rationale**:
- `AzureOpenAIGatewayOptions` already has 6/9 needed fields (Endpoint, ApiKey, DeploymentName, UseManagedIdentity, ChatDeploymentName, EmbeddingDeploymentName)
- Creating a duplicate class violates DRY and constitution principle VI (code duplication)
- The `Gateway:AzureOpenAI` config path is the established convention — all gateway-related config lives under `Gateway:`
- Only 3 fields to add: `AgentAIEnabled` (bool), `MaxToolCallRounds` (int), `Temperature` (double)

**Alternatives considered**:
- Create separate top-level `AzureOpenAIOptions` class with `AzureOpenAI:*` path — rejected because it duplicates Endpoint/ApiKey/DeploymentName fields and creates a confusing parallel config section
- Create `AgentAIOptions` as a standalone — rejected because it fragments the AI config (you'd need both Gateway:AzureOpenAI and AgentAI sections)

### R2: NuGet Packages — What to Add and Where

**Context**: `Microsoft.Extensions.AI` 9.4.0-preview already in Agents.csproj. Need Azure OpenAI concrete client.

**Decision**: Add `Azure.AI.OpenAI` 2.1.0 and `Microsoft.Extensions.AI.OpenAI` 9.4.0-preview.1.25207.5 to Core.csproj only.

**Rationale**:
- Spec FR-018 requires concrete SDK in Core only — agents depend on `IChatClient` abstraction
- `Azure.AI.OpenAI` provides `AzureOpenAIClient` (extends `OpenAIClient`)
- `Microsoft.Extensions.AI.OpenAI` provides `.AsChatClient(deploymentName)` extension to convert `OpenAIClient` to `IChatClient`
- Core already has `Azure.Identity` for credential handling
- Agents.csproj already has `Microsoft.Extensions.AI` — no new package needed there

**Alternatives considered**:
- Add packages to Agents.csproj — rejected per FR-018 and clean architecture (agents shouldn't know about Azure OpenAI SDK)
- Add packages to Mcp.csproj — rejected because Core is the infrastructure layer; Mcp is the host, Core provides services

### R3: IChatClient Construction Pattern

**Context**: Need to construct `IChatClient` from Azure OpenAI config in a factory that supports both API key and managed identity auth.

**Decision**: Factory method in `CoreServiceExtensions` that conditionally registers `IChatClient` as singleton.

**Construction pattern**:
```csharp
// API Key auth:
var client = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
IChatClient chatClient = client.AsChatClient(deploymentName);

// Managed Identity auth (Azure Government):
var client = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
IChatClient chatClient = client.AsChatClient(deploymentName);
```

**Rationale**:
- `AzureOpenAIClient.AsChatClient(string modelId)` is the official M.E.AI adapter extension
- `DefaultAzureCredential` already used in Core for Azure ARM client — same pattern
- Conditional registration: if Endpoint is empty/null, don't register → `IChatClient` resolves to null via optional constructor params (existing pattern from `AiRemediationPlanGenerator`)
- Singleton lifetime is appropriate — `AzureOpenAIClient` is thread-safe and designed for reuse

**Alternatives considered**:
- Register as Scoped — rejected because the client is stateless and thread-safe; creating per-request is wasteful
- Always register with a null wrapper — rejected; DI optional parameter pattern already works (proven in `AiRemediationPlanGenerator`)
- Factory interface (`IAzureOpenAIChatClientFactory`) — rejected as over-engineering for a singleton that's conditionally registered; adds unnecessary abstraction

### R4: Function-Calling Approach — Manual Loop vs. FunctionInvokingChatClient

**Context**: M.E.AI provides `FunctionInvokingChatClient` that wraps an inner `IChatClient` and automatically handles the tool-call loop. Alternative is a manual loop in `BaseAgent.TryProcessWithAiAsync`.

**Decision**: Manual loop in `BaseAgent.TryProcessWithAiAsync`.

**Rationale**:
- **Full control over tool execution**: Manual loop integrates with existing `BaseTool.ExecuteAsync()` instrumented wrapper (ToolMetrics, Stopwatch, error recording)
- **Per-call logging**: Can log each tool call in the chain with agent name, tool name, arguments, and result — required by constitution principle V (Observability)
- **Custom error handling**: Can handle unknown tool names, tool exceptions, and max-rounds gracefully per spec edge cases
- **Simpler fallback**: Can return `null` to trigger deterministic fallback — `FunctionInvokingChatClient` doesn't support this pattern
- **Testability**: Manual loop is easier to unit test — mock `IChatClient` returns controlled sequences of `FunctionCallContent` and text responses
- **No delegate wrapping**: `FunctionInvokingChatClient` requires `AIFunction` instances with invocable delegates; manual loop only needs tool metadata for `ChatOptions.Tools` and dispatches via existing `BaseTool.ExecuteAsync`

**Alternatives considered**:
- `FunctionInvokingChatClient` wrapper — rejected because: (a) requires wrapping each `BaseTool` as an invocable `AIFunction` delegate, duplicating dispatch logic, (b) loses integration with `ToolMetrics` instrumentation, (c) opaque error handling makes fallback and logging harder, (d) `MaximumIterationsPerRequest` is less flexible than our per-request control

### R5: Tool-to-AITool Conversion Strategy

**Context**: `BaseTool` has `Name`, `Description`, `Parameters` (IReadOnlyDictionary<string, ToolParameter>). Need to provide `List<AITool>` to `ChatOptions.Tools` for the LLM to see available functions.

**Decision**: Use `AIFunctionFactory.Create` with a thin delegate wrapper per tool, passing name/description. Build JSON Schema from `ToolParameter` metadata.

**Pattern**:
```csharp
private List<AITool> BuildToolDefinitions()
{
    return Tools.Select(tool =>
    {
        return AIFunctionFactory.Create(
            async (IDictionary<string, object?> arguments) =>
            {
                var args = arguments.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value);
                return await tool.ExecuteAsync(args, CancellationToken.None);
            },
            new AIFunctionFactoryOptions
            {
                Name = tool.Name,
                Description = tool.Description
            });
    }).Cast<AITool>().ToList();
}
```

Actually, for the manual loop approach, we don't need invocable delegates on the AIFunction — we dispatch manually. We only need the metadata. So we can construct the tool definitions more cheaply. However, `AIFunctionFactory.Create` is the cleanest way to get properly-formed `AIFunction` instances with correct JSON schemas.

**Rationale**:
- `AIFunctionFactory.Create` generates proper function-calling JSON Schema from the delegate signature
- The delegate is only used for schema generation; actual dispatch happens through our manual `BaseTool.ExecuteAsync` call
- `ToolParameter.Type` maps to JSON Schema types: "string" → `string`, "integer" → `int`, "boolean" → `bool`, "number" → `double`

**Alternatives considered**:
- Manually construct `AIFunction` with hand-built JSON Schema — more code, fragile, no benefit
- Create a `BaseToolAdapter : AIFunction` subclass — over-engineering for schema generation

### R6: BaseAgent Integration Point

**Context**: Need to add AI processing to `BaseAgent` without breaking existing `ProcessAsync` contract.

**Decision**: Add `protected async Task<AgentResponse?> TryProcessWithAiAsync()` to `BaseAgent`. Each concrete agent calls this from `ProcessAsync` and falls back to current behavior if it returns null.

**Pattern**:
```csharp
// In BaseAgent:
protected async Task<AgentResponse?> TryProcessWithAiAsync(
    string message, AgentConversationContext context, CancellationToken ct)
{
    if (_chatClient == null || !_aiOptions?.AgentAIEnabled == true)
        return null;
    // ... LLM tool-calling loop ...
}

// In each concrete agent's ProcessAsync:
public override async Task<AgentResponse> ProcessAsync(...)
{
    var aiResponse = await TryProcessWithAiAsync(message, context, ct);
    if (aiResponse != null) return aiResponse;
    
    // ... existing deterministic behavior ...
}
```

**Rationale**:
- **Non-breaking**: Existing `ProcessAsync` signature unchanged. Abstract contract preserved.
- **Opt-in per agent**: Each agent chooses where in its `ProcessAsync` to try AI (e.g., ComplianceAgent might apply auth gate first, then try AI)
- **Clean fallback**: `null` return means "AI not available, do it the old way"
- **Testable**: Can test AI path and fallback path independently

**Alternatives considered**:
- Make `ProcessAsync` non-abstract and add AI logic in base — rejected because concrete agents have unique pre-processing (auth gates, intent classification) that must run before AI
- Add a separate `ProcessWithAiAsync` abstract method — rejected because it would require all agents to implement a new abstract, breaking existing code

### R7: MCP Server and Chat Changes

**Context**: Spec says ChatHub integration (US3, FR-009, FR-015). Need to determine actual change scope.

**Decision**: No changes to McpServer.cs, McpHttpBridge.cs, ChatHub.cs, or ChatService.cs.

**Rationale**:
- The call chain is: ChatHub → ChatService → HTTP POST /mcp/chat → McpHttpBridge → McpServer.ProcessChatRequestAsync → AgentOrchestrator.SelectAgent → agent.ProcessAsync
- AI integration happens **inside** `agent.ProcessAsync` — agents internally call `TryProcessWithAiAsync`
- `McpServer.ProcessChatRequestAsync` already passes conversation history to `AgentConversationContext.MessageHistory`
- `AgentResponse` envelope is unchanged — `Response` field contains AI text or raw tool output depending on mode
- Conversation history passing already works: `ChatService` loads last 20 messages from DB and sends them in the HTTP request; `McpServer` populates `AgentConversationContext.MessageHistory`
- `IChatClient` registration in MCP `Program.cs` is needed for DI to resolve it into agents
- Chat project connects to MCP via HTTP — it doesn't instantiate agents directly, so no `IChatClient` registration needed there

**Correction to spec FR-015**: "`IChatClient` MUST be registered in both MCP Program.cs and Chat Program.cs" — this is incorrect. Chat doesn't instantiate agents; it calls MCP over HTTP. `IChatClient` only needs registration in MCP `Program.cs` (via `services.AddAtoCopilotCore()` which will include the new registration). Chat project needs NO changes.

**Alternatives considered**:
- Modify McpServer to detect AI-capable agents and call a different method — rejected because it violates separation of concerns; agents should manage their own AI state
- Register IChatClient in Chat project — unnecessary since Chat connects to MCP over HTTP

### R8: System Prompt Enhancement Strategy

**Context**: 5 prompt files need "Response Guidelines" and "Tool Selection" sections appended. Must not rewrite existing content.

**Decision**: Append two new sections to each `.prompt.txt` file after existing content.

**Pattern**:
```text
[... existing prompt content unchanged ...]

## Response Guidelines

- Format responses in Markdown with headers, tables, and code blocks where appropriate
- Use severity badges: [CRITICAL], [HIGH], [MODERATE], [LOW] for compliance findings
- Provide actionable recommendations, not just raw data
- If a tool returns an error, explain it in user-friendly language
- If you need more information, ask a clarifying question
- Never expose internal tool names, error codes, or JSON structure to the user

## Tool Selection

- Analyze the user's intent before selecting a tool
- For multi-step requests, chain tools in logical order
- If unsure which tool to use, ask the user to clarify
- Prefer specific tools over general ones when the intent is clear
- [Agent-specific tool selection guidance here]
```

**Rationale**:
- Appending preserves existing behavior and expertise definitions
- Two standardized sections ensure consistency across agents
- Agent-specific tool guidance varies (ComplianceAgent has 65+ tools vs. ConfigurationAgent has 1)
- The "Tool Selection" section will be customized per agent based on their registered tools

**Alternatives considered**:
- Rewrite prompts from scratch — explicitly prohibited by spec FR-011
- Use a shared base prompt file — fragile; agents have very different roles and tool sets

### R9: Error Handling Strategy for LLM Failures

**Context**: LLM calls can fail (timeout, rate limit, 500 error, empty response). Need graceful fallback.

**Decision**: Wrap the entire `TryProcessWithAiAsync` body in a try-catch. On any exception, log the error and return `null` (triggering fallback to deterministic processing).

**Rationale**:
- Matches existing `AiRemediationPlanGenerator` pattern: every method wraps in try/catch, returns null on failure
- Agents always have a working fallback — the existing keyword routing and tool dispatch
- Per-request fallback means one LLM failure doesn't disable AI for subsequent requests
- Logging the exception provides observability without user-facing errors

**Alternatives considered**:
- Circuit breaker pattern (disable AI after N failures) — over-engineering for v1; can be added later
- Retry with backoff — adds latency; the user is waiting; fallback is instant and provides a result
