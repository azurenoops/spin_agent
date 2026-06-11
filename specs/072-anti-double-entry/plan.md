# Plan — 072: Anti-Double-Entry (SPIN/eMASS Sync Status)

---

## Overview

4-phase plan. Each phase has a hard checkpoint: the build must pass (`dotnet build`) and
relevant tests must pass before the next phase begins. All phases target the same branch
`072-anti-double-entry`.

---

## Phase 1 — Backend Foundation (Sprint 1, Days 1–2)

**Goal:** Data model in place, migration applied, stub endpoints registered.

**Tasks:** T001, T002, T003, T004, T005, T010, T011

**Work:**
1. Create `EmassFieldSnapshot.cs` entity and `EmassTrackedField.cs` constants
2. Add `DitprId` column to `RegisteredSystem`
3. Register `DbSet<EmassFieldSnapshot>` and EF config in `AtoCopilotContext.cs`
4. Run `dotnet ef migrations add Feature072_EmassFieldSnapshot`
5. Create `EmassSyncEndpoints.cs` with stub route handlers (all return `501 Not Implemented`)
6. Register `MapEmassSyncEndpoints()` in startup

**Checkpoint 1:** `dotnet build Ato.Copilot.sln` passes. Migration file exists and
`dotnet ef database update` succeeds on SQLite dev database. Stub endpoints visible in Swagger
at `GET /api/systems/{systemId}/emass-sync-status`.

---

## Phase 2 — Backend Logic: Pre-Population & Divergence (Sprint 1, Days 3–5)

**Goal:** `EmassCommitJobHandler` creates snapshots; divergence check runs at entity save;
both sync-status endpoints functional.

**Tasks:** T006, T007, T008, T009, T012, T013, T014

**Work:**
1. Define `IEmassFieldSyncService` interface
2. Implement `EmassFieldSyncService` — upsert, divergence check, sync status computation
3. Extend `EmassCommitJobHandler` to create snapshots post-commit (non-destructive pre-population)
4. Wire `CheckDivergenceAsync` into `RegisteredSystem` / `SecurityCategorization` / `ControlBaseline` saves
5. Implement `GET /api/systems/{id}/emass-sync-status` — delegates to service
6. Implement `POST /api/systems/{id}/emass-sync-status/dismiss`
7. Define DTOs: `EmassSystemSyncStatusDto`, `EmassFieldStatusDto`

**Checkpoint 2:** Run integration tests T021. After an eMASS import commit:
- `EmassFieldSnapshot` rows exist for all present fields
- `GET /api/systems/{id}/emass-sync-status` returns `InSync`
- Edit `RegisteredSystem.Name` → status becomes `Diverged`
- ISSO role: 200; SCA role: 403

---

## Phase 3 — Frontend: Sync Badge & Drawer (Sprint 2, Days 1–3)

**Goal:** `EmassSyncBadge` and `SyncStatusDrawer` render in System Overview.

**Tasks:** T015, T016, T017, T018

**Work:**
1. Create `src/features/emass-sync/api.ts` with typed Axios client
2. Create `EmassSyncBadge.tsx` — green/yellow/red pill badge
3. Create `SyncStatusDrawer.tsx` — per-field drawer panel
4. Integrate badge into System Overview page header

**Checkpoint 3:** Navigate to a system overview in the dev browser. Badge renders Green
(after a mock eMASS import). Click badge → drawer opens with field rows. Switch to SCA role
→ badge hidden. MSW handler T022 passes in Vitest.

---

## Phase 4 — Frontend: Inline Field Banner & Tests (Sprint 2, Days 4–5)

**Goal:** `EmassFieldBanner` renders on tracked fields; full test coverage; DoD complete.

**Tasks:** T019, T020, T021, T022, T023, T024, T025

**Work:**
1. Create `EmassFieldBanner.tsx` — origin/divergence inline notice
2. Integrate banner into system registration / edit forms for all 5 tracked fields
3. Complete integration tests (T021) — all 12 backend cases passing
4. Register MSW handlers (T022)
5. Complete frontend unit tests (T023)
6. Docs update (T024)
7. Mark spec `Status: Implemented` (T025)
8. Final `dotnet test Ato.Copilot.sln` — zero failures

**Checkpoint 4 (Final):** All DoD items checked. `dotnet test` passes. Vitest passes.
Build passes. PR approved and linked to #60.

---

## Dependencies

| Dependency | Required By | Risk |
|------------|-------------|------|
| `EmassImportSession` entity stable | Phase 1 (T001) | Low — entity mature, FK only |
| `EmassCommitJobHandler` pipeline available | Phase 2 (T006) | Low — existing, extensible |
| `RegisteredSystem`, `SecurityCategorization`, `ControlBaseline` save points identifiable | Phase 2 (T009) | Low-Medium — search required |
| Feature 071 (Issue #59) round-trip sync | Not required — complementary | None — this feature is standalone |
| `MSAL auth interceptor` pattern | Phase 3 (T015) | Low — pattern in existing API clients |
| Drawer/modal pattern in dashboard | Phase 3 (T017) | Low — search existing components |

## Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| `EmassCommitJobHandler` commit phase structure unclear | Medium | Medium | Grep for `// Phase` comments; read handler top-to-bottom before T006 |
| Save points for divergence hook not obvious | Medium | Low | Grep `SaveChangesAsync` in `SystemProfileService`, `CategorizationService`, `BaselineService` |
| `UserPreference` entity already exists | Low | Low | Search before creating; reuse existing pattern |
| eMASS-to-SPIN field mapping ambiguity (e.g., categorization level string normalization) | Medium | Low | See `research.md R3`; use ToUpperInvariant() + Enum.TryParse as fallback |
| Feature 071 (#59) spec changes overlapping entity | Low | Low | These features own different entities; no shared mutations |
