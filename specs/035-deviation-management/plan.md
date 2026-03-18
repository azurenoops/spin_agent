# Implementation Plan: Deviation Management

**Branch**: `035-deviation-management` | **Date**: 2026-03-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/035-deviation-management/spec.md`

## Summary

Track false positives, risk acceptances, and waivers as first-class **Deviation** records with a formal approval workflow (Pending → Approved/Denied → Expired/Revoked), evidence linkage, expiration/review cycles, and integration across the dashboard (dedicated Deviations page + cross-page indicators), all three chat surfaces (dashboard, Teams, VS Code), the Todo panel (new `deviation` and `outstanding-info` categories), and the Intelligent Suggestions engine (expiring deviations, missing evidence, outstanding information gaps). The existing `RiskAcceptance` entity is migrated into the unified Deviation model and deprecated.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (backend), TypeScript 5 / React 19 (dashboard), TypeScript 5 / Node.js (M365 Teams + VS Code extensions)
**Primary Dependencies**: EF Core 9.0, ASP.NET Core Minimal APIs, Serilog, SignalR, Recharts (frontend), @microsoft/signalr (frontend)
**Storage**: SQLite (dev) / Azure SQL (prod) via EF Core — existing `AtoCopilotContext`
**Testing**: xUnit + FluentAssertions + Moq (backend), Vitest (frontend)
**Target Platform**: Azure Government (AzureUSGovernment primary, AzureCloud secondary)
**Project Type**: Web service + React SPA dashboard + M365/VS Code extensions
**Performance Goals**: Simple queries < 5s, complex operations < 30s, HTTP status endpoints < 200ms (p95)
**Constraints**: Pagination required for all collection endpoints (default 50), CancellationToken on all async, <512MB steady-state memory
**Scale/Scope**: ~1–100 deviations per system, ~5 new MCP tools, 1 new dashboard page, 2 new Todo categories, suggestion engine extensions

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | ✅ PASS | Spec clarified via 5 Q&A rounds; all requirements documented |
| II. BaseAgent/BaseTool Architecture | ✅ PASS | All tools will extend `BaseTool`; registered via `RegisterTool()` in `ComplianceAgent` |
| III. Testing Standards | ✅ PASS | Unit + integration tests planned for entity, service, tools, endpoints; boundary/edge-case coverage |
| IV. Azure Government & Compliance | ✅ PASS | No new Azure SDK dependencies; audit trail via `DashboardActivity`; NIST 800-53 aligned |
| V. Observability & Structured Logging | ✅ PASS | All tool executions log input/duration/result; state transitions logged to audit trail |
| VI. Code Quality & Maintainability | ✅ PASS | Single-responsibility services; DI; XML docs on public members; no magic values |
| VII. User Experience Consistency | ✅ PASS | MCP tools use standard envelope (status/data/metadata); actionable errors with suggestion field |
| VIII. Performance Requirements | ✅ PASS | Paginated queries; CancellationToken throughout; bounded result sets |

**Gate result**: ✅ ALL PASS — proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/035-deviation-management/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── mcp-tools.md     # MCP tool contracts
│   └── api-endpoints.md # REST endpoint contracts
└── tasks.md             # Phase 2 output (via /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/
│   │   └── DeviationModels.cs           # Deviation entity, enums, DTOs
│   ├── Interfaces/Compliance/
│   │   └── IDeviationService.cs         # Service interface
│   ├── Services/
│   │   ├── DeviationService.cs          # Service implementation
│   │   └── TodoService.cs              # Extended: deviation + outstanding-info categories
│   └── Migrations/
│       └── YYYYMMDD_Feature035_DeviationManagement.cs
├── Ato.Copilot.Agents/
│   └── Compliance/Tools/
│       └── DeviationTools.cs            # 5 MCP tools (request, review, list, revoke, extend)
├── Ato.Copilot.Mcp/
│   └── Endpoints/
│       └── DashboardEndpoints.cs        # Extended: deviation CRUD + approve/deny/revoke/extend
├── Ato.Copilot.Dashboard/
│   └── src/
│       ├── pages/
│       │   └── DeviationsPage.tsx       # Dedicated dashboard page
│       ├── components/
│       │   ├── DeviationDetailDrawer.tsx
│       │   ├── DeviationSummaryCards.tsx
│       │   └── DeviationTable.tsx
│       └── components/chat/
│           └── phasePageSuggestions.ts   # Extended: deviation + outstanding-info suggestions
extensions/
├── m365/src/cards/
│   └── deviationCard.ts                 # Teams Adaptive Card
└── vscode/src/
    └── (finding context menu extension)

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── Services/DeviationServiceTests.cs
│   └── Tools/DeviationToolsTests.cs
└── Ato.Copilot.Tests.Integration/
    └── Endpoints/DeviationEndpointsTests.cs
```

**Structure Decision**: Follows existing project structure — new entity/service/tools in Core/Agents/Mcp layers; new React page in Dashboard; new card in M365. No new projects added.

## Complexity Tracking

> No constitution violations detected — this section is empty.

## Constitution Re-Check (Post-Design)

*GATE: Re-evaluated after Phase 1 design artifacts are complete.*

| Principle | Status | Post-Design Notes |
|-----------|--------|-------------------|
| I. Documentation as Source of Truth | ✅ PASS | All design decisions documented in research.md with rationale and alternatives |
| II. BaseAgent/BaseTool Architecture | ✅ PASS | 5 tools extend `BaseTool`; registered via `RegisterTool()` in `ComplianceAgent` |
| III. Testing Standards | ✅ PASS | Unit tests (DeviationServiceTests, DeviationToolsTests), integration tests (DeviationEndpointsTests), boundary/edge-case coverage (duplicate active, CAT I authority, max extension, orphaned finding) |
| IV. Azure Government & Compliance | ✅ PASS | Audit trail via `DashboardActivity` for all state transitions; NIST 800-53 control alignment; no new Azure SDK dependencies |
| V. Observability & Structured Logging | ✅ PASS | All tools log input/duration/result; service logs state transitions; background expiration service logs revert operations |
| VI. Code Quality & Maintainability | ✅ PASS | Single `IDeviationService` interface; DI constructor injection; XML docs; no magic values (enums for type/status) |
| VII. User Experience Consistency | ✅ PASS | MCP tools follow standard envelope (status/data/metadata); dashboard uses existing MetricCard/drawer/table patterns; actionable error messages |
| VIII. Performance Requirements | ✅ PASS | Paginated list endpoints (default 50); CancellationToken on all async methods; bounded queries with indexes on status/expiration/finding |

**Post-design gate result**: ✅ ALL PASS — no violations or complexity justifications needed.
