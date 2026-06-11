# Tasks — 071: Categorization Flexibility & Service-Branch Overlays

_Issue #62 — phased implementation. Each task cites the file path(s) it touches._

---

## Phase 1 — Data Model & Migration

_Priority: P1 | Unblocks all other backend work_

- [ ] **T001**: Create `CategorizationOverlay` entity and supporting enums
  - File: `src/Ato.Copilot.Core/Models/Compliance/CategorizationOverlay.cs`
  - File: `src/Ato.Copilot.Core/Models/Compliance/CategorizationOverlayEnums.cs`
  - See `data-model.md §1` for full C# model

- [ ] **T002**: Create `CategorizationAuditEntry` entity and `CategorizationChangeType` enum
  - File: `src/Ato.Copilot.Core/Models/Compliance/CategorizationAuditEntry.cs`
  - File: `src/Ato.Copilot.Core/Models/Compliance/CategorizationChangeType.cs`
  - See `data-model.md §2` for full C# model

- [ ] **T003**: Extend `SecurityCategorization` with Feature 071 columns
  - File: `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`
  - Append after `ModifiedAt`: `OverlayIds`, `CustomDimensions`, `LastModifiedBy`, `OverlayAdjustedImpactLevel`
  - See `data-model.md §3` for exact property declarations

- [ ] **T004**: Register DbSets in `AtoCopilotContext`
  - File: `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
  - Add `DbSet<CategorizationOverlay>` and `DbSet<CategorizationAuditEntry>`
  - See `data-model.md §4` for placement and XML docs

- [ ] **T005**: Configure EF Core entities in `OnModelCreating`
  - File: `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
  - Add EF config blocks per `data-model.md §4` (indexes, FK, delete behavior)

- [ ] **T006**: Generate EF Core migration
  ```bash
  dotnet ef migrations add Feature071_CategorizationOverlaysAndAudit \
    --project src/Ato.Copilot.Core \
    --startup-project src/Ato.Copilot.Mcp
  ```
  - Verify migration creates `CategorizationOverlays`, `CategorizationAuditEntries` tables
  - Verify `ALTER TABLE SecurityCategorizations` adds the 3 new columns

- [ ] **T007**: Seed built-in overlay catalog data files
  - Directory: `data/overlays/builtin/`
  - Files: `army-rmf-v3.json`, `navy-secnav-v2.json`, `af-rmf-v4.json`,
    `usmc-rmf-v1.json`, `ussf-rmf-v1.json`, `dodi-8510-mac-cl.json`,
    `dod-privacy-overlay.json`, `classified-information-overlay.json`,
    `cross-domain-solution-overlay.json`, `space-platform-overlay.json`
  - Create `src/Ato.Copilot.Core/Data/Seeding/CategorizationOverlaySeedService.cs`
    that loads JSON files at startup and upserts rows with `Scope = BuiltIn`

---

## Phase 2 — Backend Service Layer

_Priority: P1 | Depends on Phase 1_

- [ ] **T008**: Define `ICategorizationOverlayService` interface
  - File: `src/Ato.Copilot.Core/Interfaces/Compliance/ICategorizationOverlayService.cs`
  - Methods:
    - `ListCatalogAsync(Guid tenantId, bool dodEnabled, CancellationToken ct)` → `IReadOnlyList<CategorizationOverlayDto>`
    - `ApplyOverlayAsync(string systemId, string overlayVersionId, Guid tenantId, string userId, string justification, CancellationToken ct)` → `ApplyOverlayResult`
    - `RemoveOverlayAsync(string systemId, string overlayVersionId, Guid tenantId, string userId, string justification, CancellationToken ct)` → `RemoveOverlayResult`
    - `GetAuditTrailAsync(string categorizationId, Guid tenantId, CancellationToken ct)` → `IReadOnlyList<CategorizationAuditEntryDto>`
    - `ResolveOverlayStackAsync(string categorizationId, Guid tenantId, CancellationToken ct)` → `OverlayResolutionResult`
    - `ValidateOverlayAsync(CategorizationOverlay overlay, CancellationToken ct)` → `OverlayValidationResult`

- [ ] **T009**: Implement `CategorizationOverlayService`
  - File: `src/Ato.Copilot.Core/Services/Compliance/CategorizationOverlayService.cs`
  - Implements: overlay stack resolution, SP 800-60 override application, custom dimension merging,
    aggregation rule evaluation (declarative JSON rule engine — no scripting),
    AO notification check (query `RmfPhase`; emit `SignificantChange` if Authorize/Monitor)
  - On any IL change: write `CategorizationAuditEntry` + set `RequiresAoNotification` flag

- [ ] **T010**: Implement overlay validation rules
  - File: `src/Ato.Copilot.Core/Services/Compliance/CategorizationOverlayValidator.cs`
  - Validate: `OVERLAY_FIPS199_BASE_LOCKED`, `OVERLAY_RULE_CANNOT_LOWER_FIPS199`,
    `OVERLAY_RULE_REFERENCES_UNKNOWN_DIMENSION`, DoD gate check
  - Used by both the org overlay editor endpoint (Phase 3) and the seed loader (Phase 1)

- [ ] **T011**: Background job — AO notification dispatch
  - File: `src/Ato.Copilot.Core/BackgroundJobs/CategorizationAoNotificationJob.cs`
  - Polls `CategorizationAuditEntries` where `RequiresAoNotification = true AND AoNotificationDispatched = false`
  - Emits Feature 035 `SignificantChange` event; sets `AoNotificationDispatched = true`

---

## Phase 3 — HTTP API Endpoints

_Priority: P1 | Depends on Phase 2_

- [ ] **T012**: Create `CategorizationOverlayEndpoints.cs` — route declarations
  - File: `src/Ato.Copilot.Mcp/Endpoints/CategorizationOverlayEndpoints.cs`
  - Routes (see `contracts/http-api.md`):
    - `GET /api/categorization-overlays` → `ListCatalogAsync`
    - `POST /api/systems/{systemId}/categorization/overlays` → `ApplyOverlayAsync`
    - `DELETE /api/systems/{systemId}/categorization/overlays/{overlayVersionId}` → `RemoveOverlayAsync`
    - `GET /api/systems/{systemId}/categorization/audit` → `GetAuditTrailAsync`
    - `GET /api/systems/{systemId}/categorization/overlay-stack` → `GetOverlayStackAsync`
  - Tag: `.WithTags("Categorization Overlays")`

- [ ] **T013**: Register `MapCategorizationOverlayEndpoints()` in startup
  - File: `src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs`
    (or wherever existing categorization endpoints are registered)
  - Add: `app.MapCategorizationOverlayEndpoints();`

- [ ] **T014**: Create `CategorizationOverlayAdminEndpoints.cs` — org overlay CRUD
  - File: `src/Ato.Copilot.Mcp/Endpoints/CategorizationOverlayAdminEndpoints.cs`
  - Routes:
    - `POST /api/org/categorization-overlays` → `CreateOrgOverlayAsync` (OrgAdministrator)
    - `PUT /api/org/categorization-overlays/{id}` → `UpdateOrgOverlayAsync` (OrgAdministrator)
    - `POST /api/org/categorization-overlays/{id}/submit-review` → `SubmitForReviewAsync`
    - `POST /api/org/categorization-overlays/{id}/approve` → `ApproveOverlayAsync` (ISSM)
    - `POST /api/org/categorization-overlays/{id}/retire` → `RetireOverlayAsync` (OrgAdministrator)

---

## Phase 4 — MCP Agent Tools

_Priority: P1 | Depends on Phase 2_

- [ ] **T015**: Add `compliance_apply_categorization_overlay` tool
  - File: `src/Ato.Copilot.Agents/Compliance/Tools/CategorizationTools.cs`
    (add as new class alongside `CategorizeSystemTool`)
  - Parameters: `system_id` (required), `overlay_version_id` (required), `justification` (required)
  - Calls `ICategorizationOverlayService.ApplyOverlayAsync`
  - Returns: resolved `ImpactLevel`, `NistBaseline`, overlay stack summary, audit entry ID
  - RBAC: ISSO, ISSM

- [ ] **T016**: Add `compliance_remove_categorization_overlay` tool
  - File: `src/Ato.Copilot.Agents/Compliance/Tools/CategorizationTools.cs`
  - Parameters: `system_id` (required), `overlay_version_id` (required), `justification` (required)
  - Calls `ICategorizationOverlayService.RemoveOverlayAsync`
  - RBAC: ISSO, ISSM

- [ ] **T017**: Add `compliance_get_categorization_audit` tool
  - File: `src/Ato.Copilot.Agents/Compliance/Tools/CategorizationTools.cs`
  - Parameters: `system_id` (required), `limit` (optional, default 20), `since` (optional ISO-8601)
  - Calls `ICategorizationOverlayService.GetAuditTrailAsync`
  - Returns: ordered list of audit entries with overlay names, IL changes, change reasons
  - RBAC: any authenticated user (Analyst+)

- [ ] **T018**: Add `compliance_list_categorization_overlays` tool
  - File: `src/Ato.Copilot.Agents/Compliance/Tools/CategorizationTools.cs`
  - Parameters: `service_branch` (optional), `scope` (optional: builtin|org|csp), `search` (optional)
  - Calls `ICategorizationOverlayService.ListCatalogAsync`
  - Returns: overlay catalog grouped by service branch / scope, with version + description
  - RBAC: any authenticated user

- [ ] **T019**: Update `CategorizeSystemTool` description to mention overlay support
  - File: `src/Ato.Copilot.Agents/Compliance/Tools/CategorizationTools.cs`
  - Update `Description` property to note that overlays can be applied post-categorization
    via `compliance_apply_categorization_overlay`

---

## Phase 5 — SSP Export Integration

_Priority: P2 | Depends on Phase 3_

- [ ] **T020**: Extend SSP §10 export with overlay trace rationale
  - File: search for SSP §10 categorization section in `src/Ato.Copilot.Core/` SSP export logic
    (`SspExport.cs`, `SspAuthoringTools.cs`, or related)
  - Add: auto-generate categorization rationale paragraph from `OverlayResolutionResult`
    (overlay names, versions, key overrides, aggregation rule used)
  - Format: Markdown / structured text compatible with existing OSCAL prose fields

---

## Phase 6 — Frontend (Deferred — API-First)

_Priority: P2 | Deferred until Phase 3 API is stable_

- [ ] **T021**: Overlay picker component in categorization wizard (design handoff required)
- [ ] **T022**: Methodology drilldown panel — AO/SCA read-only trace view
- [ ] **T023**: Overlay upgrade notice on system detail page
- [ ] **T024**: Org overlay editor (`/admin/categorization-overlays/new`)

---

## Phase 7 — Tests

_Priority: P1 (unit) / P2 (integration) | Depends on Phase 2 + 3_

- [ ] **T025**: Unit tests — `CategorizationOverlayValidator`
  - File: `tests/Ato.Copilot.Tests.Unit/Compliance/CategorizationOverlayValidatorTests.cs`
  - Cover: FIPS199 base lock, cannot-lower-floor, unknown-dimension, DoD gate

- [ ] **T026**: Unit tests — overlay stack resolution
  - File: `tests/Ato.Copilot.Tests.Unit/Compliance/OverlayStackResolutionTests.cs`
  - Cover: single overlay, stacked overlays, conflict resolution, aggregation rule evaluation

- [ ] **T027**: Integration tests — `GET /api/categorization-overlays`
  - File: `tests/Ato.Copilot.Tests.Integration/Endpoints/CategorizationOverlayEndpointTests.cs`
  - Cover: built-in catalog list, DoD gate, org-specific overlays, pagination

- [ ] **T028**: Integration tests — apply / remove overlay + audit trail
  - File: `tests/Ato.Copilot.Tests.Integration/Endpoints/CategorizationOverlayEndpointTests.cs`
  - Cover: apply + verify IL change, remove + verify IL revert, audit entry created

- [ ] **T029**: Integration tests — AO notification dispatch
  - File: `tests/Ato.Copilot.Tests.Integration/BackgroundJobs/CategorizationAoNotificationJobTests.cs`
  - Cover: IL change in Monitor phase → `RequiresAoNotification = true`; job dispatches event
