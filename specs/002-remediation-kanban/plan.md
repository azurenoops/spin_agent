# Implementation Plan: Remediation Kanban

**Branch**: `002-remediation-kanban` | **Date**: 2026-02-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-remediation-kanban/spec.md`

## Summary

Extend the Security Posture Intelligence Navigator with a Kanban-style task management layer for compliance remediation.
This feature adds four new EF Core entities (RemediationBoard, RemediationTask, TaskComment,
TaskHistoryEntry) plus two ephemeral models (SavedView, NotificationConfig) to the existing
data context. A new `IKanbanService` interface coordinates board/task/comment lifecycle,
status transition rules, validation triggers, and RBAC enforcement. New MCP tools expose
Kanban operations through the Compliance Agent's natural language interface. The existing
assessment, remediation, evidence, and document-generation services are extended to integrate
with the board — findings become tasks, remediations update task status, and POA&M generation
pulls from active boards.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0
**Primary Dependencies**: Azure.Identity 1.13, Azure.ResourceManager 1.13, Microsoft.EntityFrameworkCore 9.0, Serilog 4.2, xUnit 2.9, FluentAssertions 7.0, Moq 4.20
**Storage**: SQLite (dev) / SQL Server (prod) via EF Core — `AtoCopilotContext` extended with 4 new DbSets; new migration
**Testing**: xUnit + FluentAssertions + Moq (`dotnet test`). WebApplication + UseTestServer for integration tests.
**Target Platform**: Linux containers (Azure Government), macOS/Windows dev
**Project Type**: MCP agent server (stdio + HTTP dual-mode)
**Performance Goals**: Board creation <30s for 200 findings; status transitions <2s; board display <3s for 100 tasks; CSV export <60s for 100-task board
**Constraints**: <512MB steady-state memory; all async with CancellationToken; paginated responses; startup <10s
**Scale/Scope**: Up to 500 tasks per board; up to 50 active boards per subscription; 6 Kanban columns; 4 user roles; ~10 new MCP tools

## Architecture Decisions

### New Entities vs. Extending Existing

The Kanban feature introduces four NEW persistent entities rather than extending existing ones:

| Entity | Why New | Why Not Extend Existing |
|--------|---------|----------------------|
| `RemediationBoard` | Boards are a new concept — no existing entity represents grouped remediation work | `RemediationPlan` is execution-focused (steps, scripts, dry-run); boards are tracking-focused |
| `RemediationTask` | Tasks have unique lifecycle (6-column Kanban), assignment, comments, SLA | `ComplianceFinding` is a scan result, not a work item; `RemediationStep` is an execution step |
| `TaskComment` | Threaded discussion with edit windows is new to the system | `AuditLogEntry` is immutable system logging, not user discussion |
| `TaskHistoryEntry` | Immutable per-task changelog is different from system audit log | `AuditLogEntry` covers system-wide actions; task history is task-scoped |

`RemediationTask` references `ComplianceFinding.Id` (for traceability) and
`RemediationBoard.Id` (for grouping). This keeps existing entities unchanged.

### Service Layer Design

A single `IKanbanService` interface consolidates all Kanban operations:

```
IKanbanService
├── Board operations: Create, Get, List, Archive, Export
├── Task operations: Create, Get, Update, Move, Assign
├── Comment operations: Add, Edit, Delete, List
├── History operations: GetTaskHistory, ExportBoardHistory
├── View operations: SaveView, GetView, ListViews
└── Bulk operations: BulkAssign, BulkMove, BulkClose
```

**Rationale**: A single service avoids fragmenting Kanban logic across many interfaces.
Status transition rules, RBAC enforcement, and validation triggers are co-located.
The service delegates to `IAtoComplianceEngine` and `IRemediationEngine` for assessment
and remediation operations — it does not duplicate scan/fix logic.

### Service Lifetimes (new services only)

| Service | Lifetime | Rationale |
|---------|----------|-----------|
| `IKanbanService` / `KanbanService` | Scoped | Per-request DB operations and user context |
| `INotificationService` / `NotificationService` | Singleton | Maintains HTTP clients for webhook delivery |
| `IOverdueScanService` / `OverdueScanHostedService` | Singleton | `IHostedService` background timer for overdue detection |

### Concurrency Strategy

Concurrent task moves are handled by EF Core optimistic concurrency:
- `RemediationTask.RowVersion` — a `[Timestamp]` column (SQL Server) / trigger-based
  version (SQLite) used as a concurrency token.
- On `DbUpdateConcurrencyException`, the service returns a conflict error:
  `"Task was modified by another user. Current status: {status}. Please retry."`
- No pessimistic locking — Kanban operations are short-lived (sub-second).

### Task ID Generation

Task IDs (`REM-001`, `REM-002`, ...) are generated per board:
- `RemediationBoard` has a `NextTaskNumber` integer (starts at 1).
- On task creation: `taskId = $"REM-{board.NextTaskNumber:D3}"`, then increment.
- Concurrency-safe: the board row's `RowVersion` prevents duplicate IDs under concurrent
  inserts. On conflict, retry with incremented number.

### Notification Architecture

Notifications are fire-and-forget via `INotificationService`:
- Email: via `SmtpClient` / `MailKit` (configured in appsettings).
- Teams: via incoming webhook URL (HTTP POST with Adaptive Card JSON).
- Slack: via webhook URL (HTTP POST with Block Kit JSON).
- Notification dispatch does NOT block the primary operation. Failures are logged,
  not surfaced to the user.
- Notification preferences stored in `IAgentStateManager` (ephemeral) keyed by user ID.

### Integration Points

```
ComplianceAssessmentTool ─── (assessment completes) ──► IKanbanService.CreateBoardFromAssessment()
RemediationExecuteTool ──── ("Fix REM-NNN") ──────────► IKanbanService.ExecuteTaskRemediation()
EvidenceCollectTool ─────── ("Evidence for REM-NNN") ─► IKanbanService.CollectTaskEvidence()
DocumentGenerateTool ────── ("POA&M from board") ─────► IKanbanService.GetOpenTasksForPoam()
```

Existing services are NOT modified. `IKanbanService` wraps calls to them with
task-aware pre/post processing (status transitions, history logging, comments).

### RBAC Extension

Extend `ComplianceRoles` with new role constants:

| Spec Role | Maps To | Existing? |
|-----------|---------|-----------|
| Compliance Officer | `ComplianceRoles.Administrator` | Yes |
| Platform Engineer | `ComplianceRoles.Analyst` | Yes |
| Security Lead | `ComplianceRoles.SecurityLead` | **New** |
| Auditor | `ComplianceRoles.Auditor` | Yes |

Only one new constant needed. Permission checks in `IKanbanService` use the existing
`ComplianceAuthorizationMiddleware` for HTTP requests and a new `KanbanPermissions` helper
class for fine-grained action authorization.

### Dependency Graph (new components only)

```
ComplianceAgent → IKanbanService → AtoCopilotContext
                                 → IAtoComplianceEngine (validation re-scans)
                                 → IRemediationEngine (task remediation)
                                 → IEvidenceStorageService (task evidence)
                                 → INotificationService (async dispatch)
                                 → IAgentStateManager (saved views, notification prefs)

OverdueScanHostedService → IServiceProvider → IKanbanService (scoped per tick)
```

No circular dependencies. `IKanbanService` is the single entry point for all Kanban
operations, isolating the new feature from existing service internals.

## Constitution Check (Pre-Research)

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Gate | Status |
|---|-----------|------|--------|
| I | Documentation as Source of Truth | New features MUST update relevant `/docs/*.md` | PASS — plan.md, data-model.md, contracts/, quickstart.md will be created |
| II | BaseAgent/BaseTool Architecture | All tools MUST extend `BaseTool` | PASS — all new Kanban tools will extend `BaseTool` with `Name`, `Description`, `Parameters`, `ExecuteAsync()` |
| III | Testing Standards | 80%+ unit coverage; positive + negative; boundary tests | PASS — test plan includes unit tests for every service method, RBAC, status transitions, edit windows |
| IV | Azure Government & Compliance | Dual cloud support; no hardcoded creds; NIST mapping | PASS — no new Azure SDK interactions; Kanban uses existing engine/remediation services that already comply |
| V | Observability & Structured Logging | Tool executions MUST log input/duration/success | PASS — `KanbanService` will log all operations via Serilog; all tool invocations audited |
| VI | Code Quality & Maintainability | DI only; XML docs; no magic values; warnings-as-errors | PASS — `IKanbanService` via constructor DI; XML docs on all public types; SLA values in config |
| VII | User Experience Consistency | Standard envelope schema; actionable errors | PASS — all Kanban tool responses use `{status, data, metadata}` envelope; RBAC errors include required role |
| VIII | Performance Requirements | Status transitions <5s; board display paginated; CancellationToken | PASS — transitions <2s target; board display <3s; pagination on all list endpoints |

**Gate result: PASS** — No violations. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/002-remediation-kanban/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (MCP tool schemas)
│   └── kanban-tools.md
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Constants/
│   │   ├── ComplianceRoles.cs                   # Extend: add SecurityLead constant
│   │   └── KanbanConstants.cs                   # NEW: status names, SLA defaults, task ID format
│   ├── Data/
│   │   └── Context/
│   │       └── AtoCopilotContext.cs              # Extend: add RemediationBoard, RemediationTask, TaskComment, TaskHistoryEntry DbSets
│   ├── Interfaces/
│   │   └── Kanban/
│   │       ├── IKanbanService.cs                # NEW: board/task/comment/history/view/bulk operations
│   │       └── INotificationService.cs          # NEW: notification dispatch interface
│   ├── Models/
│   │   └── Kanban/
│   │       ├── KanbanModels.cs                  # NEW: RemediationBoard, RemediationTask, TaskComment, TaskHistoryEntry, SavedView
│   │       └── KanbanEnums.cs                   # NEW: TaskStatus, TaskSeverity, HistoryEventType, NotificationChannel
│   └── Migrations/
│       └── [timestamp]_AddKanbanEntities.cs     # NEW: EF Core migration
├── Ato.Copilot.Agents/
│   ├── Compliance/
│   │   ├── Tools/
│   │   │   ├── ComplianceTools.cs               # Extend: ComplianceAssessmentTool to offer board creation
│   │   │   └── KanbanTools.cs                   # NEW: ~10 Kanban MCP tools
│   │   ├── Services/
│   │   │   ├── KanbanService.cs                 # NEW: IKanbanService implementation
│   │   │   ├── NotificationService.cs           # NEW: INotificationService implementation
│   │   │   └── OverdueScanHostedService.cs      # NEW: IHostedService for overdue detection
│   │   └── Agents/
│   │       └── ComplianceAgent.cs               # Extend: register Kanban tools, add routing for Kanban intents
│   └── Extensions/
│       └── ServiceCollectionExtensions.cs        # Extend: register IKanbanService, INotificationService, OverdueScanHostedService
├── Ato.Copilot.Mcp/
│   ├── Tools/
│   │   └── ComplianceMcpTools.cs                # Extend: register Kanban MCP tool definitions
│   └── Server/
│       └── McpHttpBridge.cs                     # Extend: add /mcp/boards endpoint (optional)
tests/
├── Ato.Copilot.Tests.Unit/
│   ├── Services/
│   │   ├── KanbanServiceTests.cs                # NEW: comprehensive tests
│   │   └── NotificationServiceTests.cs          # NEW: notification dispatch tests
│   └── Tools/
│       └── KanbanToolTests.cs                   # NEW: tool execution tests
└── Ato.Copilot.Tests.Integration/
    └── KanbanEndpointTests.cs                   # NEW: integration tests
```

**Structure Decision**: Follows the existing multi-project layout. Kanban entities live in a
new `Models/Kanban/` namespace under Core. The `IKanbanService` interface lives in a new
`Interfaces/Kanban/` namespace. Service implementation (`KanbanService`) lives alongside
existing compliance services. New tools are in a separate `KanbanTools.cs` file. No new
projects — all additions fit within the existing 4-project structure.

## Post-Design Constitution Re-Check

After completing data-model.md, contracts/kanban-tools.md, and quickstart.md:

| # | Principle | Status | Evidence |
|---|-----------|--------|----------|
| I | Deterministic builds | PASS | No new projects. 2 new NuGet packages (MailKit, Microsoft.Extensions.Http.Resilience) pinned to minimum versions ≥4.10.0 and ≥9.0.0 respectively. `dotnet build` remains single-command. |
| II | Reproducible tests | PASS | All new service logic tested via xUnit+Moq. State transitions covered by parameterized tests. In-memory SQLite for integration tests. No external service dependencies in tests. |
| III | Convention over configuration | PASS | Follows existing patterns: `BaseTool` for tools, EF Core `DbContext` extension, DI registration in `ServiceCollectionExtensions`. Kanban enums follow same pattern as compliance enums. |
| IV | Fail-safe defaults | PASS | `TaskStatus.Backlog` default. SLA defaults from configuration with hardcoded fallbacks. `DryRun=true` on remediation scripts. Status transitions validated before execution. Concurrency conflicts fail-fast with retry guidance. |
| V | Single-subscription scope | PASS | `RemediationBoard.SubscriptionId` scopes all boards. Tasks inherit subscription from board. No cross-subscription queries. |
| VI | Structured error surfaces | PASS | 18 Kanban-specific error codes defined in contracts (e.g., `INVALID_TRANSITION`, `CONCURRENCY_CONFLICT`, `BLOCKER_COMMENT_REQUIRED`). All follow existing `errorCode + message + suggestion` pattern. |
| VII | Tool-response envelope consistency | PASS | All 13 tools use the standard `{ status, data, metadata }` envelope. Paginated tools include `pagination` object. Bulk operations use `status: "partial"` for mixed results. |
| VIII | Memory-safe agent state | PASS | `SavedView` and `NotificationConfig` stored via existing `IAgentStateManager`. Notification queue uses bounded `Channel<T>` (capacity 500). `OverdueScanHostedService` creates scoped DI per tick — no unbounded growth. |

All 8 principles PASS. No violations requiring justification.

## Complexity Tracking

> No Constitution Check violations to justify. The feature adds no new projects, follows
> existing patterns (BaseAgent/BaseTool, EF Core, DI), and stays within the single-subscription
> scope boundary.
