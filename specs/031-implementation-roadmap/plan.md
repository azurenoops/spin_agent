# Implementation Plan: Implementation Roadmap

**Branch**: `031-implementation-roadmap` | **Date**: 2026-03-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/031-implementation-roadmap/spec.md`

## Summary

Transform compliance gap analysis data into AI-driven, phased implementation roadmaps with effort estimates, risk reduction projections, and bi-directional Kanban integration. Surfaces roadmaps through three channels: MCP tools (Teams Adaptive Cards), Visual Compliance Dashboard (React SPA), and PDF export. Follows existing BaseTool/BaseAgent architecture, EF Core entity patterns, and dashboard endpoint conventions.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0  
**Primary Dependencies**: ASP.NET Core 9.0, EF Core 9.0 (SQL Server), Azure OpenAI (effort estimation), React 19, TypeScript 5, Recharts 2, Tailwind CSS 3  
**Storage**: SQL Server via `AtoCopilotContext` (EF Core, `EnsureCreatedAsync` model)  
**Testing**: xUnit + FluentAssertions + Moq (unit), WebApplicationFactory (integration)  
**Target Platform**: Docker container (Linux), Azure Government  
**Project Type**: Web service (MCP server) + SPA dashboard + Teams bot extension  
**Performance Goals**: Roadmap generation < 30s, dashboard page load < 3s, Kanban conversion < 10s  
**Constraints**: < 512MB memory steady-state, all async operations honor CancellationToken, RBAC via PIM tiers  
**Scale/Scope**: Up to 325 controls per baseline, typically 3–5 phases per roadmap (AI default, no hard cap), up to 100 items per roadmap

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. Documentation as Source of Truth | PASS | Spec exists at `/specs/031-implementation-roadmap/spec.md` with 5 clarifications resolved |
| II. BaseAgent/BaseTool Architecture | PASS | New MCP tools will extend `BaseTool` per existing pattern in `ComplianceTools.cs` |
| III. Testing Standards | PASS | Plan includes unit tests (positive + negative per method), integration tests for dashboard endpoints and MCP tools, and persona test cases in unified-rmf-test-script.md |
| IV. Azure Government & Compliance First | PASS | No new Azure service interactions — uses existing EF Core + Azure OpenAI patterns. No credential handling changes. |
| V. Observability & Structured Logging | PASS | Tool executions will log input parameters, duration, success/failure per existing BaseTool pattern |
| VI. Code Quality & Maintainability | PASS | Entity models follow existing conventions (GUID IDs, ConcurrentEntity base, XML docs). Service follows single-responsibility. |
| VII. User Experience Consistency | PASS | MCP responses use standard envelope (`McpToolResult.Success/Error`). Dashboard DTOs follow `ErrorResponse` pattern. Adaptive Cards follow `cardRouter.ts` data.type routing. |
| VIII. Performance Requirements | PASS | SC-001 (30s generation), SC-006 (3s dashboard load), SC-004 (10s Kanban conversion) all within constitution limits |

**Gate result**: PASS — no violations. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/031-implementation-roadmap/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── mcp-tools.md     # MCP tool schemas
│   └── dashboard-api.md # Dashboard REST endpoints
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
# Backend (C# / .NET 9.0)
src/Ato.Copilot.Core/
├── Models/Roadmap/
│   ├── ImplementationRoadmap.cs      # Entity
│   ├── RoadmapPhase.cs               # Entity
│   ├── RoadmapItem.cs                # Entity
│   └── RoadmapEnums.cs               # Status enums
├── Interfaces/Roadmap/
│   └── IRoadmapService.cs            # Service interface
└── Data/Context/
    └── AtoCopilotContext.cs           # Add DbSets (existing file)

src/Ato.Copilot.Mcp/
├── Services/
│   └── RoadmapService.cs             # Service implementation
├── Dtos/Dashboard/
│   └── RoadmapDtos.cs                # Dashboard DTOs
└── Endpoints/
    └── DashboardEndpoints.cs          # Add roadmap routes (existing file)

src/Ato.Copilot.Agents/
└── Compliance/Tools/
    └── RoadmapTools.cs                # MCP tools (generate, get, update, export)

# Frontend (React / TypeScript)
src/Ato.Copilot.Dashboard/src/
├── api/
│   └── roadmap.ts                     # API client
├── types/
│   └── dashboard.ts                   # Add roadmap types (existing file)
├── pages/
│   └── Roadmap.tsx                    # Roadmap page
└── components/
    └── charts/
        ├── RoadmapTimeline.tsx         # Gantt-style timeline
        └── RiskReductionCurve.tsx      # Line chart

# M365 Teams Extension
extensions/m365/src/cards/
├── roadmapCard.ts                     # Adaptive Card builder
├── cardRouter.ts                      # Add roadmap routing (existing file)
└── index.ts                           # Re-export (existing file)

# Tests
tests/Ato.Copilot.Tests.Unit/
└── Roadmap/
    ├── RoadmapServiceTests.cs
    ├── RoadmapToolsTests.cs
    └── RiskReductionCalculatorTests.cs

tests/Ato.Copilot.Tests.Integration/
└── Roadmap/
    └── RoadmapEndpointsTests.cs
```

**Structure Decision**: Follows existing multi-project layout — entities in `Core/Models/`, service interface in `Core/Interfaces/`, service implementation in `Mcp/Services/`, MCP tools in `Agents/Compliance/Tools/`, dashboard DTOs in `Mcp/Dtos/Dashboard/`, dashboard endpoints added to existing `DashboardEndpoints.cs`, React page and components in `Dashboard/src/`. No new projects needed.
