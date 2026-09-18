# Tasks: 015 — Persona-Driven RMF Workflows

**Input**: Design documents from `/specs/015-persona-workflows/`
**Prerequisites**: plan.md ✅, spec.md ✅, data-model.md ✅, contracts/mcp-tools.md ✅, research.md ✅, quickstart.md ✅

**Tests**: Included per constitution principle III (Testing Standards — NON-NEGOTIABLE). Each tool gets positive + negative unit tests.

**Organization**: Tasks are grouped by RMF workflow phase (matching spec Part 5 and plan.md phases). Each phase is independently testable and delivers an increment of RMF lifecycle capability.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[US#]**: Maps to spec capability numbers (1.x = Phase 1, 2.x = Phase 2, etc.)
- Exact file paths included in all task descriptions

---

## Phase Mapping

The spec, plan, and tasks use different granularity. Plan phase numbers match plan.md headers; spec references use capability section numbers (§x.y within Part 5) or Part numbers for non-capability sections:

| Spec Section | Plan Phase | Task Phases | User Stories |
|-----------|-----------|-------------|-------------|
| — | Phase 0 (Research) | — (completed) | — |
| — | — | Phase 1 (Setup), Phase 2 (Foundational) | — |
| §1.1–§1.11: RMF Foundation | Phase 1 (RMF Foundation) | Phase 3 (US1), Phase 4 (US2), Phase 5 (US3), Phase 6 (US4) | US1–US4 |
| §2.1–§2.8: SSP & IaC | Phase 2 (SSP Authoring) | Phase 7 (US5), Phase 8 (US6) | US5–US6 |
| §3.1–§3.13: Assessment & Auth | Phase 3 (Assessment & Auth) | Phase 9 (US7), Phase 10 (US8) | US7–US8 |
| §4.1–§4.7: ConMon & Lifecycle | Phase 4 (ConMon & Lifecycle) | Phase 11 (US9) | US9 |
| §5.1–§5.6: Interoperability | Phase 5 (Interoperability) | Phase 12 (US10), Phase 13 (US11), Phase 16 (§5.3–§5.6) | US10–US11 |
| Part 6: Agent Routing & UX | — | Phase 14 (US12), Phase 15 (US13) | US12–US13 |
| Part 8: Documentation | Phase 6 (Documentation) | Phase 16 (Polish) + per-phase inline docs | — |
| §9a.1–§9a.6: Monitoring Integration | Phase 7 (Monitoring Integration) | Phase 17 (Integration) | — |

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization — new enums, constants, reference data files, and DbContext scaffolding that all phases depend on.

- [X] T001 Create branch `015-persona-workflows` from `main` and verify clean build
- [X] T002 [P] Add new enums (`RmfPhase`, `SystemType`, `MissionCriticality`, `AzureCloudEnvironment`, `ImpactValue`, `RmfRole`, `TailoringAction`, `InheritanceType`, `ImplementationStatus`, `EffectivenessDetermination`, `CatSeverity`, `AuthorizationDecisionType`, `PoamStatus`) to `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
- [X] T003 [P] Add `AuthorizingOfficial` role constant to `src/Ato.Copilot.Core/Constants/ComplianceRoles.cs`
- [X] T004 [P] Extend `src/Ato.Copilot.Core/Constants/ComplianceFrameworks.cs` with baseline control counts (Low=131, Moderate=325, High=421), RMF step display names, and FIPS 199 notation helpers
- [X] T005 [P] Create reference data file `src/Ato.Copilot.Agents/Compliance/Resources/nist-800-53-baselines.json` with Low/Moderate/High control ID lists
- [X] T006 [P] Create reference data file `src/Ato.Copilot.Agents/Compliance/Resources/sp800-60-information-types.json` (~180 entries with provisional C/I/A impacts)
- [X] T007 [P] Create reference data file `src/Ato.Copilot.Agents/Compliance/Resources/cnssi-1253-overlays.json` (~450 entries with IL-specific parameter overrides)
- [X] T008 [P] Create unit tests for new enums and constants in `tests/Ato.Copilot.Tests.Unit/Models/RmfEnumAndConstantTests.cs`

**Checkpoint**: All shared enums, constants, and reference data files exist. Build passes with zero warnings.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core entities and DbContext changes that ALL user story phases depend on. `RegisteredSystem` is the anchor FK for everything downstream.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T009 Create `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs` with `RegisteredSystem` entity (all fields per data-model.md) including `AzureEnvironmentProfile` as owned entity
- [X] T010 [P] Add `SecurityCategorization` and `InformationType` entities to `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`
- [X] T011 [P] Add `AuthorizationBoundary` and `RmfRoleAssignment` entities to `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`
- [X] T012 [P] Add `ControlBaseline`, `ControlTailoring`, and `ControlInheritance` entities to `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`
- [X] T013 Add 8 new DbSets (`RegisteredSystems`, `SecurityCategorizations`, `InformationTypes`, `AuthorizationBoundaries`, `RmfRoleAssignments`, `ControlBaselines`, `ControlTailorings`, `ControlInheritances`) to `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` with EF Core configuration (keys, relationships, owned types, JSON columns, unique constraints)
- [X] T014 Create EF Core migration for Phase 2 foundational entities
- [X] T015 [P] Create unit tests for `RegisteredSystem` entity validation in `tests/Ato.Copilot.Tests.Unit/Models/RegisteredSystemTests.cs`
- [X] T016 [P] Create unit tests for `SecurityCategorization` computed properties (high-water mark, IL derivation, FIPS 199 notation) in `tests/Ato.Copilot.Tests.Unit/Models/SecurityCategorizationTests.cs`
- [X] T017 [P] Create unit tests for `ControlBaseline` / `ControlTailoring` / `ControlInheritance` validation in `tests/Ato.Copilot.Tests.Unit/Models/BaselineModelTests.cs`

**Checkpoint**: Foundation ready — `RegisteredSystem` anchor entity and all Phase 1 entities exist in DbContext. All model tests pass. User story implementation can now begin.

---

## Phase 3: User Story 1 — System Registration & RMF Lifecycle (Priority: P1, Spec §1.1–1.4) 🎯 MVP

**Goal**: ISSMs can register systems, define authorization boundaries, assign RMF roles, and track RMF lifecycle state with gate conditions.

**Independent Test**: Register a system → define boundary → assign roles → advance through Prepare → Categorize gate check.

### Services for US1

- [X] T018 [US1] Create `src/Ato.Copilot.Core/Interfaces/Compliance/IRmfLifecycleService.cs` with methods: `RegisterSystemAsync`, `GetSystemAsync`, `ListSystemsAsync`, `AdvanceRmfStepAsync`, `CheckGateConditionsAsync`
- [X] T019 [US1] Implement `RmfLifecycleService` in `src/Ato.Copilot.Agents/Compliance/Services/RmfLifecycleService.cs` — system registration, RBAC-scoped queries, RMF step state machine with gate validation per contracts
- [X] T020 [US1] Create `src/Ato.Copilot.Core/Interfaces/Compliance/IBoundaryService.cs` with methods: `DefineBoundaryAsync`, `ExcludeResourceAsync`, `GetBoundaryAsync`, `AutoDiscoverResourcesAsync`
- [X] T021 [US1] Implement `BoundaryService` in `src/Ato.Copilot.Agents/Compliance/Services/BoundaryService.cs` — boundary CRUD, Azure auto-discovery from subscription IDs

### Tools for US1

- [X] T022 [P] [US1] Create `RegisterSystemTool` in `src/Ato.Copilot.Agents/Compliance/Tools/RmfRegistrationTools.cs` implementing `compliance_register_system` per contracts
- [X] T023 [P] [US1] Create `ListSystemsTool` in `src/Ato.Copilot.Agents/Compliance/Tools/RmfRegistrationTools.cs` implementing `compliance_list_systems` with pagination
- [X] T024 [P] [US1] Create `GetSystemTool` in `src/Ato.Copilot.Agents/Compliance/Tools/RmfRegistrationTools.cs` implementing `compliance_get_system` with nested entities
- [X] T025 [US1] Create `AdvanceRmfStepTool` in `src/Ato.Copilot.Agents/Compliance/Tools/RmfRegistrationTools.cs` implementing `compliance_advance_rmf_step` with gate checks
- [X] T026 [P] [US1] Create `DefineBoundaryTool` in `src/Ato.Copilot.Agents/Compliance/Tools/RmfRegistrationTools.cs` implementing `compliance_define_boundary`
- [X] T027 [P] [US1] Create `ExcludeFromBoundaryTool` in `src/Ato.Copilot.Agents/Compliance/Tools/RmfRegistrationTools.cs` implementing `compliance_exclude_from_boundary`
- [X] T028 [P] [US1] Create `AssignRmfRoleTool` in `src/Ato.Copilot.Agents/Compliance/Tools/RmfRegistrationTools.cs` implementing `compliance_assign_rmf_role`
- [X] T029 [P] [US1] Create `ListRmfRolesTool` in `src/Ato.Copilot.Agents/Compliance/Tools/RmfRegistrationTools.cs` implementing `compliance_list_rmf_roles`

### MCP Registration for US1

- [X] T030 [US1] Register 8 new RMF registration tools in `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs` — inject services, wire tool methods
- [X] T031 [US1] Register `RmfLifecycleService` and `BoundaryService` in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs`

### Tests for US1

- [X] T032 [P] [US1] Create unit tests for `RegisterSystemTool` (valid registration, missing required fields, duplicate name) in `tests/Ato.Copilot.Tests.Unit/Tools/RmfRegistrationToolTests.cs`
- [X] T033 [P] [US1] Create unit tests for `AdvanceRmfStepTool` (successful advance, gate failure, force override, invalid step) in `tests/Ato.Copilot.Tests.Unit/Tools/RmfRegistrationToolTests.cs`
- [X] T034 [P] [US1] Create unit tests for boundary tools (define, exclude, auto-discover, invalid system) in `tests/Ato.Copilot.Tests.Unit/Tools/RmfRegistrationToolTests.cs`
- [X] T035 [P] [US1] Create unit tests for role assignment tools (assign, list, duplicate role, RBAC enforcement) in `tests/Ato.Copilot.Tests.Unit/Tools/RmfRegistrationToolTests.cs`

### Integration Test for US1

- [X] T204 [US1] Create end-to-end integration test in `tests/Ato.Copilot.Tests.Integration/Tools/RmfRegistrationIntegrationTests.cs` — register system → define boundary → assign roles → advance step → verify gate check, all via MCP HTTP bridge

### Phase Documentation for US1

- [X] T205 [P] [US1] Write tool reference entries for 8 registration tools in `docs/architecture/agent-tool-catalog.md` and registration workflow section in `docs/guides/issm-guide.md`

**Checkpoint**: US1 complete — systems can be registered, boundaries defined, roles assigned, and RMF state tracked. All 8 tools functional, integration-tested, and documented.

---

## Phase 4: User Story 2 — Security Categorization (Priority: P1, Spec §1.5) 🎯 MVP

**Goal**: ISSMs can perform FIPS 199 security categorization with SP 800-60 information types, compute high-water mark, derive DoD IL, and determine the appropriate NIST baseline.

**Independent Test**: Categorize a registered system with 3 information types → verify C/I/A high-water mark → verify IL derivation → verify FIPS 199 notation.

### Services for US2

- [X] T036 [US2] Create `src/Ato.Copilot.Core/Interfaces/Compliance/ICategorizationService.cs` with methods: `CategorizeSystemAsync`, `GetCategorizationAsync`, `SuggestInfoTypesAsync`, `ComputeHighWaterMarkAsync`
- [X] T037 [US2] Implement `CategorizationService` in `src/Ato.Copilot.Agents/Compliance/Services/CategorizationService.cs` — FIPS 199 C/I/A calculation, DoD IL derivation, SP 800-60 lookup, AI info type suggestion

### Tools for US2

- [X] T038 [P] [US2] Create `CategorizeSystemTool` in `src/Ato.Copilot.Agents/Compliance/Tools/CategorizationTools.cs` implementing `compliance_categorize_system` per contracts
- [X] T039 [P] [US2] Create `GetCategorizationTool` in `src/Ato.Copilot.Agents/Compliance/Tools/CategorizationTools.cs` implementing `compliance_get_categorization`
- [X] T040 [P] [US2] Create `SuggestInfoTypesTool` in `src/Ato.Copilot.Agents/Compliance/Tools/CategorizationTools.cs` implementing `compliance_suggest_info_types`

### MCP & DI Registration for US2

- [X] T041 [US2] Register 3 categorization tools in `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs` and `CategorizationService` in DI

### Tests for US2

- [X] T042 [P] [US2] Create unit tests for `CategorizeSystemTool` (valid categorization, zero info types, impact adjustment with justification) in `tests/Ato.Copilot.Tests.Unit/Tools/CategorizationToolTests.cs`
- [X] T043 [P] [US2] Create unit tests for high-water mark computation (all Low, mixed, all High, NSS flag) in `tests/Ato.Copilot.Tests.Unit/Tools/CategorizationToolTests.cs`
- [X] T044 [P] [US2] Create unit tests for DoD IL derivation (Low→IL2, Moderate→IL4, High→IL5, NSS→IL6) in `tests/Ato.Copilot.Tests.Unit/Tools/CategorizationToolTests.cs`

### Integration Test for US2

- [X] T206 [US2] Create integration test in `tests/Ato.Copilot.Tests.Integration/Tools/CategorizationIntegrationTests.cs` — categorize registered system with info types → verify high-water mark → verify IL derivation via MCP HTTP bridge

### Phase Documentation for US2

- [X] T207 [P] [US2] Write tool reference entries for 3 categorization tools in `docs/architecture/agent-tool-catalog.md` and categorization section in `docs/guides/issm-guide.md`

**Checkpoint**: US2 complete — systems can be categorized with FIPS 199, IL derived, baseline level determined. Integration-tested and documented.

---

## Phase 5: User Story 3 — Control Baseline Selection, Tailoring & Inheritance (Priority: P1, Spec §1.6–1.10) 🎯 MVP

**Goal**: ISSMs can select NIST 800-53 baselines, apply CNSSI 1253 overlays, tailor controls, declare inheritance, and generate CRMs.

**Independent Test**: Select baseline for a Moderate system → apply CNSSI 1253 IL4 overlay → tailor (add 2, remove 1) → set inheritance (50 inherited, 10 shared) → generate CRM → verify counts.

### Services for US3

- [X] T045 [US3] Create `src/Ato.Copilot.Core/Interfaces/Compliance/IBaselineService.cs` with methods: `SelectBaselineAsync`, `TailorBaselineAsync`, `SetInheritanceAsync`, `GetBaselineAsync`, `GenerateCrmAsync`
- [X] T046 [US3] Implement `BaselineService` in `src/Ato.Copilot.Agents/Compliance/Services/BaselineService.cs` — baseline selection from `nist-800-53-baselines.json`, CNSSI 1253 overlay application from `cnssi-1253-overlays.json`, tailoring with audit, inheritance tracking, CRM generation
- [X] T047 [US3] Create reference data loader service `src/Ato.Copilot.Agents/Compliance/Services/ReferenceDataService.cs` for loading and caching baseline, overlay, and SP 800-60 JSON files

### Tools for US3

- [X] T048 [P] [US3] Create `SelectBaselineTool` in `src/Ato.Copilot.Agents/Compliance/Tools/BaselineTools.cs` implementing `compliance_select_baseline` per contracts
- [X] T049 [P] [US3] Create `TailorBaselineTool` in `src/Ato.Copilot.Agents/Compliance/Tools/BaselineTools.cs` implementing `compliance_tailor_baseline`
- [X] T050 [P] [US3] Create `SetInheritanceTool` in `src/Ato.Copilot.Agents/Compliance/Tools/BaselineTools.cs` implementing `compliance_set_inheritance`
- [X] T051 [P] [US3] Create `GetBaselineTool` in `src/Ato.Copilot.Agents/Compliance/Tools/BaselineTools.cs` implementing `compliance_get_baseline`
- [X] T052 [US3] Create `GenerateCrmTool` in `src/Ato.Copilot.Agents/Compliance/Tools/BaselineTools.cs` — generate Customer Responsibility Matrix with inherited/shared/customer breakdowns and STIG applicability

### MCP & DI Registration for US3

- [X] T053 [US3] Register 5 baseline tools in `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs` and `BaselineService` + `ReferenceDataService` in DI

### Tests for US3

- [X] T054 [P] [US3] Create unit tests for `SelectBaselineTool` (Low/Moderate/High baseline, overlay application, missing categorization) in `tests/Ato.Copilot.Tests.Unit/Tools/BaselineToolTests.cs`
- [X] T055 [P] [US3] Create unit tests for `TailorBaselineTool` (add control, remove control, warn on overlay-required, duplicate tailoring) in `tests/Ato.Copilot.Tests.Unit/Tools/BaselineToolTests.cs`
- [X] T056 [P] [US3] Create unit tests for `SetInheritanceTool` (set inherited, set shared with responsibility, set customer, invalid control) in `tests/Ato.Copilot.Tests.Unit/Tools/BaselineToolTests.cs`
- [X] T057 [P] [US3] Create unit tests for CRM generation (correct counts, empty baseline, all inherited) in `tests/Ato.Copilot.Tests.Unit/Tools/BaselineToolTests.cs`

### Integration Test for US3

- [X] T208 [US3] Create integration test in `tests/Ato.Copilot.Tests.Integration/Tools/BaselineIntegrationTests.cs` — select baseline → apply overlay → tailor → set inheritance → generate CRM → verify counts via MCP HTTP bridge

### Phase Documentation for US3

- [X] T209 [P] [US3] Write tool reference entries for 5 baseline tools in `docs/architecture/agent-tool-catalog.md`, baseline/tailoring section in `docs/guides/issm-guide.md`, and update `docs/reference/nist-coverage.md`

**Checkpoint**: US3 complete — full baseline lifecycle: select → overlay → tailor → inheritance → CRM. Phase 1 of spec (RMF Foundation, capabilities 1.1–1.11) is fully delivered. Integration-tested and documented.

---

## Phase 6: User Story 4 — STIG Mapping Display (Priority: P2, Spec §1.11)

**Goal**: ISSMs and Engineers can see DISA STIG IDs and CAT levels mapped to each NIST control in the baseline.

**Independent Test**: Query STIG mappings for a control (e.g., AC-2) → see STIG Rule IDs, benchmark names, CAT levels.

- [X] T058 [P] [US4] Extend `StigControl` entity with XCCDF fields (`BenchmarkId`, `BenchmarkVersion`, `GroupId`, `RuleVersion`, `Weight`, `FixText`, `CheckContent`, `CciIds`) in `src/Ato.Copilot.Core/Models/Compliance/StigModels.cs`
- [X] T059 [P] [US4] Add `StigBenchmark` entity to `src/Ato.Copilot.Core/Models/Compliance/StigModels.cs`
- [X] T060 [US4] Create reference data file `src/Ato.Copilot.Agents/Compliance/Resources/cci-nist-mapping.json` (~7,575 CCI→NIST control entries)
- [X] T061 [US4] Expand `src/Ato.Copilot.Agents/Compliance/Resources/stig-controls.json` from 7 to ~200 priority STIG rules with XCCDF fields
- [X] T062 [US4] Create `ShowStigMappingTool` in `src/Ato.Copilot.Agents/Compliance/Tools/BaselineTools.cs` — query STIG rules mapped to NIST controls via CCI
- [X] T063 [US4] Register STIG mapping tool in `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs`
- [X] T064 [P] [US4] Create unit tests for STIG mapping (valid control, no mappings, CCI chain) in `tests/Ato.Copilot.Tests.Unit/Tools/BaselineToolTests.cs`

**Checkpoint**: US4 complete — STIG-to-NIST mapping visible for baseline controls.

---

## Phase 7: User Story 5 — SSP Authoring & Narrative Management (Priority: P1, Spec §2.1–2.3) 🎯 MVP

**Goal**: ISSOs and Engineers can write per-control implementation narratives, get AI-suggested drafts, auto-populate inherited control narratives, track SSP completeness, and generate SSP documents.

**Independent Test**: Write narrative for AC-1 → suggest narrative for AC-2 → batch-populate inherited controls → check progress (should show 3/325 customer + N inherited) → generate SSP Markdown.

### Entities & DbContext for US5

- [x] T065 [US5] Create `ControlImplementation` entity in `src/Ato.Copilot.Core/Models/Compliance/SspModels.cs` with all fields per data-model.md
- [x] T066 [US5] Add `ControlImplementations` DbSet to `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` with unique constraint on (`RegisteredSystemId`, `ControlId`)
- [ ] T067 [US5] Create EF Core migration for `ControlImplementation` entity *(known gap — DbSet + configuration exist in AtoCopilotContext.cs but formal migration not yet generated; tests pass via in-memory provider; required before production deployment)*

### Services for US5

- [x] T068 [US5] Create `src/Ato.Copilot.Core/Interfaces/Compliance/ISspService.cs` with methods: `WriteNarrativeAsync`, `SuggestNarrativeAsync`, `BatchPopulateNarrativesAsync`, `GetNarrativeProgressAsync`, `GenerateSspAsync`
- [x] T069 [US5] Implement `SspService` in `src/Ato.Copilot.Agents/Compliance/Services/SspService.cs` — narrative CRUD, AI suggestion via KnowledgeBase agent, inherited auto-population from CRM, progress tracking, SSP Markdown generation with per-control details

### Tools for US5

- [x] T070 [P] [US5] Create `WriteNarrativeTool` in `src/Ato.Copilot.Agents/Compliance/Tools/SspAuthoringTools.cs` implementing `compliance_write_narrative` per contracts
- [x] T071 [P] [US5] Create `SuggestNarrativeTool` in `src/Ato.Copilot.Agents/Compliance/Tools/SspAuthoringTools.cs` implementing `compliance_suggest_narrative`
- [x] T072 [P] [US5] Create `BatchPopulateNarrativesTool` in `src/Ato.Copilot.Agents/Compliance/Tools/SspAuthoringTools.cs` implementing `compliance_batch_populate_narratives`
- [x] T073 [P] [US5] Create `NarrativeProgressTool` in `src/Ato.Copilot.Agents/Compliance/Tools/SspAuthoringTools.cs` implementing `compliance_narrative_progress`
- [x] T074 [US5] Create `GenerateSspTool` in `src/Ato.Copilot.Agents/Compliance/Tools/SspAuthoringTools.cs` implementing `compliance_generate_ssp` (Markdown format initially)

### MCP & DI Registration for US5

- [x] T075 [US5] Register 5 SSP authoring tools in `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs` and `SspService` in DI

### Tests for US5

- [x] T076 [P] [US5] Create unit tests for `WriteNarrativeTool` (write new, update existing, invalid control, RBAC) in `tests/Ato.Copilot.Tests.Unit/Tools/SspAuthoringToolTests.cs`
- [x] T077 [P] [US5] Create unit tests for `SuggestNarrativeTool` (suggestion with confidence, no system context) in `tests/Ato.Copilot.Tests.Unit/Tools/SspAuthoringToolTests.cs`
- [x] T078 [P] [US5] Create unit tests for `BatchPopulateNarrativesTool` (populate inherited, skip customer, idempotent) in `tests/Ato.Copilot.Tests.Unit/Tools/SspAuthoringToolTests.cs`
- [x] T079 [P] [US5] Create unit tests for `NarrativeProgressTool` (100%, 0%, partial, family filter) in `tests/Ato.Copilot.Tests.Unit/Tools/SspAuthoringToolTests.cs`
- [x] T080 [P] [US5] Create unit tests for `GenerateSspTool` (full SSP, section filter, missing narratives warning) in `tests/Ato.Copilot.Tests.Unit/Tools/SspAuthoringToolTests.cs`

### Progress Indicators for US5

- [x] T210 [US5] Add streaming progress indicator to `GenerateSspTool` (30s+ for 325 controls) and `BatchPopulateNarrativesTool` — emit progress events via MCP progress notifications per spec §5.4

### Integration Test for US5

- [x] T211 [US5] Create integration test in `tests/Ato.Copilot.Tests.Integration/Tools/SspAuthoringIntegrationTests.cs` — write narrative → suggest narrative → batch populate inherited → check progress → generate SSP via MCP HTTP bridge

### Phase Documentation for US5

- [x] T212 [P] [US5] Write tool reference entries for 5 SSP tools in `docs/architecture/agent-tool-catalog.md` and SSP authoring workflow in `docs/guides/engineer-guide.md`

**Checkpoint**: US5 complete — SSP authoring workflow functional with progress indicators. Narratives can be written, suggested, auto-populated, and compiled into an SSP document. Integration-tested and documented. *(Note: T067 EF Core migration pending — see task for details.)*

---

## Phase 8: User Story 6 — IaC Diagnostics & Engineer Experience (Priority: P2, Spec §2.4–2.8)

**Goal**: Engineers get inline IaC diagnostics in VS Code (squiggly underlines), Quick Fix code actions, suggested fixes on findings, and an expanded STIG library.

**Independent Test**: Open a Bicep file with a non-compliant NSG → see CAT II warning squiggle → hover for STIG info → apply Quick Fix → verify fix applied.

### IaC Scanner Expansion

- [X] T081 [P] [US6] Expand IaC scanner rules from 5 to 50+ in existing scanner tool — add rules for NSG, storage encryption, key vault, logging, identity, RBAC per common STIG findings
- [X] T082 [P] [US6] Add `suggestedFix` field (unified diff format) to IaC finding results — language-specific for Bicep/HCL/ARM JSON

### VS Code Extension for US6

- [X] T083 [P] [US6] Create `extensions/vscode/src/diagnostics/iacDiagnosticsProvider.ts` — map IaC scan results to VS Code `Diagnostic` entries (CAT I/II → Error, CAT III → Warning) with STIG rule ID in message
- [X] T084 [P] [US6] Create `extensions/vscode/src/codeActions/iacCodeActionProvider.ts` — Quick Fix code actions that apply `suggestedFix` diffs, "Apply All Fixes" for non-conflicting changes
- [X] T085 [US6] Wire diagnostics and code action providers into VS Code extension activation in `extensions/vscode/src/extension.ts`

### Tests for US6

- [X] T086 [P] [US6] Create unit tests for expanded IaC scanner rules (50+ rules, suggested fix format) in `tests/Ato.Copilot.Tests.Unit/Tools/IacScannerToolTests.cs`
- [X] T087 [P] [US6] Create VS Code extension tests for diagnostics provider (diagnostic severity mapping, hover content) in `extensions/vscode/src/test/diagnostics.test.ts`
- [X] T088 [P] [US6] Create VS Code extension tests for code action provider (apply fix, apply all) in `extensions/vscode/src/test/codeActions.test.ts`

**Checkpoint**: US6 complete — Engineers see inline compliance diagnostics in VS Code with one-click fixes.

---

## Phase 9: User Story 7 — Assessment Artifacts & CAT Severity (Priority: P1, Spec §3.1–3.6) 🎯 MVP

**Goal**: SCAs can record per-control effectiveness determinations (Satisfied/OtherThanSatisfied), map to DoD CAT severity, take immutable snapshots, compare assessments, and verify evidence chain of custody.

**Independent Test**: Assess 5 controls (3 Satisfied, 2 OtherThanSatisfied with CAT II) → take snapshot → verify SHA-256 hash → run second assessment → compare snapshots.

### Entities & DbContext for US7

- [x] T089 [US7] Create `ControlEffectiveness` and `AssessmentRecord` entities in `src/Ato.Copilot.Core/Models/Compliance/AssessmentModels.cs`
- [x] T090 [US7] Add `ControlEffectiveness` and `AssessmentRecords` DbSets to `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
- [x] T091 [US7] Add `CatSeverity` nullable field to existing `ComplianceFinding` entity in `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
- [x] T092 [US7] Enhance existing `ComplianceSnapshot` entity with `IntegrityHash` (SHA-256) and `IsImmutable` flag in `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
- [x] T093 [US7] Enhance existing `ComplianceEvidence` entity with `CollectorIdentity`, `CollectionMethod`, `IntegrityVerifiedAt` fields in `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
- [x] T094 [US7] Add optional `RegisteredSystemId` FK to existing `ComplianceAssessment` entity (nullable for backward compat)
- [x] T095 [US7] Create EF Core migration for Phase 9 entities and modified entities

### Services for US7

- [x] T096 [US7] Create `src/Ato.Copilot.Core/Interfaces/Compliance/IAssessmentArtifactService.cs` with methods: `AssessControlAsync`, `TakeSnapshotAsync`, `CompareSnapshotsAsync`, `VerifyEvidenceAsync`, `CheckEvidenceCompletenessAsync`
- [x] T097 [US7] Implement `AssessmentArtifactService` in `src/Ato.Copilot.Agents/Compliance/Services/AssessmentArtifactService.cs` — per-control effectiveness recording, SHA-256 snapshot creation, diff comparison, evidence hash verification

### Tools for US7

- [x] T098 [P] [US7] Create `AssessControlTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AssessmentArtifactTools.cs` implementing `compliance_assess_control` per contracts
- [x] T099 [P] [US7] Create `TakeSnapshotTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AssessmentArtifactTools.cs` — create immutable SHA-256-hashed snapshot
- [x] T100 [P] [US7] Create `CompareSnapshotsTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AssessmentArtifactTools.cs` — side-by-side diff of two snapshots
- [x] T101 [P] [US7] Create `VerifyEvidenceTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AssessmentArtifactTools.cs` — recompute hash and verify evidence integrity
- [x] T102 [P] [US7] Create `CheckEvidenceCompletenessTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AssessmentArtifactTools.cs` — report controls with/without verified evidence
- [x] T213 [P] [US7] Create `GenerateSarTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AssessmentArtifactTools.cs` implementing `compliance_generate_sar` per contracts — Security Assessment Report with per-family findings, CAT breakdown, overall determination

### MCP & DI Registration for US7

- [x] T103 [US7] Register 6 assessment artifact tools in `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs` and `AssessmentArtifactService` in DI

### Tests for US7

- [x] T104 [P] [US7] Create unit tests for `AssessControlTool` (Satisfied, OtherThanSatisfied with CAT, missing CAT on OtherThanSatisfied, RBAC) in `tests/Ato.Copilot.Tests.Unit/Tools/AssessmentArtifactToolTests.cs`
- [x] T105 [P] [US7] Create unit tests for `TakeSnapshotTool` (create snapshot, verify hash, attempt modification blocked) in `tests/Ato.Copilot.Tests.Unit/Tools/AssessmentArtifactToolTests.cs`
- [x] T106 [P] [US7] Create unit tests for `CompareSnapshotsTool` (identical, different, missing snapshot) in `tests/Ato.Copilot.Tests.Unit/Tools/AssessmentArtifactToolTests.cs`
- [x] T107 [P] [US7] Create unit tests for evidence tools (verify integrity pass/fail, completeness calculation) in `tests/Ato.Copilot.Tests.Unit/Tools/AssessmentArtifactToolTests.cs`

### Integration Test for US7

- [x] T214 [US7] Create integration test in `tests/Ato.Copilot.Tests.Integration/Tools/AssessmentIntegrationTests.cs` — assess 5 controls → take snapshot → verify hash → generate SAR → compare snapshots via MCP HTTP bridge

### Phase Documentation for US7

- [x] T215 [P] [US7] Write tool reference entries for 6 assessment tools in `docs/architecture/agent-tool-catalog.md` and assessment workflow in `docs/guides/sca-guide.md`

**Checkpoint**: US7 complete — SCAs can record per-control effectiveness, take immutable snapshots, generate SAR, compare assessment cycles, and verify evidence integrity. Integration-tested and documented.

---

## Phase 10: User Story 8 — Authorization Decisions & Risk Acceptance (Priority: P1, Spec §3.7–3.13) 🎯 MVP

**Goal**: AOs can issue authorization decisions (ATO/ATOwC/IATT/DATO), accept risk on specific findings, and view risk registers. ISSMs can create formal POA&M items, generate SAR/RAR, and bundle authorization packages.

**Independent Test**: Generate SAR → create 3 POA&M items → AO issues ATOwC with 2 risk acceptances + expiration date → verify system advances to Monitor step → generate authorization package ZIP.

### Entities & DbContext for US8

- [X] T108 [US8] Create `AuthorizationDecision` and `RiskAcceptance` entities in `src/Ato.Copilot.Core/Models/Compliance/AuthorizationModels.cs`
- [X] T109 [US8] Create `PoamItem` and `PoamMilestone` entities in `src/Ato.Copilot.Core/Models/Compliance/AuthorizationModels.cs`
- [X] T110 [US8] Add `AuthorizationDecisions`, `RiskAcceptances`, `PoamItems`, `PoamMilestones` DbSets to `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
- [X] T111 [US8] Add optional `PoamItemId` FK to existing `RemediationTask` entity in `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs`
- [X] T112 [US8] Create EF Core migration for Phase 10 entities

### Services for US8

- [X] T113 [US8] Create `src/Ato.Copilot.Core/Interfaces/Compliance/IAuthorizationService.cs` with methods: `IssueAuthorizationAsync`, `AcceptRiskAsync`, `GetRiskRegisterAsync`, `CreatePoamAsync`, `ListPoamAsync`, `GenerateRarAsync`, `BundlePackageAsync`
- [X] T114 [US8] Implement `AuthorizationService` in `src/Ato.Copilot.Agents/Compliance/Services/AuthorizationService.cs` — authorization decision workflow, risk acceptance with auto-expire, POA&M CRUD, RAR generation, package bundling (SSP + SAR + RAR + POA&M + CRM → ZIP)

### Tools for US8

- [X] T115 [P] [US8] Create `IssueAuthorizationTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AuthorizationTools.cs` implementing `compliance_issue_authorization` per contracts — `AuthorizingOfficial` role only
- [X] T116 [P] [US8] Create `CreatePoamTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AuthorizationTools.cs` implementing `compliance_create_poam`
- [X] T117 [P] [US8] Create `ListPoamTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AuthorizationTools.cs` implementing `compliance_list_poam` with filtering
- [X] T118 [P] [US8] Create `GenerateRarTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AuthorizationTools.cs` — Risk Assessment Report generation
- [X] T119 [US8] Create `BundleAuthorizationPackageTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AuthorizationTools.cs` — ZIP bundle of SSP + SAR + RAR + POA&M + CRM + ATO Letter
- [X] T120 [P] [US8] Create `ShowRiskRegisterTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AuthorizationTools.cs` — active/expired/revoked risk acceptances
- [X] T216 [US8] Create `AcceptRiskTool` in `src/Ato.Copilot.Agents/Compliance/Tools/AuthorizationTools.cs` implementing `compliance_accept_risk` per contracts — AO-only risk acceptance with justification, compensating control, and auto-expiration

### MCP & DI Registration for US8

- [X] T121 [US8] Register 7 authorization tools in `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs` and `AuthorizationService` in DI

### Tests for US8

- [X] T122 [P] [US8] Create unit tests for `IssueAuthorizationTool` (ATO, ATOwC, IATT, DATO, wrong role blocked, expiration required) in `tests/Ato.Copilot.Tests.Unit/Tools/AuthorizationToolTests.cs`
- [X] T123 [P] [US8] Create unit tests for `CreatePoamTool` (valid POA&M, milestones, linked finding, missing required fields) in `tests/Ato.Copilot.Tests.Unit/Tools/AuthorizationToolTests.cs`
- [X] T124 [P] [US8] Create unit tests for risk acceptance (accept, auto-expire, revoke, compensating control) in `tests/Ato.Copilot.Tests.Unit/Tools/AuthorizationToolTests.cs`
- [X] T125 [P] [US8] Create unit tests for RAR generation (risk by family, aggregate risk level) in `tests/Ato.Copilot.Tests.Unit/Tools/AuthorizationToolTests.cs`
- [X] T126 [P] [US8] Create unit tests for package bundling (all documents present, missing SAR error) in `tests/Ato.Copilot.Tests.Unit/Tools/AuthorizationToolTests.cs`

### Integration Test for US8

- [X] T217 [US8] Create integration test in `tests/Ato.Copilot.Tests.Integration/Tools/AuthorizationIntegrationTests.cs` — generate SAR → create POA&M items → AO issues ATOwC → accept risk → verify Monitor step → bundle authorization package via MCP HTTP bridge

### Phase Documentation for US8

- [X] T218 [P] [US8] Write tool reference entries for 7 authorization tools in `docs/architecture/agent-tool-catalog.md`, authorization workflow in `docs/guides/issm-guide.md`, and AO guide in `docs/guides/ao-quick-reference.md`

**Checkpoint**: US8 complete — Full authorization workflow: SAR → RAR → POA&M → authorization decision → risk acceptance → package bundling. Spec Phase 3 (capabilities 3.1–3.13) fully delivered. Integration-tested and documented.

---

## Phase 11: User Story 9 — Continuous Monitoring & Lifecycle (Priority: P2, Spec §4.1–4.7)

**Goal**: ISSMs can create ConMon plans, generate periodic reports, track ATO expiration, detect significant changes, and view multi-system dashboards.

**Independent Test**: Create ConMon plan → generate monthly report → report significant change → verify ATO expiration alert at 90 days → view multi-system dashboard.

### Entities & DbContext for US9

- [X] T127 [US9] Create `ConMonPlan`, `ConMonReport`, `SignificantChange` entities in `src/Ato.Copilot.Core/Models/Compliance/ConMonModels.cs`
- [X] T128 [US9] Add `ConMonPlans`, `ConMonReports`, `SignificantChanges` DbSets to `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
- [X] T129 [US9] Create EF Core migration for Phase 11 entities

### Services for US9

- [X] T130 [US9] Create `src/Ato.Copilot.Core/Interfaces/Compliance/IConMonService.cs` with methods: `CreatePlanAsync`, `GenerateReportAsync`, `ReportChangeAsync`, `CheckExpirationAsync`, `GetDashboardAsync`
- [X] T131 [US9] Implement `ConMonService` in `src/Ato.Copilot.Agents/Compliance/Services/ConMonService.cs` — plan CRUD, report generation from POA&M + assessment data *(Watch data integration deferred to Phase 17 T240–T243)*, expiration checks at 90/60/30 days, significant change recording, multi-system dashboard with RBAC filtering

### Tools for US9

- [X] T132 [P] [US9] Create `CreateConMonPlanTool` in `src/Ato.Copilot.Agents/Compliance/Tools/ConMonTools.cs` implementing `compliance_create_conmon_plan` per contracts
- [X] T133 [P] [US9] Create `GenerateConMonReportTool` in `src/Ato.Copilot.Agents/Compliance/Tools/ConMonTools.cs` implementing `compliance_generate_conmon_report`
- [X] T134 [P] [US9] Create `ReportSignificantChangeTool` in `src/Ato.Copilot.Agents/Compliance/Tools/ConMonTools.cs` implementing `compliance_report_significant_change`
- [X] T135 [P] [US9] Create `TrackAtoExpirationTool` in `src/Ato.Copilot.Agents/Compliance/Tools/ConMonTools.cs` — expiration status with graduated alerts
- [X] T136 [US9] Create `MultiSystemDashboardTool` in `src/Ato.Copilot.Agents/Compliance/Tools/ConMonTools.cs` — all systems with name, IL, RMF step, auth status, expiration, score, alerts
- [X] T219 [US9] Create `ReauthorizationWorkflowTool` in `src/Ato.Copilot.Agents/Compliance/Tools/ConMonTools.cs` — detect reauthorization triggers (significant change, expiration, continuous monitoring failure), clone previous assessment, regress RMF step to Assess per spec §4.5
- [X] T220 [US9] Implement notification delivery stub in `ConMonService` — MCP-response-only notification data per spec §4.7 *(full Teams/VS Code delivery and alert pipeline integration deferred to Phase 17 T244–T246)*

### MCP & DI Registration for US9

- [X] T137 [US9] Register 7 ConMon tools in `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs` and `ConMonService` in DI

### Tests for US9

- [X] T138 [P] [US9] Create unit tests for `CreateConMonPlanTool` (valid plan, duplicate plan, missing system) in `tests/Ato.Copilot.Tests.Unit/Tools/ConMonToolTests.cs`
- [X] T139 [P] [US9] Create unit tests for `GenerateConMonReportTool` (monthly, quarterly, annual, score delta from authorization) in `tests/Ato.Copilot.Tests.Unit/Tools/ConMonToolTests.cs`
- [X] T140 [P] [US9] Create unit tests for `ReportSignificantChangeTool` (change reported, reauthorization flagged) in `tests/Ato.Copilot.Tests.Unit/Tools/ConMonToolTests.cs`
- [X] T141 [P] [US9] Create unit tests for ATO expiration (90/60/30 day alerts, expired, no authorization) in `tests/Ato.Copilot.Tests.Unit/Tools/ConMonToolTests.cs`
- [X] T142 [P] [US9] Create unit tests for multi-system dashboard (multiple systems, RBAC filtering, empty) in `tests/Ato.Copilot.Tests.Unit/Tools/ConMonToolTests.cs`

### Integration Test for US9

- [X] T221 [US9] Create integration test in `tests/Ato.Copilot.Tests.Integration/Tools/ConMonIntegrationTests.cs` — create ConMon plan → generate report → report significant change → verify reauthorization trigger → check expiration alert via MCP HTTP bridge

### Phase Documentation for US9

- [X] T222 [P] [US9] Write tool reference entries for 7 ConMon tools in `docs/architecture/agent-tool-catalog.md`, ConMon workflow in `docs/guides/issm-guide.md`, and update `docs/reference/rmf-process.md`

**Checkpoint**: US9 complete — Continuous monitoring lifecycle: plans, reports, expiration, significant changes, reauthorization triggers, notification delivery, multi-system view. Spec Phase 4 (capabilities 4.1–4.7) fully delivered. Integration-tested and documented.

---

## Phase 12: User Story 10 — eMASS & OSCAL Interoperability (Priority: P2, Spec §5.1–5.2)

**Goal**: ISSMs can export system data in eMASS-compatible Excel format, import eMASS data with conflict resolution, and export OSCAL JSON.

**Independent Test**: Export controls to eMASS Excel → verify column headers match eMASS template → import a modified eMASS file with dry-run → export OSCAL SSP JSON.

### Entities & Packages for US10

- [X] T143 [US10] Create `EmassControlExportRow` and `EmassPoamExportRow` records in `src/Ato.Copilot.Core/Models/Compliance/EmassModels.cs` (25 and 24 fields per research.md R5)
- [X] T144 [US10] Add `ClosedXML` NuGet package to `src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj`

### Services for US10

- [X] T145 [US10] Create `src/Ato.Copilot.Core/Interfaces/Compliance/IEmassExportService.cs` with methods: `ExportControlsAsync`, `ExportPoamAsync`, `ImportAsync`, `ExportOscalAsync`
- [X] T146 [US10] Implement `EmassExportService` in `src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs` — ClosedXML Excel generation with eMASS-matching headers, import with skip/overwrite/merge conflict resolution, dry-run mode, OSCAL JSON SSP/assessment-results/POA&M export

### Tools for US10

- [X] T147 [P] [US10] Create `ExportEmassTool` in `src/Ato.Copilot.Agents/Compliance/Tools/EmassExportTools.cs` implementing `compliance_export_emass` per contracts
- [X] T148 [P] [US10] Create `ImportEmassTool` in `src/Ato.Copilot.Agents/Compliance/Tools/EmassExportTools.cs` implementing `compliance_import_emass`
- [X] T149 [P] [US10] Create `ExportOscalTool` in `src/Ato.Copilot.Agents/Compliance/Tools/EmassExportTools.cs` implementing `compliance_export_oscal`

### MCP & DI Registration for US10

- [X] T150 [US10] Register 3 eMASS/OSCAL tools in `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs` and `EmassExportService` in DI

### Tests for US10

- [X] T151 [P] [US10] Create unit tests for `ExportEmassTool` (controls export, POA&M export, full export, column header validation) in `tests/Ato.Copilot.Tests.Unit/Tools/EmassExportToolTests.cs`
- [X] T152 [P] [US10] Create unit tests for `ImportEmassTool` (skip conflicts, overwrite, merge, dry-run, malformed Excel) in `tests/Ato.Copilot.Tests.Unit/Tools/EmassExportToolTests.cs`
- [X] T153 [P] [US10] Create unit tests for `ExportOscalTool` (SSP model, assessment-results model, POA&M model) in `tests/Ato.Copilot.Tests.Unit/Tools/EmassExportToolTests.cs`

### Integration Test for US10

- [X] T223 [US10] Create integration test in `tests/Ato.Copilot.Tests.Integration/Tools/EmassIntegrationTests.cs` — export controls to Excel → verify eMASS column headers → import with dry-run → export OSCAL JSON via MCP HTTP bridge

### Phase Documentation for US10

- [X] T224 [P] [US10] Write tool reference entries for 3 eMASS/OSCAL tools in `docs/architecture/agent-tool-catalog.md` and eMASS integration section in `docs/guides/issm-guide.md`

**Checkpoint**: US10 complete — eMASS Excel import/export and OSCAL JSON export functional. Integration-tested and documented.

---

## Phase 13: User Story 11 — Document Templates & PDF Export (Priority: P2, Spec §5.1, §5.1a)

**Goal**: Organizations can upload custom DOCX templates. Documents can be exported as PDF. Template management tools available to admins.

**Independent Test**: Upload a custom SSP DOCX template → generate SSP using custom template → generate SSP as PDF with built-in format → verify merge fields populated.

### Packages & Services for US11

- [X] T154 [US11] Add `QuestPDF` NuGet package to `src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj`
- [X] T155 [US11] Create `src/Ato.Copilot.Core/Interfaces/Compliance/IDocumentTemplateService.cs` with methods: `UploadTemplateAsync`, `ListTemplatesAsync`, `ValidateTemplateAsync`, `RenderDocxAsync`, `RenderPdfAsync`
- [X] T156 [US11] Implement `DocumentTemplateService` in `src/Ato.Copilot.Agents/Compliance/Services/DocumentTemplateService.cs` — template storage, merge field validation, QuestPDF rendering for built-in format, DOCX mail-merge for custom templates

### Tools for US11

- [X] T157 [P] [US11] Create `UploadTemplateTool` in `src/Ato.Copilot.Agents/Compliance/Tools/TemplateManagementTools.cs` — upload DOCX with document type declaration + merge field validation
- [X] T158 [P] [US11] Create `ListTemplatesTool` in `src/Ato.Copilot.Agents/Compliance/Tools/TemplateManagementTools.cs`
- [X] T159 [P] [US11] Create `UpdateTemplateTool` and `DeleteTemplateTool` in `src/Ato.Copilot.Agents/Compliance/Tools/TemplateManagementTools.cs`
- [X] T160 [US11] Enhance existing `DocumentGenerationTool` to support format parameter ("markdown" | "docx" | "pdf") and template selection in `src/Ato.Copilot.Agents/Compliance/Tools/DocumentGenerationTool.cs`

### MCP & DI Registration for US11

- [X] T161 [US11] Register 4 template management tools in `src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs` and `DocumentTemplateService` in DI

### Tests for US11

- [X] T162 [P] [US11] Create unit tests for template upload (valid DOCX, invalid file, missing merge fields) in `tests/Ato.Copilot.Tests.Unit/Tools/TemplateManagementToolTests.cs`
- [X] T163 [P] [US11] Create unit tests for PDF generation (built-in format, performance < 15s for 325 controls) in `tests/Ato.Copilot.Tests.Unit/Tools/TemplateManagementToolTests.cs`
- [X] T164 [P] [US11] Create unit tests for DOCX rendering (merge field population, custom template) in `tests/Ato.Copilot.Tests.Unit/Tools/TemplateManagementToolTests.cs`

### Progress Indicator for US11

- [X] T225 [US11] Add streaming progress indicator to PDF generation (15s+ for full SSP) — emit progress events via MCP progress notifications per spec §5.4

### Integration Test for US11

- [X] T226 [US11] Create integration test in `tests/Ato.Copilot.Tests.Unit/Tools/TemplateIntegrationTests.cs` — upload DOCX template → generate SSP with custom template → generate PDF → verify output via MCP HTTP bridge

### Phase Documentation for US11

- [X] T227 [P] [US11] Write tool reference entries for 4 template tools in `docs/architecture/agent-tool-catalog.md` and template management section in `docs/guides/issm-guide.md`

**Checkpoint**: US11 complete — Documents can be exported as PDF/DOCX using built-in or custom templates with progress indicators. Integration-tested and documented.

---

## Phase 14: User Story 12 — Compliance Agent RMF Step Routing (Priority: P2, Spec §Part 6)

**Goal**: Compliance Agent prompt routing is RMF-step-aware — tools are prioritized based on the system's current RMF step.

**Independent Test**: Set system to Step 2 (Select) → ask agent for help → verify baseline/tailoring tools are prioritized over assessment tools.

- [X] T165 [US12] Update `src/Ato.Copilot.Agents/Compliance/Prompts/compliance-agent.prompt.txt` to include RMF step context — tool groups organized by step (Prepare, Categorize, Select, Implement, Assess, Authorize, Monitor) per spec Part 6
- [X] T166 [US12] Update Compliance Agent routing logic in `src/Ato.Copilot.Agents/Compliance/Agents/ComplianceAgent.cs` to prepend current system's RMF step context to tool selection
- [X] T167 [P] [US12] Create unit tests for RMF step routing (correct tools prioritized per step, no system context graceful fallback) in `tests/Ato.Copilot.Tests.Unit/Agents/ComplianceAgentRoutingTests.cs`

**Checkpoint**: US12 complete — Agent intelligently surfaces step-relevant tools.

---

## Phase 15: User Story 13 — Adaptive Cards for RMF Entities (Priority: P3, Spec §Part 6)

**Goal**: Teams and VS Code surfaces show rich Adaptive Cards for RMF entities (system registration, categorization, baseline, authorization decision, ConMon report, multi-system dashboard).

**Independent Test**: Query system status via Teams → see Adaptive Card with system name, IL, RMF step, scores, alerts.

- [X] T168 [P] [US13] Create Adaptive Card template for RegisteredSystem summary in `extensions/m365/src/cards/systemSummaryCard.ts`
- [X] T169 [P] [US13] Create Adaptive Card template for SecurityCategorization (FIPS 199 notation, IL, C/I/A) in `extensions/m365/src/cards/categorizationCard.ts`
- [X] T170 [P] [US13] Create Adaptive Card template for AuthorizationDecision (ATO type, expiration, risk level) in `extensions/m365/src/cards/authorizationCard.ts`
- [X] T171 [P] [US13] Create Adaptive Card template for MultiSystem dashboard in `extensions/m365/src/cards/dashboardCard.ts`
- [X] T172 [P] [US13] Create VS Code webview panel for RMF system overview in `extensions/vscode/src/panels/rmfOverviewPanel.ts`
- [X] T173 [P] [US13] Create M365 extension tests for new Adaptive Cards in `extensions/m365/src/test/cards.test.ts`
- [X] T174 [P] [US13] Create VS Code extension tests for RMF panel in `extensions/vscode/src/test/rmfPanel.test.ts`

**Checkpoint**: US13 complete — Rich UI surfaces for RMF data in Teams and VS Code.

---

## Phase 16: Polish & Cross-Cutting Concerns

**Purpose**: Documentation consolidation (per-phase inline docs already produced alongside each user story), cross-cutting quality gates, CI/CD, and final validation.

### Documentation (Spec Part 8)

- [X] T175 [P] Create `docs/architecture/overview.md` — system architecture, component diagram, data flow, deployment topology
- [X] T176 [P] Create `docs/architecture/data-model.md` — all EF Core entities with ER diagram (Mermaid), generated from model files
- [X] T177 [P] Create `docs/architecture/agent-tool-catalog.md` — every agent and tool with name, description, parameters, RBAC role, RMF step
- [X] T178 [P] Create `docs/architecture/rmf-step-map.md` — matrix of RMF steps × tools × personas × artifacts
- [X] T179 [P] Create `docs/architecture/security.md` — RBAC model (7 roles), CAC/session, PIM, audit logging, data protection
- [X] T180 [P] Create `docs/guides/issm-guide.md` — end-to-end RMF workflow walkthrough for ISSM persona
- [X] T181 [P] Create `docs/guides/sca-guide.md` — independent assessment workflow for SCA persona
- [X] T182 [P] Create `docs/guides/engineer-guide.md` — VS Code `@ato` usage, IaC scanning, remediation for Engineer persona
- [X] T183 [P] Create `docs/guides/ao-quick-reference.md` — one-page AO guide: package review, decision, risk acceptance
- [X] T184 [P] Create `docs/guides/teams-bot-guide.md` — Teams bot installation, commands, Adaptive Cards, notifications
- [X] T185 [P] Update `docs/guides/deployment.md` — add new entities, migrations, new Azure service principal permissions, new NuGet packages
- [X] T186 [P] Create `docs/api/mcp-server.md` — all MCP tools as API reference grouped by RMF step
- [X] T187 [P] Create `docs/api/vscode-extension.md` — chat commands, diagnostics, code actions, panels, configuration
- [X] T188 [P] Create `docs/reference/nist-coverage.md` — control coverage by family (scanned vs. manual attestation)
- [X] T189 [P] Create `docs/reference/stig-coverage.md` — STIG IDs in library, NIST mapping, CAT levels, technology applicability
- [X] T190 [P] Create `docs/reference/impact-levels.md` — IL2–IL6 definitions, Azure requirements, control implications
- [X] T191 [P] Create `docs/reference/rmf-process.md` — 7-step RMF with DoD guidance, artifact checklists, role responsibilities
- [X] T192 [P] Create `docs/reference/glossary.md` — all DoD/RMF/NIST acronyms and terms
- [X] T193 [P] Create `docs/dev/contributing.md` — how to add tools, entities, Adaptive Cards, reference data
- [X] T194 [P] Create `docs/dev/testing.md` — test structure, naming, mock patterns, coverage requirements
- [X] T195 [P] Create `docs/dev/code-style.md` — C# and TypeScript conventions, naming, folder structure rules
- [X] T196 [P] Create `docs/dev/release.md` — versioning, changelog, Docker tagging, extension publishing

### CI/CD & Production (Spec §5.3–§5.6)

- [X] T197 Create `.github/actions/ato-compliance-gate/action.yml` — GitHub Actions composite action for IaC scanning in PRs, blocks on CAT I/II, respects risk acceptances
- [X] T198 Replace hardcoded PIM eligible roles with Microsoft Graph PIM API calls in existing PIM tools (`src/Ato.Copilot.Agents/Compliance/Tools/`)
- [X] T199 Replace `Task.Delay(100ms)` with real subprocess execution (az CLI / PowerShell) in existing remediation tools (`src/Ato.Copilot.Agents/Compliance/Tools/`)
- [ ] T255 [P] [US4] Expand `src/Ato.Copilot.Agents/Compliance/Resources/stig-controls.json` from ~200 priority rules to ~880 rules covering all common DoD technologies per spec §5.6. Add CCI→NIST mapping expansion (~7,575 entries) in `cci-nist-mapping.json`. Update `docs/reference/stig-coverage.md` with new coverage matrix

### Cross-Cutting Quality (Constitution Compliance)

- [X] T228 [P] Verify all new tools emit structured log entries per Constitution V — audit: tool name, user, system_id, timestamp, result status. Create spot-check test in `tests/Ato.Copilot.Tests.Unit/CrossCutting/StructuredLoggingTests.cs`
- [X] T229 [P] Verify all long-running operations (>2s) emit progress events — SSP generation, PDF export, batch populate, authorization package bundling. Create test in `tests/Ato.Copilot.Tests.Unit/CrossCutting/ProgressIndicatorTests.cs`

### Final Validation

- [X] T200 Run full test suite — verify all tests pass with zero warnings, 80%+ coverage
- [X] T201 Run quickstart.md validation — register system, categorize, select baseline, check progress, generate SSP
- [X] T202 [P] Update `CHANGELOG.md` with Feature 015 summary: new capabilities, new entities, new tools, new documentation, breaking changes (AuthorizingOfficial role)
- [X] T203 Verify Docker build succeeds and MCP server starts with all new tools registered

**Checkpoint**: All documentation shipped. CI/CD gate functional. Full test suite passing. Docker build clean.

---

## Phase 17: Monitoring & Alert Pipeline Integration (Spec §9a)

**Purpose**: Bridge subscription-scoped monitoring services (ComplianceWatchService, AlertManager, AlertNotificationService) with system-scoped RMF services (ConMonService). Ensures alerts trigger notifications, ConMon reports include Watch data, and drift auto-creates significant change records.

**⚠️ Post-implementation integration**: These tasks address CRITICAL gaps identified during `/speckit.analyze` — services built in Features 005 and 015 are currently isolated.

**Independent Test**: Create alert via WatchService → verify notification sent → generate ConMon report → verify Watch data included → trigger drift threshold → verify SignificantChange auto-created → verify expiration alert created and notified.

### Entity & DB Changes

- [X] T231 [P] Add nullable `RegisteredSystemId` FK (string, optional) to `ComplianceAlert` entity in `src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs` — backward-compatible: existing alerts remain null
- [X] T232 [P] Add FK relationship configuration for `ComplianceAlert.RegisteredSystemId` → `RegisteredSystem.Id` in `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
- [ ] T233 Create EF Core migration for `ComplianceAlert.RegisteredSystemId` FK addition

### Alert → Notification Pipeline (F3: CRITICAL)

- [X] T234 Inject optional `IAlertNotificationService?` into `AlertManager` constructor in `src/Ato.Copilot.Agents/Compliance/Services/AlertManager.cs` — if non-null, call `SendNotificationAsync()` after successful `CreateAlertAsync()` persistence
- [X] T235 [P] Create unit tests for AlertManager notification integration (notification sent on create, null service gracefully skipped, notification failure doesn't block alert creation) in `tests/Ato.Copilot.Tests.Unit/Services/AlertManagerNotificationTests.cs`
- [X] T236 Update DI registration in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs` — wire `IAlertNotificationService` into `AlertManager` constructor

### System-to-Subscription Bridge (F2: CRITICAL)

- [X] T237 Create `SystemSubscriptionResolver` helper in `src/Ato.Copilot.Agents/Compliance/Services/SystemSubscriptionResolver.cs` — resolves `subscriptionId` → `RegisteredSystemId` by querying `RegisteredSystem.AzureProfile.SubscriptionIds` (reverse lookup with caching)
- [X] T238 Inject `SystemSubscriptionResolver` into `ComplianceWatchService` — populate `ComplianceAlert.RegisteredSystemId` when creating alerts for subscriptions that belong to a registered system
- [X] T239 [P] Create unit tests for `SystemSubscriptionResolver` (valid lookup, no match returns null, cache invalidation, multiple systems sharing subscription → first match) in `tests/Ato.Copilot.Tests.Unit/Services/SystemSubscriptionResolverTests.cs`

### ConMon Report Enrichment (F1: CRITICAL, F5: HIGH)

- [X] T240 Inject `IComplianceWatchService` and `IComplianceMonitoringService` into `ConMonService` via `IServiceScopeFactory` scope resolution in `src/Ato.Copilot.Agents/Compliance/Services/ConMonService.cs`
- [X] T241 Enrich `ConMonService.GenerateReportAsync()` to include Watch data: monitoring enabled status, active drift alert count, auto-remediation rule count, last monitoring check timestamp — resolved via the system's `AzureEnvironmentProfile.SubscriptionIds`
- [X] T242 Add `MonitoringStatus`, `DriftAlertCount`, `AutoRemediationRuleCount`, `LastMonitoringCheck` fields to `ConMonReport` entity in `src/Ato.Copilot.Core/Models/Compliance/ConMonModels.cs`
- [X] T243 [P] Create unit tests for ConMon report enrichment (Watch data included, Watch unavailable gracefully skipped, multi-subscription aggregation) in `tests/Ato.Copilot.Tests.Unit/Services/ConMonReportEnrichmentTests.cs`

### ConMon Event → Alert Pipeline (F4: HIGH)

- [X] T244 Inject `IAlertManager` into `ConMonService` — auto-create `ComplianceAlert` when `CheckExpirationAsync()` detects alert level Warning/Urgent/Expired (graduated severity: Info@90d, Warning@60d, High@30d, Critical@expired)
- [X] T245 Auto-create `ComplianceAlert` (type: SignificantChange, severity: High) when `ReportChangeAsync()` records a change with `RequiresReauthorization = true`
- [X] T246 Replace stub implementation in `NotificationDeliveryTool` (`compliance_send_notification`) in `src/Ato.Copilot.Agents/Compliance/Tools/ConMonTools.cs` — create alert via `IAlertManager` which triggers the AlertManager → AlertNotificationService pipeline
- [X] T247 [P] Create unit tests for ConMon alert creation (expiration alert at each severity level, significant change alert, duplicate alert suppression via AlertCorrelationService) in `tests/Ato.Copilot.Tests.Unit/Services/ConMonAlertPipelineTests.cs`

### Watch Drift → Significant Change Auto-Detection (F7: MEDIUM)

- [X] T248 Add `SignificantDriftThreshold` (int, default: 5) to `MonitoringOptions` in `src/Ato.Copilot.Core/Configuration/GatewayOptions.cs`
- [X] T249 In `ComplianceWatchService.DetectDriftAsync()`, when drifted resource count exceeds threshold for a system with a registered `RegisteredSystemId`, resolve `IConMonService` and call `ReportChangeAsync()` with `changeType = "configuration_drift"`
- [X] T250 [P] Create unit tests for auto-drift significant change (threshold not met → no change reported, threshold exceeded → change created, no registered system → skipped) in `tests/Ato.Copilot.Tests.Unit/Services/DriftSignificantChangeTests.cs`

### Documentation & Service Clarity (F6: MEDIUM)

- [X] T251 [P] Add XML doc comments to `ComplianceMonitoringService` and `ComplianceWatchService` clarifying scope/purpose distinction. Update `docs/architecture/overview.md` with monitoring layer diagram showing data flow: WatchService → AlertManager → AlertNotificationService → ConMonService. Update `docs/architecture/agent-tool-catalog.md` with modified tool/service signatures and `docs/guides/issm-guide.md` with updated ConMon workflow reflecting Watch data enrichment

### Integration Test

- [ ] T252 Create end-to-end integration test in `tests/Ato.Copilot.Tests.Integration/Tools/MonitoringIntegrationTests.cs` — register system with subscriptions → enable Watch monitoring → create drift alert → verify notification sent → verify alert has RegisteredSystemId → generate ConMon report → verify Watch data enrichment → check expiration → verify expiration alert created

### Final Validation

- [X] T253 Run full test suite — verify `dotnet build` passes with zero warnings and all existing 3,143+ tests still pass plus new integration tests, per Constitution Quality Gates
- [X] T254 Update `CHANGELOG.md` with Phase 17 summary: monitoring/alert pipeline integration, new entity fields, new service dependencies

**Checkpoint**: Phase 17 complete — monitoring services fully integrated. Alerts auto-trigger notifications. ConMon reports include Watch data. Drift auto-creates significant changes. Expiration events create graduated alerts. Full pipeline: Watch → AlertManager → AlertNotification → ConMon.

---

## Phase 18: RMF Phase Transition Audit Trail

- [X] T256 [US1] Persist immutable `RmfPhase.Transitioned` audit entries atomically with successful phase changes and test rejected-transition behavior in `tests/Ato.Copilot.Tests.Integration/Tools/RmfRegistrationIntegrationTests.cs`
- [X] T257 [US1] Expose system phase history through the Dashboard API and render it on system detail with HTTP and browser E2E coverage
- [X] T258 [US1] Include RMF phase transition history in authorization package exports and verify package contents

**Checkpoint**: Successful RMF transitions are auditable from the system detail experience and authorization package; rejected transitions do not create success records.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — can start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3 (US1)**: Depends on Phase 2 — System Registration is anchor entity
- **Phase 4 (US2)**: Depends on Phase 3 — needs RegisteredSystem for FK
- **Phase 5 (US3)**: Depends on Phase 4 — needs SecurityCategorization for baseline selection
- **Phase 6 (US4)**: Depends on Phase 5 — extends baseline with STIG mappings
- **Phase 7 (US5)**: Depends on Phase 5 — needs ControlBaseline for narrative tracking
- **Phase 8 (US6)**: Can start after Phase 7 — extends IaC scanner + VS Code extension
- **Phase 9 (US7)**: Depends on Phase 7 — assessment needs SSP narratives
- **Phase 10 (US8)**: Depends on Phase 9 — authorization needs assessment artifacts
- **Phase 11 (US9)**: Depends on Phase 10 — ConMon needs AuthorizationDecision
- **Phase 12 (US10)**: Can start after Phase 10 (needs all entities for export)
- **Phase 13 (US11)**: Can start after Phase 7 (needs DocumentGenerationTool)
- **Phase 14 (US12)**: Can start after Phase 3 (needs registered systems)
- **Phase 15 (US13)**: Can start after Phase 10 (needs all RMF entities for cards)
- **Phase 16 (Polish)**: Depends on all desired phases being complete
- **Phase 17 (Integration)**: Depends on Phase 11 (US9 — ConMon) and Phase 16 (Polish). Requires ComplianceWatchService, AlertManager, AlertNotificationService, and ConMonService all implemented.
- **Phase 18 (RMF Audit Trail)**: Depends on Phase 3 (US1), Phase 10 (US8), and Phase 15 (US13).

### Critical Path

```
Phase 1 → Phase 2 → Phase 3 (US1) → Phase 4 (US2) → Phase 5 (US3) → Phase 7 (US5) → Phase 9 (US7) → Phase 10 (US8) → Phase 11 (US9) → Phase 16 → Phase 17
```

### Parallel Opportunities After Critical Path

Once Phase 5 (US3) completes:
- **Phase 6 (US4)** and **Phase 7 (US5)** can run in parallel
- **Phase 14 (US12)** can start (needs registered systems from Phase 3)

Once Phase 7 (US5) completes:
- **Phase 8 (US6)** and **Phase 9 (US7)** can run in parallel
- **Phase 13 (US11)** can start

Once Phase 10 (US8) completes:
- **Phase 11 (US9)**, **Phase 12 (US10)**, and **Phase 15 (US13)** can run in parallel

### Within Each Phase

- Models/entities before services
- Services before tools
- Tools before MCP registration
- Tests can run in parallel with each other (all marked [P])
- DI registration after tools are created

---

## Parallel Examples

### Phase 1 (Setup) — All Tasks Parallel

```
T002: Add enums to ComplianceModels.cs
T003: Add AuthorizingOfficial role
T004: Create ComplianceFrameworks.cs
T005: Create nist-800-53-baselines.json
T006: Create sp800-60-information-types.json
T007: Create cnssi-1253-overlays.json
T008: Create enum/constant tests
```

### Phase 9 (US7) — Tool Tasks Parallel

```
T098: AssessControlTool
T099: TakeSnapshotTool
T100: CompareSnapshotsTool
T101: VerifyEvidenceTool
T102: CheckEvidenceCompletenessTool
```

### Phase 16 (Polish) — All Docs Parallel

```
T175–T196: All 22 documentation files can be written simultaneously
```

---

## Implementation Strategy

### MVP First (Phases 1–5 + 7 + 9–10)

1. Complete Setup + Foundational → foundation ready
2. Complete US1 (Registration) → systems exist
3. Complete US2 (Categorization) → systems categorized
4. Complete US3 (Baselines) → controls selected
5. Complete US5 (SSP Authoring) → controls documented
6. Complete US7 (Assessment Artifacts) → controls assessed
7. Complete US8 (Authorization) → ATO issued
8. **STOP and VALIDATE**: End-to-end RMF lifecycle works
9. Deploy/demo — this is the MVP

### Incremental Delivery

After MVP:
1. Add US4 (STIG mapping) → richer control context
2. Add US6 (IaC diagnostics) → engineer experience
3. Add US9 (ConMon) → post-ATO lifecycle
4. Add US10/US11 (eMASS/templates) → interoperability
5. Add US12/US13 (routing/cards) → UX polish
6. Add Polish → docs, CI/CD, tests

### Task Count Summary

| Phase | Story | Tasks | Parallel |
|-------|-------|-------|----------|
| 1 | Setup | 8 | 7 |
| 2 | Foundational | 9 | 5 |
| 3 | US1 — Registration | 20 | 13 |
| 4 | US2 — Categorization | 11 | 6 |
| 5 | US3 — Baselines | 15 | 9 |
| 6 | US4 — STIG Mapping | 7 | 3 |
| 7 | US5 — SSP Authoring | 19 | 11 |
| 8 | US6 — IaC Diagnostics | 8 | 6 |
| 9 | US7 — Assessment | 22 | 11 |
| 10 | US8 — Authorization | 22 | 11 |
| 11 | US9 — ConMon | 20 | 11 |
| 12 | US10 — eMASS/OSCAL | 13 | 7 |
| 13 | US11 — Templates/PDF | 14 | 7 |
| 14 | US12 — Step Routing | 3 | 1 |
| 15 | US13 — Adaptive Cards | 7 | 7 |
| 16 | Polish | 32 | 28 |
| 17 | Monitoring Integration | 24 | 10 |
| **Total** | | **254** | **153 (60%)** |

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks in the same phase
- [US#] label maps task to spec capability numbers for traceability
- Each user story checkpoint is independently testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- 60% of tasks are parallelizable — significant opportunity for multi-developer execution
