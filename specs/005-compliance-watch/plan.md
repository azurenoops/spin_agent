# Implementation Plan: Compliance Watch

**Branch**: `005-compliance-watch` | **Date**: 2026-02-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/005-compliance-watch/spec.md`

## Summary

Compliance Watch adds continuous compliance monitoring and alerting to the Security Posture Intelligence Navigator. The system captures compliant baselines after assessments, runs scheduled and event-driven compliance checks, detects drift from baselines, creates typed alerts with a full lifecycle (NEW → ACKNOWLEDGED → IN_PROGRESS → RESOLVED/DISMISSED/ESCALATED), supports alert rules, suppression, multi-channel notifications, escalation paths, correlation/grouping, historical queries, Kanban integration, and opt-in auto-remediation. Implementation extends the existing `IComplianceMonitoringService`, adds new entities to `AtoCopilotContext`, creates 23 new MCP tools following the `BaseTool` pattern, and introduces two new `BackgroundService` implementations for scheduled monitoring and escalation.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0  
**Primary Dependencies**: EF Core 9.0, Azure.ResourceManager, Serilog, Microsoft.Extensions.Hosting, System.Threading.Channels  
**Storage**: SQLite (development) / SQL Server (production) via `AtoCopilotContext`; existing patterns — `IDbContextFactory<AtoCopilotContext>` for Singleton services  
**Testing**: xUnit 2.9.3 + FluentAssertions 7.0.0 + Moq 4.20.72 — currently 974 tests (901 unit + 73 integration)  
**Target Platform**: Azure Government (primary), Azure Commercial (secondary); dual-mode MCP server (stdio + HTTP)  
**Project Type**: MCP server (Model Context Protocol) — agent-based compliance tooling  
**Performance Goals**: Simple queries < 5s, complex operations < 30s, health endpoints < 200ms p95. Event-driven detection within 5 minutes. Notification delivery within 2 minutes for Critical/High.  
**Constraints**: Steady-state memory < 512MB, bounded result sets (default page size 50), all async operations honor `CancellationToken`, server startup < 10s  
**Scale/Scope**: Single-subscription to multi-subscription monitoring, 2-year alert retention (configurable to 7 years), 90-day daily + 2-year weekly compliance snapshots

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Documentation as Source of Truth | PASS | Plan and spec live under `/specs/005-compliance-watch/`. New `/docs/` pages will be created for monitoring configuration and alert management. |
| II | BaseAgent/BaseTool Architecture | PASS | All new tools extend `BaseTool` with `Name`, `Description`, `Parameters`, `ExecuteCoreAsync()`. Registered via `RegisterTool()` in `ComplianceAgent` constructor. System prompts externalized in `*.prompt.txt`. |
| III | Testing Standards | PASS | Unit tests for every new service/tool method (positive + negative). Integration tests for every new MCP tool endpoint. Boundary tests for alert SLA windows, pagination limits, frequency intervals. Target 80%+ coverage. |
| IV | Azure Government & Compliance First | PASS | Dual-cloud environment support maintained. Activity Log events accessed via `Azure.ResourceManager`. No hardcoded credentials. NIST 800-53 control family mappings preserved in alert rules. |
| V | Observability & Structured Logging | PASS | All new tools instrumented via `BaseTool.ExecuteAsync()` (Stopwatch + `ToolMetrics`). Background services log execution duration, alert counts, and errors. New `ToolMetrics` tags for monitoring operations. |
| VI | Code Quality & Maintainability | PASS | Single-responsibility services (monitoring engine, alert manager, notification dispatcher, escalation handler, correlation engine). Constructor DI only. XML docs on all public members. Named constants for SLA windows, rate limits, intervals. |
| VII | User Experience Consistency | PASS | New monitoring tools follow existing response patterns. Kanban-style JSON envelope for alert tools. Consistent error codes and suggestion fields. Compliance context (control family, framework) included in all alert outputs. |
| VIII | Performance Requirements | PASS | Alert queries bounded/paginated (default 50). All async methods accept `CancellationToken`. Background timers configurable. No unbounded `SELECT *`. Simple alert queries < 5s. |

**Gate result: PASS** — No violations. Proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/005-compliance-watch/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   └── tool-responses.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/
│   │   └── Compliance/
│   │       └── ComplianceModels.cs         # + MonitoringConfiguration, ComplianceBaseline,
│   │                                       #   ComplianceAlert, AlertRule, SuppressionRule,
│   │                                       #   EscalationPath, AlertNotification,
│   │                                       #   ComplianceSnapshot, AlertIdCounter,
│   │                                       #   AutoRemediationRule, enums
│   ├── Interfaces/
│   │   └── Compliance/
│   │       └── ComplianceInterfaces.cs     # + IComplianceWatchService, IAlertManager,
│   │                                       #   IAlertNotificationService, IEscalationService,
│   │                                       #   IAlertCorrelationService, IComplianceEventSource
│   ├── Configuration/
│   │   └── GatewayOptions.cs               # + MonitoringOptions, AlertOptions,
│   │                                       #   NotificationOptions, EscalationOptions,
│   │                                       #   RetentionPolicyOptions
│   └── Data/
│       └── Context/
│           └── AtoCopilotContext.cs         # + 10 new DbSets + entity config
│
├── Ato.Copilot.Agents/
│   └── Compliance/
│       ├── Services/
│       │   ├── ComplianceWatchService.cs        # Core monitoring engine
│       │   ├── AlertManager.cs                  # Alert CRUD + lifecycle
│       │   ├── AlertNotificationService.cs      # Multi-channel dispatch
│       │   ├── AlertCorrelationService.cs       # Grouping + anomaly detection
│       │   ├── ComplianceWatchHostedService.cs   # Scheduled monitoring loop
│       │   └── EscalationHostedService.cs        # SLA expiry + escalation
│       ├── Tools/
│   │   └── ComplianceWatchTools.cs           # 23 new BaseTool implementations
│       └── Prompts/
│           └── ComplianceAgent.prompt.txt        # Updated system prompt
│
├── Ato.Copilot.Mcp/
│   └── Tools/
│       └── ComplianceMcpTools.cs                 # + 23 new MCP method wrappers
│
└── Ato.Copilot.State/                            # No changes expected

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── Services/
│   │   ├── ComplianceWatchServiceTests.cs
│   │   ├── AlertManagerTests.cs
│   │   ├── AlertNotificationServiceTests.cs
│   │   ├── AlertCorrelationServiceTests.cs
│   │   └── EscalationServiceTests.cs
│   └── Tools/
│       └── ComplianceWatchToolTests.cs
│
└── Ato.Copilot.Tests.Integration/
    └── Tools/
        └── ComplianceWatchIntegrationTests.cs
```

**Structure Decision**: Feature 005 follows the existing multi-project layout established by Features 001–004. No new projects are needed — all new code fits within `Ato.Copilot.Core` (models, interfaces, configuration), `Ato.Copilot.Agents` (services, tools, background services), and `Ato.Copilot.Mcp` (tool exposure). This preserves the 4-project architecture: Core → Agents → Mcp → State.

## Complexity Tracking

> No constitution violations detected. All new code fits within the existing 4-project structure and follows established patterns (BaseTool, Singleton services with IDbContextFactory, BackgroundService). No complexity justifications needed.

## Post-Design Constitution Re-Check

*Re-evaluated after Phase 1 design completion.*

| # | Principle | Status | Post-Design Notes |
|---|-----------|--------|-------------------|
| I | Documentation as Source of Truth | PASS | Plan, research, data-model, contracts, and quickstart all generated under `/specs/005-compliance-watch/`. |
| II | BaseAgent/BaseTool Architecture | PASS | 23 new tools defined in contracts, each extending `BaseTool`. Tool registration pattern unchanged. |
| III | Testing Standards | PASS | Quickstart identifies 7 test files covering all services and tools. Boundary tests planned for SLA windows, pagination, rate limits. |
| IV | Azure Government & Compliance First | PASS | Event-driven monitoring uses Activity Log polling (works in both Gov and Commercial). Control family mappings preserved in AlertRule defaults. |
| V | Observability & Structured Logging | PASS | Background services log run duration and alert counts. All tools instrumented via BaseTool wrapper. ToolMetrics extended for monitoring operations. |
| VI | Code Quality & Maintainability | PASS | 6 single-responsibility service interfaces defined. No service-locator pattern. All entities have XML docs planned. Named constants for all SLA/rate-limit values. |
| VII | User Experience Consistency | PASS | All 23 tool contracts include standard JSON envelope, error codes, and suggestion fields. Pagination support on list endpoints. |
| VIII | Performance Requirements | PASS | Data model includes indexes for all common query patterns. Bounded result sets enforced. CancellationToken on all async interfaces. |

**Post-design gate result: PASS** — No violations introduced during design.
