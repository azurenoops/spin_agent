# Implementation Plan: Control Inheritance & Customer Responsibility Matrix

**Branch**: `043-control-inheritance` | **Date**: 2026-03-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/043-control-inheritance/spec.md`

## Summary

Build a dashboard UI and REST API layer for managing control inheritance designations, CSP responsibility profiles, and CRM generation. The backend service layer (`IBaselineService`) already provides `SetInheritanceAsync`, `GenerateCrmAsync`, `GetBaselineAsync`, and `TailorBaselineAsync`. This feature exposes those services via new REST endpoints in `DashboardEndpoints.cs`, adds an `InheritanceAuditEntry` model for immutable change tracking, introduces a CSP profile loading system (JSON config files), CRM export (CSV/Excel via ClosedXML), CRM import with column mapping, a new "Control Inheritance" React dashboard page, and a combined "Categorization & Baseline" management page under the "Compliance Posture" nav group.

### Post-Implementation Enhancements

After initial feature delivery, the following enhancements were implemented:

- **Narrative Auto-Status**: When inheritance designations are applied (manual, bulk, CSP profile, CRM import), narrative implementation statuses are automatically updated (Inherited→Implemented, Shared→PartiallyImplemented). All write responses include `narrativesAutoUpdated` count.
- **Categorization-to-Baseline Cascade**: When system categorization level changes, the baseline is auto-reselected. `SelectBaselineAsync` now snapshots inheritance designations before deleting the old baseline, reapplies them to matching controls in the new baseline, and auto-updates narrative statuses.
- **Combined Categorization & Baseline Page**: `BaselineManagement.tsx` was extended with a RecategorizeDialog for changing system categorization inline, with cascade banner feedback.
- **Nav Reorder**: Control Inheritance moved before Narratives in the Compliance Posture group.
- **Terminology Alignment**: Gate checks and boundary management page changed from "resources" to "components" (querying `ComponentSystemAssignments`).
- **Regenerate Narrative Fallback**: `RegenerateNarrativeWithAiAsync` falls back to deterministic `GenerateEnrichedNarrative` when AI is disabled.
- **Layout Fix**: `min-w-0` added to `<main>` in `PageLayout.tsx` to prevent table overflow under the side panel.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (backend), TypeScript 5 / React 19 (frontend)  
**Primary Dependencies**: EF Core 9.0, ASP.NET Core Minimal APIs, ClosedXML 0.104.2 (Excel I/O), Serilog (logging); React 19, Vite 6, Tailwind CSS 3, Axios, React Router 7 (frontend)  
**Storage**: SQLite (dev) / SQL Server (prod) via EF Core — existing `AtoCopilotContext`  
**Testing**: xUnit 2.9, FluentAssertions 7.0, Moq 4.20 (`dotnet test`); Vitest (frontend); Playwright (E2E)  
**Target Platform**: Docker (Linux container), Azure Government  
**Project Type**: Full-stack web application (ASP.NET Core API + React SPA dashboard)  
**Performance Goals**: Page load <2s for 325-control baseline (SC-001); bulk update of 50 controls <3s (SC-002); CRM generation <3s (SC-003)  
**Constraints**: <200ms p95 for status/health endpoints; paginated query results (default 50); all async operations honor `CancellationToken`  
**Scale/Scope**: ~325 controls per High baseline; 20 control families; 2 new dashboard pages; 10 new REST endpoints; 1 new EF model + 1 new DbSet

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | ✅ PASS | Follows existing `/docs/` patterns; spec is the source |
| II. BaseAgent/BaseTool Architecture | ✅ N/A | No new agents or tools — this feature exposes existing `IBaselineService` through REST endpoints only |
| III. Testing Standards | ✅ PASS | Unit tests for new service methods (CRM export, CSP profile loading, audit entry creation); integration tests for all new endpoints; boundary tests for pagination, empty baselines |
| IV. Azure Government & Compliance First | ✅ PASS | NIST 800-53 control-level inheritance maps directly to FedRAMP CRM; no Azure service calls in this feature |
| V. Observability & Structured Logging | ✅ PASS | All endpoint calls logged via existing middleware; audit entries for all write operations |
| VI. Code Quality & Maintainability | ✅ PASS | Constructor injection; XML docs on public types; no magic values; PascalCase naming |
| VII. User Experience Consistency | ✅ PASS | Standard ErrorResponse envelope; consistent table/filter patterns matching existing pages (POA&M, Deviations) |
| VIII. Performance Requirements | ✅ PASS | Paginated list endpoint; indexed queries on `ControlBaselineId`; export operations bounded by baseline size (<400 controls typical) |

## Project Structure

### Documentation (this feature)

```text
specs/043-control-inheritance/
├── plan.md              # This file
├── research.md          # Phase 0 output — research findings
├── data-model.md        # Phase 1 output — entity schemas
├── quickstart.md        # Phase 1 output — developer quickstart
├── contracts/           # Phase 1 output — REST API contracts
│   └── api.md           #   Endpoint specifications
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Compliance/
│   │   └── RmfModels.cs                    # EXISTING — add InheritanceAuditEntry model
│   ├── Data/Context/
│   │   └── AtoCopilotContext.cs             # EXISTING — add DbSet<InheritanceAuditEntry>, EF config
│   └── Interfaces/Compliance/
│       └── IBaselineService.cs              # EXISTING — no changes (methods already exist)
├── Ato.Copilot.Agents/
│   └── Compliance/Services/
│       └── BaselineService.cs              # EXISTING — add audit entry creation in SetInheritanceAsync
├── Ato.Copilot.Mcp/
│   ├── Endpoints/
│   │   └── DashboardEndpoints.cs           # EXISTING — add ~8 new endpoint mappings + categorization cascade logic
│   └── Services/
│       ├── CspProfileService.cs            # NEW — loads & applies CSP inheritance profiles from JSON
│       └── CrmExportService.cs             # NEW — CSV/Excel CRM export + CRM import parsing
├── Ato.Copilot.Dashboard/
│   └── src/
│       ├── pages/
│       │   ├── ControlInheritance.tsx       # NEW — main inheritance dashboard page + narrative auto-status banner
│       │   ├── BaselineManagement.tsx       # NEW — combined categorization & baseline page with RecategorizeDialog
│       │   ├── BoundaryManagement.tsx       # EXISTING — resources→components terminology update
│       │   └── Narratives.tsx              # EXISTING — regenerate error banner added
│       ├── components/
│       │   ├── layout/
│       │   │   ├── SystemLayout.tsx          # EXISTING — nav reorder (Control Inheritance before Narratives)
│       │   │   └── PageLayout.tsx           # EXISTING — min-w-0 fix for table overflow
│       │   ├── wizard/steps/
│       │   │   └── SetCategorization.tsx     # EXISTING — adjustmentJustification fix
│       │   └── inheritance/
│       │       ├── InheritanceTable.tsx      # NEW — filterable table with inline edit
│       │       ├── InheritanceSummaryBar.tsx  # NEW — summary counts bar
│       │       ├── BulkUpdateToolbar.tsx     # NEW — bulk-action toolbar
│       │       ├── CrmView.tsx              # NEW — CRM generation view
│       │       ├── CspProfileDialog.tsx     # NEW — profile selection & preview dialog
│       │       ├── CrmImportDialog.tsx      # NEW — CSV/Excel import with column mapping
│       │       └── AuditHistoryPanel.tsx    # NEW — per-control audit trail
│       ├── api/
│       │   ├── inheritance.ts              # NEW — Axios API client functions
│       │   └── systemDetail.ts             # EXISTING — SetCategorizationResponse cascade fields added
│       └── types/
│           └── inheritance.ts              # NEW — TypeScript type definitions + narrativesAutoUpdated on responses
└── seed-data/
    └── csp-profiles/
        └── azure-gov-fedramp-high.json     # NEW — pre-built Azure Gov CSP profile

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── CspProfileServiceTests.cs           # NEW
│   ├── CrmExportServiceTests.cs            # NEW
│   └── InheritanceAuditTests.cs            # NEW
└── Ato.Copilot.Tests.Integration/
    └── InheritanceEndpointTests.cs          # NEW
```

**Structure Decision**: Full-stack web application structure following existing patterns. Backend adds to existing `DashboardEndpoints.cs` (consistent with all other dashboard features), two new service classes in `Ato.Copilot.Mcp/Services/`, one new model in `Ato.Copilot.Core`. Frontend adds two new pages (ControlInheritance + BaselineManagement) and a component folder under the existing dashboard SPA. CSP profile seed data lives under `src/seed-data/csp-profiles/` as JSON config files. Post-implementation enhancements also modified existing files: `BaselineService.cs` (inheritance preservation + narrative auto-status), `RmfLifecycleService.cs` and `TodoService.cs` (terminology alignment), `CapabilityService.cs` (regenerate fallback), `SystemLayout.tsx` (nav reorder), `PageLayout.tsx` (layout fix), `SetCategorization.tsx` (validation fix), and `BoundaryManagement.tsx` (terminology).

## Notes

- US6 (Cross-Portfolio Inheritance, P4) is deferred — requires schema migration and is explicitly marked "future" in the spec
- The PUT /inheritance endpoint handles both single edit (changeSource: "Manual") and bulk update (changeSource: "BulkUpdate") — same endpoint, different UI flows
- CRM generation (GET /crm) wraps the existing `BaselineService.GenerateCrmAsync()` with no modifications
- CSP profile seed data (T021) requires research into Microsoft's published Azure Government CRM for accurate control-level designations
- ClosedXML is already a project dependency — no additional packages needed for Excel export
- T035 implements role-gated writes per FR-026 using the existing auth context pattern from `AuditLoggingMiddleware.cs`
- Total tasks: 50 original + 10 post-implementation enhancements = 60

### Post-Implementation Architecture Notes

- **SelectBaselineAsync inheritance preservation**: Before deleting the old baseline, the method snapshots all `ControlInheritance` records (ControlIdentifier, InheritanceType, Provider, CustomerResponsibility, SetBy). After creating the new baseline, it looks up matching controls by identifier and reapplies designations. Log messages include reapplied count.
- **Narrative auto-status**: Implemented in `BaselineService.SetInheritanceAsync`. After applying designations, queries `ControlImplementation` records for the affected controls and updates `ImplementationStatus` (Inherited→Implemented, Shared→PartiallyImplemented). The `InheritanceResult` includes `NarrativesAutoUpdated`.
- **Categorization cascade**: Implemented in the `POST /systems/{systemId}/categorization` endpoint in `DashboardEndpoints.cs`. Before saving categorization, captures the current baseline level. After save, compares levels; if different, calls `SelectBaselineAsync`. Response includes `baselineReselected`, `baselineControls`, `inheritancesReapplied`.
- **Frontend cascade UX**: `BaselineManagement.tsx` shows a dismissable blue banner when cascade occurs. `ControlInheritance.tsx` shows a dismissable blue banner when narratives are auto-updated. `CrmImportDialog.tsx` shows auto-updated count in the result step.

## Complexity Tracking

No constitution violations to justify.
