# Tasks: eMASS Workflow Sync & OSCAL 1.1.2 Upgrade

**Feature**: 071-emass-workflow-sync | **Issue**: #59
**Input**: `specs/071-emass-workflow-sync/spec.md`, `data-model.md`, `contracts/`
**Prerequisites**: Feature 041 foundation (entities, services, OSCAL schema bundles) MUST be deployed

**Path Conventions**
- Core models/interfaces: `src/Ato.Copilot.Core/`
- Service implementations & MCP tools: `src/Ato.Copilot.Agents/Compliance/`
- Dashboard API endpoints: `src/Ato.Copilot.Mcp/`
- Dashboard frontend: `src/Ato.Copilot.Dashboard/src/`
- Unit tests: `tests/Ato.Copilot.Tests.Unit/`
- Integration tests: `tests/Ato.Copilot.Tests.Integration/`

**Task legend**
- `[P]` = can run in parallel with other `[P]` tasks in the same phase (different files)
- `[USn]` = maps to User Story n in spec.md
- `[x]` = complete | `[ ]` = pending

---

## Phase 1 — OSCAL 1.1.2 Upgrade (US1 — P0 Blocking)

**Goal**: Fix the version mismatch that causes every current eMASS package submission to be rejected. This phase is independent and can ship immediately.

**Independent Test**: Export POA&M and AR via existing MCP tools; confirm `"oscal-version": "1.1.2"` in both outputs and that Feature 041 package generation still passes all tests.

- [ ] T001 [US1] Upgrade `BuildOscalPoam()` in `src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs`:
  - Set `metadata.oscal-version` to `"1.1.2"`
  - Rename field `related-observations` → `related-findings` (1.1.2 schema breaking change)
  - Add `import-ssp` reference using system SSP UUID
  - Validate output against bundled `oscal_poam_schema.json` via `OscalSchemaValidationService` in unit test

- [ ] T002 [US1] Upgrade `BuildOscalAssessmentResults()` in `src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs`:
  - Set `metadata.oscal-version` to `"1.1.2"`
  - Add `reviewed-controls.control-selections` array
  - Update `target.type` and `target.status.state` to valid 1.1.2 enum values
  - Add `import-ap` reference
  - Validate output against bundled `oscal_assessment-results_schema.json` in unit test

- [ ] T003 [P] [US1] Add/update unit tests in `tests/Ato.Copilot.Tests.Unit/Compliance/EmassExportServiceTests.cs`:
  - `BuildOscalPoam_Returns_Oscal112_Schema` — assert version, related-findings field
  - `BuildOscalAssessmentResults_Returns_Oscal112_Schema` — assert version, reviewed-controls
  - `Feature041PackageGeneration_StillPasses_AfterUpgrade` — regression guard

**Checkpoint**: Both OSCAL builders emit 1.1.2. All Feature 041 tests pass. Ship independently.

---

## Phase 2 — Foundation (New Entities + Interfaces)

**Goal**: Add `EmassConflict`, `EmassWorkflowStatus` DTO, and service interfaces. All downstream phases depend on this.

**⚠️ CRITICAL**: Phase 3+ cannot start until this phase is complete.

- [ ] T004 [P] Create `EmassConflict` entity in `src/Ato.Copilot.Core/Models/Compliance/EmassConflict.cs`:
  - Fields per `data-model.md`: `Id`, `RegisteredSystemId`, `SyncBatchId`, `EntityType`, `EntityId`, `FieldName`, `SpinValue`, `EmassValue`, `ConflictStatus` (enum), `DetectedAt`, `ResolvedAt`, `ResolvedBy`
  - Index on `(RegisteredSystemId, ConflictStatus)` for status dashboard queries

- [ ] T005 [P] Create `EmassWorkflowStatus` DTO in `src/Ato.Copilot.Core/Dtos/Dashboard/EmassWorkflowDtos.cs`:
  - `EmassWorkflowStatus`: `SystemId`, `OverallStatus` (enum: NeverExported/UpToDate/PendingExport/HasConflicts), `LastExportedAt?`, `LastSyncedAt?`, `UnresolvedConflictCount`, `ExportSummary` (per-category counts), `ReadinessStatus`
  - `EmassExportReadinessResult`: `SystemId`, `IsReady`, `Gaps` (list of `ReadinessGap`)
  - `ReadinessGap`: `FieldName`, `Description`, `Severity` (Blocking/Advisory), `FixUrl`
  - `EmassConflictDto`: surface fields for ISSO review UI
  - `ResolveConflictRequest`: `ConflictId`, `Resolution` (KeepSpin/AcceptEmass/Deferred)

- [ ] T006 [P] Create service interfaces in `src/Ato.Copilot.Core/Interfaces/Compliance/`:
  - `IEmassExportReadinessService` — `CheckReadinessAsync(systemId, ct)` → `EmassExportReadinessResult`
  - `IEmassRoundTripSyncService` — `StartSyncAsync(systemId, excelStream, ct)` → `EmassConflict[]`; `ResolveConflictAsync(conflictId, resolution, resolvedBy, ct)`
  - `IEmassWorkflowStatusService` — `GetStatusAsync(systemId, ct)` → `EmassWorkflowStatus`

- [ ] T007 Register new DbSet `EmassConflicts` in `AtoCopilotContext`, configure relationships and indexes in `OnModelCreating`, create EF Core migration `AddEmassConflict`

**Checkpoint**: Entities, DTOs, interfaces, and migration in place. All subsequent phases can begin.

---

## Phase 3 — Export Readiness Service (US2 + US5)

**Goal**: Gate eMASS exports with a pre-flight check that catches `DitprId`/`EmassId` gaps and other required fields before the ISSO attempts a doomed submission.

**Independent Test**: Call readiness check on a system with `DitprId` empty — confirm it returns `IsReady=false` with a Blocking gap for `DitprId`.

- [ ] T008 [US2] [US5] Implement `EmassExportReadinessService` in `src/Ato.Copilot.Agents/Compliance/Services/EmassExportReadinessService.cs`:
  - Check `DitprId` present (Blocking)
  - Check `EmassId` present (Blocking)
  - Check system categorization CIA values populated (Blocking)
  - Check at least one Approved SSP section (Advisory)
  - Check all POA&M items have scheduled completion dates (Advisory)
  - Return `EmassExportReadinessResult` with gap list

- [ ] T009 [P] [US5] Update `EmassExportService` Excel export methods to include `DitprId` and `EmassId` in the correct eMASS template columns; update OSCAL builders to populate `metadata.system-id` array with both identifier schemes

- [ ] T010 [P] [US2] Add unit tests in `tests/Ato.Copilot.Tests.Unit/Compliance/EmassExportReadinessServiceTests.cs`:
  - Test each Blocking gap fires correctly
  - Test Advisory gaps do not block `IsReady=true`
  - Test fully populated system returns `IsReady=true`

**Checkpoint**: Readiness check correctly gates export attempts. `DitprId`/`EmassId` are in all export outputs.

---

## Phase 4 — Round-Trip Sync Service (US3)

**Goal**: Enable safe import of a newer eMASS Excel — diff against SPIN state, persist conflicts, never auto-merge.

**Independent Test**: Upload an eMASS Excel with 3 changed fields; confirm exactly 3 `EmassConflict` records created, SPIN data unchanged, resolve one with `AcceptEmass` and verify SPIN updates only that field.

- [ ] T011 [US3] Implement `EmassRoundTripSyncService` in `src/Ato.Copilot.Agents/Compliance/Services/EmassRoundTripSyncService.cs`:
  - Accept `Stream excelStream`; delegate parsing to existing `EmassImportParser`
  - Diff parsed data against current SPIN state field-by-field (controls, system info, categorization)
  - Write `EmassConflict` records only for changed fields (skip identical fields per FR-013)
  - Warn and require acknowledgment if prior-batch unresolved conflicts exist (FR-012)
  - Return summary: `BatchId`, `ConflictsCreated`, `IdenticalFields`, `SkippedUnresolved`

- [ ] T012 [US3] Implement conflict resolution method in `EmassRoundTripSyncService`:
  - Accept `ResolveConflictRequest`
  - When `AcceptEmass`: update SPIN entity with `EmassValue`, set `ConflictStatus = AcceptEmass`, record `ResolvedAt`/`ResolvedBy`
  - When `KeepSpin` or `Deferred`: update `ConflictStatus` only, no SPIN data change
  - Emit domain event for audit log

- [ ] T013 [P] [US3] Add unit tests in `tests/Ato.Copilot.Tests.Unit/Compliance/EmassRoundTripSyncServiceTests.cs`:
  - Identical fields produce no conflicts
  - Changed fields produce exactly one conflict each
  - SPIN data unchanged after sync run (before resolution)
  - AcceptEmass resolution updates SPIN; KeepSpin does not
  - Second sync with unresolved conflicts triggers warning

**Checkpoint**: Round-trip sync creates conflict records non-destructively. Resolution is explicit-only.

---

## Phase 5 — Workflow Status Service (US4)

**Goal**: Aggregate export history, pending counts, and conflict count into a single DTO that gives the ISSO a clear picture of SPIN→eMASS state.

**Independent Test**: Call `GET /api/systems/{id}/emass/status` on a system with 2 prior exports and 1 unresolved conflict; confirm all fields in response.

- [ ] T014 [US4] Implement `EmassWorkflowStatusService` in `src/Ato.Copilot.Agents/Compliance/Services/EmassWorkflowStatusService.cs`:
  - Query most recent export timestamps from `EmassExportHistory` or `AuthorizationPackage` records
  - Count unresolved `EmassConflict` records for the system
  - Aggregate per-category pending counts (controls not yet in latest export, POA&M items created/updated since last export)
  - Call `IEmassExportReadinessService.CheckReadinessAsync` for summary `ReadinessStatus`
  - Determine `OverallStatus` enum: NeverExported / UpToDate / PendingExport / HasConflicts

- [ ] T015 [P] [US4] Add unit tests in `tests/Ato.Copilot.Tests.Unit/Compliance/EmassWorkflowStatusServiceTests.cs`:
  - NeverExported when no export history
  - HasConflicts takes priority when conflicts exist
  - PendingExport when controls updated after last export

**Checkpoint**: Status DTO accurately reflects system's eMASS workflow state.

---

## Phase 6 — API Endpoints (US2 + US3 + US4)

**Goal**: Expose all new services via REST endpoints; enforce RBAC.

- [ ] T016 [P] [US4] Add `GET /api/systems/{id}/emass/status` to `src/Ato.Copilot.Mcp/Endpoints/EmassWorkflowEndpoints.cs`:
  - Returns `EmassWorkflowStatus` JSON
  - RBAC: ISSO, ISSM, AO

- [ ] T017 [P] [US2] Add `GET /api/systems/{id}/emass/readiness` to `EmassWorkflowEndpoints.cs`:
  - Returns `EmassExportReadinessResult`
  - RBAC: ISSO, ISSM, AO

- [ ] T018 [P] [US3] Add sync endpoints to `EmassWorkflowEndpoints.cs`:
  - `POST /api/systems/{id}/emass/sync` — accepts multipart Excel upload, returns sync summary + conflicts
  - `GET /api/systems/{id}/emass/conflicts` — paginated list of `EmassConflictDto` filtered by status
  - `PUT /api/systems/{id}/emass/conflicts/{conflictId}` — resolve a conflict; RBAC: ISSO, ISSM only

- [ ] T019 [P] Register `EmassWorkflowEndpoints` in `src/Ato.Copilot.Mcp/Program.cs` (or equivalent startup)

**Checkpoint**: All endpoints accessible, RBAC enforced, responses follow API envelope pattern.

---

## Phase 7 — MCP Tools (US2 + US4)

**Goal**: Expose workflow status and readiness check as agent-callable tools so the ISSO can query state via chat.

- [ ] T020 [P] [US4] [US2] Implement `emass_get_workflow_status` and `emass_check_export_readiness` MCP tools in `src/Ato.Copilot.Agents/Compliance/Tools/EmassWorkflowTools.cs`:
  - Follow existing `BaseTool` envelope pattern
  - `emass_get_workflow_status(system_id)` — returns markdown table of export status per category + conflict count
  - `emass_check_export_readiness(system_id)` — returns formatted readiness card; Blocking gaps shown in bold
  - RBAC: ISSO, ISSM, AO for both tools

- [ ] T021 [P] [US4] [US2] Register new tools in the MCP tool registry; update `docs/architecture/agent-tool-catalog.md` with parameter tables, response schemas, RBAC, and example invocations

**Checkpoint**: Both MCP tools callable from dashboard chat, Teams, and VS Code.

---

## Phase 8 — Dashboard Status Page (US4)

**Goal**: Give the ISSO a visual eMASS workflow status view and conflict resolution UI.

- [ ] T022 [P] [US4] Create `EmassStatusPage` React component at `src/Ato.Copilot.Dashboard/src/pages/EmassStatus.tsx`:
  - Fetch `GET /api/systems/{id}/emass/status` on mount
  - Display: overall status badge, last exported / last synced timestamps, per-category counts (exported / pending), unresolved conflict count with link to conflict list
  - Show readiness warning banner when `IsReady=false`

- [ ] T023 [P] [US3] Create `EmassConflictList` React component at `src/Ato.Copilot.Dashboard/src/components/EmassConflictList.tsx`:
  - Display field-level conflicts with `SpinValue` vs `EmassValue` side-by-side
  - Resolve buttons: "Keep SPIN" / "Accept eMASS" / "Defer" per conflict row
  - Batch resolve with confirmation dialog for "Accept All eMASS" action

- [ ] T024 [P] Add `emass-status.ts` API client in `src/Ato.Copilot.Dashboard/src/api/emass-status.ts`:
  - `getEmassStatus(systemId)` → `EmassWorkflowStatus`
  - `getEmassReadiness(systemId)` → `EmassExportReadinessResult`
  - `uploadEmassSync(systemId, file)` → sync summary
  - `getEmassConflicts(systemId, params)` → paginated `EmassConflictDto[]`
  - `resolveConflict(systemId, conflictId, resolution)` → `EmassConflictDto`

- [ ] T025 Register new route `/systems/:id/emass/status` in dashboard router

**Checkpoint**: ISSO can view eMASS workflow status and resolve conflicts from the dashboard.

---

## Phase 9 — Polish & Cross-Cutting

- [ ] T026 [P] Structured Serilog logging for sync runs: log batch ID, conflicts created, elapsed time
- [ ] T027 [P] Update `docs/guides/emass-workflow.md` (new guide) covering: status page, readiness check, round-trip sync workflow, conflict resolution
- [ ] T028 [P] Update `docs/getting-started/isso.md` with eMASS workflow sync section
- [ ] T029 Run `dotnet test` end-to-end; verify all Feature 041 tests still pass; verify NFR-001 sync performance on mock data (400 controls, 150 POA&M items < 30 s)
- [ ] T030 Code review: CancellationToken propagation, RBAC enforcement, API envelope consistency, 80% test coverage

---

## Dependencies & Execution Order

```
Phase 1 (OSCAL Upgrade) ──────────────────────────────────► Ship immediately (independent)

Phase 2 (Foundation) ──────────────────────────────────────► BLOCKS Phases 3–7
                         │
        ┌────────────────┼────────────────┬────────────────┐
        ▼                ▼                ▼                ▼
Phase 3 (Readiness)  Phase 4 (Sync)  Phase 5 (Status)  Phase 6 (API) ◄─ depends on 3+4+5
                                                            │
                                                            ▼
                                                       Phase 7 (MCP Tools)
                                                            │
                                                            ▼
                                                       Phase 8 (Dashboard)
                                                            │
                                                            ▼
                                                       Phase 9 (Polish)
```

## Parallel Opportunities

After Phase 2 completes, these can run simultaneously:
- T008–T010 (Readiness service + tests)
- T011–T013 (Sync service + tests)
- T014–T015 (Status service + tests)

Within Phase 6 (API), all four endpoint tasks are parallel (same file but additive methods).

Phase 1 (OSCAL Upgrade) is fully independent — merge to `main` as a hotfix before the rest of the feature lands.
