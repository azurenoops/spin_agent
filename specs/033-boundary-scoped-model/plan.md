# Implementation Plan: Boundary-Scoped Model

**Branch**: `033-boundary-scoped-model` | **Date**: 2026-03-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/033-boundary-scoped-model/spec.md`

## Summary

Restructure the data model to make authorization boundaries the primary organizing container for capabilities, components, and gap analysis. Introduce `AuthorizationBoundaryDefinition` as a named security perimeter entity within a registered system. Existing `AuthorizationBoundary` resource records, `SystemComponent`, and `CapabilityControlMapping` gain FK references to boundaries. Gap analysis, narrative propagation, component inventory, and dashboard UI become boundary-scoped. Chat channels (VS Code, Teams) gain boundary-aware commands. Azure Resource Graph integration enables resource discovery and auto-suggest for both boundary creation and component creation.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (backend), TypeScript 5.7 (dashboard)
**Primary Dependencies**: ASP.NET Core, Entity Framework Core, Azure.Identity, Azure.ResourceManager.ResourceGraph (new for US8), React 19, Vite 6.0, Tailwind CSS 3.4
**Storage**: SQL Server (production), SQLite (development) via EF Core
**Testing**: xUnit, FluentAssertions, Moq (unit), WebApplicationFactory (integration)
**Target Platform**: Azure Government (primary), Azure Commercial (secondary)
**Project Type**: Web service + SPA dashboard + VS Code extension + Teams bot
**Performance Goals**: MCP tool response <5s (simple), <30s (complex). Dashboard API <200ms p95 for status endpoints.
**Constraints**: Memory <512MB steady-state. Bounded result sets with pagination (default 50). CancellationToken support on all async paths.
**Scale/Scope**: Up to 20 boundaries per system. Existing entity counts: ~50 systems, ~500 components, ~200 capabilities, ~2000 mappings.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | PASS | Feature spec is comprehensive. `/docs/` will be updated in implementation (FR-022 references). |
| II. BaseAgent/BaseTool Architecture | PASS | New MCP tools (boundary CRUD, discovery) will extend `BaseTool`. Existing `DefineBoundaryTool` already follows pattern. |
| III. Testing Standards | PASS | Unit + integration tests required for all entity changes, service layer, API endpoints, and MCP tools. Boundary-value tests for nullable FK, 0/1/20 boundaries. |
| IV. Azure Government & Compliance First | PASS | Azure Resource Graph queries will use `DefaultAzureCredential` chain. US regions only. No hardcoded credentials. |
| V. Observability & Structured Logging | PASS | Boundary operations will log create/delete/reassign events. MCP tool timing already instrumented via `BaseTool.ExecuteAsync`. |
| VI. Code Quality & Maintainability | PASS | New entity follows existing patterns (GUID PK, MaxLength, Required attributes). Service layer via DI. |
| VII. User Experience Consistency | PASS | MCP tool responses follow standard envelope schema. Dashboard components follow existing card/form patterns. |
| VIII. Performance Requirements | PASS | Boundary queries bounded by 20-per-system assumption. Azure Resource Graph paginated. All new async methods accept CancellationToken. |

**Gate Result**: ALL PASS — proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/033-boundary-scoped-model/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── api-endpoints.md
│   └── mcp-tools.md
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/
│   │   ├── RmfModels.cs              # AuthorizationBoundaryDefinition (new), AuthorizationBoundary (modify)
│   │   ├── SystemComponent.cs        # Add AuthorizationBoundaryDefinitionId FK
│   │   ├── CapabilityControlMapping.cs # Add AuthorizationBoundaryDefinitionId FK
│   │   └── SspModels.cs              # ControlImplementation (unchanged)
│   ├── Data/Context/
│   │   └── AtoCopilotContext.cs      # New DbSet, OnModelCreating relationships
│   ├── Migrations/
│   │   └── [timestamp]_Feature033_BoundaryScopedModel.cs
│   ├── Services/
│   │   ├── CapabilityService.cs      # Boundary-scoped gap analysis, narrative propagation
│   │   ├── NarrativeTemplateService.cs # Composite narrative generation
│   │   └── BoundaryDefinitionService.cs # New: CRUD for AuthorizationBoundaryDefinition
│   └── Dtos/Dashboard/
│       └── BoundaryDtos.cs           # New DTOs for boundary definition responses
├── Ato.Copilot.Agents/
│   └── Compliance/
│       ├── Services/
│       │   ├── BoundaryService.cs    # Modify to work with boundary definitions
│       │   └── AzureResourceDiscoveryService.cs # New: Azure Resource Graph integration
│       └── Tools/
│           ├── RmfRegistrationTools.cs # Modify DefineBoundaryTool, add new tools
│           └── BoundaryDefinitionTools.cs # New: boundary definition CRUD tools
├── Ato.Copilot.Mcp/                  # Register new tools
├── Ato.Copilot.Dashboard/
│   └── src/
│       ├── pages/
│       │   ├── SystemDetail.tsx      # Boundary summary section
│       │   ├── ComponentInventory.tsx # Group by boundary
│       │   ├── GapAnalysis.tsx       # Boundary selector
│       │   └── BoundaryManagement.tsx # New: boundary CRUD page
│       ├── components/
│       │   ├── cards/BoundarySummaryCard.tsx # New
│       │   ├── cards/BoundaryComparisonTable.tsx # New
│       │   └── forms/BoundaryForm.tsx # New
│       ├── api/
│       │   ├── boundaries.ts         # New: boundary API client
│       │   └── azureDiscovery.ts     # New: Azure resource discovery API client
│       └── types/dashboard.ts        # Boundary DTOs

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── BoundaryDefinitionServiceTests.cs
│   ├── CapabilityServiceBoundaryTests.cs
│   └── NarrativeTemplateBoundaryTests.cs
├── Ato.Copilot.Tests.Integration/
│   ├── BoundaryEndpointTests.cs
│   └── BoundaryMcpToolTests.cs
```

**Structure Decision**: Follows existing multi-project structure. New entity added to `Core/Models/Compliance/`. New service in `Core/Services/`. New MCP tools in `Agents/Compliance/Tools/`. Dashboard pages/components in existing directory structure. No new projects created.

## Constitution Re-Check (Post-Design)

| Principle | Status | Design Impact |
|-----------|--------|---------------|
| I. Documentation as Source of Truth | PASS | Contracts documented in `contracts/`. Data model in `data-model.md`. |
| II. BaseAgent/BaseTool Architecture | PASS | 3 new tools extend `BaseTool`. Registered via `RegisterTool()`. |
| III. Testing Standards | PASS | Unit tests for entity, service, narrative. Integration tests for API + MCP tools. Boundary-value tests specified. |
| IV. Azure Government & Compliance | PASS | `DefaultAzureCredential` with `AzureAuthorityHosts.AzureGovernment`. US regions only. |
| V. Observability | PASS | `BaseTool.ExecuteAsync` handles timing. Audit events for boundary create/delete/reassign. |
| VI. Code Quality | PASS | Follows existing entity/DTO/service patterns. DI for all services. No magic values. |
| VII. UX Consistency | PASS | Standard envelope schema. Boundary selector follows existing filter pattern. |
| VIII. Performance | PASS | Bounded by 20 boundaries/system. Composite indexes for common queries. Pagination on Azure Resource Graph. CancellationToken on all async paths. |

**Gate Result**: ALL PASS — design complete.

## Complexity Tracking

No constitution violations to justify. Design follows existing patterns with no new abstractions beyond those required by the spec.
