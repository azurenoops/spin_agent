# Tasks: Control Inheritance & Customer Responsibility Matrix

**Input**: Design documents from `/specs/043-control-inheritance/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api.md, quickstart.md

**Tests**: Required per constitution (Principle III). Test tasks included in each phase.

**Organization**: Tasks grouped by user story for independent implementation and testing. US1+US2 (both P1) are combined in a single phase because bulk update extends the view/manage page. US6 (P4, cross-portfolio) is deferred to a future feature.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Add new data model, register in EF context, and create frontend skeleton files

- [X] T001 Add InheritanceAuditEntry model and InheritanceChangeSource enum to src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs
- [X] T002 Register DbSet\<InheritanceAuditEntry\> and add EF entity configuration (indexes on ControlInheritanceId, ControlBaselineId+Timestamp, Timestamp) to src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs
- [X] T003 [P] Create TypeScript type definitions (InheritanceDesignation, InheritanceSummary, InheritanceListResponse, CrmResult, CrmFamilyGroup, CrmEntry, AuditEntry, CspProfile, ImportPreview) in src/Ato.Copilot.Dashboard/src/types/inheritance.ts
- [X] T004 [P] Create Axios API client with functions for list, set, getCrm, exportCrm, getAudit, getProfiles, applyProfile, importPreview, importApply in src/Ato.Copilot.Dashboard/src/api/inheritance.ts
- [X] T034 [P] Create shared known-providers constant list (Azure Government FedRAMP High, AWS GovCloud FedRAMP High, etc.) in src/Ato.Copilot.Dashboard/src/components/inheritance/constants.ts

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Audit logging, core REST endpoints, and dashboard navigation — MUST complete before any UI story

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T005 Add audit entry creation logic to BaselineService.SetInheritanceAsync — capture previous values, create InheritanceAuditEntry for each changed control, persist via DbContext in src/Ato.Copilot.Agents/Compliance/Services/BaselineService.cs
- [X] T006 Add GET /systems/{systemId}/inheritance endpoint with family, inheritanceType, search filters and page/pageSize/sortBy/sortDirection pagination to src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T007 Add PUT /systems/{systemId}/inheritance endpoint accepting designations array and changeSource, calling BaselineService.SetInheritanceAsync in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T008 [P] Add GET /systems/{systemId}/inheritance/{controlId}/audit endpoint returning chronological audit entries from InheritanceAuditEntries in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T009 Add Control Inheritance page route under SystemLayout and sidebar navigation entry in the Compliance Posture group (before Narratives, after Categorization) in src/Ato.Copilot.Dashboard
- [X] T035 Add role validation for write endpoints (PUT /inheritance, POST /apply-profile, POST /import/apply) — check user role from auth context, return 403 if not AO or Security Engineer per FR-026 in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs

**Checkpoint**: Core API ready — all endpoints callable, audit entries created on writes, role-gated writes enforced, dashboard has route stub

---

## Phase 3: User Story 1 — View & Manage Designations + User Story 2 — Bulk Update (Priority: P1) 🎯 MVP

**Goal**: Security engineers can view all inheritance designations in a filterable table with summary bar, edit individual controls inline, select multiple controls for bulk update, and view per-control audit history.

**Independent Test**: Open Control Inheritance page for a system with a baseline. Verify summary bar shows all Undesignated. Set one control to "Inherited" with provider — confirm row updates and summary recalculates. Select 10 controls, bulk-update to "Shared" — confirm all rows update. Click a control to view audit trail — verify entries appear.

- [X] T010 [P] [US1] Create InheritanceSummaryBar component displaying total, inherited, shared, customer, undesignated counts and inheritance percentage in src/Ato.Copilot.Dashboard/src/components/inheritance/InheritanceSummaryBar.tsx
- [X] T011 [P] [US1] Create InheritanceTable component with columns (Control ID, Family, Inheritance Type, Provider, Customer Responsibility, Set By, Set At), family/type filter dropdowns, search, pagination, and inline-edit capability in src/Ato.Copilot.Dashboard/src/components/inheritance/InheritanceTable.tsx
- [X] T012 [P] [US1] Create AuditHistoryPanel component showing chronological change log for a selected control in src/Ato.Copilot.Dashboard/src/components/inheritance/AuditHistoryPanel.tsx
- [X] T013 [P] [US2] Create BulkUpdateToolbar component with checkbox selection, inheritance type dropdown, provider dropdown (known providers + free text), and Apply button in src/Ato.Copilot.Dashboard/src/components/inheritance/BulkUpdateToolbar.tsx
- [X] T014 [US1] Assemble ControlInheritance page integrating InheritanceSummaryBar, InheritanceTable, BulkUpdateToolbar, AuditHistoryPanel, and "Select a baseline first" empty state in src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx

### Tests for US1+US2

- [X] T036 [P] [US1] Write unit tests for InheritanceAuditEntry creation in BaselineService.SetInheritanceAsync — positive: new designation creates audit entry, update records previous values; negative: identical values skip audit in tests/Ato.Copilot.Tests.Unit/InheritanceAuditTests.cs
- [X] T037 [P] [US1] Write integration tests for GET /inheritance (pagination, family filter, type filter, Undesignated filter), PUT /inheritance (single, bulk, invalid type 400, missing provider 400), and GET /{controlId}/audit endpoints in tests/Ato.Copilot.Tests.Integration/InheritanceEndpointTests.cs

**Checkpoint**: MVP complete — view, filter, inline-edit, bulk-update, and audit trail all functional with passing tests

---

## Phase 4: User Story 3 — Generate & Export CRM (Priority: P2)

**Goal**: Users can generate a family-grouped Customer Responsibility Matrix and export it as CSV or Excel in Custom, FedRAMP, or eMASS format.

**Independent Test**: With inheritance designations set for 20+ controls, click "Generate CRM." Verify family-grouped table with correct counts. Export as CSV in FedRAMP format — verify downloaded file has FedRAMP column structure. Export as Excel in eMASS format — verify file opens with correct layout.

- [X] T015 [P] [US3] Create CrmExportService with CSV (StringBuilder, RFC 4180 quoting) and Excel (ClosedXML) generators for Custom, FedRAMP, and eMASS layouts, and register in DI in src/Ato.Copilot.Mcp/Services/CrmExportService.cs
- [X] T016 [US3] Add GET /systems/{systemId}/inheritance/crm endpoint wrapping BaselineService.GenerateCrmAsync in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T017 [US3] Add GET /systems/{systemId}/inheritance/crm/export endpoint with format (csv/excel) and layout (custom/fedramp/emass) query params, returning file download in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T018 [P] [US3] Create CrmView component with family-grouped table, summary statistics, and export buttons with format selector dialog in src/Ato.Copilot.Dashboard/src/components/inheritance/CrmView.tsx
- [X] T019 [US3] Integrate CRM view toggle (Generate CRM button and CrmView panel) into ControlInheritance page in src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx

### Tests for US3

- [X] T038 [P] [US3] Write unit tests for CrmExportService — CSV and Excel generators for Custom, FedRAMP, and eMASS layouts (6 format combinations), RFC 4180 quoting edge cases, and empty baseline handling in tests/Ato.Copilot.Tests.Unit/CrmExportServiceTests.cs

**Checkpoint**: CRM generation and export fully functional across all three formats with passing tests

---

## Phase 5: User Story 4 — Apply CSP Inheritance Profile (Priority: P2)

**Goal**: Users can apply a pre-built CSP profile (Azure Government FedRAMP High) to bulk-designate all controls with preview and conflict resolution.

**Independent Test**: On a system with no designations, click "Apply CSP Profile," select Azure Government FedRAMP High, preview counts, confirm. Verify all applicable controls receive designations and Shared controls have customer responsibility pre-filled. On a system with existing designations, verify "Skip existing" and "Overwrite all" conflict options work correctly.

- [X] T020 [P] [US4] Create CspProfileService that loads all JSON profiles from src/seed-data/csp-profiles/ at startup into an in-memory collection, with singleton DI registration in src/Ato.Copilot.Mcp/Services/CspProfileService.cs
- [X] T021 [P] [US4] Create Azure Government FedRAMP High seed profile covering NIST 800-53 Rev 5 High baseline controls with inheritance types and customer responsibility text in src/seed-data/csp-profiles/azure-gov-fedramp-high.json
- [X] T022 [US4] Add GET /systems/{systemId}/inheritance/csp-profiles endpoint listing available profiles from CspProfileService in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T023 [US4] Add POST /systems/{systemId}/inheritance/apply-profile endpoint with preview mode and conflict resolution (skip/overwrite), calling BaselineService.SetInheritanceAsync with ProfileApply source in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T024 [P] [US4] Create CspProfileDialog with profile selection dropdown, preview summary (inherited/shared/customer counts, conflicts), conflict resolution radio buttons, and confirm/cancel actions in src/Ato.Copilot.Dashboard/src/components/inheritance/CspProfileDialog.tsx
- [X] T025 [US4] Integrate Apply CSP Profile button and CspProfileDialog into ControlInheritance page in src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx

### Tests for US4

- [X] T039 [P] [US4] Write unit tests for CspProfileService — JSON loading at startup, control matching against baseline, unmatched controls silently skipped, conflict counts with skip/overwrite modes in tests/Ato.Copilot.Tests.Unit/CspProfileServiceTests.cs

**Checkpoint**: CSP profile application end-to-end — profile loads, preview works, bulk apply with conflict resolution, with passing tests

---

## Phase 6: User Story 5 — Import CRM Spreadsheet (Priority: P3)

**Goal**: Users can upload a CSV or Excel CRM file, map columns, preview the import, and apply designations with conflict resolution.

**Independent Test**: Upload a CSV with 50 control rows. Map columns in preview dialog. Verify matched controls show correct designations. Apply with "Skip existing" — confirm only new designations applied. Upload a file with invalid control IDs — verify they are flagged and excluded.

- [X] T026 [US5] Add CRM file parsing (CSV via StreamReader, Excel via ClosedXML) with column detection and sample row extraction to CrmExportService in src/Ato.Copilot.Mcp/Services/CrmExportService.cs
- [X] T027 [US5] Add POST /systems/{systemId}/inheritance/import/preview endpoint accepting multipart file upload, returning detected columns, suggested mapping, and sample rows in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T028 [US5] Add POST /systems/{systemId}/inheritance/import/apply endpoint accepting column mapping and conflict resolution, validating control IDs against baseline, calling SetInheritanceAsync with CrmImport source in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T029 [P] [US5] Create CrmImportDialog with file upload dropzone, column mapping UI (source column → target field), preview table with row count and designation breakdown, conflict resolution options, and not-found flagging in src/Ato.Copilot.Dashboard/src/components/inheritance/CrmImportDialog.tsx
- [X] T030 [US5] Integrate Import CRM button and CrmImportDialog into ControlInheritance page in src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx

### Tests for US5

- [X] T040 [P] [US5] Write integration tests for POST /import/preview (column detection, suggested mapping, sample rows) and POST /import/apply (valid import, skip conflicts, overwrite conflicts, not-found control flagging) in tests/Ato.Copilot.Tests.Integration/InheritanceEndpointTests.cs

**Checkpoint**: CRM import end-to-end — upload, map, preview, apply with validation and conflict handling, with passing tests

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, performance validation, and final end-to-end testing

- [X] T041 [P] Add Control Inheritance page documentation to docs/guides/ — page overview, managing designations, bulk update workflow, CRM generation and export, CSP profile application, CRM import workflow
- [X] T042 [P] Update docs/architecture/overview.md and docs/api/mcp-server.md to include Control Inheritance REST endpoints and data model references
- [X] T043 Validate performance targets: SC-001 (page load <2s for 325-control baseline), SC-002 (bulk update 50 controls <3s), SC-003 (CRM generation <3s) — add response-time assertions to integration tests per Constitution VIII
- [X] T044 Run quickstart.md manual test flow end-to-end and validate all 9 endpoints respond correctly

---

## Phase 8: User Story 7 — Categorization & Baseline Management Page (Priority: P1)

**Goal**: Users can view baseline details (level, overlay, control counts, family breakdown, tailoring history), select a new baseline, recategorize the system, or re-select an existing baseline from a combined Categorization page under Compliance Posture. When categorization level changes, the baseline auto-cascades with inheritance preservation.

**Independent Test**: Navigate to the Categorization page for a system with no baseline — verify "Select Baseline" CTA. Select a baseline. Verify summary cards, family breakdown table. Open RecategorizeDialog, change impact level — verify baseline auto-reselects and cascade banner appears.

- [X] T045 [US7] Add GET /systems/{systemId}/baseline endpoint returning baseline details (level, overlay, control counts, family breakdown, tailoring history, control IDs) to src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T046 [P] [US7] Add BaselineDetailResponse TypeScript type and getBaselineDetail API function to src/Ato.Copilot.Dashboard/src/api/systemDetail.ts
- [X] T047 [US7] Create BaselineManagement page with loading skeleton, no-baseline CTA state, detail view (level badge, summary cards, metadata panel, family breakdown table with search filter and distribution bars, tailoring history table), SelectBaselineDialog modal, and RecategorizeDialog with FIPS 199 info type picker, adjustmentJustification auto-population, and cascadeBanner for auto-baseline reselection in src/Ato.Copilot.Dashboard/src/pages/BaselineManagement.tsx
- [X] T048 [US7] Add BaselineManagement import and route (path="baseline") to src/Ato.Copilot.Dashboard/src/App.tsx
- [X] T049 [US7] Add "Categorization" nav entry as first item in Compliance Posture group (path: baseline), with Control Inheritance before Narratives in src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx
- [X] T050 [US7] Rebuild and deploy Docker containers (ato-copilot + ato-dashboard) — verify healthy

**Checkpoint**: Baseline management page accessible via nav, shows baseline details or select CTA, deployed and healthy

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US1+US2 (Phase 3)**: Depends on Foundational — the MVP
- **US3 (Phase 4)**: Depends on Foundational — can run in parallel with Phase 3 (backend tasks)
- **US4 (Phase 5)**: Depends on Foundational — can run in parallel with Phases 3-4 (backend tasks)
- **US5 (Phase 6)**: Depends on Phase 4 (CrmExportService) — extends the same service
- **Polish (Phase 7)**: Depends on all desired user stories being complete
- **Baseline Management (Phase 8)**: Depends on Foundational — standalone page, no dependencies on other user stories

### User Story Dependencies

- **US1+US2 (P1)**: Can start after Foundational — no dependencies on other stories
- **US3 (P2)**: Can start after Foundational — CRM generation wraps existing service, independent of US1/US2 UI
- **US4 (P2)**: Can start after Foundational — profile service is standalone, dialog is independent
- **US5 (P3)**: Depends on T015 (CrmExportService) from US3 — import parsing lives in the same service

### Within Each User Story

- Backend services/endpoints before frontend components
- Frontend components before page assembly (integration task)
- Components marked [P] can be developed in parallel
- Page-level integration task (T014, T019, T025, T030) is always last in the phase

### Parallel Opportunities

- **Phase 1**: T003, T004, T034 (frontend) can run in parallel with T001→T002 (backend)
- **Phase 2**: T008 (audit endpoint) can run in parallel with T006→T007 (list/set endpoints); T035 (role validation) after T007
- **Phase 3**: T010, T011, T012, T013 can all run in parallel (separate component files); T036, T037 (tests) after T005+T014
- **Phase 4**: T015 (backend service) and T018 (frontend component) can run in parallel; T038 (tests) after T015
- **Phase 5**: T020, T021 (backend) and T024 (frontend dialog) can run in parallel; T039 (tests) after T020
- **Phase 6**: T029 (frontend dialog) can run in parallel with T026 (backend parsing); T040 (tests) after T028
- **Cross-phase**: Backend tasks from US3, US4 can overlap with frontend work from US1+US2

---

## Parallel Example: Phase 3 (US1+US2)

```bash
# Launch all components in parallel (separate files, no dependencies):
Task T010: "InheritanceSummaryBar in .../InheritanceSummaryBar.tsx"
Task T011: "InheritanceTable in .../InheritanceTable.tsx"
Task T012: "AuditHistoryPanel in .../AuditHistoryPanel.tsx"
Task T013: "BulkUpdateToolbar in .../BulkUpdateToolbar.tsx"

# Then assemble page (depends on all 4 above):
Task T014: "ControlInheritance page in .../ControlInheritance.tsx"
```

---

## Implementation Strategy

### MVP First (US1+US2 Only)

1. Complete Phase 1: Setup (T001-T004, T034)
2. Complete Phase 2: Foundational (T005-T009, T035)
3. Complete Phase 3: US1+US2 — View, Manage, Bulk Update (T010-T014, T036-T037)
4. **STOP and VALIDATE**: Run tests, test the Control Inheritance page independently
5. Deploy/demo if ready — users can view, edit, and bulk-update designations

### Incremental Delivery

1. Setup + Foundational → Infrastructure ready
2. Add US1+US2 → View/edit/bulk page → Deploy (MVP!)
3. Add US3 → CRM generation + export → Deploy
4. Add US4 → CSP profiles with one-click apply → Deploy
5. Add US5 → CRM import for external data → Deploy
6. Each story adds capability without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: Phase 3 (US1+US2 frontend components)
   - Developer B: Phase 4 (US3 CrmExportService + endpoints) + Phase 5 (US4 CspProfileService + endpoints)
3. After backend for US3/US4 is ready:
   - Developer A finishes Phase 3, picks up US3/US4/US5 frontend
   - Developer B adds Phase 6 backend (import parsing + endpoints)

---

## Notes

- US6 (Cross-Portfolio Inheritance, P4) is deferred — requires schema migration and is explicitly marked "future" in the spec
- The PUT /inheritance endpoint handles both single edit (changeSource: "Manual") and bulk update (changeSource: "BulkUpdate") — same endpoint, different UI flows
- CRM generation (GET /crm) wraps the existing `BaselineService.GenerateCrmAsync()` with no modifications
- CSP profile seed data (T021) requires research into Microsoft's published Azure Government CRM for accurate control-level designations
- ClosedXML is already a project dependency — no additional packages needed for Excel export
- T035 implements role-gated writes per FR-026 using the existing auth context pattern from `AuditLoggingMiddleware.cs`
- Total tasks: 60 (50 original + 10 post-implementation enhancements)

---

## Phase 9: Post-Implementation Enhancements

**Purpose**: Enhancements discovered and implemented after initial feature delivery — narrative auto-status, categorization cascade, combined page, nav reorder, terminology alignment, bug fixes, and layout fixes.

- [X] T051 [US8] Add narrative auto-status logic to BaselineService.SetInheritanceAsync — after applying designations, query ControlImplementation records and update ImplementationStatus (Inherited→Implemented, Shared→PartiallyImplemented); add NarrativesAutoUpdated to InheritanceResult in src/Ato.Copilot.Agents/Compliance/Services/BaselineService.cs and src/Ato.Copilot.Core/Interfaces/Compliance/IBaselineService.cs
- [X] T052 [US8] Update all 3 inheritance write endpoints (SetInheritanceDesignations, ApplyCspProfile, ImportCrmApply) to include narrativesAutoUpdated in response in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T053 [P] [US8] Add narrativesAutoUpdated field to SetInheritanceResponse, ApplyProfileResult, ImportApplyResult TypeScript types in src/Ato.Copilot.Dashboard/src/types/inheritance.ts
- [X] T054 [US8] Add narrative auto-update blue dismissable banner to ControlInheritance.tsx (handleSave, handleBulkApply, handleApplyProfile capture result and show banner when narrativesAutoUpdated > 0) in src/Ato.Copilot.Dashboard/src/pages/ControlInheritance.tsx
- [X] T055 [US8] Add auto-updated narratives count display in CrmImportDialog result step in src/Ato.Copilot.Dashboard/src/components/inheritance/CrmImportDialog.tsx
- [X] T056 [US9] Modify SelectBaselineAsync to snapshot inheritance designations before deleting old baseline, reapply to matching controls in new baseline, and auto-update narrative statuses in src/Ato.Copilot.Agents/Compliance/Services/BaselineService.cs
- [X] T057 [US9] Add categorization-to-baseline cascade in POST /systems/{systemId}/categorization endpoint — inject IBaselineService, capture previousBaselineLevel, auto-call SelectBaselineAsync if level changed, include baselineReselected/baselineControls/inheritancesReapplied in response in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T058 [P] [US9] Add SetCategorizationResponse cascade fields (baselineReselected, baselineControls, inheritancesReapplied) to src/Ato.Copilot.Dashboard/src/api/systemDetail.ts and add cascadeBanner state + handleCategorizationSaved handler to BaselineManagement.tsx
- [X] T059 Fix resources→components terminology in RmfLifecycleService.cs (CheckPrepareToCategorizeAsync: query ComponentSystemAssignments), TodoService.cs (boundary check text), and BoundaryManagement.tsx (remove Resources column, update text)
- [X] T060 [P] Fix regenerate narrative fallback in CapabilityService.cs (RegenerateNarrativeWithAiAsync falls back to GenerateEnrichedNarrative when AI disabled) and add regenError amber banner to Narratives.tsx
- [X] T061 [P] Fix adjustmentJustification auto-population for non-provisional info types in RecategorizeDialog (BaselineManagement.tsx) and SetCategorization wizard step (SetCategorization.tsx)
- [X] T062 [P] Add min-w-0 to main element in PageLayout.tsx to fix table overflow under side panel

**Checkpoint**: All post-implementation enhancements complete — narrative auto-status, categorization cascade, combined page, nav reorder, terminology, regenerate fallback, validation fix, layout fix
