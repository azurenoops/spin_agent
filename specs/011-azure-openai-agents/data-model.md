# Data Model: Add Azure OpenAI to Security Posture Intelligence Navigator Agents

**Feature**: 011-azure-openai-agents | **Date**: 2026-02-25

## Entity Overview

This feature introduces no new database entities or storage changes. All new types are configuration models, DI-registered services, and in-memory processing structures. The existing `AgentConversationContext.MessageHistory` handles conversation state.

## Modified Entities

### AzureOpenAIGatewayOptions (MODIFY)

**Location**: `src/Ato.Copilot.Core/Configuration/GatewayOptions.cs`
**Config Path**: `Gateway:AzureOpenAI`
**Purpose**: Configuration model for Azure OpenAI client construction and AI behavior control.

| Field | Type | Default | Source | Description |
|-------|------|---------|--------|-------------|
| Endpoint | string | `""` | Existing | Azure OpenAI endpoint URL (e.g., `https://my-resource.openai.azure.us`) |
| ApiKey | string | `""` | Existing | API key for authentication (alternative to Managed Identity) |
| DeploymentName | string | `"gpt-4o"` | Existing | Default model deployment name |
| UseManagedIdentity | bool | `false` | Existing | Use DefaultAzureCredential instead of API key |
| ChatDeploymentName | string | `"gpt-4o"` | Existing | Deployment name for chat completions |
| EmbeddingDeploymentName | string | `"text-embedding-ada-002"` | Existing | Deployment name for embeddings |
| **AgentAIEnabled** | **bool** | **`false`** | **NEW** | Master switch for AI-powered agent processing. When false, all agents use deterministic tool dispatch. |
| **MaxToolCallRounds** | **int** | **`5`** | **NEW** | Maximum number of LLM→tool→LLM rounds per request. Prevents infinite loops. |
| **Temperature** | **double** | **`0.3`** | **NEW** | LLM temperature parameter. Low value (0.3) for deterministic compliance responses. |

**Validation rules**:
- `MaxToolCallRounds` must be >= 1 and <= 20
- `Temperature` must be >= 0.0 and <= 2.0
- `AgentAIEnabled` requires non-empty `Endpoint` to have effect (if Endpoint is empty, AI is effectively disabled regardless)
- `DeploymentName` or `ChatDeploymentName` must be non-empty when `Endpoint` is configured

**State transitions**: None — this is a read-only configuration model bound at startup.

### BaseAgent (MODIFY)

**Location**: `src/Ato.Copilot.Agents/Common/BaseAgent.cs`
**Purpose**: Abstract base for all agents. Extended with optional AI processing capability.

| Field | Type | Visibility | Source | Description |
|-------|------|------------|--------|-------------|
| Logger | ILogger | protected | Existing | Structured logger |
| Tools | List\<BaseTool\> | protected | Existing | Registered tools for this agent |
| **_chatClient** | **IChatClient?** | **private** | **NEW** | Optional LLM client. Null when AI unavailable. |
| **_aiOptions** | **AzureOpenAIGatewayOptions?** | **private** | **NEW** | AI configuration. Null when unconfigured. |

**New methods**:

| Method | Signature | Description |
|--------|-----------|-------------|
| Constructor (overload) | `BaseAgent(ILogger, IChatClient?, AzureOpenAIGatewayOptions?)` | New constructor accepting optional AI dependencies. Existing `BaseAgent(ILogger)` constructor preserved for backward compatibility. |
| TryProcessWithAiAsync | `protected Task<AgentResponse?>(string, AgentConversationContext, CancellationToken)` | Attempts AI-powered processing. Returns null if AI unavailable or disabled → caller falls back to deterministic processing. |
| BuildChatContext | `private List<ChatMessage>(string, AgentConversationContext)` | Constructs message list: system prompt + conversation history + user message. |
| BuildToolDefinitions | `private List<AITool>()` | Converts registered `BaseTool` instances into `AITool` metadata for the LLM. |

**Relationships**:
- Has-a: `IChatClient?` (optional, from DI)
- Has-a: `AzureOpenAIGatewayOptions?` (optional, from DI/IOptions)
- Has-many: `BaseTool` (via `Tools` list — existing)
- Extended-by: ComplianceAgent, ConfigurationAgent, KnowledgeBaseAgent

### AgentResponse (NO CHANGE)

**Location**: `src/Ato.Copilot.Agents/Common/BaseAgent.cs`
**Purpose**: Response envelope from agent processing.

| Field | Type | Description |
|-------|------|-------------|
| Success | bool | Whether processing succeeded |
| Response | string | Response text — now may contain AI-generated natural language OR raw tool output |
| AgentName | string | Name of the responding agent |
| ToolsExecuted | List\<ToolExecutionResult\> | Tools invoked during processing (populated in both AI and direct modes) |
| ProcessingTimeMs | double | Total processing time including LLM calls |

No schema changes. The `Response` field's content changes semantically (natural language when AI enabled vs. raw tool output when disabled) but the type and structure are unchanged.

### AgentConversationContext (NO CHANGE)

**Location**: `src/Ato.Copilot.Agents/Common/BaseAgent.cs`
**Purpose**: Per-request conversation context passed from McpServer to agents.

| Field | Type | Description |
|-------|------|-------------|
| ConversationId | string | Unique conversation identifier |
| UserId | string | Authenticated user ID |
| MessageHistory | List\<(string Role, string Content)\> | Prior messages in this conversation |
| WorkflowState | Dictionary\<string, object\> | Arbitrary state for multi-step workflows |

No changes. The existing `MessageHistory` is already populated by `McpServer.ProcessChatRequestAsync` from the conversation history passed by `ChatService`. The AI processing path reads this history to build LLM context.

### BaseTool (NO CHANGE)

**Location**: `src/Ato.Copilot.Agents/Common/BaseTool.cs`
**Purpose**: Abstract base for all tools.

| Field | Type | Description |
|-------|------|-------------|
| Name | string | Tool name used for function-calling |
| Description | string | Human/LLM-readable description |
| Parameters | IReadOnlyDictionary\<string, ToolParameter\> | Parameter schema |
| RequiredPimTier | PimTier | Authorization level required |

No changes. The existing `Name`, `Description`, and `Parameters` metadata is already structured to generate LLM function-calling schemas. `ExecuteAsync` (instrumented wrapper) continues to be the execution entry point.

### ToolParameter (NO CHANGE)

**Location**: `src/Ato.Copilot.Agents/Common/BaseTool.cs`

| Field | Type | Description |
|-------|------|-------------|
| Name | string | Parameter name |
| Description | string | Parameter description for LLM |
| Type | string | JSON Schema type: "string", "integer", "boolean", "number" |
| Required | bool | Whether the parameter is required |

No changes. This metadata is sufficient for generating function-calling JSON schemas.

## Relationship Diagram

```
┌─────────────────────────────────────────────────┐
│                 appsettings.json                │
│  Gateway:AzureOpenAI: { Endpoint, ApiKey,       │
│    DeploymentName, UseManagedIdentity,           │
│    AgentAIEnabled, MaxToolCallRounds, Temperature}│
└────────────────────┬────────────────────────────┘
                     │ binds to
                     ▼
┌─────────────────────────────────────────────────┐
│        AzureOpenAIGatewayOptions                │
│  (Configuration model in Core)                   │
└────────────────────┬────────────────────────────┘
                     │ read by
                     ▼
┌─────────────────────────────────────────────────┐
│   CoreServiceExtensions.AddAtoCopilotCore()     │
│   → Constructs AzureOpenAIClient                │
│   → Calls .AsChatClient(deploymentName)         │
│   → Registers IChatClient singleton (or skips)  │
└────────────────────┬────────────────────────────┘
                     │ injected into
                     ▼
┌─────────────────────────────────────────────────┐
│              BaseAgent (abstract)                │
│  IChatClient? _chatClient                       │
│  AzureOpenAIGatewayOptions? _aiOptions          │
│                                                 │
│  TryProcessWithAiAsync() → manual loop:         │
│    1. BuildChatContext (system + history + user) │
│    2. BuildToolDefinitions (BaseTool → AITool)   │
│    3. chatClient.GetResponseAsync(messages, opts)│
│    4. If FunctionCallContent → execute BaseTool  │
│    5. Send FunctionResultContent → loop          │
│    6. Return final text as AgentResponse         │
├─────────────────────────────────────────────────┤
│  ComplianceAgent  │  ConfigAgent  │  KBAgent    │
│  ProcessAsync():  │  ProcessAsync │  ProcessAsync│
│   auth gate       │   classify    │   analyze   │
│   TryAI() ──┐     │   TryAI()    │   TryAI()   │
│   fallback ◄┘     │   fallback   │   fallback  │
└─────────────────────────────────────────────────┘
```

## No New Storage

This feature does not introduce any new database tables, migrations, or persistent storage mechanisms. Conversation history persistence continues to be managed by the Chat project's `ChatDbContext` — the AI processing path reads from `AgentConversationContext.MessageHistory` (populated per-request from the chat database by `ChatService`) and does not write back to the database directly. AI responses flow back through the existing `McpChatResponse` → HTTP → `ChatService` → `ChatDbContext` save path.
