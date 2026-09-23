# Foundry Agent Provisioning Contract

**Feature**: 028-foundry-agents | **Date**: 2026-03-13

## Overview

This contract defines the interface between Security Posture Intelligence Navigator agents and the Azure AI Foundry service during agent provisioning. Each concrete agent (Compliance, Configuration, KnowledgeBase) creates or updates a Foundry agent at startup.

## Provisioning Interface

### Input (per agent)

| Field | Source | Description |
|-------|--------|-------------|
| `agentName` | `BaseAgent.AgentName` | Unique agent display name (e.g., "Compliance Agent") |
| `instructions` | `BaseAgent.GetSystemPrompt()` | System prompt text from `*.prompt.txt` file |
| `model` | `AzureAiOptions.DeploymentName` | Model deployment name (default: `gpt-4o`) |
| `tools` | `BaseAgent.BuildFoundryToolDefinitions()` | List of `FunctionToolDefinition` from registered `BaseTool` instances |

### Tool Definition Schema

Each `BaseTool` is converted to a `FunctionToolDefinition`:

```json
{
  "type": "function",
  "function": {
    "name": "compliance_status",
    "description": "Get the current compliance assessment status for a system",
    "parameters": {
      "type": "object",
      "properties": {
        "system_id": {
          "type": "string",
          "description": "The system identifier"
        }
      },
      "required": ["system_id"]
    }
  }
}
```

### Output

| Field | Type | Description |
|-------|------|-------------|
| `agentId` | `string` | Foundry agent ID (stored in `BaseAgent._foundryAgentId`) |

### Idempotency

- **First startup**: Creates a new Foundry agent via `CreateAgentAsync`
- **Subsequent startups**: Lists agents, matches by name, updates via `UpdateAgentAsync`
- **Failure**: Sets `_foundryAgentId = null`, logs warning, agent falls back to IChatClient path

## Run Lifecycle Contract

### Request Flow

```
User Message → BaseAgent.TryProcessWithBackendAsync
  → Provider == Foundry?
    → TryProcessWithFoundryAsync
      → Get or create thread (ConversationId → ThreadId)
      → CreateMessageAsync (user role)
      → CreateRunAsync (agentId)
      → Poll GetRunAsync until terminal
        → RequiresAction? → dispatch tools → SubmitToolOutputsToRunAsync → re-poll
        → Completed? → read last assistant message → return AgentResponse
        → Failed/Cancelled/Expired? → return null (triggers fallback)
        → Timeout (60s)? → CancelRunAsync → return null
    → Provider == OpenAi?
      → TryProcessWithAiAsync (existing IChatClient path)
```

### Tool Dispatch During RequiresAction

| Step | SDK Method | Description |
|------|-----------|-------------|
| 1 | `run.RequiredAction` → `SubmitToolOutputsAction` | Cast required action |
| 2 | Iterate `.ToolCalls` | For each `RequiredFunctionToolCall` |
| 3 | Match `functionCall.Name` to `Tools` | Case-insensitive lookup |
| 4 | `tool.ExecuteAsync(args)` | Execute locally |
| 5 | `SubmitToolOutputsToRunAsync(run, outputs)` | Batch submit all tool results |

### Error Responses

| Condition | Tool Output Submitted | Fallback |
|-----------|----------------------|----------|
| Unknown tool name | Error message string | Model self-corrects |
| Tool execution failure | Exception message string | Model self-corrects |
| Run timeout (>60s) | N/A (run cancelled) | IChatClient → deterministic |
| Run failed/cancelled | N/A | IChatClient → deterministic |

## Configuration Contract

All AI configuration is exposed via the `AzureAi` section (bound to `AzureAiOptions`):

```json
{
  "AzureAi": {
    "Enabled": true,
    "Provider": "Foundry|OpenAi",
    "Endpoint": "https://<openai-endpoint>/",
    "DeploymentName": "gpt-4o",
    "FoundryProjectEndpoint": "https://<foundry-project>/",
    "RunTimeoutSeconds": 60,
    "MaxToolIterations": 10
  }
}
```

Environment variable prefix: `ATO_AZUREAI__` (double underscore for nested binding).
