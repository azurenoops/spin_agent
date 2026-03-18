# Implementation Plan: Org-Wide Risk Solutions & Context-Aware Narrative Generation

**Branch**: `036-risk-solutions` | **Date**: 2026-03-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/036-risk-solutions/spec.md`

## Summary

Enrich the SSP narrative generation engine to produce 3PAO-ready narratives by embedding linked component names (People, Places, Things), boundary context, and responsible personnel into auto-generated text. Refactor SystemComponent from system-scoped to org-wide with a new ComponentSystemAssignment join entity for multi-system reuse. Enable cascade narrative regeneration with per-system transactional integrity, NarrativeVersion audit trail, and impact preview.

## Technical Context

**Language/Version**: C# 9 / .NET 8 (backend), TypeScript / React 18 (dashboard)
**Primary Dependencies**: EF Core 8 (SQL Server), ASP.NET Minimal APIs, IChatClient (Azure OpenAI), Vite, TailwindCSS
**Storage**: SQL Server (Docker, EnsureCreated + EnsureSchemaAdditions pattern)
**Testing**: xUnit + FluentAssertions + Moq (unit), WebApplicationFactory (integration)
**Target Platform**: Linux containers (Docker Compose), Azure Government
**Project Type**: Web service + SPA dashboard
**Performance Goals**: Cascade regeneration of 500 narratives within 10 seconds (SC-001)
**Constraints**: Per-system transactional batches (FR-015), NarrativeVersion on every cascade update (FR-016), AI fallback to deterministic templates when AI is disabled (FR-018)
**Scale/Scope**: Up to 5 systems x 100 controls x 5 capabilities = 2,500 max narratives affected per cascade operation

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Design Gate (Phase 0)

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | PASS | Spec follows spec-template. Plan follows plan-template. |
| II. BaseAgent/BaseTool Architecture | N/A | No new agents or tools in this feature. |
| III. Testing Standards | PASS | Unit tests for NarrativeTemplateService, ComponentService, CapabilityService cascade logic. Integration tests for new endpoints. |
| IV. Azure Government & Compliance First | PASS | No new Azure interactions. Existing Managed Identity / Key Vault patterns unchanged. |
| V. Observability & Structured Logging | PASS | Cascade operations will log per-system narrative counts and failures via Serilog. |
| VI. Code Quality & Maintainability | PASS | BoundaryMappingContext extended with component fields. Single-responsibility maintained in NarrativeTemplateService. |
| VII. User Experience Consistency | PASS | Impact preview follows existing modal pattern. Component library uses existing PageLayout shell. |
| VIII. Performance Requirements | PASS | Cascade within 10s for 500 narratives. Paginated component library. CancellationToken honored. |

### Post-Design Re-Evaluation (Phase 1)

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | PASS | research.md, data-model.md, contracts/ all created under specs/036-risk-solutions/. |
| II. BaseAgent/BaseTool Architecture | N/A | No agents/tools. Confirmed: all changes are in Core Services and Dashboard endpoints. |
| III. Testing Standards | PASS | data-model.md specifies test files: NarrativeTemplateServiceTests.cs, ComponentServiceTests.cs, CapabilityServiceCascadeTests.cs (new), ComponentLibraryEndpointTests.cs (new). Boundary-value tests required for nullable RegisteredSystemId, empty component lists, max-length names. |
| IV. Azure Government & Compliance First | PASS | No new Azure service interactions. ComponentSystemAssignment uses GUIDs, no cloud-specific dependencies. |
| V. Observability & Structured Logging | PASS | Cascade operations per contracts: log per-system counts, skipped custom narratives, failed systems. ChangeReason field on NarrativeVersion provides audit trail. |
| VI. Code Quality & Maintainability | PASS | ComponentContext record is 3 fields. BoundaryMappingContext adds 1 optional param (backward-compatible). ComponentSystemAssignment entity follows existing join-entity pattern. Single-responsibility: cascade logic stays in CapabilityService/ComponentService, template logic stays in NarrativeTemplateService. |
| VII. User Experience Consistency | PASS | Impact preview endpoint returns uniform schema (totalNarratives, totalSystems, customSkipped, bySystem[]). Component library API follows existing paginated list pattern (items, totalCount, page, pageSize). |
| VIII. Performance Requirements | PASS | Cascade batched per-system with transactions. Impact preview is read-only count query. Component library paginated (default 50). CancellationToken to be honored on all async cascade methods. |

## Project Structure

### Documentation (this feature)

```text
specs/036-risk-solutions/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research findings
├── data-model.md        # Phase 1 entity definitions
├── contracts/           # Phase 1 API contracts
│   ├── component-library-api.md
│   └── narrative-generation-api.md
└── tasks.md             # Phase 2 output (speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/
│   │   ├── SystemComponent.cs           # MODIFIED: RegisteredSystemId → nullable
│   │   └── ComponentSystemAssignment.cs # NEW: join entity
│   ├── Services/
│   │   ├── NarrativeTemplateService.cs  # MODIFIED: enriched generation + AI-assisted mode via IChatClient
│   │   ├── CapabilityService.cs         # MODIFIED: cascade logic with component/boundary context
│   │   └── ComponentService.cs          # MODIFIED: org-wide CRUD + system assignments
│   ├── Prompts/
│   │   └── NarrativeGeneration.prompt.txt  # NEW: system prompt for AI narrative generation
│   └── Data/Context/
│       └── AtoCopilotContext.cs         # MODIFIED: new entity config, updated indexes
├── Ato.Copilot.Mcp/
│   ├── Endpoints/DashboardEndpoints.cs  # MODIFIED: new /components endpoints, impact preview
│   └── Program.cs                       # MODIFIED: EnsureSchemaAdditions for migration
├── Ato.Copilot.Dashboard/src/
│   ├── pages/
│   │   ├── ComponentLibrary.tsx         # NEW: org-wide component library page
│   │   └── ComponentInventory.tsx       # MODIFIED: system-scoped view uses assignments
│   ├── components/layout/
│   │   ├── PageLayout.tsx               # MODIFIED: add Components to top nav
│   │   └── SystemLayout.tsx             # MODIFIED: remove Components from side nav
│   ├── api/
│   │   └── components.ts               # MODIFIED: new org-wide + assignment endpoints
│   └── App.tsx                          # MODIFIED: add /components route

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── Services/NarrativeTemplateServiceTests.cs  # MODIFIED: test enriched templates
│   ├── Services/ComponentServiceTests.cs          # MODIFIED: test org-wide CRUD
│   └── Services/CapabilityServiceCascadeTests.cs  # NEW: test cascade with components
└── Ato.Copilot.Tests.Integration/
    └── ComponentLibraryEndpointTests.cs           # NEW: integration tests
```

**Structure Decision**: Follows existing project layout. No new projects needed — all changes are within existing Core, Mcp, Dashboard, and Tests projects.
