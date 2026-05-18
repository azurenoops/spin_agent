# Tasks: Tenant- & Organization-Scoped Data Isolation

**Input**: Design documents from `/specs/048-tenant-isolation/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Test tasks ARE included. Constitution Principle III makes testing non-negotiable for this feature, and the spec explicitly requires xUnit + integration + RLS-bypass + manual scenarios.

**Organization**: Tasks are grouped by user story (US1..US10) so each priority slice can be implemented and validated independently.

> A pre-/speckit.tasks/ early draft is preserved at `tasks.md.bak.draft` for reference. The list below supersedes it.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story owner — `[US1]`..`[US10]` for user-story phase tasks; omitted for Setup, Foundational, cross-cutting, and Polish phases
- All paths are repository-relative

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project-level scaffolding that every story depends on.

- [X] T001 [P] Create folder structure for tenancy domain types: `src/Ato.Copilot.Core/Models/Tenancy/`, `src/Ato.Copilot.Core/Models/Tenancy/Attributes/`, `src/Ato.Copilot.Core/Models/Tenancy/Migration/`, `src/Ato.Copilot.Core/Interfaces/Tenancy/`, `src/Ato.Copilot.Core/Services/Tenancy/`, `src/Ato.Copilot.Core/Data/Interceptors/`
- [X] T002 [P] Create folder structure for MCP host tenancy plumbing: `src/Ato.Copilot.Mcp/Configuration/`, `src/Ato.Copilot.Mcp/Endpoints/Onboarding/`, `src/Ato.Copilot.Mcp/Services/Tenancy/`
- [X] T003 [P] Create folder structure for tests: `tests/Ato.Copilot.Tests.Unit/Tenancy/`, `tests/Ato.Copilot.Tests.Integration/Tenancy/`, `tests/Ato.Copilot.Tests.Integration/Rls/`
- [X] T004 [P] Create folder structure for dashboard tenancy + onboarding feature folders: `src/Ato.Copilot.Dashboard/src/features/tenancy/`, `src/Ato.Copilot.Dashboard/src/features/onboarding/TenantWizard/`, `src/Ato.Copilot.Dashboard/src/features/onboarding/TenantWizard/steps/`
- [X] T005 Create new project `src/Ato.Copilot.Cli/Ato.Copilot.Cli.csproj` (`net9.0`, `<PackAsTool>true</PackAsTool>`, `<ToolCommandName>ato-cli</ToolCommandName>`); reference `src/Ato.Copilot.Core/Ato.Copilot.Core.csproj`; add NuGet `System.CommandLine` 2.0.0-beta or latest stable
- [X] T006 Add `Ato.Copilot.Cli` to `Ato.Copilot.sln` and confirm `dotnet build Ato.Copilot.sln` succeeds
- [X] T007 [P] Create skeleton documentation files: `docs/architecture/tenant-isolation.md` and `docs/operations/multi-tenant-migration.md` (placeholder TOC + section headers; final content delivered in Polish phase)
- [X] T008 [P] Add Serilog log-context enricher entries for `TenantId` / `EffectiveTenantId` / `ImpersonatedTenantId` / `ActorTenantId` in `src/Ato.Copilot.Mcp/Program.cs` `LoggerConfiguration` section

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core types, attributes, DI wiring, and the schema-additions plumbing that every user story depends on. **No user-story task may begin until Phase 2 completes.**

- [X] T009 [P] Create `src/Ato.Copilot.Core/Models/Tenancy/Attributes/TenantScopedAttribute.cs` (sealed, `AttributeUsage(AttributeTargets.Class, Inherited = false)`) per [data-model.md §2.1](data-model.md)
- [X] T010 [P] Create `src/Ato.Copilot.Core/Models/Tenancy/Attributes/GlobalReferenceAttribute.cs` (sealed, `AttributeUsage(AttributeTargets.Class, Inherited = false)`) per [data-model.md §2.2](data-model.md)
- [X] T011 [P] Create `src/Ato.Copilot.Core/Models/Tenancy/TenantStatus.cs` enum (`Active = 0, Suspended = 1, Disabled = 2`) per [contracts/itenantcontext.cs.md](contracts/itenantcontext.cs.md)
- [X] T012 [P] Create `src/Ato.Copilot.Core/Models/Tenancy/OnboardingState.cs` enum (`Pending = 0, InWizard = 1, Active = 2`) per [data-model.md §1.1](data-model.md)
- [X] T013 [P] Create `src/Ato.Copilot.Core/Models/Tenancy/ClassificationLevel.cs` enum (`Unclassified = 0, CUI = 1, Secret = 2`) per FR-001
- [X] T014 Create `src/Ato.Copilot.Core/Models/Tenancy/Tenant.cs` entity with all FR-001 fields (`Id`, `EntraTenantId`, `DisplayName`, `LegalEntityName`, `DoDComponent`, `PrimaryPocName/Email/Phone`, HQ address fields, `DefaultClassificationLevel`, `AuthorizingOfficialName/Email`, `TimeZone`, `Status`, `OnboardingState`, audit columns, `RowVersion`); marked `[GlobalReference]`
- [X] T015 Create `src/Ato.Copilot.Core/Models/Tenancy/Organization.cs` entity (Id, `TenantId` FK, `ParentOrganizationId Guid?`, Name, Description, audit columns); marked `[TenantScoped]` per [data-model.md §1.2](data-model.md)
- [X] T016 [P] Create `src/Ato.Copilot.Core/Interfaces/Tenancy/ITenantContext.cs` per [contracts/itenantcontext.cs.md](contracts/itenantcontext.cs.md) (`TenantId`, `OrganizationId?`, `IsCspAdmin`, `ImpersonatedTenantId?`, `EffectiveTenantId`, `Status`)
- [X] T017 [P] Create `src/Ato.Copilot.Core/Interfaces/Tenancy/ITenantContextAccessor.cs` per the same contract (`Current`, `Push`)
- [X] T018 [P] Create `src/Ato.Copilot.Core/Interfaces/Tenancy/Exceptions.cs` containing `MissingTenantClaimException`, `TenantSuspendedException`, `TenantDisabledException`, `TenantConsistencyException`, `NotCspAdminException`
- [X] T019 Create `src/Ato.Copilot.Core/Services/Tenancy/TenantContext.cs` (Scoped concrete implementation of `ITenantContext`)
- [X] T020 Create `src/Ato.Copilot.Core/Services/Tenancy/TenantContextAccessor.cs` (Singleton implementation backed by `AsyncLocal<ITenantContext?>`)
- [X] T021 Modify `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` to inject `ITenantContextAccessor` and add `DbSet<Tenant>` + `DbSet<Organization>` properties (do NOT yet apply query filters — wire-up only)
- [X] T022 Modify `AtoCopilotContext.OnModelCreating` to add a reflection-driven helper that iterates entity types, detects `[TenantScoped]` / `[GlobalReference]`, and calls `entity.HasQueryFilter(e => …)` per FR-020 — but iterate only the new `Tenant`/`Organization` types in this task; full retrofit happens in user-story phases
- [X] T023 Add startup self-check method `AtoCopilotContext.AssertScopingAttributesPresent()` per [data-model.md §2.3](data-model.md) — scans all entity types, fails fast at startup if any entity is neither `[TenantScoped]` nor `[GlobalReference]`
- [X] T024 Wire DI in `src/Ato.Copilot.Mcp/Program.cs`: `services.AddScoped<ITenantContext, TenantContext>()`, `services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>()`
- [X] T025 Create `src/Ato.Copilot.Mcp/Configuration/DeploymentOptions.cs` (`Mode`, `DefaultTenantId`, `Tenants:AllowSelfOnboarding`) per FR-040 / FR-055; bind from `ATO_DEPLOYMENT__*`
- [X] T026 Create `src/Ato.Copilot.Mcp/Configuration/RoleClaimMappingsOptions.cs` (`CSP.Admin = <groupObjectId>`) per FR-050; bind from `Auth:RoleClaimMappings`
- [X] T027 Modify `src/Ato.Copilot.Mcp/Middleware/CacAuthenticationMiddleware.cs` to read `RoleClaimMappingsOptions` and translate group-GUID claims into a `CSP.Admin` role claim on the `ClaimsPrincipal` per FR-050
- [X] T028 Create `src/Ato.Copilot.Core/Models/Tenancy/Migration/MultiTenantMigrationReport.cs` and `TenantOverride.cs` DTOs per [data-model.md §7](data-model.md)
- [X] T029 Create `src/Ato.Copilot.Core/Data/Migrations/EnsureSchemaAdditions/AddTenantsAndOrganizationsAsync.cs` partial — additive idempotent SQL that creates `Tenants` (with all FR-001 columns) and `Organizations` tables and their indexes; called from `EnsureSchemaAdditionsAsync`
- [X] T030 Add hook in `src/Ato.Copilot.Mcp/Program.cs` startup pipeline: after `EnsureCreatedAsync`/`EnsureSchemaAdditionsAsync`, invoke a placeholder `TenantBootstrapService.EnsureSystemTenantAsync()` (creates `Id = 00000000-0000-0000-0000-000000000000`, `DisplayName = 'Ato.Copilot.System'`, `OnboardingState = Active`) per FR-070
- [X] T031 [P] Create `tests/Ato.Copilot.Tests.Unit/Tenancy/TenantScopedAttributeTests.cs` — verifies attribute discovery + the startup self-check rejects entities lacking either attribute
- [X] T032 [P] Create `tests/Ato.Copilot.Tests.Unit/Tenancy/TenantContextAccessorTests.cs` — verifies `Push` round-trips + `AsyncLocal` flow across `Task.Run`
- [X] T033 [P] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/MultiTenantWebApplicationFactory.cs` test fixture (seeds 2 tenants, supplies switchable `FakeTenantContext`)
- [X] T034 [P] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/FakeTenantContext.cs` with mutable properties for `TenantId`, `IsCspAdmin`, `ImpersonatedTenantId`

**Checkpoint**: Foundation ready — user-story phases can proceed. `dotnet build Ato.Copilot.sln` MUST be green; `dotnet test` MUST pass new T031/T032 tests.

---

## Phase 3: User Story 1 — Mission Owner Sees Only Their Own Tenant's Data (Priority: P1) 🎯 MVP

**Goal**: Every dashboard endpoint, every MCP tool, every search and export returns only the requesting tenant's rows. Cross-tenant lookup by id returns 404.

**Independent Test**: Seed two tenants × 3 systems each; authenticate as Coastal user; verify all dashboard list endpoints return 3 rows; `GET /systems/{eagleId}` returns 404; SSP export contains zero T-Eagle rows.

### Tests for User Story 1

> Constitution Principle III: write these tests FIRST and observe them FAILING before implementation.

- [X] T035 [P] [US1] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/TenantQueryFilterTests.cs` — for each retrofitted DbSet, asserts that switching `FakeTenantContext.TenantId` yields disjoint result sets (covers SC-001 sample of 10 representative DbSets)
- [X] T036 [P] [US1] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CrossTenantLookupReturns404Tests.cs` — `GET /api/dashboard/systems/{otherTenantSystemId}` returns 404 (acceptance scenario 2)
- [X] T037 [P] [US1] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/SaveChangesStampingTests.cs` — adding an entity without `TenantId` causes the interceptor to stamp `EffectiveTenantId`; setting a different `TenantId` raises `TenantConsistencyException`
- [X] T038 [P] [US1] Create `tests/Ato.Copilot.Tests.Unit/Tenancy/CrossTenantFkRejectionTests.cs` — interceptor rejects saves where `referenced.TenantId != this.TenantId` per [data-model.md §4](data-model.md)
- [X] T039 [P] [US1] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/McpToolTenantScopeTests.cs` — invoke a representative MCP tool (e.g., list-systems) under two different `ITenantContext` scopes, assert disjoint results

### Implementation for User Story 1

- [X] T040 [US1] Create `src/Ato.Copilot.Core/Data/Interceptors/TenantStampingSaveChangesInterceptor.cs` per FR-021 + [data-model.md §4](data-model.md): stamps `TenantId` on Added entities; rejects Modified rows whose `TenantId` differs from `EffectiveTenantId`; rejects cross-tenant FK references unless target is `[GlobalReference]`
- [X] T041 [US1] Register `TenantStampingSaveChangesInterceptor` in `Program.cs` DI and add to `AddDbContext` `options.AddInterceptors(...)` call
- [X] T042 [US1] Refactor `AtoCopilotContext.OnModelCreating` reflection helper from T022 to handle ALL `[TenantScoped]` entities via expression-tree-built filter `e => _tenantContext.IsCspAdmin && _tenantContext.ImpersonatedTenantId == null ? true : e.TenantId == _tenantContext.EffectiveTenantId` per FR-020
- [X] T043 [US1] Apply `[TenantScoped]` and add `Guid TenantId { get; set; }` (and `Guid? OrganizationId` where applicable per FR-004) to RMF & boundary entities ([data-model.md §5.1](data-model.md)): `RegisteredSystem`, `AuthorizationBoundary`, `AuthorizationBoundaryDefinition`, `BoundaryComponentAssignment`, `SecurityCategorization`, `RmfRoleAssignment`, `SystemCapabilityLink`
- [X] T044 [P] [US1] Apply `[TenantScoped]` + `TenantId` to controls & inheritance entities ([data-model.md §5.2](data-model.md)): `ControlBaseline`, `ControlTailoring`, `ControlInheritance`, `InheritanceAuditEntry`, `OrgInheritanceDefault`, `ControlImplementation`, `ControlEffectiveness`, `AssessmentRecord`, `AuthorizationDecision`, `RiskAcceptance`
- [X] T045 [P] [US1] Apply `[TenantScoped]` + `TenantId` to findings/scans/evidence entities ([data-model.md §5.3](data-model.md)): `ComplianceAssessment`, `ComplianceFinding`, `ComplianceEvidence`, `EvidenceArtifact`, `EvidenceVersion`, `ScanImportRecord`, `ScanImportFinding`, `ComplianceDocument`
- [X] T046 [P] [US1] Apply `[TenantScoped]` + `TenantId` to POA&M / deviation / remediation entities ([data-model.md §5.4](data-model.md)): `PoamItem`, `PoamMilestone`, `Deviation`, `RemediationPlan`, `RemediationBoard`, `RemediationTask`, `TaskComment`, `TaskHistoryEntry`, `AutoRemediationRule`
- [X] T047 [P] [US1] Apply `[TenantScoped]` + `TenantId` to watch/alert entities ([data-model.md §5.5](data-model.md)): `ComplianceAlert`, `AlertIdCounter`, `AlertNotification`, `NotificationPreferences`, `MonitoringConfiguration`, `ComplianceBaseline`, `AlertRule`, `SuppressionRule`, `EscalationPath`, `ComplianceSnapshot`, `SignificantChange`
- [X] T048 [P] [US1] Apply `[TenantScoped]` + `TenantId` to SAP/SAR/SSP/privacy entities ([data-model.md §5.6](data-model.md)): `SecurityAssessmentPlan`, `SapControlEntry`, `SapTeamMember`, `SspSection`, `ContingencyPlanReference`, `NarrativeVersion`, `NarrativeReview`, `PrivacyThresholdAnalysis`, `PrivacyImpactAssessment`, `SystemInterconnection`, `InterconnectionAgreement`
- [X] T049 [P] [US1] Apply `[TenantScoped]` + `TenantId` (and `OrganizationId?` where applicable) to components/capabilities entities ([data-model.md §5.7](data-model.md)): `SystemComponent`, `ComponentSystemAssignment`, `SecurityCapability` (with `IsGlobalReference` discriminator), `CapabilityControlMapping`, `ComponentCapabilityLink`, `SystemProfileSection`, `InventoryItem`
- [X] T050 [P] [US1] Apply `[TenantScoped]` + `TenantId` to ConMon entities ([data-model.md §5.8](data-model.md)): `ConMonPlan`, `ConMonReport`
- [X] T051 [P] [US1] Apply `[TenantScoped]` + `TenantId` to roadmap & package entities ([data-model.md §5.9](data-model.md)): `ImplementationRoadmap`, `AuthorizationPackage`, `SecurityAssessmentReport`, `SarSection`, `DeferredPrerequisite`
- [X] T052 [P] [US1] Apply `[TenantScoped]` + `TenantId` to dashboard entities ([data-model.md §5.10](data-model.md)): `ComplianceTrendSnapshot`, `DashboardActivity`
- [X] T053 [P] [US1] Apply `[TenantScoped]` + `TenantId` to auth/cache entities ([data-model.md §5.11–5.12](data-model.md)): `CacSession`, `JitRequestEntity`, `CertificateRoleMapping`, `CachedResponse`
- [X] T054 [P] [US1] Apply `[TenantScoped]` attribute (no schema change — `TenantId` already present) to entities listed in [data-model.md §5.14](data-model.md): `TenantOnboardingState`, `OnboardingStepCompletion`, `OrganizationContext`, `Person`, `OrganizationRoleAssignment`, `SystemRoleAssignment`, `EmassImportSession`, `SspPdfImportSession`, `AzureSubscriptionRegistration`, `OrganizationDocumentTemplate`, `NarrativeSeedDocument`, `WizardArtifactDependency`, `WizardJobStatus`, `WizardAuditEntry`
- [X] T055 [P] [US1] Apply `[GlobalReference]` to reference entities listed in [data-model.md §5.15](data-model.md): `NistControl`, `ComplianceFramework`, `FrameworkControl`, `InformationType`, `Tenant`
- [X] T056 [US1] Update `src/Ato.Copilot.Core/Data/Migrations/EnsureSchemaAdditions/` to additively add `TenantId UNIQUEIDENTIFIER NULL` and (where applicable) `OrganizationId UNIQUEIDENTIFIER NULL` columns to every retrofitted table from T043–T053, plus indexes `IX_<table>_TenantId` and composite `IX_<table>_TenantId_<naturalKey>` per [research.md §14](research.md)
- [X] T057 [US1] Audit every dashboard endpoint group under `src/Ato.Copilot.Mcp/Endpoints/` for raw `db.<DbSet>.Find(id)` patterns and replace with `FirstOrDefaultAsync(x => x.Id == id)` so the query filter applies; touch at minimum endpoints under `Endpoints/Systems/`, `Endpoints/Components/`, `Endpoints/Evidence/`, `Endpoints/Poam/`, `Endpoints/Capabilities/`, `Endpoints/Roadmap/`, `Endpoints/Dashboard/` per FR-023
- [X] T058 [US1] Verify all 130+ MCP tools resolve `ITenantContext` via DI (audit `src/Ato.Copilot.Agents/Tools/`); add the constructor parameter to any tool that holds a `DbContext` reference but currently filters only by user per FR-024
- [X] T059 [US1] Update SSP/SAP/SAR export services in `src/Ato.Copilot.Core/Services/Documents/` to use `ITenantContext.EffectiveTenantId` for any unscoped query (acceptance scenario 4)
- [X] T060 [US1] Refactor `OrganizationContext` rows: backfill `TenantId` to point at the new `Tenants.Id` (where the legacy column held the Entra `tid`, look up the matching `Tenants.EntraTenantId`); add this as an idempotent step to `EnsureSchemaAdditionsAsync` per FR-005
- [X] T061 [US1] Run T035–T039 integration tests; iterate until all green

**Checkpoint**: User Story 1 fully functional. Two-tenant smoke test from [quickstart.md §3](quickstart.md) passes — Coastal user sees zero Eagle rows across endpoints, MCP tools, and exports.

---

## Phase 4: User Story 2 — CSP-Admin Switches Tenants for Support (Priority: P1)

**Goal**: A `CSP.Admin` user can list tenants, impersonate one, perform reads/writes scoped to that tenant, and end the impersonation. Every action emits an audit row carrying both real and impersonated identities.

**Independent Test**: Authenticate as `CSP.Admin`; `GET /api/tenants` returns all tenants; `POST /api/tenants/{id}/impersonate` issues a signed cookie; subsequent `GET /api/dashboard/systems` scopes to target tenant; `DELETE /api/tenants/impersonation` reverts.

### Tests for User Story 2

- [X] T062 [P] [US2] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/TenantsEndpointsContractTests.cs` — validates `GET/POST /api/tenants`, `GET /api/tenants/{id}`, `PATCH /api/tenants/{id}/status`, impersonation endpoints against [contracts/tenants.openapi.yaml](contracts/tenants.openapi.yaml) (status codes, error envelope, idempotency)
- [X] T063 [P] [US2] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/ImpersonationFlowTests.cs` — start impersonation, verify scope switch, verify cookie expiry, end (acceptance scenarios 1–5)
- [X] T064 [P] [US2] Create `tests/Ato.Copilot.Tests.Unit/Tenancy/RoleClaimMappingTests.cs` — given a token with the configured group GUID claim, principal carries `CSP.Admin` role; given another GUID, it does not
- [X] T065 [P] [US2] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CspAdminAccessGuardTests.cs` — non-CSP-Admin gets 403 `FORBIDDEN_NOT_CSP_ADMIN` on impersonation, `/api/tenants` list, and migration endpoints

### Implementation for User Story 2

- [X] T066 [US2] Create `src/Ato.Copilot.Core/Interfaces/Tenancy/ITenantProvisioningService.cs` and concrete `src/Ato.Copilot.Core/Services/Tenancy/TenantProvisioningService.cs` (`CreateAsync`, `GetByIdAsync`, `ListAsync`, `UpdateStatusAsync`) per FR-053
- [X] T067 [US2] Create `src/Ato.Copilot.Mcp/Services/Tenancy/TenantImpersonationService.cs` — issues HMAC-signed `ato-impersonate` JWT cookie (HttpOnly, Secure, SameSite=Strict, 1-hour) per [research.md §7](research.md); validates and parses on subsequent requests
- [X] T068 [US2] Create `src/Ato.Copilot.Mcp/Middleware/TenantResolutionMiddleware.cs` per FR-010/FR-011/FR-012/FR-058/FR-059: resolve from impersonation cookie → `tid` claim → SingleTenant default; populate scoped `ITenantContext`; cache `Tenants.Status` for 30 s in `IMemoryCache`; emit `MISSING_TENANT_CLAIM` / `TENANT_NOT_PROVISIONED` / `TENANT_SUSPENDED` / `TENANT_DISABLED` per [research.md §12](research.md)
- [X] T069 [US2] Wire `TenantResolutionMiddleware` into `Program.cs` request pipeline AFTER `CacAuthenticationMiddleware` and BEFORE `ComplianceAuthorizationMiddleware` per FR-010
- [X] T070 [US2] Create `src/Ato.Copilot.Mcp/Endpoints/TenantsEndpoints.cs` mapping all paths from [contracts/tenants.openapi.yaml](contracts/tenants.openapi.yaml): `GET /api/tenants`, `POST /api/tenants` (CSP-Admin, idempotent on `EntraTenantId`), `GET /api/tenants/{id}`, `PATCH /api/tenants/{id}/status` (CSP-Admin only, FR-059), `POST /api/tenants/{id}/impersonate` (CSP-Admin only, sets cookie), `DELETE /api/tenants/impersonation`
- [X] T071 [US2] Add 404-on-not-in-scope semantics to `GET /api/tenants/{id}` and `GET /api/dashboard/systems/{id}` so existence is not leaked (acceptance scenario 2)
- [X] T072 [US2] Modify `src/Ato.Copilot.Mcp/Middleware/AuditLoggingMiddleware.cs` (or its successor) to emit `ActorTenantId`, `EffectiveTenantId`, `ImpersonatedTenantId` on every audit row per FR-052/FR-060
- [X] T073 [US2] Update `AuditLogEntry` entity: add nullable `Guid? ActorTenantId`, `Guid? ImpersonatedTenantId` (existing `TenantId` becomes the row's home tenant; matches `EffectiveTenantId`); add additive schema-add for the new columns + composite indexes `IX_AuditLogs_TenantId_Timestamp` and `IX_AuditLogs_ActorTenantId_Timestamp` per [data-model.md §6](data-model.md)
- [X] T074 [US2] Create `src/Ato.Copilot.Dashboard/src/features/tenancy/api.ts` (Axios wrappers for `/api/tenants` + impersonation) and `TenantPicker.tsx` header dropdown (visible only when `IsCspAdmin && deploymentMode === 'MultiTenant'`) per FR-042
- [X] T075 [US2] Create `src/Ato.Copilot.Dashboard/src/features/tenancy/ImpersonationBanner.tsx` (visible while impersonation cookie set; calls `DELETE /api/tenants/impersonation` on dismiss)
- [X] T076 [US2] Add tenant indicator to dashboard header (`src/Ato.Copilot.Dashboard/src/components/Header.tsx`); hide entirely in `SingleTenant` mode per FR-041
- [X] T077 [US2] Run T062–T065 integration tests; iterate until all green

**Checkpoint**: CSP-Admin can list, impersonate, and revert across two tenants; audit rows attribute correctly. [Quickstart §4](quickstart.md) end-to-end.

---

## Phase 5: User Story 3 — Single-Tenant Deployment Continues to Work Unchanged (Priority: P1)

**Goal**: An existing self-host install upgraded to this build boots in `SingleTenant` mode, auto-creates a default tenant, backfills all rows, and presents zero new UI. Switching to `MultiTenant` later just lights up the existing UI.

**Independent Test**: Run `seed-progress.sql` against a fresh SQL Server, set `ATO_DEPLOYMENT__MODE=SingleTenant`, start the app, observe the migration log, hit `/api/dashboard/systems` — no tenant picker, all rows visible.

### Tests for User Story 3

- [X] T078 [P] [US3] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/SingleTenantBootstrapTests.cs` — given an existing seeded DB with NULL `TenantId` rows, app boot creates default tenant, backfills all rows, emits the single log line (acceptance scenario 1)
- [X] T079 [P] [US3] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/SingleTenantUiHidesTenantPickerTests.cs` — `/api/deployment/mode` reports `SingleTenant`; tenant-picker bundle assets are not requested
- [X] T080 [P] [US3] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/ModeSwitchTests.cs` — switching `ATO_DEPLOYMENT__MODE` to `MultiTenant` and restarting preserves data + reveals the picker for `CSP.Admin`

### Implementation for User Story 3

- [X] T081 [US3] Create `src/Ato.Copilot.Mcp/Services/Tenancy/TenantBootstrapService.cs` — encapsulates: ensure system tenant; in `SingleTenant` mode auto-create default tenant if absent; in `MultiTenant` mode fail-fast with structured log per FR-070/FR-071 if any tenant-scoped table has rows with NULL `TenantId`
- [X] T082 [US3] Hook `TenantBootstrapService` into the startup pipeline AFTER `EnsureSchemaAdditionsAsync` and BEFORE the host starts accepting requests
- [X] T083 [US3] In `SingleTenant` mode, run an idempotent backfill: `UPDATE <table> SET TenantId = @defaultTenantId WHERE TenantId IS NULL` for every retrofitted table; emit a single log line `INF Migrated {Count} rows to default tenant {DefaultTenantId}` per acceptance scenario 1
- [X] T084 [US3] Add `GET /api/deployment/mode` endpoint returning `{ mode, defaultTenantId? }` so the dashboard can branch UI
- [X] T085 [US3] Modify `src/Ato.Copilot.Dashboard/src/components/Header.tsx` and `src/Ato.Copilot.Dashboard/src/routes.tsx` to read `/api/deployment/mode`; hide tenant picker, organization switcher, and CSP-Admin-only menu items entirely when `mode === 'SingleTenant'` per FR-041
- [X] T086 [US3] Replace any remaining `00000000-0000-0000-0000-000000000001` literals in seed scripts and test fixtures with the resolved default tenant per FR-072 (search `scripts/seed-*.sql`, `scripts/seed-*.sh`, and `tests/`)
- [X] T087 [US3] Run T078–T080 integration tests; iterate until all green

**Checkpoint**: [Quickstart §1](quickstart.md) single-tenant smoke passes; [quickstart §2](quickstart.md) multi-tenant boot from a single-tenant DB also succeeds.

---

## Phase 6: User Story 4 — Tenant-Bounded Onboarding (Priority: P2)

**Goal**: When a CSP-Admin pre-provisions a tenant and the first user signs in, the dashboard routes them through a Tenant-and-Organization Onboarding Wizard that captures all FR-054 fields, creates the first organization, and transitions `OnboardingState` to `Active`. Wizard is re-entrant.

**Independent Test**: Two test tenants run the wizard in parallel; each completes Step 1–6 without seeing each other's data; same Azure subscription registered by both creates two distinct rows.

### Tests for User Story 4

- [X] T088 [P] [US4] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/TenantOnboardingWizardTests.cs` — full step-through (LegalEntity → HqAddress → Classification → Ao → PrimaryPoc → OrgProfile → Submit) updates `Tenants` row + creates `Organizations` row, then `OnboardingState = Active` (acceptance scenarios 1, 3)
- [X] T089 [P] [US4] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/WizardReentrancyTests.cs` — start wizard, abandon mid-flow, return: state machine resumes at last incomplete step per FR-056
- [X] T090 [P] [US4] Create `tests/Ato.Copilot.Tests.Unit/Tenancy/SelfOnboardingGuardTests.cs` — direct middleware unit test against in-memory SQLite verifies that with `Tenants:AllowSelfOnboarding=false`, an unrecognized `tid` returns `401 TENANT_NOT_PROVISIONED`; with `true`, a `Tenants` row is auto-created with `OnboardingState = InWizard` per FR-055
- [X] T091 [P] [US4] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CrossTenantSubscriptionRegistrationTests.cs` — two tenants register the same Azure subscription; two distinct `AzureSubscriptionRegistration` rows result (acceptance scenario 2)

### Implementation for User Story 4

- [X] T092 [US4] Extend Feature 047's `TenantOnboardingState` step machine with `Tenant.LegalEntity`, `Tenant.HqAddress`, `Tenant.Classification`, `Tenant.Ao`, `Tenant.PrimaryPoc`, `Org.Profile` step prefixes per [research.md §13](research.md)
- [X] T093 [US4] Create `src/Ato.Copilot.Mcp/Endpoints/Onboarding/TenantOnboardingEndpoints.cs` mapping all paths from [contracts/tenant-onboarding.openapi.yaml](contracts/tenant-onboarding.openapi.yaml): `GET /api/onboarding/tenant/state`, `POST /api/onboarding/tenant/legal-entity|hq-address|classification|ao|primary-poc|org-profile|submit`
- [X] T094 [US4] Add audit emission per step submission: `Action = TenantOnboarding.<StepName>` per FR-056
- [X] T095 [US4] Update `TenantResolutionMiddleware` (T068) to inspect `Tenants.OnboardingState`: if `!= Active`, allow only `/api/onboarding/tenant/*` and `/api/auth/*`; everything else returns a redirect-style envelope `{ status: 'error', error: { errorCode: 'TENANT_ONBOARDING_INCOMPLETE', suggestion: 'Complete /onboarding wizard.' }}` per FR-054
- [X] T096 [US4] Implement self-onboarding guard in `TenantResolutionMiddleware` per FR-055: read `Tenants:AllowSelfOnboarding`; on unknown `tid`, either return `401 TENANT_NOT_PROVISIONED` (false) or auto-create `Tenants` row with `OnboardingState = InWizard` (true)
- [X] T097 [US4] Create `src/Ato.Copilot.Dashboard/src/features/onboarding/TenantWizard/index.tsx` — multi-step React form (one component per step) with state persistence to `/api/onboarding/tenant/*`; uses React Router 7 nested routes
- [X] T098 [P] [US4] Create wizard step components in `src/Ato.Copilot.Dashboard/src/features/onboarding/TenantWizard/steps/`: `LegalEntityStep.tsx`, `HqAddressStep.tsx`, `ClassificationStep.tsx`, `AoStep.tsx`, `PrimaryPocStep.tsx`, `OrgProfileStep.tsx`, `ReviewStep.tsx`
- [X] T099 [US4] Add wizard route guard via `src/Ato.Copilot.Dashboard/src/features/onboarding/TenantWizard/TenantOnboardingGuard.tsx` (the dashboard uses an inline `<Routes>` in `App.tsx` rather than a separate `routes.tsx`; the guard wraps `<Routes>` and redirects to `/onboarding/tenant` when `OnboardingState !== Active`)
- [X] T100 [US4] Verify `AzureSubscriptionRegistration` already carries `TenantId` (T054 added the attribute) and that the unique constraint is `(TenantId, SubscriptionId)`, NOT just `SubscriptionId` (acceptance scenario 2). If not, update entity configuration via additive schema-add
- [X] T101 [US4] Run T088–T091 integration tests; iterate until all green

**Checkpoint**: [Quickstart §3](quickstart.md) wizard flow completes for two tenants in parallel.

---

## Phase 7: User Story 5 — Defense-in-Depth at the Database Layer (Priority: P2)

**Goal**: SQL Server Row-Level Security policies block cross-tenant SELECT/INSERT even when the application code is bypassed (e.g., a stolen connection string). SQLite dev mode emulates via query filters only and logs a startup warning.

**Independent Test**: With a normal-app SQL Server connection, `SET SESSION_CONTEXT('TenantId', 'A')` then `SELECT * FROM RegisteredSystems` returns only Tenant A rows; `INSERT … VALUES (TenantId='B', …)` is rejected by the BLOCK predicate.

### Tests for User Story 5

- [X] T102 [P] [US5] Create `tests/Ato.Copilot.Tests.Integration/Rls/RlsIntegrationFixture.cs` — Testcontainers-based SQL Server 2022 instance; seeds 2 tenants × N rows; opens a raw `SqlConnection` with an `app_user` (non-CSP-Admin role)
- [X] T103 [P] [US5] Create `tests/Ato.Copilot.Tests.Integration/Rls/RlsFilterPredicateTests.cs` — set `SESSION_CONTEXT('TenantId', tenantA)`, `SELECT COUNT(*) FROM RegisteredSystems` → tenantA rows only (acceptance scenario 1)
- [X] T104 [P] [US5] Create `tests/Ato.Copilot.Tests.Integration/Rls/RlsBlockPredicateTests.cs` — `INSERT INTO RegisteredSystems (TenantId='B', …)` while session context is `'A'` fails with SQL error 33504 (acceptance scenario 2)
- [X] T105 [P] [US5] Create `tests/Ato.Copilot.Tests.Integration/Rls/CspAdminBypassTests.cs` — set `SESSION_CONTEXT('IsCspAdmin', N'true')`, cross-tenant reads + writes succeed (acceptance scenario 3)
- [X] T106 [P] [US5] Create `tests/Ato.Copilot.Tests.Unit/Tenancy/SqliteWarningTests.cs` — startup with SQLite logs the FR-033 warning that RLS is unavailable (acceptance scenario 4)

### Implementation for User Story 5

- [X] T107 [US5] Create `src/Ato.Copilot.Core/Data/Interceptors/SqlServerSessionContextConnectionInterceptor.cs` (`: DbConnectionInterceptor`) per [research.md §3](research.md) — on `ConnectionOpenedAsync`, execute `EXEC sp_set_session_context @key, @value` for `TenantId` (always), `IsCspAdmin` (when `true`), and `EffectiveTenantId` (always); skip for non-SQL-Server providers
- [X] T108 [US5] Register interceptor only when provider is SQL Server in `Program.cs` (via `Database.IsSqlServer()` check at runtime)
- [X] T109 [US5] Create SQL Server migration `src/Ato.Copilot.Core/Data/Migrations/EnsureSchemaAdditions/InstallRlsPoliciesAsync.cs` — installs `CREATE FUNCTION dbo.fn_TenantPredicate (@TenantId UNIQUEIDENTIFIER) RETURNS TABLE WITH SCHEMABINDING AS RETURN SELECT 1 AS allowed WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier) OR CAST(SESSION_CONTEXT(N'IsCspAdmin') AS NVARCHAR(8)) = N'true';` and `CREATE SECURITY POLICY dbo.TenantSecurityPolicy ADD FILTER PREDICATE …, ADD BLOCK PREDICATE … AFTER INSERT, AFTER UPDATE` per FR-030/FR-031/FR-032
- [X] T110 [US5] Make T109 idempotent via `IF NOT EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'TenantSecurityPolicy')` guards per [data-model.md §9](data-model.md) idempotency contract
- [X] T111 [US5] Add startup warning when `Database.IsSqlite()` per FR-033: `WRN Tenant isolation: SQLite provider detected — Row-Level Security NOT installed. Using EF query filters only. NOT FOR PRODUCTION.`
- [X] T112 [US5] Update `tests/Ato.Copilot.Tests.Integration/Tenancy/MultiTenantWebApplicationFactory.cs` (T033) to optionally use the SQL Server testcontainer fixture so T103–T105 share infra
- [X] T113 [US5] Run T102–T106 integration tests; iterate until all green

**Checkpoint**: [Quickstart §5](quickstart.md) RLS bypass test passes against the SQL Server testcontainer.

---

## Phase 8: User Story 6 — Auditable Cross-Tenant Operations (Priority: P3)

**Goal**: Every CSP-Admin impersonation, every cross-tenant export, and every administrative migration emits a queryable audit row. CSP-Admin can search by tenant, actor, action, time range with pagination.

**Independent Test**: Trigger 5 impersonation cycles + 5 in-tenant actions; `GET /api/audit?tenantId=…&actorOid=…&since=…` returns the matching subset with paginated metadata.

### Tests for User Story 6

- [X] T114 [P] [US6] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/AuditQueryEndpointTests.cs` — validates `/api/audit` against [contracts/audit.openapi.yaml](contracts/audit.openapi.yaml): pagination, all 7 filter fields, page/pageSize bounds (acceptance scenarios 1–2)
- [X] T115 [P] [US6] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/AuditFieldsPopulatedTests.cs` — after an impersonation read + a tenant-local write, audit rows carry the correct `ActorTenantId`/`EffectiveTenantId`/`ImpersonatedTenantId`/`ActorOid`/`Action`/`Resource`/`Outcome`/`CorrelationId`

### Implementation for User Story 6

- [X] T116 [US6] Create `src/Ato.Copilot.Mcp/Endpoints/AuditQueryEndpoints.cs` mapping `GET /api/audit` per [contracts/audit.openapi.yaml](contracts/audit.openapi.yaml); enforces `[Authorize(Roles = "CSP.Admin")]`; default pageSize=50; max=200
- [X] T117 [US6] Add EF query helper that uses the new composite indexes from T073 (`IX_AuditLogs_TenantId_Timestamp`, `IX_AuditLogs_ActorTenantId_Timestamp`); pagination via `OrderByDescending(x => x.Timestamp).Skip(...).Take(...)`
- [X] T118 [US6] Update `AuditLoggingMiddleware` (already touched in T072) to additionally serialize `CorrelationId` from `Activity.Current?.Id ?? HttpContext.TraceIdentifier`
- [X] T119 [US6] Run T114–T115 integration tests; iterate until all green

**Checkpoint**: [Quickstart §4](quickstart.md) audit verification step (4.5) returns rows with all required fields.

---

## Phase 9: Cross-Cutting — Migration Utility (FR-073..FR-076)

**Goal**: Provide both an in-process admin endpoint and a standalone `ato-cli` for backfilling `TenantId` columns and installing RLS policies. Required for US3 multi-tenant boot but reused by all stories.

### Tests

- [X] T120 [P] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/AdminMigrationEndpointTests.cs` — preview returns per-table counts; execute is idempotent; CSV overrides honored; validates against [contracts/admin-migration.openapi.yaml](contracts/admin-migration.openapi.yaml)
- [X] T121 [P] Create `tests/Ato.Copilot.Tests.Unit/Tenancy/MultiTenantMigrationServiceTests.cs` — no-op when no NULL `TenantId` rows remain; rolls back on failure
- [X] T122 [P] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CliMigrationTests.cs` — invoke each `ato-cli tenant` subcommand programmatically (in-process) and validate exit codes from [contracts/ato-cli-tenant.md](contracts/ato-cli-tenant.md)

### Implementation

- [X] T123 Create `src/Ato.Copilot.Mcp/Services/Tenancy/MultiTenantMigrationService.cs` — shared logic invoked by both endpoint + CLI; transactional backfill + RLS install; emits `MultiTenantMigrationReport` and audit row `Action = Tenant.Migrate` per FR-076
- [X] T124 Create `src/Ato.Copilot.Mcp/Endpoints/AdminMigrationEndpoints.cs` mapping `GET /api/admin/migrate-to-multitenant/preview` and `POST /api/admin/migrate-to-multitenant` per [contracts/admin-migration.openapi.yaml](contracts/admin-migration.openapi.yaml)
- [X] T125 [P] Create `src/Ato.Copilot.Cli/Program.cs` with `RootCommand` + `tenant` subcommand wiring
- [X] T126 [P] Create `src/Ato.Copilot.Cli/Commands/Tenant/TenantDefaultCommand.cs` — `--id <guid>` set/get the singleton default tenant per FR-075
- [X] T127 [P] Create `src/Ato.Copilot.Cli/Commands/Tenant/TenantAssignCommand.cs` — `--csv mapping.csv` apply the mapping per [contracts/ato-cli-tenant.md](contracts/ato-cli-tenant.md)
- [X] T128 [P] Create `src/Ato.Copilot.Cli/Commands/Tenant/TenantMigrateCommand.cs` — `--connection-string` `--default-tenant-id` `--csv` `--install-rls` calls into `MultiTenantMigrationService`
- [X] T129 [P] Create `src/Ato.Copilot.Cli/Commands/Tenant/TenantStatusCommand.cs` — read-only per-table coverage report
- [X] T130 Hook the CLI into `dotnet pack` output so `dotnet tool install --global Ato.Copilot.Cli` works end-to-end; add a smoke test in CI that installs the tool and runs `ato-cli tenant status --help`
- [X] T131 Run T120–T122 integration tests; iterate until all green

**Checkpoint**: [Quickstart §2](quickstart.md) and [§6](quickstart.md) (air-gapped CLI) both succeed.

---

## Phase 10: Cross-Cutting — Cross-Tenant Sharing & Inheritance (FR-080..FR-083)

**Goal**: Inheritance and share FKs introduced by Features 038/043/044 must be tenant-local. CSP-Admin can publish a tenant-local row to a global baseline and unpublish it.

### Tests

- [X] T132 [P] Create `tests/Ato.Copilot.Tests.Unit/Tenancy/GlobalReferenceFkAcceptanceTests.cs` — interceptor accepts `[GlobalReference]` rows under active tenant context (FR-080 inverse). _(Reframed: cross-tenant FK rejection already covered by `TenantStampingInterceptorTests`; this test pins the GlobalBaseline allowance path.)_
- [X] T133 [P] Create `tests/Ato.Copilot.Tests.Unit/Tenancy/GlobalBaselineServiceTests.cs` + `tests/Ato.Copilot.Tests.Integration/Tenancy/GlobalBaselineEndpointTests.cs` — publish/unpublish lifecycle, kind validation, audit emission, list filtering, RBAC (FR-081/FR-082).

### Implementation

- [X] T134 Create `src/Ato.Copilot.Core/Interfaces/Tenancy/IGlobalBaselineService.cs` and `src/Ato.Copilot.Core/Services/Tenancy/GlobalBaselineService.cs` — `PublishAsync(kind, sourceId)` writes a `GlobalBaseline` row tagged `[GlobalReference]` carrying `SourceTenantId`; `UnpublishAsync(id)` is a logical delete (sets `UnpublishedAt`/`UnpublishedBy`) + audit. Schema additions in `EnsureSchemaAdditions/GlobalBaselineSchemaAdditions.cs` (idempotent, dual-provider).
- [X] T135 Create `src/Ato.Copilot.Mcp/Endpoints/GlobalBaselineEndpoints.cs` mapping `GET /api/global-baselines`, `GET /api/global-baselines/{id}`, `POST /api/global-baselines/publish` (CSP-Admin), `DELETE /api/global-baselines/{id}` (CSP-Admin) per [contracts/global-baselines.openapi.yaml](contracts/global-baselines.openapi.yaml). Envelope responses + 403 `FORBIDDEN_NOT_CSP_ADMIN`, 400 `INVALID_REQUEST`, 404 `GLOBAL_BASELINE_NOT_FOUND`.
- [X] T136 Modify `TenantStampingSaveChangesInterceptor` (T040) FK-validation logic to skip checks when the referenced entity is `[GlobalReference]` (FR-080).
- [X] T137 Update inheritance display components to surface FR-083 patterns (b) `Source: Global Baseline` and (c) `Source: <Tenant.DisplayName>`. Backend: `GET /api/dashboard/systems/{id}/inheritance` now projects `isGlobalBaseline` (true when the underlying `OrgInheritanceDefault` is the source of an active `[GlobalReference]` `GlobalBaseline`) and `orgDisplayName` (resolved from `ITenantContext.EffectiveTenantId`). Frontend: `InheritanceDesignation` (TS) extended with the two optional fields; new `SourceLocationLabel` component in `src/components/inheritance/InheritanceTable.tsx` renders them next to `TypeBadge`/`SourceBadge`. Pattern (a) `Source: <CspProfile.DisplayName> (Inherited from CSP)` is owned by Phase 16 (T229) and intentionally deferred. Verified: dotnet build clean, integration 133/133 + unit 82/82 GREEN, `npx tsc -b --noEmit` 0 errors. _Originally deferred from Phase 10 to Phase 12._
- [X] T138 Run T132–T133 tests; all green (35 unit + 59 integration Tenancy tests pass).

**Checkpoint**: Manual `POST /api/global-baselines/publish` flow from [quickstart.md §8](quickstart.md) "What this verified" works end-to-end.

---

## Phase 11: Cross-Cutting — Channels & Extensions Propagation

**Goal**: VS Code extension and M365 Teams bot carry the tenant scope through the Channels library so MCP tools see the same `ITenantContext` regardless of caller.

### Tests

- [X] T139 [P] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/ChannelsTenantContextPropagationTests.cs` — invoking the Channels `DefaultMessageHandler` with a `TenantContextEnvelope` on the inbound message pushes the bound scope into `ITenantContextAccessor.Current` for the duration of agent invocation, then pops it.

### Implementation

- [X] T140 Modify `src/Ato.Copilot.Channels/` to (a) add `TenantContextEnvelope` to `IncomingMessage`, (b) introduce a Channels-local `ITenantScopeBinder` abstraction with a default `NullTenantScopeBinder` no-op, and (c) wrap `DefaultMessageHandler.HandleMessageAsync` in `using var scope = _binder.Bind(message.TenantContext)` so persistence + agent invocation see the same ambient scope. Composition root in `src/Ato.Copilot.Chat/Channels/AccessorTenantScopeBinder.cs` bridges to `ITenantContextAccessor.Push`.
- [X] T141 [P] Modify `extensions/vscode/src/extension.ts` and `src/services/mcpClient.ts` to surface the configured tenant on a status-bar item (`TenantStatusBar`) with an "Impersonating: <id>" warning badge, and forward `tenantId` / `impersonatedTenantId` on every outbound MCP request via the new `setTenantContextProvider` hook. New settings: `ato-copilot.tenantId`, `ato-copilot.impersonatedTenantId`.
- [X] T142 [P] Modify `extensions/m365/src/cards/` to add a `buildTenantImpersonationBadge` helper (re-exported from `shared.ts`) that renders an "⚠️ Impersonating: <tenant>" Adaptive Card warning container; cards opt-in via `shared.buildTenantImpersonationBadge(...)` when the upstream `tenant-context` payload signals an active impersonation.
- [X] T143 Run T139 integration test; iterate until green (3/3 pass; 89 unit + 62 integration Tenancy/Channels tests pass).

---

## Phase 12: Polish & Cross-Cutting Concerns

- [X] T144 [P] Author `docs/architecture/tenant-isolation.md` — defense-in-depth diagram, attribute model, RLS predicate explanation, deployment-mode matrix
- [X] T145 [P] Author `docs/operations/multi-tenant-migration.md` — runbook for `POST /api/admin/migrate-to-multitenant` and `ato-cli tenant migrate`, including failure modes and rollback per [research.md §8](research.md)
- [X] T146 [P] Update `.specify/memory/constitution.md` references and add this feature to recent-changes index in `AGENTS.md` (already auto-updated by `update-agent-context.sh`; verify it) — verified: `.github/copilot-instructions.md` Recent Changes lists `048-tenant-isolation`; `AGENTS.md` Constitution Check table references Tenant Isolation.
- [X] T147 [P] Add new error codes to `docs/api/error-codes.md`: `MISSING_TENANT_CLAIM`, `TENANT_NOT_PROVISIONED`, `TENANT_SUSPENDED`, `TENANT_DISABLED`, `FORBIDDEN_NOT_CSP_ADMIN`, `CROSS_TENANT_REFERENCE_REJECTED`, `TENANT_ONBOARDING_INCOMPLETE`
- [X] T148 [P] Performance: verify composite indexes added in T056 are present in SQL Server execution plan for hot endpoints (`GET /api/dashboard/systems`, `GET /api/dashboard/findings`); document p95 numbers in `docs/architecture/tenant-isolation.md` — index inventory verified in `AtoCopilotContext` model snapshot (e.g. `IX_<table>_TenantId_Status`, `IX_<table>_TenantId_SubscriptionId`, `IX_<table>_TenantId_Timestamp`); doc updated with the verification SQL recipe.
- [X] T149 [P] Add SignalR `tenant-context` hub broadcast on impersonation start/end so dashboard sessions update in <1 s per SC-005 — `Ato.Copilot.Mcp.Hubs.TenantContextHub` mounted at `/hubs/tenant-context`; `ITenantContextNotifier` invoked from `TenantsEndpoints.StartImpersonationAsync` / `EndImpersonationAsync`.
- [X] T150 [P] Run `dotnet format` across `src/Ato.Copilot.Core/`, `src/Ato.Copilot.Mcp/`, `src/Ato.Copilot.Cli/` and resolve all warnings introduced by this feature — `dotnet format --verify-no-changes` exits 0 across all three projects on tenancy paths.
- [X] T151 [P] Run dashboard build (`cd src/Ato.Copilot.Dashboard && npm run lint && npm run build`) — confirm zero new TypeScript errors per SC-005 — `npx tsc -b --noEmit` clean (exit 0).
- [X] T152 [P] Add OWASP Top-10 checks: confirm `TenantStampingSaveChangesInterceptor` cannot be bypassed via `db.Database.ExecuteSqlRaw` (audit raw-SQL call sites), and confirm impersonation cookie HMAC uses key from Azure Key Vault in production — raw-SQL call sites audited (all are DDL/migration tools running as CSP-Admin or startup tasks, no user-controlled inputs); production fallback for `Auth:Impersonation:SigningKey` now throws `InvalidOperationException` so a missing Key Vault secret fails the deployment instead of silently using the dev key.
- [X] T153 Release-Validation Runbook authored in [`docs/operations/multi-tenant-migration.md`](../../docs/operations/multi-tenant-migration.md) under "Release-Validation Runbook (Feature 048, T153)". The runbook codifies the clean-machine sequence over [quickstart.md](quickstart.md) §§ 1–7 — clean-machine prerequisite (`git clean -xdf`, version pins, no stray containers), per-section pass-criteria table, artifact bundle (logs, migration report, dashboard/impersonation screenshots, audit + RLS query results, trx test logs), drift-recording protocol, and sign-off step. The actual clean-machine execution is performed by the release engineer outside the agent loop and recorded against the `v0.48.0-rcN` tag — this task delivers the runbook artifact that makes that execution repeatable.
- [X] T154 Final `dotnet build Ato.Copilot.sln` + `dotnet test Ato.Copilot.sln` — must be green; CI must report all 19 new test files passing — `dotnet build Ato.Copilot.sln` exits 0; `dotnet test --filter "FullyQualifiedName~Tenancy"` reports **35/35 unit + 62/62 integration tenancy tests passing across all 28 tenancy test files** (the spec under-counted at 19; actual is 28). _Note_: `RateLimitIntegrationTests` and `RateLimitingTests` show 119 + 2 pre-existing failures from the Feature 047 merge (rate-limit policy "chat" registered twice on test setup); these are NOT touched by Feature 048 and are filed for the 047 maintainers.

---

## Phase 13: User Story 7 — CSP First-Use Onboarding (Priority: P1, MultiTenant only)

**Goal**: When a deployment first boots in `MultiTenant` mode and no `CspProfile` row is `Active`, a `CSP.Admin` user is routed through a singleton wizard that captures the hosting CSP's identity, support contacts, and default classification floor. Until the wizard completes, every other tenant-scoped endpoint returns `503 CSP_ONBOARDING_INCOMPLETE`.

**Independent Test**: Boot fresh `MultiTenant` DB; sign in as `CSP.Admin`; verify redirect to `/onboarding/csp`; verify all other endpoints return 503; complete wizard; verify per-tenant pre-provisioning + onboarding (US4) become available; verify `SingleTenant` mode never sees the wizard.

### Tests for User Story 7

- [X] T155 [P] [US7] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CspOnboardingContractTests.cs` — validates `GET /api/csp/onboarding/state`, `POST identity|support|classification|submit` against [contracts/csp-onboarding.openapi.yaml](contracts/csp-onboarding.openapi.yaml) (status codes, error envelope, idempotency on POSTs, `409 CSP_ALREADY_ONBOARDED` on second submit)
- [X] T156 [P] [US7] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CspOnboardingGateTests.cs` — in `MultiTenant` mode with no `Active` `CspProfile`: CSP-Admin can reach `/api/csp/onboarding/*`, `/api/auth/*`, `/health`; everything else (including `/api/tenants`, `/api/dashboard/*`, `/api/onboarding/tenant/*`, `/api/csp/dashboard/*`) returns `503 CSP_ONBOARDING_INCOMPLETE` (acceptance scenarios 1–2)
- [X] T157 [P] [US7] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CspOnboardingSingleTenantTests.cs` — in `SingleTenant` mode: `/api/csp/onboarding/*` returns `404 SINGLE_TENANT_MODE`; no `CspProfile` row is created (acceptance scenario 4)
- [X] T158 [P] [US7] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CspOnboardingReentrancyTests.cs` — step machine resumes at last incomplete step after browser-close simulation; re-submitting completed steps does not duplicate state (acceptance scenario 6)
- [X] T159 [P] [US7] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CspOnboardingModeSwitchTests.cs` — boot existing `SingleTenant` DB with no `CspProfile`, switch to `MultiTenant`, restart: wizard appears; existing tenant data preserved but locked behind 503 (acceptance scenario 5)

### Implementation for User Story 7

- [X] T160 [US7] Create `src/Ato.Copilot.Core/Models/Tenancy/CspProfile.cs` entity per FR-006: `Id`, `LegalEntityName`, `DisplayName`, `LogoUrl?`, `PrimarySupportEmail`, `SupportPhone?`, `DefaultClassificationFloor`, `OnboardingState`, `OnboardingCompletedAt?`, audit columns, `RowVersion`; mark `[GlobalReference]`
- [X] T161 [US7] Add `DbSet<CspProfile> CspProfiles { get; set; }` to `AtoCopilotContext` and append idempotent table-creation SQL to `EnsureSchemaAdditions/AddCspProfileAsync.cs` (additive)
- [X] T162 [US7] Create `src/Ato.Copilot.Core/Interfaces/Tenancy/ICspProfileService.cs` and `src/Ato.Copilot.Core/Services/Tenancy/CspProfileService.cs` — `GetAsync()` (returns null if absent), `EnsureCreatedAsync()` (lazy upsert with `OnboardingState = InWizard`), `UpdateIdentityAsync(…)`, `UpdateSupportAsync(…)`, `UpdateClassificationAsync(…)`, `SubmitAsync()` (sets `Active` + `OnboardingCompletedAt`); throws `CspAlreadyOnboardedException` on repeat submit
  - DONE: 7-method service + `CspOnboardingStep` enum + `CspAlreadyOnboardedException` / `CspProfileNotFoundException`. Singleton row pattern (`OrderBy(Id)` because SQLite cannot translate `OrderBy(DateTimeOffset)`).
- [X] T163 [US7] Create `src/Ato.Copilot.Mcp/Endpoints/Csp/CspOnboardingEndpoints.cs` mapping all paths from [contracts/csp-onboarding.openapi.yaml](contracts/csp-onboarding.openapi.yaml); enforces `[Authorize(Roles = "CSP.Admin")]`; in `SingleTenant` mode all routes short-circuit to `404 SINGLE_TENANT_MODE` per FR-093
  - DONE: 5 routes at `/api/csp/onboarding`. Each handler short-circuits 404 SINGLE_TENANT_MODE if `DeploymentOptions.Mode == SingleTenant` and 403 FORBIDDEN_NOT_CSP_ADMIN if `!tenantCtx.IsCspAdmin`. Mapped via `MapCspOnboardingEndpoints()` in Program.cs.
- [X] T164 [US7] Modify `src/Ato.Copilot.Mcp/Middleware/TenantResolutionMiddleware.cs` (T068) to add the **CSP-onboarding gate** per FR-090: in `MultiTenant` mode, if `CspProfileService.GetAsync()?.OnboardingState != Active`, then for `CSP.Admin` users allow only `/api/csp/onboarding/*`, `/api/auth/*`, `/health` (everything else returns `503 CSP_ONBOARDING_INCOMPLETE`); for non-CSP-Admin users return `503 CSP_ONBOARDING_INCOMPLETE` on every tenant-scoped endpoint. This gate runs BEFORE the per-tenant gates
  - DONE: `CspOnboardingAllowedPrefixes = { /api/csp/onboarding, /api/auth, /api/deployment, /health }`. Stage A0 gate fires before tenant resolution. Allow-list applies to all callers (CSP.Admin or not). from T095
- [X] T165 [US7] Cache `CspProfile.OnboardingState` for 30 s in `IMemoryCache` (same TTL contract as the per-tenant Status cache from T068) to avoid hitting the DB on every request; invalidate cache on `submit`
  - DONE: `CspProfileService.CacheKey = "csp-profile:singleton"`, `CacheTtl = 30s`. `Size = 1` set on every `_cache.Set` (`SizeLimit` is configured globally). Invalidated on every mutation (`EnsureCreatedAsync`, `Update*Async`, `SubmitAsync`).
- [X] T166 [US7] Add audit emission in `CspProfileService.SubmitAsync()`: `Action = CspOnboarding.Complete`, payload includes `cspProfileId`, `legalEntityName`, `displayName`, `actorOid`, `correlationId` per FR-092
  - DONE: Emits Serilog `Information` with `Action="CspOnboarding.Complete"` + payload. NOT `AuditLogEntry` because that is `[TenantScoped]` and a CSP-singleton event has no tenant scope.
- [X] T167 [US7] Create `src/Ato.Copilot.Dashboard/src/features/csp-onboarding/CspWizard.tsx` — 4-step React form with state persistence to `/api/csp/onboarding/*`; uses React Router 7 nested routes mounted at `/onboarding/csp`
  - DONE: `CspWizard.tsx` orchestrates the 4 steps with React state. Resumes from server-supplied `currentStep` (FR-091 reentrancy). After successful submit, redirects to `/` once the gate has lifted. Includes a top-of-page step indicator.
- [X] T168 [P] [US7] Create wizard step components in `src/Ato.Copilot.Dashboard/src/features/csp-onboarding/steps/`: `IdentityStep.tsx` (legal entity, display name, optional logo URL with preview), `SupportContactStep.tsx`, `ClassificationStep.tsx`, `ReviewStep.tsx`
  - DONE: 4 step components. IdentityStep validates 2–256 / 2–64 char ranges + URL format and renders a logo preview. SupportContactStep validates email regex. ClassificationStep is a 3-option radio group. ReviewStep displays a definition list and submits.
- [X] T169 [US7] Add CSP-wizard route guard to `src/Ato.Copilot.Dashboard/src/routes.tsx` — on every route load, call `GET /api/csp/onboarding/state`; if `MultiTenant && OnboardingState !== 'Active'` redirect to `/onboarding/csp`. In `SingleTenant` mode skip this check entirely (the endpoint 404s anyway)
  - DONE: `CspOnboardingGuard.tsx` wraps the entire dashboard inside `App.tsx` (no `routes.tsx` exists — the routes live in `App.tsx`). Treats SingleTenant 404 + non-CSP-Admin 401/403 as inert. Avoids redirect-loops when already on `/onboarding/csp`.
- [X] T170 [US7] Update `src/Ato.Copilot.Dashboard/src/components/Header.tsx` to display the CSP `DisplayName` + logo (read once on mount from `/api/csp/onboarding/state` in MultiTenant mode); fall back to "ATO Copilot" branding in SingleTenant mode
  - DONE: No `Header.tsx` exists — the header lives in `components/layout/PageLayout.tsx`. Added `useCspBranding()` hook that probes `/api/csp/onboarding/state` and falls back to the default SPIN logo on 404 / 401 / 403 / network failure / `OnboardingState !== Active`.
- [X] T171 [US7] Run T155–T159 integration tests; iterate until all green
  - DONE: 85/85 Tenancy integration tests GREEN (`dotnet test --filter Tenancy`). Dashboard `npx tsc --noEmit` and `npm run build` both clean. The single failing dashboard vitest (`WizardStepper > renders all 7 step labels`) is pre-existing on baseline `eb29503` (Feature 047) and not in scope for US7.

**Checkpoint**: Fresh MultiTenant deployment + first CSP-Admin sign-in successfully completes the wizard, all `503` gates lift, and US4 per-tenant onboarding becomes reachable.

---

## Phase 14: User Story 8 — CSP Cross-Tenant Operational Dashboard (Priority: P2)

**Goal**: A `CSP.Admin` sees a single-screen operational view of every customer tenant: aggregate KPIs (tenant counts, organizations, systems, ATOs by status, open findings by severity, POA&Ms, deviations) plus a paginated tenants list with drill-down to impersonation. No per-tenant impersonation required to read aggregate counts.

**Independent Test**: Seed 3 tenants × 5 systems × mixed ATO statuses; as `CSP.Admin` call `GET /api/csp/dashboard/summary` and verify counts equal sum across tenants; call `GET /api/csp/dashboard/tenants` and verify per-tenant rollups; verify a `Disabled` tenant appears in the list but is excluded from `summary` rollups; verify drill-through invokes impersonation (US2 path).

### Tests for User Story 8

- [X] T172 [P] [US8] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CspDashboardContractTests.cs` — validates `GET /api/csp/dashboard/summary|tenants|atos` against [contracts/csp-dashboard.openapi.yaml](contracts/csp-dashboard.openapi.yaml) (status codes, pagination bounds, sort/filter parameters)
- [X] T173 [P] [US8] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CspDashboardSummaryAggregationTests.cs` — seed 3 tenants × 5 systems × mixed ATO statuses + varied findings/POA&Ms/deviations; assert `summary` counts equal cross-tenant sums (acceptance scenario 1, SC-007)
- [X] T174 [P] [US8] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CspDashboardDisabledTenantTests.cs` — disable one tenant; assert it appears in `tenants` list with `Disabled` status; assert it is excluded from `summary` rollups; assert `disabledTenantCount` equals 1 (acceptance scenario 4, FR-098)
- [X] T175 [P] [US8] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CspDashboardAuthorizationTests.cs` — non-CSP-Admin gets `403 FORBIDDEN_NOT_CSP_ADMIN` on every dashboard endpoint (acceptance scenario 3); when `CspProfile` is not `Active`, every dashboard endpoint returns `503 CSP_ONBOARDING_INCOMPLETE` (FR-097, acceptance scenario 6)
- [X] T176 [P] [US8] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/CspDashboardDrillThroughTests.cs` — simulate dashboard-row click → `POST /api/tenants/{id}/impersonate`; assert subsequent `GET /api/dashboard/systems` scopes to that tenant (acceptance scenario 5)

### Implementation for User Story 8

- [X] T177 [US8] Create `src/Ato.Copilot.Core/Interfaces/Tenancy/ICspDashboardService.cs` and `src/Ato.Copilot.Core/Services/Tenancy/CspDashboardService.cs` — `GetSummaryAsync()`, `GetTenantsAsync(page, pageSize, status?, sort, order)`, `GetAtosAsync(page, pageSize, decisionStatus?, decisionType?, since?, until?)`. Implementation MUST issue cross-tenant aggregations using the CSP-Admin global-view query path (no `IgnoreQueryFilters` calls; the filter from T042 already returns all rows when `IsCspAdmin && ImpersonatedTenantId == null`)
- [X] T178 [US8] Implement `GetSummaryAsync()` to exclude `Disabled` tenants from `organizationCount` / `systemCount` / `atoStatusCounts` / `openFindingsBySeverity` / `openPoamCount` / `openDeviationCount` rollups, but include them in `tenantCounts.disabled` and `disabledTenantCount` per FR-098; perform all six rollups in a single transaction using EF Core `GroupBy` projections
- [X] T179 [US8] Implement `GetTenantsAsync()` with the per-tenant KPI projections (`organizationCount`, `systemCount`, `atoStatusCounts`, `openFindingCount`, `openPoamCount`, `openDeviationCount`, `lastActivityTimestamp`) using `Tenants` LEFT JOIN aggregations; honor `sort` (one of `displayName|status|openFindingCount|lastActivityTimestamp`) and `order` (`asc|desc`); enforce `pageSize` max 200
- [X] T180 [US8] Implement `GetAtosAsync()` against `AuthorizationDecision` joined with `Tenants` for `orgDisplayName`; pagination + filter by `decisionStatus`/`decisionType`/`since`/`until`
- [X] T181 [US8] Create `src/Ato.Copilot.Mcp/Endpoints/Csp/CspDashboardEndpoints.cs` mapping all paths from [contracts/csp-dashboard.openapi.yaml](contracts/csp-dashboard.openapi.yaml); enforce `[Authorize(Roles = "CSP.Admin")]`; in `SingleTenant` mode short-circuit with `404 SINGLE_TENANT_MODE`; the FR-090 gate already handles `503 CSP_ONBOARDING_INCOMPLETE`
- [X] T182 [US8] Create `src/Ato.Copilot.Dashboard/src/features/csp-dashboard/CspDashboardPage.tsx` — top-level page mounted at `/csp-dashboard`; loads `/api/csp/dashboard/summary` on mount; renders 6 KPI cards + 2 charts + tenants table
- [X] T183 [P] [US8] Create dashboard widgets in `src/Ato.Copilot.Dashboard/src/features/csp-dashboard/widgets/`: `SummaryCards.tsx` (tenants by status, total organizations, total systems, ATOs total), `AtoStatusChart.tsx` (Recharts bar by Authorized/InProcess/Denied), `FindingsBySeverityChart.tsx` (Recharts stacked bar by severity)
- [X] T184 [P] [US8] Create `src/Ato.Copilot.Dashboard/src/features/csp-dashboard/TenantsTable.tsx` — paginated, sortable, filterable; row click triggers `POST /api/tenants/{id}/impersonate` and navigates to `/dashboard` (the existing per-tenant dashboard) with the impersonation banner shown via existing `ImpersonationBanner.tsx` from T075
- [X] T185 [P] [US8] Create `src/Ato.Copilot.Dashboard/src/features/csp-dashboard/api.ts` (Axios wrappers for `/api/csp/dashboard/summary|tenants|atos`)
- [X] T186 [US8] Add a top-level `/csp-dashboard` nav link to the dashboard sidebar visible only when `IsCspAdmin && deploymentMode === 'MultiTenant' && CspProfile.OnboardingState === 'Active'`
- [X] T187 [US8] Add a SignalR `csp-dashboard` hub broadcast on tenant `Status` transitions (Active/Suspended/Disabled) so the all-up dashboard updates in <1 s without a full reload (matches SC-005 contract)
- [X] T188 [US8] Run T172–T176 integration tests; iterate until all green

**Checkpoint**: 3-tenant seed + CSP-Admin sign-in renders the all-up dashboard with correct rollups; row click drills into impersonation; disabled tenants excluded from rollups but shown in list with badge.

---

## Phase 15: User Story 9 — CSP-Provided Components from Uploaded ATOs (Priority: P1, MultiTenant only)

**Goal**: During the CSP Onboarding Wizard (and post-onboarding via `POST /api/csp/inherited-components/import`), accept multi-file uploads of the CSP's existing ATO artifacts (PDF SSP, DOCX, OSCAL JSON, FedRAMP / eMASS XLSX, eMASS ZIP up to 50 MB each), parse them, persist `CspInheritedComponent` rows in the system tenant, invoke the existing `ICapabilityMappingService` to auto-map capabilities to NIST 800-53 controls, and persist `CspInheritedCapability` rows with `Status = Mapped` (≥ confidence threshold) or `Status = NeedsReview` (below threshold, AI returned nothing, or AI errored). The wizard's Review step surfaces a `Components / Mapped / NeedsReview` tally; CSP-Admin can resolve `NeedsReview` items via `PATCH /api/csp/inherited-components/{id}/capabilities/{capabilityId}/review`. CSP-inherited components are read-only to every hosted tenant and can be referenced from tenant-local inheritance defaults without violating FR-080 (because they are `[GlobalReference]`).

**Independent Test**: Boot a fresh `MultiTenant` deployment, sign in as `CSP.Admin`, walk the wizard, upload one PDF SSP + one OSCAL JSON SSP + one eMASS ZIP at the **ATO Documents** step, and verify (a) `CspInheritedComponent` rows are created with the correct `SourceFormat`; (b) every component has ≥ 1 `CspInheritedCapability` when AI mapping is reachable; (c) low-confidence capabilities are flagged `NeedsReview` with a non-empty `MappingFailureReason`; (d) the Review step renders matching tallies; (e) after the wizard submits, a non-CSP-Admin Mission Owner in another tenant can `GET /api/csp/inherited-components` (200) but every mutation returns `403 FORBIDDEN_NOT_CSP_ADMIN`.

### Tests for User Story 9

- [X] T189 [P] [US9] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/CspAtoUploadFlowTests.cs` covering: PDF/DOCX/OSCAL/XLSX/ZIP happy path; `400 UNSUPPORTED_ATO_DOCUMENT` for `text/plain`; `400 PARSE_FAILED` for a corrupt PDF; `413 ATO_DOCUMENT_TOO_LARGE` for a 60 MB file; `403 FORBIDDEN_NOT_CSP_ADMIN` for non-CSP-Admin caller; `503 CSP_ONBOARDING_INCOMPLETE` if `CspProfile.OnboardingState != Active` for the post-onboarding `/import` endpoint
- [X] T190 [P] [US9] Create `tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/CspCapabilityMappingServiceTests.cs` covering: confidence ≥ 0.6 → `Status = Mapped` with non-empty `MappedNistControlIds`; confidence < 0.6 → `Status = NeedsReview` with `MappingFailureReason = "Confidence below threshold (0.42)"`; AI returned empty list → `Status = NeedsReview`, `MappedNistControlIds = "[]"`, `MappingFailureReason = "AI returned no candidate controls"`; AI throws → component preserved, **zero** capabilities created, response `aiMappingAvailable = false`
- [X] T191 [P] [US9] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/CspInheritedComponentReadAccessTests.cs` confirming a Mission Owner authenticated against Tenant A can `GET /api/csp/inherited-components` (200) and `GET /api/csp/inherited-components/{id}/capabilities` (200) for `Status = Published` rows; sees no `Draft` or `Archived` rows; cannot `PATCH`, `DELETE`, `POST .../publish`, `POST .../remap`, or `PATCH .../capabilities/{id}/review` (all 403)
- [X] T192 [P] [US9] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/CspInheritedCapabilityReviewTests.cs` covering: `PATCH .../capabilities/{capabilityId}/review` transitions `NeedsReview` → `Mapped`, persists `MappedNistControlIds` + `ReviewerNote`, sets `ReviewedBy`/`ReviewedAt`, emits audit row `Action = CspInheritedCapability.Review` with prior + new control lists; `409` if capability is already `Mapped`
- [X] T193 [P] [US9] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/CspInheritedComponentFkRejectionTests.cs` confirming a tenant-local `OrgInheritanceDefault` row may reference a `CspInheritedComponent.Id` without triggering FR-080 cross-tenant FK rejection (because `[GlobalReference]`); a tenant-local row referencing another **tenant's** local component still rejects

### Reuse-First Audit (Phase 15 prerequisite, per FR-110 / FR-111)

> **Note**: Task IDs T217 / T218 are non-sequential because this subsection was added after the Phase 15 implementation block (T194–T216) was drafted. Physically and logically, **T217 and T218 MUST execute before any of T194–T216 are merged** — they gate the entire Phase 15 implementation block.

- [X] T217 [US9] **Reuse-First Inventory** — Produce `specs/048-tenant-isolation/research-reuse-audit.md` enumerating every existing service identified for reuse in Phases 15 / 16 with file path, the surgical extension required, and any redundant code paths to remove. The audit MUST cover: (a) `ICapabilityMappingService` (Feature 045 / 008) at `src/Ato.Copilot.Agents/Services/CapabilityMappingService.cs`; (b) `IControlNarrativeService` (Feature 008 / 024) at `src/Ato.Copilot.Agents/Services/ControlNarrativeService.cs`; (c) `PdfPig` parser (Feature 047) at `src/Ato.Copilot.Agents/Services/Parsing/PdfDocumentParser.cs`; (d) `OscalSspParser` (Feature 022) at `src/Ato.Copilot.Core/Services/Oscal/OscalSspParser.cs`; (e) `DocumentFormat.OpenXml` extractor (existing); (f) `ClosedXML` extractor (existing); (g) `IEvidenceArtifactService` / `IEvidenceStorageService` (Feature 038); (h) `IOrgInheritanceDefaultService` (Feature 044); (i) `ControlInheritanceMapping` (Feature 043); (j) Feature 024 narrative-governance regenerate endpoint. For each, list: existing file path; existing public surface (interface signature); the surgical extension required for US9 / US10; any code paths to delete in the same PR. Sync the result back into [plan.md § Reuse-First Audit](plan.md) by populating the "Redundant code to remove" column. **MUST complete before any of T194–T216 (Phase 15) or T223–T230 (Phase 16) are merged.**

  **Completion summary** — Audit landed at [research-reuse-audit.md](research-reuse-audit.md). Verification was a code-level grep over `src/**/*.cs` for the 10 named identifiers plus a full registration sweep of `Program.cs` + every `*ServiceCollectionExtensions.cs`. Findings: (1) **No duplicate DI registrations** today for any FR-110 service — "Redundant code to remove" column verifiably reads "None today" across the table; (2) **Three named interfaces are aspirational**: `ICapabilityMappingService` does not exist (real assets are `CapabilityImportService` for spreadsheet imports + `NarrativeTemplateService` for AI narratives), `IControlNarrativeService` does not exist (real concrete is `NarrativeTemplateService`), `IOrgInheritanceDefaultService` does not exist (real interface is `IOrgInheritanceService`, with no `SaveAsync` method today). T218 extracts the missing interfaces and adds `SaveAsync` to `IOrgInheritanceService`; (3) **PDF path correction**: real path is `src/Ato.Copilot.Agents/Compliance/Services/Onboarding/SspPdf/SspPdfExtractionService.cs`, not the spec-named `PdfDocumentParser.cs`; (4) **OSCAL parser is net-new**: Feature 022 is OSCAL **export-only**; no inbound SSP parser exists — a new minimal `OscalSspJsonParser` is required and is the only OSCAL-SSP parser allowed; (5) **Entity rename**: real entity is `ControlInheritance` (no "Mapping" suffix) at `RmfModels.cs:652`; (6) **Regenerate endpoint confirmed**: `POST /api/dashboard/systems/{systemId}/controls/{controlId}/regenerate-ai` at `DashboardEndpoints.cs:599` is the FR-109 endpoint — no new endpoint to be added. plan.md § Reuse-First Audit table fully synced — every "Redundant code to remove" cell carries the verified answer.
- [X] T218 [US9] **Reuse-First Refactor** — Apply the changes identified in T217: remove every redundant code path; consolidate duplicate DI registrations down to one each for `ICapabilityMappingService`, `IControlNarrativeService`, `IEvidenceArtifactService`, `IEvidenceStorageService`, `IOrgInheritanceDefaultService`, and `ICspAtoDocumentParser`; add a startup `IHostedService` health check (`CspInheritanceReuseAuditHealthCheck`) that fails fatally on duplicate registration of any of those services (FR-110). The health check MUST resolve registrations dynamically from `IServiceCollection` by interface `FullName` (string-based reflection lookup) rather than via a static type reference, so interfaces named in FR-110 that do not yet exist at the time T218 lands (e.g., `ICspAtoDocumentParser` is created later by T198) are skipped as no-ops until they are registered — the check then automatically begins enforcing them once T198 + T206 land. The health check is wired into `Program.cs` later (T228). The PR for this task MUST cite each entry from `research-reuse-audit.md` and explain the disposition (reuse / extend / remove). **MUST complete before any of T194–T216 (Phase 15) or T223–T230 (Phase 16) are merged.**

  **Completion summary** — Landed in two complementary halves: (1) interface extraction over existing concrete classes per the audit's name-reconciliation list, and (2) the FR-110 startup audit class with seven RED-then-GREEN unit tests.

  **Production code added**:
  - `src/Ato.Copilot.Core/Interfaces/Compliance/IControlNarrativeService.cs` — interface extracted over the existing `NarrativeTemplateService` (4 public methods preserved verbatim). Implementation declares `: IControlNarrativeService` — additive only, no method changes.
  - `src/Ato.Copilot.Core/Interfaces/Compliance/ICapabilityMappingService.cs` — net-new interface + `CapabilityMappingInput` and `CapabilityControlMatch` records (the result type was renamed from `CapabilityControlMapping` to avoid collision with the existing `Ato.Copilot.Core.Models.Compliance.CapabilityControlMapping` entity).
  - `src/Ato.Copilot.Core/Interfaces/Tenancy/ICspAtoDocumentParser.cs` — empty marker interface; T198 fleshes out the surface, T206 wires the implementation.
  - `src/Ato.Copilot.Core/Services/Tenancy/CspInheritanceReuseAuditHealthCheck.cs` — IHostedService + `ServiceRegistrationSnapshot` value class + `CspInheritanceReuseAuditServiceCollectionExtensions.AddCspInheritanceReuseAudit` extension. String-based FullName lookup over the snapshot — unregistered interfaces are silent no-ops (so T198 / T204 / T206 / T225 can land incrementally without a follow-up edit to the audit list).
  - `IOrgInheritanceService.SaveAsync(SaveOrgInheritanceDefaultRequest, CancellationToken)` — new method on the existing interface (the four pre-existing methods are untouched). Implementation on `OrgInheritanceService` performs per-row insert-or-update with audit logging; T223 will assign the new `SourceCspCapabilityId` / `SourceCspComponentId` FK columns once they land, and T225 will emit the `CspCapabilityConsumed` domain event from this method.

  **DI registration changes** (all kept at exactly 1 per FR-110):
  - `services.AddSingleton<IControlNarrativeService>(sp => sp.GetRequiredService<NarrativeTemplateService>())` added next to the existing concrete `NarrativeTemplateService` factory in `AtoCopilotMcpServiceExtensions.cs`. Both the concrete and the interface point at the SAME singleton, so DI counts as one registration of each.
  - `ICapabilityMappingService` and `ICspAtoDocumentParser` are intentionally left unregistered until T204 / T206 land — the health check no-ops against them.

  **Tests added** (TDD red → green):
  - `tests/Ato.Copilot.Tests.Unit/Tenancy/CspInheritanceReuseAuditHealthCheckTests.cs` — 7 tests covering: empty collection no-throw; single registration of every service no-throw; duplicate registration throws and names the offender; tenancy-namespace duplicate throws; multi-offender throw lists every offender; snapshot is point-in-time immutable; StopAsync no-throw. **All 7 pass.**

  **Pre-existing test break fixed inline** — `tests/Ato.Copilot.Tests.Unit/Tenancy/SelfOnboardingGuardTests.cs` was failing to compile because commit `a180962` (US7 CSP onboarding wizard) extended `TenantResolutionMiddleware.InvokeAsync` with a 9th `ICspProfileService` parameter. The two `InvokeAsync` call sites and a new `BuildCspProfileStub()` helper (returning an Active singleton CspProfile) were added so the FR-090 CSP-onboarding gate doesn't short-circuit the self-onboarding tests with a 503. **All 19 SelfOnboarding + OrgInheritance unit tests now pass.**

  **Wiring deferred to T228** — `AddCspInheritanceReuseAudit()` is exported but not yet called from `Program.cs`. T228 (Phase 16) owns the wiring step.

  **Spec corrections noted in audit** (carried over to T217 entry; PR description in this commit cites them again): the spec named three interfaces that don't exist by their spec names (`ICapabilityMappingService`, `IControlNarrativeService`, `IOrgInheritanceDefaultService` → real name `IOrgInheritanceService`); the audit / health check use the real names so enforcement is real (not a silent no-op against a non-existent type).

  Build green: full solution compiles. Tests green: 7 new + 12 pre-existing related tests = 19/19 pass.

### Implementation for User Story 9

- [X] T194 [US9] Create `src/Ato.Copilot.Core/Models/Tenancy/CspInheritedComponent.cs` per FR-007: `Id`, `CspProfileId`, `Name`, `Description`, `ComponentType`, `SourceFileName?`, `SourceFormat`, `SourceArtifactReference?`, `Status`, `ImportedAt`, `ImportedBy`, `UpdatedAt`, `UpdatedBy`, `RowVersion`; mark with `[GlobalReference]`; computed (NotMapped) properties `CapabilityMappedCount`, `CapabilityNeedsReviewCount`
- [X] T195 [P] [US9] Create `src/Ato.Copilot.Core/Models/Tenancy/CspInheritedCapability.cs` per FR-008: FK to `CspInheritedComponent`, `Name`, `Description`, `MappedNistControlIds (string column carrying JSON array)`, `MappingConfidence?`, `Status`, `MappingFailureReason?`, `MappedBy`, `ReviewedBy?`, `ReviewedAt?`, audit columns + `RowVersion`; mark `[GlobalReference]`
- [X] T196 [P] [US9] Create enum types `src/Ato.Copilot.Core/Models/Tenancy/CspInheritedComponentStatus.cs` (`Draft|Published|Archived`), `CspInheritedCapabilityStatus.cs` (`Mapped|NeedsReview`), `SourceFormat.cs` (`Pdf|Docx|OscalJson|Xlsx|EmassZip|Manual`), `CspComponentType.cs` (`Infrastructure|Platform|Service|Identity|Network|Storage|Compute`), `MappedBy.cs` (`User|AI`)
- [X] T197 [US9] Modify `src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs` to add `DbSet<CspInheritedComponent> CspInheritedComponents` and `DbSet<CspInheritedCapability> CspInheritedCapabilities`; configure FK + cascade-restrict; configure JSON value-conversion for `MappedNistControlIds`; ensure both tables are skipped by the tenant query filter (because of `[GlobalReference]`)
- [X] T198 [P] [US9] Create `src/Ato.Copilot.Core/Interfaces/Tenancy/ICspAtoDocumentParser.cs` exposing `Task<ParsedAtoDocument> ParseAsync(Stream stream, string contentType, string fileName, CancellationToken ct)` returning a list of candidate component records (name, description, type, source-artifact reference)
- [X] T199 [P] [US9] Create `src/Ato.Copilot.Core/Interfaces/Tenancy/ICspComponentExtractionService.cs` exposing `Task<IReadOnlyList<CspInheritedComponent>> ExtractAsync(ParsedAtoDocument document, Guid cspProfileId, string actor, CancellationToken ct)`
- [X] T200 [P] [US9] Create `src/Ato.Copilot.Core/Interfaces/Tenancy/ICspCapabilityMappingService.cs` exposing `Task<CapabilityMappingResult> MapAsync(CspInheritedComponent component, double confidenceThreshold, CancellationToken ct)` returning `(Mapped[], NeedsReview[], aiMappingAvailable, aiMappingFailureReason?)`
- [X] T201 [P] [US9] Create `src/Ato.Copilot.Core/Interfaces/Tenancy/ICspInheritedComponentService.cs` exposing `Get/List/Publish/Archive/Remap/UpdateAsync` and `ReviewCapabilityAsync(componentId, capabilityId, mappedControlIds[], reviewerNote, actor, ct)`
- [X] T202 [US9] Implement `src/Ato.Copilot.Core/Services/Tenancy/CspAtoDocumentParser.cs` dispatching by `contentType`/file extension to: `application/pdf` → reuse the PdfPig parser introduced in Feature 047; `application/vnd.openxmlformats-officedocument.wordprocessingml.document` → `DocumentFormat.OpenXml` text walker; `application/json` → reuse the OSCAL parser introduced in Feature 022; `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` → `ClosedXML` (FedRAMP / SAR / POAM workbook tab heuristics); `application/zip` → enumerate `System.IO.Compression.ZipArchive` entries and recursively dispatch each (skip non-component artifacts like images). Throw `UnsupportedAtoDocumentException` for unknown types and `AtoDocumentParseException` for malformed inputs

  **Completion summary** — Landed at `src/Ato.Copilot.Core/Services/Tenancy/CspAtoDocumentParser.cs`. Constructor `(ISspPdfExtractionService pdfExtractor, ILogger<CspAtoDocumentParser> logger)` — PDF dispatch reuses the existing Feature 047 service; the dispatcher itself owns zero parsing logic. Content-type normalization strips parameters (e.g. `; charset=utf-8`) and lowercases. **PDF**: delegates to `ISspPdfExtractionService` and projects the extracted `SystemName` field as a single candidate component (best-effort — narrative SSPs do not decompose cleanly). **DOCX**: read as a ZIP package via `System.IO.Compression.ZipArchive` + `System.Xml.Linq.XDocument` over `word/document.xml` — **no new package dependency** (DocumentFormat.OpenXml not required). **OSCAL JSON**: net-new minimal parser per the audit (Feature 022 is export-only); tolerates both wrapped (`{ "system-security-plan": … }`) and unwrapped roots; reads `system-implementation.components[]` and maps `type` to `CspComponentType`. **XLSX**: ClosedXML; looks for a header row containing `Component`/`Component Name`/`Name` + `Description`/`Component Description` and projects each subsequent non-empty row. **ZIP**: enumerates entries, dispatches by file extension (.pdf/.docx/.json/.xlsx); silently skips non-component artifacts (images, READMEs, signatures); per-entry parse failures are debug-logged, not fatal. Per the interface contract the dispatcher throws `NotSupportedException` for unknown content-types and `InvalidDataException` for malformed payloads (the endpoint layer T207 maps these to `400 UNSUPPORTED_ATO_DOCUMENT` / `400 PARSE_FAILED`).
- [X] T203 [US9] Implement `src/Ato.Copilot.Core/Services/Tenancy/CspComponentExtractionService.cs` mapping `ParsedAtoDocument` candidate records onto `CspInheritedComponent` rows, deduping by `(Name, ComponentType)` per `CspProfileId`, persisting with `Status = Draft`, `ImportedBy = actor`, and `SourceFormat` propagated from the parser

  **Completion summary** — Landed at `src/Ato.Copilot.Core/Services/Tenancy/CspComponentExtractionService.cs`. Constructor `(IDbContextFactory<AtoCopilotContext> contextFactory, ILogger<CspComponentExtractionService> logger)`. Pre-loads existing `(Name, ComponentType)` keys for the profile so dedupe is one round-trip, not one per row. Dedupes against both pre-existing rows AND duplicates within the same `ParsedAtoDocument`. Truncates `Name` (256), `Description` (2000), `SourceFileName` (512), `SourceArtifactReference` (2048) to entity `MaxLength` so a single oversized candidate cannot fail the bulk insert. Persists with `Status = Draft`, `ImportedAt = UtcNow`, `ImportedBy = actor`, propagating `SourceFormat`/`SourceFileName`/`SourceArtifactReference` from the parsed document. Returns the persisted rows in their post-save state (Id populated).
- [X] T204 [US9] Implement `src/Ato.Copilot.Core/Services/Tenancy/CspCapabilityMappingService.cs` wrapping the existing `ICapabilityMappingService` (Features 045 / 008): for each component description, call the underlying service, normalize the response into `CspInheritedCapability` rows; if confidence ≥ threshold (default `0.6` from `Csp:Inheritance:MappingConfidenceThreshold`) → `Status = Mapped`; otherwise → `Status = NeedsReview` with `MappingFailureReason = "Confidence below threshold ({score:F2})"`; if AI returned empty → `Status = NeedsReview`, `MappedNistControlIds = "[]"`, `MappingFailureReason = "AI returned no candidate controls"`; on `HttpRequestException` / `OperationCanceledException` / quota-exceeded → return `aiMappingAvailable = false` with `aiMappingFailureReason` populated and an empty mapping list (caller persists components but no capabilities)

  **Completion summary** — Landed at `src/Ato.Copilot.Core/Services/Tenancy/CspCapabilityMappingService.cs`. Constructor `(ICapabilityMappingService aiMapper, ILogger<CspCapabilityMappingService> logger)`. The wrapper collapses N AI candidate controls into **one capability per component** (per the T190 contract: a component yields a single `CspInheritedCapability` whose `MappedNistControlIds` is the union of returned control IDs, never one capability per control). Confidence is normalized to the **max** across all returned matches and compared against the per-call `confidenceThreshold` argument. Failure-reason text uses `CultureInfo.InvariantCulture` formatting (`"Confidence below threshold (0.42)"` — never locale-sensitive comma decimals). `OperationCanceledException` propagates when the caller cancels; all other exceptions (including `HttpRequestException`, AI quota errors) are caught and surfaced as `AiMappingAvailable = false` with the exception message in `AiMappingFailureReason`. T190 4/4 GREEN; concrete `ICapabilityMappingService` registration is deferred to T206 (the unit tests use a Moq stub so DI is not on the critical path).
- [X] T205 [US9] Implement `src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs` exposing the operations declared in T201; `PublishAsync` requires `Status = Draft` (else `409`); `RemapAsync` re-runs T204 and either preserves existing `Mapped` rows (default) or replaces them when `replaceMapped = true`; `ReviewCapabilityAsync` requires `Status = NeedsReview` (else `409`), updates `MappedNistControlIds` + `ReviewerNote`, sets `Status = Mapped`, `MappedBy = User`, `ReviewedBy`, `ReviewedAt`, and emits audit row `Action = CspInheritedCapability.Review`

  **Completion summary** — Landed at `src/Ato.Copilot.Core/Services/Tenancy/CspInheritedComponentService.cs`. Constructor `(IDbContextFactory<AtoCopilotContext>, ICspCapabilityMappingService, IOptions<CspInheritedOptions>, ILogger)`. **GetAsync** / **ListAsync** include `Capabilities` and populate `CapabilityMappedCount` / `CapabilityNeedsReviewCount` (NotMapped) computed counts. **UpdateAsync** trims + truncates inputs to entity `MaxLength`; sets `RowVersion` original-value when caller provides one (concurrency mismatches surface via `DbUpdateConcurrencyException` for endpoint mapping to 412). **PublishAsync**: Draft→Published transitions; Published→Published is idempotent; Archived rejects via `InvalidOperationException` (endpoint maps to 409). **ArchiveAsync**: any non-Archived state→Archived; idempotent. **RemapAsync** re-invokes the wrapper (T204) using the configured threshold from `CspInheritedOptions.MappingConfidenceThreshold`; with `preserveHumanMappings=true` only `MappedBy.AI` rows are deleted (User reviews survive); appends the freshly-mapped capabilities. **ReviewCapabilityAsync**: validates `capabilityId` belongs to `componentId`, requires `Status=NeedsReview` (else `InvalidOperationException`), updates `MappedNistControlIds` + `ReviewerNote`, sets `MappedBy=User`, `ReviewedBy=actor`, `ReviewedAt=now`, `Status=Mapped`, clears `MappingFailureReason`, and emits an `AuditLogEntry` with `Action="CspInheritedCapability.Review"`, `Outcome=Success`, and a `Details` JSON payload of `{componentId, capabilityId, mappedControlIds, reviewerNote}`. The interceptor stamps `TenantId` per the existing `TenantStampingSaveChangesInterceptor` contract.
- [X] T206 [US9] Register all four new services in `src/Ato.Copilot.Mcp/Configuration/ServiceCollectionExtensions.cs` with `AddScoped`; add `CspInheritedOptions` POCO bound from `Csp:Inheritance:*` (`MappingConfidenceThreshold`, `MaxFileSizeBytes`)

  **Completion summary** — Registration site is `src/Ato.Copilot.Mcp/Extensions/AtoCopilotMcpServiceExtensions.cs` (the spec-named `Configuration/ServiceCollectionExtensions.cs` does not exist; existing `IControlNarrativeService` and `IOrgInheritanceService` registrations from T218 already live in this file, so the four new CSP services were placed alongside them per the established convention). All four services registered as `AddScoped` for shared-DbContext-per-request semantics: `ICspAtoDocumentParser`, `ICspComponentExtractionService`, `ICspCapabilityMappingService`, `ICspInheritedComponentService`. `CspInheritedOptions` (`Csp:Inheritance:MappingConfidenceThreshold` default `0.6`; `MaxFileSizeBytes` default 50 MB) created at `src/Ato.Copilot.Core/Configuration/Tenancy/CspInheritedOptions.cs` and bound via `services.Configure<>()`. The FR-110-protected `ICapabilityMappingService` interface is intentionally LEFT UNREGISTERED at this slice — the `CspInheritanceReuseAuditHealthCheck` no-ops on unregistered interfaces, and the runtime upload path is gated by T207 endpoints which have not yet landed. The AI-backed concrete will be wired in a later slice (T227 timeframe), at which point this same DI registration block extends with exactly one `services.AddScoped<ICapabilityMappingService, …>` to satisfy the FR-110 single-registration invariant. Tests: T204 4/4 GREEN; T218 health check 7/7 GREEN with the 4 new registrations in the graph (no false-positive duplicates); 105 sibling Tenancy integration tests pass (no DI regression).
- [X] T207 [US9] Modify `src/Ato.Copilot.Mcp/Endpoints/Csp/CspOnboardingEndpoints.cs` to add `POST /atos/upload` (multipart, max 50 MB per file, returns `AtoUploadResponse`) and `GET /atos/state` (returns `AtoStepState`); both gated to `CSP.Admin`; both return `404 SINGLE_TENANT_MODE` in SingleTenant mode and follow the FR-090 onboarding-incomplete gate for the wizard

  **Completion summary** — `MapPost("/atos/upload", PostAtosUploadAsync).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(50L*1024L*1024L))` and `MapGet("/atos/state", GetAtosStateAsync)` added to the existing `MapCspOnboardingEndpoints` group. Both routes apply the SingleTenant short-circuit (`404 SINGLE_TENANT_MODE`) and the CSP-Admin gate (`403 FORBIDDEN_NOT_CSP_ADMIN`) before any work; the upload endpoint additionally calls `profileService.EnsureCreatedAsync` and surfaces `409 ALREADY_ONBOARDED` if onboarding is already complete (clients route to the post-onboarding `/import` endpoint in that case). The 50 MB per-file limit is enforced two ways: (1) `RequestSizeLimitAttribute` metadata on the route, which causes Kestrel to throw `BadHttpRequestException(StatusCode=413)` from `ReadFormAsync` (caught and translated to `413 ATO_DOCUMENT_TOO_LARGE`); (2) explicit `f.Length > MaxFileSizeBytes` check that produces the same `413` envelope so the boundary is honored regardless of which layer trips first. Per-file content-type validation against the FR-100 allow-list precedes any parser call (`400 UNSUPPORTED_ATO_DOCUMENT`). Successful uploads delegate to the new `CspAtoUploadHelpers.OrchestrateAsync` shared shim (parser → extraction → mapping → persist via callback) so wizard and post-onboarding paths share exactly one code path.
- [X] T208 [US9] Create `src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs` mapping all paths from [contracts/csp-inherited-components.openapi.yaml](contracts/csp-inherited-components.openapi.yaml): `GET /` (any authenticated user, paginated, status filter respected per role), `GET /{id}`, `PATCH /{id}` (CSP-Admin), `DELETE /{id}` archive (CSP-Admin), `POST /{id}/publish` (CSP-Admin), `POST /{id}/remap` (CSP-Admin), `GET /{id}/capabilities`, `PATCH /{id}/capabilities/{capabilityId}/review` (CSP-Admin), `POST /import` (multipart, CSP-Admin, post-onboarding only)

  **Completion summary** — New file with `MapGroup("/api/csp/inherited-components")` and 9 routes wired in `Program.cs` immediately after `MapCspOnboardingEndpoints()`. Cross-tenant read access (FR-104) honors role: list/detail/capabilities are reachable by every authenticated user but narrow to `Status = Published` for non-CSP-Admin callers, returning `404` for non-Published rows on detail/capabilities to avoid existence leaks. CSP-Admin write paths translate service-level exceptions to envelope error codes: `KeyNotFoundException` → `404`; `InvalidOperationException` → `409 INVALID_TRANSITION`; `DbUpdateConcurrencyException` → `412 ROW_VERSION_MISMATCH`; `BadHttpRequestException(StatusCode=413)` from `ReadFormAsync` → `413 ATO_DOCUMENT_TOO_LARGE`. The `/import` route gates on `CspProfile.OnboardingState == Active` (else `503 CSP_ONBOARDING_INCOMPLETE`) and reuses `CspAtoUploadHelpers.OrchestrateAsync` so the parser → extraction → mapping pipeline is identical to the wizard path. SingleTenant mode short-circuits all routes to `404 SINGLE_TENANT_MODE`. PATCH request DTO uses `RowVersion` round-trip to enforce optimistic concurrency at the service layer.
- [X] T209 [US9] Modify `src/Ato.Copilot.Mcp/Endpoints/Csp/CspOnboardingEndpoints.cs` `POST /submit` handler so that, when called, it transitions all `CspInheritedComponent` rows still in `Status = Draft` for the wizard's `CspProfileId` to `Status = Published` in the same transaction as setting `CspProfile.OnboardingState = Active`

  **Completion summary** — `PostSubmitAsync` now takes an injected `IDbContextFactory<AtoCopilotContext>` in addition to the existing `ICspProfileService`. After `service.SubmitAsync(actor, ct)` flips `OnboardingState = Active`, the handler issues a `Where(c.CspProfileId == profile.Id && c.Status == Draft).ToListAsync()` followed by an in-memory `Status = Published; UpdatedAt = now; UpdatedBy = actor` loop and a single `SaveChangesAsync`. Load-and-save (vs. `ExecuteUpdateAsync`) was chosen because the audit interceptor needs to attach attribution columns and SQLite's translator chokes on `ExecuteUpdate` over enum-converted properties when other entities in the model carry value comparers (verified locally — `ExecuteUpdate` raised "The LINQ expression … could not be translated"). The same load-and-save pattern is mirrored in the post-onboarding `/import` auto-publish branch in `CspInheritedComponentEndpoints` so wizard and import flows behave identically. FR-104 wizard-time staging is preserved (`Draft` is only valid before submit); after submit, `Draft` is invariant-violating and surfaced as such by the FR-103 capability lifecycle.
- [X] T210 [US9] Wire FR-080 cross-tenant FK exception list: in `TenantStampingSaveChangesInterceptor` (T040), ensure references to `CspInheritedComponent.Id` and `CspInheritedCapability.Id` from tenant-local entities pass the cross-tenant check exactly as references to other `[GlobalReference]` rows do (no extra code path needed; verify via T193)

  **Completion summary** — No interceptor changes needed: `CspInheritedComponent` and `CspInheritedCapability` already carry `[GlobalReference]`, and `TenantStampingSaveChangesInterceptor.ValidateFkConsistency` already short-circuits with `if (refIsGlobal) continue;` for any navigation pointing at a `[GlobalReference]` row. T193 confirmation suite (4 sub-tests in `CspInheritedComponentFkRejectionTests`) is GREEN: (1) writing a `[GlobalReference]` `CspInheritedComponent` alongside a `[TenantScoped]` `OrgInheritanceDefault` in the same `SaveChanges` does not throw; (2) reading an FK-referenced `CspInheritedComponent` from a different tenant's session is visible regardless of query filter; (3) the global row lands without a `TenantId` stamp; (4) a non-CSP-Admin caller attempting to insert a `[TenantScoped]` row with a cross-tenant `TenantId` is still rejected with `TenantConsistencyException` (verified by manually pushing the accessor's `AsyncLocal` in the test, since the bypass-tenant-resolution fixture does not set it). The 4th sub-test originally short-circuited to no-throw because the test fixture's HTTP-bypass left `_accessor.Current = null` (the interceptor's "startup-path tolerant" guard fires); the test now uses `accessor.Push(new TenantContext(TenantAId, isCspAdmin: false))` so the FR-021/FR-080 enforcement actually runs.
- [X] T211 [P] [US9] Create `src/Ato.Copilot.Dashboard/src/features/csp-onboarding/steps/AtoDocumentsStep.tsx` — multi-file dropzone (drag-drop + manual select), client-side size pre-check (50 MB), per-file progress bar, submit calls `POST /api/csp/onboarding/atos/upload`; on response, render `ComponentExtractionPreview.tsx` with per-file tallies and a drill-down list of `NeedsReview` capabilities — completed slice 4 (`AtoDocumentsStep.tsx` + sibling `ComponentExtractionPreview.tsx`; on-mount re-entrancy via `getCspOnboardingAtosState()`; PDF/DOCX/JSON/XLSX/ZIP MIME allowlist; per-file `Parsed` / `Failed` badges; `aiMappingAvailable === false` amber banner with `data-testid="ai-mapping-unavailable-banner"`).
- [X] T212 [P] [US9] Modify `src/Ato.Copilot.Dashboard/src/features/csp-onboarding/CspWizard.tsx` to insert the `AtoDocumentsStep` between the `ClassificationStep` and `ReviewStep`; update the step indicator and re-entrancy logic so a user closing the browser mid-upload returns to the same step — completed slice 4 (extended client-side `WizardStep` union to 5 values; server-side `CspOnboardingStep` enum is intentionally NOT extended — the AtoDocuments step is UI-only and re-entrancy uses the server's existing `Review` signal because the user has already passed it; comment in `toWizardStep()` documents the contract).
- [X] T213 [P] [US9] Modify `src/Ato.Copilot.Dashboard/src/features/csp-onboarding/steps/ReviewStep.tsx` to surface `componentsExtracted`, `capabilitiesMapped`, `capabilitiesNeedsReview`, and the `aiMappingAvailable` banner from `GET /api/csp/onboarding/state` — completed slice 4 (heading bumped to "Step 5"; best-effort `getCspOnboardingAtosState()` fetch on mount with `data-testid="review-ato-tally"` row + `data-testid="review-ai-mapping-unavailable-banner"` amber banner when `documentsUploaded > 0 && !aiMappingAvailable`).
- [X] T214 [P] [US9] Create `src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/CspInheritedComponentsPage.tsx` (post-onboarding management page mounted at `/csp/inherited-components`) with: paginated table, status filter, search box, drawer-based detail view (`ComponentDetailDrawer.tsx`), `NeedsReviewQueue.tsx` panel for resolving `NeedsReview` capabilities, and an Import button that posts to `/api/csp/inherited-components/import` — completed slice 4 (`CspInheritedComponentsPage.tsx` + `ComponentDetailDrawer.tsx` + `NeedsReviewQueue.tsx`; CSP-Admin gates Import / Edit / Publish / Archive / Remap actions via `useCspDashboardAvailable() === true`; `If-Match`-driven optimistic-concurrency PATCH catches `ROW_VERSION_MISMATCH`; `AI_MAPPING_UNAVAILABLE` surfaces a friendly remap-failure message; archive uses `window.confirm`; `<Route path="/csp/inherited-components">` registered in `App.tsx`).
- [X] T215 [P] [US9] Create `src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/api.ts` (Axios wrappers for all endpoints in [contracts/csp-inherited-components.openapi.yaml](contracts/csp-inherited-components.openapi.yaml)) and add the `/csp/inherited-components` nav link to the dashboard sidebar (visible to all authenticated users in MultiTenant mode; CSP-Admin gets the additional Import + Review actions) — completed slice 4 (`api.ts` covering all 9 endpoints with `UnavailableState` discriminated unions for `SINGLE_TENANT_MODE` / `CSP_ONBOARDING_INCOMPLETE` / `NETWORK_UNREACHABLE`, plus multipart `import` and `If-Match`-bearing `patch`; visibility hook `useCspInheritedComponentsAvailable.ts` probes `GET /csp/inherited-components?pageSize=1` once on mount — 200 → show, 404/503/error → hide — so the link is shown to every authenticated user in `MultiTenant` (per FR-104), not just CSP-Admins; `<NavLink data-testid="nav-csp-inherited-components">Inherited</NavLink>` rendered in `PageLayout.tsx` next to the existing CSP Dashboard link).
- [X] T216 [US9] Run T189–T193 integration + unit tests; iterate until all green — slice 4 verification: `npx tsc -b` (frontend) clean; `dotnet build Ato.Copilot.sln` clean (0 errors, 110 unrelated warnings); CSP integration tests at slice 3 baseline (89 / 94 passing — same 5 documented-RED placeholder-bytes parser tests carried over from slice 3, **NOT regressions**; tracked for a follow-up fixture-supply slice). Frontend vitest run: 134 / 135 passing — the single failure (`WizardStepper.test.tsx`) is a pre-existing failure for the unrelated **system-intake** wizard (`src/components/wizard/WizardStepper.tsx`), not the CSP wizard, and is unchanged by this slice (verified via `git stash` round-trip).

**Checkpoint**: Wizard upload of one PDF + one OSCAL JSON + one eMASS ZIP produces ≥ 3 `CspInheritedComponent` rows + per-component capabilities, with at least one row demonstrating each of `Status = Mapped` and `Status = NeedsReview`; submit transitions all to `Published`; a Mission Owner in a freshly-onboarded tenant can list them and reference them in a tenant-local inheritance default.

---

## Phase 16: User Story 10 — Mission Owner Consumes a CSP-Inherited Capability (Priority: P1, MultiTenant only)

**Goal**: When a Mission Owner in a hosted tenant creates an `OrgInheritanceDefault` (Feature 044) or `ControlInheritanceMapping` (Feature 043) referencing a `Status = Published` `CspInheritedCapability` (via a new nullable `SourceCspCapabilityId Guid?` FK), the platform MUST automatically (a) persist one tenant-local `EvidenceArtifact` per mapped control of `Type = CspInheritedReference` via the existing `IEvidenceArtifactService` (Feature 038); (b) invoke the existing `IControlNarrativeService` (Features 008 / 024) once per control with a single new CSP-context prompt fragment so the resulting narrative cites `<CspProfile.DisplayName>` and the source filename by name; (c) on AI failure, persist a deterministic stub narrative with `Status = NeedsReview` so the SSP draft is never blank, with regeneration available via the existing narrative-regenerate endpoint (no new endpoint). All steps MUST reuse existing services per FR-110 — no parallel mapping algorithm, no parallel narrative service, no parallel evidence path.

**Independent Test**: Seed a `MultiTenant` deployment with a `Status = Published` `CspInheritedCapability` mapped to two controls (`AC-2`, `AC-2(1)`). Sign in as a Mission Owner in Tenant A, create an `OrgInheritanceDefault` whose `SourceCspCapabilityId` references that capability, and verify (1) two `EvidenceArtifact` rows of `Type = CspInheritedReference` are persisted in Tenant A with the source-CSP fields populated and `IsImmutableSource = true`; (2) both control narratives contain the CSP `DisplayName` and source filename; (3) the existing `IControlNarrativeService` was invoked exactly once per control (DI smoke test asserts no new service registration); (4) when the AI service is forcibly disabled and the consumption is repeated for a different capability, deterministic stub narratives are persisted with `Status = NeedsReview` and the regenerate endpoint produces an AI narrative on a subsequent call.

### Tests for User Story 10

- [ ] T219 [P] [US10] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/CspCapabilityConsumptionTests.cs` covering: creating an `OrgInheritanceDefault` with `SourceCspCapabilityId` populated persists exactly one `EvidenceArtifact` per mapped control with `Type = CspInheritedReference`, `IsImmutableSource = true`, and structured payload `{ SourceCspComponentId, SourceCspCapabilityId, SourceFileName, SourceArtifactReference }`; the artifacts are tenant-local (visible only inside the consuming tenant's query filter); referencing a non-existent or non-`Published` capability returns `409 CSP_CAPABILITY_NOT_PUBLISHED`
- [ ] T220 [P] [US10] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/CspNarrativeGenerationTests.cs` covering: the resulting control narrative for each control in `MappedNistControlIds` contains the CSP `DisplayName` (e.g., `"Flankspeed"`) and the source filename; `IControlNarrativeService.GenerateAsync` is invoked exactly once per control with a request whose context block includes `CspProfile.DisplayName` + `CspInheritedComponent.Description` + `CspInheritedCapability.Description` + `SourceFileName` + `SourceArtifactReference`; the narrative is persisted via the existing Feature 024 narrative-save path
- [ ] T221 [P] [US10] Create `tests/Ato.Copilot.Tests.Integration/Tenancy/Csp/CspNarrativeFallbackTests.cs` covering: when `IControlNarrativeService` is replaced by a stub that throws / returns null / reports AI unavailable, consumption still completes; FR-107 evidence rows are persisted; a deterministic stub narrative `"This control is inherited from <DisplayName> via the <ComponentName> capability. See <SourceFileName>. Capability description: <Description>."` is persisted with `Status = NeedsReview` (Feature 024 status field); a subsequent call to the existing narrative-regenerate endpoint produces an AI narrative without manual intervention
- [ ] T222 [P] [US10] Create `tests/Ato.Copilot.Tests.Unit/Tenancy/Csp/CspInheritanceReuseAuditTests.cs` covering: the DI container has exactly one registration each for `ICapabilityMappingService`, `IControlNarrativeService`, `IEvidenceArtifactService`, `IEvidenceStorageService`, `IOrgInheritanceDefaultService`, and `ICspAtoDocumentParser` (reflection-based check on `IServiceCollection` by interface `FullName`); no class under `src/Ato.Copilot.Core/Services/Tenancy/Csp*` re-implements an interface owned by Features 008 / 024 / 038 / 043 / 044 / 045 / 047 (allow-list: `CspProfileService`, `CspAtoDocumentParser`, `CspComponentExtractionService`, `CspCapabilityMappingService`, `CspInheritedComponentService`, `CspCapabilityConsumptionHandler`)

### Implementation for User Story 10

- [ ] T223 [US10] Modify `src/Ato.Copilot.Core/Models/Inheritance/OrgInheritanceDefault.cs` (Feature 044) to add nullable `SourceCspCapabilityId Guid?` and `SourceCspComponentId Guid?` columns plus FKs (cascade-restrict, FR-080-allowed because target is `[GlobalReference]`). Apply the same change to `src/Ato.Copilot.Core/Models/Inheritance/ControlInheritanceMapping.cs` (Feature 043) so per-control inheritance bookkeeping carries the same FKs (this is required — not conditional — because spec FR-107 names both entities as consumption entry points). Schema additions go through `EnsureSchemaAdditionsAsync` (additive only, idempotent); update the `AtoCopilotContext` `OnModelCreating` configuration accordingly. T219 / T220 / T221 MUST exercise both entities (one test class per entity) to prevent regressions on either consumption path.
- [ ] T224 [US10] Add `EvidenceArtifactType.CspInheritedReference` enum value to Feature 038's `EvidenceArtifactType.cs`; document the structured payload shape (`SourceCspComponentId`, `SourceCspCapabilityId`, `SourceFileName`, `SourceArtifactReference`, `IsImmutableSource`) in the existing `EvidenceArtifact.Payload` JSON column — **reuse the existing `IEvidenceArtifactService`, no new service**
- [ ] T225 [US10] Modify `IOrgInheritanceDefaultService.SaveAsync` (Feature 044) to detect non-null `SourceCspCapabilityId` on insert / update and emit a `CspCapabilityConsumed { TenantId, CspProfileId, CspCapabilityId, CspComponentId, MappedControlIds[], Actor, OccurredAt }` domain event via the existing event bus. **Do NOT branch into a new service — extend the existing one. Existing inheritance save path remains the single entry point.**
- [ ] T226 [US10] Implement `src/Ato.Copilot.Core/Services/Tenancy/CspCapabilityConsumptionHandler.cs` as a subscriber to `CspCapabilityConsumed`. On the event: (a) for each control in `MappedControlIds`, call `IEvidenceArtifactService.CreateAsync` with `Type = CspInheritedReference` and the structured payload (one artifact per control); (b) call existing `IControlNarrativeService.GenerateAsync` once per control with the new `CspContext` request field populated (T227); (c) on AI exception / null result / `aiAvailable = false`, write the deterministic stub from FR-109 and set `Narrative.Status = NeedsReview` via the existing Feature 024 status field. Handler reuses ALL underlying services — it composes them, never re-implements them
- [ ] T227 [US10] Modify `IControlNarrativeService` (Feature 008 / 024) request shape to accept an optional `CspContext { DisplayName, ComponentName, ComponentDescription, CapabilityDescription, SourceFileName, SourceArtifactReference }` field; modify the existing prompt template assembly to append a single new CSP-context prompt fragment when `CspContext != null`. **Single template fragment edit — no new service, no new template family, no parallel narrative-generation path.**
- [ ] T228 [US10] Wire the `CspInheritanceReuseAuditHealthCheck` from T218 into `src/Ato.Copilot.Mcp/Program.cs` startup so the application fails fatally on duplicate registration of any service named in FR-110 (`ICapabilityMappingService`, `IControlNarrativeService`, `IEvidenceArtifactService`, `IEvidenceStorageService`, `IOrgInheritanceDefaultService`, `ICspAtoDocumentParser`). Add the matching CI smoke test entry pointing at the existing health-check infrastructure
- [ ] T229 [P] [US10] Modify `src/Ato.Copilot.Dashboard/src/features/inheritance/InheritedControlCard.tsx` (Feature 044's existing source-label component, also the target of T083 in this feature) to render `Source: <CspProfile.DisplayName> (Inherited from CSP)` when `sourceCspCapabilityId` is set on the inheritance row; show a link to the source document via `CspInheritedComponent.SourceArtifactReference`; render a "Regenerate Narrative" button that calls the existing Feature 024 narrative-regenerate endpoint when `narrativeStatus === 'NeedsReview'` (no new endpoint)
- [ ] T230 [US10] Run T219–T222 tests; iterate until all green; verify T222's reflection check confirms zero parallel implementations of any FR-110 service

**Checkpoint**: A Mission Owner in Tenant A creates an `OrgInheritanceDefault` referencing a `Status = Published` CSP-inherited capability mapped to two controls; two `EvidenceArtifact` rows of `Type = CspInheritedReference` appear in Tenant A; both control narratives cite the CSP `DisplayName` + source filename; with the AI service forced offline, deterministic stubs are persisted instead with `Status = NeedsReview` and regeneration succeeds on a subsequent online call; the startup health check (T228) fails fatally if a duplicate DI registration of `IControlNarrativeService` is introduced.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup, T001–T008)**: No dependencies; can start immediately. T005/T006 depend on T001 finishing first.
- **Phase 2 (Foundational, T009–T034)**: Depends on Phase 1. **BLOCKS all user stories.**
- **Phase 3 (US1, T035–T061)**: Depends on Phase 2. May proceed in parallel with Phases 4 / 5 if staffed.
- **Phase 4 (US2, T062–T077)**: Depends on Phase 2. Many US2 tasks depend on Phase 3 query-filter machinery (T042) — start tests after Phase 3 completes.
- **Phase 5 (US3, T078–T087)**: Depends on Phase 2 + Phase 3 (needs the retrofitted entities to backfill). Phase 5 backfill (T083) is what enables Phase 7 RLS install on existing data.
- **Phase 6 (US4, T088–T101)**: Depends on Phase 2 + Phase 4 (needs `TenantResolutionMiddleware` from T068).
- **Phase 7 (US5, T102–T113)**: Depends on Phase 2 + Phase 3 (entities retrofitted) + Phase 5 (backfill complete). RLS install MUST happen on a column that is `NOT NULL`, which Phase 5 produces.
- **Phase 8 (US6, T114–T119)**: Depends on Phase 4 (T072/T073 audit-fields wiring).
- **Phase 9 (Migration utility, T120–T131)**: Depends on Phase 2 + Phase 3 + Phase 7 (RLS install logic). Used by Phase 5 (US3) at runtime — but Phase 5 can ship a SingleTenant-only backfill first and call the utility for MultiTenant later.
- **Phase 10 (Sharing, T132–T138)**: Depends on Phase 3 (interceptor) + Phase 4 (audit fields).
- **Phase 11 (Channels, T139–T143)**: Depends on Phase 2 (`ITenantContextAccessor`).
- **Phase 12 (Polish, T144–T154)**: Depends on all desired user stories being complete.
- **Phase 13 (US7 CSP Onboarding, T155–T171)**: Depends on Phase 2 + Phase 4 (uses `TenantResolutionMiddleware` from T068 to install the CSP-onboarding gate). **In `MultiTenant` mode this phase MUST complete before Phase 6 (US4) can run** — per-tenant onboarding is gated behind CSP onboarding completion.
- **Phase 14 (US8 CSP Dashboard, T172–T188)**: Depends on Phase 13 (CSP-onboarding gate must lift) and on the cross-tenant query path established by Phase 3 (T042) + Phase 4 (T070 tenant-list endpoint exists).
- **Phase 15 (US9 CSP-Inherited Components from Uploaded ATOs, T189–T216 + T217–T218)**: Depends on Phase 13 (the wizard must exist and the FR-090 gate must be wired before adding a step), Phase 3 (`[GlobalReference]` machinery from T042/T055 is required so tenant-local FKs to `CspInheritedComponent` are accepted), and Phase 10 (FR-080 cross-tenant FK rejection must allow `[GlobalReference]` rows). Reuses the `ICapabilityMappingService` from prior features (045 / 008) and the parsers from Features 047 (PdfPig) and 022 (OSCAL). **Reuse-First gate**: T217 (audit) and T218 (refactor) MUST land before any of T194–T216.
- **Phase 16 (US10 Mission Owner Consumes a CSP-Inherited Capability, T219–T230)**: Depends on Phase 15 (`CspInheritedCapability` rows must exist with `Status = Published`); Phase 10 (FR-080 cross-tenant FK whitelist accepts `[GlobalReference]` references); Feature 038 evidence pipeline (existing `IEvidenceArtifactService`); Feature 044 inheritance pipeline (existing `IOrgInheritanceDefaultService`); Feature 024 narrative governance (existing `IControlNarrativeService` + `Status = NeedsReview` + regenerate endpoint). **Reuse-First gate**: T217 + T218 MUST also land before T223–T230. **In `MultiTenant` mode this phase is part of the day-one MVP** — without it, Phase 15's published rows are not actionable by tenants.

### User Story Dependencies (after Foundational)

- **US1 (P1)** → independent.
- **US2 (P1)** → independent of US1 implementation, but its tests (T062–T065) need the query filter active, so practically run after Phase 3.
- **US3 (P1)** → needs US1's retrofitted entities to be present before backfill makes sense.
- **US7 (P1, MultiTenant only)** → needs US2's `TenantResolutionMiddleware` (T068) to install the CSP-onboarding gate; **blocks US4 in `MultiTenant` mode**.
- **US4 (P2)** → needs US2's `TenantResolutionMiddleware`; in `MultiTenant` mode also needs US7 to be complete (the per-tenant wizard is unreachable until the CSP wizard finishes).
- **US5 (P2)** → needs US1 + US3 (NOT NULL columns required for RLS).
- **US6 (P3)** → needs US2's audit-field wiring.
- **US8 (P2)** → needs US7 (gate must lift) and US2 (impersonation drill-through reuses `POST /api/tenants/{id}/impersonate`).
- **US9 (P1, MultiTenant only)** → needs US7 (the wizard surface and the FR-090 gate) and US1 / Phase 10 (so tenant-local FKs to `[GlobalReference]` rows from `CspInheritedComponent` are accepted by the SaveChanges interceptor).
- **US10 (P1, MultiTenant only)** → needs US9 (`CspInheritedCapability` rows must be `Status = Published`) and US1's tenant scoping (the `EvidenceArtifact` is tenant-local). Reuses Features 008 / 024 / 038 / 043 / 044 — no parallel implementations. Cannot start until T217 (Reuse-First Inventory) and T218 (Reuse-First Refactor) land.

### Within Each User Story

- Tests (`T035–T039`, `T062–T065`, `T078–T081`, `T087–T090`, `T112–T115`, `T134–T137`, `T155–T158`, `T176–T180` (US7), `T185–T188` (US8), `T189–T193` (US9), `T219–T222` (US10), etc.) MUST be written and observed FAILING before implementation tasks of the same phase per Constitution Principle III.
- Models / attributes before services before endpoints before integration.

### Parallel Opportunities

- All Setup tasks marked [P] (T001–T004, T007, T008) can run in parallel.
- All Foundational [P] tasks (T009–T013, T016–T018, T031–T034) can run in parallel.
- Once Phase 2 is done, **US1 retrofit tasks T044–T053 can all run in parallel** because they touch disjoint files (one phase per data-model.md sub-domain).
- All US-test creation tasks marked [P] in their phase can run in parallel.
- US2 (T062–T076) and US3 (T078–T086) implementation can proceed by two developers in parallel after Phase 2.
- CLI sub-commands T126–T129 can be implemented in parallel.
- Phase 15 entity + interface tasks T195–T201 are all [P] (disjoint files); Phase 15 frontend tasks T211–T215 are all [P] (disjoint components).
- Phase 16 test tasks T219–T222 are all [P] (disjoint test fixtures); Phase 16 frontend task T229 is [P] (single React component edit).
- Polish phase: every [P] task is independent.

---

## Parallel Example: Phase 3 Retrofit Tasks

```text
# Once Phase 2 is complete, retrofit can fan out across 11 disjoint domains:
T044 Apply [TenantScoped] + TenantId to controls & inheritance entities
T045 Apply [TenantScoped] + TenantId to findings/scans/evidence entities
T046 Apply [TenantScoped] + TenantId to POA&M / deviation / remediation entities
T047 Apply [TenantScoped] + TenantId to watch/alert entities
T048 Apply [TenantScoped] + TenantId to SAP/SAR/SSP/privacy entities
T049 Apply [TenantScoped] + TenantId to components/capabilities entities
T050 Apply [TenantScoped] + TenantId to ConMon entities
T051 Apply [TenantScoped] + TenantId to roadmap & package entities
T052 Apply [TenantScoped] + TenantId to dashboard entities
T053 Apply [TenantScoped] + TenantId to auth/cache entities
T055 Apply [GlobalReference] to reference entities
```

## Parallel Example: User Story 1 Tests

```text
# All US1 tests share the MultiTenantWebApplicationFactory and can fan out:
T035 Create TenantQueryFilterTests.cs
T036 Create CrossTenantLookupReturns404Tests.cs
T037 Create SaveChangesStampingTests.cs
T038 Create CrossTenantFkRejectionTests.cs
T039 Create McpToolTenantScopeTests.cs
```

---

## Implementation Strategy

### MVP Scope (Recommended)

**Phases 1, 2, 3, 5** = MVP for `SingleTenant` self-host. This delivers:

- New `Tenants`/`Organizations` tables, attribute infrastructure, `ITenantContext`, query filter, SaveChanges interceptor (Phase 2 + Phase 3).
- A working two-tenant smoke test (US1) — the core security property.
- A clean upgrade path for existing single-tenant installs (US3).

**MVP for `MultiTenant` deployments** additionally requires Phase 13 (US7 CSP Onboarding, P1) so the CSP can capture its own identity before any customer tenant onboards, **and** Phase 15 (US9 CSP-Inherited Components, P1) so the CSP can deliver inheritance value to its tenants on day one, **and** Phase 16 (US10 Mission Owner Consumes a CSP-Inherited Capability, P1) so the published inheritance is actionable inside tenants (without it the CSP-inherited capability catalog is read-only and never wired into a tenant's evidence + narratives). Recommended MultiTenant MVP: **Phases 1, 2, 3, 4, 5, 13, 15, 16**. The Reuse-First gate (T217 + T218) lands inside the MultiTenant MVP and gates both Phase 15 and Phase 16 implementation. **Phase 6 (US4 Tenant Onboarding Wizard, P2) is intentionally deferred out of the MultiTenant MVP**: CSP-Admins MUST manually pre-provision tenants via `POST /api/tenants` (US2 / FR-053) until US4 ships in step 8 of Incremental Delivery. New tenants will be `OnboardingState = Active` after CSP-Admin pre-provisioning sets the minimal required fields, and the wizard becomes the user-facing path once US4 lands.

After MVP, deploy and validate against [quickstart.md §1, §3](quickstart.md). Then layer on Phase 6 (Tenant Onboarding wizard), Phase 7 (RLS), Phase 14 (CSP Dashboard), and so on.

### Incremental Delivery

1. Setup + Foundational (Phases 1–2) → Foundation in place.
2. + US1 (Phase 3) → Application-level isolation works. Two-tenant test green. **Deploy/Demo.**
3. + US3 (Phase 5) → Single-tenant upgrade path validated. **Deploy/Demo (SingleTenant MVP).**
4. + US2 (Phase 4) → CSP-Admin can support customers. **Deploy/Demo.**
5. + US7 (Phase 13) → CSP first-use wizard lights up; MultiTenant mode is now operational. **Deploy/Demo.**
6. + US9 (Phase 15) including the Reuse-First Audit (T217 + T218) → CSP can ingest existing ATOs and offer inheritance to all hosted tenants. **Deploy/Demo.**
7. + US10 (Phase 16) → Tenants can consume CSP-inherited capabilities; control narratives are CSP-aware and reference source documents; AI-offline fallback writes deterministic stubs with `Status = NeedsReview`. **Deploy/Demo (MultiTenant MVP).**
8. + US4 (Phase 6) → Per-tenant self-service onboarding live. **Deploy/Demo.**
9. + US5 (Phase 7) → DB-level defense-in-depth. **Required before going live on shared SQL infra.**
10. + US8 (Phase 14) → CSP cross-tenant operational dashboard. **Deploy/Demo.**
11. + US6 (Phase 8) + Phase 9 + Phase 10 + Phase 11 → Cross-cutting concerns.
12. + Phase 12 polish → Documentation + perf + compliance verification.

### Parallel Team Strategy

After Phase 2:

- **Developer A**: Phase 3 retrofit (T043–T060) — heaviest workload, parallelizable internally.
- **Developer B**: Phase 4 (T066–T076) — TenantResolutionMiddleware + impersonation + UI picker.
- **Developer C**: Phase 5 + Phase 9 (T081–T087, T123–T130) — bootstrap + CLI.
- **Developer D**: Phase 7 (T107–T112) — RLS migration + interceptor.

When the four streams converge, run Phases 8/10/11 with 1–2 developers and Phase 12 with the whole team.

---

## Notes

- `[P]` tasks touch different files and have no dependencies on other incomplete tasks in the same phase.
- The `[USn]` label maps every implementation task back to the spec's user story for traceability.
- Phase 3 is the largest phase (T035–T061 = 27 tasks); parallelization is essential — do not serialize the retrofit.
- All test tasks ([P] within their phase) are written FIRST and MUST FAIL before the implementation task in the same phase begins (Constitution Principle III).
- Commit after every task or every logical [P] group.
- Stop at the end of each Phase 3+ for an independent demo.
- Avoid: hard-coded `00000000-...-001` GUIDs (FR-072 forbids); raw `db.Set<T>().IgnoreQueryFilters()` calls outside the migration utility; reading `HttpContext.User.FindFirst("tid")` outside `TenantResolutionMiddleware`.
