# Implementation Plan: Unified Security Capabilities Hub

**Branch**: `045-capabilities-hub` | **Date**: 2026-03-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/045-capabilities-hub/spec.md`

## Summary

Elevate the Security Capabilities page to be the single source of truth for defining where control inheritance comes from. This feature restructures the import pipeline so CSP profile and CRM imports flow through the Capabilities page — creating components (Things), capabilities grouped by provider + NIST family, component-capability links, control mappings, org inheritance designations, and enriched SSP narratives in a single transactional operation. The existing CSP profile JSON schema is extended with a `services[]` array for per-service granularity. A coverage API and dashboard provide org-wide and per-system visibility. The old import buttons on the Control Inheritance page are removed entirely; a cross-link banner directs users to the Capabilities page. A Coverage % KPI card is added to the Portfolio Risk Profile dashboard.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (backend), TypeScript 5 / React 19 (frontend)
**Primary Dependencies**: EF Core 9.0, ASP.NET Core Minimal APIs, ClosedXML 0.104.2 (Excel I/O), Serilog (logging); React 19, Vite 6, Tailwind CSS 3, Axios, React Router 7 (frontend)
**Storage**: SQLite (dev) / SQL Server (prod) via EF Core — existing `AtoCopilotContext`
**Testing**: xUnit 2.9, FluentAssertions 7.0, Moq 4.20 (`dotnet test`); Vitest (frontend); Playwright (E2E)
**Target Platform**: Docker (Linux container), Azure Government
**Project Type**: Full-stack web application (ASP.NET Core API + React SPA dashboard)
**Performance Goals**: CSP/CRM import pipeline <30s; coverage endpoint <2s; page load <2s; KPI card overhead <200ms; preview dialog <5s
**Constraints**: <200ms p95 for status/health endpoints; paginated query results (default 50); all async operations honor `CancellationToken`; memory <512MB steady-state
**Scale/Scope**: ~325 controls per High baseline; ~160 controls per CSP profile; ~10 CSP services; ~20 capabilities per provider; 21 functional requirements; 6 performance targets; 4 performance tests; 12 documentation files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | ✅ PASS | 12 doc files identified in spec for update; follows existing `/docs/` patterns |
| II. BaseAgent/BaseTool Architecture | ✅ N/A | No new agents or tools — this feature modifies existing services and adds REST endpoints + React UI |
| III. Testing Standards | ✅ PASS | Unit tests for import pipeline service, coverage computation, deduplication; integration tests for all new endpoints; 4 performance tests (PT-001–PT-004); boundary tests for empty profiles, duplicate imports, conflict resolution |
| IV. Azure Government & Compliance First | ✅ PASS | NIST 800-53 control-level mapping; CSP profile represents Azure Government FedRAMP High CRM; no new Azure service calls |
| V. Observability & Structured Logging | ✅ PASS | Import operations logged with structured Serilog (profile name, counts, duration); coverage endpoint logged via existing middleware |
| VI. Code Quality & Maintainability | ✅ PASS | Constructor injection; XML docs on public types; service-level separation (CapabilityImportService for pipeline orchestration); PascalCase naming |
| VII. User Experience Consistency | ✅ PASS | Standard ErrorResponse envelope; preview dialog before destructive actions; progress feedback for imports >2s; cross-link banner for page navigation; guided empty state for onboarding |
| VIII. Performance Requirements | ✅ PASS | 6 performance targets defined (PERF-001–006); 4 performance integration tests (PT-001–004); paginated list endpoints; bounded result sets |

## Project Structure

### Documentation (this feature)

```text
specs/045-capabilities-hub/
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
│   │   └── ComponentCapabilityLink.cs        # EXISTING — no changes
│   ├── Services/
│   │   ├── CapabilityService.cs              # EXISTING — no changes (dedup handled by CapabilityImportService)
│   │   └── ComponentService.cs               # EXISTING — no changes (bulk creation handled by CapabilityImportService)
│   ├── Data/Context/
│   │   └── AtoCopilotContext.cs              # EXISTING — no schema changes (all entities exist)
│   └── Interfaces/Compliance/
│       └── ICapabilityService.cs             # EXISTING — no changes (import pipeline uses CapabilityImportService directly)
├── Ato.Copilot.Agents/
│   └── Compliance/Services/
│       └── OrgInheritanceService.cs          # EXISTING — no changes (called after import)
├── Ato.Copilot.Mcp/
│   ├── Endpoints/
│   │   └── DashboardEndpoints.cs             # EXISTING — add coverage endpoint, import-via-capabilities endpoints; remove old CSP/CRM from inheritance section
│   └── Services/
│       ├── CspProfileService.cs              # EXISTING — extend to parse services[] format (backward-compat)
│       ├── CrmExportService.cs               # EXISTING — no changes (parsing logic reused)
│       └── CapabilityImportService.cs        # NEW — orchestrates full import pipeline (CSP + CRM → Components → Capabilities → Mappings → OrgInheritance → Narratives)
├── Ato.Copilot.Dashboard/
│   └── src/
│       ├── pages/
│       │   ├── CapabilityLibrary.tsx          # EXISTING — major rewrite: add import buttons, coverage cards, component badges, guided empty state, 3-layer header
│       │   ├── ControlInheritance.tsx         # EXISTING — remove CSP/CRM import buttons, add cross-link banner, add component context tooltips
│       │   ├── PortfolioRiskProfile.tsx       # EXISTING — add Coverage % KPI card
│       │   └── ComponentInventory.tsx         # EXISTING — add capability coverage counts, "Create Capability" quick action
│       ├── components/
│       │   └── capabilities/
│       │       ├── CspImportDialog.tsx        # NEW — CSP profile import preview + confirm dialog
│       │       ├── CrmImportDialog.tsx        # NEW — CRM file import with column mapping + preview
│       │       ├── ComponentPickerModal.tsx   # NEW — multi-select component picker for linking
│       │       ├── CoverageCards.tsx          # NEW — summary cards (Total, Mapped, Gap, %)
│       │       └── GuidedEmptyState.tsx       # NEW — three action cards for first-time users
│       ├── api/
│       │   ├── capabilities.ts               # EXISTING — add import and coverage API functions
│       │   └── portfolio.ts                  # EXISTING — add coverage KPI call
│       └── types/
│           └── capabilities.ts               # EXISTING — add import preview/result types, coverage types
└── seed-data/
    └── csp-profiles/
        └── azure-gov-fedramp-high.json       # EXISTING — rewrite with services[] array format

tests/
├── Ato.Copilot.Tests.Unit/
│   ├── CapabilityImportServiceTests.cs       # NEW — pipeline orchestration, dedup, conflict resolution
│   ├── CspProfileServiceExtTests.cs          # NEW — services[] parsing, backward compat
│   └── CoverageComputationTests.cs           # NEW — coverage % calculation, per-family, per-system
└── Ato.Copilot.Tests.Integration/
    ├── CapabilityImportEndpointTests.cs       # NEW — full pipeline integration tests + performance tests (PT-001, PT-002, PT-004)
    └── CoverageEndpointTests.cs              # NEW — coverage endpoint integration + performance test (PT-003)
```

**Structure Decision**: Full-stack web application structure following existing patterns. The new `CapabilityImportService` in `Ato.Copilot.Mcp/Services/` orchestrates the full import pipeline (CSP/CRM → Components → Capabilities → Mappings → OrgInheritance → Narratives) as a single transactional operation. The existing `CspProfileService` is extended with schema parsing for the services[] format. Frontend adds 5 new components under `capabilities/` and modifies 4 existing pages. The CSP profile seed data file is rewritten with the `services[]` format. No new EF models or schema migrations needed — all entities (`SecurityCapability`, `SystemComponent`, `ComponentCapabilityLink`, `CapabilityControlMapping`, `OrgInheritanceDefault`, `ControlInheritance`) already exist.

## Notes

- No new EF models or database migrations — all required entities exist from Features 030, 043, and 044
- `CapabilityImportService` is the key new service — it coordinates across `CspProfileService`, `CapabilityService`, `ComponentService`, `OrgInheritanceService`, and `NarrativeTemplateService`
- The CSP profile JSON rewrite is backward-compatible: `CspProfileService` will try `services[]` first, fall back to flat `controls[]`
- CRM import reuses existing `CrmExportService.ParseCsv()`/`ParseExcel()` parsing but routes results through the new capabilities pipeline
- The old CSP/CRM import endpoints in DashboardEndpoints.cs (lines 5992–6180) will be replaced, not just wrapped — since there are no production users (confirmed in clarification Q4)
- Performance tests follow the existing `Stopwatch` assertion pattern from `OrgInheritanceEndpointTests.cs`
- Zero-systems fallback: Coverage % denominator uses CSP profile's declared baseline level when no systems are registered; returns null when neither systems nor CSP profiles exist

## Complexity Tracking

No constitution violations to justify.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
