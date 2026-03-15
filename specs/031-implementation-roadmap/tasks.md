# Tasks: Implementation Roadmap

**Input**: Design documents from `/specs/031-implementation-roadmap/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Required per Constitution III (Testing Standards, NON-NEGOTIABLE). Unit and integration test tasks are included in Phase 9 (Polish).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create entity models, enums, and register with EF Core context

- [X] T001 [P] Create roadmap enums (RoadmapStatus, PhaseStatus, ItemStatus, GapType, ItemSeverity) in src/Ato.Copilot.Core/Models/Roadmap/RoadmapEnums.cs
- [X] T002 [P] Create ImplementationRoadmap entity with SystemId FK, Status, TotalEstimatedEffort, TotalRiskPoints, LinkedBoardId, Version, RowVersion per data-model.md in src/Ato.Copilot.Core/Models/Roadmap/ImplementationRoadmap.cs
- [X] T003 [P] Create RoadmapPhase entity with RoadmapId FK, DisplayOrder, EstimatedEffort, RiskPoints, RiskReductionPercent, TargetStartWeek/EndWeek, cached counts per data-model.md in src/Ato.Copilot.Core/Models/Roadmap/RoadmapPhase.cs
- [X] T004 [P] Create RoadmapItem entity with PhaseId/RoadmapId FKs, ControlId, GapType, Severity, RiskPoints, EstimatedEffortDays, AssignedRole, DependsOn, LinkedTaskId per data-model.md in src/Ato.Copilot.Core/Models/Roadmap/RoadmapItem.cs
- [X] T005 Register ImplementationRoadmaps, RoadmapPhases, and RoadmapItems DbSets with indexes (IX_SystemId_Status, IX_RoadmapId_DisplayOrder, IX_PhaseId, IX_ControlId) in src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Service interface, DTOs, core service scaffold with risk calculation and dependency data

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T006 [P] Create IRoadmapService interface with method signatures for GenerateRoadmapAsync, GetRoadmapAsync, GetRoadmapProgressAsync, UpdateRoadmapAsync, CreateBoardFromRoadmapAsync, SyncRoadmapItemStatusAsync, ExportRoadmapPdfAsync in src/Ato.Copilot.Core/Interfaces/Roadmap/IRoadmapService.cs
- [X] T007 [P] Create RoadmapDtos (RoadmapDto, RoadmapPhaseDto, RoadmapItemDto, RoadmapProgressDto, RiskCurvePointDto, PhaseProgressDto) per contracts/dashboard-api.md in src/Ato.Copilot.Mcp/Dtos/Dashboard/RoadmapDtos.cs
- [X] T008 [P] Add nullable RoadmapItemId property (string?, indexed) to existing RemediationTask entity for bi-directional Kanban sync per data-model.md in src/Ato.Copilot.Core/Models/ (existing RemediationTask file)
- [X] T009 Scaffold RoadmapService class with constructor injection (AtoCopilotContext, IChatClient, IKanbanService, ILogger), static control dependency dictionary per research R4, and CalculateRiskReduction pure function per research R3 (CAT I=10, CAT II=5, CAT III=1) in src/Ato.Copilot.Mcp/Services/RoadmapService.cs
- [X] T010 Register IRoadmapService → RoadmapService in the DI container and add QuestPDF NuGet package to Ato.Copilot.Mcp.csproj

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 — Generate a Phased Roadmap from Gap Analysis (Priority: P1) 🎯 MVP

**Goal**: ISSM generates a multi-phase implementation roadmap from gap analysis data with AI-driven clustering, effort estimates, and risk projections

**Independent Test**: Run gap analysis on a system with a selected baseline and unmapped controls, then request a roadmap. Verify a phased plan is returned with effort estimates, risk reduction percentages, and correct dependency sequencing.

### Implementation for User Story 1

- [X] T011 [US1] Implement GenerateRoadmapAsync in RoadmapService.cs — fetch gap analysis via CapabilityService.GetGapAnalysisAsync, build AI clustering prompt with control metadata and dependency constraints (R1), deserialize structured JSON response into phases, calculate risk reduction per phase (R3), estimate effort via AI prompt (R2) with historical Kanban task duration query (median completion days per ControlId from completed RemediationTasks) to refine AI estimates when data exists (FR-004), persist roadmap with Draft status, enforce one-Active-per-system rule, handle edge cases: no-baseline returns error per contracts/mcp-tools.md no-baseline response, zero-gaps returns success message per contracts/mcp-tools.md no-gaps response (FR-015) in src/Ato.Copilot.Mcp/Services/RoadmapService.cs
- [X] T012 [US1] Implement GetRoadmapAsync in RoadmapService.cs — load active roadmap for a system with eager-loaded phases and items, return null if none exists in src/Ato.Copilot.Mcp/Services/RoadmapService.cs
- [X] T013 [US1] Implement deterministic fallback clustering in RoadmapService.cs — severity-first grouping (Critical → High → Medium) when AI call fails, with dependency validation post-assignment in src/Ato.Copilot.Mcp/Services/RoadmapService.cs
- [X] T014 [P] [US1] Create GenerateRoadmapTool extending BaseTool with Name="compliance_generate_roadmap", system_id parameter, RequiredPimTier="Compliance.SecurityLead", calls RoadmapService.GenerateRoadmapAsync, returns McpToolResult with roadmap JSON per contracts/mcp-tools.md in src/Ato.Copilot.Agents/Compliance/Tools/RoadmapTools.cs
- [X] T015 [P] [US1] Create GetRoadmapTool extending BaseTool with Name="compliance_get_roadmap", system_id and include_items parameters, no PIM tier restriction (read-only), calls RoadmapService.GetRoadmapAsync in src/Ato.Copilot.Agents/Compliance/Tools/RoadmapTools.cs
- [X] T016 [US1] Register GenerateRoadmapTool and GetRoadmapTool with the ComplianceAgent tool list in src/Ato.Copilot.Agents/Compliance/ (existing agent registration file)

**Checkpoint**: ISSM can generate and retrieve a phased roadmap via MCP tools. MVP complete.

---

## Phase 4: User Story 2 — View Roadmap in the Dashboard (Priority: P1)

**Goal**: Dashboard renders a roadmap page with timeline visualization, risk reduction curve, and expandable phase detail tables

**Independent Test**: Generate a roadmap for a system, navigate to `/systems/:id/roadmap` in the dashboard, verify timeline bars, risk curve, and phase tables render with live data.

### Implementation for User Story 2

- [X] T017 [US2] Add GET /api/dashboard/systems/{systemId}/roadmap endpoint (includeItems query param) returning RoadmapDto or 404, mapping entities to DTOs per contracts/dashboard-api.md in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T018 [P] [US2] Add roadmap TypeScript types (Roadmap, RoadmapPhase, RoadmapItem, RoadmapProgress, RiskCurvePoint) per dashboard-api.md response shapes in src/Ato.Copilot.Dashboard/src/types/dashboard.ts
- [X] T019 [P] [US2] Create roadmap API client with fetchRoadmap(systemId) and fetchRoadmapProgress(systemId) functions in src/Ato.Copilot.Dashboard/src/api/roadmap.ts
- [X] T020 [P] [US2] Create RoadmapTimeline component — horizontal Gantt-style phase bars on a week-based axis with progress fill, phase names, effort labels, and status badges using Tailwind CSS in src/Ato.Copilot.Dashboard/src/components/charts/RoadmapTimeline.tsx
- [X] T021 [P] [US2] Create RiskReductionCurve component — Recharts AreaChart showing projected cumulative risk reduction from week 1 through final phase, with week on X-axis and risk reduction % on Y-axis in src/Ato.Copilot.Dashboard/src/components/charts/RiskReductionCurve.tsx
- [X] T022 [US2] Create Roadmap page — summary metric cards (total gaps, effort, risk reduction, timeline), RoadmapTimeline, RiskReductionCurve, expandable phase tables with columns (Control ID, Gap Type, Effort, Role, Dependencies, Status), fetch data via roadmap API client in src/Ato.Copilot.Dashboard/src/pages/Roadmap.tsx
- [X] T023 [US2] Add /systems/:id/roadmap route to the dashboard router and add Roadmap tab link on the SystemDetail page in src/Ato.Copilot.Dashboard/src/ (existing router and SystemDetail files)

**Checkpoint**: Roadmap page is fully functional in the dashboard with timeline, risk curve, and phase tables.

---

## Phase 5: User Story 3 — View Roadmap via M365 Teams Chat (Priority: P1)

**Goal**: Teams Adaptive Cards render roadmap summaries and phase details with action buttons

**Independent Test**: Send "Generate an implementation roadmap for [System]" in Teams, verify an Adaptive Card renders with phase summaries, effort totals, risk projections, and action buttons.

### Implementation for User Story 3

- [X] T024 [P] [US3] Create roadmapCard.ts — summary Adaptive Card builder for data.type "roadmap" showing total gaps, phase count, total effort, risk reduction, per-phase rows (name, timeline, control count, effort, risk %), action buttons (Create Kanban Board, Export PDF, Show Phase Details), follows buildAgentAttribution + buildSuggestionButtons pattern in extensions/m365/src/cards/roadmapCard.ts
- [X] T025 [P] [US3] Create roadmapPhaseDetailCard.ts — detail Adaptive Card builder for data.type "roadmapPhaseDetail" showing items table with columns (Control ID, Effort, Role, Gap Type, Dependencies, Status), "Back to Roadmap" button in extensions/m365/src/cards/roadmapPhaseDetailCard.ts
- [X] T026 [US3] Add "roadmap" and "roadmapPhaseDetail" case routing to the switch statement in cardRouter.ts, calling roadmapCard and roadmapPhaseDetailCard builders in extensions/m365/src/cards/cardRouter.ts
- [X] T027 [US3] Re-export roadmapCard and roadmapPhaseDetailCard from the cards barrel file in extensions/m365/src/cards/index.ts

**Checkpoint**: Roadmap data from MCP tools renders as Adaptive Cards in Teams with all action buttons functional.

---

## Phase 6: User Story 4 — Bridge Roadmap to Kanban Execution (Priority: P2)

**Goal**: Convert roadmap into a pre-populated Kanban board with bi-directional status sync

**Independent Test**: Generate a roadmap, trigger "Create Kanban Board", verify tasks are created with correct control IDs, effort, and roles. Move a task to Done and confirm the roadmap phase progress updates.

### Implementation for User Story 4

- [X] T028 [US4] Implement CreateBoardFromRoadmapAsync in RoadmapService.cs — create RemediationBoard via IKanbanService, create one RemediationTask per RoadmapItem with control ID, effort, role, set LinkedTaskId on each RoadmapItem and RoadmapItemId on each task, set LinkedBoardId on roadmap, warn if board already exists in src/Ato.Copilot.Mcp/Services/RoadmapService.cs
- [X] T029 [US4] Create CreateBoardFromRoadmapTool extending BaseTool with Name="compliance_create_board_from_roadmap", system_id parameter, RequiredPimTier="Compliance.SecurityLead", returns board summary per contracts/mcp-tools.md in src/Ato.Copilot.Agents/Compliance/Tools/RoadmapTools.cs
- [X] T030 [US4] Implement SyncRoadmapItemStatusAsync in RoadmapService.cs — map TaskStatus to ItemStatus, update RoadmapItem status, recalculate phase CompletedItemCount and Status, update phase to InProgress/Complete as appropriate in src/Ato.Copilot.Mcp/Services/RoadmapService.cs
- [X] T031 [US4] Add post-move hook in KanbanService.MoveTaskAsync — after persisting task status change, check if task has RoadmapItemId and call IRoadmapService.SyncRoadmapItemStatusAsync to propagate status. Also add post-delete hook in KanbanService.DeleteTaskAsync — if deleted task has RoadmapItemId, clear the RoadmapItem's LinkedTaskId, revert item status to NotStarted, and log a warning (spec edge case: Kanban task deleted) in src/Ato.Copilot.Mcp/Services/ (existing KanbanService file)
- [X] T032 [US4] Register CreateBoardFromRoadmapTool with the ComplianceAgent tool list in src/Ato.Copilot.Agents/Compliance/ (existing agent registration file)

**Checkpoint**: Roadmap-to-Kanban bridge is functional with bi-directional sync on task status changes.

---

## Phase 7: User Story 5 — Track Roadmap Progress Over Time (Priority: P2)

**Goal**: Display actual completion vs. planned timeline with overdue phase highlighting and projected vs. actual risk reduction

**Independent Test**: Generate a roadmap, complete some Kanban tasks, verify the progress view shows actual-vs-planned metrics with overdue flags.

### Implementation for User Story 5

- [X] T033 [US5] Implement GetRoadmapProgressAsync in RoadmapService.cs — compute per-phase completion %, overall completion %, overdue detection (compare TargetEndWeek against current week), build risk curve data points (week → risk points remaining → reduction %), compare projected vs actual risk reduction by fetching latest gap analysis via CapabilityService.GetGapAnalysisAsync and computing actual reduction as (initial total gaps − current open gaps) / initial total gaps × 100% (FR-014) in src/Ato.Copilot.Mcp/Services/RoadmapService.cs
- [X] T034 [US5] Create GetRoadmapProgressTool extending BaseTool with Name="compliance_get_roadmap_progress", system_id parameter, no PIM tier restriction (read-only), returns progress JSON per contracts/mcp-tools.md in src/Ato.Copilot.Agents/Compliance/Tools/RoadmapTools.cs
- [X] T035 [US5] Add GET /api/dashboard/systems/{systemId}/roadmap/progress endpoint returning RoadmapProgressDto with risk curve array and per-phase progress per contracts/dashboard-api.md in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T036 [US5] Add progress section to Roadmap.tsx — dual-line RiskReductionCurve (projected + actual), per-phase progress bars with overdue highlighting (red badge + days overdue count), items completed vs total in src/Ato.Copilot.Dashboard/src/pages/Roadmap.tsx
- [X] T037 [US5] Register GetRoadmapProgressTool with the ComplianceAgent tool list in src/Ato.Copilot.Agents/Compliance/ (existing agent registration file)

**Checkpoint**: Progress tracking shows actual vs. planned metrics with overdue highlighting in both MCP and dashboard.

---

## Phase 8: User Story 6 — Update and Reassign Roadmap Items (Priority: P3)

**Goal**: ISSM can restructure phases, reassign controls, adjust effort, merge/split phases, with changes propagating to linked Kanban tasks

**Independent Test**: Generate a roadmap, move a control between phases, change a role assignment, update effort, verify all changes persist and linked Kanban tasks reflect the changes.

### Implementation for User Story 6

- [X] T038 [US6] Implement UpdateRoadmapItemAsync in RoadmapService.cs — move item between phases (update PhaseId, recalculate both phases' RiskPoints/RiskReductionPercent/TotalItemCount), update effort estimate, update assigned role, recalculate parent roadmap TotalEstimatedEffort in src/Ato.Copilot.Mcp/Services/RoadmapService.cs
- [X] T039 [US6] Implement MergePhasesAsync in RoadmapService.cs — move all items from source phase to target phase, delete empty source phase, renumber DisplayOrder for subsequent phases, recalculate target phase aggregates in src/Ato.Copilot.Mcp/Services/RoadmapService.cs
- [X] T040 [US6] Implement SplitPhaseAsync in RoadmapService.cs — create new phase at next DisplayOrder, move items after split index to new phase, renumber subsequent phases, recalculate both phases' aggregates in src/Ato.Copilot.Mcp/Services/RoadmapService.cs
- [X] T041 [US6] Implement linked Kanban task propagation — when roadmap item role or effort changes, update the corresponding RemediationTask's assignee and effort fields if LinkedTaskId is set in src/Ato.Copilot.Mcp/Services/RoadmapService.cs
- [X] T042 [US6] Create UpdateRoadmapTool extending BaseTool with Name="compliance_update_roadmap", system_id + move_item/update_effort/update_role/merge_phases/split_phase parameters, RequiredPimTier="Compliance.SecurityLead" per contracts/mcp-tools.md in src/Ato.Copilot.Agents/Compliance/Tools/RoadmapTools.cs
- [X] T043 [US6] Register UpdateRoadmapTool with the ComplianceAgent tool list in src/Ato.Copilot.Agents/Compliance/ (existing agent registration file)
- [X] T044 [US6] Handle edge case: auto-remove empty phases after item moves and warn when new gaps appear that are untracked by the active roadmap (FR-016) in src/Ato.Copilot.Mcp/Services/RoadmapService.cs

**Checkpoint**: All roadmap restructuring operations work with Kanban propagation. All user stories complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: PDF export, unit/integration tests, documentation, and validation

- [X] T045 [P] Implement ExportRoadmapPdfAsync in RoadmapService.cs using QuestPDF — render header (system name, status, date), summary metrics, phase timeline bar chart, phase detail tables (Control ID, Gap Type, Effort, Role, Status), risk reduction curve line chart per research R6 in src/Ato.Copilot.Mcp/Services/RoadmapService.cs
- [X] T046 [P] Create ExportRoadmapPdfTool extending BaseTool with Name="compliance_export_roadmap_pdf", system_id parameter, no PIM tier restriction (read-only), returns base64 PDF per contracts/mcp-tools.md in src/Ato.Copilot.Agents/Compliance/Tools/RoadmapTools.cs
- [X] T047 Add GET /api/dashboard/systems/{systemId}/roadmap/export endpoint returning PDF binary with Content-Disposition header per contracts/dashboard-api.md in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T048 Register ExportRoadmapPdfTool with the ComplianceAgent tool list in src/Ato.Copilot.Agents/Compliance/ (existing agent registration file)
- [X] T049 [P] Create RoadmapServiceTests — unit tests for CalculateRiskReduction (positive: mixed severities, single severity; negative: empty items, zero total points), GenerateRoadmapAsync (mock IChatClient, verify phase assignment, verify dependency ordering), GetRoadmapAsync (found/not found), CreateBoardFromRoadmapAsync (mock IKanbanService) in tests/Ato.Copilot.Tests.Unit/Roadmap/RoadmapServiceTests.cs
- [X] T050 [P] Create RoadmapServiceRiskCalculationTests — unit tests for CalculateRiskReduction method: weighted severity formula (CAT I=10, CAT II=5, CAT III=1), percentage calculation, cumulative reduction across phases, edge cases: all same severity, empty items, zero total points in tests/Ato.Copilot.Tests.Unit/Roadmap/RoadmapServiceRiskCalculationTests.cs
- [X] T051 [P] Create RoadmapToolsTests — unit tests for each MCP tool (GenerateRoadmapTool, GetRoadmapTool, GetRoadmapProgressTool, UpdateRoadmapTool, CreateBoardFromRoadmapTool, ExportRoadmapPdfTool) verifying parameter validation, RBAC enforcement, McpToolResult envelope format in tests/Ato.Copilot.Tests.Unit/Roadmap/RoadmapToolsTests.cs
- [X] T052 Create RoadmapEndpointsTests — integration tests using WebApplicationFactory for GET roadmap, GET progress, GET export endpoints verifying 200/404 responses, DTO shapes, PDF content-type, and response-time assertions per constitution Quality Gates: GET roadmap < 3s (SC-006), GET progress < 5s, GET export < 30s (SC-001) in tests/Ato.Copilot.Tests.Integration/Roadmap/RoadmapEndpointsTests.cs
- [X] T053 [P] Update feature documentation — add implementation roadmap section to docs/guides/ covering usage for ISSM, ISSO, Engineer, AO personas per existing guide structure in docs/guides/
- [X] T054 Run quickstart.md validation — execute all steps from specs/031-implementation-roadmap/quickstart.md (generate roadmap, view dashboard, restructure, bridge to Kanban, export PDF, Teams cards) and verify end-to-end flow

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational — core generation logic
- **US2 (Phase 4)**: Depends on Foundational + US1 endpoint (T017 needs GetRoadmapAsync from T012)
- **US3 (Phase 5)**: Depends on Foundational — can start after Phase 2 (card builders consume MCP response JSON)
- **US4 (Phase 6)**: Depends on Foundational — can start after Phase 2 (Kanban bridge uses service methods)
- **US5 (Phase 7)**: Depends on Foundational — can start after Phase 2 (progress reads stored data)
- **US6 (Phase 8)**: Depends on Foundational — can start after Phase 2 (update operates on stored entities)
- **Polish (Phase 9)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Phase 2 — no dependencies on other stories
- **User Story 2 (P1)**: Requires US1 T012 (GetRoadmapAsync) for the dashboard endpoint — start after US1 or implement with stub data
- **User Story 3 (P1)**: Can start after Phase 2 — card builders are independent of backend implementation
- **User Story 4 (P2)**: Can start after Phase 2 — CreateBoardFromRoadmap uses IKanbanService + stored entities
- **User Story 5 (P2)**: Can start after Phase 2 — progress reads stored entity data
- **User Story 6 (P3)**: Can start after Phase 2 — update operates on stored entities

### Within Each User Story

- Service methods before MCP tools
- MCP tools before tool registration
- API endpoints before frontend components
- Components before page assembly
- Page before route registration

### Parallel Opportunities

- **Setup**: T001, T002, T003, T004 can all run in parallel (different files)
- **Foundational**: T006, T007, T008 can run in parallel (different files)
- **US1**: T014 and T015 can run in parallel (same file but independent tool classes)
- **US2**: T018, T019, T020, T021 can run in parallel (different files)
- **US3**: T024 and T025 can run in parallel (different files)
- **US4-US6**: Mostly sequential within each story (service methods depend on each other)
- **Polish**: T045+T046, T049+T050+T051 can run in parallel (different files)
- **Cross-story**: US3 and US4 can run in parallel with US1 (after Phase 2)

---

## Parallel Example: User Story 2

```bash
# Launch all independent frontend tasks together:
Task T018: "Add roadmap TypeScript types to dashboard.ts"
Task T019: "Create roadmap API client in roadmap.ts"
Task T020: "Create RoadmapTimeline.tsx component"
Task T021: "Create RiskReductionCurve.tsx component"

# Then sequentially:
Task T022: "Create Roadmap.tsx page" (depends on T020, T021)
Task T023: "Add roadmap route to router" (depends on T022)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (5 tasks)
2. Complete Phase 2: Foundational (5 tasks)
3. Complete Phase 3: User Story 1 (6 tasks)
4. **STOP and VALIDATE**: ISSM can generate and retrieve roadmaps via MCP tools
5. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready (10 tasks)
2. Add User Story 1 → Generate roadmaps via MCP (MVP!) — 16 tasks cumulative
3. Add User Story 2 → Dashboard visualization — 23 tasks cumulative
4. Add User Story 3 → Teams Adaptive Cards — 27 tasks cumulative
5. Add User Story 4 → Kanban bridge — 32 tasks cumulative
6. Add User Story 5 → Progress tracking — 37 tasks cumulative
7. Add User Story 6 → Restructuring — 44 tasks cumulative
8. Polish → PDF export, tests, docs — 54 tasks total

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together (10 tasks)
2. Once Foundational is done:
   - Developer A: User Story 1 (core generation) → User Story 5 (progress)
   - Developer B: User Story 2 (dashboard) → User Story 6 (restructuring)
   - Developer C: User Story 3 (Teams cards) → User Story 4 (Kanban bridge)
3. Team completes Polish phase together

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks in same phase
- [USx] label maps task to specific user story for traceability
- Each user story should be independently completable and testable after Foundational
- All MCP tool classes go in a single RoadmapTools.cs file per existing convention
- All dashboard DTOs go in a single RoadmapDtos.cs file per existing convention
- Entities extend ConcurrentEntity base class with RowVersion for optimistic concurrency
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Archived roadmap listing/comparison is a post-MVP enhancement (spec edge case: multiple roadmaps per system); active roadmap retrieval covers the primary use case
