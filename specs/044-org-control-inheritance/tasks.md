# Tasks: Org-Level Control Inheritance

**Input**: Design documents from `/specs/044-org-control-inheritance/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-contracts.md, quickstart.md

**Tests**: Each implementation phase includes corresponding test creation per Constitution Principle III (Testing Standards). Unit tests in `tests/Ato.Copilot.Tests.Unit/Services/OrgInheritanceServiceTests.cs`, integration tests in `tests/Ato.Copilot.Tests.Integration/Endpoints/OrgInheritanceEndpointTests.cs`.

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Exact file paths included in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Entity model, enum extensions, migration, DbContext, interface — shared across all stories

- [X] T001 Add `OrgInheritanceDefault` entity class to `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs` with fields: Id, ControlId, InheritanceType, Provider, SourceCapabilityIds, SourceCapabilityNames, MappingRole, DerivedAt per data-model.md
- [X] T002 Extend `ControlInheritance` entity in `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs` with `DesignationSource` (string, 20, nullable) and `OrgInheritanceDefaultId` (string, 36, nullable FK) properties
- [X] T003 [P] Add `OrgDerived` and `OrgPropagation` values to `InheritanceChangeSource` enum in `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`
- [X] T004 Register `DbSet<OrgInheritanceDefault>` and configure entity (unique index on ControlId, FK from ControlInheritance) in `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
- [X] T005 Create EF Core migration `Feature044_OrgLevelInheritance` — OrgInheritanceDefaults table, DesignationSource + OrgInheritanceDefaultId columns on ControlInheritances, backfill existing rows with DesignationSource="Manual"
- [X] T006 [P] Create `IOrgInheritanceService` interface in `src/Ato.Copilot.Core/Interfaces/Compliance/IOrgInheritanceService.cs` with DeriveOrgDefaultsAsync, PropagateToSystemAsync, RevertToOrgDefaultsAsync, GetOrgDefaultsAsync per contracts/api-contracts.md
- [X] T007 [P] Add result record types (OrgDerivationResult, OrgPropagationResult, RevertResult, RevertSkip, OrgDefaultsListResult) in `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`

**Checkpoint**: Schema ready, interface contract defined. Build must pass: `dotnet build Ato.Copilot.sln`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core service implementation — derivation engine that all user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T008 Implement `OrgInheritanceService.DeriveOrgDefaultsAsync()` in `src/Ato.Copilot.Agents/Compliance/Services/OrgInheritanceService.cs` — query org-wide CapabilityControlMappings (RegisteredSystemId=null, ImplementationStatus=Implemented), group by ControlId, apply precedence (Primary/Supporting→Inherited, Shared→Shared), merge providers, upsert OrgInheritanceDefaults, remove stale defaults. Include structured logging: input params, derivation count, execution duration, success/failure per Constitution V.
- [X] T009 Implement `OrgInheritanceService.PropagateToSystemAsync()` in `src/Ato.Copilot.Agents/Compliance/Services/OrgInheritanceService.cs` — for given baseline control IDs, copy org defaults to ControlInheritance records where no existing override (DesignationSource != Manual/ProfileApply/CrmImport), set DesignationSource=OrgDerived, create audit entries with source=OrgDerived. Include structured logging: systemId, propagated count, skipped count, duration.
- [X] T010 Implement `OrgInheritanceService.RevertToOrgDefaultsAsync()` in `src/Ato.Copilot.Agents/Compliance/Services/OrgInheritanceService.cs` — for given control IDs, look up current org defaults, replace system override with org default values, set DesignationSource=OrgDerived, create audit entry, skip controls with no org default
- [X] T011 Implement `OrgInheritanceService.GetOrgDefaultsAsync()` in `src/Ato.Copilot.Agents/Compliance/Services/OrgInheritanceService.cs` — paginated query on OrgInheritanceDefaults with family/type/search filters, return items + summary counts
- [X] T012 [P] Register `OrgInheritanceService` as scoped service in DI container in `src/Ato.Copilot.Mcp/Program.cs` (or appropriate DI registration location)

**Checkpoint**: Core derivation engine functional. Verify with: `dotnet build Ato.Copilot.sln`

---

## Phase 3: User Story 1 — Org-Level Inheritance Derivation from Capabilities (Priority: P1) 🎯 MVP

**Goal**: When capabilities are linked to components and mapped to NIST controls, org-level defaults are automatically derived. Capabilities page becomes single source of truth.

**Independent Test**: Create capability with Primary mapping → verify org default generated as "Inherited". Create Shared mapping → verify "Shared". Delete capability → verify org default removed.

### Implementation for User Story 1

- [X] T013 [US1] Hook `IOrgInheritanceService.DeriveOrgDefaultsAsync()` into `CapabilityService.CreateMappingsAsync()` in `src/Ato.Copilot.Core/Services/CapabilityService.cs` — call after mappings are saved, within same transaction
- [X] T014 [US1] Hook `IOrgInheritanceService.DeriveOrgDefaultsAsync()` into `CapabilityService.UpdateCapabilityAsync()` in `src/Ato.Copilot.Core/Services/CapabilityService.cs` — call after capability update when ImplementationStatus changes
- [X] T015 [US1] Hook `IOrgInheritanceService.DeriveOrgDefaultsAsync()` into `CapabilityService.DeleteCapabilityAsync()` in `src/Ato.Copilot.Core/Services/CapabilityService.cs` — call after capability deletion
- [X] T016 [P] [US1] Add `GET /api/dashboard/inheritance/org-defaults` endpoint in `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` — calls GetOrgDefaultsAsync with query params (family, inheritanceType, search, page, pageSize)
- [X] T017 [P] [US1] Add `POST /api/dashboard/inheritance/org-defaults/derive` endpoint in `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` — calls DeriveOrgDefaultsAsync, returns OrgDerivationResult

**Checkpoint**: Org defaults auto-derive when capabilities change. Build: `dotnet build Ato.Copilot.sln`. Verify with curl:
```
curl -s http://localhost:3001/api/dashboard/inheritance/org-defaults | jq .summary
curl -X POST http://localhost:3001/api/dashboard/inheritance/org-defaults/derive | jq .
```

### Tests for User Story 1

- [X] T017a [P] [US1] Unit tests for derivation logic in `tests/Ato.Copilot.Tests.Unit/Services/OrgInheritanceServiceTests.cs` — test DeriveOrgDefaultsAsync: Primary→Inherited, Supporting→Inherited, Shared→Shared, precedence (Primary over Shared), multi-provider merge, Planned capability excluded, empty mappings returns zero defaults
- [X] T017b [P] [US1] Integration tests for org-default endpoints in `tests/Ato.Copilot.Tests.Integration/Endpoints/OrgInheritanceEndpointTests.cs` — GET /org-defaults returns paginated results, POST /org-defaults/derive returns derivation summary, error paths (no capabilities returns empty)

---

## Phase 4: User Story 2 — System Inherits Org Defaults on Baseline Selection (Priority: P1)

**Goal**: When ISSM selects a baseline, the system auto-receives applicable org-level defaults for controls in that baseline, eliminating the "apply CSP profile" step.

**Independent Test**: Set up org defaults for 50 controls. Create system, select Moderate baseline. Verify 50 controls show "Inherited"/"Shared" with DesignationSource=OrgDerived, remaining show Undesignated.

### Implementation for User Story 2

- [X] T018 [US2] Hook `IOrgInheritanceService.PropagateToSystemAsync()` into `BaselineService.SelectBaselineAsync()` in `src/Ato.Copilot.Agents/Compliance/Services/BaselineService.cs` — call after inheritance snapshot reapplication (line ~119), before narrative auto-population, passing baseline control IDs
- [X] T019 [US2] Extend `ListInheritanceDesignations` response DTO in `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` — add `designationSource` and `orgDefault` (nested object with id, inheritanceType, provider, sourceCapabilities) to each item; add `orgDefaultCount`, `systemOverrideCount`, `sourceBreakdown` to summary
- [X] T020 [US2] Update list endpoint query in `DashboardEndpoints.cs` to LEFT JOIN OrgInheritanceDefault on ControlInheritance.OrgInheritanceDefaultId to populate the `orgDefault` field; include DesignationSource in response

**Checkpoint**: New system + baseline selection → controls auto-populated from org defaults. Build: `dotnet build Ato.Copilot.sln`. Verify with:
```
curl -s http://localhost:3001/api/dashboard/systems/{systemId}/inheritance | jq '.summary | {orgDefaultCount, systemOverrideCount}'
```

### Tests for User Story 2

- [X] T020a [P] [US2] Unit tests for propagation in `tests/Ato.Copilot.Tests.Unit/Services/OrgInheritanceServiceTests.cs` — test PropagateToSystemAsync: propagates only to controls without overrides, skips Manual/ProfileApply/CrmImport designations, handles empty org defaults, boundary: zero baseline controls

---

## Phase 5: User Story 3 — ISSM Overrides Org Defaults at System Level (Priority: P1)

**Goal**: ISSM can override any org-derived designation, with visual indicator. Can revert to org default with one click.

**Independent Test**: Override an org-derived "Inherited" to "Shared". Verify DesignationSource=Manual. Click revert → verify back to OrgDerived.

### Implementation for User Story 3

- [X] T021 [US3] Update `SetInheritanceDesignations` endpoint logic in `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` — when overriding an OrgDerived designation, set DesignationSource="Manual", preserve OrgInheritanceDefaultId for "diverged from" reference. For bulk operations, set audit ChangeSource="BulkUpdate" while DesignationSource remains "Manual". Validate: when inheritanceType="Customer", require non-empty customerResponsibility field (return 400 if missing).
- [X] T022 [US3] Add `POST /api/dashboard/systems/{systemId}/inheritance/revert-to-org-defaults` endpoint in `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs` — calls RevertToOrgDefaultsAsync with controlIds from request body
- [X] T023 [US3] Update `BaselineService.SetInheritanceAsync()` in `src/Ato.Copilot.Agents/Compliance/Services/BaselineService.cs` — pass DesignationSource through to ControlInheritance records and set OrgInheritanceDefaultId=null for manual overrides (or preserve it for "diverged from" tracking)

**Checkpoint**: Override + revert cycle works end-to-end. Build: `dotnet build Ato.Copilot.sln`. Verify:
```
curl -X PUT http://localhost:3001/api/dashboard/systems/{systemId}/inheritance -d '{"designations":[{"controlId":"AC-2","inheritanceType":"Shared","provider":"Custom","customerResponsibility":"Test"}],"setBy":"test","changeSource":"Manual"}'
curl -X POST http://localhost:3001/api/dashboard/systems/{systemId}/inheritance/revert-to-org-defaults -d '{"controlIds":["AC-2"],"revertedBy":"test"}'
```

### Tests for User Story 3

- [X] T023a [P] [US3] Unit tests for revert in `tests/Ato.Copilot.Tests.Unit/Services/OrgInheritanceServiceTests.cs` — test RevertToOrgDefaultsAsync: reverts override to org default, skips controls with no org default, customerResponsibility required validation when type=Customer (returns error)
- [X] T023b [P] [US3] Integration tests for revert endpoint in `tests/Ato.Copilot.Tests.Integration/Endpoints/OrgInheritanceEndpointTests.cs` — POST /revert-to-org-defaults happy path, partial revert (some controls have no org default), 400 when customerResponsibility missing for Customer type

---

## Phase 6: User Story 4 — Dashboard Shows Org Defaults vs. System Overrides (Priority: P2)

**Goal**: Visual distinction between org-derived and overridden designations. Source summary bar, filters, tooltips showing source capability info.

**Independent Test**: On system with mix of org defaults and overrides, verify summary bar counts, filter by "Org Defaults Only", hover org-default control → see capability name.

### Implementation for User Story 4

- [X] T024 [P] [US4] Add org-default API functions to `src/Ato.Copilot.Dashboard/src/api/inheritance.ts` — `getOrgDefaults(query)`, `deriveOrgDefaults()`, `revertToOrgDefaults(systemId, controlIds, revertedBy)`
- [X] T025 [US4] Extend `listInheritance()` in `src/Ato.Copilot.Dashboard/src/api/inheritance.ts` — add `source` query param, extend response types with `designationSource`, `orgDefault` nested object, `sourceBreakdown` in summary
- [X] T026 [US4] Add source filter dropdown ("All Sources", "Org Defaults Only", "System Overrides Only", "Undesignated") to `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx` — wire to list endpoint `source` query param
- [X] T027 [US4] Add designation source badges to inheritance table rows in `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx` — "Org Default" badge (green/blue), "Override" badge (amber/orange) next to control ID or in a new column
- [X] T028 [US4] Add secondary source summary bar below existing summary cards in `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx` — shows "N Org Default / N System Override / N Undesignated" using sourceBreakdown from API response
- [X] T029 [US4] Add org-default info tooltip/popover for org-derived rows in `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx` — on hover/click shows source capability name, linked component, mapping role from `orgDefault` field
- [X] T029a [US4] Add override info tooltip/popover for overridden rows in `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx` — on hover/click shows original org-level default value alongside current system-override value (covers US4 Acceptance Scenario 5)
- [X] T030 [US4] Add "View Org Defaults" button in page header — opens read-only modal/panel listing all org-level defaults via `getOrgDefaults()` API in `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx`

**Checkpoint**: Dashboard shows clear visual distinction between org defaults and overrides with filtering. Build frontend: `cd src/Ato.Copilot.Dashboard && npm run build`

---

## Phase 7: User Story 5 — CRM Generation Uses Effective Inheritance (Priority: P2)

**Goal**: CRM reflects effective inheritance (org defaults + overrides). Exports include "Designation Source" column.

**Independent Test**: System with 80 org-default + 15 overrides → generate CRM → verify override values used for 15, org-default values for 80, "Designation Source" column correct.

### Implementation for User Story 5

- [X] T031 [P] [US5] Extend `GenerateCrmAsync()` in `src/Ato.Copilot.Agents/Compliance/Services/BaselineService.cs` — include DesignationSource in CRM result items, map "OrgDerived"→"Org Default", "Manual"→"System Override", others accordingly
- [X] T032 [P] [US5] Extend `CrmExportService` CSV/Excel generation in `src/Ato.Copilot.Mcp/Services/CrmExportService.cs` — add "Designation Source" column to all layout formats (custom, FedRAMP, eMASS)

### Tests for User Story 5

- [X] T032a [P] [US5] Unit tests for CRM designation source in `tests/Ato.Copilot.Tests.Unit/Services/BaselineServiceTests.cs` — test GenerateCrmAsync includes DesignationSource mapping: OrgDerived→"Org Default", Manual→"System Override", undesignated→empty; verify CSV export contains "Designation Source" column header

**Checkpoint**: Generate CRM → export CSV → verify Designation Source column present. Build: `dotnet build Ato.Copilot.sln`. Verify:
```
curl -s http://localhost:3001/api/dashboard/systems/{systemId}/inheritance/crm | jq '.families[0].controls[0]'
```

---

## Phase 8: User Story 6 — Org Defaults Update When Capabilities Change (Priority: P2)

**Goal**: When capabilities are added/modified/deleted, org defaults re-derive and cascade to systems that haven't overridden.

**Independent Test**: Add new capability → verify org defaults updated → verify non-overridden systems see updated designations → verify overridden systems unchanged.

### Implementation for User Story 6

- [X] T033 [US6] Add cascade propagation to `OrgInheritanceService.DeriveOrgDefaultsAsync()`
- [X] T034 [US6] Add structured logging for cascade propagation

**Checkpoint**: Modify capability mapping → org defaults update → systems cascade. Build: `dotnet build Ato.Copilot.sln`. Verify:
```
curl -X POST http://localhost:3001/api/dashboard/inheritance/org-defaults/derive | jq '{affectedSystems, derivedCount, removedCount}'
```

---

## Phase 9: User Story 7 — Audit Trail Tracks Change Sources (Priority: P3)

**Goal**: Audit log shows source of each change: OrgDerived, OrgPropagation, ProfileApply, Manual.

**Independent Test**: Perform org-default propagation, manual override, CSP profile apply → review audit log → all three source types present.

### Implementation for User Story 7

- [X] T035 [US7] Update audit entry creation in `BaselineService.SetInheritanceAsync()`
- [X] T036 [US7] Ensure all `OrgInheritanceService` methods create audit entries with correct source
- [X] T037 [P] [US7] Extend audit panel/endpoint response in `src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs`

### Tests for User Story 7

- [X] T037a [P] [US7] Integration tests for audit source types

**Checkpoint**: Audit trail shows all source types. Build: `dotnet build Ato.Copilot.sln`. Verify:
```
curl -s http://localhost:3001/api/dashboard/systems/{systemId}/inheritance/AC-2/audit | jq '.[].changeSource'
```

---

## Phase 10: User Story 8 — CSP Profile Application Becomes Optional (Priority: P3)

**Goal**: Apply CSP Profile demoted to "More Actions" dropdown. Banner shows org-default coverage. Backward compatible when no org defaults exist.

**Independent Test**: System with org defaults → "Apply CSP Profile" in More Actions, banner shows coverage count. System with no org defaults → button prominent (backward compat).

### Implementation for User Story 8

- [X] T038 [US8] Reorganize action buttons in `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx`
- [X] T039 [US8] Add org-default coverage banner in `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx`
- [X] T040 [US8] Update CSP profile conflict resolution default in `src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx`
- [X] T041 [US8] Add "Revert Selected to Org Defaults" button in BulkUpdateToolbar

**Checkpoint**: Button layout matches FR-015. Banner shown when org defaults exist. CSP Profile demoted. Build and verify: `cd src/Ato.Copilot.Dashboard && npm run build`

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, final validation, deployment

- [X] T042 [P] Update `docs/architecture/data-model.md` with OrgInheritanceDefault entity and ControlInheritance extensions
- [X] T043 [P] Update `docs/api/vscode-extension.md` or relevant API docs with new org-default endpoints
- [X] T044 Build all projects: `dotnet build Ato.Copilot.sln` — zero warnings
- [X] T045 Rebuild Docker: `docker compose -f docker-compose.mcp.yml up -d --build`
- [X] T046 Run quickstart.md validation — verify org defaults derive from capabilities, system inherits on baseline selection, override + revert cycle, CRM export with source column
- [X] T047 [P] Add response-time assertions to integration tests for org-default endpoints — derivation ≤5s for 800 controls, page load ≤5s, CRM generation ≤30s (SC-001/SC-002/SC-003)
- [X] T048 Verify Feature 043 CSP profile apply code sets `InheritanceChangeSource.ProfileApply` in audit entries — if missing, add `changeSource: ProfileApply` to Apply CSP Profile handler in `BaselineService` or relevant endpoint

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (entity + interface must exist)
- **US1 (Phase 3)**: Depends on Phase 2 (derivation engine)
- **US2 (Phase 4)**: Depends on Phase 2 + US1 (org defaults must exist to propagate)
- **US3 (Phase 5)**: Depends on Phase 2 + US2 (system must have org-derived designations to override)
- **US4 (Phase 6)**: Depends on US2 (needs designationSource field populated to show badges)
- **US5 (Phase 7)**: Depends on US2 (needs designationSource in CRM data)
- **US6 (Phase 8)**: Depends on US1 + US2 (derivation + propagation must work before cascade)
- **US7 (Phase 9)**: Depends on US1-US3 (audit entries created by those stories)
- **US8 (Phase 10)**: Depends on US4 (needs source summary data for banner/button logic)
- **Polish (Phase 11)**: After all desired stories complete

### User Story Dependencies

```
Phase 1 (Setup) ──► Phase 2 (Foundational)
                        │
                        ├──► US1 (Derivation) ──► US6 (Cascade on Change)
                        │        │
                        │        ▼
                        ├──► US2 (System Propagation) ──► US4 (Dashboard UI) ──► US8 (CSP Optional)
                        │        │                            │
                        │        ▼                            ▼
                        └──► US3 (Override + Revert)     US5 (CRM Export)
                                 │
                                 ▼
                             US7 (Audit Trail)
```

### Parallel Opportunities

- **Phase 1**: T003, T006, T007 can run in parallel
- **Phase 2**: T008-T011 are sequential (each builds on prior), T012 can parallel with T011
- **US1**: T016, T017 can run in parallel (different endpoints)
- **US4**: T024 can start in parallel with T025-T030 (API client vs page UI)
- **US5 + US6**: Can run in parallel (different files — BaselineService/CrmExportService vs OrgInheritanceService)
- **US7**: T037 can run in parallel with T035-T036 (frontend vs backend)
- **US8**: T038-T041 are all in ControlInheritance.tsx — must be sequential

---

## Parallel Example: After Foundational Complete

```bash
# Developer A: US1 (Phases 3)
T013-T017: Hook derivation into CapabilityService, add endpoints

# Developer B: US4 frontend (Phase 6, after US2)
T024-T030: Dashboard UI badges, filters, source bar, tooltips

# After US1 complete:
# Developer A: US2 (Phase 4) → US3 (Phase 5) → US6 (Phase 8)
# Developer B: US5 (Phase 7) + US7 (Phase 9)
```

---

## Implementation Strategy

### MVP First (User Stories 1-3 Only)

1. Complete Phase 1: Setup — entities, migration, interface
2. Complete Phase 2: Foundational — OrgInheritanceService core methods
3. Complete Phase 3: US1 — Derivation from capabilities + endpoints
4. Complete Phase 4: US2 — System propagation on baseline selection
5. Complete Phase 5: US3 — Override + revert
6. **STOP AND VALIDATE**: Org defaults derive → systems inherit → ISSM can override and revert
7. Deploy/demo — this is the MVP

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 → Org defaults auto-derive from capabilities (MVP foundation)
3. US2 + US3 → Full override/revert cycle (MVP complete!)
4. US4 → Dashboard indicators (UX improvement)
5. US5 + US6 → CRM export + cascade updates (operational maturity)
6. US7 → Audit trail (compliance readiness)
7. US8 → CSP Profile optional (workflow simplification)

---

## Summary

| Metric | Count |
|--------|-------|
| Total Tasks | 56 |
| Setup (Phase 1) | 7 |
| Foundational (Phase 2) | 5 |
| US1 — Derivation (P1) | 5 + 2 test |
| US2 — System Propagation (P1) | 3 + 1 test |
| US3 — Override + Revert (P1) | 3 + 2 test |
| US4 — Dashboard UI (P2) | 8 |
| US5 — CRM Export (P2) | 2 + 1 test |
| US6 — Cascade Updates (P2) | 2 |
| US7 — Audit Trail (P3) | 3 + 1 test |
| US8 — CSP Optional (P3) | 4 |
| Polish (Final) | 7 |
| Parallelizable [P] tasks | 20 |
| MVP scope (US1-US3) | 28 tasks |
