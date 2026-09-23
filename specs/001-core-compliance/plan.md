# Implementation Plan: Core Compliance Capabilities

**Branch**: `001-core-compliance` | **Date**: 2026-02-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-core-compliance/spec.md`

## Summary

Implement the Compliance Agent and Configuration Agent for the Security Posture Intelligence Navigator. The Compliance Agent
provides NIST 800-53 compliance assessment (resource, policy, and combined scans), remediation
(single and batch with dry-run), evidence collection, document generation (SSP/SAR/POA&M), and
compliance monitoring against Azure Government subscriptions. The Configuration Agent manages
subscription defaults, framework/baseline preferences, and environment settings through shared
state. All nine existing service interfaces in `Ato.Copilot.Core` will receive concrete
implementations backed by Azure Resource Graph, Azure Policy, and Defender for Cloud APIs.
EF Core persistence, middleware wiring, and role-based access will be activated.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0  
**Primary Dependencies**: Azure.Identity 1.13, Azure.ResourceManager 1.13, Microsoft.Extensions.AI 9.4-preview, Microsoft.EntityFrameworkCore 9.0, Serilog 4.2, xUnit 2.9, FluentAssertions 7.0, Moq 4.20  
**Storage**: SQLite (dev) / SQL Server (prod) via EF Core — `AtoCopilotContext` already defined, needs DI registration and migrations  
**Testing**: xUnit + FluentAssertions + Moq (`dotnet test`). WebApplicationFactory for integration tests.  
**Target Platform**: Linux containers (Azure Government), macOS/Windows dev  
**Project Type**: MCP agent server (stdio + HTTP dual-mode)  
**Performance Goals**: Simple queries <5s; combined assessment <60s for 50–200 resources; health endpoints <200ms p95  
**Constraints**: <512MB steady-state memory; <1GB for bulk operations; all async with CancellationToken; startup <10s  
**Scale/Scope**: Single-subscription scope for Phase 1; 20 NIST control families; 3 scan types; 3 user roles; 12+ MCP tools

## Architecture Decisions

### Service Lifetimes

| Service | Lifetime | Rationale |
|---------|----------|-----------|
| `ArmClient` | Singleton | Thread-safe, expensive to construct |
| `IAtoComplianceEngine` | Scoped | Holds per-request scan state |
| `IRemediationEngine` | Scoped | Holds per-request remediation state |
| `INistControlsService` | Singleton | Stateless catalog lookup |
| `IAzurePolicyComplianceService` | Scoped | Per-request Azure API calls |
| `IDefenderForCloudService` | Scoped | Per-request Azure API calls |
| `IEvidenceStorageService` | Scoped | Per-request DB operations |
| `IComplianceMonitoringService` | Scoped | Per-request Azure API calls |
| `IDocumentGenerationService` | Scoped | Per-request document generation |
| `IAssessmentAuditService` | Scoped | Per-request DB operations |
| `IAgentStateManager` | Singleton | Shared ConcurrentDictionary |
| `IConversationStateManager` | Singleton | Shared ConcurrentDictionary |
| `AtoCopilotContext` | Scoped | EF Core best practice |
| `ComplianceAgent` | Scoped | Per-request agent lifecycle |
| `ConfigurationAgent` | Scoped | Per-request agent lifecycle |

### Orchestration Pattern

`IAtoComplianceEngine` is the **orchestrator** — it delegates to scan-type-specific services
and merges results. It does NOT contain scan logic itself:
1. Calls `INistControlsService.GetControls()` for control catalog.
2. Calls `IAzurePolicyComplianceService.ScanAsync()` for policy findings.
3. Calls resource scan logic (internal) using `ArmClient` for resource findings.
4. Calls `IDefenderForCloudService.GetComplianceAsync()` for Defender findings.
5. Merges, deduplicates, and correlates findings by control ID.
6. Persists via `AtoCopilotContext`.

### Dependency Graph

```
ComplianceAgent → IAtoComplianceEngine → INistControlsService
                                       → IAzurePolicyComplianceService
                                       → IDefenderForCloudService
                → IRemediationEngine    → AtoCopilotContext
                → IEvidenceStorageService → AtoCopilotContext
                → IDocumentGenerationService → AtoCopilotContext
                → IComplianceMonitoringService → ArmClient
                → IAssessmentAuditService → AtoCopilotContext

ConfigurationAgent → IAgentStateManager (no Azure deps)
```

No circular dependencies. `IAgentStateManager` is consumed (read) by `ComplianceAgent`
services but only written by `ConfigurationAgent`.

### Thread Safety

- `ArmClient` (singleton): Thread-safe per Azure SDK docs.
- `IAgentStateManager` / `IConversationStateManager` (singleton): Uses `ConcurrentDictionary` — thread-safe for individual operations. Multi-step read-then-write operations on `ConfigurationSettings` MUST use locking via `SemaphoreSlim` to prevent lost updates.
- `AtoCopilotContext` (scoped): NOT thread-safe — each request gets its own instance.
- `INistControlsService` (singleton): Catalog data is read-only after initialization — no locking needed.

### Agent Architecture

- `BaseAgent` contract is sufficient for `ConfigurationAgent`. No additional hooks needed —
  `ConfigurationAgent` uses `RegisterTool()` for `ConfigurationTool` and reads/writes
  `IAgentStateManager` via constructor-injected dependency.
- Agent handoff protocol: The MCP server's intent router re-routes the **same message** to
  the target agent. It does NOT create a new tool invocation — the original user message is
  passed to the target agent's `ProcessAsync()` method.
- Agent-to-agent notification is NOT required. `ComplianceAgent` reads `IAgentStateManager`
  on each request — it always gets the latest configuration values set by
  `ConfigurationAgent`.

### State Management Boundaries

- **EF Core (persistent)**: `ComplianceAssessment`, `ComplianceFinding`, `ComplianceEvidence`,
  `ComplianceDocument`, `NistControl`, `RemediationPlan`, `AuditLogEntry`.
- **IAgentStateManager (ephemeral)**: `ConfigurationSettings`, catalog source status,
  active remediation locks.
- **IConversationStateManager (ephemeral)**: `lastAssessmentId`, `lastDiscussedControls`,
  `pendingRemediationPlanId`, `scanType`, `subscriptionId`.
- `ConfigurationSettings` are **ephemeral** — lost on server restart. The first-time
  experience prompts users to re-configure. Future enhancement: persist to DB.
- `IConversationStateManager` entries expire after **30 minutes** of inactivity. A
  background `Timer` cleans up expired entries every 5 minutes.

### Performance Architecture

- The 60-second `CancellationTokenSource` timeout applies at the **engine level** (total
  for a combined scan). Sub-scans share the same token; if the resource scan takes 50s,
  the policy scan gets 10s before cancellation.
- Resource Graph queries execute **sequentially** (due to rate limits: 15 req/5s). Each
  query fetches up to 1,000 rows. Results are streamed to the caller as they arrive —
  NOT buffered in memory.
- For bulk operations (loading 1,189 controls, assessment with 200+ resources), use
  `IAsyncEnumerable<T>` to stream results and limit peak memory. Buffer size: 100 items
  max per batch.

### Migration Strategy

- Use `db.Database.MigrateAsync()` at startup (auto-migrate).
- Single migration set targeting SQLite dialect. EF Core handles SQL translation for
  SQL Server at runtime.
- If `MigrateAsync()` fails, log the error and terminate with exit code 1. Do NOT
  continue with an un-migrated database.

## Constitution Check (Pre-Research)

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Gate | Status |
|---|-----------|------|--------|
| I | Documentation as Source of Truth | New features MUST update relevant `/docs/*.md` | PASS — docs will be created as part of this feature |
| II | BaseAgent/BaseTool Architecture | All agents MUST extend `BaseAgent`; all tools MUST extend `BaseTool` | PASS — `ComplianceAgent` already extends `BaseAgent`; `ConfigurationAgent` will follow same pattern. System prompts externalized in `*.prompt.txt`. |
| III | Testing Standards | 80%+ unit coverage; positive + negative cases; boundary tests; regression tests | PASS — test plan includes unit, integration, and manual test phases |
| IV | Azure Government & Compliance First | Dual cloud support; `DefaultAzureCredential`; no hardcoded creds; NIST control mapping | PASS — all Azure interactions will use `DefaultAzureCredential` with dual-cloud `AzureAuthorityHosts` |
| V | Observability & Structured Logging | Serilog structured logging; tool execution logging | PASS — Serilog already configured; all tool executions will log input/duration/outcome |
| VI | Code Quality & Maintainability | DI only; XML docs on all methods/variables; no magic values; warnings-as-errors | PASS — all services via constructor injection; XML docs on all methods and variables (FR-018) |
| VII | User Experience Consistency | Standard envelope schema; actionable errors; mode parity; progress feedback | PASS — all tool responses will use `{status, data, metadata}` envelope; progress streaming for long operations |
| VIII | Performance Requirements | Tool response times; memory budget; pagination; CancellationToken; startup time | PASS — performance targets aligned with spec (5s simple, 60s assessment, pagination on collections) |

**Gate result: PASS** — No violations. Proceeding to Phase 0.

### Post-Design Re-Check (after Phase 1)

| # | Principle | Verified Against | Status |
|---|-----------|-----------------|--------|
| I | Documentation | spec.md, plan.md, research.md, data-model.md, contracts/, quickstart.md all created | PASS |
| II | BaseAgent/BaseTool | `ConfigurationAgent` extends `BaseAgent`; `ConfigurationTool` extends `BaseTool`; prompt in `ConfigurationAgent.prompt.txt` | PASS |
| III | Testing Standards | Data model defines validation rules with boundary constraints; contracts define error codes; quickstart includes verification checklist | PASS |
| IV | Azure Government | Research confirms dual-cloud `ArmEnvironment.AzureGovernment`; `DefaultAzureCredential` with `AzureAuthorityHosts`; gov SQL endpoint `*.database.usgovcloudapi.net` | PASS |
| V | Observability | `AuditLogEntry` entity persists all actions; all tool contracts include `metadata.executionTimeMs` | PASS |
| VI | Code Quality | 9 service classes (single-responsibility); all via constructor DI; constants extracted to `ControlFamilies`, `ComplianceFrameworks` | PASS |
| VII | UX Consistency | Response envelope `{status, data, metadata}` in all contracts; error codes with `suggestion` field; progress feedback for assessments | PASS |
| VIII | Performance | Assessment <60s with `CancellationTokenSource` timeout; pagination via `SkipToken` on Resource Graph; bounded result sets on all collections | PASS |

**Post-design gate result: PASS** — No violations.

## Project Structure

### Documentation (this feature)

```text
specs/001-core-compliance/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (MCP tool schemas)
│   ├── compliance-tools.md
│   └── configuration-tools.md
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/                          # Models, interfaces, configuration
│   ├── Configuration/
│   │   └── GatewayOptions.cs                  # (existing)
│   ├── Constants/
│   │   ├── ComplianceRoles.cs                 # (existing)
│   │   └── ComplianceFrameworks.cs            # NEW: framework/baseline constants
│   ├── Data/
│   │   └── Context/
│   │       └── AtoCopilotContext.cs           # (existing — needs DI registration)
│   ├── Interfaces/
│   │   └── Compliance/
│   │       └── IComplianceInterfaces.cs       # (existing — 9 interfaces)
│   └── Models/
│       └── Compliance/
│           └── ComplianceModels.cs            # (existing — extend with scan type, config)
├── Ato.Copilot.State/                         # In-memory state management
│   ├── Abstractions/
│   │   └── IStateManagers.cs                  # (existing)
│   └── Implementations/
│       └── InMemoryStateManagers.cs           # (existing)
├── Ato.Copilot.Agents/                        # Agent & tool implementations
│   ├── Common/
│   │   ├── BaseAgent.cs                       # (existing)
│   │   └── BaseTool.cs                        # (existing)
│   ├── Compliance/
│   │   ├── Agents/
│   │   │   └── ComplianceAgent.cs             # (existing — enhance routing)
│   │   ├── Configuration/
│   │   │   └── ComplianceAgentOptions.cs      # (existing)
│   │   ├── Prompts/
│   │   │   └── ComplianceAgent.prompt.txt     # (existing — update)
│   │   ├── Services/                          # NEW: all 9 service implementations
│   │   │   ├── AtoComplianceEngine.cs
│   │   │   ├── RemediationEngine.cs
│   │   │   ├── NistControlsService.cs
│   │   │   ├── AzurePolicyComplianceService.cs
│   │   │   ├── DefenderForCloudService.cs
│   │   │   ├── EvidenceStorageService.cs
│   │   │   ├── ComplianceMonitoringService.cs
│   │   │   ├── DocumentGenerationService.cs
│   │   │   └── AssessmentAuditService.cs
│   │   └── Tools/                             # (existing tool shells — implement)
│   ├── Configuration/                         # NEW: Configuration Agent
│   │   ├── Agents/
│   │   │   └── ConfigurationAgent.cs
│   │   ├── Prompts/
│   │   │   └── ConfigurationAgent.prompt.txt
│   │   └── Tools/
│   │       └── ConfigurationTool.cs
│   └── Extensions/
│       └── ServiceCollectionExtensions.cs     # (existing — add new registrations)
├── Ato.Copilot.Mcp/                           # MCP server
│   ├── Program.cs                             # (existing — wire middleware, DbContext)
│   ├── Extensions/
│   │   └── McpServiceExtensions.cs            # (existing — add ConfigurationAgent)
│   ├── Middleware/
│   │   ├── AuditLoggingMiddleware.cs          # (existing — wire into pipeline)
│   │   └── ComplianceAuthorizationMiddleware.cs # (existing — wire into pipeline)
│   ├── Server/
│   │   ├── McpServer.cs                       # (existing — add agent routing)
│   │   └── McpHttpBridge.cs                   # (existing)
│   └── Tools/
│       └── ComplianceMcpTools.cs              # (existing — add configuration tools)
tests/
├── Ato.Copilot.Tests.Unit/                    # Unit tests
│   ├── Agents/
│   │   ├── ComplianceAgentTests.cs            # (existing — expand)
│   │   └── ConfigurationAgentTests.cs         # NEW
│   ├── Services/                              # NEW: service implementation tests
│   │   ├── AtoComplianceEngineTests.cs
│   │   ├── RemediationEngineTests.cs
│   │   ├── NistControlsServiceTests.cs
│   │   ├── AzurePolicyComplianceServiceTests.cs
│   │   ├── DefenderForCloudServiceTests.cs
│   │   ├── EvidenceStorageServiceTests.cs
│   │   ├── ComplianceMonitoringServiceTests.cs
│   │   ├── DocumentGenerationServiceTests.cs
│   │   └── AssessmentAuditServiceTests.cs
│   └── Tools/                                 # NEW: tool tests
│       ├── ComplianceToolTests.cs
│       └── ConfigurationToolTests.cs
└── Ato.Copilot.Tests.Integration/             # NEW: integration test project
    ├── Ato.Copilot.Tests.Integration.csproj
    └── McpToolEndpointTests.cs
```

**Structure Decision**: Follows the existing multi-project solution layout. No new projects
except `Tests.Integration`. Service implementations live in `Agents/Compliance/Services/`
alongside the agent that consumes them. Configuration Agent gets its own namespace under
`Agents/Configuration/`.

## Complexity Tracking

> No Constitution Check violations to justify.
