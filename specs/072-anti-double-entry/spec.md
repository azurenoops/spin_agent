# Spec 072 — Anti-Double-Entry: SPIN/eMASS Sync Status

**Epic:** #60 — Minimize Double Work (SPIN/eMASS)
**GitHub Issue:** #60
**Wave:** 9 — Core UX & Integrations
**Status:** Draft
**Branch:** `072-anti-double-entry`
**Feature Predecessor:** Feature 071 (Issue #59) — eMASS round-trip sync & readiness check

---

## Background

ISSOs maintaining both eMASS (DoD system of record) and SPIN (ATO Copilot workspace) must
currently duplicate data entry in both systems. When eMASS data is imported into SPIN via
the onboarding wizard (Feature 047), fields like `system name`, `DitprId`, security
categorization levels, baseline, and control counts are pre-populated — but SPIN has no
mechanism to:

1. Alert the ISSO that a field was sourced from eMASS (so they know editing it creates divergence)
2. Track whether a key field has since diverged from the last-imported eMASS value
3. Show a rollup "sync status" badge on the system overview that communicates import freshness

Feature 071 (Issue #59) addresses the **backend sync mechanism** — the round-trip pipeline,
readiness checks, and export triggers. Feature 072 (this spec) addresses the **UX/workflow side**:

- Field-level inline warnings when an eMASS-sourced field is edited
- Pre-population completeness enforcement for eMASS-imported fields
- Divergence detection for key fields vs last-imported eMASS values
- Sync status badge on the System Overview page: Green=In sync, Yellow=Diverged, Red=Missing

These two features are **complementary, not duplicate**. #59 moves data; #72 helps the ISSO
understand the data lineage and avoid redundant work.

### eMASS-Sourced Fields in Scope

The following fields are tracked for divergence (sourced during eMASS import via
`EmassImportSession` → `EmassCommitJobHandler`):

| SPIN Field | eMASS Source Column | Entity |
|---|---|---|
| `RegisteredSystem.Name` | `system_name` | `RegisteredSystem` |
| `RegisteredSystem.Acronym` | `system_acronym` (if present) | `RegisteredSystem` |
| `SecurityCategorization.OverallLevel` | `categorization` / impact level | `SecurityCategorization` |
| `ControlBaseline.BaselineType` | `baseline` / control count | `ControlBaseline` |
| `RegisteredSystem.DitprId` | `system_identifier` (DoD IT Portfolio Repository ID) | `RegisteredSystem` |
| `RegisteredSystem.SystemType` | `system_type` | `RegisteredSystem` |

---

## Verified Source Facts

The following facts were verified against actual source code before authoring this spec.
Do not re-verify; trust these facts during implementation.

**`RegisteredSystem` model** (`src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`):
- `Id: string` PK (GUID string)
- `TenantId: Guid` (TenantScoped)
- `Name: string` `[MaxLength(200)]`
- `Acronym: string?` `[MaxLength(20)]`
- `SystemType: SystemType` enum
- `Description: string?` `[MaxLength(2000)]`
- `MissionCriticality: MissionCriticality` enum
- `IsNationalSecuritySystem: bool`
- `HostingEnvironment: string` `[MaxLength(100)]`
- `CurrentRmfStep: RmfPhase`
- No existing `DitprId` or `EmassSourcedAt` fields — these must be added

**`SecurityCategorization` model** (`src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`):
- `Id: string` PK, `TenantId: Guid`, `RegisteredSystemId: string`
- `IsNationalSecuritySystem: bool`
- `Justification: string?` `[MaxLength(4000)]`
- `CategorizedBy: string` `[MaxLength(200)]`
- No existing `EmassSourcedAt` or `EmassValue` fields — these must be added

**`EmassImportSession`** (`src/Ato.Copilot.Core/Models/Onboarding/EmassImportSession.cs`):
- `Id: Guid` PK, `TenantId: Guid`
- `OriginalFileName: string`, `StorageBlobKey: string`
- `ContentChecksumSha256: string` — used as version tag
- `Status: EmassImportStatus` (Uploaded → Parsing → Parsed → Importing → Imported → Failed)
- `CreatedAt: DateTimeOffset`, `UpdatedAt: DateTimeOffset`

**`EmassImportParser`** (`src/Ato.Copilot.Agents/Compliance/Services/Onboarding/Emass/EmassImportParser.cs`):
- Parses XLSX or PackageZip
- Column headers (case-insensitive): `system_identifier`, `system_name`; optional: `controls`, `poams`
- Per FR-031: malformed systems flagged via `EmassParsedSystem.MalformedReason`

**`AtoCopilotContext`** — contains `DbSet<RegisteredSystem>`, `DbSet<SecurityCategorization>`,
`DbSet<EmassImportSession>`. `RegisteredSystem` uses string PK pattern (GUID as string).

**Dashboard** (`src/Ato.Copilot.Dashboard/src`): React/Vite/TypeScript, Tailwind CSS.
Existing `SystemLayout.tsx` sidebar, `App.tsx` routes, `PageLayout`/`PageHero` pattern.

---

## User Stories

### US1 (P1): Field-Level eMASS Origin Warning

**As an ISSO**, when I edit a field in SPIN that was originally imported from eMASS,
I want to see an inline notice that tells me when it was imported and that editing here
won't update eMASS, so I understand the data lineage and make informed edits.

**Acceptance Criteria:**

1. When the ISSO focuses a SPIN form field that has an `EmassFieldSnapshot` record for that
   field, an inline banner renders below the field reading:
   "📥 Imported from eMASS on [date]. Editing here won't update eMASS."
2. The banner shows the date in local timezone using the format `MMM D, YYYY` (e.g., `Jun 5, 2026`).
3. The banner renders for fields: system name, acronym, DitprId, security categorization level,
   baseline type — any field with a matching `EmassFieldSnapshot` record.
4. The banner does not render if no `EmassFieldSnapshot` exists for the field
   (i.e., field was entered manually, not imported).
5. The banner does not block input or validation — it is informational only.
6. If the current SPIN value diverges from the `EmassFieldSnapshot.EmassValue`, the banner
   text changes to: "⚠️ Diverged from eMASS value (imported [date]): eMASS had '[emassValue]'."
7. The banner is accessible: `role="note"`, `aria-label="eMASS import notice"`.
8. The banner can be dismissed per-session (preference stored in `localStorage`; does not
   affect the divergence badge on the overview page).

**Edge Cases:**

- Field has an `EmassFieldSnapshot` but `EmassValue` is null/empty: show origin warning
  without the "eMASS had '...'" diverged fragment.
- ISSO dismisses the banner, then navigates away and returns: banner is hidden per-session
  (localStorage key: `emass-field-banner-dismissed-{fieldName}-{systemId}`).
- Multiple imports: only the most recent `EmassFieldSnapshot` row per `(SystemId, FieldName)` is used.

---

### US2 (P1): Pre-Population Completeness

**As an ISSO**, when eMASS data has been imported for my system, I want SPIN to pre-populate
all fields it can derive from the import, so I don't have to re-enter data already in eMASS.

**Acceptance Criteria:**

1. After a successful `EmassImportSession` commit, the following SPIN fields are pre-populated
   (if not already set) from the eMASS import data:
   - `RegisteredSystem.Name` ← `system_name`
   - `RegisteredSystem.Acronym` ← `system_acronym` (if column present)
   - `RegisteredSystem.DitprId` ← `system_identifier`
   - `SecurityCategorization.OverallLevel` ← impact level derived from eMASS categorization field
   - `ControlBaseline.BaselineType` ← derived from eMASS baseline/control-count column
2. For each pre-populated field, an `EmassFieldSnapshot` record is created with:
   `FieldName`, `EmassValue` (raw string from import), `ImportedAt`, `ImportSessionId`.
3. Fields that are already populated in SPIN are **not overwritten** unless the ISSO
   explicitly triggers a re-sync (out of scope for this feature; defer to Feature 071).
4. A toast notification confirms: "✓ [N] fields pre-populated from eMASS import."
5. The pre-population happens synchronously within the `EmassCommitJobHandler` pipeline,
   after the core import is committed (FR-094 / existing commit handler phase).

**Edge Cases:**

- eMASS categorization column is absent: `SecurityCategorization.OverallLevel` is not
  pre-populated; no snapshot created for that field.
- `system_identifier` is blank/malformed: `DitprId` is not set; log a structured warning.
- Pre-population fails for one field (e.g., enum parse error): other fields still pre-populate;
  failure is logged but does not abort the commit job.

---

### US3 (P1): Divergence Detection for Key Fields

**As an ISSO**, I want SPIN to automatically detect when a SPIN field diverges from its last
imported eMASS value, so I can see at a glance which fields are out of sync without
manually comparing both systems.

**Acceptance Criteria:**

1. After any `RegisteredSystem`, `SecurityCategorization`, or `ControlBaseline` save in SPIN,
   a background check compares the saved field values against their `EmassFieldSnapshot` records.
2. When a divergence is detected (current SPIN value ≠ `EmassFieldSnapshot.EmassValue`),
   the `EmassFieldSnapshot.IsDiverged` flag is set to `true` and `DivergenceDetectedAt`
   is stamped.
3. When the SPIN value is brought back into alignment (or re-imported), `IsDiverged` is set
   to `false` and `ReconciledAt` is stamped.
4. Divergence check runs synchronously at save time (not a background job), targeting < 50 ms
   added latency (only a handful of field lookups via indexed query).
5. The divergence state is reflected immediately on the System Overview sync status badge
   (see US4) without requiring a page reload (React state updated optimistically after save).

**Edge Cases:**

- Divergence check service is called but no `EmassFieldSnapshot` rows exist for the system:
  divergence check is a no-op; no error thrown.
- Type mismatch between eMASS string value and SPIN enum (e.g., eMASS had `"Moderate"` and
  SPIN uses `ImpactLevel.Moderate`): compare after normalizing to canonical string representation.

---

### US4 (P1): Sync Status Badge on System Overview

**As an ISSO**, I want to see a visual sync status badge on the System Overview page that
tells me whether my SPIN data is in sync with eMASS, so I can quickly identify divergence
without opening each field.

**Acceptance Criteria:**

1. The System Overview page (`/systems/:id/overview` or equivalent) shows a "eMASS Sync"
   badge in the header area.
2. Badge states:
   - **Green / "In Sync"** — eMASS import exists and all tracked fields match eMASS values
     (no `EmassFieldSnapshot` with `IsDiverged = true`).
   - **Yellow / "Diverged"** — eMASS import exists but ≥1 tracked field has diverged
     (`IsDiverged = true`). Badge label: "Diverged ([N] field(s))".
   - **Red / "Not Imported"** — no `EmassImportSession` with `Status = Imported` exists for
     this system. Badge label: "Not Imported from eMASS".
3. Clicking the badge opens a `SyncStatusDrawer` panel listing:
   - Import date (last successful `EmassImportSession.UpdatedAt`)
   - Per-field rows: field name, eMASS value, current SPIN value, status (✓ In Sync / ⚠ Diverged)
4. The `SyncStatusDrawer` includes a "View Import History" link to the import sessions list.
4. The badge data is fetched from `GET /api/systems/{id}/emass-sync-status` and
   cached for 60 seconds client-side.
5. The badge is hidden (not rendered) if the user role is SCA or AO (read-only roles
   — they do not own import/sync workflows).
6. The badge renders within 200 ms of page load (non-blocking; loaded asynchronously after
   the main page content).
7. The badge has `data-testid="emass-sync-badge"` and `data-status="in-sync|diverged|not-imported"`.

**Edge Cases:**

- System has no eMASS import but has manually-entered data: Red "Not Imported" badge.
- eMASS import is present but all `EmassFieldSnapshot` rows have been deleted: Red "Not Imported".
- Network error fetching badge status: badge renders a neutral gray "Sync Unknown" state;
  no page crash; error logged to console.
- Multiple systems open in tabs: each tab's badge is independently fetched and cached.

---

## Functional Requirements

| ID | Requirement |
|----|-------------|
| FR-001 | A new `EmassFieldSnapshot` entity MUST be created to store per-field eMASS import metadata: `SystemId`, `FieldName`, `EmassValue`, `ImportedAt`, `ImportSessionId`, `IsDiverged`, `DivergenceDetectedAt`, `ReconciledAt`. |
| FR-002 | `EmassFieldSnapshot` MUST be `TenantScoped` with EF Core query filter. |
| FR-003 | `EmassFieldSnapshot` rows are created/updated in `EmassCommitJobHandler` after the core commit phase, for each pre-populated field. |
| FR-004 | Tracked eMASS fields: `Name`, `Acronym`, `DitprId` on `RegisteredSystem`; `OverallLevel` on `SecurityCategorization`; `BaselineType` on `ControlBaseline`. |
| FR-005 | `RegisteredSystem` MUST gain a `DitprId: string?` `[MaxLength(50)]` column to store the DoD IT Portfolio Repository identifier sourced from `system_identifier`. |
| FR-006 | The inline field banner MUST render for any form field that has a matching `EmassFieldSnapshot` row, showing import date and divergence state. |
| FR-007 | The banner text MUST distinguish between "sourced from eMASS (in sync)" and "diverged from eMASS value". |
| FR-008 | The banner MUST be dismissible per-session via `localStorage` without affecting the sync badge. |
| FR-009 | Divergence detection MUST run synchronously at entity-save time in the SPIN backend, adding < 50 ms latency. |
| FR-010 | `GET /api/systems/{id}/emass-sync-status` MUST return: `importDate`, `overallStatus` (InSync\|Diverged\|NotImported), `fieldStatuses[]` with per-field rows. |
| FR-011 | The sync status endpoint MUST be accessible to ISSO and ISSM roles; SCA and AO roles receive 403. |
| FR-012 | The sync status badge MUST render in the System Overview page header area with three states: Green/InSync, Yellow/Diverged, Red/NotImported. |
| FR-013 | The `SyncStatusDrawer` MUST list all tracked fields with their eMASS value, current SPIN value, and status. |
| FR-014 | Clicking "View Import History" in the drawer MUST navigate to the eMASS import sessions list (existing onboarding wizard page or equivalent route). |
| FR-015 | Pre-population in `EmassCommitJobHandler` MUST NOT overwrite existing SPIN values (non-destructive merge). |
| FR-016 | A `POST /api/systems/{id}/emass-sync-status/dismiss` endpoint MUST allow the ISSO to soft-dismiss field-banner warnings (stored in `UserPreference` or equivalent); badge remains visible. |
| FR-017 | All mutations to `EmassFieldSnapshot` rows MUST write an `AuditLogEntry`. |
| FR-018 | The `GET /api/systems/{id}/emass-sync-status` response MUST be computable in < 100 ms via indexed queries on `EmassFieldSnapshot`. |

---

## Architecture Decisions

See `research.md` for full decision records. Summary:

| Decision | Choice | Rationale |
|----------|--------|-----------|
| New entity vs extending existing models | New `EmassFieldSnapshot` table | Avoids schema churn on core entities; field list may grow; normalized storage |
| Divergence check timing | Synchronous at save, not background job | Immediate feedback; low overhead (indexed lookup, < 50 ms) |
| Banner dismiss storage | `localStorage` (client-side) | Server round-trip for UI preference is overkill; banner is informational |
| Sync status endpoint | New `GET /api/systems/{id}/emass-sync-status` | Clean separation; composable by dashboard and chat tools |
| SCA/AO badge visibility | Hidden (not 403) | Cleaner UX — roles that don't own import workflow don't need the noise |
| Pre-population strategy | Non-destructive merge in commit handler | Prevents data loss; respects ISSO edits made before re-import |

---

## Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-001 | `GET /api/systems/{id}/emass-sync-status` P95 response time ≤ 100 ms for systems with up to 20 tracked fields. |
| NFR-002 | Divergence check at entity save adds ≤ 50 ms latency (single indexed query on `EmassFieldSnapshot`). |
| NFR-003 | All new endpoints covered by integration tests in `Ato.Copilot.Tests.Integration`. |
| NFR-004 | MSW mock for `GET /api/systems/:id/emass-sync-status` registered in the dashboard test suite. |
| NFR-005 | `EmassFieldSnapshot` table includes index on `(TenantId, SystemId, FieldName)` for efficient per-field lookup. |
| NFR-006 | Sync status badge renders without blocking the page — loaded asynchronously with a skeleton placeholder. |
| NFR-007 | Feature 072 MUST NOT break the existing `EmassImportService` / `EmassCommitJobHandler` pipeline. |

---

## Test Plan

### Backend Integration Tests
**File:** `tests/Ato.Copilot.Tests.Integration/EmassSync/EmassFieldSnapshotTests.cs`

| Test | Scenario |
|------|----------|
| `CommitJob_CreatesFieldSnapshots_ForAllTrackedFields` | Commit with all columns → 6 snapshot rows |
| `CommitJob_DoesNotOverwrite_ExistingSpinValues` | Pre-existing Name → not overwritten |
| `CommitJob_PartialColumns_CreatesOnlyPresentFields` | Missing `system_acronym` → no Acronym snapshot |
| `SyncStatus_InSync_WhenNoFieldDiverged` | All snapshots `IsDiverged=false` → InSync |
| `SyncStatus_Diverged_WhenOneFieldChanged` | Edit Name → IsDiverged true → Diverged status |
| `SyncStatus_NotImported_WhenNoSession` | No Imported session → NotImported status |
| `SyncStatusEndpoint_Returns403_ForSCA` | SCA role → 403 |
| `SyncStatusEndpoint_Returns403_ForAO` | AO role → 403 |
| `SyncStatusEndpoint_Returns200_ForISSO` | ISSO role → 200 with field rows |
| `DivergenceCheck_ReconcileOnReSync` | Revert Name to eMASS value → IsDiverged = false |
| `FieldSnapshot_AuditLog_WrittenOnCreate` | Commit → AuditLogEntry for each snapshot |
| `FieldSnapshot_AuditLog_WrittenOnDivergence` | Save diverged field → AuditLogEntry |

### Frontend Unit Tests
**File:** `src/Ato.Copilot.Dashboard/src/features/emass-sync/__tests__/`

| Test | Scenario |
|------|----------|
| `SyncBadge renders Green for InSync status` | MSW mock status=InSync → green badge |
| `SyncBadge renders Yellow with count for Diverged` | status=Diverged, fieldCount=2 → "Diverged (2 fields)" |
| `SyncBadge renders Red for NotImported` | status=NotImported → red badge |
| `SyncBadge hidden for SCA role` | `settings.role = 'SCA'` → badge absent |
| `SyncStatusDrawer opens on badge click` | Click badge → drawer with field rows |
| `EmassFieldBanner renders for sourced field` | MSW snapshot mock → banner visible |
| `EmassFieldBanner shows diverged text when value changed` | Value ≠ emassValue → diverged text |
| `EmassFieldBanner dismissible via localStorage` | Click dismiss → banner hidden |

### MSW Handlers
File: `src/Ato.Copilot.Dashboard/src/mocks/handlers/emassSync.ts`
- `GET /api/systems/:id/emass-sync-status` → 200 with `SyncStatusDto`
- `POST /api/systems/:id/emass-sync-status/dismiss` → 204

---

## Definition of Done

- [ ] `EmassFieldSnapshot` entity, EF Core migration, and `DbSet` registration complete
- [ ] `RegisteredSystem.DitprId` column added via migration
- [ ] `EmassCommitJobHandler` extended to create `EmassFieldSnapshot` rows post-commit
- [ ] Divergence check runs at `RegisteredSystem` / `SecurityCategorization` / `ControlBaseline` save
- [ ] `GET /api/systems/{id}/emass-sync-status` passes all integration tests
- [ ] `POST /api/systems/{id}/emass-sync-status/dismiss` passes integration tests
- [ ] Sync badge component renders all 3 states in the System Overview
- [ ] `SyncStatusDrawer` opens with per-field rows on badge click
- [ ] Inline `EmassFieldBanner` renders on form fields with snapshots
- [ ] Banner dismissible via `localStorage` without affecting badge
- [ ] Pre-population (non-destructive) verified via integration test
- [ ] SCA/AO badge hidden; ISSO/ISSM see badge
- [ ] MSW handlers registered and Vitest suite passing
- [ ] No regression in `dotnet test Ato.Copilot.sln`
- [ ] PR approved and linked to issue #60
