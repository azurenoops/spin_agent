# Implementation Plan: Add Azure OpenAI to Security Posture Intelligence Navigator Agents

**Branch**: `011-azure-openai-agents` | **Date**: 2026-02-25 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/011-azure-openai-agents/spec.md`

## Summary

Wire up Azure OpenAI as the LLM backend for Security Posture Intelligence Navigator agents, transforming them from deterministic keyword-routing + tool-dispatch systems into AI-powered conversational assistants that use LLM function-calling to select tools, chain multi-step operations, and return natural language responses interpreting tool results. The implementation adds conditional `IChatClient` singleton registration in `CoreServiceExtensions` backed by the `Azure.AI.OpenAI` SDK, extends `BaseAgent` with an optional AI processing path (`TryProcessWithAiAsync`), updates all 3 concrete agents to accept `IChatClient?`, enhances system prompts with tool-selection and response-formatting guidance, and gates all AI behavior behind a `AgentAIEnabled` feature flag (default: off). When AI is unavailable or disabled, the system falls back to current direct-tool-execution with zero regressions.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (`net9.0` across all projects)
**Primary Dependencies**:
- `Microsoft.Extensions.AI` 9.4.0-preview.1.25207.5 (already in Agents.csproj — IChatClient abstraction)
- `Azure.AI.OpenAI` (to add to Core.csproj — concrete Azure OpenAI client)
- `Microsoft.Extensions.AI.OpenAI` (to add to Core.csproj — .AsChatClient() adapter)
- `Azure.Identity` 1.13.2 (already in Core.csproj — DefaultAzureCredential)
- `Microsoft.EntityFrameworkCore` 9.0.0 (existing — SQLite dev / SqlServer prod)
- `Microsoft.Extensions.AI` abstraction-only in Agents.csproj (no concrete SDK reference)

**Storage**: SQLite (dev) / SQL Server (prod) via EF Core. Chat has separate `ChatDbContext` for conversation persistence. No new storage for this feature — conversation history already managed in `AgentConversationContext.MessageHistory` (in-memory per request) and `ChatDbContext` (persistent in Chat service).

**Testing**: xUnit 2.9.3 + FluentAssertions 7.0.0 + Moq 4.20.72. Baseline: 2271 unit + 33 integration tests across 134 test files. `IChatClient` mocking pattern established in `AiRemediationPlanGeneratorTests.cs` — uses `Mock<IChatClient>` with `GetResponseAsync` setup returning `ChatResponse`.

**Target Platform**: Azure Government (IL5/IL6) — `.us` endpoints, US-only regions (usgovvirginia, usgovarizona, usgovtexas). Dual-mode MCP server: stdio (GitHub Copilot/Claude integration) and HTTP (web chat via SignalR). Docker container deployment.

**Project Type**: Multi-project solution (5 src + 2 test projects). Web service (MCP HTTP) + CLI/stdio (MCP stdio) + SPA backend (Chat with SignalR).

**Performance Goals**: Simple MCP tool responses <5s, complex operations <30s. HTTP health endpoints <200ms p95. LLM calls add inherent latency — bounded by MaxToolCallRounds (default 5) and per-request timeouts.

**Constraints**: <512MB steady-state memory. Must work fully disconnected (no Azure OpenAI) — degraded mode with zero AI dependency at runtime. Azure Government endpoints only. Feature flag kill switch required.

**Scale/Scope**: 5 src projects, 2 test projects, 3 agents (Compliance: 65+ tools, Configuration: 1 tool, KnowledgeBase: 7 tools), ~73 total tools. 5 prompt files. ~2304 existing tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Assessment |
|---|-----------|--------|------------|
| I | Documentation as Source of Truth | PASS | Full spec at `specs/011-azure-openai-agents/spec.md`. No conflicting docs in `/docs/`. New configuration options will be documented in appsettings. |
| II | BaseAgent/BaseTool Architecture | PASS | Feature extends `BaseAgent` with optional `IChatClient?` — does not break or replace pattern. All agents still extend `BaseAgent`, all tools still extend `BaseTool`. `TryProcessWithAiAsync` is a new protected method, not a replacement for `ProcessAsync`. System prompts remain in `.prompt.txt` files. Tools registered via `RegisterTool()`. |
| III | Testing Standards | PASS | Spec requires 8+ new unit tests covering: TryProcessWithAiAsync, IChatClient registration, fallback, feature flags, tool-call chaining, max rounds, config validation. All 2304 existing tests must pass unchanged. IChatClient mocking pattern already established. |
| IV | Azure Government & Compliance First | PASS | Factory uses `DefaultAzureCredential` (Managed Identity for prod, Azure CLI for dev). Supports Azure Government endpoints via configurable Endpoint URL. No hardcoded credentials — ApiKey from config/Key Vault, or Managed Identity. US-region endpoints only. |
| V | Observability & Structured Logging | PASS | AI processing path will log: LLM call duration, tool-call chain (tool names + sequence), fallback-to-direct events, max-rounds-hit events, LLM errors/timeouts. Existing `ToolMetrics` instrumentation in `BaseTool.ExecuteAsync` continues to work unchanged. |
| VI | Code Quality & Maintainability | PASS | Single-responsibility: inline factory in `CoreServiceExtensions` constructs client, `BaseAgent.TryProcessWithAiAsync` handles AI loop, each agent decides AI vs. direct. All dependencies via constructor injection. XML docs on all new public types. No magic values — all thresholds from configuration. |
| VII | User Experience Consistency | PASS | `AgentResponse` envelope maintained. AI responses returned via same `Response` field. `McpChatResponse` structure unchanged. When AI is off, output identical to current behavior. |
| VIII | Performance Requirements | PASS | LLM calls bounded by MaxToolCallRounds (default 5). Feature flag allows instant disable if latency unacceptable. CancellationToken honored throughout async chain. No unbounded queries introduced. |

**Gate Result**: ALL PASS. Proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/011-azure-openai-agents/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── agent-ai-contract.md
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Configuration/
│   │   └── GatewayOptions.cs          # MODIFY: extend AzureOpenAIGatewayOptions with AgentAIEnabled, MaxToolCallRounds, Temperature
│   ├── Extensions/
│   │   └── CoreServiceExtensions.cs   # MODIFY: add IChatClient factory registration
│   └── Ato.Copilot.Core.csproj        # MODIFY: add Azure.AI.OpenAI + Microsoft.Extensions.AI.OpenAI packages
│
├── Ato.Copilot.Agents/
│   ├── Common/
│   │   ├── BaseAgent.cs               # MODIFY: add IChatClient? param, TryProcessWithAiAsync, tool-to-AIFunction conversion
│   │   └── BaseTool.cs                # NO CHANGE
│   ├── Compliance/
│   │   ├── Agents/ComplianceAgent.cs  # MODIFY: accept IChatClient?, call TryProcessWithAiAsync when AI enabled
│   │   └── Prompts/ComplianceAgent.prompt.txt  # MODIFY: append Response Guidelines + Tool Selection sections
│   ├── Configuration/
│   │   ├── Agents/ConfigurationAgent.cs        # MODIFY: accept IChatClient?, call TryProcessWithAiAsync when AI enabled
│   │   └── Prompts/ConfigurationAgent.prompt.txt # MODIFY: append Response Guidelines + Tool Selection sections
│   ├── KnowledgeBase/
│   │   ├── Agents/KnowledgeBaseAgent.cs         # MODIFY: accept IChatClient?, call TryProcessWithAiAsync when AI enabled
│   │   └── Prompts/KnowledgeBaseAgent.prompt.txt # MODIFY: append Response Guidelines + Tool Selection sections
│   └── Extensions/
│       └── ServiceCollectionExtensions.cs       # MODIFY: pass IChatClient? to agent constructors
│
├── Ato.Copilot.Mcp/
│   ├── appsettings.json               # MODIFY: add Gateway:AzureOpenAI section
│   └── Server/
│       └── McpServer.cs               # NO CHANGE (agents handle AI internally)
│
└── Ato.Copilot.Chat/
    └── (NO CHANGES — Chat connects to MCP via HTTP, AI happens at agent level inside MCP)

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── Common/
│   │   └── BaseAgentAiProcessingTests.cs   # NEW: TryProcessWithAiAsync tests
│   ├── Configuration/
│   │   └── AzureOpenAIGatewayOptionsTests.cs   # NEW: Config defaults + binding tests
│   ├── Extensions/
│   │   └── CoreServiceExtensionsAiTests.cs     # NEW: IChatClient registration tests
│   └── Agents/
│       ├── ComplianceAgentAiTests.cs       # NEW: AI-enabled ComplianceAgent tests
│       ├── ConfigurationAgentAiTests.cs    # NEW: AI-enabled ConfigurationAgent tests
│       └── KnowledgeBaseAgentAiTests.cs    # NEW: AI-enabled KnowledgeBaseAgent tests
```

**Structure Decision**: No new projects added. All changes fit within existing project boundaries — `Azure.AI.OpenAI` concrete SDK in Core (infrastructure layer), `IChatClient` abstraction already in Agents (via `Microsoft.Extensions.AI`), and agent-level AI processing stays in Agents. This maintains the current clean dependency graph: `Core` → `Agents` → `Mcp/Chat`.

## Complexity Tracking

> No constitution violations to justify. All gates pass.
