# Tasks: eMASS Authorization Package Export

**Input**: Design documents from `/specs/041-emass-package/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Not included as separate tasks — run `dotnet test` during Polish phase per quickstart.md. Add test tasks if TDD is desired.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. User stories are ordered by dependency chain, not strictly by priority label — US1 (P1) is the package orchestrator that depends on most P2 stories being built first.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Core models/interfaces/config**: `src/Ato.Copilot.Core/`
- **Service implementations & MCP tools**: `src/Ato.Copilot.Agents/Compliance/`
- **Dashboard API controllers**: `src/Ato.Copilot.Mcp/Controllers/`
- **Dashboard frontend**: `src/Ato.Copilot.Dashboard/src/`
- **Unit tests**: `tests/Ato.Copilot.Tests.Unit/`
- **Integration tests**: `tests/Ato.Copilot.Tests.Integration/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add dependencies, bundle schemas, and extend configuration for package generation

- [x] T001 Add NuGet package references for JsonSchema.Net and DocumentFormat.OpenXml to src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj
- [x] T002 [P] Bundle NIST OSCAL 1.1.2 JSON schema files (oscal_ssp_schema.json, oscal_poam_schema.json, oscal_assessment-results_schema.json, oscal_assessment-plan_schema.json) as embedded resources in src/Ato.Copilot.Core/Resources/oscal-schemas/
- [x] T003 [P] Extend ExportSettings with PackagesPath property (computed as Path.Combine(DataPath, "packages")) in src/Ato.Copilot.Core/Configuration/ExportSettings.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Create all new entities, enumerations, interfaces, DTOs, and the EF Core migration. All user stories depend on these existing.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T004 [P] Create enumeration types (PackageStatus, EvidenceMode, PackageArtifactType, SarStatus, SarSectionType, ValidationSeverity) in src/Ato.Copilot.Core/Models/Compliance/ — one file per enum or grouped logically per data-model.md
- [x] T005 [P] Create AuthorizationPackage entity with all fields, relationships to PackageArtifact[] and PackageValidationResult, and index definitions per data-model.md in src/Ato.Copilot.Core/Models/Compliance/AuthorizationPackage.cs
- [x] T006 [P] Create PackageArtifact entity with PackageArtifactType, FK to AuthorizationPackage, and unique index on (PackageId, ArtifactType) in src/Ato.Copilot.Core/Models/Compliance/PackageArtifact.cs
- [x] T007 [P] Create SecurityAssessmentReport entity with SarStatus lifecycle, FK to RegisteredSystem, optional FK to SecurityAssessmentPlan, and SarSection[] child collection in src/Ato.Copilot.Core/Models/Compliance/SecurityAssessmentReport.cs
- [x] T008 [P] Create PackageValidationResult and ValidationFinding entities with FK chain to AuthorizationPackage, severity enum, and index definitions in src/Ato.Copilot.Core/Models/Compliance/PackageValidationResult.cs
- [x] T009 [P] Create EvidenceManifest model (generatedAt, systemId, totalArtifacts, totalSizeBytes, embeddingMode, artifacts array) for JSON serialization in src/Ato.Copilot.Core/Models/Compliance/EvidenceManifest.cs
- [x] T010 [P] Create service interfaces (IAuthorizationPackageService, ISecurityAssessmentReportService, IOscalSapExportService, IOscalSchemaValidationService, IPackageValidationService) in src/Ato.Copilot.Core/Interfaces/Compliance/
- [x] T011 [P] Create PackageDtos (GeneratePackageRequest, PackageResponse, PackageListResponse, PackageDetailResponse, ReadinessChecklistItem) in src/Ato.Copilot.Core/Dtos/Dashboard/PackageDtos.cs
- [x] T012 [P] Create SarDtos (CreateSarRequest, SarResponse, SarSectionResponse, EditSarSectionRequest, ReviewSarRequest) in src/Ato.Copilot.Core/Dtos/Dashboard/SarDtos.cs
- [x] T013 Register new DbSets (AuthorizationPackages, PackageArtifacts, SecurityAssessmentReports, SarSections, PackageValidationResults, ValidationFindings) in AtoCopilotContext, configure entity relationships and indexes in OnModelCreating, and create EF Core migration AddAuthorizationPackageAndSar
- [x] T014 [P] Create PackageUuidRegistry utility class (SspUuid, SapUuid, AssessmentResultsUuid, PoamUuid, component/party UUID mappings, deterministic UUID v5 generation using namespace 6ba7b810-9dad-11d1-80b4-00c04fd430c8 with seed "{PackageId}:{EntityType}:{EntityId}" per data-model.md) in src/Ato.Copilot.Agents/Compliance/Services/PackageUuidRegistry.cs

**Checkpoint**: Foundation ready — all entities, interfaces, DTOs, and database schema in place. User story implementation can now begin.

---

## Phase 3: User Story 2 — OSCAL Version Consistency (Priority: P1)

**Goal**: Upgrade existing OSCAL Assessment Results and POA&M exports from 1.0.6 to 1.1.2 so all OSCAL artifacts use the same version for eMASS acceptance.

**Independent Test**: Export each OSCAL artifact individually and verify all contain `"oscal-version": "1.1.2"` in their metadata sections.

### Implementation for User Story 2

- [x] T015 [US2] Upgrade BuildOscalAssessmentResults() to OSCAL 1.1.2 — add reviewed-controls with control-selections, update target.type and target.status.state, set oscal-version to 1.1.2, add import-ap reference — in src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs
- [x] T016 [US2] Upgrade BuildOscalPoam() to OSCAL 1.1.2 — rename related-observations to related-findings, add import-ssp reference, set oscal-version to 1.1.2 — in src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs

**Checkpoint**: All existing OSCAL exports (SSP already 1.1.2, plus now AR and POA&M) produce consistent OSCAL 1.1.2 output.

---

## Phase 4: User Story 3 — Security Assessment Report Generation (Priority: P2)

**Goal**: Create a SAR entity with four-state lifecycle, auto-populate findings from assessment data, enable narrative editing, and export as a Word document.

**Independent Test**: Complete a system assessment (findings exist), generate a SAR, verify it contains all required NIST SP 800-37 sections, edit narrative sections, advance through lifecycle to Approved, and export as Word document.

### Implementation for User Story 3

- [x] T017 [US3] Implement SecurityAssessmentReportService — create SAR from assessment data (auto-populate FindingsSummary/FindingDetails from ControlEffectivenessRecord/ComplianceFinding), manage SarSection CRUD, enforce SarStatus lifecycle transitions (NotStarted→Draft→UnderReview→Approved with revision path) — in src/Ato.Copilot.Agents/Compliance/Services/SecurityAssessmentReportService.cs
- [x] T018 [US3] Add SAR Word document export method to SecurityAssessmentReportService — generate .docx with title page, table of contents, executive summary, assessment scope, findings tables (by severity/family), individual finding details, and recommendations using DocumentFormat.OpenXml — in src/Ato.Copilot.Agents/Compliance/Services/SecurityAssessmentReportService.cs
- [x] T019 [P] [US3] Implement SarTools MCP tools (compliance_generate_sar, compliance_edit_sar_section, compliance_review_sar) following BaseTool envelope pattern in src/Ato.Copilot.Agents/Compliance/Tools/SarTools.cs
- [x] T020 [P] [US3] Add SAR dashboard API endpoints (POST /sar, GET /sar/{sarId}, PUT /sar/{sarId}/sections/{sectionType}, POST /sar/{sarId}/submit, POST /sar/{sarId}/review, GET /sar/{sarId}/export) with RBAC (SCA/ISSM/AO) to src/Ato.Copilot.Mcp/Controllers/PackageController.cs
- [x] T021 [P] [US3] Create SarEditor React component for narrative section editing and lifecycle status display in src/Ato.Copilot.Dashboard/src/components/SarEditor.tsx

**Checkpoint**: SAR can be generated, edited, reviewed, approved, and exported as Word — fully independent of package generation.

---

## Phase 5: User Story 4 — OSCAL SAP Export (Priority: P2)

**Goal**: Convert existing Security Assessment Plan (Feature 018 entities) to OSCAL 1.1.2 assessment-plan JSON format.

**Independent Test**: Create a SAP (controls, team, schedule, methodology), export as OSCAL JSON, verify structure conforms to OSCAL assessment-plan model with reviewed-controls, assessment-subjects, assessment-activities, tasks, and responsible-parties.

### Implementation for User Story 4

- [x] T022 [US4] Implement OscalSapExportService — map SecurityAssessmentPlan + SapControlEntry + SapTeamMember entities to OSCAL 1.1.2 assessment-plan JSON per R5 mapping table (metadata, import-ssp, reviewed-controls, assessment-subjects, assessment-activities, tasks, responsible-parties). Validate mapping against the bundled OSCAL 1.1.2 assessment-plan schema during development to ensure property completeness. — in src/Ato.Copilot.Agents/Compliance/Services/OscalSapExportService.cs
- [x] T023 [US4] Add model="assessment-plan" support to existing compliance_export_oscal MCP tool, delegating to OscalSapExportService, in the existing OSCAL export tool file under src/Ato.Copilot.Agents/Compliance/Tools/

**Checkpoint**: OSCAL SAP export produces valid assessment-plan JSON from existing SAP data, available via MCP tool.

---

## Phase 6: User Story 8 — OSCAL JSON Schema Validation (Priority: P2)

**Goal**: Validate all OSCAL artifacts against official NIST OSCAL 1.1.2 JSON schemas using bundled schema files, reporting specific property-path violations.

**Independent Test**: Generate each OSCAL artifact type, run schema validation, verify that valid artifacts pass and intentionally malformed artifacts report specific violations with JSON property paths.

### Implementation for User Story 8

- [x] T024 [US8] Implement OscalSchemaValidationService — load bundled schemas from embedded resources at startup using SchemaRegistry for $ref resolution, validate JSON documents against Draft 2020-12 schemas via JsonSchema.Net, return detailed errors with JSON Pointer paths — in src/Ato.Copilot.Agents/Compliance/Services/OscalSchemaValidationService.cs
- [x] T025 [P] [US8] Implement compliance_validate_oscal_schema MCP tool (parameters: system_id, model) following BaseTool envelope pattern in src/Ato.Copilot.Agents/Compliance/Tools/PackageTools.cs
- [x] T026 [P] [US8] Add POST /exports/validate-oscal dashboard endpoint (accept model type and optional content, return schema validation results) to src/Ato.Copilot.Mcp/Controllers/PackageController.cs

**Checkpoint**: Any OSCAL artifact can be validated against the official NIST schema, both via MCP tool and dashboard API.

---

## Phase 7: User Story 7 — Standalone OSCAL POA&M and Assessment Results Exports (Priority: P2)

**Goal**: Expose existing (now 1.1.2) OSCAL POA&M and Assessment Results as direct dashboard downloads, alongside the new OSCAL SAP export.

**Independent Test**: Navigate to Documents page, click each standalone export button, download valid OSCAL 1.1.2 JSON files.

### Implementation for User Story 7

- [x] T027 [US7] Add standalone OSCAL export dashboard endpoints — GET /exports/oscal-poam, GET /exports/oscal-assessment-results, GET /exports/oscal-sap — as synchronous JSON downloads with RBAC (ISSM/AO per FR-029), delegating to EmassExportService and OscalSapExportService in src/Ato.Copilot.Mcp/Controllers/PackageController.cs
- [x] T028 [P] [US7] Add OSCAL POA&M, Assessment Results, and SAP export buttons to the Documents page in src/Ato.Copilot.Dashboard/src/pages/Documents.tsx

**Checkpoint**: All four OSCAL artifact types (SSP already exists, plus POA&M, AR, SAP) are individually downloadable from the dashboard.

---

## Phase 8: User Story 6 — Evidence Repository Integration (Priority: P2)

**Goal**: Integrate the evidence repository (Feature 038) into the package pipeline — generate an evidence manifest mapping artifacts to controls, and bundle or link evidence files.

**Independent Test**: Upload evidence artifacts linked to controls, generate a package, verify the archive contains evidence-manifest.json with correct mappings and the evidence files in an evidence/ directory.

### Implementation for User Story 6

- [x] T029 [US6] Implement evidence manifest generation — query IEvidenceArtifactService.ListForSystemAsync() for in-scope controls, build EvidenceManifest JSON per R6 schema (artifactId, fileName, controlId, category, collectionMethod, contentHash, path), exclude expired artifacts and out-of-scope controls — in src/Ato.Copilot.Agents/Compliance/Services/AuthorizationPackageService.cs
- [x] T030 [US6] Implement evidence file bundling — copy files from IFileStorageProvider into evidence/{controlId}/ directory within ZIP when embedded mode selected, fall back to manifest-only with download URLs when total evidence exceeds 100 MB threshold — in src/Ato.Copilot.Agents/Compliance/Services/AuthorizationPackageService.cs

**Checkpoint**: Evidence manifest generation and file bundling logic ready for integration into the package assembly pipeline.

---

## Phase 9: User Story 5 — Pre-Submission Package Validation (Priority: P2)

**Goal**: Validate authorization package completeness and internal consistency before generation, surfacing errors and warnings with remediation guidance.

**Independent Test**: Run validation against a system with known issues (missing SAR, incomplete SSP section, orphaned POA&M references), verify all issues are surfaced with severity and remediation steps.

### Implementation for User Story 5

- [x] T031 [US5] Implement PackageValidationService — check artifact presence (all 6 required), authorization boundary definition (FR-020a — block if missing), OSCAL version consistency across artifacts, cross-artifact control ID matching (SSP↔POA&M↔AR), SSP section completeness (all Approved), SAR status (Approved), POA&M-to-finding reference integrity, evidence coverage (flag missing as warnings), and integrate OscalSchemaValidationService for each OSCAL artifact — return PackageValidationResult with ValidationFinding[] — in src/Ato.Copilot.Agents/Compliance/Services/PackageValidationService.cs
- [x] T032 [P] [US5] Implement compliance_validate_package MCP tool (parameter: system_id, returns readiness checklist and cross-reference checks) in src/Ato.Copilot.Agents/Compliance/Tools/PackageTools.cs
- [x] T033 [P] [US5] Add POST /packages/validate dashboard endpoint (returns readiness checklist with errors/warnings per contracts/api-endpoints.md) to src/Ato.Copilot.Mcp/Controllers/PackageController.cs

**Checkpoint**: A comprehensive readiness check can be run independently before committing to package generation.

---

## Phase 10: User Story 1 — Generate Complete Authorization Package (Priority: P1) 🎯 MVP Integration

**Goal**: Orchestrate generation of all six artifacts (OSCAL SSP, OSCAL POA&M, OSCAL AR, OSCAL SAP, SAR Word, evidence manifest + files) into a single ZIP archive with background processing, validation, and real-time progress.

**Independent Test**: Select a system with completed SSP, assessment data, active POA&M items, finalized SAP, and approved SAR. Click "Generate Authorization Package." Receive a downloadable ZIP containing all six artifacts, all passing OSCAL schema validation, with consistent cross-references.

### Implementation for User Story 1

- [x] T034 [US1] Implement AuthorizationPackageService.EnqueuePackageAsync() — run readiness checks via PackageValidationService, create AuthorizationPackage entity in Pending status, write PackageExportJob to bounded Channel(capacity: 20), return package ID — in src/Ato.Copilot.Agents/Compliance/Services/AuthorizationPackageService.cs
- [x] T035 [US1] Implement PackageBackgroundService — consume from Channel, generate PackageUuidRegistry, invoke each artifact builder in sequence (SSP→POA&M→AR→SAP→SAR→Evidence), assemble into atomic ZIP via ZipArchive on FileStream, run schema validation, persist ZIP to ExportSettings.PackagesPath, update PackageArtifact records, handle atomic failure (delete partial file on error, populate FailureReason and FailedArtifactType with structured remediation guidance per FR-004a), enforce 15-minute hard timeout per FR-036a, send SignalR notifications per artifact — in src/Ato.Copilot.Agents/Compliance/Services/PackageBackgroundService.cs
- [x] T036 [US1] Register all DI services — AuthorizationPackageService, PackageBackgroundService (as hosted service), SecurityAssessmentReportService, OscalSapExportService, OscalSchemaValidationService, PackageValidationService, Channel\<PackageExportJob\> (bounded, 20), IPackageExportNotifier — in the service registration / Startup configuration
- [x] T037 [US1] Add SignalR PackageHub (/hubs/package) with server-to-client events (PackageStatusChanged, PackageArtifactGenerated, PackageValidationComplete, PackageComplete, PackageFailed) and implement IPackageExportNotifier to dispatch hub messages — in src/Ato.Copilot.Mcp/
- [x] T038 [P] [US1] Implement PackageTools MCP tools (compliance_generate_package, compliance_package_status) following BaseTool envelope pattern in src/Ato.Copilot.Agents/Compliance/Tools/PackageTools.cs
- [x] T039 [P] [US1] Add package generation dashboard API endpoints (POST /packages with readiness gating, GET /packages/{packageId} with artifact details, GET /packages/{packageId}/download as binary stream) with RBAC (ISSM/AO) to src/Ato.Copilot.Mcp/Controllers/PackageController.cs
- [x] T040 [P] [US1] Create package.ts API client (generatePackage, getPackageStatus, getPackageDetail, downloadPackage, validatePackage, createSar, exportSar methods) in src/Ato.Copilot.Dashboard/src/api/package.ts
- [x] T041 [US1] Create PackageGenerationDialog React component — evidence mode selector, readiness checklist display, progress tracking via SignalR (establish SignalR connection to /hubs/package and subscribe to PackageStatusChanged/PackageArtifactGenerated/PackageComplete/PackageFailed events), download link on completion, error state display (show which artifact failed, error detail, and remediation steps per FR-004a) — in src/Ato.Copilot.Dashboard/src/components/PackageGenerationDialog.tsx
- [x] T042 [US1] Extend Documents.tsx page with package generation button, PackageGenerationDialog integration, package history list, SAR management section, and error/failure state rendering (which artifact failed, remediation guidance) in src/Ato.Copilot.Dashboard/src/pages/Documents.tsx

**Checkpoint**: End-to-end package generation works — from readiness check through background generation, real-time progress, validation, to ZIP download containing all six artifacts.

---

## Phase 11: User Story 9 — Package History & Re-download (Priority: P3)

**Goal**: Provide a history of generated packages with metadata, re-download capability, and retention expiration.

**Independent Test**: Generate multiple packages over time, view history sorted by date, download a specific prior package, verify expired packages show metadata but block download.

### Implementation for User Story 9

- [x] T043 [US9] Implement package history queries in AuthorizationPackageService — list by system with pagination (limit/offset), filter by status (include/exclude failed), sort by GeneratedAt descending, enforce retention expiration (ExpiresAt), return audit metadata (generating user, artifact count, validation status, file size) — in src/Ato.Copilot.Agents/Compliance/Services/AuthorizationPackageService.cs
- [x] T043a [US9] Implement expired package cleanup — add a method to AuthorizationPackageService that deletes ZIP files past ExpiresAt from ExportSettings.PackagesPath and updates the entity (clear FilePath, set status to Expired or retain metadata). Invoke from PackageBackgroundService on a periodic timer (e.g., daily) or as a startup check — in src/Ato.Copilot.Agents/Compliance/Services/AuthorizationPackageService.cs
- [x] T044 [P] [US9] Implement compliance_list_packages MCP tool (parameters: system_id, limit, include_failed) in src/Ato.Copilot.Agents/Compliance/Tools/PackageTools.cs
- [x] T045 [P] [US9] Add GET /packages list endpoint with pagination and filtering (limit, offset, includeFailed query params) to src/Ato.Copilot.Mcp/Controllers/PackageController.cs

**Checkpoint**: Full audit trail of generated packages is accessible and downloadable within retention period.

---

## Phase 12: User Story 10 — Performance Validation (Priority: P2)

**Goal**: Validate and enforce performance requirements for package generation — wall-clock time, memory usage, concurrent job handling, and schema validation speed.

**Independent Test**: Generate a package for a Moderate baseline system and verify it completes in <2 minutes. Run 3 concurrent generation jobs and verify all complete without timeout or memory pressure.

### Implementation for User Story 10

- [x] T046 [US10] Implement streaming ZIP assembly — refactor PackageBackgroundService to write each artifact directly to a ZipArchive entry stream (no full in-memory buffer), stream evidence files from IFileStorageProvider one at a time into evidence/ directory entries — in src/Ato.Copilot.Agents/Compliance/Services/PackageBackgroundService.cs
- [x] T047 [US10] Implement parallel artifact generation — invoke OSCAL SSP, POA&M, AR, SAP, and SAR builders concurrently via Task.WhenAll, then stream results sequentially into ZIP entries — in src/Ato.Copilot.Agents/Compliance/Services/PackageBackgroundService.cs
- [x] T048 [P] [US10] Cache compiled OSCAL schemas at startup — load bundled JSON schemas once during OscalSchemaValidationService construction (singleton lifetime), store as Dictionary\<string, JsonSchema\> keyed by artifact type — in src/Ato.Copilot.Agents/Compliance/Services/OscalSchemaValidationService.cs

**Checkpoint**: Package generation meets time targets (<2 min Moderate, <5 min High) with streaming memory profile (<512 MB steady-state).

---

## Phase 13: User Story 11 — Chat-Driven Document and eMASS Operations (Priority: P3)

**Goal**: Enable natural language access to all package and SAR operations via dashboard chat, Teams, and VS Code using existing MCP tools.

**Independent Test**: Ask the dashboard chat "Generate an authorization package for [system name]" and verify the AI calls the correct MCP tool and returns a formatted response with package ID and status link.

### Implementation for User Story 11

- [x] T049 [US11] Add package operation intents to ShowDocumentsTool — handle phrases like "generate authorization package," "package status," "validate package readiness," "export OSCAL POA&M" by dispatching to the corresponding MCP tool (compliance_generate_package, compliance_package_status, compliance_validate_package, compliance_export_oscal) — in src/Ato.Copilot.Agents/Compliance/Tools/PackageTools.cs
- [x] T050 [US11] Add SAR operation intents to ShowDocumentsTool — handle phrases like "generate SAR," "SAR status," "edit SAR section," "submit SAR for review" by dispatching to the corresponding MCP tool (compliance_generate_sar, compliance_edit_sar_section, compliance_review_sar) — in src/Ato.Copilot.Agents/Compliance/Tools/SarTools.cs
- [x] T051 [P] [US11] Format chat responses with structured cards — implement suggestion card helpers for package tools returning status tables (artifact-by-artifact progress), readiness checklists (errors/warnings formatted), and follow-up action buttons (e.g., "Package ready — validate it?" or "SAR in Draft — submit for review?") — in src/Ato.Copilot.Agents/Compliance/Tools/PackageTools.cs

**Checkpoint**: All package and SAR operations are accessible via natural language chat with formatted, actionable responses.

---

## Phase 14: User Story 12 — Documentation Updates (Priority: P2)

**Goal**: Create and update all user-facing documentation for eMASS package workflows, MCP tools, entities, and persona guides.

**Independent Test**: Run `mkdocs serve`, navigate to the eMASS Package guide, verify the page renders correctly with accurate content covering the end-to-end workflow.

### Implementation for User Story 12

- [x] T052 [P] [US12] Create docs/guides/emass-package.md — end-to-end eMASS authorization package guide covering: package generation workflow, readiness checklist, SAR creation and lifecycle, OSCAL exports (SSP, POA&M, AR, SAP), evidence integration, pre-submission validation, schema validation, package history, troubleshooting common eMASS import errors, and a section on OSCAL schema update process (how bundled schemas are sourced from NIST, validated, and updated with application releases per FR-026)
- [x] T053 [P] [US12] Update persona guides — add eMASS package sections to docs/guides/issm-guide.md (generate, validate, download workflow), docs/getting-started/isso.md (SAR review contributions), docs/guides/ao-quick-reference.md (authorization package review, SAR findings), docs/guides/sca-guide.md (SAR generation and lifecycle)
- [x] T054 [P] [US12] Update docs/architecture/agent-tool-catalog.md — add reference entries for all 8 new MCP tools and update compliance_export_oscal entry with assessment-plan model type, including parameter tables, response schemas, RBAC notes, and example invocations
- [x] T055 [P] [US12] Update docs/architecture/data-model.md — document AuthorizationPackage, PackageArtifact, SecurityAssessmentReport, SarSection, PackageValidationResult, ValidationFinding, and EvidenceManifest entities with field definitions and relationships
- [x] T056 [P] [US12] Update RMF phase guides — add eMASS package content to docs/rmf-phases/authorize.md (package generation, eMASS submission), docs/rmf-phases/assess.md (SAR generation from findings), docs/rmf-phases/monitor.md (package re-generation for continuous authorization)
- [x] T057 [US12] Update mkdocs.yml nav — add eMASS Authorization Package entry under Guides section, verify all updated pages are accessible from existing nav locations

**Checkpoint**: All documentation renders correctly in MkDocs with accurate, complete content for all package workflows, tools, and entities.

---

## Phase 15: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, cleanup, and deployment readiness

- [x] T058 [P] Update docs/api/mcp-server.md with new MCP tools (compliance_generate_package, compliance_package_status, compliance_validate_package, compliance_list_packages, compliance_generate_sar, compliance_edit_sar_section, compliance_review_sar, compliance_validate_oscal_schema, updated compliance_export_oscal)
- [x] T059 [P] Update docs/api/vscode-extension.md with package generation and SAR management commands
- [x] T060 Run quickstart.md end-to-end validation — build solution, run migrations, generate SAR, approve SAR through lifecycle, validate package readiness, generate full package, verify ZIP contents, validate all OSCAL artifacts against schemas, test concurrent package generation for 2+ systems simultaneously to verify no conflicts or resource contention
- [x] T061 Code review and cleanup — ensure consistent error handling with standard envelope responses, CancellationToken propagation in all async paths, structured Serilog logging for package generation lifecycle, RBAC enforcement on all endpoints per FR-027/FR-028/FR-029, verify unit test coverage meets 80% threshold per Constitution Principle III (Testing Standards)

---

## Phase 16: Dashboard Integration (FR-045–FR-050)

**Goal**: Surface SAP/SAR generation, viewing, and persistence in the dashboard Assessments and Documents pages so users can generate, review, and navigate eMASS package artifacts without leaving the UI.

### SAP/SAR Generation & View Dialogs

- [x] T062 [US3] Add SAP and SAR generation buttons to the Assessments page that invoke the existing MCP tool endpoints (POST /api/v1/systems/{systemId}/sap, POST /api/v1/systems/{systemId}/sar) with loading state and success/error feedback in src/Ato.Copilot.Dashboard/src/pages/Assessments.tsx
- [x] T063 [US4] Implement SAP detail view modal on Assessments page with: blue explainer banner, generated-at timestamp, assessment methodology summary (first family's methods), family coverage breakdown with control counts, and status-conditional Next Steps with navigation link to Run Assessment in src/Ato.Copilot.Dashboard/src/pages/Assessments.tsx
- [x] T064 [US3] Implement SAR detail view modal on Assessments page with: blue explainer banner, SAR lifecycle status, compliance rate metric card (color-coded: green ≥80%, amber 60-79%, red <60%), findings-by-severity breakdown, descriptive subtitles under metric cards, and status-conditional Next Steps with navigation links to Remediation, POA&M, and Documents pages in src/Ato.Copilot.Dashboard/src/pages/Assessments.tsx

### SAR Persistence (Backend + Frontend)

- [x] T065 [US3] Add `GET /api/v1/systems/{systemId}/sar` REST endpoint in PackageEndpoints.cs that calls `GetSarForSystemAsync(systemId)` (returns latest SAR ordered by CreatedAt desc) in src/Ato.Copilot.Mcp/Endpoints/PackageEndpoints.cs
- [x] T066 [US3] Add `getLatestSar(systemId)` API client function in sar.ts that calls `GET /systems/{systemId}/sar` via v1Client and returns `SarResponse` in src/Ato.Copilot.Dashboard/src/api/sar.ts
- [x] T067 [US3] Update Assessments page mount useEffect to fetch both SAP (getLatestSap) and SAR (getLatestSar) on component mount so previously generated artifacts persist across page navigations in src/Ato.Copilot.Dashboard/src/pages/Assessments.tsx

### Documents Page Integration

- [x] T068 [US3] Add SAR entry to the Documents page Authorization Package section alongside existing SSP, POA&M, Assessment Results, and SAP artifacts in src/Ato.Copilot.Dashboard/src/pages/Documents.tsx

**Checkpoint**: SAP and SAR generation buttons work on Assessments page, view modals display with correct data and guidance, SAR persists across navigation, and Documents page shows SAR in the package section.

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1: Setup ──────────────────┐
                                 ▼
Phase 2: Foundational ───────────┤ BLOCKS all user stories
                                 │
         ┌───────────────────────┼───────────────────────────────┐
         ▼                       ▼                               ▼
Phase 3: US2 (1.1.2)    Phase 4: US3 (SAR)          Phase 5: US4 (SAP)
         │                       │                               │
         ▼                       ▼                               ▼
Phase 6: US8 (Schema)   Phase 7: US7 (Standalone)    ◄──────────┘
         │                       │
         ▼                       │
Phase 8: US6 (Evidence)          │
         │                       │
         ▼                       │
Phase 9: US5 (Validation)        │
         │                       │
         ├───────────────────────┘
         ▼
Phase 10: US1 (Package Orchestration) ──── depends on US2+US3+US4+US5+US6+US8
         │
         ▼
Phase 11: US9 (History)
         │
         ├─────────────────────┐
         ▼                     ▼
Phase 12: US10 (Performance)  Phase 13: US11 (Chat Ops)
         │                     │
         ├─────────────────────┘
         ▼
Phase 14: US12 (Documentation) ──── ships after features are stable
         │
         ▼
Phase 15: Polish
         │
         ▼
Phase 16: Dashboard Integration ──── depends on US3+US4 (SAR/SAP services exist)
```

### User Story Dependencies

| User Story | Depends On | Can Start After |
|------------|-----------|-----------------|
| **US2** — OSCAL Version Consistency | Foundational (Phase 2) | Phase 2 complete |
| **US3** — SAR Generation | Foundational (Phase 2) | Phase 2 complete |
| **US4** — OSCAL SAP Export | Foundational (Phase 2) | Phase 2 complete |
| **US8** — OSCAL Schema Validation | Setup (schemas bundled), Foundational | Phase 2 complete |
| **US7** — Standalone Exports | US2 (upgraded builders), US4 (SAP builder) | Phases 3 + 5 complete |
| **US6** — Evidence Integration | Foundational (Phase 2) | Phase 2 complete |
| **US5** — Package Validation | US8 (schema validation), US2 (version checks) | Phases 3 + 6 complete |
| **US1** — Generate Package | US2, US3, US4, US5, US6, US8 (all artifacts + validation) | Phases 3–9 complete |
| **US9** — Package History | US1 (package entity and generation) | Phase 10 complete |
| **US10** — Performance Validation | US1 (package generation exists to optimize) | Phase 10 complete |
| **US11** — Chat-Driven Operations | US1 + US3 (MCP tools exist to dispatch) | Phase 10 complete |
| **US12** — Documentation Updates | US1–US11 (features must be stable to document) | Phases 10–13 complete |

### Within Each User Story

1. Service implementation before MCP tools and API endpoints
2. MCP tools and API endpoints can be parallel (different files)
3. Frontend components after their corresponding API endpoints
4. Core implementation before integration points

### Parallel Opportunities

**After Phase 2 completes, these can start simultaneously:**
- US2 (OSCAL upgrades in EmassExportService)
- US3 (SAR service, tools, endpoints — entirely new files)
- US4 (OSCAL SAP export — new service)
- US6 (Evidence manifest logic — new methods)
- US8 (Schema validation — new service)

**Within Phase 4 (US3), after T017+T018:**
- T019 (SarTools), T020 (SAR API endpoints), T021 (SarEditor) — all parallel

**Within Phase 10 (US1), after T034+T035+T036+T037:**
- T038 (PackageTools), T039 (Package API), T040 (package.ts) — all parallel

---

## Parallel Example: User Story 3 (SAR)

```bash
# Sequential: Service implementation first
Task T017: "Implement SecurityAssessmentReportService..."
Task T018: "Add SAR Word document export method..."

# Then parallel: MCP tools, API, and frontend (different files)
Task T019: "Implement SarTools MCP tools..."         # SarTools.cs
Task T020: "Add SAR dashboard API endpoints..."       # PackageController.cs
Task T021: "Create SarEditor React component..."      # SarEditor.tsx
```

## Parallel Example: User Story 1 (Package Orchestration)

```bash
# Sequential: Core orchestration
Task T034: "Implement AuthorizationPackageService.EnqueuePackageAsync()..."
Task T035: "Implement PackageBackgroundService..."
Task T036: "Register all DI services..."
Task T037: "Add SignalR PackageHub..."

# Then parallel: Tools, API, client, and frontend (different files)
Task T038: "Implement PackageTools MCP tools..."          # PackageTools.cs
Task T039: "Add package generation dashboard API..."      # PackageController.cs
Task T040: "Create package.ts API client..."              # package.ts

# Sequential: Frontend depends on API client
Task T041: "Create PackageGenerationDialog..."            # uses package.ts
Task T042: "Extend Documents.tsx..."                      # integrates dialog
```

## Parallel Example: Post-MVP Phases

```bash
# After Phase 11 (US9 History) completes, these can start simultaneously:
Worker A → Phase 12: US10 (T046-T048) — Performance optimization
Worker B → Phase 13: US11 (T049-T051) — Chat-driven operations

# Then documentation can begin once features are stable:
Phase 14: US12 (T052-T057) — All documentation tasks are [P] (independent files)
```

---

## Implementation Strategy

### MVP First (US2 + US3 + US4 + US8 → US1)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks everything)
3. Complete Phases 3–6 in parallel: US2 (1.1.2 upgrades), US3 (SAR), US4 (SAP), US8 (Schema validation)
4. Complete Phase 8: US6 (Evidence integration)
5. Complete Phase 9: US5 (Package validation)
6. Complete Phase 10: US1 (Package orchestration) — **this is the MVP delivery**
7. **STOP and VALIDATE**: Generate a full authorization package end-to-end
8. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US2 → All OSCAL exports now consistent 1.1.2 *(deployable increment)*
3. US3 → SAR generation available standalone *(deployable increment)*
4. US4 + US7 → All OSCAL types exportable from dashboard *(deployable increment)*
5. US8 → Schema validation available *(deployable increment)*
6. US5 + US6 → Validation and evidence ready *(deployable increment)*
7. US1 → Full package generation *(MVP — deployable increment)*
8. US9 → Package history *(deployable increment)*
9. US10 → Performance validated and optimized *(deployable increment)*
10. US11 → Chat-driven operations across all surfaces *(deployable increment)*
11. US12 → Full documentation suite *(deployable increment)*
12. Polish → Production-ready *(final increment)*

### Suggested MVP Scope

**Minimum**: Phases 1–10 (Setup through US1). This delivers the core value — a complete eMASS authorization package in a single action.

**Extended**: Add Phases 11–13 (US9 History + US10 Performance + US11 Chat) for full operational capability.

**Complete**: All 15 phases including documentation and polish for production readiness.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks in same phase
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable at its checkpoint
- Commit after each task or logical group
- Stop at any checkpoint to validate the story independently
- All OSCAL builders MUST use PackageUuidRegistry for cross-artifact consistency when called from the package pipeline
- SAR Word export uses DocumentFormat.OpenXml (consistent with existing SSP Word export pattern)
- Schema validation uses bundled embedded resources — no network calls (air-gapped compatible)
- Background job uses bounded Channel(20) with atomic ZIP assembly (no partial output on failure)
- Performance tasks (US10) refine existing code in PackageBackgroundService and OscalSchemaValidationService — no new files
- Chat tasks (US11) add tool dispatch and formatting to existing PackageTools.cs and SarTools.cs — no new services
- Documentation tasks (US12) are all [P] — each doc file is independent and can be written in parallel
