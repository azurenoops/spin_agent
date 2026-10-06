# Implementation Plan: Mission System Details

**Branch**: `046-mission-system-details` | **Date**: 2026-03-26 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/046-mission-system-details/spec.md`

## Summary

### Focused offering drawer follow-up

See Feature 079's focused drawer plan and tasks. Reuse the Environment dialog,
canonical relationship/applicability transport and responsibility workflow.
Keep the independent profile editor, its navigation guard and all prior forms,
diagrams and exports unchanged. Receiver routes verify captured source context;
responsibility review retains the full system baseline. The inline guard restores
the initiating drawer link on canceled navigation. The scope projection's review
flag remains labeled diagnostics rather than overriding canonical task status.

### Environment & hosting organization follow-up

Reuse the existing profile form and native form-associated header submit. Keep
all scalar keys/legacy values, unknown keys, dirty-state handling and governance
actions. Render all deployment, network/location and recovery/operating fields
in one visible editor card inside the existing actual HTML form, without disclosures;
render independent provider/subscription workflows after, outside the draft form.
Replace provider cards with a named compact table; retain revisions, published
duties, source references and management in the existing inspectors. Use shared
page columns/sidebar with both banners above them. No API/backend/export changes.
Constitution gate: failing layout/save tests first, focused frontend regressions,
strict TypeScript/build, desktop/mobile synthetic browser acceptance and retained
native profile/export regression. For the subsequent presentation-only unification,
rerun focused frontend selectors, strict TypeScript/build and desktop/mobile browser
acceptance; prior native/export checks are not rerun. No external writes or shared
feature mutations. No new abstraction, schema or complexity deviation is needed.

### Data information-handling follow-up

Extend DataTypeEntry with nine nullable bounded declared handling fields and
an additive rerunnable DataTypeHandlingSchemaAdditions module, called during
normal SQLite/SQL Server startup. Existing data/catalog/privacy/categorization
records remain authoritative; no duplicate approval framework is introduced.
Save/GET parsing validates impact/privacy choices and HTTPS/application-relative
source references. Legacy omitted fields preserve stored values, explicit
null clears fields, and whole-section UnderReview locking stays unchanged.

Capture new fields in working/retained review snapshots and native SSP/OSCAL/
Word/PDF, design information sources and scoped AI grounding. Preserve names/
classification/source/destination/regulations/order/custom context and old
approved hashes/bytes. Native text identifies handling/CIA/privacy fields as
declarations, not a separately approved categorization/PIA.

Use the existing dialog/form for detailed records and section context. Header
Save Draft submits the live existing form; Add moves beside the compact table.
Readiness counts ten documentation fields plus CUI category when CUI applies,
without completion for unknown CIA or pending privacy review. Correction opens
the named missing record; authoritative source routes are explicit.
TDD/native/schema/browser/TS gates remain; preserve other staged Users/overview/
AI work, runtime environment and peer services. No dependency or GitHub write.

### Users category documentation follow-up

Extend UserCategory with eight nullable bounded documentation fields and add
rerunnable SQLite/SQL Server columns through the existing schema-additions
module. Do not backfill guessed classifications. Accept/validate fields in the
existing authorized child parser and GET DTO; absent new fields from legacy
clients retain recorded values, explicit null clears them. Treat every new
field change as a business revision, preserving under-review locking, draft/
retained-approval isolation, audit snapshots and independent category review.

Wire fields into working/approved SSP/OSCAL/Word/PDF and System design/AI source
projections. Descriptive names for data/environments are not canonical links,
role grants or proof of authorized access. Use existing dialog/form and scoped
save rather than a second record store. The compact table keeps details/count
in the inspector; sidebar measures ten recorded documentation fields per active
category and provides a named correction action without claiming ATO readiness.
Header Save Draft targets the existing form. No production/mock revision or
approval metadata is fabricated.

Constitution gate: failing model/UI tests before production changes; legacy
field/review safeguards, API/native output tests, schema rerun, TS/build and
desktop/mobile save/reload acceptance. Preserve other staged overview and
unstaged AI-first-pass work; no new dependency or GitHub writes.

Allow Mission Owners to contribute structured system profile data (mission, users, data types, environment, ports/protocols, leveraged authorizations) and business-side narrative drafts through a governed three-tier contribution model. Contributions flow through a Draft → UnderReview → Approved governance lifecycle (with Mission Owner withdrawal from UnderReview). Only ISSM-approved content feeds into SSP generation. The dashboard is enhanced with 7 new UI areas, a role-switcher widget, role-aware views, and Mission Owner notifications (To Do + email).

**Technical approach**: Extend the existing EF Core data model with 8 new entities + 1 new enum + 1 new RmfRole value. Implement 7 MCP tools (BaseTool pattern) backed by a service-layer RBAC model. Enhance the React dashboard with profile section forms, governance badges, a completeness tracker (5 mandatory / 1 optional section), a role-switcher widget, and role-aware view logic. All API endpoints target < 500ms p95 response time.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (backend), TypeScript / React 18 (frontend)
**Primary Dependencies**: ASP.NET Core, EF Core 9.0.0, Axios, Tailwind CSS
**Storage**: SQLite (dev) / SQL Server (prod), dual-provider via `AtoCopilotContext`
**Testing**: xUnit + FluentAssertions + Moq (unit), WebApplicationFactory (integration), 80%+ coverage gate
**Target Platform**: Azure Government (Linux containers), FedRAMP High
**Project Type**: Web service (MCP server) + SPA dashboard
**Performance Goals**: < 500ms p95 for all profile-related API endpoints (SC-011); < 1s completeness update post-save (SC-003)
**Constraints**: BaseAgent/BaseTool NON-NEGOTIABLE; no field-level encryption; CAC auth deferred (role switcher interim)
**Scale/Scope**: ~20 files (NEW + MODIFY); 8 new entities; 7 MCP tools; 13 REST endpoints; 10 dashboard components

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Verdict | Notes |
|---|-----------|---------|-------|
| I | Documentation as Source of Truth | **PASS** | Spec, plan, data-model, contracts all in `/specs/046-*/`. No docs conflicts. |
| II | BaseAgent/BaseTool Architecture | **PASS** | All 7 MCP tools extend `BaseTool`. Registered in `ComplianceAgent` constructor. System prompts externalized. |
| III | Testing Standards | **PASS** | Unit tests for service layer (positive + negative + boundary). Integration tests for all 7 MCP tools (happy + error). Manual test scenarios in quickstart.md. |
| IV | Azure Government & Compliance First | **PASS** | No new Azure interactions. Data at rest inherits existing encryption. Role model extends existing NIST-aligned RBAC. |
| V | Observability & Structured Logging | **PASS** | Tool executions auto-logged via BaseTool. Service layer logs state transitions with actor identity. Audit trail entity captures all governance transitions. |
| VI | Code Quality & Maintainability | **PASS** | Single-responsibility services. DI for all dependencies. XML docs on public types. No magic values (enums for section types and statuses). |
| VII | User Experience Consistency | **PASS** | Standard envelope for MCP responses. Actionable error messages with error codes + suggestions. Reuses existing Tailwind design system. |
| VIII | Performance Requirements | **PASS** | < 500ms p95 target (SC-011). Paginated child entity queries. CancellationToken on all async ops. Bounded result sets. |

**Post-design re-check**: All 8 principles remain PASS after Phase 1 design. The withdrawal transition (UnderReview → Draft) adds one state path but follows the same governance pattern. The 5-mandatory/1-optional completeness model simplifies the denominator. Email notification reuses existing infrastructure (no new Azure services).

## Project Structure

### Documentation (this feature)

```text
specs/046-mission-system-details/
├── spec.md              # Feature specification (13 user stories, 50 FRs, 11 SCs)
├── plan.md              # This file
├── research.md          # Phase 0 output (12 research decisions)
├── data-model.md        # Phase 1 output (7 new entities + enums)
├── quickstart.md        # Phase 1 output (build/test + 15 smoke tests)
├── contracts/
│   ├── mcp-tools.md     # 7 MCP tools + 13 REST endpoints
│   └── dashboard-ui.md  # 10 UI sections + component contracts
└── tasks.md             # Phase 2 output (57 tasks across 16 phases)
```

### Source Code (repository root)

```text
# Backend — NEW files
src/Ato.Copilot.Core/Models/Compliance/SystemProfileModels.cs   # NEW — ProfileSectionType enum, SystemProfileSection, child entities
src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs          # MODIFY — Add 7 DbSets + OnModelCreating config
src/Ato.Copilot.Agents/Compliance/Services/SystemProfileService.cs  # NEW — ISystemProfileService + implementation
src/Ato.Copilot.Agents/Compliance/Services/NotificationService.cs   # NEW — INotificationService (To Do + email)
src/Ato.Copilot.Agents/Compliance/Tools/SystemProfileTools.cs  # NEW — 7 BaseTool implementations
src/Ato.Copilot.Agents/Compliance/ComplianceAgent.cs           # MODIFY — Register 7 new tools

# Backend — test files
tests/Ato.Copilot.Tests.Unit/Compliance/SystemProfileServiceTests.cs    # NEW
tests/Ato.Copilot.Tests.Unit/Compliance/SystemProfileToolsTests.cs      # NEW
tests/Ato.Copilot.Tests.Integration/Compliance/SystemProfileIntegrationTests.cs  # NEW

# Frontend — NEW files
src/Ato.Copilot.Dashboard/src/pages/SystemProfile.tsx                   # NEW — Profile section page
src/Ato.Copilot.Dashboard/src/components/forms/ProfileSectionForm.tsx   # NEW — Form component
src/Ato.Copilot.Dashboard/src/components/cards/ProfileReadinessCard.tsx # NEW — MetricCard wrapper
src/Ato.Copilot.Dashboard/src/components/layout/RoleSwitcher.tsx        # NEW — Role switcher widget
src/Ato.Copilot.Dashboard/src/api/systemProfile.ts                     # NEW — Profile API module
src/Ato.Copilot.Dashboard/src/api/businessContext.ts                    # NEW — Business context API module

# Frontend — MODIFY files
src/Ato.Copilot.Dashboard/src/hooks/useSettings.ts                     # MODIFY — Add 'MissionOwner' to role union
src/Ato.Copilot.Dashboard/src/api/client.ts                            # MODIFY — X-Simulated-Role interceptor
src/Ato.Copilot.Dashboard/src/App.tsx                                   # MODIFY — Route + RoleSwitcher mount
src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx        # MODIFY — Sidebar nav + System Details tab
src/Ato.Copilot.Dashboard/src/components/cards/TodoPanel.tsx            # MODIFY — YOUR PROFILE TASKS section
src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx                    # MODIFY — Metrics, banners
src/Ato.Copilot.Dashboard/src/pages/Narratives.tsx                      # MODIFY — Business-context side panel
src/Ato.Copilot.Dashboard/src/types/dashboard.ts                       # MODIFY — Type extensions
```

**Structure Decision**: Follows the existing backend/frontend split (C# backend under `src/Ato.Copilot.*`, React dashboard under `src/Ato.Copilot.Dashboard/src/`). All new backend code follows the existing `Compliance/` namespace organization. All new frontend code follows existing component file naming and directory conventions.

## Key Design Decisions from Clarifications

| # | Decision | Source |
|---|----------|--------|
| Q1 | ISSO reads profiles, incorporates into narratives; only ISSM approves | Session clarification |
| Q2 | ISSM assigns MO role at wizard Step 5; MO notified via To Do + email | Session clarification |
| Q3 | Two-state versioning (Approved + Draft); audit trail for history | Session clarification |
| Q4 | Same DB-level encryption; no field-level encryption needed | Session clarification |
| Q5 | Hybrid flagging: static -1 control list + ISSM per-system overrides | Session clarification |
| Q6 | "Not Started" is computed (no record = Not Started); SspSectionStatus unchanged | Clarify session |
| Q7 | 5 mandatory / 1 optional (Leveraged Auth); completeness = X of 5 | Clarify session |
| Q8 | Withdrawal allowed: UnderReview → Draft before ISSM acts; audit-trailed | Clarify session |
| Q9 | < 500ms p95 for all profile API endpoints | Clarify session |
| Q10 | MO notification: To Do panel task + email with link to system profile | Clarify session |
# Inventory & boundary cleanup implementation (October 6)

The Components & system scope clarification reuses the full governed graph and
revision-fenced design editor. Raw disposition is displayed rather than silently
coercing a contradictory legacy record through the diagram's effective disposition
helper. Server validation already rejects included SharedService/SeparatelyAuthorized
records; the editor must report that conflict before staging. No schema, permission,
provider, tenant, placement or authorization contract changes are planned.
BoundaryDefinition properties establish name/type/primary only, not reviewed
authorization coverage or internal-area classification. Graph groups are displayed
as recorded internal groups, not renamed boundaries. Existing source selectors stay
in Advanced scope details with their actual purpose explained. Native export tests
must verify the approved snapshot remains authoritative after subsequent draft edits.

Browser validation exposed the shared Axios client's existing behavior of rejecting
the server error envelope without HTTP status. Scope-save errors therefore retain
the server message and append explicit input-retention/revision-comparison guidance;
do not depend solely on Axios status to inform the user. The global client contract
is unchanged.

WCAG browser checks found insufficient 4.39-4.44:1 contrast in the task's
transparent sidebar/introduction and System definition breadcrumb on the shell
background. Darken only those task support labels/copy and the definition
breadcrumb; retain the shell layout and dark-theme contrast.

Add a governed inventory workspace around the existing boundary register,
reusing design API/editor/unsaved guard and effective scope helper. Reuse all
existing annotation fields and native reviewed-output persistence; no migration,
new tool or new approval lifecycle. Show the existing canonical definition
register directly after the scope introduction, before the governed component
register. Remove the duplicate scope explanation and bottom disclosure only.
Reuse the existing child content, drawer invokers and handlers unchanged; retain
the immediate canonical-write notice and independent governed draft status.
Validate layout order and visible source management with failing-first targeted
tests, then desktop/mobile 4196 inspection without feature writes.
Validate counts, provenance, draft/concurrency/read-only handling, mobile layout,
save/reload and retained native outputs. Existing dirty/staged work is retained.

Banner/action follow-up: pass the canonical status as a dedicated render slot
to the governed workspace and move its existing revision ribbon above the
introduction, outside either column layout. Replace the canonical column/sidebar
wrapper with the full-width register. Supply an authorized heading action to
the register for the existing create dialog; replace the obsolete header-action
focus fallback with the connected Add System Boundary trigger. Keep immediate
canonical-write guidance in the register and dialogs. No backend/schema/API or
export contract changes; no new scope-selection behavior. Constitution gate:
existing authorization/tenant handlers retained, failing-first tests and local
type/build/browser verification required, no complexity deviation or new feature.

Browser checks require preserving the canonical heading typography after adding
its action wrapper. The full-width canonical table also exposed 3.76:1 header
contrast in the governed workspace: use the existing readable sidebar slate
colors for these headers, including dark mode, without changing other tables.

Shared-column layout follow-up: move the existing introduction, canonical child
register and inventory feedback into the main content of the existing
SystemTaskColumns with its existing stretch option so the sidebar column spans
both tables while support cards remain at the top. Leave both status slots above
the grid and every dialog outside it. Reuse the existing 1051px two-column breakpoint, min-width guards and table
scroll wrappers; do not change shared presentation/CSS or backend contracts.
Verify structural containment failing-first, then exact 1440/390 geometry, local
table scrolling, keyboard/cancel behavior and unchanged live GET snapshots.
