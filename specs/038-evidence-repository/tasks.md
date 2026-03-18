# Tasks: Evidence Repository

**Input**: Design documents from `/specs/038-evidence-repository/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/evidence-api.md

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Exact file paths included in all descriptions

---

## Phase 1: Setup

**Purpose**: Project initialization — no user story work yet

- [X] T001 Create `ArtifactCategory` and `CollectionMethod` enums in `src/Ato.Copilot.Core/Models/Compliance/EvidenceArtifactModels.cs`
- [X] T002 Create `EvidenceArtifact` entity with all fields, data annotations, and navigation properties in `src/Ato.Copilot.Core/Models/Compliance/EvidenceArtifactModels.cs`
- [X] T003 Create `EvidenceVersion` entity with all fields, data annotations, and navigation properties in `src/Ato.Copilot.Core/Models/Compliance/EvidenceArtifactModels.cs`
- [X] T004 Add `DbSet<EvidenceArtifact>` and `DbSet<EvidenceVersion>` to `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs`
- [X] T005 Add EF Core fluent configuration (indexes, FK relationships, delete behaviors) for `EvidenceArtifact` and `EvidenceVersion` in `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` `OnModelCreating` per data-model.md
- [X] T006 Create TypeScript evidence types (`EvidenceArtifactDto`, `EvidenceVersionDto`, `EvidenceSummaryDto`, `ControlEvidenceDto`, enums) in `src/Ato.Copilot.Dashboard/src/types/evidence.ts`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T007 Create `IFileStorageProvider` interface (`SaveAsync`, `GetAsync`, `DeleteAsync`, `ExistsAsync`) in `src/Ato.Copilot.Core/Interfaces/Storage/IFileStorageProvider.cs`
- [X] T008 Implement `LocalFileStorageProvider` using Docker-mountable volume path with structured `evidence/{systemId}/{artifactId}/{filename}` convention in `src/Ato.Copilot.Mcp/Services/Storage/LocalFileStorageProvider.cs`
- [X] T009 [P] Add `Azure.Storage.Blobs` NuGet package to `src/Ato.Copilot.Mcp/Ato.Copilot.Mcp.csproj` and implement `AzureBlobStorageProvider` with same path convention in `src/Ato.Copilot.Mcp/Services/Storage/AzureBlobStorageProvider.cs`
- [X] T010 Create `IEvidenceArtifactService` interface with methods: `UploadAsync`, `GetByIdAsync`, `ListForSystemAsync`, `ListForControlAsync`, `GetSummaryAsync`, `DownloadAsync`, `DeleteAsync`, `ReplaceAsync` in `src/Ato.Copilot.Core/Interfaces/Compliance/IEvidenceArtifactService.cs`
- [X] T011 Implement `EvidenceArtifactService` with file validation (extension + content-type allowlist, size limit, zero-byte check), SHA-256 hashing via `EvidenceStorageService.ComputeHash()`, and `IFileStorageProvider` delegation in `src/Ato.Copilot.Mcp/Services/EvidenceArtifactService.cs`
- [X] T012 Register `IFileStorageProvider` (conditionally: Local or AzureBlob based on `appsettings.json` `Evidence:StorageProvider` setting / environment variable) and `IEvidenceArtifactService` in DI container in `src/Ato.Copilot.Mcp/Program.cs`
- [X] T013 [P] Create Axios evidence API service with all endpoint functions (`uploadEvidence`, `getEvidence`, `listEvidence`, `downloadEvidence`, `getEvidenceSummary`, `getControlEvidence`, `deleteEvidence`, `replaceEvidence`, `collectEvidence`) in `src/Ato.Copilot.Dashboard/src/api/evidence.ts`

**Checkpoint**: Foundation ready — entities in DB, file storage working, service layer complete, frontend API client ready

---

## Phase 3: User Story 1 — Upload Evidence to a Control (Priority: P1) 🎯 MVP

**Goal**: Users can upload evidence files to control implementations, view evidence on control narratives, and download files

**Independent Test**: Navigate to any control narrative → click "Attach Evidence" → upload a PNG/PDF → verify it appears in the evidence list → download and verify file fidelity

### Backend — US1

- [X] T014 [US1] Add `POST /systems/{systemId}/evidence` endpoint (multipart/form-data upload with validation, category, description; resolve uploader identity from existing dashboard user-identity pattern — request header or session context) in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs` per contracts/evidence-api.md
- [X] T015 [US1] Add `GET /systems/{systemId}/controls/{controlId}/evidence` endpoint (returns direct + inherited + automated evidence for a control) in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs`
- [X] T016 [US1] Add `GET /systems/{systemId}/evidence/{evidenceId}/download` endpoint (streams file with correct Content-Disposition and Content-Type) in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs`

### Frontend — US1

- [X] T017 [US1] Create `EvidenceUploadDialog.tsx` component with file picker, description field, category dropdown, collection method dropdown, file validation (type, size), and upload progress in `src/Ato.Copilot.Dashboard/src/components/EvidenceUploadDialog.tsx`
- [X] T018 [US1] Create `EvidenceSection.tsx` component showing evidence list (filename, category, uploader, date, download link) for embedding in control narrative views in `src/Ato.Copilot.Dashboard/src/components/EvidenceSection.tsx`
- [X] T019 [US1] Integrate `EvidenceSection.tsx` and "Attach Evidence" button into the Narratives page control detail view in `src/Ato.Copilot.Dashboard/src/pages/Narratives.tsx`

**Checkpoint**: US1 complete — users can upload, view, and download evidence on control narratives

---

## Phase 4: User Story 2 — Evidence Repository Page (Priority: P1) 🎯 MVP

**Goal**: Centralized Evidence page with search, filter, sort, summary bar, inline detail panel, and "Evidence" nav item with badge

**Independent Test**: Navigate to a system → Evidence page → verify all evidence appears in a unified table → search/filter → click a row for detail panel → click Control ID to navigate to narrative

### Backend — US2

- [X] T020 [US2] Add `GET /systems/{systemId}/evidence` endpoint (paginated, unified query of `EvidenceArtifact` + `ComplianceEvidence`, with search, filter by family/category/source/date, sort) in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs`
- [X] T021 [US2] Add `GET /systems/{systemId}/evidence/summary` endpoint (total count, manual vs automated breakdown, coverage percentage) in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs`
- [X] T022 [US2] Add `GET /systems/{systemId}/evidence/{evidenceId}` endpoint (single evidence detail with version history) in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs`

### Frontend — US2

- [X] T023 [US2] Create `EvidenceRepository.tsx` page with summary bar (total, manual/automated breakdown, coverage %), searchable/filterable/sortable evidence table, and pagination in `src/Ato.Copilot.Dashboard/src/pages/EvidenceRepository.tsx`
- [X] T024 [US2] Create `EvidenceDetailPanel.tsx` slide-over component (full metadata, file preview for images/PDFs via `<img>`/`<iframe>`, download button, version history) following `DeviationDetailDrawer.tsx` pattern in `src/Ato.Copilot.Dashboard/src/components/EvidenceDetailPanel.tsx`
- [X] T025 [US2] Add "Evidence" nav item with count badge after "Remediation" in `navItems` array in `src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx`
- [X] T026 [US2] Add `/systems/:id/evidence` route mapping to `EvidenceRepository` page in `src/Ato.Copilot.Dashboard/src/App.tsx`

**Checkpoint**: US2 complete — Evidence Repository page is fully functional with unified view, search/filter, detail panel, and nav integration

---

## Phase 5: User Story 3 — Capability-Level Evidence (Priority: P2)

**Goal**: Users can attach evidence to security capabilities; evidence propagates to all linked controls with "Inherited from [Capability]" label

**Independent Test**: Navigate to Capability Coverage → select a capability → attach evidence → verify it appears on capability detail and all linked control narratives with "Inherited" label

### Backend — US3

- [X] T027 [US3] Update `POST /systems/{systemId}/evidence` endpoint to accept `securityCapabilityId` as an alternative to `controlImplementationId` with mutual exclusivity validation in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs`
- [X] T028 [US3] Update `GET /systems/{systemId}/controls/{controlId}/evidence` endpoint to include capability-inherited evidence (query `CapabilityControlMapping` → `EvidenceArtifact` where `SecurityCapabilityId` matches) with "Inherited from [Capability Name]" label in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs`

### Frontend — US3

- [X] T029 [US3] Add "Attach Evidence" button and `EvidenceSection.tsx` to capability detail view in `src/Ato.Copilot.Dashboard/src/pages/CapabilityCoverage.tsx`
- [X] T030 [US3] Update `EvidenceSection.tsx` to display inherited evidence in a separate "Inherited Evidence" section with provenance label in `src/Ato.Copilot.Dashboard/src/components/EvidenceSection.tsx`

**Checkpoint**: US3 complete — capability-level evidence works and propagates to linked controls

---

## Phase 6: User Story 4 — Metadata and Categorization (Priority: P2)

**Goal**: Full metadata (category, collection method) support in upload dialog and filterable on Evidence Repository page

**Independent Test**: Upload evidence with "Scan Result" category and "Automated Scan" method → verify metadata displays on detail → filter by category on Evidence Repository

> **Note**: The `ArtifactCategory` and `CollectionMethod` enums were created in Phase 1 (T001), and the upload dialog already includes category/method dropdowns from T017. This phase ensures end-to-end filtering and display are complete.

- [X] T031 [US4] Verify and finalize category filter dropdown on Evidence Repository page, ensuring `GET /systems/{systemId}/evidence?category=ScanResult` filters correctly in `src/Ato.Copilot.Dashboard/src/pages/EvidenceRepository.tsx`
- [X] T032 [US4] Update `EvidenceDetailPanel.tsx` to display artifact category chip and collection method in the metadata section in `src/Ato.Copilot.Dashboard/src/components/EvidenceDetailPanel.tsx`

**Checkpoint**: US4 complete — category and collection method are fully integrated in upload, display, and filtering

---

## Phase 7: User Story 5 — Trigger Automated Collection (Priority: P2)

**Goal**: "Collect Evidence" button on control narratives triggers the existing `EvidenceStorageService` and displays results alongside manual evidence

**Independent Test**: Navigate to AC-2 narrative → click "Collect Evidence" → verify loading indicator → verify new automated evidence record appears within 10 seconds

### Backend — US5

- [X] T033 [US5] Add `POST /systems/{systemId}/controls/{controlId}/collect-evidence` endpoint that resolves subscription ID from `RegisteredSystem`, invokes `IEvidenceStorageService.CollectEvidenceAsync()`, and returns the new `ComplianceEvidence` record in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs`

### Frontend — US5

- [X] T034 [US5] Add "Collect Evidence" button with loading spinner to `EvidenceSection.tsx` on control narrative views, calling `collectEvidence()` API and refreshing the evidence list on completion in `src/Ato.Copilot.Dashboard/src/components/EvidenceSection.tsx`

**Checkpoint**: US5 complete — automated evidence collection is accessible from the dashboard

---

## Phase 8: User Story 6 — Delete and Replace Evidence (Priority: P3)

**Goal**: Users can soft-delete evidence (with confirmation) and replace evidence (retaining version history with configurable retention)

**Independent Test**: Delete an evidence item → verify it disappears → replace evidence → verify new file appears and old version shows in history with retention status

### Backend — US6

- [X] T035 [US6] Add `DELETE /systems/{systemId}/evidence/{evidenceId}` endpoint (soft-delete: sets `IsDeleted`, `DeletedBy`, `DeletedAt`) in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs`
- [X] T036 [US6] Add `PUT /systems/{systemId}/evidence/{evidenceId}` endpoint (creates `EvidenceVersion` for old file, computes `PurgeAfter` from retention setting, uploads new file, updates artifact record) in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs`
- [X] T037 [P] [US6] Add `GET /systems/{systemId}/evidence/{evidenceId}/versions` and `GET /systems/{systemId}/evidence/{evidenceId}/versions/{versionId}/download` endpoints for version history and old version download in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs`

### Frontend — US6

- [X] T038 [US6] Add delete button with confirmation dialog to `EvidenceSection.tsx` and `EvidenceDetailPanel.tsx`, calling `deleteEvidence()` API and refreshing list in `src/Ato.Copilot.Dashboard/src/components/EvidenceSection.tsx` and `src/Ato.Copilot.Dashboard/src/components/EvidenceDetailPanel.tsx`
- [X] T039 [US6] Add "Replace" button to `EvidenceDetailPanel.tsx` that opens `EvidenceUploadDialog.tsx` in replace mode, calling `replaceEvidence()` API in `src/Ato.Copilot.Dashboard/src/components/EvidenceDetailPanel.tsx`
- [X] T040 [US6] Add version history section to `EvidenceDetailPanel.tsx` showing previous versions with timestamps, uploaders, retention status (file available / purged), and download links for non-purged versions in `src/Ato.Copilot.Dashboard/src/components/EvidenceDetailPanel.tsx`
- [X] T049 [US6] Implement `EvidenceVersionPurgeService` as a .NET `BackgroundService` that periodically queries `EvidenceVersion` records where `PurgeAfter < UtcNow && !IsFilePurged`, deletes files via `IFileStorageProvider.DeleteAsync()`, and sets `IsFilePurged = true` in `src/Ato.Copilot.Mcp/Services/EvidenceVersionPurgeService.cs`. Register as hosted service in `Program.cs`. Default interval configurable via `Evidence:PurgeIntervalHours`

**Checkpoint**: US6 complete — evidence lifecycle management (delete, replace, version history, auto-purge) is fully functional

---

## Phase 9: Server-Side Configuration

**Purpose**: Server-side `EvidenceOptions` binding and read-only config endpoint

- [X] T041 [P] Add `Evidence` configuration section to `appsettings.json` with keys: `Evidence:StorageProvider` (Local|AzureBlob, default: Local), `Evidence:AzureBlobConnectionString`, `Evidence:AzureBlobContainerName`, `Evidence:RetentionDays` (default: 365), `Evidence:LocalStoragePath` (default: `/data/evidence`), `Evidence:PurgeIntervalHours` (default: 24). Create strongly-typed `EvidenceOptions` class and bind via `services.Configure<EvidenceOptions>()` in `src/Ato.Copilot.Mcp/Configuration/EvidenceOptions.cs` and `src/Ato.Copilot.Mcp/Program.cs`
- [X] T042 [P] Add `GET /evidence/settings` read-only endpoint returning current storage provider, retention days, and local storage path from server config. Optionally display as read-only info in the dashboard settings panel in `src/Ato.Copilot.Dashboard/Endpoints/DashboardEndpoints.cs`

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [X] T043 [P] (`ILogger<T>` — Serilog is the configured sink per constitution) for all evidence operations (upload, download, delete, replace, collect) in `src/Ato.Copilot.Mcp/Services/EvidenceArtifactService.cs`
- [X] T044 [P] Add unit tests for `EvidenceArtifactService` (upload validation, hash computation, CRUD, soft-delete, replace with versioning) in `tests/Ato.Copilot.Tests.Unit/Services/EvidenceArtifactServiceTests.cs`
- [X] T045 [P] Add unit tests for `LocalFileStorageProvider` (save, get, delete, exists, path conventions) in `tests/Ato.Copilot.Tests.Unit/Services/LocalFileStorageProviderTests.cs`
- [X] T050 [P] Add unit tests for `AzureBlobStorageProvider` (save, get, delete, exists — mock `BlobServiceClient`/`BlobContainerClient` via Moq) in `tests/Ato.Copilot.Tests.Unit/Services/AzureBlobStorageProviderTests.cs`
- [X] T046 [P] Add integration tests for evidence API endpoints (upload, list, detail, download, delete, replace, summary, collect-trigger) in `tests/Ato.Copilot.Tests.Integration/Evidence/EvidenceEndpointsTests.cs`
- [X] T047 Docker build and deployment verification — build both images, `docker compose up`, run quickstart.md verification steps
- [X] T048 Run `dotnet build Ato.Copilot.sln` with zero warnings and `dotnet test` with all tests passing

---

## Phase 11: Documentation

**Purpose**: Update user-facing and architecture documentation to reflect Evidence Repository feature

- [X] T051 [P] Add "Evidence Repository" section to `docs/guides/compliance-dashboard.md` documenting: Evidence page navigation, upload workflow (Attach Evidence button on narratives), Evidence Repository page (search, filter, sort, summary bar), inline detail panel, capability-inherited evidence labels, and automated vs. manual evidence distinction. Place after the Implementation Roadmap section
- [X] T052 [P] Update `docs/architecture/data-model.md` to add `EvidenceArtifact` and `EvidenceVersion` entities with field tables, `ArtifactCategory` and `CollectionMethod` enums, ER diagram relationships (`RegisteredSystem ||--o{ EvidenceArtifact`, `EvidenceArtifact ||--o{ EvidenceVersion`, FK relationships to `ControlImplementation` and `SecurityCapability`), and the file type allowlist
- [X] T053 [P] Update `docs/architecture/overview.md` to add `EvidenceVersionPurgeService` to the Hosted Services list, `IEvidenceArtifactService` and `IFileStorageProvider` to the Services Layer, and `EvidenceArtifact`/`EvidenceVersion` to the Core entities summary
- [X] T054 [P] Update `docs/getting-started/isso.md` to add evidence upload as a key ISSO workflow — mention navigating to a control narrative to attach evidence, and using the Evidence Repository page to review all evidence for a system
- [X] T055 [P] Update `docs/guides/sca-guide.md` to reference the dashboard Evidence Repository page as a complementary interface for evidence verification alongside the existing `compliance_verify_evidence` and `compliance_check_evidence_completeness` MCP tools
- [X] T056 [P] Update `docs/guides/engineer-guide.md` to add an "Attach Evidence" step after narrative authoring in the SSP Authoring Workflow section, explaining how engineers can upload configuration exports, scan results, and screenshots to support their narratives

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3 (US1 — P1)**: Depends on Phase 2 — MVP core
- **Phase 4 (US2 — P1)**: Depends on Phase 2; can partially parallel with Phase 3 (backend endpoints are independent, but `EvidenceSection.tsx` from US1 is reused)
- **Phase 5 (US3 — P2)**: Depends on Phase 3 (`EvidenceSection.tsx`) and Phase 4 (repository page for unified view)
- **Phase 6 (US4 — P2)**: Depends on Phase 3 (upload dialog) and Phase 4 (repository filtering)
- **Phase 7 (US5 — P2)**: Depends on Phase 3 (narrative integration)
- **Phase 8 (US6 — P3)**: Depends on Phase 3 (evidence CRUD) and Phase 4 (detail panel)
- **Phase 9 (Configuration)**: Can run in parallel with any user story after Phase 2; server-side `EvidenceOptions` binding
- **Phase 10 (Polish)**: Depends on all desired user stories being complete
- **Phase 11 (Documentation)**: Depends on Phase 3 (US1) and Phase 4 (US2) for accurate content; can run in parallel with Phases 5–10

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 — No dependencies on other stories
- **US2 (P1)**: Can start after Phase 2 — Reuses `EvidenceSection.tsx` from US1 but page is independent
- **US3 (P2)**: Depends on US1 (reuses `EvidenceSection.tsx` and upload dialog)
- **US4 (P2)**: Depends on US1 (upload) + US2 (filtering) — mostly verification/polish
- **US5 (P2)**: Depends on US1 (narrative integration point)
- **US6 (P3)**: Depends on US1 (CRUD) + US2 (detail panel)

### Parallel Opportunities

Within each phase, tasks marked `[P]` can run in parallel:
- Phase 2: T009, T013 can parallel with T008, T010, T011
- Phase 9: T041, T042 are fully independent
- Phase 10: T043, T044, T045, T050, T046 all target different files
- Phase 11: T051, T052, T053, T054, T055, T056 all target different doc files

---

## Parallel Example: Phase 2 (Foundational)

```text
# Batch 1 — Can all start simultaneously:
T007: Create IFileStorageProvider interface
T010: Create IEvidenceArtifactService interface
T013: [P] Create Axios evidence API service (frontend, fully independent)

# Batch 2 — After T007 completes:
T008: Implement LocalFileStorageProvider
T009: [P] Implement AzureBlobStorageProvider (can parallel with T008)

# Batch 3 — After T007, T010 complete:
T011: Implement EvidenceArtifactService (needs both interfaces)

# Batch 4 — After T011 complete:
T012: Register services in DI
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2)

1. Complete Phase 1: Setup (entities, types) — T001–T006
2. Complete Phase 2: Foundational (storage, services, API client) — T007–T013
3. Complete Phase 3: US1 — Upload Evidence to Controls — T014–T019
4. Complete Phase 4: US2 — Evidence Repository Page — T020–T026
5. **STOP and VALIDATE**: Both P1 stories functional and testable
6. Docker build + deploy

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 → Upload/view/download on control narratives → **MVP deploy**
3. US2 → Evidence Repository page with unified view → **Deploy**
4. US3 → Capability-level evidence → **Deploy**
5. US4 → Metadata filtering polish → **Deploy**
6. US5 → Automated collection trigger → **Deploy**
7. US6 → Delete and replace with versioning → **Deploy**
8. Settings + Polish + Documentation → Final release

### Suggested MVP Scope

**US1 + US2** (Phases 1–4, tasks T001–T026): 26 tasks covering the core evidence upload, view, download, repository page, and navigation. This delivers the full "unified evidence system" concept with both manual and automated evidence visible in one place.

**Full feature** (Phases 1–11, tasks T001–T056): 56 tasks including all 6 user stories, server-side configuration, tests, Docker verification, and documentation updates.
