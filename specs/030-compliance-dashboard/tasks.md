# Tasks: Visual Compliance Dashboard & Risk Solutions Library

**Input**: Design documents from `/specs/030-compliance-dashboard/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/dashboard-api.md, quickstart.md

**Tests**: Not explicitly requested in the feature specification. Tests are omitted from task phases.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. US7 (Dashboard API Endpoints) is merged into each frontend user story's backend work since the API is the backend half of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization, React SPA scaffolding, and base configuration

- [x] T001 Create React SPA project scaffold with Vite + TypeScript + Tailwind CSS in src/Ato.Copilot.Dashboard/ (package.json, vite.config.ts, tsconfig.json, tailwind.config.js, index.html, src/main.tsx)
- [x] T002 [P] Install frontend dependencies: react, react-dom, react-router-dom, recharts, axios, tailwindcss, @tailwindcss/typography in src/Ato.Copilot.Dashboard/package.json
- [x] T003 [P] Create shared TypeScript type definitions mirroring backend DTOs in src/Ato.Copilot.Dashboard/src/types/dashboard.ts (PortfolioSystemSummary, SystemDetailResponse, HeatmapResponse, TrendDataPoint, SecurityCapabilityDto, SystemComponentDto, GapAnalysisResponse, PaginatedResponse, ErrorResponse)
- [x] T004 [P] Create Axios API client with base URL config, auth header interceptor, and error handling in src/Ato.Copilot.Dashboard/src/api/client.ts
- [x] T005 [P] Create usePolling custom hook (configurable 15-30s interval, pause on tab blur, resume on focus) in src/Ato.Copilot.Dashboard/src/hooks/usePolling.ts
- [x] T006 [P] Create PageLayout component (header, sidebar nav, main content area) in src/Ato.Copilot.Dashboard/src/components/layout/PageLayout.tsx
- [x] T007 [P] Create App.tsx with React Router routes: / (portfolio), /systems/:id (detail), /capabilities (library), /systems/:id/components (inventory), /systems/:id/gaps (gap analysis) in src/Ato.Copilot.Dashboard/src/App.tsx
- [x] T008 [P] Create environment config (.env.local template with VITE_API_BASE_URL and VITE_POLL_INTERVAL_MS) in src/Ato.Copilot.Dashboard/.env.example
- [x] T009 [P] Add Dashboard Dtos directory with common types: PaginatedResponse, ErrorResponse (with error, errorCode, details, suggestion fields per Constitution Principle VII), and pagination query helpers in src/Ato.Copilot.Mcp/Dtos/Dashboard/CommonDtos.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: EF Core entities, DbContext configuration, enums, and EF migration that ALL user stories depend on

**CRITICAL**: No user story work can begin until this phase is complete

- [x] T010 Create CapabilityStatus enum (Planned, InProgress, Implemented, Deprecated) in src/Ato.Copilot.Core/Models/Enums/CapabilityStatus.cs
- [x] T011 [P] Create CapabilityMappingRole enum (Primary, Supporting, Shared) in src/Ato.Copilot.Core/Models/Enums/CapabilityMappingRole.cs
- [x] T012 [P] Create ComponentType enum (Person, Place, Thing) in src/Ato.Copilot.Core/Models/Enums/ComponentType.cs
- [x] T013 [P] Create ComponentStatus enum (Active, Planned, Decommissioned) in src/Ato.Copilot.Core/Models/Enums/ComponentStatus.cs
- [x] T014 Create SecurityCapability entity (Id, Name, Provider, Category, Description, ImplementationStatus, Owner, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy) with navigation properties in src/Ato.Copilot.Core/Models/SecurityCapability.cs
- [x] T015 [P] Create CapabilityControlMapping entity (Id, SecurityCapabilityId, ControlId, RegisteredSystemId, Role, CreatedAt, CreatedBy) with navigation properties in src/Ato.Copilot.Core/Models/CapabilityControlMapping.cs
- [x] T016 [P] Create SystemComponent entity (Id, RegisteredSystemId, Name, ComponentType, SubType, Description, Owner, Status, CreatedAt, CreatedBy, ModifiedAt) with navigation properties in src/Ato.Copilot.Core/Models/SystemComponent.cs
- [x] T017 [P] Create ComponentCapabilityLink join entity (SystemComponentId, SecurityCapabilityId) in src/Ato.Copilot.Core/Models/ComponentCapabilityLink.cs
- [x] T018 [P] Create ComplianceTrendSnapshot entity (Id, RegisteredSystemId, CapturedAt, ComplianceScore, CatICount, CatIICount, CatIIICount, OpenPoamCount, OverduePoamCount, NarrativeCoverage, Source) in src/Ato.Copilot.Core/Models/ComplianceTrendSnapshot.cs
- [x] T018a [P] Create DashboardActivity entity (Id, RegisteredSystemId, EventType, Timestamp, Actor, Summary, RelatedEntityType, RelatedEntityId) with navigation properties and index on (RegisteredSystemId, Timestamp DESC) in src/Ato.Copilot.Core/Models/DashboardActivity.cs
- [x] T019 Add SecurityCapabilityId (nullable FK) and IsManuallyCustomized (bool, default false) columns to existing ControlImplementation entity in src/Ato.Copilot.Core/Models/SspModels.cs
- [x] T020 Register DbSets (including DashboardActivities) and configure entity relationships, indexes, and constraints (unique Name on SecurityCapability, composite unique on CapabilityControlMapping, composite PK on ComponentCapabilityLink, trend index on RegisteredSystemId+CapturedAt DESC, activity index on RegisteredSystemId+Timestamp DESC) in src/Ato.Copilot.Core/Data/AtoCopilotContext.cs
- [x] T021 Generate EF Core migration for all new entities (including DashboardActivity) and modified ControlImplementation via `dotnet ef migrations add AddDashboardEntities` in src/Ato.Copilot.Mcp/
- [x] T022 Configure CORS policy on MCP server to allow dashboard SPA origin (localhost:5173 in dev) in src/Ato.Copilot.Mcp/ startup configuration
- [x] T022a Configure authentication middleware for the /api/dashboard/* route group — reuse existing Bearer token auth handler and wire RmfRoleAssignment-based authorization filter to enforce RBAC on all dashboard endpoints in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T023 [P] Create DashboardEndpoints static class with route group registration method for /api/dashboard/* prefix in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs (empty endpoint stubs, wire into McpHttpBridge.ConfigureEndpoints)

**Checkpoint**: Database schema ready, CORS configured, endpoint routing scaffolded — user story implementation can begin

---

## Phase 3: User Story 7 + User Story 1 — Portfolio Dashboard Overview (Priority: P1) MVP

**Goal**: Backend portfolio API endpoint + frontend portfolio dashboard page showing all registered systems with sort/filter, ATO countdown severity indicators, and 15-30s polling refresh

**Independent Test**: Register 3+ systems with varying RMF phases, run assessments on at least one, navigate to dashboard — all systems appear with correct IL, RMF phase, compliance score, ATO expiration, and POA&M counts. Sort by compliance score descending. Verify polling updates within 30 seconds.

### Backend — Portfolio API

- [x] T024 [US1] Create PortfolioSystemSummaryDto (systemId, name, impactLevel, currentRmfPhase, complianceScore, complianceScoreDelta, atoExpirationDate, atoStatus, atoDaysRemaining, atoSeverity, openPoamCount, overduePoamCount, catICounts, catIICounts, catIIICounts) in src/Ato.Copilot.Mcp/Dtos/Dashboard/PortfolioDtos.cs
- [x] T025 [US1] Create DashboardService with GetPortfolioAsync method — query RegisteredSystems with joins to AuthorizationDecision, PoamItem, ComplianceFinding, ComplianceAssessment; compute ATO severity (green/yellow/red/expired/none), finding counts by CatSeverity; support sort/filter/pagination in src/Ato.Copilot.Mcp/Services/DashboardService.cs
- [x] T026 [US1] Register DashboardService in DI container and wire GET /api/dashboard/portfolio endpoint with query parameters (sortBy, sortDir, impactLevel, rmfPhase, cursor, pageSize) with RBAC filtering in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T027 [US1] Add Serilog structured logging to DashboardService.GetPortfolioAsync (log request params, result count, execution duration) in src/Ato.Copilot.Mcp/Services/DashboardService.cs

### Frontend — Portfolio Dashboard Page

- [x] T028 [P] [US1] Create API function getPortfolio(params) calling GET /api/dashboard/portfolio with sort/filter/pagination params in src/Ato.Copilot.Dashboard/src/api/portfolio.ts
- [x] T029 [P] [US1] Create MetricCard reusable component (title, value, subtitle, optional trend arrow, optional severity color) in src/Ato.Copilot.Dashboard/src/components/cards/MetricCard.tsx
- [x] T030 [P] [US1] Create AtoCountdown component (displays days remaining with graduated severity: green > 90d, yellow 30-90d, red < 30d, black = expired; "--" when no ATO) in src/Ato.Copilot.Dashboard/src/components/cards/AtoCountdown.tsx
- [x] T031 [US1] Create SystemSummaryRow component (table row with system name, impact level, RMF phase badge, compliance score, ATO countdown, POA&M counts; clickable to navigate to /systems/:id) in src/Ato.Copilot.Dashboard/src/components/cards/SystemSummaryRow.tsx
- [x] T032 [US1] Create PortfolioDashboard page with sortable/filterable system table, polling via usePolling hook (15s), empty state "No systems registered", filter dropdowns for impact level and RMF phase in src/Ato.Copilot.Dashboard/src/pages/PortfolioDashboard.tsx

### Tests for User Story 1

- [x] T032a [P] [US1] Unit tests for DashboardService.GetPortfolioAsync — test sort (all 6 columns asc/desc), filter (impactLevel, rmfPhase), ATO severity computation (green/yellow/red/expired/none), pagination (cursor, pageSize boundary 0/1/100/101), empty portfolio, CAT severity counts in tests/Ato.Copilot.Tests.Unit/Services/DashboardServiceTests.cs
- [x] T032b [P] [US1] Integration test for GET /api/dashboard/portfolio — happy path (3 systems returned with correct fields), RBAC filtering (user only sees assigned systems), sort by complianceScore desc, filter by impactLevel=Moderate, pagination cursor, 404 on unauthorized system, error response schema validation (errorCode + suggestion present) in tests/Ato.Copilot.Tests.Integration/Endpoints/DashboardEndpointsTests.cs

**Checkpoint**: Portfolio dashboard renders all systems with live data, sorting, filtering, ATO severity indicators, and auto-refresh. MVP is independently usable as a read-only status board.

---

## Phase 4: User Story 2 — Single-System Compliance Roadmap (Priority: P1)

**Goal**: Backend system detail + heatmap endpoints + frontend system detail page with RMF phase progress, control family heatmap, key metrics, and activity feed

**Independent Test**: Register one system, advance through 3 RMF phases, write narratives for several control families, run an assessment, import a scan. Open system detail — verify RMF phases, heatmap colors, metric cards, and activity feed all render correctly.

### Backend — System Detail API

- [x] T033 [US2] Create SystemDetailDto (systemId, name, impactLevel, baselineLevel, currentRmfPhase, rmfPhaseProgress[], keyMetrics{}, recentActivity[]) with nested RmfPhaseProgressDto, KeyMetricsDto, and RecentActivityDto in src/Ato.Copilot.Mcp/Dtos/Dashboard/SystemDetailDtos.cs
- [x] T034 [P] [US2] Create HeatmapDto (systemId, baselineLevel, families[] with familyCode, familyName, totalControls, assessedControls, satisfiedControls, compliancePercent, severity) in src/Ato.Copilot.Mcp/Dtos/Dashboard/HeatmapDtos.cs
- [x] T035 [US2] Add GetSystemDetailAsync to DashboardService — query system with RMF phase progress (compute narrative coverage per phase), key metrics (score + delta, POA&M counts, ATO countdown, finding counts by CAT severity, narrative coverage), and recent activity from AuditLogEntry (last 10 events) in src/Ato.Copilot.Mcp/Services/DashboardService.cs
- [x] T036 [US2] Add GetHeatmapAsync to DashboardService — query ControlBaseline.ControlIds, join with ControlEffectiveness records, group by NistControl.Family, compute per-family compliance percentage and severity (green >=80%, yellow 50-79%, red <50%, gray = not assessed) in src/Ato.Copilot.Mcp/Services/DashboardService.cs
- [x] T037 [US2] Wire GET /api/dashboard/systems/{systemId} and GET /api/dashboard/systems/{systemId}/heatmap endpoints with RBAC check in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs

### Frontend — System Detail Page

- [x] T038 [P] [US2] Create API functions getSystemDetail(systemId) and getHeatmap(systemId) in src/Ato.Copilot.Dashboard/src/api/systemDetail.ts
- [x] T039 [P] [US2] Create RmfPhaseProgress component (horizontal stepper showing 7 RMF phases: Prepare, Categorize, Select, Implement, Assess, Authorize, Monitor; current phase highlighted, completion % per phase, phases before current shown as complete) in src/Ato.Copilot.Dashboard/src/components/charts/RmfPhaseProgress.tsx
- [x] T040 [P] [US2] Create ComplianceHeatmap component (19-cell grid, one per NIST control family, color-coded by severity, family code label + percentage, clickable to drill-down; families not in baseline hidden) in src/Ato.Copilot.Dashboard/src/components/charts/ComplianceHeatmap.tsx
- [x] T041 [P] [US2] Create FindingsSeverityCard component (total findings count with stacked bar showing CAT I/II/III breakdown with severity colors) in src/Ato.Copilot.Dashboard/src/components/cards/FindingsSeverityCard.tsx
- [x] T042 [P] [US2] Create ActivityFeed component (list of recent events with event icon, timestamp, actor, summary; max 10 items) in src/Ato.Copilot.Dashboard/src/components/cards/ActivityFeed.tsx
- [x] T043 [US2] Create SystemDetail page composing RmfPhaseProgress, ComplianceHeatmap, MetricCard (score+trend, POA&Ms, ATO countdown), FindingsSeverityCard, and ActivityFeed; polling via usePolling; back navigation to portfolio in src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx

### Heatmap Drill-Down (FR-010)

- [x] T043a [US2] Create HeatmapControlsDto (systemId, familyCode, familyName, controls[] with controlId, controlTitle, complianceStatus, hasNarrative, isManuallyCustomized, securityCapabilityName) in src/Ato.Copilot.Mcp/Dtos/Dashboard/HeatmapDtos.cs
- [x] T043b [US2] Add GetHeatmapControlsAsync(systemId, familyCode) to DashboardService — query controls in the specified family from the system's baseline, join with ControlEffectiveness and ControlImplementation to populate compliance status, narrative presence, and capability name in src/Ato.Copilot.Mcp/Services/DashboardService.cs
- [x] T043c [US2] Wire GET /api/dashboard/systems/{systemId}/heatmap/{familyCode}/controls endpoint with RBAC check in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs
- [x] T043d [P] [US2] Create API function getHeatmapControls(systemId, familyCode) in src/Ato.Copilot.Dashboard/src/api/systemDetail.ts
- [x] T043e [US2] Create ControlDrillDown component (modal/panel showing individual controls for a family with control ID, title, compliance status badge, narrative status, linked capability name; triggered from ComplianceHeatmap cell click) in src/Ato.Copilot.Dashboard/src/components/charts/ControlDrillDown.tsx

### Tests for User Story 2

- [x] T043f [P] [US2] Unit tests for DashboardService — GetSystemDetailAsync (RMF phase progress with 7 phases, narrative coverage computation, key metrics aggregation, recent activity from DashboardActivity, system not found), GetHeatmapAsync (19 families, severity thresholds green/yellow/red/gray, families not in baseline excluded), GetHeatmapControlsAsync (controls within family, compliance status mapping) in tests/Ato.Copilot.Tests.Unit/Services/DashboardServiceTests.cs
- [x] T043g [P] [US2] Integration tests for GET /api/dashboard/systems/{systemId}, GET /api/dashboard/systems/{systemId}/heatmap, GET /api/dashboard/systems/{systemId}/heatmap/{familyCode}/controls — happy path, 404 on unknown system, RBAC enforcement in tests/Ato.Copilot.Tests.Integration/Endpoints/DashboardEndpointsTests.cs

**Checkpoint**: System detail page fully renders RMF roadmap, heatmap, metrics, and activity feed with live polling. Drill-down from portfolio works.

---

## Phase 5: User Story 3 — Security Capabilities Library (Priority: P1)

**Goal**: Backend capability CRUD + narrative auto-generation + propagation endpoints, plus frontend capability library page with search, filtering, create/edit/delete, and auto-narrative preview

**Independent Test**: Create "Multi-Factor Authentication" capability, map to 5 controls (AC-2, AC-7, IA-2, IA-5, IA-8), verify narratives generated. Update provider from "Duo" to "Okta" — verify all 5 narratives regenerated. Manually edit one narrative, update capability again — verify edited narrative is skipped and "upstream change" shown.

### Backend — Capability Service & API

- [x] T044 [US3] Create SecurityCapabilityDto (id, name, provider, category, categoryName, description, implementationStatus, owner, mappedControlCount, systemsUsingCount, createdAt, modifiedAt), CreateCapabilityRequest, UpdateCapabilityResponse (with narrativesUpdated, narrativesSkipped), and DeleteCapabilityResponse DTOs in src/Ato.Copilot.Mcp/Dtos/Dashboard/CapabilityDtos.cs
- [x] T045 [US3] Create CapabilityMappingDto (id, controlId, controlTitle, controlFamily, role, registeredSystemId, registeredSystemName, narrativeStatus, isManuallyCustomized), CreateMappingsRequest (mappings[]), and CreateMappingsResponse (created, warnings[], narrativesGenerated) DTOs in src/Ato.Copilot.Mcp/Dtos/Dashboard/CapabilityMappingDtos.cs
- [x] T046 [US3] Create NarrativeTemplateService with GenerateNarrative(capability, control) method — template-based string interpolation with control-family-specific contextual wrappers per R6 in src/Ato.Copilot.Mcp/Services/NarrativeTemplateService.cs
- [x] T047 [US3] Create CapabilityService with CRUD methods: GetCapabilitiesAsync (search, category filter, status filter, pagination), GetCapabilityByIdAsync, CreateCapabilityAsync, UpdateCapabilityAsync (with narrative propagation: regenerate narratives where SecurityCapabilityId matches and IsManuallyCustomized is false, log skipped customized narratives, create audit entries), DeleteCapabilityAsync (set affected ControlImplementation.SecurityCapabilityId to null, create DashboardActivity entries) in src/Ato.Copilot.Mcp/Services/CapabilityService.cs
- [x] T048 [US3] Add GetMappingsAsync and CreateMappingsAsync to CapabilityService — validate controlIds exist in NistControl table, check duplicate primary role warnings, trigger NarrativeTemplateService.GenerateNarrative for each mapping, write to ControlImplementation in src/Ato.Copilot.Mcp/Services/CapabilityService.cs
- [x] T049 [US3] Register CapabilityService and NarrativeTemplateService in DI container; wire capability endpoints: GET/POST/PUT/DELETE /api/dashboard/capabilities, GET/POST /api/dashboard/capabilities/{id}/mappings in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs

### Frontend — Capability Library Page

- [x] T050 [P] [US3] Create API functions getCapabilities(params), createCapability(data), updateCapability(id, data), deleteCapability(id), getCapabilityMappings(id), createCapabilityMappings(id, data) in src/Ato.Copilot.Dashboard/src/api/capabilities.ts
- [x] T051 [P] [US3] Create CapabilityCard component (name, provider, category badge, status badge, mapped control count, click to expand details) in src/Ato.Copilot.Dashboard/src/components/cards/CapabilityCard.tsx
- [x] T052 [P] [US3] Create CapabilityForm component (create/edit form with name, provider, category dropdown of 19 NIST families, description textarea, implementation status select, owner input; validation matching backend constraints) in src/Ato.Copilot.Dashboard/src/components/forms/CapabilityForm.tsx
- [x] T053 [P] [US3] Create MappingPanel component (list of mapped controls grouped by family, showing control ID, title, narrative status badge: Populated/Empty/Customized; add-mapping button with control ID autocomplete and role selector) in src/Ato.Copilot.Dashboard/src/components/cards/MappingPanel.tsx
- [x] T054 [US3] Create CapabilityLibrary page with search bar, category and status filter dropdowns, paginated capability card grid, create capability modal, click-to-expand detail with mapping panel, delete confirmation dialog; empty state "Create your first Security Capability" in src/Ato.Copilot.Dashboard/src/pages/CapabilityLibrary.tsx

### Tests for User Story 3

- [x] T054a [P] [US3] Unit tests for NarrativeTemplateService.GenerateNarrative — template interpolation with capability name/provider/description, family-specific context wrappers for AC/IA/SC families, null/empty inputs in tests/Ato.Copilot.Tests.Unit/Services/NarrativeTemplateServiceTests.cs
- [x] T054b [P] [US3] Unit tests for CapabilityService — CreateCapabilityAsync (validation, duplicate name 409), UpdateCapabilityAsync (narrative propagation: regenerate non-customized, skip customized, count updated/skipped), DeleteCapabilityAsync (null out SecurityCapabilityId, create DashboardActivity entries), GetMappingsAsync, CreateMappingsAsync (validate controlIds, duplicate primary warning, narrative generation) in tests/Ato.Copilot.Tests.Unit/Services/CapabilityServiceTests.cs
- [x] T054c [P] [US3] Integration tests for GET/POST/PUT/DELETE /api/dashboard/capabilities and GET/POST /api/dashboard/capabilities/{id}/mappings — CRUD happy paths, 409 duplicate name, 400 validation, mapping creation triggers narrative, narrative propagation on update in tests/Ato.Copilot.Tests.Integration/Endpoints/DashboardEndpointsTests.cs

**Checkpoint**: Capabilities CRUD works end-to-end. Narrative generation and propagation verified. Manual override protection in place.

---

## Phase 6: User Story 4 — Capability-to-Control Mapping / Gap Analysis (Priority: P2)

**Goal**: Backend gap analysis endpoint + frontend gap analysis page showing coverage matrix, unmapped controls, and duplicate primary warnings

**Independent Test**: Create 5 capabilities mapped to various controls on a Moderate baseline system. View gap analysis — verify 325 total controls, correct covered/gap counts per family, families below 50% highlighted.

### Backend — Gap Analysis API

- [x] T055 [US4] Create GapAnalysisDto (systemId, baselineLevel, totalBaselineControls, coveredControls, gapCount, coveragePercent, familyBreakdown[] with familyCode, familyName, totalControls, coveredControls, gapCount, coveragePercent, isBelow50, unmappedControls[]) in src/Ato.Copilot.Mcp/Dtos/Dashboard/GapAnalysisDtos.cs
- [x] T056 [US4] Add GetGapAnalysisAsync to CapabilityService — query ControlBaseline.ControlIds for the system, find all CapabilityControlMapping entries scoped to that system (or org-wide null scope), compute per-family coverage, identify unmapped controls in src/Ato.Copilot.Mcp/Services/CapabilityService.cs
- [x] T057 [US4] Wire GET /api/dashboard/systems/{systemId}/gaps endpoint with RBAC check in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs

### Frontend — Gap Analysis Page

- [x] T058 [P] [US4] Create API function getGapAnalysis(systemId) in src/Ato.Copilot.Dashboard/src/api/gapAnalysis.ts
- [x] T059 [P] [US4] Create CoverageMatrix component (table with rows per control family: family name, total controls, covered, gaps, coverage bar chart; rows below 50% highlighted red; summary header row with totals) in src/Ato.Copilot.Dashboard/src/components/charts/CoverageMatrix.tsx
- [x] T060 [US4] Create GapAnalysis page composing summary MetricCards (total/covered/gaps/coverage%), CoverageMatrix, expandable per-family detail showing unmapped control IDs and titles; navigation from system detail in src/Ato.Copilot.Dashboard/src/pages/GapAnalysis.tsx

### Tests for User Story 4

- [x] T060a [P] [US4] Unit tests for CapabilityService.GetGapAnalysisAsync — Moderate baseline (325 controls), correct per-family coverage, unmapped controls identified, families below 50% flagged, empty mappings (100% gap), full coverage (0% gap) in tests/Ato.Copilot.Tests.Unit/Services/CapabilityServiceTests.cs
- [x] T060b [P] [US4] Integration test for GET /api/dashboard/systems/{systemId}/gaps — happy path with partial coverage, RBAC enforcement, 404 on unknown system in tests/Ato.Copilot.Tests.Integration/Endpoints/DashboardEndpointsTests.cs

**Checkpoint**: Gap analysis displays accurate coverage data. Unmapped controls visible per family. Below-50% families highlighted.

---

## Phase 7: User Story 5 — System Component Inventory (Priority: P2)

**Goal**: Backend component CRUD endpoints + frontend inventory page with Person/Place/Thing sections, capability linking, and deletion flagging

**Independent Test**: Add 3 People (ISSM, ISSO, SCA), 2 Places (Azure Gov East, Azure Gov West), 5 Things (Entra ID, Defender, Key Vault, Log Analytics, Sentinel). Link some to capabilities. Remove one Thing — verify linked capabilities flagged for review.

### Backend — Component Service & API

- [x] T061 [US5] Create SystemComponentDto (id, name, componentType, subType, description, owner, status, linkedCapabilities[], createdAt, modifiedAt), CreateComponentRequest (name, componentType, subType, description, owner, status, linkedCapabilityIds[]), ComponentSummaryDto (personCount, placeCount, thingCount, totalCount), and DeleteComponentResponse (deletedId, flaggedCapabilities[]) in src/Ato.Copilot.Mcp/Dtos/Dashboard/ComponentDtos.cs
- [x] T062 [US5] Create ComponentService with CRUD methods: GetComponentsAsync (type filter, status filter, search, pagination with summary counts), CreateComponentAsync (create SystemComponent + ComponentCapabilityLink entries), UpdateComponentAsync, DeleteComponentAsync (on Active component deletion: create DashboardActivity flagging linked capabilities for review) in src/Ato.Copilot.Mcp/Services/ComponentService.cs
- [x] T063 [US5] Register ComponentService in DI container; wire GET/POST /api/dashboard/systems/{systemId}/components, PUT/DELETE /api/dashboard/components/{id} endpoints with RBAC check in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs

### Frontend — Component Inventory Page

- [x] T064 [P] [US5] Create API functions getComponents(systemId, params), createComponent(systemId, data), updateComponent(id, data), deleteComponent(id) in src/Ato.Copilot.Dashboard/src/api/components.ts
- [x] T065 [P] [US5] Create ComponentSection component (collapsible section for one ComponentType with item count badge, list of component rows with name, subType, owner, status, linked capability tags) in src/Ato.Copilot.Dashboard/src/components/cards/ComponentSection.tsx
- [x] T066 [P] [US5] Create ComponentForm component (create/edit form with name, type radio group Person/Place/Thing, subType input, description, owner, status select, capability multi-select linking; validation) in src/Ato.Copilot.Dashboard/src/components/forms/ComponentForm.tsx
- [x] T067 [US5] Create ComponentInventory page with 3 collapsible ComponentSections (People, Places, Things), search bar, type/status filters, add-component modal, delete confirmation with capability-impact warning; pagination in src/Ato.Copilot.Dashboard/src/pages/ComponentInventory.tsx

### Tests for User Story 5

- [x] T067a [P] [US5] Unit tests for ComponentService — CreateComponentAsync (validation, capability linking), UpdateComponentAsync, DeleteComponentAsync (Active component: DashboardActivity entries created for linked capabilities, Decommissioned component: no flagging), GetComponentsAsync (type/status filter, search, summary counts) in tests/Ato.Copilot.Tests.Unit/Services/ComponentServiceTests.cs
- [x] T067b [P] [US5] Integration tests for GET/POST /api/dashboard/systems/{systemId}/components and PUT/DELETE /api/dashboard/components/{id} — CRUD happy paths, capability linking, deletion flagging, RBAC enforcement in tests/Ato.Copilot.Tests.Integration/Endpoints/DashboardEndpointsTests.cs

**Checkpoint**: Component CRUD works end-to-end. Sections render per type. Capability linking and deletion flagging verified.

---

## Phase 8: User Story 6 — Compliance Trend Analytics (Priority: P2)

**Goal**: Backend trend snapshot hosted service + trend API endpoint + frontend trend chart with multi-system comparison, granularity selector, and decline highlighting

**Independent Test**: Run multiple assessments over several days for a system. View trend chart — verify data points at each assessment date, score values match snapshots, declining segments highlighted in red/orange.

### Backend — Trend Service & API

- [x] T068 [US6] Create ComplianceTrendSnapshotService as BackgroundService — daily midnight UTC snapshot capture for all active systems (query ComplianceAssessment.ComplianceScore, ComplianceFinding counts by CatSeverity, PoamItem open/overdue counts, narrative coverage from ControlImplementation); also expose CaptureSnapshotAsync(systemId) for on-demand assessment-triggered capture in src/Ato.Copilot.Mcp/Services/ComplianceTrendSnapshotService.cs
- [x] T069 [P] [US6] Create TrendDataDto (systemId, granularity, dataPoints[] with date, complianceScore, catICount, catIICount, catIIICount, openPoamCount, overduePoamCount, narrativeCoverage, isSignificantDecline) in src/Ato.Copilot.Mcp/Dtos/Dashboard/TrendDtos.cs
- [x] T070 [US6] Add GetTrendsAsync to DashboardService — query ComplianceTrendSnapshot by systemId and date range, aggregate by granularity (daily/weekly/monthly/quarterly), compute isSignificantDecline (>5% drop between consecutive points) in src/Ato.Copilot.Mcp/Services/DashboardService.cs
- [x] T071 [US6] Register ComplianceTrendSnapshotService as hosted service in DI; wire GET /api/dashboard/systems/{systemId}/trends endpoint with query params (startDate, endDate, granularity) and RBAC check in src/Ato.Copilot.Mcp/Endpoints/DashboardEndpoints.cs

### Frontend — Trend Chart

- [x] T072 [P] [US6] Create API function getTrends(systemId, params) in src/Ato.Copilot.Dashboard/src/api/trends.ts
- [x] T073 [P] [US6] Create TrendChart component (Recharts LineChart with compliance score line, date x-axis, score y-axis 0-100; configurable granularity toggle; declining segments >5% highlighted red/orange; tooltip with full metrics per data point; multi-system overlay with legend when multiple systemIds provided) in src/Ato.Copilot.Dashboard/src/components/charts/TrendChart.tsx
- [x] T074 [US6] Integrate TrendChart into SystemDetail page — add trend section below metrics with date range picker, granularity selector (daily/weekly/monthly/quarterly), and rendered TrendChart in src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx

### Tests for User Story 6

- [x] T074a [P] [US6] Unit tests for ComplianceTrendSnapshotService — CaptureSnapshotAsync (correct metrics captured, Source="Assessment" for on-demand, Source="Scheduled" for daily), daily execution scheduling, empty system list handling in tests/Ato.Copilot.Tests.Unit/Services/ComplianceTrendSnapshotServiceTests.cs
- [x] T074b [P] [US6] Unit tests for DashboardService.GetTrendsAsync — date range filtering, granularity aggregation (daily passthrough, weekly/monthly/quarterly averaging), isSignificantDecline computation (>5% drop), empty snapshot set in tests/Ato.Copilot.Tests.Unit/Services/DashboardServiceTests.cs
- [x] T074c [P] [US6] Integration test for GET /api/dashboard/systems/{systemId}/trends — happy path with date range, granularity param, RBAC enforcement, 404 on unknown system in tests/Ato.Copilot.Tests.Integration/Endpoints/DashboardEndpointsTests.cs

**Checkpoint**: Background snapshot service runs daily. Trend chart renders accurate time-series data with decline highlighting. Multi-system comparison works.

---

## Phase 9: Documentation & Cross-Cutting Concerns

**Purpose**: Documentation updates, SSP integration, edge cases, responsive design, and final validation

### Documentation (New & Updated)

- [x] T075a [P] Create user guide for dashboard features in docs/guides/compliance-dashboard.md — portfolio overview (sort/filter/ATO countdown), system detail (RMF progress, heatmap, metrics, activity feed, drill-down), trend analytics (granularity, date range, decline indicators)
- [x] T075b [P] Create user guide for Security Capabilities Library in docs/guides/security-capabilities.md — creating capabilities, NIST family categories, mapping controls, role assignment (Primary/Supporting/Shared), narrative auto-generation, manual override protection, upstream change notification
- [x] T075c [P] Create user guide for component inventory in docs/guides/component-inventory.md — People/Places/Things model, creating/linking components, capability linking, SSP Appendix A generation, deletion flagging behavior
- [x] T075d [P] Create user guide for gap analysis in docs/guides/gap-analysis.md — coverage matrix, unmapped controls, per-family breakdown, below-50% highlighting
- [x] T075e [P] Update docs/api/mcp-server.md with dashboard API endpoint reference (16 endpoints including heatmap drill-down, auth requirements, pagination, CORS configuration, ErrorResponse schema with errorCode/suggestion)
- [x] T075f [P] Update docs/architecture/data-model.md with new entities: SecurityCapability, CapabilityControlMapping, SystemComponent, ComponentCapabilityLink, ComplianceTrendSnapshot, DashboardActivity, and modified ControlImplementation fields
- [x] T075g [P] Update docs/architecture/overview.md with dashboard architecture section — standalone React SPA, API endpoints on MCP server, background snapshot service, polling architecture
- [x] T075h [P] Update docs/getting-started/index.md with dashboard setup instructions (npm install, Vite dev server, environment config)
- [x] T075i [P] Update docs/dev/contributing.md with dashboard development workflow — frontend conventions (TypeScript types, Axios client, usePolling hook), testing requirements (Vitest + RTL for frontend, xUnit for backend)
- [x] T075j [P] Create or update docs/release-notes/ with feature 030 release notes entry — Visual Compliance Dashboard, Security Capabilities Library, Component Inventory, Gap Analysis, Compliance Trends

### Edge Cases & Polish

- [x] T076 [P] Add empty/zero-state handling across all pages: "No assessments yet" with CTA on system detail, "No systems registered" on portfolio, "Create your first Security Capability" on library, "No trend data available" on trends in src/Ato.Copilot.Dashboard/src/pages/*.tsx
- [x] T077 [P] Add responsive breakpoints: single-column reflow on mobile/narrow screens, collapsible sidebar, heatmap simplified to list view below 768px in src/Ato.Copilot.Dashboard/src/components/layout/PageLayout.tsx and src/Ato.Copilot.Dashboard/src/components/charts/ComplianceHeatmap.tsx
- [x] T078 [P] Integrate component inventory with SSP Appendix A generation — add method to DocumentGenerationService to query SystemComponent table and populate Appendix A component table (name, type, description, linked capabilities, owner) in src/Ato.Copilot.Mcp/Services/ (existing DocumentGenerationService)
- [x] T079 Run quickstart.md validation — follow all setup steps end-to-end (EF migration, MCP server start, npm install, Vite dev server, API curl tests) and verify full workflow
- [x] T080 Run `dotnet build Ato.Copilot.sln` with zero warnings and `dotnet test` with 80%+ coverage on all new services per Constitution Quality Gates

---

## Phase 10: Dashboard UX Enhancements (Post-MVP)

**Goal**: Improve navigation ergonomics and enrich the control family drill-down with severity, POA&M, and actionable links.

### Grouped Navigation Sidebar (FR-036, FR-037)

- [x] T081 [US2] Refactor SystemLayout.tsx sidebar navigation from flat `navItems` array to grouped `navGroups` structure with 4 groups: System Profile (Overview, Components, Boundaries, Capabilities), Compliance Posture (Narratives, Legal & Regulatory, Gap Analysis), Assessment & Remediation (Assessments, Remediation, POA&M, Evidence, Deviations), Planning & Delivery (Implementation Roadmap, Documents) — add `NavItem` and `NavGroup` TypeScript interfaces, render group labels when expanded and thin dividers when collapsed in src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx

### Enhanced Control Family Drill-Down (FR-010a–FR-010e)

- [x] T082a [US2] Add `CatSeverity` (string?) and `PoamStatus` (string?) fields to `HeatmapControlDto` in src/Ato.Copilot.Core/Dtos/Dashboard/HeatmapDtos.cs
- [x] T082b [US2] Extend `GetHeatmapControlsAsync` in DashboardService.cs to join `PoamItems` by SecurityControlNumber and map `CatSeverity` from ControlEffectivenessRecord and `PoamStatus` from the latest matching POA&M item into HeatmapControlDto in src/Ato.Copilot.Mcp/Services/DashboardService.cs
- [x] T082c [US2] Add `catSeverity` and `poamStatus` fields to `HeatmapControl` TypeScript interface in src/Ato.Copilot.Dashboard/src/types/dashboard.ts
- [x] T082d [US2] Rewrite ControlDrillDown.tsx with: clickable summary stat cards (Satisfied/Failing/Not Assessed) as filter controls, CAT severity breakdown badges on Failing card, severity color map (CAT I=red, CAT II=amber, CAT III=blue), POA&M status badges with color coding, action banner with "Go to Remediation" and "View POA&Ms" navigation links when failing controls exist, filter indicator with reset, enhanced table columns (Control, Title with inline capability name, Status, Severity, Narrative, POA&M), failing row red tint, footer quick links to Edit Narratives and Run Assessment pages in src/Ato.Copilot.Dashboard/src/components/charts/ControlDrillDown.tsx

**Checkpoint**: Clicking a heatmap tile opens the enhanced drill-down showing severity badges, POA&M status, summary filters, and action links. Grouped sidebar renders correct groups in both expanded and collapsed states.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US7+US1: Portfolio Dashboard (Phase 3)**: Depends on Foundational — first deliverable (MVP)
- **US2: System Detail (Phase 4)**: Depends on Foundational — can run in parallel with Phase 3
- **US3: Capabilities Library (Phase 5)**: Depends on Foundational — can run in parallel with Phases 3-4
- **US4: Gap Analysis (Phase 6)**: Depends on Phase 5 (CapabilityService) — requires capabilities to exist
- **US5: Component Inventory (Phase 7)**: Depends on Foundational — can run in parallel with Phases 3-5
- **US6: Compliance Trends (Phase 8)**: Depends on Foundational — can run in parallel with Phases 3-7
- **Polish (Phase 9)**: Depends on all story phases being complete
- **UX Enhancements (Phase 10)**: Can run after Phase 4 (System Detail) — enriches existing drill-down and navigation

### User Story Dependencies

- **US1 (Portfolio Dashboard)**: No story dependencies — standalone
- **US2 (System Detail)**: No story dependencies — standalone (navigates from US1 but testable independently)
- **US3 (Capabilities Library)**: No story dependencies — standalone
- **US4 (Gap Analysis)**: Depends on US3 (CapabilityService must exist for coverage queries)
- **US5 (Component Inventory)**: No story dependencies — standalone (links to capabilities but testable without)
- **US6 (Compliance Trends)**: No story dependencies — standalone

### Within Each User Story

- DTOs before services
- Services before endpoints
- Backend (API) before frontend (pages)
- Reusable components ([P]) can be built in parallel
- Page composition after all components ready

---

## Parallel Opportunities

### After Phase 2 (Foundational) completes, these can all run simultaneously:

```
Phase 3 (US1: Portfolio)     ─┐
Phase 4 (US2: System Detail) ─┤─── All in parallel
Phase 5 (US3: Capabilities)  ─┤
Phase 7 (US5: Components)    ─┤
Phase 8 (US6: Trends)        ─┘

Phase 6 (US4: Gap Analysis)  ─── After Phase 5 completes
Phase 9 (Polish)             ─── After all stories complete
```

### Within Phase 3 (US1 — Portfolio):

```
# Backend (sequential: DTOs → Service → Endpoints)
T024 → T025 → T026 + T027

# Frontend (parallel components, then page)
T028 ─┐
T029 ─┤─── All in parallel → T032 (page composes all)
T030 ─┤
T031 ─┘

# Tests (after implementation)
T032a + T032b ─── In parallel
```

### Within Phase 5 (US3 — Capabilities):

```
# Backend (sequential: DTOs → Services → Endpoints)
T044 + T045 → T046 + T047 → T048 → T049

# Frontend (parallel components, then page)
T050 ─┐
T051 ─┤─── All in parallel → T054 (page composes all)
T052 ─┤
T053 ─┘

# Tests (after implementation)
T054a + T054b + T054c ─── All in parallel
```

---

## Implementation Strategy

### MVP First (Phase 1 + 2 + 3 Only)

1. Complete Phase 1: Setup (project scaffold, dependencies)
2. Complete Phase 2: Foundational (entities, migration, CORS, endpoint routing)
3. Complete Phase 3: US1 Portfolio Dashboard + US7 API
4. **STOP and VALIDATE**: Portfolio dashboard renders all systems with metrics, sorting, filtering, polling
5. Deploy/demo — immediate value as a compliance status board

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Phase 3: US1 Portfolio Dashboard (with tests) → **MVP deployed** (read-only status board)
3. Phase 4: US2 System Detail + Drill-Down (with tests) → Drill-down from portfolio works
4. Phase 5: US3 Capabilities Library (with tests) → "Write once, apply everywhere" active
5. Phase 6: US4 Gap Analysis (with tests) → Coverage visibility added
6. Phase 7: US5 Component Inventory (with tests) → SSP Appendix A integration
7. Phase 8: US6 Compliance Trends (with tests) → Time-series analytics available
8. Phase 9: Documentation + Polish → All docs created/updated, responsive design, edge cases, build validation

### Parallel Team Strategy

With multiple developers after Foundational is complete:

- **Developer A**: Phase 3 (Portfolio) → Phase 4 (System Detail) → Phase 9 (Polish)
- **Developer B**: Phase 5 (Capabilities) → Phase 6 (Gap Analysis) → Phase 9 (Polish)
- **Developer C**: Phase 7 (Components) → Phase 8 (Trends) → Phase 9 (Polish)

---

## Notes

- [P] tasks = different files, no dependencies on in-progress tasks
- [USn] label maps task to specific user story for traceability
- All behavior changes include corresponding test tasks per Constitution Principle III (NON-NEGOTIABLE)
- All error responses MUST include errorCode and suggestion fields per Constitution Principle VII
- Backend DTOs mirror TypeScript types defined in T003 — keep in sync
- All endpoints must honor RBAC via RmfRoleAssignment (users see only their systems)
- All async methods must accept CancellationToken per Constitution Principle VIII
- All services must use constructor DI per Constitution Principle VI
- All documentation (docs/) must be updated or created for new features per Constitution Quality Gates
- NIST SP 800-53 references target Revision 5 (20 control families including PT and SR)
- Commit after each task or logical group
