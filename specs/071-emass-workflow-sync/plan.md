# Implementation Plan: eMASS Workflow Sync & OSCAL 1.1.2 Upgrade

**Feature**: 071-emass-workflow-sync | **Issue**: #59 | **Wave**: 9

## Overview

Feature 071 is a targeted extension of Feature 041 (eMASS Authorization Package Export). It fixes a P0 OSCAL version defect and adds round-trip workflow capabilities so SPIN can serve as the ISSO's primary ATO workspace.

**Delivery strategy**: Phase 1 (OSCAL upgrade) ships as a hotfix immediately. Phases 2–9 ship as the full feature.

---

## Milestones

| Milestone | Phases | Description | Target |
|-----------|--------|-------------|--------|
| M0 — Hotfix | Phase 1 | OSCAL 1.1.2 upgrade — unblocks eMASS submissions today | Sprint 1, Day 3 |
| M1 — Foundation | Phase 2 | Entities, DTOs, interfaces, migration | Sprint 1, Day 5 |
| M2 — Services | Phases 3–5 | Readiness, sync, status services + tests | Sprint 2 |
| M3 — API + Tools | Phases 6–7 | REST endpoints + MCP tools | Sprint 2 |
| M4 — Dashboard | Phase 8 | Status page + conflict list UI | Sprint 3 |
| M5 — Done | Phase 9 | Polish, docs, full regression | Sprint 3 |

---

## Phase Breakdown

### Phase 1 — OSCAL 1.1.2 Upgrade (T001–T003)

**Owner**: Backend developer  
**Files touched**:
- `src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs` — upgrade both builders
- `tests/Ato.Copilot.Tests.Unit/Compliance/EmassExportServiceTests.cs` — new + regression tests

**Risk**: Low. Changes are confined to two methods in one file. Feature 041 regression tests serve as guardrails.

**Definition of Done**: Both builders emit `"oscal-version": "1.1.2"`. All Feature 041 tests pass. PR merged to `main` as hotfix.

---

### Phase 2 — Foundation (T004–T007)

**Owner**: Backend developer  
**Files touched**:
- `src/Ato.Copilot.Core/Models/Compliance/EmassConflict.cs` (new)
- `src/Ato.Copilot.Core/Dtos/Dashboard/EmassWorkflowDtos.cs` (new)
- `src/Ato.Copilot.Core/Interfaces/Compliance/IEmassExportReadinessService.cs` (new)
- `src/Ato.Copilot.Core/Interfaces/Compliance/IEmassRoundTripSyncService.cs` (new)
- `src/Ato.Copilot.Core/Interfaces/Compliance/IEmassWorkflowStatusService.cs` (new)
- `AtoCopilotContext.cs` — new DbSet + config
- EF Core migration `AddEmassConflict`

**Risk**: Medium. EF migrations must not conflict with Feature 041 migrations. Coordinate migration ordering with Feature 041 team.

---

### Phases 3–5 — Services (T008–T015)

**Parallelizable after Phase 2.**

| Phase | Service | Key Risk |
|-------|---------|----------|
| 3 — Readiness | `EmassExportReadinessService` | Must use correct DB column names for `DitprId`/`EmassId` |
| 4 — Sync | `EmassRoundTripSyncService` | Parser reuse — do not modify `EmassImportParser` directly |
| 5 — Status | `EmassWorkflowStatusService` | Depends on export history query design from Feature 041 |

**Implementation note for Phase 4**: `EmassRoundTripSyncService` MUST reuse `EmassImportParser` without modifying it. If the parser needs new output fields, create a wrapper or adapter — do not change the parser that the onboarding flow depends on.

---

### Phases 6–7 — API + MCP Tools (T016–T021)

**Parallelizable after Phase 2 (interfaces defined).**

All new endpoints go in a new `EmassWorkflowEndpoints.cs` file to avoid merge conflicts with Feature 041's `PackageEndpoints.cs`.

MCP tools follow the `BaseTool` envelope pattern already established in `EmassExportTools.cs`.

---

### Phase 8 — Dashboard (T022–T025)

**Parallelizable after Phase 6 (API contracts exist).**

The status page is a new route (`/systems/:id/emass/status`) — no changes to existing pages are required. The conflict list is a new component used only by the status page.

---

### Phase 9 — Polish (T026–T030)

- Serilog structured logging for sync runs (batch ID, duration, conflict count)
- New guide: `docs/guides/emass-workflow.md`
- Update `docs/getting-started/isso.md`
- Full `dotnet test` regression including Feature 041
- Performance test: sync 400 controls + 150 POA&M items must complete < 30 s

---

## Risks & Mitigations

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| OSCAL 1.1.2 schema diff introduces unexpected breaking changes | Medium | High | Compare OSCAL 1.0.6 vs 1.1.2 changelogs before coding T001/T002; run against bundled schemas |
| EF migration conflicts with Feature 041 | Medium | Medium | Coordinate migration baseline; apply Feature 041 migrations first |
| `EmassImportParser` does not expose enough data for field-level diff | Low | Medium | Add a thin adapter class rather than modifying the parser |
| Round-trip sync performance > 30 s for large systems | Low | Medium | Use batched DB inserts for conflict records; avoid N+1 queries |
| Dashboard status page adds latency to ISSO workflow | Low | Low | Status endpoint is read-only, responds < 500 ms per NFR-002 |

---

## Testing Strategy

| Phase | Test Type | Coverage Target |
|-------|-----------|----------------|
| Phase 1 | Unit | 100% of upgraded OSCAL builder methods |
| Phase 2 | None — pure model/interface definitions | N/A |
| Phases 3–5 | Unit | 80% per Constitution Principle III |
| Phases 6–7 | Integration | Happy path + RBAC rejection for each endpoint |
| Phase 8 | Manual / E2E | ISSO workflow walkthrough in staging |
| Phase 9 | Regression | All Feature 041 tests + new Feature 071 tests must pass |

---

## Definition of Done

- [ ] All `tasks.md` tasks marked `[x]`
- [ ] `dotnet test` passes with no regressions against Feature 041
- [ ] OSCAL exports verified 1.1.2 with schema validation
- [ ] Sync creates conflicts without modifying SPIN data
- [ ] Status endpoint responds < 500 ms
- [ ] MCP tools callable from dashboard chat
- [ ] `docs/guides/emass-workflow.md` complete
- [ ] PR approved by 1 reviewer
- [ ] Issue #59 linked and acceptance criteria verified
