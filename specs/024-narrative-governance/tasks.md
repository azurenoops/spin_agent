# Tasks: Narrative Governance — Version Control + Approval Workflow

**Input**: Design documents from `/specs/024-narrative-governance/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Add new NuGet dependency and create new entity model file

- [X] T001 Add DiffPlex NuGet package to `src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj`
- [X] T002 [P] Create `NarrativeVersion`, `NarrativeReview` entities and `ReviewDecision` enum in `src/Ato.Copilot.Core/Models/Compliance/NarrativeGovernanceModels.cs` per data-model.md

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Database schema, service interface, and DI wiring that ALL user stories depend on

**CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 [P] Add `ApprovalStatus` (`SspSectionStatus`, default `Draft`), `CurrentVersion` (`int`, default 1), and `ApprovedVersionId` (`Guid?`) fields to `ControlImplementation` in `src/Ato.Copilot.Core/Models/Compliance/SspModels.cs`
- [X] T004 Add `DbSet<NarrativeVersion>`, `DbSet<NarrativeReview>` and entity configurations (indexes, FKs, conversions) to `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` per data-model.md EF Core Configuration section
- [X] T005 Create and apply EF Core migration `NarrativeGovernance` with data seeding to bootstrap `NarrativeVersion` v1 for existing `ControlImplementation` rows with non-null narratives
- [X] T006 [P] Create `INarrativeGovernanceService` interface with all method signatures in `src/Ato.Copilot.Core/Interfaces/Compliance/INarrativeGovernanceService.cs` per contracts/tool-contracts.md
- [X] T007 Create `NarrativeGovernanceService` skeleton class implementing `INarrativeGovernanceService` in `src/Ato.Copilot.Agents/Compliance/Services/NarrativeGovernanceService.cs` and register as scoped service in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs`

**Checkpoint**: Foundation ready — database schema, entities, service interface, and DI wiring in place

---

## Phase 3: User Story 1 — Narrative Version History (Priority: P1) MVP

**Goal**: Every narrative edit creates an immutable version record; users can view history, diff versions, and rollback — delivering audit trail and rollback safety net without approval workflows.

**Independent Test**: Write a narrative, update it 3 times, verify all versions retrievable newest-first with author/timestamp; diff versions 1 vs 3; rollback to version 1 creating version 4.

### Implementation for User Story 1

- [X] T008 [US1] Update `WriteNarrativeAsync` signature in `src/Ato.Copilot.Core/Interfaces/Compliance/ISspService.cs` and enhance implementation in `src/Ato.Copilot.Agents/Compliance/Services/SspService.cs` to create a `NarrativeVersion` record on every write, increment `ControlImplementation.CurrentVersion`, and keep `Narrative` field in sync with latest version content (FR-001, FR-006)
- [X] T009 [US1] Add optional `expected_version` (`int?`) and `change_reason` (`string?`) parameters to `WriteNarrativeTool` in `src/Ato.Copilot.Agents/Compliance/Tools/SspAuthoringTools.cs` and pass through to `WriteNarrativeAsync`; include `version_number`, `approval_status`, `previous_version` in tool response (FR-006, FR-032 contract)
- [X] T010 [US1] Implement `GetNarrativeHistoryAsync` in `src/Ato.Copilot.Agents/Compliance/Services/NarrativeGovernanceService.cs` — query `NarrativeVersion` by (systemId, controlId) ordered newest-first with pagination (FR-002)
- [X] T011 [P] [US1] Implement `GetNarrativeDiffAsync` in `src/Ato.Copilot.Agents/Compliance/Services/NarrativeGovernanceService.cs` — load two `NarrativeVersion` records by version numbers and produce line-level unified diff using DiffPlex `Differ` class (FR-003)
- [X] T012 [US1] Implement `RollbackNarrativeAsync` in `src/Ato.Copilot.Agents/Compliance/Services/NarrativeGovernanceService.cs` — copy content from target version into a new `NarrativeVersion` record with incremented version number and `Draft` status (FR-004, FR-005)
- [X] T013 [P] [US1] Create `NarrativeHistoryTool` class extending `BaseTool` in `src/Ato.Copilot.Agents/Compliance/Tools/NarrativeGovernanceTools.cs` with `system_id`, `control_id`, `page`, `page_size` parameters per contracts/tool-contracts.md
- [X] T014 [P] [US1] Create `NarrativeDiffTool` class extending `BaseTool` in `src/Ato.Copilot.Agents/Compliance/Tools/NarrativeGovernanceTools.cs` with `system_id`, `control_id`, `from_version`, `to_version` parameters per contracts/tool-contracts.md
- [X] T015 [P] [US1] Create `RollbackNarrativeTool` class extending `BaseTool` in `src/Ato.Copilot.Agents/Compliance/Tools/NarrativeGovernanceTools.cs` with `system_id`, `control_id`, `target_version`, `change_reason` parameters per contracts/tool-contracts.md
- [X] T016 [US1] Register `NarrativeHistoryTool`, `NarrativeDiffTool`, `RollbackNarrativeTool` as `AddSingleton` + `BaseTool` forwarding in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs` and add `RegisterTool()` calls in `src/Ato.Copilot.Agents/Compliance/Agents/ComplianceAgent.cs`
- [X] T040 [US1] Create unit tests in `tests/Ato.Copilot.Tests.Unit/Compliance/NarrativeGovernanceServiceTests.cs` — test `WriteNarrativeAsync` version creation (positive: creates version, increments CurrentVersion; negative: null content, missing control), `GetNarrativeHistoryAsync` (pagination, empty history, newest-first ordering), `GetNarrativeDiffAsync` (valid diff, non-existent version), `RollbackNarrativeAsync` (creates new Draft version with prior content, non-existent target version) using xUnit, FluentAssertions, Moq
- [X] T040 [US1] Create integration tests in `tests/Ato.Copilot.Tests.Integration/Compliance/NarrativeGovernanceToolTests.cs` — test `NarrativeHistoryTool`, `NarrativeDiffTool`, `RollbackNarrativeTool` happy paths and error paths (SYSTEM_NOT_FOUND, CONTROL_NOT_FOUND, VERSION_NOT_FOUND) via WebApplicationFactory

**Checkpoint**: Version history fully functional — every write creates a version, history/diff/rollback tools operational. MVP deliverable.

---

## Phase 4: User Story 2 — Narrative Approval Workflow (Priority: P2)

**Goal**: ISSOs submit narratives for ISSM review; ISSMs approve or request revision; status lifecycle (Draft → InReview → Approved/NeedsRevision) enforced; batch review supported.

**Independent Test**: ISSO submits a Draft narrative → status becomes InReview → ISSM approves → status becomes Approved; ISSO writes new update → new Draft version created, Approved version unaffected; ISSM batch-approves an AC family.

### Implementation for User Story 2

- [X] T017 [US2] Implement `SubmitNarrativeAsync` in `src/Ato.Copilot.Agents/Compliance/Services/NarrativeGovernanceService.cs` — transition latest `NarrativeVersion` from Draft to InReview, validate status precondition, populate `SubmittedBy` and `SubmittedAt` fields on the `NarrativeVersion` record (FR-008, FR-022)
- [X] T018 [US2] Implement `ReviewNarrativeAsync` in `src/Ato.Copilot.Agents/Compliance/Services/NarrativeGovernanceService.cs` — approve (set Approved, update `ApprovedVersionId` on `ControlImplementation`) or request revision (set NeedsRevision with reviewer comments), create `NarrativeReview` record (FR-009, FR-011, FR-012)
- [X] T019 [US2] Implement `BatchReviewNarrativesAsync` in `src/Ato.Copilot.Agents/Compliance/Services/NarrativeGovernanceService.cs` — iterate InReview narratives by family_filter or control_ids, call review logic per control, return reviewed/skipped counts (FR-013a)
- [X] T020 [US2] Add InReview edit guard to `WriteNarrativeAsync` in `src/Ato.Copilot.Agents/Compliance/Services/SspService.cs` and `RollbackNarrativeAsync` in `NarrativeGovernanceService.cs` — reject writes/rollbacks when current status is InReview with `UNDER_REVIEW` error (FR-010)
- [X] T021 [P] [US2] Create `SubmitNarrativeTool` class extending `BaseTool` in `src/Ato.Copilot.Agents/Compliance/Tools/NarrativeGovernanceTools.cs` with `system_id`, `control_id` parameters per contracts/tool-contracts.md
- [X] T022 [P] [US2] Create `ReviewNarrativeTool` class extending `BaseTool` in `src/Ato.Copilot.Agents/Compliance/Tools/NarrativeGovernanceTools.cs` with `system_id`, `control_id`, `decision`, `comments` parameters per contracts/tool-contracts.md
- [X] T023 [P] [US2] Create `BatchReviewNarrativesTool` class extending `BaseTool` in `src/Ato.Copilot.Agents/Compliance/Tools/NarrativeGovernanceTools.cs` with `system_id`, `decision`, `comments`, `family_filter`, `control_ids` parameters per contracts/tool-contracts.md
- [X] T024 [US2] Register `SubmitNarrativeTool`, `ReviewNarrativeTool`, `BatchReviewNarrativesTool` as `AddSingleton` + `BaseTool` forwarding in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs` and add `RegisterTool()` calls in `src/Ato.Copilot.Agents/Compliance/Agents/ComplianceAgent.cs`
- [X] T040 [US2] Add unit tests to `tests/Ato.Copilot.Tests.Unit/Compliance/NarrativeGovernanceServiceTests.cs` — test `SubmitNarrativeAsync` (Draft→InReview transition, invalid status rejection, SubmittedBy/SubmittedAt populated), `ReviewNarrativeAsync` (approve sets Approved + ApprovedVersionId, reject sets NeedsRevision with comments, invalid status rejection, comments required on rejection), `BatchReviewNarrativesAsync` (batch approve, batch reject, mixed statuses), InReview edit guard on `WriteNarrativeAsync` and `RollbackNarrativeAsync`
- [X] T040 [US2] Add integration tests to `tests/Ato.Copilot.Tests.Integration/Compliance/NarrativeGovernanceToolTests.cs` — test `SubmitNarrativeTool`, `ReviewNarrativeTool`, `BatchReviewNarrativesTool` happy paths and error paths (INVALID_STATUS, COMMENTS_REQUIRED, UNDER_REVIEW) via WebApplicationFactory

**Checkpoint**: Full approval lifecycle operational — submit, review, batch review, edit guards enforced.

---

## Phase 5: User Story 3 — Narrative Approval Progress Dashboard (Priority: P3)

**Goal**: ISSM sees aggregate approval status counts, overall approval percentage, per-family breakdown, review queue, and staleness warnings for unapproved narratives under Approved SSP §10.

**Independent Test**: Create narratives in Draft/InReview/Approved/NeedsRevision states across AC and SI families, query progress dashboard, verify accurate counts and percentages; filter by "SI" family.

### Implementation for User Story 3

- [X] T025 [US3] Implement `GetNarrativeApprovalProgressAsync` in `src/Ato.Copilot.Agents/Compliance/Services/NarrativeGovernanceService.cs` — aggregate `ControlImplementation.ApprovalStatus` counts by family, compute approval percentage, build review queue (InReview control IDs), generate staleness warnings for unapproved narratives under Approved SSP §10 (FR-014, FR-015, FR-016, FR-026)
- [X] T026 [US3] Create `NarrativeApprovalProgressTool` class extending `BaseTool` in `src/Ato.Copilot.Agents/Compliance/Tools/NarrativeGovernanceTools.cs` with `system_id`, `family_filter` parameters per contracts/tool-contracts.md
- [X] T027 [US3] Register `NarrativeApprovalProgressTool` as `AddSingleton` + `BaseTool` forwarding in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs` and add `RegisterTool()` call in `src/Ato.Copilot.Agents/Compliance/Agents/ComplianceAgent.cs`
- [X] T040 [US3] Add unit tests to `tests/Ato.Copilot.Tests.Unit/Compliance/NarrativeGovernanceServiceTests.cs` — test `GetNarrativeApprovalProgressAsync` (correct status counts by family, approval percentage calculation, review queue includes InReview controls, staleness warnings for unapproved narratives under Approved §10, family filter)

**Checkpoint**: Progress dashboard operational — ISSM can track approval status across all controls and families.

---

## Phase 6: User Story 4 — Batch Narrative Submission (Priority: P4)

**Goal**: ISSO submits all Draft narratives for a control family (or all families) for ISSM review in a single action.

**Independent Test**: Write 5 AC-family narratives in Draft, batch-submit with family_filter "AC", verify all 5 transition to InReview; mix Draft and Approved, verify only Draft narratives submitted.

### Implementation for User Story 4

- [X] T028 [US4] Implement `BatchSubmitNarrativesAsync` in `src/Ato.Copilot.Agents/Compliance/Services/NarrativeGovernanceService.cs` — query Draft `ControlImplementation` records by family_filter, transition each to InReview, return submitted/skipped counts with control ID lists (FR-013)
- [X] T029 [US4] Create `BatchSubmitNarrativesTool` class extending `BaseTool` in `src/Ato.Copilot.Agents/Compliance/Tools/NarrativeGovernanceTools.cs` with `system_id`, `family_filter` parameters per contracts/tool-contracts.md
- [X] T030 [US4] Register `BatchSubmitNarrativesTool` as `AddSingleton` + `BaseTool` forwarding in `src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs` and add `RegisterTool()` call in `src/Ato.Copilot.Agents/Compliance/Agents/ComplianceAgent.cs`
- [X] T040 [US4] Add unit tests to `tests/Ato.Copilot.Tests.Unit/Compliance/NarrativeGovernanceServiceTests.cs` — test `BatchSubmitNarrativesAsync` (all Draft submitted, mixed statuses skip non-Draft, family filter, empty result)

**Checkpoint**: Batch submission operational — ISSOs can submit entire control families for review in one action.

---

## Phase 7: User Story 5 — Concurrent Edit Protection (Priority: P5)

**Goal**: Detect conflicting edits using optimistic concurrency — second write with stale `expected_version` fails with actionable conflict error.

**Independent Test**: Two users read same version, User A writes successfully, User B writes with stale expected_version → conflict error with current version number and last modifier.

### Implementation for User Story 5

- [X] T031 [US5] Implement `expected_version` validation in `WriteNarrativeAsync` in `src/Ato.Copilot.Agents/Compliance/Services/SspService.cs` — when `expected_version` is provided and does not match `ControlImplementation.CurrentVersion`, reject with concurrency conflict including current version, last modifier, and last modification timestamp (FR-017, FR-018)
- [X] T032 [US5] Add `CONCURRENCY_CONFLICT` error code handling to `WriteNarrativeTool` in `src/Ato.Copilot.Agents/Compliance/Tools/SspAuthoringTools.cs` — return structured error with `current_version`, `last_modified_by`, `last_modified_at` fields per contracts/tool-contracts.md
- [X] T040 [US5] Add unit tests to `tests/Ato.Copilot.Tests.Unit/Compliance/NarrativeGovernanceServiceTests.cs` — test `expected_version` concurrency validation (match succeeds, mismatch rejected with current version/last modifier, null expected_version bypasses check)

**Checkpoint**: Concurrent edit protection operational — stale writes rejected with resolution information.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: SSP integration, existing tool updates, and documentation

- [X] T033 [P] Update `GenerateSspAsync` in `src/Ato.Copilot.Agents/Compliance/Services/SspService.cs` to use `ApprovedVersionId` for approved narrative content with fallback to `Narrative` field (Draft) and warning when no approved version exists (FR-024)
- [X] T034 [P] Update `NarrativeProgressTool` response in `src/Ato.Copilot.Agents/Compliance/Tools/SspAuthoringTools.cs` to distinguish Draft-only and Approved narratives in completion status (FR-025)
- [X] T035 [P] Add staleness warnings to `SspCompletenessTool` in `src/Ato.Copilot.Agents/Compliance/Tools/SspAuthoringTools.cs` for unapproved narratives under Approved SSP §10 section (FR-026)
- [X] T036 [P] Update `docs/architecture/agent-tool-catalog.md` with 8 new tool entries and updated `compliance_write_narrative` entry per contracts/tool-contracts.md (FR-027, FR-032)
- [X] T037 [P] Update `docs/architecture/data-model.md` with `NarrativeVersion`, `NarrativeReview` entities, enhanced `ControlImplementation` fields, `ReviewDecision` enum, and ER diagram additions per data-model.md (FR-028)
- [X] T038 [P] Update `docs/persona-test-cases/environment-checklist.md` and `docs/persona-test-cases/tool-validation.md` with all 8 new tools, and add end-to-end test scenarios to persona test cases (FR-029, FR-030)
- [X] T039 [P] Update `docs/guides/engineer-guide.md`, `docs/guides/issm-guide.md`, and `docs/guides/sca-guide.md` with narrative governance workflows, version history access, and approval procedures (FR-031)
- [X] T040 Build `dotnet build Ato.Copilot.sln` with zero warnings, run `dotnet test Ato.Copilot.sln`, and validate quickstart.md verification workflow end-to-end

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — **BLOCKS all user stories**
- **US1 (Phase 3)**: Depends on Phase 2 — no other story dependencies — **MVP**
- **US2 (Phase 4)**: Depends on Phase 2 + Phase 3 (approval applies to versioned narratives)
- **US3 (Phase 5)**: Depends on Phase 2 (reads ApprovalStatus); can run in parallel with US2 if US1 complete
- **US4 (Phase 6)**: Depends on Phase 4 (batch submit uses submit logic from US2)
- **US5 (Phase 7)**: Depends on Phase 3 (concurrency applies to version-aware writes)
- **Polish (Phase 8)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: Foundational only — independent, MVP-complete
- **US2 (P2)**: US1 (approvals apply to specific versions)
- **US3 (P3)**: US1 (reads ApprovalStatus set by US1+US2; can start after US1 if US2 not needed for counts)
- **US4 (P4)**: US2 (batch submit uses submit workflow from US2)
- **US5 (P5)**: US1 (concurrency check on version-aware writes)

### Within Each User Story

- Service methods before tool classes
- Tool classes before DI/agent registration
- Core implementation before integration

### Parallel Opportunities

Within **Phase 3** (US1):
- T010, T011 can run in parallel (independent service methods)
- T013, T014, T015 can run in parallel (separate tool classes in same file)

Within **Phase 4** (US2):
- T021, T022, T023 can run in parallel (separate tool classes)

Within **Phase 8** (Polish):
- T033, T034, T035 can run in parallel (different tools in different methods)
- T036, T037, T038, T039 can run in parallel (different documentation files)

---

## Parallel Example: User Story 1

```text
# After T008, T009 complete (version-creating write):

# Launch service methods in parallel:
T010: "Implement GetNarrativeHistoryAsync in NarrativeGovernanceService.cs"
T011: "Implement GetNarrativeDiffAsync in NarrativeGovernanceService.cs"

# After T010, T011, T012 complete:

# Launch tool classes in parallel:
T013: "Create NarrativeHistoryTool in NarrativeGovernanceTools.cs"
T014: "Create NarrativeDiffTool in NarrativeGovernanceTools.cs"
T015: "Create RollbackNarrativeTool in NarrativeGovernanceTools.cs"
```

---

## Phase 8: User Story 6 — Control Narrative Workspace (Priority: P1)

**Goal**: Replace the competing dashboard list/review composition with a concise,
control-oriented workspace and state-preserving detail drawer while retaining the
existing narrative, proposal, reference, evidence, governance, and bulk workflows.

**Independent Test**: Load independently populated Policy/Technical records and
current/stale proposals, verify consistent view counts and filters, deep-link the
drawer, inspect evidence/history/provenance, and exercise view-only, author, and
review permissions plus stale/foreign mutation failures.

- [x] T050 [US6] Document the aggregate dashboard contract and verified SSP/OSCAL/eMASS consumers in `specs/024-narrative-governance/contracts/dashboard-workspace.md`
- [x] T051 [US6] Add failing backend projection tests for readable control names, consistent view counts, independent Policy/Technical display state, approved snapshots, current proposals, authorization, tenant/system isolation, and history in `tests/Ato.Copilot.Tests.Integration/Tenancy/ControlNarrativeWorkspaceHttpTests.cs`
- [x] T052 [US6] Add failing frontend API validation tests for malformed, foreign, incomplete, stale, and permission-limited workspace/detail responses in `src/Ato.Copilot.Dashboard/src/__tests__/api/controlNarrativeWorkspace.test.ts`
- [x] T053 [US6] Add failing page and drawer tests for views, filters, independent statement selection, direct links, focus restoration, retained list state, authorized actions, stale proposals, empty content, evidence/history, and retry states in `src/Ato.Copilot.Dashboard/src/__tests__/pages/ControlNarrativeWorkspace.test.tsx`
- [x] T054 [US6] Add a tenant-scoped aggregate read projection over existing `ControlImplementation`, `NarrativeVersion`, `NarrativeReview`, `NarrativeProposal`, NIST control, validation-link, and responsibility records in `src/Ato.Copilot.Mcp/Endpoints/Dashboard/DashboardNarrativeWorkspaceEndpoints.cs`
- [x] T055 [US6] Add fail-closed TypeScript transport validation in `src/Ato.Copilot.Dashboard/src/api/controlNarrativeWorkspace.ts`
- [x] T056 [US6] Implement the compact list, truthful counts, combined filter toolbar, actionable proposal notification, and collapsed SSP contribution disclosure in `src/Ato.Copilot.Dashboard/src/features/narratives/ControlNarrativeWorkspace.tsx`
- [x] T057 [US6] Implement the responsive, URL-addressable Statements/Evidence/History drawer with readable proposal comparison, provenance, responsibility dependencies, action explanations, keyboard focus, and retained list state in `src/Ato.Copilot.Dashboard/src/features/narratives/ControlNarrativeDrawer.tsx`
- [x] T058 [US6] Recompose `src/Ato.Copilot.Dashboard/src/pages/NarrativeWorkspace.tsx` so the new workspace is primary while existing reference import/library, proposal review, direct editing, creation, and bulk workflows remain reachable
- [x] T059 [US6] Add desktop/mobile Playwright coverage for filtering/count consistency, deep links, keyboard interaction, responsive drawer layout, authorized actions, stale proposal handling, and navigation-state retention in `src/Ato.Copilot.Dashboard/e2e/tests/control-narratives.spec.ts`
- [x] T060 [US6] Verify targeted backend/frontend tests, dashboard type-check/build, supported themes, desktop/mobile browser behavior, `git diff --check`, and the actual SSP/OSCAL/eMASS export behavior; record any unresolved approved-snapshot export gap without claiming package readiness

**Checkpoint**: Control narratives are understandable and operable from one
control-oriented workspace; no new narrative store or client-derived authorization
path exists.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (DiffPlex + models)
2. Complete Phase 2: Foundational (schema, migration, service interface, DI)
3. Complete Phase 3: User Story 1 (version history, diff, rollback)
4. **STOP and VALIDATE**: Write → update → history → diff → rollback chain works independently
5. Deploy/demo if ready — audit trail and rollback safety net delivered

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 → Version history operational → **MVP!**
3. US2 → Approval workflow (submit/review/batch review) → Deploy/Demo
4. US3 → Progress dashboard → Deploy/Demo
5. US4 → Batch submission → Deploy/Demo
6. US5 → Concurrent edit protection → Deploy/Demo
7. US6 → Control narrative workspace → Deploy/Demo
8. Polish → SSP integration, docs, final validation

### Parallel Team Strategy

With multiple developers after Foundational is complete:

- Developer A: US1 (P1) → US2 (P2, depends on US1)
- Developer B: US5 (P5, depends on US1 only — can start after A finishes US1) → US4 (P4, depends on US2)
- Developer C: US3 (P3, depends on US1 only — can start after A finishes US1) → Polish docs

---

## Notes

- [P] tasks = different files or independent methods, no ordering dependencies
- [USn] label maps task to spec user story for traceability
- Each user story is independently testable after its checkpoint
- Test tasks (T041–T047) follow each user story's implementation tasks, per Constitution Principle III (Testing Standards — NON-NEGOTIABLE)
- `SspSectionStatus` enum is reused for narrative approval status (research R-003) — no new enum needed
- DiffPlex `Differ` class provides line-level unified diff (research R-002)
- Tool response envelope follows existing `{ status, data, metadata }` pattern
- All tools use constructor-injected `INarrativeGovernanceService` (research R-007)
- `ControlImplementation.Narrative` field continues to hold latest version content for backward compatibility (research R-004)
- `NarrativeVersion.SubmittedBy`/`SubmittedAt` fields provide audit trail coverage for Draft→InReview transitions (FR-022)
