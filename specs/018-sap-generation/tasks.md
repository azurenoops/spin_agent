# Tasks: Feature 018 — SAP Generation

**Input**: Design documents from `/specs/018-sap-generation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/sap-tools.md, quickstart.md

**Tests**: Included — plan.md commits to ~135+ tests (Constitution Check III).

**Organization**: Tasks grouped by user story (mapped from spec.md Part 5 capability phases).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)
- All file paths are relative to repository root

---

## Phase 1: Setup (Shared Infrastructure)

### US5 - Connected guided assessment workflow (2026-09-28)

Parent: [azurenoops/spin_agent#211](https://github.com/azurenoops/spin_agent/issues/211).
External issue write/link approval remains a separate gate.

- [ ] GUIDE01 Approve/create/link the user-story issue and mirror this checklist.
- [x] GUIDE02 Add failing plan/preview/finalization/concurrency/scope tests.
- [x] GUIDE03 Implement one retained-plan projection and focused draft editors using existing SAP records/services and actual validation rules.
- [x] GUIDE04 Add failing collection/import/result lineage, coverage, review, retry and SAR-source tests.
- [x] GUIDE05 Preserve supported Azure/import methods with real permissions/readiness, scope and partial-failure handling; no automatic baseline-wide passes.
- [x] GUIDE06 Pin original plan/scope and explicit review/reconciliation provenance on existing result records; preserve historical associations.
- [x] GUIDE07 Prepare retained SAR drafts from selected sources through the existing report service with idempotency and authorization.
- [x] GUIDE08 Add failing UI tests and implement mock-aligned Plan/Results pages, workflow links, editors, result details, review and SAR readiness.
- [x] GUIDE09 Preserve findings/remediation/POA&M and supported import/procedure/team/schedule actions; remove duplicate SAP generation controls.
- [x] GUIDE10 Verify rendered SAP fields and SAR selected-result/evidence/review provenance, and document remaining export connections.
- [x] GUIDE11 Run Dashboard typecheck/build, relevant backend/frontend tests, and desktop/mobile/theme/keyboard browser checks.
- [x] GUIDE12 Deploy locally for manual acceptance, report verified results and baseline failures; no push.

Final local results: 64 backend tests passed (20 existing Nessus placeholders
skipped), 100 frontend tests passed, 5 browser cases passed, Dashboard static/build
checks and API image build passed. Four unrelated PendingImpacts constructor
errors still block the whole solution's unit project. Actual SQL Server additive
columns and local API health are verified. Organization-account manual acceptance
and GUIDE01 GitHub approval remain external gates; no grants or pushes were made.

**Purpose**: Create SAP entities, enum, DTOs, and database configuration required by all user stories

- [X] T001 Create SecurityAssessmentPlan, SapControlEntry, SapTeamMember entities, SapStatus enum, and all DTOs (SapMethodOverrideInput, SapTeamMemberInput, SapGenerationInput, SapUpdateInput, SapDocument, SapFamilySummary, SapValidationResult) in src/Ato.Copilot.Core/Models/Compliance/SapModels.cs
- [X] T002 Register SecurityAssessmentPlans, SapControlEntries, SapTeamMembers DbSets in src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs
- [X] T003 Add OnModelCreating configuration for SecurityAssessmentPlan (composite index on SystemId+Status, Status string conversion), SapControlEntry (JSON conversions for list properties, unique index on PlanId+ControlId), SapTeamMember (cascade delete) in src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs

**Checkpoint**: Database schema ready — entities can be persisted and queried

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Create service interface, implementation skeleton, and DI registration that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 Create ISapService interface with GenerateSapAsync, UpdateSapAsync, FinalizeSapAsync, GetSapAsync, ListSapsAsync, ValidateSapAsync method signatures in src/Ato.Copilot.Core/Interfaces/Compliance/ISapService.cs
- [X] T005 Create SapService class skeleton implementing ISapService with IServiceScopeFactory + ILogger constructor injection and NotImplementedException stubs in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T006 Register ISapService/SapService singleton in src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs (near existing SSP/SAR block, lines ~257–271)

**Checkpoint**: Foundation ready — ISapService injectable, entities persist, user story implementation can begin

---

## Phase 3: User Story 1 — SAP Core Generation (Priority: P1) 🎯 MVP

**Goal**: SCA generates a complete SAP document from baseline, OSCAL objectives, STIG mappings, and evidence data via `compliance_generate_sap`

**Independent Test**: Call `compliance_generate_sap` with a system that has a Moderate baseline → returns Markdown SAP with 15 sections, ~325 control entries, assessment objectives, all three default methods, STIG benchmark list, and evidence gap summaries

**Capabilities**: 1.1 (Objective Extraction), 1.2 (Method Resolution), 1.3 (Scope Assembly), 1.4 (STIG Test Plan), 1.5 (Evidence Mapping), 1.6 (Entity), 1.7 (Content Generator), 1.8 (Generate Tool)

### Tests for User Story 1

> **Write these tests FIRST — ensure they FAIL before implementation**

- [X] T007 [P] [US1] Entity construction and default value tests for SecurityAssessmentPlan, SapControlEntry, SapTeamMember, SapStatus enum, and all DTOs in tests/Ato.Copilot.Tests.Unit/Models/SapModelTests.cs
- [X] T008 [P] [US1] GenerateSapAsync unit tests — success with objectives/methods/STIGs, missing baseline (hard error), system not found, empty baseline, method overrides applied, draft overwrite, phase/role warnings passed through in response envelope, evidence gap population, CancellationToken cancellation in tests/Ato.Copilot.Tests.Unit/Services/SapServiceTests.cs

### Implementation for User Story 1

- [X] T009 [US1] Implement prerequisite validation — require ControlBaseline (BASELINE_NOT_FOUND error), warn if not RmfPhase.Assess, warn if no SCA RmfRoleAssignment in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T010 [US1] Implement control scope assembly — load ControlBaseline.ControlIds, build Dictionary<string, ControlInheritance> via .Include(), annotate each control with inheritance type (Customer/Inherited/Shared) and tailoring status in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T011 [US1] Implement assessment objective extraction — call NistControlsService.GetControlEnhancementAsync per baseline control, collect objectives list, track controls with/without objectives in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T012 [US1] Implement assessment method resolution — default all controls to [Examine, Interview, Test], apply SCA method_overrides from SapGenerationInput, validate method values, persist as SapControlEntry.AssessmentMethods in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T013 [US1] Implement STIG/SCAP test plan builder — call IStigKnowledgeService.GetStigsByCciChainAsync per control, group by BenchmarkId, count rules per benchmark, include scan import history via IScanImportService in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T014 [US1] Implement evidence requirements mapping — generate static prose per method type (Examine→documents, Interview→personnel, Test→scans), query ComplianceEvidence per control for gap summary (EvidenceExpected/EvidenceCollected counts) in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T015 [US1] Implement SAP Markdown content generator — assemble all 15 sections (Introduction, System Description, Assessment Scope, Objectives, Methods, Procedures, Excluded Controls, STIG Plan, Team, Schedule, ROE, Evidence Requirements, Risk Approach, Appendix A Control Matrix, Appendix B STIG List) using StringBuilder in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T016 [US1] Implement GenerateSapAsync orchestrator — validate prerequisites, delete existing Draft (overwrite), create SecurityAssessmentPlan entity, build SapControlEntry per control via T009–T014 pipeline, render content via T015, persist with SaveChangesAsync, return SapDocument DTO in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T017 [US1] Create GenerateSapTool extending BaseTool — Name="compliance_generate_sap", Parameters per contracts/sap-tools.md, RBAC=[Analyst, SecurityLead, Administrator], ExecuteCoreAsync calls ISapService.GenerateSapAsync, returns { status, data, metadata } envelope in src/Ato.Copilot.Agents/Compliance/Tools/SapTools.cs
- [X] T018 [US1] Register GenerateSapTool two-step singleton (AddSingleton<GenerateSapTool> + AddSingleton<BaseTool>(sp => sp.GetRequiredService<GenerateSapTool>())) in src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs
- [X] T019 [P] [US1] Add compliance_generate_sap wrapper method with [Description] attribute, typed parameters (string system_id, string? assessment_id, string? schedule_start, string? schedule_end, string? team_members, string? scope_notes, string? method_overrides, string? rules_of_engagement, string? format), delegate to _generateSapTool.ExecuteAsync in src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs
- [X] T020 [P] [US1] GenerateSapTool execution tests — success response envelope, RBAC rejection for all disallowed roles (Viewer, Auditor, ISSO, PlatformEngineer, AuthorizingOfficial), BASELINE_NOT_FOUND error code, SYSTEM_NOT_FOUND error code, INVALID_METHOD error code, INVALID_FORMAT error code in tests/Ato.Copilot.Tests.Unit/Tools/SapToolTests.cs

**Checkpoint**: `compliance_generate_sap` produces a complete Markdown SAP from baseline data — MVP functional

---

## Phase 4: User Story 2 — SAP Customization & Draft Management (Priority: P2)

**Goal**: SCA refines and finalizes a Draft SAP — update schedule/team/ROE/method overrides, then lock as immutable Finalized with integrity hash

**Independent Test**: Generate a Draft SAP → call `compliance_update_sap` to change schedule and override AC-2 methods → call `compliance_finalize_sap` → verify SAP is immutable with SHA-256 hash → verify update on Finalized SAP returns SAP_FINALIZED error

**Capabilities**: 2.1 (Update Tool), 2.2 (Method Override), 2.3 (Team Mgmt), 2.4 (ROE), 2.5 (Finalization)

### Tests for User Story 2

> **Write these tests FIRST — ensure they FAIL before implementation**

- [X] T021 [P] [US2] UpdateSapAsync unit tests — update schedule dates, update team members (add/replace), update ROE text, apply method overrides on Draft, reject update on Finalized (SAP_FINALIZED), re-render content after update, SAP_NOT_FOUND for invalid ID in tests/Ato.Copilot.Tests.Unit/Services/SapServiceTests.cs
- [X] T022 [P] [US2] FinalizeSapAsync unit tests — success sets status/hash/finalizer/timestamp, SHA-256 matches Content, reject on already Finalized, reject on non-existent SAP, immutability after finalization in tests/Ato.Copilot.Tests.Unit/Services/SapServiceTests.cs

### Implementation for User Story 2

- [X] T023 [US2] Implement UpdateSapAsync — load Draft SAP by ID, apply schedule/scopeNotes/rulesOfEngagement updates, re-render Markdown content, return updated SapDocument in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T024 [US2] Implement per-control method override persistence in UpdateSapAsync — validate each method is Examine/Interview/Test (INVALID_METHOD), find or create SapControlEntry, update AssessmentMethods list in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T025 [US2] Implement assessment team management in UpdateSapAsync — clear existing SapTeamMember list, add new entries from SapTeamMemberInput list, validate name/organization/role required in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T026 [US2] Implement FinalizeSapAsync — load Draft SAP, reject if already Finalized (SAP_FINALIZED), compute SHA-256 of Content, set Status=Finalized/FinalizedBy/FinalizedAt/ContentHash, SaveChangesAsync in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T027 [US2] Create UpdateSapTool extending BaseTool — Name="compliance_update_sap", Parameters per contracts/sap-tools.md, RBAC=[Analyst, SecurityLead, Administrator] in src/Ato.Copilot.Agents/Compliance/Tools/SapTools.cs
- [X] T028 [US2] Create FinalizeSapTool extending BaseTool — Name="compliance_finalize_sap", Parameters per contracts/sap-tools.md, RBAC=[Analyst, SecurityLead, Administrator] in src/Ato.Copilot.Agents/Compliance/Tools/SapTools.cs
- [X] T029 [US2] Register UpdateSapTool and FinalizeSapTool two-step singletons in src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs
- [X] T030 [US2] Add compliance_update_sap wrapper method with [Description] attribute in src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs
- [X] T031 [US2] Add compliance_finalize_sap wrapper method with [Description] attribute in src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs
- [X] T032 [P] [US2] UpdateSapTool and FinalizeSapTool execution tests — success envelopes, RBAC rejection, SAP_FINALIZED on update/re-finalize, SAP_NOT_FOUND in tests/Ato.Copilot.Tests.Unit/Tools/SapToolTests.cs

**Checkpoint**: Draft SAP lifecycle complete — generate → update → finalize → immutable

---

## Phase 5: User Story 3 — SAP Retrieval & Export (Priority: P3)

**Goal**: SCA/ISSM retrieves, lists, and exports SAPs in Markdown/DOCX/PDF formats

**Independent Test**: Generate and finalize a SAP → call `compliance_get_sap` by ID → call `compliance_get_sap` by system_id (returns latest Finalized) → call `compliance_list_saps` → re-generate SAP with format=docx → verify DOCX output → re-generate with format=pdf → verify PDF output

**Capabilities**: 3.1 (Get SAP), 3.2 (List SAPs), 3.3 (DOCX Export), 3.4 (PDF Export)

### Tests for User Story 3

> **Write these tests FIRST — ensure they FAIL before implementation**

- [X] T033 [P] [US3] GetSapAsync unit tests — retrieve by sap_id, retrieve by system_id (prefers Finalized over Draft), SAP_NOT_FOUND for invalid ID, SAP_NOT_FOUND for system with no SAPs in tests/Ato.Copilot.Tests.Unit/Services/SapServiceTests.cs
- [X] T034 [P] [US3] ListSapsAsync unit tests — returns Draft + Finalized history, orders by GeneratedAt descending, empty list for system with no SAPs, includes scope summary per SAP in tests/Ato.Copilot.Tests.Unit/Services/SapServiceTests.cs
- [X] T035 [P] [US3] DOCX and PDF export tests — format=docx produces byte array, format=pdf produces byte array, format=markdown returns string content, INVALID_FORMAT for unsupported format in tests/Ato.Copilot.Tests.Unit/Services/SapServiceTests.cs

### Implementation for User Story 3

- [X] T036 [US3] Implement GetSapAsync — sap_id takes precedence over system_id, system_id returns latest SAP (prefer Finalized, fallback to Draft), include ControlEntries and TeamMembers via .Include(), return SapDocument DTO in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T037 [US3] Implement ListSapsAsync — query all SAPs for system_id ordered by GeneratedAt descending, project to summary DTOs with status/dates/scope counts, no content field in list results in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T038 [P] [US3] Add "sap" merge-field schema to DocumentTemplateService.MergeFieldSchemas dictionary and implement PopulateSapData method in BuildMergeDataAsync switch with fields: SystemName, SystemAcronym, BaselineLevel, TotalControls, CustomerControls, InheritedControls, SharedControls, ControlMatrix, StigBenchmarks, AssessmentTeam, ScheduleStart, ScheduleEnd, RulesOfEngagement, PreparedBy, PreparedDate in src/Ato.Copilot.Agents/Compliance/Services/DocumentTemplateService.cs
- [X] T039 [US3] Implement SAP PDF rendering via QuestPDF following existing document rendering patterns (section headers, control tables, team roster, STIG benchmark table) in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T04 [US3] Integrate format parameter dispatch in GenerateSapAsync — format=markdown returns Content string, format=docx calls DocumentTemplateService.RenderDocxAsync, format=pdf calls QuestPDF renderer in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T04 [US3] Create GetSapTool extending BaseTool — Name="compliance_get_sap", Parameters per contracts/sap-tools.md, RBAC=[all except Viewer] in src/Ato.Copilot.Agents/Compliance/Tools/SapTools.cs
- [X] T04 [US3] Create ListSapsTool extending BaseTool — Name="compliance_list_saps", Parameters per contracts/sap-tools.md, RBAC=[all except Viewer] in src/Ato.Copilot.Agents/Compliance/Tools/SapTools.cs
- [X] T04 [US3] Register GetSapTool and ListSapsTool two-step singletons in src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs
- [X] T04 [US3] Add compliance_get_sap wrapper method with [Description] attribute in src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs
- [X] T04 [US3] Add compliance_list_saps wrapper method with [Description] attribute in src/Ato.Copilot.Mcp/Tools/ComplianceMcpTools.cs
- [X] T04 [P] [US3] GetSapTool and ListSapsTool execution tests — success envelopes, RBAC (accessible to Analyst/SecurityLead/Administrator/Auditor/ISSO/PlatformEngineer/AuthorizingOfficial, rejected for Viewer), SAP_NOT_FOUND, SYSTEM_NOT_FOUND in tests/Ato.Copilot.Tests.Unit/Tools/SapToolTests.cs

**Checkpoint**: All 5 MCP tools operational — SAPs can be generated, updated, finalized, retrieved, listed, and exported in 3 formats

---

## Phase 6: User Story 4 — Integration & Observability (Priority: P4)

**Goal**: Cross-cutting concerns — SAP completeness validation, SAP-to-SAR alignment, structured logging, RMF lifecycle integration

**Independent Test**: Generate SAP with missing team/schedule → call ValidateSapAsync → verify warnings returned. Generate SAR for system with finalized SAP → verify SAR includes "SAP Alignment" section.

**Capabilities**: 4.1 (RMF Lifecycle), 4.2 (Completeness Validation), 4.3 (Structured Logging), 4.4 (Dashboard Summary), 3.5 (SAP-SAR Alignment)

### Tests for User Story 4

> **Write these tests FIRST — ensure they FAIL before implementation**

- [X] T047 [P] [US4] ValidateSapAsync unit tests — complete SAP passes, missing team warns, missing schedule warns, controls missing methods warns, controls missing objectives warns, returns SapValidationResult counts in tests/Ato.Copilot.Tests.Unit/Services/SapServiceTests.cs

### Implementation for User Story 4

- [X] T048 [US4] Implement ValidateSapAsync — check all baseline controls have SapControlEntry, at least one method per control, team has at least one assessor, schedule has start+end dates, return SapValidationResult with IsComplete flag and Warnings list in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T049 [US4] Implement SAP-to-SAR alignment — add method to cross-reference finalized SAP controls with ComplianceAssessment results, identify planned-but-unassessed and assessed-but-unplanned controls, expose for SAR generation integration in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T050 [US4] Add structured Serilog logging across all SapService methods — log SAP generated (system_id, sap_id, control_count, duration_ms), SAP updated (sap_id, updated_fields), SAP finalized (sap_id, content_hash, finalized_by), validation run (sap_id, is_complete, warning_count) in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T051 [US4] Expose SAP status summary for RMF lifecycle queries — add GetSapStatusAsync returning latest SAP state (None/Draft/Finalized), scope coverage percentage, assessment readiness indicator for system dashboard in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs

**Checkpoint**: SAP fully integrated with observability, validation, and SAR alignment

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Performance validation, envelope conformance, documentation, code quality

- [X] T052 [P] SAP performance benchmark tests — GenerateSapAsync with Moderate baseline (~325 controls) completes in <15s, High baseline (~421 controls) in <30s in tests/Ato.Copilot.Tests.Unit/Performance/SapPerformanceTests.cs
- [X] T053 [P] Verify all 5 SAP tool responses conform to { status, data, metadata } envelope schema with correct error codes per contracts/sap-tools.md
- [X] T054 [P] XML documentation pass — verify all public types/methods in ISapService, SapService, SapTools, SapModels have XML doc comments
- [X] T055 [P] CancellationToken propagation pass — verify all async methods accept and forward CancellationToken to EF Core/service calls in src/Ato.Copilot.Agents/Compliance/Services/SapService.cs
- [X] T056 Run quickstart.md smoke test validation — build solution, run all tests, generate SAP for test system

**Checkpoint**: Feature 018 complete — performance validated, documentation complete, all tests passing

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — BLOCKS US2 (update/finalize needs GenerateSapAsync)
- **US2 (Phase 4)**: Depends on US1 — can start after US1 checkpoint
- **US3 (Phase 5)**: Depends on US1 — can run in parallel with US2 (GetSap/ListSaps only need persisted entities)
- **US4 (Phase 6)**: Depends on US1 — can run in parallel with US2/US3 (validation/logging are additive)
- **Polish (Phase 7)**: Depends on US1–US4 completion

### User Story Dependencies

- **US1 (P1)**: Foundation only — no cross-story dependencies. MVP.
- **US2 (P2)**: Depends on US1 (needs SAP entity + GenerateSapAsync for Draft to exist). Cannot parallelize with US1.
- **US3 (P3)**: Depends on US1 (needs persisted SAP entities). Can parallelize with US2 after US1 completes.
- **US4 (P4)**: Depends on US1 (needs SapService methods). Can parallelize with US2/US3 after US1 completes.

### Within Each User Story

1. Tests written FIRST — must FAIL before implementation
2. Service methods before tools (SapService → SapTools)
3. Tools before DI registration (SapTools → ServiceCollectionExtensions)
4. DI registration before MCP wrapper (ServiceCollectionExtensions → ComplianceMcpTools)
5. Tool execution tests after tool creation

### Parallel Opportunities

- **Phase 1**: T002 and T003 are sequential (same file) — no parallelism
- **Phase 2**: T004 → T005 → T006 sequential (dependency chain)
- **Phase 3 (US1)**: T007 ∥ T008 (test files); T019 ∥ T020 (different files, after T018)
- **Phase 4 (US2)**: T021 ∥ T022 (test files); T032 standalone after tools exist
- **Phase 5 (US3)**: T033 ∥ T034 ∥ T035 (test files); T038 ∥ T036 (different files); T046 standalone
- **Phase 6 (US4)**: T047 standalone
- **Phase 7**: T052 ∥ T053 ∥ T054 ∥ T055 (all independent)
- **Cross-story**: After US1 completes, US2 + US3 + US4 can proceed in parallel (if staffed)

---

## Parallel Example: User Story 1

```
# Launch tests for US1 together (they will FAIL):
Task T007: Entity construction tests in tests/.../SapModelTests.cs
Task T008: GenerateSapAsync tests in tests/.../SapServiceTests.cs

# Implementation sequence (SapService.cs — same file, sequential):
Task T009: Prerequisite validation
Task T010: Control scope assembly
Task T011: Assessment objective extraction
Task T012: Assessment method resolution
Task T013: STIG/SCAP test plan builder
Task T014: Evidence requirements mapping
Task T015: Markdown content generator
Task T016: GenerateSapAsync orchestrator

# Tool + registration (different files, partially parallel):
Task T017: GenerateSapTool in SapTools.cs
Task T018: DI registration in ServiceCollectionExtensions.cs
Task T019 ∥ T020: MCP wrapper (ComplianceMcpTools.cs) ∥ Tool tests (SapToolTests.cs)
```

---

## Parallel Example: After US1 Completes

```
# US2, US3, US4 can start in parallel (different service methods, tools):

Developer A — US2 (Customization):
  T021 ∥ T022: Tests → T023–T026: Service → T027–T028: Tools → T029–T032: Registration

Developer B — US3 (Retrieval & Export):
  T033 ∥ T034 ∥ T035: Tests → T036–T037: Service → T038: DOCX → T039–T040: PDF/Format
  → T041–T042: Tools → T043–T046: Registration

Developer C — US4 (Integration):
  T047: Tests → T048–T051: Service methods
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T003)
2. Complete Phase 2: Foundational (T004–T006)
3. Complete Phase 3: User Story 1 (T007–T020)
4. **STOP and VALIDATE**: `compliance_generate_sap` produces correct Markdown SAP
5. Deploy/demo if ready — SCA can generate SAPs immediately

### Incremental Delivery

1. Setup + Foundational → Entities and service skeleton ready
2. US1 (Core Generation) → SCA generates SAPs → **MVP!**
3. US2 (Customization) → SCA refines and finalizes SAPs
4. US3 (Retrieval & Export) → ISSM reviews, DOCX/PDF export
5. US4 (Integration) → SAP-SAR alignment, observability
6. Each story adds value without breaking previous stories

### Key Design Decisions (from research.md)

| Decision | Reference | Impact |
|----------|-----------|--------|
| R1: Follow SspService pattern | SspService.cs (367–585) | SapService constructor, scope factory, StringBuilder |
| R2: SapDocument DTO pattern | SarDocument in AssessmentArtifactService | Return type for all SAP operations |
| R3: GetControlEnhancementAsync for objectives | NistControlsService.cs (203–250) | T011 implementation — no new OSCAL method needed |
| R4: Include + Dictionary for inheritance | SspService.cs (528) | T010 implementation — O(1) per-control lookup |
| R5: GetStigsByCciChainAsync for STIG mapping | StigKnowledgeService.cs (44–195) | T013 implementation — richest reverse mapping |
| R6: Evidence gap via COUNT query | AssessmentArtifactService.cs (370–457) | T014 implementation — EvidenceExpected/Collected |
| R7: "sap" merge-field in DocumentTemplateService | DocumentTemplateService.cs (273–350) | T038 implementation — DOCX export |
| R8: Two-step singleton DI | ServiceCollectionExtensions.cs (395–406) | T018, T029, T043 registrations |
| R9: ComplianceMcpTools wrapper pattern | ComplianceMcpTools.cs (1902–1914) | T019, T030–T031, T044–T045 wrappers |
| R10: Greenfield — no existing SAP code | Confirmed via codebase search | Clean implementation, no refactoring needed |

---

## Notes

- [P] tasks = different files, no dependencies on in-progress tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable after its checkpoint
- Test tasks are TDD-style: write first, ensure they FAIL, then implement
- Commit after each task or logical group
- All async methods must accept and propagate CancellationToken
- All tool responses must follow { status, data, metadata } envelope schema
- Error codes must match contracts/sap-tools.md (8 error codes defined)
- Total: 56 tasks across 7 phases
