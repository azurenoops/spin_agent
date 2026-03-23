# Implementation Plan: Registered System Intake Wizard

**Branch**: `042-system-intake-wizard` | **Date**: 2026-03-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/042-system-intake-wizard/spec.md`

## Summary

Replace the existing single-dialog "+ Add System" flow on the Systems (Portfolio) page with a 7-step guided intake wizard implemented as a modal overlay. The wizard walks users through: (1) System Registration, (2) Security Capabilities, (3) System Components, (4) Authorization Boundaries, (5) Assign RMF Roles, (6) Verify Roles, and (7) Set Categorization. Each step persists data on transition using existing backend API endpoints. A new `SystemCapabilityLink` entity enables Step 2. Systems with incomplete setup display a "Setup Incomplete" badge computed from existing data relationships.

## Technical Context

**Language/Version**: TypeScript 5.7 (frontend), C# .NET 8 (backend)  
**Primary Dependencies**: React 19, React Router 7, Vite 6, Tailwind CSS 3, Axios (frontend); EF Core, Serilog (backend)  
**Storage**: SQL Server via Entity Framework Core (existing `AtoCopilotContext`)  
**Testing**: Vitest + Testing Library (frontend unit), Playwright (frontend E2E), xUnit + FluentAssertions + Moq (backend)  
**Target Platform**: Web browser (1024px+ width), Azure Government hosting  
**Project Type**: Web application (React SPA + .NET API)  
**Performance Goals**: <2s wizard initial load, <1s step transitions, <300ms search filtering  
**Constraints**: Modal overlay (no route changes), batch saves at step boundaries only, SP 800-60 data bundled client-side  
**Scale/Scope**: 7 wizard steps, ~12 new React components, 1 new entity, 3 new API endpoints, 4 documentation pages updated

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | ✅ PASS | Feature follows spec.md; documentation updates planned (FR-021, FR-022) |
| II. BaseAgent/BaseTool Architecture | ✅ N/A | No new agents or tools. Wizard is UI-only, orchestrating existing API endpoints |
| III. Testing Standards | ✅ PASS | Plan includes Vitest unit tests for wizard components, Playwright E2E tests, xUnit integration tests for new endpoint |
| IV. Azure Government & Compliance | ✅ PASS | No new Azure interactions; uses existing auth flow; SP 800-60 is NIST standard |
| V. Observability & Structured Logging | ✅ PASS | New capability-link endpoint will use existing Serilog patterns |
| VI. Code Quality & Maintainability | ✅ PASS | Components follow single responsibility (one per step); DI for services |
| VII. User Experience Consistency | ✅ PASS | Wizard follows existing modal/form patterns; error responses use existing envelope |
| VIII. Performance Requirements | ✅ PASS | NFR targets (2s/1s/300ms) within constitution limits; pagination on all lists |

### Post-Design Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | ✅ PASS | New guide at `docs/guides/system-intake-wizard.md`; persona guides updated |
| II. BaseAgent/BaseTool Architecture | ✅ N/A | No agents/tools touched |
| III. Testing Standards | ✅ PASS | Each wizard step component gets unit tests; new API endpoint gets integration test |
| IV. Azure Government & Compliance | ✅ PASS | No new Azure service calls |
| V. Observability & Structured Logging | ✅ PASS | Capability-link service inherits existing logging |
| VI. Code Quality & Maintainability | ✅ PASS | Wizard state in single reducer; each step is an independent component <50 lines of logic |
| VII. User Experience Consistency | ✅ PASS | Stepper mirrors existing RmfPhaseProgress pattern; errors follow existing error envelope |
| VIII. Performance Requirements | ✅ PASS | Lazy step rendering; debounced search; static SP 800-60 bundle; pagination on all collection endpoints |

## Project Structure

### Documentation (this feature)

```text
specs/042-system-intake-wizard/
├── plan.md              # This file
├── research.md          # Phase 0 output — technology decisions
├── data-model.md        # Phase 1 output — entity details & relationships
├── quickstart.md        # Phase 1 output — dev setup instructions
├── contracts/           # Phase 1 output — API contracts
│   └── dashboard-api.md
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/Ato.Copilot.Dashboard/src/
├── components/
│   └── wizard/                         # NEW: Intake wizard components
│       ├── IntakeWizard.tsx            # Main wizard modal container
│       ├── WizardStepper.tsx           # Progress indicator (7 steps)
│       └── steps/
│           ├── SystemRegistration.tsx  # Step 1
│           ├── SecurityCapabilities.tsx # Step 2
│           ├── SystemComponents.tsx    # Step 3
│           ├── AuthorizationBoundaries.tsx # Step 4
│           ├── AssignRoles.tsx         # Step 5
│           ├── VerifyRoles.tsx         # Step 6
│           ├── SetCategorization.tsx   # Step 7
│           └── CompletionSummary.tsx   # Final success screen
├── hooks/
│   └── useIntakeWizard.ts             # NEW: Wizard state reducer
├── api/
│   └── capabilityLinks.ts             # NEW: Capability-link API client
├── data/
│   └── sp800-60-information-types.json # NEW: Bundled SP 800-60 reference
├── pages/
│   └── PortfolioDashboard.tsx         # MODIFIED: Replace dialog with wizard
└── types/
    └── dashboard.ts                    # MODIFIED: Add setup completion fields

src/Ato.Copilot.Core/
├── Models/Compliance/
│   └── SystemCapabilityLink.cs        # NEW: Join entity
├── Services/
│   └── SystemCapabilityLinkService.cs # NEW: CRUD service
├── Data/Context/
│   └── AtoCopilotContext.cs           # MODIFIED: Add DbSet
└── Services/
    └── DashboardService.cs            # MODIFIED: Setup completion in portfolio DTO

src/Ato.Copilot.Chat/
└── Controllers/
    └── DashboardApiController.cs      # MODIFIED: Add capability-link endpoints

tests/
├── Ato.Copilot.Tests.Unit/
│   └── Services/
│       └── SystemCapabilityLinkServiceTests.cs # NEW
└── Ato.Copilot.Tests.Integration/
    └── Dashboard/
        └── CapabilityLinkEndpointTests.cs # NEW

docs/
├── guides/
│   └── system-intake-wizard.md        # NEW: Full wizard guide
└── getting-started/
    ├── issm.md                        # MODIFIED: Reference wizard
    ├── isso.md                        # MODIFIED: Reference wizard
    └── engineer.md                    # MODIFIED: Reference wizard
```

**Structure Decision**: Web application structure with React frontend and .NET backend. All new frontend code goes under the existing `src/Ato.Copilot.Dashboard/src/` directory following existing patterns. The wizard components live in a new `components/wizard/` subdirectory. Backend changes are minimal — one new entity, one new service, and endpoint additions to the existing controller.

## Complexity Tracking

No constitution violations. Testing phase (Phase 11 in tasks.md) covers unit tests (xUnit for backend, Vitest for frontend), integration tests (WebApplicationFactory), and E2E tests (Playwright) per Constitution Principle III. The design uses existing patterns and minimal new abstractions.
