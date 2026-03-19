# Implementation Plan: POA&M Management

**Branch**: `039-poam-management` | **Date**: 2026-03-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/039-poam-management/spec.md`

## Summary

Dedicated POA&M Management page with component linkage, lifecycle tracking, bidirectional remediation-task sync, scan-import auto-generation, trend analytics, eMASS/OSCAL export, Jira/ServiceNow integration, and comprehensive MCP tool coverage. The feature introduces 4 new entities, extends 2 existing entities, adds 18 new MCP tools, updates 3 existing tools, and spans both the .NET backend (Core, Agents, Mcp) and the React dashboard frontend.

## Technical Context

**Language/Version**: C# / .NET 9.0 (backend); TypeScript 5.7 / React 19 (frontend)
**Primary Dependencies**: EF Core 9.0 (SqlServer + SQLite), Azure.Identity 1.13.2, Azure.AI.OpenAI 2.1.0, Serilog 4.2.0, QuestPDF 2025.7.0 (backend); React Router 7.0, Axios 1.7, Recharts 2.15, Tailwind CSS 3.4, Vite 6.0 (frontend)
**Storage**: SQL Server (production) / SQLite (dev) via `AtoCopilotContext`; Key Vault for ticketing credentials
**Testing**: xUnit 2.9.3, FluentAssertions 7.0, Moq 4.20, EF InMemory 9.0 (backend); Vitest 3.0 (frontend)
**Target Platform**: Docker container on Azure Government (Linux); React SPA served from Chat server (port 5001)
**Project Type**: Full-stack web service (MCP server + React dashboard)
**Performance Goals**: POA&M page load < 3s (SC-001); bulk creation 100+ items < 10s (SC-002); MCP tool responses < 5s simple / < 30s complex (Constitution VIII)
**Constraints**: Server-side pagination required (25/50/100 page sizes); memory < 512MB steady-state; NIST 800-53 compliance; Azure Gov data residency (US only)
**Scale/Scope**: ~500 POA&M items per system typical, 500+ edge case for bulk import; 5 persona roles; 21 MCP tools; 11 user stories; 22 FRs; 11 SCs

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Documentation as Source of Truth | PASS | Feature adds FR-020/021/022 for docs updates (US9). New `docs/guides/poam-management.md` + updates to persona guides, tool catalog, data model, RMF phases. |
| II | BaseAgent/BaseTool Architecture | PASS | All 18 new MCP tools extend `BaseTool`. Registered via `ComplianceMcpTools`. System prompts externalized in `*.prompt.txt`. |
| III | Testing Standards | PASS | Plan includes unit tests (xUnit + Moq for services, Vitest for components), integration tests (WebApplicationFactory for MCP endpoints), boundary tests for pagination/bulk/concurrency. |
| IV | Azure Government & Compliance | PASS | Key Vault for ticketing credentials (no hardcoded secrets). `DefaultAzureCredential` chain. NIST 800-53 control mapping via existing PoamItem.SecurityControlNumber. US-only data residency. |
| V | Observability & Structured Logging | PASS | All tool executions log input/duration/result via BaseTool infrastructure. Cascade operations log origin flags. Audit trail records actor/timestamp/details. |
| VI | Code Quality & Maintainability | PASS | DI throughout; single-responsibility services (PoamService, PoamSyncService, TicketingService); XML docs on all public types; no magic values (enums for status/severity). |
| VII | User Experience Consistency | PASS | Standard MCP response envelope `{ status, data, metadata }`. Actionable error messages with codes. Progress feedback for bulk operations > 2s. UI confirmation for cascade operations. |
| VIII | Performance Requirements | PASS | Server-side pagination (default 25); bounded result sets; CancellationToken on all async ops; bulk creation targets < 10s for 100+ items. |

**Gate Result**: PASS — No violations. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/039-poam-management/
├── spec.md              # Feature specification
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   ├── api-endpoints.md # Dashboard API contract (REST)
│   └── mcp-tools.md     # MCP tool contract (21 tools)
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
# Backend (.NET 9.0)
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/
│   │   └── AuthorizationModels.cs     # PoamItem (extend), PoamMilestone (existing)
│   ├── Models/Kanban/
│   │   └── KanbanModels.cs            # RemediationTask (extend nav property)
│   ├── Models/Poam/                   # NEW: PoamComponentLink, TicketingIntegration, PoamTicketSync
│   ├── Data/Context/
│   │   └── AtoCopilotContext.cs       # Add new DbSets + relationships
│   └── Services/
│       ├── PoamService.cs             # NEW: CRUD, lifecycle, pagination, bulk ops
│       ├── PoamSyncService.cs         # NEW: Bidirectional sync, cascade logic
│       └── TicketingService.cs        # NEW: Jira/ServiceNow integration
├── Ato.Copilot.Agents/
│   └── Compliance/Tools/Poam/         # NEW: 18 new BaseTool implementations
│       ├── UpdatePoamTool.cs
│       ├── GetPoamTool.cs
│       ├── ClosePoamTool.cs
│       ├── UpdatePoamMilestoneTool.cs
│       ├── LinkPoamComponentTool.cs
│       ├── UnlinkPoamComponentTool.cs
│       ├── PoamByComponentTool.cs
│       ├── LinkPoamTaskTool.cs
│       ├── UnlinkPoamTaskTool.cs
│       ├── CreateTaskFromPoamTool.cs
│       ├── PoamMetricsTool.cs
│       ├── PoamTrendTool.cs
│       ├── ExportPoamTool.cs
│       ├── BulkUpdatePoamTool.cs
│       ├── BulkCreatePoamFromFindingsTool.cs
│       ├── ConfigureTicketingTool.cs
│       ├── SyncPoamTicketTool.cs
│       └── BulkSyncTicketsTool.cs
├── Ato.Copilot.Mcp/
│   └── Endpoints/
│       └── DashboardEndpoints.cs      # New POA&M REST endpoints for dashboard
└── Ato.Copilot.Dashboard/            # Dashboard server (serves React SPA)

# Frontend (React 19 / TypeScript 5.7)
src/Ato.Copilot.Dashboard/src/
├── pages/
│   ├── PoamManagement.tsx            # NEW: Main POA&M page
│   ├── Remediation.tsx               # MODIFY: Remove POA&M UI, add linked column
│   └── ComponentInventory.tsx        # EXISTING: Wire into router
├── components/
│   ├── poam/                         # NEW: POA&M-specific components
│   │   ├── PoamSummaryCards.tsx
│   │   ├── PoamTable.tsx
│   │   ├── PoamDetailDrawer.tsx
│   │   ├── PoamCreateForm.tsx
│   │   ├── PoamLifecycleActions.tsx
│   │   ├── PoamTrendCharts.tsx
│   │   ├── PoamExportDialog.tsx
│   │   ├── ComponentPicker.tsx
│   │   ├── SyncIndicator.tsx
│   │   └── CascadeConfirmDialog.tsx
│   └── layout/
│       └── SystemLayout.tsx          # MODIFY: Add Components + POA&M nav items
├── api/
│   ├── poam.ts                       # NEW: POA&M API client
│   └── remediation.ts               # MODIFY: Add linked POA&M column support
├── hooks/
│   └── usePoam.ts                    # NEW: POA&M data hooks
└── types/
    └── poam.ts                       # NEW: POA&M TypeScript interfaces

# Tests
tests/
├── Ato.Copilot.Tests.Unit/
│   ├── Services/
│   │   ├── PoamServiceTests.cs       # NEW
│   │   ├── PoamSyncServiceTests.cs   # NEW
│   │   └── TicketingServiceTests.cs  # NEW
│   └── Tools/Poam/                   # NEW: Tool unit tests
└── Ato.Copilot.Tests.Integration/
    └── Poam/                         # NEW: API integration tests

# Documentation
docs/
├── guides/
│   ├── poam-management.md            # NEW
│   └── remediation-kanban.md         # MODIFY: Refocused scope
├── architecture/
│   ├── agent-tool-catalog.md         # MODIFY: Add 21 tool entries
│   └── data-model.md                 # MODIFY: Add 3 new entities
├── reference/
│   └── tool-inventory.md             # MODIFY: Add POA&M tool category
└── rmf-phases/
    ├── assess.md                     # MODIFY: Finding → POA&M auto-creation
    ├── monitor.md                    # MODIFY: POA&M trend tracking
    └── authorize.md                  # MODIFY: POA&M export for auth packages
```

**Structure Decision**: Full-stack web application pattern. Backend services in Core (data + logic), tool implementations in Agents (MCP), REST endpoints in Mcp (dashboard API). Frontend React components in Dashboard with page-per-feature routing. Tests parallel the source structure.

## Complexity Tracking

> No Constitution Check violations — table not required.

## Constitution Re-Check (Post Phase 1 Design)

| # | Principle | Status | Post-Design Verification |
|---|-----------|--------|--------------------------|
| I | Documentation as Source of Truth | PASS | US9 + FR-020/021/022 cover 7 doc areas. `data-model.md` and `contracts/` provide implementation references. |
| II | BaseAgent/BaseTool Architecture | PASS | 18 tools specified in `contracts/mcp-tools.md` — all extend BaseTool, `compliance_` prefix, registered via ComplianceMcpTools. |
| III | Testing Standards | PASS | ConcurrentEntity for optimistic concurrency. PoamHistoryEntry insert-only audit. R-001 identifies cascade edge cases (circular prevention, conflict resolution). |
| IV | Azure Government & Compliance | PASS | TicketingIntegration.KeyVaultSecretUri stores reference, never credential. SecretClient.GetSecretAsync for retrieval. DefaultAzureCredential chain. |
| V | Observability & Structured Logging | PASS | PoamHistoryEntry with 17 event types. CascadeOrigin enum tracks propagation source. BaseTool auto-logs tool executions. |
| VI | Code Quality & Maintainability | PASS | 3 single-responsibility services (PoamService, PoamSyncService, TicketingService). ITicketingProvider interface for extensibility. No magic values — enums throughout. |
| VII | User Experience Consistency | PASS | API contract follows existing ErrorResponse + PaginatedResponse patterns. 10 error codes with suggestions. CascadeConfirmDialog in UI. |
| VIII | Performance Requirements | PASS | Server-side pagination (25/50/100). 10 indexes for filter/sort. Bulk operations with progress. CancellationToken on all async ops. |

**Gate Result**: PASS — No violations introduced by Phase 1 design.
