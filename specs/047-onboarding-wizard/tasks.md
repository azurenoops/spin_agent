# Tasks: Tenant Onboarding Wizard (Feature 047)

**Input**: Design documents from `/specs/047-onboarding-wizard/`
**Prerequisites**: [plan.md](./plan.md) ✅ · [spec.md](./spec.md) ✅ · [research.md](./research.md) ✅ · [data-model.md](./data-model.md) ✅ · [contracts/](./contracts/) ✅ · [quickstart.md](./quickstart.md) ✅

**Tests**: Tests are **REQUIRED** for this feature. Constitution Principle III mandates a unit test for every public service method (positive + negative) and an integration test for every endpoint (happy + at least one error). [quickstart.md §"Automated test coverage to validate"](./quickstart.md) enumerates the additional invariants that MUST have failing tests before implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. Story labels map to user stories from spec.md (US1–US7).

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story this task belongs to (US1–US7); omitted for Setup, Foundational, Cross-Cutting, and Polish phases
- File paths are absolute repo-relative

## Path Conventions

This feature is a web application layered into the existing multi-project .NET solution and the existing React Dashboard. Paths use the existing top-level projects:

- **Backend (C#)**: `src/Ato.Copilot.Core/`, `src/Ato.Copilot.Agents/`, `src/Ato.Copilot.Mcp/`
- **Frontend (TS/React)**: `src/Ato.Copilot.Dashboard/src/`
- **Tests**: `tests/Ato.Copilot.Tests.Unit/Onboarding/`, `tests/Ato.Copilot.Tests.Integration/Onboarding/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization, dependency adds, folder scaffolding, configuration sections.

- [X] T001 Add `PdfPig` (latest 0.1.x, MIT) NuGet to [src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj](src/Ato.Copilot.Agents/Ato.Copilot.Agents.csproj) (research §R3)
- [X] T002 [P] Create backend onboarding folder skeletons: `src/Ato.Copilot.Core/Models/Onboarding/`, `src/Ato.Copilot.Core/Interfaces/Onboarding/`, `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/`, `src/Ato.Copilot.Mcp/Endpoints/Onboarding/`, `src/Ato.Copilot.Mcp/Hubs/Onboarding/`
- [X] T003 [P] Create frontend onboarding folder skeleton: `src/Ato.Copilot.Dashboard/src/features/onboarding/{steps,api,hooks,components}/` and `src/Ato.Copilot.Dashboard/src/features/admin/imported-documents/`
- [X] T004 [P] Create test folder skeletons: `tests/Ato.Copilot.Tests.Unit/Onboarding/`, `tests/Ato.Copilot.Tests.Integration/Onboarding/`, `tests/Ato.Copilot.Tests.Integration/TestData/onboarding/` (sample fixtures land in T138)
- [X] T005 Add `OnboardingOptions` configuration section to [src/Ato.Copilot.Mcp/appsettings.json](src/Ato.Copilot.Mcp/appsettings.json) and [src/Ato.Copilot.Mcp/appsettings.Development.json](src/Ato.Copilot.Mcp/appsettings.Development.json) with default `Limits` (EmassMaxBytes=52428800, SspPdfMaxBytes=26214400, SspPdfBatchMax=25, TemplateMaxBytes=26214400, TemplatesPerType=10, NarrativeSeedMaxBytes=52428800, NarrativeSeedTenantBudgetBytes=5368709120), `Progress.PollingFallbackSeconds=10`, and `LongRunningThresholdSeconds=10` (spec.md §Assumptions — inline vs. background-job cutoff) per research §R11

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain entities, EF Core wiring, migration, authorization policy, background-job runner, SignalR notifier, audit infrastructure, error envelope, wizard navigation endpoints/UI shell. Every user story depends on this phase.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Entity classes (data-model.md §1–§13)

- [X] T006 [P] Create `TenantOnboardingState` entity in `src/Ato.Copilot.Core/Models/Onboarding/TenantOnboardingState.cs` per data-model §1
- [X] T007 [P] Create `OnboardingStepCompletion` entity in `src/Ato.Copilot.Core/Models/Onboarding/OnboardingStepCompletion.cs` per data-model §2
- [X] T008 [P] Create `OrganizationContext` entity + `BranchAffiliation` enum in `src/Ato.Copilot.Core/Models/Onboarding/OrganizationContext.cs` per data-model §3
- [X] T009 [P] Create `Person` entity in `src/Ato.Copilot.Core/Models/Onboarding/Person.cs` per data-model §4 (with `IsLinkedToDirectory`, nullable `EntraObjectId`)
- [X] T010 [P] Create `OrganizationRoleAssignment` entity + `OrganizationRole` enum in `src/Ato.Copilot.Core/Models/Onboarding/OrganizationRoleAssignment.cs` per data-model §5
- [X] T011 [P] Create `EmassImportSession` entity + `EmassImportStatus` enum in `src/Ato.Copilot.Core/Models/Onboarding/EmassImportSession.cs` per data-model §6
- [X] T012 [P] Create `SspPdfImportSession` entity + `SspPdfStatus` / `SspPdfRejectReason` enums in `src/Ato.Copilot.Core/Models/Onboarding/SspPdfImportSession.cs` per data-model §7
- [X] T013 [P] Create `AzureSubscriptionRegistration` entity + `SubscriptionStatus` enum in `src/Ato.Copilot.Core/Models/Onboarding/AzureSubscriptionRegistration.cs` per data-model §8
- [X] T014 [P] Create `OrganizationDocumentTemplate` entity + `TemplateType` / `TemplateValidationStatus` enums in `src/Ato.Copilot.Core/Models/Onboarding/OrganizationDocumentTemplate.cs` per data-model §9
- [X] T015 [P] Create `NarrativeSeedDocument` entity in `src/Ato.Copilot.Core/Models/Onboarding/NarrativeSeedDocument.cs` per data-model §10
- [X] T016 [P] Create `WizardArtifactDependency` entity + `ArtifactSourceKind` / `ArtifactDependentKind` enums in `src/Ato.Copilot.Core/Models/Onboarding/WizardArtifactDependency.cs` per data-model §11
- [X] T017 [P] Create `WizardJobStatus` entity + `WizardJobType` / `WizardJobState` enums in `src/Ato.Copilot.Core/Models/Onboarding/WizardJobStatus.cs` per data-model §12
- [X] T018 [P] Create `WizardAuditEntry` entity + `WizardAuditAction` enum in `src/Ato.Copilot.Core/Models/Onboarding/WizardAuditEntry.cs` per data-model §13

### EF Core wiring + migration

- [X] T019 Add 13 `DbSet<...>` properties to [src/Ato.Copilot.Core/Data/AtoCopilotContext.cs](src/Ato.Copilot.Core/Data/AtoCopilotContext.cs) and configure indexes in `OnModelCreating` — including the **filtered unique index** `(TenantId, TemplateType) WHERE IsDefault = 1` on `OrganizationDocumentTemplates` (data-model §9 invariant), composite index `(TenantId, TemplateType, Status)` on the same, `(TenantId, ArtifactKind)` and `(TenantId, Status)` on `WizardArtifactDependencies`, `(TenantId, JobType, State)` on `WizardJobStatuses`, and `(TenantId, Email)` unique on `Persons`
- [X] T020 Create migration `src/Ato.Copilot.Core/Migrations/Migration_2026_05_07_Onboarding.cs` (and matching `Designer` + snapshot updates) creating all 13 tables + indexes; add seed migration entry to existing migrations registration (data-model §"Migration note")
- [X] T021 [P] Add `EnsureSchemaAdditions` step (if the project uses one) to register the new DbSets so `EnsureCreatedAsync()` works in the SQLite dev path

### Configuration + cross-cutting backend infrastructure

- [X] T022 [P] Create `OnboardingOptions` POCO + `IOptions<OnboardingOptions>` registration in `src/Ato.Copilot.Mcp/Configuration/OnboardingOptions.cs` (binds to the JSON section added in T005)
- [X] T023 [P] Create wizard error-code catalog in `src/Ato.Copilot.Core/Onboarding/WizardErrorCodes.cs` enumerating every code from [contracts/progress-events.md](./contracts/progress-events.md) (~21 codes)
- [X] T024 [P] Create `WizardStorageKeys` helper in `src/Ato.Copilot.Core/Onboarding/WizardStorageKeys.cs` returning `wizard/templates/{tenantId}/{templateId}/{filename}`, `wizard/imports/emass/{tenantId}/{sessionId}/{filename}`, `wizard/imports/ssp-pdf/{tenantId}/{sessionId}/{filename}` (plan.md "Storage container layout")
- [X] T025 [P] Create `OnboardingAdministratorPolicy` + `OnboardingAuthorizationFilter` in `src/Ato.Copilot.Mcp/Authorization/OnboardingAdministratorPolicy.cs`; register the policy in [src/Ato.Copilot.Mcp/Program.cs](src/Ato.Copilot.Mcp/Program.cs) per research §R10 (FR-001/FR-002/FR-009)
- [X] T026 [P] Create `IWizardAuditService` interface + `WizardAuditService` impl in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/Auditing/` writing to both Serilog (`WizardAudit` enricher) and the persisted `WizardAuditEntry` DbSet (research §R12)

### Background job infrastructure

- [X] T027 [P] Create `IWizardJobRunner` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/IWizardJobRunner.cs` with `EnqueueAsync<T>(WizardJobType, T payload, CancellationToken)` returning the persisted `WizardJobStatus` row
- [X] T028 Create Channels-backed `WizardJobRunner` + hosted `WizardJobHostedService` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/Jobs/` per research §R7 (one bounded `Channel<WizardJobEnvelope>`, configurable concurrency, persists state transitions, emits SignalR events through the notifier)
- [X] T029 [P] Create `IWizardProgressNotifier` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/IWizardProgressNotifier.cs` (`PublishAsync(WizardJobStatusEvent, CancellationToken)`)
- [X] T030 Create `SignalRWizardProgressNotifier` in `src/Ato.Copilot.Mcp/Hubs/Onboarding/SignalRWizardProgressNotifier.cs` using existing `NotificationHub` group `wizard-{tenantId}` and method `WizardJobStatus`, mirroring `SignalRSspExportNotifier` (research §R2)
- [X] T031 Add SignalR hub-method `SubscribeToWizardJob(string jobId)` + auth check in [src/Ato.Copilot.Mcp/Hubs/NotificationHub.cs](src/Ato.Copilot.Mcp/Hubs/NotificationHub.cs) joining caller to `wizard-{tenantId}-job-{jobId}` only when caller passes `OnboardingAdministratorPolicy`

### Bootstrap + wizard-state services

- [X] T032 Create `IBootstrapAdministratorService` + `BootstrapAdministratorService` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/BootstrapAdministratorService.cs` granting first-authenticated-user the in-app `Administrator` RMF role under a tenant-level lock (research §R10, FR-001) — emit `WIZARD_BOOTSTRAP_RACE` when the lock is contended
- [X] T033 Create `IOnboardingStateService` + `OnboardingStateService` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/OnboardingStateService.cs` exposing `GetAsync`, `StartAsync` (calls Bootstrap admin grant), `MarkStepSkippedAsync` (FR-006/FR-007), `MarkStepCompletedAsync`, `CompleteOnboardingAsync` (FR-008). `MarkStepCompletedAsync` MUST populate `OnboardingStepCompletion.DurationMs` (data-model §2) and emit a structured `wizard.step_completed` Serilog event with `tenantId`, `actorUserId`, `stepName`, `stepNumber`, and `durationMs` for FR-063 per-step analytics. `MarkStepSkippedAsync` MUST emit the matching `wizard.step_skipped` event.
- [X] T034 Create `IWizardArtifactDependencyService` + impl in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/WizardArtifactDependencyService.cs` with `LinkAsync(sourceArtifactId, dependentArtifactId, ...)`, `FlagDependentsStaleAsync(sourceArtifactId, reason)` (cascade per research §R6), `ListBySourceAsync(sourceArtifactId, page, pageSize)`, `RerunAsync(dependencyId)`

### Foundational HTTP endpoints + polling fallback

- [X] T035 [P] Create `OnboardingStateEndpoints` minimal-API module in `src/Ato.Copilot.Mcp/Endpoints/Onboarding/OnboardingStateEndpoints.cs` exposing `GET /api/onboarding/state`, `POST /api/onboarding/start`, `POST /api/onboarding/steps/{stepName}/skip` (returns envelope per Constitution VII; uses `OnboardingAdministratorPolicy`)
- [X] T036 [P] Create `WizardJobsEndpoints` minimal-API module in `src/Ato.Copilot.Mcp/Endpoints/Onboarding/WizardJobsEndpoints.cs` exposing `GET /api/onboarding/jobs/{jobId}` polling-fallback per FR-066 + [contracts/progress-events.md](./contracts/progress-events.md) "Polling fallback"
- [X] T037 Register both endpoint modules in [src/Ato.Copilot.Mcp/Program.cs](src/Ato.Copilot.Mcp/Program.cs)

### Foundational frontend (wizard shell + progress component)

- [X] T038 [P] Create wizard shell route at `src/Ato.Copilot.Dashboard/src/features/onboarding/OnboardingShell.tsx` (registers route `/onboarding`, redirects to `/dashboard` when state.status === Completed unless `?stepNav=admin`, gates non-admin users to a read-only summary view per FR-002)
- [X] T039 [P] Create `useOnboardingState` hook + `onboardingApi` axios client in `src/Ato.Copilot.Dashboard/src/features/onboarding/api/onboardingApi.ts` with state/start/skip wrappers
- [X] T040 [P] Create `WizardStepNavigator` component in `src/Ato.Copilot.Dashboard/src/features/onboarding/components/WizardStepNavigator.tsx` showing current/completed/remaining steps + deep links (FR-004/FR-005)
- [X] T041 [P] Create `BackgroundJobProgress` component in `src/Ato.Copilot.Dashboard/src/features/onboarding/components/BackgroundJobProgress.tsx` — subscribes to SignalR `WizardJobStatus`, falls back to 2 s polling of `/api/onboarding/jobs/{jobId}` after `PollingFallbackSeconds` of silence ([contracts/progress-events.md](./contracts/progress-events.md) "Polling fallback")
- [X] T042 Add `/onboarding` route registration + admin-menu link "Re-open Onboarding Wizard" in [src/Ato.Copilot.Dashboard/src/App.tsx](src/Ato.Copilot.Dashboard/src/App.tsx) (or the equivalent router root)

### Foundational tests (Constitution III)

- [X] T043 [P] Unit tests for `OnboardingStateService` (Get/Start/Skip/Complete + non-admin denial + verifies `MarkStepCompletedAsync` populates `DurationMs` and emits a `wizard.step_completed` Serilog event with the expected fields per FR-063) in `tests/Ato.Copilot.Tests.Unit/Onboarding/OnboardingStateServiceTests.cs`
- [X] T044 [P] Unit tests for `BootstrapAdministratorService` race conditions (two concurrent first-users → exactly one wins, other gets `WIZARD_BOOTSTRAP_RACE`) in `tests/Ato.Copilot.Tests.Unit/Onboarding/BootstrapAdministratorServiceTests.cs`
- [X] T045 [P] Unit tests for `WizardArtifactDependencyService.FlagDependentsStaleAsync` covering all 4 source kinds (Template / EmassImport / SspPdfImport / NarrativeSeed) — SC-013 in `tests/Ato.Copilot.Tests.Unit/Onboarding/WizardArtifactDependencyServiceTests.cs`
- [X] T046 [P] Unit tests for `SignalRWizardProgressNotifier` — verifies `wizard-{tenantId}` group + `WizardJobStatus` method name in `tests/Ato.Copilot.Tests.Unit/Onboarding/SignalRWizardProgressNotifierTests.cs`
- [X] T047 [P] Integration tests for foundational endpoints (`/api/onboarding/state`, `/start`, `/steps/{stepName}/skip`, `/jobs/{jobId}`) including a 403 path with `OnboardingAdministratorPolicy` denial in `tests/Ato.Copilot.Tests.Integration/Onboarding/FoundationalEndpointsTests.cs`
- [X] T047a Discovery + binding: locate the canonical integration points required by downstream stories — (1) the SSP cover-page renderer that must consume `OrganizationContext` (FR-014; targeted by T054), (2) the system-creation pipeline that must inherit organization-level role assignments (FR-024; targeted by T068), (3) the Azure scope resolver that must consume `AzureSubscriptionRegistration` (FR-072 / FR-076; targeted by T103), and (4) the SSP / SAR / SAP / CRM / H-W-S-W export renderers that must resolve org default templates (FR-085; targeted by T114). Record the resolved file paths (and any new abstractions to introduce) in [plan.md §"Project Structure"](./plan.md) before starting the corresponding wiring tasks. Removes the "or the equivalent" hedge from T054 / T068 / T103 / T114.

**Checkpoint**: Foundation ready — user story implementation can now begin in parallel.

---

## Phase 3: User Story 1 — Establish Organization & Branch Context (Priority: P1) 🎯 MVP

**Goal**: First-time admin captures organization name + branch + optional metadata; downstream features (SSP cover pages, narrative letterheads, document export headers) automatically inherit it without re-entry (FR-010..FR-014).

**Independent Test**: From the spec — "Sign in to a fresh tenant, complete only Step 1 with organization name and branch, then attempt to create a system in any subsequent feature: the SSP draft and any export rendered for that system MUST show the captured organization name on the cover page without further prompting."

### Tests for User Story 1 (write first; ensure failing)

- [X] T048 [P] [US1] Unit tests for `OrganizationContextService` (get/upsert + validation: empty name rejected, missing branch qualifier rejected when branch=`IndustryPartnerOther`) in `tests/Ato.Copilot.Tests.Unit/Onboarding/OrganizationContextServiceTests.cs`
- [X] T049 [P] [US1] Integration test for `GET/PUT /api/onboarding/organization-context` (happy path + 400 missing-qualifier path + 403 non-admin path + audit-row written) in `tests/Ato.Copilot.Tests.Integration/Onboarding/OrganizationContextEndpointsTests.cs`
- [X] T050 [P] [US1] Integration test confirming SSP cover-page renderer reads `OrganizationContext.OrganizationName` (FR-014) in `tests/Ato.Copilot.Tests.Integration/Onboarding/OrganizationContextSspCoverPageTests.cs`

### Implementation for User Story 1

- [X] T051 [P] [US1] Create `IOrganizationContextService` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/IOrganizationContextService.cs`
- [X] T052 [US1] Implement `OrganizationContextService` (get + upsert + branch-qualifier validation) in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/OrganizationContextService.cs` — writes `OrganizationContextSaved` audit entry on every change
- [X] T053 [US1] Create `OrganizationContextEndpoints` minimal-API module in `src/Ato.Copilot.Mcp/Endpoints/Onboarding/OrganizationContextEndpoints.cs` exposing `GET/PUT /api/onboarding/organization-context` per [contracts/onboarding-api.yaml](./contracts/onboarding-api.yaml); register in `Program.cs`
- [X] T054 [US1] Wire SSP export cover-page renderer to read `OrganizationContext` for the active tenant in the file resolved by T047a (item 1) per FR-014 — fall back to a placeholder string only when no `OrganizationContext` exists
- [X] T055 [P] [US1] Create `Step1OrganizationContext.tsx` React component in `src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step1OrganizationContext.tsx` (form with org name + branch dropdown + conditional qualifier + optional fields)
- [X] T056 [US1] Wire `Step1OrganizationContext` into `OnboardingShell` step list and disable Skip per FR-007

**Checkpoint**: User Story 1 fully functional — fresh-tenant admin can capture org context, persist it, and see it on subsequent SSP cover pages. MVP can ship at this checkpoint.

---

## Phase 4: User Story 2 — Assign RMF Roles During Onboarding (Priority: P1)

**Goal**: Admin pre-fills ISSM/ISSO/Administrator/Assessor at tenant level; new systems inherit defaults; per-system overrides allowed without affecting org defaults (FR-020..FR-026, hybrid Person identity per research §R1).

**Independent Test**: From the spec — "Sign in to the wizard, complete Steps 1 and 2 with role assignments for at least ISSM and ISSO, then create a new system in the portfolio: the new system MUST show the captured ISSM and ISSO as default role-holders without re-entry; per-system overrides MUST not affect the organization-level default."

### Tests for User Story 2

- [X] T057 [P] [US2] Unit tests for `PersonService` (create-local, list, search-directory mock, promote — verifies UUID stable across promotion per research §R1) in `tests/Ato.Copilot.Tests.Unit/Onboarding/PersonServiceTests.cs`
- [X] T058 [P] [US2] Unit tests for `OrganizationRoleAssignmentService` (add/remove + last-admin invariant + ISSM-multiple warn) in `tests/Ato.Copilot.Tests.Unit/Onboarding/OrganizationRoleAssignmentServiceTests.cs`
- [X] T059 [P] [US2] Integration tests for `/api/onboarding/persons*` endpoints (create-local, search-directory mock-Graph, promote 200 + 409 already-linked) in `tests/Ato.Copilot.Tests.Integration/Onboarding/PersonEndpointsTests.cs`
- [X] T060 [P] [US2] Integration test for `/api/onboarding/role-assignments` (add, list, delete-with-replacement, delete-last-admin → 409 `WIZARD_LAST_ADMIN_PROTECTED`) in `tests/Ato.Copilot.Tests.Integration/Onboarding/RoleAssignmentEndpointsTests.cs`
- [X] T061 [P] [US2] Integration test confirming a newly-created system inherits org-level role assignments and per-system override does not mutate org default (FR-024/FR-025) in `tests/Ato.Copilot.Tests.Integration/Onboarding/RoleAssignmentInheritanceTests.cs`

### Implementation for User Story 2

- [X] T062 [P] [US2] Create `IPersonService` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/IPersonService.cs` (`ListAsync`, `SearchLocalAsync`, `CreateLocalAsync`, `SearchDirectoryAsync`, `PromoteToDirectoryAsync`)
- [X] T063 [P] [US2] Create `IOrganizationRoleAssignmentService` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/IOrganizationRoleAssignmentService.cs`
- [X] T064 [US2] Implement `PersonService` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/PersonService.cs` — uses existing `Microsoft.Graph` client for `SearchDirectoryAsync`; `PromoteToDirectoryAsync` is one-way (research §R1) and writes `PersonPromoted` audit
- [X] T065 [US2] Implement `OrganizationRoleAssignmentService` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/OrganizationRoleAssignmentService.cs` — enforces last-admin invariant, pre-populates ISSM with current user (FR-021), allows multiple ISSO/Assessor (FR-023)
- [X] T066 [US2] Create `PersonEndpoints` minimal-API module in `src/Ato.Copilot.Mcp/Endpoints/Onboarding/PersonEndpoints.cs` exposing list/create/search-directory/promote per [contracts/onboarding-api.yaml](./contracts/onboarding-api.yaml); register in `Program.cs`
- [X] T067 [US2] Create `RoleAssignmentEndpoints` minimal-API module in `src/Ato.Copilot.Mcp/Endpoints/Onboarding/RoleAssignmentEndpoints.cs` exposing list/create/delete; register in `Program.cs`
- [X] T068 [US2] Hook system-creation pipeline (Feature 042 / 046 system intake) to copy organization-level role assignments to the new system at create-time in the file resolved by T047a (item 2) — FR-024
- [X] T069 [P] [US2] Create `Step2RoleAssignments.tsx` React component in `src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step2RoleAssignments.tsx` with directory-search modal + free-text-create fallback + promote action
- [X] T070 [US2] Wire `Step2RoleAssignments` into `OnboardingShell`

**Checkpoint**: User Stories 1 AND 2 both work independently. With these P1 stories complete, the **minimum tenant onboarding** is shippable.

---

## Phase 5: User Story 3 — Bulk Import Existing Systems from eMASS (Priority: P2)

**Goal**: Admin uploads an eMASS export (XLSX or package ZIP), the wizard parses it as a background job, presents a per-system preview with conflict resolution (Merge/Skip/Overwrite), then commits as a second background job and produces a downloadable log (FR-030..FR-038, SC-007).

**Independent Test**: From the spec — "Upload a sample eMASS export containing 5 systems with mixed states (one new, one matching an existing system, one with malformed data). The wizard MUST surface the parse summary, allow per-system Merge/Skip/Overwrite decisions, complete the import as a background job with SignalR progress, and produce an importable subset reflecting those decisions plus a downloadable log."

### Tests for User Story 3

- [X] T071 [P] [US3] Unit tests for `EmassImportParser` against `sample-emass-5-systems.zip` fixture (5 systems parsed; malformed system flagged; control + POA&M counts correct) in `tests/Ato.Copilot.Tests.Unit/Onboarding/EmassImportParserTests.cs`
- [X] T072 [P] [US3] Unit tests for `EmassImportService` commit logic (Merge/Skip/Overwrite per-system decisions; partial-failure preserves successes — SC-007) in `tests/Ato.Copilot.Tests.Unit/Onboarding/EmassImportServiceTests.cs`
- [X] T073 [P] [US3] Integration tests for `/api/onboarding/imports/emass/*` endpoints (upload 202 + jobId, preview 200, commit 202, log 200, oversized → 413, invalid format → 415) in `tests/Ato.Copilot.Tests.Integration/Onboarding/EmassImportEndpointsTests.cs`
- [X] T074 [P] [US3] Integration test confirming successful commit links `EmassImportSession → System` rows via `WizardArtifactDependency` (foundation for cascade) in `tests/Ato.Copilot.Tests.Integration/Onboarding/EmassImportDependencyTests.cs`

### Implementation for User Story 3

- [X] T075 [P] [US3] Create `IEmassImportParser` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/IEmassImportParser.cs` (research §R4 — reuse Feature 015/041 importers)
- [X] T076 [P] [US3] Create `IEmassImportService` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/IEmassImportService.cs` (`StartParseAsync`, `GetPreviewAsync`, `CommitAsync`, `GetLogAsync`)
- [X] T077 [US3] Implement `EmassImportParser` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/Emass/EmassImportParser.cs` — delegates to Feature 015/041 internals where compatible; flags malformed systems instead of failing the batch (FR-031)
- [X] T078 [US3] Implement `EmassImportService` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/Emass/EmassImportService.cs` — uses `IFileStorageProvider` (key `wizard/imports/emass/{tenantId}/{sessionId}/{filename}`), enqueues `EmassParse` and `EmassCommit` jobs via `IWizardJobRunner`, links dependents via `IWizardArtifactDependencyService`
- [X] T079 [US3] Implement `EmassParseJobHandler` and `EmassCommitJobHandler` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/Emass/Handlers/` — emit progress via `IWizardProgressNotifier` (FR-064/FR-065)
- [X] T080 [US3] Create `EmassImportEndpoints` minimal-API module in `src/Ato.Copilot.Mcp/Endpoints/Onboarding/EmassImportEndpoints.cs` exposing upload, preview, commit, log per [contracts/imports-api.yaml](./contracts/imports-api.yaml); enforce `Limits.EmassMaxBytes` (FR-036); register in `Program.cs`
- [X] T081 [P] [US3] Create `Step3EmassImport.tsx` React component in `src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step3EmassImport.tsx` with file-drop, `BackgroundJobProgress` mount, preview table with per-system decision dropdowns, commit button, log download
- [X] T082 [US3] Wire `Step3EmassImport` into `OnboardingShell` (Skip allowed per FR-006)

**Checkpoint**: Bulk migration path works end-to-end with progress + audit + cascade tracking.

---

## Phase 6: User Story 4 — Ingest System Data from SSP PDF Exports (Priority: P2)

**Goal**: Admin uploads one or more SSP PDFs as a batch; the wizard extracts structured fields (digital PDFs only via PdfPig — research §R3), surfaces per-PDF rejections with specific error codes, lets the admin correct low-confidence fields, and imports the system with PDF-source provenance (FR-040..FR-046, SC-008).

**Independent Test**: From the spec — "Upload a digital and a password-protected SSP PDF as a single batch. The wizard MUST extract the digital PDF's system identification + categorization + boundary + parseable controls, MUST reject the password-protected PDF with `WIZARD_SSP_PDF_PASSWORD_PROTECTED` (no partial system), MUST allow per-field corrections, and MUST import the corrected system with PDF-source audit metadata."

### Tests for User Story 4

- [X] T083 [P] [US4] Unit tests for `SspPdfExtractionService` against fixtures: digital PDF → fields extracted with confidence bands; encrypted → `Encrypted`; password-protected → `PasswordProtected`; image-only → `ImageOnly`; non-NIST framework → `UnknownFramework` in `tests/Ato.Copilot.Tests.Unit/Onboarding/SspPdfExtractionServiceTests.cs`
- [X] T084 [P] [US4] Unit tests for `SspPdfImportService` corrections + commit flow (manual corrections preserved on re-extract per spec edge case "Replacing an SSP PDF after manual field corrections") in `tests/Ato.Copilot.Tests.Unit/Onboarding/SspPdfImportServiceTests.cs`
- [X] T085 [P] [US4] Integration tests for `/api/onboarding/imports/ssp-pdf/*` endpoints (batch upload 202 with per-PDF jobIds, batch summary, extraction, corrections PUT, import 201 with provenance audit, batch-too-large → 422) in `tests/Ato.Copilot.Tests.Integration/Onboarding/SspPdfImportEndpointsTests.cs`

### Implementation for User Story 4

- [X] T086 [P] [US4] Create `ISspPdfExtractionService` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/ISspPdfExtractionService.cs`
- [X] T087 [P] [US4] Create `ISspPdfImportService` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/ISspPdfImportService.cs`
- [X] T088 [US4] Implement `SspPdfExtractionService` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/SspPdf/SspPdfExtractionService.cs` using PdfPig (research §R3) — emits `SspPdfRejectReason` for each rejection category (FR-044/FR-045)
- [X] T089 [US4] Implement `SspPdfImportService` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/SspPdf/SspPdfImportService.cs` — uses `IFileStorageProvider` key `wizard/imports/ssp-pdf/{tenantId}/{sessionId}/{filename}`, enqueues `SspPdfExtract` jobs (one per PDF), supports field corrections (FR-042), creates system with provenance metadata `Source: SSP PDF (filename)` (FR-043)
- [X] T090 [US4] Implement `SspPdfExtractJobHandler` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/SspPdf/Handlers/` emitting per-PDF progress
- [X] T091 [US4] Create `SspPdfImportEndpoints` minimal-API module in `src/Ato.Copilot.Mcp/Endpoints/Onboarding/SspPdfImportEndpoints.cs` exposing upload, batch summary, extraction, corrections, import per [contracts/imports-api.yaml](./contracts/imports-api.yaml); enforce `Limits.SspPdfMaxBytes` and `Limits.SspPdfBatchMax`; register in `Program.cs`
- [X] T092 [P] [US4] Create `Step4SspPdfImport.tsx` React component in `src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step4SspPdfImport.tsx` with batch file-drop, per-PDF status cards, field-correction form with confidence bands, import action
- [X] T093 [US4] Wire `Step4SspPdfImport` into `OnboardingShell` (Skip allowed)

**Checkpoint**: Both bulk-import paths (eMASS + SSP PDF) work independently with rejection categorization.

---

## Phase 7: User Story 5 — Select Azure Subscriptions (Priority: P2)

**Goal**: Admin grants the ARM scope incrementally, the wizard enumerates subscriptions visible to the user's delegated token (no tenant-wide service principal — research §R8/§R9), and persisted selections become the default scope for every Azure-touching feature (FR-070..FR-077, SC-010).

**Independent Test**: From the spec — "Sign in with Entra ID, reach the Azure subscription step, approve the ARM consent prompt, see your visible subscriptions, select two, and complete the wizard. Then trigger an Azure Policy evidence pull from any later feature: the query MUST scope to the two selected subscriptions only, with no further user prompting."

### Tests for User Story 5

- [X] T094 [P] [US5] Unit tests for `AzureSubscriptionEnumerationService` — happy path enumerates via mocked `ArmClient`; missing-consent path surfaces `WIZARD_ARM_CONSENT_REQUIRED`; expired token → `WIZARD_ARM_TOKEN_EXPIRED`; ARM-unreachable → `WIZARD_ARM_UNREACHABLE` in `tests/Ato.Copilot.Tests.Unit/Onboarding/AzureSubscriptionEnumerationServiceTests.cs`
- [X] T095 [P] [US5] Unit tests for `AzureSubscriptionRegistrationService` — replace-set semantics (newly-invisible subs flagged `Unavailable` not removed per FR-074) in `tests/Ato.Copilot.Tests.Unit/Onboarding/AzureSubscriptionRegistrationServiceTests.cs`
- [X] T096 [P] [US5] Integration test for `GET /api/onboarding/azure/subscriptions` returns 403 + `WWW-Authenticate: Bearer error="insufficient_claims"` when ARM consent missing (FR-070a) in `tests/Ato.Copilot.Tests.Integration/Onboarding/AzureSubscriptionEndpointsTests.cs`
- [X] T097 [P] [US5] Integration test confirming downstream Azure-touching feature scopes to selected subscription set (FR-072 / SC-010) in `tests/Ato.Copilot.Tests.Integration/Onboarding/AzureSubscriptionDownstreamScopeTests.cs`

### Implementation for User Story 5

- [X] T098 [P] [US5] Create `IAzureSubscriptionEnumerationService` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/IAzureSubscriptionEnumerationService.cs`
- [X] T099 [P] [US5] Create `IAzureSubscriptionRegistrationService` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/IAzureSubscriptionRegistrationService.cs`
- [X] T100 [US5] Implement `AzureSubscriptionEnumerationService` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/AzureSubscriptions/AzureSubscriptionEnumerationService.cs` using delegated `TokenCredential` from `Microsoft.Identity.Web` (research §R8) — explicitly **no server-side cache** (FR-074 freshness)
- [X] T101 [US5] Implement `AzureSubscriptionRegistrationService` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/AzureSubscriptions/AzureSubscriptionRegistrationService.cs` — preserves `Unavailable` rows on PUT replacement (FR-074)
- [X] T102 [US5] Create `AzureSubscriptionEndpoints` minimal-API module in `src/Ato.Copilot.Mcp/Endpoints/Onboarding/AzureSubscriptionEndpoints.cs` per [contracts/azure-subscriptions-api.yaml](./contracts/azure-subscriptions-api.yaml) — emit `WWW-Authenticate: Bearer error="insufficient_claims", claims="..."` 403 when ARM scope absent (FR-070a / research §R8); register in `Program.cs`
- [X] T103 [US5] Wire downstream Azure-touching services (Azure Policy / Defender / inventory / JIT / assessments) to consume `AzureSubscriptionRegistration`-scoped subscription IDs at query time in the file resolved by T047a (item 3) — FR-072 / FR-076
- [X] T104 [P] [US5] Create `Step5AzureSubscriptions.tsx` React component in `src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step5AzureSubscriptions.tsx` — uses `acquireTokenPopup({ scopes: ['https://management.azure.com/user_impersonation'] })` on first attempt, surfaces "Connect Azure" CTA on `WIZARD_ARM_CONSENT_REQUIRED`, displays subscription picker
- [X] T105 [US5] Wire `Step5AzureSubscriptions` into `OnboardingShell` (Skip allowed per FR-073)

**Checkpoint**: Azure subscription scope is bound; downstream features auto-scope without re-prompting.

---

## Phase 8: User Story 6 — Upload Custom Document Templates (Priority: P2)

**Goal**: Admin uploads org-branded templates for SSP / SAR / SAP / CRM / H/W/S/W List, marks defaults, sees validation warnings, and the export pipeline picks up the org default automatically (FR-080..FR-088, SC-011, SC-013).

**Independent Test**: From the spec — "Upload one custom SSP DOCX, mark it as default for the SSP type, and request an SSP export from any system: the export MUST render using the uploaded template (organization branding visible) without further prompting; if the upload fails validation, the wizard MUST surface a specific warning naming the missing placeholder or column."

### Tests for User Story 6

- [X] T106 [P] [US6] Unit tests for `DocxTemplateValidator` (placeholder presence + size check) and `XlsxTemplateValidator` (required column headers) in `tests/Ato.Copilot.Tests.Unit/Onboarding/OrganizationTemplateValidatorTests.cs`
- [X] T107 [P] [US6] Unit tests for `OrganizationTemplateService` covering: upload + inline validation (< 5 MB) + background validation (> 5 MB), default-uniqueness invariant under concurrent toggles, replace produces cascade summary, delete blocked when `IsDefault=true` (`WIZARD_TEMPLATE_DEFAULT_PROTECTED`), per-type-count limit enforcement in `tests/Ato.Copilot.Tests.Unit/Onboarding/OrganizationTemplateServiceTests.cs`
- [X] T108 [P] [US6] Integration tests for `/api/onboarding/templates*` endpoints (list, upload 201, get, patch, delete 204 + 409, download, replace 200 with cascade summary, default toggle, default clear) in `tests/Ato.Copilot.Tests.Integration/Onboarding/OrganizationTemplateEndpointsTests.cs`
- [X] T109 [P] [US6] Integration test confirming SSP DOCX export pipeline uses the uploaded default template (SC-011) in `tests/Ato.Copilot.Tests.Integration/Onboarding/CustomTemplateRenderingTests.cs`

### Implementation for User Story 6

- [X] T110 [P] [US6] Create `IOrganizationTemplateValidator` interface + per-type implementations (`DocxTemplateValidator` for SSP/SAR/SAP, `XlsxTemplateValidator` for CRM / H/W/S/W) in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/Templates/Validators/` per research §R5
- [X] T111 [P] [US6] Create `IOrganizationTemplateService` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/IOrganizationTemplateService.cs`
- [X] T112 [US6] Implement `OrganizationTemplateService` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/Templates/OrganizationTemplateService.cs` — uses `IFileStorageProvider` (`wizard/templates/{tenantId}/{templateId}/{filename}`); inline validation when ≤ 5 MB, otherwise enqueues `TemplateValidation` job; default-uniqueness via the filtered unique index + a single `using var tx = ctx.Database.BeginTransactionAsync()` on toggle (data-model §9 invariant); replace flags dependents stale via `IWizardArtifactDependencyService`
- [X] T113 [US6] Create `OrganizationTemplateEndpoints` minimal-API module in `src/Ato.Copilot.Mcp/Endpoints/Onboarding/OrganizationTemplateEndpoints.cs` per [contracts/templates-api.yaml](./contracts/templates-api.yaml) — enforce `Limits.TemplateMaxBytes` and `Limits.TemplatesPerType`; register in `Program.cs`
- [X] T114 [US6] Wire export pipeline to resolve org default templates via `OrganizationTemplateService.GetActiveDefaultAsync(tenantId, type)` in the SSP / SAR / SAP / CRM / H-W-S-W export renderers resolved by T047a (item 4) — FR-085 fallback to built-in when no default
- [X] T115 [P] [US6] Create `Step6Templates.tsx` React component in `src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step6Templates.tsx` with five upload slots, per-slot validation warnings panel, "Mark Default" toggle
- [X] T116 [US6] Wire `Step6Templates` into `OnboardingShell` (Skip allowed)

**Checkpoint**: Custom-template flow ships with SC-011 + SC-013 (template-replace cascade) coverage.

---

## Phase 9: User Story 7 — Seed Narratives from Reference Documents (Priority: P3)

**Goal**: Admin uploads policy / SOP / DoDI documents; they live in the Feature 038 evidence repository and are indexed; subsequent narrative authoring offers AI suggestions that cite the seed document (FR-050..FR-055, SC-009, SC-012).

**Independent Test**: From the spec — "Upload an organizational cybersecurity policy PDF tagged 'policy', then begin authoring a NIST control narrative for any system: the system MUST surface a suggestion drawn from the uploaded policy with a citation to its filename, and that suggestion MUST disappear if the source document is deleted."

### Tests for User Story 7

- [X] T117 [P] [US7] Unit tests for `NarrativeSeedDocumentService` (upload routes through `IEvidenceArtifactService`; delete with cited document → 409 unless `confirmCitations=true`; citation marker propagation on delete) in `tests/Ato.Copilot.Tests.Unit/Onboarding/NarrativeSeedDocumentServiceTests.cs`
- [X] T118 [P] [US7] Integration tests for `/api/onboarding/narrative-seeds*` endpoints (list, upload 202 + indexing job, delete 204 + 409 cited) in `tests/Ato.Copilot.Tests.Integration/Onboarding/NarrativeSeedEndpointsTests.cs`

### Implementation for User Story 7

- [X] T119 [P] [US7] Create `INarrativeSeedDocumentService` interface in `src/Ato.Copilot.Core/Interfaces/Onboarding/INarrativeSeedDocumentService.cs`
- [X] T120 [US7] Implement `NarrativeSeedDocumentService` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/NarrativeSeeds/NarrativeSeedDocumentService.cs` — delegates byte storage to existing Feature 038 `IEvidenceArtifactService` (FR-051); persists `NarrativeSeedDocument` metadata; enqueues `NarrativeSeedIndex` job (job stub OK for v1 if downstream indexing is feature-flagged)
- [X] T121 [US7] Implement `NarrativeSeedIndexJobHandler` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/NarrativeSeeds/Handlers/NarrativeSeedIndexJobHandler.cs` — registers the seed document with the existing AI suggestion provider so suggestions surface citations
- [X] T122 [US7] Create `NarrativeSeedEndpoints` minimal-API module in `src/Ato.Copilot.Mcp/Endpoints/Onboarding/NarrativeSeedEndpoints.cs` per [contracts/templates-api.yaml](./contracts/templates-api.yaml); enforce `Limits.NarrativeSeedMaxBytes` and `NarrativeSeedTenantBudgetBytes`; register in `Program.cs`
- [X] T123 [P] [US7] Create `Step7NarrativeSeeds.tsx` React component in `src/Ato.Copilot.Dashboard/src/features/onboarding/steps/Step7NarrativeSeeds.tsx` with file-drop, label/tag form, indexing-progress display
- [X] T124 [US7] Wire `Step7NarrativeSeeds` into `OnboardingShell` (Skip allowed; final step)
- [X] T125 [US7] Wire onboarding completion: when this step (or its skip) is the last to complete, call `OnboardingStateService.CompleteOnboardingAsync` to mark `Status=Completed` and route to `/dashboard` (FR-008)

**Checkpoint**: All seven user stories are independently deliverable.

---

## Phase 10: Cross-Cutting — Imported Documents Management View (FR-090..FR-097, SC-013/SC-014)

**Purpose**: Single management surface that lists every wizard-uploaded artifact (Templates + eMASS + SSP PDF + Narrative Seeds), shows dependents, and exposes admin-initiated re-run for flagged artifacts. Depends on every story's dependency-tagging being in place (T078, T089, T112, T120).

### Tests for Phase 10

- [X] T126 [P] Unit tests for `WizardArtifactInventoryService` paginated listing, filter-by-kind, dependents-count rollup in `tests/Ato.Copilot.Tests.Unit/Onboarding/WizardArtifactInventoryServiceTests.cs`
- [X] T127 [P] Integration test confirming cascade re-run produces fresh dependents for **each** of the four source kinds end-to-end (SC-013) in `tests/Ato.Copilot.Tests.Integration/Onboarding/CascadeRerunAcrossAllKindsTests.cs`
- [X] T128 [P] Integration test asserting `GET /api/onboarding/imports` paginates ≤ 200/page and returns `dependentsCount` per row in `tests/Ato.Copilot.Tests.Integration/Onboarding/ImportedDocumentsManagementViewTests.cs`

### Implementation for Phase 10

- [X] T129 [P] Create `IWizardArtifactInventoryService` interface + impl in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/WizardArtifactInventoryService.cs` — UNION queries across `OrganizationDocumentTemplates`, `EmassImportSessions`, `SspPdfImportSessions`, `NarrativeSeedDocuments` joined to `WizardArtifactDependency` aggregate counts
- [X] T130 Create `ImportedDocumentsEndpoints` minimal-API module in `src/Ato.Copilot.Mcp/Endpoints/Onboarding/ImportedDocumentsEndpoints.cs` exposing `GET /api/onboarding/imports`, `GET /api/onboarding/imports/{id}/dependencies`, `POST /api/onboarding/dependencies/{id}/rerun` per [contracts/onboarding-api.yaml](./contracts/onboarding-api.yaml); register in `Program.cs`
- [X] T131 Implement `ExportRerenderJobHandler` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/Cascade/ExportRerenderJobHandler.cs` — re-renders SSP/SAR/SAP/CRM/H-S-S exports for dependents flagged stale by template replace
- [X] T132 Implement `ImportRerenderJobHandler` in `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/Cascade/ImportRerenderJobHandler.cs` — re-runs eMASS / SSP-PDF imports preserving manual edits per spec edge cases ("Replacing an eMASS export after user edits", "Replacing an SSP PDF after manual field corrections")
- [X] T133 [P] Create `ImportedDocumentsView.tsx` React component in `src/Ato.Copilot.Dashboard/src/features/admin/imported-documents/ImportedDocumentsView.tsx` — paginated table (filter chips: Template / eMASS / SSP PDF / Narrative Seed), per-row Replace / Delete / Re-run buttons + dependents drawer
- [X] T134 Register `/admin/imported-documents` route + admin-menu link "Imported Documents" in [src/Ato.Copilot.Dashboard/src/App.tsx](src/Ato.Copilot.Dashboard/src/App.tsx) (or equivalent router)

**Checkpoint**: SC-013 (cascade across all four kinds) and SC-014 (replace any artifact in < 2 minutes) are demonstrable.

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Final hardening, sample fixtures, persona test cases, documentation alignment, performance + observability spot checks.

- [X] T135 [P] Add `OnboardingTelemetry` enricher in `src/Ato.Copilot.Mcp/Logging/OnboardingTelemetry.cs` so every wizard endpoint logs `tenantId`, `userId`, `wizardStep`, `jobId`, `wizardErrorCode` per Constitution VI / research §R12
- [X] T136 [P] Verify every wizard error path returns `ProblemEnvelope` with `errorCode` + `message` + `suggestion` (Constitution VII) — add a `tests/Ato.Copilot.Tests.Integration/Onboarding/ErrorEnvelopeContractTests.cs` parametric test that calls each documented error code from [contracts/progress-events.md](./contracts/progress-events.md) and asserts shape
- [X] T137 [P] Verify all 13 new entities carry a `TenantId` filter at the EF Core query-level (tenant isolation; supports Constitution IV Azure Government & Compliance First by guaranteeing per-tenant data residency) — add `tests/Ato.Copilot.Tests.Integration/Onboarding/TenantIsolationTests.cs` cross-tenant query-tampering test
- [X] T138 [P] Add quickstart fixtures referenced by tests + manual walkthrough: `tests/Ato.Copilot.Tests.Integration/TestData/onboarding/sample-emass-5-systems.zip`, `sample-ssp.pdf`, `sample-ssp-encrypted.pdf`, `template-ssp.docx`, `template-sar.docx`, `template-sap.docx`, `template-crm.xlsx`, `template-hwsw.xlsx`, `policy-acme-cybersecurity.pdf` (referenced in [quickstart.md](./quickstart.md))
- [X] T139 [P] Add persona test case scripts under `docs/persona-test-cases/047-onboarding-wizard.md` covering bootstrap-admin-grant, fresh-tenant happy path, re-run after template replace, ARM-consent-declined, eMASS partial failure, SSP-PDF rejection categories
- [X] T140 [P] Run [quickstart.md](./quickstart.md) end-to-end against a clean dev environment; capture screenshots into `docs/screenshots/047-onboarding-wizard/` and link them from the spec
- [X] T141 Performance spot-check vs Success Criteria: SC-001 (Step 1 + 2 < 90 s), SC-002 (eMASS 1k-control commit < 5 min), SC-003 (Azure subscription enumeration < 30 s), SC-004 (custom template upload + validation feedback < 10 s); record measured values in `docs/persona-test-cases/047-onboarding-wizard.md`
- [X] T142 Confirm the `/admin/imported-documents` view loads ≤ 200 artifacts with `dependentsCount` populated in < 2 s on the seeded dev DB (SC-014)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1 — **BLOCKS all user stories**.
- **Phase 3 (US1, P1)**: After Phase 2.
- **Phase 4 (US2, P1)**: After Phase 2 (independent of US1; can run parallel to US1 with different developers).
- **Phase 5–9 (US3–US7)**: After Phase 2; each pair is independently testable. Recommended order P2 → P3 by priority, but a multi-developer team can run them in parallel.
- **Phase 10 (Cross-Cutting Management View)**: Depends on **at least one** of US3 / US4 / US6 / US7 having shipped its dependency-tagging tasks (T078, T089, T112, T120 respectively); **fully complete only after all four have shipped** (SC-013).
- **Phase 11 (Polish)**: After all desired user stories + Phase 10 are complete.

### Within Each Story

- All `[P]`-marked test tasks for a story can run in parallel.
- Tests MUST be written and observed failing before the corresponding implementation task is started (Constitution III).
- Interfaces (`I*` in `Ato.Copilot.Core/Interfaces/Onboarding/`) before service implementations.
- Service implementations before endpoint modules.
- Backend before the React step component (the component consumes the API).
- Step component wiring into `OnboardingShell` is the **last** task of each story (sequential dependency on the prior step components only when changing the same `OnboardingShell.tsx` file).

### Critical Sequential Edges (across stories)

- **T019** modifies `AtoCopilotContext` → blocks every entity-using task.
- **T028** (job runner) → blocks every job handler (T079, T090, T121, T131, T132).
- **T030** (SignalR notifier) → blocks every progress-emitting handler.
- **T034** (`WizardArtifactDependencyService`) → blocks T078, T089, T112, T120.
- **OnboardingShell wiring** (T056, T070, T082, T093, T105, T116, T124) all touch the same router file — sequence them in priority order.

### Parallel Opportunities

- **All entity tasks T006–T018** are 13 different files → all `[P]`, run together.
- **All test tasks within a single story** are typically `[P]`.
- **All seven user-story phases** can be assigned to different developers once Phase 2 is done.
- **Polish tasks T135–T140** are `[P]`.

---

## Parallel Example: Phase 2 Foundation

```bash
# Launch all 13 entity tasks together once T005 is in:
Task: T006 — Create TenantOnboardingState entity
Task: T007 — Create OnboardingStepCompletion entity
Task: T008 — Create OrganizationContext entity
Task: T009 — Create Person entity
Task: T010 — Create OrganizationRoleAssignment entity
Task: T011 — Create EmassImportSession entity
Task: T012 — Create SspPdfImportSession entity
Task: T013 — Create AzureSubscriptionRegistration entity
Task: T014 — Create OrganizationDocumentTemplate entity
Task: T015 — Create NarrativeSeedDocument entity
Task: T016 — Create WizardArtifactDependency entity
Task: T017 — Create WizardJobStatus entity
Task: T018 — Create WizardAuditEntry entity

# Then T019 (sequential) → T020 → T021.
```

## Parallel Example: User Story 6 Tests

```bash
# Launch all US6 test tasks together (different files):
Task: T106 — Validator unit tests
Task: T107 — OrganizationTemplateService unit tests
Task: T108 — Template endpoints integration tests
Task: T109 — SSP rendering integration test
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 — both P1)

1. Complete Phase 1 (Setup) — T001..T005.
2. Complete Phase 2 (Foundational) — T006..T047.
3. Complete Phase 3 (US1) — T048..T056.
4. Complete Phase 4 (US2) — T057..T070.
5. **STOP and VALIDATE**: Test US1 + US2 independently per [quickstart.md §4–§5](./quickstart.md). A new tenant can sign in, capture organization context, assign roles, and watch new systems inherit them. **MVP ships at this checkpoint.**

### Incremental Delivery

1. Setup + Foundational → Foundation ready.
2. US1 + US2 → MVP demo (organization-aware system creation).
3. US3 → eMASS bulk-import demo (SC-007).
4. US4 → SSP PDF ingestion demo (SC-008).
5. US5 → Azure subscription scope demo (SC-010).
6. US6 → Branded export demo (SC-011) + cascade demo (SC-013 partial).
7. US7 → Narrative seed demo (SC-009).
8. Phase 10 → Imported Documents management view (SC-013 full + SC-014).
9. Phase 11 → Polish + persona test cases.

### Parallel Team Strategy

With three developers after Phase 2 ships:

- **Developer A**: US1 + US2 (P1; ship MVP early).
- **Developer B**: US3 + US5 (eMASS import + Azure subscriptions; both rely on background jobs + delegated tokens — natural co-ownership).
- **Developer C**: US4 + US6 + US7 (SSP PDF + templates + seeds; all touch storage routing + Feature 038 surfaces — natural co-ownership).

Then converge for Phase 10 (cross-cutting management view) + Phase 11 (polish).

---

## Notes

- `[P]` tasks = different files, no dependencies — safe to parallelize.
- `[Story]` label maps task to user story for traceability (US1–US7).
- Tests are **required** for every public service method (positive + negative) and every endpoint (happy + at least one error). The integration tests under each story phase satisfy this; story-specific edge cases (SC-007, SC-008, SC-010, SC-011, SC-013) have dedicated tests.
- Verify tests fail before implementing (Constitution III).
- Commit after each task or logical group; do not batch multiple stories into one commit.
- Stop at any "Checkpoint" line to validate the current increment independently.
- Avoid: vague tasks, same-file conflicts (mind the `OnboardingShell` sequencing edge), cross-story dependencies that break independence.
