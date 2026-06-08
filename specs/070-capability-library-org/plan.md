# Plan — 070: Capability Library (Org Scope)

---

## Overview

5-phase plan. Each phase has a hard checkpoint: the build must pass (`dotnet build`) and the
relevant tests must pass before the next phase begins. All phases target the same branch
`070-capability-library-org`.

---

## Phase 1 — Backend Foundation (Sprint 1, Days 1–2)

**Goal:** Data model in place, migration applied, stub endpoints registered.

**Tasks:** T001, T002, T003, T004, T005, T006

**Work:**
1. Create `CapabilitySubscription.cs` entity and `CapabilitySubscriptionStatus.cs` enum
2. Register `DbSet` and EF config in `AtoCopilotContext.cs`
3. Run `dotnet ef migrations add Feature225_CapabilitySubscription`
4. Create `CapabilityLibraryEndpoints.cs` with stub route handlers (all return `501 Not Implemented`)
5. Register `MapCapabilityLibraryEndpoints()` in startup

**Checkpoint 1:** `dotnet build Ato.Copilot.sln` passes. Migration file exists and
`dotnet ef database update` succeeds on SQLite dev database. Route stubs registered
(visible in Swagger/OpenAPI at `GET /api/capability-library`).

---

## Phase 2 — Backend Logic (Sprint 1, Days 3–5)

**Goal:** All 4 endpoints functional with correct auth gates and audit logging.

**Tasks:** T007, T008, T009, T010, T011, T012

**Work:**
1. Implement `ListCapabilityLibraryAsync` — EF query with join, filters, pagination, projection
2. Implement `SubscribeCapabilityAsync` — idempotency check, insert, audit log, async chain trigger
3. Implement `ListSubscriptionsAsync` — tenant-scoped Active subscriptions with capability join
4. Implement `UnsubscribeCapabilityAsync` — soft-delete, audit log, async chain trigger
5. Register `IssoOrIssm` authorization policy in DI
6. Define `CapabilityLibraryItemDto` and `CapabilitySubscriptionDto` C# records

**Checkpoint 2:** All 4 endpoints return correct HTTP status codes when called via `curl` or
Swagger. `dotnet test tests/Ato.Copilot.Tests.Integration` passes for new test file T022.
Auth gates verified manually: ISSO → 201 on subscribe; SCA role → 403.

---

## Phase 3 — Frontend API Client & List Page (Sprint 2, Days 1–3)

**Goal:** `CapabilityLibraryPage.tsx` renders real data from the Phase 2 backend.

**Tasks:** T013, T014, T015, T016

**Work:**
1. Create `src/features/capability-library/api.ts` with typed Axios client
2. Create `CapabilityLibraryPage.tsx` — paginated grid, filters, subscribe toggle
3. Register `/capability-library` route in `App.tsx`
4. Add sidebar nav entry in `SystemLayout.tsx`

**Checkpoint 3:** Navigate to `/capability-library` in the dev browser. Grid renders Published
capabilities. Filter dropdowns work. Pagination next/prev works. Subscribe button visible for
ISSO role; hidden for SCA/AO (switchable via RoleSwitcher). MSW handler T023 passes in Vitest.

---

## Phase 4 — Frontend Detail Page (Sprint 2, Days 4–5)

**Goal:** `CapabilityDetailPage.tsx` with full control coverage table and subscribe/unsubscribe flow.

**Tasks:** T017, T018, T019, T020, T021

**Work:**
1. Create `CapabilityDetailPage.tsx` — control table, subscription action panel, unsubscribe
   confirmation dialog
2. Register `/capability-library/:id` route in `App.tsx`
3. Verify role switcher includes ISSO/ISSM
4. Apply subscribe-button visibility gate (`canSubscribe` check)
5. Verify `IssoOrIssm` policy wired in DI

**Checkpoint 4:** Clicking a capability card navigates to detail page. Control coverage table
renders. Subscribe → success toast and button state change. Unsubscribe confirmation dialog
appears → confirm → 204 → button reverts. SCA role: subscribe button absent.

---

## Phase 5 — Tests, Docs & DoD (Sprint 3, Day 1)

**Goal:** Full test pass, spec updated, DoD checklist complete.

**Tasks:** T022, T023, T024, T025, T026

**Work:**
1. Complete integration test coverage (T022) — all 14 backend test cases passing
2. Complete MSW handler registration (T023)
3. Complete frontend unit tests (T024)
4. Docs update (T025)
5. Mark spec `Status: Implemented` (T026)
6. Final `dotnet test Ato.Copilot.sln` — zero failures
7. PR created, linked to #225, DoD checklist verified

**Checkpoint 5 (Final):** All DoD items checked. `dotnet test` passes. Vitest passes.
Build passes. PR approved.

---

## Dependencies

| Dependency | Required By | Risk |
|------------|-------------|------|
| `CspInheritedCapability` and `CspInheritedComponent` entities stable | Phase 1 | Low — entities mature, no active changes |
| `AuditLogEntry` service/pattern available | Phase 2 (T008, T010) | Low — AuditLogs DbSet present in context |
| Epic #223 inherited controls chain | Phase 2 (T008, T010) | Medium — fire-and-forget; stub if not yet wired |
| `RequireAuth` component pattern in React | Phase 3 (T015) | Low — pattern already used by all routes |
| MSAL auth interceptor pattern | Phase 3 (T013) | Low — pattern in `csp-inherited-components/api.ts` |

## Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Epic #223 SSP chain not yet available | Medium | Low | Log warning, skip chain call; Epic #223 wires it later |
| `IssoOrIssm` policy conflicts with existing auth policies | Low | Medium | Search for existing `AddAuthorization` call first; do not duplicate |
| Large catalog performance (>5k capabilities) | Low | Medium | Compound index in migration; add `.AsNoTracking()` to read queries |
| Cross-boundary FK causes EF migration complications | Low | Low | FK with `DeleteBehavior.Restrict` is safe; does not affect tenant filter |
