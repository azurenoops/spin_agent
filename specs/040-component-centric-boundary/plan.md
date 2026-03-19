# Implementation Plan: Component-Centric Boundary Model

**Branch**: `040-component-centric-boundary` | **Date**: 2026-03-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/040-component-centric-boundary/spec.md`

## Summary

Refactor the authorization boundary architecture so that `SystemComponent` becomes the single source of truth for all assets (People, Places, Things). This involves: (1) adding Azure resource fields to `SystemComponent`, (2) creating a new `BoundaryComponentAssignment` join entity for per-boundary include/exclude scope tracking, (3) adding Azure discovery to both the org-wide Component Library and system-level Components pages, (4) migrating existing `AuthorizationBoundary` resource rows to `SystemComponent` records, (5) linking `ComplianceFinding` records to components via `ComponentId`, and (6) simplifying the Boundary Management UI to a unified component-assignment view.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: EF Core 8, Azure.ResourceManager, Azure.ResourceManager.ResourceGraph, ASP.NET Core Minimal APIs, React 18 + TypeScript (Vite dashboard)  
**Storage**: SQLite (dev), SQL Server (prod) via EF Core  
**Testing**: xUnit, FluentAssertions, Moq (unit); WebApplicationFactory (integration)  
**Target Platform**: Azure Government (primary), Azure Commercial (secondary)  
**Project Type**: Web service (MCP server) + React dashboard SPA  
**Performance Goals**: Azure discovery of 50 resources < 3 minutes; boundary assignment < 30 seconds; migration of 1,000 rows < 60 seconds; assessment detail page load < 5 seconds  
**Constraints**: Memory < 512MB steady-state; MCP tool response < 5s for queries, < 30s for complex ops; all collections paginated  
**Scale/Scope**: ~10 concurrent users per system; typical system has 50-200 components; 1-5 boundary definitions per system

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | ✅ PASS | Spec references existing `/docs/` guides; documentation updates tracked in Story 8 |
| II. BaseAgent/BaseTool Architecture | ✅ PASS | New MCP tools will extend `BaseTool`; no agent changes needed |
| III. Testing Standards | ✅ PASS | Unit + integration tests required for all new entities, services, endpoints, tools |
| IV. Azure Government & Compliance First | ✅ PASS | Reuses existing `ArmClient` with `DefaultAzureCredential`; supports Gov + Commercial |
| V. Observability & Structured Logging | ✅ PASS | Discovery service already has Serilog logging; new tools use `BaseTool` instrumentation; BoundaryMigrationService requires explicit Serilog logging for migration start, progress, dedup counts, commit, and rollback events |
| VI. Code Quality & Maintainability | ✅ PASS | DI throughout; no magic values; XML docs on all public members |
| VII. User Experience Consistency | ✅ PASS | MCP tools follow standard envelope schema; dashboard UI follows existing patterns |
| VIII. Performance Requirements | ✅ PASS | Migration < 60s; discovery paginated; assessment view < 5s |

**Gate Result (Pre-Design)**: ALL PASS — proceed to Phase 0.

### Post-Design Re-Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | ✅ PASS | Spec Story 8 tracks doc validation; no new doc guidance needed |
| II. BaseAgent/BaseTool Architecture | ✅ PASS | 7 new tools extend `BaseTool` per contracts/mcp-tools.md |
| III. Testing Standards | ✅ PASS | Unit + integration test files listed in project structure; boundary/edge tests required per data-model.md |
| IV. Azure Government & Compliance First | ✅ PASS | Reuses `ArmClient` + `DefaultAzureCredential`; Resource Graph in Gov cloud; Entra ID behind org setting |
| V. Observability & Structured Logging | ✅ PASS | Discovery service already logged; new tools inherit `BaseTool` instrumentation (ToolMetrics); BoundaryMigrationService requires Serilog structured logging |
| VI. Code Quality & Maintainability | ✅ PASS | All new public types have XML docs; DI throughout; no magic values |
| VII. User Experience Consistency | ✅ PASS | MCP tools follow standard envelope; dashboard follows existing component/boundary patterns |
| VIII. Performance Requirements | ✅ PASS | Migration < 60s; discovery paginated; risk summary computed server-side with indexes |

**Gate Result (Post-Design)**: ALL PASS.

## Project Structure

### Documentation (this feature)

```text
specs/040-component-centric-boundary/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── dashboard-api.md # REST API contracts for dashboard endpoints
│   └── mcp-tools.md     # MCP tool contracts
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/
│   │   ├── SystemComponent.cs            # MODIFY: Add Azure resource fields
│   │   ├── BoundaryComponentAssignment.cs # NEW: Join entity
│   │   ├── ComplianceModels.cs           # MODIFY: Add ComponentId FK to ComplianceFinding
│   │   └── RmfModels.cs                  # MODIFY: Mark AuthorizationBoundary deprecated navigation
│   ├── Data/Context/
│   │   └── AtoCopilotContext.cs          # MODIFY: Add DbSet + configure new entity
│   └── Services/
│       ├── ComponentService.cs           # MODIFY: Add discovery-import, boundary assignment, component linking
│       ├── BoundaryLockService.cs        # NEW: Pessimistic boundary lock with auto-expiry
│       └── BoundaryMigrationService.cs   # NEW: Data migration service
├── Ato.Copilot.Agents/
│   └── Compliance/
│       ├── Services/
│       │   └── AzureResourceDiscoveryService.cs  # MODIFY: Add component-oriented methods
│       └── Tools/
│           └── ComponentBoundaryTools.cs          # NEW: MCP tools for boundary-component ops
├── Ato.Copilot.Mcp/
│   └── Endpoints/
│       └── DashboardEndpoints.cs         # MODIFY: Add boundary-component + discovery endpoints
└── Ato.Copilot.Dashboard/
    └── src/
        ├── api/
        │   ├── boundaries.ts             # MODIFY: Add component assignment API calls
        │   ├── components.ts             # MODIFY: Add system-level discovery
        │   └── azureDiscovery.ts         # MODIFY: Add component library discovery
        ├── pages/
        │   ├── BoundaryManagement.tsx    # MODIFY: Unified component assignment view
        │   ├── ComponentInventory.tsx    # MODIFY: Add system-level Azure discovery
        │   └── ComponentLibrary.tsx      # MODIFY (if exists) or page within existing route
        └── types/
            └── dashboard.ts              # MODIFY: Add new DTO types

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── BoundaryComponentAssignmentTests.cs    # NEW
│   ├── BoundaryMigrationServiceTests.cs       # NEW
│   └── ComponentFindingLinkageTests.cs        # NEW
└── Ato.Copilot.Tests.Integration/
    ├── BoundaryComponentEndpointTests.cs      # NEW
    ├── ComponentDiscoveryEndpointTests.cs     # NEW
    ├── ComponentFindingEndpointTests.cs       # NEW
    └── BoundaryMigrationIntegrationTests.cs   # NEW
```

**Structure Decision**: Follows existing multi-project solution layout. New entity goes in `Ato.Copilot.Core/Models/Compliance/`. New service in `Ato.Copilot.Core/Services/`. New MCP tools in `Ato.Copilot.Agents/Compliance/Tools/`. Dashboard modifications in existing files. No new projects needed.

## Complexity Tracking

> No constitution violations — no entries needed.
