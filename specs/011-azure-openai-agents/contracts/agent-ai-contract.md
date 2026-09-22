# Agent AI Contract: Add Azure OpenAI to Security Posture Intelligence Navigator Agents

**Feature**: 011-azure-openai-agents | **Date**: 2026-02-25

## Overview

This document defines the public interface contracts affected by the Azure OpenAI integration. The feature modifies **no external API surfaces** — all MCP protocol endpoints, SignalR hub methods, and HTTP endpoints remain unchanged. The changes are internal to the agent processing layer: the `BaseAgent` abstraction gains an optional AI processing path, and the response content changes semantically (from raw tool JSON to natural language) when AI is enabled.

## Contract 1: BaseAgent Abstract Contract

### Existing Contract (Preserved)

```
BaseAgent
├── Properties (abstract)
│   ├── AgentId: string
│   ├── AgentName: string
│   └── Description: string
├── Methods (abstract)
│   ├── GetSystemPrompt(): string
│   ├── CanHandle(message: string): double  // 0.0–1.0 confidence
│   └── ProcessAsync(message, context, ct): AgentResponse
└── Methods (protected)
    └── RegisterTool(tool: BaseTool): void
```

### Extended Contract (New)

```
BaseAgent
├── Constructor (new overload)
│   └── BaseAgent(logger: ILogger, chatClient: IChatClient?, aiOptions: AzureOpenAIGatewayOptions?)
├── Methods (protected, new)
│   └── TryProcessWithAiAsync(message: string, context: AgentConversationContext, ct: CancellationToken): AgentResponse?
│       Input:
│         - message: User's natural language message
│         - context: Conversation context with MessageHistory and UserId
│         - ct: Cancellation token (honored throughout async chain)
│       Output:
│         - AgentResponse when AI processes successfully
│         - null when AI is unavailable, disabled, or fails (caller should fall back)
│       Behavior:
│         1. Returns null immediately if IChatClient is null or AgentAIEnabled is false
│         2. Builds chat context: system prompt → conversation history → user message
│         3. Converts registered Tools to AITool definitions for LLM
│         4. Calls IChatClient.GetResponseAsync with messages and tool definitions
│         5. If LLM returns FunctionCallContent: executes named BaseTool, sends result back
│         6. Repeats step 4-5 up to MaxToolCallRounds times
│         7. Returns LLM's final text response as AgentResponse.Response
│         8. On any exception: logs error, returns null (triggers fallback)
│       Error handling:
│         - Unknown tool name from LLM → sends error result to LLM for retry
│         - Tool execution exception → sends error message to LLM
│         - LLM timeout/failure → catches exception, returns null
│         - Max rounds exceeded → returns summary response (not null)
│         - Empty LLM response → returns user-friendly "please rephrase" message
```

### Concrete Agent Integration Pattern

Each concrete agent (ComplianceAgent, ConfigurationAgent, KnowledgeBaseAgent) follows this pattern in `ProcessAsync`:

```
ProcessAsync(message, context, ct):
  1. [Agent-specific pre-processing] (e.g., auth gate for ComplianceAgent)
  2. aiResponse = TryProcessWithAiAsync(message, context, ct)
  3. If aiResponse is not null → return aiResponse
  4. [Existing deterministic processing] (keyword routing, tool dispatch)
```

## Contract 2: IChatClient DI Registration

### Registration Contract

```
When Gateway:AzureOpenAI:Endpoint is non-empty:
  → IChatClient registered as singleton in DI
  → Constructed from AzureOpenAIClient.AsChatClient(ChatDeploymentName)
  → Auth: DefaultAzureCredential (if UseManagedIdentity) or ApiKeyCredential (if ApiKey set)

When Gateway:AzureOpenAI:Endpoint is empty or section missing:
  → IChatClient NOT registered in DI
  → Optional constructor parameters receive null
  → All agents operate in deterministic mode
  → No startup errors, no warning logs
```

### Resolution Contract

```
Services that accept IChatClient:
  ├── BaseAgent subclasses (IChatClient? chatClient = null)
  └── AiRemediationPlanGenerator (IChatClient? chatClient = null)

All consumers use nullable optional parameters.
DI resolves to null when IChatClient is not registered.
No service throws on null IChatClient.
```

## Contract 3: Configuration Contract

### Config Section: Gateway:AzureOpenAI

```json
{
  "Gateway": {
    "AzureOpenAI": {
      "Endpoint": "",                    // Required for AI. Empty = AI disabled.
      "ApiKey": "",                      // Required if UseManagedIdentity is false
      "DeploymentName": "gpt-4o",        // Default model deployment
      "ChatDeploymentName": "gpt-4o",    // Chat completion deployment
      "EmbeddingDeploymentName": "text-embedding-ada-002",
      "UseManagedIdentity": false,       // true in production (Azure Gov)
      "AgentAIEnabled": false,           // Master AI switch. Default: OFF.
      "MaxToolCallRounds": 5,            // Max LLM→tool→LLM cycles per request
      "Temperature": 0.3                 // LLM temperature (0.0–2.0)
    }
  }
}
```

### Feature Flag Behavior

| Endpoint | AgentAIEnabled | IChatClient | Agent Behavior |
|----------|---------------|-------------|----------------|
| Empty | false | null | Deterministic (current behavior) |
| Empty | true | null | Deterministic (flag ignored without client) |
| Set | false | registered | Deterministic (flag overrides client) |
| Set | true | registered | **AI-powered processing** |

## Contract 4: MCP Chat Response (UNCHANGED)

The `McpChatResponse` returned by `/mcp/chat` is unchanged:

```json
{
  "success": true,
  "response": "string — natural language (AI) or raw tool output (deterministic)",
  "conversationId": "string",
  "agentName": "string",
  "processingTimeMs": 0.0,
  "toolsExecuted": [],
  "errors": [],
  "suggestions": [],
  "requiresFollowUp": false,
  "metadata": {}
}
```

The only semantic change: when AI is enabled, `response` contains LLM-generated natural language interpreting tool results. When AI is disabled, `response` contains raw tool output as before. The schema is identical in both cases.

## Contract 5: System Prompt Contract

Each agent's `.prompt.txt` file maintains its existing content and appends two standardized sections:

```
[Existing prompt content — unchanged]

## Response Guidelines

[Standardized formatting and behavior rules]

## Tool Selection

[Agent-specific tool selection guidance based on registered tools]
```

The prompt is loaded by `GetSystemPrompt()` (unchanged method) and passed as the system message to the LLM. When AI is disabled, the prompt is loaded but never sent to an LLM — no behavioral change.
