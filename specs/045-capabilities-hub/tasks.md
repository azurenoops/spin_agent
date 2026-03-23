# Tasks: Unified Security Capabilities Hub

**Input**: Design documents from `/specs/045-capabilities-hub/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/api.md ✅, quickstart.md ✅

**Tests**: Included — spec defines 4 performance tests (PT-001–PT-004) and plan lists 5 test files.

**Organization**: Tasks grouped by user story (7 stories, P1–P7) for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US7)
- Setup/Foundational/Polish phases have NO story label

---

## Phase 1: Setup

**Purpose**: Schema extensions, seed data, and shared TypeScript types needed before any feature work.

- [X] T001 Extend CspProfile with CspService DTO and Services property in src/Ato.Copilot.Mcp/Services/CspProfileService.cs — add `CspService` class (Name, Category, Description, Controls), add `Services` list to `CspProfile`, update profile loader to try services[] first then fall back to flat controls[]
- [X] T002 [P] Rewrite src/seed-data/csp-profiles/azure-gov-fedramp-high.json with services[] array format — group existing controls under ~10 CSP service objects (Microsoft Entra ID, Azure Key Vault, Azure Monitor, etc.) per research finding R8
- [X] T003 [P] Add TypeScript types for import preview/result, coverage response, and component link operations in src/Ato.Copilot.Dashboard/src/types/capabilities.ts — CspImportResult, CrmImportResult, CspImportPreview, CrmImportPreview, CoverageResponse, OrgWideCoverage, FamilyCoverage, SystemCoverage, ConflictDetail per contracts/api.md

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core service shell and API enhancements that MUST be complete before any user story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Create CapabilityImportService.cs in src/Ato.Copilot.Mcp/Services/ with constructor injection (AtoCopilotContext, CspProfileService, CapabilityService, ComponentService, OrgInheritanceService, NarrativeTemplateService, ILogger) and register in DI container. Include XML documentation on the class and all public methods per constitution VI.
- [X] T005 [P] Enhance GET /capabilities endpoint in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs to include linkedComponents array (id, name, componentType from ComponentCapabilityLink join) and systemCount per capability
- [X] T006 [P] Add API client functions in src/Ato.Copilot.Dashboard/src/api/capabilities.ts — importCspProfile(), importCrm(), getCoverage(), linkComponentCapabilities(), unlinkComponentCapability() per contracts/api.md; add coverage KPI call to src/Ato.Copilot.Dashboard/src/api/portfolio.ts

**Checkpoint**: Foundation ready — user story implementation can begin

---

## Phase 3: User Story 1 — CSP Profile Import via Capabilities Page (Priority: P1) 🎯 MVP

**Goal**: Import a CSP profile from the Capabilities page, creating the full Component → Capability → Control Mapping → Org Inheritance → Narrative pipeline in one transactional operation.

**Independent Test**: Import the Azure Gov FedRAMP High profile and verify components, capabilities, mappings, inheritance designations, and narratives all exist with correct linkages.

**FRs**: FR-001, FR-002, FR-003, FR-004, FR-005, FR-019, FR-020

### Implementation for User Story 1

- [X] T007 [US1] Implement FindOrCreateComponentAsync and FindOrCreateCapabilityAsync dedup methods in src/Ato.Copilot.Mcp/Services/CapabilityImportService.cs — component dedup by (Name, ComponentType=Thing, RegisteredSystemId=null), capability dedup by (Name, Provider) case-insensitive per research R3
- [X] T008 [US1] Implement ImportCspProfileAsync in src/Ato.Copilot.Mcp/Services/CapabilityImportService.cs — parse services[] from CspProfileService → create/reuse components per service → create/reuse capabilities per service (Provider+Category grouping) → bulk-add ComponentCapabilityLinks → create CapabilityControlMappings with Primary conflict→Supporting resolution (FR-019) → call OrgInheritanceService.DeriveOrgDefaultsAsync() → call NarrativeTemplateService.GenerateEnrichedNarrative() per mapping → single SaveChangesAsync (FR-020) per research R1. Include structured Serilog logging: log profile name, step progress, import counts (components/capabilities/mappings created vs reused), and total elapsed duration per constitution V.
- [X] T009 [US1] Implement ImportCspProfilePreviewAsync (dryRun mode) in src/Ato.Copilot.Mcp/Services/CapabilityImportService.cs — same parsing and dedup logic but returns preview counts and conflict details without persisting, per contracts/api.md dryRun response
- [X] T010 [US1] Add POST /capabilities/import/csp-profile endpoint in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs — accept profileId, conflictResolution, dryRun; delegate to CapabilityImportService; return 200 with import/preview result, 404 if profile not found
- [X] T011 [US1] Remove POST /inheritance/apply-profile endpoint from src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs — replaced by POST /capabilities/import/csp-profile per contracts/api.md
- [X] T012 [P] [US1] Create CspImportDialog.tsx in src/Ato.Copilot.Dashboard/src/components/capabilities/ — profile selector dropdown (from GET /inheritance/csp-profiles), dryRun preview step showing counts and conflicts, confirm button calling importCspProfile(dryRun=false), result summary with created/reused counts. Must show loading state with spinner and elapsed time during import execution (>2s Operations require progress indicators per constitution VII).
- [X] T013 [US1] Add "Import CSP Profile" button to src/Ato.Copilot.Dashboard/src/pages/CapabilityLibrary.tsx — opens CspImportDialog, refreshes capability list on successful import

### Tests for User Story 1

- [X] T014 [P] [US1] Add unit tests in tests/Ato.Copilot.Tests.Unit/CspProfileServiceExtTests.cs — test services[] parsing with multiple services, backward compat with flat controls[] only, mixed format handling
- [X] T015 [P] [US1] Add unit tests in tests/Ato.Copilot.Tests.Unit/CapabilityImportServiceTests.cs — CSP pipeline orchestration: component creation, capability dedup (Name+Provider case-insensitive), Primary conflict resolution (existing Primary → new becomes Supporting), narrative generation calls, single SaveChangesAsync
- [X] T016 [US1] Add integration tests in tests/Ato.Copilot.Tests.Integration/CapabilityImportEndpointTests.cs — POST /capabilities/import/csp-profile dryRun=true returns preview, dryRun=false creates full pipeline, duplicate import reuses existing records

**Checkpoint**: CSP Profile Import fully functional — User Story 1 independently testable

---

## Phase 4: User Story 2 — Capabilities Page Coverage Dashboard (Priority: P2)

**Goal**: Show coverage summary cards (Total Capabilities, Mapped Controls, Gap Controls, Coverage %) on the Capabilities page with a 3-layer header, and a Coverage % KPI card on the Portfolio Risk Profile dashboard.

**Independent Test**: Create capabilities with varying control mappings and verify summary cards show accurate counts. Verify Coverage % KPI on Portfolio Risk Profile.

**FRs**: FR-008, FR-009, FR-010, FR-018, FR-021

### Implementation for User Story 2

- [X] T017 [US2] Implement ComputeCoverageAsync in src/Ato.Copilot.Mcp/Services/CapabilityImportService.cs — query CapabilityControlMappings for mapped control count, determine baseline denominator (highest active system baseline → CSP profile declared baseline fallback → null per research R5), compute per-family breakdown, optional per-system breakdown
- [X] T018 [US2] Add GET /capabilities/coverage endpoint in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs — accept includePerSystem and includePerFamily query params, return CoverageResponse with orgWide (including null for no-baseline case) and optional perSystem array per contracts/api.md
- [X] T019 [P] [US2] Create CoverageCards.tsx in src/Ato.Copilot.Dashboard/src/components/capabilities/ — four summary cards: Total Capabilities, Mapped Controls, Gap Controls, Coverage % (or "N/A" when null); responsive grid layout
- [X] T020 [US2] Add coverage cards section and 3-layer contextual header ("Components → Capabilities → Control Inheritance") to src/Ato.Copilot.Dashboard/src/pages/CapabilityLibrary.tsx — fetch coverage on mount, show CoverageCards above capability list, add header explaining the model (FR-010)
- [X] T021 [US2] Add Coverage % KPI card to src/Ato.Copilot.Dashboard/src/pages/PortfolioRiskProfile.tsx — call GET /capabilities/coverage, render orgWide.coveragePercent as a KPI card (or "N/A" when null) alongside existing KPI cards after line ~84 per research R5

### Tests for User Story 2

- [X] T022 [P] [US2] Add unit tests in tests/Ato.Copilot.Tests.Unit/CoverageComputationTests.cs — coverage % calculation, per-family breakdown, zero-systems fallback to CSP baseline, null when no CSP profiles, empty capabilities returns zero counts
- [X] T023 [US2] Add integration tests in tests/Ato.Copilot.Tests.Integration/CoverageEndpointTests.cs — GET /capabilities/coverage with data returns correct percentages, with no systems returns CSP fallback, with no data returns null fields

**Checkpoint**: Coverage dashboard fully functional — User Stories 1 AND 2 independently testable

---

## Phase 5: User Story 3 — CRM Import via Capabilities Page (Priority: P3)

**Goal**: Import a CRM spreadsheet (CSV/Excel) from the Capabilities page, grouping rows by Provider + NIST Family into capabilities and creating the full pipeline.

**Independent Test**: Upload a CRM CSV with multiple providers and verify components, capabilities, mappings, and inheritance records are correctly created and linked.

**FRs**: FR-006, FR-007

### Implementation for User Story 3

- [X] T024 [US3] Implement ImportCrmAsync pipeline in src/Ato.Copilot.Mcp/Services/CapabilityImportService.cs — accept parsed CRM rows + column mapping → group by provider (one component per provider) → group by provider+NIST family (one capability per group, e.g., "Azure / Access Control") → bulk ComponentCapabilityLinks → CapabilityControlMappings → DeriveOrgDefaultsAsync → GenerateEnrichedNarrative → single SaveChangesAsync; handle empty provider as "Unspecified Provider" (no component), skip unmatched control IDs with count in result. Include structured Serilog logging: log file name, row count, step progress, import counts, and total elapsed duration per constitution V.
- [X] T025 [US3] Implement ImportCrmPreviewAsync (dryRun mode) in src/Ato.Copilot.Mcp/Services/CapabilityImportService.cs — parse + group + dedup check without persisting, return preview counts including unmatchedRows
- [X] T026 [US3] Add POST /capabilities/import/crm endpoint in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs — accept multipart/form-data (file + columnMapping JSON + conflictResolution + dryRun), call CrmExportService.ParseCsv()/ParseExcel() then delegate to ImportCrmAsync/PreviewAsync, return 200 with result per contracts/api.md
- [X] T027 [US3] Remove POST /inheritance/import/apply and POST /inheritance/import/preview endpoints from src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs — replaced by POST /capabilities/import/crm per contracts/api.md
- [X] T028 [P] [US3] Create CrmImportDialog.tsx in src/Ato.Copilot.Dashboard/src/components/capabilities/ — file upload step (CSV/XLSX), column mapping step (auto-detect + manual override), dryRun preview step showing counts + unmatchedRows, confirm step, result summary. Must show loading state with spinner and elapsed time during import execution (>2s operations require progress indicators per constitution VII).
- [X] T029 [US3] Add "Import CRM" button to src/Ato.Copilot.Dashboard/src/pages/CapabilityLibrary.tsx — opens CrmImportDialog, refreshes capability list and coverage on successful import

### Tests for User Story 3

- [X] T030 [P] [US3] Add CRM-specific unit tests in tests/Ato.Copilot.Tests.Unit/CapabilityImportServiceTests.cs — CRM pipeline: provider+family grouping produces correct capability names, empty provider grouped as "Unspecified Provider", unmatched control IDs counted, existing provider component reused
- [X] T031 [US3] Add CRM integration tests in tests/Ato.Copilot.Tests.Integration/CapabilityImportEndpointTests.cs — POST /capabilities/import/crm dryRun returns preview, apply creates full pipeline, duplicate import reuses existing records

**Checkpoint**: CRM Import fully functional — User Stories 1, 2, AND 3 independently testable

---

## Phase 6: User Story 4 — Link Components to Capabilities (Priority: P4)

**Goal**: Allow users to manually link existing components to capabilities via a component picker modal.

**Independent Test**: Create a component and a capability independently, link them via the modal, confirm the link appears as a badge on the capability card.

**FRs**: FR-012

### Implementation for User Story 4

- [X] T032 [US4] Add POST /components/{componentId}/capabilities and DELETE /components/{componentId}/capabilities/{capabilityId} endpoints in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs — bulk link (accepts capabilityIds array, returns linksCreated + linksAlreadyExist) and single unlink (returns 204) per contracts/api.md
- [X] T033 [P] [US4] Create ComponentPickerModal.tsx in src/Ato.Copilot.Dashboard/src/components/capabilities/ — multi-select component list with search/filter by type and name, pre-selected indicators for already-linked components, save button calls linkComponentCapabilities()
- [X] T034 [US4] Add "Link Components" action to capability cards in src/Ato.Copilot.Dashboard/src/pages/CapabilityLibrary.tsx — opens ComponentPickerModal for the selected capability, refreshes component badges on save

**Checkpoint**: Component linking functional — all stories through US4 independently testable

---

## Phase 7: User Story 5 — Control Inheritance Page Simplification (Priority: P5)

**Goal**: Remove old import buttons from Control Inheritance page, add cross-link banner to Control Inheritance page, and show component context in org default tooltips.

**Independent Test**: Navigate to Control Inheritance page and confirm banner renders, old buttons are gone, and tooltips show component context.

**FRs**: FR-013, FR-014, FR-015

### Implementation for User Story 5

- [X] T035 [P] [US5] Remove CSP Profile import button (~line 425) and CRM Import button (~line 442) from src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx — remove button JSX, associated state, and dialog imports per research R7
- [X] T036 [US5] Add cross-link banner to top of src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx — "Designations derived from Security Capabilities. [Manage Capabilities →]" with React Router link to /capabilities per FR-013
- [X] T037 [US5] Add component context tooltips on org default indicators in src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx — on hover, show which components back the source capability using data from enhanced GET /capabilities response (FR-015)

**Checkpoint**: Control Inheritance page simplified — no direct import actions remain

---

## Phase 8: User Story 6 — Component Page Capability Coverage (Priority: P6)

**Goal**: Show capability coverage counts per component and provide a "Create Capability from Component" quick action.

**Independent Test**: View a component with linked capabilities and confirm counts display. Use quick action to create a pre-filled capability.

**FRs**: FR-016, FR-017

### Implementation for User Story 6

- [X] T038 [P] [US6] Add capability coverage counts to src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx — display count of linked capabilities and mapped controls per component (query from GET /capabilities linkedComponents data or add a component-level count)
- [X] T039 [US6] Add "Create Capability from Component" quick action on Thing-type components in src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx — button opens capability creation form with name and provider pre-filled from the component per FR-017

**Checkpoint**: Component page enhanced with capability traceability

---

## Phase 9: User Story 7 — Guided Empty State (Priority: P7)

**Goal**: Show a guided onboarding experience when no capabilities exist.

**Independent Test**: Load the Capabilities page with zero capabilities and confirm three action cards display with working links.

**FRs**: FR-011

### Implementation for User Story 7

- [X] T040 [P] [US7] Create GuidedEmptyState.tsx in src/Ato.Copilot.Dashboard/src/components/capabilities/ — three action cards: "Create Manually" (description + opens capability form), "Import CSP Profile" (opens CspImportDialog), "Import CRM" (opens CrmImportDialog); responsive card layout
- [X] T041 [US7] Integrate GuidedEmptyState.tsx into src/Ato.Copilot.Dashboard/src/pages/CapabilityLibrary.tsx — conditional render: show GuidedEmptyState when capabilities count is zero, show normal list view otherwise

**Checkpoint**: First-time user experience complete — all 7 user stories fully functional

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Performance tests, documentation updates, and final validation.

- [X] T042 [P] Add performance tests in tests/Ato.Copilot.Tests.Integration/ — PT-001: CspProfileImport_HighBaseline_CompletesWithinPerformanceBudget (<30s) in CapabilityImportEndpointTests.cs; PT-002: CrmImport_325Rows_CompletesWithinPerformanceBudget (<30s) in CapabilityImportEndpointTests.cs; PT-003: CoverageEndpoint_50Capabilities_ReturnsWithinBudget (<2s) in CoverageEndpointTests.cs; PT-004: CspProfileImport_DuplicateRun_NoPerformanceDegradation (<30s) in CapabilityImportEndpointTests.cs
- [ ] T043 [P] Rewrite docs/guides/security-capabilities.md as the primary Capabilities Hub guide — CSP profile import flow, CRM import flow, coverage dashboard, component badges, 3-layer model, guided empty state, link-components workflow
- [X] T044 [P] Update docs/guides/control-inheritance.md — remove CSP Profile and CRM Import button references, add cross-link banner documentation, update org default tooltip docs
- [X] T045 [P] Update docs/architecture/data-model.md — add CSP profile services[] schema extension, document import pipeline data flow diagram (CSP/CRM → Components → Capabilities → Mappings → Inheritance → Narratives)
- [X] T046 [P] Update docs/architecture/overview.md — add Capabilities Hub architecture section showing 3-layer model, update system diagram with import pipeline
- [X] T047 [P] Update docs/api/mcp-server.md — add GET /capabilities/coverage, POST /capabilities/import/csp-profile, POST /capabilities/import/crm; remove POST /inheritance/apply-profile, POST /inheritance/import/apply, POST /inheritance/import/preview
- [X] T048 [P] Update docs/architecture/agent-tool-catalog.md — add coverage endpoint, update CSP profile import endpoint to reflect new pipeline, add import-via-capabilities endpoints
- [X] T049 [P] Update docs/reference/tool-inventory.md — add coverage endpoint row, update CSP/CRM import rows; update docs/reference/glossary.md — add Capabilities Hub, 3-Layer Model, Coverage %, Gap Controls terms
- [X] T050 [P] Update docs/guides/issm-guide.md — change CSP profile import instructions to reference Capabilities page; update docs/guides/ao-quick-reference.md — add Coverage % KPI to Portfolio Risk Profile section
- [X] T051 Add CHANGELOG.md version entry and create docs/release-notes/ file for Feature 045
- [X] T052 Run quickstart.md validation — build backend, build frontend, run unit tests, run integration tests, verify Docker deployment. Manually verify PERF-004 (Capabilities page load <2s), PERF-005 (KPI card overhead <200ms), and PERF-006 (preview dialog <5s) as frontend rendering budgets.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **User Stories (Phases 3–9)**: All depend on Foundational (Phase 2) completion
  - US1 (P1): No dependencies on other stories — **MVP target**
  - US2 (P2): No dependencies on other stories (runs independent coverage queries)
  - US3 (P3): Shares dedup helpers from US1 (T007) but these are in Foundational-adjacent code; can start after Phase 2
  - US4 (P4): No dependencies on other stories
  - US5 (P5): Benefits from US1/US3 completion (import buttons removed only make sense when new path exists) — **recommended after US1+US3**
  - US6 (P6): No dependencies on other stories
  - US7 (P7): Benefits from US1+US3 (empty state action cards link to import dialogs) — **recommended after US1+US3**
- **Polish (Phase 10)**: Depends on all user stories being complete

### Within Each User Story

- Backend service methods before endpoints
- Endpoints before frontend components
- Frontend components before page integration
- Implementation before tests (tests validate the implementation)

### Key File Touchpoints

| File | Tasks |
|------|-------|
| CapabilityImportService.cs | T004, T007, T008, T009, T017, T024, T025 |
| DashboardEndpoints.cs | T005, T010, T011, T018, T026, T027, T032 |
| CapabilityLibrary.tsx | T013, T020, T029, T034, T041 |
| ControlInheritance.tsx | T035, T036, T037 |
| CapabilityImportServiceTests.cs | T015, T030 |
| CapabilityImportEndpointTests.cs | T016, T031, T042 |

### Parallel Opportunities

- **Phase 1**: T002 and T003 can run in parallel (different files)
- **Phase 2**: T005 and T006 can run in parallel after T004 (different files)
- **Phase 3 (US1)**: T012 (frontend) and T014, T015 (unit tests) can run in parallel with each other and with T007–T011 (backend) once their inputs exist
- **Phase 4 (US2)**: T019 (frontend component) and T022 (unit tests) can run in parallel with T017–T018 (backend)
- **Phase 5 (US3)**: T028 (frontend component) and T030 (unit tests) can run in parallel with T024–T027 (backend)
- **Phase 6 (US4)**: T033 (frontend component) can run in parallel with T032 (backend endpoint)
- **Phases 7–9**: Can run in parallel if staffed (US5, US6, US7 touch different files)
- **Phase 10**: All doc updates (T043–T050) can run in parallel

---

## Parallel Example: User Story 1 (Phase 3)

```bash
# Backend (sequential within):
T007: Dedup helpers in CapabilityImportService.cs
T008: CSP import pipeline in CapabilityImportService.cs
T009: dryRun preview in CapabilityImportService.cs
T010: POST endpoint in DashboardEndpoints.cs
T011: Remove old endpoint in DashboardEndpoints.cs

# Frontend (parallel with backend after T003):
T012: CspImportDialog.tsx (new file, no backend dependency for component creation)
T013: CapabilityLibrary.tsx integration (needs T012)

# Tests (parallel with frontend):
T014: CspProfileServiceExtTests.cs (can start after T001)
T015: CapabilityImportServiceTests.cs (can start after T008)
T016: Integration tests (needs T010)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T003)
2. Complete Phase 2: Foundational (T004–T006)
3. Complete Phase 3: User Story 1 (T007–T016)
4. **STOP and VALIDATE**: Import a CSP profile end-to-end, verify full pipeline
5. Deploy/demo if ready — this is the highest-value single delivery

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. **US1** → CSP Profile Import works → Deploy/Demo (**MVP!**)
3. **US2** → Coverage Dashboard visible → Deploy/Demo
4. **US3** → CRM Import works → Deploy/Demo
5. **US4** → Manual component linking → Deploy/Demo
6. **US5** → Control Inheritance page cleaned up → Deploy/Demo
7. **US6** → Component page enhanced → Deploy/Demo
8. **US7** → Guided empty state → Deploy/Demo
9. **Polish** → Docs, perf tests, validation → Final release

### FR Coverage

| FR | Task(s) | Phase |
|----|---------|-------|
| FR-001 (CSP import pipeline) | T008, T010 | 3 (US1) |
| FR-002 (Preview dialog) | T009, T012, T025, T028 | 3 (US1), 5 (US3) |
| FR-003 (Deduplication) | T007 | 3 (US1) |
| FR-004 (Org inheritance derivation) | T008 | 3 (US1) |
| FR-005 (Narrative generation) | T008 | 3 (US1) |
| FR-006 (CRM import) | T024, T026 | 5 (US3) |
| FR-007 (CRM grouping) | T024 | 5 (US3) |
| FR-008 (Coverage cards) | T019, T020 | 4 (US2) |
| FR-009 (Component badges) | T005 | 2 (Found.) |
| FR-010 (3-layer header) | T020 | 4 (US2) |
| FR-011 (Guided empty state) | T040, T041 | 9 (US7) |
| FR-012 (Link Components) | T032, T033, T034 | 6 (US4) |
| FR-013 (Cross-link banner) | T036 | 7 (US5) |
| FR-014 (Remove old buttons) | T035 | 7 (US5) |
| FR-015 (Component tooltips) | T037 | 7 (US5) |
| FR-016 (Component coverage) | T038 | 8 (US6) |
| FR-017 (Create cap from component) | T039 | 8 (US6) |
| FR-018 (Coverage endpoint) | T017, T018 | 4 (US2) |
| FR-019 (Primary conflict) | T008 | 3 (US1) |
| FR-020 (Transactional) | T008 | 3 (US1) |
| FR-021 (Coverage KPI card) | T021 | 4 (US2) |

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks in the same phase
- [Story] label maps each task to its user story for traceability
- Each user story is independently completable and testable
- All entities exist — no EF Core migrations needed (data-model.md confirms)
- CapabilityImportService.cs is the most-touched backend file (7 tasks across 3 stories) — implement sequentially within each story
- DashboardEndpoints.cs is modified across 4 phases — each phase adds/removes distinct endpoint groups
- CapabilityLibrary.tsx is modified across 5 phases — each phase adds distinct UI sections
- Commit after each task or logical group
- Stop at any checkpoint to validate the story independently
