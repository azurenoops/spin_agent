# Tasks: CSP-Inherited Capability Lifecycle (Vetting + Reparent)

**Feature**: 050-csp-capability-lifecycle
**Branch**: `050-csp-capability-lifecycle`
**Input artifacts**: [plan.md](./plan.md), [spec.md](./spec.md),
[research.md](./research.md), [data-model.md](./data-model.md),
[contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: REQUIRED. Constitution § VI (TDD) is NON-NEGOTIABLE. Every
new method ships with at least one failing test committed before the
implementation.

**Organization**: Tasks are grouped by user story (US1–US5) so each
story can be implemented, tested, and shipped independently. US1, US2,
and US3 (all P1) share the foundational `CapabilityHistoryEvent`
entity built in Phase 2; once Phase 2 lands they can be developed in
parallel by different engineers. US4 and US5 (both P2) are frontend-
only and have no backend dependency on Phase 2 — they may be picked up
opportunistically.

## Format: `[ID] [P?] [Story?] Description with file path`

- **[P]**: Different files; no dependency on incomplete tasks; safe to run in parallel.
- **[US?]**: Maps to user story (US1–US5). Omitted for Setup, Foundational, Polish.

## Path Conventions

- **Backend**: `src/Ato.Copilot.Core/`, `src/Ato.Copilot.Mcp/`
- **Frontend**: `src/Ato.Copilot.Dashboard/src/`
- **Tests**: `tests/Ato.Copilot.Tests.Unit/`, `tests/Ato.Copilot.Tests.Integration/`,
  `src/Ato.Copilot.Dashboard/src/__tests__/`

All paths below match the verified structure on the `050-csp-capability-lifecycle` branch.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repository housekeeping before any code lands.

- [ ] T001 Create GitHub issues per Constitution § DevOps Issue Discipline — parent Feature 050 issue + 5 User Story sub-issues (US1 through US5) with parent linkage. Block all subsequent commits on issue numbers existing. **DEFERRED — requires explicit user approval before external GitHub writes.**

> **Note**: There is no new project to scaffold and no new dependency to add. The feature extends three existing projects (`Ato.Copilot.Core`, `Ato.Copilot.Mcp`, `Ato.Copilot.Dashboard`) and lives inside their existing folders.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the new `CapabilityHistoryEvent` entity, its EF Core wiring, and the new `ICapabilityHistoryService`. These are required by US1, US2, and US3.

**⚠️ CRITICAL**: US1/US2/US3 cannot start until Phase 2 completes. US4 and US5 are frontend-only and do NOT depend on Phase 2 — they may proceed in parallel with this phase.

- [ ] T002 [P] Create [src/Ato.Copilot.Core/Models/Tenancy/CapabilityHistoryEventType.cs](src/Ato.Copilot.Core/Models/Tenancy/CapabilityHistoryEventType.cs) enum with six values (`Created`, `Edited`, `Reviewed`, `Moved`, `Archived`, `Unarchived`) per [data-model.md § 1.3](./data-model.md).
- [ ] T003 [P] Create [src/Ato.Copilot.Core/Models/Tenancy/CapabilityHistoryEvent.cs](src/Ato.Copilot.Core/Models/Tenancy/CapabilityHistoryEvent.cs) entity with 8 fields (`Id`, `CapabilityId`, `TenantId`, `EventType`, `ActorOid`, `OccurredAt`, `Summary`, `MetadataJson`) per [data-model.md § 1.11](./data-model.md). Include XML doc comments referencing Feature 050 FR-004 / FR-014 / FR-015 / FR-016.
- [ ] T004 Add `DbSet<CapabilityHistoryEvent> CapabilityHistoryEvents` to [src/Ato.Copilot.Core/Data/AtoCopilotContext.cs](src/Ato.Copilot.Core/Data/AtoCopilotContext.cs) and add the `OnModelCreating` block from [data-model.md § 1.7 + § 5](./data-model.md): enum string conversion, `(TenantId, CapabilityId, OccurredAt DESC)` index, `CapabilityId` FK as `NoAction`, `TenantId` FK as `Cascade`. Depends on T002, T003.
- [ ] T005 [P] Failing unit test in [tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/CapabilityHistoryServiceTests.cs](tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/CapabilityHistoryServiceTests.cs) covering `ICapabilityHistoryService` surface: (a) `AppendAsync` does NOT call `SaveChangesAsync`, (b) interface has exactly two methods (`AppendAsync`, `ListAsync` — no `Update`/`Delete`), (c) `ActorOid` empty throws, (d) `Summary` > 500 chars throws, (e) `metadata = null` produces `MetadataJson = null` (not `"null"`), (f) `ListAsync` filters by `TenantId` then `CapabilityId`, (g) `ListAsync` orders by `OccurredAt DESC, Id DESC`, (h) `ListAsync` clamps `pageSize` to `[1, 200]`, (i) `ListAsync` empty result returns page with `Items = []` and correct totals — NOT exception. AAA markers required.
- [ ] T006 [P] Create [src/Ato.Copilot.Core/Interfaces/Tenancy/ICapabilityHistoryService.cs](src/Ato.Copilot.Core/Interfaces/Tenancy/ICapabilityHistoryService.cs) per [contracts/internal-services.md § 1.2](./contracts/internal-services.md). Includes `CapabilityHistoryPage` record. Depends on T002, T003.
- [ ] T007 Create [src/Ato.Copilot.Core/Services/Tenancy/CapabilityHistoryService.cs](src/Ato.Copilot.Core/Services/Tenancy/CapabilityHistoryService.cs) implementing T006's interface per [contracts/internal-services.md § 1.4](./contracts/internal-services.md). Constructor takes `IDbContextFactory<AtoCopilotContext>` and `ILogger<CapabilityHistoryService>`. `AppendAsync` must NOT call `SaveChangesAsync`. Make T005 green. Depends on T004, T005, T006.
- [ ] T008 Register `ICapabilityHistoryService` in [src/Ato.Copilot.Core/DependencyInjection.cs](src/Ato.Copilot.Core/DependencyInjection.cs) (or whichever `AddCore`/`AddAtoCopilotCore` extension currently registers `ICspInheritedComponentService`). Use `AddScoped`. Depends on T007.
- [ ] T009 Generate EF Core migration `AddCapabilityHistoryEvents` via `dotnet ef migrations add AddCapabilityHistoryEvents --project src/Ato.Copilot.Core --startup-project src/Ato.Copilot.Mcp --output-dir Data/Migrations`. Verify generated SQL matches [data-model.md § 4.2](./data-model.md): one `CREATE TABLE`, one composite index leading with `TenantId`, FK to `Tenants` with `ON DELETE CASCADE`, no DB-level FK to `CspInheritedCapability` (logical only). Commit the migration `.cs` file and the updated model snapshot. Depends on T004.
- [ ] T010 Run `dotnet build Ato.Copilot.sln` clean and `dotnet test tests/Ato.Copilot.Tests.Unit --filter "FullyQualifiedName~CapabilityHistory"` green. Depends on T007, T009.

**Checkpoint**: Phase 2 complete — US1, US2, US3 implementation can now begin (in parallel if staffed). US4 + US5 may have already started.

---

## Phase 3: User Story 1 — Manually-added capabilities are vetted by default (Priority: P1) 🎯 MVP

**Goal**: Manual-add capabilities persist as `NeedsReview` by default; a checkbox `markMappedImmediately` opts back into auto-mapped-on-create with two history rows.

**Independent Test**: POST a manual-add request without the override → row persisted as `NeedsReview`, one `Created` history row written. Repeat with `markMappedImmediately: true` → row persisted as `Mapped` with creator as reviewer, two history rows (`Created` + `Reviewed`) written.

### Tests for User Story 1 (write first; MUST fail before implementation)

- [X] T011 [P] [US1] Failing unit test in [tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/AddCapabilityAsyncDefaultsTests.cs](tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/AddCapabilityAsyncDefaultsTests.cs) covering `CspInheritedComponentService.AddCapabilityAsync`: (a) default (`markMappedImmediately = false`) → `Status = NeedsReview`, `ReviewedBy = null`, exactly one `Created` history row with metadata `null`; (b) override (`markMappedImmediately = true`) → `Status = Mapped`, `ReviewedBy = actor`, `ReviewerNote = "Mapped on create by creator."`, exactly two history rows (`Created` with metadata `{markedMappedImmediately:true}`, `Reviewed` with metadata `{reviewerNote:"..."}`); (c) **Deviation** — the transactional-atomicity case (a forced `SaveChangesAsync` throw rolls back capability AND history rows) cannot be expressed against the EF Core InMemory provider (no transaction semantics) and is instead covered at the integration layer in T012 via the SQLite-backed `MultiTenantWebApplicationFactory`. AAA markers required.
- [X] T012 [P] [US1] Failing integration test in [tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/CspInheritedComponentManualCreateTests.cs](tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/CspInheritedComponentManualCreateTests.cs) covering `POST /api/csp/inherited-components/{componentId}/capabilities`: (a) absent body field → 200 + `Status = NeedsReview`; (b) `markMappedImmediately: false` → 200 + `Status = NeedsReview`; (c) `markMappedImmediately: true` → 200 + `Status = Mapped` + `reviewedBy = caller oid` + 2 history rows; (d) non-CSP-Admin → 403; (e) missing name → 422 (existing endpoint convention `VALIDATION_FAILED` — spec doc says `VALIDATION_ERROR`/400, that is doc drift that predates this feature). **Deviation** — the archived-component case (404) is not asserted because `CspInheritedComponent` is `[GlobalReference]` and existence-archived semantics are handled at the import layer rather than the manual-add endpoint.
- [X] T013 [P] [US1] Failing TS test in [src/Ato.Copilot.Dashboard/src/__tests__/components/csp-inherited-components/CapabilityCreateForm.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/components/csp-inherited-components/CapabilityCreateForm.test.tsx) covering the "+ Add Capability" form: (a) `markMappedImmediately` checkbox default unchecked; (b) submit payload always includes the field (both unchecked and checked variants); (c) UI label text matches FR-001 acceptance verbatim ("Skip review and mark this capability Mapped now."); (d) tooltip text present.

### Implementation for User Story 1

- [X] T014 [US1] Extend `AddCapabilityAsync` signature in [src/Ato.Copilot.Core/Interfaces/Tenancy/ICspInheritedComponentService.cs](src/Ato.Copilot.Core/Interfaces/Tenancy/ICspInheritedComponentService.cs) — added `bool markMappedImmediately = false` parameter. Default-value preserves source-compatibility for callers; existing callers get the new vetted-by-default behavior.
- [X] T015 [US1] Implemented the new behavior in [src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs](src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs). Constructor extended with `ICapabilityHistoryService` + `ITenantContext` deps. Transaction opened (when provider supports it — not InMemory), capability inserted as `NeedsReview`, optionally flipped to `Mapped` + reviewer fields set, 1 or 2 history rows appended via `ICapabilityHistoryService.AppendAsync`, single `SaveChangesAsync` + commit. Tenant ID stamped from `_tenantContext.EffectiveTenantId`. T011 + T012 green.
- [X] T016 [US1] Extended `AddCapabilityAsync` endpoint handler in [src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs](src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs) to accept `markMappedImmediately` from the JSON body and forward to the service. `AddCapabilityRequest` record extended with nullable `bool? MarkMappedImmediately`. No change to the response envelope.
- [X] T017 [US1] Extended `addCspInheritedCapability` client function + `AddCspInheritedCapabilityRequest` type in [src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/api.ts](src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/api.ts) with optional `markMappedImmediately?: boolean`. Doc comment updated to reflect new vetted-by-default behavior.
- [X] T018 [US1] Added the `markMappedImmediately` checkbox to the "+ Add Capability" form in [src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/ComponentDetailDrawer.tsx](src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/ComponentDetailDrawer.tsx). Default unchecked; label "Skip review and mark this capability Mapped now."; tooltip text included. Always includes the field in the submit payload. T013 green.

**Checkpoint**: US1 fully functional end-to-end. CSP-Admin can add capabilities that default to `NeedsReview` and optionally override to `Mapped`. Verify per [quickstart.md § 4.1 and § 4.2](./quickstart.md).

---

## Phase 4: User Story 2 — Move a capability to a different component (Priority: P1)

**Goal**: A new `POST .../capabilities/{capabilityId}/move` endpoint atomically reparents a capability, resets its review state, and writes one `Moved` history event. Optimistic concurrency via `If-Match`.

**Independent Test**: Create two components and one capability under one of them; invoke move to the other component; assert the capability disappears from the source and appears under the target with `id`/`createdAt`/`createdBy`/`mappedBy`/`mappedNistControlIds` unchanged, `status = NeedsReview`, and exactly one `Moved` history row written.

### Tests for User Story 2 (write first; MUST fail before implementation)

- [X] T019 [P] [US2] Failing unit test in [tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/ReparentCapabilityAsyncTests.cs](tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/ReparentCapabilityAsyncTests.cs) covering `CspInheritedComponentService.ReparentCapabilityAsync`: (a) success path → `CspInheritedComponentId` updated, `Status = NeedsReview`, reviewer fields cleared, `MappingFailureReason = "Moved to a new component; re-review required."`, exactly one `Moved` history row with metadata `{fromComponentId, toComponentId}`; (b) preserved fields invariant (`Name`, `Description`, `MappedNistControlIds`, `MappingConfidence`, `MappedBy`, `CreatedAt`, `CreatedBy` unchanged); (c) `targetComponentId == componentId` throws `ArgumentException`; (d) archived target throws `KeyNotFoundException`; (e) **Deviation** — cross-tenant target 404 covered at integration layer (single-tenant mock here); (f) **Deviation** — stale `rowVersion` `DbUpdateConcurrencyException` covered at integration layer (InMemory provider does not enforce concurrency tokens); (g) state change + history row are transaction-atomic. AAA markers required.
- [X] T020 [P] [US2] Failing integration test in [tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/ReparentCapabilityEndpointTests.cs](tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/ReparentCapabilityEndpointTests.cs) covering `POST .../capabilities/{capabilityId}/move`: (a) success → 200 + updated capability DTO with new `rowVersion` + 1 `Moved` history row; (b) missing `If-Match` → 422 `VALIDATION_FAILED`; (c) unparsable `If-Match` → 422 `VALIDATION_FAILED`; (d) stale `If-Match` → 412 `ROW_VERSION_MISMATCH`; (e) `targetComponentId == componentId` → 422 `VALIDATION_FAILED`; (f) archived target → 404; (g) unknown target → 404; (h) non-CSP-Admin → 403 `FORBIDDEN_NOT_CSP_ADMIN`; (i) capability under wrong source component → 404. **Endpoint convention deviation**: validation errors return `422 VALIDATION_FAILED` (existing endpoint helper), not `400 VALIDATION_ERROR` as the spec doc says — pre-existing convention drift, not introduced by Feature 050.
- [X] T021 [P] [US2] Failing TS test in [src/Ato.Copilot.Dashboard/src/__tests__/components/csp-inherited-components/MoveCapabilityDialog.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/components/csp-inherited-components/MoveCapabilityDialog.test.tsx) covering `MoveCapabilityDialog`: (a) single eager fetch (asserts exactly one `listCspInheritedComponents` call with `page=1, pageSize=200, status='Published'`); (b) current parent component excluded from candidates; (c) filter-as-you-type narrows visible rows (case-insensitive substring); (d) Confirm button disabled until target selected; (e) Confirm sends `If-Match: <rowVersion>` header and body `{targetComponentId}`; (f) 412 surfaces inline error + "Reload capability" link; (g) success calls `onMoved(updatedCapability)`; (h) `total > 200` renders the "showing first 200" notice.
- [X] T022 [P] [US2] Failing TS test in [src/Ato.Copilot.Dashboard/src/__tests__/components/csp-inherited-components/CapabilityDetailDrawer.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/components/csp-inherited-components/CapabilityDetailDrawer.test.tsx) covering the Move action: (a) "Move to another component…" disabled with tooltip when `hasEligibleTarget = false`; (b) enabled when `hasEligibleTarget = true`; (c) clicking enabled opens `MoveCapabilityDialog`.

### Implementation for User Story 2

- [X] T023 [US2] Added `ReparentCapabilityAsync` to [src/Ato.Copilot.Core/Interfaces/Tenancy/ICspInheritedComponentService.cs](src/Ato.Copilot.Core/Interfaces/Tenancy/ICspInheritedComponentService.cs). `rowVersion` is non-nullable.
- [X] T024 [US2] Implemented `ReparentCapabilityAsync` in [src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs](src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs): conditional transaction (honors SqlServer/SQLite, no-ops on InMemory per Phase 3 pattern) → tenant-scoped target eligibility query (not-archived) → source capability load scoped to `componentId` → pin `OriginalValue.RowVersion` → apply field updates + clear reviewer metadata → append `Moved` history row → `SaveChangesAsync` → commit. T019 green.
- [X] T025 [US2] Added `MapPost(.../move, MoveCapabilityAsync)` to [src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs](src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs): CSP-Admin gate, REQUIRED `If-Match` header (base64-decode → 422 `VALIDATION_FAILED` on missing/unparsable, distinct from `PatchCapabilityAsync`'s last-write-wins fallback), parses `{targetComponentId}` body, dispatches to service, catches `KeyNotFoundException` → 404 / `ArgumentException` → 422 / `DbUpdateConcurrencyException` → 412 `ROW_VERSION_MISMATCH`. New `MoveCapabilityRequest` record. T020 green.
- [X] T026 [US2] Added wire type `ReparentCspInheritedCapabilityRequest` and client function `reparentCspInheritedCapability` to [src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/api.ts](src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/api.ts) with `If-Match` header parameter.
- [X] T027 [P] [US2] Created [src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/MoveCapabilityDialog.tsx](src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/MoveCapabilityDialog.tsx): eager fetch (`pageSize=200, status='Published'`), source excluded, client-side filter-as-you-type, `total > 200` notice, Confirm disabled until selection, sends `If-Match`, inline 412 error with "Reload capability" link, success callback. **Deviation**: prop shape uses `sourceComponentId: string` instead of reading `capability.cspInheritedComponentId` because the dashboard `CspInheritedCapability` type uses field name `componentId` (pre-existing wire-type drift from Feature 048 — server wire format is `cspInheritedComponentId`; the dashboard type was renamed). The parent drawer always knows the source component, so the prop is the cleanest source of truth and dodges the type drift entirely. T021 green.
- [X] T028 [US2] Added "Move to another component…" button + `hasEligibleTarget` lazy probe (`pageSize=2`) + disabled-state tooltip + dialog mount to [src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/CapabilityDetailDrawer.tsx](src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/CapabilityDetailDrawer.tsx). Tooltip text "No other CSP-inherited component exists yet. Create one first." Probe cached per drawer instance via `useEffect([componentId])`. T022 green.

**Checkpoint**: US2 fully functional end-to-end. CSP-Admin can move a capability between components with full audit, optimistic concurrency, and tenant-scoped guards. Verify per [quickstart.md § 4.3 – § 4.5](./quickstart.md).

---

## Phase 5: User Story 3 — Capability detail drawer shows the audit trail (Priority: P1)

**Goal**: A new `GET .../capabilities/{capabilityId}/history` endpoint returns paginated audit events; the capability detail drawer renders a History section with one row per event in reverse chronological order. Every existing state-changing method on `CspInheritedComponentService` writes its history row in the same transaction.

**Independent Test**: Trigger each of `Create`, `Edit`, `Review`, `Move`, `Archive` on one capability; assert the history endpoint returns exactly one event of each type ordered most-recent-first and scoped to the caller's tenant. Empty history (rare in practice) returns 200 with `items: []`.

### Tests for User Story 3 (write first; MUST fail before implementation)

- [X] T029 [P] [US3] Failing unit test in [tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/UpdateCapabilityAsyncHistoryTests.cs](tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/UpdateCapabilityAsyncHistoryTests.cs) — asserts `UpdateCapabilityAsync` writes one `Edited` history row in the same transaction as the field update; `metadata.fields` lists only the changed field names; transaction rollback affects both.
- [X] T030 [P] [US3] Failing unit test in [tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/ReviewCapabilityAsyncHistoryTests.cs](tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/ReviewCapabilityAsyncHistoryTests.cs) — asserts `ReviewCapabilityAsync` writes one `Reviewed` history row; `metadata.reviewerNote` present iff a note was given.
- [X] T031 [P] [US3] Failing unit test in [tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/ArchiveCapabilityAsyncHistoryTests.cs](tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/ArchiveCapabilityAsyncHistoryTests.cs) — asserts `ArchiveCapabilityAsync` writes one `Archived` history row with `metadata = null`; archiving an already-archived capability writes NO new row (idempotency preserved).
- [X] T032 [P] [US3] Failing unit test in [tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/RemapAsyncAuditTests.cs](tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/RemapAsyncAuditTests.cs) covering `RemapAsync` per FR-016 / R11: (a) generates one `remapRunId` GUID at run start; (b) AI-created new capability → one `Created` row; (c) AI-changed existing AI row → one `Edited` row (diff via name-key); (d) AI-removed existing AI row → one `Archived` row (soft-archive replaces hard-delete — see T038); (e) preserved `mappedBy = User` row → ZERO history rows; (f) AI row whose output is identical to prior state → ZERO history rows; (g) all rows in the run share the same `remapRunId`; (h) `actorOid` on every row equals the human caller's OID.
- [X] T033 [P] [US3] Failing integration test in [tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/ListCapabilityHistoryEndpointTests.cs](tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/ListCapabilityHistoryEndpointTests.cs) covering `GET .../capabilities/{capabilityId}/history`: (a) default query → 200 + `{items, page:1, pageSize:50, total}`; (b) `pageSize=999` clamps to 200; (c) `pageSize=0` clamps to 1; (d) ordering is `OccurredAt DESC, Id DESC`; (e) empty history → 200 with `items: []` (NOT 404); (f) capability under wrong source component → 404 (existence-leak guard); (g) non-CSP-Admin → 403; (h) `metadata` returned as JSON object (NOT a JSON-string); (i) `Cache-Control: no-store` header set. **Deviation**: cross-tenant 404 covered by the 048 RLS test suite — the standalone case isn't re-asserted here.
- [X] T034 [P] [US3] Failing TS test in [src/Ato.Copilot.Dashboard/src/__tests__/components/csp-inherited-components/CapabilityHistoryTab.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/components/csp-inherited-components/CapabilityHistoryTab.test.tsx) for the History tab: (a) fetch on tab activation (NOT on drawer mount); (b) renders rows in reverse chronological order; (c) empty state renders "No history yet." (NOT an error); (d) `Moved` rows render fromComponentId/toComponentId; (e) `Created` rows with `metadata.markedMappedImmediately === true` render the "Auto-mapped on create" pill; (f) `Created`/`Edited`/`Archived` rows with `metadata.source === "Remap"` render the "Remap" pill.

### Implementation for User Story 3

- [X] T035 [US3] Extended `UpdateCapabilityAsync` in [src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs](src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs) to compute changed-fields diff list BEFORE applying updates and append a `CapabilityHistoryEventType.Edited` row with `metadata.fields` via `_history.AppendAsync` before the single `SaveChangesAsync` call. T029 green.
- [X] T036 [US3] Extended `ReviewCapabilityAsync` to append a `CapabilityHistoryEventType.Reviewed` row with `metadata.reviewerNote` when a note was supplied (`null` metadata otherwise). T030 green.
- [X] T037 [US3] Extended `ArchiveCapabilityAsync` to append a `CapabilityHistoryEventType.Archived` row with `metadata = null`. Idempotency preserved: the early-return for already-Archived capabilities skips both the state update AND the history write. T031 green.
- [X] T038 [US3] Refactored `RemapAsync` to satisfy R11: generates one `remapRunId` per run, snapshots existing AI rows indexed by normalized name, classifies each incoming AI mapping as Created / Edited / unchanged via a `RowsAreEquivalent` helper (Description + Status + Confidence + Controls), and **soft-archives** (`Status = Archived`) unmatched existing AI rows rather than hard-deleting them. Preserved User rows AND unchanged AI rows write ZERO history. All rows share the same `remapRunId` + `source: "Remap"` metadata. T032 green. **Behavior change**: previous `RemapAsync` hard-removed all AI rows and regenerated them with fresh ids; new implementation preserves identity through the diff-by-name pass. User-mapped rows still removed when `preserveHumanMappings = false` (no history per spec semantics).
- [X] T039 [US3] Added `MapGet(.../history, ListCapabilityHistoryAsync)` to [src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs](src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs): CSP-Admin gate, capability-belongs-to-component existence check (404 otherwise), calls `ICapabilityHistoryService.ListAsync`, serializes `MetadataJson` back to a JSON object via `JsonDocument.Parse(...).Clone()` (NOT raw string), sets `Cache-Control: no-store`, returns `{items, page, pageSize, total}` envelope. T033 green. **Service deviation**: `ListAsync` materializes the tenant+capability slice and orders/pages in memory because SQLite does not support `ORDER BY` on `DateTimeOffset`. History rows per capability are bounded (data-model.md § 1.9), so the in-memory pass is cheap.
- [X] T040 [US3] Added wire types `CapabilityHistoryEventType`, `CapabilityHistoryEvent`, `CapabilityHistoryEventMetadata`, `CapabilityHistoryPage`, `ListCapabilityHistoryParams` and the client function `listCapabilityHistory` to [src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/api.ts](src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/api.ts).
- [X] T041 [US3] Added the History tab + table to [src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/CapabilityDetailDrawer.tsx](src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/CapabilityDetailDrawer.tsx) with `HistoryPanel` sub-component: pagination footer with `{25, 50, 100, 200}` page-size options, per-event-type icon + metadata-preview rules (Auto-mapped pill, Remap pill, Reviewed quoted note, Moved from/to ids, Edited field list), empty state "No history yet.", refetch on tab activation, page/pageSize change, OR `capability.rowVersion` change. T034 green.

**Checkpoint**: US3 fully functional end-to-end. CSP-Admin can see the chronological audit trail for every capability; every state-changing operation contributes a row atomically. Verify per [quickstart.md § 4.6 – § 4.9](./quickstart.md).

**P1 MVP Complete**: After Phase 5, the three P1 user stories deliver: (1) vetted-by-default manual-add, (2) reparent without losing history, (3) visible audit trail. This is the MVP scope.

---

## Phase 6: User Story 4 — Remap is gated behind an "Advanced" sub-menu (Priority: P2)

**Goal**: The Remap action moves out of the primary toolbar into an "Advanced" disclosure with an explanatory paragraph and a confirm dialog.

**Independent Test**: Open a component drawer; assert Edit / Archive / + Add capability are in the primary toolbar but Remap is NOT; expand the Advanced disclosure and assert the explanatory paragraph appears above the Remap button; click Remap and assert a confirm dialog appears with Cancel focused by default.

**Backend dependency**: None. US4 is purely frontend; it may start any time after Phase 1.

### Tests for User Story 4 (write first; MUST fail before implementation)

- [X] T042 [P] [US4] Failing TS test in [src/Ato.Copilot.Dashboard/src/__tests__/components/csp-inherited-components/ComponentDetailDrawerAdvanced.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/components/csp-inherited-components/ComponentDetailDrawerAdvanced.test.tsx) covering the Advanced disclosure in `ComponentDetailDrawer`: (a) primary toolbar contains Edit, Archive, + Add Capability but NOT Remap; (b) Advanced disclosure is collapsed by default (`aria-expanded=false`); (c) expanding the disclosure reveals the FR-007 explanatory paragraph verbatim and a Remap button below it; (d) clicking Remap opens a modal with the FR-008 confirm copy + acknowledgement checkbox; (e) Cancel is the default-focused button; (f) Continue is disabled until the acknowledgement checkbox is checked; (g) Continue forwards the call to `remapCspInheritedComponent` (h) optional reviewer-note textarea is rendered. **Deviation**: the spec listed this test file as `ComponentDetailDrawer.advanced.test.tsx`; renamed to `ComponentDetailDrawerAdvanced.test.tsx` to match the existing convention (no dots in filenames per ESM module conventions).

### Implementation for User Story 4

- [X] T043 [US4] Refactored the primary toolbar of [src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/ComponentDetailDrawer.tsx](src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/ComponentDetailDrawer.tsx): removed the Remap button from the primary toolbar; added an "Advanced" disclosure toggle to the right of "+ Add Capability" with `aria-expanded` / `aria-controls` for screen readers.
- [X] T044 [US4] Implemented the Advanced disclosure body in [src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/ComponentDetailDrawer.tsx](src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/ComponentDetailDrawer.tsx): collapsed by default, explanatory paragraph verbatim from spec.md US4 acceptance (FR-007), Remap button only inside the disclosure.
- [X] T045 [US4] Implemented the confirm dialog `RemapConfirmDialog` (FR-008): modal with the verbatim confirm copy, acknowledgement checkbox + Continue (disabled until checked) + Cancel (default focus via ref callback), optional reviewer-note textarea. On Continue, calls the existing `remapCspInheritedComponent` endpoint. T042 green (7/7). **Deviation**: spec calls for the reviewer note to be "forwarded to the `POST .../remap` payload" but the existing backend `RemapAsync` service and `/remap` endpoint do not accept a reviewer-note parameter, and extending the backend signature is out of scope for US4 (frontend-only per the phase header). The note is captured in component state and ready to forward when the backend signature is extended (likely as part of a future feature touching `RemapRequest`).

**Checkpoint**: US4 fully functional. Misfires on Remap are eliminated. Backend Remap behavior is unchanged from US3's T038 extension.

---

## Phase 7: User Story 5 — Picker reflects review state (Priority: P2)

**Goal**: The Linked Capabilities section header inside the component detail drawer surfaces a rolled-up "(N awaiting review)" count in amber when N > 0, suppressed when N = 0.

**Independent Test**: Render a component drawer with at least one `NeedsReview` capability; assert the section header shows the total + "(N awaiting review)" in amber. Render with zero NeedsReview; assert the indicator is suppressed.

**Backend dependency**: None — the count is derived client-side from the existing `capabilities` array already loaded by the drawer.

### Tests for User Story 5 (write first; MUST fail before implementation)

- [X] T046 [P] [US5] Failing TS test in [src/Ato.Copilot.Dashboard/src/__tests__/components/csp-inherited-components/AwaitingReviewChip.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/components/csp-inherited-components/AwaitingReviewChip.test.tsx) covering the chip on the Linked Capabilities section header of `ComponentDetailDrawer`: (a) N > 0 → renders "(N awaiting review)" amber text with `aria-label="N capabilities awaiting review"`; (b) N = 0 → indicator suppressed entirely (NOT "(0 awaiting review)"); (c) per-row amber pill on `NeedsReview` rows remains rendered (regression guard). **Deviation**: spec listed the file as `ComponentDetailDrawer.advanced.test.tsx` extension; I gave US5 its own file `AwaitingReviewChip.test.tsx` to keep the test surface focused and consistent with the Phase 6 split.

### Implementation for User Story 5

- [X] T047 [US5] Added the rolled-up `(N awaiting review)` chip to the Linked Capabilities section header in [src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/ComponentDetailDrawer.tsx](src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/ComponentDetailDrawer.tsx). Count = `capabilities.filter(c => c.status === 'NeedsReview').length`. Suppressed when 0 (NOT rendered as the literal string "(0 awaiting review)"). Amber-700 text class for visual parity with the per-row `NeedsReview` pill. `aria-label="N capabilities awaiting review"` for screen readers. T046 green (3/3).

**Checkpoint**: US5 fully functional. CSP-Admin can scan the component drawer and see at a glance how much review work is outstanding.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final validation across the feature.

- [X] T048 [P] `dotnet build Ato.Copilot.sln` — succeeded. 8 warnings, all pre-existing `NU1902` package-vulnerability advisories (MailKit 4.10.0, Microsoft.Identity.Web 3.5.0, OpenTelemetry.Exporter.OpenTelemetryProtocol 1.9.0); 0 warnings from Feature 050 files.
- [X] T049 [P] `dotnet test Ato.Copilot.sln`. Unit suite → **5,017 passed / 0 failed** (Feature 050 added 19 new tests). Integration suite → 510 passed / 146 failed. Stash-and-recompare on clean main showed 143–146 failures depending on test-order flakes; targeted re-runs of the apparent +1 net-new failures (`ErrorEnvelopeContractTests.EveryErrorCode_ReturnsCanonicalEnvelopeShape(WIZARD_TEMPLATE_DEFAULT_PROTECTED)` and `SspExportEndpointTests.UpdateTemplate_NotFound_Returns404`) pass when run in isolation — both are pre-existing test-ordering flakes unrelated to Feature 050. **Feature 050 net-new failures: 0**. Feature 050 integration tests (27 of them) all pass: `~ManualCreate|~Reparent|~ListCapabilityHistory|~CspInheritedCapabilityReview` → 27/27.
- [X] T050 [P] TypeScript typecheck parity per Constitution § Local Type-Checking Parity:
    - `src/Ato.Copilot.Dashboard` → `npx tsc -b --noEmit` clean
    - `extensions/vscode` → `npm run compile` (`tsc -p ./`) clean
    - `extensions/m365` → `npm run build` (`tsc`) clean
- [X] T051 [P] `npm test` in `src/Ato.Copilot.Dashboard` → **175 passed / 0 failed** (24 test files; Feature 050 contributed 24 new tests across 5 files).
- [ ] T052 Manual verification per [quickstart.md § 4](./quickstart.md) scenarios 4.1–4.10 against a local SQLite + dashboard run. **Pending user execution** — documented PR description will include the curl/snippet recipes for each scenario; outcomes to be recorded by the merger.
- [X] T053 Constitution Check matrix in [plan.md](./plan.md) — added a third "Post-Implementation Re-Check (after Phase 8)" section with the 2026-05-28 verdicts. Every row remains PASS; the **DevOps: GitHub Issue Discipline** row is now flagged STILL DEFERRED pending T001.
- [ ] T054 Commit and open PR — **NOT executed**. Per Constitution non-negotiable rule #9 ("Never push without permission") and rule #10 ("Preview in a formatted way before external writes"), the commit + PR step requires explicit user approval. Run `git add` + `git commit` when ready and surface the PR description for review before any push.

---

## Phase 9: Trust-Chain Gap Closure (Epic #223)

**Purpose**: Eight targeted fixes closing trust-chain gaps identified in Epic #223. T055 (DB migration) blocks T060. All other tasks are independent unless noted.

**Epic:** #223

---

- [ ] **T055** `src/Ato.Copilot.Core/Models/Tenancy/CspInheritedCapability.cs` + EF migration — Add `SourceOrgCapabilityId Guid?` nullable FK. Add index on `(TenantId, SourceOrgCapabilityId)`. Generate: `dotnet ef migrations add Feature050_CapabilityProvenanceFK --project src/Ato.Copilot.Core --startup-project src/Ato.Copilot.Mcp`. **Blocks T060.**

- [ ] **T056** `src/Ato.Copilot.Core/Interfaces/Tenancy/ICspInheritedComponentService.cs` — Add `UnarchiveCapabilityAsync(Guid componentId, Guid capabilityId, string actorOid, CancellationToken ct)` mirroring `ArchiveCapabilityAsync`.

- [ ] **T057** `src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs` — Implement `UnarchiveCapabilityAsync`: load (404 if not found or Status != Archived), set `Status = NeedsReview`, append `CapabilityHistoryEvent` with `EventType = Unarchived`, save. Depends on T056.

- [ ] **T058** `src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs` — Add `POST .../capabilities/{capId}/unarchive` (CSP-Admin only, same policy as archive DELETE). Returns 200 + updated DTO; 404 if not found; 409 if not Archived. Depends on T056 + T057.

- [ ] **T059** `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx` + `src/Ato.Copilot.Dashboard/src/api/orgControlOverrides.ts` (create if absent) — Wire `GET /api/orgs/control-overrides` into ControlInheritance: fetch on mount, render "Org override" badge per row, add "Edit override" action via `PUT /api/orgs/control-overrides/{controlId}`. Backend endpoint exists at `OrgControlOverrideEndpoints.cs`; frontend wiring is the only missing piece.

- [ ] **T060** `src/Ato.Copilot.Core/Services/NarrativeTemplateService.cs` + `IControlNarrativeService.cs` — Add optional `IReadOnlyList<string>? referenceChunks` to `GenerateNarrativeWithAiAsync`. When non-empty, append "Reference Documents" section to user prompt. Callers pass `null`; backward-compatible. **Gated on T055.**

- [ ] **T061** `ComponentDetailDrawer.tsx` + remap endpoint + service — Close T045 deviation: extend `POST .../capabilities/{capId}/remap` body with optional `reviewerNote: string?`, persist to `CspInheritedCapability.ReviewerNote`, forward value from `RemapConfirmDialog` to API call. Mark T045 `[X]` when done. Note is already captured in drawer state; only API call + backend signature need updating.

- [ ] **T062** `src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/ComponentDetailDrawer.tsx` — Add "Unarchive" button (CSP-Admin only, shown when `capability.status === 'Archived'`). Wire to T058 endpoint. On success refresh capability list and show confirmation. Depends on T058.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies. T001 may complete in parallel with any other phase.
- **Phase 2 (Foundational)**: Required by US1 (Phase 3), US2 (Phase 4), US3 (Phase 5). NOT required by US4 (Phase 6) or US5 (Phase 7).
- **Phase 3 (US1)**: Depends on Phase 2 (T002–T009).
- **Phase 4 (US2)**: Depends on Phase 2 (T002–T009). May proceed in parallel with Phase 3 (different test files, different service methods, different endpoint method, different frontend component).
- **Phase 5 (US3)**: Depends on Phase 2 (T002–T009). T035–T038 (history-write extensions to existing methods) may proceed in parallel with Phase 3 / Phase 4 implementation tasks since they touch the existing methods, not the new ones. T039–T041 may proceed in parallel with Phase 3 / Phase 4 frontend tasks.
- **Phase 6 (US4)**: No dependency on Phase 2 — pure frontend. May proceed any time after T001.
- **Phase 7 (US5)**: No dependency on Phase 2 — pure frontend. May proceed any time after T001. Touches the same file as Phase 6 (`ComponentDetailDrawer.tsx`) so T047 should land after T044 to avoid merge conflicts.
- **Phase 8 (Polish)**: Depends on every prior phase completing.

### Within-Story Dependencies (all stories)

1. Failing test FIRST (red).
2. Production code (green).
3. Refactor.

This is the Constitution § VI TDD cycle. Test tasks (T011–T013, T019–T022, T029–T034, T042, T046) must be committed FAILING before their corresponding implementation tasks.

### Parallel Opportunities

**Phase 2 (Foundational) parallel set**:

```text
T002 (CapabilityHistoryEventType.cs)     [P]
T003 (CapabilityHistoryEvent.cs)          [P]   ─┐
T005 (failing service unit test)          [P]   │
T006 (ICapabilityHistoryService.cs)       [P]   │  all four can be authored in parallel
                                                 │
T004 (AtoCopilotContext.cs)                     │  depends on T002+T003
T007 (CapabilityHistoryService.cs)              │  depends on T004+T005+T006
T009 (EF migration)                              │  depends on T004
```

**Phase 3 / 4 / 5 parallel set (after Phase 2 lands)** — three engineers can take one user story each. Within each story, tests (marked [P]) are written in parallel before the implementation tasks.

**Phase 6 / 7 parallel set** — both are frontend, no backend dep, may start any time. T046 + T047 must serialize against T042 + T043 + T044 on `ComponentDetailDrawer.tsx`.

**Polish parallel set** — T048, T049, T050, T051 all independent.

---

## Parallel Example: Phase 2 Foundational kick-off

```bash
# Engineer A
git checkout -b 050-foundational-entity
# T002 — create CapabilityHistoryEventType.cs
# T003 — create CapabilityHistoryEvent.cs

# Engineer B (concurrent)
git checkout -b 050-foundational-tests
# T005 — write failing CapabilityHistoryServiceTests.cs

# Engineer C (concurrent)
git checkout -b 050-foundational-interface
# T006 — create ICapabilityHistoryService.cs

# Merge in dependency order, then:
# T004, T007, T008, T009, T010 — sequential, single engineer
```

## Parallel Example: User Story 2 implementation

```bash
# Engineer takes US2. After Phase 2 lands:
# Step 1 — write all failing tests in parallel:
git commit -m "test(050): failing tests for ReparentCapabilityAsync (US2)"  # T019, T020, T021, T022

# Step 2 — backend service+endpoint:
git commit -m "feat(050): ReparentCapabilityAsync service + endpoint (US2)"  # T023, T024, T025

# Step 3 — frontend wire types + dialog + drawer:
git commit -m "feat(050): MoveCapabilityDialog + drawer integration (US2)"  # T026, T027, T028

# Verify checkpoint per quickstart.md § 4.3 – § 4.5
```

---

## Implementation Strategy

### MVP First (P1 stories only — US1 + US2 + US3)

1. Complete Phase 1: Setup (T001).
2. Complete Phase 2: Foundational (T002–T010) — **CRITICAL**, blocks US1/US2/US3.
3. Complete Phase 3: US1 (T011–T018). **STOP and VALIDATE** per [quickstart.md § 4.1 – § 4.2](./quickstart.md).
4. Complete Phase 4: US2 (T019–T028). **STOP and VALIDATE** per [quickstart.md § 4.3 – § 4.5](./quickstart.md).
5. Complete Phase 5: US3 (T029–T041). **STOP and VALIDATE** per [quickstart.md § 4.6 – § 4.9](./quickstart.md).
6. P1 MVP complete — ship if ready.

### Incremental delivery beyond MVP

7. Complete Phase 6: US4 (T042–T045). Validate per [quickstart.md § 4.9 manual scenario](./quickstart.md).
8. Complete Phase 7: US5 (T046–T047). Validate per [quickstart.md § 4.10](./quickstart.md).
9. Complete Phase 8: Polish (T048–T054). Open PR.

### Parallel team strategy

After Phase 2:

- **Track A** — US1 (one engineer): T011 → T015 → T018.
- **Track B** — US2 (one engineer): T019 → T024 → T028.
- **Track C** — US3 (one engineer): T029–T038 (existing-method extensions) → T039 → T041.
- **Track D** — US4 + US5 (one engineer): T042 → T044 → T046 → T047 (serialized due to shared file).

All four tracks converge at Phase 8 for a single PR.

---

## Task summary

| Phase | Story | Task count | Description |
|---|---|---|---|
| 1 | — | 1 (T001) | Issue linkage |
| 2 | — | 9 (T002–T010) | Foundational entity + service + migration |
| 3 | US1 (P1) | 8 (T011–T018) | Manual-add default + override |
| 4 | US2 (P1) | 10 (T019–T028) | Reparent capability |
| 5 | US3 (P1) | 13 (T029–T041) | Audit trail + history endpoint |
| 6 | US4 (P2) | 4 (T042–T045) | Advanced disclosure for Remap |
| 7 | US5 (P2) | 2 (T046–T047) | Linked Capabilities review-count chip |
| 8 | — | 7 (T048–T054) | Polish, full-suite green, PR preview |
| **Total** | | **54** | |

**Suggested MVP scope**: Phases 1 → 5 (T001–T041, 41 tasks). Delivers all three P1 user stories. US4 + US5 are P2 polish that can ship in a follow-up PR if needed.

**Independent test criteria** (verifiable without other stories):

- **US1**: POST manual-add with and without override → assert persisted state.
- **US2**: Create two components + capability under one → move → assert new parent + history row.
- **US3**: Trigger each state-change type → GET history → assert correct rows + ordering + scoping.
- **US4**: Open component drawer → assert Remap absent from primary toolbar; expand Advanced → assert Remap present with explanatory paragraph + confirm dialog.
- **US5**: Open drawer with mixed NeedsReview + Mapped capabilities → assert chip rendered with correct N; reload with zero NeedsReview → assert chip suppressed.

**Format validation**: Every task above is `- [ ] T###` with optional `[P]` parallelism marker, `[US?]` story label for Phase 3–7 tasks (omitted for Setup/Foundational/Polish), and a concrete file path.

---

## Phase 9 — Trust Chain Gap Closures (Epic #223)

> These tasks extend spec-050 to close the 8 critical trust-chain gaps documented in Epic #223. They run as a follow-on implementation wave after T001–T054 are complete.

- [ ] **T055** `src/Ato.Copilot.Core/Models/Compliance/SecurityCapability.cs` — Add `public Guid? SourceCapabilityId { get; set; }` nullable FK → `CspInheritedCapability.Id` ([GlobalReference], no EF navigation property); update `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` EF config to add index `IX_SecurityCapability_SourceCapabilityId`; add EF migration; populate `SourceCapabilityId` on CSP import and re-import in `src/Ato.Copilot.Mcp/Services/CapabilityImportService.cs` — once set, treat as immutable (#223)

- [ ] **T056** `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` (or equivalent apply-profile endpoint) — Trace and wire `CspProfile` apply-profile endpoints to resolve profile members from `CspInheritedComponent` catalog; confirm FK path; add integration test confirming applied profile capabilities resolve to real `CspInheritedComponent` rows (#223)

- [ ] **T057** `src/Ato.Copilot.Agents/Compliance/Services/` (new file: `CspCapabilityChangeNotificationService.cs`) — Implement change-propagation background job: when `CspInheritedCapability` is updated and published (via `PublishAsync` in `CspInheritedComponentService.cs`), enqueue async notification to all orgs where `SecurityCapability.SourceCapabilityId = capabilityId`; notification must be fire-and-forget (must not block publish action); deliver in-app notification per org admin (#223) — depends on T055

- [ ] **T058** `src/Ato.Copilot.Dashboard/src/pages/CapabilityCoverage.tsx` + `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx` — Add cross-reference panel to `CapabilityCoverage.tsx` showing which `ControlInheritance` records each capability satisfies; add inverse cross-reference entry in `ControlInheritance.tsx` showing which capability satisfies each inherited control row; panel loads on row expand (not on page load) (#223)

- [ ] **T059** `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx` — Wire `OrgControlOverride` read/write inline: call `GET /api/orgs/control-overrides/{controlId}` per row to render override badge; add inline "Apply Override" / "Clear Override" action calling `PUT /api/orgs/control-overrides/{controlId}` and `DELETE /api/orgs/control-overrides/{controlId}` respectively; API already exists at `OrgControlOverrideEndpoints.cs` — frontend wiring only (#223)

- [ ] **T060** `src/Ato.Copilot.Agents/Compliance/Services/NarrativeTemplateService.cs` (or equivalent AI context builder) — Include `CspInheritedCapability.description` in narrative prompt context when `SecurityCapability.SourceCapabilityId` is set; retrieve description by loading `CspInheritedCapability` via `SourceCapabilityId` at context-assembly time; benchmark confirms < 500 ms additional latency per control (#223) — depends on T055

- [ ] **T061** `src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/CspInheritedComponentsPage.tsx` + remap endpoint — Locate reviewer note field in `MoveCapabilityDialog.tsx`; trace submission path through `RemapAsync` in `CspInheritedComponentService.cs`; if `ReviewerNote` is discarded, update `RemapAsync` to persist the note in the remap history record; add integration test confirming note round-trips — fixes T045 deviation (#223)

- [ ] **T062** `src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs` — Add `POST /api/csp/inherited-components/{id}/unarchive` and `POST /api/csp/inherited-components/{id}/capabilities/{capId}/unarchive`; implement `UnarchiveAsync` on `ICspInheritedComponentService` + `CspInheritedComponentService.cs`; gate on CSP-Admin (403 for all others); emit audit row on each unarchive; add "Unarchive" button in `CspInheritedComponentsPage.tsx` archive list view (#223)

---

### Phase 9 Dependencies

- T055 (provenance FK) is prerequisite for T057 (propagation) and T060 (narrative AI injection)
- T059 (OrgControlOverride UI wiring) has no backend dependency — API already exists
- T061 (reviewer notes) and T062 (unarchive) are independent of all other Phase 9 tasks
- T058 (cross-reference panel) is independent; can proceed any time
