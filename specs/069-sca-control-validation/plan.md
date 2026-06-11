# Implementation Plan — Spec 069: SCA Control Implementation Validation Link

**Target:** Wave 9 — SCA Evidence & Validation  
**Estimated phases:** 9  
**Priority tasks marked [P]**

---

## Sequencing Rationale

This feature has a clear layered dependency chain:
1. Data model first (entity + migration)
2. Service interface + implementation (all other layers depend on this)
3. MCP tool (depends on service)
4. API endpoints (depends on service)
5. IaC scan hook (depends on service)
6. Dashboard UI (depends on API endpoints)
7. Assess-control warning (depends on service)
8. Tests (depend on all implementation)
9. CI verification (final gate)

---

## Phase 1 — Data Model & Migration
**Tasks:** T001–T004  
**Blocked by:** Nothing — start immediately  
**Blocks:** All other phases

Key actions:
- Add `ControlValidationLink` entity to `SspModels.cs`
- Add `ControlValidationLinkType` enum
- Add navigation property to `ControlImplementation`
- Configure EF Core (DbSet, indexes, unique constraint, tenant filter)
- Generate migration `Add_ControlValidationLinks`

**Done signal:** `dotnet ef migrations add` succeeds; migration SQL reviewed and correct.

---

## Phase 2 — Service Layer
**Tasks:** T005–T006  
**Blocked by:** Phase 1 (entity must exist)  
**Blocks:** Phases 3, 4, 5, 7

Key actions:
- Define `IControlValidationLinkService` with 4 methods
- Implement `ControlValidationLinkService` — all queries filtered by `TenantId`
- Implement upsert logic in `UpsertScanLinkAsync` using `(ControlImplementationId, LinkTarget)` unique constraint
- Register service in DI

**Done signal:** Service compiles; unit tests for `UpsertScanLinkAsync` idempotency pass.

---

## Phase 3 — MCP Tool
**Tasks:** T007–T009  
**Blocked by:** Phase 2  
**Blocks:** Nothing directly (parallel to Phase 4)

Key actions:
- Create `GetControlValidationTool` in new file `ControlValidationTools.cs`
- Implement `get`, `add`, `delete` branches via `action` parameter
- Register in DI with `Compliance.Auditor` RBAC
- Return `{ status, data: { links, total }, metadata }` shape

**Done signal:** Tool callable from MCP test harness; NOT_FOUND, empty-list, and CRUD cases work.

---

## Phase 4 — Dashboard API Endpoints
**Tasks:** T010–T013  
**Blocked by:** Phase 2  
**Blocks:** Phase 6 (UI depends on these endpoints)

Key actions:
- Create `ControlValidationEndpoints.cs` in `Endpoints/Compliance/`
- Implement `GET`, `POST`, `DELETE` at `/api/systems/{id}/controls/{controlId}/validation`
- Register endpoints in app startup
- RBAC: Reader for GET, Auditor for POST/DELETE

**Done signal:** Endpoints return 200/201/204/404 correctly; tested with `curl` or Swagger UI.

---

## Phase 5 — IaC Scan Auto-Link Hook
**Tasks:** T014–T015  
**Blocked by:** Phase 2  
**Blocks:** Nothing (independent of UI)

Key actions:
- Inject `IControlValidationLinkService` into `IacComplianceScanTool` (optional/null-safe)
- After scan execution, iterate findings → call `UpsertScanLinkAsync` for each control-mapped finding
- Add `validation_links_created: N` to scan response metadata

**Done signal:** Scan with a control-mapped Bicep file produces `ControlValidationLink` records; scan response shows `validation_links_created > 0`.

---

## Phase 6 — Dashboard UI
**Tasks:** T016–T020  
**Blocked by:** Phase 4  
**Blocks:** Phase 7 (warning depends on panel existing)

Key actions:
- Add API methods to `complianceApi.ts`
- Create `ValidationEvidencePanel.tsx` (link list, badges, add/delete)
- Create `AddValidationLinkModal.tsx`
- Mount panel on control detail page
- Implement `buildAzurePortalUrl` utility

**Done signal:** Control detail page shows Validation Evidence panel; Add and Delete actions work end-to-end; Azure Portal URL opens for `AzureResource` links.

---

## Phase 7 — Warning on Assess-Control with 0 Links
**Tasks:** T021–T022  
**Blocked by:** Phase 2 (service), Phase 6 (UI panel in place)  
**Blocks:** Nothing

Key actions:
- `AssessControlTool`: after recording Satisfied, fetch link count; if 0, include `warnings` array in JSON
- Dashboard: if panel shows 0 links and user marks control Satisfied, show inline non-blocking warning

**Done signal:** MCP response for Satisfied control with 0 links includes `warnings`; Dashboard shows warning message.

---

## Phase 8 — Testing
**Tasks:** T023–T027  
**Blocked by:** All implementation phases  

Key actions:
- Backend unit tests: service, tool, IaC scan hook
- Frontend unit tests: panel rendering, role gating, Azure URL utility

**Done signal:** All new tests pass; no regressions in existing test suite.

---

## Phase 9 — CI Verification
**Tasks:** T028–T030  
**Blocked by:** Phase 8

Key actions:
- `dotnet build && dotnet test`
- `npm run build`
- Definition of Done checklist complete

**Done signal:** CI green; PR updated and ready for review.
