# Quickstart: Add Azure OpenAI to Security Posture Intelligence Navigator Agents

**Feature**: 011-azure-openai-agents | **Date**: 2026-02-25

## Prerequisites

- .NET 9.0 SDK installed
- Solution builds: `dotnet build Ato.Copilot.sln`
- All existing tests pass: `dotnet test Ato.Copilot.sln`
- Branch: `011-azure-openai-agents`

## What This Feature Does

Adds optional Azure OpenAI LLM-powered processing to Security Posture Intelligence Navigator agents. When configured and enabled, agents use the LLM to understand user intent, select appropriate tools via function-calling, and return natural language responses that interpret tool results. When unconfigured or disabled, agents behave exactly as before — deterministic keyword routing and raw tool output.

## Implementation Order

### Step 1: Configuration (P1)

1. **Add 3 fields to `AzureOpenAIGatewayOptions`** in `src/Ato.Copilot.Core/Configuration/GatewayOptions.cs`:
   - `AgentAIEnabled` (bool, default false)
   - `MaxToolCallRounds` (int, default 5)
   - `Temperature` (double, default 0.3)

2. **Add `Gateway:AzureOpenAI` section** to `src/Ato.Copilot.Mcp/appsettings.json`

3. **Add NuGet packages** to `src/Ato.Copilot.Core/Ato.Copilot.Core.csproj`:
   - `Azure.AI.OpenAI` (2.1.0)
   - `Microsoft.Extensions.AI.OpenAI` (9.4.0-preview.1.25207.5)

### Step 2: IChatClient Factory (P1)

4. **Add IChatClient registration** in `src/Ato.Copilot.Core/Extensions/CoreServiceExtensions.cs`:
   - Read `AzureOpenAIGatewayOptions` from configuration
   - If Endpoint is non-empty: construct `AzureOpenAIClient` → `.AsChatClient()` → register as singleton
   - If Endpoint is empty: skip registration (IChatClient resolves to null)

### Step 3: BaseAgent AI Processing (P2)

5. **Extend `BaseAgent`** in `src/Ato.Copilot.Agents/Common/BaseAgent.cs`:
   - Add constructor overload accepting `IChatClient?` and `AzureOpenAIGatewayOptions?`
   - Add `TryProcessWithAiAsync()` with manual LLM tool-calling loop
   - Add `BuildChatContext()` and `BuildToolDefinitions()` helpers

### Step 4: Concrete Agent Updates (P2)

6. **Update agent constructors** to accept `IChatClient?` and `IOptions<AzureOpenAIGatewayOptions>?`:
   - `ComplianceAgent` — call `TryProcessWithAiAsync()` after auth gate
   - `ConfigurationAgent` — call `TryProcessWithAiAsync()` before intent classification
   - `KnowledgeBaseAgent` — call `TryProcessWithAiAsync()` before query analysis

7. **Update DI registrations** in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs`:
   - Pass `IChatClient?` (via `sp.GetService<IChatClient>()`) and `IOptions<AzureOpenAIGatewayOptions>` to agent constructors

### Step 5: System Prompt Enhancement (P3)

8. **Append sections** to each `.prompt.txt` file:
   - `ComplianceAgent.prompt.txt` — "Response Guidelines" + "Tool Selection" (compliance-specific)
   - `ConfigurationAgent.prompt.txt` — "Response Guidelines" + "Tool Selection" (config-specific)
   - `KnowledgeBaseAgent.prompt.txt` — "Response Guidelines" + "Tool Selection" (KB-specific)
   - `KanbanAgent.prompt.txt` — "Response Guidelines" + "Tool Selection" (kanban-specific)
   - `PimAgent.prompt.txt` — "Response Guidelines" + "Tool Selection" (PIM-specific)

### Step 6: Tests (ALL)

9. **Write unit tests** for:
   - `AzureOpenAIGatewayOptions` — validation of new fields, defaults
   - IChatClient factory — registration when configured, null when unconfigured, Azure Gov endpoint
   - `BaseAgent.TryProcessWithAiAsync` — AI path, fallback path, tool-call chaining, max rounds, unknown tool, LLM error
   - Each agent's `ProcessAsync` — AI-enabled behavior, AI-disabled fallback
   - Feature flag toggling — `AgentAIEnabled` true/false behavior

## Validation Commands

```bash
# Build
dotnet build Ato.Copilot.sln

# Run all tests (must be 0 failures including existing 2304 tests)
dotnet test Ato.Copilot.sln

# Run only new AI tests
dotnet test tests/Ato.Copilot.Tests.Unit --filter "FullyQualifiedName~Ai"

# Verify degraded mode (no Azure OpenAI config — all agents work as before)
dotnet run --project src/Ato.Copilot.Mcp -- --http
# POST to /mcp/chat with a compliance message → should get raw tool output
```

## Configuration for Testing

To test AI-enabled mode locally, add to `appsettings.Development.json`:

```json
{
  "Gateway": {
    "AzureOpenAI": {
      "Endpoint": "https://your-resource.openai.azure.com",
      "ApiKey": "your-api-key",
      "ChatDeploymentName": "gpt-4o",
      "AgentAIEnabled": true,
      "MaxToolCallRounds": 5,
      "Temperature": 0.3
    }
  }
}
```

For Azure Government:
```json
{
  "Gateway": {
    "AzureOpenAI": {
      "Endpoint": "https://your-resource.openai.azure.us",
      "UseManagedIdentity": true,
      "ChatDeploymentName": "gpt-4o",
      "AgentAIEnabled": true
    }
  }
}
```

## Key Design Decisions

| Decision | Choice | See |
|----------|--------|-----|
| Config model | Extend existing `AzureOpenAIGatewayOptions` | [research.md#R1](research.md) |
| Packages | `Azure.AI.OpenAI` + `Microsoft.Extensions.AI.OpenAI` in Core only | [research.md#R2](research.md) |
| Function-calling | Manual loop (not FunctionInvokingChatClient) | [research.md#R4](research.md) |
| Agent integration | `TryProcessWithAiAsync()` returns null for fallback | [research.md#R6](research.md) |
| MCP/Chat changes | None — AI happens inside agent.ProcessAsync | [research.md#R7](research.md) |
