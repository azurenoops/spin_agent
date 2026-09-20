# Tasks: Unified RMF Role Assignments with Org → System Inheritance

**Branch**: `049-unified-rmf-role-assignments`
**Input**: Design documents from [specs/049-unified-rmf-role-assignments/](.)
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)
**Tests**: REQUIRED — Constitution §VI TDD is non-negotiable; every new code path opens with a failing AAA-marked unit test (FR-022).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- `[P]` — parallelizable (different files, no in-phase dependencies)
- `[Story]` — `[US1]` / `[US2]` / `[US3]` / `[US4]` for story-scoped tasks; absent for Setup / Foundational / Polish
- Every task includes exact file paths from [Project Structure](./plan.md#project-structure)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Open the directories and scaffolding the rest of the feature populates. No domain logic.

- [X] T001 [P] Create directory [src/Ato.Copilot.Core/Services/Roles/](../../src/Ato.Copilot.Core/Services/Roles/) (add an empty `.gitkeep` so git tracks the directory before the first interface lands).
- [X] T002 [P] Create directory [src/Ato.Copilot.Mcp/Workers/](../../src/Ato.Copilot.Mcp/Workers/) (add `.gitkeep`).
- [X] T003 [P] Create directories [tests/Ato.Copilot.Tests.Unit/Roles/](../../tests/Ato.Copilot.Tests.Unit/Roles/) and [tests/Ato.Copilot.Tests.Integration/Roles/](../../tests/Ato.Copilot.Tests.Integration/Roles/) (each with `.gitkeep`).
- [X] T004 [P] Create directories [src/Ato.Copilot.Dashboard/src/components/roles/](../../src/Ato.Copilot.Dashboard/src/components/roles/), [src/Ato.Copilot.Dashboard/src/types/](../../src/Ato.Copilot.Dashboard/src/types/), and [src/Ato.Copilot.Dashboard/src/lib/api/](../../src/Ato.Copilot.Dashboard/src/lib/api/) (each with `.gitkeep` where they don't already exist).
- [X] T004b Verify the dashboard test runner is wired (Vitest + `@testing-library/react`). Run `cd src/Ato.Copilot.Dashboard && npm test -- --run` and confirm the existing baseline passes. If the runner is missing, add `vitest`, `@testing-library/react`, `@testing-library/jest-dom`, and `jsdom` to `devDependencies` and add a minimal `vitest.config.ts` plus a `"test": "vitest"` script in `package.json` so subsequent `.test.tsx` tasks (T033, T034, T043, T044) have a runner. Blocks every US2/US3 dashboard test.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema-level and DTO-level types every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase completes. The `OrganizationRole` enum extension blocks every C# compilation in this feature; the shared record types are used by every interface contract.

- [X] T005 Extend [src/Ato.Copilot.Core/Models/Onboarding/OrganizationRoleAssignment.cs](../../src/Ato.Copilot.Core/Models/Onboarding/OrganizationRoleAssignment.cs) — append `MissionOwner`, `AuthorizingOfficial`, `SystemOwner` to the `OrganizationRole` enum (ordinals 4–6) per [data-model.md § 1](./data-model.md#1-organizationrole-enum--extension). Do NOT reorder existing values.
- [X] T006 [P] Write failing unit test in [tests/Ato.Copilot.Tests.Unit/Roles/OrganizationRoleEnumTests.cs](../../tests/Ato.Copilot.Tests.Unit/Roles/OrganizationRoleEnumTests.cs) asserting (a) the 4 existing values still serialize to their existing strings, (b) the 3 new values exist and serialize to `"MissionOwner"`, `"AuthorizingOfficial"`, `"SystemOwner"`, (c) `nameof(MissionOwner).Length <= 32` (column-fit check per `HasMaxLength(32)`). AAA markers required.
- [X] T007 [P] Create [src/Ato.Copilot.Core/Services/Roles/RoleRecordTypes.cs](../../src/Ato.Copilot.Core/Services/Roles/RoleRecordTypes.cs) — the shared record types: `AuthorizationResult` (readonly record struct), `SoDWarning`, `PropagationIntent`, `ResolvedRoleAssignment`, `SystemRoleSnapshot`, and the `RoleAssignmentSource` enum — exact shapes per [contracts/internal-services.md](./contracts/internal-services.md).
- [X] T008 [P] Create [src/Ato.Copilot.Core/Services/Roles/OrganizationRoleToRmfRoleMap.cs](../../src/Ato.Copilot.Core/Services/Roles/OrganizationRoleToRmfRoleMap.cs) — static helper with `TryMap(OrganizationRole) → RmfRole?` and `TryMap(RmfRole) → OrganizationRole?` per [data-model.md § Cross-enum mapping](./data-model.md#cross-enum-mapping-organizationrole--rmfrole). `Assessor ↔ Sca` is the only non-identity edge; `Administrator → null` on the C#-to-RMF direction.
- [X] T009 [P] Write failing unit test [tests/Ato.Copilot.Tests.Unit/Roles/OrganizationRoleToRmfRoleMapTests.cs](../../tests/Ato.Copilot.Tests.Unit/Roles/OrganizationRoleToRmfRoleMapTests.cs) — 7 identity cells + `Assessor↔Sca` + `Administrator→null` + round-trip stability. AAA markers.
- [X] T010 Create [src/Ato.Copilot.Core/Observability/RoleMetrics.cs](../../src/Ato.Copilot.Core/Observability/RoleMetrics.cs) — singleton `IDisposable` exposing `Meter("Ato.Copilot")` with 3 counters (`legacy_role_endpoint_call_total`, `legacy_role_endpoint_bypass_total`, `sod_violation_warning_total`) and 1 histogram (`org_role_propagation_duration_seconds`) per [contracts/internal-services.md § 5](./contracts/internal-services.md#5-rolemetrics).
- [X] T011 [P] Write failing unit test [tests/Ato.Copilot.Tests.Unit/Roles/RoleMetricsTests.cs](../../tests/Ato.Copilot.Tests.Unit/Roles/RoleMetricsTests.cs) using `MeterListener` to assert instrument names, units, descriptions, and label cardinality bounds. AAA markers.

**Checkpoint**: Foundation ready — every C# project compiles with the extended enum and the new shared types; metrics are wired but not yet emitted. User story 1 implementation can now begin.

---

## Phase 3: User Story 1 — Banner clears when Mission Owner is named at the Org level (Priority: P1) 🎯 MVP

**Goal**: An ISSM names a person as Mission Owner once at the org level. Every system in that tenant reflects the assignment on the next read; the red 30-day banner clears.

**Independent Test**: Given an Org with no Mission Owner and N systems older than 30 days, write a single Org-level Mission Owner assignment; refresh any system detail page; verify `profileCompleteness.missionOwnerAssigned == true` with no per-system action.

**Drives**: FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-015, FR-017, FR-022, FR-024, FR-026, FR-027, FR-028, FR-029 · SC-001 (in part), SC-002, SC-006, SC-008, SC-009 (partial — server-side), SC-011

### Tests for User Story 1 — write FIRST, ensure they FAIL ⚠️

- [X] T012 [P] [US1] Failing unit test [tests/Ato.Copilot.Tests.Unit/Roles/UnifiedRoleReaderTests.cs](../../tests/Ato.Copilot.Tests.Unit/Roles/UnifiedRoleReaderTests.cs) — combinatorial precedence-chain coverage per [data-model.md § Read-time precedence](./data-model.md#read-time-precedence-fr-003-encoded-by-iunifiedrolereader): override > inherited > org-fallback > legacy > not-assigned. Includes `IsPrimary` tie-break and most-recent `CreatedAt` tie-break. AAA markers.
- [X] T013 [P] [US1] Failing unit test [tests/Ato.Copilot.Tests.Unit/Roles/RoleAuthorizationServiceTests.cs](../../tests/Ato.Copilot.Tests.Unit/Roles/RoleAuthorizationServiceTests.cs) — `[Theory]` enumerating: (a) all 6 × 6 = 36 RmfRole × RmfRole cells per the FR-027 matrix, (b) 6 `CallerEffectiveRole.None` (null caller) × RmfRole cells — all denied, (c) 6 `IsTenantAdministrator=true` bypass cells — all allowed, (d) 6 bootstrap-flag cells — all allowed. Asserts `AuthorizationResult.Allowed` and `DeniedReason` text per [contracts/internal-services.md § 2](./contracts/internal-services.md#2-iroleauthorizationservice). AAA markers.
- [X] T014 [P] [US1] Failing unit test [tests/Ato.Copilot.Tests.Unit/Roles/SoDConflictDetectorTests.cs](../../tests/Ato.Copilot.Tests.Unit/Roles/SoDConflictDetectorTests.cs) — conflict pairs from FR-026 (`AuthorizingOfficial` × {`SystemOwner`, `Issm`, `Isso`}; `Sca` × {`Issm`, `Isso`, `SystemOwner`}) plus 5 non-conflict pairs. Uses `Microsoft.EntityFrameworkCore.InMemory` provider. AAA markers.
- [X] T015 [P] [US1] Failing unit test [tests/Ato.Copilot.Tests.Unit/Roles/OrganizationRoleFanoutQueueTests.cs](../../tests/Ato.Copilot.Tests.Unit/Roles/OrganizationRoleFanoutQueueTests.cs) — bounded-channel contract: enqueue under capacity succeeds; enqueue at capacity blocks under `BoundedChannelFullMode.Wait`; `ChannelReader.ReadAllAsync` drains in FIFO order. AAA markers.
- [X] T016 [P] [US1] Failing integration test [tests/Ato.Copilot.Tests.Integration/Roles/OrganizationRoleFanoutWorkerTests.cs](../../tests/Ato.Copilot.Tests.Integration/Roles/OrganizationRoleFanoutWorkerTests.cs) — Arrange 500 `RegisteredSystem` rows + 1 active `OrganizationRoleAssignment`. Enqueue a single intent. Drain. Assert 500 inherited `SystemRoleAssignment` rows materialized with `IsInherited=true` + matching `SourceOrganizationRoleAssignmentId`. Re-enqueue the same intent — assert idempotent (no duplicate rows). Assert startup reconciliation produces the same end-state when intents are missing. AAA markers.
- [X] T017 [P] [US1] Failing integration test [tests/Ato.Copilot.Tests.Integration/Roles/TenantIsolationRolesTests.cs](../../tests/Ato.Copilot.Tests.Integration/Roles/TenantIsolationRolesTests.cs) — Seed two tenants with disjoint role rows. Assert `IUnifiedRoleReader.GetSystemRolesAsync` for Tenant A's systems returns ONLY Tenant A's persons; cross-tenant query returns 7 × `NotAssigned`. Drives FR-004, SC-006. AAA markers.
- [X] T017a [P] [US1] Failing integration test [tests/Ato.Copilot.Tests.Integration/Roles/OrgRoleSoftRemoveCascadeTests.cs](../../tests/Ato.Copilot.Tests.Integration/Roles/OrgRoleSoftRemoveCascadeTests.cs) — Seed 1 active `OrganizationRoleAssignment` for role `MissionOwner` + 3 inherited `SystemRoleAssignment` rows + 1 per-system override row (`IsInherited=false`). Soft-remove the Org row via `OrganizationRoleAssignmentService`. Assert: (a) all 3 inherited rows are soft-removed in the same `SaveChangesAsync` (RemovedAt non-null, matching timestamp), (b) the override row is preserved. Drives FR-007. AAA markers.
- [X] T018 [P] [US1] Failing integration test [tests/Ato.Copilot.Tests.Integration/Roles/RoleAuthorizationMatrixCoverageTests.cs](../../tests/Ato.Copilot.Tests.Integration/Roles/RoleAuthorizationMatrixCoverageTests.cs) — `WebApplicationFactory<Program>`-based generator that iterates every disallowed cell from the FR-027 matrix and asserts HTTP 403 with envelope `error.code == "RBAC_ROLE_ASSIGN_DENIED"`, `error.callerEffectiveRole` and `error.targetRole` set. Drives SC-009. AAA markers.
- [X] T018a [P] [US1] Failing unit test [tests/Ato.Copilot.Tests.Unit/Roles/CallerEffectiveRoleResolverTests.cs](../../tests/Ato.Copilot.Tests.Unit/Roles/CallerEffectiveRoleResolverTests.cs) — Asserts the resolver returns a `CallerEffectiveRole` struct carrying (a) the **highest-privileged** `RmfRole?` per the gradient `Issm > Isso > {AO, Sca, SystemOwner, MissionOwner}` and (b) `IsTenantAdministrator: bool` set true when the caller holds an active `OrganizationRole.Administrator` row (per [contracts/internal-services.md § 6](./contracts/internal-services.md#6-icallereffectiveroleresolver)). Includes: (a) caller with zero roles → `CallerEffectiveRole.None`, (b) caller with `Isso` only → `(Isso, false)`, (c) caller with `{Isso, Issm}` → `(Issm, false)`, (d) caller with Administrator only → `(null, true)`, (e) caller with `{Administrator, Issm}` → `(Issm, true)` (both fields populated), (f) tenant isolation (roles held in a different tenant MUST NOT count), (g) legacy `RmfRoleAssignment` rows are honored as a fallback source. Drives FR-027. AAA markers.
- [X] T018b [P] [US1] Failing integration test [tests/Ato.Copilot.Tests.Integration/Roles/NewSystemInitializesInheritedRolesTests.cs](../../tests/Ato.Copilot.Tests.Integration/Roles/NewSystemInitializesInheritedRolesTests.cs) — Seed a tenant with 3 active `OrganizationRoleAssignment` rows (one for `MissionOwner`, `AuthorizingOfficial`, `SystemOwner`). Create a new `RegisteredSystem` via the production code path (NOT raw `DbSet.Add`). Assert: (a) 3 inherited `SystemRoleAssignment` rows are present for the new system with `IsInherited=true` and `SourceOrganizationRoleAssignmentId` pointing at the matching Org row, (b) they are visible **synchronously** on the next `IUnifiedRoleReader.GetSystemRolesAsync` call (no worker dependency), (c) re-running creation for the same system is idempotent (no duplicates), (d) Org rows from a different tenant are NOT inherited. Drives FR-005, FR-015. AAA markers.

### Implementation for User Story 1

- [X] T019 [P] [US1] Create [src/Ato.Copilot.Core/Services/Roles/IUnifiedRoleReader.cs](../../src/Ato.Copilot.Core/Services/Roles/IUnifiedRoleReader.cs) — interface per [contracts/internal-services.md § 1](./contracts/internal-services.md#1-iunifiedrolereader): `GetSystemRolesAsync(tenantId, systemId, ct)` and `GetMissionOwnerAsync(tenantId, systemId, ct)`.
- [X] T020 [US1] Create [src/Ato.Copilot.Core/Services/Roles/UnifiedRoleReader.cs](../../src/Ato.Copilot.Core/Services/Roles/UnifiedRoleReader.cs) — resolves the 5-step precedence chain (override → inherited → org-fallback → legacy → not-assigned). Navigates `Person` for display name. MUST be N+1-free. **Storage-format note (A2)**: `OrganizationRoleAssignment.Role` is persisted as `nvarchar(32)` (`HasConversion<string>()`) while `SystemRoleAssignment.Role` is persisted as `int` and `RmfRoleAssignment.Role` is also `int`. A single SQL `LEFT JOIN` with role equality across all three tables would emit a server-side `CAST` and defeat indexes. Instead, issue **three tenant-scoped per-system fetches in parallel** (`Task.WhenAll`) — one per table, each keyed on `(TenantId, SystemId)` — then zip the three result sets in memory by `RmfRole`. The whole resolve is `O(7)` per system after the three round-trips. (depends on T019)
- [X] T020a [P] [US1] Create [src/Ato.Copilot.Core/Services/Roles/ICallerEffectiveRoleResolver.cs](../../src/Ato.Copilot.Core/Services/Roles/ICallerEffectiveRoleResolver.cs) — single method `ValueTask<CallerEffectiveRole> ResolveAsync(Guid tenantId, Guid principalPersonId, CancellationToken ct)` per [contracts/internal-services.md § 6](./contracts/internal-services.md#6-icallereffectiveroleresolver). Returns a struct carrying (a) the highest-privileged `RmfRole?` the caller currently holds for the tenant, AND (b) `IsTenantAdministrator: bool` set true when the caller holds an active `OrganizationRole.Administrator` row. (`CallerEffectiveRole` is declared in `RoleRecordTypes.cs` per T007 — update T007 if not yet present.)
- [X] T020b [US1] Create [src/Ato.Copilot.Core/Services/Roles/CallerEffectiveRoleResolver.cs](../../src/Ato.Copilot.Core/Services/Roles/CallerEffectiveRoleResolver.cs) — implementation. Tenant-scoped union read across `OrganizationRoleAssignment` + `SystemRoleAssignment` + legacy `RmfRoleAssignment` filtered by `(TenantId, PersonId, RemovedAt is null)`. From `OrganizationRoleAssignment` rows: set `IsTenantAdministrator=true` when any row has `Role == OrganizationRole.Administrator`; all other rows contribute to the RmfRole reduction via `OrganizationRoleToRmfRoleMap.TryMap` (T008). Apply privilege gradient `Issm > Isso > {AO, Sca, SystemOwner, MissionOwner}` over the 6-value RmfRole space and return the max (or `null` if empty) in `CallerEffectiveRole.RmfRole`. Both fields are independent — a caller may hold BOTH Administrator and an RmfRole-bearing row. Inject `IDbContextFactory<AtoCopilotContext>` per the singleton pattern used by `WizardJobHostedService`. Cache results per request scope only — do NOT cache across requests (role assignments mutate in this very feature). (depends on T020a, T008)
- [X] T021 [P] [US1] Create [src/Ato.Copilot.Core/Services/Roles/IRoleAuthorizationService.cs](../../src/Ato.Copilot.Core/Services/Roles/IRoleAuthorizationService.cs) per [contracts/internal-services.md § 2](./contracts/internal-services.md#2-iroleauthorizationservice).
- [X] T022 [US1] Create [src/Ato.Copilot.Core/Services/Roles/RoleAuthorizationService.cs](../../src/Ato.Copilot.Core/Services/Roles/RoleAuthorizationService.cs) — pure-functional, no DB I/O; matrix encoded as `static readonly ImmutableDictionary<RmfRole, ImmutableHashSet<RmfRole>>` over 6 RmfRole keys (no Administrator key — see § 2 design note); `Authorize(CallerEffectiveRole, RmfRole, bool)` short-circuits to Allowed when (a) `isBootstrapSession=true` or (b) `caller.IsTenantAdministrator=true`, otherwise consults the matrix using `caller.RmfRole` as the key (returns Denied when key is null). (depends on T021)
- [X] T023 [P] [US1] Create [src/Ato.Copilot.Core/Services/Roles/ISoDConflictDetector.cs](../../src/Ato.Copilot.Core/Services/Roles/ISoDConflictDetector.cs) per [contracts/internal-services.md § 3](./contracts/internal-services.md#3-isodconflictdetector).
- [X] T024 [US1] Create [src/Ato.Copilot.Core/Services/Roles/SoDConflictDetector.cs](../../src/Ato.Copilot.Core/Services/Roles/SoDConflictDetector.cs) — single tenant-scoped query: `select Role from OrganizationRoleAssignments where TenantId=@tid and PersonId=@pid and RemovedAt is null`; cross-product against static SoD pairs; emits `SoDWarning` per match with `DodiReference="DoDI 8510.01 Enclosure 3 § 4.b"`. (depends on T023)
- [X] T025 [P] [US1] Create [src/Ato.Copilot.Core/Services/Roles/IOrganizationRoleFanoutQueue.cs](../../src/Ato.Copilot.Core/Services/Roles/IOrganizationRoleFanoutQueue.cs) per [contracts/internal-services.md § 4](./contracts/internal-services.md#4-iorganizationrolefanoutqueue--organizationrolefanoutworker).
- [X] T026 [US1] Create [src/Ato.Copilot.Core/Services/Roles/OrganizationRoleFanoutQueue.cs](../../src/Ato.Copilot.Core/Services/Roles/OrganizationRoleFanoutQueue.cs) — wraps `Channel.CreateBounded<PropagationIntent>(new BoundedChannelOptions(1024) { FullMode = Wait, SingleReader = true, SingleWriter = false })`. (depends on T025)
- [X] T027 [US1] Create [src/Ato.Copilot.Mcp/Workers/OrganizationRoleFanoutWorker.cs](../../src/Ato.Copilot.Mcp/Workers/OrganizationRoleFanoutWorker.cs) — `BackgroundService` per [contracts/internal-services.md § 4 worker contract](./contracts/internal-services.md#organizationrolefanoutworker-responsibilities): startup reconciliation sweep → drain channel forever → per-intent idempotent batched insert (100-system batches) → Serilog structured event per iteration → `RoleMetrics.RecordPropagation`. (depends on T010, T026)
- [X] T028 [US1] Modify [src/Ato.Copilot.Agents/Compliance/Services/SystemProfileService.cs](../../src/Ato.Copilot.Agents/Compliance/Services/SystemProfileService.cs) — replace the direct legacy `RmfRoleAssignment` read for Mission Owner resolution with `IUnifiedRoleReader.GetMissionOwnerAsync(tenantId, systemId, ct)`. Keep the same returned shape (`missionOwnerAssigned`, `missionOwnerName`) so the dashboard contract is unchanged. (depends on T020)
- [X] T029 [US1] Modify [src/Ato.Copilot.Agents/Compliance/Services/Onboarding/OrganizationRoleAssignmentService.cs](../../src/Ato.Copilot.Agents/Compliance/Services/Onboarding/OrganizationRoleAssignmentService.cs) — on every Org-role write: (a) call `IRoleAuthorizationService.Authorize(...)` and return 403 envelope on deny, (b) call `ISoDConflictDetector.DetectAsync(...)` and flow warnings into the response envelope (non-blocking), (c) on successful commit, call `IOrganizationRoleFanoutQueue.EnqueueAsync(PropagationIntent)` (fire-and-forget for fan-out), (d) on soft-remove of an Org row, cascade-soft-remove inherited `SystemRoleAssignment` rows in the same `SaveChangesAsync` per FR-007. (depends on T022, T024, T026)
- [X] T029a [US1] Modify the production `RegisteredSystem`-create code path (search `src/Ato.Copilot.Agents/**` for the service that calls `_db.RegisteredSystems.Add(...)` — likely `SystemRegistrationService` or the wizard's Step 4 commit handler) — after the new system is staged but BEFORE `SaveChangesAsync`, query the tenant's active `OrganizationRoleAssignment` rows and stage one inherited `SystemRoleAssignment` per Org row with `IsInherited=true`, `SourceOrganizationRoleAssignmentId=<orgRow.Id>`, `Role=` the cross-enum mapped `RmfRole` (skip Org rows whose `OrganizationRoleToRmfRoleMap.TryMap` returns `null`, i.e., `Administrator`). All staging MUST land in the SAME `SaveChangesAsync` as the system insert (atomic; rollback on failure). Idempotency: if an inherited row already exists for `(TenantId, SystemId, Role, SourceOrganizationRoleAssignmentId)` skip it. Drives FR-005, FR-015. (depends on T008, T020 — needs the cross-enum map and reader contract)
- [X] T030 [US1] Wire DI in [src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs](../../src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs) — register `IOrganizationRoleFanoutQueue` (singleton), `OrganizationRoleFanoutWorker` (hosted, gated on the existing `RegisterHostedServices` flag), `RoleMetrics` (singleton), `IRoleAuthorizationService` (singleton), `ISoDConflictDetector` (scoped), `IUnifiedRoleReader` (scoped), `ICallerEffectiveRoleResolver` (scoped). Place registrations next to `WizardJobChannel` / `WizardJobHostedService` for consistency. (depends on T010, T020, T020b, T022, T024, T026, T027)

**Checkpoint US1**: Run `dotnet test --filter "FullyQualifiedName~Roles"`. All US1 tests above MUST pass. Manually exercise the quickstart steps in [quickstart.md § 4](./quickstart.md#4-user-story-1-walkthrough--wizard-write-clears-banner) — banner clears on next read after Org-level Mission Owner is named (via the existing wizard or an `OrganizationRoleAssignmentService` MCP call). At this point US1 is independently demoable.

---

## Phase 4: User Story 2 — Actionable banner and per-system override surface (Priority: P2)

**Goal**: The banner becomes clickable; the Roles panel shows the full 7-role state with inherited / override indicators; an ISSO can clear the banner from the dashboard in ≤ 3 clicks.

**Independent Test**: With US1 shipped, open a system detail page that shows the banner, click "Assign Mission Owner", pick a person, submit — banner is hidden on next render; the new row has `IsInherited=false`, `SourceOrganizationRoleAssignmentId=null`.

**Drives**: FR-008, FR-009, FR-010, FR-011, FR-012, FR-013 (partial — type extension), FR-021, FR-023, FR-025 (partial — dashboard surfaces), FR-026 (warnings render), FR-027 (client-side affordance hide), FR-029 (banner reads from unified) · SC-003, SC-005, SC-007

### Tests for User Story 2 — write FIRST, ensure they FAIL ⚠️

- [X] T031 [P] [US2] Failing integration test [tests/Ato.Copilot.Tests.Integration/Roles/SystemRolesEndpointsTests.cs](../../tests/Ato.Copilot.Tests.Integration/Roles/SystemRolesEndpointsTests.cs) — exercises `POST/DELETE/GET /api/roles/system/{systemId}` and `POST/DELETE /api/roles/organization` per [contracts/http-api.md](./contracts/http-api.md): envelope shape, 7-role coverage, `source` enum values, `ROLE_INHERITED_NOT_REMOVABLE` 409 on inherited DELETE, `RBAC_ROLE_ASSIGN_DENIED` 403 paths, SoD warning render. AAA markers.
- [X] T032 [P] [US2] Failing integration test [tests/Ato.Copilot.Tests.Integration/Roles/InvalidRoleSuggestionTests.cs](../../tests/Ato.Copilot.Tests.Integration/Roles/InvalidRoleSuggestionTests.cs) — submits the string `"misionowner"` to every write endpoint (legacy + new) and asserts the response `error.suggestion` text contains ALL 7 role names verbatim. Drives FR-012, SC-007. AAA markers.
- [X] T033 [P] [US2] Failing dashboard test [src/Ato.Copilot.Dashboard/src/components/roles/AssignRoleDialog.test.tsx](../../src/Ato.Copilot.Dashboard/src/components/roles/AssignRoleDialog.test.tsx) — `@testing-library/react`: (a) role dropdown filters by `RBAC_ASSIGNABLE_BY[callerEffectiveRole]`, (b) `lockRole=true` disables the role dropdown, (c) SoD warning from the response renders inline, (d) `bootstrap` prop wires `bootstrap=true` into the POST body. AAA markers.
- [X] T034 [P] [US2] Failing dashboard test [src/Ato.Copilot.Dashboard/src/components/cards/RoleAssignmentPanel.test.tsx](../../src/Ato.Copilot.Dashboard/src/components/cards/RoleAssignmentPanel.test.tsx) — renders 7 rows; `Inherited` rows show "Override" button; `Override` rows show "Remove override" button; `OrgFallback` rows show "Pending" badge with tooltip. AAA markers.

### Implementation for User Story 2

- [X] T035 [P] [US2] Create [src/Ato.Copilot.Dashboard/src/types/roles.ts](../../src/Ato.Copilot.Dashboard/src/types/roles.ts) — `RMF_ROLES` const tuple, `RmfRole` union, `RBAC_ASSIGNABLE_BY` table, `RoleAssignmentSource` union, `ResolvedRoleAssignment`, `SystemRolesResponse`, `SoDWarning`, `AssignmentResult` shapes — verbatim from [contracts/frontend-types.md § 1](./contracts/frontend-types.md#1-domain-types--srcatocopilotdashboardsrctypesrolests-new).
- [X] T036 [P] [US2] Create [src/Ato.Copilot.Dashboard/src/lib/api/roles.ts](../../src/Ato.Copilot.Dashboard/src/lib/api/roles.ts) — `rolesApi` object with `getSystemRoles`, `assignSystemRole`, `removeSystemRole`, `assignOrgRole`, `removeOrgRole` per [contracts/frontend-types.md § 6](./contracts/frontend-types.md#6-api-client-functions).
- [X] T037 [US2] Create [src/Ato.Copilot.Dashboard/src/components/roles/AssignRoleDialog.tsx](../../src/Ato.Copilot.Dashboard/src/components/roles/AssignRoleDialog.tsx) — single shared dialog used by banner + Roles panel + wizard. Props per [contracts/frontend-types.md § 2](./contracts/frontend-types.md#2-assignroledialog--shared-component-r8-from-researchmd). Renders SoD warnings; calls `rolesApi.assignSystemRole` or `rolesApi.assignOrgRole` based on `scope.kind`. (depends on T035, T036)
- [X] T038 [US2] Create [src/Ato.Copilot.Mcp/Endpoints/SystemRolesEndpoints.cs](../../src/Ato.Copilot.Mcp/Endpoints/SystemRolesEndpoints.cs) — Minimal-API mapping for `POST /api/roles/system/{systemId}`, `DELETE /api/roles/system/{systemId}/{role}/{personId}`, `GET /api/roles/system/{systemId}`, `POST /api/roles/organization`, `DELETE /api/roles/organization/{role}/{personId}`, and `GET /api/roles/effective` per [contracts/http-api.md § New Unified Endpoints](./contracts/http-api.md#new-unified-endpoints). Authorization via `IRoleAuthorizationService`; the caller's effective role is resolved once per request via `ICallerEffectiveRoleResolver.ResolveAsync(...)` and passed into every `Authorize` call. **Bootstrap guard (security-sensitive)**: when the `POST /api/roles/organization` body sets `bootstrap=true`, the handler MUST verify server-side that `_db.OrganizationRoleAssignments.CountAsync(r => r.TenantId == tid && r.RemovedAt == null) == 0` before passing `isBootstrapSession: true` to `Authorize`. If the precondition fails, the flag MUST be ignored and authz MUST fall through to the FR-027 matrix. SoD warnings via `ISoDConflictDetector`; reads via `IUnifiedRoleReader`. Register the endpoint group in [src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs](../../src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs). (depends on T020b, T022, T024, T029 — services already wired)
- [X] T039 [US2] Modify [src/Ato.Copilot.Dashboard/src/components/cards/RoleAssignmentPanel.tsx](../../src/Ato.Copilot.Dashboard/src/components/cards/RoleAssignmentPanel.tsx) — fetch via `rolesApi.getSystemRoles`; render 7 rows (one per `RmfRole`); render `Inherited` / `Override` / `Pending` / `Legacy` source badges; "Override" / "Remove override" / "Assign" affordances filtered by `RBAC_ASSIGNABLE_BY[callerEffectiveRole]`; opens the shared `AssignRoleDialog`. (depends on T037)
- [X] T040 [US2] Modify the inline Mission Owner banner in [src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx](../../src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx) — keep the banner inline (no component extraction this iteration per Constitution §II Simplicity); add an "Assign Mission Owner" button that opens the shared `AssignRoleDialog` with `scope={kind: 'organization'}`, `initialRole='MissionOwner'`, `lockRole=true`. Recompute `missionOwnerAssigned` from the now-unified profile-completeness flag (already updated server-side in T028). (depends on T037)
- [X] T040a [US2] **Wire `RoleAssignmentPanel` into the page.** In [src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx](../../src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx), import `RoleAssignmentPanel` from `@/components/cards/RoleAssignmentPanel` and render it as a new section below the banner (it is currently orphaned — `grep_search` confirms no existing import). Pass `systemId` and the server-resolved `callerEffectiveRole`. To populate `callerEffectiveRole` on the client, add a new endpoint `GET /api/roles/effective` to [src/Ato.Copilot.Mcp/Endpoints/SystemRolesEndpoints.cs](../../src/Ato.Copilot.Mcp/Endpoints/SystemRolesEndpoints.cs) — handler calls `ICallerEffectiveRoleResolver.ResolveAsync(tenantId, principalPersonId, ct)` and returns `{ effectiveRole: RmfRole | null }`. Add a typed `rolesApi.getEffectiveRole()` to [src/Ato.Copilot.Dashboard/src/lib/api/roles.ts](../../src/Ato.Copilot.Dashboard/src/lib/api/roles.ts). The page fetches it on mount and threads it into both `RoleAssignmentPanel` and `AssignRoleDialog`. Drives FR-008 (wiring), FR-027 (client affordance hiding). (depends on T020b, T038, T039)
- [X] T041 [US2] Update the user-facing invalid-role suggestion string in [src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs](../../src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs) and [src/Ato.Copilot.Mcp/Endpoints/SystemRolesEndpoints.cs](../../src/Ato.Copilot.Mcp/Endpoints/SystemRolesEndpoints.cs) — single shared constant containing all 7 role names in canonical order. Drives FR-012, SC-007. (depends on T038)
- [X] T042 [US2] Run `npm run typecheck` in [src/Ato.Copilot.Dashboard](../../src/Ato.Copilot.Dashboard) and resolve every error in modified files (Constitution § Local Type-Checking Parity, FR-023, SC-005). (depends on T035, T036, T037, T039, T040)

**Checkpoint US2**: All US2 tests pass; manual walkthrough in [quickstart.md § 5](./quickstart.md#5-user-story-2-walkthrough--system-list--roles-panel) succeeds; banner is actionable, Roles panel shows 7 rows with correct source badges.

---

## Phase 5: User Story 3 — Onboarding wizard captures Mission Owner / AO / System Owner (Priority: P3)

**Goal**: Wizard Step 2 exposes inputs for the 3 new Org-level roles. New tenants who name them never see the banner on systems they create.

**Independent Test**: A new tenant completes the wizard with a Mission Owner named in Step 2; the wizard creates a system in Step 4; the system opens with the banner already cleared via inheritance.

**Drives**: FR-013, FR-014, FR-015, FR-016, FR-025 (wizard surface), FR-027 (bootstrap exception) · SC-001 (full)

### Tests for User Story 3 — write FIRST, ensure they FAIL ⚠️

- [ ] T043 [P] [US3] Failing dashboard test [src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step2RoleAssignments.test.tsx](../../src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step2RoleAssignments.test.tsx) — renders 7 role rows (was 4); all 3 new rows are optional (no validation block on submit); submit posts each filled role with `bootstrap=true`. AAA markers.
- [ ] T044 [P] [US3] Failing dashboard test [src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step2RoleAssignments.resume.test.tsx](../../src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step2RoleAssignments.resume.test.tsx) — hydrates from an older saved-state shape (missing the 3 new role fields); component renders without crash; missing fields display as "Unassigned". Drives FR-016. AAA markers.
- [ ] T045 [P] [US3] Failing integration test [tests/Ato.Copilot.Tests.Integration/Roles/WizardBootstrapAuthorizationTests.cs](../../tests/Ato.Copilot.Tests.Integration/Roles/WizardBootstrapAuthorizationTests.cs) — first Org-role write of a session passes with `bootstrap=true` regardless of caller's effective role; second write in the same session evaluates the FR-027 matrix normally. AAA markers.
- [ ] T045b [P] [US3] Failing integration test [tests/Ato.Copilot.Tests.Integration/Roles/WizardBootstrapServerSideGuardTests.cs](../../tests/Ato.Copilot.Tests.Integration/Roles/WizardBootstrapServerSideGuardTests.cs) — **security-sensitive negative test**: seed a tenant with ≥1 existing active `OrganizationRoleAssignment` row (i.e., bootstrap is over). Send `POST /api/roles/organization` with `bootstrap=true` from an authenticated principal whose effective role is `Isso` and a `targetRole` of `AuthorizingOfficial` (a cell that `Isso` MUST NOT be allowed to assign). Assert: (a) HTTP 403 with `error.code == "RBAC_ROLE_ASSIGN_DENIED"`, (b) zero new rows written, (c) `bootstrap=true` was **ignored** because the server verified the precondition `count(OrganizationRoleAssignment WHERE TenantId=X AND RemovedAt IS NULL) == 0` before honoring the flag. Drives FR-027 security boundary. AAA markers.

### Implementation for User Story 3

- [ ] T046 [US3] Modify [src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step2RoleAssignments.tsx](../../src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step2RoleAssignments.tsx) — add 3 new input rows for `MissionOwner`, `AuthorizingOfficial`, `SystemOwner`; extend `WizardStep2RolesState` per [contracts/frontend-types.md § 5](./contracts/frontend-types.md#5-wizardstep2rolesstate--extended-state-shape); add resume-tolerant hydration that defaults missing fields to `null`; on submit, call `rolesApi.assignOrgRole(role, personId, isPrimary=true, bootstrap=true)` for every filled new row (3 max). (depends on T035, T036)
- [ ] T047 [US3] Modify [src/Ato.Copilot.Agents/Compliance/Services/Onboarding/OrganizationRoleAssignmentService.cs](../../src/Ato.Copilot.Agents/Compliance/Services/Onboarding/OrganizationRoleAssignmentService.cs) — accept a `bootstrap: bool` parameter on the write entry point; pass it through to `IRoleAuthorizationService.Authorize(..., isBootstrapSession: bootstrap)`. (depends on T029)
- [ ] T048 [US3] Update [src/Ato.Copilot.Mcp/Endpoints/SystemRolesEndpoints.cs](../../src/Ato.Copilot.Mcp/Endpoints/SystemRolesEndpoints.cs) — accept optional `bootstrap` field on the `POST /api/roles/organization` request body and forward to the service. Bootstrap requests MUST still be authenticated (FR-027 retains "authenticated" — the matrix is the only thing the bootstrap flag bypasses). (depends on T047)
- [ ] T049 [US3] Run `npm run typecheck` in [src/Ato.Copilot.Dashboard](../../src/Ato.Copilot.Dashboard) — every modified TS file passes `tsc --noEmit`. (depends on T046)

**Checkpoint US3**: All US3 tests pass; manual walkthrough — complete the wizard with a Mission Owner named, create a system, open the system, banner is hidden on first load.

---

## Phase 6: User Story 4 — Legacy `RmfRoleAssignment` reconciliation (Priority: P4)

**Goal**: Legacy callers continue working unchanged; the legacy `POST` writes through atomically to both tables; legacy responses carry `Deprecation` + `Sunset` headers; usage telemetry feeds the sunset decision.

**Independent Test**: Take a tenant snapshot containing legacy rows; apply the feature; verify (a) banner state unchanged for every system, (b) a `POST /api/dashboard/systems/{systemId}/roles` succeeds and writes both tables atomically, (c) under fault-injection between the two `Add` calls, BOTH tables roll back.

**Drives**: FR-017, FR-018, FR-019, FR-020, FR-021 · SC-004, SC-010, telemetry feed for sunset planning

### Tests for User Story 4 — write FIRST, ensure they FAIL ⚠️

- [ ] T050 [P] [US4] Failing integration test [tests/Ato.Copilot.Tests.Integration/Roles/LegacyWriteThroughAtomicityTests.cs](../../tests/Ato.Copilot.Tests.Integration/Roles/LegacyWriteThroughAtomicityTests.cs) — uses an `IDbCommandInterceptor` fault-injection harness to throw between the legacy `Add` and the unified `Add` during a single `SaveChangesAsync`. Asserts: (a) the endpoint returns 503 with `error.code == "ROLE_WRITE_THROUGH_FAILED"`, (b) pre-fault and post-fault row counts in `RmfRoleAssignments`, `SystemRoleAssignments`, `OrganizationRoleAssignments` are equal. Drives SC-010. Repeats the cycle 100 times to assert determinism. AAA markers.
- [ ] T051 [P] [US4] Failing integration test [tests/Ato.Copilot.Tests.Integration/Roles/DeprecationHeadersTests.cs](../../tests/Ato.Copilot.Tests.Integration/Roles/DeprecationHeadersTests.cs) — asserts `Deprecation: true`, `Sunset: <RFC 7231 date>` (= `LaunchDate + 90 days`), and `Link: </api/roles/system/...>; rel="successor-version"` headers on every `GET`/`POST`/`DELETE` response from the legacy endpoint, including 4xx and 5xx responses. Drives FR-019. AAA markers.
- [ ] T052 [P] [US4] Failing unit test [tests/Ato.Copilot.Tests.Unit/Roles/LegacyEnumStabilityTests.cs](../../tests/Ato.Copilot.Tests.Unit/Roles/LegacyEnumStabilityTests.cs) — asserts the serialized string and ordinal of every `RmfRole` value matches a checked-in golden fixture. Drives FR-020, SC-004. AAA markers.

### Implementation for User Story 4

- [ ] T053 [US4] Add `LaunchDate` to [src/Ato.Copilot.Mcp/appsettings.json](../../src/Ato.Copilot.Mcp/appsettings.json) (placeholder `""` in committed file) and [src/Ato.Copilot.Mcp/appsettings.Production.json](../../src/Ato.Copilot.Mcp/appsettings.Production.json) (real ISO-8601 string pinned at production cutover) — inline comment: `// Production launch date; do NOT change after cutover. Drives Sunset header = LaunchDate + 90 days per FR-019.` In [src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs](../../src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs), bind a strongly-typed `LaunchOptions` record (`record LaunchOptions(DateTimeOffset? LaunchDateUtc)`) and **fail-fast at startup** when `LaunchDateUtc` is `null` AND the host environment is `Production` or `Staging` (use `IHostEnvironment.IsProduction() || environment.IsStaging()`). Throw `InvalidOperationException("LaunchDate is unset in production; Sunset header would be incorrect (FR-019)")` so the host refuses to start.
- [ ] T054 [US4] Create [src/Ato.Copilot.Mcp/Endpoints/DeprecationHeadersExtensions.cs](../../src/Ato.Copilot.Mcp/Endpoints/DeprecationHeadersExtensions.cs) — `IEndpointConventionBuilder.WithDeprecationHeaders(DateTimeOffset launchDateUtc)` extension that adds an endpoint filter writing the three headers on every response (including non-2xx). Per R7 from [research.md](./research.md). (depends on T053)
- [ ] T055 [US4] Modify [src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs](../../src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs) — for `POST` and `DELETE`: stage both the legacy `RmfRoleAssignment` mutation AND the equivalent unified `SystemRoleAssignment` mutation (with `IsInherited=false`) on the SAME `AtoCopilotContext`; commit with a SINGLE `SaveChangesAsync`. On exception, return 503 with envelope `error.code = "ROLE_WRITE_THROUGH_FAILED"`. Apply `WithDeprecationHeaders(launchDateUtc)` to all three legacy routes (`GET`, `POST`, `DELETE`). Call `IRoleAuthorizationService.Authorize(...)` for telemetry only (do NOT block); increment `legacy_role_endpoint_bypass_total` when the matrix would have denied. Increment `legacy_role_endpoint_call_total` on every call. Invoke `ISoDConflictDetector.DetectAsync(...)` and surface warnings in the response envelope. **Metrics scrape verification (O1)**: as part of the task's manual acceptance, hit the existing OpenTelemetry / Prometheus `/metrics` endpoint in dev (after sending one `POST` and one `DELETE` to the legacy route) and confirm both `legacy_role_endpoint_call_total` and `legacy_role_endpoint_bypass_total` appear in the scrape output with the expected labels. Capture the output in the PR description. (depends on T010, T022, T024, T054)
- [ ] T056 [US4] Verify the existing legacy MCP-tool description strings in [src/Ato.Copilot.Agents/Tools/**](../../src/Ato.Copilot.Agents/Tools/) — update only the user-facing suggestion text where it enumerates valid roles, per FR-025. **No envelope shape changes.** Drives SC-007 (string drift detector also catches any miss).

**Checkpoint US4**: All US4 tests pass; manual walkthrough [quickstart.md § 7](./quickstart.md#7-user-story-4-walkthrough--legacy-endpoint-deprecation--write-through-atomicity-fr-018--fr-019--sc-004--sc-010) succeeds; both tables receive the row atomically; deprecation headers visible.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Cross-story drift prevention, documentation, and final validation.

- [ ] T057 [P] Audit [src/Ato.Copilot.Agents/Tools/](../../src/Ato.Copilot.Agents/Tools/) for any MCP-tool description string that enumerates RMF roles; update each to the canonical 7-role list per FR-025. The SC-007 drift-prevention test from T032 catches missed surfaces.
- [ ] T057a [P] Add automated drift-prevention unit test [tests/Ato.Copilot.Tests.Unit/Tools/RoleEnumerationDriftTests.cs](../../tests/Ato.Copilot.Tests.Unit/Tools/RoleEnumerationDriftTests.cs) — reflection-scan every `BaseTool` subclass under `src/Ato.Copilot.Agents/Tools/`, read each tool's `Description` and `Parameters` schema, regex-search for any role-name enumeration (`MissionOwner|AuthorizingOfficial|SystemOwner|Issm|Isso|Sca|Administrator`); if a tool description mentions ANY role name from the set, it MUST mention ALL 7. Fail with a descriptive message naming the offending tool. Drives FR-025, SC-007. AAA markers.
- [ ] T058 [P] Add a new SSP front-matter regression test [tests/Ato.Copilot.Tests.Integration/Roles/SspFrontMatterPartyTypeTests.cs](../../tests/Ato.Copilot.Tests.Integration/Roles/SspFrontMatterPartyTypeTests.cs) — when the tenant has named individuals for Mission Owner / AO / System Owner, the OSCAL `party-uuid` references resolve to `party type=person` entries (never `type=organization`). Drives SC-008. AAA markers.
- [ ] T058a Rewire the SSP / OSCAL `party` exporter to source role assignments from `IUnifiedRoleReader` instead of reading `RmfRoleAssignment` directly. Search `src/Ato.Copilot.Agents/Compliance/Services/Ssp/` (and any `OscalExportService` / `SspFrontMatterBuilder` siblings) for the existing role-resolution call. For each of `MissionOwner`, `AuthorizingOfficial`, `SystemOwner`, `Issm`, `Isso`, `Sca`: call `IUnifiedRoleReader.GetSystemRolesAsync(tenantId, systemId, ct)`, find the matching `ResolvedRoleAssignment`, emit a `party type=person` element (NEVER `party type=organization`) referencing the resolved `Person`. When `source == 'not-assigned'`, omit the role from the export (do not emit a placeholder organization). Drives SC-008 (makes T058 pass for Org-level-only tenants). (depends on T020 — needs the reader)
- [ ] T059 [P] Re-run the full legacy test suite — `dotnet test --filter "FullyQualifiedName~RmfRoleAssignment|LegacyRole"` — and assert the pre-feature passing test count equals the post-feature passing count. Drives SC-004.
- [ ] T060 [P] Update [docs/rmf-phases/](../../docs/rmf-phases/) and [docs/dev/contributing.md](../../docs/dev/contributing.md) with: (a) the new unified-reader architecture diagram, (b) the role-tiered authorization matrix, (c) the 90-day deprecation timeline. Add the canonical 7-role list to the developer reference.
- [ ] T061 Run the end-to-end [quickstart.md](./quickstart.md) manually on a clean Docker stack; capture screenshots for the persona test cases under [docs/persona-test-cases/feature-049-unified-rmf-role-assignments.md](../../docs/persona-test-cases/feature-049-unified-rmf-role-assignments.md) (new file).
- [ ] T062 Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` — both MUST be clean.
- [ ] T063 Run `npm run typecheck && npm run build` in [src/Ato.Copilot.Dashboard](../../src/Ato.Copilot.Dashboard) and in [extensions/vscode](../../extensions/vscode) (if MCP tool description strings were updated in T057, the VS Code extension's bundled descriptions may need re-bundling). Drives SC-005.

---

## Dependencies & Execution Order

### Phase dependencies

```text
Setup (Phase 1)
    │
    ▼
Foundational (Phase 2)              ◄── BLOCKS everything below
    │
    ▼
US1 / P1  (Phase 3)                 ◄── reader + worker + authz + SoD + DI
    │
    ▼
US2 / P2  (Phase 4)                 ◄── dashboard UI; depends on US1's services
    │
    ▼
US3 / P3  (Phase 5)                 ◄── wizard; depends on US1's services + US2's dialog
    │
    ▼
US4 / P4  (Phase 6)                 ◄── legacy write-through + deprecation headers + telemetry
    │
    ▼
Polish (Phase 7)
```

Per [plan.md § Implementation Phasing](./plan.md#implementation-phasing), the four user stories are sequenced US1 → US2 → US3 → US4. US2 reuses `AssignRoleDialog` which US3 also consumes; US2 must complete first.

### Within each user story

- Tests (T012–T018b for US1, T031–T034 for US2, T043–T045b for US3, T050–T052 for US4) MUST be written and MUST fail before their implementation tasks run.
- Within a story's implementation: interfaces (e.g., T019, T020a, T021, T023, T025, T035) before implementations (T020, T020b, T022, T024, T026, T036); services before endpoints (T020, T020b, T022, T024 before T038); endpoints before UI consumers (T038 before T039, T040, T040a).

### Parallel opportunities

Setup (T001–T004) — all 4 parallel. T004b sequential (verifies the runner; subsequent dashboard test tasks depend on it).
Foundational test-and-helper writes (T006, T007, T008, T009, T011) — 5 parallel after T005 lands. T010 sequential (RoleMetrics referenced by later tasks).
US1 tests (T012–T018b) — 11 parallel.
US1 interfaces (T019, T020a, T021, T023, T025) — 5 parallel; T035 already-parallel-on-dashboard side.
US2 tests (T031–T034) — 4 parallel.
US3 tests (T043, T044, T045, T045b) — 4 parallel.
US4 tests (T050, T051, T052) — 3 parallel.
Polish (T057, T057a, T058, T059, T060) — 5 parallel.

---

## Parallel example: User Story 1 test landing

```bash
# After T010 (RoleMetrics) is on disk, all 11 US1 tests can be written in parallel
# by different team members (or by the same engineer in 11 quick edits):
# T012 — UnifiedRoleReaderTests.cs
# T013 — RoleAuthorizationServiceTests.cs
# T014 — SoDConflictDetectorTests.cs
# T015 — OrganizationRoleFanoutQueueTests.cs
# T016 — OrganizationRoleFanoutWorkerTests.cs
# T017 — TenantIsolationRolesTests.cs
# T017a — OrgRoleSoftRemoveCascadeTests.cs (FR-007)
# T018 — RoleAuthorizationMatrixCoverageTests.cs
# T018a — CallerEffectiveRoleResolverTests.cs
# T018b — NewSystemInitializesInheritedRolesTests.cs (FR-005, FR-015)
dotnet test --filter "FullyQualifiedName~Roles" # ALL should fail at this point (TDD red)
```

---

## Phase 8: Issue #937 — Mission Owner workflow regression

- [x] T064 Add failing tenant-scoped Entra OID resolution tests in `tests/Ato.Copilot.Tests.Unit/Roles/CallerEffectiveRoleResolverTests.cs`
- [x] T065 Add failing unified Mission Owner completeness tests in `tests/Ato.Copilot.Tests.Unit/Compliance/SystemProfileServiceTests.cs`
- [x] T066 [P] Add failing Mission Owner option and permission-explanation component tests under `src/Ato.Copilot.Dashboard/src/__tests__/`, including the organization onboarding role-assignment step
- [x] T067 [P] Add failing Roles & Permissions navigation coverage under `src/Ato.Copilot.Dashboard/src/__tests__/`
- [x] T068 Resolve Entra OIDs to tenant-scoped internal Person IDs in `src/Ato.Copilot.Core/Services/Roles/CallerEffectiveRoleResolver.cs`
- [x] T069 Read Mission Owner completeness through `IUnifiedRoleReader` in `src/Ato.Copilot.Agents/Compliance/Services/SystemProfileService.cs`
- [x] T070 Use the canonical seven-role domain in `src/Ato.Copilot.Dashboard/src/components/cards/GateActionDialog.tsx`, `src/Ato.Copilot.Dashboard/src/components/wizard/steps/AssignRoles.tsx`, and the organization onboarding role-assignment API/UI; keep invalid-role guidance aligned with all seven roles
- [x] T071 Add Roles & Permissions navigation and assignment permission explanation in `src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx` and role-assignment surfaces
- [x] T072 Add Chromium coverage for system override persistence, organization fallback, and read-only guidance; verify authorization and tenant isolation in backend integration/unit tests
- [x] T073 Run focused unit, integration, Vitest, type-check, build, and Chromium E2E validation for issue #937; record unrelated full-dashboard baseline failures in the PR

---

## Implementation strategy

**MVP**: complete Phases 1–3 (Setup, Foundational, US1). At that point the banner clears for every tenant that names a Mission Owner via the existing Org-level surface (wizard or MCP tool). This is the customer's primary complaint resolved. The dashboard banner remains read-only — that's US2 territory — but the underlying loop is closed.

**Increment 2**: US2. Banner becomes actionable; Roles panel ships. This is the highest-leverage user-experience win after the MVP.

**Increment 3**: US3. Wizard captures the 3 new roles for new tenants. The customer impact is preventative ("new tenants never hit the bug"), so it follows the curative US1+US2.

**Increment 4**: US4. Legacy reconciliation, atomicity, deprecation headers, telemetry. Customer-invisible but mandatory for clean sunset planning.

**Polish**: Phase 7. Drift prevention, regression assertions, docs, manual quickstart pass.

---

## Format validation

Every task above:

- ✅ starts with `- [ ]`
- ✅ has a sequential `T###` ID
- ✅ uses `[P]` only for in-phase parallelizable tasks
- ✅ uses `[USx]` ONLY for user-story-phase tasks (Setup, Foundational, Polish have no story label)
- ✅ includes an exact file path

**Total**: 74 tasks across 7 phases.

**Per-story breakdown**:
- Setup: 5 (T001–T004, T004b)
- Foundational: 7 (T005–T011)
- US1 (P1) — MVP: 25 (10 tests — T012–T018, T017a, T018a, T018b — plus 15 implementation — T019–T030, T020a, T020b, T029a)
- US2 (P2): 13 (4 tests + 9 implementation — includes T040a wiring + `/api/roles/effective` endpoint)
- US3 (P3): 8 (4 tests + 4 implementation — includes T045b bootstrap server-side guard test)
- US4 (P4): 7 (3 tests + 4 implementation)
- Polish: 9 (T057–T063, T057a, T058a)

**Constitution gate**: PASS (re-verified post-design). TDD ordering enforced. No §II/§III deviations. No `[NEEDS CLARIFICATION]` markers remain — see [Q1–Q5 in spec.md § Clarifications](./spec.md#session-2026-05-19).
