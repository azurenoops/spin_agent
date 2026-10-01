# Plan — 070: Capability Library (Org Scope)

---

## Overview

### AI first-pass investigation and design gate (2026-10-01)

Verified gaps and reuse points:

1. The existing confirmation table retains accepted source reviews, not proposed
   drafts. Review notes on that table cannot safely hold unaccepted generated
   content. The panel's current memory-only fields do not meet durable draft,
   edit-history or refresh-comparison requirements.
2. `SystemEnvironmentService.PublishedDuties.cs` already validates an exact
   offering/hosting revision against an accepted publication context and immutable
   capability release. `ProviderCapabilityRelease.SnapshotJson.DutiesJson`
   contains explicit Provider/Shared/Customer control allocations. Reuse this
   resolver; do not infer splits from `MappedNistControlIds`.
3. Published-duty DTOs carry control allocations, release IDs/revisions/hashes and
   capability descriptions. They do not promise control-specific customer-duty
   prose. Missing prose must remain a source gap, not a copied generic assertion.
4. `SystemProviderScope` distinguishes assignment/hosting revisions, relationship
   review and published duties. Capability adoption binds subscriptions to actual
   assignments; a legacy subscription may lack that binding.
5. `NarrativeGroundingService` already scopes published references, system
   capabilities, boundary/component assignments, evidence and responsibilities.
   Reuse authorized grounding where applicable, while including existing narrative
   text and surfacing missing control-specific context explicitly.
6. `NarrativeTemplateService.GenerateGroundedDraftAsync` demonstrates the configured
   `IChatClient`/JSON-response pattern with explicit unavailable/invalid-output
   errors. Responsibility suggestions require their own bounded output contract
   and deterministic published-split enforcement, not a narrative renamed as a
   responsibility record.
7. Existing provider confirmation enforces ISSM/ISSO, revision pins and transactional
   reconciliation. System-only designation uses `BaselineService.SetInheritanceAsync`;
   inspect and isolate its implementation-status propagation before draft
   acceptance. Do not bypass it by silently writing a second inheritance model.

Proposed implementation increments:

1. Specify versioned responsibility-draft persistence, per-field source references,
   human edits, generation attempts and stale detection. Keep it distinct from
   confirmation, not a new approval-role system.
2. Failing-first source resolver/generator tests: explicit split precedence,
   conflicting/partial releases, absent provider scope, insufficient records,
   invalid citations, unavailable AI and generation failures.
3. Failing-first persistence/HTTP tests: reader versus ISSM/ISSO authority, tenant
   and system isolation, source and draft concurrency, preserved accepted records,
   stored generation/source history and existing confirmation integration.
4. Panel integration: existing saved draft first, editable source-backed fields,
   prepare/refresh status, per-field provenance and explicit comparison/apply.
5. Verify matrix, CRM, SSP and eMASS preparation outputs after authorized acceptance;
   run required checks and provide a local walkthrough before acceptance.

Design decision raised: add durable proposed drafts and a narrowly scoped,
status-preserving system-only confirmation path, rather than pretending that
memory-only fields or accepted confirmation rows are drafts. The user was not
available to answer the design question. No new schema or production generation
path has been implemented at this investigation checkpoint.

Separate open defect: the live narrative workspace request started at 14:02:22 UTC
while supporting library/access/proposal requests completed promptly. The running
API image is `auto-catalog-20261001-e98474e9`. A large-catalog streaming reader is
present in another session's local changes; no live speedup or deployment is
claimed from its presence. Continue measuring the actual request before diagnosis.

### Task-oriented review panel increment (2026-10-01)

Verified before implementation:

- The current session branch has the older grouped review page. The worktree
  `requirement-coverage-enhancement-nav`, serving 5197, already contains the matrix
  and right-side `SetupDialog`. Improve that worktree in place; preserve all its
  unrelated edits. No GitHub write or push is authorized by this increment.
- `CapabilityResponsibilityReview.tsx` clears input on allocation switches and
  remounts on every preview generation. Its 409 handler closes the panel and
  clears edits. Save errors appear both behind and inside the panel. These are
  the UX failure paths to correct, not reasons to replace the matrix.
- The retained sequential backend log identifies the screenshot request as PUT
  confirmation, HTTP 500, at 12:53:46 UTC, failing in `ApplyAsync` during save with
  SQL 2628. The original response body is not retained and cannot be asserted.
  Isolated SQL tests identified `SubscriptionReconcile` (21 characters) versus
  `InheritanceAuditEntries.ChangeSource` (20). The 32-character model/startup
  upgrade and local database repair are already present; do not repeat a live
  confirmation or truncate user/audit data.
- `CapabilitySubscriptionEndpoints` exposes read, confirm, reconcile and dispatch,
  not draft/proposal persistence. `CapabilityResponsibilityService` enforces
  effective ISSM/ISSO write access, system/tenant scope, serializable mutation,
  baseline/source/review pins and retained confirmations. ReviewNotes and both
  acknowledgement fields already exist in the backend. Reuse these optional
  request/response fields for the panel, with no new approval lifecycle.
- Existing `Confirm_PersistsOnlyApplicableControls_AndPreservesNarrativeStatus`
  verifies designation, CRM and generated SSP while preserving approved narrative
  and Planned implementation status. Confirmation is not package export/submission.
- Source display contains names/descriptions/mappings and a redacted artifact
  reference, not verified system scope, retention or evidence sufficiency.
  Show those limits honestly rather than copying fictional mock facts.

Implementation order:

1. Add failing tests for plain-language choices, conditional fields, preserved
   drafts, explicit summary/confirmation, failed load/save and refresh conflicts.
2. Refine only the existing drawer; reuse SetupDialog (optional fixed footer),
   scoped API client, source parsers and review notes. Keep saved state separate
   from local input, and require a new acknowledgement after refresh.
3. Run targeted UI/API and real SQL/responsibility/authorization tests, then the
   requested full `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln`.
   Dashboard has no typecheck script: run `npx tsc --noEmit` and `npm run build`.
   Record failures/warnings rather than claiming an unverified clean baseline.
4. Verify the drawer in a synthetic browser fixture (including keyboard/mobile),
   and provide AU-11 manual steps for 5197 before user acceptance. Do not perform
   customer confirmations in the live system on the user's behalf.

Constitution check: extend existing #957 requirements (issue read verified OPEN),
no new feature/approval model, no auth bypass, no source invention, TDD/AAA,
scoped TypeScript checks and document traceability required. No speculative
abstraction. Full modified-path coverage and user acceptance must be reported
honestly; neither is presumed. Roll back only this increment's UI/API/doc hunks,
not the independent audit-column repair or other pending worktree changes.

Verification recorded on 2026-10-01:

- Red: nine UI/API expectations failed before the panel implementation; the
  returned-review-evidence guard separately failed before its implementation.
- Green: 81 Dashboard tests; 22 isolated Chromium browser cases at 1440px/390px,
  including axe WCAG A/AA checks, keyboard/focus, sticky actions, stale conflicts,
  failed saves, scoped links and narrative impact navigation.
- Targeted backend: 1 unit + 82 integration passed with Docker required and no
  skips. Includes real audit-column SQL tests, direct authorization/tenant checks,
  and three new allocation-to-CRM/SSP/eMASS-controls preparation tests. Both full
  run failures passed their isolated reruns; this does not make the full run green.
- Full `dotnet test Ato.Copilot.sln`: 7,965 unit passed / 1 failed; 1,767 integration
  passed / 1 failed / 20 skipped. Failures were narrative import cancellation
  (`ConcurrentImportsTranslateDuplicateLineageToExplicitConflict`) and scan-job
  temporary-file cleanup (`Worker_DispatchesCklXccdfAndNessusJobsToMatchingImporters`).
  Do not suppress, change or claim to have fixed these unrelated failures.
- Final `dotnet build Ato.Copilot.sln`: passed, 0 errors / 105 warnings on rebuild.
  Earlier incremental build reported 0 warnings. The zero-warning gate is not met.
  The new panel contract test compiled and ran successfully.
- `npx tsc --noEmit` and Dashboard production build passed. Vite still reports its
  oversized-chunk warning. No warning limit was raised or suppressed.
- Touched frontend aggregate coverage: 98.73% statements/lines, 90.38% branches,
  100% functions. New panel: 100% statements/lines/functions, 91.97% branches.
  Do not represent this as 100% branch/path coverage.
- Port 5197 serves the new panel module. A read-only call through the shared live
  browser verified the running backend supplies the review-notes/acknowledgement
  fields and denies confirmation to its current CSP oversight user. No live
  responsibility confirmation was performed.
- Another session added an initially uncompilable catalog-reader test while the
  full run was in progress. Its helper subsequently appeared; the targeted run
  then passed without editing that session's files. Changes remain uncommitted
  and unpushed in the serving worktree, not in the Overview branch.

Remaining acceptance gates: human AU-11 review, full-suite/zero-warning criteria
and complete modified-path coverage. This is a verified local UI increment, not
release acceptance, an approved SSP change or an eMASS submission.

Issue #957 follow-up: explicit system-scoped allocation confirmation supersedes the
original automatic-inheritance assumption. See
[the handoff contract](contracts/responsibility-handoff.md) for current API, persistence,
concurrency, role, provider-source event and narrative outbox boundaries. The dedicated
reconciler is necessary because the legacy designation writer also changes narrative
implementation status and cannot safely own subscription overlap/unsubscribe behavior.
No organization-default derivation is introduced.

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
