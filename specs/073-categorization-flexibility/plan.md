# Plan — 071: Categorization Flexibility & Service-Branch Overlays

---

## Overview

7-phase plan delivering Feature 053 (Issue #62). Each phase has a hard checkpoint: the build
must pass (`dotnet build`) and relevant tests must pass before the next phase begins.
All phases target branch `071-categorization-flexibility`.

Estimated total: **4 sprints (8 weeks)** for full feature. Phases 1–4 form the P1 MVP that
unblocks authorization package correctness for Army, Navy, and Air Force customers.
Phases 5–7 complete P2 scope (SSP export integration, frontend, overlay versioning).

---

## Phase 1 — Data Model & Migration (Sprint 1, Days 1–3)

**Goal:** New entities in place, migration applied, seed data loading at startup.

**Tasks:** T001, T002, T003, T004, T005, T006, T007

**Work:**
1. Create `CategorizationOverlay.cs`, `CategorizationOverlayEnums.cs`
2. Create `CategorizationAuditEntry.cs`, `CategorizationChangeType.cs`
3. Extend `SecurityCategorization` in `RmfModels.cs` (+3 columns)
4. Register DbSets; configure EF indexes and FK in `AtoCopilotContext.OnModelCreating`
5. Run migration; verify SQL matches `data-model.md §5`
6. Create `data/overlays/builtin/` JSON seed files for 10 built-in overlays
7. Implement `CategorizationOverlaySeedService` (startup upsert, idempotent)

**Checkpoint 1:**
- `dotnet build Ato.Copilot.sln` → 0 errors
- `dotnet ef database update` succeeds; `CategorizationOverlays` and
  `CategorizationAuditEntries` tables exist with correct schema
- Startup log shows "Loaded N built-in categorization overlays" (N = 10)

---

## Phase 2 — Backend Service Layer (Sprint 1, Days 4–5 + Sprint 2, Day 1)

**Goal:** Business logic for overlay application, stack resolution, validation, and audit writing.

**Tasks:** T008, T009, T010, T011

**Work:**
1. Define `ICategorizationOverlayService` interface (6 methods)
2. Implement `CategorizationOverlayService`:
   - `ListCatalogAsync`: join built-in + org overlays, apply DoD gate, group by ServiceBranch
   - `ApplyOverlayAsync`: validate → merge SP 800-60 overrides → recompute IL → write audit entry
   - `RemoveOverlayAsync`: unpin overlay from `OverlayIds` JSON → recompute IL → write audit entry
   - `ResolveOverlayStackAsync`: deterministic stack resolution (priority order, max-wins conflicts)
   - `GetAuditTrailAsync`: ordered DESC by `ChangedAt`, with overlay name snapshot
   - `ValidateOverlayAsync`: validation rules (FIPS lock, floor, dimension refs, DoD gate)
3. Implement `CategorizationOverlayValidator` (shared by service + admin endpoints)
4. Implement `CategorizationAoNotificationJob` background job

**Checkpoint 2:**
- Unit tests T025, T026 pass
- `ICategorizationOverlayService` can be injected in a console test and resolves the
  built-in overlay catalog (10 items)
- Validation correctly rejects a synthetic overlay that tries to lower a FIPS 199 value

---

## Phase 3 — HTTP API Endpoints (Sprint 2, Days 2–5)

**Goal:** All 5 read/write overlay endpoints functional with correct auth gates and audit logging.

**Tasks:** T012, T013, T014

**Work:**
1. Create `CategorizationOverlayEndpoints.cs` (5 routes) + register at startup
2. Create `CategorizationOverlayAdminEndpoints.cs` (5 org-admin routes)
3. Wire auth policies: ISSO/ISSM for mutating endpoints; any auth for read endpoints
4. Define DTOs: `CategorizationOverlayDto`, `OverlayStackDto`, `CategorizationAuditEntryDto`,
   `ApplyOverlayRequest`, `ApplyOverlayResult`

**Checkpoint 3:**
- `dotnet build` passes; Swagger/OpenAPI lists all 10 new routes
- `GET /api/categorization-overlays` returns 10 built-in overlays in < 200 ms (curl verify)
- `POST /api/systems/{id}/categorization/overlays` with `army-rmf:v3` body:
  - Returns 200 with updated `ImpactLevel` and `overlayStack`
  - `CategorizationAuditEntries` table has 1 new row
- ISSO → 200; SCA → 403 for POST (verified manually)
- Integration tests T027, T028 pass

---

## Phase 4 — MCP Agent Tools (Sprint 3, Days 1–3)

**Goal:** Agent tools allowing Cyborg/Clarami to apply, remove, audit, and list overlays via MCP.

**Tasks:** T015, T016, T017, T018, T019

**Work:**
1. Add 4 new tool classes in `CategorizationTools.cs`:
   - `ApplyCategorizationOverlayTool` (`compliance_apply_categorization_overlay`)
   - `RemoveCategorizationOverlayTool` (`compliance_remove_categorization_overlay`)
   - `GetCategorizationAuditTool` (`compliance_get_categorization_audit`)
   - `ListCategorizationOverlaysTool` (`compliance_list_categorization_overlays`)
2. Update `CategorizeSystemTool.Description` to cross-reference overlay tools
3. Register all 4 new tools in MCP tool registry

**Checkpoint 4:**
- MCP tool list includes all 4 new tools
- Agent conversation: "Apply the Army RMF overlay to system ACME-001" → tool invokes,
  returns updated IL and audit entry ID
- Agent conversation: "Show categorization audit for system ACME-001" → tool returns
  ordered audit trail with overlay names

---

## Phase 5 — SSP Export Integration (Sprint 3, Days 4–5)

**Goal:** SSP §10 categorization section auto-generates rationale from overlay trace.

**Tasks:** T020

**Work:**
1. Locate SSP §10 export logic (search `SspExport.cs` / `SspAuthoringTools.cs`)
2. Inject `ICategorizationOverlayService.ResolveOverlayStackAsync` call
3. Generate prose: "System categorized per FIPS 199 high-water-mark (C/I/A).
   The following overlays were applied: [Army RMF KS v3.2 — added MAC dimension, raised
   Personnel Management C/I to Moderate]. Final Impact Level: Moderate."
4. Ensure retired overlay references show `[RetiredFromCatalog]` marker in prose

**Checkpoint 5:**
- Export test: system with 1 overlay applied → SSP §10 body includes overlay rationale
- System with no overlays → SSP §10 body contains only FIPS 199 rationale (no regression)

---

## Phase 6 — Frontend (Sprint 4)

**Goal:** Overlay picker in categorization wizard; methodology drilldown for AO; upgrade notice.

**Tasks:** T021, T022, T023, T024

**Dependency:** UX/design handoff required before sprint starts. Phase 3 API must be stable.

**Checkpoint 6:**
- Wizard shows overlay picker with 10 built-in overlays grouped by branch
- Picking "Army RMF KS" adds MAC + Mission Impact questions to wizard
- AO drilldown panel opens from `ImpactLevel` badge with full resolution chain
- Upgrade notice appears on system page when a newer catalog version is available

---

## Phase 7 — Tests & Hardening (Sprint 4)

**Goal:** Full test coverage for all phases; integration tests green in CI.

**Tasks:** T025, T026, T027, T028, T029

**Checkpoint 7 (Release Gate):**
- `dotnet test Ato.Copilot.sln` → all tests pass
- CI pipeline green on branch `071-categorization-flexibility`
- Requirements checklist (`checklists/requirements.md`) verified 100%
- PR review approved by 2 reviewers; merged to `main`
