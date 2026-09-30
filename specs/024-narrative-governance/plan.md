# Implementation Plan: Narrative Governance — Version Control + Approval Workflow

**Branch**: `024-narrative-governance` | **Date**: 2026-03-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/024-narrative-governance/spec.md`

## Summary

Add version history, line-level diffing, rollback, ISSM approval workflows, and
a control-oriented dashboard workspace to per-control SSP narratives
(`ControlImplementation`). The dashboard reuses the current implementation,
proposal, reference, evidence, responsibility, and immutable version stores through
an aggregate read projection; it does not add a parallel narrative store.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0; TypeScript 5.7 / React 19
**Primary Dependencies**: ASP.NET Core, EF Core 9.0.0, Serilog, DiffPlex, React Router, Axios
**Storage**: SQLite (dev) / SQL Server (prod) via EF Core dual-provider  
**Testing**: xUnit, FluentAssertions, Moq  
**Target Platform**: Azure Government (Linux containers)  
**Project Type**: MCP server plus React dashboard
**Performance Goals**: Simple queries <5s, batch operations (≤50 controls) <5s  
**Constraints**: <512MB steady-state memory, paginated result sets (default 50)  
**Scale/Scope**: ~325 controls per Moderate baseline, unlimited version retention

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Documentation as Source of Truth | ✅ PASS | Dashboard scope is documented in FR-033–FR-047 and tracked by azurenoops/spin_agent#1048 |
| II | BaseAgent/BaseTool Architecture | ✅ PASS | All 8 new tools extend `BaseTool`; registered via `RegisterTool()` in `ComplianceAgent` |
| III | Testing Standards | ✅ PASS | Unit tests (service layer) + integration tests (tool endpoints) + boundary/edge cases specified |
| IV | Azure Government & Compliance | ✅ PASS | No new Azure interactions; narrative governance maps to NIST CM-3, CM-5, AU-12 |
| V | Observability & Structured Logging | ✅ PASS | Tool executions logged per existing pattern; state transitions auditable (FR-022) |
| VI | Code Quality & Maintainability | ✅ PASS | New service interface (`INarrativeGovernanceService`), separate tool file, no duplication |
| VII | User Experience Consistency | ✅ PASS | Standard `{ status, data, metadata }` response envelope; actionable error messages with codes |
| VIII | Performance Requirements | ✅ PASS | Paginated history (default 50), batch ops ≤5s for 50 controls, `CancellationToken` support |

**Gate result**: ✅ ALL PASS — the user approved and the dashboard redesign is
tracked by azurenoops/spin_agent#1048.

**Post-Phase 1 re-check**: ✅ Design artifacts (data-model.md, contracts/) confirm all principles satisfied. DiffPlex is MIT-licensed and .NET Standard 2.0 compatible. No new Azure service dependencies.

## Project Structure

### Documentation (this feature)

```text
specs/024-narrative-governance/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0: Research decisions
├── data-model.md        # Phase 1: Entity design
├── quickstart.md        # Phase 1: Developer quickstart
├── contracts/
│   └── tool-contracts.md  # Phase 1: Tool parameter/response contracts
├── checklists/
│   └── requirements.md   # Spec quality checklist
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/
│   │   ├── SspModels.cs                          # MODIFY: Add fields to ControlImplementation
│   │   └── NarrativeGovernanceModels.cs           # NEW: NarrativeVersion, NarrativeReview, ReviewDecision
│   ├── Interfaces/Compliance/
│   │   ├── ISspService.cs                         # MODIFY: Update WriteNarrativeAsync signature
│   │   └── INarrativeGovernanceService.cs         # NEW: Governance service interface
│   └── Data/Context/
│       └── AtoCopilotContext.cs                   # MODIFY: Add DbSets + entity config
│
├── Ato.Copilot.Agents/
│   ├── Compliance/
│   │   ├── Services/
│   │   │   ├── SspService.cs                      # MODIFY: Enhance WriteNarrativeAsync for versioning
│   │   │   └── NarrativeGovernanceService.cs      # NEW: Service implementation
│   │   ├── Tools/
│   │   │   ├── SspAuthoringTools.cs               # MODIFY: Add params to WriteNarrativeTool
│   │   │   └── NarrativeGovernanceTools.cs        # NEW: 8 tool classes
│   │   └── Agents/
│   │       └── ComplianceAgent.cs                 # MODIFY: RegisterTool() for 8 new tools
│   └── Extensions/
│       └── ServiceCollectionExtensions.cs         # MODIFY: DI registration

tests/
├── Ato.Copilot.Tests.Unit/
│   └── Compliance/
│       └── NarrativeGovernanceServiceTests.cs     # NEW: Unit tests
└── Ato.Copilot.Tests.Integration/
    └── Compliance/
        └── NarrativeGovernanceToolTests.cs        # NEW: Integration tests

docs/
├── architecture/
│   ├── agent-tool-catalog.md                      # MODIFY: 8 new entries + update write_narrative
│   └── data-model.md                              # MODIFY: New entities, fields, ER diagram
├── persona-test-cases/
│   ├── environment-checklist.md                   # MODIFY: Add new tools
│   └── tool-validation.md                         # MODIFY: Add new tools
└── guides/
    ├── engineer-guide.md                          # MODIFY: Narrative versioning workflows
    ├── issm-guide.md                              # MODIFY: Review/approval workflows
    └── sca-guide.md                               # MODIFY: Audit trail access

src/Ato.Copilot.Dashboard/
├── src/pages/NarrativeWorkspace.tsx               # MODIFY: compact page composition and preserved sub-workflows
├── src/features/narratives/                       # NEW: list, drawer, statement/evidence/history presentation
├── src/api/narratives.ts                          # MODIFY: validated workspace projection/detail/history contract
└── src/__tests__/pages/                           # MODIFY: list/detail/navigation/permission regressions

src/Ato.Copilot.Mcp/Endpoints/Dashboard/
└── DashboardNarrativeWorkspaceEndpoints.cs        # NEW: tenant-scoped aggregate read projection
```

**Structure Decision**: Follows the existing project structure — models in `Ato.Copilot.Core`, service + tools in `Ato.Copilot.Agents`, tests in dedicated test projects. New service (`INarrativeGovernanceService`) keeps `ISspService` focused. New tool file (`NarrativeGovernanceTools.cs`) keeps `SspAuthoringTools.cs` manageable.

## Complexity Tracking

> No constitution violations — table not applicable.
