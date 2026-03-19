# Tasks: Component-Centric Boundary Model

**Input**: Design documents from `/specs/040-component-centric-boundary/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/dashboard-api.md, contracts/mcp-tools.md, quickstart.md

**Tests**: Included — plan.md constitution check III requires unit + integration tests for all new entities, services, endpoints, and tools.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Verify baseline project health before making changes

- [X] T001 Verify solution builds cleanly with `dotnet build Ato.Copilot.sln`
- [X] T002 Run existing test suites to confirm green baseline with `dotnet test`

---

## Phase 2: Foundational (Data Model & Schema)

**Purpose**: Core entity changes that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 [P] Add Azure resource fields (AzureResourceId, AzureResourceType, AzureResourceGroup, AzureLocation) and BoundaryAssignments navigation collection to SystemComponent per data-model.md in src/Ato.Copilot.Core/Models/Compliance/SystemComponent.cs
- [X] T004 [P] Create BoundaryComponentAssignment entity with all properties (Id, SystemComponentId, AuthorizationBoundaryDefinitionId, IsInScope, ExclusionRationale, InheritanceProvider, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy) and navigation properties per data-model.md in src/Ato.Copilot.Core/Models/Compliance/BoundaryComponentAssignment.cs
- [X] T005 [P] Add optional ComponentId FK (MaxLength 36) and Component navigation property to ComplianceFinding per data-model.md in src/Ato.Copilot.Core/Models/Compliance/ComplianceModels.cs
- [X] T006 [P] Add ComponentAssignments navigation collection to AuthorizationBoundaryDefinition and mark AuthorizationBoundary as deprecated per data-model.md in src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs
- [X] T007 Configure BoundaryComponentAssignment DbSet, unique index on (SystemComponentId, AuthorizationBoundaryDefinitionId), index on AuthorizationBoundaryDefinitionId, cascade deletes, SystemComponent.AzureResourceId index, ComplianceFinding.ComponentId index with SetNull delete per data-model.md in src/Ato.Copilot.Core/Data/Context/AtoCopilotContext.cs
- [X] T008 Generate EF Core migration with `dotnet ef migrations add F040_ComponentCentricBoundary` and verify it applies with `dotnet ef database update`
- [X] T009 [P] Add TypeScript DTO types (BoundaryComponentDto, AssignComponentRequest, UpdateAssignmentRequest, BoundaryLockStatus, DiscoverAzureRequest, DiscoveredResource, DiscoveryResponse, ImportAzureRequest, ImportAzureResponse, ComponentRiskSummary, AssessmentComponentRisks) per contracts/dashboard-api.md §6 in src/Ato.Copilot.Dashboard/src/types/dashboard.ts
- [X] T010 Verify solution builds and migration applies cleanly with `dotnet build Ato.Copilot.sln && dotnet ef database update`

**Checkpoint**: Data model complete — user story implementation can now begin

---

## Phase 3: User Story 1 — Azure Discovery in Component Library (Priority: P1) 🎯 MVP

**Goal**: ISSO can discover Azure resources from a subscription, filter/paginate results, and bulk-import them as org-wide "Thing" SystemComponents. Duplicates are flagged and skipped. Partial discovery failures show a warning banner with retry.

**Independent Test**: Perform a discovery scan, import three resources, verify new "Thing" components appear in the library with correct Azure fields, confirm re-scan shows "Already imported."

### Implementation for User Story 1

- [X] T011 [P] [US1] Extend AzureResourceDiscoveryService with DiscoverForComponentsAsync method returning flat resource list with alreadyImported flags and per-resource-group partial failure tracking (FailedResourceGroups list) per research.md R2/R8 in src/Ato.Copilot.Agents/Compliance/Services/AzureResourceDiscoveryService.cs
- [X] T012 [P] [US1] Add ImportAzureComponentsAsync to ComponentService — bulk creates org-wide "Thing" SystemComponents from discovery results, deduplicates by matching AzureResourceId (scoped to null RegisteredSystemId for org-wide), returns imported/skipped counts per quickstart.md Step 3.1 in src/Ato.Copilot.Core/Services/ComponentService.cs
- [X] T013 [P] [US1] Add org-level discovery endpoint (POST /components/discover-azure) and import endpoint (POST /components/import-azure) per contracts/dashboard-api.md §3.1–3.2 in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T014 [P] [US1] Create ComponentBoundaryTools.cs with compliance_discover_azure_resources (T040-1) and compliance_import_azure_components (T040-2) MCP tools extending BaseTool with standard envelope responses, and register both tools in DI per contracts/mcp-tools.md in src/Ato.Copilot.Agents/Compliance/Tools/ComponentBoundaryTools.cs and src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs
- [X] T015 [P] [US1] Add org-level discovery and import API client functions (discoverAzureResources, importAzureComponents) in src/Ato.Copilot.Dashboard/src/api/azureDiscovery.ts
- [X] T016 [US1] Add "Discover from Azure" UI to Component Library page — subscription picker, paginated/filterable resource list (by resource group, type, name), bulk import with skip-if-imported badges, partial failure warning banner with "Retry Failed" action per spec.md US1 acceptance scenarios in src/Ato.Copilot.Dashboard/src/pages/ComponentLibrary.tsx

### Tests for User Story 1

- [X] T017 [P] [US1] Write unit tests for discovery dedup logic, partial failure handling, and import bulk-create in tests/Ato.Copilot.Tests.Unit/ComponentDiscoveryTests.cs
- [X] T018 [US1] Write integration tests for org-level discovery and import endpoints (POST /components/discover-azure, POST /components/import-azure) via WebApplicationFactory in tests/Ato.Copilot.Tests.Integration/ComponentDiscoveryEndpointTests.cs

**Checkpoint**: US1 fully functional — org-level Azure discovery and import works end-to-end

---

## Phase 4: User Story 2 — System-Level Azure Discovery in Component Inventory (Priority: P1)

**Goal**: ISSM/ISSO can discover Azure resources scoped to a specific system's subscription and import them as system-scoped "Thing" components (RegisteredSystemId set). Resources that already exist in the org library offer an "assign existing" option instead of creating duplicates.

**Independent Test**: Navigate to a system's Components page, run Azure discovery, import four resources, verify four system-scoped "Thing" components with correct RegisteredSystemId and Azure fields.

### Implementation for User Story 2

- [X] T019 [US2] Add system-scoped import method to ComponentService — creates system-scoped components (RegisteredSystemId = systemId), detects org-library duplicates (existsInOrgLibrary flag), supports assignExistingOrgComponents to reuse org components via ComponentSystemAssignment per spec.md US2 in src/Ato.Copilot.Core/Services/ComponentService.cs
- [X] T020 [P] [US2] Add system-level discovery endpoint (POST /systems/{systemId}/components/discover-azure) and import endpoint (POST /systems/{systemId}/components/import-azure) with existsInOrgLibrary and assignExistingOrgComponents support per contracts/dashboard-api.md §4.1–4.2 in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T021 [P] [US2] Add system-level discovery API client functions (discoverSystemAzureResources, importSystemAzureComponents) in src/Ato.Copilot.Dashboard/src/api/components.ts
- [X] T022 [US2] Add "Discover from Azure" UI to system-level Component Inventory page — subscription auto-select from system config, "Exists in org library" badge with "Assign existing" option, disabled button with tooltip when no subscription configured per spec.md US2 in src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx

### Tests for User Story 2

- [X] T023 [P] [US2] Write unit tests for system-scoped import and org-library dedup detection in tests/Ato.Copilot.Tests.Unit/ComponentDiscoveryTests.cs
- [X] T024 [US2] Write integration tests for system-level discovery and import endpoints via WebApplicationFactory in tests/Ato.Copilot.Tests.Integration/ComponentDiscoveryEndpointTests.cs

**Checkpoint**: US1 + US2 complete — both org-level and system-level Azure discovery work independently

---

## Phase 5: User Story 3 — Boundary Component Assignment with Include/Exclude (Priority: P1)

**Goal**: ISSM can assign components to boundary definitions with per-boundary InScope/Excluded scope, mandatory exclusion rationale, and optional inheritance provider. Same component can appear in multiple boundaries with different scope statuses. Pessimistic lock prevents concurrent editing conflicts.

**Independent Test**: Assign one component to two boundaries — "In Scope" in Boundary A, "Excluded" (with rationale) in Boundary B — verify each boundary shows correct status independently.

### Implementation for User Story 3

- [X] T025 [US3] Add boundary-component CRUD methods to ComponentService — AssignComponentToBoundaryAsync (with duplicate check returning 409), UpdateBoundaryAssignmentAsync (with rationale validation when excluded), RemoveComponentFromBoundaryAsync (deletes assignment only), ListBoundaryComponentsAsync (with search/type/scope filters and pagination) per spec.md US3 in src/Ato.Copilot.Core/Services/ComponentService.cs
- [X] T026 [P] [US3] Create BoundaryLockService with in-memory ConcurrentDictionary — acquire lock (with userId + displayName), release lock, check status, auto-expiry after 5 minutes per research.md R3 in src/Ato.Copilot.Core/Services/BoundaryLockService.cs
- [X] T027 [US3] Add boundary-component CRUD endpoints (GET/POST/PUT/DELETE per contracts/dashboard-api.md §1.1–1.4) and lock endpoints (POST/DELETE/GET per §2.1–2.3) in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T028 [US3] Add compliance_assign_component_to_boundary (T040-3), compliance_list_boundary_components (T040-4), compliance_update_component_scope (T040-5), and compliance_remove_component_from_boundary (T040-6) MCP tools extending BaseTool per contracts/mcp-tools.md, and register in DI in src/Ato.Copilot.Agents/Compliance/Tools/ComponentBoundaryTools.cs and src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs
- [X] T029 [P] [US3] Add boundary-component assignment and lock API client functions (listBoundaryComponents, assignComponent, updateAssignment, removeAssignment, acquireLock, releaseLock, checkLockStatus) in src/Ato.Copilot.Dashboard/src/api/boundaries.ts
- [X] T030 [US3] Implement boundary component assignment UI on Boundary Management page — Components tab with component picker, InScope/Excluded toggle, exclusion rationale textarea (required when excluded), inheritance provider input, lock acquisition on edit start, lock-held message for other users per spec.md US3 in src/Ato.Copilot.Dashboard/src/pages/BoundaryManagement.tsx

### Tests for User Story 3

- [X] T031 [P] [US3] Write unit tests for assignment validation (duplicate prevention, rationale required when excluded, scope toggle, lock acquire/release/expiry) in tests/Ato.Copilot.Tests.Unit/BoundaryComponentAssignmentTests.cs
- [X] T032 [US3] Write integration tests for boundary-component CRUD and lock endpoints via WebApplicationFactory in tests/Ato.Copilot.Tests.Integration/BoundaryComponentEndpointTests.cs

**Checkpoint**: US1 + US2 + US3 complete — full component lifecycle from discovery through boundary assignment

---

## Phase 6: User Story 6 — Component-Level Assessment Findings (Priority: P1)

**Goal**: Compliance findings are automatically linked to components by matching ResourceId to AzureResourceId. Assessment detail view shows per-component risk summaries (open findings, highest severity, overdue remediations). Remediation page displays associated component name. Unlinked findings appear in a separate section with import prompt. Retroactive linking occurs when new components are created.

**Independent Test**: Import an Azure resource as a component, run an assessment producing findings for that resource's ARM ID, verify per-component risk summaries show correct counts and the Remediation page displays the component name.

### Implementation for User Story 6

- [X] T033 [US6] Add finding-component resolution methods to ComponentService — ResolveFindingComponentsAsync (match ComplianceFinding.ResourceId → SystemComponent.AzureResourceId within same system, update ComponentId) and RetroactiveLinkComponentAsync (on new component creation, link unlinked findings in same system) per research.md R5 in src/Ato.Copilot.Core/Services/ComponentService.cs
- [X] T034 [US6] Add component risk summary endpoint (GET /systems/{systemId}/assessments/{assessmentId}/component-risks) returning per-component aggregation and unlinked count, and add optional componentId query parameter to existing findings endpoint per contracts/dashboard-api.md §5.1–5.2 in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T035 [US6] Implement compliance_component_risk_summary MCP tool (T040-7) extending BaseTool per contracts/mcp-tools.md and register in DI in src/Ato.Copilot.Agents/Compliance/Tools/ComponentBoundaryTools.cs and src/Ato.Copilot.Agents/Extensions/ServiceCollectionExtensions.cs
- [X] T036 [P] [US6] Add component risk summary API client function in src/Ato.Copilot.Dashboard/src/api/components.ts
- [X] T037 [US6] Add per-component risk summary display (open findings, highest severity, overdue remediations) with component filter/group to Assessment detail view in src/Ato.Copilot.Dashboard/src/pages/Assessments.tsx, "Unlinked Resources" section with import prompt in Assessments.tsx, and component name on Remediation page tasks in src/Ato.Copilot.Dashboard/src/pages/Remediation.tsx per spec.md US6

### Tests for User Story 6

- [X] T038 [P] [US6] Write unit tests for finding-component resolution (ResourceId matching, retroactive linking, unlinked detection, and finding linkage from non-Azure sources including STIG/SCAP imports, Prisma Cloud imports, and ACAS/Nessus scans that produce findings with Azure resource IDs) in tests/Ato.Copilot.Tests.Unit/ComponentFindingLinkageTests.cs
- [X] T039 [US6] Write integration tests for component risk summary endpoint and componentId filtering via WebApplicationFactory in tests/Ato.Copilot.Tests.Integration/ComponentFindingEndpointTests.cs

**Checkpoint**: All P1 stories complete — discovery, assignment, and finding linkage operational

---

## Phase 7: User Story 5 — Data Migration from AuthorizationBoundary to SystemComponent (Priority: P2)

**Goal**: Existing AuthorizationBoundary resource rows are automatically migrated on application startup. Each unique ResourceId becomes one org-wide "Thing" SystemComponent with Azure fields. BoundaryComponentAssignment records preserve original scope, rationale, and inheritance. Migration is transactional, idempotent, and completes in < 60 seconds for 1,000 rows.

**Independent Test**: Seed database with five AuthorizationBoundary rows (three in-scope, two excluded with rationale), run migration, verify five SystemComponent records and five BoundaryComponentAssignment records with correct data.

### Implementation for User Story 5

- [X] T040 [US5] Create BoundaryMigrationService as IHostedService — idempotency check via sentinel flag (__MigrationFlags table), group AuthorizationBoundary rows by ResourceId, create one org-wide "Thing" SystemComponent per unique resource (AzureResourceId, AzureResourceType, AzureResourceGroup from parsed ResourceId, Name from ResourceName or generated), create one BoundaryComponentAssignment per original row preserving IsInBoundary → IsInScope + ExclusionRationale + InheritanceProvider, wrap entirely in single BeginTransactionAsync/CommitAsync, insert sentinel on success, implement Serilog structured logging for migration start, row counts, dedup counts, progress checkpoints, commit success, and rollback events per research.md R4 and data-model.md migration flow in src/Ato.Copilot.Core/Services/BoundaryMigrationService.cs
- [X] T041 [US5] Register BoundaryMigrationService as hosted service in application startup in src/Ato.Copilot.Mcp/Program.cs or appropriate startup configuration

### Tests for User Story 5

- [X] T042 [P] [US5] Write unit tests for migration logic — dedup by ResourceId, scope preservation, rationale preservation, idempotency (skip when flag exists) in tests/Ato.Copilot.Tests.Unit/BoundaryMigrationServiceTests.cs
- [X] T043 [US5] Write integration test — seed 5 AuthorizationBoundary rows, run migration, verify 5 SystemComponents + 5 BoundaryComponentAssignments created with correct data, verify re-run is no-op in tests/Ato.Copilot.Tests.Integration/BoundaryMigrationIntegrationTests.cs

**Checkpoint**: Migration verified — legacy data successfully converted

---

## Phase 8: User Story 4 — Simplified Boundary Management Page (Priority: P2)

**Goal**: Boundary Management page no longer exposes raw Azure resource entry; all boundary assets are managed through the unified component assignment view (built in US3). Migrated resource data (from US5) appears as components.

**Independent Test**: Open Boundary Management for a system that previously had raw resources, verify resources now appear as components, confirm old "Resources" tab is removed.

**Depends On**: US3 (component assignment UI), US5 (migration)

### Implementation for User Story 4

- [X] T044 [US4] Remove legacy "Resources" and "Manual" tabs from Boundary Management page, make "Components" tab the primary/only asset view, ensure discovery flows redirect to Component Library import per spec.md US4 in src/Ato.Copilot.Dashboard/src/pages/BoundaryManagement.tsx
- [X] T045 [US4] Update any API calls or state management that referenced legacy resource tab endpoints to use boundary-component assignment endpoints in src/Ato.Copilot.Dashboard/src/api/boundaries.ts

**Checkpoint**: Boundary Management fully component-centric — no raw resource management exposed

---

## Phase 9: User Story 7 — NIST P-16 to P-17 Workflow Alignment (Priority: P3)

**Goal**: System guides ISSM to complete asset identification (NIST P-16) before authorization boundary definition (P-17). Component Library is positioned as P-16; boundary definitions as P-17.

**Independent Test**: Navigate a fresh system with no components to Boundary Management, verify P-16 guidance message appears, add components, verify guidance clears.

### Implementation for User Story 7

- [X] T046 [US7] Add P-16 guidance message component to Boundary Management page — display when system has zero components in library, directing user to populate Component Library first (P-16 before P-17), hide when components exist per spec.md US7 in src/Ato.Copilot.Dashboard/src/pages/BoundaryManagement.tsx
- [X] T047 [P] [US7] Add component-count check API (or reuse existing component list endpoint with count-only param) to support the P-16 guidance conditional in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs

**Checkpoint**: NIST workflow guidance operational

---

## Phase 9a: User Story 9 — Entra ID Discovery for Person Components (Priority: P3)

**Goal**: ISSO can optionally discover Entra ID users/groups and import them as org-wide "Person" components from the Component Library. Feature is gated behind an org-level setting (disabled by default). Not available at system level.

**Independent Test**: Enable the org setting, click "Discover from Entra ID" on Component Library, import two users, verify two org-wide "Person" components are created. When disabled, the button is hidden.

**Depends On**: Phase 2 Foundational (entity model)

### Implementation for User Story 9

- [X] T057 [US9] Add EntraIdDiscoveryService with DiscoverUsersAndGroupsAsync method using Microsoft Graph API (User.Read.All), returning users/groups with alreadyImported dedup flags per spec.md US9 and FR-005a in src/Ato.Copilot.Agents/Compliance/Services/EntraIdDiscoveryService.cs
- [X] T058 [P] [US9] Add org-level Entra ID discovery setting (EntraIdDiscoveryEnabled, default false) to organization configuration in src/Ato.Copilot.Core/Models/Compliance/OrganizationSettings.cs (or existing settings entity)
- [X] T059 [US9] Add Entra ID discovery endpoint (POST /components/discover-entra) and import endpoint (POST /components/import-entra) gated behind the org setting check per FR-005a in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [X] T060 [P] [US9] Add Entra ID discovery and import API client functions (discoverEntraIdUsers, importEntraIdPeople) in src/Ato.Copilot.Dashboard/src/api/azureDiscovery.ts
- [X] T061 [US9] Add "Discover from Entra ID" button to Component Library page — conditionally visible when org setting enabled, paginated user/group list with import, "Already imported" badges per spec.md US9 in src/Ato.Copilot.Dashboard/src/pages/ComponentLibrary.tsx

### Tasks for FR-026/FR-027 (Stale Resource Indicator + Re-link)

- [X] T062 [P] [US1] Add stale resource detection to DiscoverForComponentsAsync — for previously imported components whose AzureResourceId is no longer found in the subscription, return a "notFoundInAzure" flag per FR-026 in src/Ato.Copilot.Agents/Compliance/Services/AzureResourceDiscoveryService.cs
- [X] T063 [P] [US6] Add RelinkComponentFindingsAsync method to ComponentService — re-runs ComponentId resolution for all findings in the same system as the given component per FR-027 in src/Ato.Copilot.Core/Services/ComponentService.cs
- [X] T064 [US6] Add "Re-link Findings" action to component detail panel in src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx and corresponding endpoint (POST /systems/{systemId}/components/{componentId}/relink-findings) in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs per FR-027

### Tests for User Story 9

- [X] T065 [P] [US9] Write unit tests for Entra ID discovery (user/group fetching, dedup, setting gate) in tests/Ato.Copilot.Tests.Unit/EntraIdDiscoveryTests.cs
- [X] T066 [US9] Write integration tests for Entra ID discovery and import endpoints (POST /components/discover-entra, POST /components/import-entra) with setting-disabled rejection in tests/Ato.Copilot.Tests.Integration/EntraIdDiscoveryEndpointTests.cs

**Checkpoint**: Entra ID Person discovery operational (when enabled)

---

## Phase 10: User Story 8 — Documentation Alignment (Priority: P2)

**Goal**: All pre-updated documentation files accurately reflect the implemented component-centric workflow with zero stale references.

**Independent Test**: Walk through each updated guide step-by-step in the running application and confirm every described action, navigation path, and expected result matches actual behavior.

### Implementation for User Story 8

- [X] T048 [P] [US8] Validate docs/guides/issm-guide.md against running application — verify Step 2 (Identify System Components) exists and works, confirm workflow overview matches, verify NIST P-16/P-17 info callout accuracy, fix any discrepancies in docs/guides/issm-guide.md
- [X] T049 [P] [US8] Validate docs/getting-started/issm.md against running application — verify Step 2 (Identify System Components) with Discover from Azure guidance, confirm boundary is Step 3, fix any discrepancies in docs/getting-started/issm.md
- [X] T050 [P] [US8] Validate docs/guides/component-inventory.md against running application — verify Assessment & Remediation Linkage section accurately describes where per-component risk summaries appear, fix any discrepancies in docs/guides/component-inventory.md
- [X] T051 [P] [US8] Validate docs/guides/compliance-dashboard.md against running application — verify Risk Visibility note under Component Inventory section, confirm risk summary locations are correct, fix any discrepancies in docs/guides/compliance-dashboard.md
- [X] T052 [US8] Perform final cross-document review — verify no stale references to old workflow order (boundary before components) or incorrect risk summary page locations across all four docs

**Checkpoint**: Documentation validated and accurate

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, cleanup, and cross-cutting improvements

- [X] T053 Run full quickstart.md validation — follow all 8 steps in quickstart.md against the feature branch, verify build/test/run commands work as documented in specs/040-component-centric-boundary/quickstart.md
- [X] T054 [P] Verify all acceptance scenarios from spec.md — walk through each user story's acceptance scenarios end-to-end in the running application
- [X] T055 [P] Run full test suite (`dotnet test`) and verify zero regressions across unit and integration tests
- [X] T056 Performance validation — verify Azure discovery of 50 resources completes in < 3 minutes, boundary assignment < 30 seconds, migration of seeded 1,000 rows < 60 seconds, assessment detail page load < 5 seconds per plan.md performance goals

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1: Setup ──→ Phase 2: Foundational ──┬──→ Phase 3: US1 (P1) ──→ Phase 4: US2 (P1)
                                            │
                                            ├──→ Phase 5: US3 (P1) ──┐
                                            │                         ├──→ Phase 8: US4 (P2)
                                            ├──→ Phase 7: US5 (P2) ──┘
                                            │
                                            ├──→ Phase 6: US6 (P1)
                                            │
                                            ├──→ Phase 9: US7 (P3)
                                            │
                                            └──→ Phase 9a: US9 (P3)
                                            
All Phases ──→ Phase 10: US8 (P2) ──→ Phase 11: Polish
```

### User Story Dependencies

| Story | Depends On | Can Parallel With |
|-------|-----------|-------------------|
| **US1** (Phase 3) | Foundational only | US3, US5, US6, US7 |
| **US2** (Phase 4) | US1 (shared discovery service) | US3, US5, US6, US7 |
| **US3** (Phase 5) | Foundational only | US1, US2, US5, US6, US7 |
| **US6** (Phase 6) | Foundational only | US1, US2, US3, US5, US7 |
| **US5** (Phase 7) | Foundational only | US1, US2, US3, US6, US7 |
| **US4** (Phase 8) | US3 + US5 (needs assignment UI + migrated data) | US7 |
| **US7** (Phase 9) | Foundational only | US1–US6, US9 |
| **US9** (Phase 9a) | Foundational only | US1–US7 |
| **US8** (Phase 10) | All stories complete | None |

### Within Each User Story

1. Service methods before endpoints/tools (services provide the logic)
2. Backend endpoints before frontend (APIs must exist for UI to call)
3. MCP tools parallel with API endpoints (both consume services)
4. Frontend API client before UI components
5. Tests after implementation (validate behavior)

---

## Parallel Execution Examples

### Parallel Batch: Foundational Entity Changes (T003–T006)

```
Parallel:
  T003: SystemComponent Azure fields        (SystemComponent.cs)
  T004: BoundaryComponentAssignment entity   (BoundaryComponentAssignment.cs)
  T005: ComplianceFinding ComponentId        (ComplianceModels.cs)
  T006: AuthorizationBoundaryDefinition nav  (RmfModels.cs)
Then sequential:
  T007: DbContext configuration              (AtoCopilotContext.cs)
  T008: EF Core migration
```

### Parallel Batch: US1 Service Layer (T011–T012)

```
Parallel:
  T011: Discovery service extension          (AzureResourceDiscoveryService.cs)
  T012: Import service method                (ComponentService.cs)
Then parallel:
  T013: API endpoints                        (DashboardEndpoints.cs)
  T014: MCP tools + DI                       (ComponentBoundaryTools.cs)
  T015: Frontend API client                  (azureDiscovery.ts)
Then:
  T016: Component Library UI                 (ComponentLibrary.tsx)
```

### Parallel Batch: Multiple Stories After Foundational

```
With 3 developers after Phase 2:
  Developer A: US1 (T011–T018) → US2 (T019–T024)
  Developer B: US3 (T025–T032)
  Developer C: US6 (T033–T039) → US5 (T040–T043)
Then converge:
  US4 (T044–T045) after Developer B + C complete
  US7 (T046–T047) anytime
  US8 (T048–T052) after all stories
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1 — Azure Discovery in Component Library
4. **STOP and VALIDATE**: Discover resources, import, verify components
5. Deploy/demo if ready — org-level Azure discovery is immediately valuable

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. **US1** → Org-level discovery works → Deploy/Demo (MVP!)
3. **US2** → System-level discovery works → Deploy/Demo
4. **US3** → Boundary assignment works → Deploy/Demo
5. **US6** → Finding linkage works → Deploy/Demo
6. **US5** → Migration runs → Deploy/Demo
7. **US4** → Simplified UI → Deploy/Demo
8. **US7** → NIST guidance → Deploy/Demo
9. **US8** → Docs validated → Feature complete

### Key Pitfalls (from quickstart.md)

- Migration transaction scope: Entire migration in single transaction — don't call SaveChangesAsync multiple times without BeginTransactionAsync
- Unique constraint on BoundaryComponentAssignment: Check for existing assignment before creating, return 409 on duplicate
- Exclusion rationale validation: Enforce in service layer, not database (DB can't do conditional NOT NULL)
- AzureResourceId dedup: Scope query to correct RegisteredSystemId (null for org-wide, GUID for system-scoped)
- Finding linkage timing: Call ResolveFindingComponentsAsync both after assessment/import AND after component creation (retroactive)
- Lock cleanup: In-memory locks are lost on server restart (acceptable — no edit session survives restart)

---

## Notes

- Total tasks: **56**
- [P] tasks = different files, no dependencies on incomplete tasks
- [US#] label maps task to specific user story for traceability
- User stories US1, US3, US5, US6, US7 can start immediately after Phase 2
- US2 depends on US1; US4 depends on US3 + US5; US8 depends on all
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
