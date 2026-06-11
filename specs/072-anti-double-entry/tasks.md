# Tasks — 072: Anti-Double-Entry (SPIN/eMASS Sync Status)

_Issue #60 — phased implementation. Each task cites the file path(s) it touches._

---

## Phase 1 — Data Model: `EmassFieldSnapshot` Entity & Migration

_Issue #60 | Priority: P1 | Unblocks all backend logic_

- [ ] **T001**: Create `EmassFieldSnapshot` entity
  - File: `src/Ato.Copilot.Core/Models/Onboarding/EmassFieldSnapshot.cs`
  - Fields (see `data-model.md` for full spec):
    - `Id: Guid` PK
    - `TenantId: Guid` ([TenantScoped])
    - `SystemId: string` `[MaxLength(36)]` FK → `RegisteredSystem.Id`
    - `FieldName: string` `[MaxLength(100)]` — one of: `Name`, `Acronym`, `DitprId`, `OverallLevel`, `BaselineType`, `SystemType`
    - `EmassValue: string?` `[MaxLength(1000)]` — raw string value from eMASS import column
    - `ImportedAt: DateTimeOffset` — stamped from `EmassImportSession.UpdatedAt`
    - `ImportSessionId: Guid` FK → `EmassImportSession.Id`
    - `IsDiverged: bool` default `false`
    - `DivergenceDetectedAt: DateTimeOffset?`
    - `ReconciledAt: DateTimeOffset?`
    - `CreatedAt: DateTimeOffset`, `UpdatedAt: DateTimeOffset`
  - Add `[TenantScoped]` attribute
  - Navigation: `EmassImportSession ImportSession` (no nav to RegisteredSystem — string PK boundary)

- [ ] **T002**: Add `DitprId` column to `RegisteredSystem`
  - File: `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`
  - Add to `RegisteredSystem` class (after `Acronym`):
    ```csharp
    /// <summary>DoD IT Portfolio Repository identifier (eMASS system_identifier field). Feature 072.</summary>
    [MaxLength(50)]
    public string? DitprId { get; set; }
    ```

- [ ] **T003**: Register `DbSet<EmassFieldSnapshot>` in `AtoCopilotContext`
  - File: `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
  - Add after the existing eMASS-related DbSets (search for `EmassImportSession`):
    ```csharp
    /// <summary>Feature 072 (#60): per-field eMASS import snapshots for divergence tracking.</summary>
    public DbSet<EmassFieldSnapshot> EmassFieldSnapshots => Set<EmassFieldSnapshot>();
    ```

- [ ] **T004**: Configure EF Core entity in `OnModelCreating`
  - File: `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
  - In `OnModelCreating`:
    - PK: `HasKey(s => s.Id)`
    - Index: `HasIndex(s => new { s.TenantId, s.SystemId, s.FieldName }).HasDatabaseName("IX_EmassFieldSnapshots_TenantId_SystemId_FieldName")`
    - FK: `HasOne(s => s.ImportSession).WithMany().HasForeignKey(s => s.ImportSessionId).OnDelete(DeleteBehavior.Restrict)`
    - Apply tenant query filter matching existing `TenantScoped` pattern

- [ ] **T005**: Generate EF Core migration
  - Command (from repo root):
    ```bash
    dotnet ef migrations add Feature072_EmassFieldSnapshot \
      --project src/Ato.Copilot.Core \
      --startup-project src/Ato.Copilot.Mcp
    ```
  - Migration file: `src/Ato.Copilot.Core/Migrations/YYYYMMDDHHMMSS_Feature072_EmassFieldSnapshot.cs`
  - Verify migration creates:
    - `EmassFieldSnapshots` table with all columns
    - `DitprId` column on `RegisteredSystems` table
    - Index `IX_EmassFieldSnapshots_TenantId_SystemId_FieldName`
  - Update snapshot: `src/Ato.Copilot.Core/Migrations/AtoCopilotContextModelSnapshot.cs` (auto-generated)

---

## Phase 2 — Backend: Pre-Population in Commit Handler

_Issue #60 | Priority: P1 | Depends on T001–T005_

- [ ] **T006**: Extend `EmassCommitJobHandler` to create `EmassFieldSnapshot` rows
  - File: `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/Emass/Handlers/EmassCommitJobHandler.cs`
  - After the core import commit phase, iterate over committed `RegisteredSystem` records:
    1. For each tracked field (`Name`, `Acronym`, `DitprId`, `SystemType`):
       - Check if a current SPIN value exists; if not, apply pre-population from the eMASS parsed value
       - Upsert an `EmassFieldSnapshot` row: insert if no row for `(TenantId, SystemId, FieldName)`,
         or update `EmassValue` / `ImportedAt` / `ImportSessionId` if re-import
       - Set `IsDiverged = false`, `ReconciledAt = UtcNow` (fresh import is in sync)
    2. For `SecurityCategorization.OverallLevel` and `ControlBaseline.BaselineType`:
       - Same upsert pattern after categorization/baseline entities are committed
  - Non-destructive rule: if `RegisteredSystem.Name` is already set and non-empty, do NOT
    overwrite it; still create the snapshot with the eMASS value for divergence tracking
  - Write `AuditLogEntry` for each new snapshot: `Action="EmassFieldSnapshot.Created"`,
    `ActorOid=commitJob.CreatedBy`, `SystemId`, `FieldName`, `TenantId`, `Timestamp`
  - Catch per-field exceptions; log warning and continue (do not abort commit job)

- [ ] **T007**: Create `IEmassFieldSyncService` interface
  - File: `src/Ato.Copilot.Core/Interfaces/Onboarding/IEmassFieldSyncService.cs`
  - Methods:
    ```csharp
    Task UpsertSnapshotsAsync(Guid tenantId, string systemId, Guid sessionId,
        IReadOnlyDictionary<string, string?> emassFieldValues, CancellationToken ct = default);
    Task CheckDivergenceAsync(Guid tenantId, string systemId, CancellationToken ct = default);
    Task<EmassSystemSyncStatusDto> GetSyncStatusAsync(Guid tenantId, string systemId, CancellationToken ct = default);
    ```

- [ ] **T008**: Implement `EmassFieldSyncService`
  - File: `src/Ato.Copilot.Agents/Compliance/Services/EmassFieldSyncService.cs`
  - `UpsertSnapshotsAsync`: upsert `EmassFieldSnapshot` rows; audit log each new/updated row
  - `CheckDivergenceAsync`:
    1. Load `EmassFieldSnapshot` rows for `(TenantId, SystemId)`
    2. For each row, resolve the current SPIN value from the appropriate entity
    3. Compare (normalize enum strings): if mismatch → `IsDiverged = true`, `DivergenceDetectedAt = UtcNow`;
       if match and currently diverged → `IsDiverged = false`, `ReconciledAt = UtcNow`
    4. Save changes; write `AuditLogEntry` for each state transition
  - `GetSyncStatusAsync`:
    1. Check for `EmassImportSession` with `Status = Imported` for this system (via `TenantId`
       and system identifier match in `EmassImportSession` — see `research.md R1` for join strategy)
    2. If none: return `{ OverallStatus = NotImported }`
    3. Load all `EmassFieldSnapshot` rows for the system
    4. Compute `OverallStatus`: `InSync` if zero diverged rows; `Diverged` if any `IsDiverged = true`
    5. Return `EmassSystemSyncStatusDto` with field rows

- [ ] **T009**: Wire `CheckDivergenceAsync` into entity save pipeline
  - Files: wherever `RegisteredSystem`, `SecurityCategorization`, `ControlBaseline` saves are finalized
    (search for `await db.SaveChangesAsync` in `SystemProfileService.cs`, `CategorizationService.cs`,
    `BaselineService.cs` — call `IEmassFieldSyncService.CheckDivergenceAsync` after a successful save)
  - Pattern: fire-and-forget is NOT appropriate here; await the divergence check before returning
    the HTTP response (it is fast — indexed lookup, < 50 ms target)
  - Guard: if `IEmassFieldSyncService` throws, log and swallow — do not fail the main save

---

## Phase 3 — Backend: Sync Status Endpoint

_Issue #60 | Priority: P1 | Depends on T007–T009_

- [ ] **T010**: Create `EmassSync EndpointExtensions` — route registration
  - File: `src/Ato.Copilot.Mcp/Endpoints/EmassSyncEndpoints.cs`
  - Namespace: `Ato.Copilot.Mcp.Endpoints`
  - Declare route handlers:
    - `GET /api/systems/{systemId}/emass-sync-status` → `GetEmassSyncStatusAsync`
    - `POST /api/systems/{systemId}/emass-sync-status/dismiss` → `DismissEmassBannerAsync`
  - Tag: `.WithTags("eMASS Sync")`
  - Register via `MapEmassSyncEndpoints()` extension method

- [ ] **T011**: Register `MapEmassSyncEndpoints()` in startup
  - File: `src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs`
    (or wherever `MapCspInheritedComponentEndpoints` / `MapCapabilityLibraryEndpoints` are registered)
  - Add: `app.MapEmassSyncEndpoints();`

- [ ] **T012**: Implement `GET /api/systems/{systemId}/emass-sync-status`
  - File: `src/Ato.Copilot.Mcp/Endpoints/EmassSyncEndpoints.cs`
  - Logic:
    1. Resolve `systemId` — verify it exists in tenant (EF + RLS)
    2. Call `IEmassFieldSyncService.GetSyncStatusAsync(tenantId, systemId)`
    3. Return 200 with `EmassSystemSyncStatusDto` in standard envelope
  - Auth: `RequireAuthorization("IssoOrIssm")` (SCA/AO → 403)
  - Return shape: `{ status, data: EmassSystemSyncStatusDto, metadata }`
  - See `contracts/http-api.md` for full DTO schema

- [ ] **T013**: Implement `POST /api/systems/{systemId}/emass-sync-status/dismiss`
  - File: `src/Ato.Copilot.Mcp/Endpoints/EmassSyncEndpoints.cs`
  - Logic: upsert a `UserPreference` record with key `emass-banner-dismissed:{systemId}`
    set to `true` for the current user OID (or use an existing preference storage pattern
    in the codebase — search for `UserPreference` table)
  - If no `UserPreference` model exists, create a simple key-value `UserPreference` entity
    (see `data-model.md §2`)
  - Return 204 No Content
  - Auth: `RequireAuthorization("IssoOrIssm")`

- [ ] **T014**: Add `EmassSystemSyncStatusDto` and related DTOs
  - File: `src/Ato.Copilot.Mcp/Endpoints/EmassSyncEndpoints.cs` (inner records) or
    `src/Ato.Copilot.Core/Models/Dto/EmassSyncDtos.cs`
  - See `contracts/http-api.md` for field definitions

---

## Phase 4 — Frontend: Sync Status Badge & Drawer

_Issue #60 | Priority: P1 | Depends on T010–T014_

- [ ] **T015**: Create API client module
  - File: `src/Ato.Copilot.Dashboard/src/features/emass-sync/api.ts`
  - Functions: `getEmassSyncStatus(systemId)`, `dismissEmassBanner(systemId)`
  - Types: `EmassSystemSyncStatusDto`, `EmassFieldStatusDto`, `SyncOverallStatus`
  - See `contracts/http-api.md` for type definitions
  - Attach `attachAuthInterceptor` (MSAL silent renewal — same pattern as other features)
  - Cache `getEmassSyncStatus` result for 60 s in `useQuery` / `useState` (no re-fetch within window)

- [ ] **T016**: Create `EmassSyncBadge.tsx` component
  - File: `src/Ato.Copilot.Dashboard/src/features/emass-sync/EmassSyncBadge.tsx`
  - Props: `systemId: string`
  - Fetches `GET /api/systems/{systemId}/emass-sync-status` on mount
  - Renders:
    - Green pill: `✓ eMASS In Sync` when `overallStatus === 'InSync'`
    - Yellow pill: `⚠ Diverged (N field(s))` when `overallStatus === 'Diverged'`
    - Red pill: `✗ Not Imported` when `overallStatus === 'NotImported'`
    - Gray pill skeleton while loading
  - Hidden (`return null`) if `settings.role === 'SCA' || settings.role === 'AO'`
  - `data-testid="emass-sync-badge"`, `data-status={overallStatus.toLowerCase()}`
  - On click: opens `SyncStatusDrawer`

- [ ] **T017**: Create `SyncStatusDrawer.tsx` component
  - File: `src/Ato.Copilot.Dashboard/src/features/emass-sync/SyncStatusDrawer.tsx`
  - Props: `systemId: string`, `isOpen: boolean`, `onClose: () => void`
  - Sections:
    - Header: "eMASS Sync Status" + close button
    - Import date: "Last imported: [importDate]" or "Never imported"
    - Field status table: field name | eMASS value | SPIN value | status icon (✓/⚠)
    - Footer: "View Import History" link → eMASS import page route
  - Accessible: `role="dialog"`, `aria-label="eMASS sync status"`
  - Uses existing drawer/modal pattern in the codebase (search for existing drawer components)

- [ ] **T018**: Integrate `EmassSyncBadge` into System Overview
  - File: wherever the System Overview page header is rendered
    (search for `SystemOverviewPage` or `overview` route in `App.tsx`)
  - Add `<EmassSyncBadge systemId={systemId} />` in the page header area, after the system name
  - Import guard: only render if feature flag `VITE_FEATURE_EMASS_SYNC=true`
    (defaults to `true` — env var allows disabling in dev if needed)

---

## Phase 5 — Frontend: Inline `EmassFieldBanner`

_Issue #60 | Priority: P1 | Depends on T015_

- [ ] **T019**: Create `EmassFieldBanner.tsx` component
  - File: `src/Ato.Copilot.Dashboard/src/features/emass-sync/EmassFieldBanner.tsx`
  - Props:
    ```typescript
    interface EmassFieldBannerProps {
      systemId: string;
      fieldName: string;           // e.g. 'Name', 'DitprId', 'OverallLevel'
      currentValue: string;        // current SPIN field value (stringified)
    }
    ```
  - Behavior:
    1. On mount (or `fieldName` change): call `getEmassSyncStatus(systemId)` (uses cached response)
    2. Find the field row matching `fieldName` in the response
    3. If no row: render nothing
    4. If row exists and `isDiverged = false`: render origin banner
       "📥 Imported from eMASS on [importedAt]. Editing here won't update eMASS."
    5. If row exists and `isDiverged = true`: render divergence banner
       "⚠️ Diverged from eMASS value (imported [importedAt]): eMASS had '[emassValue]'."
  - Dismiss: check `localStorage.getItem('emass-field-banner-dismissed-{fieldName}-{systemId}')`
    — if set, render nothing. On dismiss button click: set that key and re-render.
  - Accessible: `role="note"`, `aria-label="eMASS import notice"`

- [ ] **T020**: Integrate `EmassFieldBanner` into System Registration / Edit forms
  - Files: forms where the tracked fields appear
    (search for `<input` or form field components for `system.Name`, `system.DitprId`,
     `categorization.OverallLevel`, `baseline.BaselineType` — likely in
     `src/Ato.Copilot.Dashboard/src/features/system-profile/` or equivalent)
  - Add `<EmassFieldBanner systemId={systemId} fieldName="Name" currentValue={system.name} />`
    below each tracked field input
  - Tracked fields to banner: `Name`, `Acronym`, `DitprId`, `OverallLevel`, `BaselineType`

---

## Phase 6 — Testing

_Issue #60 | Priority: P1 | Depends on Phases 2–5_

- [ ] **T021**: Backend integration test file
  - File: `tests/Ato.Copilot.Tests.Integration/EmassSync/EmassFieldSnapshotTests.cs`
  - Test matrix: see `spec.md § Test Plan`
  - Use `WebApplicationFactory` pattern consistent with existing integration tests
  - Seed: `EmassImportSession` (Imported), `RegisteredSystem`, related entities

- [ ] **T022**: MSW handler file
  - File: `src/Ato.Copilot.Dashboard/src/mocks/handlers/emassSync.ts`
  - Handlers:
    - `GET /api/systems/:systemId/emass-sync-status` → 200 `EmassSystemSyncStatusDto`
    - `POST /api/systems/:systemId/emass-sync-status/dismiss` → 204
  - Register in `src/mocks/handlers/index.ts`

- [ ] **T023**: Frontend unit tests
  - File: `src/Ato.Copilot.Dashboard/src/features/emass-sync/__tests__/EmassSyncBadge.test.tsx`
  - File: `src/Ato.Copilot.Dashboard/src/features/emass-sync/__tests__/EmassFieldBanner.test.tsx`
  - Use Vitest + Testing Library; see `spec.md § Test Plan` for test list

---

## Phase 7 — Docs & Spec Finalization

_Issue #60 | Priority: P2_

- [ ] **T024**: Update `docs/guides/emass-package.md` (if it exists from Feature 041)
  to mention sync status badge and field-level divergence warnings.

- [ ] **T025**: Mark spec `Status: Implemented` after all DoD items checked
  - File: `specs/072-anti-double-entry/spec.md`
  - Change `Status: Draft` → `Status: Implemented`
