# Tasks: Registered System Intake Wizard

**Input**: Design documents from `/specs/042-system-intake-wizard/`
**Prerequisites**: plan.md, spec.md, data-model.md, contracts/dashboard-api.md, research.md, quickstart.md

**Organization**: Tasks grouped by user story. US8 (Wizard Navigation & Progress Tracking) is embedded in the Foundational phase since it IS the wizard shell that all steps depend on. US9 (Performance) and US10 (Documentation) are cross-cutting and placed in the Polish phase. Testing tasks are in Phase 11.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Create directory structure, reference data, and shared types

- [x] T001 Create wizard component directories under src/Ato.Copilot.Dashboard/src/components/wizard/ and src/Ato.Copilot.Dashboard/src/components/wizard/steps/
- [x] T002 [P] Copy SP 800-60 information types reference data from src/Ato.Copilot.Agents/Compliance/Resources/ to src/Ato.Copilot.Dashboard/src/data/sp800-60-information-types.json, ensuring each entry has id, name, category, confidentiality, integrity, and availability fields
- [x] T003 [P] Add wizard-related TypeScript types to src/Ato.Copilot.Dashboard/src/types/dashboard.ts: extend PortfolioSystemSummary with isSetupComplete, hasBoundary, hasRoles, hasCategorization booleans; add WizardStep enum (Registration through Categorization); add WizardState interface with currentStep, systemId, per-step data, and validation errors; add SystemCapabilityLink interface; add Sp80060InfoType interface

---

## Phase 2: Foundational — Backend + Wizard Shell (US8)

**Purpose**: Core backend changes and the wizard modal shell (IntakeWizard, WizardStepper, useIntakeWizard hook) that ALL step components depend on. This phase satisfies **User Story 8 — Wizard Navigation and Progress Tracking (P1)**.

**⚠️ CRITICAL**: No user story step component can be implemented until this phase is complete.

### Backend

- [x] T004 [P] Update DashboardService to compute setup completion status (hasBoundary = COUNT(AuthorizationBoundaryDefinitions) > 0, hasRoles = COUNT(active RmfRoleAssignments) > 0, hasCategorization = SecurityCategorization IS NOT NULL, isSetupComplete = all three true) and include in PortfolioSystemSummaryDto in src/Ato.Copilot.Core/Services/DashboardService.cs

### Frontend

- [x] T005 [P] Implement useIntakeWizard reducer hook in src/Ato.Copilot.Dashboard/src/hooks/useIntakeWizard.ts: manage currentStep (1–7), systemId (set after Step 1), per-step form data, validation errors, step completion status array, isOpen flag; expose actions: nextStep (persist then advance), prevStep (navigate back preserving data), skipStep, goToStep (backward only to completed steps), cancel, reset; batch save data only on step transitions per NFR-004
- [x] T006 [P] Create WizardStepper progress indicator component in src/Ato.Copilot.Dashboard/src/components/wizard/WizardStepper.tsx: render 7 labeled steps (System Registration, Security Capabilities, System Components, Authorization Boundaries, Assign RMF Roles, Verify Roles, Set Categorization); show checkmark icon on completed steps, highlight current step, gray out future steps; allow click navigation to completed steps only (no forward-skip per FR-005); follow existing RmfPhaseProgress visual pattern
- [x] T007 Create IntakeWizard modal container in src/Ato.Copilot.Dashboard/src/components/wizard/IntakeWizard.tsx: full-screen modal overlay on /systems route; integrate WizardStepper at top; render current step component via conditional rendering (lazy — only mount active step); provide Next, Back, Skip (steps 2–7), and Cancel buttons; Cancel discards unsaved current-step data and closes modal (FR-018); pass systemId and step data from useIntakeWizard to each step component; on final step completion show CompletionSummary; responsive layout for 1024px+ (FR-023)
- [x] T008 [P] Create CompletionSummary component in src/Ato.Copilot.Dashboard/src/components/wizard/steps/CompletionSummary.tsx: display success message with system name and summary of completed steps; provide "Go to System" button that navigates to the newly created system's detail page (FR-017)
- [x] T009 Update PortfolioDashboard.tsx in src/Ato.Copilot.Dashboard/src/pages/PortfolioDashboard.tsx: replace existing Add System dialog trigger with IntakeWizard modal (open on "+ Add System" click); render "Setup Incomplete" badge on system cards where isSetupComplete is false (FR-024); remove or gate the old single-dialog form code

**Checkpoint**: Wizard shell opens from Systems page, stepper displays, navigation works (Next/Back/Cancel), but step content areas are empty placeholders. US8 acceptance scenarios verifiable.

---

## Phase 3: User Story 1 — Register a New System (Priority: P1) 🎯 MVP

**Goal**: Users can click "+ Add System," fill in system registration details in the wizard's Step 1, and see the new system appear in the portfolio.

**Independent Test**: Click "+ Add System," fill in system name, type, mission criticality, hosting environment, click Next, confirm the system appears in the portfolio list with "Setup Incomplete" badge.

- [x] T010 [US1] Implement SystemRegistration step component in src/Ato.Copilot.Dashboard/src/components/wizard/steps/SystemRegistration.tsx: form fields for name (required, max 200 chars), acronym (optional, max 20 chars), systemType (required, select: MajorApplication/Enclave/PlatformIt), missionCriticality (required, select: MissionCritical/MissionEssential/MissionSupport), hostingEnvironment (required, text/select), description (optional, max 2000 chars); inline validation on required fields before allowing Next (FR-008); validate system name uniqueness via POST /api/dashboard/systems returning 409 on duplicate (FR-009); on successful Next, call POST /api/dashboard/systems, store returned systemId in wizard state; display validation errors inline (US1 scenario 3)

**Checkpoint**: MVP complete — full system registration via wizard. Deliverable increment.

---

## Phase 4: User Story 2 — Add Security Capabilities (Priority: P2)

**Goal**: After Step 1, users search and link existing security capabilities to the system in Step 2.

**Independent Test**: Complete Step 1, advance to Step 2, search for a capability by name/category, select capabilities, click Next, confirm they appear linked to the system.

### Backend (new entity + endpoints)

- [x] T011 [P] [US2] Create SystemCapabilityLink entity in src/Ato.Copilot.Core/Models/Compliance/SystemCapabilityLink.cs: fields Id (string, PK, GUID), RegisteredSystemId (string, FK, required, cascade delete), SecurityCapabilityId (string, FK, required, restrict delete), LinkedAt (DateTime, UTC), LinkedBy (string, required); add unique constraint on (RegisteredSystemId, SecurityCapabilityId)
- [x] T012 [US2] Register SystemCapabilityLink in src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs: add DbSet<SystemCapabilityLink>, configure entity relationships and unique index in OnModelCreating
- [ ] T012a [US2] Create EF Core migration for SystemCapabilityLink entity: run `dotnet ef migrations add AddSystemCapabilityLink --project src/Ato.Copilot.Core --startup-project src/Ato.Copilot.Mcp` and verify the migration applies cleanly
- [x] T013 [US2] Implement SystemCapabilityLinkService in src/Ato.Copilot.Core/Services/SystemCapabilityLinkService.cs: LinkCapabilities(systemId, capabilityIds[], user) — validate system exists, validate all capability IDs exist, skip duplicates, create links, return linked items with capability names; GetLinksForSystem(systemId) — return links with capability details; RemoveLink(systemId, linkId) — delete link; register in DI; add Serilog structured logging for all CRUD operations (link created, link removed, validation failures)
- [x] T014 [US2] Add capability-link endpoints to src/Ato.Copilot.Chat/Controllers/DashboardApiController.cs: POST /systems/{systemId}/capability-links (accepts { capabilityIds: string[] }, returns { linkedCount, items }), GET /systems/{systemId}/capability-links (returns { items, totalCount }), DELETE /systems/{systemId}/capability-links/{linkId} (returns { deletedId, message }); follow existing error envelope pattern for 404/400 responses per contracts/dashboard-api.md

### Frontend

- [x] T015 [P] [US2] Create capability-link API client in src/Ato.Copilot.Dashboard/src/api/capabilityLinks.ts: linkCapabilities(systemId, capabilityIds[]), getCapabilityLinks(systemId), removeCapabilityLink(systemId, linkId); use existing Axios instance and error handling patterns
- [x] T016 [US2] Implement SecurityCapabilities step component in src/Ato.Copilot.Dashboard/src/components/wizard/steps/SecurityCapabilities.tsx: searchable list of available capabilities using GET /api/dashboard/capabilities with search, category, and status filters (FR-019); display selected capabilities as linked items with remove action; on Next call POST /capability-links to persist selections; on Skip advance without linking (FR-007); debounce search input for responsiveness; on step entry, re-fetch capability data and if any previously selected items no longer exist, show a warning notification and remove stale selections (M7 edge case)

**Checkpoint**: System registration + capability linking works end-to-end.

---

## Phase 5: User Story 3 — Define System Components (Priority: P2)

**Goal**: Users add Person, Place, or Thing components to the system in Step 3.

**Independent Test**: Advance to Step 3, add components of each type (Person, Place, Thing) with name/type/sub-type/description/owner, confirm they appear in the system's component inventory.

- [x] T017 [P] [US3] Implement SystemComponents step component in src/Ato.Copilot.Dashboard/src/components/wizard/steps/SystemComponents.tsx: inline add form with fields name (required), componentType (required, select: Person/Place/Thing), subType (optional), description (optional), owner (optional), plus personName and email fields when type is Person; list of added components with Remove action; "Add" button appends to local list; on Next call POST /api/dashboard/systems/{systemId}/components for each new component; on Skip advance without adding (FR-007); integrate existing AI description generation feature for the description field (FR-020)

**Checkpoint**: Steps 1–3 functional. System has a component inventory.

---

## Phase 6: User Story 4 — Add Authorization Boundaries (Priority: P2)

**Goal**: Users define authorization boundaries and assign components to them in Step 4.

**Independent Test**: Advance to Step 4, create a boundary (Physical/Logical/Hybrid), assign components from Step 3, confirm the boundary appears in the system's Boundary Management page.

- [x] T018 [P] [US4] Implement AuthorizationBoundaries step component in src/Ato.Copilot.Dashboard/src/components/wizard/steps/AuthorizationBoundaries.tsx: boundary creation form with name (required, unique within system), boundaryType (required, select: Physical/Logical/Hybrid), description (optional), isPrimary toggle; display list of created boundaries; for each boundary, show a component assignment selector listing components added in Step 3 (fetch from GET /systems/{systemId}/components); on Next persist boundaries via POST /api/dashboard/systems/{systemId}/boundary-definitions and assignments via POST /boundary-definitions/{id}/component-assignments; on Skip advance without creating boundaries (FR-007)

**Checkpoint**: Steps 1–4 functional. System has boundaries with component assignments.

---

## Phase 7: User Story 5 — Assign RMF Roles (Priority: P3)

**Goal**: Users assign personnel to standard RMF roles by selecting from existing Person components.

**Independent Test**: Advance to Step 5, assign at least one role (e.g., ISSM) to a Person component, confirm it appears on the system's role management view.

- [x] T019 [US5] Implement AssignRoles step component in src/Ato.Copilot.Dashboard/src/components/wizard/steps/AssignRoles.tsx: list the 5 standard RMF roles (Authorizing Official, ISSM, ISSO, SCA, System Owner) with current assignment status (assigned/unassigned); for each role provide a searchable Person selector querying GET /api/dashboard/components?type=Person (org-wide, per FR-013); on person selection immediately persist via POST /api/dashboard/systems/{systemId}/roles; no free-text entry permitted; on Skip advance without assigning roles (FR-007)

**Checkpoint**: Steps 1–5 functional. System has role assignments.

---

## Phase 8: User Story 6 — Verify Role Assignments (Priority: P3)

**Goal**: Users review a read-only summary of role assignments before proceeding.

**Independent Test**: Complete Step 5, advance to Step 6, confirm all assigned roles display correctly with person name, role, and assignment date; navigate Back to edit, then return.

- [x] T020 [US6] Implement VerifyRoles step component in src/Ato.Copilot.Dashboard/src/components/wizard/steps/VerifyRoles.tsx: fetch role assignments via GET /api/dashboard/systems/{systemId}/roles; display read-only summary table with columns: Role, Assigned Person, Assignment Date (FR-014); show "No roles assigned" message if Step 5 was skipped; Back button returns to Step 5 for corrections; Next advances to Step 7

**Checkpoint**: Steps 1–6 functional. Role assignment + verification flow complete.

---

## Phase 9: User Story 7 — Set Security Categorization (Priority: P3)

**Goal**: Users select SP 800-60 information types, review auto-suggested C/I/A impacts, override as needed, and finalize the FIPS 199 categorization.

**Independent Test**: Advance to Step 7, select information types from the grouped list, confirm C/I/A levels auto-populate, override a level, click Finish, confirm categorization is saved and overall FIPS 199 category is correct.

- [x] T021 [US7] Implement SetCategorization step component in src/Ato.Copilot.Dashboard/src/components/wizard/steps/SetCategorization.tsx: load SP 800-60 information types from src/Ato.Copilot.Dashboard/src/data/sp800-60-information-types.json; display searchable list grouped by SP 800-60 Volume II category (FR-015); on type selection auto-populate C/I/A impact level fields with recommended values as editable suggestions; allow user to override any level (Low/Moderate/High); compute overall FIPS 199 category as high-water mark across all selected types' C/I/A levels (FR-016); optional fields: isNationalSecuritySystem toggle, justification text; "Finish" button persists via POST /api/dashboard/systems/{systemId}/categorization then shows CompletionSummary; "Skip & Finish" completes without categorization (FR-007); debounce search on information type list; on step entry, re-validate that previously selected information types still exist in the bundled data (edge case: data file updated between sessions)

**Checkpoint**: All 7 wizard steps functional. Full intake flow operational.

---

## Phase 10: Polish & Cross-Cutting Concerns (US9, US10)

**Purpose**: Performance optimization (US9), documentation updates (US10), and final validation.

### Performance (US9)

- [x] T022 [P] Add debounced search (300ms) to all searchable lists in SecurityCapabilities (Step 2), SystemComponents Person selector, AssignRoles Person selector, and SetCategorization information type selector per NFR-003
- [x] T023 [P] Optimize wizard step transitions: ensure only the active step component is mounted (lazy rendering) in IntakeWizard.tsx; verify step transitions complete within 1 second under normal conditions (NFR-002); verify wizard initial load is interactive within 2 seconds (NFR-001)
- [x] T024 [P] Verify responsive layout across all wizard steps at 1024px, 1280px, and 1920px breakpoints (FR-023); fix any overflow or layout issues in step forms and searchable lists

### Documentation (US10)

- [x] T025 [P] Create system intake wizard guide at docs/guides/system-intake-wizard.md: document all 7 wizard steps with field descriptions, explain skip behavior, cover edge cases (session expiry, network failure, duplicate names, Setup Incomplete badge, resuming setup from individual pages)
- [x] T026 [P] Update ISSM getting-started guide at docs/getting-started/issm.md to reference the intake wizard as the primary method for registering new systems (FR-022)
- [x] T027 [P] Update ISSO getting-started guide at docs/getting-started/isso.md and engineer guide at docs/getting-started/engineer.md to reference the intake wizard (FR-022)

### Validation

- [x] T028 Run quickstart.md end-to-end validation: run `dotnet build Ato.Copilot.sln` (zero warnings), run `dotnet test` (all tests pass, 80%+ coverage on new services per Constitution III), run `cd src/Ato.Copilot.Dashboard && npm run build` (zero errors), run `npm run test` (all frontend tests pass); then manually: start backend (dotnet run), start dashboard (npm run dev), open /systems, click "+ Add System," walk through all 7 steps, verify system appears in portfolio, verify "Setup Incomplete" badge logic, verify documentation pages render

---

## Phase 11: Testing (Constitution Principle III)

**Purpose**: Unit, integration, and E2E tests for all new code. Constitution Principle III requires all behavior changes to include corresponding test changes.

### Backend Unit Tests (xUnit + FluentAssertions + Moq)

- [x] T029 [P] Write unit tests for SystemCapabilityLinkService in tests/Ato.Copilot.Tests.Unit/Services/SystemCapabilityLinkServiceTests.cs: test LinkCapabilities (happy path, duplicate skip, invalid system, invalid capability IDs), GetLinksForSystem (empty, with links), RemoveLink (exists, not found); mock AtoCopilotContext; minimum 80% service coverage
- [x] T030 [P] Write unit tests for DashboardService setup completion logic in tests/Ato.Copilot.Tests.Unit/Services/DashboardServiceSetupCompletionTests.cs: test isSetupComplete computation (all true, all false, partial), hasBoundary/hasRoles/hasCategorization individual flags with various data states

### Backend Integration Tests (xUnit + WebApplicationFactory)

- [ ] T031 [P] Write integration tests for capability-link API endpoints in tests/Ato.Copilot.Tests.Integration/Endpoints/CapabilityLinkEndpointTests.cs: POST /systems/{id}/capability-links (201 success, 404 invalid system, 400 invalid payload), GET /systems/{id}/capability-links (200 with items, 200 empty), DELETE /systems/{id}/capability-links/{linkId} (200 success, 404 not found); use WebApplicationFactory with in-memory database

### Frontend Unit Tests (Vitest + React Testing Library)

- [x] T032 [P] Write unit tests for useIntakeWizard reducer hook in src/Ato.Copilot.Dashboard/src/hooks/__tests__/useIntakeWizard.test.ts: test nextStep (advances, persists data), prevStep (navigates back, preserves data), skipStep (advances without data), goToStep (backward only), cancel (resets), setSystemId after Step 1; test that forward-skip to unreached step is rejected
- [x] T033 [P] Write unit tests for WizardStepper component in src/Ato.Copilot.Dashboard/src/components/wizard/__tests__/WizardStepper.test.tsx: test completed steps show checkmark, current step highlighted, future steps disabled/grayed, click on completed step fires callback, click on future step does nothing
- [x] T034 [P] Write unit tests for SystemRegistration step in src/Ato.Copilot.Dashboard/src/components/wizard/steps/__tests__/SystemRegistration.test.tsx: test required field validation (empty name, empty type, empty criticality), duplicate name error display, successful form submission calls onNext, cancel discards form data
- [x] T035 [P] Write unit tests for SetCategorization step in src/Ato.Copilot.Dashboard/src/components/wizard/steps/__tests__/SetCategorization.test.tsx: test info type search and selection, C/I/A auto-population from selected type, manual override of suggested levels, FIPS 199 high-water mark computation, Skip & Finish behavior

### E2E / Smoke Test

- [ ] T036 Write a Playwright E2E test for wizard happy path in tests/e2e/intake-wizard.spec.ts: open /systems, click "+ Add System", complete Step 1 (register system), skip Steps 2-6, complete Step 7 (set categorization), verify completion summary, navigate to system detail page, return to portfolio and verify system appears with correct name; verify "Setup Incomplete" badge shows when roles/boundary are missing

**Checkpoint**: All tests pass. `dotnet test` and `npm run test` both green. 80%+ coverage on SystemCapabilityLinkService and useIntakeWizard hook.

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1: Setup ─────────────────────────────┐
                                             ▼
Phase 2: Foundational (US8 shell) ──────────┐
         ⚠️ BLOCKS all step implementations  │
                                             ▼
Phase 3: US1 (P1) ─── MVP checkpoint ──────┐
                                            ▼
Phase 4: US2 (P2) ─┐                       │
Phase 5: US3 (P2) ─┤ Can run in parallel   │
Phase 6: US4 (P2) ─┘ after Phase 2         │
                                            ▼
Phase 7: US5 (P3) ─────────────────────────┐
                                            ▼
Phase 8: US6 (P3) ── depends on US5 data ──┐
                                            │
Phase 9: US7 (P3) ── can parallel with ────┘
         US5/US6 (different files)          │
                                            ▼
Phase 10: Polish (US9, US10) ── after all step components exist
```
Phase 11: Testing — can run in parallel with Phase 10 (different files), after all implementation phases complete.
### User Story Dependencies

- **US1 (P1)**: Depends on Foundational (Phase 2) only — no other story dependencies
- **US2 (P2)**: Depends on Foundational — includes its own backend entity chain (T011→T012→T013→T014)
- **US3 (P2)**: Depends on Foundational — uses existing component endpoints
- **US4 (P2)**: Depends on Foundational — may reference components from US3 for assignment but works independently
- **US5 (P3)**: Depends on Foundational — uses existing role + Person component endpoints
- **US6 (P3)**: Depends on US5 data model understanding but uses independent GET endpoint
- **US7 (P3)**: Depends on Foundational — uses bundled SP 800-60 data + existing categorization endpoint
- **US8 (P1)**: Satisfied by Foundational phase (wizard shell)
- **US9 (P2)**: Cross-cutting — requires all step components to exist
- **US10 (P3)**: Cross-cutting — requires feature knowledge, no code dependency

### Within Each Phase

- Models/entities before services
- Services before API endpoints
- API clients parallel with backend work (different layer)
- Step components after their required API infrastructure
- [P] tasks within a phase can run simultaneously

---

## Parallel Execution Examples

### Phase 2: Foundational (3 parallel tracks)

```
Track A (backend):  T004 (DashboardService setup completion)
Track B (state):    T005 (useIntakeWizard hook)
Track C (UI):       T006 (WizardStepper) ─┐
                    T008 (CompletionSummary)│
                                           ▼
Converge:           T007 (IntakeWizard container, needs T005 + T006)
                    T009 (PortfolioDashboard update, needs T004 + T007)
```

### Phase 4: US2 (2 parallel tracks)

```
Track A (backend):  T011 → T012 → T013 → T014 (entity → context → service → endpoint)
Track B (frontend): T015 (API client, parallel with backend)
                           ▼
Converge:           T016 (SecurityCapabilities step, needs T014 + T015)
```

### Phases 5–6–9: P2/P3 Stories (3 parallel tracks after Phase 2)

```
Track A: T017 (US3 — SystemComponents)
Track B: T018 (US4 — AuthorizationBoundaries)
Track C: T019 (US5 — AssignRoles) → T020 (US6 — VerifyRoles)
         T021 (US7 — SetCategorization, parallel with Track C)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (wizard shell + backend prep)
3. Complete Phase 3: US1 — System Registration
4. **STOP and VALIDATE**: Open wizard, register a system, confirm it appears in portfolio with "Setup Incomplete" badge
5. Deploy/demo if ready — users can register systems immediately

### Incremental Delivery

1. Setup + Foundational → Wizard shell ready
2. US1 → System registration works → **Deploy MVP**
3. US2 → Capability linking works → Deploy
4. US3 + US4 (parallel) → Components + boundaries → Deploy
5. US5 + US6 → Role assignment + verification → Deploy
6. US7 → Categorization → Deploy (all 7 steps complete)
7. Polish → Performance + docs → Final release

### Parallel Team Strategy

With multiple developers after Foundational:
- Developer A: US1 (P1) → US2 (P2, has backend chain)
- Developer B: US3 (P2) → US4 (P2) → US5 (P3) → US6 (P3)
- Developer C: US7 (P3) → Documentation (US10)
- All: Performance polish (US9) at the end

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks in the same phase
- [Story] label maps task to specific user story for traceability
- Each user story is independently testable after Foundational phase
- Commit after each task or logical group of tasks
- Stop at any checkpoint to validate story independently
- The wizard does NOT change RMF phase — systems start in Prepare; gate advancement is handled by the existing advanceRmfStep endpoint
- "Setup Incomplete" badge is computed dynamically, not stored — avoids stale state when data changes outside the wizard
- All behavior changes MUST include corresponding test changes per Constitution Principle III (NON-NEGOTIABLE)
