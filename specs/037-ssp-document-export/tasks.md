# Tasks: SSP Document Export

**Input**: Design documents from `/specs/037-ssp-document-export/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Included — plan.md lists unit and integration test files in the project structure.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuration for export file storage and limits

- [x] T001 Add ExportSettings configuration section (DataPath, RetentionDays, MaxExportSizeBytes, MaxTemplateSizeBytes) to src/Ato.Copilot.Mcp/appsettings.json and bind to a strongly-typed ExportSettings class in src/Ato.Copilot.Core/Configuration/ExportSettings.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entity models, DTOs, service interface, database tables, DI wiring, and core service scaffolding that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T002 [P] Create SspExport entity with 14 fields per data-model.md (Id, SystemId, Format, Status, FilePath, FileSize, ContentHash, TemplateId, GeneratedBy, GeneratedAt, CompletedAt, ExpiresAt, ErrorMessage, ControlCount) in src/Ato.Copilot.Core/Models/Compliance/SspExport.cs
- [x] T003 [P] Create SspTemplate entity with 11 fields per data-model.md (Id, Name, Description, FilePath, FileSize, MergeFields, IsDefault, IsActive, UploadedBy, UploadedAt, UpdatedAt) in src/Ato.Copilot.Core/Models/Compliance/SspTemplate.cs
- [x] T004 [P] Create request/response DTOs — CreateExportRequest, ExportSummaryDto, ExportDetailDto, TemplateListDto, CreateTemplateResponse, SspExportJob record in src/Ato.Copilot.Core/Dtos/Dashboard/SspExportDtos.cs
- [x] T005 [P] Create ISspExportService interface with EnqueueExportAsync, GetExportAsync, ListExportsAsync, GetExportFileStreamAsync, GenerateDocxAsync, GeneratePdfAsync, GenerateOscalJsonAsync, UploadTemplateAsync, ListTemplatesAsync, DeleteTemplateAsync, RenameTemplateAsync in src/Ato.Copilot.Core/Interfaces/Compliance/ISspExportService.cs
- [x] T006 Implement SspExportService scaffolding — constructor DI (ComplianceDbContext, SspService, DocumentTemplateService, OscalSspExportService, IHubContext<NotificationHub>, ILogger, IOptions<ExportSettings>), Channel<SspExportJob> queue, file I/O helpers (EnsureDirectoryExists, ComputeSha256, GetExportFilePath), EnqueueExportAsync, ListExportsAsync, GetExportAsync, GetExportFileStreamAsync in src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs
- [x] T007 Implement SspExportBackgroundService : BackgroundService — Channel consumer loop, format dispatch (docx→GenerateDocxAsync, pdf→GeneratePdfAsync, json→GenerateOscalJsonAsync), status transitions (Pending→Processing→Completed/Failed), SignalR notification dispatch (SspExportReady, SspExportFailed) via IHubContext<NotificationHub> in src/Ato.Copilot.Agents/Compliance/Services/SspExportBackgroundService.cs
- [x] T008 Register infrastructure in Program.cs — CREATE TABLE SspExports and SspTemplates with indexes in EnsureSchemaAdditionsAsync, add DbSet<SspExport> and DbSet<SspTemplate>, register ISspExportService/SspExportService (scoped), register SspExportBackgroundService (hosted), register Channel<SspExportJob> (singleton), bind ExportSettings from configuration in src/Ato.Copilot.Mcp/Program.cs

**Checkpoint**: Foundation ready — all entities, tables, DI, and service scaffolding in place. User story implementation can now begin.

---

## Phase 3: User Story 1 — Export SSP as Word Document (Priority: P1) 🎯 MVP

**Goal**: ISSM/ISSO clicks "Export SSP" → Word, receives a .docx with all 13 NIST 800-18 sections populated from system data

**Independent Test**: POST to /api/dashboard/systems/{systemId}/exports with format "docx", poll status until Completed, download the file, open in Word and verify all sections present

### Implementation for User Story 1

- [x] T009 [US1] Implement GenerateDocxAsync in SspExportService — call SspService.GenerateSspAsync to get SspDocument, map Markdown sections to the 17 SSP merge fields (SystemName, SecurityCategorization, ControlNarratives, AuthorizationBoundary, Components, etc.), call DocumentTemplateService.RenderDocxAsync with default template, write result to exports/{systemId}/{exportId}.docx, enforce 50 MB limit (FR-020: reject and set Status=Failed if exceeded, warn in logs at 40 MB), compute SHA-256 hash, update SspExport entity with FilePath/FileSize/ContentHash/ControlCount/CompletedAt in src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs
- [x] T010 [US1] Add POST /systems/{systemId}/exports endpoint — parse CreateExportRequest body, validate format is docx|pdf|json, validate systemId exists, extract user identity from JWT, RBAC check (ISSM/ISSO/AO), call EnqueueExportAsync, return 202 Accepted with export summary per contracts/api.md in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T011 [US1] Add GET /systems/{systemId}/exports/{exportId}/download endpoint — validate export exists and status is Completed, resolve file path via GetExportFileStreamAsync, return FileStreamResult with Content-Type per format (application/vnd.openxmlformats-officedocument.wordprocessingml.document for docx) and Content-Disposition attachment header per contracts/api.md in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T012 [US1] Add structured audit logging for export operations — Serilog log with userId, systemId, format, contentHash, fileSize, controlCount, durationMs on export completion; log export failures with error details per FR-021 in src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs
- [x] T012a [US1] Add export progress reporting — dispatch SignalR SspExportProgress events (step name, percentage) at key milestones during GenerateDocxAsync/GeneratePdfAsync/GenerateOscalJsonAsync (loading data: 20%, rendering: 60%, writing file: 80%, computing hash: 90%) per contracts/api.md SignalR events and Constitution VII (operations >2s MUST provide progress indicators) in src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs

**Checkpoint**: Word SSP export works end-to-end via API. Can be tested with curl per quickstart.md.

---

## Phase 4: User Story 5 — Documents Page Integration (Priority: P1)

**Goal**: Documents page shows "Export SSP" button, export dialog with format selection, and export history table with download links

**Independent Test**: Navigate to Documents page, click Export SSP, select Word, see progress, receive notification, see export in history table, click Download

### Implementation for User Story 5

- [x] T013 [P] [US5] Create exports API client — requestExport(systemId, format, templateId?), listExports(systemId, format?, limit?, offset?), getExport(systemId, exportId), downloadExport(systemId, exportId) using axios with Bearer auth in src/Ato.Copilot.Dashboard/src/api/exports.ts
- [x] T014 [US5] Add GET /systems/{systemId}/exports endpoint — query SspExports by systemId with optional format filter, pagination (limit/offset), order by GeneratedAt DESC, join SspTemplate for templateName, return items array with total count per contracts/api.md in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T015 [US5] Add GET /systems/{systemId}/exports/{exportId} endpoint — return export detail with status, fileSize, contentHash, controlCount, generatedBy, templateName, expiresAt per contracts/api.md in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T016 [US5] Create ExportSspDialog component — modal with format radio buttons (Word/PDF/OSCAL), template dropdown (populated from GET /templates when templates exist), Export button that calls requestExport, progress spinner, SignalR listener for SspExportReady/SspExportFailed events via existing useNotifications hook, auto-close on completion with download link in src/Ato.Copilot.Dashboard/src/components/ExportSspDialog.tsx
- [x] T017 [US5] Update Documents page — add "Export SSP" button in ExportsSection header (disabled with tooltip when no baseline selected per FR-009), add "Manage Templates" button next to Export SSP (visible to ISSM/Admin roles per FR-019), render ExportSspDialog on Export SSP click, render TemplateManagementDialog on Manage Templates click, add export history table showing format icon, generatedBy, generatedAt, fileSize, status badge, and Download button for completed exports (filter Status != Failed from visible list per FR-017), limit to 10 most recent with "View All" expansion in src/Ato.Copilot.Dashboard/src/pages/Documents.tsx

**Checkpoint**: Full UI flow works — user can export Word SSP from Documents page and see it in history. MVP complete.

---

## Phase 5: User Story 2 — Export SSP as PDF (Priority: P2)

**Goal**: Export SSP as a formatted, print-ready PDF with table of contents, page numbers, headers/footers

**Independent Test**: Export SSP as PDF, open in a PDF viewer, verify TOC with clickable links, page numbers, all 13 sections rendered, control tables don't break awkwardly across pages

### Implementation for User Story 2

- [x] T018 [US2] Implement GeneratePdfAsync in SspExportService — call SspService.GenerateSspAsync, parse Markdown sections into structured content blocks, render via QuestPDF Document.Create() fluent API with: letter-size pages, header with system name and "SYSTEM SECURITY PLAN", footer with page numbers, two-pass table of contents (first pass collects section titles + page numbers, second pass renders TOC page), control narrative tables using ShowEntire/KeepTogether to prevent mid-row page breaks, enforce 50 MB limit (FR-020: reject and set Status=Failed if exceeded, warn at 40 MB), write result to exports/{systemId}/{exportId}.pdf in src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs

**Checkpoint**: PDF export works via the same dialog — user selects PDF format from ExportSspDialog, receives formatted PDF.

---

## Phase 6: User Story 3 — Export SSP as OSCAL JSON (Priority: P3)

**Goal**: Export SSP in NIST OSCAL SSP JSON format for machine-readable interoperability with eMASS, XACTA, and FedRAMP pipelines

**Independent Test**: Export SSP as OSCAL, validate the JSON file against the NIST OSCAL SSP schema, confirm system-implementation.components entries exist

### Implementation for User Story 3

- [x] T019 [US3] Implement GenerateOscalJsonAsync in SspExportService — call OscalSspExportService.ExportAsync(systemId), serialize OscalExportResult.OscalJson to exports/{systemId}/{exportId}.json via System.Text.Json with indented formatting, store any OscalExportResult.Warnings in SspExport.ErrorMessage field (as informational, status still Completed), enforce 50 MB limit (FR-020), compute SHA-256 hash, validate output against NIST OSCAL SSP JSON schema (log validation warnings per SC-004) in src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs

**Checkpoint**: All three export formats work — Word, PDF, OSCAL JSON all selectable from the same ExportSspDialog.

---

## Phase 7: User Story 4 — Custom SSP Templates (Priority: P4)

**Goal**: ISSM/Admin uploads custom .docx templates with organizational branding; users select a template when exporting

**Independent Test**: Upload a custom .docx template, verify it appears in the template list with detected merge fields, export SSP using the custom template, verify output uses the template's styles/headers/footers

### Implementation for User Story 4

- [ ] T020 [P] [US4] Implement UploadTemplateAsync in SspExportService — validate IFormFile is valid .docx (ZipArchive with word/document.xml), enforce 10 MB size limit (FR-020), extract merge fields via regex on {{FieldName}} patterns, validate SystemName merge field present, save file to templates/{templateId}.docx, persist SspTemplate entity in src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs
- [ ] T021 [US4] Implement ListTemplatesAsync, DeleteTemplateAsync, and RenameTemplateAsync in SspExportService — list active templates ordered by name with pagination (limit/offset), soft-delete sets IsActive=false (reject if deleting the only default template), implement SetDefaultTemplateAsync to toggle IsDefault (ensure only one default at a time), implement RenameTemplateAsync to update Name/Description with unique-name validation in src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs
- [ ] T022 [US4] Add POST /templates endpoint — accept multipart/form-data (file, name, description, isDefault), RBAC check (ISSM/Administrator), call UploadTemplateAsync, return 201 with template ID, detected merge fields, and upload timestamp per contracts/api.md in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [ ] T023 [P] [US4] Add GET /templates endpoint — call ListTemplatesAsync with pagination (limit/offset), return items array with id, name, description, fileSize, isDefault, mergeFields, uploadedBy, uploadedAt and total count per contracts/api.md in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T024 [US4] Add DELETE /templates/{templateId} endpoint — RBAC check (ISSM/Administrator), call DeleteTemplateAsync, return 204 on success per contracts/api.md in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T024a [US4] Add PUT /templates/{templateId} endpoint — accept JSON body with optional name and description fields, RBAC check (ISSM/Administrator), call RenameTemplateAsync, validate unique name among active templates, return 200 with updated template per contracts/api.md in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T025 [US4] Create TemplateManagementDialog component — modal with template list table (name, description, size, default badge, uploaded date), upload form (file picker, name input, description textarea, isDefault toggle), set-default action, rename action (inline edit), delete with confirmation dialog in src/Ato.Copilot.Dashboard/src/components/TemplateManagementDialog.tsx
- [x] T026 [US4] Add template API methods to exports client — listTemplates(limit?, offset?), uploadTemplate(file, name, description?, isDefault?), deleteTemplate(templateId), renameTemplate(templateId, name?, description?) in src/Ato.Copilot.Dashboard/src/api/exports.ts
- [x] T027 [US4] Update ExportSspDialog — when exporting as Word, show template dropdown populated from GET /templates (default template pre-selected); pass selected templateId to requestExport; hide template dropdown for PDF/OSCAL formats in src/Ato.Copilot.Dashboard/src/components/ExportSspDialog.tsx
- [x] T028 [US4] Update GenerateDocxAsync to load custom template — when SspExportJob.TemplateId is not null, read the SspTemplate entity, load the .docx file from templates/{templateId}.docx, pass to DocumentTemplateService.RenderDocxAsync instead of default template; append any missing NIST sections at end per US4 acceptance scenario 5 in src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs

**Checkpoint**: Custom template workflow complete — upload, select, export, verify branding applied.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Retention cleanup, progress reporting, audit completeness, documentation, validation

- [x] T029 [P] Implement SspExportRetentionService : BackgroundService — daily PeriodicTimer, query SspExports where ExpiresAt < UtcNow, delete filesystem files, remove database records, log cleanup count via Serilog per research.md R7 in src/Ato.Copilot.Agents/Compliance/Services/SspExportRetentionService.cs
- [x] T031 [P] Add audit logging for template operations — log template upload (userId, templateName, fileSize, mergeFieldCount), template rename (userId, templateId, oldName, newName), and template deletion (userId, templateId, templateName) via Serilog per FR-021 in src/Ato.Copilot.Agents/Compliance/Services/SspExportService.cs
- [x] T032 [P] Write unit tests for SspExportService — test EnqueueExportAsync creates Pending entity, GenerateDocxAsync produces valid file path and SHA-256 hash, GenerateDocxAsync rejects exports exceeding 50 MB (FR-020), ListExportsAsync returns correct pagination, UploadTemplateAsync rejects invalid .docx and oversized files, DeleteTemplateAsync sets IsActive=false, RenameTemplateAsync updates name with unique-name check, RBAC helper approves/denies correct roles in tests/Ato.Copilot.Tests.Unit/SspExportServiceTests.cs
- [x] T033 [P] Write integration tests for export endpoints — test POST /exports returns 202 and valid exportId, GET /exports returns paginated list with Status!=Failed filter, GET /exports/{id}/download streams file with correct Content-Type, POST /templates accepts valid .docx upload, GET /templates returns paginated template list, PUT /templates/{id} renames template, DELETE /templates/{id} returns 204, RBAC enforcement returns 403 for unauthorized roles, performance assertion: export of test system completes within 60s (SC-001), OSCAL export output validates against NIST OSCAL SSP JSON schema (SC-004) in tests/Ato.Copilot.Tests.Integration/SspExportEndpointTests.cs
- [x] T034 Update user documentation with SSP export guide — how to export, format options, custom templates, retention policy in docs/guides/ssp-export.md
- [x] T035 Run quickstart.md validation — execute all curl commands from quickstart.md against running stack, verify expected responses

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US1: Word Export (Phase 3)**: Depends on Foundational (Phase 2)
- **US5: Documents UI (Phase 4)**: Depends on US1 (Phase 3) — needs POST and download endpoints
- **US2: PDF Export (Phase 5)**: Depends on Foundational (Phase 2) — can run in parallel with US1/US5
- **US3: OSCAL Export (Phase 6)**: Depends on Foundational (Phase 2) — can run in parallel with US1/US5/US2
- **US4: Templates (Phase 7)**: Depends on US1 (Phase 3) — extends GenerateDocxAsync with template loading
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (Word)**: No dependencies on other stories — standalone MVP
- **US5 (Documents UI)**: Depends on US1 for the POST/download API endpoints
- **US2 (PDF)**: Independent of US1/US5 — only needs Foundational phase. Can start after Phase 2.
- **US3 (OSCAL)**: Independent of US1/US5/US2 — only needs Foundational phase. Can start after Phase 2.
- **US4 (Templates)**: Depends on US1 — extends the DOCX export path with custom template support

### Within Each User Story

- Models before services
- Services before endpoints
- Backend before frontend
- Core implementation before polish

### Parallel Opportunities

- All Foundational tasks marked [P] can run in parallel (T002, T003, T004, T005 — four different files)
- US2 (Phase 5) and US3 (Phase 6) can both run in parallel with US1/US5 — they only need Phase 2 complete
- Within US4: T020 and T021 can run in parallel (different service methods); T023 and T025, T026 can run in parallel (different files)
- All Polish tasks marked [P] can run in parallel (T029–T034 — six different files)

---

## Parallel Example: User Story 1 (Word Export)

```
# After Phase 2 is complete, launch US1 implementation:
Task T009: "Implement GenerateDocxAsync in SspExportService"

# Once T009 is done, T010 and T011 can run in parallel (different endpoint methods):
Task T010: "Add POST /systems/{systemId}/exports endpoint"
Task T011: "Add GET /systems/{systemId}/exports/{exportId}/download endpoint"

# T012 can run in parallel with T010/T011 (different file section):
Task T012: "Add structured audit logging for export operations"
```

## Parallel Example: User Stories 2 & 3 (after Phase 2)

```
# US2 and US3 can start simultaneously after Phase 2 — independent format handlers:
Task T018: "Implement GeneratePdfAsync in SspExportService"   (US2)
Task T019: "Implement GenerateOscalJsonAsync in SspExportService"  (US3)
```

## Parallel Example: User Story 4 (Templates)

```
# Service methods in parallel (no dependencies between upload and list/delete):
Task T020: "Implement UploadTemplateAsync"
Task T021: "Implement ListTemplatesAsync and DeleteTemplateAsync"

# Endpoints after service methods, but GET can parallel with POST:
Task T022: "Add POST /templates endpoint"
Task T023: "Add GET /templates endpoint"

# Frontend components in parallel with endpoints:
Task T025: "Create TemplateManagementDialog"
Task T026: "Add template API methods to exports client"
```

---

## Implementation Strategy

### MVP First (US1 + US5 Only)

1. Complete Phase 1: Setup (T001)
2. Complete Phase 2: Foundational (T002–T008)
3. Complete Phase 3: US1 Word Export (T009–T012)
4. Complete Phase 4: US5 Documents UI (T013–T017)
5. **STOP and VALIDATE**: Export Word SSP from Documents page end-to-end
6. Deploy/demo if ready — users can generate and download Word SSPs

### Incremental Delivery

1. Setup + Foundational → Infrastructure ready
2. Add US1 (Word) + US5 (UI) → Test end-to-end → Deploy (MVP!)
3. Add US2 (PDF) → Same UI, new format option → Deploy
4. Add US3 (OSCAL) → Same UI, new format option → Deploy
5. Add US4 (Templates) → Custom branding support → Deploy
6. Polish → Retention, progress, tests, docs → Deploy

### Parallel Team Strategy

With multiple developers after Phase 2:
- Developer A: US1 (Word backend) → US5 (UI)
- Developer B: US2 (PDF) + US3 (OSCAL) — independent format handlers
- Developer C: US4 (Templates) — starts after US1 merge
- All: Polish phase tasks in parallel

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- SspExportService.cs is the most-edited file — avoid parallel edits within the same story phase
- The download endpoint (T011) handles all three formats — Content-Type is determined from SspExport.Format
- ExportSspDialog (T016) supports all formats from the start but PDF/OSCAL options work only after their story phases complete
