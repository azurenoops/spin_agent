# Implementation Plan: Org-Level Control Inheritance

**Branch**: `044-org-control-inheritance` | **Date**: 2026-03-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/044-org-control-inheritance/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Move default control inheritance designations from per-system manual CSP profile application to org-level automatic derivation from org-wide capabilities and their control mappings. A new `OrgInheritanceDefault` table stores derived defaults. Systems inherit org defaults on baseline selection, ISSMs override per-system, and CRM generation uses effective inheritance (org defaults + overrides). The Apply CSP Profile action becomes optional.

## Technical Context

**Language/Version**: C# 13 / .NET 9, TypeScript 5 / React 19  
**Primary Dependencies**: EF Core 9, ASP.NET Core Minimal APIs, Vite 6, Tailwind CSS 3, Axios  
**Storage**: SQL Server (EF Core migrations, `AtoCopilotContext`)  
**Testing**: xUnit + FluentAssertions + Moq (unit), WebApplicationFactory (integration)  
**Target Platform**: Docker containers (Linux), Azure Government  
**Project Type**: Web service + SPA dashboard  
**Performance Goals**: Org-default derivation <5s, inheritance page load <5s, CRM generation <30s  
**Constraints**: Synchronous inline derivation (no background jobs), single-org scope  
**Scale/Scope**: ~416 controls per Moderate baseline, ~11 org-wide capabilities, ~5 registered systems

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | PASS | Spec exists at `/specs/044-org-control-inheritance/spec.md`; docs update required post-implementation |
| II. BaseAgent/BaseTool Architecture | N/A | No new agents or tools — this feature extends existing dashboard services |
| III. Testing Standards | PASS | Unit tests for `OrgInheritanceService`, integration tests for new API endpoints, boundary tests for derivation edge cases |
| IV. Azure Gov & Compliance First | PASS | No new Azure service interactions; existing credential patterns apply; NIST 800-53 control model preserved |
| V. Observability & Structured Logging | PASS | Derivation and propagation events will use existing Serilog structured logging patterns |
| VI. Code Quality & Maintainability | PASS | New service follows single-responsibility (derivation logic separate from propagation); DI-injected via constructor |
| VII. User Experience Consistency | PASS | Existing response envelope pattern; visual indicators follow existing badge/filter patterns in dashboard |
| VIII. Performance Requirements | PASS | Derivation operates on ~416 controls max per baseline; synchronous inline is well within 5s target |

**Gate Result: PASS** — No violations. Proceed to Phase 0.  
**Post-Design Re-Check: PASS** — After Phase 1 design, no new violations. New `OrgInheritanceService` uses constructor DI (VI), includes test plan (III), structured logging for derivation events (V), bounded queries with pagination (VIII), standard response envelopes (VII).

## Project Structure

### Documentation (this feature)

```text
specs/044-org-control-inheritance/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/
│   │   ├── RmfModels.cs              # Extend: add OrgInheritanceDefault entity, DesignationSource enum
│   │   └── DashboardEnums.cs         # Extend: add InheritanceChangeSource.OrgDerived, OrgPropagation
│   ├── Interfaces/Compliance/
│   │   └── IOrgInheritanceService.cs  # NEW: org derivation + propagation contract
│   ├── Data/Context/
│   │   └── AtoCopilotContext.cs       # Extend: add DbSet<OrgInheritanceDefault>, config
│   └── Migrations/
│       └── [timestamp]_Feature044_OrgControlInheritance.cs  # NEW
├── Ato.Copilot.Agents/
│   └── Compliance/Services/
│       ├── OrgInheritanceService.cs    # NEW: derivation, propagation, revert logic
│       └── BaselineService.cs          # Extend: call org propagation on baseline selection
├── Ato.Copilot.Mcp/
│   └── Endpoints/
│       └── DashboardEndpoints.cs       # Extend: new org-default endpoints, modify inheritance list
└── Ato.Copilot.Dashboard/
    └── src/
        ├── api/
        │   └── inheritance.ts          # Extend: org-default API calls
        └── pages/
            └── ControlInheritance.tsx   # Extend: source badges, filters, button layout, org view

tests/
├── Ato.Copilot.Tests.Unit/
│   └── Services/
│       └── OrgInheritanceServiceTests.cs  # NEW: derivation, precedence, propagation tests
└── Ato.Copilot.Tests.Integration/
    └── Endpoints/
        └── OrgInheritanceEndpointTests.cs  # NEW: API endpoint integration tests
```

**Structure Decision**: Extends existing project structure — new service in `Ato.Copilot.Agents`, new entity in `Ato.Copilot.Core`, new endpoints in `Ato.Copilot.Mcp`, frontend changes in `Ato.Copilot.Dashboard`. No new projects required.

## Complexity Tracking

No constitution violations. No complexity justifications needed.
